# -*- coding: utf-8 -*-
import ctypes, ctypes.wintypes as wt, sys
sys.stdout.reconfigure(encoding="utf-8")
u32 = ctypes.WinDLL("user32", use_last_error=True)
u32.GetWindowThreadProcessId.argtypes=[wt.HWND, ctypes.POINTER(ctypes.c_uint)]
u32.GetWindowThreadProcessId.restype=ctypes.c_uint
u32.GetWindowTextW.argtypes=[wt.HWND, ctypes.c_wchar_p, ctypes.c_int]
u32.IsWindowVisible.argtypes=[wt.HWND]
u32.IsWindowVisible.restype=ctypes.c_bool
res=[]
WNDENUMPROC=ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, ctypes.c_void_p)
def cb(h, l):
    if u32.IsWindowVisible(h):
        n=ctypes.create_unicode_buffer(256)
        u32.GetWindowTextW(h, n, 256)
        pid=ctypes.c_uint()
        u32.GetWindowThreadProcessId(h, ctypes.byref(pid))
        res.append((h, pid.value, n.value))
    return True
u32.EnumWindows(WNDENUMPROC(cb), 0)
k32=ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.argtypes=[ctypes.c_uint32, ctypes.c_bool, ctypes.c_uint]
k32.OpenProcess.restype=wt.HANDLE
k32.QueryFullProcessImageNameW.argtypes=[wt.HANDLE, ctypes.c_uint32, ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_uint32)]
k32.QueryFullProcessImageNameW.restype=ctypes.c_bool
k32.CloseHandle.argtypes=[wt.HANDLE]
seen={}
for h,pid,t in res:
    if pid in seen: continue
    name=""
    hp=k32.OpenProcess(0x1000, False, pid)
    if hp:
        buf=ctypes.create_unicode_buffer(1024); sz=ctypes.c_uint32(1024)
        if k32.QueryFullProcessImageNameW(hp, 0, buf, ctypes.byref(sz)): name=buf.value
        k32.CloseHandle(hp)
    seen[pid]=(name,t)
targets=[(h,pid,t) for h,pid,t in res if t=="GamepadT9"]
print("=== 标题为 GamepadT9 的窗口 ===")
for h,pid,t in targets:
    print("  hwnd=0x%X pid=%d path=%s" % (h, pid, seen.get(pid,("?",""))[0]))
print("\n=== 路径含 柄输/GamepadT9 的进程 ===")
for pid,(name,t) in seen.items():
    if "柄输" in name or "GamepadT9" in name:
        print("  pid=%d %s  (窗口: %s)" % (pid, name, t))
