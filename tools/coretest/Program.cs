using Binput;

/// <summary>
/// 无头驱动 InputCore: 直接喂 PadState, 不走手柄也不走键盘钩子。
/// 用来验证游戏模式的拼音流程(联想 -> LB/RB 选 -> RT 发出)。
/// </summary>
static class Program
{
    static DateTime _now = new DateTime(2026, 1, 1, 12, 0, 0);
    static ushort _btn;
    static short _lx, _ly;
    static byte _rt;          // RT 是扳机不是按钮, 单独驱动
    static short _rx, _ry;    // 右摇杆(符号轮盘)
    static int _fail;

    // ---------- 驱动 ----------
    static void Frame(InputCore c, int ms = 120)
    {
        _now = _now.AddMilliseconds(ms);
        c.Poll(new PadState { Connected = true, Buttons = _btn, LX = _lx, LY = _ly,
                              RightTrigger = _rt, RX = _rx, RY = _ry }, _now);
    }

    static void Tap(InputCore c, ushort b, int ms = 140)
    {
        _btn |= b; Frame(c, ms);        // 这一帧被识别为"刚按下"
        _btn &= (ushort)~b; Frame(c, ms);
    }

    /// <summary>RT: 按一下就是回车。</summary>
    static void RT(InputCore c, int ms = 140)
    {
        _rt = 255; Frame(c, ms);
        _rt = 0; Frame(c, ms);
    }

    /// <summary>十字键右: 换到下一个拼音切分(LB/RB 已经不干这个了)。</summary>
    static void Right(InputCore c) => Tap(c, GamepadInput.DPAD_RIGHT);

    /// <summary>十字键左: 换回上一个拼音切分。</summary>
    static void Left(InputCore c) => Tap(c, GamepadInput.DPAD_LEFT);

    /// <summary>RB: 发出光标选中的那条拼音; 没有待发拼音时它是空格; 符号框里是换类。</summary>
    static void Send(InputCore c) => Tap(c, GamepadInput.RB);

    /// <summary>
    /// 右摇杆推到"更多"扇区(右下角)停够 700ms -> 唤出符号面板。
    /// 扇区号 = floor(((角度+360)%360 + 30) / 60), "更多"是第 5 个, 对应角度 -90..-30。
    /// </summary>
    static void OpenPunct(InputCore c)
    {
        double a = -60.0 * Math.PI / 180.0;
        _rx = (short)(Math.Cos(a) * 32767 * 0.9);
        _ry = (short)(Math.Sin(a) * 32767 * 0.9);
        for (int i = 0; i < 9; i++) Frame(c, 120);      // 9×120 = 1080ms > 700
        _rx = 0; _ry = 0;
        Frame(c, 60);                                   // 回中, 轮盘收起
    }

    static void StickTo(int n)          // 左摇杆推到九宫格 n (1..9)
    {
        int col = (n - 1) % 3, row = (n - 1) / 3;
        double nx = (col - 1) * 0.9, ny = (1 - row) * 0.9;
        _lx = (short)(nx * 32767); _ly = (short)(ny * 32767);
    }

    static void StickCenter() { _lx = 0; _ly = 0; }

    /// <summary>左摇杆推到**任意坐标**(-1..1, y 向上为正)。
    /// 九宫格那 9 个方向用 StickTo 就够, 符号网格是 5×5, 得能推到任意一点。</summary>
    static void StickXY(double nx, double ny)
    {
        _lx = (short)(nx * 32767); _ly = (short)(ny * 32767);
    }

    /// <summary>摇杆推到 n 再按 A = 输入一个九键数字。</summary>
    static void Num(InputCore c, int n)
    {
        StickTo(n);
        Frame(c, 60);
        Tap(c, GamepadInput.A, 60);
        StickCenter();
        Frame(c, 60);
    }

    static void NumSeq(InputCore c, string s)
    {
        foreach (char ch in s) Num(c, ch - '0');
    }

    static InputCore New()
    {
        var c = new InputCore { Visible = true };
        c.SetPreview(true);
        TextOutput.Reset();
        return c;
    }

    static string Cur(InputCore c) =>
        c.Segs.Count == 0 ? "(无)"
            : string.Join("'", c.Segs[Math.Min(c.PinyinIdx, c.Segs.Count - 1)]);

    static List<string> All(InputCore c) =>
        c.Segs.Select(s => string.Join("'", s)).ToList();

    static void Show(InputCore c, string tag)
    {
        var top = c.Segs.Take(6).Select((s, i) =>
            (i == c.PinyinIdx ? "[" + string.Join("'", s) + "]" : string.Join("'", s)));
        Console.WriteLine($"    {tag,-22} 数字=\"{c.Digits}\" 焦点={c.Focus} " +
                          $"选中({c.PinyinIdx + 1}/{c.Segs.Count})={Cur(c)}  前几项: {string.Join(" | ", top)}");
    }

    static void Check(string what, string got, string want)
    {
        bool ok = got == want;
        if (!ok) _fail++;
        Console.WriteLine($"    {(ok ? "OK  " : "FAIL")}  {what}: 得到 \"{got}\"" + (ok ? "" : $" 期望 \"{want}\""));
    }

    static void CheckTrue(string what, bool ok)
    {
        if (!ok) _fail++;
        Console.WriteLine($"    {(ok ? "OK  " : "FAIL")}  {what}");
    }

    // ---------- 用例 ----------
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 有几组会真的写存档, 整份挪到临时目录 ——
        // 真机上那份不碰(比"先备份再还原"稳, 中途崩了也不会留下半截)。
        string _tmp = Path.Combine(Path.GetTempPath(), "coretest_" + Guid.NewGuid().ToString("N")[..8]);
        Config.SetDirForTest(_tmp);

        string dict = Path.Combine(AppContext.BaseDirectory, "dict.txt");
        if (!File.Exists(dict))
            dict = @"C:\Users\<你>\Binput\dict.txt";
        if (File.Exists(dict))
            Console.WriteLine($"词库: {T9Engine.LoadExternalDict(dict)} 条  ({dict})\n");
        else
            Console.WriteLine("没找到 dict.txt, 用内置演示词库\n");

        Case1_WholePhrase();
        Case2_AllReadingsReachable();
        Case3_PinFirstSyllable();
        Case4_RbToEndOpensGrid();
        Case5_RbIsSpaceWhenIdle();
        Case6_FlushOnHide();
        Case7_NeverEmpty();
        Case8_ANotConfirm();
        Case9_RtIsEnter();
        Case10_FitDrivesRowAndGrid();
        Case11_NeverEmptyEvenGibberish();
        Case12_DpadBecomesArrows();
        Case13_XOnlyDeletes();
        Case14_PunctCategories();
        Case15_TwoVersionsSameTyping();
        Case16_FullKeyboard();
        Case17_YFiveJobs();
        Case18_SettingsDpadNav();
        Case19_HotFromDict();
        Case20_LearnsYourPicking();
        Case21_UsePersists();
        Case22_ThemePersists();
        Case23_LayoutFlip();
        Case25_ModePersists();
        Case26_HeadCoverage();
        Case27_LiveFeedback();
        Case28_NumLayerFx();
        Case29_SlideStickRecenter();
        Case30_MineIsThisSplit();
        Case31_WhoSentMoreRanksFirst();
        Case32_PickedIsWhatGetsSent();
        Case33_FitCacheKeyMissesContent();
        Case34_FxStyleTableKeepsUp();
        Case35_KeyShapeTableAndRadius();
        Case36_WakePadL3R3();
        Case37_ResizeWindow();

