# -*- coding: utf-8 -*-
"""
真机冒烟: 启动 --debug --game, 用 keybd_event 模拟手柄键盘, 走一遍
"打数字 -> LB/RB 挑拼音 -> A 发给输入法 -> Y 只定住第一个字",
然后打印本次新增的应用日志。

键位映射(见 DebugPad.cs):
  I J K L = 左摇杆(上/左/下/右)    方向键 = 十字键
  空格 = A   Esc = B   [ ] = LB/RB   Q = LT   E = RT   Z+C = 唤出
"""
import ctypes, os, subprocess, sys, time
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"C:\Users\<你>\WorkBuddy"
EXE = os.path.join(ROOT, "GamepadT9-Game-x64-selfcontained", "GamepadT9.exe")
LOG = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9", "log.txt")
MODE = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9", "mode.txt")

u32 = ctypes.WinDLL("user32", use_last_error=True)
u32.keybd_event.argtypes = [ctypes.c_uint8, ctypes.c_uint8, wintypes.DWORD, ctypes.c_void_p]
u32.keybd_event.restype = None
u32.GetForegroundWindow.restype = wintypes.HWND
u32.GetWindowTextW.argtypes = [wintypes.HWND, ctypes.c_wchar_p, ctypes.c_int]

KEYEVENTF_KEYUP = 0x0002
VK = dict(A=0x20, B=0x1B, LB=0xDB, RB=0xDD, UP=0x26, Y=0x59,
          I=0x49, J=0x4A, K=0x4B, L=0x4C)


def down(vk):
    u32.keybd_event(vk, 0, 0, None)
    time.sleep(0.05)


def up(vk):
    u32.keybd_event(vk, 0, KEYEVENTF_KEYUP, None)
    time.sleep(0.05)


def tap(vk):
    down(vk)
    up(vk)


def press9(n):
    """左摇杆推到九宫格 n, 再按 A"""
    col, row = (n - 1) % 3, (n - 1) // 3
    xs = [VK["J"]] if col == 0 else ([VK["L"]] if col == 2 else [])
    ys = [VK["I"]] if row == 0 else ([VK["K"]] if row == 2 else [])
    for k in xs + ys:
        down(k)
    time.sleep(0.22)
    tap(VK["A"])
    for k in xs + ys:
        up(k)
    time.sleep(0.14)


def fg_title():
    h = u32.GetForegroundWindow()
    buf = ctypes.create_unicode_buffer(256)
    u32.GetWindowTextW(h, buf, 256)
    return buf.value


def log_size():
    try:
        return os.path.getsize(LOG)
    except OSError:
        return 0


def main():
    os.makedirs(os.path.dirname(MODE), exist_ok=True)
    with open(MODE, "w", encoding="ascii") as f:
        f.write("1 0")            # 游戏模式开 + 仅复制
    mark = log_size()

    p = subprocess.Popen([EXE, "--debug", "--game"], cwd=os.path.dirname(EXE))
    time.sleep(6)
    print("启动完成, 前台:", fg_title())

    print("--- 打 7468326 (想要 shou'dao) ---")
    for d in (7, 4, 6, 8, 3, 2, 6):
        press9(d)

    print("--- RB 换下一个切分 ---")
    tap(VK["RB"]); time.sleep(0.3)
    print("--- LB 换回来 ---")
    tap(VK["LB"]); time.sleep(0.3)
    print("--- A 整段发给输入法 ---")
    tap(VK["A"]); time.sleep(0.7)

    print("--- 再打 7468326, 用 Y 只定住第一个字 ---")
    for d in (7, 4, 6, 8, 3, 2, 6):
        press9(d)
    tap(VK["Y"]); time.sleep(0.5)

    time.sleep(2)
    print("=== 本次新增日志 ===")
    with open(LOG, "rb") as f:
        f.seek(mark)
        print(f.read().decode("utf-8", "replace"))

    p.terminate()
    try:
        p.wait(5)
    except subprocess.TimeoutExpired:
        p.kill()
    subprocess.run(["taskkill", "/F", "/IM", "GamepadT9.exe"],
                   capture_output=True)


if __name__ == "__main__":
    main()
