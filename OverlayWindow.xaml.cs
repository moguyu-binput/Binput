using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Binput;

public partial class OverlayWindow : Window, IQuickUI
{
    public OverlayWindow()
    {
        InitializeComponent();

        // 舞台尺寸: 窗口**一次定死**, 不再跟着内容变(SizeToContent 已撤)。
        // XAML 里那两个数是同一对常量抄过去的, 这里再赋一次 —— 免得以后改了常量
        // 忘了改 XAML, 两边对不上。
        Width = StageW; Height = StageH;

        // 发键闸门: 预览框还在往上升的时候先把键扣下, 升到位再补发(见 NoteGate)。
        // 这里是**唯一**装它的地方; 探针(tools/ 下那几个)不创建窗口, 于是恒为 null、
        // 照发不误 —— 它们本来也不该被界面动画拦住。
        TextOutput.Hold = NoteGate;

        BuildKey9();
        BuildKey26();
        BuildCandRow();
        BuildCandGrid();
        BuildStickLayer();
        BuildPinyinRow();
        BuildZeroKey();

        // 【2.1.8: 分类名那块牌子撤了】它原来在这儿领一份 Fg 笔刷。现在"第几类"
        // 写在符号网格正中间那一格上, 那个字走的是网格自己的 Paint(见 Render),
        // 换主题跟着笔刷走, 这儿不用再管。
        // 【2.1.9: 深色小板上的字一律用 ChipInk / ChipSub】
        // 这几块板(◀LB / RB▶、"滑行"、"已复制")的底色是写死的深色,
        // 而 Fg / Pinyin / Sub 换主题时会翻成深色 —— 深字压深板等于没有。
        // 2.1.8 把"滑行"改成 Fg 就是踩了这个坑(可爱粉下 Fg 是深棕, 二字整个糊掉);
        // ◀▶ 用的 Pinyin 在可爱粉下是深蓝, 一样糊, 而且默认那套还带着一抹蓝。
        // 详见 ChipInk 那条注释。
        SlideHintText.Foreground = ChipInk;
        CopyHintText.Foreground = ChipInk;
        // 彩蛋那几句和上面两块同一块深色板(见 XAML 里的 EggChip), 一样得恒为浅色。
        EggHintText.Foreground = ChipInk;
        GridArrowLMark.Foreground = ChipInk;
        GridArrowRMark.Foreground = ChipInk;
        GridArrowLLab.Foreground = ChipSub;
        GridArrowRLab.Foreground = ChipSub;

        Loaded += (_, _) => PositionBottomCenter();
        SizeChanged += (_, _) => PositionBottomCenter();

        // 预览框的高度跟着字数走(1~5 行), 以及"有字才浮出来"。推到 Loaded 优先级再做:
        // LineCount 要等 TextBox 自己排完版才准, 在 TextChanged 当场读还是上一版的行数。
        NoteBox.TextChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            SizePreview();
            SyncNotePanel();
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        // 齿轮的高亮交给 HitTestTick(按指针位置判), 不用 MouseEnter ——
        // 界面在穿透/非穿透之间切来切去, 进出的通知不一定送得到。

        Loaded += (_, _) => BuildSettingsSide();

        // 界面被隐藏(收起 / 预输入版里点到别的窗口自动收起)时要翻回正面:
        // 不然下次 L3+R3 唤出来直接就是设置那一面, 键盘反而不见了。
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            if (Flipped) Unflip();
            // 还在"停在屏幕外"的那一两帧里就被收起了: 把状态清干净,
            // 免得下一轮唤出接着用上一轮的旧账(顺便把透明度还回来)
            _holdGen++;
            _holdOff = false;
            HoldFade(1);            // 同上: 还内容, 不动窗口透明度
            OnShown = null;          // 还没露脸就被收起了, 抢前台那件事也一并作罢
        };

        // 角落按钮(正面齿轮 / 背面返回箭头): 事件挂在**窗口**上, 命中判定自己做。
        // 挂在按钮上会丢 MouseUp(界面默认鼠标穿透 + 翻面时整块换内容),
        // 一丢按下状态就永远挂着, 表现就是"鼠标一碰齿轮界面就跟着跑"。
        PreviewMouseDown += OnWinDown;
        PreviewMouseMove += OnWinMove;
        PreviewMouseUp += OnWinUp;

