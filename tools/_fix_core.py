# -*- coding: utf-8 -*-
"""
2.1.36 同号重出(第三次) —— InputCore 发送侧:
  学的时候必须带着切法(带撇), 不能把撇抹掉 —— 撇就是音节边界,
  抹掉之后 chuang'sheng 和 chu'ang'sheng 又变成同一条了。

InputCore.cs 是 CRLF, 一律 '\r\n'.join, 写完要查没有裸 LF / \r\r\n。
"""
import sys

P = r"C:\Users\<你>\Binput\InputCore.cs"
src = open(P, "rb").read().decode("utf-8").replace("\r\n", "\n")   # 进来统一 LF

done = []
def rep(old, new, tag):
    global src
    n = src.count(old)
    if n != 1:
        print(f"!! [{tag}] 命中 {n} 次, 期望 1 次 —— 中止")
        print(repr(old[:300]))
        sys.exit(1)
    src = src.replace(old, new)
    done.append(tag)

L = lambda xs: "\n".join(xs)

# ---------- 1. _lastSent 的注释 ----------
rep(
"""    /// <summary>SendPinyin 最近一次记下的那条。用来判断"整串是不是已经记过了"。</summary>
    string _lastSent = "";""",
L([
"    /// <summary>",
"    /// SendPinyin 最近一次记下的那条 —— 存的是**带撇的切法**(\"chuang'sheng\")。",
"    /// 用来判断\"整串是不是已经记过了\"。",
"    /// <para>这里不能存连写: 连写会把 chuang'sheng 和 chu'ang'sheng 认成同一条,",
"    /// 于是 LearnWhole 里\"这次发的和上次是不是同一条\"的判断跟着失真。</para>",
"    /// </summary>",
"    string _lastSent = \"\";",
]),
"_lastSent 注释")

# ---------- 2. LearnWhole: 整串带着撇记 ----------
rep(
"""    void LearnWhole(string raw)
    {
        string key = raw.Replace("'", "");
        if (key.Length < T9Engine.MYWORD_MINLEN) return;   // 太短的都是普通词
        if (key == _lastSent) return;                       // 整串一次发完的, 已经记过
        T9Engine.BumpUse(key);
        // 光记"用过几次"不够 —— 还得让它下次能进候选池。SentPinyin 是带撇号的
        // (pao'pao'sao), 撇号就是音节边界, 正好直接拆开交给 AddMine。
        T9Engine.AddMine(raw.Split('\\''));
    }""",
L([
"    void LearnWhole(string raw)",
"    {",
"        // 【2.1.36】整串**带着撇**记, 不再先 Replace 掉。",
"        // 以前抹成连写(paopaosao)再交给 BumpUse, 引擎那边只能拿 SplitKey 猜\"块数",
"        // 最少那种\" —— 于是你发的是 chu'ang'sheng, 被记成了 chuang'sheng。现在带着",
"        // 撇, 你亲手定的那一刀原样传过去, 一个猜字都不需要。",
"        if (raw.Length == 0) return;",
"        if (raw.Replace(\"'\", \"\").Length < T9Engine.MYWORD_MINLEN) return;   // 太短的都是普通词",
"        if (raw == _lastSent) return;                       // 整串一次发完的, 已经记过",
"        T9Engine.BumpUse(raw);",
"        // 光记\"用过几次\"不够 —— 还得让它下次能进候选池。SentPinyin 是带撇号的",
"        // (pao'pao'sao), 撇号就是音节边界, 正好直接拆开交给 AddMine。",
"        T9Engine.AddMine(raw.Split('\\''));",
"    }",
]),
"LearnWhole")

# ---------- 3. DropSent: 同上, 留着撇比对 ----------
rep(
"""    void DropSent()
    {
        if (_sentPinyin.Length > 0) _lastSent = _sentPinyin.Replace("'", "");
        SentPinyin = "";
    }""",
L([
"    void DropSent()",
"    {",
"        // 和 LearnWhole 同一套口径: 留着撇, 才能跟 _lastSent 对得上",
"        if (_sentPinyin.Length > 0) _lastSent = _sentPinyin;",
"        SentPinyin = \"\";",
"    }",
]),
"DropSent")

# ---------- 4. SendPinyin: 记学时用切法身份 ----------
rep(
"""        if (learn) { _lastSent = string.Concat(syls); T9Engine.BumpUse(_lastSent); }""",
L([
"        // 记的是**这一刀**(\"chuang'sheng\"), 不是这一串字母(\"chuangsheng\") ——",
"        // 去掉撇的话, chu'ang'sheng 会和 chuang'sheng 共用同一个计数器,",
"        // \"哪个发得多哪个排前面\"就成了永远做不到的空话(2.1.36 连报三次的根)。",
"        if (learn) { _lastSent = KeyOfSplit(syls); T9Engine.BumpUse(_lastSent); }",
]),
"SendPinyin 记学")

open(P, "wb").write(src.replace("\n", "\r\n").encode("utf-8"))
print("已写入, 完成:", ", ".join(done))
b = open(P, "rb").read()
print("CRLF=", b.count(b"\r\n"), "裸LF=", b.count(b"\n") - b.count(b"\r\n"), "CRCRLF=", b.count(b"\r\r\n"))
