using Binput;
using System.IO;
using System.Text;

// useprobe —— 验"输入习惯这份文件"的两件事:
//   1) **隐私**: 存在本机的是密文, 别人直接打开看不到你常打的词和自己拼的名字;
//   2) **搬家**: 导出 -> 导入 能完整搬到另一台机器上, 而且不覆盖那边已经学的。
//
// 顺带守住两条不能破的规矩:
//   - 1.1.0 以前留下的**明文**文件, 升级上来必须还读得出来(一条都不许丢);
//   - 文件坏了 / 拿错文件了, 最多是"读不到", 不许崩。

int fail = 0;
void Ok(string name, bool good, string got = "")
{
    Console.WriteLine((good ? "    OK    " : "    FAIL  ") + name + (got.Length > 0 ? ": " + got : ""));
    if (!good) fail++;
}

// 整个探针跑在一个临时目录里 —— Config.SetDirForTest 把数据目录挪过来,
// **不去动你机器上真实那份**(以前是先备份再还原, 现在根本不碰它)。
string tmp = Path.Combine(Path.GetTempPath(), "useprobe_" + Guid.NewGuid().ToString("N")[..8]);
string portable = Config.Dir;    // 切目录之前先看一眼: 真机上它该在程序旁边
Config.SetDirForTest(tmp);
string real = Config.SavePath;   // 2.1.5 起: Binput.save(必须在切目录**之后**取)
Console.WriteLine($"真机上的数据目录: {portable}");
Console.WriteLine($"本次用的假数据目录: {tmp}");
Ok("数据目录跟着程序走(程序旁的 save 文件夹)",
   portable.Replace("/", "\\").EndsWith("\\save"), portable);

var demo = new Dictionary<string, int>
{
    ["paopaosao"] = 5,      // 自己按 Y 拼出来的网名
    ["shoudao"] = 17,
    ["zhang"] = 9,
};
var demoLast = new Dictionary<string, int> { ["paopaosao"] = 41, ["shoudao"] = 38, ["zhang"] = 40 };

