# -*- coding: utf-8 -*-
"""
mineprobe 跟进: 学习记录的键是**带撇的切法**, 不再是不带撇的连写。
探测的动作不变(照旧按 Y 三次、照旧收起), 只是"读回来"时用新身份去查。
mineprobe/Program.cs 保持它原来的换行。
"""
import sys

P = r"C:\Users\<你>\Binput\tools\mineprobe\Program.cs"
b0 = open(P, "rb").read()
crlf = b0.count(b"\r\n") > 0
src = b0.decode("utf-8").replace("\r\n", "\n")
NL = "\r\n" if crlf else "\n"

done = []
def rep(old, new, tag, want=1):
    global src
    n = src.count(old)
    if n != want:
        print(f"!! [{tag}] 命中 {n} 次, 期望 {want} —— 中止")
        sys.exit(1)
    src = src.replace(old, new)
    done.append(tag)

rep('        int n = T9Engine.UseOf("paopaosao");',
    '        int n = T9Engine.UseOf("pao\'pao\'sao");', "场景1 UseOf")

rep('''                + $" 成词={T9Engine.HasPhrase(string.Concat(s))} "
                + $"我的={T9Engine.IsMine(string.Concat(s))}");''',
'''                + $" 成词={T9Engine.HasPhrase(string.Concat(s))} "
                + $"我的={T9Engine.IsMine(string.Join("'", s))}");''',
"IsMine 按切法")

rep('        bool has = c.Segs.Any(s => string.Concat(s) == "paopaosao");',
    '        bool has = c.Segs.Any(s => string.Join("\'", s) == "pao\'pao\'sao");',
"候选里有没有")

rep('''    int before = T9Engine.UseOf("paopaosao");
    c.DebugFlush();          // 收起: 数字已经发完, 没有"程序替你补发"这回事
    int after = T9Engine.UseOf("paopaosao");''',
'''    int before = T9Engine.UseOf("pao\'pao\'sao");
    c.DebugFlush();          // 收起: 数字已经发完, 没有"程序替你补发"这回事
    int after = T9Engine.UseOf("pao\'pao\'sao");''',
"场景2 before/after")

rep('''    int before = T9Engine.UseOf("paopaosao");
    c.DebugFlush();
    int after = T9Engine.UseOf("paopaosao");''',
'''    int before = T9Engine.UseOf("pao\'pao\'sao");
    c.DebugFlush();
    int after = T9Engine.UseOf("pao\'pao\'sao");''',
"场景3 before/after")

rep('    bool ok = c.Segs.Any(s => string.Concat(s) == "zhangsanfeng")',
    '    bool ok = c.Segs.Any(s => string.Join("\'", s) == "zhang\'san\'feng")',
"场景4")

open(P, "wb").write(src.replace("\n", NL).encode("utf-8"))
print("已写入,", ", ".join(done))
b = open(P, "rb").read()
print("CRLF=", b.count(b"\r\n"), "裸LF=", b.count(b"\n") - b.count(b"\r\n"), "CRCRLF=", b.count(b"\r\r\n"))
