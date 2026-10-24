# -*- coding: utf-8 -*-
"""把 212 那套发布脚本复制成 213(只换版本号 / 发布目录名)。

【注意】批量替换会把**历史叙述**里的版本号也换掉(比如"2.1.2 修掉的七处"
会被写成 2.1.3), 复制完必须 diff 一遍手工改回去 —— 这条是 2.1.2 那次踩的坑。
"""
import io, os, sys

sys.stdout.reconfigure(encoding="utf-8")
T = r"C:\Users\<你>\Binput\tools"

JOBS = [
    ("make_dist_212.py", "make_dist_213.py"),
    ("smoke_212.py", "smoke_213.py"),
    ("zip_212.py", "zip_213.py"),
    ("_vercheck_212.py", "_vercheck_213.py"),
]

for a, b in JOBS:
    p = os.path.join(T, a)
    s = io.open(p, encoding="utf-8").read()
    s = s.replace("_pub_212", "_pub_213").replace("2.1.2", "2.1.3")
    q = os.path.join(T, b)
    io.open(q, "w", encoding="utf-8", newline="\n").write(s)
    print("%-20s -> %-20s %6d 字节" % (a, b, len(s.encode("utf-8"))))
