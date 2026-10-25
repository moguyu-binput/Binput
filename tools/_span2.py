# -*- coding: utf-8 -*-
"""2.1.7 Span 重构 第二批: MakeCellBox / 动画倍数 / 字号自动缩 / 页面定义。"""
import io

P = r"C:\Users\<你>\Binput\OverlayWindow.xaml.cs"
s = io.open(P, encoding="utf-8", newline="").read()
assert "\r\r\n" not in s and s.count("\n") == s.count("\r\n"), "源文件换行不干净"
L = s.split("\r\n")
n = 0


def find(sub, start=0):
    for i in range(start, len(L) - len(sub) + 1):
        if L[i:i + len(sub)] == sub:
            return i
    raise SystemExit("!! 找不到:\n" + "\n".join(sub))


def rep(old, new, tag):
    global n
    i = find(old)
    L[i:i + len(old)] = new
    n += 1
    print("  ok  %s (第 %d 行)" % (tag, i + 1))


# ---------- 常量: SOLO_W / SET_POP_SOLO 撤掉, 改成按格子大小算 ----------
rep(["    /// <summary>",
     "    /// 「关于」那一页: **九格并成一大格**, 这就是那一格的边长(352 = 九键那块 360",
     "    /// 的正方形四面各让出 4 的缝)。它不写死宽高, 而是让 3×3 的网格把它撑出来",
     "    /// (见 MakePanel / MakeCellBox), 于是它天然就是\"九颗键并起来\"的那么大。",
     "    /// </summary>",
     "    const double SOLO_W = SET_CARD - SET_GAP * 2;",
     "",
     "    /// <summary>那一大格里正文的字号。比格子的 16 小一号 —— 它要装十几行说明。</summary>",
     "    const double SOLO_FS = 13;",
     "",
     "    /// <summary>",
     "    /// 那一大格\"浮起来\"放大多少。**不能照格子那个 1.14 来**: 112 的格子放大到",
     "    /// 128 只是探出 16, 352 的一大格放大到 401 就顶出卡片外去了(卡片里只有 388)。",
     "    /// 1.045 那一下放大到 368, 冲顶那 85 毫秒也才 385, 刚好还在卡片里。",
     "    /// </summary>",
     "    const double SET_POP_SOLO = 1.045;",],
    ["    /// <summary>合过块的那格(长条 / 一大格)里正文的**基准**字号。",
     "    /// 比格子的 16 小一号 —— 它要装十几行说明。放不下会再自动往下缩(见 FitTitle)。</summary>",
     "    const double SOLO_FS = 13;",
     "    /// <summary>自动缩号的下限。再小就认不出来了, 宁可让末尾截断。</summary>",
     "    const double SOLO_FS_MIN = 9;",],
    "常量")

# ---------- MakeCellBox: solo -> 由格子占的矩形推 ----------
rep(["        bool solo = page.Solo;",
     "        var b = new Border",
     "        {",
     "            // **一格 = 一颗九键的键**: 边长 SET_CELL(112), 四面各留 SET_GAP(4) ——",],
    ["        // 这一格占哪块矩形(见 SetNav.TileOf), 由此推出它的像素大小。",
     "        // 「一格 = 一颗九键的键」: 1×1 就是 SET_CELL(112) 见方; 合成一块的 ——",
     "        // 比如 3×3 那块 —— 就是三格连起来那么大(352), 于是它天然就是\"几颗键并起来\"。",
     "        var t = SetNav.TileOf(page, i);",
     "        double tileW = t.Cols * (SET_CELL + SET_GAP * 2) - SET_GAP * 2;",
     "        double tileH = t.Rows * (SET_CELL + SET_GAP * 2) - SET_GAP * 2;",
     "        bool big = t.MaxSide > 1;                  // 合过块的那一格",
     "        var b = new Border",
     "        {",
     "            // **一格 = 一颗九键的键**: 边长 SET_CELL(112), 四面各留 SET_GAP(4) ——",],
    "MakeCellBox 头")

