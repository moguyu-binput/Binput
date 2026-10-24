namespace Binput;

// ================= 设置页: 一层里"哪几格、摆在哪个位置、光标停哪儿" =================
//
// 这一整个文件是**纯算法**, 不碰 WPF: 给一层(SetPage)和它的格子, 回答
//   · 这一层摆几格 / 有几页 / 第几页第 i 格是哪一格
//   · 第 i 格**摆在哪个位置**(占第几行第几列、横跨几行几列)
//   · 光标能不能停在这一格
//   · 进这一层 / 退回这一层时, 光标默认停哪儿(回中)
// 放在单独一个文件里是为了能被无头测试台(tools/fprobe)直接编进去 ——
// 这几条规矩改坏了, 探针当场就能抓到, 不用连手柄翻半天界面才发现。
//
// ---------------------------------------------------------------------------
// 【2.1.7: 合块只写一行声明】
// 以前"把几格合成一块"是**两把各写各的锤子**:
//   · 窗口F 底下那条长条 = page.Foot 挂一个现造控件 + 下面六个函数里各自特判;
//   · 「关于」那一大格 = page.Solo 一个开关 + 又是另外五处 p.Solo ? … : …。
// 两套不能组合 —— 做不出"上面一条长条 + 下面六格", 也做不出"左边一整列"。
//
// 现在统一成一句话: **每格声明它占哪块矩形**(见 Span), 其余全部推导。
//   new Span(2, 0, 1, 3)   // 7 8 9 三格合成的那条长条
//   new Span(0, 0, 3, 3)   // 九格并成一大格(「关于」那一页)
//   不给就是 new Span(i / 3, i % 3) —— 一格一个位置, 三列顺序排。
// 于是"位置在哪儿 / 摇杆落在哪格 / 回中回哪儿 / 摆的时候跨几行几列"全都由这张表算,
// 加一种新摆法 = 写一行 Span, 不用再动这文件。
// ---------------------------------------------------------------------------

/// <summary>
/// 一格占的位置: 从第几行第几列起、横跨几行几列。
/// **这就是"合块"的全部表达方式** —— 想要什么形状, 写一行这个。
/// </summary>
public readonly struct Span
{
    public readonly int Row, Col, Rows, Cols;

    public Span(int row, int col, int rows = 1, int cols = 1)
    {
        Row = row; Col = col;
        Rows = Math.Max(1, rows); Cols = Math.Max(1, cols);
    }

    public int RowEnd => Row + Rows;
    public int ColEnd => Col + Cols;

    /// <summary>这一块盖住 (row, col) 这一点吗。</summary>
    public bool Has(int row, int col) =>
        row >= Row && row < RowEnd && col >= Col && col < ColEnd;

    /// <summary>
    /// 导航上算它落在**哪一列**。横跨三列的长条, 推左下 / 正下 / 右下都算它 ——
    /// 取的是它横跨的那几列的正中间那列。只占一列的格子就是它自己那一列。
    /// </summary>
    public int NavCol => Col + Cols / 2;

    /// <summary>导航上算它落在**哪一行**(同理, 取横跨那几行的中间)。</summary>
    public int NavRow => Row + Rows / 2;

    /// <summary>占了几格(1 = 普通一格)。渲染那头拿它决定字号和"浮起来"放大多少。</summary>
    public int Area => Rows * Cols;

    /// <summary>最长那条边占了几格。决定"浮起来放大多少"用的是这个, 不是面积 ——
    /// 一条 1×3 的长条和一块 3×3 的大格, 放大倍数该一样(都是三格宽)。</summary>
    public int MaxSide => Math.Max(Rows, Cols);
}

