"""1.0.8 两项改动的截图验证:
  pgrid    = 拼音网格展开(验"网格不再重复拼音行那几条")
  press9   = 九键选中键正被按住(验"按下缩小")
  press26  = 全键盘选中键正被按住
  idle9    = 九键不按(press9 的对照, 用来量出缩小了多少)
另外跑动态效果关掉的那一档, 确认那时候是变暗而不是缩小。
"""
import os
import subprocess
import sys

EXE = r"C:\Users\<你>\Binput\bin\Release\net8.0-windows\GamepadT9.exe"
OUT = r"C:\Users\<你>\Binput\tools\shots"
FX_FILE = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9", "fx.txt")
THEME_FILE = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9", "theme.txt")
os.makedirs(OUT, exist_ok=True)


def _swap(path, val):
    """临时改一个配置项, 返回原值(原文件不存在则返回 None)。"""
    old = None
    if os.path.exists(path):
        old = open(path, encoding="utf-8", errors="ignore").read()
    if val is None:
        try:
            os.remove(path)
        except OSError:
            pass
        return old
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "w").write(val)
    return old


def shot(demo, name, fx=None, theme="0"):
    path = os.path.join(OUT, name + ".png")
    if os.path.exists(path):
        os.remove(path)
    # 主题钉成纯色第 0 套: 贴纸风有光晕和半透明层, 像素量出来的尺寸不可信
    old_theme = _swap(THEME_FILE, theme)
    old_fx = _swap(FX_FILE, None if fx is None else ("1" if fx else "0"))
    try:
        p = subprocess.Popen([EXE, "--debug", "--nosingle", "--game",
                              "--demo=" + demo, "--shot=" + path])
        try:
            p.wait(timeout=30)
        except subprocess.TimeoutExpired:
            p.kill()
            print("!! " + name + ": 超时")
            return 1
    finally:
        _swap(THEME_FILE, old_theme)
        _swap(FX_FILE, old_fx)
    ok = os.path.exists(path) and os.path.getsize(path) > 1000
    print(("OK " if ok else "!! ") + name)
    return 0 if ok else 1


rc = 0
try:
    rc |= shot("pgrid", "v108_pgrid", fx=True)
    rc |= shot("press9", "v108_press9", fx=True)
    rc |= shot("press26", "v108_press26", fx=True)
    rc |= shot("press9", "v108_press9_nofx", fx=False)   # 动态效果关 -> 该变暗而不是缩小
finally:
    pass
sys.exit(rc)
