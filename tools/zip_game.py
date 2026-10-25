#!/usr/bin/env python
"""把两个发布目录各打成一个 zip。

只用 Python 的 zipfile —— 本机 PowerShell 的 Compress-Archive 会静默失败。
.pdb 调试符号不收进包。

用法:
    python tools/zip_game.py
"""
import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 发布目录名 -> zip 名
TARGETS = [
    ("GamepadT9-Game-x64", "GamepadT9-Game-x64.zip"),
    ("GamepadT9-Game-x64-selfcontained", "GamepadT9-Game-x64-selfcontained.zip"),
]
SKIP_EXT = (".pdb",)


def human(n):
    return "%.1f MB" % (n / 1048576) if n >= 1048576 else "%d KB" % (n / 1024)


def main():
    for d, zipname in TARGETS:
        src = os.path.join(ROOT, d)
        if not os.path.isdir(src):
            print("!! 跳过 %s (目录不存在)" % d)
            continue
        zp = os.path.join(ROOT, zipname)
        n = 0
        with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for root, _dirs, files in os.walk(src):
                for f in sorted(files):
                    if f.endswith(SKIP_EXT):
                        continue
                    p = os.path.join(root, f)
                    z.write(p, os.path.relpath(p, src))
                    n += 1
        print("%-40s %2d 个文件  %s" % (zipname, n, human(os.path.getsize(zp))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