try
{
    // ---- 1. 空目录: 什么都没有时是 0 条; 随手放的 txt 一概不认 ----
    Console.WriteLine("\n=== 1. 空目录 / 不是我们家的文件 ===");
    Ok("没存过 -> 0 条", Config.LoadUse(out _).Count == 0);
    Ok("也没凭空生成一个存档", !File.Exists(real));
    // 2.1.5 起只认 Binput.save 这一份(整份加密)。2.1.4 以前那份明文 pinyin.txt
    // 不再去读 —— 反正还没正式发出去过, 不背着兼容的包袱。
    File.WriteAllText(Path.Combine(tmp, "pinyin.txt"), "paopaosao 5 41\n");
    Config.Reload();
    Ok("随手放的 txt 不认(读到 0 条)", Config.LoadUse(out _).Count == 0);
    File.Delete(Path.Combine(tmp, "pinyin.txt"));
    Config.Reload();

    // ---- 2. 存盘之后应该是密文: 文件里搜不到任何一个词 ----
    Console.WriteLine("\n=== 2. 隐私: 存盘后是密文 ===");
    Config.SaveUse(demo, demoLast);
    var raw = File.ReadAllBytes(real);
    string asText = Encoding.UTF8.GetString(raw);
    Ok("文件写出来了", raw.Length > 0, raw.Length + " 字节");
    Ok("开头是自家的密文标记 BIN1", raw.Length >= 4 && asText.StartsWith("BIN1"));
    bool leaked = false;
    foreach (var k in demo.Keys)
        if (asText.Contains(k)) { leaked = true; Console.WriteLine($"       泄漏了 \"{k}\""); }
    Ok("文件里搜不到任何一个词(明文全没了)", !leaked);

    // ---- 3. 密文要能原样读回来 ----
    Console.WriteLine("\n=== 3. 往返: 存进去再读出来 ===");
    var back = Config.LoadUse(out var backLast);
    Ok("条数一致", back.Count == demo.Count, back.Count + " 条");
    bool same = demo.All(kv => back.GetValueOrDefault(kv.Key) == kv.Value)
             && demoLast.All(kv => backLast.GetValueOrDefault(kv.Key) == kv.Value);
    Ok("每条的次数和 last 都对得上", same);

    // ---- 4. 导出: 跨机器用的那份必须是明文 ----
    Console.WriteLine("\n=== 4. 导出 / 导入(换机器) ===");
    string exp = Path.Combine(Path.GetTempPath(), "useprobe_export.txt");
    Ok("导出成功", Config.ExportAll(exp, demo, demoLast, null));
    string expText = File.ReadAllText(exp);
    Ok("导出的是明文(别的机器才读得懂)", expText.Contains("paopaosao"), "里面有 paopaosao");
    Ok("导出的文件带 [use] 段标记", expText.Contains("[use]"));

    Config.ImportAll(exp, out var imported, out var impLast, out var impQuick);
    Ok("导出的文件能读回来", imported.Count == 3, imported.Count + " 条");
    Ok("导回来的次数没错", imported.GetValueOrDefault("shoudao") == 17);
    Ok("没写快捷时快捷段是空的", impQuick.Count == 0);

    // ---- 5. 合并: 取高值, 不覆盖这台机器上学的 ----
    Console.WriteLine("\n=== 5. 合并: 两边并起来, 不是替换 ===");
    // 【2.1.36】学习和排序的键改成了**带撇的切法**("shou'dao"), 以前存的则是连写
    // ("shoudao")。老存档读进来必须自动翻过去 —— 不然一次升级就把攒了几千条的习惯全作废。
    T9Engine.LoadUse(new Dictionary<string, int> { ["shoudao"] = 3, ["benji"] = 8 });
    Ok("老存档(连写键)被翻成了切法键",
       !T9Engine.DumpUse().ContainsKey("shoudao") && T9Engine.UseOf("shou'dao") == 3);
    int before = T9Engine.UseCount;
    int changed = T9Engine.MergeUse(imported);
    Ok("次数取高值(本机 3 / 导入 17 -> 17)", T9Engine.UseOf("shou'dao") == 17);
    Ok("本机原来那条没被抹掉", T9Engine.DumpUse().ContainsKey("ben'ji"));
    Ok("新增的进来了", T9Engine.DumpUse().ContainsKey("pao'pao'sao"));

    // ---- 5b. 迁移必须**经得起反复读写**: 存盘 -> 读回 -> 再存盘, 不许一直变 ----
    // 翻的时候"多条老键撞到同一个新键"是常事(shoudao 和 shou'dao 两种写法),
    // 那种合并只该发生一次。要是每一趟都少几条, 用户攒的习惯会被来回读写磨光。
    Console.WriteLine("\n=== 5b. 老存档迁移: 再读写一趟不许继续变 ===");
    T9Engine.ClearUse();
    T9Engine.LoadUse(new Dictionary<string, int>
    {
        ["chuangsheng"] = 5,    // 切得开 -> chuang'sheng
        ["paopaosao"] = 2,      // 切得开 -> pao'pao'sao
        ["zhang"] = 9,          // 单音节, 切不开 -> 照原样留着
    });
    var once = T9Engine.DumpUse();
    Ok("老键被翻成了切法键", once.ContainsKey("chuang'sheng") && once.ContainsKey("pao'pao'sao"));
    Ok("连写那个老键本身已经不在了", !once.ContainsKey("chuangsheng"));
    Ok("切不开的单音节照原样留着", once.ContainsKey("zhang") && once["zhang"] == 9);

    T9Engine.LoadUse(new Dictionary<string, int>(once), T9Engine.DumpLast());
    var twice = T9Engine.DumpUse();
    Ok("再读写一趟: 条数没变", once.Count == twice.Count, once.Count + " -> " + twice.Count);
    Ok("再读写一趟: 每一条的键和次数都没动",
       once.Count == twice.Count && once.All(kv => twice.TryGetValue(kv.Key, out int v) && v == kv.Value));
    Ok("变动条数报得对", changed == 3, changed + " 条（paopaosao/zhang 新增 + shoudao 抬高）");
    Console.WriteLine($"        合并前 {before} 条 -> 现在 {T9Engine.UseCount} 条");

    // ---- 6. 坏文件 / 拿错文件: 最多读不到, 不许崩 ----
    Console.WriteLine("\n=== 6. 坏了也不崩 ===");
    string junk = Path.Combine(Path.GetTempPath(), "useprobe_junk.txt");
    File.WriteAllText(junk, "这不是一份输入习惯\n###\n");
    Ok("拿错文件 -> 读到 0 条而不是崩",
       !Config.ImportAll(junk, out var junkUse, out _, out _) && junkUse.Count == 0);
    File.WriteAllText(real, "这不是一份存档\n###\n");   // 连自家标记都没有
    Config.Reload();
    Ok("存档坏了 -> 读到 0 条而不是崩", Config.LoadUse(out _).Count == 0);
    var emptyFile = Path.Combine(Path.GetTempPath(), "useprobe_none.txt");
    if (File.Exists(emptyFile)) File.Delete(emptyFile);
    Ok("文件不存在 -> 读到 0 条",
       !Config.ImportAll(emptyFile, out var noUse, out _, out _) && noUse.Count == 0);

    File.Delete(junk);

    // ---- 7. 习惯 + 快捷 走同一份备份 ----
    Console.WriteLine("\n=== 7. 导出 / 导入: 习惯和快捷一起走 ===");
    // 2.1.1 起是 45 个固定槽位: 槽位 2 就是数字键 3
    QuickStore.Load();                            // 临时目录 -> 0 条
    QuickStore.Set(2, QKind.Phrase, "大家好呀");   // 内容里没空格也行, 有空格才考验切分
    QuickStore.Set(9, QKind.Hotkey, "ctrl+v");
    Ok("快捷存了 2 条", QuickStore.Count == 2, QuickStore.Count + " 条");
    Ok("槽位 2 = 数字键 3", QuickStore.ByNum(3)?.Text == "大家好呀",
       QuickStore.ByNum(3)?.Text ?? "<空>");
    string exp2 = Path.Combine(Path.GetTempPath(), "useprobe_export2.txt");
    Ok("导出(含快捷)", Config.ExportAll(exp2, demo, demoLast, QuickStore.Slots));
    string t2 = File.ReadAllText(exp2);
    Ok("文件里两个段都在", t2.Contains("[use]") && t2.Contains("[quick]"));

    Config.ImportAll(exp2, out var u2, out _, out var q2);
    Ok("习惯读回 3 条", u2.Count == 3, u2.Count + " 条");
    Ok("快捷读回 2 条", q2.Count == 2, q2.Count + " 条");
    Ok("种类没串(1 条 key)", q2.Count(x => x.Kind == QKind.Hotkey) == 1);
    Ok("短语内容没被切坏", q2.Any(x => x.Kind == QKind.Phrase && x.Text == "大家好呀"));

    // ---- 8. 老备份(只有习惯、没有段标记)照样能导 ----
    Console.WriteLine("\n=== 8. 兼容: 1.2.2 以前导出的老文件 ===");
    string legacy = Path.Combine(Path.GetTempPath(), "useprobe_legacy.txt");
    File.WriteAllText(legacy, "# Binput 输入习惯\nnihao 4 12\nshoudao 2 30\n");
    Config.ImportAll(legacy, out var u3, out _, out var q3);
    Ok("整份按习惯读", u3.Count == 2, u3.Count + " 条");
    Ok("次数没错", u3.GetValueOrDefault("nihao") == 4);
    int addedLegacy = QuickStore.Merge(q3);
    Ok("老文件不会往快捷里塞东西", addedLegacy == 0 && QuickStore.Count == 2, "新增 " + addedLegacy);

    // 真·换机器: 从零合并一遍(重复的不加、序号不被抢)
    int added = QuickStore.Merge(q2);
    Ok("一模一样的不重复加", added == 0 && QuickStore.Count == 2, "新增 " + added);
    Ok("这台机器上绑的序号没被抢走", QuickStore.ByNum(3) != null);
    QuickStore.Merge(new[] { (0, QKind.Phrase, "新机器上的一条") });
    Ok("新来的能进来", QuickStore.Count == 3);
    Ok("新来的不抢号(落在第 2 页往后)", QuickStore.At(10)?.Text == "新机器上的一条",
       QuickStore.At(10)?.Text ?? "<空>");

    // ---- 9. 数据保险: 读不出来的那份不许被盖掉 ----
    Console.WriteLine("\n=== 9. 数据保险(1.2.2 加的三道) ===");
    File.WriteAllText(real, "BIN1" + new string('?', 200));   // 有头, 但解不开
    Config.Reload();
    var brokenRead = Config.LoadUse(out _);
    Ok("解不开 -> 读到 0 条(不崩)", brokenRead.Count == 0);
    Ok("认作\"读不出来\", 不是\"还没学过\"", Config.UseBroken);
    Config.SaveUse(new Dictionary<string, int> { ["xin"] = 1 },
                   new Dictionary<string, int> { ["xin"] = 1 });
    var park = Directory.GetFiles(tmp, "Binput.save.bad-*");
    Ok("坏的那份改名留底了(没被盖掉)", park.Length == 1,
       park.Length > 0 ? Path.GetFileName(park[0]) : "没找到");
    Ok("留底的还是原来那份", park.Length == 1 && File.ReadAllText(park[0]).StartsWith("BIN1"));
    Ok("新数据照常写进去了", Config.LoadUse(out _).Count == 1);
    Ok("写盘不留 .tmp", !Directory.GetFiles(tmp, "*.tmp").Any());
    Config.SaveUse(new Dictionary<string, int> { ["xin"] = 2 },
                   new Dictionary<string, int> { ["xin"] = 2 });
    Ok("再存一次会留一份 .bak", Directory.GetFiles(tmp, "Binput.save.bak").Length == 1);
    Ok("标记已经收回(不用再当坏文件供着)", !Config.UseBroken);

    // ---- 10. 2.1.5: 一份存档装全部, 而且不许无限长 ----
    Console.WriteLine("\n=== 10. 一份存档装全部 + 习惯有封顶 ===");
    Config.SaveTheme(3);
    Config.SaveMode(true, 2);
    Config.SaveWake("kb:f7");
    Config.SaveUse(demo, demoLast);
    QuickStore.Set(0, QKind.Phrase, "一条快捷");
    var files = Directory.GetFiles(tmp).Select(Path.GetFileName).ToList();
    Ok("目录里除了存档和日志没有别的散件",
       files.All(f => f.StartsWith("Binput.save") || f == "log.txt"),
       string.Join(", ", files));
    Ok("设置和习惯在同一份里", File.Exists(real) && new FileInfo(real).Length > 0);

    var many = new Dictionary<string, int>();
    for (int i = 0; i < 9000; i++) many["p" + i] = 1 + (i % 5);   // 1~5 次各 1800 条
    Config.SaveUse(many, new Dictionary<string, int>());
    var after = Config.LoadUse(out _);
    Ok("习惯有封顶(8000 条一过就砍)", after.Count == 7200, after.Count + " 条");
    Ok("砍掉的是最冷的那批(剩下的最少也用过 2 次)", after.Count > 0 && after.Values.Min() == 2,
       after.Count > 0 ? after.Values.Min() + " 次" : "空");
    Ok("封顶之后读回来不崩、条数稳定", Config.LoadUse(out _).Count == 7200);

    File.Delete(exp); File.Delete(exp2); File.Delete(legacy);
}
finally
{
    // 整个临时目录丢掉 —— 真机上那份从头到尾没被碰过
    try { Directory.Delete(tmp, true); } catch { }
    Console.WriteLine("\n(临时目录已删除, 真实的输入习惯文件没动过)");
}

Console.WriteLine(fail == 0 ? "\n全部通过" : $"\n有 {fail} 处不对");
return fail == 0 ? 0 : 1;
