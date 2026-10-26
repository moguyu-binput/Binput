using System.Reflection;
using Binput;
using GB = Windows.Gaming.Input.GamepadButtons;                  // 标准视图的按键掩码
using GL = Windows.Gaming.Input.GameControllerButtonLabel;        // 微软给每颗键标的名字

namespace Hidprobe;

/// <summary>
/// 第十套探针: PS5 DualSense。
///
/// <para>要真手柄的那部分(枚举、开句柄、读循环、HidP 解报告)**测不了** —— 交给
/// <c>diag</c> 和 <c>live</c> 两个模式。这里钉的是解出来之后的**映射逻辑**:
/// 按钮用法号 -> 键、hat -> 十字键、轴的换算。这三块是纯的, 能钉死。</para>
///
/// <para>顺带记一笔教训: 第一版是手写字节偏移的, USB 对上了、蓝牙错了 ——
/// 蓝牙的报告号是 0x01 不是 0x31, 按钮字节也不在同一个位置。现在改成全程交给
/// HidP(Windows 照设备自带的描述符解), 一个字节偏移都不写。</para>
/// </summary>
static class Program
{
    static int _fail;

    static void Main(string[] argv)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.WriteLine("=== hidprobe: PS5 DualSense (HID / HidP) ===\n");

        if (argv.Length > 0 && argv[0] == "diag")
        {
            Console.WriteLine("--- 现场扫描(真手柄)---");
            Console.WriteLine(HidPad.Diag());
            Console.WriteLine("换手柄 / 换连接方式之后跑这个, 拿真值对。");
            return;
        }

        // list: **所有** HID 设备摊开看。插线不行、蓝牙行的那种问题靠它定位 ——
        // 两种连法下 Windows 给的设备常常不是同一份描述符。
        if (argv.Length > 0 && argv[0] == "list")
        {
            Console.WriteLine("--- 所有 HID 设备(不挑食)---");
            Console.WriteLine(HidPad.ListAll());
            Console.WriteLine("(上面标了\"HidP 能当游戏手柄解\"的, 就是理论上能走的; "
                + "把整段发回来)");
            return;
        }

        // all: 三条通道**同时在读什么**。手柄插了好几只、或者某只"连上了但没反应",
        // 跑这个一眼看出是谁挡了谁(以前三路是"谁先接上用谁", 前面的会把后面挡死)。
        if (argv.Length > 0 && argv[0] == "all")
        {
            All(argv.Length > 1 && int.TryParse(argv[1], out int n1) ? n1 : 60);
            return;
        }

        // live: 走**真实的后台读线程**, 验枚举 + 读循环 + 快照这一整条路。
        if (argv.Length > 0 && argv[0] == "live")
        {
            Live();
            return;
        }

        Case1_Usages();
        Case2_Ignored();
        Case3_Hat();
        Case4_Axis();
        Case5_NintendoSwap();
        Case7_FirstFrameDeadline();
        Case8_PickLayout();

