# -*- coding: utf-8 -*-
"""2.0.0-beta: 两个包各真的启动一次, 并且**确认窗口是这个进程自己的**。

上一版栽在这里: 完整版根本起不来, 而冒烟只测了精简版, 出了好几版没人发现。
这一版又差点栽在另一头 —— 检测窗口时只看标题 "GamepadT9", 结果匹配到用户
机器上正在跑的 1.2.2, 明明是别人的窗口也算"起来了"。现在按 pid 认窗口。
"""
import ctypes, ctypes.wintypes as wt, os, subprocess, sys, time
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:/Users/<你>/WorkBuddy"
u32 = ctypes.WinDLL("user32", use_last_error=True)
u32.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.POINTER(wt.DWORD)]

def windows_of(pid):
    out = []
    @ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
    def cb(h, l):
        d = wt.DWORD()
        u32.GetWindowThreadProcessId(h, ctypes.byref(d))
        if d.value == pid and u32.IsWindowVisible(h):
            n = u32.GetWindowTextLengthW(h)
            b = ctypes.create_unicode_buffer(n + 1)
            u32.GetWindowTextW(h, b, n + 1)
            out.append(b.value)
        return True
    u32.EnumWindows(cb, 0)
    return out

ok = True
for name in ("柄输-2.0.0-beta2-精简版", "柄输-2.0.0-beta2-完整版"):
    d = os.path.join(ROOT, name)
    p = subprocess.Popen([os.path.join(d, "柄输.exe")], cwd=d)
    wins, alive, elapsed = [], False, None
    try:
        t0 = time.time()
        for _ in range(100):
            time.sleep(0.1)
            wins = windows_of(p.pid)
            if wins:
                elapsed = time.time() - t0
                break
        alive = p.poll() is None
        tag = f"{elapsed:.1f}s" if elapsed is not None else "没等到"
        print(f"{name}\n    自己的窗口={wins or '无'}  出现耗时={tag}  进程活着={alive}  退出码={p.poll()}")
        if not wins or not alive: ok = False
    finally:
        time.sleep(0.4); p.kill()
print("\n结论:", "两个包都真的起来了 ✅" if ok else "!! 有包起不来")
sys.exit(0 if ok else 1)
