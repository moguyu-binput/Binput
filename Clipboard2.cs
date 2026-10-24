using System.Runtime.InteropServices;

namespace Binput;

/// <summary>
/// 原生剪贴板读写。
/// 为什么不用 WPF 的 System.Windows.Clipboard: 它走的是 OLE(OleSetClipboard),
/// 失败时内部会自重试 10 次、每次 Sleep(100ms) —— 在 FF14 里实测稳定抛
/// CLIPBRD_E_CANT_OPEN, 外面再套重试就是好几秒的界面假死。
/// 直接 OpenClipboard + SetClipboardData(CF_UNICODETEXT) 成功率最高, 也最可控。
/// </summary>
public static class Clipboard2
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    static extern IntPtr GetOpenClipboardWindow();

    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalFree(IntPtr hMem);

    const uint GMEM_MOVEABLE = 0x0002;
    const uint CF_UNICODETEXT = 13;

    /// <summary>重试次数 × 每次 40ms —— 最多 480ms, 绝不让它把界面卡住。</summary>
    const int RETRIES = 12, RETRY_MS = 40;

    public static bool Set(string s)
    {
        s ??= "";
        for (int i = 0; i < RETRIES; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    // CF_UNICODETEXT 需要 (字符数+1)*2 字节, 末尾补 \0
                    IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (nuint)((s.Length + 1) * 2));
                    if (h == IntPtr.Zero) { App.Log("剪贴板: GlobalAlloc 失败"); return false; }

                    IntPtr p = GlobalLock(h);
                    if (p == IntPtr.Zero)
                    {
                        GlobalFree(h);
                        App.Log("剪贴板: GlobalLock 失败");
                        return false;
                    }
                    Marshal.Copy(s.ToCharArray(), 0, p, s.Length);
                    Marshal.WriteInt16(p, s.Length * 2, 0);
                    GlobalUnlock(h);

                    if (SetClipboardData(CF_UNICODETEXT, h) == IntPtr.Zero)
                    {
                        int e = Marshal.GetLastWin32Error();
                        GlobalFree(h);
                        App.Log($"剪贴板: SetClipboardData 失败 err={e}");
                        return false;
                    }
                    // 成功之后 h 归剪贴板所有, 不能再 GlobalFree
                    return true;
                }
                finally { CloseClipboard(); }
            }

            int err = Marshal.GetLastWin32Error();
            if (i == 0) LogOwner(err);       // 只记一次, 别刷屏
            Thread.Sleep(RETRY_MS);
        }
        return false;
    }

    /// <summary>
    /// 打开剪贴板失败时把"谁占着"记下来 —— 这是判断还能不能救的唯一线索。
    /// 常见元凶: 剪贴板增强软件(Ditto / ArsClip / 系统剪贴板历史)、远程桌面、以及游戏自己。
    /// </summary>
    static void LogOwner(int err)
    {
        try
        {
            IntPtr h = GetOpenClipboardWindow();
            if (h == IntPtr.Zero)
            {
                App.Log($"剪贴板打不开 err={err}, 但无人持有(可能是权限/会话问题)");
                return;
            }
            App.Log($"剪贴板打不开 err={err} — 占用者: {Win32.Describe(h)}");
        }
        catch (Exception ex) { App.Log("读剪贴板占用者失败: " + ex.Message); }
    }

}