        Console.WriteLine(_fail == 0 ? "\n全部通过" : $"\n有 {_fail} 项没过");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }

    /// <summary>
    /// 两个版本(预输入版 / 直出版)的打法必须一模一样:
    /// 九键都只把数字解成拼音发给系统输入法, 26 键都只出纯英文。
    /// 唯一的区别是"字去哪儿"(预览框 / 当前焦点窗口), 那不在 InputCore 里。
    /// </summary>
    /// <summary>
    /// 音节热度不再手填, 改成从拼音表统计。这里验的是"统计出来的是人话":
    /// shi/yi/bu 该在前面, den/ei 该在后面, pao 该比 pou 高。
    /// 没在表里出现过的音节走兜底 10+长度。
    /// </summary>
    static void Case19_HotFromDict()
    {
        Console.WriteLine("【19】音节热度: 从拼音表统计, 不用手填");

        CheckTrue($"热度表有内容({T9Engine.HotCount} 个音节)", T9Engine.HotCount > 300);
        CheckTrue("shi 比 den 高", T9Engine.HotOf("shi") > T9Engine.HotOf("den"));
        CheckTrue("yi 比 ei 高", T9Engine.HotOf("yi") > T9Engine.HotOf("ei"));
        CheckTrue("pao 比 pou 高(这才是最想要的那条)", T9Engine.HotOf("pao") > T9Engine.HotOf("pou"));
        CheckTrue("bu 比 nou 高", T9Engine.HotOf("bu") > T9Engine.HotOf("nou"));

        // 兜底: 不是音节的串 -> 10 + 长度
        Check("没学过/不存在的音节走兜底", T9Engine.HotOf("zzzz").ToString("0"), "14");

        // duo 不能被拆成 du+o: 早年试过"所有切法都算", o 会被 -uo 那一堆词顶到第一
        CheckTrue("duo 的热度不是 0(统计时没被拆开)", T9Engine.HotOf("duo") > 0);

        Console.WriteLine($"      shi={T9Engine.HotOf("shi"):F0} yi={T9Engine.HotOf("yi"):F0} "
            + $"bu={T9Engine.HotOf("bu"):F0} de={T9Engine.HotOf("de"):F0} "
            + $"pao={T9Engine.HotOf("pao"):F0} pou={T9Engine.HotOf("pou"):F0} "
            + $"o={T9Engine.HotOf("o"):F0} den={T9Engine.HotOf("den"):F0}");
    }

    /// <summary>
    /// 输入习惯: 挑过并发出去的那条切分, 下次同样这几个数字自动排到前面。
    /// 两个关键约束: ① 加成必须封顶, 打几次之后还能切回别的;
    ///              ② 加成压不过"整串是个词"那 1000 分, 学不出不通顺的切法。
    /// </summary>
    static void Case20_LearnsYourPicking()
    {
        Console.WriteLine("【20】输入习惯: 挑过的切分自动排前面, 且封顶");

        // 排序是"块数优先, 分数其次"(见 CompleteSegs), 所以学习只在同块数那一档里
        // 起作用。素材 24326: bidao(1167) vs cidao(1164), 同是两块、只差 3 分 ——
        // 这种"两种都挺像话"的才是学习该管的区间。
        T9Engine.ClearUse();
        var c = New();
        NumSeq(c, "24326");
        CheckTrue("24326 不止一种切分", c.Segs.Count > 1);
        // 键是**带撇的切法**("cidao"), 不是去撇的连写 —— T9Engine.BumpUse 按这个认人
        string first = string.Join("'", c.Segs[0]);
        double top = T9Engine.Rank(c.Segs[0]);

        int at = -1;
        for (int i = 1; i < c.Segs.Count; i++)
        {
            if (c.Segs[i].Length != c.Segs[0].Length) continue;      // 必须同块数
            if (top - T9Engine.Rank(c.Segs[i]) >= 160) continue;     // 差距要在加成范围内
            at = i; break;
        }
        CheckTrue("找得到一条同块数、差距在 160 以内的切分", at > 0);
        string pick = string.Join("'", c.Segs[at]);
        CheckTrue("挑的不是本来就排第一的那条", pick != first);

        // 连挑 20 次 -> 它该排到最前
        for (int i = 0; i < 20; i++) T9Engine.BumpUse(pick);
        var c2 = New();
        NumSeq(c2, "24326");
        Check("学过的那条排到了第一", string.Join("'", c2.Segs[0]), pick);
        CheckTrue("原来那条没丢, 只是往后挪了",
                  c2.Segs.Any(s => string.Join("'", s) == first));

        // **刻意不封顶**(1.0.3 起): 封顶的话"打了一百次"和"打了五次"会一样高,
        // 打得多的词就压不住打得少的了。改验"冲刺那一段过了之后就只是慢慢涨":
        // 头四次有 SPRINT 的猛涨, 第五次起每次只加 TAIL(3 分)。
        double r1 = T9Engine.Rank(c2.Segs[0]);
        T9Engine.BumpUse(pick);
        double r2 = T9Engine.Rank(c2.Segs[0]);
        Check("第 5 次起每次只涨 TAIL(3 分), 不再是冲刺那段", (r2 - r1).ToString("0.0"), "3.0");

        // 1.0.9 起这条**翻得动了**, 而且正是要翻得动: 用户报的就是"按 Y 拼出来的名字
        // 记不住" —— 整串成不成词(那是词库说了算)不该压过你亲手定过好几次的那条。
        // 现在同一串里你反复挑的那条, 拼够 MYWORD_MIN(2 次)就当它是"你的词", 排到最前。
        T9Engine.ClearUse();
        var c4 = New();
        NumSeq(c4, "7468326");
        string top2 = string.Join("'", c4.Segs[0]);
        string alt = string.Join("'", c4.Segs[c4.Segs.Count - 1]);
        for (int i = 0; i < 20; i++) T9Engine.BumpUse(alt);
        var c5 = New();
        NumSeq(c5, "7468326");
        Check("反复挑的那条能翻过整串成词的那条", string.Join("'", c5.Segs[0]), alt);
        CheckTrue("被翻过去的那条没消失, 只是往后挪了",
                  c5.Segs.Any(s => string.Join("'", s) == top2));

        T9Engine.ClearUse();
        Check("清除后一条不剩", T9Engine.UseCount.ToString(), "0");
        var c3 = New();
        NumSeq(c3, "24326");
        Check("清除后排序回到原样", string.Join("'", c3.Segs[0]), first);
    }

    /// <summary>
    /// 输入习惯得能存住 —— 上回就是因为落盘时机不对, 设置每次开机都被抹掉。
    /// 这里真的会写一次存档, 所以先备份再还原。
    /// </summary>
    static void Case21_UsePersists()
    {
        Console.WriteLine("【21】输入习惯: 存盘再读回来还是原样");

        string file = Config.SavePath;                 // 2.1.5 起: 设置和习惯都在这一份里
        string backup = File.Exists(file) ? File.ReadAllText(file) : null;
        try
        {
            var put = new Dictionary<string, int>
            {
                ["shoudao"] = 17, ["shouda"] = 2, ["zhang"] = 9, ["bidao"] = 1,
            };
            // 第二列是 1.0.7 才加的"最后一次是全局第几次输入", 这里不涉及衰减, 传空即可
            Config.SaveUse(put, new Dictionary<string, int>());
            CheckTrue("存档写出来了", File.Exists(file));

            var got = Config.LoadUse(out var gotLast);
            Check("条数一致", got.Count.ToString(), put.Count.ToString());
            Check("shoudao 的次数", got.GetValueOrDefault("shoudao").ToString(), "17");
            Check("shouda 的次数", got.GetValueOrDefault("shouda").ToString(), "2");
            Check("zhang 的次数", got.GetValueOrDefault("zhang").ToString(), "9");

            // 清掉: 写个空的进去, 读回来也该是空的(而不是崩掉)
            Config.SaveUse(new Dictionary<string, int>(), new Dictionary<string, int>());
            Check("清空后读回来是 0 条", Config.LoadUse(out _).Count.ToString(), "0");

            // 文件坏了也不能连累别的: 随便写点垃圾进去, 读回来最多是没有
            File.WriteAllText(file, "这不是一行正常的数据\n###\n");
            Config.Reload();
            CheckTrue("坏文件读不崩", Config.LoadUse(out _).Count == 0);

            T9Engine.LoadUse(put);
            Check("读进引擎后条数对", T9Engine.UseCount.ToString(), put.Count.ToString());
            T9Engine.ClearUse();
        }
        finally
        {
            if (backup != null) File.WriteAllText(file, backup);
            else if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>
    /// 主题: 存盘再读回来还是原样, 而且写坏了不能连累启动。
    /// 和输入习惯一样, 它也在 %LOCALAPPDATA%\Binput\ 下 —— 换版本(整个目录删掉重放)
    /// 不该把用户挑的主题冲掉。
    /// </summary>
    static void Case22_ThemePersists()
    {
        Console.WriteLine("【22】主题: 存盘再读回来还是原样, 坏文件不连累启动");

        string file = Config.SavePath;
        string backup = File.Exists(file) ? File.ReadAllText(file) : null;
        try
        {
            Config.SaveTheme(5);
            CheckTrue("存档写出来了(主题也在这一份里)", File.Exists(file));
            Check("读回来是 5", Config.LoadTheme().ToString(), "5");

            Config.SaveTheme(0);
            Check("原色是 0", Config.LoadTheme().ToString(), "0");

            // 文件坏了(或者被人手改成别的): 退回原色, 不能抛
            File.WriteAllText(file, "不是数字");
            Config.Reload();
            Check("坏文件退回 0(原色)", Config.LoadTheme().ToString(), "0");
            File.WriteAllText(file, "-3");
            Check("负数也退回 0", Config.LoadTheme().ToString(), "0");
        }
        finally
        {
            if (backup != null) File.WriteAllText(file, backup);
            else if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>
    /// **Y 短按**换九键 / 全键盘(2.3.2 起从 L3 搬过来的): 布局必须真的换过来,
    /// 而且换之前先把"换"这件事交出去 —— 主程序接到的是"动画演到中点才执行"的
    /// 那个动作, 这样界面才能先压扁再展开。没接(无头测试就没接)就直接换, 不能卡住不换。
    /// </summary>
    static void Case23_LayoutFlip()
    {
        Console.WriteLine("【23】Y 短按换布局: 没接翻转回调就直接换, 接了就交给动画中点");

        // 没接回调: 松手就换
        var c = New();
        Check("起始是九键", c.Layout.ToString(), "CN9");
        Tap(c, GamepadInput.Y, 200);
        Check("没接回调: Y 松手直接换成全键盘", c.Layout.ToString(), "EN26");
        Tap(c, GamepadInput.Y, 200);
        Check("再按一次换回九键", c.Layout.ToString(), "CN9");

        // 接了回调: 松手那一帧不能换, 得等 mid 被调用
        var c2 = New();
        int midCalls = 0;
        Action? midGot = null;
        c2.FlipLayout = m => { midCalls++; midGot = m; };   // 只接住, 先不执行(模拟动画还没演到中点)
        Tap(c2, GamepadInput.Y, 200);
        Check("接了回调: 松手时布局还没换(等动画)", c2.Layout.ToString(), "CN9");
        Check("回调被调用了一次", midCalls.ToString(), "1");
        CheckTrue("回调拿到的是“换布局”那个动作", midGot != null);

        // 动画还在演的时候再按 Y: 不理, 免得连按两下翻两次、结果翻回原样
        Tap(c2, GamepadInput.Y, 200);
        Check("动画没演完再按 Y 不重复触发", midCalls.ToString(), "1");

        // 动画演到中点 -> 真正换
        midGot!();
        Check("动画中点执行后换成全键盘", c2.Layout.ToString(), "EN26");

        // 【2.3.3 起 L3 彻底闲着】换布局归 Y 短按(2.3.2), 数字层出 0 和窗口F 解绑
        // 也归了 Y(2.3.3)。这一条守的是"它不会再顺手干点别的"。
        var c3 = New();
        Tap(c3, GamepadInput.L3, 200);
        Check("L3 不再换布局(归 Y 了)", c3.Layout.ToString(), "CN9");
        Console.WriteLine();
    }

    /// <summary>
    /// 版本 / 档位存在存档里(2.1.5 起不再单独一个文件): 存进去读出来还是原样。
    /// </summary>
    static void Case25_ModePersists()
    {
        Console.WriteLine("【25】版本/档位: 存进去读出来还是原样");

        string file = Config.SavePath;
        bool had = File.Exists(file);
        string backup = had ? File.ReadAllText(file) : "";
        try
        {
            Config.SaveMode(true, 3);
            var m = Config.LoadMode();
            CheckTrue("预输入版读回来", m.Preview);
            Check("档位 FF14=3", m.Tier.ToString(), "3");

            // 2.1.5 起版本和档位都在存档里, 不再单独一个文件, 也不再去认
            // 1.0.1 那种三字段的老格式 —— 没正式发出去过, 不背这个包袱。
            Config.Reload();
            var o = Config.LoadMode();
            CheckTrue("重开之后版本还在", o.Preview);
            Check("重开之后档位还是 3", o.Tier.ToString(), "3");
        }
        finally
        {
            if (had) File.WriteAllText(file, backup);
            else if (File.Exists(file)) File.Delete(file);
        }
        Console.WriteLine();
    }

    static void Case15_TwoVersionsSameTyping()
    {
        Console.WriteLine("【15】预输入版 / 直出版: 打法一致, 九键只出拼音, 26 键只出英文");

        string Type9(bool preview)
        {
            var c = new InputCore { Visible = true };
            c.SetPreview(preview);
            TextOutput.Reset();
            NumSeq(c, "7468326");
            Send(c);                 // RB: 发出 shou'dao
            Send(c);                 // RB 第二下: 空格上屏
            return TextOutput.Flatten();
        }

        string a = Type9(true), b = Type9(false);
        Check("预输入版 九键发出拼音再上屏", a, "shou'daoSPACE");
        Check("直出版 九键一模一样", b, a);

        // 26 键: 返回"记下来的那一条原始记录", 好看清走的是 UNICODE 还是虚拟键
        string Raw26(bool preview)
        {
            var c = new InputCore { Visible = true };
            c.SetPreview(preview);
            c.Layout = Layout.EN26;
            c.SyncIme();
            TextOutput.Reset();
            // 全键盘第二排字母的 f(默认起始位)
            c.Row26 = Keyboard26.HomeRow; c.Col26 = 4;
            Tap(c, GamepadInput.A);
            return TextOutput.Sent.Count == 1 ? TextOutput.Sent[0] : "(没发出去)";
        }

        string e1 = Raw26(true), e2 = Raw26(false);
        Console.WriteLine($"    26 键发出: {e1}");
        Check("26 键是 UNICODE 直投(纯字母, 不过输入法)", e1,
              "U:" + Keyboard26.At(Keyboard26.HomeRow, 4).Ch);
        Check("直出版 26 键也一样", e2, e1);
        Console.WriteLine();
    }

    // ---------- 26 键全键盘的测试台 ----------
    /// <summary>按字样找一个键(比如 "2" / "▲" / "Space"), 返回它在第几排第几列。</summary>
    static (int Row, int Col) FindKey(string label)
    {
        for (int r = 0; r < Keyboard26.RowCount; r++)
            for (int col = 0; col < Keyboard26.Len(r); col++)
                if (Keyboard26.At(r, col).Label == label) return (r, col);
        return (-1, -1);
    }

    /// <summary>
    /// 在全键盘上按一下某个键, 返回真正发出来的那条记录。
    /// lb=true 表示"按 A 的同时 LB 是按住的" —— 那就是真键盘按住 Shift 打一下。
    /// </summary>
    static string Hit26(string label, bool lb = false)
    {
        _btn = 0; _rt = 0; _rx = 0; _ry = 0; StickCenter();
        var c = new InputCore { Visible = true };
        c.SetPreview(false);
        c.Layout = Layout.EN26;
        c.SyncIme();
        TextOutput.Reset();

        var (r, col) = FindKey(label);
        if (r < 0) return "(没这个键)";
        c.Row26 = r; c.Col26 = col;

        if (lb) { _btn |= GamepadInput.LB; Frame(c, 60); }
        Tap(c, GamepadInput.A);
        if (lb) { _btn = (ushort)(_btn & ~GamepadInput.LB); Frame(c, 60); }
        return TextOutput.Sent.Count == 1 ? TextOutput.Sent[0] : "(没发出去:" + TextOutput.Sent.Count + ")";
    }

    static void Case16_FullKeyboard()
    {
        Console.WriteLine("【16】26 键 = 笔记本全键盘: 数字行 / F1~F12 / Tab / 退格 / 方向键 / LB=Shift");

        // 1) 排布: 该有的都得有, 且键数在"笔记本 84 键"这个量级
        int total = 0;
        for (int r = 0; r < Keyboard26.RowCount; r++) total += Keyboard26.Len(r);
        Console.WriteLine($"    一共 {Keyboard26.RowCount} 排 {total} 键");
        CheckTrue("键数在 75~90 之间(84 键笔记本那个量级)", total >= 75 && total <= 90);
        CheckTrue("有数字行(1~0)", FindKey("1").Row >= 0 && FindKey("0").Row >= 0);
        CheckTrue("有 F1~F12", FindKey("F1").Row >= 0 && FindKey("F12").Row >= 0);
        CheckTrue("有 Esc / Tab / Caps / 回车 / 退格",
            FindKey("Esc").Row >= 0 && FindKey("Tab").Row >= 0 && FindKey("Caps").Row >= 0
            && FindKey("Enter").Row >= 0 && FindKey("Back").Row >= 0);
        CheckTrue("有方向键和空格",
            FindKey("▲").Row >= 0 && FindKey("▼").Row >= 0 && FindKey("Space").Row >= 0);

        // 2) 按住 LB = 按住 Shift: 数字键出上档符号, 字母出大写
        Check("数字键 2 单独按 = 2", Hit26("2"), "U:2");
        Check("按住 LB 打 2 = @(真键盘的 Shift+2)", Hit26("2", lb: true), "U:@");
        Check("按住 LB 打 3 = #", Hit26("3", lb: true), "U:#");
        Check("字母 q 单独按 = 小写", Hit26("q"), "U:q");
        Check("按住 LB 打 q = 大写 Q", Hit26("q", lb: true), "U:Q");

        // 3) 功能键走虚拟键
        Check("退格 = VK BACK", Hit26("Back"), "VK:BACK");
        Check("回车 = VK ENTER", Hit26("Enter"), "VK:ENTER");
        Check("Tab = VK TAB", Hit26("Tab"), "VK:TAB");
        Check("F5 = VK F5", Hit26("F5"), "VK:F5");
        Check("▲ = VK UP", Hit26("▲"), "VK:UP");

        // 4) 空间键
        Check("空格", Hit26("Space"), "U: ");
        Console.WriteLine();
    }

    /// <summary>7468326 -> LB/RB 选 -> RT 一次发两个字的拼音给输入法。</summary>
    static void Case1_WholePhrase()
    {
        Console.WriteLine("【1】联想整段: 7468326 -> 十字键左右选 -> RB 发到输入法");
        var c = New();
        NumSeq(c, "7468326");
        Show(c, "打完 7 个数字");
        CheckTrue("联想出了 shou'dao 这类两字串",
            c.Segs.Any(s => s.Length == 2 && string.Concat(s) == "shoudao"));

        Right(c); Show(c, "十字键右 往后一个");
        string afterRb = Cur(c);
        Left(c); Show(c, "十字键左 翻回来");
        Check("十字键左能翻回右之前那个", Cur(c), "shou'dao");
        CheckTrue("十字键右确实换了一个切分", afterRb != "shou'dao");
        CheckTrue("焦点始终留在九宫格(不抢焦点)", c.Focus == FocusZone.Keyboard);

        TextOutput.Reset();
        Send(c);
        Check("RB 发给输入法的整串", TextOutput.Flatten(), "shou'dao");
        Check("已发拼音累计", c.SentPinyin, "shou'dao");
        Check("数字已清空", c.Digits, "");
        Console.WriteLine();
    }

    /// <summary>726 这种六个读音的, 必须每个都选得到(pao / sao 之前选不到就是这个用例)。</summary>
    static void Case2_AllReadingsReachable()
    {
        Console.WriteLine("【2】726 六种读音: 行里摆得下的直接选, 摆不下的在展开网格里");
        var c = New();
        NumSeq(c, "726");
        c.PinyinFit = 4;              // 模拟界面量出来"这一行放得下 4 个"
        Show(c, "打完 3 个数字");

        var row = c.PinyinRowItems();
        Console.WriteLine("    行里显示的: " + string.Join(" | ", row));
        CheckTrue("行里摆 4 个", row.Count == 4);
        // 不写死是哪四个: 排序现在是从拼音表统计出来的, 字典一换顺序就可能变。
        // 要验的是"行里摆的就是排最前的四个", 以及六种读音全都够得到。
        var top4 = c.Segs.Take(4).Select(s => string.Concat(s)).ToList();
        CheckTrue("行里摆的就是排最前的 4 个", row.SequenceEqual(top4));

        var seen = new List<string>();
        for (int i = 0; i < 4; i++) { seen.Add(Cur(c)); Right(c); }
        Console.WriteLine("    行里 RB 转一圈: " + string.Join(" | ", seen));
        foreach (var want in top4)
            CheckTrue($"行里能选到 {want}", seen.Contains(want));

        CheckTrue("行内最后一个再 RB -> 展开网格", c.GridPinyin && c.Focus == FocusZone.CandGrid);
        var grid = c.GridItems();
        Console.WriteLine("    网格里: " + string.Join(" | ", grid.Take(6)));
        // 1.0.8 起网格**只装行里摆不下**的那几条 —— 行里那四个不该在网格里再出现一遍
        // (以前是全量重摆一遍, 同一条拼音行和网格里各有一份, 看着像同时选中了两个)。
        // 所以这里不再写死"六种读音都在网格里", 改验"不重复 + 合起来一条不落"。
        foreach (var want in top4)
            CheckTrue($"行里那条不在网格里重复出现 {want}", !grid.Contains(want));
        CheckTrue("行 + 网格 = 全部候选, 一条不落",
                  row.Count + grid.Count == c.Segs.Count);

        Tap(c, GamepadInput.B);
        CheckTrue("B 退回九宫格", c.Focus == FocusZone.Keyboard);
        Console.WriteLine("    全部候选: " + string.Join(" | ", All(c)));
        // 别写成 while(...) 死等 —— 万一哪天 pao 排不进这一行, 探针就挂在这儿不吭声,
        // 看着像"卡住了", 其实是一条断言永远没跑到。翻不到就明确报出来。
        int guard = 0;
        while (Cur(c) != "pao" && guard++ < 60) Left(c);
        Check("行里翻得到 pao", Cur(c), "pao");
        TextOutput.Reset();
        Send(c);
        Check("选 pao 后 RB 发的", TextOutput.Flatten(), "pao");
        Console.WriteLine();
    }

    /// <summary>
    /// 整段联想里没有想要的: 用 Y 只定住头一个音节,
    /// 剩下的数字自动重新切分出第二个字的可能读音。
    /// </summary>
    static void Case3_PinFirstSyllable()
    {
        Console.WriteLine("【3】整段联想都不是我要的: Y 只定住第一个字, 剩下的自动联想第二个字");
        var c = New();
        NumSeq(c, "7468326");
        Show(c, "打完 7468326");

        for (int i = 0; i < 6; i++) Right(c);
        Show(c, "翻到别的切分");

        string whole = Cur(c);
        TextOutput.Reset();
        Tap(c, GamepadInput.Y);              // Y = 只发头一个音节
        Show(c, "按 Y 之后");
        string sent = TextOutput.Flatten();
        Console.WriteLine($"    整段是 {whole}, Y 只发了: {sent}");
        Check("Y 只发头一个音节", sent, whole.Split('\'')[0]);
        CheckTrue("发的不含撇(没把整串发出去)", !sent.Contains('\''));
        CheckTrue("剩下的数字还在, 没被吃掉", c.Digits.Length > 0);
        CheckTrue("剩下的数字重新切出了第二个字的读音",
            c.Segs.Count > 0 && c.Segs.All(s => s.All(y => y.All(char.IsAsciiLetter))));
        Console.WriteLine("    第二个字可选: " + string.Join(" | ", All(c).Take(6)));

        string second = Cur(c);
        TextOutput.Reset();
        Send(c);
        Check("再按 RB 前面自动补撇", TextOutput.Flatten(), "'" + second);
        Check("累计成完整一串", c.SentPinyin, sent + "'" + second);
        Console.WriteLine();

        // 九宫格**正中心那颗(5)**没有字母: 长按它是开窗口F, 短按什么也不做。
        // 以前空着的是左上角那颗 1 —— 2.0.0 把 JKL 搬去左上角之后, 轮到中心空出来了。
        // 规矩还是那一条: **空着比误触好**。摇杆一松手本来就停在中间,
        // 要是"回中 + A"也算一个键, 滑完顺手按一下就多一个数字。
        Console.WriteLine("    九宫格的 5 键: 无功能(长按 = 开窗口F)");
        var c2 = New();
        NumSeq(c2, "7468326");
        string d2 = c2.Digits, w2 = Cur(c2);
        TextOutput.Reset();
        Num(c2, 5);
        Check("按中心键什么都不发", TextOutput.Flatten(), "");
        Check("按中心键数字串没变(没被多打一个 5)", c2.Digits, d2);
        CheckTrue("按中心键之后待发的还是原样", Cur(c2) == w2);
        CheckTrue("短按也不会把窗口F 弹出来", !c2.QuickMode);
        Console.WriteLine();
    }

    /// <summary>RB 一路往右翻到底 -> 展开全部拼音(不再用 ↑)。</summary>
    static void Case4_RbToEndOpensGrid()
    {
        Console.WriteLine("【4】RB 翻到最后一个 -> 展开全部拼音网格, RT 发出");
        var c = New();
        NumSeq(c, "7468326");
        c.PinyinFit = 3;              // 这一行只摆得下 3 个
        for (int i = 0; i < 3; i++) Right(c);
        Console.WriteLine($"    翻了 3 次 RB 后: 焦点={c.Focus} 网格装拼音={c.GridPinyin} " +
                          $"光标 {c.GridIdx + 1}/{c.Segs.Count}");
        CheckTrue("展开了拼音网格", c.GridPinyin && c.Focus == FocusZone.CandGrid);
        CheckTrue("网格列数降到 3(放得下整串)", c.GridColsNow == 3);

        // 网格只装行外那几条, 下标有个 GridOffset 的偏移 —— 光标那一条要问 SegIdxNow,
        // 不能直接拿 GridIdx 去 Segs 里取(1.0.8 的"按 Y 切错一条"就是这么来的)
        string want = string.Join("'", c.Segs[c.SegIdxNow]);
        TextOutput.Reset();
        Send(c);
        Check("网格里 RB 发的", TextOutput.Flatten(), want);
        CheckTrue("发完退回九宫格", c.Focus == FocusZone.Keyboard && !c.GridPinyin);
        Console.WriteLine();
    }

    /// <summary>没有待发拼音时 RB 仍是空格(不然丢了这个快捷上屏)。</summary>
    static void Case5_RbIsSpaceWhenIdle()
    {
        Console.WriteLine("【5】RB 发拼音 -> 紧接着再按一下就是空格(上屏第一个候选)");
        var c = New();
        TextOutput.Reset();
        Tap(c, GamepadInput.RB);
        Check("待发为空时 RB 是空格", TextOutput.Flatten(), "SPACE");

        // 打了拼音: 第一下 RB 发出去, 第二下 RB 就是空格上屏
        var c2 = New();
        NumSeq(c2, "4864");
        TextOutput.Reset();
        Send(c2);
        Check("第一下 RB 发出拼音", TextOutput.Flatten(), "hu'ni");
        TextOutput.Reset();
        Send(c2);
        Check("第二下 RB 变成空格(上屏首选)", TextOutput.Flatten(), "SPACE");
        Console.WriteLine();
    }

    /// <summary>收起前要补发拼音 + 补上屏, 而且不能抛异常。</summary>
    static void Case6_FlushOnHide()
    {
        Console.WriteLine("【6】收起前 Flush: 半截数字要补发, 挂着的拼音要补空格上屏");
        var c = New();
        NumSeq(c, "4864");
        Show(c, "打完 4864 没收尾");
        TextOutput.Reset();
        try
        {
            c.Flush();
            CheckTrue("Flush 没抛异常", true);
        }
        catch (Exception ex)
        {
            CheckTrue("Flush 没抛异常 (抛了: " + ex.GetType().Name + ": " + ex.Message + ")", false);
        }
        Console.WriteLine("    Flush 发出: " + TextOutput.Flatten());
        Check("Flush 后没有残留数字", c.Digits, "");
        Check("Flush 后没有挂着半截拼音", c.SentPinyin, "");
        Console.WriteLine();
    }

    /// <summary>拼到一半也不能空: 7 -> p q r s, 94 -> 三种读音, 949 -> yi'w ...</summary>
    static void Case7_NeverEmpty()
    {
        Console.WriteLine("【7】拼到一半拼音候选也不能空");
        var c = New();

        Num(c, 7);
        Show(c, "按了 7");
        CheckTrue("7 -> 有候选(不是空)", c.Segs.Count > 0);
        Check("7 的第一个候选", All(c).FirstOrDefault() ?? "", "p");
        CheckTrue("7 出的是 p q r s", All(c).Take(4).SequenceEqual(new[] { "p", "q", "r", "s" }));

        Num(c, 2);
        Show(c, "再按 2 (72)");
        CheckTrue("72 出的是 pa sa", All(c).Take(2).SequenceEqual(new[] { "pa", "sa" }));

        var c2 = New();
        Num(c2, 9);
        Show(c2, "按了 9");
        CheckTrue("9 出的是 w x y z", All(c2).Take(4).SequenceEqual(new[] { "w", "x", "y", "z" }));

        Num(c2, 4);
        Show(c2, "再按 4 (94)");
        // 同样不写死顺序(现在按音节常用度排), 只要求三种读音都在、且不为空
        var three = All(c2).Take(3).ToList();
        CheckTrue("94 出的是 yi xi zi 三种",
                  three.Contains("yi") && three.Contains("xi") && three.Contains("zi"));

        Num(c2, 9);
        Show(c2, "再按 9 (949)");
        CheckTrue("949 不是空", c2.Segs.Count > 0);
        foreach (var want in new[] { "yi'w", "xi'w", "zi'w" })
            CheckTrue($"949 里有 {want}", All(c2).Contains(want));

        // 拼一半也能直接发出去(输入法会自己往下补)
        TextOutput.Reset();
        Send(c2);
        Console.WriteLine("    拼一半直接 RB 发出: " + TextOutput.Flatten());
        CheckTrue("拼一半也能发(不会卡住)", TextOutput.Flatten().Length > 0);
        Console.WriteLine();
    }

    /// <summary>A 只能输入数字, 不能再当"确认拼音"。
    /// 顺带验了换位的后遗症: 摇杆回中停的是**没有字母的中心格**, 所以干按一下 A 不该出数字。</summary>
    static void Case8_ANotConfirm()
    {
        Console.WriteLine("【8】A 只输入数字, 不再确认拼音");
        var c = New();
        NumSeq(c, "7468326");
        string before = Cur(c);
        TextOutput.Reset();
        Tap(c, GamepadInput.A);          // 光标停在中心(5) -> 什么都不该发生
        Show(c, "回中时按 A 之后");
        Check("A 没有发拼音出去", TextOutput.Flatten(), "");
        Check("回中的时候 A 不打字", c.Digits, "7468326");

        StickTo(2); Frame(c, 60);        // 挪到一颗有字母的格子上
        Tap(c, GamepadInput.A, 60);
        StickCenter(); Frame(c, 60);
        Show(c, "挪到 2 号格再按 A");
        Check("挪到 2 号格按 A, 打出来的就是 2", c.Digits, "74683262");
        CheckTrue("拼音跟着变了(这正是要躲开的坑)", Cur(c) != before);
        Console.WriteLine();
    }

    /// RT 现在是"按一下就是回车"(以前的"短按候选右移 + 长按回车"拆成了:
    /// 候选右移归十字键, 回车归 RT 短按, 长按进度条也删了)。
    /// 要验的就是: 一下回车、不碰拼音、按久了也不会连发。
    static void Case9_RtIsEnter()
    {
        Console.WriteLine("【9】RT 按一下就是回车(不再分长短按, 也不再动候选)");
        var c = New();
        NumSeq(c, "7468326");
        TextOutput.Reset();

        RT(c, 140);                      // 短按
        Console.WriteLine("    RT 发出: " + TextOutput.Flatten());
        Check("RT 就是回车", TextOutput.Flatten(), "ENTER");
        CheckTrue("拼音没被顺手发出去", c.Digits == "7468326" && c.SentPinyin.Length == 0);
        CheckTrue("也没跳进候选行", c.Focus == FocusZone.Keyboard);

        // 按住很久也只能有一个回车, 不能连发
        TextOutput.Reset();
        _rt = 255;
        for (int i = 0; i < 6; i++) Frame(c, 120);   // 按满 720ms
        _rt = 0; Frame(c, 120);
        Check("长按也只有一个回车", TextOutput.Flatten(), "ENTER");

        // 发拼音归 RB
        TextOutput.Reset();
        Send(c);
        Check("RB 发的", TextOutput.Flatten(), "shou'dao");
        Console.WriteLine();
    }

    /// <summary>
    /// 拼音行摆几个由界面量出来的 PinyinFit 决定:
    /// 摆得下就只在这几个里转, 最后一个再往右就展开全部; 摆得下全部时别弹网格。
    /// </summary>
    static void Case10_FitDrivesRowAndGrid()
    {
        Console.WriteLine("【10】行里摆几个由量出来的 fit 决定, 最后一个再往右才展开");
        var c = New();
        NumSeq(c, "7468326");            // 24 种切分
        c.PinyinFit = 3;                 // 界面量出来: 这一行只放得下 3 个
        Check("摆 3 个时行里就 3 条", c.PinyinRowItems().Count.ToString(), "3");
        Check("行起点固定为 0(不再横向滚动)", c.PinyinRowStart().ToString(), "0");

        Right(c); Right(c);
        Check("十字键右两次到第 3 个", c.PinyinIdx.ToString(), "2");
        CheckTrue("这时候还不该展开", !c.GridPinyin);
        Right(c);
        CheckTrue("第 3 个再往右 -> 展开全部", c.GridPinyin && c.Focus == FocusZone.CandGrid);
        // 展开之后接着按十字键右要能继续往后走(以前 RB 到这儿就断了, 只能换十字键)
        int g0 = c.GridIdx;
        Right(c);
        CheckTrue("展开后十字键右还能继续往后", c.GridIdx > g0);
        Console.WriteLine();

        // 反过来: 一行摆得下全部的时候, 不该多此一举弹网格
        Console.WriteLine("    行里摆得下全部时不弹网格");
        var c2 = New();
        NumSeq(c2, "726");
        // 【2.1.19】现在是 8 种: pan pao ran rao san sao + pa'o sa'o。
        // 后两条以前**根本没有**: 去重键当时用的是"字母串", pa'o 和 pao 撞成一条,
        // 后到的 pa'o 被整条丢掉 —— 于是想让第一个字是 pa 的人从头翻到尾也选不出来。
        // 可撇是真会发给输入法的(见 SendPinyin), 切法不同就是两条候选, 一条都不能省。
        int n2 = c2.Segs.Count;
        Check("726 是 8 种切分", n2.ToString(), "8");
        CheckTrue("pa 打头的那一条在候选里", c2.Segs.Any(s => s[0] == "pa"));
        CheckTrue("sa 打头的那一条在候选里", c2.Segs.Any(s => s[0] == "sa"));
        c2.PinyinFit = 8;
        Check("摆得下时行里就是全部 8 条", c2.PinyinRowItems().Count.ToString(), "8");
        for (int i = 0; i < n2; i++) Right(c2);
        CheckTrue("都摆得下就不弹网格", !c2.GridPinyin && c2.Focus == FocusZone.Keyboard);
        CheckTrue("十字键右在行内循环(回到头一个)", c2.PinyinIdx == 0);
        Console.WriteLine();
    }

    /// <summary>
    /// 拼音行一条都不能少: 连按 4444 这种 g/h/i 怎么组都不是音节的,
    /// 以前拼音行会整片空掉。现在兜底成"按键上的字母硬拼", 至少不空、也能发出去。
    /// </summary>
    static void Case11_NeverEmptyEvenGibberish()
    {
        Console.WriteLine("【11】凑不出音节的数字串(4444 / 99 …)拼音行也不能空");
        foreach (var s in new[] { "4", "44", "444", "4444", "9", "99", "999",
                                  "2", "22", "222222", "7", "72", "94", "949" })
        {
            var c = New();
            NumSeq(c, s);
            var items = c.PinyinRowItems();
            bool ok = items.Count > 0;
            if (!ok) _fail++;
            Console.WriteLine($"    {(ok ? "OK  " : "FAIL")}  {s,-7} -> " +
                              (ok ? string.Join(" | ", items.Take(6)) : "(空!)"));
        }

        // 兜底出来的串要能真的发出去
        var c2 = New();
        NumSeq(c2, "4444");
        string first = c2.PinyinRowItems()[0];
        TextOutput.Reset();
        Send(c2);
        Check("4444 的第一条能发出去", TextOutput.Flatten(), first);
        Console.WriteLine();
    }

    /// <summary>
    /// 拼音发出去之后, 系统输入法自己的候选窗就弹出来了 —— 这时候十字键直接当
    /// 键盘方向键(左右选词 / 上下翻页), 词库联想全交给它。
    /// 但没发拼音、或者又在拼新数字的时候, 十字键的左右还得回去管拼音切分。
    /// </summary>
    /// <summary>
    /// 十字键的新规矩: **只有"拼音行正摆着好几种切分"时才挑拼音, 其余一律发方向键。**
    /// 以前拼数字时左右是换切分、26 键时四个方向是挪光标, 想回头改中间某个字
    /// 只能一路退格删过去。现在除那一种情况外上下左右都直接发方向键。
    /// </summary>
    static void Case12_DpadBecomesArrows()
    {
        Console.WriteLine("【12】十字键: 挑拼音时才挑拼音, 其余一律方向键");

        // 1) 还在拼数字、拼音行有好几种切分: 左右换切分, 一个方向键都不发
        var c = New();
        NumSeq(c, "4864");
        Show(c, "打完 4864(还没发)");
        CheckTrue("拼着数字 -> 归拼音(PinyinPicking)", c.PinyinPicking);
        TextOutput.Reset();
        Right(c);
        Check("拼数字时十字键右不发方向键", TextOutput.Flatten(), "");
        CheckTrue("拼数字时十字键右还是换切分", c.PinyinIdx > 0);
        Left(c);

        // 2) 发出去之后(输入法候选窗开着): 四个方向都是方向键
        TextOutput.Reset();
        Send(c);
        Check("RB 发出拼音", TextOutput.Flatten(), "hu'ni");
        CheckTrue("发完 -> 进入选词中", c.ImePicking);
        CheckTrue("提示行也跟着变", c.StatusDisplay().Contains("选词中"));

        TextOutput.Reset();
        Tap(c, GamepadInput.DPAD_RIGHT);
        Check("十字键右 -> 键盘右", TextOutput.Flatten(), "RIGHT");
        TextOutput.Reset();
        Tap(c, GamepadInput.DPAD_LEFT);
        Check("十字键左 -> 键盘左", TextOutput.Flatten(), "LEFT");
        TextOutput.Reset();
        Tap(c, GamepadInput.DPAD_UP);
        Check("十字键上 -> 键盘上", TextOutput.Flatten(), "UP");
        TextOutput.Reset();
        Tap(c, GamepadInput.DPAD_DOWN);
        Check("十字键下 -> 键盘下", TextOutput.Flatten(), "DOWN");

        // 3) 又按了新数字、拼音行又有好几种切分 -> 十字键回去挑拼音
        Num(c, 7);                      // 7 -> p q r s, 四种可挑
        Show(c, "又按了 7");
        CheckTrue("又开始拼数字 -> 再归拼音", c.PinyinPicking);
        TextOutput.Reset();
        Right(c);
        Check("拼新数字时十字键右又不是方向键了", TextOutput.Flatten(), "");

        // 3b) 【2.1.19】按一颗键时拼音行不能再只有一种。
        //     以前 2 只给 a(它是 2 上唯一能成音节的字母), 等于替你决定了;
        //     想打 bi / ci 的人在这一步一步都走不出去。而 7 那种键上没有单字母
        //     音节的走的却是另一条路, p q r s 四个全给 —— 同一个动作两种结果,
        //     看着就是"有的键显示不全"。现在一并补上键上每一个字母,
        //     于是它**有得挑**, 十字键右就该继续挑拼音, 不发方向键。
        var c6 = New();
        Num(c6, 2);
        Check("按 2 时键上三个字母都在", string.Join("|", All(c6)), "a|b|c");
        CheckTrue("有得挑 -> 算挑拼音", c6.PinyinPicking);
        TextOutput.Reset();
        Right(c6);
        Check("按 2 时十字键右是挑拼音(不是方向键)", TextOutput.Flatten(), "");

        // 3c) 反过来, 真只有一种切分的时候十字键还是要回到方向键 ——
        //     不然按下去没反应, 还白白挡着挪光标。(211 整串拼不出完整音节,
        //     只补得出 a'j 一条, 见 CompleteSegs 里 PartTails 那一段。)
        var c7 = New();
        NumSeq(c7, "211");
        CheckTrue("211 只补得出一条", c7.Segs.Count == 1);
        CheckTrue("只有一种切分 -> 不算挑拼音", !c7.PinyinPicking);
        TextOutput.Reset();
        Tap(c7, GamepadInput.DPAD_RIGHT);
        Check("那种情况下十字键右 = 方向键", TextOutput.Flatten(), "RIGHT");

        // 4) 空格上屏、这一句拼音结束之后: 十字键回到方向键。
        //    这才是这次改的重点 —— 上屏完想改前面那个字, 直接按左键挪回去就行。
        var c2 = New();
        NumSeq(c2, "4864");
        Send(c2);
        CheckTrue("c2 进入选词中", c2.ImePicking);
        TextOutput.Reset();
        Send(c2);                       // 第二下 RB = 空格上屏
        Check("上屏发出空格", TextOutput.Flatten(), "SPACE");
        CheckTrue("上屏后不算选词中(候选窗关了)", !c2.ImePicking);
        CheckTrue("上屏后也不算挑拼音(数字已清空)", !c2.PinyinPicking);
        TextOutput.Reset();
        Right(c2);
        Check("上屏后十字键右 = 方向键(能挪回去改字)", TextOutput.Flatten(), "RIGHT");
        TextOutput.Reset();
        Tap(c2, GamepadInput.DPAD_UP);
        Check("上屏后十字键上 = 方向上", TextOutput.Flatten(), "UP");

        // 5) 九键啥也没拼(刚打开 / 刚清空): 十字键也是方向键
        var c4 = New();
        CheckTrue("空状态既不选词也不挑拼音", !c4.ImePicking && !c4.PinyinPicking);
        TextOutput.Reset();
        Tap(c4, GamepadInput.DPAD_LEFT);
        Check("九键空状态十字键左 = 方向键", TextOutput.Flatten(), "LEFT");

        // 6) 26 键: 十字键是方向键, 选键只剩摇杆(键盘上本来也有方向键)
        var c5 = New();
        c5.Layout = Layout.EN26;
        c5.SyncIme();
        int row0 = c5.Row26, col0 = c5.Col26;
        TextOutput.Reset();
        Tap(c5, GamepadInput.DPAD_RIGHT);
        Check("26 键十字键右 = 方向键", TextOutput.Flatten(), "RIGHT");
        CheckTrue("26 键十字键不再挪键盘光标", c5.Row26 == row0 && c5.Col26 == col0);
        CheckTrue("26 键的提示行写明十字键是方向键",
            c5.StatusDisplay().Contains("十字键=方向键") && c5.StatusDisplay().Contains("摇杆选键"));

        // 7) 直出版也一样: 两个版本的打法是同一套
        var c3 = new InputCore { Visible = true };
        NumSeq(c3, "4864");
        Send(c3);
        CheckTrue("直出版同样进选词中", c3.ImePicking);
        TextOutput.Reset();
        Right(c3);
        Check("直出版选词时十字键右也是方向键", TextOutput.Flatten(), "RIGHT");
        Console.WriteLine();
    }

    /// <summary>
    /// X 只管删, 不管退。"全部拼音" / "全部符号"里按一下 X 就把整个列表关掉,
    /// 想接着挑还得重新展开一遍 —— 退出一律改由 B 负责。
    /// </summary>
    static void Case13_XOnlyDeletes()
    {
        Console.WriteLine("【13】X 只删除不退出(全部拼音 / 全部符号), 退出归 B");

        // 1) 全部拼音网格
        var c = New();
        NumSeq(c, "7468326");
        c.PinyinFit = 3;
        for (int i = 0; i < 3; i++) Right(c);
        CheckTrue("先展开全部拼音网格", c.GridPinyin && c.Focus == FocusZone.CandGrid);
        int segs0 = c.Segs.Count, grid0 = c.GridIdx;

        TextOutput.Reset();
        Tap(c, GamepadInput.X);
        Console.WriteLine($"    按 X: 数字 \"7468326\" -> \"{c.Digits}\", " +
                          $"网格 {segs0} 条 -> {c.Segs.Count} 条, 光标 {grid0} -> {c.GridIdx}");
        CheckTrue("按 X 之后还留在拼音网格里", c.GridPinyin && c.Focus == FocusZone.CandGrid);
        Check("而且真的删掉了一个数字", c.Digits, "746832");
        CheckTrue("光标没跑出范围", c.GridIdx >= 0 && c.GridIdx < c.Segs.Count);

        while (c.Digits.Length > 1) Tap(c, GamepadInput.X);
        Tap(c, GamepadInput.X);                    // 最后一个也删掉
        Check("数字全删光了", c.Digits, "");
        CheckTrue("真的没得挑了才退回九宫格", !c.GridPinyin && c.Focus == FocusZone.Keyboard);

        // 2) 全部符号
        var c2 = New();
        OpenPunct(c2);
        CheckTrue("唤出了全部符号", c2.Punct && c2.Focus == FocusZone.CandGrid);
        TextOutput.Reset();
        Tap(c2, GamepadInput.X);
        Check("符号框里 X 发的是退格", TextOutput.Flatten(), "BACK");
        CheckTrue("按 X 之后还留在符号框里", c2.Punct && c2.Focus == FocusZone.CandGrid);
        Tap(c2, GamepadInput.B);
        CheckTrue("按 B 才退出符号框", !c2.Punct && c2.Focus == FocusZone.Keyboard);
        Console.WriteLine();
    }

    /// <summary>
    /// 符号面板: 常用 / 中文 / 英文 / 数学 / 箭头 / 数字 / 单位 / 图形八类, LB / RB 换类。
    /// 【2.1.7】网格从 5×8 改成 **5×5**, 于是一类只有 24 个位置。
    /// 【2.1.8】正中间那一格不再是空的, 改成**标签格**: 装的是当前分类名,
    /// 是一颗真格子(会画、会高亮), 光标**默认就停在它上面** —— 进面板、换类、
    /// 松手回中三处都落在它上。它装的不是符号, 所以按 A 什么也不打; 而摇杆
    /// 推着的时候也够不到它(圆撑成方之后至少一轴出了中间那列), 只由回中到。
    /// 每一类必须正好 24 个, 而且不能有重复项(重复项等于浪费格子)。
    /// </summary>
    static void Case14_PunctCategories()
    {
        Console.WriteLine("【14】符号面板: 八类, LB/RB 换类, 每类都不超网格容量");
        CheckTrue("分类名和分类表一一对应",
            InputCore.PunctCatNames.Length == InputCore.PunctCats.Length);

        for (int i = 0; i < InputCore.PunctCats.Length; i++)
        {
            var cat = InputCore.PunctCats[i];
            int n = cat.Length;
            bool ok = n == 24 && cat.Distinct().Count() == n;
            if (!ok) _fail++;
            Console.WriteLine($"    {(ok ? "OK  " : "FAIL")}  {InputCore.PunctCatNames[i],-3} {n,2} 个: " +
                              string.Join(" ", cat.Take(14)) + (n > 14 ? " …" : ""));
        }

        var c = New();
        OpenPunct(c);
        Check("默认落在第一类", c.PunctCatName, "常用");

        // 25 格 = 24 个符号 + 正中间那格写**分类名**(2.1.8 的标签格)
        Check("进面板光标就落在正中间", c.GridIdx.ToString(), c.GridCenter.ToString());
        var g0 = c.GridItems();
        Check("网格一页 25 格", g0.Count.ToString(), "25");
        Check("正中间是第 12 格(5×5 的正中)", c.GridCenter.ToString(), "12");
        Check("正中间那格写的是分类名", g0[c.GridCenter], "常用");
        Check("正中间那格不算符号(按 A 不会打出东西)",
              c.PunctIdxOfGrid(c.GridCenter).ToString(), "-1");
        var want = new List<string>();
        for (int i = 0; i < 25; i++)
        {
            int k = c.PunctIdxOfGrid(i);
            want.Add(k < 0 ? c.PunctCatName
                     : k < InputCore.PunctCats[0].Length ? InputCore.PunctCats[0][k] : "");
        }
        CheckTrue("网格里装的是第一类(正中间是分类名)", g0.SequenceEqual(want));
        Check("24 个符号 + 1 个分类名 = 25 格都有内容",
              g0.Count(x => x.Length > 0).ToString(), "25");

        Send(c);
        Check("RB 换到下一类", c.PunctCatName, "中文");
        CheckTrue("网格跟着换", c.GridItems()[0] == InputCore.PunctCats[1][0]);
        CheckTrue("换类后光标回到正中间", c.GridIdx == c.GridCenter);

        // ---- 摇杆**直接落点**(2.1.7): 推到哪一格就是哪一格 ----
        CheckTrue("焦点在网格上", c.Focus.ToString() == "CandGrid");

        StickXY(-0.9, 0.9); Frame(c, 60);
        Check("推左上角 -> 第 0 格", c.GridIdx.ToString(), "0");

        // 斜推 45 度: 摇杆推到底是圆的, 每轴只有 0.5 —— 不撑成方形的话
        // 算出来是第 3 格(进不了角上那格)。撑成方形之后是 0.707 -> 真的到角上。
        StickXY(0.5, 0.5); Frame(c, 60);
        Check("斜推右上角 -> 真的落在角上那格(第 4 格)", c.GridIdx.ToString(), "4");

        // 正中间那格**摇杆推不到**: 圆撑成方之后, 推着的时候至少有一轴已经
        // 出了中间那一列/行。所以标签格只可能由"松手回中"到, 不会被顺手推上去。
        StickXY(0.5, 0.5); Frame(c, 60);
        CheckTrue("推着的时候够不到正中间(标签格只由回中到)",
                   c.GridIdx != c.GridCenter);

        StickXY(0.5, -0.5); Frame(c, 60);
        Check("斜推右下角 -> 最后一格(第 24 格)", c.GridIdx.ToString(), "24");

        TextOutput.Reset();
        Tap(c, GamepadInput.A, 60);
        Check("按 A 上屏的就是最后那一格的符号",
              string.Join("|", TextOutput.Sent), "U:" + InputCore.PunctCats[1][23]);

        // 松手**回中**(2.1.8): 落回正中间那个标签格 —— 和九键"松手回 5"同一个规矩
        StickCenter(); Frame(c, 60);
        Check("松手回中 -> 落在正中间那个标签格(第 12 格)",
              c.GridIdx.ToString(), "12");

        TextOutput.Reset();
        Tap(c, GamepadInput.A, 60);
        Check("停在标签格上按 A 什么都不发(它装的不是符号)",
              string.Join("|", TextOutput.Sent), "");
        Check("标签格上写的还是当前分类名", c.GridItems()[c.GridCenter], "中文");

        Tap(c, GamepadInput.LB);
        Check("LB 换回上一类", c.PunctCatName, "常用");

        int n2 = InputCore.PunctCats.Length;
        for (int i = 0; i < n2; i++) Send(c);
        Check("转满一圈回到原点", c.PunctCat.ToString(), "0");
        for (int i = 0; i < 3; i++) Tap(c, GamepadInput.LB);
        CheckTrue("往回翻也不越界", c.PunctCat >= 0 && c.PunctCat < n2);
        Console.WriteLine($"    往回三次后在: {c.PunctCatName} ({c.PunctCat + 1}/{n2})");

        // 符号框里 RB 是换类, 不再是空格
        TextOutput.Reset();
        Send(c);
        Check("符号框里 RB 不发空格", TextOutput.Flatten(), "");
        Console.WriteLine();
    }

    /// <summary>
    /// Y 有五种活(2.3.3), 判据只有一条: **这一下能不能切分**。
    ///   1. 拼音行有得挑 -> 切分, 按下即生效, 不进长按计时(打字打一半手指多停一会儿,
    ///      不该把设置页弹出来, 也不该顺手把布局换了);
    ///   2. 没拼音 -> 短按 = 换九键 / 全键盘(原 L3 那桩);
    ///   3. 没拼音 -> 按住满**半秒** = 进 / 出设置(原 R3 短按那桩, 2.3.3 从 1 秒缩到 500ms);
    ///   4. 数字层 -> 按一下出 0(原 L3 那桩);
    ///   5. 窗口F 绑数字那层 -> 按一下解除绑定(原 L3 那桩)。
    /// 顺带守着: L3 那三桩活全交出去了(换布局 / 出 0 / 解绑), R3 也不再调界面。
    /// </summary>
    static void Case17_YFiveJobs()
    {
        Console.WriteLine("【17】Y: 切分 / 短按换布局 / 长按半秒进设置 / 数字层出 0 / 窗口F 解绑");

        // 1) 有拼音: Y 就是切分, 按下即生效
        var cp = New();
        int flipsP = 0;
        cp.ToggleSettings = () => flipsP++;
        NumSeq(cp, "7468326");                    // shou'dao, 拼音行里好几条切分
        CheckTrue("先拼出一串拼音", cp.Segs.Count > 0);
        int len0 = cp.Digits.Length;
        Tap(cp, GamepadInput.Y, 140);
        CheckTrue("有拼音按 Y -> 切走了头一个音节(数字串变短)", cp.Digits.Length < len0);
        Check("有拼音按 Y 不换布局", cp.Layout.ToString(), "CN9");
        Check("有拼音按 Y 不进设置", flipsP.ToString(), "0");

        // 有拼音时**按住** Y 也不进设置 —— 这是最容易串味的一处
        var cp2 = New();
        int flipsP2 = 0;
        cp2.ToggleSettings = () => flipsP2++;
        NumSeq(cp2, "7468326");
        _btn |= GamepadInput.Y;
        for (int i = 0; i < 20; i++) Frame(cp2, 40);      // 20×40 = 800ms, 远超半秒
        _btn = (ushort)(_btn & ~GamepadInput.Y);
        Frame(cp2, 40);
        Check("有拼音时长按 Y 不进设置", flipsP2.ToString(), "0");
        Check("有拼音时长按 Y 也不换布局", cp2.Layout.ToString(), "CN9");

        // 2) 没拼音: 短按 = 换九键 / 全键盘
        var c = New();
        int flips0 = 0;
        c.ToggleSettings = () => flips0++;
        Check("起始是九键", c.Layout.ToString(), "CN9");
        Tap(c, GamepadInput.Y, 140);
        Check("没拼音短按 Y -> 换成全键盘", c.Layout.ToString(), "EN26");
        Tap(c, GamepadInput.Y, 140);
        Check("再短按一次换回九键", c.Layout.ToString(), "CN9");
        Check("短按没进设置", flips0.ToString(), "0");

        // 3) 没拼音: 按住满半秒 = 进设置, 而且松手**不**再补那一下换布局
        var c2 = New();
        int flips2 = 0;
        c2.ToggleSettings = () => flips2++;
        _btn |= GamepadInput.Y;
        for (int i = 0; i < 15; i++) Frame(c2, 40);      // 15×40 = 600ms > 500
        Check("按住 Y 满半秒 -> 进一次设置", flips2.ToString(), "1");
        _btn = (ushort)(_btn & ~GamepadInput.Y);
        Frame(c2, 40);
        Check("长按松手不再补那一下换布局", c2.Layout.ToString(), "CN9");
        Check("长按只翻一次(不会每帧翻一次)", flips2.ToString(), "1");

        // 差一点不够半秒就不算长按 —— 320ms 那一下应该走短按(换布局)
        var c3 = New();
        int flips3 = 0;
        c3.ToggleSettings = () => flips3++;
        _btn |= GamepadInput.Y;
        for (int i = 0; i < 8; i++) Frame(c3, 40);       // 8×40 = 320ms < 500
        Check("按住 320ms 还不算长按", flips3.ToString(), "0");
        _btn = (ushort)(_btn & ~GamepadInput.Y);
        Frame(c3, 40);
        Check("不够半秒 -> 松手走短按(换布局)", c3.Layout.ToString(), "EN26");

        // 4) 数字层: Y = 出 0(2.3.3 起, 以前是 L3)
        var c5 = New();
        c5.Layout = Layout.NUM9;
        TextOutput.Reset();
        Tap(c5, GamepadInput.Y, 140);
        Check("数字层按 Y -> 出一个 0", TextOutput.Flatten(), "0");
        Check("数字层按 Y 不换布局(那一层没有换布局这回事)", c5.Layout.ToString(), "NUM9");

        // 5) 窗口F 绑数字那一层: Y = 解除绑定(以前是 L3)
        var c6 = New();
        var spy = new QuickUiSpy { LayerName = InputCore.Q_LAYER_NUM };
        c6.QuickUI = spy;
        c6.QuickOpen = true;
        c6.QEdit = -1;
        Tap(c6, GamepadInput.Y, 140);
        Check("窗口F 绑数字层按 Y -> 叫了\"解除绑定\"", spy.YCalls.ToString(), "1");
        Check("那一趟没顺手换布局", c6.Layout.ToString(), "CN9");

        // 6) L3 那三桩活全交出去了: 不出 0、不解绑、也不换布局
        var c7 = New();
        c7.Layout = Layout.NUM9;
        TextOutput.Reset();
        Tap(c7, GamepadInput.L3, 200);
        Check("数字层 L3 不再出 0(归 Y 了)", TextOutput.Flatten(), "");
        var c8 = New();
        var spy2 = new QuickUiSpy { LayerName = InputCore.Q_LAYER_NUM };
        c8.QuickUI = spy2;
        c8.QuickOpen = true;
        Tap(c8, GamepadInput.L3, 200);
        Check("窗口F 里 L3 不再解绑(归 Y 了)", spy2.YCalls.ToString(), "0");

        // 7) R3: 短按不进设置(那一桩归 Y 了), 按住也**不再**进调界面(搬到设置页了)
        var c4 = New();
        int flips4 = 0;
        c4.ToggleSettings = () => flips4++;
        Tap(c4, GamepadInput.R3, 120);
        Check("R3 短按不再进设置", flips4.ToString(), "0");
        double s0 = c4.Scale;
        _btn |= GamepadInput.R3;
        _btn |= GamepadInput.DPAD_UP;
        for (int i = 0; i < 20; i++) Frame(c4, 40);      // 20×40 = 800ms, 远超以前的 260ms
        CheckTrue("按住 R3 不再进调界面(那一档搬到设置页了)", !c4.Resizing);
        Check("按住 R3 也不再缩放", c4.Scale.ToString("F3"), s0.ToString("F3"));
        _btn = (ushort)(_btn & ~GamepadInput.DPAD_UP);
        _btn = (ushort)(_btn & ~GamepadInput.R3);
        Frame(c4, 40);
        Check("R3 长按松手也不进设置", flips4.ToString(), "0");
        Console.WriteLine();
    }

    /// <summary>
    /// 调窗口大小 / 摆位(2.3.3): 入口是设置 → 键盘 → 「调整窗口」(BeginResize),
    /// 进去之后十字键上下缩放、左摇杆挪位置, **按 B 出来**才落盘。
    /// 以前是"按住 R3"临时进、松手就退 —— 那颗键在摇杆底下, 手正推着摇杆选键的
    /// 时候够不着, 而且想边看边挪得一直绷着手指。
    /// </summary>
    static void Case37_ResizeWindow()
    {
        Console.WriteLine("【37】调窗口: 设置页进来 · 十字键缩放 · 摇杆摆位 · B 存好退出");

        var c = New();
        int saved = 0; double lastS = 0, lastX = 0, lastY = 0;
        c.LayoutSaved = (s, ox, oy) => { saved++; lastS = s; lastX = ox; lastY = oy; };

        CheckTrue("平时不在调窗口那一档", !c.Resizing);
        c.BeginResize();
        CheckTrue("点「调整窗口」-> 进了那一档", c.Resizing);
        Check("刚进来还没落盘", saved.ToString(), "0");

        // 十字键上下缩放
        double s0 = c.Scale;
        _btn |= GamepadInput.DPAD_UP;
        for (int i = 0; i < 10; i++) Frame(c, 40);
        CheckTrue("十字键上 -> 放大了", c.Scale > s0);
        double sUp = c.Scale;
        _btn = (ushort)(_btn & ~GamepadInput.DPAD_UP);
        _btn |= GamepadInput.DPAD_DOWN;
        for (int i = 0; i < 20; i++) Frame(c, 40);
        CheckTrue("十字键下 -> 缩小了", c.Scale < sUp);
        _btn = (ushort)(_btn & ~GamepadInput.DPAD_DOWN);
        Frame(c, 40);

        // 左摇杆挪位置
        double x0 = c.OffsetX, y0 = c.OffsetY;
        _lx = 30000; _ly = -30000;                  // 右 + 下(摇杆 y 正 = 上)
        for (int i = 0; i < 5; i++) Frame(c, 40);
        CheckTrue("摇杆右推 -> 往右挪了", c.OffsetX > x0);
        CheckTrue("摇杆下推 -> 往下挪了(OffsetY 变大)", c.OffsetY > y0);
        _lx = 0; _ly = 0;
        Frame(c, 40);

        // 摇杆回中还剩一点残余(死区以内)不许再漂
        double x1 = c.OffsetX;
        _lx = 3000; _ly = 0;                        // 0.09, 远在 0.3 死区里
        for (int i = 0; i < 5; i++) Frame(c, 40);
        Check("摇杆死区里的残余不再挪动界面", c.OffsetX.ToString("F3"), x1.ToString("F3"));
        _lx = 0; _ly = 0;

        // 这一档里别的按键一律不往下传(不打字、不换布局)
        TextOutput.Reset();
        int flips = 0;
        c.ToggleSettings = () => flips++;
        Tap(c, GamepadInput.A, 120);
        Check("调窗口时按 A 不打字", TextOutput.Flatten(), "");
        Check("调窗口时按 A 不翻设置", flips.ToString(), "0");
        Check("调窗口时也换不了布局", c.Layout.ToString(), "CN9");

        // 按 B 出来: 退出 + 落盘一次
        Tap(c, GamepadInput.B, 120);
        CheckTrue("按 B -> 退出那一档", !c.Resizing);
        Check("退出时落盘一次", saved.ToString(), "1");
        CheckTrue("存下来的就是刚才调的缩放", Math.Abs(lastS - c.Scale) < 1e-9);
        CheckTrue("存下来的就是刚才摆的位置",
                  Math.Abs(lastX - c.OffsetX) < 1e-9 && Math.Abs(lastY - c.OffsetY) < 1e-9);

        // 出去之后十字键 / 摇杆恢复原样
        _btn |= GamepadInput.DPAD_UP;
        for (int i = 0; i < 5; i++) Frame(c, 40);
        Check("退出后十字键不再缩放", c.Scale.ToString("F3"), lastS.ToString("F3"));
        _btn = (ushort)(_btn & ~GamepadInput.DPAD_UP);
        _lx = 30000;
        for (int i = 0; i < 5; i++) Frame(c, 40);
        Check("退出后摇杆不再摆位(回去选键了)", c.OffsetX.ToString("F3"), lastX.ToString("F3"));
        _lx = 0; _ly = 0;

        // 界面被收起也要结掉(不然缩放白调一场)
        var c2 = New();
        int saved2 = 0;
        c2.LayoutSaved = (s, ox, oy) => saved2++;
        c2.BeginResize();
        c2.Visible = false;
        Frame(c2, 40);
        CheckTrue("界面收起时自动退出那一档", !c2.Resizing);
        Check("收起时也把缩放存下来了", saved2.ToString(), "1");
        Console.WriteLine();
    }

    /// <summary>
    /// 翻到设置那一面之后: 十字键是"上下选设置项", A 确认, B / R3 回正面,
    /// 而且**不再是键盘的方向键** —— 不然每按一下上下都往游戏里发一个方向键。
    /// 摇杆和右摇杆在那一面也不再接管, 免得一边选设置一边还在挪光标。
    /// </summary>
    static void Case18_SettingsDpadNav()
    {
        Console.WriteLine("【18】设置面: 十字键上下选设置项, A 确认, 不发方向键");

        var c = New();
        c.Settings = true;                 // 界面翻到了设置那一面
        var nav = new List<int>();
        c.SettingsNav = d => nav.Add(d);
        int picks = 0;
        c.SettingsPick = () => picks++;
        int flips = 0;
        c.ToggleSettings = () => flips++;

        TextOutput.Reset();
        Tap(c, GamepadInput.DPAD_DOWN);
        Check("设置面十字键下不发方向键", TextOutput.Flatten(), "");
        Check("设置面十字键下 -> 光标往下", string.Join(",", nav), "1");
        nav.Clear();
        Tap(c, GamepadInput.DPAD_UP);
        Check("设置面十字键上 -> 光标往上", string.Join(",", nav), "-1");
        nav.Clear();

        // 上下归"上下"那个回调, 左右归"左右"那个回调, 两条路分开走(主题那排色块靠左右挑)
        var navH = new List<int>();
        c.SettingsNavH = d => navH.Add(d);
        nav.Clear();
        Tap(c, GamepadInput.DPAD_LEFT);
        Tap(c, GamepadInput.DPAD_RIGHT);
        Check("设置面十字键左右不走上下那条", string.Join(",", nav), "");
        Check("设置面十字键左右 -> 组内左右", string.Join(",", navH), "-1,1");
        navH.Clear();

        // A = 确认当前项
        Tap(c, GamepadInput.A);
        Check("设置面按 A 确认一次", picks.ToString(), "1");
        int before = picks;
        _btn |= GamepadInput.A;
        for (int i = 0; i < 10; i++) Frame(c, 60);     // 按住 600ms
        _btn = (ushort)(_btn & ~GamepadInput.A);
        Frame(c, 60);
        Check("按住 A 只多算一次(600ms 里没连发)", (picks - before).ToString(), "1");

        // B = 回正面
        Tap(c, GamepadInput.B);
        Check("设置面按 B 回正面", flips.ToString(), "1");

        // 设置面里摇杆不动九键光标, 右摇杆也不翻出标点层
        var c2 = New();
        c2.Settings = true;
        c2.SettingsNav = _ => { };
        c2.Key9 = 5;
        StickTo(1);
        Frame(c2, 60);
        CheckTrue("设置面里摇杆不动九键光标", c2.Key9 == 5);
        StickCenter();
        Frame(c2, 60);
        _rx = (short)(Math.Cos(0) * 32767 * 0.9);
        _ry = 0;
        for (int i = 0; i < 6; i++) Frame(c2, 120);
        CheckTrue("设置面里不翻出标点层", !c2.Punct9);
        _rx = 0; _ry = 0;
        Frame(c2, 60);

        // 翻回正面, 十字键立刻变回方向键
        c.Settings = false;
        TextOutput.Reset();
        Tap(c, GamepadInput.DPAD_LEFT);
        Check("翻回正面十字键左又是方向键", TextOutput.Flatten(), "LEFT");
        Console.WriteLine();
    }

    /// <summary>
    /// 【26】【2.1.19】长串打到后面, "第一个字"一个都不许漏。
    /// 漏法有两类, 这里都盯着:
    /// <para>· <b>撞车</b> —— pa'o 和 pao 是同一串字母。以前去重键用的是字母串,
    /// 两条撞在一起只留先到的那条, 于是想让第一个字是 pa 的人从头翻到尾也选不出来。
    /// 可撇是真会发给输入法的(见 SendPinyin), 切法不同就是两条候选。</para>
    /// <para>· <b>断尾</b> —— 首音节后面那截凑不出完整音节: 7264 想要 pan 打头,
    /// 可 4 上那三个字母一个音节都组不成, 于是这个首音节一条都生成不出来。
    /// 现在补一条"拼了一半"的(pan'g), 至少让它找得到、能按 Y 定住。</para>
    /// 判据很直白: 这串数字能解出的每一个首音节, 候选里至少有一条以它开头。
    /// </summary>
    static void Case26_HeadCoverage()
    {
        Console.WriteLine("【26】长串: 每个可能的首音节都找得到");

        // 1) 撞车那一类: 726 能解出六个三字母音节, 但 pa / sa(两个字母)也得在
        var c = New();
        c.Digits = "726";
        c.DebugRefreshCands();
        var firsts = new HashSet<string>(c.Segs.Where(s => s.Length > 0).Select(s => s[0]));
        Console.WriteLine("    726 的候选: " + string.Join(" ", c.Segs.Select(s => string.Join("'", s))));
        Check("726 里找得到 pa 打头的", firsts.Contains("pa") ? "有" : "没有", "有");
        Check("726 里找得到 sa 打头的", firsts.Contains("sa") ? "有" : "没有", "有");
        Check("726 里找得到 pao 打头的", firsts.Contains("pao") ? "有" : "没有", "有");

        // 2) 断尾那一类: 7264 想要 pan / pao 打头, 4 上三个字母一个音节都组不成
        var c2 = New();
        c2.Digits = "7264";
        c2.DebugRefreshCands();
        var f2 = new HashSet<string>(c2.Segs.Where(s => s.Length > 0).Select(s => s[0]));
        Console.WriteLine("    7264 的候选: " + string.Join(" ", c2.Segs.Select(s => string.Join("'", s))));
        Check("7264 里找得到 pan 打头的", f2.Contains("pan") ? "有" : "没有", "有");
        Check("7264 里找得到 pao 打头的", f2.Contains("pao") ? "有" : "没有", "有");

        // 3) 兜底不能抢位置: "拼了一半"的那几条得排在真候选后头
        //    (它们的尾巴是猜的, 打分却按字母串查词库 —— zhan'g 会被当成 zhang 去查,
        //     张 是高频词, 不压住它就会把学会的 zhang'san'feng 顶下去)
        var c3 = New();
        c3.Digits = "7264";
        c3.DebugRefreshCands();
        int lastReal = -1, firstPart = -1;
        for (int i = 0; i < c3.Segs.Count; i++)
        {
            var s = c3.Segs[i];
            // "拼了一半"的那条: 最后一块不是完整的音节
            bool part = s.Length > 1 && !T9Engine.SyllablesOf(
                s[s.Length - 1].Select(ch => ch).Aggregate("", (a, ch) => a + T9Of(ch))).Any();
            if (part && firstPart < 0) firstPart = i;
            if (!part) lastReal = i;
        }
        Check("拼一半的排在真候选后面",
              (firstPart < 0 || lastReal < firstPart) ? "是" : "不是", "是");

        // 4) 随机长串: 每一个可能的首音节都得露一次脸
        var keys = new[] { '1', '2', '3', '4', '6', '7', '8', '9' };
        var rnd = new Random(20261010);
        int bad = 0, total = 0;
        string firstBad = "(无)";
        for (int n = 3; n <= 9; n++)
            for (int t = 0; t < 40; t++)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++) sb.Append(keys[rnd.Next(keys.Length)]);
                string d = sb.ToString();
                total++;
                var cx = New();
                cx.Digits = d;
                cx.DebugRefreshCands();
                var fx = new HashSet<string>(cx.Segs.Where(s => s.Length > 0).Select(s => s[0]));
                var miss = new List<string>();
                for (int L = Math.Min(6, d.Length); L >= 1; L--)
                    foreach (var w in T9Engine.SyllablesOf(d.Substring(0, L)))
                        if (!fx.Contains(w)) miss.Add(w);
                if (miss.Count > 0)
                {
                    bad++;
                    if (firstBad == "(无)")
                        firstBad = d + " 漏了 " + string.Join(",", miss.Distinct());
                }
            }
        Check($"随机 {total} 串, 漏首音节的串数", bad.ToString(), "0");
        if (bad > 0) Console.WriteLine("    第一处: " + firstBad);
        Console.WriteLine();
    }

    /// <summary>
    /// 【27】【2.1.21】拼音行必须**跟着每一次按键动**。
    /// 三个判据, 都对着"按下键必须有反馈"这一条:
    /// <para>· <b>不空</b> —— 只要按了键, 拼音行就得有一条。</para>
    /// <para>· <b>不停滞</b> —— 多按一下, 第一条必须变。以前连按 jkl(1 键):
    /// 761 / 7611 / 76111 摆出来的全是 po'j, 看不见自己按了几下, 可退格一下
    /// 一下得按三回 —— 用户原话"不知道输入了几个, 也就看不到要删几次"。</para>
    /// <para>· <b>不坍缩</b> —— 多按一下, 已经定下来的中间音节不许整段丢掉。
    /// po'kan'mo 再按一下, 以前直接缩成 po'j。</para>
    /// 量法是"这条切分吃掉了几个数字": 每块音节还原成数字再加起来, 理想就等于
    /// 数字串长度 —— 少一位就说明有按键没被表示出来。
    /// </summary>
    static void Case27_LiveFeedback()
    {
        Console.WriteLine("【27】拼音行跟着每一次按键动");

        int CoverOf(string[] seg) => seg.Sum(s => T9Engine.ToT9(s).Length);
        InputCore At(string d)
        {
            var c = New();
            c.Digits = d;
            c.DebugRefreshCands();
            return c;
        }
        string FirstOf(InputCore c) => c.Segs.Count > 0 ? string.Join("'", c.Segs[0]) : "(空)";

        // 1) 连按同一个键: po'j -> po'jj -> po'jjj, 一次一个样
        var texts = new List<string>();
        foreach (var d in new[] { "761", "7611", "76111", "761111" })
        {
            var c = At(d);
            texts.Add(FirstOf(c));
            Check($"{d} 有候选 + 覆盖满 {d.Length} 位",
                  c.Segs.Count > 0 && CoverOf(c.Segs[0]) == d.Length ? "是" : "不是", "是");
        }
        Console.WriteLine("    连按 1 键: " + string.Join(" | ", texts));
        Check("连按同一个键, 拼音行一次一个样",
              texts.Distinct().Count() == texts.Count ? "是" : "不是", "是");

        // 2) 不坍缩: po'kan'mo 再按一下 -> po'kan'mo'j(以前是 po'j)
        var a = At("7612666");
        var b = At("76126661");
        string ta = FirstOf(a), tb = FirstOf(b);
        Console.WriteLine($"    {ta} -> 再按一下 -> {tb}");
        // 【别把"是 kan 还是 lan"写死】第一条究竟排哪个由词库热度说了算, 而 coretest
        // 带着整份 dict, 排出来的和别的探针不一定一样。这里只盯一件事:
        // 中间那几个已经定下来的音节还在不在(坍缩成 po'j 就是只有两块)。
        Check("7612666 的第一条是 po 打头的整串切分",
              ta.StartsWith("po'") && ta.Split('\'').Length >= 3 ? "是" : "不是", "是");
        Check("再按一下不缩成 po'j(中间那几个音节还在)",
              tb.StartsWith("po'") && tb.Split('\'').Length >= 3 ? "是" : "不是", "是");
        Check("再按一下覆盖满 8 位",
              b.Segs.Count > 0 && CoverOf(b.Segs[0]) == 8 ? "是" : "不是", "是");

        // 3) 随机长串: 每加一位都要变, 覆盖位数不许倒退也不许不足
        var keys = new[] { '1', '2', '3', '4', '6', '7', '8', '9' };
        var rnd = new Random(20261010);
        int empty = 0, stall = 0, shrink = 0, total = 0;
        string firstBad = "(无)";
        void Bad(string s) { if (firstBad == "(无)") firstBad = s; }

        for (int n = 3; n <= 9; n++)
            for (int t = 0; t < 40; t++)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++) sb.Append(keys[rnd.Next(keys.Length)]);
                string d = sb.ToString();
                var c0 = At(d);
                if (c0.Segs.Count == 0) { Bad(d + " -> 一条都没有"); empty++; continue; }
                string t0 = FirstOf(c0);
                int cv0 = CoverOf(c0.Segs[0]);
                if (cv0 != d.Length)
                {
                    Bad(d + " -> " + t0 + " 只吃掉 " + cv0 + " 位");
                    shrink++;
                }
                foreach (var k in keys)
                {
                    string nx = d + k;
                    var c1 = At(nx);
                    total++;
                    if (c1.Segs.Count == 0) { Bad(nx + " -> 一条都没有"); empty++; continue; }
                    string t1 = FirstOf(c1);
                    int cv1 = CoverOf(c1.Segs[0]);
                    if (t1 == t0) { Bad(d + " -> " + nx + " 都是 " + t0); stall++; }
                    if (cv1 < cv0 + 1) { Bad(d + " (" + t0 + ") -> " + nx + " (" + t1 + ")"); shrink++; }
                }
            }

        Check($"随机 {total} 次加键, 拼音行全空", empty.ToString(), "0");
        Check($"随机 {total} 次加键, 拼音行没动", stall.ToString(), "0");
        Check($"随机 {total} 次加键, 覆盖倒退或不足", shrink.ToString(), "0");
        if (firstBad != "(无)") Console.WriteLine("    第一处: " + firstBad);
        Console.WriteLine();
    }


    /// <summary>
    /// 【28】【2.1.23】按住 LT 进 / 出数字层, 也是一场"左右缩窄 / 扩张"。
    /// 以前只有 L3 换九键 / 全键盘走那套动画, 按住 LT 是直接改 Layout 的 ——
    /// 全键盘下按一下就是"啪"地换成九键那个方框(用户原话"瞬闪切成 9 键方形窗口")。
    /// 判据: 这一趟**换了宽度**(全键盘 640 &lt;-&gt; 数字层 360)才交给 FlipLayout;
    /// 九键(360) &lt;-&gt; 数字层(360)宽度没变, 照旧直接换(那一趟本来就有换脸那场动画)。
    /// </summary>
    /// <summary>
    /// 按住 LB 滑行的时候, 左摇杆是"一支笔" —— HandleStick 在滑行那一支里就 return 了。
    /// 可"松手回中"认的是**边沿**, 边沿必须每帧都在记, 不管这一帧是被哪条分支
    /// 接走的。以前那支直接 return, 于是按下 LB 那一刻起 _stickWasActive 就冻在旧值上:
    /// 按住 LB 期间松开摇杆, 焦点一直亮在进滑行前那个方向上不回来(用户报的那个
    /// "不会自动回中"), 连松 LB 那一帧的回中也跟着错。
    /// </summary>
    static void Case29_SlideStickRecenter()
    {
        Console.WriteLine("【29】滑行中松开摇杆: 焦点回正中、摇杆点回中心");
        _btn = 0; _lx = 0; _ly = 0;

        // 1) 老路回归: 不滑行的时候, 松手本来就该回 5
        var c = New();
        StickTo(3); Frame(c, 60);
        Check("推着摇杆: 焦点在 3", c.Key9.ToString(), "3");
        StickCenter(); Frame(c, 60);
        Check("松手: 焦点回正中 5", c.Key9.ToString(), "5");
        Check("松手: 摇杆点回中心", $"{c.StickX:0.##}/{c.StickY:0.##}", "0/0");

        // 2) 摇杆推着的时候按住 LB 进滑行: 焦点不许被这一笔拖着走(老规矩)
        var c2 = New();
        StickTo(3); Frame(c2, 60);
        _btn |= GamepadInput.LB; Frame(c2, 60);
        Check("按住 LB: 进了滑行", c2.Slide.Active.ToString(), "True");
        Check("进滑行那一帧: 焦点不动", c2.Key9.ToString(), "3");
        StickTo(7); Frame(c2, 60);
        Check("滑行中换方向: 焦点仍不被拖走", c2.Key9.ToString(), "3");

        // 3) 核心: LB 还按着的时候松开摇杆 —— 焦点要回正中, 摇杆点要回中心
        StickCenter(); Frame(c2, 60);
        Check("滑行中松开摇杆: 焦点回正中 5", c2.Key9.ToString(), "5");
        Check("滑行中松开摇杆: 摇杆点回中心", $"{c2.StickX:0.##}/{c2.StickY:0.##}", "0/0");

        // 4) 松 LB 收尾, 焦点还在正中
        _btn &= unchecked((ushort)~GamepadInput.LB); Frame(c2, 60);
        Check("松 LB: 滑行结束", c2.Slide.Active.ToString(), "False");
        Check("松 LB: 焦点还在正中", c2.Key9.ToString(), "5");

        // 5) 死区里那点残余不算数: 摇杆物理回中总有一点零点几的偏移,
        //    滑行里也得跟平时一样抹成 0, 不然笔尖停在偏出中心一点的地方。
        var c3 = New();
        _btn |= GamepadInput.LB; Frame(c3, 60);
        StickXY(0.05, -0.04); Frame(c3, 60);
        Check("滑行里死区内的残余: 摇杆点仍在中心", $"{c3.StickX:0.##}/{c3.StickY:0.##}", "0/0");
        Check("滑行里死区内的残余: 没认成某个方向", c3.Slide.Sector.ToString(), "0");
        _btn &= unchecked((ushort)~GamepadInput.LB); Frame(c3, 60);
        Console.WriteLine();
    }

    /// <summary>
    /// 24826474364(创生 / 窗生…): chuang'sheng(2 块) 才是人话,
    /// chu'ang'sheng(3 块) 是同一串字母切得更碎的一种 —— 它俩**拼起来一模一样**
    /// (都是 chuangsheng)。
    /// 以前"你的词""你用得多"这两笔分都是按**连写**记的, 于是打够两次之后:
    /// ① 碎的那条跟着领了成词档那 700 多分; ② 连"多切一块扣 300"的惩罚也被一并
    /// 豁免; ③ 学习分按连写加, 两条加的一样多 —— 差距是死的, 从此打多少遍
    /// chuang'sheng 都翻不回来(用户报的就是这个)。
    /// 现在这三处都按**切法**认。
    /// </summary>
    /// <summary>
    /// 【31】同一串数字的两种切法: 谁打出去得多, 谁排前面。
    /// <para>这句话是用户连着报三次的那个问题的正面要求。以前 <c>_use</c> 的键是
    /// **连写**(chuangsheng) —— chuang'sheng 和 chu'ang'sheng 拼起来一样, 于是两种
    /// 切法**共用一个计数器**: 发的是哪一条都给两条一起加分, "谁发得多谁排前面"在
    /// 数据结构上就不成立。更糟的是 BumpUse 攒够次数时拿 SplitKey 从连写反推切法
    /// (取块数最少那种), 把 chu'ang'sheng 记成了 chuang'sheng —— 张冠李戴。</para>
    /// <para>现在一种切法一个身份: 各记各的次数, 排名跟着你真正打过的那一条走。
    /// 这条用例抓手抓的是**结果**(谁排第一), 不看内部实现。</para>
    /// </summary>
    static void Case31_WhoSentMoreRanksFirst()
    {
        Console.WriteLine("【31】同一串数字的两种切法: 谁发得多谁排前面");
        T9Engine.ClearUse();
        const string digits = "24826474364";
        const string A = "chuang'sheng", B = "chu'ang'sheng";

        var c0 = New();
        NumSeq(c0, digits);
        CheckTrue("候选里有 " + A, c0.Segs.Any(s => string.Join("'", s) == A));
        CheckTrue("同一串数字还能切成 " + B, c0.Segs.Any(s => string.Join("'", s) == B));

        // A 打出去 3 次 -> A 第一, 而且次数只加在 A 自己那条上
        SendTimes(digits, A, 3);
        Check("A(3 次) / B(0 次): A 排第一", Top(digits, 1)[0], A);
        Check("次数各记各的: A", T9Engine.UseOf(A).ToString(), "3");
        Check("次数各记各的: B 一条都没蹭到", T9Engine.UseOf(B).ToString(), "0");

        // B 打出去 5 次(比 A 多) -> 反过来 B 登顶, 但 A 不能被挤没了
        SendTimes(digits, B, 5);
        Check("B 发得更多(5 > 3): B 反超到第一", Top(digits, 1)[0], B);
        Check("两条的次数清清楚楚分开", T9Engine.UseOf(A) + "/" + T9Engine.UseOf(B), "3/5");
        CheckTrue("A 没被挤没, 只是往后挪", Top(digits, 99).Contains(A));

        // 再回头把 A 补成 8 次 -> A 又回到第一。**能来回翻**才是真记住了次数,
        // 以前因为共用计数器, 排名一旦定死就再也翻不动了。
        SendTimes(digits, A, 5);
        Check("A 补到 8 次 > B 的 5 次: A 又翻回第一", Top(digits, 1)[0], A);
        Check("次数跟着走", T9Engine.UseOf(A) + "/" + T9Engine.UseOf(B), "8/5");

        // 保送进候选池的必须是**你亲手发过的那两刀**, 不是猜出来的某一种
        var m = T9Engine.MineOf(digits).Select(s => string.Join("'", s)).ToList();
        CheckTrue("保送的是你发过的那一刀 A", m.Contains(A));
        CheckTrue("保送的也有你发过的那一刀 B", m.Contains(B));

        T9Engine.ClearUse();
        Console.WriteLine();
    }

    /// <summary>
    /// 【32】**眼睛挑中的那条, 按 RB 出门的就是那条** —— 连着报三次的那个现象的
    /// 正面要求, 抓手是"用户的手", 不是内部那条排序结果。
    /// <para>【31】问的是"谁排第一"(读 <c>Segs[0]</c>), 它绿了不代表用户的手能
    /// 挑到那条。这一组走完整按键路径: 打数字 → 看界面高亮那一格 → 按 RB →
    /// 把 <c>TextOutput</c> 真发出去的字符拼回来对比。中间任何一环把人带偏
    /// (该翻到的翻不到、高亮不是要发的那条、老存档迁移把次数记到别条头上),
    /// 只有这一组会红。</para>
    /// </summary>
    static void Case32_PickedIsWhatGetsSent()
    {
        Console.WriteLine("【32】挑中的那条 == RB 真发出去的那条 (含老存档迁移)");
        const string digits = "24826474364";
        const string A = "chuang'sheng", B = "chu'ang'sheng";
        T9Engine.ClearUse();

        // a) 干净存档第一次打: 出门的必须就是界面高亮那一格
        var c0 = New();
        NumSeq(c0, digits);
        Console.WriteLine("    (这一串候选的前几条: "
            + string.Join(" | ", c0.Segs.Take(6).Select(Keys)) + ")");
        Console.WriteLine($"    (一行能摆 FitNow={c0.FitNow} 条, 打完后 PinyinIdx={c0.PinyinIdx})");
        CheckTrue("候选里既有 " + A + " 也有 " + B,
            c0.Segs.Any(s => Keys(s) == A) && c0.Segs.Any(s => Keys(s) == B));
        string pick0 = Keys(c0.Segs[c0.PinyinIdx]);
        TextOutput.Reset();
        Send(c0);
        Check("干净存档 · 直接按 RB: 出门的就是高亮那格", SentAscii(), pick0);

        // b) A 打得多 -> 默认就停在 A 上, 手不用动
        SendTimes(digits, A, 3);
        var c1 = New();
        NumSeq(c1, digits);
        Check("A 发过 3 次后: 默认选中那条就是 A", Keys(c1.Segs[c1.PinyinIdx]), A);
        TextOutput.Reset();
        Send(c1);
        Check("A 发过 3 次后: 直接按 RB 出门的就是 A", SentAscii(), A);

        // c) 反过来 B 发得多 -> 默认停在 B 上, 但左右必须还能挑到 A
        SendTimes(digits, B, 6);
        var c2 = New();
        NumSeq(c2, digits);
        Console.WriteLine("    (B 发得更多之后的前几条: "
            + string.Join(" | ", c2.Segs.Take(6).Select(Keys)) + ")");
        int guard = 0;
        while (Keys(c2.Segs[c2.PinyinIdx]) != A && guard++ < 40) Right(c2);
        Check("B 发得多时, 靠十字键还能挑到 A", Keys(c2.Segs[c2.PinyinIdx]), A);
        TextOutput.Reset();
        Send(c2);
        Check("挑到 A 之后按 RB: 出门的是 A", SentAscii(), A);

        // d) 老存档: 2.1.35 及以前记的是**连写** chuangsheng, 迁移之后这份次数
        //    必须落在你真正发过的那一刀头上
        T9Engine.ClearUse();
        var old = new Dictionary<string, int> { ["chuangsheng"] = 9 };
        T9Engine.LoadUse(old);
        Check("老连写键 chuangsheng 迁移到 " + A, T9Engine.UseOf(A).ToString(), "9");
        Check("没发过的 " + B + " 一条都没蹭到", T9Engine.UseOf(B).ToString(), "0");
        var c3 = New();
        NumSeq(c3, digits);
        Check("带着老习惯开机: 默认选中就是你常发的那条", Keys(c3.Segs[c3.PinyinIdx]), A);

        // e) 单个字也可能被切成两刀: 打 chuang 的数字串 248264, 它同时也是 chu'ang
        //    的数字串。先发 chuang 几次之后, RB 出门的必须是 chuang, 不能是 chu'ang。
        T9Engine.ClearUse();
        const string one = "248264";
        SendTimes(one, "chuang", 3);
        var c4 = New();
        NumSeq(c4, one);
        Console.WriteLine("    (单字 248264 的前几条: "
            + string.Join(" | ", c4.Segs.Take(5).Select(Keys))
            + $"  FitNow={c4.FitNow})");
        Check("单字: 发过 3 次 chuang 之后默认选中就是 chuang", Keys(c4.Segs[c4.PinyinIdx]), "chuang");
        TextOutput.Reset();
        Send(c4);
        Check("单字: 直接按 RB 出门的是 chuang", SentAscii(), "chuang");

        T9Engine.ClearUse();
        Console.WriteLine();
    }

    /// <summary>
    /// 【33】"一行摆得下几条"那把尺子认错了人 —— 先把**触发条件**钉死。
    /// <para>OverlayWindow.Render 每次要摆拼音行之前会量一遍宽度, 那玩意挺贵, 所以带缓存:
    /// <c>Digits | Segs.Count | 行宽 | 缩放</c> 四样都一样, 就沿用上一次量出来的个数。
    /// 可真正决定"摆得下几条"的是**每条拼音有多宽** —— 这四样里一样都没带上它。</para>
    /// <para>于是只要"条数没变、只是排头那条换了人", 它就会拿上一次的个数去摆这一回的
    /// 内容: 排头从 chuang'sheng 换成更长的 chu'ang'sheng, 条数一个没动, 摆得下的却少一条
    /// —— 多出来的那格被顶到边界外, 而左右键还在那个范围里转。</para>
    /// <para>这一组碰不到界面, 只验触发条件本身: 同一串数字、同一份条数、排头那条换成了
    /// 更长的一条。条件成立 + 量宽那把钥匙里没有内容 = 结论成立。</para>
    /// </summary>
    static void Case33_FitCacheKeyMissesContent()
    {
        Console.WriteLine("【33】量宽度的钥匙里没有内容 —— 触发条件");
        const string digits = "24826474364";
        T9Engine.ClearUse();

        var c0 = New();
        NumSeq(c0, digits);
        var b = c0.Segs.Select(Keys).ToList();

        // 换成把更长那条(chu'ang'sheng)发出去几次
        SendTimes(digits, "chu'ang'sheng", 4);

        var c1 = New();
        NumSeq(c1, digits);
        var a = c1.Segs.Select(Keys).ToList();

        Console.WriteLine($"    (开头: Count={b.Count}, 前三条 {string.Join(" | ", b.Take(3))})");
        Console.WriteLine($"    (之后: Count={a.Count}, 前三条 {string.Join(" | ", a.Take(3))})");

        Check("条数一模一样(钥匙的前半截没动)", b.Count.ToString(), a.Count.ToString());
        CheckTrue("排头那条换了人 —— 而且更长",
            a[0] != b[0] && a[0].Length > b[0].Length);
        Console.WriteLine("    => 钥匙相同、内容不同: 必须重新量, 沿用旧值就是错的");

        // 换上新记号之后: "这份候选是不是刚才那一份"必须答得准 ——
        // 换了 +1, 没换不许动(动多了等于每帧都重新量一遍, 白扔开销)
        T9Engine.ClearUse();
        var c2 = New();
        int s0 = c2.SegsStamp;
        NumSeq(c2, "9");
        CheckTrue("打了数字 -> 候选换过一整份, 记号往前走", c2.SegsStamp > s0);
        int s1 = c2.SegsStamp;
        Right(c2);
        Check("只在候选里左右挑 -> 记号不许动", c2.SegsStamp.ToString(), s1.ToString());
        int s2 = c2.SegsStamp;
        Num(c2, 4);
        CheckTrue("又按一个数字 -> 重新切了一遍, 记号再走一格", c2.SegsStamp > s2);

        // ---- 最后一步: 光证明"钥匙没变、内容变了"还不够, 得证明 fit **真的会算错** ----
        // 照 Render 里那套累加算法重演一遍(每条占宽 ~ 字符数×6.5 + 26, 行可用 ~336),
        // 看看"条数不变、顺序重排"能不能真把 fit 顶下来。能顶下来, 才是会犯的错;
        // 顶不下来, 那就只是隐患。
        // 目标得从**前几条里**挑 —— SendTimes 只能靠十字键在 fit 条之内挪(II),
        // 排在 fit 之外的那条它够不着, 会**静默跳过**(一次都没学)。
        string PickTarget(List<string> items) =>
            items.Take(Math.Min(4, items.Count)).OrderByDescending(s => s.Length).First();

        var probes = new[] { "24826474364", "7468326", "9482646", "7426474364", "426" };
        foreach (var dg in probes)
        {
            T9Engine.ClearUse();
            var x0 = New(); NumSeq(x0, dg);
            var clean = x0.Segs.Select(Keys).ToList();
            SendTimes(dg, PickTarget(clean), 4);
            var x1 = New(); NumSeq(x1, dg);
            var learned = x1.Segs.Select(Keys).ToList();
            int f0 = FitLikeRender(clean), f1 = FitLikeRender(learned);
            bool sameCount = clean.Count == learned.Count;
            Console.WriteLine($"    {dg,-12} 条数 {clean.Count}->{learned.Count}"
                + $"  fit {f0}->{f1}  {(sameCount && f0 != f1 ? "** 钥匙一样却该换值 **" : "")}");
            if (dg == probes[0])
                Console.WriteLine($"       (干净头一条 {clean[0]} / 学习后头一条 {learned[0]})");
        }

        // 真正站得住的那一条因果: **摆得下几条, 到底是不是由宽度定的?**
        // 造两份条数一模一样的名单, 只差宽窄 —— 窄的那份能多摆才是。
        // 它证的是"条数不是充分判据", 和上面"条数不变而内容变了"凑成完整一条链。
        var narrow = Enumerable.Repeat("ab", 8).ToList();
        var wide = Enumerable.Repeat("chu'ang'sheng", 8).ToList();
        Check("两份条数一样多", narrow.Count.ToString(), wide.Count.ToString());
        CheckTrue("窄的那份摆得下更多条 —— 决定这件事的是宽度, 不是条数",
            FitLikeRender(narrow) > FitLikeRender(wide));
        Console.WriteLine($"    (同是 8 条: 每条 2 字符能摆 {FitLikeRender(narrow)} 条, "
            + $"每条 14 字符只能摆 {FitLikeRender(wide)} 条)");
        Console.WriteLine("    => 钥匙里既然带了条数, 就不能不认内容; 认 SegsStamp 两头都管");

        T9Engine.ClearUse();
        Console.WriteLine();
    }

    /// <summary>
    /// 【34】换页动效的"人话三件套"(名字 / 第二行小字 / 底下长说明)和存档解析,
    /// 以前散在四处各写各的: <c>App.FxStyleName</c> 一个 switch、<c>Config.LoadFxStyle</c>
    /// 一条 if 链、设置页两个数组。加一种风格漏补任何一处**都不报错** ——
    /// 名字静默退回"默认"、存档静默退回 Plain、说明一片空白, 界面上看着还能用,
    /// 其实显示的不是你新加的那种。现在这四样全由 <c>FxStyles.All</c> 一张表推出来,
    /// 这一组盯的就是**表和枚举对不对得上**。
    /// <para>【怎么知道它不是摆设】临时往枚举里加一个成员、故意不在表里补,
    /// 这一组必须变红 —— 实测红了 2 条(见提交说明)。</para>
    /// </summary>
    static void Case34_FxStyleTableKeepsUp()
    {
        Console.WriteLine("【34】换页动效那张表有没有跟上枚举 —— 漏一条就红");

        int nEnum = Enum.GetValues(typeof(FxStyle)).Length;
        Check("表的条数 = 枚举的成员数", FxStyles.Count.ToString(), nEnum.ToString());

        var gaps = FxStyles.Gaps();
        foreach (var g in gaps) Console.WriteLine("      漏: " + g);
        CheckTrue("每一种都有名字 / 第二行小字 / 底下长说明", gaps.Count == 0);

        // 两个档位叫同一个名字的话, 设置页上就分不清谁是谁了
        var names = Enum.GetValues(typeof(FxStyle)).Cast<FxStyle>()
                        .Select(FxStyles.Name).ToList();
        Check("几个档位几个不重样的名字", names.Distinct().Count().ToString(), nEnum.ToString());
        Console.WriteLine("      名字: " + string.Join(" / ", names));

        // 越界的数: 认不出, 而且取信息要退回默认那档(而不是崩、也不是空白)
        CheckTrue("正好越界那一个数认不出", !FxStyles.Has((FxStyle)nEnum));
        Check("越界的数取信息退回默认那档", FxStyles.Name((FxStyle)nEnum), FxStyles.Name(FxStyle.Plain));
        CheckTrue("差得远的数(99)也认不出", !FxStyles.Has((FxStyle)99));
        CheckTrue("表里的每一档说明都不是空的",
            Enum.GetValues(typeof(FxStyle)).Cast<FxStyle>()
                .All(s => FxStyles.Tip(s).Length > 20 && FxStyles.Note(s).Length > 0));

        Console.WriteLine("    => 加一种风格 = 枚举加成员 + 表里加一条; 少一样, 这一组当场红");
        Console.WriteLine();
    }

    static void Case35_KeyShapeTableAndRadius()
    {
        Console.WriteLine("【35】按键形状那张表有没有跟上枚举, 圆角是不是按短边取");

        int nEnum = Enum.GetValues(typeof(KeyShape)).Length;
        Check("表的条数 = 枚举的成员数", KeyShapes.Count.ToString(), nEnum.ToString());

        var gaps = KeyShapes.Gaps();
        foreach (var g in gaps) Console.WriteLine("      漏: " + g);
        CheckTrue("每一种都有名字 / 第二行小字 / 底下长说明", gaps.Count == 0);

        var names = Enum.GetValues(typeof(KeyShape)).Cast<KeyShape>()
                        .Select(KeyShapes.Name).ToList();
        Check("几个档位几个不重样的名字", names.Distinct().Count().ToString(), nEnum.ToString());
        Console.WriteLine("      名字: " + string.Join(" / ", names));

        CheckTrue("正好越界那一个数认不出", !KeyShapes.Has((KeyShape)nEnum));
        Check("越界的数取信息退回默认那档", KeyShapes.Name((KeyShape)nEnum), KeyShapes.Name(KeyShape.Square));
        CheckTrue("差得远的数(99)也认不出", !KeyShapes.Has((KeyShape)99));

        // ---- 圆角 = 短边的一半 ----
        // 这一条规矩把"哪些键变圆、哪些变胶囊"整个包住了:
        // 正方形(九键 112、设置格子 112、「关于」352) -> 正圆;
        // 长条(789 那条 352×112、全键盘的空格 190×40、候选格 72×46) -> 胶囊。
        Check("九键 112 见方 -> 正圆, 半径 56", KeyShapes.RadiusOf(112, 112).ToString("0.#"), "56");
        Check("「关于」352 见方 -> 大圆, 半径 176", KeyShapes.RadiusOf(352, 352).ToString("0.#"), "176");
        Check("26 键字母键 38×40 -> 按短边取 19", KeyShapes.RadiusOf(38, 40).ToString("0.#"), "19");
        Check("789 那条长条 352×112 -> 胶囊, 半径 56", KeyShapes.RadiusOf(352, 112).ToString("0.#"), "56");
        Check("全键盘空格 190×40 -> 胶囊, 半径 20", KeyShapes.RadiusOf(190, 40).ToString("0.#"), "20");
        Check("候选格 72×46 -> 胶囊, 半径 23", KeyShapes.RadiusOf(72, 46).ToString("0.#"), "23");
        // 竖着比横着长的时候也一样取短边(不取高)
        Check("竖着那条 45×72 -> 半径 22.5", KeyShapes.RadiusOf(45, 72).ToString("0.#"), "22.5");
        // 还没量到尺寸(那一帧还没排版): 回 0 让调用方留着原来那一档, 不许拿 0 当圆角
        Check("量不到尺寸 -> 回 0, 不许猜", KeyShapes.RadiusOf(0, 0).ToString("0.#"), "0");
        Check("只有一边量到也回 0", KeyShapes.RadiusOf(112, 0).ToString("0.#"), "0");

        Console.WriteLine("    => 加一种形状 = 枚举加成员 + 表里加一条; 圆角一律短边的一半");
        Console.WriteLine();
    }

    /// <summary>
    /// 2.3.2: 手柄唤醒键多了 L3 / R3 两个选项。它们能进这张表是因为**刚刚空出来** ——
    /// L3 换布局、R3 短按进设置这两桩活搬到 Y 上了(见【17】)。
    /// 守四件事: ① 存盘 / 读盘认得这两个写法; ② 定成 L3 / R3 之后真按得动;
    /// ③ 定了一个之后**只认这一个**(单按另一颗不动); ④ 没自定义的默认还是 L3+R3,
    /// 单按任意一颗都不动。
    /// </summary>
    static void Case36_WakePadL3R3()
    {
        Console.WriteLine("【36】手柄唤醒键: 新加的 L3 / R3 两个选项(2.3.2)");

        CheckTrue("选项表里有 L3", Array.Exists(InputCore.PadWakeOptions, o => o == "L3"));
        CheckTrue("选项表里有 R3", Array.Exists(InputCore.PadWakeOptions, o => o == "R3"));
        CheckTrue("pad:L3 是认得的存档写法", InputCore.IsWakeSpec("pad:L3"));
        CheckTrue("pad:R3 是认得的存档写法", InputCore.IsWakeSpec("pad:R3"));
        CheckTrue("pad:LB 仍然不认(那颗有活干)", !InputCore.IsWakeSpec("pad:LB"));

        // ---- 定成 L3 ----
        var c = New();
        c.WakeSpec = "pad:L3";
        CheckTrue("起始是显着的", c.Visible);
        _btn |= GamepadInput.L3;
        for (int i = 0; i < 4; i++) Frame(c, 40);      // 满 2 帧才认
        CheckTrue("按住 L3 -> 界面收起", !c.Visible);
        _btn = (ushort)(_btn & ~GamepadInput.L3);
        for (int i = 0; i < 4; i++) Frame(c, 40);
        Check("松手之后不会自己又翻回来", c.Visible ? "显" : "收", "收");

        _btn |= GamepadInput.L3;
        for (int i = 0; i < 4; i++) Frame(c, 40);
        CheckTrue("再按一次 L3 -> 唤回来", c.Visible);
        _btn = (ushort)(_btn & ~GamepadInput.L3);
        for (int i = 0; i < 4; i++) Frame(c, 40);

        // 定了 L3 之后单按 R3 不唤出 / 收起 —— 定了自定义就**只认这一个**
        _btn |= GamepadInput.R3;
        for (int i = 0; i < 4; i++) Frame(c, 40);
        CheckTrue("定了 L3 之后单按 R3 不唤出/收起", c.Visible);
        _btn = (ushort)(_btn & ~GamepadInput.R3);
        for (int i = 0; i < 4; i++) Frame(c, 40);

        // ---- 定成 R3 ----
        var c2 = New();
        c2.WakeSpec = "pad:R3";
        _btn |= GamepadInput.R3;
        for (int i = 0; i < 4; i++) Frame(c2, 40);
        CheckTrue("按住 R3 -> 界面收起", !c2.Visible);
        _btn = (ushort)(_btn & ~GamepadInput.R3);
        for (int i = 0; i < 4; i++) Frame(c2, 40);
        // 定了 R3 之后当然也不进"调界面" —— 而且 2.3.3 起这一档根本不再由 R3 触发
        // (搬到设置 → 键盘 → 「调整窗口」了), R3 现在只剩唤出这一件事。
        CheckTrue("定了 R3 之后不进\"调界面\"(那一档归设置页了)", !c2.Resizing);

        // ---- 默认(没自定义): 还是 L3+R3, 单按任何一颗都不动 ----
        var c3 = New();
        _btn |= GamepadInput.L3;
        for (int i = 0; i < 4; i++) Frame(c3, 40);
        CheckTrue("默认: 单按 L3 不唤出/收起", c3.Visible);
        _btn = (ushort)(_btn & ~GamepadInput.L3);
        for (int i = 0; i < 4; i++) Frame(c3, 40);
        _btn |= GamepadInput.R3;
        for (int i = 0; i < 4; i++) Frame(c3, 40);
        CheckTrue("默认: 单按 R3 也不唤出/收起", c3.Visible);
        _btn |= GamepadInput.L3;
        for (int i = 0; i < 4; i++) Frame(c3, 40);
        CheckTrue("默认: L3+R3 一起才收起", !c3.Visible);
        _btn = 0;
        for (int i = 0; i < 4; i++) Frame(c3, 40);

        Console.WriteLine("    => 唤醒键那串键按住期间整帧都不往下传, 所以选了它就得让出它别的活");
        Console.WriteLine();
    }

    /// <summary>
    /// 照 <c>Render.MeasurePinyinFit</c> 那套算法重演一遍: 一条一条往行里摆,
    /// 后面还有候选时给"…"留一格, 摆不下就停。这里用字符数估算宽度。
    /// </summary>
    static int FitLikeRender(List<string> items, double avail = 336)
    {
        double used = 0; int fit = 0;
        for (int i = 0; i < items.Count; i++)
        {
            bool tail = i + 1 < items.Count;
            if (fit + (tail ? 1 : 0) >= InputCore.MaxPinyinPerRow) break;
            double w = items[i].Length * 6.5 + 26 + (tail ? 20 : 0);
            if (used + w > avail) break;
            used += w;
            fit++;
        }
        return Math.Max(1, fit);
    }

    /// <summary>一条切分拼成发给输入法那样(chuang'sheng)。</summary>
    static string Keys(string[] syls) =>
        syls == null || syls.Length == 0 ? "" : string.Join("'", syls);

    /// <summary>把 TextOutput 桩记下的虚拟按键拼回真正发给输入法的那串。</summary>
    static string SentAscii()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in TextOutput.Sent)
            if (s.Length == 4 && s.StartsWith("VK:")) sb.Append(s[3]);
        return sb.ToString();
    }

    /// <summary>
    /// 把这个数字串打成 want 那条、再 RB 发出去, 重复 n 次 —— 走的是真实按键路径。
    /// </summary>
    static void SendTimes(string digits, string want, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var c = New();
            NumSeq(c, digits);
            int guard = 0;
            while (string.Join("'", c.Segs[c.PinyinIdx]) != want && guard++ < 60) Right(c);
            if (string.Join("'", c.Segs[c.PinyinIdx]) != want) return;   // 找不着就停, 别死循环
            Send(c);
            RT(c);                                   // 上屏: SentPinyin 归零, 整串记一次
        }
    }

    /// <summary>当前 _use 里所有项: 键=次数(排错时一眼能看出记成了啥)。</summary>
    static string DumpUse()
    {
        var d = T9Engine.DumpUse();
        return d.Count == 0 ? "(空)" : string.Join(", ", d.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>这个数字串上保送了哪几条切法。</summary>
    static string DumpMine(string digits)
    {
        var m = T9Engine.MineOf(digits).Select(s => string.Join("'", s)).ToList();
        return m.Count == 0 ? "(空)" : string.Join(", ", m);
    }

    /// <summary>重新打一遍这个数字串, 列出前 n 条候选。</summary>
    static List<string> Top(string digits, int n)
    {
        var c = New();
        NumSeq(c, digits);
        return c.Segs.Take(n).Select(s => string.Join("'", s)).ToList();
    }

    static void Case30_MineIsThisSplit()
    {
        Console.WriteLine("【30】自己的词: 成词档 / 免罚 / 学习分都只认你切的那一刀");
        T9Engine.ClearUse();
        const string digits = "24826474364";
        var c0 = New();
        NumSeq(c0, digits);
        CheckTrue("候选里有 chuang'sheng", c0.Segs.Any(s => string.Join("'", s) == "chuang'sheng"));
        CheckTrue("也有切碎的 chu'ang'sheng", c0.Segs.Any(s => string.Join("'", s) == "chu'ang'sheng"));

        // 一遍: 还没到 MYWORD_MIN(2), 排第一靠的是它本身最像话
        T9Engine.BumpUse("chuang'sheng");
        var c1 = New();
        NumSeq(c1, digits);
        Check("打 1 遍: chuang'sheng 第一", string.Join("'", c1.Segs[0]), "chuang'sheng");

        // 两遍: 保送生效(进 _mine)。以前就是从这一遍起碎的那条登顶。
        T9Engine.BumpUse("chuang'sheng");
        var c2 = New();
        NumSeq(c2, digits);
        Check("打 2 遍(保送生效): 还是 chuang'sheng 第一", string.Join("'", c2.Segs[0]), "chuang'sheng");
        CheckTrue("碎的那条没被顶掉, 只是往后挪",
                  c2.Segs.Any(s => string.Join("'", s) == "chu'ang'sheng"));

        // 两条现在的差距(打 2 遍时)。**必须当场算**: 学习分是查当前状态算的,
        // 等打完 20 遍再回头算, 这里拿到的就已经是 20 遍的数了。
        double gap1 = GapAt(c2, digits);

        // 二十遍: 学习分越攒越多, 两条的差距必须是**越拉越大**, 不能是死的
        for (int i = 0; i < 18; i++) T9Engine.BumpUse("chuang'sheng");
        var c3 = New();
        NumSeq(c3, digits);
        Check("打 20 遍: 仍然登顶", string.Join("'", c3.Segs[0]), "chuang'sheng");
        CheckTrue("碎的那条没蹭到学习分(不在前三条)",
                  !c3.Segs.Take(3).Any(s => string.Join("'", s) == "chu'ang'sheng"));

        // 学习分是给"那一刀"的, 不是给"这个读音"的: 差距该随次数长大
        double gap3 = GapAt(c3, digits);
        CheckTrue($"差距越打越大({gap1:0.#} -> {gap3:0.#})", gap3 > gap1 + 50);

        T9Engine.ClearUse();
        Console.WriteLine();
    }

    /// <summary>chuang'sheng 那条比 chu'ang'sheng 高多少(按当前的习惯算)。</summary>
    static double GapAt(InputCore c, string digits)
    {
        string mine = "chuang'sheng", shred = "chu'ang'sheng";
        var a = c.Segs.First(s => string.Join("'", s) == mine);
        var b = c.Segs.First(s => string.Join("'", s) == shred);
        return T9Engine.RankOrder(a, 2) - T9Engine.RankOrder(b, 2);
    }

    static void Case28_NumLayerFx()
    {
        Console.WriteLine("【28】按住 LT 进 / 出数字层: 换宽度那两趟交给动画");

        // LT 是扳机不是按钮, Frame() 没带上它 —— 这里单独喂一帧。
        void LtFrame(InputCore c, byte lt, int ms = 140)
        {
            _now = _now.AddMilliseconds(ms);
            c.Poll(new PadState { Connected = true, Buttons = _btn, LX = _lx, LY = _ly,
                                  LeftTrigger = lt, RightTrigger = _rt, RX = _rx, RY = _ry }, _now);
        }

        // 1) 全键盘 -> 数字层: 要缩窄, 得交给动画
        var c = New();
        c.Layout = Layout.EN26;
        int flips = 0;
        c.FlipLayout = mid => { flips++; mid(); };
        LtFrame(c, 255);
        Check("全键盘下按住 LT: 进了数字层", c.Layout.ToString(), "NUM9");
        Check("全键盘下按住 LT: 这一趟交给 FlipLayout 演", flips.ToString(), "1");
        LtFrame(c, 0);
        Check("松开 LT: 还原回全键盘", c.Layout.ToString(), "EN26");
        Check("松开 LT: 这一趟也交给 FlipLayout 演", flips.ToString(), "2");
        // 数字层不能赖着: 松手那一帧就得还回去(不然全键盘左下角多一颗 0)
        CheckTrue("松手后不再是数字层", c.Layout != Layout.NUM9);

        // 2) 九键 -> 数字层: 宽度没变(360 -> 360), 直接换, 不惊动动画
        var c2 = New();
        int flips2 = 0;
        c2.FlipLayout = mid => { flips2++; mid(); };
        LtFrame(c2, 255);
        Check("九键下按住 LT: 进了数字层", c2.Layout.ToString(), "NUM9");
        Check("九键下按住 LT: 宽度没变, 不演那一场", flips2.ToString(), "0");
        LtFrame(c2, 0);
        Check("松开回九键", c2.Layout.ToString(), "CN9");
        Check("松开也不演那一场", flips2.ToString(), "0");

        // 3) 没接回调(没有窗口)时照旧直接换 —— 不能因为没人演就切不过去
        var c3 = New();
        c3.Layout = Layout.EN26;
        LtFrame(c3, 255);
        Check("没接回调: 按住 LT 照样进数字层", c3.Layout.ToString(), "NUM9");
        LtFrame(c3, 0);
        Check("没接回调: 松手照样还原", c3.Layout.ToString(), "EN26");
        Console.WriteLine();
    }

    /// <summary>一个字母 -> 它在九键上是几号键(给上面判断"最后一块是不是完整音节"用)。</summary>
    static string T9Of(char ch) => T9Engine.ToT9(ch.ToString());

}
