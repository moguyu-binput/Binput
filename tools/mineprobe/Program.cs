using Binput;
using System.IO;

// mineprobe —— 验"按 Y 一个字一个字拼出来的整词, 下次能不能自己冒出来"。
//
// 用法: dotnet run --project mineprobe
//
// 1.0.9 之前的现象: 726726726(paopaosao)按 Y 定完 pao / pao / sao 之后,
// 下次打同样的数字串, 候选里**根本没有** pao'pao'sao —— 不是排得靠后, 是
// 连池子都没进。根因在 T9Engine.Segment 的 beam(每一位只留 40 条, 只按
// "块数少 + 音节热"裁), 整串成不成词它不看, 于是冷门组合被裁掉了,
// 学习分再高也没机会起作用。
//
// 这个探针走**真实路径**: 手动摆出"按 Y 三次 + 上屏"的状态机, 让
// InputCore.LearnWhole / T9Engine.AddMine 真的跑一遍, 再看 RefreshCands
// 给出的候选里有没有那条整词。不模拟、不假设。

int fail = 0;

// 探针跑在命令行里, 不许真的把空格打到当时有焦点的窗口上
InputCore.NoSend = true;

// **必须先加载真词库**。不加载的话 T9Engine 只有内置那几十条兜底词,
// _maxFreq 上不去, 分数全是失真的(实测自造词能算到 13113 分),
// 排第几根本没有参考价值 —— 探针要验的就是"排第几"。
int loaded = T9Engine.LoadExternalDict(Path.Combine(AppContext.BaseDirectory, "dict.txt"));
Console.WriteLine($"词库 {loaded} 条 / 共 {T9Engine.PhraseCount} 条");
if (loaded < 40000) { Console.WriteLine("!! 词库没加载上, 分数不可信"); fail++; }

void Head(string s) => Console.WriteLine("\n=== " + s + " ===");

/// <summary>把 c 摆成"刚刚打完 digits、还没按任何键"的样子。</summary>
void Type(InputCore c, string digits)
{
    c.Digits = "";
    foreach (var ch in digits) { c.Digits += ch.ToString(); c.DebugRefreshCands(); }
}

/// <summary>按一次 Y: 切出光标那条的头一个音节, 剩下的数字继续打。</summary>
void PressY(InputCore c)
{
    c.DebugSplit();
}

/// <summary>上屏: 拼音串归零, LearnWhole 在那一刻把整串记下来。</summary>
void Commit(InputCore c)
{
    c.DebugCommitSent();
}

/// <summary>打一串数字, 返回排第一的那条(空串 = 一个候选都没有)。</summary>
string Top1(string digits)
{
    var c = new InputCore { Layout = Layout.CN9, Focus = FocusZone.Keyboard, Visible = true };
    Type(c, digits);
    return c.Segs.Count > 0 ? string.Join("'", c.Segs[0]) : "";
}

// 一批跟 paopaosao 八竿子打不着的数字串。保送只该动 726726726, 别的一个都不许动。
// **刻意不含 726**: 场景1 里按 Y 把 pao 选了 6 次、sao 3 次, 726 的第一名从 san
// 变成 pao 是**学习**的正常结果(你确实反复选了它), 不是保送串味 ——
// 混进来会掩盖真正要抓的"无关的串被改动"。
string[] REG = {
    "7443","74433","744","7468326","426","2426","94826","4864","946","9426",
    "64","948","264","943943","486","926","726726","24263","74664",
    "7426","6426","7264","26426","946426","486426","743643","948264","246426","646426",
};

// ---- 场景 0: 先采基线(这时候"自己的词"还是空的) ----
var baseline = REG.ToDictionary(d => d, d => Top1(d));

