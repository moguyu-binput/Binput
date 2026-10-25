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

// ============================================================
// fprobe 用的"假窗口F"。
//
// 真程序里那一摞层是 WPF 画的(OverlayWindow + LayerHost), 无头环境编不进来;
// 这里给 InputCore 一个最小替身 —— 它只要能回答"叠了几层 / 光标在哪一格 /
// 最上面那层叫什么", 并把格子里那几件事转给 IQuickHost 就行。
// 每一层装什么(1 写入短语 / 2 写入快捷键 / 3 删除 / 中间绑数字), 照抄
// OverlayWindow 里 QuickDeepPage / QuickNumPage 那份定义 —— 两边要是走岔了,
// 那才是真出事了, 所以故意写成第二份而不是去引用窗口的代码。
// ============================================================
public sealed class FakeQuickUI : IQuickUI
{
    sealed class Lev { public string Name = ""; public int Sel = 4; public int Page; }
    readonly List<Lev> _st = new();

    public InputCore Core;
    /// <summary>深层正在改哪一格(窗口那边叫 _qSlot)。</summary>
    public int Slot = -1;
    public int Picks, Deeps, Pops, Pages, Refreshes, Moves;

    public int Depth => _st.Count;
    public string Layer => _st.Count == 0 ? "" : _st[^1].Name;
    /// <summary>这一层摆几格: 深层是两行(下面让给那条长条), 其余三行。</summary>
    int Slots => _st.Count > 0 && _st[^1].Name == InputCore.Q_LAYER_DEEP ? 6 : 9;

    public int Sel
    {
        get => _st.Count == 0 ? -1 : _st[^1].Sel;
        set { if (_st.Count > 0) _st[^1].Sel = Math.Clamp(value, 0, Slots - 1); }
    }
    public int Page
    {
        get => _st.Count == 0 ? 0 : _st[^1].Page;
        set { if (_st.Count > 0) _st[^1].Page = Math.Clamp(value, 0, 4); }
    }
    /// <summary>列表层光标那一格是第几号槽位。</summary>
    public int CursorSlot => _st.Count == 0 ? -1 : _st[^1].Page * 9 + _st[^1].Sel;

    public void Open() { _st.Clear(); Slot = -1; _st.Add(new Lev { Name = InputCore.Q_LAYER_LIST }); }
    public void Close() { _st.Clear(); Slot = -1; }

    void Push(string name) { _st.Add(new Lev { Name = name }); }
    bool PopOne()
    {
        if (_st.Count <= 1) return false;
        _st.RemoveAt(_st.Count - 1);
        Pops++;
        return true;
    }

    /// <summary>光标这一格是干什么的(照抄窗口里那一层九格的定义)。</summary>
    void Activate()
    {
        if (_st.Count == 0) return;
        var host = (IQuickHost)Core;
        var l = _st[^1];
        if (l.Name == InputCore.Q_LAYER_LIST)
        {
            int slot = l.Page * 9 + l.Sel;
            // 空格子往下钻一层 —— 空格子本来就没东西可发, 按上去多半是想往里写点什么
            if (QuickStore.At(slot) == null) { DoDeep(slot); return; }
            host.Send(slot);
            return;
        }
        if (l.Name == InputCore.Q_LAYER_DEEP)
        {
            switch (l.Sel)
            {
                case 0: host.WritePhrase(Slot); break;      // 1 写入短语
                case 1: host.RecordHotkey(Slot); break;     // 2 写入快捷键
                case 2: host.Delete(Slot); break;           // 3 删除
                case 4: Push(InputCore.Q_LAYER_NUM); break; // 5 中间: 绑的数字
                default: break;                             // 4 / 6 空着, 按了没反应
            }
            return;
        }
        if (l.Name == InputCore.Q_LAYER_NUM) host.Bind(Slot, l.Sel + 1);
    }

    void DoDeep(int slot)
    {
        if (_st.Count == 0 || _st[^1].Name != InputCore.Q_LAYER_LIST) return;
        Slot = slot;
        Deeps++;
        Push(InputCore.Q_LAYER_DEEP);
    }

    // ---- IQuickUI ----
    int IQuickUI.Depth => Depth;
    string IQuickUI.Layer => Layer;
    // 和窗口里 MoveSel 同一套: 走到这一层的边上就绕回来(三列, 上下 ±3 / 左右 ±1)
    void IQuickUI.Nav(int d) => Move(d * 3);
    void IQuickUI.NavH(int d) => Move(d);
    void Move(int d)
    {
        if (_st.Count == 0) return;
        int slots = Slots;
        _st[^1].Sel = ((_st[^1].Sel + d) % slots + slots) % slots;
    }
    bool _stickNav;
    // 和窗口里 LayerStick 同一套: **推着**才跟着走, 松手那一下回中,
    // 摇杆一直没动就别碰光标(否则十字键一格格挪过去的成果会被静止的摇杆顶掉)。
    void IQuickUI.Stick(double x, double y)
    {
        if (_st.Count == 0) return;
        if (Math.Sqrt(x * x + y * y) >= InputCore.DZ_N) { _stickNav = true; Sel = InputCore.PointTo9(x, y) - 1; }
        else if (_stickNav) { _stickNav = false; Sel = 4; }
    }
    void IQuickUI.Page(int d) { Page += d; Pages++; }
    void IQuickUI.Pick() { Picks++; Activate(); }
    void IQuickUI.Deep() { DoDeep(CursorSlot); }
    bool IQuickUI.Back(bool toRoot) => PopOne();
    void IQuickUI.Refresh() { Refreshes++; }
    void IQuickUI.Pop() { PopOne(); }
    void IQuickUI.Move(int slot) { Slot = slot; Moves++; }
    void IQuickUI.StageOff() { }
    void IQuickUI.StageOn() { }
    // 退回 / 重画由 core 那边叫(QuickUnbindSlot 里 Pop + Move + Refresh),
    // 这里**不能再退一层** —— 退两次就从深层一路掉回列表了。
    void IQuickUI.Y()
    {
        if (Layer != InputCore.Q_LAYER_NUM) return;
        ((IQuickHost)Core).Unbind(Slot);
    }
}
