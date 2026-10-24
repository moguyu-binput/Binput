# -*- coding: utf-8 -*-
"""2.1.7: fprobe 跟着 Span 重构改 —— 断言的行为一条不变, 只换构造方式;
再加一组锁住"合块 = 写一行声明"。"""
import io

P = r"C:\Users\<你>\Binput\tools\fprobe\Program.cs"
s = io.open(P, encoding="utf-8", newline="").read()
# fprobe/Program.cs 是 LF 结尾(不是所有工具文件都是 CRLF), 按 LF 走
assert "\r" not in s, "源文件混进了 CR"
L = s.split("\n")
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


# ---------- 长条那一组: Foot -> Layout + 第七格 ----------
rep(["        // + 底下那条长条(Foot 非 null)",
     "        SetPage Deep()",
     "        {",
     "            var cells = new List<SetCell>",
     "            {",
     "                SetNav.Cell(\"写入短语\"), SetNav.Cell(\"写入快捷键\"), SetNav.Cell(\"删除\"),",
     "                null, SetNav.Cell(\"5\"), null,",
     "            };",
     "            return new SetPage { Nine = true, Rows = 2, Cells = cells, Foot = () => new object() };",
     "        }",
     "        var p = Deep();",
     "        int foot = SetNav.BodyOf(p);                 // 长条那一个位置 = 6"],
    ["        // 前六格照三列顺序排, 第七格**横跨整行**(底下那条长条)。",
     "        // 就这一行 Layout 声明 —— 没有第二处要改。",
     "        SetPage Deep()",
     "        {",
     "            var cells = new List<SetCell>",
     "            {",
     "                SetNav.Cell(\"写入短语\"), SetNav.Cell(\"写入快捷键\"), SetNav.Cell(\"删除\"),",
     "                null, SetNav.Cell(\"5\"), null,",
     "                SetNav.Block(() => new object()),       // 7 号位: 那条长条",
     "            };",
     "            return new SetPage",
     "            {",
     "                Nine = true, Rows = 2, Cells = cells,",
     "                Layout = () => new List<Span>",
     "                {",
     "                    new Span(0, 0), new Span(0, 1), new Span(0, 2),",
     "                    new Span(1, 0), new Span(1, 1), new Span(1, 2),",
     "                    new Span(2, 0, 1, 3),                  // 7 8 9 合成的那条",
     "                },",
     "            };",
     "        }",
     "        var p = Deep();",
     "        int foot = SetNav.SlotsOf(p) - 1;            // 长条那一个位置 = 6"],
    "Deep()")

rep(["        Check(\"长条那一格能停(能高亮)\", SetNav.Landable(p, foot), \"\");",
     "        Check(\"长条没有格子 -> 按 A 没反应\", SetNav.CellAt(p, foot) == null, \"\");"],
    ["        Check(\"长条那一格能停(能高亮)\", SetNav.Landable(p, foot), \"\");",
     "        var footCell = SetNav.CellAt(p, foot);",
     "        Check(\"长条是一格真格子(摆的是自己造的控件)\", footCell?.Custom != null, \"\");",
     "        Check(\"长条只给人看 -> 按 A 没反应\",",
     "              footCell?.Act == null && footCell?.Child == null, \"\");"],
    "长条按 A")

# ---------- 第 29 组: Solo -> Layout ----------
rep(["    // 摇杆推哪儿、十字键往哪儿挪, 光标都还在那一格上(见 SetNav 里 Solo 那几处)。",
     "    {",
     "        var p = new SetPage { Solo = true, Nine = true,",
     "                              Cells = new List<SetCell> { SetNav.Cell(\"柄输 Binput...\") } };"],
    ["    // 摇杆推哪儿、十字键往哪儿挪, 光标都还在那一格上。",
     "    // 2.1.7: 那一大格不再靠 Solo 开关, 而是**写一行声明** —— 占满整个 3×3。",
     "    {",
     "        var p = new SetPage { Nine = true,",
     "                              Cells = new List<SetCell> { SetNav.Cell(\"柄输 Binput...\") },",
     "                              Layout = () => new List<Span> { new Span(0, 0, 3, 3) } };"],
    "29 组构造")

rep(["        Check(\"摆在第 0 行第 0 列\", SetNav.RowOf(p, 0) == 0 && SetNav.ColOf(p, 0) == 0,",
     "              $\"{SetNav.RowOf(p, 0)},{SetNav.ColOf(p, 0)}\");"],
    ["        Check(\"摆在第 0 行第 0 列(横跨三列, 导航上算中间那列)\",",
     "              SetNav.RowOf(p, 0) == 0 && SetNav.ColOf(p, 0) == 1,",
     "              $\"{SetNav.RowOf(p, 0)},{SetNav.ColOf(p, 0)}\");",
     "        Check(\"那一格占 3×3\", SetNav.TileOf(p, 0).MaxSide == 3, \"\");"],
    "29 组行列")

