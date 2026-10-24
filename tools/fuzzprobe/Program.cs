using Binput;
using System.IO;

// fuzzprobe —— 拿随机按键序列去撞输入状态机, 看有没有能把它撞坏的组合。
//
// 为什么需要它: 前面几个探针(gridprobe / mineprobe / segprobe)都是**摆好一个
// 特定场景再验一条断言**, 只能守住"已知的那条规则"。真正烦人的 bug 往往出在
// 没人摆过的状态组合上 —— 网格开着按 Y、候选行翻到头再按 A、符号面板和拼音
// 网格来回切…… 手点不出来, 就让机器随机撞。
//
// 它验的是**不变量**(任何时刻都必须成立的事), 不是"某个输入该出某个结果":
//   1. 光标指向的那条永远在候选范围内(不得越界)
//   2. 拼音行和展开的网格不许装同一条(1.0.8 那个"显示两份"就是破了这条)
//   3. 网格开着时光标不許落在拼音行末条上(1.0.8 那个"按 Y 切错"就是破了这条)
//   4. 拼了一半的数字串/拼音串里不许出现键位以外的字符
//   5. 任何一步都不抛异常

int fail = 0;
int dumped = 0;
void Bad(string msg) { if (fail++ < 20) Console.WriteLine("  !! " + msg); }

// 一个键都不真往外发(闸门在 TextOutput.SendOne)
InputCore.NoSend = true;

int loaded = T9Engine.LoadExternalDict(Path.Combine(AppContext.BaseDirectory, "dict.txt"));
Console.WriteLine($"词库 {loaded} 条 / 共 {T9Engine.PhraseCount} 条");
if (loaded < 40000) { Console.WriteLine("!! 词库没加载上"); fail++; }

// ---- 随机按键序列 ----
var rng = new Random(20261005);

// 会用到的按钮。刻意不碰 L3 / R3: 那两个是"显隐界面 / 翻设置页 / 调界面",
// 属于窗口那一层, 跟这里要验的输入状态机无关(而且 L3+R3 会把 Visible 翻掉)。
ushort[] BTNS = {
    GamepadInput.A, GamepadInput.B, GamepadInput.X, GamepadInput.Y,
    GamepadInput.LB, GamepadInput.RB,
    GamepadInput.DPAD_UP, GamepadInput.DPAD_DOWN,
    GamepadInput.DPAD_LEFT, GamepadInput.DPAD_RIGHT,
};

const int ROUNDS = 4000;      // 重开多少局
const int STEPS = 30;         // 每局最多按多少下

var t = new DateTime(2026, 1, 1);
int acts = 0;
string last = "开局";      // 最近一次动作, 出错时打印出来好定位

for (int round = 0; round < ROUNDS; round++)
{
    var c = new InputCore { Layout = Layout.CN9, Focus = FocusZone.Keyboard, Visible = true };
    c.SetPreview(true);
    c.PinyinFit = rng.Next(1, 5);          // 拼音行宽度随机: 1 条时网格最容易出事

    // 先随便打几个数字, 有时空着就按(空状态下按 A/B/Y 也不能崩)
    int nDigits = rng.Next(0, 7);
    for (int i = 0; i < nDigits; i++)
    {
        last = "打字";
        c.Digits += (char)('2' + rng.Next(0, 8));
        c.DebugRefreshCands();
    }

    for (int step = 0; step < STEPS; step++)
    {
        int roll = rng.Next(0, 100);
        if (roll < 22)
        {
            // 继续打字。**只能在没有网格的时候打** —— 真实操作里摇杆只挪光标,
            // 按 A 才落数字, 而网格开着时 A 是"上屏这一条", 根本轮不到打字。
            // 不加这个前提就会摆出真实到不了的状态, 报出来的"bug"是假的。
            if (!c.GridPinyin && !c.Punct)
            {
                last = "打字";
                c.Digits += (char)('2' + rng.Next(0, 8));
                c.DebugRefreshCands();
            }
        }
        else if (roll < 28)
        {
            // 打开 / 关闭符号面板。**必须走真实入口**: 自己拿字段摆 Punct = true
            // 会摆出真实按键到不了的状态(符号面板和拼音网格同时开着),
            // 那种状态报出来的"bug"是假的, 反而会把真 bug 埋掉。
            last = c.Punct ? "关符号面板(B)" : "开符号面板";
            if (c.Punct) c.ClearInput();      // 面板里按 B 就是这个
            else c.DebugOpenPunct();
        }
        else if (roll < 34)
        {
            // 直接展开 / 收起全部拼音网格(不走"右越界"那条路)
            // 直接展开 / 收起全部拼音网格(不走"右越界 / 上翻"那条路)。
            // 前提和真实入口一致: 没开符号面板、候选多到一行放不下。
            last = c.GridPinyin ? "收起网格" : "展开网格";
            if (c.GridPinyin) c.DebugCloseGrid();
            else if (!c.Punct && c.Segs.Count > c.FitNow) c.DebugOpenGrid();
        }
        else
        {
            // 按一下键: 按下 -> 松开, 中间隔一帧(状态机靠 _prevBtn 认边沿)
            ushort b = BTNS[rng.Next(BTNS.Length)];
            last = "按 " + NameOf(b);
            t = t.AddMilliseconds(30);
            c.Poll(new PadState { Connected = true, Buttons = b }, t);
            acts++;
            Check(c, "按下 " + b.ToString("X4"));
            t = t.AddMilliseconds(30);
            c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
            Check(c, "松开 " + b.ToString("X4"));
        }

        Check(c, "第 " + round + " 局第 " + step + " 步");
    }

    // 收起: 走真实的 Flush 路径
    c.Flush();
    Check(c, "收起后");
}

