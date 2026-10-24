// 滑行输入的专项探针。
// 验的是两件事: ① 摇杆画出来的路径能不能解出正确的数字串;
//                ② 记录 + 标定能不能从这些记录里反推出合适的阈值。
// 全部用项目里真实的那两份源码(Slide.cs / Config.cs), 只有 Config 的写盘
// 被 SetDirForTest 指到临时目录, 碰不到你机器上的真数据。
using Binput;
using System.Text;

int pass = 0, fail = 0;
var bad = new StringBuilder();

void Ok(string name, bool cond, string detail = "")
{
    if (cond) { pass++; Console.WriteLine("  ok   " + name); }
    else { fail++; bad.AppendLine("  FAIL " + name + (detail.Length > 0 ? "  -> " + detail : "")); Console.WriteLine("  FAIL " + name + "  " + detail); }
}

void Eq(string name, string got, string want)
    => Ok(name, got == want, $"得到\"{got}\" 期望\"{want}\"");

// ---------- 方向 -> 键 ----------
Console.WriteLine("\n=== 1) 八个方向分别对应九宫格哪一颗键 ===");
{
    // 九宫格:y 轴向上为正, 和手柄摇杆一致
    //   1 左上  2 正上  3 右上
    //   4 正左  5 中心  6 正右
    //   7 左下  8 正下  9 右下
    (int key, double x, double y)[] cases =
    {
        (6,  1.0,  0.0),
        (3,  0.7,  0.7),
        (2,  0.0,  1.0),
        (1, -0.7,  0.7),
        (4, -1.0,  0.0),
        (7, -0.7, -0.7),
        (8,  0.0, -1.0),
        (9,  0.7, -0.7),
    };
    foreach (var c in cases)
        Ok($"{c.key} 号键的方向", SlideTracker.SectorOf(c.x, c.y) == c.key,
           $"({c.x},{c.y}) -> {SlideTracker.SectorOf(c.x, c.y)}");

    // 扇区容错应该有 ±22.5 度: 正下方偏 20 度仍然认 8
    double a = -90 + 20;
    double ax = Math.Cos(a * Math.PI / 180), ay = Math.Sin(a * Math.PI / 180);
    Ok("偏 20 度还在同一格里", SlideTracker.SectorOf(ax, ay) == 8,
       "得到 " + SlideTracker.SectorOf(ax, ay));

    // 但偏 40 度就该掉进隔壁(右下 9)了
    a = -90 + 40;
    ax = Math.Cos(a * Math.PI / 180); ay = Math.Sin(a * Math.PI / 180);
    Ok("偏 40 度就换到隔壁", SlideTracker.SectorOf(ax, ay) == 9,
       "得到 " + SlideTracker.SectorOf(ax, ay));
}

// ---------- 喂路径的工具 ----------
const int FRAME = 16;          // 60Hz, 和真手柄一个量级

/// <summary>把某个方向上的点位归一化到 -1..1(半径默认推到八成, 模拟真推)。</summary>
static (double X, double Y) Pos(int key, double r = 0.85)
{
    const double D = 0.70710678;
    return key switch
    {
        6 => (r, 0.0),
        3 => (r * D, r * D),
        2 => (0.0, r),
        1 => (-r * D, r * D),
        4 => (-r, 0.0),
        7 => (-r * D, -r * D),
        8 => (0.0, -r),
        9 => (r * D, -r * D),
        _ => (0.0, 0.0),       // 0 = 回中 / 死区
    };
}

/// <summary>在这个方向上待 ms 毫秒, 按 FRAME 逐帧喂进去。</summary>
void Feed(SlideTracker tr, ref DateTime t, int key, int ms)
{
    var (x, y) = Pos(key);
    tr.Update(x, y, t);                 // 进去的那一刻(这一帧用来换扇区)
    int acc = 0;
    while (acc < ms)
    {
        int d = Math.Min(FRAME, ms - acc);
        acc += d;
        t = t.AddMilliseconds(d);
        tr.Update(x, y, t);
    }
}

DateTime T0 = new(2026, 10, 6, 12, 0, 0);

