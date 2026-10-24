# -*- coding: utf-8 -*-
"""
2.1.36 同号重出(第三次) —— T9Engine 第二批:
  SplitKey 退成"只读老存档用"; RebuildMine 不再猜; Rank 按切法身份算;
  撤掉 LearnShare 补丁; BumpUse 照原员身份保送; LoadUse/MergeUse 迁移老键。
"""
import sys

P = r"C:\Users\<你>\Binput\T9Engine.cs"
src = open(P, "rb").read().decode("utf-8").replace("\r\n", "\n")

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

# ---------- 7. SplitKey: 只给老存档迁移用 ----------
rep(
"""    /// <summary>
    /// 拼音连写 -> 若干种音节切法。paopaosao -> [pao, pao, sao]。最多 4 种, 单音节的不算。
    /// <para>**块数最少的排在最前** —— 同一串字母往往不止一种切法:
    /// paopaosao 既能切成 pao'pao'sao(3 块), 也能切成 pa'o'pa'o'sa'o(6 块),
    /// 后者每个音节都合法, 却根本不是人要打的东西。而保送进候选的那条会被
    /// 当成"你上次定的就是这么切", 送错就等于把一堆碎字母塞进拼音行。
    /// 按 Y 逐字定出来的一定是块数最少那种(一个字一个音节), 所以最少块数最贴近原意。</para>
    /// <para>**刻意不走 Segment** —— 那条路正是会被 beam 裁掉的那条(见 _mine 的说明),
    /// 拿它来还原等于什么都没修。这里按音节表直接对字母做 DFS, 精确、不截断,
    /// 而且目标串是固定的, 规模很小。</para>
    /// </summary>""",
L([
"    /// <summary>",
"    /// 拼音连写 -> 若干种音节切法。paopaosao -> [pao, pao, sao]。最多 4 种, 单音节的不算。",
"    /// <para>**块数最少的排在最前**: 同一串字母往往不止一种切法 —— paopaosao 既能切成",
"    /// pao'pao'sao(3 块), 也能切成 pa'o'pa'o'sa'o(6 块), 后者每个音节都合法却根本不是",
"    /// 人要打的东西。</para>",
"    /// <para>【2.1.36】**只给老存档迁移用**(见 <see cref=\"MigrateKey\"/>)。以前它还兼着",
"    /// \"BumpUse 攒够次数时猜你切的是哪一种\" —— 那是把 chu'ang'sheng 记成 chuang'sheng",
"    /// 的直接原因。现在学习和保送全程带着切法走, 一个猜字都不需要。</para>",
"    /// <para>**刻意不走 Segment** —— 那条路正是会被 beam 裁掉的那条(见 _mine 的说明),",
"    /// 拿它来还原等于什么都没修。这里按音节表直接对字母做 DFS, 精确、不截断,</para>",
"    /// </summary>",
]),
"SplitKey 注释")

# ---------- 8. RebuildMine: 键已经是切法, 直接拆 ----------
rep(
"""    public static void RebuildMine()
    {
        _mine.Clear();
        foreach (var kv in _use)
        {
            if (!IsMine(kv.Key)) continue;
            foreach (var s in SplitKey(kv.Key)) AddMine(s);
        }
    }""",
L([
"    public static void RebuildMine()",
"    {",
"        _mine.Clear();",
"        foreach (var kv in _use)",
"        {",
"            if (!IsMine(kv.Key)) continue;",
"            // 键本身就是带撇的切法, 拆开即可 —— 不再走 SplitKey 去猜\"最少块数那种\"",
"            var syls = kv.Key.Split('\\'');",
"            if (syls.Length >= 2) AddMine(syls);",
"        }",
"    }",
]),
"RebuildMine")

# ---------- 9. Rank: 身份用带撇的切法; 撤掉 LearnShare ----------
rep(
"""        if (syls == null || syls.Length == 0) return 0;
        string key = string.Concat(syls);
        double s = 0;

        if (HasPhrase(key)) s += WORD_BASE + WORD_SPAN * Norm(FreqOf(key));""",
L([
"        if (syls == null || syls.Length == 0) return 0;",
"        string key = KeyOf(syls);      // 身份: 带撇, 一种切法一个",
"        string flat = Letters(key);    // 查词库要用连写(词库里没撇)",
"        double s = 0;",
"",
"        if (HasPhrase(flat)) s += WORD_BASE + WORD_SPAN * Norm(FreqOf(flat));",
]),
"Rank 身份")

