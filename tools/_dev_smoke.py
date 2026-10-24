# -*- coding: utf-8 -*-
r"""开发态冒烟: 直接跑 bin\Release 里的 exe(还没出包时用它先探一遍)。

照抄 smoke_201.py 的清场 + 唤醒法, 只把 EXE 换成编译产物。
不截图 —— 只看窗口尺寸和日志。
"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"C:\Users\<你>\WorkBuddy"
EXE = os.path.join(ROOT, r"skin-lab\GamepadT9-Game\bin\Release\net8.0-windows\Binput.exe")
# 2.1.10 起日志跟着存档走(程序旁的 save\), 不再往 %LOCALAPPDATA% 里塞
LOG = os.path.join(ROOT, r"skin-lab\GamepadT9-Game\bin\Release\net8.0-windows\save\log.txt")

u32 = ctypes.WinDLL("user32", use_last_error=True)
u32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
u32.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
u32.FindWindowW.restype = wt.HWND


def logsize():
    try:
        return os.path.getsize(LOG)
    except OSError:
        return 0


# ---- 清场(用户拍板: 测试时遇到在跑的柄输直接结束, 不要问) ----
subprocess.run(["taskkill", "/F", "/IM", "柄输.exe"], capture_output=True)
subprocess.run(["taskkill", "/F", "/IM", "GamepadT9.exe"], capture_output=True)
time.sleep(1.0)

n0 = logsize()

# ---- 起第一份 ----
p = subprocess.Popen([EXE])
time.sleep(2.5)
# ---- 起第二份: 它发唤醒消息后自己退出 ----
subprocess.run([EXE], capture_output=True, timeout=20)
time.sleep(2.5)

hwnd = u32.FindWindowW(None, "柄输")
if not hwnd:
    print("!! 没找到窗口")
    rc = wt.RECT()
else:
    rc = wt.RECT()
    u32.GetWindowRect(hwnd, ctypes.byref(rc))
    w, h = rc.right - rc.left, rc.bottom - rc.top
    print(f"窗口: {w} x {h}  (期望 726 x 752)")

print("---- 日志尾部 ----")
try:
    with open(LOG, "rb") as f:
        f.seek(n0)
        tail = f.read().decode("utf-8", "replace")
    print(tail[-3000:])
except OSError as e:
    print("读日志失败:", e)

subprocess.run(["taskkill", "/F", "/IM", "GamepadT9.exe"], capture_output=True)
