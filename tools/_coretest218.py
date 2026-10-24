# -*- coding: utf-8 -*-
"""2.1.8: coretest 第 14 组跟着"正中间那格"的新规矩改。

2.1.7 那一格是**空的**(怕松手顺手打出符号); 2.1.8 它成了**标签格**:
装的是分类名, 是一颗真格子, 光标默认就停在它上面(进面板 / 换类 / 松手回中)。
摇杆**推着的时候够不到它**(圆撑成方之后至少有一轴出了中间那列), 所以只能靠
"松手回中"到 —— 这条也一起锁住。

tools/coretest/Program.cs 是 **LF**, 别按 CRLF 写。
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
P = r"C:\Users\<你>\Binput\tools\coretest\Program.cs"

L = lambda ls: "\n".join(ls)

EDITS = [
    # 1) 那段说明
    (L([
        '    /// 【2.1.7】网格从 5×8 改成 **5×5 且正中间留空**(那一格是摇杆松手的落点,',
        '    /// 放了东西每次收手都会顺手打出一个符号), 于是一类只有 24 个位置。',
        '    /// 每一类必须正好 24 个, 而且不能有重复项(重复项等于浪费格子)。']),
     L([
        '    /// 【2.1.7】网格从 5×8 改成 **5×5**, 于是一类只有 24 个位置。',
        '    /// 【2.1.8】正中间那一格不再是空的, 改成**标签格**: 装的是当前分类名,',
        '    /// 是一颗真格子(会画、会高亮), 光标**默认就停在它上面** —— 进面板、换类、',
        '    /// 松手回中三处都落在它上。它装的不是符号, 所以按 A 什么也不打; 而摇杆',
        '    /// 推着的时候也够不到它(圆撑成方之后至少一轴出了中间那列), 只由回中到。',
        '    /// 每一类必须正好 24 个, 而且不能有重复项(重复项等于浪费格子)。'])),

    # 2) 进面板 / 网格内容
    (L([
        '        // 25 格, 正中间那格空着 —— 24 个符号落在其余 24 格上',
        '        var g0 = c.GridItems();',
        '        Check("网格一页 25 格", g0.Count.ToString(), "25");',
        '        Check("正中间是第 12 格(5×5 的正中)", c.GridCenter.ToString(), "12");',
        '        Check("正中间那格是空的", g0[c.GridCenter], "");',
        '        var want = new List<string>();',
        '        for (int i = 0; i < 25; i++)',
        '        {',
        '            int k = c.PunctIdxOfGrid(i);',
        '            want.Add(k >= 0 && k < InputCore.PunctCats[0].Length ? InputCore.PunctCats[0][k] : "");',
        '        }',
        '        CheckTrue("网格里装的是第一类(中心留空)", g0.SequenceEqual(want));',
        '        Check("24 个符号一个没落", g0.Count(x => x.Length > 0).ToString(), "24");']),
     L([
        '        // 25 格 = 24 个符号 + 正中间那格写**分类名**(2.1.8 的标签格)',
        '        Check("进面板光标就落在正中间", c.GridIdx.ToString(), c.GridCenter.ToString());',
        '        var g0 = c.GridItems();',
        '        Check("网格一页 25 格", g0.Count.ToString(), "25");',
        '        Check("正中间是第 12 格(5×5 的正中)", c.GridCenter.ToString(), "12");',
        '        Check("正中间那格写的是分类名", g0[c.GridCenter], "常用");',
        '        Check("正中间那格不算符号(按 A 不会打出东西)",',
        '              c.PunctIdxOfGrid(c.GridCenter).ToString(), "-1");',
        '        var want = new List<string>();',
        '        for (int i = 0; i < 25; i++)',
        '        {',
        '            int k = c.PunctIdxOfGrid(i);',
        '            want.Add(k < 0 ? c.PunctCatName',
        '                     : k < InputCore.PunctCats[0].Length ? InputCore.PunctCats[0][k] : "");',
        '        }',
        '        CheckTrue("网格里装的是第一类(正中间是分类名)", g0.SequenceEqual(want));',
        '        Check("24 个符号 + 1 个分类名 = 25 格都有内容",',
        '              g0.Count(x => x.Length > 0).ToString(), "25");'])),

    # 3) 换类后回中
    (L(['        CheckTrue("换类后光标回到头一个", c.GridIdx == 0);']),
     L(['        CheckTrue("换类后光标回到正中间", c.GridIdx == c.GridCenter);'])),

    # 4) 摇杆: 中心格够不到 + 松手回中 + 停在标签格上按 A 什么都不发
    (L([
        '        // 正中间那格是空的: 推过去**不该**把光标挪上去(站上去按 A 什么也没有)',
        '        StickXY(0.05, 0.05); Frame(c, 60);',
        '        Check("推正中间(空的那一格) -> 光标不挪过去", c.GridIdx.ToString(), "4");',
        '',
        '        StickXY(0.5, -0.5); Frame(c, 60);',
        '        Check("斜推右下角 -> 最后一格(第 24 格)", c.GridIdx.ToString(), "24");',
        '',
        '        StickCenter(); Frame(c, 60);',
        '        Check("松手光标**留在原地**(不回中, 否则按 A 打出来的是别的符号)",',
        '              c.GridIdx.ToString(), "24");',
        '',
        '        TextOutput.Reset();',
        '        Tap(c, GamepadInput.A, 60);',
        '        Check("按 A 上屏的就是最后那一格的符号",',
        '              string.Join("|", TextOutput.Sent), "U:" + InputCore.PunctCats[1][23]);']),
     L([
        '        // 正中间那格**摇杆推不到**: 圆撑成方之后, 推着的时候至少有一轴已经',
        '        // 出了中间那一列/行。所以标签格只可能由"松手回中"到, 不会被顺手推上去。',
        '        StickXY(0.5, 0.5); Frame(c, 60);',
        '        CheckTrue("推着的时候够不到正中间(标签格只由回中到)",',
        '                   c.GridIdx != c.GridCenter);',
        '',
        '        StickXY(0.5, -0.5); Frame(c, 60);',
        '        Check("斜推右下角 -> 最后一格(第 24 格)", c.GridIdx.ToString(), "24");',
        '',
        '        TextOutput.Reset();',
        '        Tap(c, GamepadInput.A, 60);',
        '        Check("按 A 上屏的就是最后那一格的符号",',
        '              string.Join("|", TextOutput.Sent), "U:" + InputCore.PunctCats[1][23]);',
        '',
        '        // 松手**回中**(2.1.8): 落回正中间那个标签格 —— 和九键"松手回 5"同一个规矩',
        '        StickCenter(); Frame(c, 60);',
        '        Check("松手回中 -> 落在正中间那个标签格(第 12 格)",',
        '              c.GridIdx.ToString(), "12");',
        '',
        '        TextOutput.Reset();',
        '        Tap(c, GamepadInput.A, 60);',
        '        Check("停在标签格上按 A 什么都不发(它装的不是符号)",',
        '              string.Join("|", TextOutput.Sent), "");',
        '        Check("标签格上写的还是当前分类名", c.GridItems()[c.GridCenter], "中文");'])),
]


def main():
    s = io.open(P, encoding="utf-8", newline="").read()
    assert "\r" not in s, "coretest 是 LF, 读到 CR 了"
    for i, (old, new) in enumerate(EDITS, 1):
        if old not in s:
            raise SystemExit("!! 第 %d 处匹配不上:\n%s" % (i, old[:240]))
        if s.count(old) != 1:
            raise SystemExit("!! 第 %d 处不唯一(%d)" % (i, s.count(old)))
        s = s.replace(old, new, 1)
        print("  第 %d 处 OK" % i)
    io.open(P, "w", encoding="utf-8", newline="").write(s)
    t = io.open(P, encoding="utf-8", newline="").read()
    assert "\r" not in t
    print("写完, 行数=%d" % (t.count("\n") + 1))


if __name__ == "__main__":
    main()