/// <summary>设置页上的一格。</summary>
public sealed class SetCell
{
    public string Title = "";                  // 主标题(三列布局下最多四个字)
    public string Note = "";                   // 副标题(小字, 可以没有)
    /// <summary>
    /// 左上角那个小角标(窗口F 的列表层用: 写上 1~9, 表示"这一格是哪个数字键")。
    /// 设置页那些格子不写, 就是没有。
    /// </summary>
    public Func<string> Corner;
    public Action Act;                         // 按 A 就干这件事
    public Func<SetPage> Child;                // 还有下一层: 按 A 浮出它
    /// <summary>
    /// 这一格摆的是**自己造的控件**(见窗口F 底下那条长条), 不是标准的格子。
    /// 给了的话, 搭面板时摆它造出来的那个 Border; 没给就按标准格子画。
    /// 它在"选格子"这件事上跟别的格子没区别 —— 照样能停、能亮、能浮起来,
    /// 只是没有标题副标题那套(按 A 也没反应, 除非另外给了 Act)。
    /// </summary>
    public Func<object> Custom;
    public Func<string> Tip;                   // 光标停一会儿弹出来的说明(fprobe 之外已停用)
    public Func<bool> IsOn = () => false;      // 这一格是不是"现在生效的那个"
    public Func<bool> IsOff = () => false;     // 置灰(这一档现在用不上)
}

/// <summary>
/// 设置页的一层。格子和摆法都是**每次打开 / 每次刷新现问一遍**的 ——
/// 门槛那几档、当前唤醒键这种会变的东西, 不能建页面时算一次就定死。
/// </summary>
public sealed class SetPage
{
    public string Title = "";
    public Func<List<SetCell>> Build = () => new List<SetCell>();
    public bool Nine = true;                   // true = 固定 3×3 翻页; false = 首页, 三列往下排
    /// <summary>
    /// 摆几行。**恒为 3** —— 只有窗口F 的深层是 2 行(第三行让给"7 8 9 合成的长条")。
    /// </summary>
    public int Rows = 3;
    /// <summary>
    /// **怎么摆**: 第 i 项就是第 i 号位置占的那一块(见 <see cref="Span"/>)。
    /// 不给就按三列顺序排, 一格一个位置 —— 绝大多数层都是这个。
    /// 想合块就在这里写:
    /// <code>
    ///   Layout = () => new List&lt;Span&gt; { new(0, 0), new(0, 1), …, new(2, 0, 1, 3) }
    /// </code>
    /// **只有一格的那页**(「关于」)就写一项 <c>new Span(0, 0, 3, 3)</c> —— 于是它
    /// 铺满整个 3×3, 光标恒在它上面、摇杆推哪儿都是它。
    /// </summary>
    public Func<List<Span>> Layout;
    public List<SetCell> Cells = new();
    public int Page;                           // 第几页(九个一页)
    public int Sel;                            // 这一页里选中哪一格(0..8)

    /// <summary>算好的摆法(见 <see cref="SetNav.TilesOf"/>)。格子表一变就得作废。</summary>
    internal List<Span> Tiles;

    /// <summary>格子表重搭了: 摆法也跟着重算。</summary>
    public void Invalidate() => Tiles = null;
}

public static class SetNav
{
    /// <summary>没给 Layout 时, 正文(上面那几行九宫格)一共有几个位置。</summary>
    static int BodyOf(SetPage p) => p.Nine ? Math.Max(1, p.Rows) * 3
                                           : ((p.Cells.Count + 2) / 3) * 3;

    /// <summary>正文有几行。</summary>
    static int BodyRows(SetPage p) => Math.Max(1, BodyOf(p) / 3);

    /// <summary>
    /// 这一层的**摆法表**: 第 i 项就是第 i 号位置占的那一块矩形。
    /// 没给 Layout 时按三列顺序排; 给了就以它为准(长度就决定了这一层有几个位置)。
    /// 这是整个文件里**唯一**决定"格子在哪儿"的地方, 下面全是从它推出来的。
    /// </summary>
    public static List<Span> TilesOf(SetPage p)
    {
        if (p.Tiles != null) return p.Tiles;
        var ov = p.Layout?.Invoke();
        int n = ov != null && ov.Count > 0 ? ov.Count : BodyOf(p);
        var list = new List<Span>(n);
        for (int i = 0; i < n; i++)
            list.Add(ov != null && i < ov.Count ? ov[i] : new Span(i / 3, i % 3));
        p.Tiles = list;
        return list;
    }

    /// <summary>第 slot 号位置占的那一块。越界就当普通一格(调用方都先看过 SlotsOf)。</summary>
    public static Span TileOf(SetPage p, int slot)
    {
        var t = TilesOf(p);
        return slot >= 0 && slot < t.Count ? t[slot] : new Span(slot / 3, slot % 3);
    }

