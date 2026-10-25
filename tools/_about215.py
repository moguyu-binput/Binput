# -*- coding: utf-8 -*-
"""把 App.About() 那段说明砍短, 换成用户新定的那一版。

【为什么用脚本改而不是手改】App.xaml.cs 是 CRLF, 手改很容易把某几行写成 LF
(2.1.2 那次就因为一个多余的 \\r 让 make_dist 回写出空行, 工作树当场变脏)。
这里按**行**替换: 读进来按 \\r\\n 切, 换掉中间那一段, 再按 \\r\\n 拼回去,
最后断言全文没有 \\r\\r\\n 也没有裸 \\n。
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
P = r"C:\Users\<你>\Binput\App.xaml.cs"

# C# 源码里的字面量 \\n 要原样留在文件里 —— 所以这一整块用 raw 字符串写。
NEW = r'''        return
            "柄输 Binput — 手柄九键输入法\n" +
            $"当前版本: {(_preview ? "预输入版" : "直出版")}（设置里可切换）\n" +
            $"拼音表: {dict}\n" +
            $"输入习惯: {T9Engine.UseCount} 条（挑中并按 RB 发出的切分会记下来，下次自动排前）\n" +
            "存盘时已加密（绑你的 Windows 账号，双击打开是乱码）\n" +
            $"程序目录: {AppContext.BaseDirectory}\n" +
            $"数据目录: {Config.Dir}\n" +
            $"日志: {Config.LogFile}\n\n" +
            "制作人：莫古雨\n" +
            "主页：https://space.bilibili.com/45454732\n" +
            "献给所有手柄玩家";'''

src = io.open(P, encoding="utf-8", newline="").read()
lines = src.split("\r\n")

a = next(i for i, l in enumerate(lines) if '柄输 Binput — 手柄九键输入法' in l and l.strip().startswith('"'))
b = next(i for i, l in enumerate(lines) if l.strip() == '"所有设置改动立刻生效并自动保存";')
print("替换第 %d~%d 行(共 %d 行 -> %d 行)" % (a + 1, b + 1, b - a + 1, len(NEW.split("\n"))))

out = lines[:a] + NEW.split("\n") + lines[b + 1:]
txt = "\r\n".join(out)

assert "\r\r\n" not in txt, "出现了 \\r\\r\\n"
assert txt.count("\r\n") == len(out) - 1, "有裸 \\n(CRLF=%d, 行数=%d)" % (txt.count("\r\n"), len(out))

io.open(P, "w", encoding="utf-8", newline="").write(txt)
print("CRLF=%d  CRCRLF=%d  裸LF=%d" % (txt.count("\r\n"), txt.count("\r\r\n"),
                                       txt.count("\n") - txt.count("\r\n")))
io.open(P, encoding="utf-8", newline="").read()
