// InputCore / T9Engine 的替身。真版本会发按键、切输入法、读 XInput,
// 测试里换成只记账的假货, 这样能在无窗口、无手柄的环境里跑。
namespace Binput;

public struct PadState
{
    public bool Connected;
    public ushort Buttons;
    public byte LeftTrigger, RightTrigger;
    public short LX, LY, RX, RY;
}

public static class GamepadInput
{
    public const ushort DPAD_UP = 0x0001, DPAD_DOWN = 0x0002, DPAD_LEFT = 0x0004, DPAD_RIGHT = 0x0008;
    public const ushort START = 0x0010, BACK = 0x0020, L3 = 0x0040, R3 = 0x0080;
    public const ushort LB = 0x0100, RB = 0x0200;
    public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

    public static PadState GetState(int index = 0) => new PadState { Connected = false };
}

public static class App
{
    public static bool Quiet;
    public static void Log(string msg)
    {
        if (!Quiet) Console.WriteLine("        | " + msg);
    }
}

/// <summary>把"本该发给窗口的按键"记成一条字符串, 测试据此断言到底发了什么。</summary>
public static class TextOutput
{
    public static bool ImeMode;
    public static readonly List<string> Sent = new();
    // 真家伙里的虚拟键常量, 这里照抄一份, InputCore 会直接引用它们
    public const ushort VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_UP = 0x26, VK_DOWN = 0x28;
    public const ushort VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D,
                        VK_ESCAPE = 0x1B, VK_SPACE = 0x20;

    // 26 键全键盘的"粘住"修饰键。真版本里它们管真按键, 这里只管记账。
    public static bool ModCtrl, ModAlt, ModWin;
    public static bool AnyMod => ModCtrl || ModAlt || ModWin;
    public static void ClearMods() { ModCtrl = ModAlt = ModWin = false; }

    public static void Reset() { Sent.Clear(); ClearMods(); }
    // 1.2.0 快捷键用的"修饰键 + 主键"组合。桩里只记账, 不真按。
    public static void Chord(ushort mod, ushort vk) => Sent.Add($"CH:{mod:X}:{vk:X}");
    // 不过闸门的那一版(发快捷键用, 见 TextOutput.ChordRaw): 桩里一样只记账。
    public static void ChordRaw(ushort mod, ushort vk) => Sent.Add($"CHR:{mod:X}:{vk:X}");

    public static void Text(string s) { foreach (char c in s) Sent.Add("U:" + c); }
    public static void Letter(char c) => Sent.Add(ImeMode ? "VK:" + c : "U:" + c);
    public static void Digit(int d) => Sent.Add(ImeMode ? "VK:" + d : "U:" + d);
    public static void Ascii(char c) => Sent.Add("VK:" + c);
    public static void Space() => Sent.Add(ImeMode ? "VK:SPACE" : "U: ");
    public static void Enter() => Sent.Add("VK:ENTER");
    public static void Escape() => Sent.Add("VK:ESC");
    public static void Arrow(ushort vk) => Sent.Add("VK:" + vk switch
    {
        0x25 => "LEFT", 0x27 => "RIGHT", 0x26 => "UP", _ => "DOWN",
    });
    public static void Backspace() => Sent.Add("VK:BACK");

    /// <summary>26 键: 敲一个功能键, 顺便带上粘住的修饰键。</summary>
    public static void Hit(ushort vk, bool ctrl, bool alt, bool win)
    {
        Sent.Add("VK:" + Mods(ctrl, alt, win) + VkName(vk));
        ClearMods();
    }

    /// <summary>26 键的字符键。没粘修饰键时走 UNICODE 直投(26 键要纯字符)。</summary>
    public static void HitChar(char c, bool ctrl, bool alt, bool win)
    {
        if (!ctrl && !alt && !win) { Sent.Add("U:" + c); return; }
        Sent.Add("VK:" + Mods(ctrl, alt, win) + c);
        ClearMods();
    }

    static string Mods(bool ctrl, bool alt, bool win) =>
        (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (win ? "Win+" : "");

    static string VkName(ushort vk) => vk switch
    {
        0x08 => "BACK", 0x09 => "TAB", 0x0D => "ENTER", 0x1B => "ESC", 0x20 => "SPACE",
        0x25 => "LEFT", 0x27 => "RIGHT", 0x26 => "UP", 0x28 => "DOWN",
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),
        _ => "0x" + vk.ToString("X"),
    };

    /// <summary>把记下来的按键还原成人看得懂的一串(只看虚拟键那条路)。</summary>
    public static string Flatten()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in Sent)
        {
            if (s.StartsWith("VK:")) sb.Append(s.Substring(3));
            else if (s.StartsWith("U:")) sb.Append(s.Substring(2));
        }
        return sb.ToString();
    }
}

public static class ImeStatus
{
    public static bool Chinese() => true;
    public static bool SetChinese(IntPtr hwnd, bool on) => true;
    public static bool SetChinese(bool on) => true;
    public static bool EnsureChinese() => true;
    public static void Toggle() { }
}

/// <summary>
/// 窗口F 那一摞页的替身(2.3.3 加): 只记"core 叫过哪一句"。
/// coretest 以前没有这个桩 —— L3 解除绑定那一桩是探不到的(没开真窗口),
/// 现在解绑归了 Y, 这一条必须能被无头探针盯住。
/// </summary>
public sealed class QuickUiSpy : IQuickUI
{
    public string LayerName = "";
    public int YCalls;                 // Y 被叫了几次(绑数字那层 = 解除绑定)

    public int Depth { get; set; } = 1;
    public string Layer => LayerName;
    public void Nav(int d) { }
    public void NavH(int d) { }
    public void Stick(double x, double y) { }
    public void Pick() { }
    public void Deep() { }
    public bool Back(bool toRoot) => false;
    public void Page(int d) { }
    public void Refresh() { }
    public void Pop() { }
    public void Move(int slot) { }
    public void Y() => YCalls++;
    public void StageOff() { }
    public void StageOn() { }
}
