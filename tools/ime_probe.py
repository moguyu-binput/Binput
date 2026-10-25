#!/usr/bin/env python
"""查"当前前台窗口"的系统输入法状态 —— 只读, 不打字, 不改任何东西。

系统输入法版的界面上要显示"现在是中文还是英文", 靠的就是同样的两个调用:
键盘布局 LANGID 看装没装中文输入法, IMM32 的 conversion mode 看此刻是中文还是英文模式。
这个脚本用 Python 把同样的调用跑一遍, 用来确认读出来的值是对的。

用法:
    python tools/ime_probe.py          # 采样 8 次, 每 0.6 秒一次
    python tools/ime_probe.py 20       # 采样 20 次

跑起来后手动切几个窗口(记事本 / 浏览器 / 游戏), 再按 Ctrl+Space 或 Win+Space
切中英文, 看输出跟着变, 就说明程序里的状态显示也是准的。
"""
import ctypes
import sys
import time
from ctypes import wintypes

u = ctypes.WinDLL("user32", use_last_error=True)
imm = ctypes.WinDLL("imm32", use_last_error=True)

IME_CMODE_NATIVE = 0x0001

u.GetForegroundWindow.restype = wintypes.HWND
u.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(ctypes.c_uint)]
u.GetKeyboardLayout.argtypes = [ctypes.c_uint]
u.GetKeyboardLayout.restype = ctypes.c_void_p
u.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
u.GetClassNameW.restype = ctypes.c_int

imm.ImmGetContext.argtypes = [wintypes.HWND]
imm.ImmGetContext.restype = ctypes.c_void_p
imm.ImmGetConversionStatus.argtypes = [ctypes.c_void_p,
                                       ctypes.POINTER(ctypes.c_uint),
                                       ctypes.POINTER(ctypes.c_uint)]
imm.ImmReleaseContext.argtypes = [wintypes.HWND, ctypes.c_void_p]

ZH = {0x0804: "zh-CN", 0x0404: "zh-TW", 0x0C04: "zh-HK", 0x1004: "zh-SG", 0x0004: "zh"}


def probe():
    hwnd = u.GetForegroundWindow()
    if not hwnd:
        return "(没有前台窗口)"

    cls = ctypes.create_unicode_buffer(256)
    u.GetClassNameW(hwnd, cls, 256)

    # 注意: 线程 ID 是返回值, out 参数拿到的是进程 ID —— 用反了会一直读到 0
    pid = ctypes.c_uint()
    tid = u.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    lang = (u.GetKeyboardLayout(tid) or 0) & 0xFFFF

    zh = None
    imc = imm.ImmGetContext(hwnd)
    if imc:
        try:
            conv, sent = ctypes.c_uint(), ctypes.c_uint()
            if imm.ImmGetConversionStatus(imc, ctypes.byref(conv), ctypes.byref(sent)):
                zh = bool(conv.value & IME_CMODE_NATIVE)
        finally:
            imm.ImmReleaseContext(hwnd, imc)

    name = ZH.get(lang, "?")
    return "%-28s lang=0x%04X(%s)  chinese=%s" % (cls.value, lang, name, zh)


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 8
    print("采样前台窗口的输入法状态, 切窗口 / 切中英看看会不会跟着变:")
    for i in range(n):
        print("  %2d  %s" % (i + 1, probe()))
        time.sleep(0.6)
    return 0


if __name__ == "__main__":
    sys.exit(main())
