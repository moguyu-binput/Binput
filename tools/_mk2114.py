# -*- coding: utf-8 -*-
"""把 2.1.13 那套发布脚本复制成 2.1.14。

纪律: 先在**替换前**把版本摘要 / 使用说明那两块就地换掉, 最后才做全局的
版本号替换 —— 否则旧那条也会被改写成新版本号。
"""
import io
import os

T = os.path.dirname(os.path.abspath(__file__))

JOBS = [
    ("make_dist_2113.py", "make_dist_2114.py"),
    ("smoke_2113.py", "smoke_2114.py"),
    ("zip_2113.py", "zip_2114.py"),
    ("_vercheck_2113.py", "_vercheck_2114.py"),
]

BODY = [
    "2.1.14 = 9 宫格 ↔ 5x5 全符号那一下也有动效了(打字逻辑一处没碰):",
    "        1 **9 宫格换成 5x5 那一屏符号**: 以前是硬切, 现在按你选的风格走",
    "          (默认: 浮起淡入; 旋转: 整片绕左下角转出去 / 转回来)",
    "        2 **5x5 里按 LB / RB 换分类**: 同样算\"换了一屏\", 同样跟着动",
    "        3 **9 键 ↔ 全键盘照旧是左右撑开**, 不和任何一种新动画叠着演",
    "        4 这次的改法不是给这一屏补一段代码 —— 是把\"什么算换了一屏\"交给",
    "          一处判断(ScreenId), 并把它提到每帧**第一行**做。以后再加任何",
    "          一种新的屏, 动效是自动跟上的",
    "        5 顺手: \"新的一页怎么进来 / 旧的一页怎么走\"以前抄了三遍、出门那边",
    "          抄了两遍, 现在合成一对, 数字一个没动",
    '"""',
]

USAGE = [
    "26. 2.1.14 9 宫格 ↔ 5×5 全符号那一下也有动效了",
    "    · **9 宫格换成 5×5 那一整屏符号**：以前是硬切，现在按你选的风格走",
    "      （默认：浮起淡入；旋转：整片绕左下角转出去、转回来）。",
    "    · **5×5 里按 LB / RB 换分类**：同样算「换了一屏」，同样跟着动。",
    "    · **9 键 ↔ 全键盘照旧是左右撑开**，不和任何一种新动画叠着演。",
    "    · 这次的改法不是给这一屏补一段代码 —— 是把「什么算换了一屏」交给一处",
    "      判断，并把它提到每帧**第一行**去做。**以后再加任何一种新的屏，",
    "      动效是自动跟上的。**",
    "    · 顺手：「新的一页怎么进来、旧的一页怎么走」以前抄了三遍、出门那边抄了",
    "      两遍，五份各写各的数字。现在合成一对，数字一个没动。",
    '"""',
]


def cut(lines, first_pred, end_line):
    """从 first_pred 命中的那行起, 一直删到内容==end_line 的那行(连带删掉)。"""
    i = None
    for n, l in enumerate(lines):
        if first_pred(l):
            i = n
            break
    if i is None:
        raise SystemExit("!! 找不到要替换的开头")
    for j in range(i, len(lines)):
        if lines[j].strip() == end_line:
            return lines[:i], lines[j + 1:]
    raise SystemExit("!! 找不到结尾 %r" % end_line)


def main():
    for a, b in JOBS:
        p = os.path.join(T, a)
        s = io.open(p, encoding="utf-8").read()
        lines = s.replace("\r\n", "\n").split("\n")

        if a.startswith("make_dist"):
            # ① 版本摘要: 从 "2.1.13 = ..." 起到字符串结束
            head, tail = cut(lines, lambda l: l.startswith("2.1.13 = "), '"""')
            lines = head + BODY + tail
            # ② 使用说明: 末尾补一条 26.(在 USAGE 字符串结束前插进去)
            i = None
            for n, l in enumerate(lines):
                if l.strip().startswith("· **LB / RB 翻页以前压根没有换页动画**"):
                    i = n
                    break
            if i is None:
                raise SystemExit("!! 没找到使用说明里 LB/RB 那条")
            # 往后找 USAGE 字符串的结尾, 在它前面插
            j = i
            while lines[j].strip() != '"""':
                j += 1
            lines = lines[:j] + USAGE + lines[j + 1:]   # USAGE 自带结束的那行, 别再留一个
            s = "\n".join(lines)

        # ③ 最后才做全局替换 —— 此时旧的那两块已经换成 2.1.14 的说法了。
        #   使用说明里那条 2.1.13 是**历史条目**, 得先扣出来别跟着改版本号
        s = s.replace("25. 2.1.13 换页动效", "@@KEEP25@@")
        s = s.replace("2.1.13", "2.1.14")
        s = s.replace("@@KEEP25@@", "25. 2.1.13 换页动效")
        s = s.replace("2113", "2114")

        if a.startswith("_vercheck"):
            old = 'WANT_COMMIT = "6767c61"'
            new = 'WANT_COMMIT = "1e7e706"'
            if old not in s:
                raise SystemExit("!! vercheck 里的旧提交号不是 6767c61")
            s = s.replace(old, new, 1)

        q = os.path.join(T, b)
        io.open(q, "w", encoding="utf-8", newline="\n").write(s)
        print("%-22s -> %-22s %6d 字节" % (a, b, len(s.encode("utf-8"))))

    # 自检
    d = io.open(os.path.join(T, "make_dist_2114.py"), encoding="utf-8").read()
    print("\nmake_dist_2114: 提到 2.1.14 共 %d 处; 残留 2.1.13 = %d 处; 残留 2113 = %d 处"
          % (d.count("2.1.14"), d.count("2.1.13"), d.count("2113")))
    for keep in ("23. 2.1.8 改了两处", "24. 2.1.9 改了三处", "25. 2.1.13 换页动效"):
        print("  历史 %-24s %s" % (keep, "在" if keep in d else "!! 丢了"))
    for must in ("26. 2.1.14 9 宫格", "_pub_2114", "柄输-2.1.14-精简版"):
        print("  新增 %-24s %s" % (must, "在" if must in d else "!! 丢了"))
    v = io.open(os.path.join(T, "_vercheck_2114.py"), encoding="utf-8").read()
    print("  vercheck 提交号 1e7e706: %s" % ("在" if "1e7e706" in v else "!! 丢了"))


main()