    /// <summary>这一层有几个位置(一格 = 一个位置, 合起来的几格也算一个)。</summary>
    public static int SlotsOf(SetPage p) => TilesOf(p).Count;

    /// <summary>这一层一共几行(摆法表里最深的那一行决定)。</summary>
    public static int RowsOf(SetPage p)
    {
        int rows = 1;
        foreach (var t in TilesOf(p)) rows = Math.Max(rows, t.RowEnd);
        return rows;
    }

    /// <summary>第 slot 号位置在第几行。</summary>
    public static int RowOf(SetPage p, int slot) => TileOf(p, slot).Row;

    /// <summary>第 slot 号位置在第几列(横跨几列时取正中间那列)。</summary>
    public static int ColOf(SetPage p, int slot) => TileOf(p, slot).NavCol;

    /// <summary>
    /// 第 row 行第 col 列是第几号位置。**横跨多列 / 多行的那一块, 它盖住的每一格
    /// 都归它** —— 于是摇杆推向左下 / 正下 / 右下选中的都是同一条长条。
    /// 那一处没有格子就返回 -1(比如只有两行的层, 往下推是没处的)。
    /// </summary>
    public static int SlotAt(SetPage p, int row, int col)
    {
        var t = TilesOf(p);
        for (int i = 0; i < t.Count; i++)
            if (t[i].Has(row, col)) return i;
        return -1;
    }

    /// <summary>
    /// 摇杆推到第 n 格(0..8, 从 InputCore.PointTo9 减一来的)是这一层的第几号位置。
    /// 这一层压根没有那一格就返回 -1(比如只有三个格子那层, 往下推是没处的)。
    /// </summary>
    public static int StickSlot(SetPage p, int n)
    {
        if (n < 0 || n > 8) return -1;
        int i = SlotAt(p, n / 3, n % 3);
        return i >= 0 && i < SlotsOf(p) ? i : -1;
    }

    public static int PagesOf(SetPage p)
    {
        int per = SlotsOf(p);
        return per <= 0 ? 1 : Math.Max(1, (p.Cells.Count + per - 1) / per);
    }

    public static int BaseOf(SetPage p) => p.Page * SlotsOf(p);

    /// <summary>本页第 i 格对应的是哪一个格子(越界或空位返回 null)。</summary>
    public static SetCell CellAt(SetPage p, int i)
    {
        int k = BaseOf(p) + i;
        return k >= 0 && k < p.Cells.Count ? p.Cells[k] : null;
    }

    /// <summary>
    /// 光标能不能停在这一格。**空位也算能停** —— 不满九个的时候, 中间那格常常是空的,
    /// 而摇杆松手回中落的就是正中那格; 空位不能停的话"回中"就没了着落,
    /// 手一松光标还赖在原来那格上。空位只是"按 A 没反应", 位置和照样高亮跟
    /// 有内容的一模一样(见 PaintPanel 里 cell == null 那一路)。
    /// 真正不能停的只有"这一档现在用不上"那种被置灰的格子(IsOff)。
    /// </summary>
    public static bool Landable(SetPage p, int i)
    {
        if (i < 0 || i >= SlotsOf(p)) return false;
        var c = CellAt(p, i);
        return c == null || !c.IsOff();
    }

    public static int FirstLandable(SetPage p)
    {
        int slots = SlotsOf(p);
        for (int i = 0; i < slots; i++) if (Landable(p, i)) return i;
        return 0;
    }