        // 鼠标点到别的窗口 -> 通知外面: 预输入版下要把"逻辑上也算收起"记下来,
        // 否则界面虽然被盖住了, Visible 还是 true, 再按 L3+R3 会变成"收起"而不是"唤出"。
        Deactivated += (_, _) => LostActivation?.Invoke();
    }

    /// <summary>窗口被别的窗口抢走激活(典型就是鼠标点了游戏)时触发。</summary>
    public event Action LostActivation;

    /// <summary>缩放整个界面(整体等比)。手柄上按住 R3 + 左摇杆上下调, 也可以走设置窗口。</summary>
    public double Scale { get; private set; } = 1.0;

    /// <summary>
    /// 预输入版: 界面变成一个能吃焦点的前台窗口, 上面多一个记事本预览框。
    /// 非预输入版必须保持 NOACTIVATE + TRANSPARENT —— 那样它才不会抢走游戏的输入焦点。
    /// </summary>
    public bool Preview
    {
        get => _preview;
        set
        {
            if (_preview == value) return;
            _preview = value;
            Focusable = value;
            // 预输入版必须撤掉 Topmost: 系统输入法的候选窗本身是顶层窗,
            // 我们一直压在最上面的话候选就被自己的界面盖住了, 等于看不见候选。
            // 撤掉之后靠"被激活"来待在游戏前面, 候选窗反而能浮在我们上面。
            Topmost = !value;
            // 预览框不再"预输入版就一直挂着": **有字才出现**(见 SyncNotePanel)。
            // 这里只管"允许不允许它出现", 出不出得去由有没有字说了算。
            SyncNotePanel();
            if (value) SizePreview();
            ApplyExStyle();
        }
    }
    bool _preview;

    /// <summary>记事本预览框里的内容。</summary>
    public string NoteText
    {
        get => NoteBox.Text;
        set
        {
            NoteBox.Text = value ?? "";
            NoteBox.CaretIndex = NoteBox.Text.Length;
            SizePreview();
            SyncNotePanel();
        }
    }

    // ---- 预览框的尺寸: 1~5 行 ----
    /// <summary>预览框一行的高度(17px 的字 + 行距)。</summary>
    const double NOTE_LINE_H = 24;
    /// <summary>预览框上下各留的白(和 XAML 里 NoteBox 的 Padding 对齐)。</summary>
    const double NOTE_PAD_V = 8;
    /// <summary>预览框最多 5 行。再多就往上滚 —— 第一行滚出框外就看不见了。</summary>
    const int NOTE_MAX_LINES = 5;

    /// <summary>
    /// 预览框的高度 = **刚好装下现在这几行字**(1 行 40、最多 5 行 136)。
    /// 这么算是为了"正在输入的那一行离拼音候选的距离恒定": 框不多不少,
    /// 光标那行就永远贴在框底 —— 字多了框往上长, 不是往下挤到候选上。
    /// 没字的时候就是 1 行(框自己缩回去), 不会白占一大块。
    /// </summary>
    void SizePreview()
    {
        if (!_preview) return;
        // LineCount 要等排完版才有数, 没排过的时候是 -1。
        int n = NoteBox.LineCount;
        if (n < 1) n = 1;
        if (n > NOTE_MAX_LINES) n = NOTE_MAX_LINES;
        double h = NOTE_LINE_H * n + NOTE_PAD_V * 2;
        if (Math.Abs(NoteBox.MinHeight - h) > 0.5)
        {
            NoteBox.MinHeight = h;
            NoteBox.MaxHeight = h;
        }
        // 已经在露着了(而且没在演出入动画): 框跟着行数走, 但**不再播动画** ——
        // 打字时每多一行就升一次, 那是在抽, 不是浮。
        if (_noteShown && !_noteAnim) RowNote.Height = new GridLength(h + 2);
    }

    /// <summary>预览框现在是不是"有字、露出来了"的状态。</summary>
    bool _noteShown;
    /// <summary>出入场动画正在演。演的过程中高度归动画管, SizePreview 不许抢。</summary>
    bool _noteAnim;

    /// <summary>
    /// 预览框**有字才出现**: 没字的时候它那一行收成 0 —— 九键上面不会再空出一块;
    /// 一打上字, 整块框**从九键方框的上边**往上滑到自己的位置(见 NoteTick)。
    ///
    /// 四个"为什么这么做":
    ///   · **为什么动行高而不是动 NotePanel.Height**: Height 不参与 Measure ——
    ///     把控件压成 0 高, 它在外层 Auto 行里照样报"我想要 42", 那一行还是空着 42。
    ///     行高才是真说了算的那个数(这条坑踩过: 以为藏起来了, 其实留了一条空白)。
    ///   · **为什么不用 Visibility=Collapsed**: 记事本要一直握着焦点(系统输入法
    ///     的拼音和候选全挂在有焦点的编辑框上), Collapsed 一挂焦点就丢了;
    ///     行高收成 0 时控件仍然"活着", 焦点照旧, 只是看不见也点不到。
    ///   · **为什么自己逐帧插值而不是 DoubleAnimation**: GridLength 不是
    ///     DoubleAnimation 能直接写的类型, 顺手也省掉"撒手 / 冻值"那套收尾。
    ///     逐帧那一段必须挂在 CompositionTarget.Rendering 上 —— 一开始用的是
    ///     DispatcherTimer(哪怕 Render 优先级), 会被手柄轮询挤得丢帧, 320ms 只剩
    ///     四五帧, 用户反馈"太快看不到"其实是**帧不够**, 一眼就没了。
    ///   · **为什么这回动位移不动行高**: 以前是行高从 0 长到 h。可这一摞是**贴底**
    ///     往上摞的, 行长高只会把整摞的顶边往上顶 —— 框自己是在**原地冒出来**,
    ///     看着就是"在目标位置上升一行"(用户原话)。现在反过来: 行高**一次给足**,
    ///     整块框从下面(九键方框的上边)滑上来, 那才是"从方框里升出来"。
    /// </summary>
    /// <summary>
    /// 装在 TextOutput 上的那道闸门: 每次真要发键之前问它一句。
    ///
    /// 【要解决的毛病】按 RB 把拼音发出去, 系统输入法的候选条是跟着**光标**弹的。
    /// 而预览框这时候正从九键方框里往上升(头一回打字那 340 毫秒)—— 候选条按
    /// "弹出来那一瞬间光标在哪儿"定位, 于是它停在框**刚冒头**的地方; 等框升到位,
    /// 两者之间就空出一道。用户原话: "应该等待预览框上升完成后, 再输出拼音到预览框"。
    ///
    /// 所以要拦的不是"发不发", 而是"什么时候发":
    ///   框还收着 -> 先让它空着升起来, 这一批键扣下
    ///   正在升   -> 继续扣着
    ///   升到位了 -> 放(NOTE 的补发在 NoteTick 收尾那一行)
    /// </summary>
    bool NoteGate()
    {
        if (!_preview || !IsLoaded) return false;      // 直出版 / 窗口还没起来: 不管

        // 保险: 扣着超过 1 秒就一律放行(排着的也补发) —— 动画万一没演完
        // (比如界面这会儿根本没在画, Rendering 不触发), 不能把键一直压着,
        // 那会变成"按了没反应"。宁可早 300 毫秒发, 也不能把字吞了。
        if (_holdNoteAt != DateTime.MinValue &&
            (DateTime.Now - _holdNoteAt).TotalMilliseconds > 1000)
        {
            _holdNoteAt = DateTime.MinValue;
            TextOutput.Flush();
            return false;
        }

        // **顺序要紧**: _noteAnim 必须先判。框一"开始升" _noteShown 就已经是 true 了,
        // 先判它的话后面几个字母会被当成"框已经在位"直接放出去 —— 结果只有头一个
        // 字母被扣下, 剩下的照旧抢在动画前面发, 毛病一点没治
        // (这个坑是探针量出来的: 发 nihao 五个字母, 队里只压进 2 个 = 一个键)。
        bool hold;
        if (_noteAnim) hold = _noteOn;                 // 正在升 -> 扣着; 正在沉 -> 不管
        else if (_noteShown) hold = false;             // 框已经在位了, 照发不误
        else { SyncNotePanel(evenIfEmpty: true); hold = true; }   // 让空框先升起来

        _holdNoteAt = hold ? (_holdNoteAt == DateTime.MinValue ? DateTime.Now : _holdNoteAt)
                           : DateTime.MinValue;
        return hold;
    }

    /// <summary>闸门扣着键的起始时刻, 只用来做上面那道 1 秒的保险。</summary>
    DateTime _holdNoteAt;

    void SyncNotePanel(bool evenIfEmpty = false)
    {
        // evenIfEmpty: 框还收着、可马上就要往里写字了 —— 那就让它**空着先升起来**,
        // 等升到位再把字发进去(见 NoteGate)。平时不这么干: "有字才出现"是硬规矩。
        bool on = _preview && (NoteBox.Text.Length > 0 || evenIfEmpty);
        if (on == _noteShown) return;
        _noteShown = on;
        _noteAnim = true;
        NotePanel.IsHitTestVisible = on;

        double h = NoteBox.MinHeight + 2;
        double ms = on ? FxKit.MsNoteIn : FxKit.MsNoteOut;
        // 行程 = 它下面垫着的几行加起来多高(拼音行 + 候选行 + 空档 + 编辑条)。
        // 现场量, 别写死 —— 全键盘那一面、以及开着窗口F 编辑条时, 这一段不一样长。
        // 这一摞是贴底的, 那几行的下沿**正好就是九键方框的上边**。
        double d = NoteRiseDistance();

        // 行高**一次给足**: 出场时整块框直接就那么高, 只是人还在下面;
        // 收回时先保持, 等沉回方框里了才收成 0(见 NoteArrive 收尾)。
        RowNote.Height = new GridLength(h);
        NotePanel.RenderTransform = _noteTt;

        // 【这一场交给 DoubleAnimation, 不再自己逐帧插值】(2.1.26 阶段 2)
        // 以前是挂在 CompositionTarget.Rendering 上按 DateTime.Now 算进度 —— 那是当初
        // 为了躲 DispatcherTimer 丢帧才改的(定时器哪怕给了 Render 优先级, 也会被手柄
        // 那一轮一轮的轮询挤迟, 340ms 只剩四五帧)。躲是躲对了, 可代价是"进度"成了
        // 自己背着的一份状态: 没人能打断它、倒着放它, 收尾那一串(行高落定 / 撒手 /
        // 补发扣着的键)也只能挤在 tick 的最后一行里, 还得额外挂一道 1 秒的保险,
        // 因为界面不画的时候 Rendering 就不来、那一行永远等不到。
        // 现在两条动画各管一样, 缓动用 NoteEase 把原来那条掺出来的曲线原样复刻,
        // 收尾挂在**位移**那条动画的 Completed 上 —— 它跑满全程, 时钟到了就一定来。
        _noteOn = on;
        var span = TimeSpan.FromMilliseconds(ms);
        var ease = new NoteEase();

        // 位移: 出场 = 从九键方框的上边滑上来(d -> 0), 收回 = 沉回方框里(0 -> d)。
        _noteTt.BeginAnimation(TranslateTransform.YProperty, null);
        _noteTt.Y = on ? d : 0;
        var ya = new DoubleAnimation(on ? d : 0, on ? 0 : d, span) { EasingFunction = ease };
        ya.Completed += (_, _) => NoteArrive();
        _noteTt.BeginAnimation(TranslateTransform.YProperty, ya);

        // 淡入淡出: 出场**压后起**(头 28% 它正压在拼音行上, 那一段必须全透明,
        // 不然就盖住候选了); 收回在前 72% 里淡完, 最后一段它是透明的, 沉下去看不见。
        NotePanel.BeginAnimation(OpacityProperty, null);
        NotePanel.Opacity = on ? 0 : 1;
        var oa = on
            ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms * FxKit.NoteInSpan))
              { BeginTime = TimeSpan.FromMilliseconds(ms * FxKit.NoteInFrom) }
            : new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms * FxKit.NoteOutSpan));
        NotePanel.BeginAnimation(OpacityProperty, oa);
    }

    /// <summary>
    /// 预览框这一场**演完**的收尾(原来在 NoteTick 的最后一行)。
    /// 挂在位移那条动画的 Completed 上, 时钟到了就一定来 —— 不用再靠 1 秒的保险兜底。
    /// </summary>
    void NoteArrive()
    {
        if (!_noteAnim) return;
        _noteAnim = false;
        _noteTt.BeginAnimation(TranslateTransform.YProperty, null);
        NotePanel.BeginAnimation(OpacityProperty, null);
        SizePreview();      // 行数在动画期间可能又变了, 落到底时按最新的一次算
        RowNote.Height = new GridLength(_noteShown ? NoteBox.MinHeight + 2 : 0);
        NotePanel.Opacity = _noteOn ? 1 : 0;
        NotePanel.RenderTransform = null;
        _noteTt.Y = 0;      // 下一场从干净的零位起, 免得残留上一次的位移
        Settle();           // 这一行的高度刚定下来, 窗口/舞台跟着重摆一次

        // 框到位了 —— 扣着的那批键(拼音)现在才发出去。
        // 必须是**布局落定之后**: 系统输入法的候选条按"这一刻光标在哪儿"定位,
        // 早一步它就停在框还在半路上的位置, 和框错开一截(见 NoteGate)。
        _holdNoteAt = DateTime.MinValue;
        TextOutput.Flush();
    }

    /// <summary>
    /// 预览框要往上滑多远才到自己的位置 —— 它下面垫着的那几行加起来多高。
    /// (这一摞是贴底的, 那几行的下沿就是九键方框的上边, 所以这个数正好是
    /// "从方框上边升到位置"的行程。)
    /// </summary>
    double NoteRiseDistance() =>
        RowPad.ActualHeight + RowCand.ActualHeight + RowPinyin.ActualHeight;

    // ---- 预览框出入场 ----
    // 位移就这一个变换, 出场 / 收回都靠它(见 SyncNotePanel / NoteArrive)。
    // 进度、收尾全交给 DoubleAnimation, 这里不再背"演到第几成"那一份状态。
    TranslateTransform _noteTt = new TranslateTransform();
    bool _noteOn;


    /// <summary>改一行的高度(值没变就不动 —— 每次赋值都会触发一次重新排版)。</summary>
    public void TakeForeground()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        bool ok = Win32.ForceForeground(hwnd);
        App.Log($"预输入版 · 唤出: 顶前台 {(ok ? "成功" : "失败")} hwnd=0x{hwnd.ToInt64():X}");

        NoteBox.Focus();
        Keyboard.Focus(NoteBox);       // 光 Focus() 不够, WPF 的逻辑焦点也要给
        NoteBox.CaretIndex = NoteBox.Text.Length;
        Activate();
    }

    /// <summary>
    /// 往预览框里塞一段文字(窗口F 里"编辑短语"的第一趟: 把那条短语放回框里接着改)。
    /// 顺手把焦点给框 —— 放回去是为了让你接着用九键改它, 焦点不在框上字就飞到游戏里了。
    /// 直出版没有预览框, 那种情况下主程序根本不会调这个函数。
    /// </summary>
    public void QuickPutNote(string text)
    {
        if (!_preview) return;
        NoteText = text;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Win32.ForceForeground(hwnd);
        NoteBox.Focus();
        Keyboard.Focus(NoteBox);
    }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>
    /// 缩放整个界面(整体等比)。手柄上按住 R3 + 左摇杆上下调, 也可以走设置窗口。
    ///
    /// 【改完必须 Settle, 不能立刻去问坐标】这一次赋的 LayoutTransform 要到
    /// **下一轮布局**才生效; 而 CardRectInWindow 是拿"新的缩放"乘"旧的布局坐标"
    /// 算的(见那里), 两个不同时刻的数乘在一起, 结果就差一截:
    ///     横向差 726 × (新/旧 - 1) / 2, 纵向差 752 × (新/旧 - 1)
    /// 每调一档 0.008 就往左上挪 3 / 6 像素, 而且**再也没人把它摆回来**
    /// (缩放停下之后就没人调用 Reposition 了) —— 正是"越放大越往左上角跑"。
    /// </summary>
    public void SetScale(double s)
    {
        Scale = s;
        // 缩放挂在 FlipHost 上(两面一起缩), 翻面动画用的 RenderTransform 也在它身上
        FlipHost.LayoutTransform = new ScaleTransform(s, s);
        Settle();
    }

    // ==================================================================
    // 舞台: 定死 726×752 是**基准**, 不是上限
    // ------------------------------------------------------------------
    // 之前窗口一次定死、再也不动, 是为了给"往外扩"的动画一个稳定基准 —— 那条规矩
    // 没变, 变的是**内容放大到舞台装不下**的时候怎么办: 以前舞台不跟着长, 超出
    // 窗口的那一圈就被 hwnd 硬切掉(左右两端的键、下面那行灰字)。
    // 现在只往上长: 内容视觉尺寸超过基准就把窗口撑大(从来不缩回)。
    // 长 > 缩很重要 —— 缩的话会和"内容变矮"反复互相触发, 窗口开始抖。
    // ==================================================================
    public ISettingsHost Host;

    /// <summary>
    /// 拖齿轮挪界面时通知外面: (偏移X, 偏移Y, 是不是松手了)。
    /// 必须回写 InputCore —— 手柄那边(R3 + 左摇杆)也在改同一对偏移,
    /// 只改窗口自己的会被下一帧的同步逻辑盖回去, 界面一跳就回原位。
    /// </summary>
    public event Action<double, double, bool> Dragged;

    /// <summary>相对默认位置(不动点停在屏幕中线、离屏幕底 24 那一档)的偏移, R3 + 左摇杆调整。</summary>
    public double OffsetX, OffsetY;

    public void Reposition() => PositionBottomCenter();

    public bool Flipped { get; private set; }

    /// <summary>右下角那个按钮: 正面是齿轮, 背面是返回箭头, 位置一样。</summary>
    Button CornerBtn => Flipped ? BackBtn : GearBtn;

    /// <summary>
    /// 进出设置(R3 / 齿轮 / 返回箭头)。**2.1.0 起不再翻面** —— 要的就是
    /// "九颗键当场变成九个设置格", 中间演一段翻转反而把"直接换掉"这件事藏起来了。
    /// 正面(Root)和背面(Back)本来就摆在 OverPanel 的**同一格**、同一个尺寸
    /// (414×442), 换可见性这一下看着就是卡片的**内容**换了, 卡片本身没动。
    /// (FlipHost 这个容器名和 RenderTransform 那套先留着: 缩放挂的是它的
    /// LayoutTransform, 改名要动的地方太多。)
    /// </summary>
    public void Flip(bool toBack)
    {
        if (Flipped == toBack) return;
        // 换布局那一下还在演: 先把扩张动画收尾, 免得半截宽度留着
        if (_flipL) EndExpand();
        App.Log("切换 -> " + (toBack ? "设置" : "键盘"));

        // 设置页**恒是九键那么宽**(360 的内容区)。从全键盘(640)进出设置时两张卡片
        // 差着 280, 直接换就是"啪"一下缩掉 / 撑开一大截 —— 那一下没有动画兜着,
        // 比原来的翻面还扎眼。所以宽度不一样的时候, 借"左右扩张"那套机器把宽度
        // 顺过去: 换脸发生在宽度已经顺到位之后, 前后两张卡片严丝合缝。
        // 【读 ContentW 不读 KeyArea.Width】两个数最终是相等的(宽度由绑定跟着),
        // 但 ContentW 是**我们自己写进去的那个数**, 不存在"上一轮布局留下的旧值"
        // 这种事 —— 2.1.23 / 2.1.24 那两次都是在这儿读错了时刻。
        double w1 = toBack ? KEY9_BOX : ContentW;   // 换完之后内容区该多宽
        double w0 = toBack ? ContentW : SET_CARD;   // 换之前屏幕上那张的宽度
        if (Math.Abs(w0 - w1) < 1)
        {
            // 九键 -> 设置: 宽度一样, 不用演收窄。ShowSide 里会把那九个格子浮出来,
            // 看着就是"九颗键变成了九个设置格", 不再是"啪"一下硬切。
            ShowSide(toBack);
            ApplyExStyle(false);
            return;
        }
        if (toBack)
        {
            // 全键盘 -> 设置: 左右两条边收回到九键那么宽, **到位那一刻**才换成设置格。
            // keepKeys=true: 收窄这一路上**不掏九键出来占位** —— 以前掏了, 于是看着是
            // "先缩窄 -> 跳一下九键 -> 再变设置", 中间那一次的九键纯属多余的弯路。
            // 现在收窄期间还是屏幕上原本那套 26 键(跟着一起淡掉), 一气呵成。
            TweenWidth(w0, w1, () => ShowSide(true), keepKeys: true, fadeKeys: 0);
        }
        else
        {
            // 设置 -> 全键盘: 先按九键那么宽把键盘摆出来(26 键靠缩放挤着), 再撑开
            SetWidths(KEY9_BOX);
            Grid9.Visibility = Visibility.Collapsed;
            Grid26.Visibility = Visibility.Visible;
            ShowSide(false);
            TweenWidth(KEY9_BOX, w1, null, fadeKeys: 1);
        }
        ApplyExStyle(false);
    }

    /// <summary>
    /// 设置页整页定尺寸: 卡片 + 页边距 + **整页自己那圈描边**。
    /// 最后那一项是主题说了算的(可爱粉 4、其余 1), 不能按 1 算 —— 少算的话那圈
    /// 粗边就会吃掉内容区, 里面九格被挤扁(只剩左边和上边的线, 右边和下边被压到
    /// 边框底下)。**换主题改了那圈边之后也得重算一次**(见 ApplyTheme 末尾)。
    /// </summary>
    void SizeBackCard()
    {
        double bw = Back.BorderThickness.Left;
        Back.Width = BackBoxW(bw);
        Back.Height = BackBoxH(bw);
    }

    /// <summary>换掉正反两面。背面的尺寸按正面当前的实际大小定死, 窗口才不会忽然变尺寸。</summary>
    void ShowSide(bool back)
    {
        Flipped = back;
        // 换面时两个角落图标都缩回原样: 免得上一次放大留在背面返回箭头上
        ZoomTo(GearZoom, 1.0);
        ZoomTo(BackZoom, 1.0);
        _overCorner = false;
        if (back)
        {
            // 设置页的大小跟从哪一面翻过来无关: 就是"九颗键那么大的 3×3 + 一圈框",
            // 再加框外上面一行标题、下面一行返回。以前照搬正面的实际大小:
            // 从九键进是 390×498 的长条, 从全键盘进更是 640 宽 —— 同一个设置页
            // 在两个入口下长成两种形状, 九宫格也被拉散。
            // ActualWidth 是布局尺寸(缩放由 FlipHost 统一做), 所以这里用布局单位就行。
            SizeBackCard();
            // 背面是 Loaded 时才搭的; 万一翻得太早先补搭
            if (_setStack.Count == 0) BuildSettingsSide();
            RefreshSettingsSide();
            // 【2.1.15: 进设置之前先给"马上要走那一面"拍张照】以前从九键进设置是硬切:
            // 卡片当场换掉、只有那九个格子浮出来, 旧的一页连个交代都没有 —— 看着就是
            // "整个页面换了一下"。现在走和设置里**钻深层**一模一样的规矩(见 ShowLeave):
            // 旧的一页先拍张照垫在九个格子底下, 陪着演一场再撤。
            //   · 照片必须在 Root 收起来**之前**拍 —— 收起来就量不到尺寸了;
            //   · 只有"屏幕上原先摆着九颗键"才有得拍。从全键盘过来那一路是"左右收窄"
            //     的专场(见 Flip), 那儿不该再多插一张照片。
            var snapIn = Grid9.Visibility == Visibility.Visible ? SnapshotFace() : null;
            Root.Visibility = Visibility.Collapsed;
            Back.Visibility = Visibility.Visible;
            // 翻过来时把设置页的"光标"放回第一行, 并让滚动条回到顶上。
            // 要等这一面真的排好版才准, 所以推到布局之后再做。
            Dispatcher.BeginInvoke(new Action(ResetSettingsCursor),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // 【2.1.18: 窗口位置必须在**动画起步之前**定】
            // 见文件末尾 Settle 那条注释 —— 顺序换错一次界面就歪一截。
            Settle();
            // 进设置的"那一下": 九个格子轻一点、沉半格, 再浮回来 + 淡入。
            // 用的是进层那一套语言(见 RiseIn), 幅度砍半 —— 这是**换面**不是**进层**。
            // 别在格子上做: 那一层动画属于"光标浮起"(CellPop), 两套抢同一个
            // ScaleTransform 会互相打断, 刚进来中心那一格就抽搐。
            // dir = +1: 进设置算"往里走", 和往设置深处钻一层同一个转向。
            if (snapIn != null) ShowLeave(_set, snapIn, 1);
            PopSetPanel(1);
        }
        else
        {
            // 【2.1.15: 回九键同样先拍一张】这一场以前是整个界面唯一一处"动整张卡片"
            // 的地方(PopKeyPanel 动的是 Root, 连底板带描边一起淡), 于是退出来比进
            // 去难看得多。现在对称了: 给设置那一页拍张照摆在**内容区**那一格里,
            // 由 ShowFacePhoto 陪着演出去; 动的是框里那一块, 卡片本身不动。
            // 判据同上(见上面"进设置"那条): 屏幕上要回去的是全键盘就别插手。
            bool plain9 = Grid9.Visibility == Visibility.Visible;
            var snapOut = plain9 ? Snapshot(_set) : null;
            Back.Visibility = Visibility.Collapsed;
            Root.Visibility = Visibility.Visible;
            // 预输入版下焦点得还给记事本 —— 点设置控件时焦点被它们拿走了,
            // 不还回去翻回正面就打不出字
            if (_preview) { NoteBox.Focus(); Keyboard.Focus(NoteBox); }
            // 离开设置这一面 -> 层数清点回首页。否则从第二层直接回九键(不一层层退出来)
            // 之后, 下一回再进设置还停在当初钻进去的那一层, 很意外。
            if (_setStack.Count > 1) BuildSettingsSide();
            // 离开设置这一面就把摇杆那一套忘掉: 这一摞不是每次都重搭的(只有退到
            // 首页以上才 BuildSettingsSide), 留着的话下一回翻进来时 EntrySlot
            // 会拿着**上一次**在设置里握着的那个方向说话。
            _set.ForgetStick();

            // 【2.1.18: 同上 —— 所有动结构的活干完了, 先把位置定死再演动画】
            Settle();

            // 回九键那一下: 九颗键也"顶出来"一下 —— 和进设置那一下是对称的。
            // 以前只做了"进", 没做"回", 于是进有过渡、回是硬切。
            // 只对着九键做: 回全键盘那条路自带"左右撑开 + 淡入", 别叠两层。
            if (plain9)
            {
                if (snapOut != null) ShowFacePhoto(snapOut, -1);
                PopKeyPanel(-1);
            }
        }

        // 【Settle 为什么挪到两支里面、而不是留在这儿统一收尾】
        // 它干的事是"按卡片现在的位置反推窗口该摆哪儿", 而位置是用
        // TranslatePoint 量的 —— 那照的是**当前这一瞬的变换**。换页动画是靠
        // RenderTransform 摆姿势的: 旋转那一套为了不闪, 会先把 ro.Angle / sc.Scale
        // 写成起点的本地值(转了 18 度那一副)。放在最后 Settle 就是正好撞在那一刻,
        // 量到的是"内容区已经歪了 105 像素"的位置, 窗口照这个错数反推, 整界面
        // 当场瞬移 —— 用户原话: "页面的位置跳了"。
        // 2.1.17 试过"量之前先把 RenderTransform 摘掉、量完装回去", **那条路在
        // WPF 里不成立**: 变换是挂在 Visual 上的缓存, 同一个调用里改完立刻量,
        // 量到的还是改之前那一份(探针实测: 摘完仍算出歪 105 的位置)。
        // 正解是**别让两者同时发生**: 结构改完 -> 定位置 -> 再启动动画。
        App.Log($"翻面 {(back ? "到设置" : "回键盘")}: "
              + $"正面 {Root.ActualWidth:0}x{Root.ActualHeight:0} · "
              + $"背面 {Back.Width:0}x{Back.Height:0} · "
              + $"窗口 {ActualWidth:0}x{ActualHeight:0}");
    }

    /// <summary>立刻回到正面(界面正要藏起来, 没必要演)。</summary>
    void Unflip()
    {
        if (_flipL) EndExpand();
        ShowSide(false);
        ApplyExStyle(false);
    }

    // ================= 换布局: 左右两边扩张（九键 ↔ 全键盘）=================
    bool _flipL;
    DateTime _flipLAt;
    /// <summary>
    /// 扩张动画正在演。演的过程中**宽度这一组归动画管**, Render 不许插手
    /// (见 Render 里那个 !_expanding 的判断), 不然 mid() 一换布局,
    /// 下一帧就把宽度直接拽到目标值, 动画等于白演。
    /// </summary>
    bool _expanding;

    /// <summary>
    /// "全键盘收窄进设置"那一段专用: 收窄途中**不许把九键掏出来**占位
    /// (Render 里换键那一处的判断)。以前没这个标记, 收窄的中途画面上换向九键,
    /// 于是"全键盘 -> 设置"看着就是"先缩窄 -> 跳一下九键 -> 再变设置"。
    /// </summary>
    bool _keyKeep;

    /// <summary>九键 ↔ 全键盘"撑开 / 收回"的时长。</summary>
    // EXPAND_MS 已搬进 FxKit.MsExpand(230)

    /// <summary>
    /// 九键 ↔ 26 键全键盘: **左右两边扩张**(2.1.0)。
    /// 以前是"横向压到 0 再展开"那一套翻面 —— 那是翻设置用的, 用在换布局上
    /// 看着像整块界面闪了一下。现在要的是"四条边当出口"里那两条竖边:
    /// 上下钉着不动, 只有左右两条边往外推(或往回收), 卡片就这么撑开 / 收回。
    ///
    /// 26 键那七排是**按 640 排好版**的(见 BuildKey26), 动画期间给它们套一个
    /// 横向缩放: 起点 = 当前宽度 / 640、终点 = 目标宽度 / 640。于是"键盘区变宽"
    /// 和"键还原成正常宽高"是同一条曲线上的两端 —— 全程刚好填满, 既不会有
    /// 某一排被切掉半截, 也不会有键在动画里挪位(那看着才叫乱)。
    ///
    /// mid 是"真正换布局"那一下, 由 InputCore 传进来。**这次不用等演到中点**:
    /// 扩张不像压扁那样能把换脸藏起来, 索性一开始就换, 让新的一套键直接撑开。
    /// </summary>
    public void FlipLayout(Action mid)
    {
        // 看门狗: Completed 万一没跑, _flipL 会一直挂着, 之后就再也换不动布局了
        if (_flipL && (DateTime.Now - _flipLAt).TotalMilliseconds > 1000) EndExpand();
        // 已经在设置面(或者上一次还没演完): 不叠动画, 直接换
        if (_flipL || Flipped) { mid(); return; }
        // 【同样读 ContentW】见 Flip() 里那条注释。
        double w0 = ContentW;
        mid();
        // 【目标宽度按 mid() 换完的**布局**算, 不按"当前宽不等于 360 就往 360 收"】
        // 老判据是从 w0 反推的, 于是"从全键盘进录快捷键页再退回来"这种
        // (进去前就已经是 640)会被判成"该收到 360", 而退完布局其实还是九键 ——
        // 宽度和布局就这么错开一格。认布局就不会错: 26 键 640, 其余 360。
        // 【这里要问 core 现在**真的**是哪个布局】Render 是 Poll 之后才跑的,
        // _layoutNow 那一帧还是旧值, 认它就会定错目标宽度。
        var want = LayoutOf?.Invoke() ?? _layoutNow;
        TweenWidth(w0, want == Layout.EN26 ? KEY26_BOX_W : KEY9_BOX, null);
    }

    /// <summary>这一帧 core 是哪个布局(见 Render)。</summary>
    Layout _layoutNow = Layout.CN9;

    /// <summary>
    /// 问 core **现在**是哪个布局。FlipLayout 里要用它, 而 FlipLayout 是在 Poll 里
    /// 被叫起来的 —— 那时候这一帧的 Render 还没跑, _layoutNow 还是上一帧的旧值。
    /// 认旧值就会定错目标宽度(比如"本来就在全键盘上再进录快捷键页", 会被判成
    /// 该收到九键的 360)。
    /// </summary>
    public Func<Layout> LayoutOf;

    // ==================================================================
    // 「内容区宽度」只有一个源头(2.1.26)
    // ------------------------------------------------------------------
    // 键盘区(KeyArea)、框(Frame)、卡片外面那四行(候选 / 拼音 / 状态 / 预览框)的
    // 宽度, 以前是**六行赋值**, 而且无动画和有动画两条路各写了一遍 —— 一件事复制
    // 六份, 就有六次机会漏掉其中一个(2.1.23 / 2.1.24 就是这么来的)。
    //
    // 现在这六处一律绑定 ContentW(XAML 里写死), C# 只负责"这个数该是多少"。
    // 顺带解决掉那个更阴的问题: 以前手算位置读的是 KeyArea.Width(这一帧刚写进去的)
    // 或 ActualWidth(上一轮布局留下的), **两个数不是同一个时刻**, 于是每次动动画
    // 都可能踩一回。ContentW 是我们自己写进去的值, 不存在"旧数"。
    //
    // 【必须是依赖属性】DoubleAnimation 只能驱动依赖属性。
    // ==================================================================
    public static readonly DependencyProperty ContentWProperty =
        DependencyProperty.Register(nameof(ContentW), typeof(double), typeof(OverlayWindow),
            new PropertyMetadata(KEY9_BOX, OnContentW));

    /// <summary>内容区(键盘区)的宽度。九键 360、全键盘 640。</summary>
    public double ContentW
    {
        get => (double)GetValue(ContentWProperty);
        set => SetValue(ContentWProperty, value);
    }

    static void OnContentW(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (OverlayWindow)d;
        // 【26 键那七排的横向缩放必须和宽度是同一条曲线】
        // 它们是按 640 排好版的(见 BuildKey26), 键盘区变宽时靠缩放把七排压到当前
        // 宽度。以前这里是**另起一条 DoubleAnimation** —— 两条动画哪怕时长一模一样
        // 也是各起各的时钟, 会错帧, 表现就是键在动画里被挤窄 / 切掉半排(2.1.24 那种)。
        // 现在它是 ContentW 的**回调**: 宽度那一帧变, 缩放同一帧跟着变, 不存在错帧。
        w.SyncGrid26Scale();
    }

    /// <summary>
    /// 把 Grid26 的横向缩放钉成 ContentW / 640。
    /// 每帧钉是刻意的: 窗口F 那条路(InputCore 直接把 Layout 改成 EN26)不走
    /// FlipLayout, 不钉的话缩放会停在上一回的值上 —— 七排被挤成九键那么窄。
    /// </summary>
    void SyncGrid26Scale()
    {
        if (Grid26?.RenderTransform is not ScaleTransform st) return;
        double s = ContentW / KEY26_BOX_W;
        if (Math.Abs(st.ScaleX - s) > 0.0005) st.ScaleX = s;
    }

    /// <summary>
    /// 把内容区的宽度写成 w(不带动画)。框和卡片外面那几行由 XAML 的绑定跟着,
    /// 26 键的横向缩放由 OnContentW 同一帧跟着 —— 这里只剩一个数。
    /// </summary>
    void SetWidths(double w) => ContentW = w;

    /// <summary>
    /// **左右两边扩张**: 内容区宽度从 w0 顺到 w1(其余跟着), 演完调 done。
    /// 换布局(L3)和"全键盘 ↔ 设置"两条路都走这里 —— 同一套动作, 别写两份。
    ///
    /// 26 键那七排是**按 640 排好版**的(见 BuildKey26), 动画期间给它们套一个
    /// 横向缩放: 起点 = w0 / 640、终点 = w1 / 640。于是"键盘区变宽"和"键还原成
    /// 正常宽高"是同一条曲线上的两端 —— 全程刚好填满, 既不会有某一排被切掉半截,
    /// 也不会有键在动画里挪位(那看着才叫乱)。
    ///
    /// keepKeys: 这一段里**不换谁露脸**(全键盘收窄进设置用 —— 中间不能再插一趟九键)。
    /// fadeKeys: &gt;= 0 时顺便给正露脸的那套键做一次淡出(0) / 淡入(1)。
    /// </summary>
    void TweenWidth(double w0, double w1, Action done, bool keepKeys = false, double fadeKeys = -1)
    {
        _flipL = true;
        _flipLAt = DateTime.Now;
        _expanding = true;

        // 【谁露脸按**目标宽度**定, 不按"比现在宽还是窄"】
        // 录快捷键那一页要是"进去前本来就在全键盘上"(九键按过 L3), 进去这一趟是
        // 640 -> 640 —— 按宽窄判就成了"该收回", 于是撑开的那一趟摆出九颗键。
        // 认目标宽度就不会错: 640 是全键盘, 360 是九键。
        bool want26 = w1 > KEY9_BOX + 1;
        if (keepKeys)
        {
            // 谁露脸**维持原样** —— 这一路是"从全键盘收窄进设置", 中间不该再插一趟
            // 九键。Render 也认这个标记(见那里), 不会在这段里把九键掏回来。
            _keyKeep = true;
        }
        else
        {
            // 谁露脸按**目标**宽度定死 —— Render 在 _expanding 期间不碰这两行。
            //
            // 【收回这一趟、而且窗口F 正要回台: 两套键一律都不露脸】
            // 录快捷键那一页进 / 出都是"左右扩张 / 收回"演出来的。以前出去那一段
            // 让 26 键顶着(理由是"别中途插一趟九键"), 可 26 键在动画里是被整体
            // 横向缩放的(w1/640 = 360/640 = 0.5625)—— 于是演的那 230 毫秒里,
            // 全键盘的键被生生挤窄了一半, 再"啪"一下换回九键。用户原话:
            // "在保存或者按b取消的手绘动画出了点小问题, 全键盘的按键会有个缩窄的
            // 画面出现"。现在这套: 收回途中两套键都收掉, 只让框的左右两条边往回收,
            // 这一摞(F)由 StageOn 就地接回来 —— 从头到尾都只有框在动。
            // 【认 _qkOn 不认 _qkFocus】_qkOn 才是"这一摞现在在不在台上": 进去那
            // 一下 StageOff 已经把它设成 false(屏幕上该看见的是键), 出来那一下
            // StageOn 设成 true(屏幕上该看见的是这一摞)。
            bool qkBack = !want26 && _qkOn;
            Grid9.Visibility = (!qkBack && !want26) ? Visibility.Visible : Visibility.Collapsed;
            Grid26.Visibility = (!qkBack && want26) ? Visibility.Visible : Visibility.Collapsed;
        }
        // 0 = 淡出(进设置), 1 = 淡入(回全键盘)
        if (fadeKeys >= 0)
            KeyFade(KeyLayerOn(), fadeKeys < 0.5 ? 1 : 0, fadeKeys, (int)(FxKit.MsExpand * FxKit.KeyFadePart));

        // 【只动 ContentW 一个数】其余六个由绑定跟着, 26 键的横向缩放由 OnContentW
        // 同一帧跟着 —— 不再有"六条动画各跑一遍、谁慢半拍"的机会。
        //
        // 【done / EndExpand 必须挂在这一条动画上】以前挂在 Grid26 的 ScaleX 动画
        // Completed 上, 而 ScaleX 现在改由回调驱动、自己没有动画了 —— 那个 Completed
        // 永远不会触发, _flipL 就一直挂着, 只能等 1000ms 看门狗兜底(表现是"换布局
        // 卡一下才动")。
        var ease = FxKit.EaseOut();
        var span = FxKit.Ms(FxKit.MsExpand);
        var a = new DoubleAnimation(w0, w1, span) { EasingFunction = ease };
        a.Completed += (_, _) =>
        {
            BeginAnimation(ContentWProperty, null);   // 撒手, 别把值冻在动画上
            ContentW = w1;
            done?.Invoke();
            EndExpand();
        };
        BeginAnimation(ContentWProperty, a);
    }

    /// <summary>正露脸的那套键(九键 / 26 键)。淡入淡出只对着它做。</summary>
    FrameworkElement KeyLayerOn() => Grid26.Visibility == Visibility.Visible ? Grid26 : Grid9;

    /// <summary>给那套键做一次淡出 / 淡入。两层都归到同一个方法里, 免得将来某一面忘记收。</summary>
    void KeyFade(FrameworkElement layer, double from, double to, int ms)
    {
        if (layer == null) return;
        layer.BeginAnimation(OpacityProperty, null);
        layer.Opacity = from;
        layer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            { EasingFunction = FxKit.EaseOut() });
    }

    /// <summary>
    /// 把两套键的透明度都还原成 1, 顺手把动画撒手。
    /// **必须在换完面之后**调: 动画不撒的话, 下一回这套键再出场就停在半透明上。
    /// </summary>
    void KeyFadeReset()
    {
        foreach (var e in new FrameworkElement[] { Grid9, Grid26 })
        {
            e.BeginAnimation(OpacityProperty, null);
            e.Opacity = 1;
        }
    }

    /// <summary>扩张动画收尾: 撒手交还给 Render, 并按新的卡片宽度重新摆一次位置。</summary>
    void EndExpand()
    {
        _flipL = false;
        _expanding = false;
        _keyKeep = false;
        KeyFadeReset();     // 这一路演完了, 两套键都不许再带半透明
        Settle();           // 宽度刚变过 -> 先把布局落地再摆位置(见 Settle 的说明)
    }

    // ================= 齿轮 / 返回箭头: 点击翻面, 长按拖动 =================
    //
    // 事件挂在窗口上、命中判定自己做(见 PointerOverCorner), 不再挂在按钮上。
    // 原因是: 界面为了不挡游戏默认是鼠标穿透的, 而且翻面时整块内容会换掉,
    // 这两个加起来很容易把 MouseUp 弄丢 —— 一丢按下状态就永远挂着,
    // 表现正是"鼠标一碰齿轮, 界面就跟着指针跑, 点也点不动"。

    const int DRAG_HOLD_MS = 300;   // 长按阈值: 按住这么久才算"想拖"
    const double DRAG_MOVE = 3;     // 长按生效后再挪这么点(屏幕像素)就真的开始拖
    const double CLICK_SLOP = 8;    // 没按够久时, 要甩这么远才当拖; 否则算点击

    bool _down, _drag;
    DateTime _downAt;
    Win32.POINT _downFrom;
    double _ox0, _oy0;

    /// <summary>只有正面的齿轮能拖。背面返回箭头只管点击, 免得又变成"碰到就跑"。</summary>
    bool CanDrag => !Flipped;

    void OnWinDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (!PointerOverCorner()) return;
        _down = true; _drag = false;
        Win32.GetCursorPos(out _downFrom);
        _downAt = DateTime.Now;
        _ox0 = OffsetX; _oy0 = OffsetY;
        e.Handled = true;
    }

    void OnWinMove(object sender, MouseEventArgs e)
    {
        if (!_down) return;
        // 左键其实已经松开了(Up 没送到): 立刻收尾, 不然 _down 会一直挂着
        if (Mouse.LeftButton == MouseButtonState.Released) { EndPress(); return; }

        Win32.GetCursorPos(out var p);
        int dx = p.X - _downFrom.X, dy = p.Y - _downFrom.Y;
        double moved = Math.Abs(dx) + Math.Abs(dy);

        if (!_drag)
        {
            if (!CanDrag) return;                   // 背面返回箭头不拖
            if (moved < DRAG_MOVE) return;          // 几乎没动
            bool held = (DateTime.Now - _downAt).TotalMilliseconds >= DRAG_HOLD_MS;
            // 没按够久、又只是手抖 -> 还是当成点击
            if (!held && moved < CLICK_SLOP) return;
            _drag = true;
            Cursor = Cursors.SizeAll;
            SetCornerHover(true);   // 进拖动了, 齿轮放大一点给个反馈
            App.Log("齿轮: 长按生效, 进入拖动(松手就停在这)");
        }

        var d = PxToDip(dx, dy);
        OffsetX = _ox0 + d.X;
        OffsetY = _oy0 + d.Y;
        PositionBottomCenter();
        Dragged?.Invoke(OffsetX, OffsetY, false);
    }

    void OnWinUp(object sender, MouseButtonEventArgs e) => EndPress();

    /// <summary>
    /// 一次按下的收尾: 拖动过就只落盘位置, 没拖动且松手时还在按钮上就翻面。
    /// 松手时指针已经不在按钮上了(比如拖到一半松在别处)就什么都不做 ——
    /// 不然"按住齿轮往游戏里一甩"会莫名其妙翻到设置页。
    /// </summary>
    void EndPress()
    {
        if (!_down) return;
        bool dragged = _drag;
        bool over = PointerOverCorner();
        _down = false; _drag = false;
        Cursor = null;                                  // null = 退回默认箭头
        SetCornerHover(over);

        if (dragged) { App.Log($"齿轮: 拖动结束 -> 偏移 {OffsetX:0},{OffsetY:0}"); Dragged?.Invoke(OffsetX, OffsetY, true); }
        else if (over) { App.Log("角落按钮: 点击"); Flip(!Flipped); }
        else App.Log("角落按钮: 松手时指针已不在按钮上, 不翻面");
    }

    /// <summary>屏幕像素 -> WPF 单位。高分屏下不换算, 拖动会跟手感差一截。</summary>
    Point PxToDip(int dx, int dy)
    {
        var src = PresentationSource.FromVisual(this);
        var m = src?.CompositionTarget?.TransformFromDevice;
        return m == null ? new Point(dx, dy) : m.Value.Transform(new Point(dx, dy));
    }

    /// <summary>
    /// 定时查一下指针是不是停在角落按钮上(由 App 的帧循环调用)。
    /// 界面默认整块鼠标穿透(不然会挡住游戏画面), 只有指针进到这一小块方格
    /// 才把穿透临时撤掉, 齿轮才点得到; 指针一走立刻恢复。
    /// 预输入版本来就不穿透, 这一步在那边是空转。
    /// </summary>
    public void HitTestTick()
    {
        // 兜底: MouseUp 万一没送到(松手松在窗口外面 / 被别的窗口抢走),
        // 这里按"左键是不是还按着"收尾。没有这一步 _down 会一直挂着,
        // 表现就是"鼠标一碰齿轮界面就跟着跑"。
        if (_down && Mouse.LeftButton == MouseButtonState.Released) EndPress();

        if (!IsVisible || _down) return;    // 按下期间不换窗口样式, 免得消息路由被改乱
        bool over = PointerOverCorner();
        if (over == _overCorner) return;
        _overCorner = over;
        SetCornerHover(over);
        ApplyExStyle(false);
    }
    bool _overCorner;

    // ---- 悬停反馈: 不画高亮块、不改颜色, 就把图标本身放大一点 ----
    const double CORNER_HOVER = 1.22;

    /// <summary>齿轮 / 返回箭头在鼠标放上去时放大一点, 移开缩回原样。</summary>
    void SetCornerHover(bool on)
    {
        ZoomTo(Flipped ? (FrameworkElement)BackZoom : GearZoom, on ? CORNER_HOVER : 1.0);
    }

    bool PointerOverCorner()
    {
        var b = CornerBtn;
        if (b.ActualWidth <= 0) return false;
        Win32.GetCursorPos(out var p);
        var a = b.PointToScreen(new Point(0, 0));
        var c = b.PointToScreen(new Point(b.ActualWidth, b.ActualHeight));
        const int PAD = 6;      // 22px 的按钮太小了, 放宽一圈好点
        return p.X >= a.X - PAD && p.X <= c.X + PAD
            && p.Y >= a.Y - PAD && p.Y <= c.Y + PAD;
    }

    // ================= 一层层九宫格: 设置 + 窗口F 共用 =================
    //
    // 【2.1.3: 页面是平的】以前"压着的层"是有 UI 的 —— 每往里一层, Stage 里就多
    // 一块 Border 摞着(旧的缩一点压暗、中间再垫一层糊掉的快照)。现在改成:
    //   Pages   走过的那一串**纯数据**(标题 / 格子内容 / 光标 / 第几页), B 退回上一层要用
    //   Panel   现在这一页那一块面板 —— **整个 Stage 里就这么一块**
    //   Grid    Panel 里那个装格子的容器
    //   Boxes   现在这一页那几个格子(画光标 / 按下去缩回去用)
    // 进更深一页 = 旧页出门 + 新页进门; B 退一层 = 反向演一遍, 再把新露出来的
    // 那一页**重新搭一遍**(你可能在更深的地方改过它上面写的值)。
    //
    // 两套界面各抱一份: 设置摆在**背面**那张卡里, 窗口F 摆在**正面**的键盘区。
    // 下面这几个是"老名字的转发" —— 设置那一大段代码(以及它几百处调用点)
    // 一个字都不用改, F 那边则显式把 _qk 传进去, 走的是同一批方法。
    const int GWL_EXSTYLE = -20;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_TRANSPARENT = 0x00000020;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // TOOLWINDOW 恒开: 只是让它不出现在 Alt+Tab 里, 不影响拿焦点
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        int style = (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, style);
        ApplyExStyle();
    }

    /// <summary>
    /// 按当前状态切窗口扩展样式。
    /// 非预输入版: NOACTIVATE(不抢游戏的输入焦点, 但鼠标照样收得到) + TRANSPARENT(鼠标穿透)。
    /// 预输入版: 两个都拿掉 —— 我们要的就是"像切窗口一样切到它上面"。
    /// 穿透还有两个例外要临时撤掉: 翻到了设置面(那一面全是控件, 得能点),
    /// 以及指针正停在右下角齿轮上(见 HitTestTick) —— 齿轮本身要能点能拖。
    /// </summary>
    void ApplyExStyle(bool log = true)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (hwnd == IntPtr.Zero) return;

        int style = (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        // 翻到背面那一面全是控件, 得能吃焦点: 用户是主动来点设置的, 这时候抢一次
        // 焦点是合理的。翻回正面(ShowSide)会再走一次这里, 自动还回去。
        // 窗口F 占着屏幕时也得像设置面那样能吃焦点: 格子要能点(见 Render 里的 _qkFocus)。
        bool q = _qkFocus;

        if (_preview || Flipped || q) style &= ~WS_EX_NOACTIVATE;
        else style |= WS_EX_NOACTIVATE;

        if (_preview || Flipped || q || _overCorner) style &= ~WS_EX_TRANSPARENT;
        else style |= WS_EX_TRANSPARENT;

        // **写之前先比一比**: 这一个函数被唤出、翻面、齿轮命中、预输入切换等一大堆
        // 地方反复调用, 而每次 SetWindowLongPtr(GWL_EXSTYLE) 都会让 Windows 重新
        // 计算这个 layered 窗口的合成方式 —— 值其实没变也照写的话, DWM 就被白白
        // 催着重合好几次, 表现正是"唤出来那一瞬间背景忽然变透, 过几秒才慢慢回来"。
        // 值一样就直接返回, 一次都不写。
        if (_lastEx == style) return;
        _lastEx = style;

        SetWindowLongPtr(hwnd, GWL_EXSTYLE, style);
        // 真的动了样式才顺手催一次重画: 让新样式立刻落到合成面上,
        // 别把"半透明的旧合成结果"留在屏幕上(见 NudgeComposition)。
        NudgeComposition();
        if (log)
            App.Log($"窗口样式 -> {(_preview ? "预输入版(可获焦点)" : "直出版(不抢焦点)")} ex=0x{style:X}");
    }

    /// <summary>上一次写进 GWL_EXSTYLE 的值。一样就不重写(见 ApplyExStyle)。</summary>
    int _lastEx = int.MinValue;

    /// <summary>
    /// 催 DWM 把这个窗口重新合一遍。
    /// <para>窗口是透的(layered), 屏幕上那一层是 DWM 合成的, 不是 WPF 画完就完事。
    /// "唤出来背景忽然变透明、过几秒才自己恢复"就是合成层还贴着上一次的结果 ——
    /// WPF 这一侧的 Opacity 全程是 1, 变的是合成层。SWP_FRAMECHANGED 相当于
    /// 跟系统说"这块玻璃的框变了, 重新合一次", 比干等它自己想起来快得多。</para>
    /// </summary>
    void NudgeComposition()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        // WPF 这一侧也重画一遍, 两边一起更新。**内容根也要点名重画**:
        // 只 InvalidateVisual 窗口自己时, 底下那一摞有可能被判定成"没变"而跳过,
        // 于是催了半天催出来还是同一张图。
        InvalidateVisual();
        if (FlipHost != null) FlipHost.InvalidateVisual();
    }

    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004,
               SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                    int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);
    static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, int dwNewLong);
    static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, int dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                         : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);

    /// <summary>唤出头一两帧把窗口停在屏幕外(见 <see cref="ShowReady"/>)。</summary>
    bool _holdOff;
    /// <summary>停在外面期间, 第一帧已经真的画出来了(ContentRendered 来过)。</summary>
    bool _holdRendered;
    int _holdFrames;
    DateTime _holdAt;
    /// <summary>第几次唤出。快按快收时上一轮的定时器可能还没停, 用这个认出过期通知。</summary>
    int _holdGen;

    /// <summary>
    /// 唤出: 把内容画好**再**露脸, 而且头一两帧先待在屏幕外。
    /// <para>为什么非得躲到屏幕外: <see cref="Window.Show"/> 让 HWND 可见的那一瞬间,
    /// DWM 立刻把**这个窗口上一回的合成结果**贴出来 —— 尺寸、位置、内容全都是
    /// 上一次的, 连透明度也是上一次的。我们随后才设的 <c>Opacity = 0</c> 要等 WPF
    /// 下一次渲染才落到 HWND 上, 根本抢不过 DWM。于是每次唤出都能看见"旧界面
    /// 闪一下", 偶发的"透明度怪一秒又变回来"也是同一件事的慢动作版本
    /// (WPF 内部各处的透明度全程恒定, 插桩 26 次采样证实过 —— 变的是合成层)。</para>
    /// <para>所以规矩换成: **先把窗口摆到屏幕外, 再 Show**。前面那一下照旧会发生在
    /// 屏幕外, 看不见; 等 Show 之后真的渲染满一帧(实测 80ms 上下), 才连人带位置
    /// 一起挪回屏幕上。ContentRendered 靠不住(实测每次都等不到它), 真正数的是
    /// CompositionTarget.Rendering 的帧数; 万一连它都不来, 最晚 150ms 无论如何
    /// 归位 —— 宁可晚一点, 也不能让界面永远不出现。</para>
    /// </summary>
    public void ShowReady()
    {
        if (IsVisible) return;

        int gen = ++_holdGen;
        _holdOff = true;
        _holdRendered = false;
        _holdAt = DateTime.Now;
        // 保险挂在**内容**上, 不挂在窗口上 —— 见 HoldFade 那段说明。
        HoldFade(0);
        UpdateLayout();              // 尺寸先定稿, 别带着旧尺寸去量位置
        PositionBottomCenter();      // 会看到 _holdOff, 于是落在屏幕外
        Show();

        void OnRendered(object s, EventArgs e)
        {
            ContentRendered -= OnRendered;
            if (gen == _holdGen) _holdRendered = true;
        }
        ContentRendered += OnRendered;

        // ContentRendered 二次唤出之后经常不来(实测: 每次都等到兜底才亮), 所以真正
        // 靠得住的是 CompositionTarget.Rendering —— 它每渲染一帧就来一次。
        // Show 之后**满一帧**就说明新内容已经落到了合成面上, 可以归位了。
        // 订阅放在 Show 之后: 之前那些帧画的还是旧内容, 不能拿来充数。
        int frames = 0;
        void OnRendering(object s, EventArgs e) => frames++;
        CompositionTarget.Rendering += OnRendering;

        var t = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(30) };
        int n = 0;
        t.Tick += (_, _) =>
        {
            if (gen != _holdGen)                     // 这一轮已经作废(又被收起了)
            {
                t.Stop(); CompositionTarget.Rendering -= OnRendering; return;
            }
            n++;
            if (_holdRendered || frames >= 1 || n >= 5)   // n>=5: 150ms 兜底, 到点无论如何归位
            {
                t.Stop();
                CompositionTarget.Rendering -= OnRendering;
                _holdFrames = frames;
                ReleaseHold();
            }
        };
        t.Start();
    }

    /// <summary>
    /// 唤出头一两帧把内容整个藏起来 / 再放出来(见 <see cref="ShowReady"/>)。
    /// <para>**刻意不动 <c>Window.Opacity</c>** —— 窗口是 per-pixel-alpha 的分层窗口,
    /// 屏幕上那一层是 DWM 拿 WPF 提交的位图合成的; 而 <c>Opacity = 0</c> 那几帧 WPF 是
    /// 真交过一张**全透明**的图的, 等回到 1 时 WPF 未必再交一张新的, DWM 就一直贴着
    /// 那张旧图 —— 表现正是"唤出来背景莫名变透, 退出程序再进才好"(新窗口没有历史
    /// 透明帧)。实测过: 第 1 次和第 2 次唤出抓到的 WPF 渲染结果**完全一样**, 所以
    /// 毛病只在合成这一层, 不在 WPF 画出来的内容上。</para>
    /// <para>保险改挂在内容根 <c>FlipHost</c> 上: 那是 WPF 自己画的一层, 怎么改都是
    /// 一次普通的重画, 不会去动分层窗口的合成。而且那几帧窗口本来就在屏幕外,
    /// 内容藏不藏其实都看不见 —— 这一下纯粹是"万一漏一帧到屏幕上"的兜底。</para>
    /// </summary>
    void HoldFade(double v)
    {
        if (FlipHost != null) FlipHost.Opacity = v;
    }

    /// <summary>窗口真挪回屏幕上那一刻跑一次(见 <see cref="ShowReady"/>), 用完即清。</summary>
    public Action OnShown;

    /// <summary>把停在屏幕外的窗口挪回来并亮出来。重复调用无妨(定时器/ContentRendered 都可能催)。</summary>
    void ReleaseHold()
    {
        if (!_holdOff) return;
        _holdOff = false;
        HoldFade(1);
        UpdateLayout();              // 尺寸再定一次稿: 停留期间内容可能还在变
        PositionBottomCenter();
        App.Log($"唤出: 屏幕外停 {(DateTime.Now - _holdAt).TotalMilliseconds:0}ms"
                + $"({_holdFrames} 帧)后归位 ({ActualWidth:0}x{ActualHeight:0} @ {Left:0},{Top:0})");
        // 归位这一下必须催一次重合成, 而且要**隔一会儿再催一次**: 窗口刚才在屏幕外
        // 待过, DWM 手里那张合成图还是"透明/旧"的那张, 光把 Opacity 改回 1 它不一定
        // 立刻重画 —— 这就是"唤出来背景莫名变透, 过几秒才自己回来, 偶尔一直不回来"。
        NudgeComposition();
        KickCompositionLater();
        var cb = OnShown; OnShown = null;
        cb?.Invoke();
    }

    /// <summary>
    /// 归位之后隔半秒再补一脚: 合成层要是还没跟上来, 再催一次; 万一哪路把 Opacity
    /// 落在了 1 以下, 顺手拽回来。只跑一次, 不轮询。
    /// </summary>
    void KickCompositionLater()
    {
        // 催**三次**: 归位当场已经催过一次(NudgeComposition), 这里 130ms 和 400ms
        // 各补一脚。合成层什么时候跟上来不归我们管, 多给两次机会最省事 ——
        // 而且这两脚都会顺手把落在 1 以下的透明度拽回来(见 KickCompositionOnce)。
        foreach (int ms in new[] { 130, 400 })
        {
            var t = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, _) => { t.Stop(); KickCompositionOnce(); };
            t.Start();
        }
    }

    /// <summary>催促一次重合成, 顺手把落下去的透明度拽回 1(见 KickCompositionLater)。</summary>
    void KickCompositionOnce()
    {
        if (!IsVisible) return;
        if (Opacity < 1) { Opacity = 1; App.Log("唤出兜底: 窗口透明度拉回 1"); }
        if (FlipHost != null && FlipHost.Opacity < 1)
        {
            FlipHost.Opacity = 1;
            App.Log("唤出兜底: 内容透明度拉回 " + FlipHost.Opacity);
        }
        NudgeComposition();
    }


}