rep(
"""        // 你用过 -> 单独一笔加分。前 4 次陡涨, 之后缓涨不封顶, 久不用会回落。
        s += LearnBonus(key) * LearnShare(syls);
        return s;""",
L([
"        // 你用过 -> 单独一笔加分。前 4 次陡涨, 之后缓涨不封顶, 久不用会回落。",
"        // 【2.1.36】不再乘 LearnShare: 学习本来就按切法记(_use 的键带撇),",
"        // 这一笔天然只加在你亲手发过的那一刀上, 用不着再按比例分配给谁。",
"        s += LearnBonus(key);",
"        return s;",
]),
"Rank 学习分")

# ---------- 10. 删掉 LearnShare 整个函数(补丁撤销) ----------
rep(
"""    /// <summary>
    /// 学习分给这条切法几成。
    /// <para>学习记的是**连写**(chuangsheng, 见 BumpUse), 可同一串字母往往不止
    /// 一种切法 —— 于是你挑的是 chuang'sheng, 切得更碎的 chu'ang'sheng 拼起来
    /// 也是 chuangsheng, 跟着领了一模一样的一份。成词档那笔和块数惩罚都分开之后
    /// 它登不了顶了, 可它还是赖在第二位: 正经候选(ci'tang'sheng)没学过, 反倒排在
    /// 它后面。</para>
    /// <para>所以: **这串数字上你亲手定过一刀**(见 <see cref="_mine"/>)的时候,
    /// 那一刀独享这份加分, 别的切法一份也不给 —— 学习分是给"你挑的那一刀"的,
    /// 不是给"这个读音"的。没定过(普通词, 同块数里挑一条那种, 见 Case20)
    /// 就照旧人人有份。</para>
    /// </summary>
    static double LearnShare(string[] syls)
    {
        if (syls == null || syls.Length == 0) return 1;
        string digits = string.Concat(syls.Select(ToT9));
        if (!_mine.TryGetValue(digits, out var list) || list.Count == 0) return 1;
        return IsMineSplit(syls) ? 1 : 0;
    }

""",
"",
"删 LearnShare")

# ---------- 11. BumpUse: 照这条 key 原本的切法保送 ----------
rep(
"""    /// <summary>用户把这条切分发给输入法了 —— 记一次。只有"发出去"才算确认。</summary>
    public static void BumpUse(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _seq++;                                     // 全局步数往前走一格
        int now = _use.TryGetValue(key, out int n) ? n + 1 : 1;
        _use[key] = now;
        _last[key] = _seq;                          // 这条现在是"最近"用过的
        _useNew++;
        // 刚拼够 MYWORD_MIN 次 -> 补一张"保送进候选"的票(见 AddMine)。
        // 只在正好够的那一次做: 之后每次都跑 SplitKey 是白费, 而且它早进索引了。
        if (now == MYWORD_MIN)
            foreach (var s in SplitKey(key)) AddMine(s);
    }""",
L([
"    /// <summary>",
"    /// 用户把这条切分发给输入法了 —— 记一次。只有\"发出去\"才算确认。",
"    /// <para>传进来的 key 必须是**带撇的切法**(\"chuang'sheng\"), 见 <see cref=\"KeyOf\"/>。",
"    /// 以前可以传连写, 内部再跑 SplitKey 猜\"最少块数那种\" —— 那是整个 2.1.36 连着",
"    /// 报三次的根: 你发的那一刀被猜成了另一刀。</para>",
"    /// </summary>",
"    public static void BumpUse(string key)",
"    {",
"        if (string.IsNullOrEmpty(key)) return;",
"        _seq++;                                     // 全局步数往前走一格",
"        int now = _use.TryGetValue(key, out int n) ? n + 1 : 1;",
"        _use[key] = now;",
"        _last[key] = _seq;                          // 这条现在是\"最近\"用过的",
"        _useNew++;",
"        // 刚拼够 MYWORD_MIN 次 -> 补一张\"保送进候选\"的票(见 AddMine)。",
"        // 只在正好够的那一次做, 之后每隔一次都跑是白费, 而且它早进索引了。",
"        // 保送的就是这条 key 原本的切法 —— 照着撇切开即可, 一个字都不用猜。",
"        if (now == MYWORD_MIN)",
"        {",
"            var syls = key.Split('\\'');",
"            if (syls.Length >= 2) AddMine(syls);",
"        }",
"    }",
]),
"BumpUse")

