using System.Runtime.InteropServices;
using System.Threading;

namespace Binput;

/// <summary>
/// 把文字送进当前拥有焦点的窗口。
/// ASCII 走虚拟键(兼容游戏里只认按键的输入框),
/// 中文等非 ASCII 走 KEYEVENTF_UNICODE 直接投递字符, 不需要注册输入法。
/// </summary>
public static class TextOutput
{
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx, dy, mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUT_UNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public INPUT_UNION u;
    }

    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_UNICODE = 0x0004;

    public const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    public const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B;
    public const ushort VK_DELETE = 0x2E;
    public const ushort VK_SPACE = 0x20, VK_LEFT = 0x25, VK_RIGHT = 0x27;
    public const ushort VK_UP = 0x26, VK_DOWN = 0x28;
    /// <summary>左 Win。26 键全键盘最底下那排要用到(Win 键本身)。</summary>
    public const ushort VK_LWIN = 0x5B;

    /// <summary>
    /// 系统输入法模式开关。开着时字母/数字/空格都发虚拟键, 由系统输入法(微软拼音等)
    /// 接管拼音 composition 和候选; 关着时走 KEYEVENTF_UNICODE 直接投递字符, 绕开输入法。
    /// 由 InputCore.SetImeMode 统一设置, 不要单独改 —— 两边不同步会走错路径。
    /// </summary>
    public static bool ImeMode;

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    static INPUT KeyInput(ushort vk, ushort scan, uint flags)
    {
        // 扫描码补上: 真键盘按下时消息里既有虚拟键也有扫描码, 只发虚拟键的"半个"
        // 在少数游戏里会被当成无效按键丢掉。填上去没有副作用(不设 SCANCODE 标志,
        // 系统仍按 wVk 认键, wScan 只是随消息一起送过去)。
        if (scan == 0 && vk != 0) scan = (ushort)MapVirtualKey(vk, 0);
        return new()
        {
            type = INPUT_KEYBOARD,
            u = new INPUT_UNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    /// <summary>
    /// 最近一次 SendInput 有没有被系统收下。**返回 0 就是被丢了** ——
    /// 最常见的原因是 UIPI: 目标窗口(游戏)的完整性级别比我们高(比如它是以管理员
    /// 身份运行的), 系统静默丢弃, 不报错。记事本能收到、游戏收不到就是这个症状。
    /// </summary>
    public static bool LastSendOk = true;

    static bool SendOne(INPUT i)
    {
        // 探针专用闸门。放在这里是因为 SendInput 是**唯一**的出口: 探针(tools/ 下那些)
        // 跑在命令行里, 它推演状态机时会一路走到 SendPinyin, 要是这里不拦,
        // 每次跑探针都真往当时有焦点的窗口里打一串拼音 —— 你在写文档它就打进文档里。
        // 以前只在 FinishSent 里拦了空格那一下, 管不住 SendPinyin, 等于没拦。
        if (InputCore.NoSend) return true;

        uint n = SendInput(1, new[] { i }, Marshal.SizeOf<INPUT>());
        if (n == 1) return true;
        LastSendOk = false;
        return false;
    }

    // ---- 闸门: 预览框还在往上升的时候, 先把要发的键扣下 ----
    //
    // 【为什么要这道闸门】按 RB 把拼音发出去, 系统输入法的候选条会跟着**光标**
    // 弹出来。而预览框这会儿正从九键方框里往上升(头一回打字的那 340 毫秒)——
    // 候选条按"这一瞬间光标在哪儿"定位, 等框升到位, 它就落在框**下面**一截的地方,
    // 中间空出一道。用户原话: "应该等待预览框上升完成后, 再输出拼音到预览框"。
    //
    // 所以顺序反过来: 先让框(空着)升起来、升到位, 再把拼音发进去。
    // 闸门装在 Send 上 —— 它是**所有发键的唯一出口**(下面 Ascii / Letter / Key /
    // Chord / Hit 全走它), 只在一个地方拦就不会漏。
    //
    // 装不装由 UI 决定(见 OverlayWindow.NoteGate): 探针里不装, 恒为 null = 照发。
    /// <summary>返回 true = "这批先别发, 排进队里", 由 UI 演完动画调 Flush() 补发。</summary>
    public static Func<bool> Hold;

    static readonly List<INPUT> _pend = new();

    /// <summary>队里还压着几个键。</summary>
    public static int Pending => _pend.Count;

    /// <summary>
    /// 把扣下的键按**原顺序**补发出去。顺序必须保住 ——
    /// 拼音是一个字母一个键发的, 发反了就拼不成那个字。
    /// </summary>
    public static void Flush()
    {
        if (_pend.Count == 0) return;
        var batch = _pend.ToArray();
        _pend.Clear();                      // 先清再发: 补发途中又有新键进来不会乱序
        for (int i = 0; i < batch.Length; i++) SendOne(batch[i]);
    }

    /// <summary>
    /// 这一批键里有没有"不产生文字"的那种 —— 退格 / 删除 / Esc / 回车 / 方向键。
    /// 【为什么要挑出来】闸门是"要往预览框里写字了, 先把框升起来"的意思,
    /// 可退格是**删**字的: 框里本来就没字的时候按 X, 删完还是没字,
    /// 却把空框升起来了 —— 用户原话 "没有拼音候选、没有文字的时候, 按下 X 也会
    /// 跳出来预览框"。所以这类键一律不走闸门: 既不拦, 也不许它把框叫起来。
    /// </summary>
    static bool IsEditing(INPUT[] inputs)
    {
        for (int i = 0; i < inputs.Length; i++)
        {
            ushort vk = inputs[i].u.ki.wVk;
            if (vk == VK_BACK || vk == VK_DELETE || vk == VK_ESCAPE || vk == VK_RETURN
             || vk == VK_TAB || vk == VK_LEFT || vk == VK_RIGHT || vk == VK_UP || vk == VK_DOWN)
                return true;
        }
        return false;
    }

    /// <summary>
    /// gated = 过不过上面那道闸门。**默认过**。
    /// 只有"发给游戏的一串组合键"(窗口F 的快捷键)会传 false —— 见 <see cref="ChordRaw"/>)。
    /// </summary>
    static bool Send(bool gated, params INPUT[] inputs)
    {
        if (gated && Hold != null && !IsEditing(inputs) && Hold())
        {
            _pend.AddRange(inputs);
            return true;
        }
        bool ok = true;
        for (int i = 0; i < inputs.Length; i++)
            if (!SendOne(inputs[i])) ok = false;
        return ok;
    }

    static bool Send(params INPUT[] inputs) => Send(true, inputs);

    /// <summary>
    /// 输入一段文本。统一走 KEYEVENTF_UNICODE, 不走虚拟键。
    /// 原因: 发虚拟键(VK_A 之类)会被系统中文输入法拦截, 英文模式下打 'a' 反而唤出微软拼音。
    /// UNICODE 方式直接投递 WM_CHAR, IME 没有 composition 就不会拦截。
    /// 标点/上屏的词走这里; 单个字母请走 Letter(), 它会按 ImeMode 选路径。
    /// </summary>
    public static void Text(string s)
    {
        foreach (char c in s) Unicode(c);
    }

    static void Unicode(char c)
    {
        Send(KeyInput(0, c, KEYEVENTF_UNICODE),
             KeyInput(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
    }

    /// <summary>
    /// 字母。系统输入法模式下走虚拟键 —— 这正是"26 键调出输入法"那个 bug 的原理,
    /// 现在反过来利用: 让微软拼音收到按键去做拼音 composition, 词库和联想都由它给。
    /// 关掉时走 UNICODE 直投, 游戏里打英文不会被输入法插一脚。
    /// </summary>
    public static void Letter(char c)
    {
        if (!ImeMode) { Unicode(c); return; }
        if (char.IsUpper(c)) Shifted(c);
        else Ascii(c);
    }

    /// <summary>
    /// 数字。系统输入法模式下也走虚拟键: 拼字过程中数字键是"选第 N 个候选",
    /// 没在拼字时就是普通的输数字 —— 同一个动作在两种语境下都对。
    /// </summary>
    public static void Digit(int d)
    {
        if (ImeMode) { Key((ushort)('0' + d)); return; }
        Unicode((char)('0' + d));
    }

    static ushort VkOf(char c) => c switch
    {
        ' ' => 0x20,
        >= 'a' and <= 'z' => (ushort)(c - 32),
        >= 'A' and <= 'Z' => c,
        >= '0' and <= '9' => c,
        ',' => 0xBC, '.' => 0xBE, '/' => 0xBF, ';' => 0xBA,
        '\'' => 0xDE, '[' => 0xDB, ']' => 0xDD, '\\' => 0xDC,
        '-' => 0xBD, '=' => 0xBB, '`' => 0xC0,
        _ => 0
    };

    /// <summary>按虚拟键输入一个字符(不带修饰键)。</summary>
    public static void Ascii(char c)
    {
        ushort vk = VkOf(c);
        if (vk == 0) { Unicode(c); return; }
        Key(vk);
    }

    /// <summary>Shift + 虚拟键。中文输入法下 Shift+字母会直接上屏一个大写英文。</summary>
    public static void Shifted(char c)
    {
        ushort vk = VkOf(c);
        if (vk == 0) { Unicode(c); return; }
        Chord(VK_SHIFT, vk);
    }

    /// <summary>组合键: 修饰键按下 -> 敲一下 vk -> 修饰键松开。切输入法用 Ctrl+Space。</summary>
    public static void Chord(ushort mod, ushort vk) =>
        Send(true, KeyInput(mod, 0, 0),
             KeyInput(vk, 0, 0),
             KeyInput(vk, 0, KEYEVENTF_KEYUP),
             KeyInput(mod, 0, KEYEVENTF_KEYUP));

    /// <summary>
    /// 组合键的**不过闸门**版本。窗口F 发快捷键走这里(见 InputCore.SendHotkey)。
    ///
    /// 【为什么要单独一条】闸门的意思是"要往预览框里写字了, 先把框升起来再写"。
    /// 可快捷键是**发给游戏的命令**, 不是写进预览框的字 —— 走闸门的话, 发一次
    /// 快捷键就把一个空预览框凭空升起来(那一串键还被压着晚 340 毫秒才补发),
    /// 表现正是"点了输出快捷键, 它也把预览框打开了"。
    /// </summary>
    public static void ChordRaw(ushort mod, ushort vk) =>
        Send(false, KeyInput(mod, 0, 0),
             KeyInput(vk, 0, 0),
             KeyInput(vk, 0, KEYEVENTF_KEYUP),
             KeyInput(mod, 0, KEYEVENTF_KEYUP));

    // ---- 送给游戏的"慢速"版本 ----
    //
    // 上面那些是一次性连着发完的: 按下和松开之间几乎没有间隔(同一毫秒内)。
    // 对按 Windows 消息的窗口来说完全没问题, 但**游戏是按帧采样键盘状态的** ——
    // 按下和松开落在同一帧里, 采到的就是"这一帧没按键", 表现正是
    // "我们明明发了回车和 Ctrl+V, 游戏一点反应没有"。
    // 收起之后那几步(叫出聊天框、粘贴)本来就是慢节奏的一次性动作,
    // 多等这几十毫秒无所谓, 能不能被采到才是关键, 所以那边一律走这两个。

    /// <summary>
    /// 按下一个键, 按住 holdMs 再松开。返回"系统有没有收下"。
    /// <para>
    /// <paramref name="gated"/> = 过不过"预览框上升"那道闸门(见 <see cref="Hold"/>)。
    /// **送给游戏的那几步要传 false** —— 见 <see cref="ChordSlow"/>, 同一个道理。
    /// </para>
    /// </summary>
    public static bool KeySlow(ushort vk, int holdMs = 90, bool gated = true)
    {
        LastSendOk = true;
        bool a = Send(gated, KeyInput(vk, 0, 0));
        Thread.Sleep(holdMs);
        bool b = Send(gated, KeyInput(vk, 0, KEYEVENTF_KEYUP));
        return a && b && LastSendOk;
    }

    /// <summary>
    /// 组合键的慢版本: 修饰键按下 -> 等 gapMs -> 敲一下(按住 holdMs) -> 全松开。
    /// <para>
    /// 【<paramref name="gated"/> = false 是给"收起键盘之后发给游戏"用的】2.1.25:
    /// 那几步(Ctrl+V / 回车)以前默认过闸门, 于是被扣下等"预览框升到位" ——
    /// 可收起那一趟框是**正在沉下去**、而且永远不再升, 那一步就再也没人补发,
    /// 表现正是"选了『复制+粘贴+回车』, 实际只做了两步"。
    /// 闸门的意思是"要往预览框里写字了", 而这几步是发给**游戏**的命令,
    /// 和窗口F 发快捷键那个 <see cref="ChordRaw"/> 是同一类 —— 一律不过闸门。
    /// </para>
    /// </summary>
    public static bool ChordSlow(ushort mod, ushort vk, int holdMs = 90, int gapMs = 60,
                                 bool gated = true)
    {
        LastSendOk = true;
        Send(gated, KeyInput(mod, 0, 0));
        Thread.Sleep(gapMs);
        Send(gated, KeyInput(vk, 0, 0));
        Thread.Sleep(holdMs);
        Send(gated, KeyInput(vk, 0, KEYEVENTF_KEYUP));
        Thread.Sleep(gapMs);
        Send(gated, KeyInput(mod, 0, KEYEVENTF_KEYUP));
        return LastSendOk;
    }

    public static void Backspace() => Key(VK_BACK);
    /// <summary>
    /// 空格: 输入法模式下必须是虚拟键 —— 拼字时空格就是"上屏第一个候选";
    /// 关掉时走 UNICODE, 否则输入法激活时虚拟键空格会被当成选词吃掉。
    /// </summary>
    public static void Space() { if (ImeMode) Key(VK_SPACE); else Unicode(' '); }
    public static void Enter() => Key(VK_RETURN);
    public static void Escape() => Key(VK_ESCAPE);

    /// <summary>
    /// 26 键全键盘用: 敲一个键, 顺便把粘住的修饰键(Ctrl / Alt / Win)一起按下去。
    /// 修饰键必须真的按下再松开 —— Ctrl+C 这种组合是"按键消息里带着 Ctrl 状态",
    /// 光发一个 C 是复制不了的。
    /// </summary>
    public static void Hit(ushort vk, bool ctrl, bool alt, bool win)
    {
        var ins = new System.Collections.Generic.List<INPUT>(8);
        if (ctrl) ins.Add(KeyInput(VK_CONTROL, 0, 0));
        if (alt) ins.Add(KeyInput(VK_MENU, 0, 0));
        if (win) ins.Add(KeyInput(VK_LWIN, 0, 0));
        ins.Add(KeyInput(vk, 0, 0));
        ins.Add(KeyInput(vk, 0, KEYEVENTF_KEYUP));
        if (win) ins.Add(KeyInput(VK_LWIN, 0, KEYEVENTF_KEYUP));
        if (alt) ins.Add(KeyInput(VK_MENU, 0, KEYEVENTF_KEYUP));
        if (ctrl) ins.Add(KeyInput(VK_CONTROL, 0, KEYEVENTF_KEYUP));
        Send(ins.ToArray());
        ClearMods();
    }

    /// <summary>
    /// 26 键的字符键。没粘修饰键时走 UNICODE 直投 —— 26 键要的就是纯字符,
    /// 不能惊动输入法(中文态下发虚拟键字母会把拼音条唤出来)。
    /// 粘了 Ctrl / Alt / Win 就改走虚拟键, 否则 Ctrl+C 这类组合发不出去。
    /// </summary>
    public static void HitChar(char c, bool ctrl, bool alt, bool win)
    {
        if (!ctrl && !alt && !win) { Unicode(c); return; }
        ushort vk = VkOf(c);
        if (vk == 0)
        {
            // 没有对应虚拟键的字符(比如组合键 + 中文标点): 把修饰键按着,
            // 中间塞一个 UNICODE 字符, 至少字能出去
            var u = new System.Collections.Generic.List<INPUT>(8);
            if (ctrl) u.Add(KeyInput(VK_CONTROL, 0, 0));
            if (alt) u.Add(KeyInput(VK_MENU, 0, 0));
            if (win) u.Add(KeyInput(VK_LWIN, 0, 0));
            u.Add(KeyInput(0, c, KEYEVENTF_UNICODE));
            u.Add(KeyInput(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            if (win) u.Add(KeyInput(VK_LWIN, 0, KEYEVENTF_KEYUP));
            if (alt) u.Add(KeyInput(VK_MENU, 0, KEYEVENTF_KEYUP));
            if (ctrl) u.Add(KeyInput(VK_CONTROL, 0, KEYEVENTF_KEYUP));
            Send(u.ToArray());
            ClearMods();
            return;
        }
        if (char.IsAsciiLetterUpper(c) || "!@#$%^&*()_+|{}:\"<>?~".Contains(c))
            HitWithShift(vk, ctrl, alt, win);   // 上档字符: 得真按一下 Shift
        else
            Hit(vk, ctrl, alt, win);
    }

    /// <summary>Shift + 键(再叠上粘住的 Ctrl / Alt / Win)。</summary>
    static void HitWithShift(ushort vk, bool ctrl, bool alt, bool win)
    {
        var ins = new System.Collections.Generic.List<INPUT>(10);
        if (ctrl) ins.Add(KeyInput(VK_CONTROL, 0, 0));
        if (alt) ins.Add(KeyInput(VK_MENU, 0, 0));
        if (win) ins.Add(KeyInput(VK_LWIN, 0, 0));
        ins.Add(KeyInput(VK_SHIFT, 0, 0));
        ins.Add(KeyInput(vk, 0, 0));
        ins.Add(KeyInput(vk, 0, KEYEVENTF_KEYUP));
        ins.Add(KeyInput(VK_SHIFT, 0, KEYEVENTF_KEYUP));
        if (win) ins.Add(KeyInput(VK_LWIN, 0, KEYEVENTF_KEYUP));
        if (alt) ins.Add(KeyInput(VK_MENU, 0, KEYEVENTF_KEYUP));
        if (ctrl) ins.Add(KeyInput(VK_CONTROL, 0, KEYEVENTF_KEYUP));
        Send(ins.ToArray());
        ClearMods();
    }

    // 26 键的"粘住"修饰键。UI 要读它们来把 Ctrl / Alt / Win 点亮,
    // 所以放在这里(和发送路径同一个地方), 免得两边状态对不上。
    public static bool ModCtrl, ModAlt, ModWin;

    public static void ClearMods() { ModCtrl = ModAlt = ModWin = false; }

    /// <summary>
    /// 方向键。拼音发出去之后系统输入法自己的候选窗就弹出来了, 而它本来就认
    /// 左右选词 / 上下翻页 —— 直接把十字键翻译成方向键, 词库、联想、翻页全是它的,
    /// 比我们再自己做一套候选选择省事得多, 也不会和它的候选状态不同步。
    /// 恒定走虚拟键: UNICODE 投递的字符输入法是收不到的。
    /// </summary>
    public static void Arrow(ushort vk) => Key(vk);

    static void Key(ushort vk) => Send(KeyInput(vk, 0, 0), KeyInput(vk, 0, KEYEVENTF_KEYUP));
}
