# -*- coding: utf-8 -*-
"""【临时】读屏幕像素反推"唤出来之后卡片到底透了多少"。

做法: 面板本色是已知的(原色主题 Panel=0x14,0x14,0x20, alpha=0xE8=232/255≈0.91)。
      合成结果 = 背景*(1-a) + 面板*a。先量出**没盖界面时**那一点的背景色,
      再量**界面盖上去之后**同一点的颜色, 反推 a:
          a = (显示 - 背景) / (面板 - 背景)
      a≈0.91 = 正常; 明显偏小 = 界面真的比该有的更透。
"""
import ctypes
import os
import re
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")
BIN = r"C:\Users\<你>\Binput\bin\Release\net8.0-windows"
EXE = os.path.join(BIN, "Binput.exe")
LOG = os.path.join(BIN, "save", "log.txt")

gdi = ctypes.WinDLL("gdi32")
user = ctypes.WinDLL("user32")
user.GetDC.argtypes = [ctypes.c_void_p]
user.GetDC.restype = ctypes.c_void_p
gdi.GetPixel.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
gdi.GetPixel.restype = ctypes.c_uint32
user.ReleaseDC.argtypes = [ctypes.c_void_p, ctypes.c_void_p]


def pixel(x, y):
    hdc = user.GetDC(0)
    try:
        c = gdi.GetPixel(hdc, int(x), int(y))
    finally:
        user.ReleaseDC(0, hdc)
    r, g, b = c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF
    if c == 0xFFFFFFFF:
        return None
    return (r, g, b)


PANEL = (0x14, 0x14, 0x20)
A0 = 0xE8 / 255.0


def alpha_of(shown, bg):
    """三个通道各算一个 a, 取 |面板-背景| 最大的那个(最不容易被舍入带偏)。"""
    best, bestw = None, 0
    for i in range(3):
        d = PANEL[i] - bg[i]
        if abs(d) > bestw:
            bestw = abs(d)
            best = (shown[i] - bg[i]) / d
    return best, bestw


def kill():
    subprocess.run(["taskkill", "/F", "/IM", "Binput.exe"],
                   capture_output=True)
    subprocess.run(["taskkill", "/F", "/IM", "柄输.exe"],
                   capture_output=True)
    time.sleep(1.0)


def main():
    kill()
    if os.path.exists(LOG):
        os.remove(LOG)

    # 1) 起一份 -> 用第二份唤醒 -> 等它真的露脸
    p = subprocess.Popen([EXE], cwd=BIN)
    time.sleep(6)
    subprocess.run([EXE], cwd=BIN, capture_output=True)
    time.sleep(3)

    txt = open(LOG, encoding="utf-8", errors="replace").read()
    print("=== 日志尾 ===")
    print("\n".join(txt.strip().split("\n")[-8:]))
    hwnd = user.FindWindowW(None, "柄输")
    print("=== hwnd=%s 可见=%s ===" % (hwnd, user.IsWindowVisible(hwnd) if hwnd else "-"))
    rc = ctypes.wintypes.RECT()
    if hwnd:
        user.GetWindowRect(hwnd, ctypes.byref(rc))
        print("  窗口矩形: %d,%d %dx%d" % (rc.left, rc.top,
                                           rc.right - rc.left, rc.bottom - rc.top))
    m = re.search(r"卡片屏幕位置 ([-\d.]+),([-\d.]+) ([\d.]+)x([\d.]+)", txt)
    if not m:
        print("!! 日志里没读到卡片位置:\n", txt[-1500:])
        return
    lx, ly, lw, lh = (float(m.group(i)) for i in (1, 2, 3, 4))
    print(f"卡片: ({lx:.0f},{ly:.0f}) {lw:.0f}x{lh:.0f}")

    # 取卡片**左内边距**里那一点: 卡片有 14 的边距, 那一圈只有面板本色, 没有键挡着
    pts = [(lx + 6, ly + lh / 2), (lx + 6, ly + lh * 0.3), (lx + lw - 6, ly + lh / 2)]
    shown = [pixel(x, y) for x, y in pts]
    print("盖着界面时:", shown)

    # 2) 收掉界面, 同一点再量一次 = 背景
    kill()
    time.sleep(1.5)
    bg = [pixel(x, y) for x, y in pts]
    print("界面收掉后:", bg)

    print()
    for i, (s, b) in enumerate(zip(shown, bg)):
        if s is None or b is None:
            print(f"  点{i}: 读不到")
            continue
        a, w = alpha_of(s, b)
        flag = "正常" if abs(a - A0) < 0.08 else "!! 偏透"
        print(f"  点{i}: 背景{b} -> 显示{s}  反推 alpha={a:.3f} (该是 {A0:.3f}, 通道差 {w:.0f})  {flag}")


if __name__ == "__main__":
    main()
