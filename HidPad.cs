using System.Runtime.InteropServices;
using System.Text;

namespace Binput;

/// <summary>
/// 备用手柄通道: **直接读 HID 报告**, 不走 XInput, 而且**不手解字节**。
///
/// <para>为什么需要它: 程序原来只认 XInput, 而 XInput 是微软给 Xbox 手柄做的接口 ——
/// 它只认 Xbox 原装和"会讲 XInput 协议"的第三方手柄。PS5 的 DualSense 在 Windows
/// 眼里是个**普通 HID 设备**(设备管理器里叫 "Wireless Controller"), 讲的是 HID 报告,
/// 不是 XInput, 于是 <c>XInputGetState</c> 直接回"设备未连接", 程序就当它没插。
/// 插线也一样 —— 这不是蓝牙/有线的问题, 是**协议不对**。</para>
///
/// <para>**为什么不手解字节**: 第一版是手写偏移的(照 Linux 内核和 Wine 的文档推的),
/// USB 那条路对上了, **蓝牙那条路错了** —— 实测蓝牙的报告号是 <c>0x01</c> 不是
/// <c>0x31</c>, 按钮字节也不在 USB 那个位置(USB 里第 5、6 字节是扳机、第 8 字节
/// 才是按钮; 蓝牙里第 5 字节直接就是按钮)。手柄自己**带着描述符**, 而 Windows
/// 的 HidP 就是照描述符解的 —— 问它拿到的才是真值, USB / 蓝牙的差异它全替我们吃掉。
/// 所以现在**一个字节偏移都不写**。</para>
///
/// <para>问出来的用法号(实测, 见 <c>hidprobe diag</c>):
/// 摇杆 X=0x30 / Y=0x31 / Z(右摇杆 X)=0x32 / Rz(右摇杆 Y)=0x35,
/// **扳机 L2=Rx(0x33) / R2=Ry(0x34)**, 十字键 = Hat(0x39), 按钮页 0x09 用法 1~14。</para>
///
/// <para>以后要接 PS4 / Switch Pro, 就是往 PID 名单里加一条, 再跑一次 <c>hidprobe diag</c>
/// 看它们的用法号 —— 只要 HidP 解得出来, 连改动都省了。枚举、开句柄、读循环、
/// 和 XInput 抢优先级那套是共用的。</para>
/// </summary>
public static class HidPad
{
    // ---- 认设备: 索尼的 VID 和两个 PID ----
    const ushort VID_SONY = 0x054C;
    const ushort PID_DUALSENSE = 0x0CE6;      // DualSense(USB 和蓝牙**同一个** PID)
    const ushort PID_DS_EDGE = 0x0DF2;        // DualSense Edge

    static string NameOf(ushort pid) => pid == PID_DS_EDGE ? "DualSense Edge" : "DualSense";
    static bool IsOurs(ushort vid, ushort pid) =>
        vid == VID_SONY && (pid == PID_DUALSENSE || pid == PID_DS_EDGE);

    /// <summary>
    /// 多久没收到报告就算"断了"。手柄是**持续**吐报告的(不动也吐), 所以真断线
    /// 一两百毫秒就看得出来; 给 1.5 秒是防蓝牙偶尔抖一下就当掉了。
    /// </summary>
    const long STALE_MS = 1500;

    /// <summary>
    /// 读报告的缓冲区大小, 按**最长的那种**给: USB 报告 64 字节、蓝牙 78 字节。
    /// <c>ReadFile</c> 返回的是这一帧实际多少字节, 缓冲区大一点不影响。
    /// </summary>
    const int REPORT_BUF = 78;

    // ============================================================ 对外
    static volatile bool _stop;

    // 快照整个换掉, 引用赋值是原子的 —— 比给 struct 加锁省事, 也不会读到"半个状态"。
    sealed class Snap { public PadState St; public long At; }
    static Snap _snap;
    static int _dumps;                        // 首帧原始字节只打一次, 留着对线用
    static Thread _mgr;
    static string _name;

    public static void Start()
    {
        if (_mgr != null) return;
        _stop = false;
        _mgr = new Thread(ManagerLoop) { IsBackground = true, Name = "HidPad" };
        _mgr.Start();
    }

