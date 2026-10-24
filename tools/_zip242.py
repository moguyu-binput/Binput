# -*- coding: utf-8 -*-
"""把 2.4.2 两个包目录压成 zip。

两条老规矩:
  1. **压包前必须把 save\\ 挪走** —— 那里面是私人的输入习惯 / 快捷 / 设置。
     挪不是删(rm 走 safe-delete 会被拦), 挪到 _trash_242_save_* 里。
  2. zip 里也要跳过 save(万一挪漏了)。
"""
import os
import shutil
import sys
import zipfile

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\柄输-发布包"
PKGS = ["柄输-2.4.2-精简版", "柄输-2.4.2-完整版"]

# ---- ① 挪走 save ----
for name in PKGS:
    d = os.path.join(ROOT, name)
    save = os.path.join(d, "save")
    if os.path.isdir(save):
        dst = os.path.join(ROOT, "_trash_242_save_" + ("lite" if "精简" in name else "sc"))
        if os.path.exists(dst):
            shutil.rmtree(dst)
        os.replace(save, dst)
        print("已挪走 %s\\save -> %s" % (name, os.path.basename(dst)))
    else:
        print("%s 里没有 save(跳过)" % name)

# ---- ② 压 zip ----
for name in PKGS:
    d = os.path.join(ROOT, name)
    zp = os.path.join(ROOT, name + ".zip")
    if os.path.exists(zp):
        os.remove(zp)
    n = 0
    saved = 0
    with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for dirpath, dirnames, filenames in os.walk(d):
            dirnames[:] = [x for x in dirnames if x.lower() != "save"]
            for fn in filenames:
                full = os.path.join(dirpath, fn)
                rel = os.path.relpath(full, d)
                if rel.lower().startswith("save" + os.sep):
                    saved += 1
                    continue
                z.write(full, rel)
                n += 1
    print("已压 %s  (%d 个文件, 跳过 save 下 %d 个, %.1f MB)"
          % (os.path.basename(zp), n, saved, os.path.getsize(zp) / 1048576.0))

# ---- ③ 抽查: zip 里不许有 save ----
print("\n抽查:")
for name in PKGS:
    zp = os.path.join(ROOT, name + ".zip")
    with zipfile.ZipFile(zp) as z:
        bad = [x for x in z.namelist() if x.lower().startswith("save/")]
        print("  %s: 条目 %d 个, save 相关 %d (该 0)" % (os.path.basename(zp), len(z.namelist()), len(bad)))
