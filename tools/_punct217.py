# -*- coding: utf-8 -*-
"""2.1.7: 符号面板改成摇杆推着选 —— 5×5(正中间留空), 每类精简到 24 个。"""
import io

P = r"C:\Users\<你>\Binput\InputCore.cs"
s = io.open(P, encoding="utf-8", newline="").read()
assert "\r\r\n" not in s and s.count("\n") == s.count("\r\n"), "源换行不干净"
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


# ---------- 1. 符号表: 40 -> 24 个/类 ----------
i0 = find(["    public static readonly string[][] PunctCats ="])
i1 = i0
while L[i1].rstrip() != "    };":
    i1 += 1
NEW = r'''    public static readonly string[][] PunctCats =
    {
        // 常用: 中英混排, 打字时最高频的那批
        new[] { "，", "。", "！", "？", "、", "；", "：", "…",
                "（", "）", "《", "》", "【", "】", "“", "”",
                ",", ".", "!", "?", ";", ":", "@", "#" },

        // 中文标点
        new[] { "，", "。", "！", "？", "、", "；", "：", "…",
                "～", "—", "（", "）", "《", "》", "〈", "〉",
                "「", "」", "『", "』", "【", "】", "“", "”" },

        // 英文 / 半角符号
        new[] { ",", ".", "!", "?", ";", ":", "'", "\"",
                "(", ")", "[", "]", "{", "}", "<", ">",
                "/", "\\", "@", "#", "$", "%", "&", "*" },

        // 数学
        new[] { "＋", "－", "×", "÷", "＝", "≠", "≈", "≤",
                "≥", "＜", "＞", "±", "√", "∞", "∑", "∏",
                "∫", "∈", "⊂", "⊇", "∪", "∩", "∠", "⊥" },

        // 箭头 / 图形
        new[] { "←", "→", "↑", "↓", "↔", "↕", "↖", "↗",
                "↘", "↙", "▲", "▼", "◀", "▶", "△", "▽",
                "◁", "▷", "◆", "◇", "●", "○", "★", "☆" },

        // 数字
        new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
                "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩",
                "½", "¼", "¾", "²" },

        // 单位 / 货币: 中文打字里常要的那一撮
        new[] { "℃", "℉", "°", "′", "″", "㎡", "㎏", "㎝",
                "㎞", "㎜", "㏄", "℡", "№", "¥", "￥", "＄",
                "$", "€", "£", "¢", "Ω", "©", "®", "™" },

        // 图形 / 表情
        new[] { "☺", "☻", "♠", "♥", "♦", "♣", "♩", "♪",
                "♫", "♬", "♭", "♮", "★", "☆", "☀", "☁",
                "☂", "☃", "♨", "⚐", "⚑", "☎", "☯", "⚡" },
    };'''.split("\n")
L[i0:i1 + 1] = NEW
n += 1
print("  ok  PunctCats 精简到每类 24 个 (第 %d 行)" % (i0 + 1))

# ---------- 2. 符号网格: 5×8 -> 5×5, 正中间留空 ----------
rep(["    /// 现在行数据此固定: 拼音 3×5=15 格一页(每格约 66px 高, 放得下三行字),",
     "    /// 符号 5×8=40 格一页(正好一类 40 个, 永远不用翻页)。",
     "    /// </summary>",
     "    public int GridRowsNow => GridPinyin ? 5 : 8;"],
    ["    /// 现在行数据此固定: 拼音 3×5=15 格一页(每格约 66px 高, 放得下三行字)。",
     "    /// 【2.1.7: 符号改成 5×5】以前是 5×8=40 格(正好装一类), 可摇杆在 8 行里",
     "    /// 落点每档只有 25% 行程(一格约 1mm), 太密; 竖着压到 5 行之后横竖都是",
     "    /// 20% 一档, 格子也从 72×45 变成 72×72。代价是一类从 40 个精简到 24 个。",
     "    /// </summary>",
     "    public int GridRowsNow => 5;"],
    "GridRowsNow")

rep(["    public int GridTotal() => Punct ? PunctsNow().Length",
     "                                   : GridPinyin ? Math.Max(0, Segs.Count - GridOffset)",
     "                                   : 0;"],
    ["    public int GridTotal() => Punct ? GridPageSize",
     "                                   : GridPinyin ? Math.Max(0, Segs.Count - GridOffset)",
     "                                   : 0;"],
    "GridTotal")