void Dump(InputCore c, string where)
{
    if (dumped++ >= 6) return;
    Console.WriteLine($"       └ [{where}] 焦点={c.Focus} 布局={c.Layout} 数字=\"{c.Digits}\" "
        + $"拼音串=\"{c.SentPinyin}\" 候选={c.Segs.Count} 条 fit={c.FitNow} "
        + $"网格={(c.GridPinyin ? "拼音" : c.Punct ? "符号" : "无")} "
        + $"GridCount={c.GridCount()} GridIdx={c.GridIdx} GridOffset={c.GridOffset} "
        + $"PinyinIdx={c.PinyinIdx} 上一步={last}");
}

void Check(InputCore c, string where)
{
    int n = c.Segs.Count;

    // 1. 候选条数有上限。这个数跟着 InputCore.SegMax 走(现在是 28) ——
    //    长串要靠它把"第一个字"的各种可能都摆出来, 太小了就又回到"翻不到"的老毛病,
    //    太大了翻不完。改了 SegMax 记得同步改这里。
    if (n > 28) Bad($"{where}: 候选 {n} 条, 超过 28");

    // 2. 光标那条必须在范围内
    if (n > 0)
    {
        int sel = c.SegIdxNow;
        if (sel < 0 || sel >= n) Bad($"{where}: SegIdxNow={sel} 越界(共 {n} 条)");
    }

    // 3. 拼音行光标不出这一行
    if (c.PinyinIdx < 0 || (n > 0 && c.PinyinIdx >= c.FitNow))
        Bad($"{where}: PinyinIdx={c.PinyinIdx} 越界(fit={c.FitNow}, {n} 条)");

    // 4. 网格光标不出网格
    int gn = c.GridCount();
    if (gn > 0 && (c.GridIdx < 0 || c.GridIdx >= gn))
    {
        Bad($"{where}: GridIdx={c.GridIdx} 越界(网格 {gn} 格)");
        Dump(c, where);
    }

    // 5. 网格和拼音行不许装同一条 —— 1.0.8 的"显示两份"就是破了这条
    if (c.GridPinyin && gn > 0)
    {
        var row = new HashSet<string>(c.PinyinRowItems());
        foreach (var g in c.GridItems())
            if (row.Contains(g)) Bad($"{where}: \"{g}\" 行和网格里都有");

        // 6. 网格开着 = 光标在网格里, 那它指的必须是网格那条, 不能是行内末条
        if (c.Focus == FocusZone.CandGrid)
        {
            int sel = c.SegIdxNow;
            int off = c.GridOffset;
            if (sel < off) Bad($"{where}: 网格开着却选到了行内第 {sel} 条(offset={off})");
        }
    }

    // 7. 数字串 / 拼音串里不许有键位以外的东西
    foreach (char ch in c.Digits)
        if (ch < '2' || ch > '9') Bad($"{where}: 数字串里有 '{ch}'");
    foreach (char ch in c.SentPinyin)
        if (!char.IsAsciiLetter(ch) && ch != '\'') Bad($"{where}: 拼音串里有 '{ch}'");

    // 8. 焦点互斥
    if (c.Punct && c.GridPinyin)
    {
        Bad($"{where}: 符号面板和拼音网格同时开着");
        Dump(c, where);
    }
}

static string NameOf(ushort b) => b switch
{
    GamepadInput.A => "A", GamepadInput.B => "B", GamepadInput.X => "X", GamepadInput.Y => "Y",
    GamepadInput.LB => "LB", GamepadInput.RB => "RB",
    GamepadInput.DPAD_UP => "上", GamepadInput.DPAD_DOWN => "下",
    GamepadInput.DPAD_LEFT => "左", GamepadInput.DPAD_RIGHT => "右",
    _ => b.ToString("X4"),
};

Console.WriteLine($"跑了 {ROUNDS} 局 / 按键 {acts} 次");
Console.WriteLine(fail == 0 ? "\n全部通过" : $"\n有 {fail} 处不对");
return fail == 0 ? 0 : 1;
