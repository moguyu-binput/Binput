"""真机冒烟: 跑一遍 GamepadT9.exe, 看日志里有没有异常 / 翻面有没有走通。

PowerShell 工具在这台机器上拿不到 stdout, 所以统一用 Python + ctypes 起进程。
"""
import os
import subprocess
import sys
import time

# 直接测发布出来的那份(跟用户拿到的是同一个 exe), 而不是 bin 里的中间产物
EXE = r"C:\Users\<你>\Binput-x64\GamepadT9.exe"
LOG = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9", "log.txt")


def run(args, secs, tag):
    if os.path.exists(LOG):
        os.remove(LOG)
    print(f"\n===== {tag}  args={args} =====")
    p = subprocess.Popen([EXE] + args)
    time.sleep(secs)
    p.terminate()
    try:
        p.wait(timeout=8)
    except subprocess.TimeoutExpired:
        p.kill()
        p.wait(timeout=8)
    time.sleep(0.3)
    if not os.path.exists(LOG):
        print("!! 没有产生日志, 进程可能根本没起来")
        return 1
    txt = open(LOG, encoding="utf-8", errors="replace").read()
    print(txt.strip())
    bad = [w for w in ("异常", "失败", "Exception", "NullReference", "错误") if w in txt]
    if bad:
        print("!! 日志里有可疑词: " + ", ".join(bad))
        return 1
    return 0


rc = 0
rc |= run(["--settings"], 6, "直出版 + 启动即翻到设置")
rc |= run(["--settings", "--preview"], 6, "预输入版 + 启动即翻到设置")
rc |= run(["--preview"], 4, "预输入版(键盘正面)")

print("\n结果:", "有问题" if rc else "三轮都没在日志里留下异常")
sys.exit(rc)
