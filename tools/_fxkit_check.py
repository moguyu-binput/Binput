# -*- coding: utf-8 -*-
"""
验证 2.1.12「配方表」这一步没有把任何一个数字改掉。

做法：**逐行比对数字序列**。
改写没有增删任何行, 所以旧第 N 行和新第 N 行是一一对应的。
把两边都做同一件事 —— 把符号常量展开成字面数字(新这边查 FxKit, 旧这边查原先那几个
散落的 const), 然后把这行里剩下的数字全揪出来排成一串, 逐行对齐比较。

任何一眼看不见的差别都会被揪出来; 真有改了的地方(比如注释里的话术变了、
或者像 FxPop 那一晃从写死值换成按比例算)会单列出来由人确认。
"""
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "skin-lab", "GamepadT9-Game")

FILES = ["OverlayWindow.Fx.cs", "OverlayWindow.xaml.cs",
         "OverlayWindow.Layer.cs", "OverlayWindow.Render.cs"]

# 旧版本散在各处的常量 -> 值(用来把旧行展开成数字, 才比得动)
OLD_CONST = {
    "EXPAND_MS": "230", "SET_ANIM_MS": "180",
    "FLIP_HALF_MS": "65", "FLIP_STEP_MS": "32",
    "PRESS_SCALE": "0.88", "PRESS_DIM": "0.5",
    "SET_POP": "1.14", "CELL_WOBBLE": "4.5",
}

NUM = re.compile(r"-?\d+(?:\.\d+)?")

# ---- 已知、且已当面量过的差异 ----
# FxPop 那一晃的第二、三帧, 原来写死成 3.0 / -1.4, 现在按和设置格同一条公式算:
#   4.5 x 0.667 = 3.0015   (差 0.0015 度)
#   4.5 x 0.311 = 1.3995   (差 0.0005 度)
# 一个像素的千分之几, 屏幕上一个像素都不到 —— 换来的是两处用同一套回摆比例。
ACCEPT = {
    ("OverlayWindow.Fx.cs", 576),
    ("OverlayWindow.Fx.cs", 577),
}

# 老那边散落的那个名字 -> 搬到 FxKit 之后叫什么、值必须是多少。
# 这条清单才是"没抄错数"的真正证据: 逐行比对只能证明"这行没改",
# 而搬家本身(旧 -> 新)抄没抄错, 得靠这张对照表。
EXPECT = {
    # 26 键展开
    "MsExpand": "230", "KeyFadePart": "0.8",
    # 面板进出场
    "MsPageIn": "180", "MsPageOut": "180", "MsFace": "170",
    "ScalePageIn": "0.92", "DyPageIn": "26",
    "ScaleFaceIn": "0.965", "DyFaceIn": "7",
    "ScalePageOut": "0.94", "DyPageOut": "10",
    # 翻面
    "MsFlipHalf": "65", "MsFlipStep": "32",
    # 按下去
    "ScalePress": "0.88", "DimPress": "0.5",
    "MsDown": "80", "MsUp": "190", "MsRest": "150", "MsZoom": "110",
    # 浮起来四档
    "ScalePop": "1.14", "ScaleTile": "1.12", "ScaleKey26": "1.20", "ScaleSlide": "1.22",
    # 那一弹三帧
    "PopOver": "1.07", "PopBack": "0.98", "PopAt0": "85", "PopAt1": "155", "PopAt2": "215",
    # 那一晃四帧
    "WobDeg": "4.5", "WobBack1": "0.667", "WobBack2": "0.311",
    "WobAt0": "80", "WobAt1": "150", "WobAt2": "215", "WobAt3": "290",
    # ◀LB / RB▶
    "ScaleSidePop": "1.20", "ScaleSideBack": "0.94",
    "SideDeg": "14", "SideWobBack1": "-0.60", "SideWobBack2": "0.28", "SidePushPx": "7",
    "MsSidePop0": "80", "MsSidePop1": "150", "MsSidePop2": "215", "MsSideWob0": "75",
    # 预览框
    "MsNoteIn": "340", "MsNoteOut": "260",
    "NoteMixStep": "0.45", "NoteMixOut": "0.55",
    "NoteInFrom": "0.28", "NoteInSpan": "0.42", "NoteOutSpan": "0.72",
}


