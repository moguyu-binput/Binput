using Binput;

// fprobe —— 验 1.2.1 窗口F 的**新**机制。
//
// 新机制三条:
//   1) 顶部第一个按钮 = "添加到短语": 把预览框里现成的字整段存成短语, 不再另开输入窗口。
//      预览框里的字留着不动。直出版没有预览框 -> 这个按钮不给用。
//   2) 顶部第二个按钮 = "选择快捷键": 开录键页(全键盘), 只记键名一个都不往外发,
//      X 删最后一个, ↑↓ 在"键盘 / 保存"之间切, B 放弃。
//   3) 短语的"编辑": 框里没字 -> 把这条短语放回框里并**关掉 F**;
//      框里有字 -> 用框里这段覆盖这条短语, **不关 F**。
//
// 规矩(1.0.9 那次吃过亏): **只走真实入口**, 绝不直接摆私有字段假造状态。
// 手柄状态一律通过 Poll 灌进去; 状态机靠 _prevBtn 认边沿, 每个动作之间要有"松开"的空帧。

// quick.txt 是用户真在用的文件。以前是"先备份、跑完还原", 现在整份挪到临时
// 目录 —— 真机上那份从头到尾不碰(备份/还原中途出岔子会留下半截, 挪目录不会)。
string qdir = Path.Combine(Path.GetTempPath(), "fprobe_" + Guid.NewGuid().ToString("N")[..8]);
Config.SetDirForTest(qdir);
Console.WriteLine("(快捷数据用临时目录: " + qdir + ")");

int fails = 0;
void Check(string what, bool ok, string detail = "")
{
    Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {what}{(detail.Length > 0 ? "  — " + detail : "")}");
    if (!ok) fails++;
}

// preview=true 表示预输入版(有预览框); preview=false 是直出版(没有预览框)
InputCore NewCore(bool preview)
{
    var c = new InputCore { Layout = Layout.CN9, Focus = FocusZone.Keyboard, Visible = true };
    c.Preview = preview;
    string note = "";                       // 模拟"预览框里现在的字"
    c.QuickNoteText = () => preview ? note : null;
    c.QuickSetNote = s => note = s ?? "";
    c.QuickFocusGame = () => TextOutput.Sent.Add("FOCUSGAME");
    // 窗口F 那一摞层在真程序里是 WPF 画的, 这里挂一个假的上(见 Stubs.cs)
    c.QuickUI = new FakeQuickUI { Core = c };
    return c;
}
// 探针里拿不到窗口, 所以把"预览框"这层壳露出来给断言用
static string NoteOf(InputCore c) => c.QuickNoteText?.Invoke() ?? "<没有预览框>";
static FakeQuickUI UI(InputCore c) => (FakeQuickUI)c.QuickUI;

// 真窗口是在 Render 里"F 开着却没搭过 -> 现搭 / F 关了 -> 整摞撤掉"的;
// 探针没有 Render, 每次 Poll 之后自己补这一下。
static void SyncF(InputCore c)
{
    var f = UI(c);
    if (c.QuickOpen && f.Depth == 0) f.Open();
    else if (!c.QuickOpen && f.Depth > 0) f.Close();
}

// 45 个槽位全清(2.1.1 起没有 QuickStore.Add 了, 一律按槽位写)
static void ResetQuick()
{
    QuickStore.Load();
    for (int i = 0; i < QuickStore.MaxSlots; i++) QuickStore.Remove(i);
}

var T0 = new DateTime(2026, 1, 1, 12, 0, 0);

// 一次"按下 -> 保持 -> 松开"。ms = 按住多久。
void Tap(InputCore c, ref DateTime t, ushort btn, int ms = 40, short lx = 0, short ly = 0)
{
    t = t.AddMilliseconds(20);
    c.Poll(new PadState { Connected = true, Buttons = btn, LX = lx, LY = ly }, t);
    if (ms > 40)
    {
        t = t.AddMilliseconds(ms - 40);
        c.Poll(new PadState { Connected = true, Buttons = btn, LX = lx, LY = ly }, t);
    }
    t = t.AddMilliseconds(20);
    c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
    SyncF(c);
}

// 长按九宫格**正中间那颗**打开窗口F。
// 2.0.0 之前长按的是左上角那颗 1 —— 那时 1 号键没有字母; 现在 JKL 搬去了左上角,
// 中心那颗(5)才轮到空出来干这个。摇杆一松手本来就停在中间, 直接按住 A 即可,
// 不用先把它推过去 —— 但导航方式要先切回摇杆, 组里可能刚用过十字键。
void OpenF(InputCore c, ref DateTime t)
{
    t = t.AddMilliseconds(20);
    c.Poll(new PadState { Connected = true, LY = 30000 }, t);   // 推一下: Nav -> 摇杆
    t = t.AddMilliseconds(40);
    c.Poll(new PadState { Connected = true }, t);               // 松手: 回中 = 中心那颗
    Tap(c, ref t, GamepadInput.A, 600);
    SyncF(c);
}

