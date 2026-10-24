# -*- coding: utf-8 -*-
"""2.1.7: 关于那段字 —— 三条路径并成两行(日志就在数据目录里, 不用单列一行)。"""
import io

P = r"C:\Users\<你>\Binput\App.xaml.cs"
s = io.open(P, encoding="utf-8", newline="").read()
assert "\r\r\n" not in s and s.count("\n") == s.count("\r\n"), "源换行不干净"
L = s.split("\r\n")

old = ["            $\"程序目录: {AppContext.BaseDirectory}\\n\" +",
       "            $\"数据目录: {Config.Dir}\\n\" +",
       "            $\"日志: {Config.LogFile}\\n\\n\" +"]
new = ["            $\"程序目录: {AppContext.BaseDirectory}\\n\" +",
       "            // 日志就在数据目录里(Config.LogFile = Dir\\log.txt), 不用再单列一行 ——",
       "            // 三条路径占的是六行里的三行, 并成两行之后整段才塞得进那一大格。",
       "            $\"数据目录: {Config.Dir}（日志同目录）\\n\\n\" +"]

i = None
for k in range(len(L) - len(old) + 1):
    if L[k:k + len(old)] == old:
        i = k
        break
if i is None:
    raise SystemExit("!! 找不到那三行路径")
L[i:i + len(old)] = new

io.open(P, "w", encoding="utf-8", newline="").write("\r\n".join(L))
t = "\r\n".join(L)
assert "\r\r\n" not in t and t.count("\n") == t.count("\r\n")
print("ok; CRLF=%d CRCRLF=%d bareLF=%d"
      % (t.count("\r\n"), t.count("\r\r\n"), t.count("\n") - t.count("\r\n")))
