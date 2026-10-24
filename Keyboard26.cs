namespace Binput;

/// <summary>
/// 26 键全键盘上一个键的种类。
/// 以前 26 键只有三排字母 + 一排功能键(⇧ ␣ #), 打不了数字、F1~F12、
/// Tab / 退格 / 方向键, 更打不出 @ # 这类"数字键上档"的符号。
/// 现在按笔记本键盘排布, 键的种类也就多起来了 —— 每种的按法都不一样,
/// 所以干脆在键上标清楚它是什么, 发送时按种类分流。
/// </summary>
public enum KKind
{
    /// <summary>出一个字符: 字母 / 数字 / 符号。Shift 时出上档那个(如 2 → @)。</summary>
    Char,
    /// <summary>功能键: Esc / F1~F12 / Tab / 回车 / 退格 / 方向键。发虚拟键。</summary>
    Vk,
    /// <summary>大小写锁。按一下切换, 只影响字母。</summary>
    Caps,
    /// <summary>
    /// Shift。两条路都往同一个状态上并:
    /// **按住手柄 LB** 就是按住 Shift(松手就掉), **界面上按一下 ⇧** 是粘住
    /// (再按一下松开)。所以按住 LB 打数字键就是 @ # $ 那一档符号。
    /// </summary>
    Shift,
    /// <summary>Ctrl / Alt / Win: 按一下"粘住", 打下一个键时一起发出去(如 Ctrl + C)。</summary>
    Mod,
    /// <summary>空格。</summary>
    Space,
    /// <summary>打开符号面板(右摇杆"更多"那个面板的入口)。</summary>
    Punct,
}

/// <summary>
/// 26 键全键盘上的一个键。
/// W 是宽度倍数(1 = 标准键), 用它是因为真键盘各行并不等宽 ——
/// Tab / Caps / Shift / 回车 / 空格都比普通键宽, 照抄过来才像一块键盘。
/// </summary>
public readonly struct KKey
{
    public readonly string Label;
    /// <summary>按住 Shift 时显示的字样。为空表示 Shift 不影响这个键的显示。</summary>
    public readonly string ShiftLabel;
    public readonly char Ch, ShiftCh;
    /// <summary>Vk 键的虚拟键码; Mod 键借这一格存自己是 Ctrl / Alt / Win。</summary>
    public readonly ushort Vk;
    public readonly KKind Kind;
    public readonly double W;

    KKey(string label, string shiftLabel, char ch, char shiftCh, ushort vk, KKind kind, double w)
    {
        Label = label; ShiftLabel = shiftLabel; Ch = ch; ShiftCh = shiftCh;
        Vk = vk; Kind = kind; W = w;
    }

    // ---- 造键的几种写法 ----
    /// <summary>字符键。大写/上档那个是按住 Shift 时出的。</summary>
    public static KKey Chr(char ch, char shiftCh, double w = 1) =>
        new(ch.ToString(), shiftCh.ToString(), ch, shiftCh, 0, KKind.Char, w);

    /// <summary>功能键(发虚拟键)。</summary>
    public static KKey Fn(string label, ushort vk, double w = 1) =>
        new(label, "", '\0', '\0', vk, KKind.Vk, w);

    /// <summary>开关 / 修饰 / 空格 / 符号面板这类"不出字"的键。</summary>
    public static KKey Cmd(string label, KKind kind, ushort vk = 0, double w = 1) =>
        new(label, "", '\0', '\0', vk, kind, w);

    /// <summary>界面上显示的字样: 按住 Shift 时显示上档(数字键就变成 @ # $)。</summary>
    public string Show(bool shift) =>
        Kind == KKind.Char && shift && ShiftLabel.Length > 0 ? ShiftLabel : Label;

    /// <summary>这一刻按下它要打出去的字符(已把 Shift / Caps 算进去)。</summary>
    public char CharNow(bool shift, bool caps)
    {
        char c = shift && ShiftCh != '\0' ? ShiftCh : Ch;
        // Caps 只管字母, 而且和 Shift 是"反过来"的关系 —— 跟真键盘一致:
        // Caps 亮着时打字母是大写, 这时再按住 Shift 反而是小写。
        if (char.IsAsciiLetter(c))
        {
            bool upper = caps ^ shift;
            c = upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
        }
        return c;
    }
}

/// <summary>
/// 26 键 = 一块笔记本键盘(约 82 键, 不含小键盘)。
/// 排布照抄 84 键笔记本: Esc + F1~F12、数字行、三排字母、最底下一排修饰键,
/// 方向键放在右下角。没有 108 键那套(小键盘、编辑区、六键区)——
/// 手柄上就一个十字键和一根摇杆, 键越多找起来越慢, 84 键刚好够用又不至于迷路。
/// </summary>
public static class Keyboard26
{
    // F 行: Esc + F1~F12
    static readonly KKey[] RowF =
    {
        KKey.Fn("Esc", 0x1B),
        KKey.Fn("F1", 0x70), KKey.Fn("F2", 0x71), KKey.Fn("F3", 0x72),
        KKey.Fn("F4", 0x73), KKey.Fn("F5", 0x74), KKey.Fn("F6", 0x75),
        KKey.Fn("F7", 0x76), KKey.Fn("F8", 0x77), KKey.Fn("F9", 0x78),
        KKey.Fn("F10", 0x79), KKey.Fn("F11", 0x7A), KKey.Fn("F12", 0x7B),
    };

