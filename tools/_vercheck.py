# -*- coding: utf-8 -*-
"""读 exe 里的 VS_VERSION_INFO(直接用 Win32 API, 不走别的命令行工具)。"""
import ctypes, os, sys
sys.stdout.reconfigure(encoding="utf-8")
ver = ctypes.WinDLL("version")
ver.GetFileVersionInfoSizeW.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_uint32)]
ver.GetFileVersionInfoW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
ver.VerQueryValueW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p,
                               ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_uint32)]


def info(path):
    size = ctypes.c_uint32()
    if not ver.GetFileVersionInfoSizeW(path, ctypes.byref(size)):
        return None
    buf = ctypes.create_string_buffer(size.value)
    if not ver.GetFileVersionInfoW(path, 0, size.value, buf):
        return None
    ptr = ctypes.c_void_p()
    ln = ctypes.c_uint32()
    if ver.VerQueryValueW(buf, "\\", ctypes.byref(ptr), ctypes.byref(ln)):
        class FFI(ctypes.Structure):
            _fields_ = [("sig", ctypes.c_uint32), ("struc", ctypes.c_uint32),
                        ("struc2", ctypes.c_uint32), ("file", ctypes.c_uint32 * 4),
                        ("prod", ctypes.c_uint32 * 4)]
        f = ctypes.cast(ptr, ctypes.POINTER(FFI)).contents
        fv = "%d.%d.%d.%d" % (f.file[1], f.file[0], f.file[3], f.file[2])
        pv = "%d.%d.%d.%d" % (f.prod[1], f.prod[0], f.prod[3], f.prod[2])
        return fv, pv
    return None


ROOT = r"C:\Users\<你>\WorkBuddy"
for name in ("柄输-1.2.1-精简版", "柄输-1.2.1-完整版"):
    p = os.path.join(ROOT, name, "柄输.exe")
    r = info(p)
    print("%-18s FileVersion=%-10s ProductVersion=%-8s  %s"
          % (name, r[0], r[1], "OK" if r[1] == "1.2.1" else "!! 版本不对"))
