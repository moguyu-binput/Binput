# -*- coding: utf-8 -*-
r"""2.0.0-beta 发布产物冒烟 —— 唤醒法, 不碰用户正在跑的正式版。

要点:
- 直接测**发布目录里的 exe**(不是 bin\Release), 测的就是要发出去的那个文件。
- 用技能清单第 5 条的唤醒法: 起第一份 → 起第二份(它发唤醒消息后自己退出)
  → 验第一份还活着。全程只操作自己起的 pid, **绝不 taskkill 柄输.exe**
  (用户机器上可能正开着正式版, 杀掉是在毁他正在做的事)。
- 不用任何调试参数(--debug/--nosingle/--demo/--shot 已在 2.0.0-beta 里删干净)。
"""
import ctypes
import ctypes.wintypes as wt
import os
import re
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"C:\Users\<你>\WorkBuddy"
# 两个包都要各自冒一遍(以前只测精简版, 完整版其实是坏的, 出了好几版没发现):
#   python smoke_122.py                -> 精简版
#   python smoke_122.py 柄输-2.0.0-beta-完整版
EXE = os.path.join(ROOT, sys.argv[1] if len(sys.argv) > 1 else "柄输-2.0.0-beta-精简版", "柄输.exe")
OUT = os.path.join(ROOT, "tools", "shots", "v120_smoke.png")
CFG = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9")
LOG = os.path.join(CFG, "log.txt")

u32 = ctypes.WinDLL("user32", use_last_error=True)
g32 = ctypes.WinDLL("gdi32", use_last_error=True)
u32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
u32.PrintWindow.argtypes = [wt.HWND, wt.HDC, ctypes.c_uint]
u32.PrintWindow.restype = ctypes.c_bool
u32.GetDC.restype = wt.HDC
# HDC 是 64 位句柄值, 不声明 argtypes 的话 ctypes 按 C int(32 位)传,
# 句柄高位一超过 2^31 就直接 OverflowError("int too long to convert")。
g32.CreateCompatibleDC.argtypes = [wt.HDC]
g32.CreateCompatibleDC.restype = wt.HDC
g32.CreateCompatibleBitmap.argtypes = [wt.HDC, ctypes.c_int, ctypes.c_int]
g32.CreateCompatibleBitmap.restype = wt.HBITMAP
g32.SelectObject.argtypes = [wt.HDC, wt.HGDIOBJ]
g32.DeleteObject.argtypes = [wt.HGDIOBJ]
g32.DeleteDC.argtypes = [wt.HDC]
u32.ReleaseDC.argtypes = [wt.HWND, wt.HDC]


def logsize():
    try:
        return os.path.getsize(LOG)
    except OSError:
        return 0


def winrect(hwnd):
    r = wt.RECT()
    u32.GetWindowRect(hwnd, ctypes.byref(r))
    return r.right - r.left, r.bottom - r.top


def capture(hwnd, path):
    """尽力抓一张图, 但**不要拿它当断言依据**。

    WPF 的透明窗用 PrintWindow(PW_RENDERFULLCONTENT) 抓出来常常是全黑或者
    只有上半截(技能第3 节记过这个坑)。所以这里只留作人工肉眼过一眼,
    真要可靠的界面截图得靠程序自己的 --shot=(RenderTargetBitmap 从视觉树渲染),
    而那个参数已经在 2.0.0-beta 里删掉了。冒烟只断言窗口存在、尺寸合理、日志无异常。
    """
    w, h = winrect(hwnd)
    if w <= 10 or h <= 10:
        return "窗口尺寸不对 %dx%d" % (w, h)
    r = wt.RECT()
    u32.GetWindowRect(hwnd, ctypes.byref(r))
    hdc = u32.GetDC(hwnd)
    mem = g32.CreateCompatibleDC(hdc)
    bmp = g32.CreateCompatibleBitmap(hdc, w, h)
    g32.SelectObject(mem, bmp)
    u32.PrintWindow(hwnd, mem, 2)

    class BIH(ctypes.Structure):
        _fields_ = [("biSize", ctypes.c_uint32), ("biWidth", ctypes.c_int),
                    ("biHeight", ctypes.c_int), ("biPlanes", ctypes.c_uint16),
                    ("biBitCount", ctypes.c_uint16), ("biCompression", ctypes.c_uint32),
                    ("biSizeImage", ctypes.c_uint32), ("biXPelsPerMeter", ctypes.c_int),
                    ("biYPelsPerMeter", ctypes.c_int), ("biClrUsed", ctypes.c_uint32),
                    ("biClrImportant", ctypes.c_uint32)]

    hdr = BIH()
    hdr.biSize = ctypes.sizeof(hdr)
    hdr.biWidth, hdr.biHeight = w, -h
    hdr.biPlanes, hdr.biBitCount = 1, 32
    buf = ctypes.create_string_buffer(w * h * 4)
    g32.GetDIBits.argtypes = [wt.HDC, wt.HBITMAP, ctypes.c_uint, ctypes.c_uint,
                              ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint]
    g32.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(hdr), 0)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        off = 14 + ctypes.sizeof(hdr)
        f.write(b"BM")
        f.write(ctypes.c_uint32(off + len(buf)))
        f.write(ctypes.c_uint32(0))
        f.write(ctypes.c_uint32(off))
        f.write(bytes(hdr))
        f.write(buf.raw)
    g32.DeleteObject(bmp); g32.DeleteDC(mem); u32.ReleaseDC(hwnd, hdc)
    return "OK %dx%d -> %d 字节" % (w, h, os.path.getsize(path))


