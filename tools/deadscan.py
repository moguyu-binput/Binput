# -*- coding: utf-8 -*-
"""扫描 C# 源码里"声明了但别处没用到"的成员 —— 用来找调试残留 / 死代码。

用法: python deadscan.py <源码目录>
只看 private / internal 的成员和局部变量之外的方法; public 的跳过(可能是对外 API)。
"""
import os
import re
import sys
from collections import defaultdict

SRC = sys.argv[1] if len(sys.argv) > 1 else "."
SKIP_DIRS = {"obj", "bin", ".git"}

files = []
for name in sorted(os.listdir(SRC)):
    if not name.endswith(".cs"):
        continue
    files.append(os.path.join(SRC, name))

texts = {}
for p in files:
    with open(p, encoding="utf-8-sig") as f:
        texts[p] = f.read()

alltext = "\n".join(texts.values())

# 成员声明: [<modifiers>] <type> <Name>(
DECL_METHOD = re.compile(
    r"^\s{0,8}(?:(?:private|public|internal|protected|static|sealed|virtual|override|async|readonly|new|unsafe|extern|partial)\s+)+"
    r"(?:[A-Za-z_][\w<>\[\]?\.]*)\s+([A-Za-z_]\w*)\s*\(", re.M)
# 字段 / 属性: 结尾是 ; 或 = 而不是 (
DECL_FIELD = re.compile(
    r"^\s{0,8}(?:(?:private|internal|protected|static|readonly|const|new|public)\s+)+"
    r"(?:[A-Za-z_][\w<>\[\]?\.]*)\s+([A-Za-z_]\w*)\s*(?:=|;)", re.M)

report = defaultdict(list)
for p, t in texts.items():
    base = os.path.basename(p)
    names = set()
    for m in DECL_METHOD.finditer(t):
        names.add((m.group(1), "method", m.start()))
    for m in DECL_FIELD.finditer(t):
        names.add((m.group(1), "field", m.start()))
    for nm, kind, pos in sorted(names, key=lambda x: x[2]):
        line = t.count("\n", 0, pos) + 1
        # 全项目出现次数
        cnt = len(re.findall(r"\b" + re.escape(nm) + r"\b", alltext))
        if cnt <= 1:
            report[base].append((nm, kind, line, cnt))

for base in sorted(report):
    items = report[base]
    if not items:
        continue
    print(f"--- {base} ---")
    for nm, kind, line, cnt in items:
        print(f"  L{line:<5} {kind:<7} {nm}  (全项目出现 {cnt} 次)")
    print()
