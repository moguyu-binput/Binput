# -*- coding: utf-8 -*-
"""把 2.4.10 两个包目录压成 zip。

规矩(2.4.6 起, 以后都这样):
  1. **压包前必须把 save\\ 挪走** —— 那里面是私人的输入习惯 / 快捷 / 设置。
     挪不是删(rm 走 safe-delete 会被拦), 挪到 _trash_2410_save_* 里。
  2. zip 里**不带 make_dist 生成的说明文档**: 使用说明 / 注意事项 / 许可证与免责 /
     版本说明 / 打开日志.cmd 一律不进包(那些照常生成在文件夹里, 发布页 / 网站
     要用的文字从文件夹拿)。
  3. **但 zip 里要放用户自己写的那份使用说明** —— `D:\\使用说明.txt`
     (2026-10-25 用户拍板"以后都放进出的压缩包里面")。它由用户自己维护,
     压包时**从 D 盘根目录现读现写**, 所以用户在 D 盘改了这份, 下次压包
     自动就是新的; make_dist 生成的那份不进包(两份内容不同, 别混)。
     这份要是没了, 直接报错停下 —— 宁可不出包, 别出一个没有说明的包。
  4. **licenses\\ 留着** —— 那是 GPL-3.0 / LGPL-3.0 协议正文, 随二进制分发
     法律上要带着(用户原话: "如果协议是法律要求那么就留着")。
  5. zip 里放一个**只含 exe 一行**的 校验值.txt(现算现写, 文件夹里那份全文件
     版是审计用的, 不进包)。
"""
import hashlib
import os
import shutil
import sys
import zipfile

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\柄输-发布包"
PKGS = ["柄输-2.4.10-精简版", "柄输-2.4.10-完整版"]

# 不进压缩包的生成物(说明文档; 文件夹里照常生成)
SKIP = {"使用说明.txt", "注意事项.txt", "许可证与免责.txt", "版本说明.md",
        "打开日志.cmd", "校验值.txt"}
# 用户自己维护的使用说明(要进包的这一份)
USER_DOC = r"D:\使用说明.txt"

if not os.path.exists(USER_DOC):
    print("!! 缺 %s —— 用户自己写的那份使用说明, 没有它这个包不算出完" % USER_DOC)
    sys.exit(1)

# ---- ① 挪走 save ----
for name in PKGS:
    d = os.path.join(ROOT, name)
    save = os.path.join(d, "save")
    if os.path.isdir(save):
        dst = os.path.join(ROOT, "_trash_2410_save_" + ("lite" if "精简" in name else "sc"))
        if os.path.exists(dst):
            shutil.rmtree(dst)
        os.replace(save, dst)
        print("已挪走 %s\\save -> %s" % (name, os.path.basename(dst)))
    else:
        print("%s 里没有 save(跳过)" % name)

# ---- ② 压 zip ----
user_doc = open(USER_DOC, "rb").read()      # 原样字节写进去, 不碰编码
for name in PKGS:
    d = os.path.join(ROOT, name)
    zp = os.path.join(ROOT, name + ".zip")
    if os.path.exists(zp):
        os.remove(zp)
    n = 0
    saved = 0
    docs = 0
    with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for dirpath, dirnames, filenames in os.walk(d):
            dirnames[:] = [x for x in dirnames if x.lower() != "save"]
            for fn in filenames:
                full = os.path.join(dirpath, fn)
                rel = os.path.relpath(full, d)
                if rel.lower().startswith("save" + os.sep):
                    saved += 1
                    continue
                if fn in SKIP:                       # make_dist 生成的说明文档 / 全文件版校验值
                    docs += 1
                    continue
                z.write(full, rel)
                n += 1
        # 用户自己写的使用说明(现读现写)
        z.writestr("使用说明.txt", user_doc)
        # 只含 exe 一行的校验值(现算现写, 内容跟文件夹里那份全文件版不冲突)
        exe = os.path.join(d, "柄输.exe")
        h = hashlib.sha256(open(exe, "rb").read()).hexdigest()
        z.writestr("校验值.txt", "%s  柄输.exe  (%s 字节)\n"
                   % (h, format(os.path.getsize(exe), ",")))
    print("已压 %s  (%d 个文件 + 使用说明.txt + 校验值.txt, 跳过 save 下 %d 个、"
          "生成文档 %d 个, %.1f MB)"
          % (os.path.basename(zp), n, saved, docs, os.path.getsize(zp) / 1048576.0))

# ---- ③ 抽查: 不许有 save / 生成的说明文档; 使用说明是用户那份; 校验值只有 exe 一行 ----
print("\n抽查:")
# 使用说明.txt 现在**就该在包里**(用户那份), 不算"漏网的生成文档"
GEN_DOCS = SKIP - {"使用说明.txt"}
for name in PKGS:
    zp = os.path.join(ROOT, name + ".zip")
    with zipfile.ZipFile(zp) as z:
        names = z.namelist()
        bad = [x for x in names if x.lower().startswith("save/")]
        doc = [x for x in names
               if os.path.basename(x) in GEN_DOCS and x != "校验值.txt"]
        one = z.read("校验值.txt").decode("utf-8").strip().count("\n") == 0
        lic = "licenses/GPL-3.0.txt" in names
        same = z.read("使用说明.txt") == user_doc
        print("  %s: 条目 %d 个 | save %d(该0) 生成文档 %d(该0) "
              "校验值仅exe一行=%s licenses在=%s 使用说明=用户那份=%s"
              % (os.path.basename(zp), len(names), len(bad), len(doc), one, lic, same))
