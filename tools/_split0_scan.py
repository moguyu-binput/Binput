# -*- coding: utf-8 -*-
"""列出 OverlayWindow.xaml.cs 的顶层成员分布(按缩进 4 空格的方法/字段/嵌套类切片)。
只用来给人工拆文件做参考, 不做任何写入。"""
import io, re, sys

P = r"C:\Users\<你>\Binput\OverlayWindow.xaml.cs"
raw = open(P, "rb").read().decode("utf-8")
nl = "\r\n" if "\r\n" in raw else "\n"
lines = raw.split(nl)
print("lines=%d  nl=%r" % (len(lines), nl))

# 顶层成员起点: 行首 4 空格 + 非空白, 且行尾不以 ',' 结尾
starts = []
for i, s in enumerate(lines):
    if not s.startswith("    ") or s.startswith("     "):
        continue
    t = s.strip()
    if not t or t.startswith("//") or t.startswith("///"):
        continue
    starts.append(i)

out = []
for k, i in enumerate(starts):
    end = starts[k + 1] if k + 1 < len(starts) else len(lines)
    head = lines[i].strip()
    # 名称
    m = re.search(r"([A-Za-z_][A-Za-z0-9_]*)\s*\(", head)
    name = m.group(1) if m else head[:50]
    out.append((i + 1, end, end - i, name, head[:90]))

print("members=%d" % len(out))
for no, end, ln, name, head in out:
    print("%5d-%5d %4d  %-34s | %s" % (no, end, ln, name, head))
