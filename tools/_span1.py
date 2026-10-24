# -*- coding: utf-8 -*-
"""2.1.7 Span 重构: 改 OverlayWindow.xaml.cs 的渲染 / 动画 / 页面定义。
按行操作, 全程 CRLF, 每一处替换都断言命中一次。"""
import io, sys

P = r"C:\Users\<你>\Binput\OverlayWindow.xaml.cs"
s = io.open(P, encoding="utf-8", newline="").read()
assert "\r\r\n" not in s and s.count("\n") == s.count("\r\n"), "源文件换行不干净"
L = s.split("\r\n")
n = 0


def find(sub, start=0):
    """sub 是若干整行(不含换行), 返回起始下标"""
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


# ---------- 1. 「关于」那一页: Solo -> Layout ----------
rep(["        Title = \"关于\",",
     "        Solo = true,"],
    ["        Title = \"关于\",",
     "        // **九格并成一大格**: 就这一行声明。",
     "        // 摆在哪儿、摇杆落在哪儿、浮起来放大多少、字号多大, ",
     "        // 全由 SetNav 按这个矩形推出来(见 SetNav.TileOf)。",
     "        Layout = () => new List<Span> { new Span(0, 0, 3, 3) },"],
    "PageAbout")

# ---------- 2. FillPanel: 摆的位置一律来自摆法表 ----------
rep(["            // 长条那一号位置: 摆的就是 page.Foot 造出来的那条(见 QuickBar)。",
     "            // 它和别的格子一样进 boxes —— 于是也能被光标选中、也能浮起来弹一下,",
     "            // 只是 CellAt 拿不到格子(Cells 里没有第 7 个), 按 A 自然什么也不干。",
     "            Border b;",
     "            if (SetNav.IsFoot(page, i))",
     "            {",
     "                b = page.Foot?.Invoke() as Border;",
     "                if (b == null) continue;",
     "            }",
     "            else b = MakeCellBox(h, page, CellAt(page, i), i);",
     "            if (grid is Grid g)",
     "            {",
     "                // 长条从第 0 列起横跨三列(三条并一条, 正好占满那一行 360 宽)。",
     "                // 导航上它算\"落在中间那一列\"(见 SetNav.ColOf), 摆的时候得从最左起。",
     "                if (SetNav.IsFoot(page, i)) { Grid.SetColumn(b, 0); Grid.SetColumnSpan(b, 3); }",
     "                else Grid.SetColumn(b, SetNav.ColOf(page, i));",
     "                Grid.SetRow(b, SetNav.RowOf(page, i));",
     "                // 并成一格的那页: 这一格吃满整个 3×3(见 MakePanel 里那三行的注释)",
     "                if (page.Solo) { Grid.SetColumnSpan(b, 3); Grid.SetRowSpan(b, 3); }",
     "            }",],
    ["            // 摆的是自己造的控件的那一格(见 SetCell.Custom, 比如窗口F 底下那条长条)。",
     "            // 它和别的格子一样进 boxes —— 于是也能被光标选中、也能浮起来弹一下,",
     "            // 只是没有 Act / Child, 按 A 自然什么也不干。",
     "            var c0 = CellAt(page, i);",
     "            Border b = c0?.Custom?.Invoke() as Border;",
     "            b ??= MakeCellBox(h, page, c0, i);",
     "            if (grid is Grid g)",
     "            {",
     "                // **摆在哪个矩形里, 摆法表说了算**(见 SetNav.TileOf)。",
     "                // 普通一格就是 (i/3, i%3) 占 1×1; 合成一块的那些跨几行几列都写在表里,",
     "                // 这里一句都不用特判 —— 长条、一大格、以后随便什么形状都是这四行。",
     "                var t = SetNav.TileOf(page, i);",
     "                Grid.SetRow(b, t.Row);          Grid.SetColumn(b, t.Col);",
     "                Grid.SetRowSpan(b, t.Rows);     Grid.SetColumnSpan(b, t.Cols);",
     "            }",],
    "FillPanel")

# ---------- 3. MakePanel: 行数由摆法表推 ----------
rep(["        // 三列 + page.Rows 行(默认 3): 不满九个的层(比如\"JKL 位置\"只有两格)也占满",
     "        // 三行, 卡片不会一会儿高一会儿矮。窗口F 那一层是两行 —— 第三行让给了",
     "        // \"7 8 9 合成的那条长条\"(见 page.Foot)。",
     "        // 行宽 / 列宽就写\"一格 + 左右缝隙\", 于是三格一排正好 360, 和九键那块一样大。",],
    ["        // 三列 + N 行: 不满九个的层(比如\"JKL 位置\"只有两格)也占满三行,",
     "        // 卡片不会一会儿高一会儿矮。行数由摆法表推(见下面 SetNav.RowsOf)。",
     "        // 行宽 / 列宽就写\"一格 + 左右缝隙\", 于是三格一排正好 360, 和九键那块一样大。",],
    "MakePanel 注释上")
rep(["        // 行数 = 正文几行 + 底下那条长条(有才算)。长条**坐在格子里**, 不是挂在",
     "        // 格子下面另起一段 —— 它就是\"7 8 9 三格合并成的那一格\", 于是它和上面",
     "        // 六格共用同一套坐标(第 3 行、横跨三列), 光标挪下去也是一个格子。",
     "        // 【并成一格的那页照样铺满 3×3】它摆的是\"九格合成的那一个大格\", 网格得",
     "        // 是三行三列, 那一格再横跨三行三列 —— 于是它正好是九键那块 360×360。",
     "        // 按 RowsOf 走的话那一页只有一行(120 高), 大格就被压成一条扁条。",
     "        for (int i = 0; i < (page.Solo ? 3 : SetNav.RowsOf(page)); i++)",],
    ["        // 行数由摆法表里最深的那一行决定(见 SetNav.RowsOf)。长条**坐在格子里**,",
     "        // 不是挂在格子下面另起一段 —— 它就是\"7 8 9 三格合并成的那一格\", 于是它和",
     "        // 上面六格共用同一套坐标(第 3 行、横跨三列), 光标挪下去也是一个格子。",
     "        // 同理, 铺满整个 3×3 的那一大格也让网格铺三行 —— 于是它正好是九键那块",
     "        // 360×360; 只铺一行的话它就被压成一条扁条。",
     "        for (int i = 0; i < SetNav.RowsOf(page); i++)",],
    "MakePanel 行数")

io.open(P, "w", encoding="utf-8", newline="").write("\r\n".join(L))
print("改了 %d 处" % n)
t = "\r\n".join(L)
assert "\r\r\n" not in t and t.count("\n") == t.count("\r\n"), "写坏了换行"
print("CRLF=%d CRCRLF=%d bareLF=%d" % (t.count("\r\n"), t.count("\r\r\n"), t.count("\n") - t.count("\r\n")))
