using Binput;

// gridprobe —— 验 1.0.8 的"拼音网格不重复 + 只高亮光标选中的"这两条。
//
// 用法: dotnet run --project gridprobe -- <数字串>...
// 直接调 InputCore 的公开面, 不起窗口: 拼数字 -> 模拟界面量出来的 FitNow
// -> 打开网格 -> 检查网格里的条目和拼音行有没有交集, 以及光标指向的是哪一条。

// 界面量宽��要一个 WPF 窗口, 这里绕开它: 手动指定"这一行能摆几个",
// 顺便也跑一遍各种 fit 值, 确认不管行内摆几条, 网格都不会和行里重复。
foreach (var digits in args.Length > 0 ? args : new[] { "7443", "9426", "7468326", "943943" })
{
    var c = new InputCore { Layout = Layout.CN9, Focus = FocusZone.Keyboard, Visible = true };
    foreach (var ch in digits) { c.Digits += ch.ToString(); c.DebugRefreshCands(); }

    Console.WriteLine($"=== {digits} ===");
    Console.WriteLine($"全部切分 {c.Segs.Count} 条:");
    for (int i = 0; i < c.Segs.Count; i++)
        Console.WriteLine($"   [{i,2}] {string.Join("'", c.Segs[i])}");

    foreach (int fit in new[] { 1, 2, 3, 4, c.Segs.Count })
    {
        if (fit > c.Segs.Count) continue;
        c.PinyinFit = fit;
        c.DebugOpenGrid();

        var row = c.PinyinRowItems();
        var grid = c.GridItems();
        int off = c.GridOffset;
        int cnt = c.GridCount();

        // 断言 1: 网格和拼音行没有交集
        var dup = row.Where(r => grid.Contains(r)).ToList();
        // 断言 2: 网格 + 行 = 全部, 一条不多一条不少
        bool cover = row.Count + grid.Count == c.Segs.Count;
        // 断言 3: 网格第 i 格映射回 Segs 的下标, 文本必须对得上
        bool map = true;
        for (int i = 0; i < grid.Count; i++)
            if (string.Join("'", c.Segs[c.SegIdxOfGrid(i)]) != grid[i]) map = false;
        // 断言 4: 光标停在第一格时, 高亮的确实是"行外第一条"
        bool cursor = c.GridIdx == 0 && off > 0 && off < c.Segs.Count
                      && string.Join("'", c.Segs[c.SegIdxOfGrid(c.GridIdx)]) == grid[0];

        // 断言 5: 光标挪到网格第 k 格时, "当前选中那条"(SegIdxNow)必须就是第 k 格。
        // 1.0.8 里 RB 发出走的是网格下标, 而 Y 切分还在读 PinyinIdx —— 那指的是
        // **行内最后一条**, 于是"在全部拼音里选中第 5 种, 按 Y"切出来的却是
        // 拼音行末尾那条的头一个音节。这条断言就是守这个的(发拼音/切分/上屏
        // 现在统一走 SegIdxNow, 一处改就到处改)。
        bool sel = true;
        for (int k = 0; k < grid.Count; k++)
        {
            c.GridIdx = k;
            if (string.Join("'", c.Segs[c.SegIdxNow]) != grid[k]) sel = false;
        }
        // 顺带确认选中那条**不是**行内最后一条 —— 是的话就等于还是旧行为
        bool notRowLast = grid.Count == 0 || c.SegIdxNow != row.Count - 1;
        c.GridIdx = 0;

        Console.WriteLine($"  fit={fit,-2} 行内 {row.Count} 条 | 网格 {cnt} 条 (offset={off}) "
            + $"| 行∩网格={dup.Count} 覆盖完整={cover} 下标映射={map} 光标指向行外第一条={cursor}");
        Console.WriteLine($"        选中项跟着网格光标走={sel} 且不是行内末条={notRowLast}");
        Console.WriteLine($"        行: {string.Join("  ", row)}");
        Console.WriteLine($"        格: {string.Join("  ", grid)}");

        c.DebugCloseGrid();
    }
    Console.WriteLine();
}

// ---- 十字键"展开全部切分"是哪个方向 ----
//
// 1.1.0 从"↑"改成"↓": 拼音行下面就是九宫格, 顺着"往下看更多"更顺手;
// 上在拼音行里没地方去, 空着。
// 走**真实入口**(Poll 发十字键), 不拿字段摆状态 —— 摆出来的是到不了的状态,
// 报出来的"bug"是假的(1.0.9 那次就吃过这个亏)。
Console.WriteLine("=== 展开全部切分: 该是 ↓, 不该是 ↑ ===");
{
    bool ok = true;
    foreach (var digits in new[] { "7443", "9426", "7468326", "943943" })
    {
        var c = new InputCore { Layout = Layout.CN9, Focus = FocusZone.Keyboard, Visible = true };
        foreach (var ch in digits) { c.Digits += ch.ToString(); c.DebugRefreshCands(); }
        c.PinyinFit = 2;                 // 行内只摆 2 条, 剩下的才需要展开
        var t = DateTime.Now;

        // ↑ 按下 -> 松开(状态机靠 _prevBtn 认边沿, 中间隔一帧)
        t = t.AddMilliseconds(40);
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.DPAD_UP }, t);
        t = t.AddMilliseconds(40);
        c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
        bool upOpens = c.GridPinyin;

        // ↓ 按下 -> 松开
        t = t.AddMilliseconds(40);
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.DPAD_DOWN }, t);
        t = t.AddMilliseconds(40);
        c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
        bool downOpens = c.GridPinyin;

        bool good = !upOpens && downOpens;
        if (!good) ok = false;
        Console.WriteLine($"  {(good ? "OK  " : "FAIL")} {digits,-8} "
            + $"↑展开={upOpens}(该 False) ↓展开={downOpens}(该 True)");
    }
    Console.WriteLine(ok ? "\n  展开方向 = ↓，通过" : "\n  !! 展开方向不对");
    if (!ok) Environment.Exit(1);
}
