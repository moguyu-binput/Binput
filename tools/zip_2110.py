# -*- coding: utf-8 -*-
r"""把 柄输-2.1.10-精简版 / 完整版 压成 zip。

压之前**先验一遍目录**: 1.1.0 出过 dist 目录被删残、zip 照样压出来
(结果是个少 3 个文件的坏包) 的事。这里先看必需文件齐不齐, 不齐就不压。

2.1.10 起**说明文档不进包**: 使用说明 / 注意事项 / 版本说明 / 许可证与免责 /
licenses / 打开日志 这几样照旧生成在文件夹里(要看随时能看), 但 zip 里只留
跑得起来那几样 —— 柄输.exe、dict.txt、skin\(完整版另有 5 个 WPF 零件 dll)。
"""
import os
import sys
import zipfile

sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"D:\柄输-发布包"
MUST = ["柄输.exe", "dict.txt", "skin"]
# 这几样在文件夹里照旧有, 就是不压进 zip(2.1.10 起)
DROP = ["使用说明.txt", "注意事项.txt", "版本说明.md", "许可证与免责.txt",
        "打开日志.cmd", "licenses"]

for name in ("柄输-2.1.10-精简版", "柄输-2.1.10-完整版"):
    d = os.path.join(ROOT, name)
    z = os.path.join(ROOT, name + ".zip")
    lack = [m for m in MUST if not os.path.exists(os.path.join(d, m))]
    if lack:
        print(f"!! {name} 缺 {lack}, 不压")
        sys.exit(1)
    # 2.1.10 起数据目录就是**程序旁的 save\\**, 所以冒烟跑完之后这个包里一定会多出
    # 一个 save\\ —— 里面是**你自己的** Binput.save(习惯 / 快捷 / 设置)。压进 zip
    # 等于把私人习惯连同包一起发给别人, 这里一律跳过。
    n = 0
    with zipfile.ZipFile(z, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for base, dirs, files in os.walk(d):
            # save\ 是你自己的数据; 2.1.10 起那几份说明文档也不进包
            dirs[:] = [x for x in dirs if x != "save" and x != "licenses"]
            for f in files:
                if f in DROP:
                    continue
                p = os.path.join(base, f)
                zf.write(p, os.path.relpath(p, d))
                n += 1
    sd = os.path.join(d, "save")
    if os.path.isdir(sd):
        print(f"  (save\\ 里有 {len(os.listdir(sd))} 项, 是你自己的数据, 没压进去)")
    print(f"{name}: {n} 个文件 -> {os.path.getsize(z):,} 字节")
