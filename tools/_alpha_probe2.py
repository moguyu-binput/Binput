# -*- coding: utf-8 -*-
"""【临时】用 PrintWindow 把窗口自己渲染的结果抓下来读 alpha, 并且跑完整的
"唤出 -> 收起 -> 再唤出" 两轮, 对比第 1 次和第 2 次的卡片底色浓淡。

GDI 的屏幕 DC 读不到这种 per-pixel-alpha 的分层窗口(实测 GetPixel 在盖着界面
和没盖界面时返回完全一样的值), 所以只能让窗口自己画给我们看。
"""
import ctypes
import ctypes.wintypes as wt
import os
import re
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")
BIN = r"C:\Users\<你>\Binput\bin\Release\net8.0-windows"
EXE = os.path.join(BIN, "Binput.exe")
LOG = os.path.join(BIN, "save", "log.txt")

user = ctypes.WinDLL("user32")
gdi = ctypes.WinDLL("gdi32")
user.GetDC.restype = ctypes.c_void_p
user.GetLayeredWindowAttributes.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
user.GetWindowDC.restype = ctypes.c_void_p
user.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
user.FindWindowW.restype = ctypes.c_void_p
user.PrintWindow.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint]
gdi.CreateCompatibleDC.argtypes = [ctypes.c_void_p]
gdi.CreateCompatibleDC.restype = ctypes.c_void_p
gdi.CreateDIBSection.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint,
                                 ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint]
gdi.SelectObject.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
gdi.DeleteObject.argtypes = [ctypes.c_void_p]
gdi.DeleteDC.argtypes = [ctypes.c_void_p]
user.ReleaseDC.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
gdi.CreateDIBSection.restype = ctypes.c_void_p
gdi.GetPixel.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int32),
                ("biHeight", ctypes.c_int32), ("biPlanes", ctypes.c_uint16),
                ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int32),
                ("biYPelsPerMeter", ctypes.c_int32), ("biClrUsed", ctypes.c_uint32),
                ("biClrImportant", ctypes.c_uint32)]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER),
                ("bmiColors", ctypes.c_uint32 * 3)]


def grab(hwnd, w, h):
    """把窗口画进一张 32bpp DIB, 返回 BGRA 字节串(自顶向下)。"""
    screen = user.GetDC(0)
    mem = gdi.CreateCompatibleDC(screen)
    bi = BITMAPINFO()
    bi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bi.bmiHeader.biWidth = w
    bi.bmiHeader.biHeight = -h          # 负 = 自顶向下
    bi.bmiHeader.biPlanes = 1
    bi.bmiHeader.biBitCount = 32
    bits = ctypes.c_void_p()
    hbmp = gdi.CreateDIBSection(mem, ctypes.byref(bi), 0, ctypes.byref(bits), None, 0)
    old = gdi.SelectObject(mem, hbmp)
    ok = user.PrintWindow(hwnd, mem, 2)     # PW_RENDERFULLCONTENT
    buf = ctypes.string_at(bits, w * h * 4)
    gdi.SelectObject(mem, old)
    gdi.DeleteObject(hbmp)
    gdi.DeleteDC(mem)
    user.ReleaseDC(0, screen)
    return ok, buf


def px(buf, w, x, y):
    i = (y * w + x) * 4
    return buf[i], buf[i + 1], buf[i + 2], buf[i + 3]     # B G R A


def layered_alpha(hwnd):
    cr = ctypes.c_uint32()
    a = ctypes.c_ubyte()
    fl = ctypes.c_uint32()
    ok = user.GetLayeredWindowAttributes(ctypes.c_void_p(hwnd),
                                        ctypes.byref(cr), ctypes.byref(a),
                                        ctypes.byref(fl))
    return (bool(ok), int(a.value), int(fl.value))


def kill():
    subprocess.run(["taskkill", "/F", "/IM", "Binput.exe"], capture_output=True)
    subprocess.run(["taskkill", "/F", "/IM", "柄输.exe"], capture_output=True)
    time.sleep(1.2)


def wake():
    subprocess.run([EXE], cwd=BIN, capture_output=True)
    time.sleep(2.5)


def sample(tag, rel_pts):
    hwnd = user.FindWindowW(None, "柄输")
    if not hwnd:
        print(f"  [{tag}] 找不到窗口")
        return
    rc = wt.RECT()
    user.GetWindowRect(hwnd, ctypes.byref(rc))
    w, h = rc.right - rc.left, rc.bottom - rc.top
    ok, buf = grab(hwnd, w, h)
    la = layered_alpha(hwnd)
    print(f"  [{tag}] 窗口 {w}x{h} 可见={user.IsWindowVisible(hwnd)} "
          f"PrintWindow={ok} LWA(ok,alpha,flags)={la}")
    for (x, y) in rel_pts:
        b, g, r, a = px(buf, w, x, y)
        print(f"        点({x},{y}) BGR=({b},{g},{r}) alpha={a}")


def main():
    kill()
    if os.path.exists(LOG):
        os.remove(LOG)
    subprocess.Popen([EXE], cwd=BIN)
    time.sleep(6)

    wake()                                  # 第 1 次唤出
    txt = open(LOG, encoding="utf-8", errors="replace").read()
    m = re.search(r"卡片屏幕位置 ([-\d.]+),([-\d.]+) ([\d.]+)x([\d.]+)", txt)
    if not m:
        print("!! 没读到卡片位置:\n", txt[-1200:])
        return
    lx, ly, lw, lh = (float(m.group(i)) for i in (1, 2, 3, 4))
    hwnd = user.FindWindowW(None, "柄输")
    rc = wt.RECT()
    user.GetWindowRect(hwnd, ctypes.byref(rc))
    # 卡片左上角换算成窗口内的坐标
    rx, ry = int(lx - rc.left), int(ly - rc.top)
    pts = [(rx + 6, ry + int(lh / 2)), (rx + 6, ry + int(lh * 0.3)),
           (rx + int(lw) - 6, ry + int(lh / 2))]
    print(f"卡片相对窗口: ({rx},{ry}) {int(lw)}x{int(lh)}")
    sample("第1次唤出", pts)

    # 收起: 把焦点递给随便一个别的窗口(预输入版"点到别处"就收)
    me = ctypes.windll.kernel32.GetConsoleWindow()
    user.SetForegroundWindow(me)
    time.sleep(2.5)
    print("  收起后 可见=", user.IsWindowVisible(user.FindWindowW(None, "柄输") or 0))

    wake()                                  # 第 2 次唤出
    sample("第2次唤出", pts)


if __name__ == "__main__":
    main()
