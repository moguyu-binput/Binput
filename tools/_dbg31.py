# -*- coding: utf-8 -*-
"""把 Case31_Dbg 换成一个能 dump 真相的诊断版。coretest/Program.cs 是 LF。"""
import io, os

P = r"C:\Users\<你>\Binput\tools\coretest\Program.cs"
raw = open(P, "rb").read().decode("utf-8")

start = raw.index("    static void Case31_Dbg()")
end   = raw.index("    static void Case30_MineIsThisSplit()")
old   = raw[start:end]

new = '\n'.join([
    "    static void Case31_Dbg()",
    "    {",
    "        Console.WriteLine(\"【dbg】交替打 chuang'sheng / chu'ang'sheng —— 看究竟记了什么\");",
    "        T9Engine.ClearUse();",
    "        const string digits = \"24826474364\";",
    "        const string mine = \"chuang'sheng\", shred = \"chu'ang'sheng\";",
    "",
    "        for (int round = 1; round <= 6; round++)",
    "        {",
    "            string want = (round % 2 == 1) ? mine : shred;",
    "            var c = New();",
    "            NumSeq(c, digits);",
    "            int guard = 0;",
    "            while (string.Join(\"'\", c.Segs[c.PinyinIdx]) != want && guard++ < 40) Right(c);",
    "            string sel = string.Join(\"'\", c.Segs[c.PinyinIdx]);",
    "            Send(c);",
    "            string sent = c.SentPinyin;",
    "            RT(c);                       // 回车上屏 -> SentPinyin 归零 -> LearnWhole",
    "            Console.WriteLine($\"  第 {round} 轮 想发 {want,-15} 选中 {sel,-15} 发出=\\\"{sent}\\\" 发后 SentPinyin=\\\"{c.SentPinyin}\\\"\");",
    "            Console.WriteLine($\"          _use : {DumpUse()}\");",
    "            Console.WriteLine($\"          _mine: {DumpMine(digits)}\");",
    "            Console.WriteLine($\"          重打时前三条: {string.Join(\" | \", Top(digits, 3))}\");",
    "        }",
    "        T9Engine.ClearUse();",
    "        Console.WriteLine();",
    "    }",
    "",
    "    /// <summary>当前 _use 里所有项: 键=次数。</summary>",
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
    ""])

out = raw[:start] + new + raw[end:]
# 规范化: 全 LF (该文件本来就是 LF)
b = out.encode("utf-8")
b = b.replace(b'\r\n', b'\n')
open(P, "wb").write(b)

chk = open(P, "rb").read()
print("CRLF=", chk.count(b'\r\n'), "裸LF=", chk.count(b'\n') - chk.count(b'\r\n'),
      "CRCRLF=", chk.count(b'\r\r\n'))
print("done")
