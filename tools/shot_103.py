# -*- coding: utf-8 -*-
"""1.0.3 截图: 用程序自带的 --shot 从视觉树渲染 PNG。

拍三张:
  key9_letters  九键默认(只留字母, 字号放大)
  key9_digits   开关键打开(数字 + 字母, 和 1.0.2 一样)
  settings      设置页背面(看新加的"九键按键"那一节)

用法: python tools/shot_103.py
"""
import os
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"C:\Users\<你>\WorkBuddy"
EXE = os.path.join(ROOT, "skin-lab", "GamepadT9-Game", "bin", "Release",
                   "net8.0-windows", "GamepadT9.exe")
OUT = os.path.join(ROOT, "tools", "shots")
CFG = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9")
K9 = os.path.join(CFG, "key9num.txt")
THEME = os.path.join(CFG, "theme.txt")
os.makedirs(OUT, exist_ok=True)


def backup(p):
    if os.path.exists(p):
        with open(p, encoding="utf-8", errors="ignore") as f:
            return f.read()
    return None


def restore(p, v):
    if v is None:
        if os.path.exists(p):
            os.remove(p)
    else:
        with open(p, "w") as f:
            f.write(v)


def shot(args, name):
    path = os.path.join(OUT, name + ".png")
    if os.path.exists(path):
        os.remove(path)
    p = subprocess.Popen([EXE] + args)
    try:
        p.wait(timeout=40)
    except subprocess.TimeoutExpired:
        p.kill()
        print(f"!! {name}: 超时")
        return
    ok = os.path.exists(path) and os.path.getsize(path) > 1000
    print(("OK  " if ok else "!!  ") + f"{name}  {os.path.getsize(path) if os.path.exists(path) else 0} 字节")


def main():
    subprocess.run(["taskkill", "/F", "/IM", "GamepadT9.exe"], capture_output=True)
    old_k9, old_theme = backup(K9), backup(THEME)
    os.makedirs(CFG, exist_ok=True)

    with open(THEME, "w") as f:
        f.write("0")            # 原色, 看得最清楚

    with open(K9, "w") as f:
        f.write("0")            # 只留字母(1.0.3 默认)
    shot(["--debug", "--shot=" + os.path.join(OUT, "key9_letters.png")], "key9_letters")

    with open(K9, "w") as f:
        f.write("1")            # 显示数字
    shot(["--debug", "--shot=" + os.path.join(OUT, "key9_digits.png")], "key9_digits")

    with open(K9, "w") as f:
        f.write("0")
    shot(["--debug", "--settings", "--shot=" + os.path.join(OUT, "settings.png")], "settings")

    restore(K9, old_k9)
    restore(THEME, old_theme)
    print("\n输出目录:", OUT)


if __name__ == "__main__":
    main()
