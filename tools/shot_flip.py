"""真机截图: 把界面正面(带齿轮)和背面(设置)各截一张 PNG。

没有 Pillow, 所以自己用 ctypes + zlib 拼 PNG。
PowerShell 工具在这台机器上拿不到 stdout, 统一走 Python。
"""
import ctypes
import ctypes.wintypes as wt
import os
import struct
import subprocess
import sys
import time
import zlib

EXE = r"C:\Users\<你>\Binput\bin\Release\net8.0-windows\GamepadT9.exe"
OUT = r"C:\Users\<你>\Binput\tools\shots"
os.makedirs(OUT, exist_ok=True)

u = ctypes.WinDLL("user32", use_last_error=True)
g = ctypes.WinDLL("gdi32", use_last_error=True)

EnumWindowsProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)


def hwnds_of(pid):
    out = []

    def cb(h, _l):
        p = wt.DWORD()
        u.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and u.IsWindowVisible(h):
            n = u.GetWindowTextLengthW(h)
            buf = ctypes.create_unicode_buffer(n + 1)
            u.GetWindowTextW(h, buf, n + 1)
            out.append((h, buf.value))
        return True

    u.EnumWindows(EnumWindowsProc(cb), 0)
    return out


class BMIHeader(ctypes.Structure):
    _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int32),
                ("biHeight", ctypes.c_int32), ("biPlanes", ctypes.c_uint16),
                ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int32),
                ("biYPelsPerMeter", ctypes.c_int32), ("biClrUsed", ctypes.c_uint32),
                ("biClrImportant", ctypes.c_uint32)]


class BMI(ctypes.Structure):
    _fields_ = [("bmiHeader", BMIHeader), ("bmiColors", ctypes.c_uint32 * 3)]


def capture(hwnd, path):
    r = wt.RECT()
    u.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    if w < 10 or h < 10:
        return False, f"窗口太小 {w}x{h}"

    screen = u.GetDC(0)
    mem = g.CreateCompatibleDC(screen)
    bmp = g.CreateCompatibleBitmap(screen, w, h)
    g.SelectObject(mem, bmp)

    # 对 WPF AllowsTransparency/Topmost 窗口, PrintWindow 经常只抓到残影或一小块,
    # 而屏幕 DC BitBlt 在窗口可见/前景时最稳。
    SRCCOPY = 0x00CC0020
    g.BitBlt(mem, 0, 0, w, h, screen, r.left, r.top, SRCCOPY)

    bi = BMI()
    bi.bmiHeader.biSize = ctypes.sizeof(BMIHeader)
    bi.bmiHeader.biWidth = w
    bi.bmiHeader.biHeight = -h            # 负 = 自上而下, 省得后面翻转
    bi.bmiHeader.biPlanes = 1
    bi.bmiHeader.biBitCount = 32
    bi.bmiHeader.biCompression = 0
    buf = (ctypes.c_ubyte * (w * h * 4))()
    n = g.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(bi), 0)

    g.DeleteObject(bmp)
    g.DeleteDC(mem)
    u.ReleaseDC(0, screen)
    if n != h:
        return False, f"GetDIBits 只回了 {n} 行"

    rows = []
    off = 0
    for _ in range(h):
        row = bytearray(w * 3)
        for x in range(w):
            b, gg, rr = buf[off], buf[off + 1], buf[off + 2]
            row[x * 3:x * 3 + 3] = bytes((rr, gg, b))
            off += 4
        rows.append(bytes(row))
    write_png(path, w, h, rows)
    return True, f"{w}x{h}"


def write_png(path, w, h, rows):
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))
    open(path, "wb").write(png)


def shot(args, secs, name):
    p = subprocess.Popen([EXE] + args)
    time.sleep(secs)
    try:
        hs = hwnds_of(p.pid)
        hs = [(h, t) for h, t in hs if t == "GamepadT9"]
        if not hs:
            print(f"!! {name}: 没找到窗口 (候选={hwnds_of(p.pid)})")
            return 1
        ok, info = capture(hs[0][0], os.path.join(OUT, name + ".png"))
        print(f"{'OK ' if ok else '!! '}{name}: {info}")
        return 0 if ok else 1
    finally:
        p.terminate()
        try:
            p.wait(timeout=8)
        except subprocess.TimeoutExpired:
            p.kill()
            p.wait(timeout=8)


rc = 0
rc |= shot(["--debug", "--game"], 5, "front")
rc |= shot(["--debug", "--game", "--settings"], 5, "back")
print("\n截图目录:", OUT)
sys.exit(rc)
