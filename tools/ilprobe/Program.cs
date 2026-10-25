using System.Runtime.InteropServices;

// 一次性排查工具: 把"读进程完整性级别"每一步的结果都打出来,
// 看看到底是哪一步失败 —— 柄输日志里两边都显示"不受信任"说明整个读取是失败的。

static class P
{
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint a, bool i, uint p);
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr p, uint a, out IntPtr t);
    [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr t, int c, IntPtr b, int l, out int g);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr s);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr s, uint i);
    [DllImport("advapi32.dll")] static extern bool ConvertSidToStringSid(IntPtr s, out IntPtr str);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public static string Run(uint pid)
    {
        var sb = new System.Text.StringBuilder();
        IntPtr proc = OpenProcess(0x1000, false, pid);
        sb.Append("OpenProcess=" + (proc != IntPtr.Zero ? "ok" : "FAIL " + Marshal.GetLastWin32Error()));
        if (proc == IntPtr.Zero) return sb.ToString();

        if (!OpenProcessToken(proc, 0x0008, out IntPtr tok) || tok == IntPtr.Zero)
        { sb.Append(" | OpenProcessToken=FAIL " + Marshal.GetLastWin32Error()); return sb.ToString(); }
        sb.Append(" | token=ok");

        GetTokenInformation(tok, 25, IntPtr.Zero, 0, out int len);
        sb.Append(" | err=" + Marshal.GetLastWin32Error() + " len=" + len);
        if (len <= 0) { CloseHandle(tok); CloseHandle(proc); return sb.ToString(); }

        IntPtr buf = Marshal.AllocHGlobal(len);
        bool ok = GetTokenInformation(tok, 25, buf, len, out _);
        sb.Append(" | GetTokenInfo=" + (ok ? "ok" : "FAIL " + Marshal.GetLastWin32Error()));

        IntPtr sid = Marshal.ReadIntPtr(buf);
        sb.Append(" | sid=" + (sid != IntPtr.Zero ? "ok" : "NULL"));
        if (sid != IntPtr.Zero)
        {
            IntPtr pc = GetSidSubAuthorityCount(sid);
            byte n = Marshal.ReadByte(pc);
            sb.Append(" | count=" + n);
            if (n >= 1)
            {
                IntPtr pa = GetSidSubAuthority(sid, (uint)(n - 1));
                sb.Append(" | lastRid=0x" + ((uint)Marshal.ReadInt32(pa)).ToString("X"));
            }
            if (ConvertSidToStringSid(sid, out IntPtr str))
            {
                sb.Append(" | sidStr=" + Marshal.PtrToStringUni(str));
                Marshal.FreeHGlobal(str);
            }
            else sb.Append(" | ConvertSid=FAIL " + Marshal.GetLastWin32Error());
        }
        Marshal.FreeHGlobal(buf);
        CloseHandle(tok);
        CloseHandle(proc);
        return sb.ToString();
    }

    static void Main(string[] args)
    {
        var pids = new List<uint>();
        if (args.Length > 0) foreach (var a in args) if (uint.TryParse(a, out uint v)) pids.Add(v);
        else
        {
            pids.Add((uint)Environment.ProcessId);
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("ffxiv_dx11")) pids.Add((uint)p.Id);
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("notepad")) pids.Add((uint)p.Id);
        }
        foreach (var pid in pids)
        {
            string name = "";
            try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
            Console.WriteLine($"pid={pid} [{name}] -> {Run(pid)}");
        }
    }
}
