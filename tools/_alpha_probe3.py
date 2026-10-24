# -*- coding: utf-8 -*-
"""【临时】第 1 次唤出 与 第 2 次唤出(真的收起过)各抓一次窗口自身渲染, 比一比
卡片底色的浓淡。

判定办法: 面板本色是 (0x14,0x14,0x20)、alpha=0xE8≈0.91, 抓图时底下垫的是黑,
于是"正常"的卡片像素应该是 (18,18,29) 左右。数一数这个颜色的像素有多少、
它们落在哪一块 —— 两块抓图一比就知道第 2 次是不是真的变淡了。
"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")
BIN = r"C:\Users\<你>\Binput\bin\Release\net8.0-windows"
EXE = os.path.join(BIN, "Binput.exe")
LOG = os.path.join(BIN, "save", "log.txt")

user = ctypes.WinDLL("user32")
gdi = ctypes.WinDLL("gdi32")
user.GetDC.argtypes = [ctypes.c_void_p]
user.GetDC.restype = ctypes.c_void_p
user.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
user.FindWindowW.restype = ctypes.c_void_p
user.PrintWindow.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint]
user.SetForegroundWindow.argtypes = [ctypes.c_void_p]
gdi.CreateCompatibleDC.argtypes = [ctypes.c_void_p]
gdi.CreateCompatibleDC.restype = ctypes.c_void_p
gdi.CreateDIBSection.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint,
                                 ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint]
gdi.CreateDIBSection.restype = ctypes.c_void_p
gdi.SelectObject.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
gdi.DeleteObject.argtypes = [ctypes.c_void_p]
gdi.DeleteDC.argtypes = [ctypes.c_void_p]
user.ReleaseDC.argtypes = [ctypes.c_void_p, ctypes.c_void_p]


class BIH(ctypes.Structure):
    _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int32),
                ("biHeight", ctypes.c_int32), ("biPlanes", ctypes.c_uint16),
                ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int32),
                ("biYPelsPerMeter", ctypes.c_int32), ("biClrUsed", ctypes.c_uint32),
                ("biClrImportant", ctypes.c_uint32)]


class BMI(ctypes.Structure):
    _fields_ = [("bmiHeader", BIH), ("bmiColors", ctypes.c_uint32 * 3)]


def grab(hwnd, w, h):
    screen = user.GetDC(0)
    mem = gdi.CreateCompatibleDC(screen)
    bi = BMI()
    bi.bmiHeader.biSize = ctypes.sizeof(BIH)
    bi.bmiHeader.biWidth, bi.bmiHeader.biHeight = w, -h
    bi.bmiHeader.biPlanes, bi.bmiHeader.biBitCount = 1, 32
    bits = ctypes.c_void_p()
    hbmp = gdi.CreateDIBSection(mem, ctypes.byref(bi), 0, ctypes.byref(bits), None, 0)
    old = gdi.SelectObject(mem, hbmp)
    ok = user.PrintWindow(hwnd, mem, 2)
    buf = bytes(ctypes.string_at(bits, w * h * 4))
    gdi.SelectObject(mem, old)
    gdi.DeleteObject(hbmp)
    gdi.DeleteDC(mem)
    user.ReleaseDC(0, screen)
    return ok, buf


def analyse(tag, buf, w, h):
    """数"正浓的面板像素"(R,G 在 13..23、B 在 24..34), 报数量和包围盒。"""
    n = 0
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(0, h, 2):
        row = y * w * 4
        for x in range(0, w, 2):
            i = row + x * 4
            b, g, r, a = buf[i], buf[i + 1], buf[i + 2], buf[i + 3]
            if 13 <= r <= 23 and 13 <= g <= 23 and 24 <= b <= 34:
                n += 1
                x0, y0 = min(x0, x), min(y0, y)
                x1, y1 = max(x1, x), max(y1, y)
    box = f"({x0},{y0})-({x1},{y1})" if x1 >= 0 else "无"
    print(f"  [{tag}] 正浓面板像素 {n} 个, 包围盒 {box}")
    return n


def shot(tag):
    hwnd = user.FindWindowW(None, "柄输")
    if not hwnd:
        print(f"  [{tag}] 找不到窗口")
        return 0
    rc = wt.RECT()
    user.GetWindowRect(hwnd, ctypes.byref(rc))
    w, h = rc.right - rc.left, rc.bottom - rc.top
    ok, buf = grab(hwnd, w, h)
    print(f"  [{tag}] 可见={user.IsWindowVisible(hwnd)} {w}x{h} PrintWindow={ok}")
    return analyse(tag, buf, w, h)


def kill():
    subprocess.run(["taskkill", "/F", "/IM", "Binput.exe"], capture_output=True)
    subprocess.run(["taskkill", "/F", "/IM", "柄输.exe"], capture_output=True)
    time.sleep(1.2)


def main():
    kill()
    if os.path.exists(LOG):
        os.remove(LOG)
    subprocess.Popen([EXE], cwd=BIN)
    time.sleep(6)
    subprocess.run([EXE], cwd=BIN, capture_output=True)     # 第 1 次唤出
    time.sleep(2.5)
    n1 = shot("第1次唤出")

    # 真的收起: 开个记事本去抢前台 —— 预输入版"点到别处"就自己收
    note = subprocess.Popen(["notepad.exe"])
    time.sleep(2.0)
    nh = user.FindWindowW(None, "无标题 - 记事本") or user.FindWindowW("Notepad", None)
    print("  记事本 hwnd=", nh)
    if nh:
        user.SetForegroundWindow(ctypes.c_void_p(nh))
    time.sleep(2.5)
    print("  收起后 柄输可见=", user.IsWindowVisible(user.FindWindowW(None, "柄输") or 0))

    subprocess.run([EXE], cwd=BIN, capture_output=True)     # 第 2 次唤出
    time.sleep(2.5)
    n2 = shot("第2次唤出")

    print()
    print(f"第1次 {n1} 个  vs  第2次 {n2} 个   ->",
          "两次一样" if n1 and abs(n1 - n2) <= max(4, n1 * 0.02)
          else ("!! 第2次明显变淡/变透" if n2 < n1 else "第2次反而更多(?)"))
    subprocess.run(["taskkill", "/F", "/IM", "notepad.exe"], capture_output=True)


if __name__ == "__main__":
    main()