rep(["            // 【并成一格的那页不写宽高】那一格横跨三行三列, 让它被网格撑满就是",
     "            // 九键那块那么大(SOLO_W, 352)—— 写死 SET_CELL 的话它只剩 112 见方,",
     "            // 那\"九格并一格\"就白做了。",],
    ["            // 【合过块的那格不写宽高】让它被网格按自己跨的那几行几列撑出来 ——",
     "            // 写死 SET_CELL 的话它只剩 112 见方, 那\"九格并一格\"就白做了。",],
    "MakeCellBox 注释")
rep(["        if (!solo) { b.Width = SET_CELL; b.Height = SET_CELL; }"],
    ["        if (!big) { b.Width = SET_CELL; b.Height = SET_CELL; }"],
    "MakeCellBox 宽高")

rep(["            // 【并一格那页是另一套】那一格装的是十几行说明, 字号小一号(13)、",
     "            // 不加粗、靠左排 —— 加粗的 16 号字摆满一整块会糊成一片。",
     "            FontSize = solo ? SOLO_FS : 16,",
     "            FontWeight = solo ? FontWeights.Normal : FontWeights.SemiBold,",],
    ["            // 【合过块的那格是另一套】它装的是整段说明, 字号小一号(基准 13,",
     "            // 放不下会自动再缩 —— 见 FitTitle)、不加粗、靠左排 ——",
     "            // 加粗的 16 号字摆满一整块会糊成一片。",
     "            FontSize = big ? SOLO_FS : 16,",
     "            FontWeight = big ? FontWeights.Normal : FontWeights.SemiBold,",],
    "MakeCellBox 字号")
rep(["            TextAlignment = solo ? TextAlignment.Left : TextAlignment.Center,"],
    ["            TextAlignment = big ? TextAlignment.Left : TextAlignment.Center,"],
    "MakeCellBox 对齐")
rep(["            MaxWidth = solo ? SOLO_W - 30 : SET_CELL - 12,",
     "            MaxHeight = solo ? SOLO_W - 30 : 62,",],
    ["            MaxWidth = tileW - (big ? 30 : 12),",
     "            // 合过块的那格先不设 MaxHeight: 设了量出来的高度会被掐住,",
     "            // 自动缩号那一趟就永远觉得\"放得下\"(见下面 FitTitle)。缩完再补上。",
     "            MaxHeight = big ? double.PositiveInfinity : 62,",],
    "MakeCellBox 上下限")
rep(["        // **定宽**: 只给 MaxWidth 的话 TextBlock 会照内容抻到最宽、一行到头才折,",
     "        // 于是那十几行说明全成了\"一行撑到边\"。写了 Width 它才知道在哪儿断行。",
     "        if (solo) title.Width = SOLO_W - 30;",
     "        sp.Children.Add(title);",],
    ["        // **定宽**: 只给 MaxWidth 的话 TextBlock 会照内容抻到最宽、一行到头才折,",
     "        // 于是那整段说明全成了\"一行撑到边\"。写了 Width 它才知道在哪儿断行。",
     "        if (big) title.Width = tileW - 30;",
     "        sp.Children.Add(title);",
     "        if (big)",
     "        {",
     "            // **字号自动缩**: 那一大格装的是一整段说明, 里面还带着几条路径 ——",
     "            // 装在深一点的目录、或者哪天又加了一行, 写死的字号就会把最后一句切掉。",
     "            // 先按基准字号量一次真排版, 塞不下就半号半号往下试, 直到放进格子。",
     "            title.Text = cell.Title;",
     "            visFitW = tileW - 30;",
     "            visFitH = tileH - 28;",
     "            FitTitle(title, visFitW, visFitH);",
     "            title.MaxHeight = visFitH;",
     "        }",],
    "MakeCellBox 自动缩号")

# CellVis 里记下可用高宽, 文字变了(比如习惯条数)能再缩一次
rep(["    sealed class CellVis { public TextBlock Title, Note, Corner; public UIElement Badge; }"],
    ["    /// <summary>合过块的那格自动缩号用: 记下量的时候用的宽高, 文字换了能照它再缩一次。</summary>",
     "    sealed class CellVis",
     "    {",
     "        public TextBlock Title, Note, Corner;",
     "        public UIElement Badge;",
     "        public double FitW, FitH;      // 0 = 不用缩(普通一格)",
     "    }"],
    "CellVis")

