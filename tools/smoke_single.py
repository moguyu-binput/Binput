"""真机冒烟: 双击第二次 exe 只能把已经跑着的那个唤出来, 不能开第二份。

判断依据:
  1) 第二个进程的退出码 / 存活时间 —— 它应该自己很快退出;
  2) 日志里出现"收到第二个实例的唤醒消息";
  3) 全程只有 1 个 GamepadT9.exe 在跑。
"""
import os
import subprocess
import sys
import time
import ctypes
from ctypes import wintypes

EXE = r"C:\Users\<你>\Binput\bin\Release\net8.0-windows\GamepadT9.exe"
LOG = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9", "log.txt")


def count_running(name="GamepadT9.exe"):
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {name}"],
                         capture_output=True, text=True, errors="replace").stdout
    return sum(1 for line in out.splitlines() if line.lower().startswith(name.lower()))


if os.path.exists(LOG):
    os.remove(LOG)

print("== 第一次启动 ==")
a = subprocess.Popen([EXE, "--game"])
time.sleep(4)
print("进程数:", count_running())

print("\n== 第二次启动(模拟再双击一次) ==")
b = subprocess.Popen([EXE, "--game"])
try:
    rc = b.wait(timeout=6)
    print(f"第二个进程已退出, 退出码={rc}")
    exited = True
except subprocess.TimeoutExpired:
    print("!! 第二个进程 6 秒后还活着 —— 说明没走单实例分支")
    exited = False
    b.kill()
time.sleep(1)

n = count_running()
print("进程数:", n)

txt = open(LOG, encoding="utf-8", errors="replace").read() if os.path.exists(LOG) else ""
print("\n----- 日志 -----")
print(txt.strip())

ok = True
if not exited:
    ok = False
    print("!! 第二个实例没自动退出")
if n != 1:
    ok = False
    print(f"!! 进程数是 {n}, 应该是 1")
if "收到第二个实例的唤醒消息" not in txt:
    ok = False
    print("!! 第一个实例没收到唤醒消息")
else:
    print("OK: 第一个实例收到了唤醒消息")

# 顺手再确认一下: 第一个还在正常活着(没被第二个搞崩)
if a.poll() is not None:
    ok = False
    print(f"!! 第一个实例挂了, 退出码={a.poll()}")
else:
    print("OK: 第一个实例还活着")

a.terminate()
try:
    a.wait(timeout=8)
except subprocess.TimeoutExpired:
    a.kill()
    a.wait(timeout=8)

print("\n结果:", "单实例 OK" if ok else "单实例有问题")
sys.exit(0 if ok else 1)
