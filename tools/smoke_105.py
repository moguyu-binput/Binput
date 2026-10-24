# -*- coding: utf-8 -*-
"""1.0.5 冒烟: 起一份 -> 用第二份实例把它唤醒 -> 截整屏 -> 看日志。
重点确认三件事:
  1) 能起来、能画出来、不报错
  2) 日志里有 "九键数字: 隐藏(只留字母)"  (1.0.3 默认只印字母)
  3) 截图里九宫格只有一条文字带 (没有数字那一条)
"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"C:\Users\<你>\WorkBuddy"
EXE = os.path.join(ROOT, sys.argv[1] if len(sys.argv) > 1 else "柄输-1.0.5-精简版", "柄输.exe")
OUT = os.path.join(ROOT, "tools", "shots", "v105_smoke.png")
CFG = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9")
LOG = os.path.join(CFG, "log.txt")
THEME = os.path.join(CFG, "theme.txt")
K9N = os.path.join(CFG, "key9num.txt")

u32 = ctypes.WinDLL("user32", use_last_error=True)
g32 = ctypes.WinDLL("gdi32", use_last_error=True)
u32.GetForegroundWindow.restype = wt.HWND
u32.GetWindowTextW.argtypes = [wt.HWND, ctypes.c_wchar_p, ctypes.c_int]
u32.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
u32.FindWindowW.restype = wt.HWND
u32.PrintWindow.argtypes = [wt.HWND, wt.HDC, ctypes.c_uint]
u32.PrintWindow.restype = ctypes.c_bool
u32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
u32.SetProcessDPIAware = None


def logsize():
    try:
        return os.path.getsize(LOG)
    except OSError:
        return 0


def capture(hwnd, path):
    r = wt.RECT()
    u32.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    if w <= 10 or h <= 10:
        return "窗口尺寸不对 %dx%d" % (w, h)
    hdc = u32.GetWindowDC(hwnd)
    mem = g32.CreateCompatibleDC(hdc)
    bmp = g32.CreateCompatibleBitmap(hdc, w, h)
    g32.SelectObject(mem, bmp)
    ok = u32.PrintWindow(hwnd, mem, 2)      # 2 = PW_RENDERFULLCONTENT
    if not ok:
        g32.BitBlt(mem, 0, 0, w, h, hdc, 0, 0, 0x00CC0020)

    class BITMAPINFOHEADER(ctypes.Structure):
        _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int),
                    ("biHeight", ctypes.c_int), ("biPlanes", ctypes.c_uint16),
                    ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                    ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int),
                    ("biYPelsPerMeter", ctypes.c_int), ("biClrUsed", ctypes.c_uint32),
                    ("biClrImportant", ctypes.c_uint32)]
    hdr = BITMAPINFOHEADER()
    hdr.biSize = ctypes.sizeof(hdr)
    hdr.biWidth, hdr.biHeight = w, -h
    hdr.biPlanes, hdr.biBitCount = 1, 32
    hdr.biCompression = 0
    hdr.biSizeImage = 0
    buf = ctypes.create_string_buffer(w * h * 4)
    g32.GetDIBits.argtypes = [wt.HDC, wt.HBITMAP, ctypes.c_uint, ctypes.c_uint,
                              ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint]
    g32.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(hdr), 0)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"BM")
        off = 14 + ctypes.sizeof(hdr)
        total = off + len(buf)
        f.write(ctypes.c_uint32(total))
        f.write(ctypes.c_uint32(0))
        f.write(ctypes.c_uint32(off))
        f.write(bytes(hdr))
        f.write(buf.raw)
    g32.DeleteObject(bmp)
    g32.DeleteDC(mem)
    u32.ReleaseDC(hwnd, hdc)
    return "OK %dx%d -> %s" % (w, h, os.path.getsize(path))


def main():
    subprocess.run(["taskkill", "/F", "/IM", "柄输.exe"], capture_output=True)
    subprocess.run(["taskkill", "/F", "/IM", "GamepadT9.exe"], capture_output=True)
    time.sleep(1)

    old = open(THEME, encoding="utf-8", errors="ignore").read() if os.path.exists(THEME) else None
    k9n_exists = os.path.exists(K9N)
    os.makedirs(CFG, exist_ok=True)
    open(THEME, "w").write("8")             # 小天使皮肤
    if k9n_exists:                          # 1.0.3 默认值 = 只显示字母, 先清掉开关文件
        os.remove(K9N)
    mark = logsize()

    print("启动:", EXE)
    p = subprocess.Popen([EXE], cwd=os.path.dirname(EXE))
    time.sleep(5)
    print("第一份 pid:", p.pid, "还活着:", p.poll() is None)

    print("起第二份实例触发唤醒...")
    subprocess.run([EXE], cwd=os.path.dirname(EXE), timeout=20)
    time.sleep(3)

    hwnd = u32.FindWindowW(None, "GamepadT9")
    print("窗口 hwnd:", hwnd)
    if hwnd:
        buf = ctypes.create_unicode_buffer(256)
        u32.GetWindowTextW(hwnd, buf, 256)
        print("窗口标题:", repr(buf.value))
        print("截图:", capture(hwnd, OUT))

    time.sleep(1)
    print("\n=== 本次日志 ===")
    txt = ""
    if os.path.exists(LOG):
        with open(LOG, "rb") as f:
            f.seek(mark)
            txt = f.read().decode("utf-8", "replace")
        print(txt[-3000:])

    print("\n=== 断言 ===")
    print("  九键数字默认隐藏:", "隐藏(只留字母)" in txt)
    print("  dict.txt 已加载:", "词" in txt or "dict" in txt.lower())
    print("  无未处理异常:", "Exception" not in txt and "异常" not in txt)

    subprocess.run(["taskkill", "/F", "/IM", "柄输.exe"], capture_output=True)
    if old is None:
        try:
            os.remove(THEME)
        except OSError:
            pass
    else:
        open(THEME, "w").write(old.strip())
    print("\n(主题已还原为 %r)" % old)


if __name__ == "__main__":
    main()
