"""skin-lab 截图: 用程序自带 --shot 参数从视觉树渲染 PNG(不怕被别的窗口盖住)。
截图前把主题预置成 8(小天使), 结束后恢复原值 —— 配置目录和正式版是同一个。"""
import os
import subprocess
import sys
import time

EXE = r"C:\Users\<你>\Binput\bin\Release\net8.0-windows\GamepadT9.exe"
OUT = r"C:\Users\<你>\Binput\tools\shots"
THEME_FILE = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9", "theme.txt")
os.makedirs(OUT, exist_ok=True)


def shot(args, name):
    path = os.path.join(OUT, name + ".png")
    if os.path.exists(path):
        os.remove(path)
    p = subprocess.Popen([EXE] + args)
    try:
        p.wait(timeout=30)
    except subprocess.TimeoutExpired:
        p.kill()
        print(f"!! {name}: 超时")
        return 1
    ok = os.path.exists(path) and os.path.getsize(path) > 1000
    print(("OK " if ok else "!! ") + name)
    return 0 if ok else 1


# 主题预置成 8(小天使), 结束恢复
old_theme = None
if os.path.exists(THEME_FILE):
    old_theme = open(THEME_FILE, encoding="utf-8", errors="ignore").read()
os.makedirs(os.path.dirname(THEME_FILE), exist_ok=True)
open(THEME_FILE, "w").write("8")

rc = 0
try:
    rc |= shot(["--debug", "--game", "--shot=" + os.path.join(OUT, "skin_front.png")], "skin_front")
    rc |= shot(["--debug", "--game", "--settings", "--shot=" + os.path.join(OUT, "skin_back.png")], "skin_back")
    rc |= shot(["--debug", "--game", "--demo=en26", "--shot=" + os.path.join(OUT, "skin_en26.png")], "skin_en26")
    rc |= shot(["--debug", "--game", "--demo=wheel", "--shot=" + os.path.join(OUT, "skin_wheel.png")], "skin_wheel")
finally:
    if old_theme is not None:
        open(THEME_FILE, "w").write(old_theme)
    else:
        try:
            os.remove(THEME_FILE)
        except OSError:
            pass
sys.exit(rc)
