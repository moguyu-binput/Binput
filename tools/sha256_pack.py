# -*- coding: utf-8 -*-
r"""给发布包算 SHA256 校验值。

为什么需要:
  你发布的 zip 会被别人从网盘/GitHub 转手。中间任何一环被人把里面的 exe 换成夹带病毒的,
  单看文件名和大小是发现不了的。SHA256 相当于给每个文件按字节内容算一个"指纹" ——
  内容动一个 bit, 指纹就完全变样。只要你在发布页把指纹贴出来, 拿到的人在自己电脑上
  重新算一遍对得上, 就证明他手里的东西和你当初发出去的**一模一样**。

用法:
  python sha256_pack.py                     扫 D:\柄输-发布包 下所有 "-精简版/-完整版" 目录和 zip
  python sha256_pack.py --dir <某个目录>     只扫这一个
  python sha256_pack.py --check             拿着已有的 校验值.txt 逐条核对(验连amics后没被人动过)

产出:
  <每个包目录>\校验值.txt          跟着那个包走
  D:\柄输-发布包\SHA256SUMS.txt    汇总所有版本, 发布时直接把这一段贴出去

为什么同时给出 exe 和 zip 两个指纹:
  · exe 的指纹**只由源码决定**, 你重编译同样的代码得到的是同样的数 —— 适合长期贴。
  · zip 的指纹**每次重压都可能不一样**(压缩包里记着文件的修改时间), 所以只在
    "这次发布"这个语境下有效。两个都给, 谁方便用谁。
"""
import hashlib
import os
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\柄输-发布包"
OUT_NAME = "SHA256SUMS.txt"
PER_PACK = "校验值.txt"


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def list_pack_files(d):
    """列出一个包目录里该算指纹的文件(跳过私人数据目录和个人笔记)。"""
    out = []
    # 优先顺序: 主程序排最前, 别人也只关心它
    prio = ["柄输.exe", "Binput.exe", "dict.txt"]
    for name in prio:
        p = os.path.join(d, name)
        if os.path.isfile(p):
            out.append(name)
    for base, dirs, files in os.walk(d):
        dirs[:] = [x for x in dirs if x not in ("save", "licenses")]
        for f in files:
            rel = os.path.relpath(os.path.join(base, f), d)
            if rel in out or f == PER_PACK:
                continue
            out.append(rel)
    return out


def build_one(d, zips):
    lines = []
    items = [(rel, os.path.join(d, rel)) for rel in list_pack_files(d)]
    items += [(os.path.basename(z), z) for z in zips if os.path.isfile(z)]
    for rel, p in items:
        if not os.path.isfile(p):
            continue
        lines.append((sha256_of(p), rel, os.path.getsize(p)))
    return lines


