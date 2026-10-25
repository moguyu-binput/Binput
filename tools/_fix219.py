# -*- coding: utf-8 -*-
# 2.1.9 三处的源码补丁:
#   ① 设置面盖在上面时, 手柄归设置 —— 从窗口F 进设置按 B 不再把背后的 F 退掉
#   ② 框外那对 ◀LB / RB▶ 换成中性色(不蓝) + 按下翻页时跳一下转一下
#   ③ 深色小板上那几个字("滑行" / "已复制" / ◀▶)统一成恒浅色, 修可爱粉下看不见
#
# 换行纪律: .cs 一律 CRLF, OverlayWindow.xaml 是 LF。
# 这里所有文本块都先按 "\n" 写, 再统一转成目标文件的换行 —— 免得写出 \r\r\n。
import io, os, sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "skin-lab", "GamepadT9-Game")

def C(s):   # 这块是 CRLF 文件的: 转成 \r\n
    return s.replace("\n", "\r\n")
def L(s):   # 这块是 LF 文件的: 原样
    return s

def patch(path, pairs, crlf_file):
    p = os.path.join(SRC, path)
    s = io.open(p, encoding="utf-8", newline="").read()
    if crlf_file:
        assert s.count("\r\n") > 0 and s.count("\n") == s.count("\r\n"), "不是纯 CRLF: " + path
    else:
        assert "\r" not in s, "不是纯 LF: " + path
    for old, new, tag in pairs:
        o = C(old) if crlf_file else L(old)
        n = C(new) if crlf_file else L(new)
        if s.count(o) != 1:
            raise SystemExit("!! %s 锚点出现 %d 次(应为 1):\n%s" % (tag, s.count(o), o[:200]))
        s = s.replace(o, n, 1)
        print("  ok  %s" % tag)
    after = io.open(p, encoding="utf-8", newline="").read()
    if crlf_file:
        assert after.count("\n") == after.count("\r\n"), "写完不是纯 CRLF: " + path
        assert after.count("\r\r\n") == 0
    else:
        assert "\r" not in after
    io.open(p, "w", encoding="utf-8", newline="").write(s)
    print("已写 %s (%d 字节)" % (path, len(s.encode("utf-8"))))


