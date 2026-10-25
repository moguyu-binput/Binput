# -*- coding: utf-8 -*-
"""读 exe 里 VS_VERSION_INFO 的字符串版(ProductVersion 带 commit hash)。"""
import ctypes
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")

ver = ctypes.WinDLL("version")
ver.GetFileVersionInfoSizeW.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_uint32)]
ver.GetFileVersionInfoW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
ver.VerQueryValueW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p,
                               ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_uint32)]


def str_info(path):
    # 【踩过一次】GetFileVersionInfoSizeW 的第二个参数是 handle(不是大小),
    # 大小是**返回值**。照 size 参数去取会拿到 0, 后面全读空。
    handle = ctypes.c_uint32()
    size = ver.GetFileVersionInfoSizeW(path, ctypes.byref(handle))
    if not size:
        return None
    buf = ctypes.create_string_buffer(size)
    if not ver.GetFileVersionInfoW(path, 0, size, buf):
        return None
    # 语言/代码页表
    lp = ctypes.c_void_p()
    ln = ctypes.c_uint32()
    if not ver.VerQueryValueW(buf, r"\VarFileInfo\Translation", ctypes.byref(lp), ctypes.byref(ln)):
        return None
    n = ln.value // 4
    arr = ctypes.cast(lp, ctypes.POINTER(ctypes.c_uint16 * (n * 2))).contents
    lang, cp = arr[0], arr[1]
    out = {}
    for key in ("ProductVersion", "FileVersion", "ProductName"):
        q = r"\StringFileInfo\%04x%04x\%s" % (lang, cp, key)
        p = ctypes.c_void_p()
        l = ctypes.c_uint32()
        if ver.VerQueryValueW(buf, q, ctypes.byref(p), ctypes.byref(l)) and l.value:
            out[key] = ctypes.wstring_at(p, l.value - 1)
    return out


ROOT = r"D:\柄输-发布包"
WANT_COMMIT = "4984906"
for name in ("柄输-2.3.0-精简版", "柄输-2.3.0-完整版"):
    p = os.path.join(ROOT, name, "柄输.exe")
    d = str_info(p) or {}
    pv = d.get("ProductVersion", "(读不到)")
    fv = d.get("FileVersion", "(读不到)")
    # csproj 没设 Version, ProductVersion 一直是 "1.0.0+<hash>" —— 只对 hash,
    # hash 对得上就说明这份 exe 是从当前存档点烧出来的。
    ok = WANT_COMMIT in pv
    print("%-22s FileVersion=%-10s ProductVersion=%-55s %s"
          % (name, fv, pv, "OK" if ok else "!! 版本不对"))