rep(["    public List<string> GridItems()",
     "    {",
     "        if (Punct) return PunctsNow().ToList();"],
    ["    /// <summary>",
     "    /// 符号网格**正中间那一格**留空 —— 和九宫格标点层正中间留空一个道理:",
     "    /// 摇杆松手 / 轻轻一推就落在正中, 它要是放了东西, 每次收手都会顺手打出一个",
     "    /// 符号出来。于是 5×5 的 25 格里只有 24 个装符号(见 PunctCats)。",
     "    /// </summary>",
     "    public int GridCenter => (GridRowsNow / 2) * GridColsNow + GridColsNow / 2;",
     "",
     "    /// <summary>网格第 i 格装的是第几个符号。正中间那格返回 -1(它是空的)。</summary>",
     "    public int PunctIdxOfGrid(int i)",
     "    {",
     "        int c = GridCenter;",
     "        return i < c ? i : i > c ? i - 1 : -1;",
     "    }",
     "",
     "    public List<string> GridItems()",
     "    {",
     "        if (Punct)",
     "        {",
     "            // 一页 25 格, 正中间那格空着(见 GridCenter)——",
     "            // 于是 24 个符号落在其余 24 格上。",
     "            var src = PunctsNow();",
     "            var res = new List<string>();",
     "            for (int i = 0; i < GridPageSize; i++)",
     "            {",
     "                int k = PunctIdxOfGrid(i);",
     "                res.Add(k >= 0 && k < src.Length ? src[k] : \"\");",
     "            }",
     "            return res;",
     "        }"],
    "GridItems + 中心留空")

rep(["        var list = PunctsNow();",
     "        int i = Focus == FocusZone.CandRow ? CandIdx : GridIdx;",
     "        if (i < 0 || i >= list.Length) return;"],
    ["        var list = PunctsNow();",
     "        // 网格里正中间那格是空的(见 PunctIdxOfGrid), 落在那儿按 A 就是没按",
     "        int i = Focus == FocusZone.CandRow ? CandIdx : PunctIdxOfGrid(GridIdx);",
     "        if (i < 0 || i >= list.Length) return;"],
    "Commit")

# ---------- 3. 摇杆: 圆形 -> 方形, 落点 -> 任意网格 ----------
rep(["    public static int PointTo9(double nx, double ny)",
     "    {",
     "        int col = Math.Clamp((int)Math.Floor((nx + 1) / 2 * 3), 0, 2);",
     "        int row = Math.Clamp((int)Math.Floor((1 - ny) / 2 * 3), 0, 2);",
     "        return row * 3 + col + 1;",
     "    }"],
    ["    public static int PointTo9(double nx, double ny)",
     "    {",
     "        int col = Math.Clamp((int)Math.Floor((nx + 1) / 2 * 3), 0, 2);",
     "        int row = Math.Clamp((int)Math.Floor((1 - ny) / 2 * 3), 0, 2);",
     "        return row * 3 + col + 1;",
     "    }",
     "",
     "    /// <summary>",
     "    /// 摇杆的**圆形**范围 -> 正方形。手柄摇杆推到底是圆的(|(x,y)| &lt;= 1),",
     "    /// 45 度推满每个轴只到 0.707 —— 摆 5×5 要够到角上那格得两个轴同时超过 0.6,",
     "    /// 那块地方只有普通格的七分之一, 表现就是\"明明推到角上了, 光标却进不去\"。",
     "    /// 按方向等比撑到方阵边界之后, 45 度推满就是 (1,1), 四角随推随到。",
     "    /// (九键那 3×3 的阈值只有 1/3, 斜推本来就够得着, 所以 PointTo9 不动它 ——",
     "    /// 那儿改了会把手感一起改掉。)",
     "    /// </summary>",
     "    public static void Square(double nx, double ny, out double sx, out double sy)",
     "    {",
     "        double r = Math.Sqrt(nx * nx + ny * ny);",
     "        double m = Math.Max(Math.Abs(nx), Math.Abs(ny));",
     "        if (r < 1e-9 || m < 1e-9) { sx = 0; sy = 0; return; }",
     "        double k = r / m;                       // 圆 -> 方的拉伸比(1 .. 1.414)",
     "        sx = Math.Clamp(nx * k, -1, 1);",
     "        sy = Math.Clamp(ny * k, -1, 1);",
     "    }",
     "",
     "    /// <summary>",
     "    /// 摇杆点位 -> cols×rows 网格里的第几格(0 起, 左上起、行优先)。",
     "    /// 就是 PointTo9 把 3 换成参数, 另外先走一遍方形映射(见 <see cref=\"Square\"/>) ——",
     "    /// 于是 5×5 / 5×8 这种密网格也推得到每一个格子, 四角不必\"斜着使劲\"。",
     "    /// </summary>",
     "    public static int PointToGrid(double nx, double ny, int cols, int rows)",
     "    {",
     "        Square(nx, ny, out var sx, out var sy);",
     "        int col = Math.Clamp((int)Math.Floor((sx + 1) / 2 * cols), 0, cols - 1);",
     "        int row = Math.Clamp((int)Math.Floor((1 - sy) / 2 * rows), 0, rows - 1);",
     "        return row * cols + col;",
     "    }"],
    "Square + PointToGrid")