// ---- 场景 1: paopaosao(726726726) 按 Y 三次 ----
Head("场景1: 按 Y 拼 paopaosao, 拼两遍, 看第三遍它自己冒不冒出来");
{
    var c = new InputCore { Layout = Layout.CN9, Focus = FocusZone.Keyboard, Visible = true };

    for (int round = 1; round <= 3; round++)
    {
        Type(c, "726726726");
        // 每个音节都得**选中**它再按 Y —— 光标默认在第 0 条, 而第 0 条未必是
        // 我们要的那个音节, 所以这里显式把光标指到目标音节上。
        for (int k = 0; k < 3; k++)
        {
            string want = k < 2 ? "pao" : "sao";
            int idx = c.Segs.FindIndex(s => s.Length > 0 && s[0] == want);
            if (idx < 0)
            {
                Console.WriteLine($"  第 {round} 遍 第 {k + 1} 个音节: 候选里找不到 {want} -> 拼不下去");
                fail++;
                break;
            }
            c.PinyinIdx = idx;
            PressY(c);
        }
        Commit(c);

        int n = T9Engine.UseOf("pao'pao'sao");
        Console.WriteLine($"  第 {round} 遍拼完: SentPinyin 归零, 整串记住次数={n}");

        Type(c, "726726726");
        int mb = c.Segs.Count > 0 ? c.Segs.Min(s => s.Length) : 0;
        foreach (var s in c.Segs.Take(4))
            Console.WriteLine($"           {string.Join("'", s),-16} 块={s.Length} "
                + $"Rank={T9Engine.Rank(s):F0} 排序分={T9Engine.RankOrder(s, mb):F0}"
                + $" 成词={T9Engine.HasPhrase(string.Concat(s))} "
                + $"我的={T9Engine.IsMine(string.Join("'", s))}");
        var top = c.Segs.Take(3).Select(s => string.Join("'", s)).ToList();
        bool has = c.Segs.Any(s => string.Join("'", s) == "pao'pao'sao");
        Console.WriteLine($"         再打一遍 726726726 -> top3: {string.Join("  ", top)}");
        Console.WriteLine($"         候选里有 paopaosao? {(has ? "有" : "【没有】")}");
        // 第 1 遍不算(MYWORD_MIN=2, 拼两次才算数), 第 2 遍起就该有
        if (round >= 2 && !has) { Console.WriteLine("         !! 拼过两次了还进不了候选 -> 失败"); fail++; }
        if (has && string.Join("'", c.Segs[0]) != "pao'pao'sao")
        { Console.WriteLine("         !! 进是进来了, 但没排第一 -> 失败"); fail++; }
    }
}

// ---- 场景 2: 拼一遍就收起(不给上屏), 也该记住 ----
Head("场景2: 按 Y 定完就收起(没按上屏), 整串该算学过");
{
    var c = new InputCore { Layout = Layout.CN9, Focus = FocusZone.Keyboard, Visible = true, Preview = true };
    Type(c, "726726726");
    foreach (var want in new[] { "pao", "pao", "sao" })
    {
        int idx = c.Segs.FindIndex(s => s.Length > 0 && s[0] == want);
        if (idx < 0) { Console.WriteLine("  !! 候选里找不到 " + want); fail++; break; }
        c.PinyinIdx = idx;
        PressY(c);
    }
    int before = T9Engine.UseOf("pao'pao'sao");
    c.DebugFlush();          // 收起: 数字已经发完, 没有"程序替你补发"这回事
    int after = T9Engine.UseOf("pao'pao'sao");
    Console.WriteLine($"  收起前={before} 收起后={after} (该 +1, 以前会被 DropSent 抹成不记)");
    if (after != before + 1) { Console.WriteLine("  !! 收起把学过的一笔抹掉了 -> 失败"); fail++; }
}

// ---- 场景 3: 反过来, 程序替你补发的**不该**算学过 ----
Head("场景3: 数字一个没发就收起(程序替你补发) —— 不该算学过");
{
    var c = new InputCore { Layout = Layout.CN9, Focus = FocusZone.Keyboard, Visible = true, Preview = true };
    Type(c, "726726726");
    int before = T9Engine.UseOf("pao'pao'sao");
    c.DebugFlush();
    int after = T9Engine.UseOf("pao'pao'sao");
    Console.WriteLine($"  收起前={before} 收起后={after} (该不变)");
    if (after != before) { Console.WriteLine("  !! 程序补发的被当成你选的了 -> 失败"); fail++; }
}

// ---- 场景 4: 别的名字也能保送 ----
Head("场景4: 换一个名字(zhangsanfeng)再走一遍, 确认不是只修好了 paopaosao");
{
    // 张三丰: zhang=94264 san=726 feng=3364
    var c = new InputCore { Layout = Layout.CN9, Focus = FocusZone.Keyboard, Visible = true };
    string[] want = { "zhang", "san", "feng" };
    string[] nums = { "94264", "726", "3364" };
    for (int round = 1; round <= 2; round++)
    {
        for (int k = 0; k < want.Length; k++)
        {
            Type(c, nums[k]);
            int idx = c.Segs.FindIndex(s => s.Length > 0 && s[0] == want[k]);
            if (idx < 0) { Console.WriteLine($"  !! {nums[k]} 的候选里找不到 {want[k]}"); fail++; break; }
            c.PinyinIdx = idx;
            PressY(c);
        }
        Commit(c);
    }
    Type(c, "942647263364");
    Console.WriteLine($"  再打一遍 942647263364 -> top3: "
        + string.Join("  ", c.Segs.Take(3).Select(s => string.Join("'", s))));
    bool ok = c.Segs.Any(s => string.Join("'", s) == "zhang'san'feng")
              && string.Join("'", c.Segs[0]) == "zhang'san'feng";
    Console.WriteLine($"  zhangsanfeng 排第一? {(ok ? "是" : "【不是】")}");
    if (!ok) fail++;
}

// ---- 场景 5: 保送不能影响别的数字串(回归) ----
Head("场景5: 回归 —— 学了自造词之后, 30 串无关数字的第一名一个都不许变");
{
    int diff = 0;
    foreach (var d in REG)
    {
        string now = Top1(d);
        if (now != baseline[d])
        {
            Console.WriteLine($"  !! {d}: {baseline[d]} -> {now}");
            diff++;
        }
    }
    // 726726726 自己**应该**变(这正是要修的), 所以它不在 REG 里
    Console.WriteLine($"  变了 {diff} 串 (该是 0)");
    if (diff > 0) fail++;
}

Console.WriteLine(fail == 0 ? "\n全部通过" : $"\n失败 {fail} 处");
return fail == 0 ? 0 : 1;
