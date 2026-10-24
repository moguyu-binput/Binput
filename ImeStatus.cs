using System.Runtime.InteropServices;

namespace Binput;

/// <summary>
/// 读取"当前前台窗口"的系统输入法状态。
/// 键盘界面是我们自己画的、也不吃焦点, 所以真正的输入法状态只存在目标窗口那边,
/// 只能去问它: 拿前台窗口的键盘布局看语言, 再用 IMM32 看它现在是中文模式还是英文模式。
/// </summary>
public static class ImeStatus
{
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("imm32.dll")]
    static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint conv, out uint sentence);

    [DllImport("imm32.dll")]
    static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    static extern bool ImmSetConversionStatus(IntPtr hIMC, uint conv, uint sentence);

    [DllImport("imm32.dll")]
    static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    const uint WM_IME_CONTROL = 0x0283;
    const uint IMC_GETCONVERSIONMODE = 0x0002;
    const uint IME_CMODE_NATIVE = 0x0001;     // 中文(本地字符)模式, 关掉就是英文输入
    const uint IME_CMODE_FULLSHAPE = 0x0008;  // 全角

    // 简体/繁体/香港/新加坡/中性中文
    static readonly ushort[] ZhLangs = { 0x0804, 0x0404, 0x0C04, 0x1004, 0x0004 };

    /// <summary>前台窗口现在是不是在中文输入。读不到就 fall back 到键盘布局判断。</summary>
    public static bool Chinese()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            uint tid = GetWindowThreadProcessId(hwnd, out _);
            ushort lang = (ushort)((long)GetKeyboardLayout(tid) & 0xFFFF);
            bool zh = Array.IndexOf(ZhLangs, lang) >= 0;

            // 键盘布局只能说明"装了中文输入法", 说明不了此刻是中文还是英文模式:
            // 微软拼音切成英文输入时布局没变, 只有 conversion mode 会变。
            IntPtr imc = ImmGetContext(hwnd);
            if (imc != IntPtr.Zero)
            {
                try
                {
                    if (ImmGetConversionStatus(imc, out uint conv, out _))
                        return (conv & IME_CMODE_NATIVE) != 0;
                }
                finally { ImmReleaseContext(hwnd, imc); }
            }

            // 有些窗口拿不到 IMC(游戏窗口、部分自绘窗口), 直接问该窗口的 IME 窗口
            IntPtr imeWnd = ImmGetDefaultIMEWnd(hwnd);
            if (imeWnd != IntPtr.Zero)
            {
                IntPtr r = SendMessage(imeWnd, WM_IME_CONTROL,
                                       (IntPtr)IMC_GETCONVERSIONMODE, IntPtr.Zero);
                if (r != IntPtr.Zero)
                    return ((long)r & IME_CMODE_NATIVE) != 0;
            }

            return zh;
        }
        catch { return false; }
    }

    /// <summary>
    /// 把某个窗口的输入法切成中文 / 英文。
    /// 九键要用它: 九键拼音必须让系统输入法处于中文态才出汉字,
    /// 而 26 键必须切成英文态, 否则按字母会唤出拼音条 —— 那不是"纯字母"。
    /// </summary>
    public static bool SetChinese(IntPtr hwnd, bool on)
    {
        if (hwnd == IntPtr.Zero) return false;
        IntPtr imc = ImmGetContext(hwnd);
        if (imc == IntPtr.Zero) return false;
        try
        {
            ImmGetConversionStatus(imc, out uint conv, out uint sent);
            // 中文: 本地字符 + 全角符号, 比单独给 NATIVE 更接近日常中文输入状态
            uint want = on ? (conv | IME_CMODE_NATIVE | IME_CMODE_FULLSHAPE)
                           : (conv & ~IME_CMODE_NATIVE & ~IME_CMODE_FULLSHAPE);
            if (want == conv) return true;
            return ImmSetConversionStatus(imc, want, sent);
        }
        catch { return false; }
        finally { ImmReleaseContext(hwnd, imc); }
    }

    /// <summary>把当前前台窗口的输入法切成中文 / 英文。</summary>
    public static bool SetChinese(bool on) => SetChinese(GetForegroundWindow(), on);

    /// <summary>
    /// 确保前台窗口处在中文输入态。先走 ImmSetConversionStatus(精确、不会误触发别的开关),
    /// 不行再退到 Ctrl+Space。返回最终是不是中文。
    /// </summary>
    public static bool EnsureChinese()
    {
        if (Chinese()) return true;
        var hwnd = GetForegroundWindow();
        if (SetChinese(hwnd, true) && Chinese()) return true;
        Toggle();
        bool ok = Chinese();
        App.Log($"自动切中文: {(ok ? "成功" : "失败, 只能手动切")} 窗口={Win32.Describe(hwnd)}");
        return ok;
    }

    /// <summary>切换系统输入法的中/英。用 Ctrl+Space —— 各家的 IME 都认这个开关。</summary>
    public static void Toggle() => TextOutput.Chord(TextOutput.VK_CONTROL, TextOutput.VK_SPACE);
}
