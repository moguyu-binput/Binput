# -*- coding: utf-8 -*-
"""把 _center218.py 里"整块第一行贴在 crlf([ 后面"那几处的外引号换成单引号。"""
import io
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
p = "_center218.py"
s = io.open(p, encoding="utf-8", newline="").read()
n = 0
out = []
for ln in s.split("\n"):
    m = re.match(r'^(\s*(?:crlf|lf)\(\[)"(.*)",$', ln)
    if m and "'" not in m.group(2):
        out.append(m.group(1) + "'" + m.group(2) + "',")
        n += 1
    else:
        out.append(ln)
io.open(p, "w", encoding="utf-8", newline="").write("\n".join(out))
print("补换 %d 行" % n)