# ==================================================================
# 一、InputCore.cs (CRLF)
# ==================================================================
IC = [

    # --- 1) 新增"翻页动效"的钩子 ---
    (
'''    /// <summary>设置页里 LB / RB 翻页(-1 / +1)。</summary>
    public Action<int> SettingsPage;
''',
'''    /// <summary>设置页里 LB / RB 翻页(-1 / +1)。</summary>
    public Action<int> SettingsPage;
    /// <summary>
    /// **按了 LB / RB**(参数 -1 / +1)。唯一用处是让框外面那对 ◀LB / RB▶ 跟着
    /// 跳一下、转一下(见 OverlayWindow.PageTurnFx)—— 按下去得有个回响,
    /// 不然翻没翻过去全靠点数。至于这一下翻的到底是谁的页(设置 / 窗口F / 符号
    /// 分哪一类), 是 OnShoulder 下面的事, 跟这儿无关。
    /// </summary>
    public Action<int> PageFx;
''',
     "InputCore: 加 PageFx 钩子"),

    # --- 2) QuickMode 扣掉"设置盖在上面"这一档 ---
    (
'''    /// <summary>F 占着屏幕 —— 这时候九键 / 候选那些都不该再响应。</summary>
    public bool QuickMode => QuickOpen;
''',
'''    /// <summary>
    /// F 占着屏幕 —— 这时候九键 / 候选那些都不该再响应。
    /// <para>【2.1.9: 翻到设置面时 F 得让位】F 开着照样能按 R3 翻到设置, 那时候
    /// 屏幕上摆的是**设置的九宫格**, 手柄该伺候设置。以前这一句只看 QuickOpen,
    /// 于是"从窗口F 进设置、按一下 B"退的是**背后那一摞 F**(F 整个关掉),
    /// A / 十字键 / LB RB 也一股脑跑到 F 上去了 —— 眼睛和手对不上。</para>
    /// <para>想说"F 开着(不管有没有被设置盖住)"的地方请直接用
    /// <see cref="QuickOpen"/> —— 典型是界面收起时把 F 一并关掉那两处。</para>
    /// </summary>
    public bool QuickMode => QuickOpen && !Settings;
''',
     "InputCore: QuickMode 排除设置面"),

    # --- 3) 收起界面时照旧把 F 关掉(这里要的是 QuickOpen, 不是 QuickMode) ---
    (
'''                Punct9Off();
                if (QuickMode) QuickClose();
            }
''',
'''                Punct9Off();
                if (QuickOpen) QuickClose();
            }
''',
     "InputCore: 收起(组合帧)关 F 用 QuickOpen"),

    (
'''                Punct9Off();
            if (QuickMode) QuickClose();
            _prevBtn = btn; _prevLT = lt; _prevRT = rt; _prevL3 = l3; _prevR3 = r3;
''',
'''                Punct9Off();
            if (QuickOpen) QuickClose();
            _prevBtn = btn; _prevLT = lt; _prevRT = rt; _prevL3 = l3; _prevR3 = r3;
''',
     "InputCore: 收起(!Visible)关 F 用 QuickOpen"),

    # --- 4) LT 切数字层: 设置盖着时也不切 ---
    (
'''        else if (lt && !_prevLT)
        {
            if (!QuickMode)
            {
''',
'''        else if (lt && !_prevLT)
        {
            if (!QuickOpen)
            {
''',
     "InputCore: LT 数字层判据用 QuickOpen"),

    # --- 5) A 归设置 / TickQuickHold 仍按 QuickOpen 判 ---
    (
'''        if (QuickMode) HoldQuickA(btn, now);
        else HoldRepeatA(btn, now);
        if (!QuickMode) TickQuickHold(btn, now);
''',
'''        // 【2.1.9: 设置盖在上面 -> A 归设置】QuickMode 已经扣掉了设置面,
        // 于是这一条自动落到 HoldRepeatA, 由它里面那个 Settings 分支接住
        // (A = 确认光标这一项)。以前走的是 HoldQuickA, 按 A 动的是背后的 F。
        if (QuickMode) HoldQuickA(btn, now);
        else HoldRepeatA(btn, now);
        // 这一条要的是"F 开着就别跑", 跟盖没盖住无关 —— 用 QuickOpen。
        if (!QuickOpen) TickQuickHold(btn, now);
''',
     "InputCore: A 与 TickQuickHold 的判据"),

    # --- 6) L3: 设置盖着时不解除 F 的绑定 ---
    (
'''            if (QuickOpen && QEdit < 0)
            {
                QuickUI?.L3();
''',
'''            if (QuickMode && QEdit < 0)
            {
                QuickUI?.L3();
''',
     "InputCore: L3 用 QuickMode"),

    # --- 7) 左摇杆: 设置盖着时不挪 F 的光标(前面 Settings 已 return, 这里同步口径) ---
    (
'''        if (QuickOpen && QEdit < 0)
        {
            QuickUI?.Stick(rawX / 32768.0, rawY / 32768.0);
''',
'''        if (QuickMode && QEdit < 0)
        {
            QuickUI?.Stick(rawX / 32768.0, rawY / 32768.0);
''',
     "InputCore: 摇杆用 QuickMode"),

    # --- 8) 十字键: 同上 ---
    (
'''        if (QuickOpen && QEdit < 0)
        {
            int qd = dir == DIR_UP ? -1 : dir == DIR_DOWN ? 1 : 0;
''',
'''        if (QuickMode && QEdit < 0)
        {
            int qd = dir == DIR_UP ? -1 : dir == DIR_DOWN ? 1 : 0;
''',
     "InputCore: 十字键用 QuickMode"),

    # --- 9) LB / RB: 设置排到窗口F 前面, 并触发翻页动效 ---
    (
'''    void OnShoulder(int delta)
    {
        // 窗口F: LB / RB 是翻页(一页九格, 五页装 45 条)。和设置页同一套。
        if (QuickOpen && QEdit < 0) { QuickUI?.Page(delta); return; }
        // 设置页: LB / RB 是翻页(九宫格一页九个, 放不下就翻)。和"更多标点"一个路子。
        if (Settings) { SettingsPage?.Invoke(delta); return; }
''',
'''    void OnShoulder(int delta)
    {
        // 框外面那对 ◀LB / RB▶ 跟着跳一下(见 OverlayWindow.PageTurnFx)。
        // 放在最前面: "这一下翻的是谁的页"是下面的事, 而那对小牌该不该亮
        // (PageTurnable)跟归谁无关 —— 真没翻动时它本来就是收着的, 跳也看不见。
        PageFx?.Invoke(delta);

        // 【2.1.9: 设置盖在上面时, 翻的是设置那一摞】
        // 以前窗口F 那一条排在前面, 于是"从 F 进设置、按 LB"翻的是**背后那一摞 F**。
        // 设置在上就先伺候设置 —— 这和 B 那条是同一条规矩的两半:
        // 在哪儿进设置, 就从哪儿退回去, 中间的 F 不许被顺手动。
        // 设置页: LB / RB 是翻页(九宫格一页九个, 放不下就翻)。和"更多标点"一个路子。
        if (Settings) { SettingsPage?.Invoke(delta); return; }
        // 窗口F: LB / RB 是翻页(一页九格, 五页装 45 条)。和设置页同一套。
        if (QuickMode && QEdit < 0) { QuickUI?.Page(delta); return; }
''',
     "InputCore: OnShoulder 设置优先 + 翻页动效"),

    # --- 10) B: 设置那一支挪到窗口F 前面 ---
    (
'''        if (QEdit >= 0) { QuickEndHotkey(false); return; }
        if (QuickOpen)
        {
            if (QuickUI?.Back(false) != true) QuickClose();
            else Dirty = true;
            return;
        }

        // 设置页: B 是"退一层"(长按一路退回首页, 见 TickLongB)。
        // 已经在首页了就翻回正面 —— 和 R3 短按一样, 多一条退路, 不至于卡在设置里。
        // **不在第二层以下时, "按下去"这一下先不动**: 要留位置给长按,
        // 等松手(短按, 退一层)或按够 420ms(长按, 直接回首页)再决定。
        // 只有在首页时B 才是立刻翻回正面 —— 那儿没有"再退一层"这一说。
        if (Settings)
        {
            if ((SettingsDepth?.Invoke() ?? 0) > 1) { _bDeferSet = true; return; }
            if (SettingsBack?.Invoke(false) == true) { Dirty = true; return; }
            ToggleSettings?.Invoke();
            return;
        }
''',
'''        if (QEdit >= 0) { QuickEndHotkey(false); return; }

        // 设置页: B 是"退一层"(长按一路退回首页, 见 TickLongB)。
        // 已经在首页了就翻回正面 —— 和 R3 短按一样, 多一条退路, 不至于卡在设置里。
        // **不在第二层以下时, "按下去"这一下先不动**: 要留位置给长按,
        // 等松手(短按, 退一层)或按够 420ms(长按, 直接回首页)再决定。
        // 只有在首页时B 才是立刻翻回正面 —— 那儿没有"再退一层"这一说。
        //
        // 【2.1.9: 这一支必须排在窗口F **前面**】
        // 以前 F 在前面, 于是"先开窗口F、再翻进设置、按一下 B"退的是背后那一摞 F
        // (按两次就把 F 整个关了), 而屏幕上明明摆的是设置的九宫格 —— 手和眼对不上。
        // 规矩是: **眼睛看着哪一面, B 就退哪一面**; 退到设置的首页再按一下才翻回
        // 正面, 翻回去时 F 还停在进设置之前那一层(列表 / 深层 / 绑数字), 一次也不动。
        if (Settings)
        {
            if ((SettingsDepth?.Invoke() ?? 0) > 1) { _bDeferSet = true; return; }
            if (SettingsBack?.Invoke(false) == true) { Dirty = true; return; }
            ToggleSettings?.Invoke();
            return;
        }
        if (QuickOpen)
        {
            if (QuickUI?.Back(false) != true) QuickClose();
            else Dirty = true;
            return;
        }
''',
     "InputCore: OnB 设置优先于窗口F"),

    # --- 11) 状态行(现在是死代码, 口径同步一下) ---
    (
'''        if (QuickOpen)
        {
            string layer = QuickUI?.Layer ?? Q_LAYER_LIST;
''',
'''        if (QuickMode)
        {
            string layer = QuickUI?.Layer ?? Q_LAYER_LIST;
''',
     "InputCore: 状态行用 QuickMode"),
]