    public static void Stop()
    {
        _stop = true;
        // 读线程可能正堵在 ReadFile 上。它们是后台线程, 进程退出时会被收掉,
        // 不强求在这里把句柄关干净 —— 否则还得再引一套 CancelIo, 不值。
    }

    /// <summary>最近一次 HID 手柄的状态。没接 / 已经断了就是 <c>Connected = false</c>。</summary>
    public static PadState GetState()
    {
        var s = Volatile.Read(ref _snap);
        if (s == null) return default;
        if (Environment.TickCount64 - s.At > STALE_MS) return default;
        return s.St;
    }

    public static bool Connected => GetState().Connected;

    /// <summary>接的是哪一款(日志里用)。没接上时是空串。</summary>
    public static string Name => Connected ? (_name ?? "DualSense") : "";

    /// <summary>
    /// 最近一帧的原始报告(前 16 字节的十六进制)。探针拿来核对"到底收到的是什么"。
    /// </summary>
    public static string LastRaw = "";

    /// <summary>最近一帧按着的**按钮用法号**, 逗号分隔(只给探针看)。</summary>
    public static string LastUsages = "";

    /// <summary>任天堂的 VID / Switch Pro 的 PID。只用来**认人**, 不读它的报告。</summary>
    const ushort VID_NIN = 0x057E, PID_SWITCH_PRO = 0x2009;

    static int _sawNin;                  // 1=插着 0=没插着
    static long _sawNinAt;

    /// <summary>
    /// 系统里插着任天堂手柄吗? 只看 VID/PID, **不读它的报告**(它的报告 HidP 解不了,
    /// 见下面 OpenAll 里只认索尼那一段)。
    ///
    /// <para>为什么要有这个: WinRT 那一路的标准手柄拿不到名字也拿不到按钮标签,
    /// 认不出"这只是不是任天堂的"(而任天堂那四颗键要按位置对, A↔B、X↔Y 得换)。
    /// 这儿能读到 VID/PID, 就当个兜底判据。10 秒查一次够了 —— 插拔不会那么频繁。</para>
    /// </summary>
    public static bool SawNintendo
    {
        get
        {
            long now = Environment.TickCount64;
            if (now - _sawNinAt > 10000)
            {
                _sawNin = ScanNintendo() ? 1 : 0;
                _sawNinAt = now;
            }
            return _sawNin == 1;
        }
    }

