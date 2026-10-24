# -*- coding: utf-8 -*-
"""校验 1.2.0 的 zip 里的文件 == dist 目录里的文件(名字+大小)。
1.1.0 出过 dist 目录被删残 -> zip 少了 3 个文件的坑, 这里专门堵它。"""
import os, sys, zipfile
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:/Users/<你>/WorkBuddy"
MUST = ["柄输.exe", "使用说明.txt", "版本说明.md", "打开日志.cmd", "dict.txt"]
ok = True
for name in ("柄输-1.2.1-精简版", "柄输-1.2.1-完整版"):
    d = os.path.join(ROOT, name)
    z = os.path.join(ROOT, name + ".zip")
    on_disk = {}
    for base, _, files in os.walk(d):
        for f in files:
            p = os.path.join(base, f)
            on_disk[os.path.relpath(p, d).replace("\\", "/")] = os.path.getsize(p)
    with zipfile.ZipFile(z) as zf:
        names = zf.namelist()
        in_zip = {i.filename: i.file_size for i in zf.infolist()}
    miss = sorted(set(on_disk) - set(in_zip))
    extra = sorted(set(in_zip) - set(on_disk))
    diff = sorted(k for k in set(on_disk) & set(in_zip) if on_disk[k] != in_zip[k])
    lack = [m for m in MUST if m not in in_zip]
    print("== %s ==" % name)
    print("  dist 文件数=%d  zip 内文件数=%d" % (len(on_disk), len(names)))
    print("  zip 里缺的(dist 有):", miss or "无")
    print("  zip 里多的(dist 没有):", extra or "无")
    print("  大小不一致:", diff or "无")
    print("  必带文件缺失:", lack or "无")
    print("  zip 大小: %d 字节" % os.path.getsize(z))
    if miss or extra or diff or lack:
        ok = False
print("\n结论:", "全部一致 ✅" if ok else "有出入 ❌")
sys.exit(0 if ok else 1)