Console.WriteLine("\n=== 2) 慢慢地滑: 每一格都停够, 全都该记下来 ===");
{
    var tr = new SlideTracker { DwellMs = 80 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 2, 220);
    Feed(tr, ref t, 4, 240);
    Feed(tr, ref t, 6, 260);
    tr.Finish(t);
    Eq("慢慢画出来的序列", tr.Seq, "246");
    Ok("三格都判成 KEEP", tr.Steps.Count == 3 && tr.Steps.All(s => s.Verdict == SlideVerdict.Keep),
       string.Join(",", tr.Steps.Select(s => s.Key + s.Tag)));
    Ok("时长记得没错", tr.Steps[0].Ms == 220 && tr.Steps[1].Ms == 240 && tr.Steps[2].Ms == 260,
       string.Join(",", tr.Steps.Select(s => s.Ms)));
}

Console.WriteLine("\n=== 3) 用户实测那个场景: 从 9 到 4 绕了一大圈 ===");
{
    // 他说得很清楚: 滑快了会沿着外围绕过 3、2、1, 但最后停在想去的那一格。
    // 这三格都只蹭到 60 毫秒, 不该留下任何痕迹。
    var tr = new SlideTracker { DwellMs = 80 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 9, 60);
    Feed(tr, ref t, 3, 60);
    Feed(tr, ref t, 2, 60);
    Feed(tr, ref t, 1, 60);      // 2.0.0 起左上角这颗是 JKL, 一样蹭了不算
    Feed(tr, ref t, 4, 300);     // 目的地
    tr.Finish(t);
    Eq("绕路的那一串统统丢掉", tr.Seq, "4");
    Ok("绕过的四格全判 DROP", tr.Steps.Count(s => s.Verdict == SlideVerdict.Drop) == 4,
       string.Join(",", tr.Steps.Select(s => s.Key + s.Tag)));
    // 1 号是被 JKL 换过的那一格: 它现在**有字母**, 录过头漏掉它就会拼不出 ji / li 这些。
    Ok("左上角那颗 1 也在账上", tr.Steps.Any(s => s.Key == 1),
       string.Join(",", tr.Steps.Select(s => s.Key)));
}

Console.WriteLine("\n=== 3b) 换过位的那一格: 左上角停够就出 JKL ===");
{
    // 以前左上角是禁区(那时候 1 号键没字母), 2.0.0 把 JKL 从中间搬到了这里,
    // 这样摇杆绕一圈八个方向都有字母。这一格必须能被滑出来。
    var tr = new SlideTracker { DwellMs = 80 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 1, 200);
    tr.Finish(t);
    Eq("左上角滑一口气化不开", tr.Seq, "1");
    Ok("它是 KEEP, 不是被当禁区丢掉",
       tr.Steps.Count == 1 && tr.Steps[0].Verdict == SlideVerdict.Keep,
       string.Join(",", tr.Steps.Select(s => s.Key + s.Tag)));
}

Console.WriteLine("\n=== 4) 中间连着绕, 两头的键各停一下 ===");
{
    var tr = new SlideTracker { DwellMs = 80 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 9, 55);
    Feed(tr, ref t, 3, 55);
    Feed(tr, ref t, 2, 210);     // 真正想要的第一个键
    Feed(tr, ref t, 4, 260);     // 真正想要的第二个键
    tr.Finish(t);
    Eq("只留下停过的两格", tr.Seq, "24");
}

Console.WriteLine("\n=== 5) 松手不再白送: 停不够就是没有(2.1.34) ===");
{
    // 【这一组是从"白送"整个翻过来的】以前 Finish 会把最后停着的那格无条件补一个键
    // (SlipVerdict.Free): 理由听着贴心, 实际害人 —— ① 想路过一格不留痕做不到,
    // ② 松手和摇杆回中不是同一件事, 补出来的常常不是原意,
    // ③ 同一趟里这一格走另一套判据。现在和中间经过的格一个规矩: 住够才记, 没住够就丢。
    var tr = new SlideTracker { DwellMs = 80 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 4, 30);      // 只待了 30 毫秒就松手
    tr.Finish(t);
    Eq("停不够 松手也不给", tr.Seq, "");
    Ok("它是 DROP 不是 KEEP", tr.Steps.Count == 1 && tr.Steps[0].Verdict == SlideVerdict.Drop,
       string.Join(",", tr.Steps.Select(s => s.Key + s.Tag)));

    // 反过来: 住够了再松手, 还是照给(这时候早在 Update 里就已经 Commit 了,
    // 不需要松手那一手帮忙)。
    tr = new SlideTracker { DwellMs = 80 };
    t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 4, 130);     // 高高兴兴住够了
    tr.Finish(t);
    Eq("住够了照给", tr.Seq, "4");
    Ok("它是 KEEP", tr.Steps.Count == 1 && tr.Steps[0].Verdict == SlideVerdict.Keep,
       string.Join(",", tr.Steps.Select(s => s.Key + s.Tag)));
}

