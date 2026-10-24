# -*- coding: utf-8 -*-
"""
2.1.36 同号重出(第三次): 学习记录从"连写"改成"切法"。

根因: _use 的键是去撇的连写(chuangsheng), 于是 chuang'sheng 和 chu'ang'sheng
      共用同一个计数器 —— "谁发得多谁排前面"在数据结构上就不成立; 而且 BumpUse
      攒够次数时用 SplitKey 从连写反推切法(取块数最少那种), 把你发的是哪一条
      记反了。

T9Engine.cs 是 LF, 脚本统一按 LF 拼好再整体写回。
"""
import io, os, sys

P = r"C:\Users\<你>\Binput\T9Engine.cs"
src = open(P, "rb").read().decode("utf-8")
src = src.replace("\r\n", "\n")          # 读进来先统一成 LF, 写回也是 LF

done = []
def rep(old, new, tag):
    global src
    n = src.count(old)
    if n != 1:
        print(f"!! [{tag}] 命中 {n} 次, 期望 1 次 —— 中止")
        print(repr(old[:200]))
        sys.exit(1)
    src = src.replace(old, new)
    done.append(tag)

# ---------- 1. _use 的注释: 键从"连写"改成"切法" ----------
rep(
"""    /// <summary>用户挑过哪些切分: 拼音连写 -> 选中发出的次数。见 Rank / BumpUse。</summary>
    static readonly Dictionary<string, int> _use = new();""",
"\n".join([
"    /// <summary>",
"    /// 用户挑过哪些切分: **拼音切法**(带撇, 如 \"chuang'sheng\") -> 选中发出的次数。",
"    /// <para>【2.1.36】以前这里的键是**连写**(把撇去掉的 chuangsheng)。连写分不出切法 ——",
"    /// chuang'sheng 和 chu'ang'sheng 拼起来是同一串 chuangsheng, 于是两种切法共用",
"    /// 一个计数器: 不管你发的是哪一条, 两条一起加分, \"谁发得多谁排前面\"在数据这一层",
"    /// 就不成立(用户连着报的三个现象都是这一个根)。现在一种切法一个身份。</para>",
"    /// <para>查词库仍然要用连写(词库里没有撇), 由 <see cref=\"Letters\"/> 现算。</para>",
"    /// 见 Rank / BumpUse。",
"    /// </summary>",
"    static readonly Dictionary<string, int> _use = new();",
]),
"_use 注释")

# ---------- 2. 新增 KeyOf / Letters ----------
rep(
"""    /// <summary>这条拼音你亲自发出去过几次(见 BumpUse)。没发过就是 0。</summary>
    public static int UseOf(string key) =>
        key.Length > 0 && _use.TryGetValue(key, out int n) ? n : 0;""",
"\n".join([
"    /// <summary>",
"    /// 一组音节的**身份**: 带撇连起来(\"chuang'sheng\")。学习和保送全用它当键。",
"    /// <para>不能用去撇的连写当身份: chuang'sheng 和 chu'ang'sheng 会并成同一条,",
"    /// 两种切法从此同分、同命运, 谁也超不过谁。</para>",
"    /// </summary>",
"    public static string KeyOf(string[] syls) =>",
"        syls == null || syls.Length == 0 ? \"\" : string.Join('\\'', syls);",
"",
"    /// <summary>",
"    /// 把 <see cref=\"KeyOf\"/> 的身份还原成**连写**(\"chuangsheng\") —— 查词库用这个。",
"    /// </summary>",
"    public static string Letters(string key) =>",
"        string.IsNullOrEmpty(key) ? \"\" : key.Replace(\"'\", \"\");",
"",
"    /// <summary>这条切分你亲自发出去过几次(见 BumpUse)。没发过就是 0。</summary>",
"    public static int UseOf(string key) =>",
"        key.Length > 0 && _use.TryGetValue(key, out int n) ? n : 0;",
]),
"KeyOf / Letters")

# ---------- 3. IsMine: 按这条切法记, 词库按连写查 ----------
rep(
"""    public static bool IsMine(string key) =>
        key.Length >= MYWORD_MINLEN && !HasPhrase(key) && UseOf(key) >= MYWORD_MIN;""",
"\n".join([
"    public static bool IsMine(string key)",
"    {",
"        if (string.IsNullOrEmpty(key)) return false;",
"        // 词库里没有撇, 按连写问; \"这条是不是你的\"则按这条切法自己的次数算",
"        string flat = Letters(key);",
"        return flat.Length >= MYWORD_MINLEN && !HasPhrase(flat) && UseOf(key) >= MYWORD_MIN;",
"    }",
]),
"IsMine")

# ---------- 4. MineCount 跟着 IsMine 走 ----------
rep(
"""    public static int MineCount =>
        _use.Count(kv => kv.Key.Length >= MYWORD_MINLEN && !HasPhrase(kv.Key) && kv.Value >= MYWORD_MIN);""",
"\n".join([
"    public static int MineCount => _use.Count(kv => IsMine(kv.Key));",
]),
"MineCount")

# ---------- 5. _mine 那段说明: 保送的必须是"你亲手定的那一刀" ----------
rep(
"""        // 同一串字母可能已经被另一种切法占住了(见 SplitKey: paopaosao 能切成
        // 3 块也能切成 6 块)。块数更少的那种才像人话, 用它替换掉更碎的那个 ——
        // 不然 LearnWhole 拿到的精确切法(pao'pao'sao)会被 BumpUse 兜底那条
        // 先到先得的碎切法(pa'o'pa'o'sa'o)挡在门外。
        int at = list.FindIndex(s => string.Concat(s) == key);
        if (at >= 0)
        {
            if (syls.Length >= list[at].Length) return false;
            list[at] = syls;
            return true;
        }
        list.Add(syls);
        return true;""",
"\n".join([
"        // 【2.1.36】以前这里还要比\"谁的块数更少\"才决定留谁 —— 那是给 SplitKey 兜底的:",
"        // 那时 BumpUse 手上只有一串连写, 只能猜\"最少块数那种最像人话\"。而这一猜正是",
"        // 把这一刀张冠李戴的那一步: 你发的是 chu'ang'sheng, 它猜成 chuang'sheng。",
"        // 现在传进来的就是你亲手定的那一刀, 按身份去重即可, 不再比块数。",
"        string who = KeyOf(syls);",
"        if (list.Any(s => KeyOf(s) == who)) return false;",
"        list.Add(syls);",
"        return true;",
]),
"AddMine 去重")

# ---------- 6. AddMine 头部: key 改成带撇的身份 ----------
rep(
"""        string key = string.Concat(syls);
        if (key.Length < MYWORD_MINLEN) return false;
        if (HasPhrase(key)) return false;           // 词库里本来就有, Segment 会给
        if (UseOf(key) < MYWORD_MIN) return false;  // 还没拼够次数, 不作数""",
"\n".join([
"        string key = KeyOf(syls);",
"        if (Letters(key).Length < MYWORD_MINLEN) return false;",
"        if (HasPhrase(Letters(key))) return false;   // 词库里本来就有, Segment 会给",
"        if (UseOf(key) < MYWORD_MIN) return false;   // 还没拼够次数, 不作数",
]),
"AddMine 头部")

open(P, "wb").write(src.encode("utf-8"))
print("已写入, 完成:", ", ".join(done))
b = open(P, "rb").read()
print("CRLF=", b.count(b"\r\n"), "裸LF=", b.count(b"\n") - b.count(b"\r\n"), "CRCRLF=", b.count(b"\r\r\n"))