# MakeCellBox 里要声明 visFitW / visFitH
rep(["        var g = new Grid();",
     "        var sp = new StackPanel { Orientation = Orientation.Vertical,"],
    ["        double visFitW = 0, visFitH = 0;",
     "        var g = new Grid();",
     "        var sp = new StackPanel { Orientation = Orientation.Vertical,"],
    "MakeCellBox 局部变量")

rep(["        b.Tag = new CellVis { Title = title, Note = note, Badge = badge, Corner = corner };"],
    ["        b.Tag = new CellVis { Title = title, Note = note, Badge = badge, Corner = corner,",
     "                             FitW = visFitW, FitH = visFitH };"],
    "CellVis 赋值")

# ---------- PaintPanel: 长条不再是"空位"了 ----------
rep(["                // 长条是\"有内容的那一格\"(里面写着这一格存的是什么), 所以照有内容",
     "                // 的格子那样填底色; 4 / 6 那种真空位才是不填的。",
     "                b.Background = cursor ? Lit",
     "                             : SetNav.IsFoot(page, i) ? Normal : Brushes.Transparent;",],
    ["                // 长条现在是**一格真格子**(带 Custom, 见窗口F 深层), 走下面那条路;",
     "                // 这儿剩下的就是 4 / 6 那种真空位, 不填底色。",
     "                b.Background = cursor ? Lit : Brushes.Transparent;",],
    "PaintPanel 长条")

rep(["            if (vis.Title != null)",
     "            {",
     "                vis.Title.Text = cell.Title;",
     "                vis.Title.Foreground = cursor ? FgHot : off ? Sub : Fg;",
     "            }",],
    ["            if (vis.Title != null)",
     "            {",
     "                // 文字变了(比如输入习惯条数、绑到数字几)就再缩一次 ——",
     "                // 长一点的串可能就得多折一行, 照旧的字号会溢出格子。",
     "                if (vis.Title.Text != cell.Title)",
     "                {",
     "                    vis.Title.Text = cell.Title;",
     "                    if (vis.FitH > 0) FitTitle(vis.Title, vis.FitW, vis.FitH);",
     "                }",
     "                vis.Title.Foreground = cursor ? FgHot : off ? Sub : Fg;",
     "            }",],
    "PaintPanel 重新缩号")

# ---------- 动画倍数: 由格子大小推 ----------
rep(["    /// <param name=\"solo\">「关于」那一页那**一大格**(见 SET_POP_SOLO): 它太大了,",
     "    /// 照格子那个 1.14 放大就顶出卡片外头去了, 所以浮起来那一下要收敛。</param>",
     "    void CellPop(Border b, bool solo = false)",
     "    {",
     "        Panel.SetZIndex(b, 10);",
     "        b.Effect = FxShadow;",
     "        var (sc, ro) = CellTf(b);",
     "        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };",
     "        double pop = solo ? SET_POP_SOLO : SET_POP;",
     "        // 那一晃同理: 112 的格子摆 4.5 度只是\"抖一下\", 352 的一大格摆 4.5 度",
     "        // 四角能甩出去十几像素, 看着像整块在转。",
     "        double wob = solo ? 1.2 : 4.5;",],
    ["    /// <summary>",
     "    /// 浮起来的**倍数由格子大小反比算出来**(见 <see cref=\"PopOf\"/>), 不写死 ——",
     "    /// 112 的格子放大 1.14 只是探出 16; 352 的一大格放大 1.14 就是 401,",
     "    /// 顶出卡片外头去了(卡片里只有 388)。按大小算, 一大格和一小格看着才是同一个动作。",
     "    /// </summary>",
     "    void CellPop(Border b, SetPage page, int slot)",
     "    {",
     "        Panel.SetZIndex(b, 10);",
     "        b.Effect = FxShadow;",
     "        var (sc, ro) = CellTf(b);",
     "        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };",
     "        double pop = PopOf(page, slot);",
     "        // 那一晃同理: 112 的格子摆 4.5 度只是\"抖一下\", 352 的一大格摆 4.5 度",
     "        // 四角能甩出去十几像素, 看着像整块在转。",
     "        double wob = WobOf(page, slot);",],
    "CellPop")