rep(["        else",
     "        {",
     "            if (active) TryRepeat(DirOf(nx, ny), now, MoveCand);",
     "            else _repeatDir = DIR_NONE;",
     "        }",
     "    }"],
    ["        else if (Punct && Focus == FocusZone.CandGrid)",
     "        {",
     "            // 符号网格: 摇杆**直接落点** —— 推到哪一格就是哪一格(见 PointToGrid)。",
     "            // 以前在这儿摇杆是\"当方向键连发\", 一格一格挪过去; 二十几个符号",
     "            // 那么挪太慢, 而且它跟九键上\"推哪儿选哪儿\"是两套手感。",
     "            // 【落在正中间那格就当没推】那一格是空的(见 GridCenter), 站上去",
     "            // 什么也不会发生, 不如干脆别过去。",
     "            if (active)",
     "            {",
     "                int k = PointToGrid(nx, ny, GridColsNow, GridRowsNow);",
     "                if (k >= 0 && k < GridCount() && PunctIdxOfGrid(k) >= 0 && k != GridIdx)",
     "                {",
     "                    GridIdx = k;",
     "                    Dirty = true;",
     "                }",
     "            }",
     "        }",
     "        else",
     "        {",
     "            if (active) TryRepeat(DirOf(nx, ny), now, MoveCand);",
     "            else _repeatDir = DIR_NONE;",
     "        }",
     "    }"],
    "HandleStick 符号落点")

# ---------- 4. 状态提示: 十字键 -> 摇杆 ----------
rep(["        if (Focus == FocusZone.CandGrid && GridPinyin)",
     "        {",
     "            string pg = GridPageCount > 1 ? $\" (第 {GridPage + 1}/{GridPageCount} 页)\" : \"\";",
     "            return $\"更多拼音 {GridTotal()} 种{pg} · 十字键选 · ↑↓ 翻页 · RB 发出 · Y 切头 · B 返回 · {tail}\";",
     "        }"],
    ["        if (Focus == FocusZone.CandGrid && GridPinyin)",
     "        {",
     "            string pg = GridPageCount > 1 ? $\" (第 {GridPage + 1}/{GridPageCount} 页)\" : \"\";",
     "            return $\"更多拼音 {GridTotal()} 种{pg} · 十字键选 · ↑↓ 翻页 · RB 发出 · Y 切头 · B 返回 · {tail}\";",
     "        }",
     "",
     "        // 符号面板: 摇杆直接落点(不再一格一格挪), LB / RB 换分类",
     "        if (Punct && Focus == FocusZone.CandGrid)",
     "            return $\"全部符号 · {PunctCatName} {PunctsNow().Length} 个 · 摇杆选 · \"",
     "                 + $\"LB/RB 换类 · A 上屏 · B 返回 · {tail}\";"],
    "状态提示")

io.open(P, "w", encoding="utf-8", newline="").write("\r\n".join(L))
t = "\r\n".join(L)
assert "\r\r\n" not in t and t.count("\n") == t.count("\r\n"), "写坏了换行"
print("改了 %d 处; CRLF=%d CRCRLF=%d bareLF=%d"
      % (n, t.count("\r\n"), t.count("\r\r\n"), t.count("\n") - t.count("\r\n")))