rep(["        // RowsOf 只报 1 行 —— 那个 3×3 的网格是 MakePanel 专门为这一页铺的",
     "        // (大格靠 RowSpan / ColumnSpan 撑满), 不是靠行数堆出来的。",
     "        Check(\"RowsOf 只报一行(3×3 由 MakePanel 单独铺)\", SetNav.RowsOf(p) == 1,",
     "              $\"rows={SetNav.RowsOf(p)}\");",
     "        Check(\"没有长条那回事\", !SetNav.HasFoot(p) && !SetNav.IsFoot(p, 0), \"\");"],
    ["        // 网格照样铺三行 —— 那一格横跨三行三列把它撑满, 于是它正好是九键那块 360×360。",
     "        // 只铺一行的话它就被压成一条扁条。",
     "        Check(\"RowsOf 报三行(3×3 由那一格横跨撑出来)\", SetNav.RowsOf(p) == 3,",
     "              $\"rows={SetNav.RowsOf(p)}\");",
     "        Check(\"没有长条那回事(摆法表里就一项)\", SetNav.SlotsOf(p) == 1, \"\");"],
    "29 组行数")

# ---------- 新增第 30 组: 合块 = 写一行声明 ----------
rep(["        p.Sel = 0;",
     "        SetNav.Clamp(p);",
     "        Check(\"Clamp 不会把光标拽出这一页\", p.Sel == 0, $\"Sel={p.Sel}\");",
     "    }",
     "}"],
    ["        p.Sel = 0;",
     "        SetNav.Clamp(p);",
     "        Check(\"Clamp 不会把光标拽出这一页\", p.Sel == 0, $\"Sel={p.Sel}\");",
     "    }",
     "",
     "    // ==================================================================",
     "    Console.WriteLine(\"\\n=== 30) 合块只写一行声明: 1×2 横块 / 3×1 竖块 ===\");",
     "    // 2.1.7: \"把几格合成一块\"现在只有一种说法 —— 每格声明它占哪块矩形(见 Span)。",
     "    // 以前长条和一大格是两套各写各的特判, 下面这两种形状**根本做不出来**。",
     "    // 这一组锁的是\"写完那一行声明就有这套导航\", 不用连手柄翻界面去试。",
     "    {",
     "        var pH = new SetPage",
     "        {",
     "            Nine = true, Rows = 2,",
     "            Cells = new List<SetCell>",
     "            { SetNav.Cell(\"上半条\"), SetNav.Cell(\"左下\"), SetNav.Cell(\"右下\") },",
     "            Layout = () => new List<Span>",
     "            { new Span(0, 0, 1, 2), new Span(1, 0), new Span(1, 1) },",
     "        };",
     "        Check(\"横块那一格横跨两列\", SetNav.TileOf(pH, 0).Cols == 2, \"\");",
     "        Check(\"横块盖住第 0 行第 1 列\", SetNav.SlotAt(pH, 0, 1) == 0,",
     "              $\"实际={SetNav.SlotAt(pH, 0, 1)}\");",
     "        Check(\"摇杆推正上 / 左上 -> 都是横块\",",
     "              SetNav.StickSlot(pH, 1) == 0 && SetNav.StickSlot(pH, 0) == 0,",
     "              $\"{SetNav.StickSlot(pH, 0)},{SetNav.StickSlot(pH, 1)}\");",
     "        Check(\"摇杆推左下 -> 第 1 号位\", SetNav.StickSlot(pH, 3) == 1,",
     "              $\"实际={SetNav.StickSlot(pH, 3)}\");",
     "        Check(\"这一层没第三行 -> 往下推没处去\", SetNav.StickSlot(pH, 7) == -1,",
     "              $\"实际={SetNav.StickSlot(pH, 7)}\");",
     "        Check(\"横块只铺两行\", SetNav.RowsOf(pH) == 2, $\"rows={SetNav.RowsOf(pH)}\");",
     "",
     "        var pV = new SetPage",
     "        {",
     "            Nine = true, Rows = 3,",
     "            Cells = new List<SetCell>",
     "            { SetNav.Cell(\"左竖条\"), SetNav.Cell(\"右上\"), SetNav.Cell(\"右中\") },",
     "            Layout = () => new List<Span>",
     "            { new Span(0, 0, 3, 1), new Span(0, 1), new Span(1, 1) },",
     "        };",
     "        Check(\"竖块那一格纵跨三行\", SetNav.TileOf(pV, 0).Rows == 3, \"\");",
     "        Check(\"竖块盖住第 2 行第 0 列\", SetNav.SlotAt(pV, 2, 0) == 0,",
     "              $\"实际={SetNav.SlotAt(pV, 2, 0)}\");",
     "        Check(\"摇杆推左下 -> 还是竖块\", SetNav.StickSlot(pV, 6) == 0,",
     "              $\"实际={SetNav.StickSlot(pV, 6)}\");",
     "        Check(\"摇杆推正上(第 0 行第 1 列) -> 第 1 号位\", SetNav.StickSlot(pV, 1) == 1,",
     "              $\"实际={SetNav.StickSlot(pV, 1)}\");",
     "    }",
     "}"],
    "新增 30 组")

io.open(P, "w", encoding="utf-8", newline="").write("\n".join(L))
t = "\n".join(L)
assert "\r" not in t, "写坏了换行"
print("改了 %d 处; LF=%d  CR=%d" % (n, t.count("\n"), t.count("\r")))
