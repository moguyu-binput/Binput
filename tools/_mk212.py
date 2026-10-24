# -*- coding: utf-8 -*-
"""把 211 那套发布脚本复制成 212(只换版本号 / 发布目录名)。"""
import io, os, sys

sys.stdout.reconfigure(encoding="utf-8")
T = r"C:\Users\<你>\Binput\tools"

JOBS = [
    ("make_dist_211.py", "make_dist_212.py"),
    ("smoke_211.py", "smoke_212.py"),
    ("zip_211.py", "zip_212.py"),
    ("_vercheck_211.py", "_vercheck_212.py"),
]

for a, b in JOBS:
    p = os.path.join(T, a)
    s = io.open(p, encoding="utf-8").read()
    s = s.replace("_pub_211", "_pub_212").replace("2.1.1", "2.1.2")
    q = os.path.join(T, b)
    io.open(q, "w", encoding="utf-8", newline="\n").write(s)
    print("%-20s -> %-20s %6d 字节" % (a, b, len(s.encode("utf-8"))))
