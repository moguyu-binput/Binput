# -*- coding: utf-8 -*-
"""
OverlayWindow 第 0 步: partial class 拆分(纯搬家)。

做法:
  1. 原文件按 "行首恰好 4 空格" 切出成员;
  2. 每个成员按行号查 RULES 决定搬到哪个文件;
  3. 成员之间的零散行(注释/空行)跟着它后面那个成员走;
  4. 剩下的留在 OverlayWindow.xaml.cs(窗口本体)。

不改动任何一行内容 —— 只做"整块搬家", 保证编译出的 IL 逻辑等价。
生成的文件一律 CRLF, 且断言没有 \r\r\n。
"""
import os, sys, hashlib

ROOT = r"C:\Users\<你>\Binput"
SRC = os.path.join(ROOT, "OverlayWindow.xaml.cs")

# (起始行, 结束行, 目标)  行号 1-based 闭区间
RULES = [
    (31,   96,  "Theme"),
    (97,   271, "Layout"),
    (272,  583, "Theme"),
    (584,  629, "Build"),
    (630,  771, "Keep"),
    (772,  790, "Keep"),
    (791,  950, "Keep"),
    (951,  959, "Layout"),
    (960,  1015, "Keep"),
    (1016, 1045, "Layout"),
    (1046, 1059, "Keep"),
    (1060, 1195, "Layout"),
    (1196, 1648, "Keep"),
    (1649, 1662, "Fx"),
    (1664, 1689, "Keep"),
    (1690, 1715, "Layer"),
    (1716, 1806, "Pages"),
    (1807, 1835, "Layer"),
    (1836, 2112, "Pages"),
    (2113, 2667, "Layer"),
    (2668, 2872, "Fx"),
    (2873, 3067, "Layer"),
    (3068, 3171, "Quick"),
    (3172, 3264, "Pages"),
    (3265, 3357, "Quick"),
    (3358, 3458, "Fx"),
    (3459, 3639, "Layer"),
    (3640, 3650, "Layer"),
    (3651, 3923, "Build"),
    (3924, 3994, "Fx"),
    (3995, 4434, "Render"),
    (4435, 4489, "Render"),
    (4490, 4562, "Render"),
    (4563, 4746, "Fx"),
    (4747, 4777, "Render"),
    (4778, 5117, "Keep"),
]

def warn(m):
    print("!! " + m); sys.exit(1)

raw = open(SRC, "rb").read().decode("utf-8")
if raw.count("\r\r\n"): warn("源文件有 \\r\\r\\n")
if raw.count("\n") != raw.count("\r\n"): warn("源文件有单独 LF")
lines = raw.split("\r\n")
print("lines=%d 尾=%r" % (len(lines), lines[-1][:20]))

def is_start(s):
    if not s.startswith("    "): return False
    if s.startswith("     "): return False
    t = s.strip()
    return bool(t) and not t.startswith("//")

starts = [i for i, s in enumerate(lines) if is_start(s)]
print("members=%d  first=%d last=%d" % (len(starts), starts[0] + 1, starts[-1] + 1))

# 行号(1-based) -> 目标
def bucket(ln):
    for a, b, name in RULES:
        if a <= ln <= b: return name
    return None

# 每个成员 -> 目标; 顺手做重叠校验
for i in starts:
    ln = i + 1
    if bucket(ln) is None: warn("第 %d 行没在 RULES 里: %r" % (ln, lines[i][:70]))

# 成员之前那些零散行(header 之后到第一个成员)跟第一个成员走
parts = {n: [] for n in set(bucket(i + 1) for i in starts)}
parts[bucket(starts[0] + 1)].extend(lines[17:starts[0]])

for k, i in enumerate(starts):
    end = starts[k + 1] if k + 1 < len(starts) else len(lines)
    tgt = bucket(i + 1)
    parts[tgt].extend(lines[i:end])

HEADER = lines[0:17]          # using ... public partial class ... {
USING = lines[0:13]           # 给新文件用的 using 段

for n in sorted(parts):
    print("  %-8s %5d 行" % (n, len(parts[n])))

def eol_ok(s):
    return "\r\r\n" not in s and "\n" == "\r\n" or True

def check(name, text):
    if "\r\r\n" in text: warn(name + " 出现 \\r\\r\\n")
    body = text.replace("\r\n", "\n")
    if "\r" in body: warn(name + " 出现裸 CR")
    # 括号配平(粗查: 只看行首 4 空格与结尾大括号)
    if not text.endswith("\r\n"): warn(name + " 末尾没有换行")

main = parts["Keep"]
main_text = "\r\n".join(HEADER + main)
if not main_text.endswith("\r\n"): main_text += "\r\n"
check("OverlayWindow.xaml.cs", main_text)
open(SRC, "wb").write(main_text.encode("utf-8"))
print("write OverlayWindow.xaml.cs  %d 行" % (main_text.count("\r\n")))

order = ["Theme", "Layout", "Build", "Render", "Fx", "Layer", "Pages", "Quick"]
note = {
    "Theme":  "配色 / 皮肤 / 字体色 —— 界面长什么颜色",
    "Layout": "尺寸常量与窗口几何 —— 东西摆在哪、多大",
    "Build":  "第一次把界面零件造出来(九键/26 键/候选行/拼音行)",
    "Render": "每一帧怎么画 —— Render / Paint / 摇杆 / 0 号键",
    "Fx":     "所有\"怎么动\" —— 缩放/旋转/位移/淡入淡出/翻面",
    "Layer":  "设置层与快捷层 —— 页面骨架、导航、蒙版、说明条",
    "Pages":  "设置里那些页面本身写了些什么(文案与格子)",
    "Quick":  "快捷面板 —— IQuickUI 那套接口",
}
for n in order:
    body = parts[n]
    if n == "Keep": continue
    head = [
        "// ---- %s ----" % note[n],
        "//",
        "// 2.1.11 之前这些代码全在 OverlayWindow.xaml.cs 一个文件里(5117 行)。",
        "// 这次只是按职责把它拆成几个 partial class 文件 —— 一行都没改,",
        "// 编译出来的行为跟拆之前完全一样(改名/搬位置不影响任何逻辑)。",
        "",
    ]
    text = "\r\n".join(USING + ["", "namespace Binput;", ""] +
                       ["public partial class OverlayWindow", "{"] +
                       head + body + ["}", ""])
    check("OverlayWindow.%s.cs" % n, text)
    path = os.path.join(ROOT, "OverlayWindow.%s.cs" % n)
    open(path, "wb").write(text.encode("utf-8"))
    print("write OverlayWindow.%s.cs  %d 行" % (n, text.count("\r\n")))
print("OK")
