using System.Runtime.InteropServices;

namespace Binput;

/// <summary>
/// 前台窗口 / 焦点相关的 Win32 调用。
/// 预输入版要靠它们把我们的记事本窗口顶到前台, 收起时再把焦点还回游戏。
/// </summary>
public static class Win32
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder s, int n);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    /// <summary>
    /// 鼠标当前位置(屏幕像素)。主界面默认整块是鼠标穿透的,
    /// 靠定时查这个值判断"指针是不是停在齿轮上", 是就临时把穿透关掉, 齿轮才点得到。
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT p);

    const int SW_SHOW = 5, SW_RESTORE = 9;

    /// <summary>
    /// 这个窗口是不是我们自己进程的(托盘菜单、设置窗口、输入法候选窗都算)。
    /// 判断"用户是不是点到别的应用去了"时要排除掉它们。
    /// </summary>
    public static bool SameProcess(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// 把任意窗口顶成前台。
    /// 直接 SetForegroundWindow 在"调用者不是当前前台进程"时会被系统无视 ——
    /// 我们恰恰就是这种情况(手柄按一下, 前台还是游戏)。
    /// 标准解法: 先把两个线程的输入队列绑一下(AttachThreadInput), 再顶, 再解开。
    /// </summary>
    public static bool ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        IntPtr fore = GetForegroundWindow();
        if (fore == hwnd) return true;

        uint foreTid = GetWindowThreadProcessId(fore, out _);
        uint curTid = GetCurrentThreadId();

        bool attached = false;
        if (foreTid != 0 && foreTid != curTid)
            attached = AttachThreadInput(foreTid, curTid, true);

        try
        {
            ShowWindow(hwnd, IsIconic(hwnd) ? SW_RESTORE : SW_SHOW);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(foreTid, curTid, false);
        }

        return GetForegroundWindow() == hwnd;
    }

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    // ---- 进程完整性级别 ----
    // 这是"记事本里好好的、游戏里一点反应没有"最常见的隐藏原因: Windows 的 UIPI
    // 会**静默丢掉**从低完整性进程发往高完整性窗口的输入(SendInput 返回 0, 没有报错)。
    // 游戏/启动器被"以管理员身份运行"过就会高一级, 这时候我们发的键一个都到不了。

    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll")]
    static extern bool OpenProcessToken(IntPtr proc, uint access, out IntPtr token);
    [DllImport("advapi32.dll")]
    static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr buf, int len, out int got);
    // 注意: 这两个 API 是"**返回**指向子授权值的指针", 不是 bool + out 参数。
    // 一开始写成了 `static extern bool GetSidSubAuthorityCount(IntPtr sid, out byte n)`,
    // 结果那个 out 参数没有任何人写, n 永远是 0, 完整性级别全部读成 0(显示成"不受信任"),
    // UIPI 检查等于一直是瞎的。必须按"返回 IntPtr, 自己 Marshal.ReadByte"来写。
    [DllImport("advapi32.dll")]
    static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")]
    static extern IntPtr GetSidSubAuthority(IntPtr sid, uint idx);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint TOKEN_QUERY = 0x0008;
    const int TokenIntegrityLevel = 25;
    // 用 int 而不是 uint: IntegrityName 的 switch 是 int, uint 的 case 标签过不了编译。
    const int SECURITY_MANDATORY_UNTRUSTED_RID = 0x00000000,
              SECURITY_MANDATORY_LOW_RID = 0x00001000,
              SECURITY_MANDATORY_MEDIUM_RID = 0x00002000,
              SECURITY_MANDATORY_MEDIUM_PLUS_RID = 0x00002100,
              SECURITY_MANDATORY_HIGH_RID = 0x00003000,
              SECURITY_MANDATORY_SYSTEM_RID = 0x00004000;

    /// <summary>
    /// 某个进程的完整性级别, 返回那几个 RID 之一。
    /// **取不到返回 -1** —— 必须和"不受信任"(RID=0) 分开, 否则读不出来会被
    /// 误判成"两边一样", UIPI 检查就白做了。
    /// 只看整数大小就行: 我们的级别 < 对方的级别 -> 发过去的键会被 UIPI 丢掉。
    /// </summary>
    public static int IntegrityOfPid(uint pid)
    {
        IntPtr proc = IntPtr.Zero, tok = IntPtr.Zero, buf = IntPtr.Zero;
        try
        {
            proc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (proc == IntPtr.Zero) return -1;
            if (!OpenProcessToken(proc, TOKEN_QUERY, out tok) || tok == IntPtr.Zero) return -1;

            int len = 0;
            GetTokenInformation(tok, TokenIntegrityLevel, IntPtr.Zero, 0, out len);
            if (len <= 0) return -1;
            buf = Marshal.AllocHGlobal(len);
            if (!GetTokenInformation(tok, TokenIntegrityLevel, buf, len, out _)) return -1;

            // TOKEN_MANDATORY_LABEL{ SID_AND_ATTRIBUTES Label{ IntPtr Sid; uint Attr; } }
            IntPtr sid = Marshal.ReadIntPtr(buf);
            if (sid == IntPtr.Zero) return -1;
            IntPtr pn = GetSidSubAuthorityCount(sid);
            if (pn == IntPtr.Zero) return -1;
            int n = Marshal.ReadByte(pn);
            if (n < 1) return -1;
            IntPtr pv = GetSidSubAuthority(sid, (uint)(n - 1));
            return Marshal.ReadInt32(pv);
        }
        catch { return -1; }
        finally
        {
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            if (tok != IntPtr.Zero) CloseHandle(tok);
            if (proc != IntPtr.Zero) CloseHandle(proc);
        }
    }

    /// <summary>完整性级别的名字, 只给日志看。-1 = 没读出来。</summary>
    public static string IntegrityName(int rid) => rid switch
    {
        -1 => "(读不出来)",
        SECURITY_MANDATORY_UNTRUSTED_RID => "不受信任",
        SECURITY_MANDATORY_LOW_RID => "低",
        SECURITY_MANDATORY_MEDIUM_RID => "中(普通)",
        SECURITY_MANDATORY_MEDIUM_PLUS_RID => "中+",
        SECURITY_MANDATORY_HIGH_RID => "高(管理员)",
        SECURITY_MANDATORY_SYSTEM_RID => "系统",
        _ => $"0x{rid:X}",
    };

    /// <summary>某个窗口所属进程的完整性级别; 取不到返回 -1。</summary>
    public static int IntegrityOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return -1;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == 0 ? -1 : IntegrityOfPid(pid);
    }

    public static string WindowTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(无)";
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, sb, 256);
        return sb.Length > 0 ? sb.ToString() : "(无标题)";
    }

    public static string ProcName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "?";
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName + ".exe";
        }
        catch { return "?"; }
    }

    /// <summary>
    /// 单实例: 登记一条全系统唯一的消息。第二个实例启动时会把它广播出去,
    /// 已经在跑的那个收到就把界面唤出来。
    /// RegisterWindowMessage 的值在"还有任何进程登记着它"期间是稳定的,
    /// 所以两个进程拿到的是同一个号。
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string name);

    /// <summary>给别的进程发消息, 对方卡住也不会把自己拖死(带超时)。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg,
        IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

    /// <summary>
    /// 找另一个进程里标题为 title 的顶层窗口(找"另一个自己"用)。
    /// 注意不去过滤 IsWindowVisible —— 界面被藏起来的时候也要能找到,
    /// 不然第二个实例就只能靠广播唤醒, 而广播会被 UIPI 拦在权限更高的窗口外面。
    /// </summary>
    public static IntPtr FindWindowOfOtherProcess(string title)
    {
        IntPtr found = IntPtr.Zero;
        uint self = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            var sb = new System.Text.StringBuilder(64);
            if (GetWindowText(h, sb, 64) <= 0 || sb.ToString() != title) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0 || pid == self) return true;
            found = h;
            return false;      // 找到了, 停
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>给日志用的一句话描述: 标题 [进程]。</summary>
    public static string Describe(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? "(无窗口)" : $"\"{WindowTitle(hwnd)}\" [{ProcName(hwnd)}] 0x{hwnd.ToInt64():X}";
}
