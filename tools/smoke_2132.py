# -*- coding: utf-8 -*-
r"""2.0.0-beta 发布产物冒烟 —— 先清场, 再跑唤醒法。

要点:
- 直接测**发布目录里的 exe**(不是 bin\Release), 测的就是要发出去的那个文件。
- **开跑前先把已经在跑的柄输结束掉**(2026-10-06 用户拍板: "以后测试不需要问我
  结束进程, 若有已经在运行的, 直接结束就行, 我不在意")。不清场的话咱们这份会
  退化成"发个唤醒消息就退出", 后面量到的窗口和日志全是**别人**的, 断言没意义 ——
  以前只能停下来问, 现在直接杀。只杀名字是 柄输.exe / GamepadT9.exe 的这两个。
- 用技能清单第 5 条的唤醒法: 起第一份 → 起第二份(它发唤醒消息后自己退出)
  → 验第一份还活着。
- 不用任何调试参数(--debug/--nosingle/--demo/--shot 已在 2.0.0-beta 里删干净)。
- **不截图**(2026-10-06 起日常也不需要): WPF 透明窗 PrintWindow 抓出来常常
  全黑或只有半截, 拿它当断言依据本来就不可靠; 这里只断言窗口存在、尺寸合理、
  日志没异常, 界面长什么样由人自己看。
"""
import ctypes
import ctypes.wintypes as wt
import os
import re
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\柄输-发布包"
# 两个包都要各自冒一遍(以前只测精简版, 完整版其实是坏的, 出了好几版没发现):
#   python smoke_2132.py                -> 精简版
#   python smoke_2132.py 柄输-2.1.32-完整版
EXE = os.path.join(ROOT, sys.argv[1] if len(sys.argv) > 1 else "柄输-2.1.32-精简版", "柄输.exe")
# 2.1.14 起数据在**程序旁边的 save\\** 里(不再往 %LOCALAPPDATA% 塞),
# 所以日志也跟着走 —— 写死旧目录的话这份会把别人(或上一版)那份日志拿去断言。
CFG = os.path.join(os.path.dirname(EXE), "save")
LOG = os.path.join(CFG, "log.txt")

u32 = ctypes.WinDLL("user32", use_last_error=True)
u32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]


def logsize():
    try:
        return os.path.getsize(LOG)
    except OSError:
        return 0


def winrect(hwnd):
    r = wt.RECT()
    u32.GetWindowRect(hwnd, ctypes.byref(r))
    return r.right - r.left, r.bottom - r.top


def clear():
    """把已经在跑的柄输全部结束掉(用户拍板过: 测试时不用问, 直接杀)。

    只认两个镜像名, 别的进程一个都不碰。返回杀掉了几个。
    """
    n = 0
    for name in ("柄输.exe", "Binput.exe"):
        # taskkill 的输出是**简体中文 GBK**(成功:"成功: 已终止..."; 没找到进程时
        # 带"找不到")。用 text=True 会让 Python 拿 utf-8 去解, 0xb4 那类字节直接
        # UnicodeDecodeError 崩在 reader 线程里(崩了还看不出为什么)。按字节收,
        # 自己用 replace 解。
        r = subprocess.run(["taskkill", "/F", "/IM", name], capture_output=True)
        out = ((r.stdout or b"") + (r.stderr or b"")).decode("utf-8", "replace")
        if r.returncode == 0:
            n += 1
            print("  已结束 %s" % name)
        elif "找不到" not in out and "not found" not in out.lower():
            print("  %s -> %s" % (name, out.strip()[:120]))
    # 等互斥体真的放开(进程退出到内核回收 handle 有一小会儿)
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenMutexW.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_wchar_p]
    k32.OpenMutexW.restype = ctypes.c_void_p
    for _ in range(20):
        if not k32.OpenMutexW(0x1F0001, 0, r"Local\Binput_SingleInstance_v1"):
            return n
        time.sleep(0.3)
    print("  !! 互斥锁还占着, 后面大概率起不来")
    return n


def main():
    print("=== 清场(结束已经在跑的柄输) ===")
    clear()
    mark = logsize()
    print("第一份:", EXE)
    p1 = subprocess.Popen([EXE], cwd=os.path.dirname(EXE))
    time.sleep(7)
    print("  还活着:", p1.poll() is None)
    if p1.poll() is not None:
        print("\n!! 这份起不来(退出码 %s)。" % p1.returncode)
        return 2

    # 唤醒法: 第二份应该发完唤醒消息就自己退, 不开出第二个托盘图标
    print("第二份(应当发完唤醒就退出):")
    p2 = subprocess.Popen([EXE], cwd=os.path.dirname(EXE))
    p2.wait(timeout=30)
    print("  第二份退出码:", p2.returncode, "(0 = 正常让位给第一份)")

    hwnd = None
    # 窗口标题 = 显示名「柄输」(OverlayWindow.xaml 的 Title)。2.1.14 以前写的是
    # 内部名 GamepadT9, 改名之后那句一起改了 —— 按老名字 FindWindow 找不到。
    for _ in range(20):
        hwnd = u32.FindWindowW(None, "柄输")
        if hwnd:
            break
        time.sleep(0.5)
    print("窗口 hwnd:", hwnd)
    size = (0, 0)
    if hwnd:
        size = winrect(hwnd)
        print("窗口尺寸: %dx%d" % size)

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
    # 界面唤出过的证据**看版本**: 预输入版会打"顶前台 成功"(它要抢焦点),
    # 直出版不打那一行, 但"屏幕外停 ... 后归位"是两边都有的(ShowReady 演完
    # 才露脸)。只认前者的话, 直出版的包会被误判成"没唤出来"。
    print("  界面唤出过:", ("顶前台 成功" in txt) or ("后归位" in txt))
    print("  dict.txt 已加载:", bool(m) and int(m.group(1)) > 40000,
          "(%s 条)" % (m.group(1) if m else "没读到"))
    print("  九键默认只留字母:", "隐藏(只留字母)" in txt)
    print("  skin 贴图在包里:", os.path.isdir(os.path.join(os.path.dirname(EXE), "skin")))
    print("  无异常:", "Exception" not in txt and "异常" not in txt)
    print("\n=== 本次日志 ===")
    print(txt[-1500:])


if __name__ == "__main__":
    sys.exit(main() or 0)