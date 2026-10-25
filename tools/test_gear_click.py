"""验证右下角齿轮可被点击翻面, 返回箭头可翻回。"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import time
import sys

u = ctypes.WinDLL("user32", use_last_error=True)
EnumWindowsProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)

EXE = r"C:\Users\<你>\Binput\bin\Release\net8.0-windows\GamepadT9.exe"
LOG = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9", "log.txt")


def hwnds_of(pid):
    out = []
    def cb(h, _l):
        p = wt.DWORD()
        u.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and u.IsWindowVisible(h):
            out.append(h)
        return True
    u.EnumWindows(EnumWindowsProc(cb), 0)
    return out


def click(x, y):
    u.SetCursorPos(int(x), int(y))
    time.sleep(0.05)
    INPUT_MOUSE = 0
    class MOUSEINPUT(ctypes.Structure):
        _fields_ = [("dx", ctypes.c_long), ("dy", ctypes.c_long),
                    ("mouseData", ctypes.c_uint32), ("dwFlags", ctypes.c_uint32),
                    ("time", ctypes.c_uint32), ("dwExtraInfo", ctypes.c_size_t)]
    class INPUT(ctypes.Structure):
        _fields_ = [("type", ctypes.c_uint32), ("mi", MOUSEINPUT)]
    def send(f):
        inp = INPUT(); inp.type = INPUT_MOUSE; inp.mi = MOUSEINPUT(0, 0, 0, f, 0, 0)
        u.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))
    send(0x0002)  # LBUTTONDOWN
    time.sleep(0.05)
    send(0x0004)  # LBUTTONUP
    time.sleep(0.05)


def read_log():
    if not os.path.exists(LOG):
        return ""
    return open(LOG, encoding="utf-8", errors="replace").read()


def wait_for_log(text, timeout=3.0):
    start = time.time()
    while time.time() - start < timeout:
        if text in read_log():
            return True
        time.sleep(0.1)
    return False


def main():
    if os.path.exists(LOG):
        os.remove(LOG)
    p = subprocess.Popen([EXE, "--debug", "--game"])
    try:
        time.sleep(2.5)
        hs = hwnds_of(p.pid)
        if not hs:
            print("!! 没找到窗口")
            return 1
        hwnd = hs[0]
        r = wt.RECT()
        u.GetWindowRect(hwnd, ctypes.byref(r))
        print(f"窗口 rect: ({r.left}, {r.top}) {r.right - r.left}x{r.bottom - r.top}")

        # 齿轮中心: 右下角往里 12px 左右(状态栏高 26, 按钮 22x22)
        gx = r.right - 14
        gy = r.bottom - 14
        print(f"点击齿轮 {gx},{gy}")
        click(gx, gy)
        if wait_for_log("翻面 -> 设置", 3):
            print("OK 齿轮点击翻面成功")
        else:
            print("!! 没翻到设置, 最近日志:")
            print(read_log()[-800:])
            return 1

        # 点返回箭头, 位置基本和齿轮一样
        print(f"点击返回箭头 {gx},{gy}")
        click(gx, gy)
        if wait_for_log("翻面 -> 键盘", 3):
            print("OK 返回箭头翻回成功")
        else:
            print("!! 没翻回键盘")
            return 1
        return 0
    finally:
        p.terminate()
        try:
            p.wait(timeout=8)
        except subprocess.TimeoutExpired:
            p.kill()
            p.wait(timeout=8)


if __name__ == "__main__":
    sys.exit(main())
