# -*- coding: utf-8 -*-
"""两个包各自的 exe 都要**真的启动一次**才算数。

以前冒烟只测精简版, 完整版(自包含单文件)其实一启动就 fast fail 退出,
我们出了一版又一版都没发现。这里补上: 起进程 -> 等窗口 -> 看日志 -> 收掉。
"""
import ctypes, ctypes.wintypes as wt, os, subprocess, sys, time
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Users\<你>\WorkBuddy"
LOG = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9", "log.txt")
u32 = ctypes.WinDLL("user32", use_last_error=True)
u32.SetProcessDPIAware()
u32.FindWindowW.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
u32.FindWindowW.restype = wt.HWND

bad = 0
for folder in ("柄输-1.2.2-精简版", "柄输-1.2.2-完整版"):
    exe = os.path.join(ROOT, folder, "柄输.exe")
    print("== %s ==" % folder)
    if os.path.exists(LOG):
        os.remove(LOG)                      # 日志每次启动会重建, 先清掉好判断
    p = subprocess.Popen([exe], stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd=os.path.dirname(exe))
    waited = 0.0
    try:
        while waited < 45:
            time.sleep(0.5); waited += 0.5
            if u32.FindWindowW(None, "GamepadT9"):
                break
        code = p.poll()
        alive = code is None
        print("  窗口:", "出现了" if u32.FindWindowW(None, "GamepadT9") else "没出现",
              "| 用时 %.1f 秒" % waited)
        print("  进程:", "活着(正常)" if alive else "已经退出, code=%s" % code)
        if not alive:
            out, err = p.communicate(timeout=10)
            e = (err or b"").decode("utf-8", "replace")[:300]
            if e.strip():
                print("  stderr:", e.replace("\n", "\n          "))
            bad += 1
        time.sleep(1.0)
        txt = open(LOG, encoding="utf-8", errors="replace").read() if os.path.exists(LOG) else ""
        head = [l for l in txt.split("\n") if "启动" in l][:1]
        print("  日志:", head[0].strip() if head else "(没有启动记录)")
        if not head:
            bad += 1
    finally:
        try: p.kill()
        except Exception: pass
        time.sleep(0.6)

print("\n结论:", "两个包都能正常启动 ✅" if bad == 0 else "有 %d 处不对 ❌" % bad)
sys.exit(0 if bad == 0 else 1)