def main():
    args = sys.argv[1:]
    if args and args[0] == "--check":
        check_file = os.path.join(ROOT, OUT_NAME)
        if os.path.isfile(check_file):
            bad = verify_file(check_file)
        else:
            print("找不到 " + check_file)
            bad = 1
        for d in sorted(os.listdir(ROOT)):
            p = os.path.join(ROOT, d, PER_PACK)
            if os.path.isdir(os.path.join(ROOT, d)) and os.path.isfile(p):
                bad += verify_file(p)
        sys.exit(1 if bad else 0)

    targets = []
    single = False
    if args and args[0] == "--dir":
        if len(args) < 2:
            print("用法: --dir <目录>")
            sys.exit(2)
        d = args[1]
        single = True
        targets = [(os.path.basename(d.rstrip("\\/")), d,
                    [p for p in [d + ".zip"] if os.path.isfile(d + ".zip")])]
    else:
        if not os.path.isdir(ROOT):
            print("没有这个目录: " + ROOT)
            sys.exit(2)
        for name in sorted(os.listdir(ROOT)):
            d = os.path.join(ROOT, name)
            if not os.path.isdir(d):
                continue
            if not name.endswith(("-精简版", "-完整版")):
                continue
            z = os.path.join(ROOT, name + ".zip")
            targets.append((name, d, [z] if os.path.isfile(z) else []))

    if not targets:
        print("没有找到可以算的包")
        sys.exit(2)

    all_lines = ["# 柄输 发布包 SHA256 校验值",
                 "# 生成时间: " + time.strftime("%Y-%m-%d %H:%M:%S"),
                 "#",
                 "# 怎么验(Windows 自带, 不用装任何东西):",
                 "#   1) 解压 zip, 进到解出来的那个文件夹",
                 "#   2) 地址栏敲 cmd 回车, 粘下面这句:",
                 "#      certutil -hashfile 柄输.exe SHA256",
                 "#   3) 屏幕上那串 64 位的数, 和本文件里 柄输.exe 那一行对一下",
                 "#      一模一样 = 这个 exe 和我发出去的是同一个人サマ电子版; 不一样就别用。",
                 "#",
                 "# 注意: exe 的指纹只由源码决定(重编译同一份源码会得到同一个数);",
                 "#       zip 的指纹每次重新压缩都可能变(压缩包里记着文件时间), 只在本次发布有效。",
                 ""]

    for name, d, zips in targets:
        lines = build_one(d, zips)
        if not lines:
            print("!! " + name + " 是空的, 跳过")
            continue
        block = ["# ==== " + name + " ===="]
        for h, rel, size in lines:
            block.append("%s  %s  (%s 字节)" % (h, rel, format(size, ",")))
        block.append("")
        all_lines += block

        # 每个包目录里也放一份, 跟着包走
        per = os.path.join(d, PER_PACK)
        with open(per, "w", encoding="utf-8-sig", newline="\r\n") as f:
            f.write("\r\n".join(["# " + name + " —— 单个文件的 SHA256 校验值",
                                 "# 和这一比对不上, 说明这个包在别人手上被改过。", ""] + block[1:]))
        print("%-22s %d 个文件 -> %s" % (name, len(lines), per))
        for h, rel, _ in lines[:3]:
            print("   %s  %s" % (h, rel))

    out = os.path.join(ROOT, OUT_NAME)
    if not single:
        with open(out, "w", encoding="utf-8-sig", newline="\r\n") as f:
            f.write("\r\n".join(all_lines))
        print("\n汇总 -> " + out)
    else:
        print("\n(只算了一个目录, 没去动根目录那份汇总 " + OUT_NAME + ")")


def verify_file(sumfile):
    """拿清单逐条核对。返回不合的条数。"""
    print("\n核对 " + sumfile)
    bad = 0
    folder = os.path.dirname(sumfile)
    # 清单可能在发布根目录(里面的文件分散在各包目录), 也可能就在某个包里;
    # 而外层那个 zip 放在发布根目录 —— 这几个地方都得找一遍才找得全
    lines = open(sumfile, encoding="utf-8-sig").read().replace("\r\n", "\n").split("\n")
    search = [folder, ROOT]
    cur = folder
    hit = 0
    for ln in lines:
        s = ln.strip()
        if not s:
            continue
        if s.startswith("#"):
            if s.startswith("# ====") :
                # 根清单里换包了: 目标目录跟着切
                name = s.strip("# =").strip()
                p = os.path.join(folder, name)
                cur = p if os.path.isdir(p) else folder
            continue
        parts = s.split("  ")
        if len(parts) < 2:
            continue
        h, rel = parts[0], parts[1]
        # 当前包目录 -> 发布根目录(外层 zip 在那)
        cand = [os.path.join(s, rel) for s in (cur, ROOT)]
        real = next((c for c in cand if os.path.isfile(c)), None)
        if real is None:
            print("   跳过(本地没有): " + rel)
            continue
        got = sha256_of(real)
        hit += 1
        if got.lower() == h.lower():
            print("   一致   " + rel)
        else:
            bad += 1
            print("   !! 不一致! " + rel + "\n      清单: " + h + "\n      实际: " + got)
    print("   核对了 %d 个文件, 不一致 %d 个" % (hit, bad))
    return bad


if __name__ == "__main__":
    main()