        Console.WriteLine(_fail == 0 ? "\n全部通过" : $"\n有 {_fail} 项没过");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }

    /// <summary>按钮用法号 -> XInput 键。四颗功能键**按位置**对应。</summary>
    static void Case1_Usages()
    {
        Console.WriteLine("【1】按钮用法号 -> 键(□=X ×=A ○=B △=Y)");
        Check("□ (用法1) -> X", Bit(1), GamepadInput.X);
        Check("× (用法2) -> A", Bit(2), GamepadInput.A);
        Check("○ (用法3) -> B", Bit(3), GamepadInput.B);
        Check("△ (用法4) -> Y", Bit(4), GamepadInput.Y);
        Check("L1 (用法5) -> LB", Bit(5), GamepadInput.LB);
        Check("R1 (用法6) -> RB", Bit(6), GamepadInput.RB);
        Check("Create (用法9) -> BACK", Bit(9), GamepadInput.BACK);
        Check("Options (用法10) -> START", Bit(10), GamepadInput.START);
        Check("L3 (用法11)", Bit(11), GamepadInput.L3);
        Check("R3 (用法12)", Bit(12), GamepadInput.R3);
        Console.WriteLine();
    }

    /// <summary>PS 键、触摸板、扳机的数字位 —— 一律不参与。</summary>
    static void Case2_Ignored()
    {
        Console.WriteLine("【2】故意不参与的几位");
        Check("PS 键 (用法13) -> 什么都没有", Bit(13), 0);
        Check("触摸板按下 (用法14) -> 什么都没有", Bit(14), 0);
        Check("L2 数字位 (用法7) -> 什么都没有(扳机用模拟量)", Bit(7), 0);
        Check("R2 数字位 (用法8) -> 什么都没有", Bit(8), 0);
        Check("没见过的用法号 (99) -> 什么都没有", Bit(99), 0);
        Check("没按任何键", Bit(0), 0);

        // 一起按: 掩码要并起来
        var both = HidPad.MapUsages(new ushort[] { 2, 5, 9 }, 3);
        Check("同时按 × + L1 + Create", both.ToString("X4"),
              ((ushort)(GamepadInput.A | GamepadInput.LB | GamepadInput.BACK)).ToString("X4"));
        Console.WriteLine();
    }

    /// <summary>十字键是 4 位 hat, 8(以及任何越界值) = 中立。</summary>
    static void Case3_Hat()
    {
        Console.WriteLine("【3】十字键(hat switch)");
        for (uint h = 0; h <= 8; h++)
        {
            ushort want = h switch
            {
                0 => GamepadInput.DPAD_UP,
                1 => (ushort)(GamepadInput.DPAD_UP | GamepadInput.DPAD_RIGHT),
                2 => GamepadInput.DPAD_RIGHT,
                3 => (ushort)(GamepadInput.DPAD_RIGHT | GamepadInput.DPAD_DOWN),
                4 => GamepadInput.DPAD_DOWN,
                5 => (ushort)(GamepadInput.DPAD_DOWN | GamepadInput.DPAD_LEFT),
                6 => GamepadInput.DPAD_LEFT,
                7 => (ushort)(GamepadInput.DPAD_LEFT | GamepadInput.DPAD_UP),
                _ => 0,                              // 8 = 中立
            };
            Check($"hat={h}", HidPad.HatBits(h).ToString("X4"), want.ToString("X4"));
        }
        // 蓝牙那帧实测 hat 就是 8; 别的越界值(比如手柄刚连上时可能的垃圾值)也当中立
        Check("hat=9 (越界) -> 中立", HidPad.HatBits(9).ToString("X4"), "0000");
        Check("hat=255 (越界) -> 中立", HidPad.HatBits(255).ToString("X4"), "0000");
        Console.WriteLine();
    }

    static void Case4_Axis()
    {
        Console.WriteLine("【4】轴(0~255 中心 128 -> -32768~32767)");
        Check("中心 128 -> 0", HidPad.Axis(128, false).ToString(), "0");
        Check("推到最右 255", HidPad.Axis(255, false).ToString(), "32512");
        Check("推到最左 0", HidPad.Axis(0, false).ToString(), "-32768");
        // HID 的 Y 往下是正, XInput 往上是正 —— 必须翻号
        Check("往上推(HID 0) -> XInput 正", HidPad.Axis(0, true).ToString(), "32767");
        // 0~255 表示不了"绝对对称": 满上 32767, 满下 -32512, 差 256(0.4%)。
        // XInput 自己也是这个德行, 摇杆死区是 9000, 差这点摸不出来。
        Check("往下推(HID 255) -> XInput 负", HidPad.Axis(255, true).ToString(), "-32512");
        Check("中心翻号也是 0", HidPad.Axis(128, true).ToString(), "0");
        Console.WriteLine();
    }

    static ushort Bit(ushort u) => HidPad.UsageBit(u);

    /// <summary>
    /// 走真线程读 20 秒, **每 300 毫秒打一行**(不是只在变化时打 —— 那样一帧没解出来
    /// 就看不出来是"没按"还是"压根没读到")。没有真手柄这一步就验不了, 所以做成
    /// 让用户自己跑: 提示他逐个按键, 把整段发回来。
    /// </summary>
    const int LIVE_SEC = 60;          // 最长 60 秒, 按够 8 种就提前收工
    const int LIVE_WANT = 8;

    static void Live()
    {
        Console.WriteLine($"--- 实按验证(最长 {LIVE_SEC} 秒, 按够 {LIVE_WANT} 种自动结束)---");
        Console.WriteLine("逐个按: × ○ □ △、L1 R1 L2 R2、Create Options、L3 R3、十字键八个方向");
        Console.WriteLine("每按一下停一两秒再换下一颗。按完把整段发回来。\n");
        Console.WriteLine("（下面如果一直 键=0x0000 而且原始字节只有倒数第 3 位在跳,");
        Console.WriteLine("  说明手柄没在发按键数据 —— 先看手柄灯亮不亮、要不要先唤醒它）\n");

        HidPad.Start();
        ushort last = 0xFFFF;
        string lastRaw = "";
        var seen = new List<ushort>();
        var end = DateTime.Now.AddSeconds(LIVE_SEC);
        var beat = DateTime.Now;
        while (DateTime.Now < end && seen.Count < LIVE_WANT)
        {
            var st = HidPad.GetState();
            if (!st.Connected) { Console.WriteLine($"{Ts(end)} 读不到手柄(没报告 / 没连上)"); Thread.Sleep(300); continue; }

            bool changed = st.Buttons != last || HidPad.LastRaw != lastRaw;
            if (changed || DateTime.Now >= beat)
            {
                if (st.Buttons != 0 && !seen.Contains(st.Buttons)) seen.Add(st.Buttons);
                last = st.Buttons; lastRaw = HidPad.LastRaw; beat = DateTime.Now.AddSeconds(2);
                Console.WriteLine($"{Ts(end)} 键=0x{st.Buttons:X4}  用法[{HidPad.LastUsages}]  "
                    + $"{Describe(st.Buttons),-12} LT={st.LeftTrigger,-3} RT={st.RightTrigger,-3}  {lastRaw}");
            }
            Thread.Sleep(60);
        }
        HidPad.Stop();

        Console.WriteLine($"\n读到 {seen.Count} 种按键组合: "
            + string.Join(", ", seen.Select(b => "0x" + b.ToString("X4"))));
        Console.WriteLine(HidPad.Connected ? "手柄还在(HID 通道正常)" : "读不到手柄");
        Environment.Exit(seen.Count > 0 ? 0 : 1);
    }

    static string Ts(DateTime end) => $"[{Math.Max(0, (int)(LIVE_SEC - (end - DateTime.Now).TotalSeconds)):D2}s]";

    /// <summary>把按键掩码翻译成人话(按界面上显示的 Xbox 叫法)。</summary>
    static string Describe(ushort b)
    {
        if (b == 0) return "（没按）";
        var s = new List<string>();
        if ((b & GamepadInput.DPAD_UP) != 0) s.Add("上");
        if ((b & GamepadInput.DPAD_DOWN) != 0) s.Add("下");
        if ((b & GamepadInput.DPAD_LEFT) != 0) s.Add("左");
        if ((b & GamepadInput.DPAD_RIGHT) != 0) s.Add("右");
        if ((b & GamepadInput.A) != 0) s.Add("A(×)");
        if ((b & GamepadInput.B) != 0) s.Add("B(○)");
        if ((b & GamepadInput.X) != 0) s.Add("X(□)");
        if ((b & GamepadInput.Y) != 0) s.Add("Y(△)");
        if ((b & GamepadInput.LB) != 0) s.Add("LB(L1)");
        if ((b & GamepadInput.RB) != 0) s.Add("RB(R1)");
        if ((b & GamepadInput.BACK) != 0) s.Add("Back(Create)");
        if ((b & GamepadInput.START) != 0) s.Add("Start(Options)");
        if ((b & GamepadInput.L3) != 0) s.Add("L3");
        if ((b & GamepadInput.R3) != 0) s.Add("R3");
        return string.Join(" ", s);
    }

    /// <summary>
    /// 三条通道<b>同时在读什么</b>。要真手柄。
    ///
    /// 为什么要有它: 三路以前是"谁先接上用谁", 插两只手柄时前面那只会把后面整条
    /// 挡死(表现是"连上了但完全没反应")。这个模式三路一起打, 谁有数、谁在挡路
    /// 一眼就能看出来。默认 60 秒, 按 Ctrl+C 也行。
    /// </summary>
    static void All(int sec)
    {
        Console.WriteLine($"--- 三条通道同时在读什么({sec} 秒, 现在按按键 / 推摇杆)---\n");
        HidPad.Start();

        var raw = Windows.Gaming.Input.RawGameController.RawGameControllers.ToList();
        var std = Windows.Gaming.Input.Gamepad.Gamepads.ToList();
        Console.WriteLine($"WinRT: 原始 {raw.Count} 台 / 标准 {std.Count} 台"
            + $"  (HID 那边看得到任天堂手柄吗: {(HidPad.SawNintendo ? "看得到" : "没有")})");
        for (int i = 0; i < std.Count; i++)
        {
            var g = std[i];
            var rd = g.GetCurrentReading();
            Console.WriteLine($"  标准[{i}] {Name(g)}  键={rd.Buttons}"
                + $" 杆({rd.LeftThumbstickX:F2},{rd.LeftThumbstickY:F2})"
                + $"({rd.RightThumbstickX:F2},{rd.RightThumbstickY:F2})"
                + $" LT={rd.LeftTrigger:F2} RT={rd.RightTrigger:F2}");
        }
        for (int i = 0; i < raw.Count; i++)
        {
            var c = raw[i];
            var lab = new List<string>();
            for (int b = 0; b < c.ButtonCount; b++) lab.Add(b + ":" + c.GetButtonLabel(b));
            var gp = Windows.Gaming.Input.Gamepad.FromGameController(c);
            Console.WriteLine($"  WinRT 原始[{i}] {c.DisplayName}  按钮{c.ButtonCount} 轴{c.AxisCount}"
                + $" 标准视图={(gp == null ? "null" : "有")}");
            Console.WriteLine($"     标签: {string.Join(" ", lab)}");
        }
        Console.WriteLine();

        string last = "";
        var end = DateTime.Now.AddSeconds(sec);
        while (DateTime.Now < end)
        {
            var x = GamepadInput.GetStateAny();
            var r = RawPad.GetState();
            var h = HidPad.GetState();
            var mix = PadBus.Read(out string src);

            string line = $"[{Math.Max(0, (int)(sec - (end - DateTime.Now).TotalSeconds)):D2}s] "
                + $"XInput 连={(x.Connected ? 1 : 0)} 键=0x{x.Buttons:X4}  "
                + $"WinRT 连={(r.Connected ? 1 : 0)} 键=0x{r.Buttons:X4} {Name2(r),-30}"
                + $"HidP 连={(h.Connected ? 1 : 0)} 键=0x{h.Buttons:X4} {Name2(h),-12}"
                + $"| 合并 键=0x{mix.Buttons:X4} 杆({mix.LX},{mix.LY}) [{src}]";
            if (line != last) { Console.WriteLine(line); last = line; }

            // 原始视图: 哪颗键在按(带微软给的标签) + **原始轴的值**。
            // 轴要一直打: 有些设备的原始轴是 0~1(中心 0.5)而不是 -1~1, 看这个一眼看得出
            for (int i = 0; i < raw.Count; i++)
            {
                var bt = new bool[raw[i].ButtonCount];
                var sw = new Windows.Gaming.Input.GameControllerSwitchPosition[raw[i].SwitchCount];
                var ax = new double[raw[i].AxisCount];
                raw[i].GetCurrentReading(bt, sw, ax);
                var on = new List<string>();
                for (int b = 0; b < bt.Length; b++)
                    if (bt[b]) on.Add($"{b}:{raw[i].GetButtonLabel(b)}");
                Console.WriteLine($"        原始[{i}] {raw[i].DisplayName}"
                    + $" 轴: {string.Join(" ", ax.Select(v => v.ToString("F3")))}"
                    + (on.Count > 0 ? "  按着: " + string.Join(",", on) : ""));
            }
            Thread.Sleep(100);
        }
        HidPad.Stop();
        Console.WriteLine("\n完了。把上面整段发回来 —— 哪一路有数、哪一路是 0, 一眼就能定位。");
        Environment.Exit(0);
    }

    /// <summary>标准手柄的名字(<c>Gamepad</c> 没 DisplayName, 只能从原始手柄那儿对)。</summary>
    static string Name(Windows.Gaming.Input.Gamepad g)
    {
        foreach (var c in Windows.Gaming.Input.RawGameController.RawGameControllers)
            if (Windows.Gaming.Input.Gamepad.FromGameController(c) == g) return c.DisplayName;
        return "(对不上原始设备)";
    }

    /// <summary>WinRT / HID 那一路当前的设备名(给那行日志对齐用)。</summary>
    static string Name2(PadState s) =>
        s.Connected ? (string.IsNullOrEmpty(RawPad.Name) ? "" : RawPad.Name) : "";

    /// <summary>
    /// 【5】任天堂手柄的 A↔B、X↔Y 对换（2.4.3）。
    ///
    /// 微软是**按符号**给任天堂手柄标的: 底下那颗它叫 LetterB。我们要的是"底下那颗 =
    /// 确定"(和 Xbox 的 A 一样), 所以两对要换过来。这两是纯函数, 没真手柄也能钉住。
    /// </summary>
    static void Case5_NintendoSwap()
    {
        Console.WriteLine("【5】任天堂布局: A↔B、X↔Y 对换(按位置对)");
        // 标准视图: 四颗功能键对换, 别的一颗都不许动
        Check("只按 A -> 变 B", RawPad.FaceSwap(GB.A).ToString(), "B");
        Check("只按 B -> 变 A", RawPad.FaceSwap(GB.B).ToString(), "A");
        Check("只按 X -> 变 Y", RawPad.FaceSwap(GB.X).ToString(), "Y");
        Check("只按 Y -> 变 X", RawPad.FaceSwap(GB.Y).ToString(), "X");
        var keep = GB.DPadUp | GB.LeftShoulder | GB.View | GB.Menu | GB.LeftThumbstick;
        Check("十字键 / 肩键 / − + / 摇杆按下: 一个都不动",
              RawPad.FaceSwap(keep).ToString(), keep.ToString());
        Check("AB 一起按还是 AB(换了等于没换)",
              RawPad.FaceSwap(GB.A | GB.B).ToString(), (GB.A | GB.B).ToString());

        // 标签版: 只有那四颗字母键换, Xbox 那套标签不许动
                Check("标签 LetterA -> LetterB", RawPad.FlipLabel(GL.LetterA).ToString(), "LetterB");
        Check("标签 LetterB -> LetterA", RawPad.FlipLabel(GL.LetterB).ToString(), "LetterA");
        Check("标签 LetterX -> LetterY", RawPad.FlipLabel(GL.LetterX).ToString(), "LetterY");
        Check("标签 LetterY -> LetterX", RawPad.FlipLabel(GL.LetterY).ToString(), "LetterX");
        Check("标签 Minus(-) 不动", RawPad.FlipLabel(GL.Minus).ToString(), "Minus");
        Check("标签 Plus(+) 不动", RawPad.FlipLabel(GL.Plus).ToString(), "Plus");
        Check("标签 XboxA 不动(那是 Xbox 的手柄)",
              RawPad.FlipLabel(GL.XboxA).ToString(), "XboxA");
        Console.WriteLine();
    }


    /// <summary>
    /// 【7】首帧期限（2.4.11）—— 这一条是**从用户的日志里挖出来的洞**补的。
    ///
    /// <para>原来的看门狗只对"曾经有过快照、后来断供"动手；重开之后要是设备再也不给
    /// 报告，快照一直是 null，它就一句 `continue`（"还没收到过第一帧，再等等"）等下去 ——
    /// 无期。读线程堵在 ReadFile、管理线程堵在 Join，日志安静得像是没事，手柄其实瞎了。
    /// 蓝牙+游戏那一场就是这么死的：看门狗重开一次，之后 20 多秒一条日志都没有。</para>
    ///
    /// <para>这里钉三件事：到点了**必须**动手 / 没到点**不许**动手 / 中途解出过一帧
    /// 就**交回给断供那条**管；顺带钉住期限"翻倍到 10 秒封顶"（一直等不到就别
    /// 2.5 秒刷一行日志）。</para>
    /// </summary>
    static void Case7_FirstFrameDeadline()
    {
        Console.WriteLine("【7】首帧期限: 接上之后到点还没解出一帧, 看门狗必须动手");

        var t = typeof(HidPad);
        var mi = t.GetMethod("FirstFrameOverdue", BindingFlags.NonPublic | BindingFlags.Static);
        Check("找得到首帧期限那个判据", mi != null ? "有" : "没有", "有");
        if (mi == null) { Console.WriteLine(); return; }

        bool Due(long now) => (bool)mi.Invoke(null, new object[] { now });
        void Set(string f, long v)
            => t.GetField(f, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, v);
        long Get(string f) => (long)t.GetField(f, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        void Snap(bool there)   // 造/清一份"当前快照"(Snap 是私有的, 只能反射写)
        {
            var f = t.GetField("_snap", BindingFlags.NonPublic | BindingFlags.Static);
            if (!there) { f.SetValue(null, null); return; }
            var ty = f.FieldType;
            var o = Activator.CreateInstance(ty, true);
            ty.GetField("St").SetValue(o, new PadState());
            ty.GetField("At").SetValue(o, Environment.TickCount64);
            f.SetValue(null, o);
        }

        long now = Environment.TickCount64;
        Snap(false);
        Set("_roundAt", now);
        Set("_firstMs", 2500);
        Check("刚接上(0 秒): 不动手", Due(now) ? "动手" : "不动", "不动");
        Check("才过 2 秒: 还不动手", Due(now + 2000) ? "动手" : "不动", "不动");
        Check("过了 2.5 秒还没帧: 动手", Due(now + 2600) ? "动手" : "不动", "动手");

        Snap(true);                                  // 中途解出过一帧
        Check("解出过一帧: 交回断供那条管", Due(now + 9000) ? "动手" : "不动", "不动");

        // 期限: 一直等不到就翻倍, 10 秒封顶(别 2.5 秒刷一行日志)
        Snap(false);
        Set("_firstMs", 2500);
        var log = t.GetMethod("LogNoFrame", BindingFlags.NonPublic | BindingFlags.Static);
        Check("找得到那句报话", log != null ? "有" : "没有", "有");
        if (log != null)
        {
            log.Invoke(null, null); Check("喊一次 -> 期限翻倍", Get("_firstMs").ToString(), "5000");
            log.Invoke(null, null); Check("再喊一次 -> 再翻倍", Get("_firstMs").ToString(), "10000");
            log.Invoke(null, null); Check("到 10 秒封顶", Get("_firstMs").ToString(), "10000");
        }
        Console.WriteLine();
    }

    // ============================================================ 断言
    // ============================================================ 场景8: 挑"眼镜"(哪号 collection)
    /// <summary>
    /// 钉的是这个坑: 解析时一律按顶层 collection(0) 问, 描述符把摇杆塞进子
    /// collection 时就**一个都问不到**(表现 = 报告铺天盖地地来, 一个都解不出)。
    /// 挑眼镜这段是**纯的**(只吃"描述符声明了什么"), 没真手柄也能钉。
    /// </summary>
    static void Case8_PickLayout()
    {
        Console.WriteLine("\n=== 场景8: 按描述符挑 collection(别一律问顶层) ===");

        // 造 caps 的小帮手: page / 报告号 / collection / 是不是范围 / 用法(范围)
        static HidPad.Cap A(ushort page, byte rid, ushort lc, ushort u)
            => new HidPad.Cap { Page = page, Rid = rid, Lc = lc, UMin = u, UMax = u };
        static HidPad.Cap R(ushort page, byte rid, ushort lc, ushort lo, ushort hi)
            => new HidPad.Cap { Page = page, Rid = rid, Lc = lc, Range = true, UMin = lo, UMax = hi };

        // ① **这次的真凶**: 同一份描述符里 0x01(插线)挂顶层、0x31(蓝牙)挂第 3 层 ——
        //    必须**按这一帧的报告号**挑层, 混着问就是 0xC011000A(报告号对不上)
        var l1 = HidPad.PickLayoutFrom(
            new[] { A(0x01, 0x01, 0, 0x30), A(0x01, 0x31, 3, 0x30) },
            new[] { A(0x09, 0x01, 0, 0x01), A(0x09, 0x31, 3, 0x02) });
        Check("蓝牙帧(0x31) -> 挑第几层", l1.AxisLcFor(0x31), (ushort)3);
        Check("插线帧(0x01) -> 挑第几层", l1.AxisLcFor(0x01), (ushort)0);
        Check("  蓝牙帧的按钮在第几层", string.Join("/", l1.BtnLcsFor(0x31)), "3");
        Check("  插线帧的按钮在第几层", string.Join("/", l1.BtnLcsFor(0x01)), "0");

        // ② 描述符只声明了顶层(插线那种) -> 挑 0, 跟老行为一致(别把好的改坏)
        var l2 = HidPad.PickLayoutFrom(
            new[] { A(0x01, 0x01, 0, 0x30) },
            new[] { A(0x09, 0x01, 0, 0x01) });
        Check("只有顶层 -> 挑第几层", l2.AxisLcFor(0x01), (ushort)0);

        // ③ 描述符用"用法范围"一口气声明(0x30~0x35), 里面含左摇杆 X -> 也算命中
        var l3 = HidPad.PickLayoutFrom(new[] { R(0x01, 0x31, 2, 0x30, 0x35) },
                                       new[] { A(0x09, 0x31, 2, 0x01) });
        Check("范围声明 0x30~0x35 -> 挑第几层", l3.AxisLcFor(0x31), (ushort)2);

        // ④ 按钮分散在 1 和 4 两层 -> 两层都得收着(少一层就少几颗键)
        var l4 = HidPad.PickLayoutFrom(new[] { A(0x01, 0x31, 1, 0x30) },
                                       new[] { A(0x09, 0x31, 1, 0x01), A(0x09, 0x31, 4, 0x02) });
        Check("按钮分散在 1/4 -> 收全没", string.Join("/", l4.BtnLcsFor(0x31)), "1/4");

        // ⑤ 描述符里没有 0x01 页的轴(键盘鼠标之类) -> 退回 0, 别崩
        var l5 = HidPad.PickLayoutFrom(new[] { A(0x0C, 1, 0, 0x01) }, new HidPad.Cap[0]);
        Check("没有摇杆 -> 退回第几层", l5.AxisLcFor(0x31), (ushort)0);
        Check("  按钮一个没有 -> 按摇杆那层问", string.Join("/", l5.BtnLcsFor(0x31)), "0");

        // ⑥ 别的页(陀螺仪 0x40 之类)也用 0x30 -> 不能抢在通用桌面页前面
        var l6 = HidPad.PickLayoutFrom(
            new[] { A(0x40, 0x31, 5, 0x30), A(0x01, 0x31, 7, 0x30) },
            new[] { A(0x09, 0x31, 7, 0x01) });
        Check("别的页也用 0x30 -> 认哪个", l6.AxisLcFor(0x31), (ushort)7);

        // ⑦ 报告号对不上时(描述符只声明 0x01, 却来了 0x31) -> 退回第一个, 总比崩好
        Check("来了没声明过的报告号 -> 退回", l2.AxisLcFor(0x31), (ushort)0);
    }

    static void Check(string what, string got, string want)
    {
        bool ok = got == want;
        if (!ok) _fail++;
        Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {what,-34} 得到 {got,-8} 该是 {want}");
    }

    /// <summary>按键掩码和 hat 都按十六进制看, 一眼能对上位。</summary>
    static void Check(string what, ushort got, ushort want)
        => Check(what, got.ToString("X4"), want.ToString("X4"));
}