# ==================================================================
# 二、App.xaml.cs (CRLF)
# ==================================================================
APP = [
    (
'''        _core.SettingsPage = d => _win.SettingsPage(d);
''',
'''        _core.SettingsPage = d => _win.SettingsPage(d);
        // 按了 LB / RB -> 框外面那对小牌跟着跳一下(见 OverlayWindow.PageTurnFx)
        _core.PageFx = d => _win.PageTurnFx(d);
''',
     "App: 接上 PageFx"),
]

# ==================================================================
# 三、OverlayWindow.xaml.cs (CRLF)
# ==================================================================
OW = [

    # --- 1) 深色小板上那两支笔刷 ---
    (
'''    static readonly SolidColorBrush Pinyin = new(Color.FromRgb(0x9F, 0xC7, 0xF5));
    static readonly SolidColorBrush PinyinDim = new(Color.FromRgb(0x26, 0x22, 0x30));
''',
'''    static readonly SolidColorBrush Pinyin = new(Color.FromRgb(0x9F, 0xC7, 0xF5));
    static readonly SolidColorBrush PinyinDim = new(Color.FromRgb(0x26, 0x22, 0x30));
    /// <summary>
    /// **压在深色小板上的字色** —— 框外面那对 ◀LB / RB▶、卡片下面那块
    /// "滑行" / "已复制"。
    /// <para>这几块板的底色是**写死**的深色(见 XAML 里的 #C20E0C16): 它们浮在游戏
    /// 画面上, 板子本身必须深, 碰到雪地 / 天空这种亮背景才看得见。</para>
    /// <para>于是板上的字就必须**恒为浅色, 不跟主题走**。以前 ◀▶ 用的是 Pinyin
    /// (默认淡蓝、可爱粉下变成深蓝), "滑行"用的是 Fg(可爱粉下是深棕)——
    /// 两套一到可爱粉就成了"深字压深板", 一个字也看不见。而且那抹蓝跟哪儿都不搭,
    /// 用户原话"看着突兀"。现在统一成这一支: 柔和的白。</para>
    /// </summary>
    static readonly SolidColorBrush ChipInk = new(Color.FromRgb(0xEE, 0xEE, 0xF4));
    /// <summary>板上**次要**的字(LB / RB 那两个字母), 比 ChipInk 淡一档。</summary>
    static readonly SolidColorBrush ChipSub = new(Color.FromRgb(0xA8, 0xA8, 0xBA));
''',
     "OW.cs: 加 ChipInk / ChipSub"),

    # --- 2) 建界面时把那几个字换成新笔刷 ---
    (
'''        // "滑行"二字: 它在 XAML 里写的是浅色, 换到可爱粉(浅底深字)那套就成了
        // 白字压粉底 —— 几乎看不见, 所以也换成 Fg(换主题会跟着变深)。
        SlideHintText.Foreground = Fg;
        GridArrowLMark.Foreground = Pinyin;
        GridArrowRMark.Foreground = Pinyin;
        GridArrowLLab.Foreground = Sub;
        GridArrowRLab.Foreground = Sub;
''',
'''        // 【2.1.9: 深色小板上的字一律用 ChipInk / ChipSub】
        // 这几块板(◀LB / RB▶、"滑行"、"已复制")的底色是写死的深色,
        // 而 Fg / Pinyin / Sub 换主题时会翻成深色 —— 深字压深板等于没有。
        // 2.1.8 把"滑行"改成 Fg 就是踩了这个坑(可爱粉下 Fg 是深棕, 二字整个糊掉);
        // ◀▶ 用的 Pinyin 在可爱粉下是深蓝, 一样糊, 而且默认那套还带着一抹蓝。
        // 详见 ChipInk 那条注释。
        SlideHintText.Foreground = ChipInk;
        CopyHintText.Foreground = ChipInk;
        GridArrowLMark.Foreground = ChipInk;
        GridArrowRMark.Foreground = ChipInk;
        GridArrowLLab.Foreground = ChipSub;
        GridArrowRLab.Foreground = ChipSub;
''',
     "OW.cs: 小板上的字换笔刷"),

    # --- 3) 翻页那对小牌的跳动 ---
    (
'''        ro.BeginAnimation(RotateTransform.AngleProperty, rk);
    }

    /// <summary>光标走了: 缩回原样、影子撤掉、Z 序落回来。</summary>
    void CellRest(Border b)
''',
'''        ro.BeginAnimation(RotateTransform.AngleProperty, rk);
    }

    /// <summary>
    /// 框外面那对小牌(◀LB / RB▶)自己的变换组: 缩放 + 旋转 + 平移, 一次备齐。
    /// 和 <see cref="CellTf"/> 一个写法(没有就现建一个), 只是多挂一支平移 ——
    /// 翻页那一下要"往外顶出去"一点, 光放大看着像原地胖了一下。
    /// </summary>
    static (ScaleTransform, RotateTransform, TranslateTransform) SideTf(Border b)
    {
        if (b.RenderTransform is TransformGroup tg && tg.Children.Count == 3
            && tg.Children[0] is ScaleTransform s && tg.Children[1] is RotateTransform r
            && tg.Children[2] is TranslateTransform t)
            return (s, r, t);
        var g = new TransformGroup();
        s = new ScaleTransform(1, 1);
        r = new RotateTransform(0);
        t = new TranslateTransform(0, 0);
        g.Children.Add(s); g.Children.Add(r); g.Children.Add(t);
        b.RenderTransform = g;
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        return (s, r, t);
    }

    /// <summary>
    /// 按了 LB / RB(由 InputCore.OnShoulder 通过 PageFx 叫过来): 对应的那块小牌
    /// **弹一下、转一下、往外顶一下** —— 和九键按下去那颗键是同一套动作
    /// (见 <see cref="CellPop"/>), 只是小牌不"停"在放大状态, 一晃就回原样。
    /// <para>往左翻顶左边、往右翻顶右边: 那一顶就是"往这边推"的意思,
    /// 不用再去看箭头指哪。</para>
    /// </summary>
    public void PageTurnFx(int delta)
    {
        var plate = delta < 0 ? PagePlateL : PagePlateR;
        // 这一页根本不能翻的时候那对小牌是收着的(Collapsed), 跳也看不见 ——
        // 顺手省掉一次动画。
        if (plate == null || plate.Visibility != Visibility.Visible) return;

        var (sc, ro, tr) = SideTf(plate);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        int side = delta < 0 ? -1 : 1;

        // 放大: 冲过头再回落。1.20 是"看得出在动"又不至于把牌甩出舞台的量,
        // 和九键那颗键(1.14)比略大一号 —— 小牌本来就小, 不放大点看不出来。
        for (int i = 0; i < 2; i++)
        {
            var sk = new DoubleAnimationUsingKeyFrames();
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(1.20,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)), ease));
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(0.94,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(215))));
            sc.BeginAnimation(i == 0 ? ScaleTransform.ScaleXProperty
                                     : ScaleTransform.ScaleYProperty, sk);
        }

        // 转: 往翻的那一侧甩一下, 回摆两次停住(和 CellPop 那一晃同一条曲线)
        double wob = 14 * side;
        var rk = new DoubleAnimationUsingKeyFrames();
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)), ease));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob * -0.60,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob * 0.28,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(215))));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(290))));
        ro.BeginAnimation(RotateTransform.AngleProperty, rk);

        // 顶: 往翻的那一侧挪 7 像素再回来
        double push = 7 * side;
        var tk = new DoubleAnimationUsingKeyFrames();
        tk.KeyFrames.Add(new EasingDoubleKeyFrame(push,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)), ease));
        tk.KeyFrames.Add(new EasingDoubleKeyFrame(0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(215))));
        tr.BeginAnimation(TranslateTransform.XProperty, tk);
    }

    /// <summary>光标走了: 缩回原样、影子撤掉、Z 序落回来。</summary>
    void CellRest(Border b)
''',
     "OW.cs: 加 SideTf / PageTurnFx"),
]

# ==================================================================
# 四、OverlayWindow.xaml (LF)
# ==================================================================
XAML = [
    (
'''                <TextBlock Text="已复制" IsHitTestVisible="False"
''',
'''                <TextBlock x:Name="CopyHintText" Text="已复制" IsHitTestVisible="False"
''',
     "XAML: 给已复制那俩字起名字"),
]


print("== InputCore.cs ==")
patch("InputCore.cs", IC, True)
print("== App.xaml.cs ==")
patch("App.xaml.cs", APP, True)
print("== OverlayWindow.xaml.cs ==")
patch("OverlayWindow.xaml.cs", OW, True)
print("== OverlayWindow.xaml ==")
patch("OverlayWindow.xaml", XAML, False)
print("\n全部补丁已应用")
