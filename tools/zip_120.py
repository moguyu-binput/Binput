# -*- coding: utf-8 -*-
"""把 柄输-1.2.0-精简版 / 柄输-1.2.0-完整版 两个目录重新打成 zip。"""
import os, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VER = "1.2.0"

for kind in ("精简版", "完整版"):
    src = os.path.join(ROOT, "柄输-%s-%s" % (VER, kind))
    dst = os.path.join(ROOT, "柄输-%s-%s.zip" % (VER, kind))
    if not os.path.isdir(src):
        print("缺失目录:", src)
        continue
    if os.path.exists(dst):
        os.remove(dst)
    n = 0
    total = 0
    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for base, dirs, files in os.walk(src):
            for f in sorted(files):
                p = os.path.join(base, f)
                rel = os.path.relpath(p, src)
                z.write(p, rel)
                n += 1
                total += os.path.getsize(p)
    print("== 柄输-%s-%s ==" % (VER, kind))
    print("   文件 %d 个，原始 %d 字节，zip %.1f MB"
          % (n, total, os.path.getsize(dst) / 1024 / 1024))
