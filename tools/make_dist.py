#!/usr/bin/env python
"""组装发布目录并打 zip。

一键完成: publish 产物 -> dist/, 文档 -> dist/, 第三方可选组件 -> dist/可选组件/,
最后打成 zip。以后出包只跑这一个脚本, 不用再手敲一串命令。

用法:
    python tools/make_dist.py                  # 默认 GamepadT9-<今天>.zip
    python tools/make_dist.py --out a.zip      # 指定输出名
    python tools/make_dist.py --only x64       # 只打某一个架构 (x64 / arm64)

`--only` 只是不把另一个架构收进 zip, dist/ 里的旧目录不会被删
(某些安全策略会拦删除操作)。
"""
import datetime
import os
import shutil
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "dist")
# 默认带当天日期, 和之前几版命名一致; 想固定名字用 --out GamepadT9.zip
ZIP = os.path.join(ROOT, "GamepadT9-%s.zip"
                   % datetime.date.today().isoformat())

# publish 产物 -> dist 下的文件夹名
BUILDS = [
    ("publish-win-x64", "GamepadT9-x64"),
    ("publish-win-arm64", "GamepadT9-arm64"),
]
# 根目录的文档 -> dist 根
DOCS = ["使用说明书.md", "先看这里.txt"]
# 第三方可选组件 -> dist/可选组件/
EXTRAS = ["HidHide"]


def human(n):
    return "%.1f MB" % (n / 1048576) if n >= 1048576 else "%d KB" % (n / 1024)


def main():
    global ZIP
    args = sys.argv[1:]
    if "--out" in args:
        i = args.index("--out")
        if i + 1 < len(args):
            ZIP = os.path.join(ROOT, args[i + 1])

    only = None
    if "--only" in args:
        i = args.index("--only")
        if i + 1 < len(args):
            only = args[i + 1].lower()
            if only not in ("x64", "arm64"):
                print("--only 只接受 x64 或 arm64")
                return 1
    print("只打 x64" if only == "x64" else ("只打 arm64" if only else "两个架构都打"))

    os.makedirs(DIST, exist_ok=True)
    total_files = 0
    wanted = []          # 要收进 zip 的顶层条目

    # 1. 两个架构的产物
    for src, dst in BUILDS:
        if only and not dst.endswith("-" + only):
            continue
        s = os.path.join(ROOT, src)
        d = os.path.join(DIST, dst)
        if not os.path.isdir(s):
            print("!! 缺少 %s (先跑 dotnet publish -r win-%s)" % (src, dst[-4:]))
            continue
        if not os.path.exists(os.path.join(s, "GamepadT9.exe")):
            print("!! %s 里没有 GamepadT9.exe, 跳过" % src)
            continue
        # dirs_exist_ok 覆盖而不删除: 某些安全策略会拦删除操作
        shutil.copytree(s, d, dirs_exist_ok=True)
        n = len(os.listdir(d))
        sz = sum(os.path.getsize(os.path.join(d, f)) for f in os.listdir(d)
                 if os.path.isfile(os.path.join(d, f)))
        print("  %-20s %2d 个文件  %s" % (dst, n, human(sz)))
        total_files += n
        wanted.append(dst)

        # dict.txt 是松散文件, 不打进 exe。漏了程序能启动但几乎打不出字
        if not os.path.exists(os.path.join(d, "dict.txt")):
            print("  !! %s 里没有 dict.txt —— 程序会退化成演示词库" % dst)

        # 系统输入法版的启动入口。exe 是同一个, 靠 --ime 参数区分 —— 两种键盘共用
        # 一份代码, 用户可以在托盘菜单里来回切, 不用装两个程序
        with open(os.path.join(d, "启动-系统输入法版.cmd"), "w", encoding="ascii") as f:
            f.write("@echo off\r\n"
                    "rem  26-key layout that hands pinyin to the system IME\r\n"
                    "cd /d \"%~dp0\"\r\n"
                    "start \"\" \"GamepadT9.exe\" --ime\r\n")
        print("  %-20s 已写入" % "启动-系统输入法版.cmd")

    # 2. 文档
    for f in DOCS:
        p = os.path.join(ROOT, f)
        if os.path.exists(p):
            shutil.copy(p, os.path.join(DIST, f))
            print("  %-20s 已更新" % f)
        else:
            p2 = os.path.join(DIST, f)
            if not os.path.exists(p2):
                continue
            print("  %-20s 只存在于 dist/ (根目录没有, 沿用旧版)" % f)
        wanted.append(f)
        total_files += 1

    # 3. 第三方可选组件
    for name in EXTRAS:
        s = os.path.join(ROOT, "thirdparty", name)
        if not os.path.isdir(s):
            continue
        files = [f for f in os.listdir(s) if os.path.isfile(os.path.join(s, f))]
        if not files:
            continue
        d = os.path.join(DIST, "可选组件", name)
        os.makedirs(d, exist_ok=True)
        for f in files:
            shutil.copy(os.path.join(s, f), os.path.join(d, f))
            total_files += 1
        sz = sum(os.path.getsize(os.path.join(d, f)) for f in files)
        print("  可选组件/%-11s %2d 个文件  %s" % (name, len(files), human(sz)))
        wanted.append("可选组件")

    # 4. 打 zip。只收 wanted 里的顶层条目 —— 这样 --only 时不用去删
    #    dist/ 里的旧目录(删除操作会被某些安全策略拦下)
    n = 0
    with zipfile.ZipFile(ZIP, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for top in wanted:
            base = os.path.join(DIST, top)
            if os.path.isfile(base):
                z.write(base, top)
                n += 1
                continue
            for root, dirs, files in os.walk(base):
                for f in files:
                    p = os.path.join(root, f)
                    z.write(p, os.path.relpath(p, DIST))
                    n += 1
    print()
    print("zip: %s" % ZIP)
    print("     %d 个文件, %s" % (n, human(os.path.getsize(ZIP))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