rep(["    const double SET_POP = 1.14;      // 选中的那一格放大多少(和九键焦点那颗一样)",],
    ["    const double SET_POP = 1.14;      // 选中的那一格放大多少(和九键焦点那颗一样)",
     "    /// <summary>那一晃摆多少度。同上: 按格子大小反比缩放, 一大格才不会像整块在转。</summary>",
     "    const double CELL_WOBBLE = 4.5;",
     "",
     "    /// <summary>合过块的那格最长那条边的像素长度(1 格 = SET_CELL, 3 格 = 352)。</summary>",
     "    static double TilePx(SetPage p, int slot) =>",
     "        SetNav.TileOf(p, slot).MaxSide * (SET_CELL + SET_GAP * 2) - SET_GAP * 2;",
     "",
     "    /// <summary>这一格\"浮起来\"放大多少。**按格子大小反比缩放**, 不写死两种倍数 ——",
     "    /// 以后加任何形状的合块都不用再来这儿调一次。</summary>",
     "    static double PopOf(SetPage p, int slot) => 1 + (SET_POP - 1) * SET_CELL / TilePx(p, slot);",
     "",
     "    /// <summary>那一晃摆多少度(见 CELL_WOBBLE)。</summary>",
     "    static double WobOf(SetPage p, int slot) => CELL_WOBBLE * SET_CELL / TilePx(p, slot);",
     "",
     "    /// <summary>",
     "    /// 把一段长文字**缩到放得进格子**: 按基准字号量一次真排版, 塞不下就半号半号",
     "    /// 往下试。写死字号的话, 装在深一点的目录(路径长、折的行多)或者哪天又加了",
     "    /// 一行文案, 最后一句就被切掉了 —— 那一格又不会滚动。",
     "    /// 量的是真实排版(微软雅黑、真实可用宽), 不是按字数估的。",
     "    /// </summary>",
     "    static void FitTitle(TextBlock t, double w, double h)",
     "    {",
     "        var box = new Size(Math.Max(10, w), double.PositiveInfinity);",
     "        double fs = SOLO_FS;",
     "        t.FontSize = fs;",
     "        t.InvalidateMeasure();",
     "        t.Measure(box);",
     "        while (t.DesiredSize.Height > h && fs > SOLO_FS_MIN)",
     "        {",
     "            fs -= 0.5;",
     "            t.FontSize = fs;",
     "            t.InvalidateMeasure();",
     "            t.Measure(box);",
     "        }",
     "    }",],
    "倍数 + FitTitle")

rep(["        if (hot != null) CellPop(hot, page.Solo);"],
    ["        if (hot != null) CellPop(hot, page, page.Sel);"],
    "SyncCellPop")

rep(["        // 松手弹回**浮着的那个尺寸** —— 一大格浮起来放得小(SET_POP_SOLO),",
     "        // 弹回去也得是同一个数, 不然按一下 A 它会\"变大一截\"。",
     "        double to = down ? PRESS_SCALE : (p.Solo ? SET_POP_SOLO : SET_POP);",],
    ["        // 松手弹回**浮着的那个尺寸** —— 合过块的那格浮起来放得小(见 PopOf),",
     "        // 弹回去也得是同一个数, 不然按一下 A 它会\"变大一截\"。",
     "        double to = down ? PRESS_SCALE : PopOf(p, p.Sel);",],
    "SetCellDown")

# ---------- 十字键: "就一格"不再靠 Solo 判断 ----------
rep(["        // 并成一格的那页(「关于」): 场上就那一格, 十字键也没处可去 —— 直接吃掉。",
     "        if (p.Solo) return;",],
    ["        // 场上就一格(「关于」那一大块): 十字键也没处可去 —— 直接吃掉。",
     "        if (SetNav.SlotsOf(p) <= 1) return;",],
    "MoveSel")

io.open(P, "w", encoding="utf-8", newline="").write("\r\n".join(L))
t = "\r\n".join(L)
assert "\r\r\n" not in t and t.count("\n") == t.count("\r\n"), "写坏了换行"
print("改了 %d 处; CRLF=%d CRCRLF=%d bareLF=%d"
      % (n, t.count("\r\n"), t.count("\r\r\n"), t.count("\n") - t.count("\r\n")))
