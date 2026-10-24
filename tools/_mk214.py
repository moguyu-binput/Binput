# -*- coding: utf-8 -*-
"""把 213 那套发布脚本复制成 214(只换版本号 / 发布目录名)。

【注意】批量替换会把**历史叙述**里的版本号也换掉(比如"2.1.2 修掉的七处"
会被写成新版本号), 复制完必须 diff 一遍手工改回去 —— 这是第三次踩同一个坑了,
2.1.3 那版我把 make_dist 里那几段历史叙述逐条改了回来, 这次照做。
"""
import io, os, sys

sys.stdout.reconfigure(encoding="utf-8")
T = r"C:\Users\<你>\Binput\tools"

JOBS = [
    ("make_dist_213.py", "make_dist_214.py"),
    ("smoke_213.py", "smoke_214.py"),
    ("zip_213.py", "zip_214.py"),
    ("_vercheck_213.py", "_vercheck_214.py"),
]

for a, b in JOBS:
    p = os.path.join(T, a)
    s = io.open(p, encoding="utf-8").read()
    s = s.replace("_pub_213", "_pub_214").replace("2.1.3", "2.1.4")
    q = os.path.join(T, b)
    io.open(q, "w", encoding="utf-8", newline="\n").write(s)
    print("%-20s -> %-20s %6d 字节" % (a, b, len(s.encode("utf-8"))))
