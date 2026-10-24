# -*- coding: utf-8 -*-
"""找出正在跑的柄输是哪个目录下的 exe(不杀进程, 只看)。"""
import ctypes
import ctypes.wintypes as wt
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)

PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
k32.OpenProcess.restype = wt.HANDLE
psapi.GetModuleFileNameExW.argtypes = [wt.HANDLE, wt.HMODULE, ctypes.c_wchar_p, wt.DWORD]
psapi.GetModuleFileNameExW.restype = wt.DWORD
psapi.EnumProcesses.argtypes = [ctypes.POINTER(wt.DWORD), wt.DWORD, ctypes.POINTER(wt.DWORD)]
psapi.EnumProcesses.restype = wt.BOOL
k32.CloseHandle.argtypes = [wt.HANDLE]


def running():
    buf = (wt.DWORD * 4096)()
    need = wt.DWORD()
    if not psapi.EnumProcesses(buf, ctypes.sizeof(buf), ctypes.byref(need)):
        return []
    n = need.value // ctypes.sizeof(wt.DWORD)
    out = []
    for pid in buf[:n]:
        if not pid:
            continue
        h = k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if not h:
            continue
        try:
            p = ctypes.create_unicode_buffer(4096)
            if psapi.GetModuleFileNameExW(h, None, p, 4096):
                out.append((pid, p.value))
        finally:
            k32.CloseHandle(h)
    return out


hits = []
for pid, path in running():
    base = os.path.basename(path).lower()
    # 进程名可能因为单文件/编码显示成乱码, 按路径里的目录名和常见名去认
    if "柄输" in base or "gamepad" in base or "t9" in base or "柄输" in path or "gamepad" in path.lower():
        hits.append((pid, path))

if not hits:
    print("没有找到正在跑的柄输进程")
else:
    for pid, path in hits:
        print("pid=%-7d %s" % (pid, path))