def git_old(name):
    out = subprocess.run(["git", "show", "HEAD:" + name],
                         cwd=SRC, capture_output=True)
    if out.returncode != 0:
        raise SystemExit("git show 失败: " + name)
    return out.stdout.decode("utf-8")


def fxkit_consts():
    """从 OverlayWindow.FxKit.cs 里读出所有 const double 的名字和值。"""
    raw = open(os.path.join(SRC, "OverlayWindow.FxKit.cs"), "rb").read().decode("utf-8")
    d = {}
    for m in re.finditer(r"public const double\s+(.+?);", raw):
        body = m.group(1)
        for part in body.split(","):
            if "=" not in part:
                continue
            k, v = part.split("=")
            d[k.strip()] = v.strip()
    return d


def expand_new(line, consts):
    """FxKit.X -> 值; FxKit.Ms(...) / FxKit.At(...) 里的也一并摊开。反复摊到不动为止。"""
    for _ in range(6):
        before = line
        for k, v in consts.items():
            line = re.sub(r"\bFxKit\." + k + r"\b", v, line)
        # FxKit.EaseOut() / FxKit.Ms(x) / FxKit.At(x) 本身: 抹掉壳, 留下里面的数
        line = re.sub(r"\bFxKit\.(?:Ms|At)\(", "(", line)
        line = re.sub(r"\bFxKit\.(?:EaseOut|EaseIn)\(\)", "E", line)
        if line == before:
            break
    return line


def expand_old(line):
    for k, v in OLD_CONST.items():
        line = re.sub(r"\b" + k + r"\b", v, line)
    return line


def nums(line):
    return NUM.findall(line)


def main():
    consts = fxkit_consts()
    print("从 FxKit.cs 读到 %d 个常量" % len(consts))

    diffs = []
    accepted = []
    checked = 0
    skipped = 0
    for name in FILES:
        old_lines = git_old(name).replace("\r\n", "\n").split("\n")
        new_lines = open(os.path.join(SRC, name), "rb").read().decode("utf-8").split("\r\n")
        if len(old_lines) != len(new_lines):
            diffs.append((name, 0, "行数不同 %d / %d" % (len(old_lines), len(new_lines)), ""))
            continue
        for i, (o, n) in enumerate(zip(old_lines, new_lines)):
            so, sn = o.strip(), n.strip()
            # 任一边是纯注释就跳过: 话术怎么写都不改行为。
            # (已经搬走的 const 定义行也属于这一类 —— 它的新值在 FxKit.cs 里,
            #   下面那段"常量抽查"专门盯这一件事。)
            if so.startswith("//") or sn.startswith("//"):
                skipped += 1
                continue
            checked += 1
            a = nums(expand_old(o))
            b = nums(expand_new(n, consts))
            key = (name, i + 1)
            if a != b:
                (accepted if key in ACCEPT else diffs).append(
                    (name, i + 1, so, sn))

    missing, wrong = [], []
    for k, v in EXPECT.items():
        if k not in consts:
            missing.append(k)
        elif consts[k] != v:
            wrong.append("%s 期望 %s 实为 %s" % (k, v, consts[k]))
    if missing:
        raise SystemExit("FxKit 里缺这些常量: " + ", ".join(missing))
    if wrong:
        raise SystemExit("搬错数了:" + "".join("\n  " + w for w in wrong))
    print("老数字 -> 新常量 逐一对表: %d 项全部吻合" % len(EXPECT))
    print("逐行比对 %d 行, 跳过 %d 行注释" % (checked, skipped))
    print()
    for name, ln, o, n in accepted:
        print("  已知差异 %s:%d" % (name, ln))
        print("      旧 %s" % o[:120])
        print("      新 %s" % n[:120])
    if not diffs:
        print("\n== 通过: 除上面 %d 处已当面量过的, 没有任何一行的数字发生变化 =="
              % len(accepted))
        return
    print("\n以下 %d 行的数字序列变了, 逐条过目:" % len(diffs))
    for name, ln, o, n in diffs:
        print("\n%s:%d" % (name, ln))
        print("   旧 %s" % o[:150])
        print("   新 %s" % n[:150])
        print("   旧数字 %s" % nums(expand_old(o)))
        print("   新数字 %s" % nums(expand_new(n, consts)))


if __name__ == "__main__":
    main()
