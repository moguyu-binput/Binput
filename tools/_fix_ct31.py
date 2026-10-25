# -*- coding: utf-8 -*-
"""
coretest 跟进新契约: 学习和排序的键是**带撇的切法**, 不再是连写。
  1) Case20 / Case30 里手工 BumpUse 的地方改传切法键
  2) Case31_Dbg 换成正式回归用例【31】
coretest/Program.cs 是 LF。
"""
import sys

P = r"C:\Users\<你>\Binput\tools\coretest\Program.cs"
src = open(P, "rb").read().decode("utf-8").replace("\r\n", "\n")

done = []
def rep(old, new, tag, want=1):
    global src
    n = src.count(old)
    if n != want:
        print(f"!! [{tag}] 命中 {n} 次, 期望 {want} 次 —— 中止")
        print(repr(old[:300]))
        sys.exit(1)
    src = src.replace(old, new)
    done.append(tag)

L = lambda xs: "\n".join(xs)

# ---------- Case20: 手工学的那条必须按切法身份记 ----------
rep(
"""        string first = string.Concat(c.Segs[0]);
        double top = T9Engine.Rank(c.Segs[0]);""",
L([
"        // 键是**带撇的切法**(\"cidao\"), 不是去撇的连写 —— T9Engine.BumpUse 按这个认人",
"        string first = string.Join(\"'\", c.Segs[0]);",
"        double top = T9Engine.Rank(c.Segs[0]);",
]),
"Case20 first")

rep(
"""        string pick = string.Concat(c.Segs[at]);
        CheckTrue("挑的不是本来就排第一的那条", pick != first);""",
L([
"        string pick = string.Join(\"'\", c.Segs[at]);",
"        CheckTrue(\"挑的不是本来就排第一的那条\", pick != first);",
]),
"Case20 pick")

rep(
"""        Check("学过的那条排到了第一", string.Concat(c2.Segs[0]), pick);
        CheckTrue("原来那条没丢, 只是往后挪了",
                  c2.Segs.Any(s => string.Concat(s) == first));""",
L([
"        Check(\"学过的那条排到了第一\", string.Join(\"'\", c2.Segs[0]), pick);",
"        CheckTrue(\"原来那条没丢, 只是往后挪了\",",
"                  c2.Segs.Any(s => string.Join(\"'\", s) == first));",
]),
"Case20 断言1")

rep(
"""        string top2 = string.Concat(c4.Segs[0]);
        string alt = string.Concat(c4.Segs[c4.Segs.Count - 1]);""",
L([
"        string top2 = string.Join(\"'\", c4.Segs[0]);",
"        string alt = string.Join(\"'\", c4.Segs[c4.Segs.Count - 1]);",
]),
"Case20 top2/alt")

rep(
"""        Check("反复挑的那条能翻过整串成词的那条", string.Concat(c5.Segs[0]), alt);
        CheckTrue("被翻过去的那条没消失, 只是往后挪了",
                  c5.Segs.Any(s => string.Concat(s) == top2));""",
L([
"        Check(\"反复挑的那条能翻过整串成词的那条\", string.Join(\"'\", c5.Segs[0]), alt);",
"        CheckTrue(\"被翻过去的那条没消失, 只是往后挪了\",",
"                  c5.Segs.Any(s => string.Join(\"'\", s) == top2));",
]),
"Case20 断言2")

rep(
"""        Check("清除后排序回到原样", string.Concat(c3.Segs[0]), first);""",
L([
"        Check(\"清除后排序回到原样\", string.Join(\"'\", c3.Segs[0]), first);",
]),
"Case20 断言3")

# ---------- Case30: BumpUse 传切法键 ----------
rep('T9Engine.BumpUse("chuangsheng");', 'T9Engine.BumpUse("chuang\'sheng");',
    "Case30 BumpUse x3", want=3)

# ---------- Case31: dbg -> 正式回归用例 ----------
start = src.index("    static void Case31_Dbg()")
end   = src.index("    static void Case30_MineIsThisSplit()")