    static bool ScanNintendo()
    {
        foreach (string path in EnumeratePaths())
        {
            IntPtr h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                  IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID_HANDLE) continue;
            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            bool ok = HidD_GetAttributes(h, out attr);
            CloseHandle(h);
            if (ok && attr.VendorID == VID_NIN && attr.ProductID == PID_SWITCH_PRO) return true;
        }
        return false;
    }

    // ============================================================ 管理线程
    //
    // 干两件事: 找设备 -> 开句柄 -> 起读线程 -> 等读线程都死了(就是拔了/没电了) -> 再找。
    // 一个线程管到底, 不用加锁也不用判断"谁负责重连"。
    static void ManagerLoop()
    {
        int round = 0;
        while (!_stop)
        {
            var open = OpenAll(++round);
            if (open.Count == 0)
            {
                Thread.Sleep(800);
                continue;
            }

            _name = open[0].Name;
            App.Log($"HID: 接上 {open[0].Name}({open.Count} 个接口), 开始读报告");

            var threads = new List<Thread>(open.Count);
            foreach (var o in open)
                threads.Add(new Thread(() => ReadLoop(o)) { IsBackground = true });

            for (int i = 0; i < threads.Count; i++) threads[i].Start();
            for (int i = 0; i < threads.Count; i++) threads[i].Join();

            foreach (var o in open)
            {
                if (o.Preparsed != IntPtr.Zero) HidD_FreePreparsedData(o.Preparsed);
                CloseHandle(o.Handle);
            }
            if (_stop) break;
            App.Log("HID: 读不到报告了(拔了 / 没电 / 蓝牙断了), 重新找");
            _snap = null;
            _dumps = 0;
        }
    }

    // ============================================================ 读循环
    static void ReadLoop(Opened o)
    {
        var buf = new byte[o.ReportLen];
        var usages = new ushort[32];
        while (!_stop)
        {
            if (!ReadFile(o.Handle, buf, (uint)buf.Length, out uint read, IntPtr.Zero) || read == 0)
                return;                                  // 拔了 / 出错, 交给管理线程去重连

            var st = ParseHid(o.Preparsed, buf, read, usages, out int n);
            if (st == null) continue;                    // 不是主报告(固件/校准之类)

            if (_dumps++ == 0)
                App.Log("HID 首帧: " + BitConverter.ToString(buf, 0, Math.Min((int)read, 16)));
            if (n > 0) LastUsages = string.Join(",", usages.Take(n));
            else LastUsages = "";                        // 松手了要清掉, 不然探针看到的是上一帧的
            LastRaw = BitConverter.ToString(buf, 0, Math.Min((int)read, 16));

            Volatile.Write(ref _snap, new Snap { St = st.Value, At = Environment.TickCount64 });
        }
    }

    // ============================================================ 解报告
    // 用法号(Generic Desktop 页 0x01)。都是**实测**出来的, 不是推的。
    const ushort U_LX = 0x30, U_LY = 0x31, U_RX = 0x32, U_RY = 0x35;
    const ushort U_L2 = 0x33, U_R2 = 0x34;               // 扳机挂在 Rx / Ry 上
    const ushort U_HAT = 0x39;

    /// <summary>
    /// 让 Windows 照设备自带的描述符把这一帧解成 <see cref="PadState"/>。
    /// 解不出来(不是主输入报告)返回 null。
    ///
    /// <para>里头用到的几个纯函数(<see cref="Axis"/> / <see cref="HatBits"/> /
    /// <see cref="MapUsages"/>)是给 <c>tools/hidprobe</c> 钉的: 需要真手柄才能跑的
    /// 那部分测不了, 但**映射逻辑**是纯的, 能钉住。</para>
    /// </summary>
    public static PadState? ParseHid(IntPtr pp, byte[] b, uint len, ushort[] scratch, out int nUsed)
    {
        nUsed = 0;
        // X 问不到就说明这一帧不是主输入报告(可能是固件 / 校准之类), 跳过。
        if (HidP_GetUsageValue(HidPReportType.HidP_Input, 0x01, 0, U_LX, out uint lx, pp, b, len) != HIDP_OK)
            return null;

        HidP_GetUsageValue(HidPReportType.HidP_Input, 0x01, 0, U_LY, out uint ly, pp, b, len);
        HidP_GetUsageValue(HidPReportType.HidP_Input, 0x01, 0, U_RX, out uint rx, pp, b, len);
        HidP_GetUsageValue(HidPReportType.HidP_Input, 0x01, 0, U_RY, out uint ry, pp, b, len);
        HidP_GetUsageValue(HidPReportType.HidP_Input, 0x01, 0, U_L2, out uint l2, pp, b, len);
        HidP_GetUsageValue(HidPReportType.HidP_Input, 0x01, 0, U_R2, out uint r2, pp, b, len);
        HidP_GetUsageValue(HidPReportType.HidP_Input, 0x01, 0, U_HAT, out uint hat, pp, b, len);

        ushort btn = HatBits(hat);

        // ⚠ n 是**进出两用**的: 进去是"scratch 能放几个", 出来才是"实际按着几个"。
        //   第一版写成 0 传进去, HidP 一个都写不进来(只把"需要几个"写回来),
        //   结果**所有按钮全读不到** —— 十字键和扳机靠 GetUsageValue 走的, 所以那两样
        //   一直是好的, 只有按钮全哑。
        int n = scratch.Length;
        if (HidP_GetUsages(HidPReportType.HidP_Input, 0x09, 0, scratch, ref n, pp, b, len) == HIDP_OK)
            btn |= MapUsages(scratch, n);
        nUsed = n;

        return new PadState
        {
            Connected = true,
            Buttons = btn,
            LeftTrigger = (byte)Math.Min(l2, 255),
            RightTrigger = (byte)Math.Min(r2, 255),
            // HID 的轴是 0~255、中心 128, 而且 **Y 往下是正**; XInput 是 ±32768 且
            // Y 往上是正 —— 所以两个 Y 要翻号。
            LX = Axis(lx, false), LY = Axis(ly, true),
            RX = Axis(rx, false), RY = Axis(ry, true),
        };
    }

    /// <summary>HID 的 0~255(中心 128)换算成 XInput 的 -32768~32767。</summary>
    public static short Axis(uint v, bool invertY)
    {
        int d = invertY ? (128 - (int)v) : ((int)v - 128);
        return (short)Math.Clamp(d * 256, -32768, 32767);
    }

    /// <summary>
    /// 十字键: HID 的 hat 是 **一个 4 位值**, 0=上、顺时针到 7=左上;
    /// **8(以及任何超出 0~7 的值) = 中立**。XInput 是四个独立的位,
    /// 斜方向就同时置两位。
    /// </summary>
    public static ushort HatBits(uint hat)
    {
        if (hat > 7) return 0;                           // 8 = 中立; 别的越界值也当中立
        return hat switch
        {
            0 => GamepadInput.DPAD_UP,
            1 => (ushort)(GamepadInput.DPAD_UP | GamepadInput.DPAD_RIGHT),
            2 => GamepadInput.DPAD_RIGHT,
            3 => (ushort)(GamepadInput.DPAD_RIGHT | GamepadInput.DPAD_DOWN),
            4 => GamepadInput.DPAD_DOWN,
            5 => (ushort)(GamepadInput.DPAD_DOWN | GamepadInput.DPAD_LEFT),
            6 => GamepadInput.DPAD_LEFT,
            _ => (ushort)(GamepadInput.DPAD_LEFT | GamepadInput.DPAD_UP),   // 7
        };
    }

    /// <summary>
    /// 按着的按钮用法号 -> XInput 按键掩码。
    ///
    /// <para>用法号是描述符里的**声明顺序**, 和报告里那几个按钮字节的位序对应:
    /// 1=□ 2=× 3=○ 4=△ 5=L1 6=R1 7=L2 8=R2 9=Create 10=Options 11=L3 12=R3
    /// 13=PS 14=触摸板(实测这份描述符就 1~14)。</para>
    /// </summary>
    public static ushort MapUsages(ushort[] u, int n)
    {
        ushort btn = 0;
        int c = Math.Min(n, u.Length);
        for (int i = 0; i < c; i++) btn |= UsageBit(u[i]);
        return btn;
    }

    /// <summary>单个按钮用法号 -> XInput 位。四颗功能键**按位置**对应。</summary>
    public static ushort UsageBit(ushort u) => u switch
    {
        1 => GamepadInput.X,        // □ 在左
        2 => GamepadInput.A,        // × 在下
        3 => GamepadInput.B,        // ○ 在右
        4 => GamepadInput.Y,        // △ 在上
        5 => GamepadInput.LB,       // L1
        6 => GamepadInput.RB,       // R1
        7 => 0,                     // L2 的数字位 —— 扳机用模拟量, 不要这个
        8 => 0,                     // R2 的数字位
        9 => GamepadInput.BACK,     // Create(触摸板**左**边那颗)
        10 => GamepadInput.START,   // Options(触摸板**右**边那颗)
        11 => GamepadInput.L3,
        12 => GamepadInput.R3,
        _ => 0,                     // 13 = PS 键、14 = 触摸板按下: 故意不要
                                    // (PS 键在 Windows 上一按会弹 Xbox Game Bar)
    };

    // ============================================================ 枚举 / 开句柄
    sealed class Opened
    {
        public IntPtr Handle;
        public IntPtr Preparsed;    // HidP 要的"预解析数据", 关句柄前要释放
        public int ReportLen;       // 这台设备自己声明的输入报告长度(见 HIDP_CAPS 那条注释)
        public string Name;
    }

    /// <summary>
    /// 把认得的手柄接口都打开, 顺便把每家的"预解析数据"取出来(后面每帧都要用)。
    /// <paramref name="round"/> 用来决定"要不要为了认 PID 把键盘鼠标也挨个打开一遍":
    /// 平时按路径粗筛, 每 8 轮做一次全扫兜底。
    /// </summary>
    static List<Opened> OpenAll(int round)
    {
        var res = new List<Opened>();
        foreach (string path in EnumeratePaths())
        {
            // 索尼的 VID 054C 在路径里一定出现(USB 是 vid_054c, 蓝牙是 VID&0002054c)。
            bool maybe = path.IndexOf("054c", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!maybe && round % 8 != 0) continue;

            IntPtr h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                  IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID_HANDLE) continue;

            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (!HidD_GetAttributes(h, out attr) || !IsOurs(attr.VendorID, attr.ProductID))
            { CloseHandle(h); continue; }

            // 描述符拿不到就没法解报告, 这个接口等于废的
            if (!HidD_GetPreparsedData(h, out IntPtr pp)) { CloseHandle(h); continue; }

            // 报告长度按**这台设备自己声明的**来, 别写死 —— 给小了 ReadFile 报 1784
            int len = 0;
            if (HidP_GetCaps(pp, out var caps) == HIDP_OK) len = caps.InputReportByteLength;
            if (len <= 0) { HidD_FreePreparsedData(pp); CloseHandle(h); continue; }

            res.Add(new Opened
            {
                Handle = h, Preparsed = pp,
                ReportLen = Math.Max(len, 64),
                Name = NameOf(attr.ProductID),
            });
        }
        return res;
    }

    static List<string> EnumeratePaths()
    {
        var paths = new List<string>();
        Guid hid;
        HidD_GetHidGuid(out hid);

        IntPtr di = SetupDiGetClassDevs(ref hid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (di == INVALID_HANDLE) return paths;
        try
        {
            var iface = new SP_DEVICE_INTERFACE_DATA();
            iface.cbSize = Marshal.SizeOf(iface);

            for (int i = 0; SetupDiEnumDeviceInterfaces(di, IntPtr.Zero, ref hid, i, ref iface); i++)
            {
                string path = DevPath(di, ref iface);
                if (path != null) paths.Add(path);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(di); }
        return paths;
    }

    static string DevPath(IntPtr di, ref SP_DEVICE_INTERFACE_DATA iface)
    {
        // 先问一次要多大, 再按那个大小拿。
        SetupDiGetDeviceInterfaceDetail(di, ref iface, IntPtr.Zero, 0, out uint need, IntPtr.Zero);
        if (need == 0) return null;

        IntPtr buf = Marshal.AllocHGlobal((int)need);
        try
        {
            // 结构第一个字段是 cbSize: x64 上是 8, x86 上是 6(4 字节整数 + 2 字节起始字符)。
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(di, ref iface, buf, need, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>
    /// 把按钮的"能力"打出来 —— 关键是 **LinkCollection**: 如果按钮不在顶层
    /// collection(0) 里, <c>HidP_GetUsages(..., 0, ...)</c 就一个都问不到。
    /// </summary>
    static void DumpButtonCaps(StringBuilder sb, IntPtr pp)
    {
        IntPtr p = Marshal.AllocHGlobal(32 * 72);
        try
        {
            ushort n = 32;
            if (HidP_GetButtonCaps(HidPReportType.HidP_Input, p, ref n, pp) != HIDP_OK)
            { sb.AppendLine("    (问不到)"); return; }
            for (int i = 0; i < n; i++)
            {
                int b = i * 72;
                ushort page = (ushort)Marshal.ReadInt16(p, b);
                byte rid = Marshal.ReadByte(p, b + 2);
                ushort lc = (ushort)Marshal.ReadInt16(p, b + 6);
                bool isRange = Marshal.ReadByte(p, b + 12) != 0;
                ushort umin = (ushort)Marshal.ReadInt16(p, b + 56);
                ushort umax = (ushort)Marshal.ReadInt16(p, b + 58);
                sb.AppendLine($"    页 0x{page:X4} 报告号 {rid}  collection {lc}  "
                    + (isRange ? $"用法 {umin}~{umax}" : $"用法 {umin}"));
            }
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>
    /// collection 0~15 挨个问一遍"这一帧按着哪些按钮用法号"。
    /// 用来确认按钮到底挂在哪个 collection 上 —— 传 0 只查顶层, 挂错了就一个都问不到。
    /// </summary>
    static void DumpUsagesByCollection(StringBuilder sb, IntPtr pp, byte[] buf, uint got)
    {
        var list = new ushort[64];
        for (ushort lc = 0; lc < 16; lc++)
        {
            int k = list.Length;
            uint st = HidP_GetUsages(HidPReportType.HidP_Input, 0x09, lc, list, ref k, pp, buf, got);
            if (st != HIDP_OK) continue;                 // 这个 collection 里没有按钮
            if (k > 0) sb.AppendLine($"    collection {lc}: {string.Join(",", list.Take(k))}");
        }
        sb.AppendLine("    (一行都没有 = 这一帧确实没按键; 换一颗键按住再跑一次就能看出挂在哪)");
    }

    // ============================================================ 现场诊断(给 tools/hidprobe)
    /// <summary>
    /// 扫一遍所有 HID 设备, 认得的手柄读一帧, 把**原始字节**、**HidP 解出来的每个轴**、
    /// **按着的按钮用法号**全打出来。
    ///
    /// <para>存在的理由: 这一层**没有真手柄就测不了**, 而且**手写的字节偏移是靠文档推的,
    /// 已经推错过一次**(蓝牙那条路)。换手柄 / 换连接方式之后先跑这个, 拿真值对。</para>
    /// </summary>
    public static string Diag()
    {
        var sb = new StringBuilder();
        foreach (string path in EnumeratePaths())
        {
            IntPtr h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                  IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID_HANDLE) continue;

            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (!HidD_GetAttributes(h, out attr)) { CloseHandle(h); continue; }
            if (!IsOurs(attr.VendorID, attr.ProductID)) { CloseHandle(h); continue; }

            sb.AppendLine($"VID_{attr.VendorID:X4} PID_{attr.ProductID:X4} ({NameOf(attr.ProductID)})");
            if (!HidD_GetPreparsedData(h, out IntPtr pp)) { CloseHandle(h); continue; }
            try
            {
                var buf = new byte[REPORT_BUF];
                uint got = 0;
                var t = new Thread(() => ReadFile(h, buf, (uint)REPORT_BUF, out got, IntPtr.Zero))
                { IsBackground = true };
                t.Start();
                if (!t.Join(3000) || got == 0)
                { sb.AppendLine("  (3 秒没读到报告, 手柄可能睡了, 动一下再试)"); continue; }

                sb.AppendLine($"  报告 {got} 字节: " + BitConverter.ToString(buf, 0, (int)Math.Min(got, 20)));

                var axes = new (ushort u, string name)[]
                {
                    (U_LX, "左摇杆X"), (U_LY, "左摇杆Y"), (U_RX, "右摇杆X"), (U_RY, "右摇杆Y"),
                    (U_L2, "L2 扳机"), (U_R2, "R2 扳机"), (U_HAT, "十字键"),
                };
                foreach (var a in axes)
                {
                    uint st = HidP_GetUsageValue(HidPReportType.HidP_Input, 0x01, 0, a.u,
                                                 out uint v, pp, buf, got);
                    sb.AppendLine($"    {a.name,-8} (0x{a.u:X2}) -> "
                        + (st == HIDP_OK ? v.ToString() : $"(问不到, 0x{st:X8})"));
                }

                sb.AppendLine("  --- 按钮能力(描述符怎么声明的)---");
                DumpButtonCaps(sb, pp);
                sb.AppendLine("  --- 逐个 collection 问一遍按着的按钮 ---");
                DumpUsagesByCollection(sb, pp, buf, got);
            }
            finally { HidD_FreePreparsedData(pp); CloseHandle(h); }
        }
        return sb.Length == 0 ? "没找到 DualSense。" : sb.ToString();
    }

    /// <summary>
    /// **所有** HID 设备都过一遍(不管认不认得): VID/PID、报告多长、HidP 能不能把它
    /// 当游戏手柄解出来。
    ///
    /// <para>为什么要有它: "这只手柄插线就不行、蓝牙就行"这种问题, 根子常在
    /// **插线和蓝牙时 Windows 给的设备不是同一份描述符**。这个模式不挑食, 把所有
    /// 设备摊开看, 一眼就能看出插线时它变成了什么、HidP 解不解得出摇杆和按钮。</para>
    /// </summary>
    public static string ListAll()
    {
        var sb = new StringBuilder();
        int total = 0;
        foreach (string path in EnumeratePaths())
        {
            IntPtr h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                  IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID_HANDLE) continue;
            total++;

            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (!HidD_GetAttributes(h, out attr)) { CloseHandle(h); continue; }

            bool ours = IsOurs(attr.VendorID, attr.ProductID);
            bool nin = attr.VendorID == VID_NIN;
            // 键盘鼠标之类不细看(它们没有游戏手柄那套用法), 只报个名字
            if (!ours && !nin)
            {
                if (!HidD_GetPreparsedData(h, out IntPtr pp0)) { CloseHandle(h); continue; }
                int n0 = 0;
                if (HidP_GetCaps(pp0, out var c0) == HIDP_OK) n0 = c0.InputReportByteLength;
                bool gamepad0 = LooksGamepad(pp0);
                HidD_FreePreparsedData(pp0);
                CloseHandle(h);
                if (gamepad0)
                    sb.AppendLine($"VID_{attr.VendorID:X4} PID_{attr.ProductID:X4}"
                        + $"  输入报告 {n0} 字节  ** HidP 能当游戏手柄解 **");
                continue;
            }

            sb.AppendLine($"VID_{attr.VendorID:X4} PID_{attr.ProductID:X4}"
                + (ours ? $" ({NameOf(attr.ProductID)})" : " (任天堂)")
                + (ours ? "  <== 认得" : "  <== 现在还不在名单里"));
            if (!HidD_GetPreparsedData(h, out IntPtr pp)) { CloseHandle(h); continue; }
            try
            {
                int len = 64;
                if (HidP_GetCaps(pp, out var caps) == HIDP_OK && caps.InputReportByteLength > 0)
                    len = caps.InputReportByteLength;
                sb.AppendLine($"  输入报告 {len} 字节   HidP 能当游戏手柄解: "
                              + (LooksGamepad(pp) ? "能" : "不能"));

                var buf = new byte[len];
                uint got = 0;
                var t = new Thread(() => ReadFile(h, buf, (uint)len, out got, IntPtr.Zero))
                { IsBackground = true };
                t.Start();
                if (!t.Join(3000) || got == 0)
                { sb.AppendLine("  (3 秒没读到报告 —— 手柄可能睡了, 按一颗键再试)"); continue; }

                sb.AppendLine($"  首帧 {got} 字节: " + BitConverter.ToString(buf, 0, (int)Math.Min(got, 20)));
                var st = ParseHid(pp, buf, got, new ushort[32], out int n);
                sb.AppendLine(st == null
                    ? "  ParseHid: null(这一帧不是主输入报告, 或者描述符对不上)"
                    : $"  ParseHid: 键=0x{st.Value.Buttons:X4} 杆({st.Value.LX},{st.Value.LY})"
                      + $"({st.Value.RX},{st.Value.RY}) LT={st.Value.LeftTrigger} RT={st.Value.RightTrigger}"
                      + $"  用法[{LastUsages}]");
            }
            finally { HidD_FreePreparsedData(pp); CloseHandle(h); }
        }
        sb.AppendLine($"\n(一共扫了 {total} 个 HID 接口; 上面列的是像手柄的那些)");
        return sb.ToString();
    }

    /// <summary>
    /// 这份描述符里有"游戏手柄那套用法"吗? 判据很粗: 能问到左摇杆 X(0x30) +
    /// 描述符里声明了按钮用法。够用来筛掉键盘鼠标。
    /// </summary>
    static bool LooksGamepad(IntPtr pp)
    {
        // 数一数描述符声明了多少按钮 / 多少轴。传 0 长度进去, 它只回"一共几个"。
        ushort nb = 0, nv = 0;
        HidP_GetButtonCaps(HidPReportType.HidP_Input, IntPtr.Zero, ref nb, pp);
        HidP_GetValueCaps(HidPReportType.HidP_Input, IntPtr.Zero, ref nv, pp);
        return nb >= 10 && nv >= 2;      // 十颗键以上 + 至少两个轴, 才像手柄
    }

    // ============================================================ Win32
    const uint GENERIC_READ = 0x80000000;
    const uint FILE_SHARE_READ = 0x00000001, FILE_SHARE_WRITE = 0x00000002;
    const uint OPEN_EXISTING = 3;
    const int DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    /// <summary>
    /// HIDP_CAPS。**只用来问"这台设备的输入报告有多长"** —— 缓冲区必须按它给的
    /// 长度分配, 给小了 <c>ReadFile</c> 直接报 1784(ERROR_INVALID_USER_BUFFER)。
    /// 实测: PS5 蓝牙 78 字节, Switch Pro 插线却是 **362 字节** —— 早先写死 78 的那版
    /// 就是这么炸的。
    ///
    /// <para>⚠ Reserved[17] 必须写成 17 个**独立的 public 字段**: 用
    /// <c>ByValArray</c> 数组的话, 走 <c>out</c> 封送到手的可能是个 null,
    /// 内核往 null 里写 34 字节就是 AccessViolation。</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage;
        public short InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        public ushort R0, R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R12, R13, R14, R15, R16;
        public short NumberLinkCollectionNodes;
        public short NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public short NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public short NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    // ---- HidP: 让 Windows 照设备自带的描述符解 ----
    // HidP_GetUsageValue / HidP_GetUsages 收的是**预解析数据**指针(不是设备句柄),
    // 成功返回 0x00110000(HIDP_STATUS_SUCCESS) —— 注意不是 0。
    const uint HIDP_OK = 0x00110000;
    enum HidPReportType { HidP_Input = 0, HidP_Output = 1, HidP_Feature = 2 }

    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);

    [DllImport("hid.dll")]
    static extern bool HidD_FreePreparsedData(IntPtr pp);

    /// <summary>
    /// ⚠ 收的是**预解析数据**(preparsed data)指针, 不是设备句柄。
    /// 传句柄进去内核会拿它当指针解引用 —— 直接 AccessViolation。
    /// (另: <c>HidD_GetCaps</c> 是宏不是导出函数, P/Invoke 会 EntryPointNotFound。)
    /// </summary>
    [DllImport("hid.dll")]
    static extern uint HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);

    [DllImport("hid.dll")]
    static extern uint HidP_GetUsageValue(HidPReportType type, ushort page, ushort collection,
        ushort usage, out uint value, IntPtr pp, byte[] report, uint reportLen);

    [DllImport("hid.dll")]
    static extern uint HidP_GetUsages(HidPReportType type, ushort page, ushort collection,
        ushort[] list, ref int count, IntPtr pp, byte[] report, uint reportLen);

    [DllImport("hid.dll")]
    static extern uint HidP_GetButtonCaps(HidPReportType type, IntPtr caps, ref ushort len, IntPtr pp);

    /// <summary>同上, 数"有几个模拟量(轴 / 扳机)"。传 0 长度进去只会回数量。</summary>
    [DllImport("hid.dll")]
    static extern uint HidP_GetValueCaps(HidPReportType type, IntPtr caps, ref ushort len, IntPtr pp);

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);

    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_GetAttributes(IntPtr h, out HIDD_ATTRIBUTES a);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, string enumerator, IntPtr hwnd, int flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr di, IntPtr devInfo, ref Guid g,
                                                   int index, ref SP_DEVICE_INTERFACE_DATA iface);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr di, ref SP_DEVICE_INTERFACE_DATA iface,
        IntPtr detail, uint size, out uint need, IntPtr devInfoData);

    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr di);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec,
                                    uint disp, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(IntPtr h, byte[] buf, uint len, out uint read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
}