Console.WriteLine("\n=== 6) 在同一格上死等 = 连发 ===");
{
    var tr = new SlideTracker { DwellMs = 80, RepeatMs = 420 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 6, 1000);
    tr.Finish(t);
    Eq("按着不动出三个", tr.Seq, "666");
}

Console.WriteLine("\n=== 6b) 连发间隔现在能拨档(200 ~ 400) ===");
{
    // 【2.1.34】连发间隔以前写死 420, 现在是一份设置, 从 200 到 400 十档。
    // 探针把两头各跑一趟: 间隔小 = 同样一秒蹦得多, 间隔大 = 蹦得少。
    var tr = new SlideTracker { DwellMs = 80, RepeatMs = 200 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 6, 1000);     // 80 起第一个, 之后每 200 一个: 280/480/680/880
    tr.Finish(t);
    Eq("最快那档(200) 一秒出五个", tr.Seq, "66666");

    tr = new SlideTracker { DwellMs = 80, RepeatMs = 400 };
    t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 6, 1000);     // 80 / 480 / 880
    tr.Finish(t);
    Eq("最慢那档(400) 一秒出三个", tr.Seq, "666");

    // 中间那一档(出厂默认)得比最快慢、比最慢快。
    tr = new SlideTracker { DwellMs = 80, RepeatMs = 300 };
    t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 6, 1000);     // 80 / 380 / 680 / 980
    tr.Finish(t);
    Eq("出厂那档(300) 一秒出四个", tr.Seq, "6666");
}

Console.WriteLine("\n=== 7) 回中不许莫名其妙多一个 5 ===");
{
    var tr = new SlideTracker { DwellMs = 80 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 4, 220);
    Feed(tr, ref t, 0, 120);     // 松回中间歇一下
    Feed(tr, ref t, 6, 220);
    tr.Finish(t);
    Eq("中间空一下不影响", tr.Seq, "46");
    Ok("死区没被当成键记进来", !tr.Steps.Any(s => s.Key == 0 || s.Key == 5),
       string.Join(",", tr.Steps.Select(s => s.Key)));
}

Console.WriteLine("\n=== 8) 阈值调高之后, 原来够的也不够了 ===");
{
    var tr = new SlideTracker { DwellMs = 300 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 2, 150);     // 150 < 300, 该被丢掉
    Feed(tr, ref t, 4, 400);     // 400 > 300, 该记下
    tr.Finish(t);
    Eq("门槛提高后只认够久的", tr.Seq, "4");
}

Console.WriteLine("\n=== 9) 没按 LB 的时候不许自己在那记 ===");
{
    var tr = new SlideTracker { DwellMs = 80 };
    var t = T0;
    Feed(tr, ref t, 2, 300);     // 没 Begin 就喂
    Eq("没开始滑动就是空的", tr.Seq, "");
    Ok("Steps 也是空的", tr.Steps.Count == 0);
}

Console.WriteLine("\n=== 10) 一次滑完, 下一次从头再来 ===");
{
    var tr = new SlideTracker { DwellMs = 80 };
    var t = T0;
    tr.Begin(t);
    Feed(tr, ref t, 2, 220);
    tr.Finish(t);
    Eq("第一次", tr.Seq, "2");

    tr.Begin(t);
    Feed(tr, ref t, 6, 220);
    tr.Finish(t);
    Eq("第二次不带着上次的尾巴", tr.Seq, "6");
    Ok("Steps 也只剩这一次", tr.Steps.Count == 1, tr.Steps.Count.ToString());
}


Console.WriteLine($"\n=========== {pass} 项通过, {fail} 项失败 ===========");
if (fail > 0) { Console.WriteLine(bad.ToString()); return 1; }
Console.WriteLine("滑行判定全部正常。");
return 0;
