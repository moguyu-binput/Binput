using Binput;
using System.IO;

// 用法: segprobe [--old] [--use=shid*3,qige*5] [--pad=50] <数字串>...
//   默认    = 1.0.7 新逻辑(词频分级 + 个人加成(4次到顶/久不用回落) + 自造词)
//   --old   = 1.0.2 老逻辑(成词固定 +1000), 用来做 before/after 对比
//   --use=  = 假装"这几条拼音你已经各发出去 N 次", 再看排序怎么动。
//             例子: --use=shid*3               模拟你连着打了三次 shi'd
//                   --use=qige*6               模拟你连着打了六次 qi'ge
//                   --use=zhangsanfeng*2       模拟你按 Y 逐字拼了两次"张三丰"
//   --pad=N = 把全局步数往前推 N 格, 中间不发任何词。夹在两个 --use 之间,
//             就造出"前一个词是 N 次输入之前用的" —— 验证久不用会不会回落。
//             例子: --use=chesan*20 --pad=60 --use=bierbo*4
//                   = che'san 打过 20 次但已经 60 次没碰, bi'er'bo 刚打完 4 次
//   --find=bi'er'bo   诊断: 这条切分有没有进候选? 顺带打印块数分布
//   --dump            把所有候选都打出来
//   --use / --pad **按命令行顺序执行**, 所以先后顺序是有意义的。

bool old = args.Length > 0 && args[0] == "--old";
var rest = args.Skip(old ? 1 : 0).ToArray();

var digits = new List<string>();
var finds = new List<string>();
bool dump = false;
var log = new List<string>();

foreach (var a in rest)
{
    if (a.StartsWith("--find=")) { finds.Add(a[7..]); continue; }
    if (a == "--dump") { dump = true; continue; }
    if (a.StartsWith("--pad="))
    {
        if (int.TryParse(a[6..], out int step) && step > 0)
        {
            for (int i = 0; i < step; i++) T9Engine.BumpUse("\x01pad\x01");
            log.Add($"空推 {step} 步");
        }
        continue;
    }
    if (a.StartsWith("--use="))
    {
        foreach (var part in a[6..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('*');
            if (kv.Length == 2 && int.TryParse(kv[1], out int k))
            {
                for (int i = 0; i < k; i++) T9Engine.BumpUse(kv[0]);
                log.Add($"{kv[0]}×{k}");
            }
        }
        continue;
    }
    digits.Add(a);
}

if (digits.Count == 0) digits.AddRange(new[]
{
    "7443", "74433", "744", "7468326", "426", "2426", "94826", "4864", "946", "9426",
    "9426", "64", "948", "264", "943943", "486", "926",
});

int n = T9Engine.LoadExternalDict(Path.Combine(AppContext.BaseDirectory, "dict.txt"));
Console.WriteLine($"词库 {n} 条   模式: {(old ? "1.0.2 旧(成词固定+1000)" : "1.0.7 新(词频分级 + 个人加成 + 自造词)")}");
if (log.Count > 0) Console.WriteLine("按序模拟: " + string.Join("  →  ", log));
Console.WriteLine();

double RankOld(string[] syls)
{
    string key = string.Concat(syls);
    double s = 0;
    if (T9Engine.HasPhrase(key)) s += 1000;
    else
        for (int k = syls.Length - 1; k >= 1; k--)
            if (T9Engine.HasPhrase(string.Concat(syls.Take(k)))) { s += 120.0 * k; break; }
    foreach (var y in syls) s += T9Engine.HotOf(y);
    return s;
}

foreach (var d in digits)
{
    var raw = T9Engine.Segment(d, 40);
    var seen = new HashSet<string>();
    var list = new List<string[]>();
    foreach (var s in raw)
    {
        if (s.Length == 0) continue;
        if (!s.All(y => y.Length > 0 && y.All(char.IsAsciiLetter))) continue;
        if (seen.Add(string.Concat(s))) list.Add(s);
    }
    Func<string[], double> rank = old ? RankOld : T9Engine.Rank;
    int minBlocks = list.Count > 0 ? list.Min(s => s.Length) : 0;
    // 旧 = 块数硬分档(1.0.5 及以前);  新 = 分数优先 + 块数软惩罚(1.0.6)
    var byBlock = list.OrderBy(s => s.Length).ThenByDescending(rank).Take(24).ToList();
    var byScore = list.OrderByDescending(s => T9Engine.RankOrder(s, minBlocks))
                      .ThenBy(s => s.Length).Take(24).ToList();
    var segs = byScore;

    Console.WriteLine($"[{d}]");

    // 候选的块数分布 —— 看"块数少的"是不是把名额占满了
    var byLen = raw.GroupBy(s => s.Length).OrderBy(g => g.Key)
                   .Select(g => $"{g.Key}块×{g.Count()}");
    Console.WriteLine($"    候选 {raw.Count} 条, 块数分布: {string.Join("  ", byLen)}");

    foreach (var f in finds)
    {
        string flat = f.Replace("'", "");
        bool hit = raw.Any(s => string.Concat(s) == flat);
        Console.WriteLine($"    --find {f,-14} 在候选里? {(hit ? "在" : "【不在】<-- 被裁掉了")}");
    }

    if (dump)
        foreach (var s in raw.Select(s => string.Join("'", s)).Take(40))
            Console.WriteLine("       " + s);

    for (int i = 0; i < Math.Min(4, segs.Count); i++)
    {
        var s = segs[i];
        string key = string.Concat(s);
        int f = T9Engine.FreqOf(key);
        bool mine = !old && T9Engine.IsMine(key);
        string tag = mine ? " ←你造的" : (f > 0 ? "   成词" : "      ");
        string score = old ? RankOld(s).ToString("0") : T9Engine.Rank(s).ToString("0");
        string freq = f > 0 ? f.ToString() : "-";
        string learn = old ? "" : " 个人=" + T9Engine.LearnBonus(key).ToString("0");
        Console.WriteLine($"   {i + 1}. {string.Join("'", s),-16}{tag}  " +
                          $"分={score,-6} 词频={freq,-8}{learn}");
    }

    // 两种排序下各自的第一名, 不一样就标出来
    if (byBlock.Count > 0 && byScore.Count > 0)
    {
        string a = string.Join("'", byBlock[0]), b = string.Join("'", byScore[0]);
        string ra = T9Engine.Rank(byBlock[0]).ToString("0"), rb = T9Engine.Rank(byScore[0]).ToString("0");
        if (a != b)
            Console.WriteLine($"    排序之争: 块数优先={a}({ra}分)   纯分数={b}({rb}分)   ** 不一样 **");
        else
            Console.WriteLine($"    排序之争: 一致 = {a}");
    }
    Console.WriteLine();
}