rep(src[start:end],
L([
"    /// <summary>",
"    /// 【31】同一串数字的两种切法: 谁打出去得多, 谁排前面。",
"    /// <para>这句话是用户连着报三次的那个问题的正面要求。以前 <c>_use</c> 的键是",
"    /// **连写**(chuangsheng) —— chuang'sheng 和 chu'ang'sheng 拼起来一样, 于是两种",
"    /// 切法**共用一个计数器**: 发的是哪一条都给两条一起加分, \"谁发得多谁排前面\"在",
"    /// 数据结构上就不成立。更糟的是 BumpUse 攒够次数时拿 SplitKey 从连写反推切法",
"    /// (取块数最少那种), 把 chu'ang'sheng 记成了 chuang'sheng —— 张冠李戴。</para>",
"    /// <para>现在一种切法一个身份: 各记各的次数, 排名跟着你真正打过的那一条走。",
"    /// 这条用例抓手抓的是**结果**(谁排第一), 不看内部实现。</para>",
"    /// </summary>",
"    static void Case31_WhoSentMoreRanksFirst()",
"    {",
"        Console.WriteLine(\"【31】同一串数字的两种切法: 谁发得多谁排前面\");",
"        T9Engine.ClearUse();",
"        const string digits = \"24826474364\";",
"        const string A = \"chuang'sheng\", B = \"chu'ang'sheng\";",
"",
"        var c0 = New();",
"        NumSeq(c0, digits);",
"        CheckTrue(\"候选里有 \" + A, c0.Segs.Any(s => string.Join(\"'\", s) == A));",
"        CheckTrue(\"同一串数字还能切成 \" + B, c0.Segs.Any(s => string.Join(\"'\", s) == B));",
"",
"        // A 打出去 3 次 -> A 第一, 而且次数只加在 A 自己那条上",
"        SendTimes(digits, A, 3);",
"        Check(\"A(3 次) / B(0 次): A 排第一\", Top(digits, 1)[0], A);",
"        Check(\"次数各记各的: A\", T9Engine.UseOf(A).ToString(), \"3\");",
"        Check(\"次数各记各的: B 一条都没蹭到\", T9Engine.UseOf(B).ToString(), \"0\");",
"",
"        // B 打出去 5 次(比 A 多) -> 反过来 B 登顶, 但 A 不能被挤没了",
"        SendTimes(digits, B, 5);",
"        Check(\"B 发得更多(5 > 3): B 反超到第一\", Top(digits, 1)[0], B);",
"        Check(\"两条的次数清清楚楚分开\", T9Engine.UseOf(A) + \"/\" + T9Engine.UseOf(B), \"3/5\");",
"        CheckTrue(\"A 没被挤没, 只是往后挪\", Top(digits, 99).Contains(A));",
"",
"        // 再回头把 A 补成 8 次 -> A 又回到第一。**能来回翻**才是真记住了次数,",
"        // 以前因为共用计数器, 排名一旦定死就再也翻不动了。",
"        SendTimes(digits, A, 5);",
"        Check(\"A 补到 8 次 > B 的 5 次: A 又翻回第一\", Top(digits, 1)[0], A);",
"        Check(\"次数跟着走\", T9Engine.UseOf(A) + \"/\" + T9Engine.UseOf(B), \"8/5\");",
"",
"        // 保送进候选池的必须是**你亲手发过的那两刀**, 不是猜出来的某一种",
"        var m = T9Engine.MineOf(digits).Select(s => string.Join(\"'\", s)).ToList();",
"        CheckTrue(\"保送的是你发过的那一刀 A\", m.Contains(A));",
"        CheckTrue(\"保送的也有你发过的那一刀 B\", m.Contains(B));",
"",
"        T9Engine.ClearUse();",
"        Console.WriteLine();",
"    }",
"",
"    /// <summary>",
"    /// 把这个数字串打成 want 那条、再 RB 发出去, 重复 n 次 —— 走的是真实按键路径。",
"    /// </summary>",
"    static void SendTimes(string digits, string want, int n)",
"    {",
"        for (int i = 0; i < n; i++)",
"        {",
"            var c = New();",
"            NumSeq(c, digits);",
"            int guard = 0;",
"            while (string.Join(\"'\", c.Segs[c.PinyinIdx]) != want && guard++ < 60) Right(c);",
"            if (string.Join(\"'\", c.Segs[c.PinyinIdx]) != want) return;   // 找不着就停, 别死循环",
"            Send(c);",
"            RT(c);                                   // 上屏: SentPinyin 归零, 整串记一次",
"        }",
"    }",
"",
"    /// <summary>当前 _use 里所有项: 键=次数(排错时一眼能看出记成了啥)。</summary>",
"    static string DumpUse()",
"    {",
"        var d = T9Engine.DumpUse();",
"        return d.Count == 0 ? \"(空)\" : string.Join(\", \", d.Select(kv => $\"{kv.Key}={kv.Value}\"));",
"    }",
"",
"    /// <summary>这个数字串上保送了哪几条切法。</summary>",
"    static string DumpMine(string digits)",
"    {",
"        var m = T9Engine.MineOf(digits).Select(s => string.Join(\"'\", s)).ToList();",
"        return m.Count == 0 ? \"(空)\" : string.Join(\", \", m);",
"    }",
"",
"    /// <summary>重新打一遍这个数字串, 列出前 n 条候选。</summary>",
"    static List<string> Top(string digits, int n)",
"    {",
"        var c = New();",
"        NumSeq(c, digits);",
"        return c.Segs.Take(n).Select(s => string.Join(\"'\", s)).ToList();",
"    }",
"",
""]),
"Case31 转正")

# Main 里改个名
rep("        Case31_Dbg();", "        Case31_WhoSentMoreRanksFirst();", "Main 调用名")

open(P, "wb").write(src.encode("utf-8"))
print("已写入, 完成:", ", ".join(done))
b = open(P, "rb").read()
print("CRLF=", b.count(b"\r\n"), "裸LF=", b.count(b"\n") - b.count(b"\r\n"), "CRCRLF=", b.count(b"\r\r\n"))