    /// <summary>
    /// **回中**: 光标默认停在哪儿 —— 正中间那一格(手松开摇杆时落的那格)。
    /// 进设置、进下一层、退上一层都回到这儿: 手柄上没有鼠标指针,
    /// 光标停在最左上角的话，眼睛得先从角落找回才开始。
    /// 中间那格是空位也算(空位本来就能停能亮), 实在停不了才退到第一格。
    /// </summary>
    public static int HomeSlot(SetPage p)
    {
        int slots = SlotsOf(p);
        // 就一格(比如「关于」那一整块): 正中就是它, 也挪不动。
        if (slots <= 1) return 0;
        // 正中 = **中间那一行**的**中间那一列**, 不是"第 slots/2 格" ——
        // 首页那种"三列往下排"的层, 格数不一定是九: 六格时 slots/2 是第二行
        // 第一列(左下角), 按行列算才是第一行中间。
        //
        // 【行数要把长条那一行也算进去】窗口F 深层是"两行 + 底下一条",
        // 长条占的正是第三行 —— 于是整块面板的**正中是第二行中间那一格**
        // (也就是 5 号位"绑在数字几")。不算长条那一行的话, 两行的"中间行"被算成
        // 第一行, 正中就落到 2 号位「写入快捷键」上 —— 摇杆一松手光标跳到顶上去了。
        int rows = RowsOf(p);
        int midRow = (rows - 1) / 2, midCol = 1;
        int mid = SlotAt(p, midRow, midCol);
        if (mid < 0) mid = slots - 1;
        // 正中那格**能停**就停正中: 上面那格有东西自然最好, 是空位也照样停 ——
        // 空位只是"按 A 没反应", 位置和高亮跟有内容的一模一样(见 Landable)。
        // **这里以前多要了一个 CellAt != null**: 于是只有两格、四格这种"正中
        // 正好是空位"的层, 第一步判不中, 落到下面"找离正中最近的有内容的格"那一步,
        // 而那种层里所有格到正中的距离都一样, 于是永远挑到最靠前的那一格 ——
        // 表现就是从深层退回这一层时, 光标"回中"回成了左上角。
        if (Landable(p, mid)) return mid;
        // 正中停不了(那一格被置灰了): 挑离正中最近、能停的那一格
        // (按"棋盘距离"算, 一样近取靠前的)。
        // 距离按**摆法表里的行列**算, 不能拿 i/3、i%3 —— 合成一块的那些格
        // 根本不在"第 i 行第 i 列"上。
        int best = -1, bestD = int.MaxValue, bestM = int.MaxValue;
        for (int pass = 0; pass < 2 && best < 0; pass++)
        {
            for (int i = 0; i < slots; i++)
            {
                if (!Landable(p, i)) continue;
                if (pass == 0 && CellAt(p, i) == null) continue;
                var t = TileOf(p, i);
                int dr = Math.Abs(t.NavRow - midRow), dc = Math.Abs(t.NavCol - midCol);
                int d = Math.Max(dr, dc);        // 棋盘距离: 隔几圈
                int m = dr + dc;                 // 走几步: 一样近的时候靠它挑
                // 同在一圈上时, 斜角那四格(左上/右上/左下/右下)和正上/正下/正左/正右
                // 的"棋盘距离"是一样的; 再看"走几步"就能挑到正上这一格 ——
                // 只按棋盘距离挑的话会挑到最靠前的左上角, 那也是"回中"最不想看到的角。
                if (d < bestD || (d == bestD && m < bestM)) { bestD = d; bestM = m; best = i; }
            }
        }
        return best >= 0 ? best : FirstLandable(p);
    }

    /// <summary>页码 / 光标别跑到范围外(格子数会变: 版本切成直出版之后就少了好几项)。</summary>
    public static void Clamp(SetPage p)
    {
        int pages = PagesOf(p);
        if (p.Page >= pages) p.Page = pages - 1;
        if (p.Page < 0) p.Page = 0;
        if (!Landable(p, p.Sel)) p.Sel = FirstLandable(p);
    }

    /// <summary>造一格(给页面定义用, 少写点样板)。</summary>
    public static SetCell Cell(string title, string note = "", Action act = null,
                               Func<SetPage> child = null, Func<string> tip = null,
                               Func<bool> on = null, Func<bool> off = null,
                               Func<string> corner = null, Func<object> custom = null) =>
        new SetCell
        {
            Title = title, Note = note, Corner = corner, Act = act, Child = child, Tip = tip,
            Custom = custom,
            IsOn = on ?? (() => false), IsOff = off ?? (() => false),
        };

    /// <summary>造一个"摆自己造的控件"的格子(见 <see cref="SetCell.Custom"/>)。</summary>
    public static SetCell Block(Func<object> custom) => Cell("", custom: custom);
}