try
{
    ResetQuick();

    // ==================================================================
    // ==================================================================
    Console.WriteLine("=== 1) 长按中心键开窗口F: 起手是列表层, 光标回中 ===");
    {
        ResetQuick();
        var c = NewCore(true);
        var t = T0;
        OpenF(c, ref t);
        var f = UI(c);
        Check("长按中心键打开了窗口F", c.QuickOpen, $"QuickOpen={c.QuickOpen}");
        Check("那一摞只有一层(列表层)", f.Depth == 1 && f.Layer == InputCore.Q_LAYER_LIST,
              $"Depth={f.Depth} Layer=\"{f.Layer}\"");
        Check("光标回中(第 5 格)", f.Sel == 4, $"Sel={f.Sel}");
        Check("起手在第 1 页", f.Page == 0, $"Page={f.Page}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 2) 列表层: 短按 A 发出这一格, 长按 A 进深层 ===");
    {
        ResetQuick();
        QuickStore.Set(0, QKind.Phrase, "集合打本");      // 槽位 0 = 数字键 1
        var c = NewCore(true);                            // 预输入版: 发短语接进预览框
        var t = T0;
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 0;                                        // 第 1 格
        TextOutput.Reset();
        Tap(c, ref t, GamepadInput.A);
        Check("短按 A 把这一格发出去了", NoteOf(c) == "集合打本", $"框里=\"{NoteOf(c)}\"");
        Check("预输入版发完就关掉 F", !c.QuickOpen, $"QuickOpen={c.QuickOpen}");

        // 长按 = 进深层。发和改只能二选一 —— 先发一条再弹编辑层不是你要的。
        var c2 = NewCore(true); var t2 = T0;
        OpenF(c2, ref t2);
        var f2 = UI(c2);
        f2.Sel = 0;
        Tap(c2, ref t2, GamepadInput.A, 600);
        Check("长按 A 进了深层", f2.Depth == 2 && f2.Layer == InputCore.Q_LAYER_DEEP,
              $"Depth={f2.Depth} Layer=\"{f2.Layer}\"");
        Check("深层认住了是哪一格", f2.Slot == 0, $"Slot={f2.Slot}");
        Check("长按**没有**顺手把那条发出去", NoteOf(c2) == "", $"框里=\"{NoteOf(c2)}\"");

        // 空格子短按就是"往这一格写点什么" -> 直接钻进深层, 省一下
        var c3 = NewCore(true); var t3 = T0;
        OpenF(c3, ref t3);
        var f3 = UI(c3);
        f3.Sel = 3;                                       // 第 4 格(空)
        Tap(c3, ref t3, GamepadInput.A);
        Check("空格子短按也进深层", f3.Depth == 2 && f3.Slot == 3,
              $"Depth={f3.Depth} Slot={f3.Slot}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 3) 深层: 1 写入短语 / 3 删除 / 中间改绑 / 4·6 按了没反应 ===");
    {
        ResetQuick();
        var c = NewCore(true); var t = T0;
        c.QuickSetNote("上交任务");
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 0;
        Tap(c, ref t, GamepadInput.A, 600);               // 进深层(第 1 格)
        f.Sel = 0;                                        // 1 号位 = 写入短语
        Tap(c, ref t, GamepadInput.A);
        Check("写入短语: 用预览框里现成的字写进第 1 格",
              QuickStore.At(0)?.Text == "上交任务" && QuickStore.At(0).IsPhrase,
              $"第1格=\"{QuickStore.At(0)?.Text}\"");
        Check("预览框里的字留着没被清空", NoteOf(c) == "上交任务", $"框里=\"{NoteOf(c)}\"");
        Check("写完留在深层(好看见底下那条长条变了)", f.Depth == 2 && c.QuickOpen,
              $"Depth={f.Depth} QuickOpen={c.QuickOpen}");

        // 4 / 6 号位: 能停能选中, 按了没反应
        int n0 = QuickStore.Count;
        f.Sel = 3; Tap(c, ref t, GamepadInput.A);
        f.Sel = 5; Tap(c, ref t, GamepadInput.A);
        Check("4 / 6 号位按了没反应", QuickStore.Count == n0 && f.Depth == 2,
              $"条数={QuickStore.Count}(原 {n0}) Depth={f.Depth}");

        // 3 号位 = 删除: 位置留着, 后面的不往前挪(那条规矩归 quickprobe 守)
        QuickStore.Set(1, QKind.Phrase, "第二条");
        f.Sel = 2;
        Tap(c, ref t, GamepadInput.A);
        Check("删除: 第 1 格空了而第 2 格还在原处",
              QuickStore.Empty(0) && QuickStore.At(1)?.Text == "第二条",
              $"第1格={QuickStore.At(0)?.Text} 第2格={QuickStore.At(1)?.Text}");
        Check("删完退回列表层", f.Depth == 1, $"Depth={f.Depth}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 4) 写入快捷键: 只记不发, Y 保存, B 放弃 ===");
    {
        ResetQuick();
        var c = NewCore(false);                   // 直出版也能录快捷键
        var t = T0;
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 0;
        Tap(c, ref t, GamepadInput.A, 600);       // 进深层(第 1 格)
        f.Sel = 1;                                // 2 号位 = 写入快捷键
        Tap(c, ref t, GamepadInput.A);
        Check("进了录键页", c.QEdit == 1 && c.Layout == Layout.EN26,
              $"QEdit={c.QEdit} Layout={c.Layout}");

        TextOutput.Reset();
        Tap(c, ref t, GamepadInput.A, 40, 30000, 0);
        Check("按 A 记下了一颗键", c.QBuf.Length > 0, $"QBuf=\"{c.QBuf}\"");
        string one = c.QBuf;
        Tap(c, ref t, GamepadInput.A, 40, 30000, 0);
        Check("再按一颗接在后面", c.QBuf != one && c.QBuf.Contains('+'), $"QBuf=\"{c.QBuf}\"");
        Check("记键期间一个键都没真发出去", TextOutput.Sent.Count == 0,
              $"发出={TextOutput.Flatten()}");

        Tap(c, ref t, GamepadInput.X);
        Check("X 删掉了最后一个", c.QBuf == one, $"QBuf=\"{c.QBuf}\"");

        TextOutput.Reset();
        Tap(c, ref t, GamepadInput.RB);
        Check("RB 记下空格而不是真发空格", c.QBuf.EndsWith("space"), $"QBuf=\"{c.QBuf}\"");
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0, RightTrigger = 255 }, t);
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
        Check("RT(回车)被拦住了", !TextOutput.Flatten().Contains("ENTER"),
              $"发出=\"{TextOutput.Flatten()}\"");

        // Y = 保存(上面那个"保存"按钮 2.1.1 起撤了)
        string saved = c.QBuf;
        Tap(c, ref t, GamepadInput.Y);
        Check("Y 保存后退出录键页", c.QEdit == -1, $"QEdit={c.QEdit}");
        Check("存进第 1 格, 种类是快捷键", QuickStore.At(0)?.Kind == QKind.Hotkey
              && QuickStore.At(0)?.Text == saved,
              $"第1格=\"{QuickStore.At(0)?.Text}\" 期望=\"{saved}\"");
        Check("退出后布局还原成九键", c.Layout == Layout.CN9, $"Layout={c.Layout}");
        Check("保存完还在窗口F 的深层里", c.QuickOpen && f.Depth == 2,
              $"QuickOpen={c.QuickOpen} Depth={f.Depth}");

        // B = 放弃
        int n0 = QuickStore.Count;
        f.Sel = 1;
        Tap(c, ref t, GamepadInput.A);
        Tap(c, ref t, GamepadInput.A, 40, 30000, 0);
        Tap(c, ref t, GamepadInput.B);
        Check("B 放弃: 退出了也没存", c.QEdit == -1 && QuickStore.Count == n0,
              $"QEdit={c.QEdit} 条数={QuickStore.Count}(原 {n0})");
    }

    // ==================================================================
    Console.WriteLine("\n=== 5) 直出版: 没有预览框, 写入短语不给用 ===");
    {
        ResetQuick();
        var c = NewCore(false); var t = T0;
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 0;
        Tap(c, ref t, GamepadInput.A, 600);
        f.Sel = 0;                                // 1 号位 = 写入短语
        Tap(c, ref t, GamepadInput.A);
        Check("没有预览框: 写入短语不给用", QuickStore.Count == 0, $"条数={QuickStore.Count}");
        Check("HasNote = false", !c.HasNote, $"HasNote={c.HasNote}");

        // 但发短语直出版照样能用, 而且面板留着(还能接着发下一条)。
        // 发是在**列表层**按 A —— 先在深层里就得按 B 退回去。
        QuickStore.Set(0, QKind.Phrase, "直出版的短语");
        Tap(c, ref t, GamepadInput.B);
        Check("B 退回列表层", f.Depth == 1, $"Depth={f.Depth}");
        TextOutput.Reset();
        Tap(c, ref t, GamepadInput.A);
        Check("直出版照样能发短语", TextOutput.Flatten().Contains("直出版的短语"),
              $"发出=\"{TextOutput.Flatten()}\"");
        Check("直出版发短语不关面板", c.QuickOpen, $"QuickOpen={c.QuickOpen}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 6) 预输入版发短语: 接进预览框, 不复制不发送 ===");
    {
        ResetQuick();
        QuickStore.Set(0, QKind.Phrase, "档位测试");
        var c = NewCore(true); var t = T0;
        c.QuickSetNote("前面打的");
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 0;
        TextOutput.Reset();
        Tap(c, ref t, GamepadInput.A);
        Check("短语接进了预览框", NoteOf(c) == "前面打的档位测试", $"框里=\"{NoteOf(c)}\"");
        Check("没有复制 / 没有发键", TextOutput.Sent.Count == 0,
              $"发出={string.Join("|", TextOutput.Sent)}");
        Check("发完关掉了 F", !c.QuickOpen, $"QuickOpen={c.QuickOpen}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 7) 数字层长按绑了短语的数字 = 发那条 ===");
    {
        ResetQuick();
        QuickStore.Set(2, QKind.Phrase, "集合打本");     // 槽位 2 = 数字键 3
        var c = NewCore(true);
        var t = T0;
        Check("绑定成功: 3 号上就是那条", QuickStore.ByNum(3)?.Text == "集合打本",
              $"ByNum(3)={QuickStore.ByNum(3)?.Text}");

        // 按住 LT 进数字层(数字 3 = 右上角 -> 摇杆右上)
        void Hold(ushort btn, int ms, short lx, short ly, byte lt = 0)
        {
            t = t.AddMilliseconds(20);
            c.Poll(new PadState { Connected = true, Buttons = btn, LX = lx, LY = ly, LeftTrigger = lt }, t);
            t = t.AddMilliseconds(ms);
            c.Poll(new PadState { Connected = true, Buttons = btn, LX = lx, LY = ly, LeftTrigger = lt }, t);
            t = t.AddMilliseconds(20);
            c.Poll(new PadState { Connected = true, Buttons = 0, LX = lx, LY = ly, LeftTrigger = lt }, t);
        }

        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0, LeftTrigger = 255, LX = 30000, LY = 30000 }, t);
        Check("LT 进了数字层", c.Layout == Layout.NUM9, $"Layout={c.Layout}");
        Check("摇杆把光标停在 3 上", c.Key9 == 3, $"Key9={c.Key9}");

        TextOutput.Reset();
        // 长按到点那一帧: 短语发出去。【2.4.7】光标不许被按回中心 ——
        // 用户报"长按数字发快捷短语, 左摇杆瞬间闪回中心, 再回到推的地方":
        // 根因是 QuickSend(预输入版) -> QuickClose -> ClearInput 里写着 Key9 = 5,
        // 而摇杆还推着, 下一帧 HandleStick 又把它拽回 3, 中间闪一下。
        // (不能直接用下面的 Hold 助手: 它最后会松 A, 断言必须赶在那一帧之前读 Key9)
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.A, LX = 30000, LY = 30000, LeftTrigger = 255 }, t);
        t = t.AddMilliseconds(600);
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.A, LX = 30000, LY = 30000, LeftTrigger = 255 }, t);
        Check("长按后把那条短语接进了预览框", NoteOf(c) == "集合打本", $"框里=\"{NoteOf(c)}\"");
        Check("没有去动剪贴板 / 焦点", TextOutput.Sent.Count == 0,
              $"发出={string.Join("|", TextOutput.Sent)}");
        Check("发短语那一帧光标还在 3 上(不闪回中心)", c.Key9 == 3, $"Key9={c.Key9}");
        // 松开 A(摇杆还推着): 光标继续跟着手走, 不回中
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0, LX = 30000, LY = 30000, LeftTrigger = 255 }, t);
        Check("松手后光标还在 3 上", c.Key9 == 3, $"Key9={c.Key9}");

        // 松开 LT 应该回九键
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
        Check("松开 LT 回到九键", c.Layout == Layout.CN9, $"Layout={c.Layout}");

        // 短按 = 出数字
        TextOutput.Reset();
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0, LeftTrigger = 255, LX = 30000, LY = 30000 }, t);
        Hold(GamepadInput.A, 60, 30000, 30000, 255);
        Check("短按还是出数字 3", TextOutput.Flatten().Contains("3"),
              $"发出=\"{TextOutput.Flatten()}\"");
    }

    // ==================================================================
    Console.WriteLine("\n=== 7b) 界面收起时松 LT, 不能把数字层永远留在屏幕上 ===");
    {
        var c = NewCore(true);
        var t = T0;
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0, LeftTrigger = 255, LX = 30000, LY = 30000 }, t);
        Check("先在数字层里", c.Layout == Layout.NUM9, $"Layout={c.Layout}");
        c.Visible = false;                       // 界面被收起(比如焦点被抢了)
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0 }, t);   // 松 LT 时界面是收着的
        Check("收着的时候松 LT 也还原成九键", c.Layout == Layout.CN9, $"Layout={c.Layout}");
        c.Visible = true;
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
        Check("再唤出来不是数字层", c.Layout == Layout.CN9, $"Layout={c.Layout}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 8) 发快捷键: 关 F → 焦点还游戏 → (到点)发组合键 ===");
    {
        ResetQuick();
        QuickStore.Set(0, QKind.Hotkey, "alt+r");
        var c = NewCore(true);
        var t = T0;
        c.QuickSetNote("没打完的一句话");
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 0;
        TextOutput.Reset();
        Tap(c, ref t, GamepadInput.A);
        Check("先关掉了 F", !c.QuickOpen && !c.QuickMode, $"QuickOpen={c.QuickOpen}");
        Check("焦点还给了游戏", TextOutput.Sent.Contains("FOCUSGAME"),
              $"发出={string.Join("|", TextOutput.Sent)}");
        Check("这一帧还没发键(要等焦点到位)", !TextOutput.Flatten().Contains("CHR:"),
              $"发出=\"{TextOutput.Flatten()}\"");
        t = t.AddMilliseconds(400);                   // 等焦点切换的缓冲过去
        c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
        // 【认 CH: 前缀】发快捷键走的是 ChordRaw —— 不过那道"先把预览框升起来
        // 再发键"的闸门(见 TextOutput.ChordRaw), 否则发一次快捷键就凭空多出一个
        // 空预览框。桩里 Chord 记 "CH:", ChordRaw 记 "CHR:", 认前缀就能一起认。
        Check("到点后发出了组合键(走 ChordRaw, 不过预览框闸门)",
              TextOutput.Sent.Any(x => x.StartsWith("CHR:")),
              $"发出={string.Join("|", TextOutput.Sent)}");
        Check("预览框里的字没被动过", NoteOf(c) == "没打完的一句话", $"框里=\"{NoteOf(c)}\"");
    }

    // ==================================================================
    Console.WriteLine("\n=== 9) 绑数字: A 定下 / L3 解除 / B 退回 ===");
    {
        ResetQuick();
        QuickStore.Set(20, QKind.Phrase, "绑号测试");       // 第 3 页第 3 格, 不占数字
        var c = NewCore(false); var t = T0;
        OpenF(c, ref t);
        var f = UI(c);
        f.Page = 2; f.Sel = 2;                              // 槽位 2*9+2 = 20
        Tap(c, ref t, GamepadInput.A, 600);                 // 进深层
        Check("深层认住的是槽位 20", f.Slot == 20, $"Slot={f.Slot}");
        f.Sel = 4;                                          // 中间 = 绑的数字
        Tap(c, ref t, GamepadInput.A);
        Check("进了绑数字那一层", f.Depth == 3 && f.Layer == InputCore.Q_LAYER_NUM,
              $"Depth={f.Depth} Layer=\"{f.Layer}\"");
        Check("底下那套键盘没被切成数字层(靠浮层挑)", c.Layout == Layout.CN9,
              $"Layout={c.Layout}");

        f.Sel = 6;                                          // 第 7 格 = 数字 7
        Tap(c, ref t, GamepadInput.A);
        Check("绑到了数字 7", QuickStore.ByNum(7)?.Text == "绑号测试",
              $"ByNum(7)={QuickStore.ByNum(7)?.Text}");
        Check("原来那格空出来了", QuickStore.Empty(20));
        Check("绑完退回深层", f.Depth == 2, $"Depth={f.Depth}");
        Check("深层跟着这一条走, 改认第 7 格", f.Slot == 6, $"Slot={f.Slot}");

        // Y = 解除绑定(2.3.3 起; 以前是 L3)
        f.Sel = 4;
        Tap(c, ref t, GamepadInput.A);                      // 再开一层
        Check("又回到绑数字那一层", f.Layer == InputCore.Q_LAYER_NUM, $"Layer=\"{f.Layer}\"");
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.Y }, t);
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
        Check("Y 一下就解除了绑定", QuickStore.ByNum(7) == null,
              $"ByNum(7)={QuickStore.ByNum(7)?.Text}");
        Check("搬到第 2 页往后第一个空位(槽位 9)", QuickStore.At(9)?.Text == "绑号测试",
              $"槽位9={QuickStore.At(9)?.Text}");
        Check("解除完退回深层, 并跟着改认槽位 9", f.Depth == 2 && f.Slot == 9,
              $"Depth={f.Depth} Slot={f.Slot}");

        // B = 退回, 什么都不改
        int n0 = QuickStore.Count;
        f.Sel = 4;
        Tap(c, ref t, GamepadInput.A);
        Tap(c, ref t, GamepadInput.B);
        Check("B 退回深层, 不动绑定", f.Depth == 2 && c.QuickOpen
              && QuickStore.Count == n0, $"Depth={f.Depth} 条数={QuickStore.Count}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 10) 位置就是数字: 不排序, 删了也不往前挪 ===");
    {
        ResetQuick();
        QuickStore.Set(0, QKind.Phrase, "甲");
        QuickStore.Set(1, QKind.Phrase, "乙");
        QuickStore.Set(2, QKind.Phrase, "丙");
        string Order()
        {
            var s = new System.Text.StringBuilder();
            for (int i = 0; i < 12; i++)
            {
                var it = QuickStore.At(i);
                if (it != null) s.Append($"[{i}]{it.Text} ");
            }
            return s.ToString().Trim();
        }
        Check("写在哪一格就是哪个数字, 不排序",
              QuickStore.ByNum(1)?.Text == "甲" && QuickStore.ByNum(3)?.Text == "丙",
              Order());
        int n0 = QuickStore.Count;
        var c = NewCore(false); var t = T0;
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 1;
        Tap(c, ref t, GamepadInput.A, 600);
        f.Sel = 2;                                  // 删除
        Tap(c, ref t, GamepadInput.A);
        Check("删掉中间那条: 条数 -1", QuickStore.Count == n0 - 1, $"条数={QuickStore.Count}");
        Check("后面那条**没**往前挪", QuickStore.At(2)?.Text == "丙" && QuickStore.Empty(1),
              Order());
        Check("数字 2 空了, 数字 3 还是丙",
              QuickStore.ByNum(2) == null && QuickStore.ByNum(3)?.Text == "丙", Order());
    }

    // ==================================================================
    Console.WriteLine("\n=== 11) 摇杆 / 十字键 / LB·RB 翻页: F 里那三样都归那一摞 ===");
    {
        ResetQuick();
        var c = NewCore(false); var t = T0;
        OpenF(c, ref t);
        var f = UI(c);

        // 摇杆: 推哪选哪, 松手回中(和九键选键同一套)
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true, LX = -30000, LY = 30000 }, t);
        Check("摇杆推左上 -> 第 1 格", f.Sel == 0, $"Sel={f.Sel}");
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Check("松手回中 -> 第 5 格", f.Sel == 4, $"Sel={f.Sel}");

        // 十字键: 上下 ±3(三列), 左右 ±1
        Tap(c, ref t, GamepadInput.DPAD_DOWN);
        Check("十字键下 -> 第 8 格", f.Sel == 7, $"Sel={f.Sel}");
        Tap(c, ref t, GamepadInput.DPAD_RIGHT);
        Check("十字键右 -> 第 9 格", f.Sel == 8, $"Sel={f.Sel}");
        Tap(c, ref t, GamepadInput.DPAD_UP);
        Check("十字键上 -> 第 6 格", f.Sel == 5, $"Sel={f.Sel}");

        // LB / RB: 翻页(一页九格, 五页装 45 条)
        Tap(c, ref t, GamepadInput.RB);
        Check("RB -> 第 2 页", f.Page == 1, $"Page={f.Page}");
        Tap(c, ref t, GamepadInput.LB);
        Tap(c, ref t, GamepadInput.LB);
        Check("LB 退到第 1 页就停住(不绕回第 5 页)", f.Page == 0, $"Page={f.Page}");
        for (int i = 0; i < 8; i++) Tap(c, ref t, GamepadInput.RB);
        Check("最多 5 页(0~4)", f.Page == 4, $"Page={f.Page}");

        // F 开着的时候摇杆不去动九键那套光标(底下那套键盘是收着的)
        Check("F 开着时摇杆没去动九键的键位光标", c.Key9 == 5, $"Key9={c.Key9}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 12) B 一层层退, 退到列表层再 B 就关掉 F ===");
    {
        ResetQuick();
        QuickStore.Set(0, QKind.Phrase, "甲");
        var c = NewCore(false); var t = T0;
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 0;
        Tap(c, ref t, GamepadInput.A, 600);          // 深层
        f.Sel = 4;
        Tap(c, ref t, GamepadInput.A);               // 绑数字层
        Check("三层都在", f.Depth == 3, $"Depth={f.Depth}");

        Tap(c, ref t, GamepadInput.B);
        Check("B 退回深层(没关 F)", f.Depth == 2 && c.QuickOpen,
              $"Depth={f.Depth} QuickOpen={c.QuickOpen}");
        Tap(c, ref t, GamepadInput.B);
        Check("B 退回列表层(还没关 F)", f.Depth == 1 && c.QuickOpen,
              $"Depth={f.Depth} QuickOpen={c.QuickOpen}");
        Tap(c, ref t, GamepadInput.B);
        Check("列表层再按 B 才关掉 F", !c.QuickOpen && f.Depth == 0,
              $"Depth={f.Depth} QuickOpen={c.QuickOpen}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 13) 直出版发快捷键: 发得出去, 面板不用关 ===");
    {
        ResetQuick();
        QuickStore.Set(0, QKind.Hotkey, "alt+r");
        var c = NewCore(false);
        var t = T0;
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 0;
        TextOutput.Reset();
        Tap(c, ref t, GamepadInput.A);
        Check("直出版发快捷键: 面板留着(还能接着发下一条)", c.QuickOpen,
              $"QuickOpen={c.QuickOpen}");
        Check("焦点照样还给了游戏", TextOutput.Sent.Contains("FOCUSGAME"),
              $"发出={string.Join("|", TextOutput.Sent)}");
        t = t.AddMilliseconds(400);
        c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
        Check("到点发出了组合键(走 ChordRaw, 不过预览框闸门)",
              TextOutput.Sent.Any(x => x.StartsWith("CHR:")),
              $"发出={string.Join("|", TextOutput.Sent)}");
    }


    // ==================================================================
    Console.WriteLine("\n=== 15) 滑行输入 · 关掉之后 LB 就只是原来那个 LB ===");
    {
        // 影子那一档已经删了, 只剩下开 / 关。这一组测的是"关":
        // 你可以照老办法打到一半去按 LB、把摇杆推一圈, 不能因此多一个字、
        // 也不能丢一个字 —— 手柄上等于没多出这个功能。
        static (double X, double Y) SPos9(int key, double r = 0.9)
        {
            const double D = 0.70710678;
            return key switch
            {
                6 => (r, 0.0), 3 => (r * D, r * D), 2 => (0.0, r), 1 => (-r * D, r * D),
                4 => (-r, 0.0), 7 => (-r * D, -r * D), 8 => (0.0, -r), 9 => (r * D, -r * D),
                _ => (0.0, 0.0),
            };
        }
        // 在这个方向上待 ms 毫秒(按 16ms 一帧喂), LB 全程按着
        void SlideSeg(InputCore c, ref DateTime tt, int key, int ms)
        {
            var (px, py) = SPos9(key);
            short sx = (short)Math.Round(px * 30000), sy = (short)Math.Round(py * 30000);
            tt = tt.AddMilliseconds(16);
            c.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB, LX = sx, LY = sy }, tt);
            int acc = 0;
            while (acc < ms)
            {
                int d = Math.Min(16, ms - acc);
                acc += d; tt = tt.AddMilliseconds(d);
                c.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB, LX = sx, LY = sy }, tt);
            }
        }

        var c = NewCore(false);
        c.SlideMode = SlideMode.Off;
        c.SlideDwellMs = 80;
        TextOutput.Sent.Clear();
        var t = T0;

        Tap(c, ref t, GamepadInput.A, 40, 0, 30000);         // 往上 -> 2
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Tap(c, ref t, GamepadInput.A, 40, -30000, 0);        // 往左 -> 4
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Check("先照老办法打了两个键", c.Digits == "24", $"Digits={c.Digits}");

        SlideSeg(c, ref t, 2, 220);
        SlideSeg(c, ref t, 4, 230);
        SlideSeg(c, ref t, 6, 240);
        Check("关着的时候一个数字都没认出来", c.Slide.Seq == "" && !c.Slide.Active, $"seq={c.Slide.Seq}");
        Check("关着的时候 Digits 纹丝不动", c.Digits == "24", $"Digits={c.Digits}");
        Check("关着的时候也没往外发东西", TextOutput.Sent.Count == 0, string.Join("|", TextOutput.Sent));

        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Tap(c, ref t, GamepadInput.A, 40, 30000, 0);         // 往右 -> 6
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Check("松开 LB 老办法照常接着打", c.Digits == "246", $"Digits={c.Digits}");
    }

    Console.WriteLine("\n=== 16) 滑行输入 · 打字模式: 停一格就出一个数字 ===");
    {
        // 这一组测的是转正之后那一档: 滑出来的数字**真的进拼音**。
        // 兜底是"别的都不许变" —— 它只是换了个办法把数字输进去, 候选/候选框/
        // RB 上屏全是原来那套, 老办法(推摇杆 + A)也还得照常能用。
        static (double X, double Y) TPos9(int key, double r = 0.9)
        {
            const double D = 0.70710678;
            return key switch
            {
                6 => (r, 0.0), 3 => (r * D, r * D), 2 => (0.0, r), 1 => (-r * D, r * D),
                4 => (-r, 0.0), 7 => (-r * D, -r * D), 8 => (0.0, -r), 9 => (r * D, -r * D),
                _ => (0.0, 0.0),
            };
        }
        // 往某个方向推过去待 ms 毫秒(LB 全程按着)。每帧 16ms, 和轮询节奏一致。
        void SlideSeg2(InputCore c, ref DateTime tt, int key, int ms)
        {
            var (px, py) = TPos9(key);
            short sx = (short)Math.Round(px * 30000), sy = (short)Math.Round(py * 30000);
            tt = tt.AddMilliseconds(16);
            c.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB, LX = sx, LY = sy }, tt);
            int acc = 0;
            while (acc < ms)
            {
                int d = Math.Min(16, ms - acc);
                acc += d; tt = tt.AddMilliseconds(d);
                c.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB, LX = sx, LY = sy }, tt);
            }
        }

        // 记录要往临时目录里写 —— 打字模式也照样记账, 别拿你机器上那份标定数据练手
        string saved = Config.Dir;
        string tmp = Path.Combine(Path.GetTempPath(), "gt9_probe_" + Guid.NewGuid().ToString("N")[..8]);
        Config.SetDirForTest(tmp);

        var c = NewCore(false);
        c.SlideMode = SlideMode.Type;
        c.SlideDwellMs = 80;
        TextOutput.Sent.Clear();
        var t = T0;

        SlideSeg2(c, ref t, 2, 220);
        Check("停一格就出一个数字", c.Digits == "2", $"Digits={c.Digits}");
        SlideSeg2(c, ref t, 4, 230);
        Check("**不用等松手**, 第二个也跟上了", c.Digits == "24", $"Digits={c.Digits}");
        SlideSeg2(c, ref t, 6, 240);
        Check("三个都在里了", c.Digits == "246", $"Digits={c.Digits}");
        Check("滑行自己那一串也对得上", c.Slide.Seq == "246", $"seq={c.Slide.Seq}");
        Check("九宫格光标没被拖走", c.Key9 == 5, $"Key9={c.Key9}");

        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Check("松手不会把最后那格再加一遍", c.Digits == "246", $"Digits={c.Digits}");
        Check("滑行收了", !c.Slide.Active, "");

        // 老办法还得能接着往下打
        Tap(c, ref t, GamepadInput.A, 40, -30000, 0);        // 往左 -> 4
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Check("滑完再用老办法打, 接得上", c.Digits == "2464", $"Digits={c.Digits}");

        // X 退格也要照常删掉了尾巴 —— 拼错了唯一的出路是它
        int n0 = c.Digits.Length;
        Tap(c, ref t, GamepadInput.X, 40);
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Check("滑错了能按 X 一个一个退回来", c.Digits.Length == n0 - 1, $"Digits={c.Digits}");

        try { Directory.Delete(tmp, true); } catch { }
        Config.SetDirForTest(saved);
    }

    Console.WriteLine("\n=== 17) 换过位的那两颗键: 左上角是 JKL, 中心不是 ===");
    {
        var c = NewCore(false);
        var t = T0;

        // 按 A 走到左上角: 2.0.0 之后这一格是 JKL, 拼音里 j/k/l 开头的音节都从这里出。
        // (断言只认 Digits —— Tap 收尾会把摇杆松回中间, Key9 自然也就回到 5 了)
        Tap(c, ref t, GamepadInput.A, 40, -30000, 30000);   // 左上 -> 1
        Check("左上角那颗现在会出数字", c.Digits == "1", $"Digits={c.Digits}");
        var pre = T9Engine.Prefixes("1");
        Check("1 解出来就是 j / k / l", pre.Count == 3 && pre.All(s => "jkl".Contains(s[0])), string.Join(",", pre));
        Check("中心那颗已经不认 JKL 了", T9Engine.Prefixes("5").Count == 0,
              string.Join(",", T9Engine.Prefixes("5")));
        Check("hao 还是 426, 没被改动波及", T9Engine.ToT9("hao") == "426", T9Engine.ToT9("hao"));
        Check("ji 现在是 14", T9Engine.ToT9("ji") == "14", T9Engine.ToT9("ji"));

        // 摇杆回中 = 中心那颗。以前这里按下去会多打一个 5,
        // 现在中心是"长按开窗口F", 短按必须干脆不响。
        c.ClearInput();
        int n0 = c.Digits.Length;
        int sent0 = TextOutput.Sent.Count;
        Tap(c, ref t, GamepadInput.A, 40);
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Check("回中之后短按 A 不打字", c.Key9 == 5 && c.Digits.Length == n0, $"Digits={c.Digits}");
        Check("也没多发出去任何东西", TextOutput.Sent.Count == sent0, string.Join("|", TextOutput.Sent));
        Check("窗口F 也没被一按 A 就弹出来", !c.QuickMode, "");

        // 按住不放: 到点才开面板
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.A }, t = t.AddMilliseconds(16));
        for (int i = 0; i < 40; i++)
            c.Poll(new PadState { Connected = true, Buttons = GamepadInput.A }, t = t.AddMilliseconds(16));
        Check("按住中心键才开窗口F", c.QuickMode, "焦点在面板里才算开");
    }

    Console.WriteLine("\n=== 14) 深层里 4 / 6 是空位: 能停能选中, 按 A 没反应 ===");
    {
        ResetQuick();
        QuickStore.Set(0, QKind.Phrase, "甲");
        var c = NewCore(false);
        var t = T0;
        OpenF(c, ref t);
        var f = UI(c);
        f.Sel = 0;
        Tap(c, ref t, GamepadInput.A, 600);        // 进深层
        Check("起手光标回中(中间那一格 = 绑的数字)", f.Sel == 4, $"Sel={f.Sel}");

        // 空位也要能挪上去 —— 九个位置永远在同一处, 手柄靠方向记位置才不会乱
        Tap(c, ref t, GamepadInput.DPAD_LEFT);     // 中间(4) -> 4 号位(3)
        Check("十字键能停在 4 号位这个空位上", f.Sel == 3, $"Sel={f.Sel}");
        int n0 = QuickStore.Count;
        Tap(c, ref t, GamepadInput.A);
        Check("4 号位按 A 什么都没发生", QuickStore.Count == n0 && f.Depth == 2,
              $"条数={QuickStore.Count}(原 {n0}) Depth={f.Depth}");

        Tap(c, ref t, GamepadInput.DPAD_RIGHT);
        Tap(c, ref t, GamepadInput.DPAD_RIGHT);    // -> 中间(4) -> 6 号位(5)
        Check("十字键也能停在 6 号位上", f.Sel == 5, $"Sel={f.Sel}");
        Tap(c, ref t, GamepadInput.A);
        Check("6 号位按 A 也什么都没发生", QuickStore.Count == n0 && f.Depth == 2,
              $"条数={QuickStore.Count}(原 {n0}) Depth={f.Depth}");

        // 深层是两行 + 底下那条长条: 上下挪的时候只在这两行的六个位置里绕
        f.Sel = 0;
        Tap(c, ref t, GamepadInput.DPAD_DOWN);
        Check("深层两行: 从 1 号位往下到 4 号位(空着但能停)", f.Sel == 3, $"Sel={f.Sel}");
        Tap(c, ref t, GamepadInput.DPAD_DOWN);
        Check("再往下绕回第一行(下面让给了那条长条)", f.Sel == 0, $"Sel={f.Sel}");
    }

    Console.WriteLine("\n=== 18) 1 / 5 互换: 换的是格子不是数字, 滑行时一律还原 ===");
    {
        var c = NewCore(false);
        var t = T0;

        Check("默认: 快捷格在正中间", c.QuickCell == 5, $"QuickCell={c.QuickCell}");
        Check("默认: 左上角出 1(JKL)", c.DigitOfCell(1) == 1, $"={c.DigitOfCell(1)}");
        Check("默认: 中间那颗不打字", c.DigitOfCell(5) == 0, $"={c.DigitOfCell(5)}");
        Check("默认: 其余七颗就是格子号", c.DigitOfCell(3) == 3 && c.DigitOfCell(9) == 9, "");

        c.Swap15 = true;
        Check("换了: 快捷格挪到左上角", c.QuickCell == 1, $"QuickCell={c.QuickCell}");
        Check("换了: 中间那颗出 1(JKL)", c.DigitOfCell(5) == 1, $"={c.DigitOfCell(5)}");
        Check("换了: 左上角不打字", c.DigitOfCell(1) == 0, $"={c.DigitOfCell(1)}");
        Check("换了: 其余七颗照旧", c.DigitOfCell(3) == 3 && c.DigitOfCell(9) == 9, "");

        // 真按一遍: 摇杆回中(那颗是 5)按 A, 现在应该打出 JKL 的 1
        c.ClearInput();
        Tap(c, ref t, GamepadInput.A, 40);
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Check("换了之后回中按 A 出的是 1", c.Digits == "1", $"Digits={c.Digits}");

        // 左上角那颗现在短按**不该响**, 长按才开窗口 F
        c.ClearInput();
        Tap(c, ref t, GamepadInput.A, 40, -30000, 30000);
        t = t.AddMilliseconds(20);
        c.Poll(new PadState { Connected = true }, t);
        Check("换了之后左上角短按不打字", c.Digits == "", $"Digits={c.Digits}");
        for (int i = 0; i < 42; i++)
            c.Poll(new PadState { Connected = true, Buttons = GamepadInput.A, LX = -30000, LY = 30000 },
                   t = t.AddMilliseconds(16));
        Check("换了之后长按左上角开窗口F", c.QuickMode, "焦点在面板里才算开");

        // 滑行时一律还原 —— 摇杆方位不能跟着设置漂
        var c2 = NewCore(false);
        c2.Swap15 = true;
        var t2 = T0;
        Check("没按 LB 时快捷格在左上", c2.QuickCell == 1, $"QuickCell={c2.QuickCell}");
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB }, t2 = t2.AddMilliseconds(16));
        Check("一按 LB 进滑行, 快捷格立刻还原到中间", c2.QuickCell == 5, $"QuickCell={c2.QuickCell}");
        Check("滑行时左上角照样是 JKL", c2.DigitOfCell(1) == 1, $"={c2.DigitOfCell(1)}");
        c2.Poll(new PadState { Connected = true }, t2 = t2.AddMilliseconds(16));
        Check("松开 LB 又换回左上", c2.QuickCell == 1, $"QuickCell={c2.QuickCell}");
    }

    Console.WriteLine("\n=== 19) 长串候选: 第一个字的各种可能一个都不能漏 ===");
    {
        // 这一组就是用户报的那个毛病: 四个字的拼音一长, 候选里全是 san / ran / rao
        // 开头的, 想找 pao 开头的怎么翻都翻不到 —— 长串的 beam 是按整串分数裁的,
        // 冷门的第一音节走到第三四层就被热门的整条挤出池子了。
        var c = NewCore(false);

        // 726 这三位能解出 pan / pao / ran / rao / san / sao … 一整把
        string head = "726";
        string digits = head + "486";       // 后面再接一截, 把串拉长
        c.Digits = digits;
        c.DebugRefreshCands();

        var firsts = new HashSet<string>(c.Segs.Where(s => s.Length > 0).Select(s => s[0]));
        Console.WriteLine("    候选里出现的第一个字: " + string.Join(" ", firsts));
        Check("候选不是空的", c.Segs.Count > 0, $"{c.Segs.Count} 条");

        // 726 那截能解出的音节, 每一个都必须在候选里露一次脸
        var want = T9Engine.SyllablesOf(head);
        Console.WriteLine("    " + head + " 能解出: " + string.Join(" ", want));
        Check(head + " 本身能解出好几个音节(用例前提)", want.Count >= 3, string.Join(",", want));
        foreach (var w in want)
            Check($"候选里有 {w} 开头的", firsts.Contains(w), string.Join(" ", firsts));

        // 同一条不能在候选里出现两遍。
        // 【2.1.19】这里比的是**带撇的那一整串**(也就是真发给输入法的那一串):
        // 以前比字母串, 于是 pao'gu'o 和 pao'guo 被当成同一条, 后者被整条丢掉 ——
        // 想让第一个字是 pa 的人就再也选不出来了。撇是真会发出去的(见 SendPinyin),
        // 切法不同就是两条, 该都在; 只有**连撇都一样**才是真重复。
        var joined = c.Segs.Select(s => string.Join("'", s)).ToList();
        Check("没有重复的候选", joined.Count == new HashSet<string>(joined).Count, string.Join(" ", joined));

        // 也不能因为补齐就失控: 总数要有上限, 不至于排列组合爆炸
        Check("条数还在闸内(<=28)", c.Segs.Count <= 28, $"{c.Segs.Count} 条");

        // 更长的串也一样: 拿 8 位试一遍
        c.Digits = "72648674";
        c.DebugRefreshCands();
        var firsts2 = new HashSet<string>(c.Segs.Where(s => s.Length > 0).Select(s => s[0]));
        Console.WriteLine("    8 位串的第一个字: " + string.Join(" ", firsts2));
        foreach (var w in want)
            Check($"8 位串里也有 {w} 开头的", firsts2.Contains(w), string.Join(" ", firsts2));
        Check("8 位串条数也在闸内", c.Segs.Count <= 28, $"{c.Segs.Count} 条");
    }

    Console.WriteLine("\n=== 20) 符号面板里 LB 只管翻页, 绝不能顺手起一次滑行 ===");
    {
        // 用户报的: 点开"更多标点"之后按 LB 翻页, 页面会闪一下; 按 RB 翻页不闪。
        // 根因在 TickSlide 的 can 里**漏了 !Punct** —— 符号面板开着时 LB 是"换分类",
        // 可滑行也认这颗键, 于是同一下 LB 既翻页又起滑: 界面这边拼音行让出一条缝、
        // 又把"滑行"牌子挂出来, 整页抖一下。RB 不走滑行那条路, 所以不闪。
        var c = NewCore(false);
        c.DebugOpenPunct();
        Check("符号面板确实开着(用例前提)", c.Punct, $"Punct={c.Punct}");

        var t = T0;
        int before = c.PunctCat;

        // 按下 LB: 翻页要照旧, 滑行必须一动不动
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB }, t = t.AddMilliseconds(16));
        Check("按 LB 没有起滑行", !c.Slide.Active, $"Slide.Active={c.Slide.Active}");
        Check("LB 翻页照样有效", c.PunctCat != before, $"{before} -> {c.PunctCat}");

        // 松开
        c.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));
        Check("松开 LB 也没有起滑行", !c.Slide.Active, $"Slide.Active={c.Slide.Active}");

        // 同一面板里按 RB 翻页: 本来就不该碰滑行
        int mid = c.PunctCat;
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.RB }, t = t.AddMilliseconds(16));
        Check("按 RB 翻页有效", c.PunctCat != mid, $"{mid} -> {c.PunctCat}");
        Check("按 RB 也没有起滑行", !c.Slide.Active, $"Slide.Active={c.Slide.Active}");
        c.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));

        // 反过来: 关掉符号面板之后, LB 必须还能正常起滑行(别把 !Punct 加成了永久关闭)
        var c2 = NewCore(false);
        var t2 = T0;
        Check("没开符号面板时按 LB 会进滑行", true, "见下一条");
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB }, t2 = t2.AddMilliseconds(16));
        Check("关掉符号面板后 LB 照旧能滑", c2.Slide.Active, $"Slide.Active={c2.Slide.Active}");
    }

    Console.WriteLine("\n=== 22) 自定义唤醒键: 手柄组合 / 挑选页 / 键盘 VK 解析 ===");
    {
        // 默认: 还是 L3+R3 那一套
        var c = new InputCore { Visible = false, Dirty = false };
        var t = T0;
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
               t = t.AddMilliseconds(16));
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
               t = t.AddMilliseconds(16));
        Check("没配自定义时 L3+R3 照旧唤出", c.Visible, $"Visible={c.Visible}");
        c.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
               t = t.AddMilliseconds(16));
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
               t = t.AddMilliseconds(16));
        Check("同一对组合再按一次收起", !c.Visible, $"Visible={c.Visible}");

        // 自定义: 定成 Start 之后 L3+R3 作废, Start 才是唤出键
        var c2 = new InputCore { Visible = false, WakeSpec = "pad:Start" };
        t = T0;
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
                t = t.AddMilliseconds(16));
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
                t = t.AddMilliseconds(16));
        Check("定了自定义后 L3+R3 不再唤出", !c2.Visible, $"Visible={c2.Visible}");
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.START },
                t = t.AddMilliseconds(16));
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.START },
                t = t.AddMilliseconds(16));
        Check("Start 唤出", c2.Visible, $"Visible={c2.Visible}");
        // 按住不放不会连发: 第三帧还按着, 不许又切回去
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.START },
                t = t.AddMilliseconds(16));
        Check("按住不松不连发", c2.Visible, $"Visible={c2.Visible}");
        c2.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.START },
                t = t.AddMilliseconds(16));
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.START },
                t = t.AddMilliseconds(16));
        Check("全松手之后能再切", !c2.Visible, $"Visible={c2.Visible}");

        // Back+Start 是**两个都要**: 只按 Back 不算数
        var c2b = new InputCore { Visible = false, WakeSpec = "pad:Back+Start" };
        t = T0;
        for (int i = 0; i < 3; i++)
            c2b.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK },
                     t = t.AddMilliseconds(16));
        Check("Back+Start 只按 Back 不唤出", !c2b.Visible, $"Visible={c2b.Visible}");
        c2b.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK | GamepadInput.START },
                 t = t.AddMilliseconds(16));
        c2b.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK | GamepadInput.START },
                 t = t.AddMilliseconds(16));
        Check("Back+Start 一起按才唤出", c2b.Visible, $"Visible={c2b.Visible}");

        // 老存档里那种"自由组合"(pad:lb+rb)现在一律不认 —— 留着它是最坏的结果:
        // 手柄这路不认、键盘那路也没有, 就成了怎么都唤不出来(启动时会被替用户删掉)。
        var cOld = new InputCore { Visible = false, WakeSpec = "pad:lb+rb" };
        t = T0;
        cOld.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB | GamepadInput.RB },
                  t = t.AddMilliseconds(16));
        cOld.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB | GamepadInput.RB },
                  t = t.AddMilliseconds(16));
        Check("老存档那种自由组合不认(1)", !cOld.Visible, $"Visible={cOld.Visible}");
        cOld.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
                  t = t.AddMilliseconds(16));
        cOld.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
                  t = t.AddMilliseconds(16));
        Check("老存档那种自由组合不认(2: L3+R3 也照样唤不出来, 所以必须删掉)",
              !cOld.Visible, $"Visible={cOld.Visible}");

        // 定了**键盘**唤醒键之后, 手柄的 L3+R3 也跟着作废 —— 两套同时认的话,
        // 用户按了半天不知道这个界面该算谁叫出来的(以前这里漏了, 于是"用键盘设了却还是
        // L3+R3 能唤出来", 看着像压根没设上)。
        var cKb = new InputCore { Visible = false, WakeSpec = "kb:f1" };
        t = T0;
        cKb.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
                 t = t.AddMilliseconds(16));
        cKb.Poll(new PadState { Connected = true, Buttons = GamepadInput.L3 | GamepadInput.R3 },
                 t = t.AddMilliseconds(16));
        Check("定了键盘唤醒键之后 L3+R3 不再唤出", !cKb.Visible, $"Visible={cKb.Visible}");

        // 唤醒键从"自由组合"收成**清单里挑一个**: 键盘 F1~F12, 手柄 Back / Start /
        // Back+Start。挑的那一页现在是设置页九宫格的一层(见 OverlayWindow 里的
        // PageWakeKb / PageWakePad), 无头探针摸不到界面, 这里只守住**清单和取舍规则**。
        Check("键盘清单就是 F1~F12", InputCore.KeyWakeOptions.Length == 12
              && InputCore.KeyWakeOptions[0] == "f1" && InputCore.KeyWakeOptions[11] == "f12",
              string.Join(",", InputCore.KeyWakeOptions));
        Check("手柄清单只有那三项",
              string.Join("|", InputCore.PadWakeOptions) == "Back|Start|Back+Start|L3|R3",
              string.Join("|", InputCore.PadWakeOptions));
        Check("kb:f7 是合法描述", InputCore.IsWakeSpec("kb:f7"), "");
        Check("pad:Back+Start 是合法描述", InputCore.IsWakeSpec("pad:Back+Start"), "");
        Check("pad:lb+rb(老存档那种自由组合)不认", !InputCore.IsWakeSpec("pad:lb+rb"), "");
        Check("kb:ctrl+f1(老存档那种)不认", !InputCore.IsWakeSpec("kb:ctrl+f1"), "");
        Check("空串 = 默认 L3+R3, 不算自定义", !InputCore.IsWakeSpec(""), "");

        // 设置页的九宫格用**和九键选键同一个**点位映射(摇杆推方向选格、回中落回正中)。
        // 两处必须是同一套手感, 所以直接调的是 InputCore.PointTo9 那一个。
        Check("摇杆推左上 -> 第 1 格", InputCore.PointTo9(-0.8, 0.8) == 1,
              InputCore.PointTo9(-0.8, 0.8).ToString());
        Check("摇杆推右下 -> 第 9 格", InputCore.PointTo9(0.8, -0.8) == 9,
              InputCore.PointTo9(0.8, -0.8).ToString());
        Check("回中 -> 正中第 5 格", InputCore.PointTo9(0, 0) == 5,
              InputCore.PointTo9(0, 0).ToString());

        // VK 解析: 只认 F1~F12, 其余(含老存档里的 ctrl / 单个字母)一律 0 = 认不出
        Check("f9 -> VK_F9", InputCore.VkOfWakeKey("f9") == 0x78, InputCore.VkOfWakeKey("f9").ToString());
        Check("F12 -> VK_F12", InputCore.VkOfWakeKey("F12") == 0x7B, InputCore.VkOfWakeKey("F12").ToString());
        Check("ctrl 不再认(修饰键组合实测唤不出来)", InputCore.VkOfWakeKey("ctrl") == 0,
              InputCore.VkOfWakeKey("ctrl").ToString());
        Check("单个字母也不再认", InputCore.VkOfWakeKey("k") == 0, InputCore.VkOfWakeKey("k").ToString());
        Check("f13 越界不认", InputCore.VkOfWakeKey("f13") == 0, InputCore.VkOfWakeKey("f13").ToString());
    }

    Console.WriteLine("\n=== 23) 唤出那一串键, 手松开之前不许再当普通按键用 ===");
    {
        // 用户报过: 用手柄定了唤出键, 唤出来之后页面会立刻闪一下。
        // 根因是这一串键**既是唤出键、又是功能键**: LB 起滑行、RB 发拼音、A 走按住连发 ——
        // 唤出的那一帧往下接着走, LB+RB 就把滑行开一下又关掉, 界面上那几行的高度跟着
        // 弹一下。现在手柄只留 Back / Start 这两颗闲着的键, 撞车概率已经低了,
        // 但这层保护还在(以后要是放开别的键, 它立刻重新派上用场):
        // 唤出(或收起)之后, **手彻底松开之前一律不往下传**。

        var c = new InputCore { Visible = false, WakeSpec = "pad:Back+Start",
                                Layout = Layout.CN9, Focus = FocusZone.Keyboard };
        var t = T0;
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK | GamepadInput.START },
               t = t.AddMilliseconds(16));
        Check("唤出之前没起滑行", !c.Slide.Active, $"Slide.Active={c.Slide.Active}");
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK | GamepadInput.START },
               t = t.AddMilliseconds(16));
        Check("Back+Start 唤出", c.Visible, $"Visible={c.Visible}");
        // 继续按住五帧: 这一串键还按着, 什么都不该发生
        for (int i = 0; i < 5; i++)
            c.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK | GamepadInput.START },
                   t = t.AddMilliseconds(16));
        Check("按住不放不会起滑行", !c.Slide.Active, $"Slide.Active={c.Slide.Active}");
        Check("按住不放也没连着切显隐", c.Visible, $"Visible={c.Visible}");

        // 松开一半(先放 Start): 剩下的 Back 也不许这时候起别的作用
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK }, t = t.AddMilliseconds(16));
        Check("松开一半不会起滑行", !c.Slide.Active, $"Slide.Active={c.Slide.Active}");
        Check("松开一半也不再切显隐", c.Visible, $"Visible={c.Visible}");

        // 全松开: 保护到这里为止, 手柄的全部功能必须立刻回到原样(别把键永久废掉)
        c.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));
        c.Poll(new PadState { Connected = true, Buttons = GamepadInput.LB }, t = t.AddMilliseconds(16));
        Check("全松开之后 LB 照旧能起滑行", c.Slide.Active, $"Slide.Active={c.Slide.Active}");

        // 再切回去: 收起也走同一层保护(界面都看不见了, 更不能偷偷打字)
        var c2 = new InputCore { Visible = true, WakeSpec = "pad:Back+Start",
                                 Layout = Layout.CN9, Focus = FocusZone.Keyboard };
        t = T0;
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK | GamepadInput.START },
                t = t.AddMilliseconds(16));
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK | GamepadInput.START },
                t = t.AddMilliseconds(16));
        Check("同一个组合把界面收起", !c2.Visible, $"Visible={c2.Visible}");
        for (int i = 0; i < 3; i++)
            c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.BACK | GamepadInput.START },
                    t = t.AddMilliseconds(16));
        Check("收起之后按住不放也不会又唤出来", !c2.Visible, $"Visible={c2.Visible}");

        // 单键唤醒(单个 Back 就能唤出/收起): 按住不放必须不会顺着走成"连发打同一颗键"
        var c3 = new InputCore { Visible = false, WakeSpec = "pad:Start",
                                 Layout = Layout.CN9, Focus = FocusZone.Keyboard };
        t = T0;
        int sent0 = TextOutput.Sent.Count;
        c3.Poll(new PadState { Connected = true, Buttons = GamepadInput.START }, t = t.AddMilliseconds(16));
        c3.Poll(new PadState { Connected = true, Buttons = GamepadInput.START }, t = t.AddMilliseconds(16));
        Check("单键(Start)也能唤出", c3.Visible, $"Visible={c3.Visible}");
        for (int i = 0; i < 8; i++)
            c3.Poll(new PadState { Connected = true, Buttons = GamepadInput.START },
                    t = t.AddMilliseconds(16));
        Check("唤出这一下没有顺手打出字来", TextOutput.Sent.Count == sent0,
              $"发出={string.Join("|", TextOutput.Sent.Skip(sent0))}");
        // 手彻底松开 -> 这层保护必须是**临时的**, 再按一次还得能收起(别把这颗键废掉)
        c3.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));
        c3.Poll(new PadState { Connected = true, Buttons = GamepadInput.START }, t = t.AddMilliseconds(16));
        c3.Poll(new PadState { Connected = true, Buttons = GamepadInput.START }, t = t.AddMilliseconds(16));
        Check("松开之后同一个键还能把界面收起", !c3.Visible, $"Visible={c3.Visible}");
        Check("全程没有把这颗键当字打出去过", TextOutput.Sent.Count == sent0,
              $"发出={string.Join("|", TextOutput.Sent.Skip(sent0))}");
    }
    Console.WriteLine("\n=== 24) 右摇杆标点: 九宫格九格(七常用 + 中间空 + 右下更多), 长推才出 ===");
    {
        // 以前是浮在九宫格上的 6 个圆形扇区。现在改成九颗键自己翻成标点:
        // 取格用 PointTo9(和左摇杆选键同一套), 中间那格留空(松手回中落的就是它),
        // 右下角那格是"更多"。仍然是**长推**才出 —— 蹭过去不算。

        // ---- 先是这张表本身 ----
        Check("标点表九格", InputCore.PunctSymbols.Length == 9,
              $"个数={InputCore.PunctSymbols.Length}");
        Check("正中间那格是空的", InputCore.PunctSymbols[InputCore.Punct9Blank - 1].Length == 0,
              $"第5格=\"{InputCore.PunctSymbols[InputCore.Punct9Blank - 1]}\"");
        Check("右下角是\"更多\"", InputCore.PunctSymbols[InputCore.Punct9More - 1] == "更多",
              $"第9格=\"{InputCore.PunctSymbols[InputCore.Punct9More - 1]}\"");
        int solid = InputCore.PunctSymbols.Count(s => s.Length > 0 && s != "更多");
        Check("除了\"更多\"正好七个常用符号", solid == 7, $"常用={solid}");

        // ---- 长推出一个符号 ----
        var c = new InputCore { Visible = true, Layout = Layout.CN9, Focus = FocusZone.Keyboard };
        var t = T0;
        TextOutput.Reset();
        // 推到左上(第 1 格 = 逗号)
        c.Poll(new PadState { Connected = true, RX = -26000, RY = 26000 }, t = t.AddMilliseconds(16));
        Check("推右摇杆 -> 标点层开了", c.Punct9, $"Punct9={c.Punct9}");
        Check("落在第 1 格", c.Punct9Sel == 1, $"Punct9Sel={c.Punct9Sel}");
        Check("刚推上来的那一帧还没出东西", TextOutput.Sent.Count == 0,
              $"发出={string.Join("|", TextOutput.Sent)}");
        // 停够 400ms(每一帧都重推同一个位置)
        for (int i = 0; i < 30; i++)
            c.Poll(new PadState { Connected = true, RX = -26000, RY = 26000 }, t = t.AddMilliseconds(16));
        // 符号走的是 UNICODE 直投(发出里带 "U:" 前缀), 所以用 Flatten 比对
        Check("长推够了才出符号, 出的是第 1 格那个",
              TextOutput.Flatten().Contains(InputCore.PunctSymbols[0]),
              $"发出={string.Join("|", TextOutput.Sent)} 期望={InputCore.PunctSymbols[0]}");

        // ---- 中间那格留空: 停多久都不出 ----
        var c2 = new InputCore { Visible = true, Layout = Layout.CN9, Focus = FocusZone.Keyboard };
        t = T0;
        TextOutput.Reset();
        for (int i = 0; i < 40; i++)
            c2.Poll(new PadState { Connected = true, RX = 0, RY = 0, LX = 0, LY = 0 },
                    t = t.AddMilliseconds(16));
        // 正中间那格在 PointTo9 里是 |nx|<1/3 且 |ny|<1/3, 同时半径得过 0.35 的死区,
        // 所以得推到一个"斜着、不太远"的点上(0.30, 0.30 -> r≈0.42)。
        c2.Poll(new PadState { Connected = true, RX = 9830, RY = 9830 }, t = t.AddMilliseconds(16));
        Check("推到正中间: 落在第 5 格", c2.Punct9Sel == InputCore.Punct9Blank,
              $"Punct9Sel={c2.Punct9Sel}");
        for (int i = 0; i < 60; i++)
            c2.Poll(new PadState { Connected = true, RX = 9830, RY = 9830 }, t = t.AddMilliseconds(16));
        Check("正中间停再久也不出符号", TextOutput.Sent.Count == 0,
              $"发出={string.Join("|", TextOutput.Sent)}");

        // ---- 蹭过去不算: 一路划过好几格, 停不够就一个字都不该出 ----
        var c3 = new InputCore { Visible = true, Layout = Layout.CN9, Focus = FocusZone.Keyboard };
        t = T0;
        TextOutput.Reset();
        (short x, short y)[] sweep = { (-26000, 26000), (0, 26000), (26000, 26000), (26000, 0) };
        foreach (var (x, y) in sweep)
            for (int i = 0; i < 3; i++)     // 每格只停 3 帧(约 48ms), 远不够 400
                c3.Poll(new PadState { Connected = true, RX = x, RY = y }, t = t.AddMilliseconds(16));
        Check("划过去的格子一个都没出", TextOutput.Sent.Count == 0,
              $"发出={string.Join("|", TextOutput.Sent)}");

        // ---- 回中收起 ----
        var c4 = new InputCore { Visible = true, Layout = Layout.CN9, Focus = FocusZone.Keyboard };
        t = T0;
        c4.Poll(new PadState { Connected = true, RX = -26000, RY = 26000 }, t = t.AddMilliseconds(16));
        Check("先推出来", c4.Punct9, $"Punct9={c4.Punct9}");
        c4.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));
        Check("回中 -> 标点层收起", !c4.Punct9 && c4.Punct9Sel == 0,
              $"Punct9={c4.Punct9} Sel={c4.Punct9Sel}");

        // ---- 全键盘下推右摇杆: 跟按 LT 进数字层一样, 临时回到九键 ----
        var c5 = new InputCore { Visible = true, Layout = Layout.EN26, Focus = FocusZone.Keyboard };
        t = T0;
        c5.Poll(new PadState { Connected = true, RX = -26000, RY = 26000 }, t = t.AddMilliseconds(16));
        Check("全键盘: 推右摇杆临时回到九键", c5.Punct9 && c5.Layout == Layout.CN9,
              $"Punct9={c5.Punct9} Layout={c5.Layout}");
        c5.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));
        Check("回中之后还回全键盘", !c5.Punct9 && c5.Layout == Layout.EN26,
              $"Punct9={c5.Punct9} Layout={c5.Layout}");

        // ---- 数字层里推右摇杆: 退出后还回数字层, 不能被甩回九键 ----
        var c6 = new InputCore { Visible = true, Layout = Layout.CN9, Focus = FocusZone.Keyboard };
        t = T0;
        // LT 是扳机(不是按钮位), 走 LeftTrigger
        c6.Poll(new PadState { Connected = true, LeftTrigger = 255 }, t = t.AddMilliseconds(16));
        Check("先按住 LT 进数字层", c6.Layout == Layout.NUM9, $"Layout={c6.Layout}");
        c6.Poll(new PadState { Connected = true, LeftTrigger = 255, RX = -26000, RY = 26000 },
                t = t.AddMilliseconds(16));
        c6.Poll(new PadState { Connected = true, LeftTrigger = 255 }, t = t.AddMilliseconds(16));
        Check("出完标点还回数字层", !c6.Punct9 && c6.Layout == Layout.NUM9,
              $"Punct9={c6.Punct9} Layout={c6.Layout}");

        // ---- 界面收起时标点层必须跟着收(不然还占着九键的"面") ----
        var c7 = new InputCore { Visible = true, Layout = Layout.CN9, Focus = FocusZone.Keyboard };
        t = T0;
        c7.Poll(new PadState { Connected = true, RX = -26000, RY = 26000 }, t = t.AddMilliseconds(16));
        Check("先推出来(收起前)", c7.Punct9, $"Punct9={c7.Punct9}");
        c7.Visible = false;
        c7.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));
        Check("界面收起 -> 标点层一起收", !c7.Punct9, $"Punct9={c7.Punct9}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 25) 设置页的 B: 点一下退一层, 长按直接回第一层 ===");
    // 界面是 WPF, 无头探针摸不到 —— 这里守的是**InputCore 那半边的时序**:
    // "按下去先不动, 等松手(短按)或按够 420ms(长按)再决定"这条规矩一旦破掉,
    // 长按就会先掉一层再跳回去, 看着就是"长按也是一层层退"。
    {
        // depth>1 时: 按下不应立刻退层, 松手才退一层
        var calls = new List<string>();
        int depth = 3;
        var t = T0;
        var c1 = new InputCore { Visible = true, Settings = true };
        c1.SettingsDepth = () => depth;
        c1.SettingsBack = toRoot => { calls.Add(toRoot ? "root" : "one"); return true; };
        c1.ToggleSettings = () => calls.Add("toggle");
        t = T0;
        c1.Poll(new PadState { Connected = true, Buttons = GamepadInput.B }, t = t.AddMilliseconds(16));
        Check("深层按下 B: 先不动(给长按留位置)", calls.Count == 0, string.Join(",", calls));
        c1.Poll(new PadState { Connected = true, Buttons = GamepadInput.B }, t = t.AddMilliseconds(100));
        Check("还没到 420ms 仍然不动", calls.Count == 0, string.Join(",", calls));
        c1.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));   // 松手
        Check("松手 -> 退一层", calls.Count == 1 && calls[0] == "one", string.Join(",", calls));

        // 长按够了 -> 一路回首页, 而且**只回一次**(不能每帧一层)
        calls.Clear();
        var c2 = new InputCore { Visible = true, Settings = true };
        c2.SettingsDepth = () => depth;
        c2.SettingsBack = toRoot => { calls.Add(toRoot ? "root" : "one"); return true; };
        c2.ToggleSettings = () => calls.Add("toggle");
        t = T0;
        c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.B }, t = t.AddMilliseconds(16));
        for (int k = 0; k < 40; k++)                       // 按住不放, 640ms
            c2.Poll(new PadState { Connected = true, Buttons = GamepadInput.B },
                    t = t.AddMilliseconds(16));
        Check("按住够时间 -> 直接回第一层", calls.Count == 1 && calls[0] == "root",
              string.Join(",", calls));
        c2.Poll(new PadState { Connected = true }, t = t.AddMilliseconds(16));   // 松手
        Check("松手不再补那一下短按", calls.Count == 1, string.Join(",", calls));

        // 已经在首页: B 就是"翻回键盘", 按下立刻走, 不用等松手
        calls.Clear();
        var c3 = new InputCore { Visible = true, Settings = true };
        c3.SettingsDepth = () => 1;
        c3.SettingsBack = toRoot => { calls.Add(toRoot ? "root" : "one"); return false; };
        c3.ToggleSettings = () => calls.Add("toggle");
        t = T0;
        c3.Poll(new PadState { Connected = true, Buttons = GamepadInput.B }, t = t.AddMilliseconds(16));
        Check("首页按 B 立刻翻回键盘", calls.Count == 2 && calls[0] == "one" && calls[1] == "toggle",
              string.Join(",", calls));
    }

    // ==================================================================
    Console.WriteLine("\n=== 26) 设置页光标回中: 进层 / 退层都落在正中, 不许退回左上角 ===");
    // 这套算法以前是 OverlayWindow 里的私有嵌套类, 无头探针够不着, 于是"回中"
    // 只能靠翻界面拿眼睛看 —— 现在它在 SetNav 里(纯算法, 已编进本探针)。
    // 守的是这一条: **正中那格是空位也要停正中**。少了它, 只有两格 / 四格这种
    // "正中正好空着"的层会退到"离正中最近的有内容的格", 而那种层里每一格到
    // 正中的距离都一样, 于是永远挑最靠前的那一格 —— 界面上就是"从第三层退回
    // 第二层, 光标回左上角去了"。
    {
        // n 格、第 page 页的一层(默认按二级起的规矩: 固定 3×3 翻页)
        SetPage Layer(int n, int page = 0, bool nine = true, int off = -1)
        {
            var cells = new List<SetCell>();
            for (int i = 0; i < n; i++)
            {
                // 必须抄一份到局部变量: for 的 i 是同一个变量, 闭包直接抓它的话
                // 所有格子看到的都是循环结束后的那个 i(置灰那条就永远判不中)。
                int k = i;
                cells.Add(SetNav.Cell("项" + k, off: () => k == off));
            }
            return new SetPage { Nine = nine, Cells = cells, Page = page };
        }
        string Where(SetPage p) => $"正中={SetNav.SlotsOf(p) / 2} 实际={SetNav.HomeSlot(p)}";

        var p8 = Layer(8, nine: false);                    // 首页: 三列往下排
        Check("首页八格 -> 正中", SetNav.HomeSlot(p8) == 4, Where(p8));

        var p9 = Layer(9);
        Check("二级九格 -> 正中", SetNav.HomeSlot(p9) == 4, Where(p9));

        var p2 = Layer(2);
        Check("二级只有两格(正中是空位) -> 仍然正中", SetNav.HomeSlot(p2) == 4, Where(p2));

        var p4 = Layer(4);
        Check("二级只有四格(正中是空位) -> 仍然正中", SetNav.HomeSlot(p4) == 4, Where(p4));

        var p1 = Layer(1);
        Check("二级只有一格 -> 仍然正中", SetNav.HomeSlot(p1) == 4, Where(p1));

        var p12 = Layer(12, page: 1);                      // 翻到第二页(还剩三格)
        Check("翻页翻到只剩三格 -> 仍然正中", SetNav.HomeSlot(p12) == 4, Where(p12));

        // 正中那格被置灰(这一档现在用不上): 落到离正中最近那一格, 也不能是左上角
        var pOff = Layer(9, off: 4);
        Check("正中置灰 -> 退到旁边, 不是左上角",
              SetNav.HomeSlot(pOff) == 1 && !SetNav.Landable(pOff, 4), Where(pOff));

        // 回中的那一格必须**真的能停**: 否则随后那次 Clamp 会把它拽回 FirstLandable(0)
        foreach (var (name, p) in new[] { ("首页", p8), ("九格", p9), ("两格", p2),
                                          ("四格", p4), ("一格", p1), ("翻页", p12) })
        {
            p.Sel = SetNav.HomeSlot(p);
            SetNav.Clamp(p);
            Check($"回中那一格能停住({name}): Clamp 不会把它拽走",
                  p.Sel == SetNav.HomeSlot(p) && SetNav.Landable(p, p.Sel),
                  $"Clamp 后={p.Sel} 期望={SetNav.HomeSlot(p)}");
        }
    }

    // ==================================================================
    Console.WriteLine("\n=== 27) 窗口F 深层那条长条(789 合成一条)参与选格 ===");
    // 用户原话: "还是把摇杆放松位置放在中间也就是5这个位置上, 把789这个位置,
    // 当做一条…当摇杆挪向原来的789任意方向的时候, 都触发789这一条的高亮和动效,
    // 但是注意这条虽然能选中, 但是点击时无效的"。
    // 守三条:
    //   ① 松手回中落在 **5 号位**(下标 4), 不是 2 号位 —— 以前两行的"中间行"
    //      被算成第一行, 正中就成了 2 号位「写入快捷键」;
    //   ② 摇杆推向左下 / 正下 / 右下, 选中的都是**同一条**(长条那一个位置);
    //   ③ 长条那一个位置**能停能亮**(Landable), 但 CellAt 是 null —— 于是
    //      LayerActivate 里 `cell == null -> return`, 按 A 什么也不干。
    {
        // 窗口F 深层的样子: 正文两行(写入短语 / 写入快捷键 / 删除 / 空 / 绑的数字 / 空)
        // 前六格照三列顺序排, 第七格**横跨整行**(底下那条长条)。
        // 就这一行 Layout 声明 —— 没有第二处要改。
        SetPage Deep()
        {
            var cells = new List<SetCell>
            {
                SetNav.Cell("写入短语"), SetNav.Cell("写入快捷键"), SetNav.Cell("删除"),
                null, SetNav.Cell("5"), null,
                SetNav.Block(() => new object()),       // 7 号位: 那条长条
            };
            return new SetPage
            {
                Nine = true, Rows = 2, Cells = cells,
                Layout = () => new List<Span>
                {
                    new Span(0, 0), new Span(0, 1), new Span(0, 2),
                    new Span(1, 0), new Span(1, 1), new Span(1, 2),
                    new Span(2, 0, 1, 3),                  // 7 8 9 合成的那条
                },
            };
        }
        var p = Deep();
        int foot = SetNav.SlotsOf(p) - 1;            // 长条那一个位置 = 6
        Check("两行 + 一条 = 七个位置", SetNav.SlotsOf(p) == 7, $"slots={SetNav.SlotsOf(p)}");
        Check("长条坐在第三行", SetNav.RowOf(p, foot) == 2, $"row={SetNav.RowOf(p, foot)}");
        Check("长条横跨三列(导航上算中间那列)", SetNav.ColOf(p, foot) == 1,
              $"col={SetNav.ColOf(p, foot)}");

        Check("松手回中落在 5 号位(下标 4), 不是 2 号位",
              SetNav.HomeSlot(p) == 4, $"实际={SetNav.HomeSlot(p)}");

        // 摇杆: 九宫格第 n 格(0..8) -> 这一层的第几号位置
        Check("摇杆推正下 -> 选中长条", SetNav.StickSlot(p, 7) == foot, $"实际={SetNav.StickSlot(p, 7)}");
        Check("摇杆推左下 -> 还是长条", SetNav.StickSlot(p, 6) == foot, $"实际={SetNav.StickSlot(p, 6)}");
        Check("摇杆推右下 -> 还是长条", SetNav.StickSlot(p, 8) == foot, $"实际={SetNav.StickSlot(p, 8)}");
        Check("摇杆推正中 -> 5 号位", SetNav.StickSlot(p, 4) == 4, $"实际={SetNav.StickSlot(p, 4)}");
        Check("摇杆推正上 -> 2 号位", SetNav.StickSlot(p, 1) == 1, $"实际={SetNav.StickSlot(p, 1)}");

        // 长条能停(于是会高亮、会浮起来弹一下), 但按 A 没反应
        Check("长条那一格能停(能高亮)", SetNav.Landable(p, foot), "");
        var footCell = SetNav.CellAt(p, foot);
        Check("长条是一格真格子(摆的是自己造的控件)", footCell?.Custom != null, "");
        Check("长条只给人看 -> 按 A 没反应",
              footCell?.Act == null && footCell?.Child == null, "");
        p.Sel = foot;
        SetNav.Clamp(p);
        Check("光标停在长条上时 Clamp 不会把它拽走", p.Sel == foot, $"Sel={p.Sel}");

        // 十字键往下: 从 5 号位(第二行中间)往下挪一行 -> 落在长条上
        Check("从 5 号位往下挪一行 -> 长条", SetNav.SlotAt(p, 2, 1) == foot,
              $"实际={SetNav.SlotAt(p, 2, 1)}");
        Check("从 4 号位往下挪一行 -> 也落到长条", SetNav.SlotAt(p, 2, 0) == foot,
              $"实际={SetNav.SlotAt(p, 2, 0)}");

        // 没有长条的那层: 第三行本来就归 7 / 8 / 9 三格, 不能被当成一条
        var p9 = new SetPage { Nine = true, Rows = 3,
                               Cells = Enumerable.Range(0, 9)
                                                 .Select(i => SetNav.Cell("项" + i)).ToList() };
        Check("普通九格层: 正下 / 左下 / 右下是三个不同的位置",
              SetNav.StickSlot(p9, 7) == 7 && SetNav.StickSlot(p9, 6) == 6
              && SetNav.StickSlot(p9, 8) == 8,
              $"{SetNav.StickSlot(p9, 6)},{SetNav.StickSlot(p9, 7)},{SetNav.StickSlot(p9, 8)}");
        Check("普通九格层只有九个位置", SetNav.SlotsOf(p9) == 9, $"slots={SetNav.SlotsOf(p9)}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 28) 设置面占着屏幕的时候, Y 短按不许切九键 / 全键盘 ===");
    // 2.1.3 那次的原话: "R3进入设置之后, 按L3还是会切换9键和全键盘, 只是因为在设置,
    // 所以看不到变化, 只有从设置退出能看到, 改为在设置页面 L3切换页面功能失效。"
    // 2.3.2 起换布局这桩活从 L3 搬到了 **Y 短按**(见 InputCore.SwapLayout), 规矩照旧:
    //   ① 平时那一手(按下 + 松开 Y)照旧在九键 / 全键盘之间切 —— 别把这个改坏;
    //   ② 设置面翻出来的时候同一手**什么也不切**;
    //   ③ 从设置退回来之后又照旧能切。
    {
        var c = NewCore(true);
        var t = T0.AddSeconds(10);
        c.Layout = Layout.CN9;

        Tap(c, ref t, GamepadInput.Y);
        Check("平时按一下 Y -> 切成全键盘", c.Layout == Layout.EN26, $"Layout={c.Layout}");
        Tap(c, ref t, GamepadInput.Y);
        Check("再按一下 Y -> 切回九键", c.Layout == Layout.CN9, $"Layout={c.Layout}");

        // 翻到背面(摆出来的是设置的九宫格, 底下那套九键看不见)
        c.Settings = true;
        Tap(c, ref t, GamepadInput.Y);
        Check("设置面摊着的时候按 Y 不切", c.Layout == Layout.CN9, $"Layout={c.Layout}");

        c.Settings = false;
        Tap(c, ref t, GamepadInput.Y);
        Check("退回来之后 Y 又管用了", c.Layout == Layout.EN26, $"Layout={c.Layout}");

        // 2.3.3: L3 彻底闲着(换布局归 Y 短按, 数字层出 0 和窗口F 解绑也归了 Y)
        var c2 = NewCore(true);
        var t2 = T0.AddSeconds(10);
        c2.Layout = Layout.CN9;
        Tap(c2, ref t2, GamepadInput.L3);
        Check("L3 不再换布局(归 Y 了)", c2.Layout == Layout.CN9, $"Layout={c2.Layout}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 29) 「关于」那一页: 九格并成一大格, 摇杆晃不动 ===");
    // 2.1.6: 关于不再弹 Windows 消息框, 改成设置页里的一页 —— 整页就一格,
    // 说明文字摆在里头, 按 A 复制。于是导航上只有一条规矩: **就一个位置**,
    // 摇杆推哪儿、十字键往哪儿挪, 光标都还在那一格上。
    // 2.1.7: 那一大格不再靠 Solo 开关, 而是**写一行声明** —— 占满整个 3×3。
    {
        var p = new SetPage { Nine = true,
                              Cells = new List<SetCell> { SetNav.Cell("柄输 Binput...") },
                              Layout = () => new List<Span> { new Span(0, 0, 3, 3) } };
        Check("整页只有一个位置", SetNav.SlotsOf(p) == 1, $"slots={SetNav.SlotsOf(p)}");
        Check("只有一页(不翻页)", SetNav.PagesOf(p) == 1, $"pages={SetNav.PagesOf(p)}");
        Check("回中就是那一格", SetNav.HomeSlot(p) == 0, $"实际={SetNav.HomeSlot(p)}");
        Check("那一格能停(于是光标恒在它上面)", SetNav.Landable(p, 0), "");
        Check("摆在第 0 行第 0 列(横跨三列, 导航上算中间那列)",
              SetNav.RowOf(p, 0) == 0 && SetNav.ColOf(p, 0) == 1,
              $"{SetNav.RowOf(p, 0)},{SetNav.ColOf(p, 0)}");
        Check("那一格占 3×3", SetNav.TileOf(p, 0).MaxSide == 3, "");

        // 摇杆: 九个方向**全是**那一格 —— 这就是"晃不动"
        string all = string.Join(",", Enumerable.Range(0, 9).Select(n => SetNav.StickSlot(p, n)));
        Check("摇杆推九个方向都还是那一格(晃不动)", all == "0,0,0,0,0,0,0,0,0", all);
        // 十字键按行列挪, 任何一个落点也都归那一格
        Check("十字键挪一圈也还是那一格",
              SetNav.SlotAt(p, 0, 0) == 0 && SetNav.SlotAt(p, 0, 1) == 0
              && SetNav.SlotAt(p, 0, 2) == 0 && SetNav.SlotAt(p, 1, 0) == 0
              && SetNav.SlotAt(p, 2, 2) == 0, "");

        // 网格照样铺三行 —— 那一格横跨三行三列把它撑满, 于是它正好是九键那块 360×360。
        // 只铺一行的话它就被压成一条扁条。
        Check("RowsOf 报三行(3×3 由那一格横跨撑出来)", SetNav.RowsOf(p) == 3,
              $"rows={SetNav.RowsOf(p)}");
        Check("没有长条那回事(摆法表里就一项)", SetNav.SlotsOf(p) == 1, "");
        p.Sel = 0;
        SetNav.Clamp(p);
        Check("Clamp 不会把光标拽出这一页", p.Sel == 0, $"Sel={p.Sel}");
    }

    // ==================================================================
    Console.WriteLine("\n=== 30) 合块只写一行声明: 1×2 横块 / 3×1 竖块 ===");
    // 2.1.7: "把几格合成一块"现在只有一种说法 —— 每格声明它占哪块矩形(见 Span)。
    // 以前长条和一大格是两套各写各的特判, 下面这两种形状**根本做不出来**。
    // 这一组锁的是"写完那一行声明就有这套导航", 不用连手柄翻界面去试。
    {
        var pH = new SetPage
        {
            Nine = true, Rows = 2,
            Cells = new List<SetCell>
            { SetNav.Cell("上半条"), SetNav.Cell("左下"), SetNav.Cell("右下") },
            Layout = () => new List<Span>
            { new Span(0, 0, 1, 2), new Span(1, 0), new Span(1, 1) },
        };
        Check("横块那一格横跨两列", SetNav.TileOf(pH, 0).Cols == 2, "");
        Check("横块盖住第 0 行第 1 列", SetNav.SlotAt(pH, 0, 1) == 0,
              $"实际={SetNav.SlotAt(pH, 0, 1)}");
        Check("摇杆推正上 / 左上 -> 都是横块",
              SetNav.StickSlot(pH, 1) == 0 && SetNav.StickSlot(pH, 0) == 0,
              $"{SetNav.StickSlot(pH, 0)},{SetNav.StickSlot(pH, 1)}");
        Check("摇杆推左下 -> 第 1 号位", SetNav.StickSlot(pH, 3) == 1,
              $"实际={SetNav.StickSlot(pH, 3)}");
        Check("这一层没第三行 -> 往下推没处去", SetNav.StickSlot(pH, 7) == -1,
              $"实际={SetNav.StickSlot(pH, 7)}");
        Check("横块只铺两行", SetNav.RowsOf(pH) == 2, $"rows={SetNav.RowsOf(pH)}");

        var pV = new SetPage
        {
            Nine = true, Rows = 3,
            Cells = new List<SetCell>
            { SetNav.Cell("左竖条"), SetNav.Cell("右上"), SetNav.Cell("右中") },
            Layout = () => new List<Span>
            { new Span(0, 0, 3, 1), new Span(0, 1), new Span(1, 1) },
        };
        Check("竖块那一格纵跨三行", SetNav.TileOf(pV, 0).Rows == 3, "");
        Check("竖块盖住第 2 行第 0 列", SetNav.SlotAt(pV, 2, 0) == 0,
              $"实际={SetNav.SlotAt(pV, 2, 0)}");
        Check("摇杆推左下 -> 还是竖块", SetNav.StickSlot(pV, 6) == 0,
              $"实际={SetNav.StickSlot(pV, 6)}");
        Check("摇杆推正上(第 0 行第 1 列) -> 第 1 号位", SetNav.StickSlot(pV, 1) == 1,
              $"实际={SetNav.StickSlot(pV, 1)}");
    }
// ==================================================================
// 右摇杆推到"更多"扇区(右下角)停够 700ms -> 唤出全部符号(和 coretest 同一套)。
void OpenPunctHere(InputCore c, ref DateTime t)
{
    double a = -60.0 * Math.PI / 180.0;
    short rx = (short)(Math.Cos(a) * 32767 * 0.9);
    short ry = (short)(Math.Sin(a) * 32767 * 0.9);
    for (int i = 0; i < 9; i++)
    {
        t = t.AddMilliseconds(120);                       // 9 x 120 = 1080ms > 700
        c.Poll(new PadState { Connected = true, Buttons = 0, RX = rx, RY = ry }, t);
    }
    t = t.AddMilliseconds(60);                            // 回中, 轮盘收起
    c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
}

// ==================================================================
Console.WriteLine("\n=== 31) 2.1.10: 设置只从九键首页 / 全键盘进得去(2.3.2 起那一手是长按 Y) ===");
// 以前哪儿都能翻到设置, 于是"开窗口F -> 翻进设置 -> 按 B"退的是背后那摞 F,
// 2.1.9 只好加一整套"设置盖在上面时手柄归设置"的让位逻辑。现在干脆只留两处:
// 那两处**都没有上一层**, "退出来退到哪儿"这个问题就不存在了, 那套逻辑整个退回
// (QuickMode 又变回 QuickOpen 本身)。右下角齿轮也只在这两页画出来。
// 2.3.2: 进设置的那一手从 **R3 短按** 换成了 **长按 Y**(1 秒) —— 摇杆底下那颗键,
// 手正推着摇杆选键的时候够不着; 而且 R3 短按还和"按住 R3 调界面"共用一颗键。
// 判据 SettingsAllowed 一条没变, 所以下面每一条只是把"按哪一颗、按多久"换掉。
// 【长按用 ms=1100】Tap 的节奏是 按下(+20) -> 保持(ms-40) -> 松开(+20),
// 1100 那一下按住约 1080ms, 稳过 1000ms 那道闸; 40 那一下是短按。
{
    var c = NewCore(true);
    ResetQuick();
    var t = T0;
    int toggle = 0;
    c.ToggleSettings = () => toggle++;

    // 1) 九键首页 —— 行
    Check("九键首页 -> 能进设置", c.SettingsAllowed, $"Layout={c.Layout}");
    Tap(c, ref t, GamepadInput.Y, 1100);
    Check("九键首页 长按 Y -> 翻到设置", toggle == 1, $"toggle={toggle}");

    // 1.5) 短按 Y 只换布局, 不进设置 —— 两桩活不许串味
    Tap(c, ref t, GamepadInput.Y, 40);
    Check("短按 Y 不进设置(那是换布局)", toggle == 1, $"toggle={toggle}");
    Tap(c, ref t, GamepadInput.Y, 40);      // 换回九键, 后面几条的起点才对得上
    Check("短按 Y 把布局换回九键", c.Layout == Layout.CN9, $"Layout={c.Layout}");

    // 2) 已经翻过去了就不再是"进得去"(再按是翻回来, 那是窗口的事)
    c.Settings = true;
    Check("已经在设置里 -> 不算能进", !c.SettingsAllowed, $"Settings={c.Settings}");
    c.Settings = false;

    // 3) 全键盘 —— 行
    c.Layout = Layout.EN26;
    Check("全键盘 -> 能进设置", c.SettingsAllowed, $"Layout={c.Layout}");
    Tap(c, ref t, GamepadInput.Y, 1100);
    Check("全键盘 长按 Y -> 翻到设置", toggle == 2, $"toggle={toggle}");
    c.Layout = Layout.CN9;

    // 4) 数字九键 —— 不行(用户点名的两处里没有它)
    c.Layout = Layout.NUM9;
    Check("数字九键 -> 进不去", !c.SettingsAllowed, $"Layout={c.Layout}");
    Tap(c, ref t, GamepadInput.Y, 1100);
    Check("数字九键 长按 Y -> 没反应", toggle == 2, $"toggle={toggle}");
    c.Layout = Layout.CN9;

    // 5) 全部符号那一大页 —— 不行
    OpenPunctHere(c, ref t);
    Check("唤出了全部符号", c.Punct && c.Focus == FocusZone.CandGrid,
          $"Punct={c.Punct}, Focus={c.Focus}");
    Check("全部符号 -> 进不去设置", !c.SettingsAllowed, $"Punct={c.Punct}");
    Tap(c, ref t, GamepadInput.Y, 1100);
    Check("全部符号里 长按 Y -> 没反应", toggle == 2, $"toggle={toggle}");
    Tap(c, ref t, GamepadInput.B, 40);                    // 退出符号页
    Check("按 B 退出了符号页", !c.Punct, $"Punct={c.Punct}");

    // 6) 窗口F 那一摞 —— 不行, 而且 F 自己一点不受影响
    c.QuickOpenPanel();
    SyncF(c);
    ((IQuickUI)c.QuickUI).Deep();
    int depth0 = UI(c).Depth;
    Check("窗口F 里 -> 进不去设置", !c.SettingsAllowed && c.QuickOpen,
          $"open={c.QuickOpen}, depth={UI(c).Depth}");
    Tap(c, ref t, GamepadInput.Y, 1100);
    Check("窗口F 里 长按 Y -> 没反应, F 也没被顺手关掉",
          toggle == 2 && c.QuickOpen && UI(c).Depth == depth0,
          $"toggle={toggle}, depth {depth0}->{UI(c).Depth}");

    // 6.5) 界面**收起**时 —— 长按 Y 不是唤醒键(2.4.6 修)。
    //      用户报的 bug: 除了唤醒键, 长按 Y 进设置也会把软件唤醒出来。根子是
    //      TickLongY 跑在 Poll"隐藏时不处理"那道闸前面, 收起时按够半秒照样触发
    //      ToggleSettings, 而 App 那边窗口不可见时走 OpenSettings() = 先唤出再翻面。
    //      现在 core 这一层就拦住: 收起时这一桩不认, 唤醒只归唤醒键。
    {
        var c2 = NewCore(true);
        c2.Visible = false;
        int toggle2 = 0;
        c2.ToggleSettings = () => toggle2++;
        Tap(c2, ref t, GamepadInput.Y, 1100);                  // 按住约 1080ms, 稳过闸
        Check("收起时 长按 Y -> 界面不唤出", !c2.Visible, $"Visible={c2.Visible}");
        Check("收起时 长按 Y -> 也不进设置", toggle2 == 0, $"toggle={toggle2}");
        Tap(c2, ref t, GamepadInput.Y, 40);                    // 短按那一下同样没反应
        Check("收起时 短按 Y -> 也不换布局",
              toggle2 == 0 && c2.Layout == Layout.CN9,
              $"toggle={toggle2}, Layout={c2.Layout}");
        c2.Visible = true;                                     // 门只挡"收起"这一态
        Tap(c2, ref t, GamepadInput.Y, 1100);
        Check("唤出后 长按 Y -> 照旧进设置", toggle2 == 1, $"toggle={toggle2}");
    }

    // 7) 顺带: 2.1.9 那套让位逻辑退回去了 —— 设置和 F 不可能同时在,
    //    QuickMode 现在就是 QuickOpen 本身。
    c.Settings = true;
    Check("QuickMode 退回 QuickOpen 本身", c.QuickMode == c.QuickOpen,
          $"QuickMode={c.QuickMode}, QuickOpen={c.QuickOpen}");
    c.Settings = false;
}

// ==================================================================
Console.WriteLine("\n=== 32) 2.1.10: 符号页按 RB 也要把右边那块牌踢一脚 ===");
// RB 在符号网格里走的是 HoldRepeatRb 那一条, 它以前**直接调** NextPunctCategory,
// 绕过了 OnShoulder —— 而框外那对小牌的动效(PageFx)是 OnShoulder 头一句发的。
// 结果: 按 LB 左边跳、按 RB 右边不跳, 看着像 RB 失灵, 其实类照换。
{
    var c = NewCore(true);
    ResetQuick();
    var t = T0;
    var fx = new List<int>();
    c.PageFx = d => fx.Add(d);

    OpenPunctHere(c, ref t);
    Check("唤出了全部符号", c.Punct && c.Focus == FocusZone.CandGrid,
          $"Punct={c.Punct}, Focus={c.Focus}");

    string cat0 = c.PunctCatName;
    Tap(c, ref t, GamepadInput.RB, 40);
    Check("RB -> 换到下一类", c.PunctCatName != cat0, $"{cat0} -> {c.PunctCatName}");
    Check("RB -> 右边那块牌收到 +1", fx.Count == 1 && fx[0] == 1, string.Join(",", fx));

    Tap(c, ref t, GamepadInput.LB, 40);
    Check("LB -> 换回上一类", c.PunctCatName == cat0, $"{c.PunctCatName}");
    Check("LB -> 左边那块牌收到 -1", fx.Count == 2 && fx[1] == -1, string.Join(",", fx));
}

// ==================================================================
Console.WriteLine("\n=== 33) 2.1.14: 换屏管线 —— 什么算\"换了一屏\" ===");
// ScreenId 是这一整套第一次能被自动测住的东西: 它只依赖 InputCore, 不碰 WPF,
// 于是"某个场景漏了动画"不再只有肉眼防线。这一组盯的就是那条流水线认人认得对不对。
{
    // 九键字母那一面 = 根; 其余都算"往外走了一层"
    var root = new ScreenFace(Layout.CN9, false, false, false, false, false, 0, 5, Quick: false);
    Check("起手认得出是字母九键这一面", ScreenId.IsRoot(root), ScreenId.Name(root));

    // 1) 9 宫格 -> 5x5 符号网格。这一跳整整漏了一个版本: 它换的是 Visibility,
    //    而拍照原来排在 Render 第 246 行、写 Visibility 在第 148 行 —— 等轮到相机
    //    面板早就长出来了。现在拍照提到了 Render 的第一行。
    var c = NewCore(true);
    ResetQuick();
    var liveRoot = ScreenId.Of(c);
    Check("新建出来的状态认出来就是根", liveRoot == root, ScreenId.Name(liveRoot) + " / GridCols=" + liveRoot.GridCols);
    c.Punct = true;
    c.Focus = FocusZone.CandGrid;
    var grid = ScreenId.Of(c);
    Check("9 宫格 -> 5x5 网格: 算换了一屏", ScreenId.WorthFx(root, grid), ScreenId.Name(grid));
    Check("  方向是往里走(+1)", ScreenId.DirTo(root, grid) == 1, ScreenId.DirTo(root, grid).ToString());
    Check("5x5 网格 -> 退回来: 方向是 -1", ScreenId.DirTo(grid, root) == -1, ScreenId.DirTo(grid, root).ToString());

    // 2) 网格里 LB / RB 换分类 —— 就是用户问的那句"5x5 的左右切页也可以吧"。
    int cat0 = c.PunctCat;
    c.PunctCat = (cat0 + 1) % c.PunctCatCount;
    var catGrid = ScreenId.Of(c);
    Check("LB/RB 换分类: 也算换了一屏", ScreenId.WorthFx(grid, catGrid), ScreenId.Diff(grid, catGrid));
    // 【2.1.16】方向得跟着"往第几个"走。以前 DirTo 只问"终点是不是根", 于是网格里
    // 往回翻一类也判成 +1 —— 而旋转那一套里 +1 演的是"转走 + 浮现"、-1 才是
    // "下沉 + 转回来", 判错了就成了按 LB 往回走也演"转出去", 方向感整个反掉。
    Check("  往下一类(RB): dir=+1", ScreenId.DirTo(grid, catGrid) == 1,
          ScreenId.DirTo(grid, catGrid).ToString());
    int back0 = (cat0 - 1 + c.PunctCatCount) % c.PunctCatCount;
    c.PunctCat = back0;
    var backGrid = ScreenId.Of(c);
    Check("  往上一类(LB): dir=-1", ScreenId.DirTo(grid, backGrid) == -1,
          ScreenId.DirTo(grid, backGrid).ToString());
    // 循环翻页: 最后一类 -> 第 0 类算往前, 第 0 类 -> 最后一类算往回
    int last = c.PunctCatCount - 1;
    c.PunctCat = last; var atLast = ScreenId.Of(c);
    c.PunctCat = 0;    var atZero = ScreenId.Of(c);
    Check("  最后一类 -> 第 0 类: 算往前(+1)", ScreenId.DirTo(atLast, atZero) == 1,
          ScreenId.DirTo(atLast, atZero).ToString());
    Check("  第 0 类 -> 最后一类: 算往回(-1)", ScreenId.DirTo(atZero, atLast) == -1,
          ScreenId.DirTo(atZero, atLast).ToString());
    // 进 / 出网格本身仍然按"是不是根"判: 进去 +1、退出 -1
    Check("  进网格: dir=+1", ScreenId.DirTo(root, grid) == 1);
    Check("  出网格回九键: dir=-1", ScreenId.DirTo(grid, root) == -1);
    c.PunctCat = cat0;

    // 2c) 【2.1.16】进符号网格那一瞬只许认出**一副**骨架。
    //     列数以前读的是 c.GridColsNow(跟着 GridPinyin 走), 而 GridPinyin 比 Focus
    //     晚一帧翻过来 —— 于是"九宫格标点 -> 符号网格"会连着认出两副骨架, 同一趟
    //     连演两场(日志里同一个网格名连着出现两次), 看着就是闪两下。
    {
        var c2 = NewCore(true);
        ResetQuick();
        c2.Punct = true;
        c2.Focus = FocusZone.CandGrid;
        var g1 = ScreenId.Of(c2);
        var g2 = ScreenId.Of(c2);
        Check("进符号网格: 只认出一副骨架(GridPinyin 滞后不再算换屏)", g1 == g2,
              ScreenId.Diff(g1, g2));
        Check("  这一副的列数就是符号网格那 5 列", g1.GridCols == 5, g1.GridCols.ToString());
        // 拼音网格(3 列)换到符号网格(5 列)仍然算换了一屏
        var py2 = ScreenId.Of(NewCore(true));
        py2 = new ScreenFace(py2.Layout, py2.Punct9, py2.Slide, py2.Swap15,
                             true, false, 0, 3, py2.Quick);
        Check("拼音网格 -> 符号网格: 换了一屏", ScreenId.WorthFx(py2, g1), ScreenId.Diff(py2, g1));
    }

    // 3) 【铁律】内容变了不算换屏 —— 破了就是"推一格摇杆转一次圈"
    var same = ScreenId.Of(c);
    c.GridIdx = 7;
    Check("网格里挪光标: 不重演一场", ScreenId.Of(c) == same, ScreenId.Diff(same, ScreenId.Of(c)));
    c.GridIdx = 0;

    // 4) 唯一的例外: 九键 <-> 全键盘是"左右扩张"的专场, 不许再摞一层转 / 浮
    var k26 = new ScreenFace(Layout.EN26, false, false, false, false, false, 0, 5, Quick: false);
    Check("九键 -> 全键盘: 不演(左右扩张专场)", !ScreenId.WorthFx(root, k26), ScreenId.Diff(root, k26));
    Check("全键盘 -> 九键: 一样不演", !ScreenId.WorthFx(k26, root));
    // 数字层不是全键盘 —— 那是换了一副面孔, 用户点名要"和标点一样处理"
    var numFace = new ScreenFace(Layout.NUM9, false, false, false, false, false, 0, 5, Quick: false);
    Check("九键 -> 数字层: 照演(它不是全键盘)", ScreenId.WorthFx(root, numFace), ScreenId.Diff(root, numFace));
    Check("  数字层 -> 九键: dir=-1 退回根", ScreenId.DirTo(numFace, root) == -1);

    // 5) 按住 LB 进滑行 / 松开回字母: 正反两面自动成对, 不用哪儿记一笔状态
    var slide = new ScreenFace(Layout.CN9, false, true, true, false, false, 0, 5, Quick: false);
    Check("进滑行: dir=+1", ScreenId.WorthFx(root, slide) && ScreenId.DirTo(root, slide) == 1, ScreenId.Name(slide));
    Check("松开回字母: dir=-1", ScreenId.DirTo(slide, root) == -1);

    // 6) 九宫格标点面 / JKL 换位
    var p9 = new ScreenFace(Layout.CN9, true, false, false, false, false, 0, 5, Quick: false);
    Check("进九宫格标点: dir=+1", ScreenId.WorthFx(root, p9) && ScreenId.DirTo(root, p9) == 1, ScreenId.Name(p9));
    var swap = new ScreenFace(Layout.CN9, false, false, true, false, false, 0, 5, Quick: false);
    Check("JKL 换位: 也算换了一屏", ScreenId.WorthFx(root, swap), ScreenId.Name(swap));

    // 7) 拼音网格 3 列 <-> 符号网格 5 列: 排布换了就是换了
    var py = new ScreenFace(Layout.CN9, false, false, false, true, false, 0, 3, Quick: false);
    var pn = new ScreenFace(Layout.CN9, false, false, false, true, true, 0, 5, Quick: false);
    Check("拼音网格 -> 符号网格: 换了一屏", ScreenId.WorthFx(py, pn), ScreenId.Diff(py, pn));

    // 8) GridIdx 这类每帧都在动的东西不在身份里(破了就成连发式转圈)
    Check("同一副骨架两次认出来一样", ScreenId.Of(c) == ScreenId.Of(c));

    // 9) 【2.1.15】窗口F: 开 / 关这一摞也算换了一屏。
    //    以前管线里写着"_qkOn 就跳过", 进出 F 于是成了全界面唯一一处换页没有动画的
    //    地方; 现在把它交给同一条管线, 开 / 关自动就有过渡, 不用在别处再抄一遍。
    {
        ResetQuick();
        var cf = NewCore(true);
        var tf = T0;
        var now0 = ScreenId.Of(cf);
        Check("没开 F 时: 还是字母九键那一面", now0 == root, ScreenId.Name(now0));

        OpenF(cf, ref tf);
        var fOpen = ScreenId.Of(cf);
        Check("开 F: 算换了一屏(台上摆的换了另一副东西)",
              ScreenId.WorthFx(root, fOpen), ScreenId.Diff(root, fOpen));
        Check("  开 F 是往里走(+1)", ScreenId.DirTo(root, fOpen) == 1, ScreenId.DirTo(root, fOpen).ToString());
        Check("  F 开着时不算根", !ScreenId.IsRoot(fOpen), ScreenId.Name(fOpen));

        // F 内部翻第几页 / 光标在第几格: 归那一摞自己的动画, 这条管线不许再摞一场
        UI(cf).Page = 2;
        UI(cf).Sel = 7;
        Check("F 里翻页挪光标: 不重演一场", ScreenId.Of(cf) == fOpen,
              ScreenId.Diff(fOpen, ScreenId.Of(cf)));

        Tap(cf, ref tf, GamepadInput.B);
        var fShut = ScreenId.Of(cf);
        Check("B 关掉 F: 回到根", !cf.QuickOpen && fShut == root,
              $"QuickOpen={cf.QuickOpen} / {ScreenId.Name(fShut)}");
        Check("  关 F 是退回来(-1)", ScreenId.DirTo(fOpen, fShut) == -1, ScreenId.DirTo(fOpen, fShut).ToString());

        // 录快捷键那一页自带"左右扩张", 不许再摞一层(判据只看两边是不是全键盘,
        // 于是"F 里进录键页" / "录完退回 F" 两趟都自动落下 —— 和第 4 小节同一条)
        var rec = new ScreenFace(Layout.EN26, false, false, false, false, false, 0, 5, Quick: true);
        Check("F 里进录快捷键页: 不演(扩张专场)", !ScreenId.WorthFx(fOpen, rec),
              ScreenId.Diff(fOpen, rec));
        Check("  录完退回 F: 一样不演", !ScreenId.WorthFx(rec, fOpen), ScreenId.Diff(rec, fOpen));
    }
}


// ==================================================================
Console.WriteLine("\n=== 34) 2.1.17: 符号网格里动右摇杆, 不许自己演换页 ===");
// 用户原话: "进标点页面动右摇杆会有切换页面的动画，但是页面没有切换"。
// 根因(这根探针当初就是为复现它临时搭出来的): 展开网格开着的时候, 标点轮盘那一支
// **照样每帧在跑** —— 手推着走、半径掉到 PUNCT9_MIN_R(0.35)以下就把标点九键层关
// 一回, 推出来再开, 一趟能开关十几回。而那一面在网格开着时压根不露脸, 屏幕上什么
// 都没变, 换屏管线却每记一笔就演一场。
{
    var c = NewCore(true);
    ResetQuick();
    var t = T0;

    OpenPunctHere(c, ref t);
    Check("唤出了全部符号网格", c.Punct && c.Focus == FocusZone.CandGrid,
          $"Punct={c.Punct}, Focus={c.Focus}");

    // 绕满两整圈, 半径一路起伏: 真实手推一定会反复穿过轮盘那条回中门槛
    var grid = ScreenId.Of(c);
    int changes = 0;
    string diff = "";
    for (int deg = 0; deg < 720; deg += 10)
    {
        double a = deg * Math.PI / 180.0;
        double r = 0.55 + 0.35 * Math.Sin(deg * Math.PI / 180.0 * 1.7);
        t = t.AddMilliseconds(16);
        c.Poll(new PadState { Connected = true, Buttons = 0,
                              RX = (short)(Math.Cos(a) * r * 32767),
                              RY = (short)(Math.Sin(a) * r * 32767) }, t);
        var now = ScreenId.Of(c);
        if (now != grid) { changes++; diff = ScreenId.Diff(grid, now); grid = now; }
    }
    Check("网格里晃两整圈: 身份一次都不能变", changes == 0,
          $"变了 {changes} 次, 最后一次的差异=({diff})");

    // 只推拉不换向: 正对着那条回中门槛来回穿
    changes = 0; diff = "";
    for (int k = 0; k < 60; k++)
    {
        double r = Math.Abs(0.95 - k * 0.03);
        t = t.AddMilliseconds(16);
        c.Poll(new PadState { Connected = true, Buttons = 0,
                              RX = (short)(0.7 * r * 32767),
                              RY = (short)(-0.7 * r * 32767) }, t);
        var now = ScreenId.Of(c);
        if (now != grid) { changes++; diff = ScreenId.Diff(grid, now); grid = now; }
    }
    Check("一路推着穿过回中门槛: 还是不能变", changes == 0,
          $"变了 {changes} 次, 最后一次的差异=({diff})");

    // 关掉网格之前的常规操作(A 选一个符号)不该被这段时间里攒下的账影响
    Tap(c, ref t, GamepadInput.A);
    t = t.AddMilliseconds(20);
    c.Poll(new PadState { Connected = true }, t);
}

// 夹带的那件真事: 从全键盘开的符号网格, 标点层是"临时翻回九键"的(Punct9On 记下了
// _beforePunct)。轮盘那支每关一次就要把布局还原回全键盘 —— 那一次可是**真翻页**。
{
    var c = NewCore(true);
    ResetQuick();
    c.Layout = Layout.EN26;
    var t = T0;

    OpenPunctHere(c, ref t);
    Check("从全键盘也进得去符号网格", c.Punct && c.Focus == FocusZone.CandGrid,
          $"Punct={c.Punct}, Focus={c.Focus}, Layout={c.Layout}");

    // 逐帧盯着: 轮盘那支每关一次就会把布局还原回全键盘 —— 那一瞬是**真翻页**
    int slips = 0;
    for (int k = 0; k < 40; k++)
    {
        double r = k < 20 ? 0.9 - k * 0.04 : 0.1 + (k - 20) * 0.04;
        t = t.AddMilliseconds(16);
        c.Poll(new PadState { Connected = true, Buttons = 0,
                              RX = (short)(0.7 * r * 32767),
                              RY = (short)(-0.7 * r * 32767) }, t);
        if (c.Layout != Layout.CN9) slips++;
    }
    Check("这一路上布局一次都没被拽回全键盘", slips == 0, $"被拽回 {slips} 帧");
}

}
finally
{
    try { Directory.Delete(qdir, true); } catch { }
    Console.WriteLine("\n(临时目录已删除, 真实的快捷文件没动过)");
}

Console.WriteLine(fails == 0 ? "\n全部通过" : $"\n!! {fails} 条没过");
return fails == 0 ? 0 : 1;