# ---------- 12. 新增 MigrateKey + LoadUse 迁移 ----------
rep(
"""    public static void LoadUse(Dictionary<string, int> data, Dictionary<string, int> last = null)
    {
        foreach (var kv in data)
            if (kv.Key.Length > 0 && kv.Value > 0) _use[kv.Key] = kv.Value;

        _last.Clear();
        _seq = 0;
        if (last != null)
            foreach (var kv in last)
            {
                if (kv.Key.Length == 0 || kv.Value < 0) continue;   // -1 = 老文件没记过
                _last[kv.Key] = kv.Value;
                if (kv.Value > _seq) _seq = kv.Value;
            }""",
L([
"    /// <summary>",
"    /// 存档里的键还原成**切法身份**。2.1.36 以前存的是连写(没有撇), 那种连当初怎么",
"    /// 切的都没记, 只能猜\"块数最少那种最像人话\" —— 猜错的概率不小, 但比一升级就把",
"    /// 几千条积累全作废强得多。新格式(本来带撇)原样返回。",
"    /// </summary>",
"    static string MigrateKey(string key)",
"    {",
"        if (string.IsNullOrEmpty(key)) return \"\";",
"        if (key.Contains('\\'')) return key;                    // 已经是切法身份",
"        if (!key.All(char.IsAsciiLetter)) return key;           // 哨兵之类, 照原样留着",
"        var guess = SplitKey(key);",
"        return guess.Count > 0 ? KeyOf(guess[0]) : key;         // 猜不出来就保持原样",
"    }",
"",
"    public static void LoadUse(Dictionary<string, int> data, Dictionary<string, int> last = null)",
"    {",
"        // 【2.1.36 老存档迁移】先把连写的老键翻成切法键。同一个新键可能被好几条老键",
"        // 翻到(psalms 和 psalm's 都会翻成前者), 取次数高的那条 —— 这里是\"还原\",",
"        // 不是\"合并\", 相加会凭空把冷词抬热。",
"        var use = new Dictionary<string, int>();",
"        foreach (var kv in data)",
"        {",
"            if (kv.Key.Length == 0 || kv.Value <= 0) continue;",
"            string k = MigrateKey(kv.Key);",
"            if (k.Length == 0) continue;",
"            if (!use.TryGetValue(k, out int had) || kv.Value > had) use[k] = kv.Value;",
"        }",
"        foreach (var kv in use) _use[kv.Key] = kv.Value;",
"",
"        _last.Clear();",
"        _seq = 0;",
"        if (last != null)",
"            foreach (var kv in last)",
"            {",
"                if (kv.Key.Length == 0 || kv.Value < 0) continue;   // -1 = 老文件没记过",
"                _last[MigrateKey(kv.Key)] = kv.Value;",
"                if (kv.Value > _seq) _seq = kv.Value;",
"            }",
]),
"LoadUse 迁移")

# ---------- 13. MergeUse: 导入的也可能是老格式 ----------
rep(
"""        foreach (var kv in data)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
            if (!_use.TryGetValue(kv.Key, out int cur) || kv.Value > cur)
            {
                _use[kv.Key] = kv.Value;
                changed++;
            }
            if (!_last.ContainsKey(kv.Key)) _last[kv.Key] = _seq;
        }""",
L([
"        foreach (var kv in data)",
"        {",
"            if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;",
"            string k = MigrateKey(kv.Key);          // 别处导来的可能还是老格式",
"            if (k.Length == 0) continue;",
"            if (!_use.TryGetValue(k, out int cur) || kv.Value > cur)",
"            {",
"                _use[k] = kv.Value;",
"                changed++;",
"            }",
"            if (!_last.ContainsKey(k)) _last[k] = _seq;",
"        }",
]),
"MergeUse 迁移")

open(P, "wb").write(src.encode("utf-8"))
print("已写入, 完成:", ", ".join(done))
b = open(P, "rb").read()
print("CRLF=", b.count(b"\r\n"), "裸LF=", b.count(b"\n") - b.count(b"\r\n"), "CRCRLF=", b.count(b"\r\r\n"))
