# -*- coding: utf-8 -*-
"""2.1.7: coretest 跟着符号面板改 —— 5×5 中心留空 + 摇杆直接落点。"""
import io

P = r"C:\Users\<你>\Binput\tools\coretest\Program.cs"
s = io.open(P, encoding="utf-8", newline="").read()
assert "\r" not in s, "coretest 是 LF 文件, 却混进了 CR"
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


# ---------- 摇杆辅助: 能推到任意坐标(九宫格之外还要测 5×5) ----------
rep(["    static void StickCenter() { _lx = 0; _ly = 0; }"],
    ["    static void StickCenter() { _lx = 0; _ly = 0; }",
     "",
     "    /// <summary>左摇杆推到**任意坐标**(-1..1, y 向上为正)。",
     "    /// 九宫格那 9 个方向用 StickTo 就够, 符号网格是 5×5, 得能推到任意一点。</summary>",
     "    static void StickXY(double nx, double ny)",
     "    {",
     "        _lx = (short)(nx * 32767); _ly = (short)(ny * 32767);",
     "    }"],
    "StickXY")

# ---------- Case14: 网格容量 40 -> 24, 中心留空 ----------
rep(["    /// 符号面板: 常用 / 中文 / 英文 / 数学 / 箭头 / 数字 / 单位 / 图形八类, LB / RB 换类。",
     "    /// 每一类都必须装得进 5×8 的网格(40 格), 而且不能有重复项(重复项等于浪费格子)。",
     "    /// </summary>"],
    ["    /// 符号面板: 常用 / 中文 / 英文 / 数学 / 箭头 / 数字 / 单位 / 图形八类, LB / RB 换类。",
     "    /// 【2.1.7】网格从 5×8 改成 **5×5 且正中间留空**(那一格是摇杆松手的落点,",
     "    /// 放了东西每次收手都会顺手打出一个符号), 于是一类只有 24 个位置。",
     "    /// 每一类必须正好 24 个, 而且不能有重复项(重复项等于浪费格子)。",
     "    /// </summary>"],
    "Case14 注释")

rep(["            bool ok = n > 0 && n <= 40 && cat.Distinct().Count() == n;"],
    ["            bool ok = n == 24 && cat.Distinct().Count() == n;"],
    "Case14 容量")

rep(["        var c = New();",
     "        OpenPunct(c);",
     "        Check(\"默认落在第一类\", c.PunctCatName, \"常用\");",
     "        CheckTrue(\"网格里装的是第一类\", c.GridItems().SequenceEqual(InputCore.PunctCats[0]));",
     "",
     "        Send(c);",
     "        Check(\"RB 换到下一类\", c.PunctCatName, \"中文\");",
     "        CheckTrue(\"网格跟着换\", c.GridItems().SequenceEqual(InputCore.PunctCats[1]));",
     "        CheckTrue(\"换类后光标回到头一个\", c.GridIdx == 0);"],
    ["        var c = New();",
     "        OpenPunct(c);",
     "        Check(\"默认落在第一类\", c.PunctCatName, \"常用\");",
     "",
     "        // 25 格, 正中间那格空着 —— 24 个符号落在其余 24 格上",
     "        var g0 = c.GridItems();",
     "        Check(\"网格一页 25 格\", g0.Count == 25, $\"实际={g0.Count}\");",
     "        Check(\"正中间是第 12 格(5×5 的正中)\", c.GridCenter == 12, $\"实际={c.GridCenter}\");",
     "        Check(\"正中间那格是空的\", g0[c.GridCenter].Length == 0, $\"实际='{g0[c.GridCenter]}'\");",
     "        var want = new List<string>();",
     "        for (int i = 0; i < 25; i++)",
     "        {",
     "            int k = c.PunctIdxOfGrid(i);",
     "            want.Add(k >= 0 && k < InputCore.PunctCats[0].Length ? InputCore.PunctCats[0][k] : \"\");",
     "        }",
     "        CheckTrue(\"网格里装的是第一类(中心留空)\", g0.SequenceEqual(want));",
     "        Check(\"24 个符号一个没落\", g0.Count(x => x.Length > 0).ToString(), \"24\");",
     "",
     "        Send(c);",
     "        Check(\"RB 换到下一类\", c.PunctCatName, \"中文\");",
     "        CheckTrue(\"网格跟着换\", c.GridItems()[0] == InputCore.PunctCats[1][0]);",
     "        CheckTrue(\"换类后光标回到头一个\", c.GridIdx == 0);",
     "",
     "        // ---- 摇杆**直接落点**(2.1.7): 推到哪一格就是哪一格 ----",
     "        CheckTrue(\"焦点在网格上\", c.Focus.ToString() == \"CandGrid\", c.Focus.ToString());",
     "",
     "        StickXY(-0.9, 0.9); Frame(c, 60);",
     "        Check(\"推左上角 -> 第 0 格\", c.GridIdx == 0, $\"实际={c.GridIdx}\");",
     "",
     "        // 斜推 45 度: 摇杆推到底是圆的, 每轴只有 0.5 —— 不撑成方形的话",
     "        // 算出来是第 3 格(进不了角上那格)。撑成方形之后是 0.707 -> 真的到角上。",
     "        StickXY(0.5, 0.5); Frame(c, 60);",
     "        Check(\"斜推右上角 -> 真的落在角上那格(第 4 格)\", c.GridIdx == 4, $\"实际={c.GridIdx}\");",
     "",
     "        // 正中间那格是空的: 推过去**不该**把光标挪上去(站上去按 A 什么也没有)",
     "        StickXY(0.05, 0.05); Frame(c, 60);",
     "        Check(\"推正中间(空的那一格) -> 光标不挪过去\", c.GridIdx == 4, $\"实际={c.GridIdx}\");",
     "",
     "        StickXY(0.5, -0.5); Frame(c, 60);",
     "        Check(\"斜推右下角 -> 最后一格(第 24 格)\", c.GridIdx == 24, $\"实际={c.GridIdx}\");",
     "",
     "        StickCenter(); Frame(c, 60);",
     "        Check(\"松手光标**留在原地**(不回中, 否则按 A 打出来的是别的符号)\",",
     "              c.GridIdx == 24, $\"实际={c.GridIdx}\");",
     "",
     "        TextOutput.Reset();",
     "        Tap(c, GamepadInput.A, 60);",
     "        Check(\"按 A 上屏的就是最后那一格的符号\",",
     "              TextOutput.LastText == InputCore.PunctCats[1][23],",
     "              $\"实际='{TextOutput.LastText}'\");"],
    "Case14 网格 + 摇杆落点")

io.open(P, "w", encoding="utf-8", newline="").write("\n".join(L))
t = "\n".join(L)
assert "\r" not in t
print("改了 %d 处; LF=%d CR=%d" % (n, t.count("\n"), t.count("\r")))
