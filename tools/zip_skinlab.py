#!/usr/bin/env python
"""把 skin-lab 的 Release 产物打成一个 zip, 交给用户自己真机试皮肤。

skin 素材目录(bin 旁边)要一起带上 —— 玻璃贴片是外部 PNG, 少了就退回纯色兜底。
.pdb 不收进包。

用法: python tools/zip_skinlab.py
"""
import os
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "skin-lab-test.zip")
RELEASE = os.path.join(ROOT, "skin-lab", "GamepadT9-Game", "bin", "Release", "net8.0-windows")
SKIN = os.path.join(ROOT, "skin-lab", "GamepadT9-Game", "skin")
SKIP_EXT = (".pdb",)


def human(n):
    return "%.1f MB" % (n / 1048576) if n >= 1048576 else "%d KB" % (n / 1024)


def add_dir(z, src, arc):
    n = 0
    for root, _dirs, files in os.walk(src):
        for f in sorted(files):
            if f.endswith(SKIP_EXT):
                continue
            p = os.path.join(root, f)
            z.write(p, os.path.join(arc, os.path.relpath(p, src)))
            n += 1
    return n


def main():
    if not os.path.isdir(RELEASE):
        print("!! 没有 Release 产物:", RELEASE)
        return 1
    n = 0
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        n += add_dir(z, RELEASE, "GamepadT9")
        # 项目一般已经把 skin 复制到输出目录了; 没有才从源码目录补一份, 免得包里两份
        if os.path.isdir(SKIN) and not os.path.isdir(os.path.join(RELEASE, "skin")):
            n += add_dir(z, SKIN, os.path.join("GamepadT9", "skin"))
    print("skin-lab-test.zip  %d 个文件  %s" % (n, human(os.path.getsize(OUT))))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