def main():
    mark = logsize()
    print("第一份:", EXE)
    p1 = subprocess.Popen([EXE], cwd=os.path.dirname(EXE))
    time.sleep(7)
    print("  还活着:", p1.poll() is None)
    if p1.poll() is not None:
        # 机器上已经有一份在跑(常常是用户自己开的正式版): 咱们这份会退化成
        # "发唤醒消息就退出", 后面量到的窗口和日志全是**别人**的, 断言没意义。
        # 这种情况直接停 —— 顺便也不能去杀那个已经在跑的实例。
        print("\n!! 已经有一份柄输在跑了, 这一份只是把它唤醒就退出了。")
        print("!! 冒烟需要独占互斥锁: 先退出正在跑的那份(托盘右键 → 退出)再跑。")
        return 2

    # 唤醒法: 第二份应该发完唤醒消息就自己退, 不开出第二个托盘图标
    print("第二份(应当发完唤醒就退出):")
    p2 = subprocess.Popen([EXE], cwd=os.path.dirname(EXE))
    p2.wait(timeout=30)
    print("  第二份退出码:", p2.returncode, "(0 = 正常让位给第一份)")

    hwnd = None
    # 窗口标题是内部名 GamepadT9(日志里"焦点=GamepadT9 [柄输.exe]"那行),
    # 不是显示名"柄输" —— 按后者 FindWindow 永远找不到。
    for _ in range(20):
        hwnd = u32.FindWindowW(None, "GamepadT9")
        if hwnd:
            break
        time.sleep(0.5)
    print("窗口 hwnd:", hwnd)
    size = (0, 0)
    if hwnd:
        size = winrect(hwnd)
        print("窗口尺寸: %dx%d" % size)
        print("截图(仅供肉眼参考, 不可靠):", capture(hwnd, OUT))

    print("第一份被唤醒后还活着:", p1.poll() is None)

    time.sleep(1)
    # 只结束自己起的这两个 pid, 不 taskkill 任何别的东西
    for p in (p1,):
        if p.poll() is None:
            p.terminate()
            try:
                p.wait(timeout=10)
            except subprocess.TimeoutExpired:
                p.kill()

    txt = ""
    if os.path.exists(LOG):
        # 【踩过一次】日志是**每次启动就截断重写**的, 所以启动之后它可能比出发前
        # 量到的 mark 还小。那样还从 mark 往后读就是读到文件尾之后 —— 一个字都没有,
        # 于是"dict 加载 / 唤出成功 / 九键只留字母"三条全部假失败(日志里其实都有)。
        # 变小了就从头读: 这时候整份日志本来就是这一次的。
        cur = logsize()
        start = mark if cur >= mark else 0
        with open(LOG, "rb") as f:
            f.seek(start)
            txt = f.read().decode("utf-8", "replace")

    m = re.search(r"拼音表加载 (\d+) 条", txt)
    print("\n=== 断言 ===")
    print("  进程没崩:", p1.returncode is not None)
    print("  只开一份(第二份让位):", p2.returncode == 0)
    print("  窗口画出来了:", bool(hwnd))
    print("  窗口尺寸合理:", size[0] > 200 and size[1] > 200, "(%dx%d)" % size)
    print("  界面唤出过:", "顶前台 成功" in txt)
    print("  dict.txt 已加载:", bool(m) and int(m.group(1)) > 40000,
          "(%s 条)" % (m.group(1) if m else "没读到"))
    print("  九键默认只留字母:", "隐藏(只留字母)" in txt)
    print("  skin 贴图在包里:", os.path.isdir(os.path.join(os.path.dirname(EXE), "skin")))
    print("  无异常:", "Exception" not in txt and "异常" not in txt)
    print("\n=== 本次日志 ===")
    print(txt[-1500:])


if __name__ == "__main__":
    sys.exit(main() or 0)