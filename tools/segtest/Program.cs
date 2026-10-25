using Binput;
using System.IO;
int n = T9Engine.LoadExternalDict(Path.Combine(AppContext.BaseDirectory, "dict.txt"));
Console.WriteLine($"词库 {n} 条\n");

foreach (var d in new[] { "726", "7426", "426", "7468326", "9426", "2426", "94826", "4864", "946" })
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
    var top = list.OrderBy(s => s.Length).ThenByDescending(T9Engine.Rank).Take(6).ToList();
    Console.WriteLine($"[{d}] 共 {list.Count} 种 -> 前 {top.Count}:  " +
        string.Join("  |  ", top.Select(s => string.Join("'", s) + (T9Engine.HasPhrase(string.Concat(s)) ? "*" : ""))));
}
Console.WriteLine("\n(* = 词库里真有这个词)");