    // 数字行: ` 1~0 - = 退格
    static readonly KKey[] RowNum =
    {
        KKey.Chr('`', '~'),
        KKey.Chr('1', '!'), KKey.Chr('2', '@'), KKey.Chr('3', '#'),
        KKey.Chr('4', '$'), KKey.Chr('5', '%'), KKey.Chr('6', '^'),
        KKey.Chr('7', '&'), KKey.Chr('8', '*'), KKey.Chr('9', '('),
        KKey.Chr('0', ')'),
        KKey.Chr('-', '_'), KKey.Chr('=', '+'),
        KKey.Fn("Back", 0x08, 2),
    };

    // 第一排字母: Tab Q~P [ ] \
    static readonly KKey[] RowTop =
    {
        KKey.Fn("Tab", 0x09, 1.5),
        KKey.Chr('q', 'Q'), KKey.Chr('w', 'W'), KKey.Chr('e', 'E'),
        KKey.Chr('r', 'R'), KKey.Chr('t', 'T'), KKey.Chr('y', 'Y'),
        KKey.Chr('u', 'U'), KKey.Chr('i', 'I'), KKey.Chr('o', 'O'),
        KKey.Chr('p', 'P'),
        KKey.Chr('[', '{'), KKey.Chr(']', '}'), KKey.Chr('\\', '|', 1.5),
    };

    // 第二排字母: Caps A~L ; ' 回车
    static readonly KKey[] RowHome =
    {
        KKey.Cmd("Caps", KKind.Caps, 0, 1.75),
        KKey.Chr('a', 'A'), KKey.Chr('s', 'S'), KKey.Chr('d', 'D'),
        KKey.Chr('f', 'F'), KKey.Chr('g', 'G'), KKey.Chr('h', 'H'),
        KKey.Chr('j', 'J'), KKey.Chr('k', 'K'), KKey.Chr('l', 'L'),
        KKey.Chr(';', ':'), KKey.Chr('\'', '"'),
        KKey.Fn("Enter", 0x0D, 2.25),
    };

    // 第三排字母: Shift Z~M , . / Shift
    static readonly KKey[] RowBottom =
    {
        KKey.Cmd("Shift", KKind.Shift, 0, 2.25),
        KKey.Chr('z', 'Z'), KKey.Chr('x', 'X'), KKey.Chr('c', 'C'),
        KKey.Chr('v', 'V'), KKey.Chr('b', 'B'), KKey.Chr('n', 'N'),
        KKey.Chr('m', 'M'),
        KKey.Chr(',', '<'), KKey.Chr('.', '>'), KKey.Chr('/', '?'),
        KKey.Cmd("Shift", KKind.Shift, 0, 2.75),
    };

    // 最底下一排: Ctrl Win Alt 空格 Alt 符号 方向键
    static readonly KKey[] RowMod =
    {
        KKey.Cmd("Ctrl", KKind.Mod, TextOutput.VK_CONTROL, 1.25),
        KKey.Cmd("Win",  KKind.Mod, TextOutput.VK_LWIN, 1.25),
        KKey.Cmd("Alt",  KKind.Mod, TextOutput.VK_MENU, 1.25),
        KKey.Cmd("Space", KKind.Space, 0, 5),
        KKey.Cmd("Alt",  KKind.Mod, TextOutput.VK_MENU, 1.25),
        KKey.Cmd("符号", KKind.Punct, 0, 1.25),
        KKey.Fn("▲", 0x26),
        KKey.Fn("◀", 0x25), KKey.Fn("▼", 0x28), KKey.Fn("▶", 0x27),
    };

    // 编辑区那一撮(笔记本上一般在右上角 / 方向键上面): 打印屏、插入、删除、行首行尾、翻页
    static readonly KKey[] RowEdit =
    {
        KKey.Fn("PrtSc", 0x2C, 1.25),
        KKey.Fn("Ins", 0x2D, 1.25), KKey.Fn("Del", 0x2E, 1.25),
        KKey.Fn("Home", 0x24, 1.25), KKey.Fn("End", 0x23, 1.25),
        KKey.Fn("PgUp", 0x21, 1.25), KKey.Fn("PgDn", 0x22, 1.25),
    };

    public static readonly KKey[][] Rows =
        { RowF, RowNum, RowTop, RowHome, RowBottom, RowEdit, RowMod };

    /// <summary>默认停在第二排字母的 F 上(食指起始位), 跟盲打一样。</summary>
    public const int HomeRow = 3;

    public static int RowCount => Rows.Length;
    public static int Len(int row) => Rows[Math.Clamp(row, 0, Rows.Length - 1)].Length;
    public static KKey At(int row, int col) =>
        Rows[Math.Clamp(row, 0, Rows.Length - 1)][Math.Clamp(col, 0, Len(row) - 1)];

    /// <summary>这一行一共多少个"标准键宽", 界面拿它对每行做居中。</summary>
    public static double RowUnits(int row)
    {
        double s = 0;
        foreach (var k in Rows[Math.Clamp(row, 0, Rows.Length - 1)]) s += k.W;
        return s;
    }

    /// <summary>最宽那一行有多少个单位 —— 界面按它算键盘区的宽度。</summary>
    public static double MaxUnits
    {
        get
        {
            double m = 0;
            for (int r = 0; r < Rows.Length; r++) m = Math.Max(m, RowUnits(r));
            return m;
        }
    }
}
