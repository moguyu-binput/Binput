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

public partial class OverlayWindow
{
// ---- 设置层与快捷层 —— 页面骨架、导航、蒙版、说明条 ----
//
// 2.1.11 之前这些代码全在 OverlayWindow.xaml.cs 一个文件里(5117 行)。
// 这次只是按职责把它拆成几个 partial class 文件 —— 一行都没改,
// 编译出来的行为跟拆之前完全一样(改名/搬位置不影响任何逻辑)。

    readonly LayerHost _set = new();
    /// <summary>窗口F 那一摞(列表页 / 编辑页 / 绑数字页)。</summary>
    readonly LayerHost _qk = new();

    List<SetPage> _setStack => _set.Pages;
    Border _veil { get => _set.Veil; set => _set.Veil = value; }
    Image _veilImg { get => _set.VeilImg; set => _set.VeilImg = value; }
    bool _setAnim { get => _set.Anim; set => _set.Anim = value; }
    Border _setCellCur { get => _set.Cur; set => _set.Cur = value; }
    bool _setDown { get => _set.Down; set => _set.Down = value; }
    bool _setStickNav { get => _set.StickNav; set => _set.StickNav = value; }

    // ---- 每格的"小说明"(光标停一会儿才弹) ----
    // 原先这些话是**印在设置页上的小字**, 一页二十来条, 字比按钮还多, 想找的那个
    // 按钮被埋在底下。现在一行都不印, 只在光标(或鼠标)在这一行上停够 SET_TIP_DELAY
    // 毫秒之后, 从这行旁边浮出一小块 —— 跟 Windows 里鼠标停在文件上弹出的提示一样。
    // 存的是 Func 不是 string: 滑行门槛、记住了多少条这种**每次都不一样**的说明,
    // 得在弹出来的那一刻现算, 不能建页面时算一次就定死。
    int _tipIdx = -1;         // 计时器认的是哪一格; 光标一走就重置
    DateTime _tipAt;          // 认住这一行的时刻
    bool _tipShown;           // 这一行的说明已经弹出来了(重复调用别再挪一次位置)
    bool _tipMouse;           // 这次是鼠标悬停认住的行(那就别被手柄光标抢回去)
    const int SET_TIP_DELAY = 650;

    /// <summary>
    /// 设置那一格**标题**的可用高度上限。2.1.25 之前是 62, 而「收起之后」那四档是
    /// 照"一步一行"写上去的(复制 / 粘贴 / 回车, 见 GameMode.Steps), 16 号字三行
    /// 要 64 —— 第三行正好被切掉, 后两档看着都"少一步", "复制+粘贴+回车"还跟
    /// "复制+粘贴"长得一模一样。给到 70; uiprobe 会拿三行真量一遍, 再缩回去就报红。
    /// </summary>
    public const double CELL_TITLE_MAXH = 70;

    // 两个版本: 打法完全一样(九键只解拼音发给系统输入法, 26 键只出纯英文),
    // 区别只有"打出来的字先去哪儿"。
    Grid _setGrid => _set.Grid;                     // 现在这一页那个 3×n 的格子容器

    /// <summary>某一摞现在最上面那一层。</summary>
    static SetPage TopOf(LayerHost h) => h.Top;

    /// <summary>设置这一摞当前(最上面)那一层。</summary>
    SetPage TopPage => TopOf(_set);

    static int SlotsOf(SetPage p) => SetNav.SlotsOf(p);

    static int PagesOf(SetPage p) => SetNav.PagesOf(p);

    static int BaseOf(SetPage p) => SetNav.BaseOf(p);

    static SetCell CellAt(SetPage p, int i) => SetNav.CellAt(p, i);

    static bool Landable(SetPage p, int i) => SetNav.Landable(p, i);

    static SetCell Cell(string title, string note = "", Action act = null,
                        Func<SetPage> child = null, Func<string> tip = null,
                        Func<bool> on = null, Func<bool> off = null,
                        Func<string> corner = null) =>
        SetNav.Cell(title, note, act, child, tip, on, off, corner);

    // ---------- 各层 ----------
    // 每一层就是"一句话标题 + 一堆格子"。格子要么当场把事办了(Act),
    // 要么还有下一层(Child)。字数都压到四个字以内 —— 三列布局下一格只有
    // 一百来像素宽, 想说的话都放进 Tip(光标停一会儿才弹), 不占格子的地方。

    const int COPY_HINT_MS = 1000;

    /// <summary>「已复制」亮到什么时候(见 Render 里 CopyChip 那两行)。</summary>
    DateTime _copyUntil = DateTime.MinValue;

    /// <summary>
    /// 关于页按 A: 整段说明复制进剪贴板, 卡片下面那行亮一下「已复制」。
    /// 复制进去的就是格子上摆的那段字(版本号、三个路径都在里头)—— 出问题时
    /// 要发给别人看, 不用照着屏幕一个字一个字敲。
    /// </summary>
    void CopyAbout()
    {
        string s = Host?.About() ?? "柄输";
        if (!Clipboard2.Set(s)) { App.Log("关于: 复制失败"); return; }
        _copyUntil = DateTime.Now.AddMilliseconds(COPY_HINT_MS);
        App.Log("关于: 已复制到剪贴板");
    }

    // ---------- 彩蛋: 连点设置首页正中那个空位 ----------
    //
    // 首页那九格里正中那一格(4 号位)**本来就是个空位** —— 按 A 什么都不干
    // (LayerActivate 里 cell == null 直接 return, 连重画都不起)。空着也是空着,
    // 就让它偷偷攒个数: 每 20 下在卡片下面那行(「滑行」二字的位置)冒一句话,
    // 亮一秒就没。攒到 100 下, 除了冒那句「100连打达成！」, 整套配色还会换成
    // 攒到 100 下, 除了冒那句「100连打达成！」, 还会**永久解锁土豪金**并立刻切过去
    // (见 Theme.UnlockGold)。
    //
    // 【间隔超过 1 秒就从头数】要的是"连打", 不是"这辈子一共按过多少下" ——
    // 慢慢悠悠按到 100 下不算数。
    // 【100 之后不收尾】继续每 20 下复播这五句(120 下又回到第一句)。
    // 【土豪金一解锁就永久在】解锁这件事单独存盘(Config 的 gold_open), 关掉程序再开,
    // 设置 → 皮肤里那一格还在 —— 不用再连打一次。没解锁的时候那一格是**空**的:
    // 一排方块里平白缺一块, 明摆着"这儿藏着个还没发现的"。

    /// <summary>两下之间超过这么久就算断, 从 0 重新数。</summary>
    const int EGG_GAP_MS = 1000;

    /// <summary>冒出来的那句话亮多久(和「已复制」一个规矩)。</summary>
    const int EGG_HINT_MS = 1000;

    /// <summary>每攒够这么多下冒一句。</summary>
    const int EGG_STEP = 20;

    /// <summary>五句话, 按 20/40/60/80/100 下的顺序。第 100 下是"达成"那一句。</summary>
    static readonly string[] EggWords =
    {
        "累不累啊...", "还在点吗...", "拇指酸了吧...", "没东西的...别点了...", "100连打达成！",
    };

    int _eggHits;
    DateTime _eggLast = DateTime.MinValue;
    DateTime _eggUntil = DateTime.MinValue;

    /// <summary>正中那个空位被按了一下(只在设置首页算数)。</summary>
    void TapEggCenter()
    {
        var now = DateTime.Now;
        if ((now - _eggLast).TotalMilliseconds > EGG_GAP_MS) _eggHits = 0;
        _eggLast = now;
        _eggHits++;
        if (_eggHits % EGG_STEP != 0) return;

        // 第几档: 1~5 是一轮, 6 往上接着循环(120 下又是第一句)。
        int n = _eggHits / EGG_STEP;
        EggHintText.Text = EggWords[(n - 1) % EggWords.Length];
        _eggUntil = now.AddMilliseconds(EGG_HINT_MS);
        // 【日志只记数】那几句话是界面上的字, 不进日志(和"不记用户正文"一个规矩)。
        App.Log("彩蛋: 连打 " + _eggHits + " 下 -> 第 " + ((n - 1) % EggWords.Length + 1) + " 句");

        // 整轮达成(100 / 200 / 300 ... 下) -> 永久解锁土豪金 + 切过去。
        // 第二次整轮(200 下)走的是同一条路, 但 UnlockGold 里"已解锁"会早退, 不再写盘。
        if (n % EggWords.Length == 0)
        {
            UnlockGold();
            RefreshSettings();      // 皮肤那页它那一格得从"空"变成「土豪金」
        }
    }

    // ---------- 改值的小动作(改完立刻重画: 别的格子上写着当前值) ----------
    void SetPreview(bool on) { if (Host != null) { Host.Preview = on; RefreshSettings(); } }
    void SetKey9Num(bool on) { if (Host != null) { Host.Key9Num = on; RefreshSettings(); } }
    void SetSlide(SlideMode m) { if (Host != null) { Host.SlideMode = m; RefreshSettings(); } }

    // ---------- 建 / 拆页 ----------
    //
    // 【2.1.3: 场上只有一块面板】
    // 以前每往里走一层就往 Stage 里多摞一块 Border(旧的缩一点压暗, 中间再垫一层
    // 把整个 Stage 拍成图糊掉的毛玻璃), 于是"深层其实还压着好几层"在界面上看得见
    // —— 格子底板撤掉之后尤其明显。现在规矩是:
    //   · **同一时刻只有现在这一页被画出来**(Stage 里就一块 Panel);
    //   · 老页只留**数据**在 Pages 里, B 退回上一层时按它重建(见 ShowPage);
    //   · 要走动画就先给旧页拍张快照留在 Veil 上, 让它陪着演那 180 毫秒, 演完撤掉。
    // 以后想换别样的换页动画(推、淡、翻), 改 ShowPage 这一处 + RiseIn / SinkLeave
    // 那一对就够了, 导航和输入代码一个字都不用动。

    void BuildSettingsSide()
    {
        _set.Reset();
        _set.Stage = SetStage;
        _set.Active = () => Flipped;
        _set.Pick = _ => SettingsActivate();
        _set.Chrome = PaintChrome;
        // (2.1.1: 原来这里还要给右下角返回箭头旁边那行"翻回键盘"的小字写一句 ——
        //  那行字撤了, 箭头自己就说明意思了。)
        PushPage(_set, PageRoot(), false);
    }

    /// <summary>走进更深一页。animate=false = 建第一页 / 不带动画地重搭。</summary>
    void PushPage(LayerHost h, SetPage page, bool animate, int home = -1)
    {
        h.Pages.Add(page);
        ShowPage(h, animate, home, 1);
    }

    /// <summary>退回上一层: h.Pages 已经弹掉了, 这里负责把新露出来的那一页画出来。</summary>
    void PopPage(LayerHost h, bool animate) => ShowPage(h, animate, -1, -1);

    /// <summary>
    /// 把 <see cref="LayerHost.Top"/> 画成场上唯一那一页。
    ///
    /// **内容在这里重搭**(Build + Clamp): 退回来的时候, 你可能刚在更深的地方改过
    /// 这一页上写的值(主题、版本、绑到数字几……), 不重搭就会把旧值留在格子上。
    /// 这是"页面平级化"唯一一处要盯着的行为差别(原来重搭的是**所有**层)。
    ///
    /// **换过渡动画就改这里** —— 契约只有一句: 演完之后场上只剩这一页。
    /// </summary>
    /// <param name="home">进页后光标停哪格, -1 = 交给 <see cref="EntrySlot"/> 挑。</param>
    /// <param name="dir">这一场往哪边走: +1 = 往里钻一层, -1 = 退回上一层。
    /// 演"扑克牌"那套要拿它定转向 —— 进去和退出来必须是相反的。</param>
    void ShowPage(LayerHost h, bool animate, int home, int dir)
    {
        ResetCellScale(h);
        var page = h.Top;
        page.Cells = page.Build();
        page.Invalidate();              // 格子表变了, 摆法要重算
        Clamp(page);

        // 快照要在旧页摘掉**之前**拍: 拍的就是"刚才那一页长什么样"。
        // 大小量不出来(背面没翻过来之类)就拿不到, 那就直接换成静态切换, 不演。
        var snap = animate ? Snapshot(h) : null;
        DropPanel(h);

        h.Panel = MakePanel(page, out var grid);
        h.Grid = grid;
        h.Stage?.Children.Add(h.Panel);
        Panel.SetZIndex(h.Panel, 2);            // 毛玻璃垫在 1 上(见 EnsureVeil)
        FillPanel(h);
        // 进页 / 退页之后光标落在哪儿: 摇杆还拖着就跟着手走, 没碰才回正中。
        Land(h, page, EntrySlot(h, page, home));

        if (snap != null) { ShowLeave(h, snap, dir); RiseIn(h, h.Panel, dir); }
        else HideVeil(h);
    }

    /// <summary>把当前这一块面板从 Stage 里摘掉(整摞重来 / 已经换页了)。</summary>
    void DropPanel(LayerHost h)
    {
        if (h.Panel != null) h.Stage?.Children.Remove(h.Panel);
        h.Panel = null;
        h.Grid = null;
        h.Boxes = new List<Border>();
        h.Hosts = new List<FrameworkElement>();
    }

    /// <summary>
    /// 这一页的内容可能变了(别处改了设置 / 写了新短语): 按现在的真实状态重搭。
    /// 【只搭当前这一页】底下那些页已经没有 UI 了 —— 等你真的退回那一页,
    /// ShowPage 会照着最新的值重新搭一遍(见那里的说明)。
    /// </summary>
    void RefreshPage(LayerHost h)
    {
        var p = h.Top;
        if (p == null) return;
        p.Cells = p.Build();
        p.Invalidate();                 // 同上
        Clamp(p);
        if (h.Panel != null) FillPanel(h);
    }

    /// <summary>页里的内容变了(或者刚建好)就整个重画一遍格子。</summary>
    void FillPanel(LayerHost h)
    {
        var page = h.Top;
        var grid = h.Grid;
        grid.Children.Clear();
        var boxes = new List<Border>();
        var hosts = new List<FrameworkElement>();
        int slots = SlotsOf(page);
        for (int i = 0; i < slots; i++)
        {
            // 摆的是自己造的控件的那一格(见 SetCell.Custom, 比如窗口F 底下那条长条)。
            // 它和别的格子一样进 boxes —— 于是也能被光标选中、也能浮起来弹一下,
            // 只是没有 Act / Child, 按 A 自然什么也不干。
            var c0 = CellAt(page, i);
            Border b = c0?.Custom?.Invoke() as Border;
            b ??= MakeCellBox(h, page, c0, i);
            // 每格外面再套一层(浮力那层水波挂它身上; 格子里的变换留给"光标浮起 /
            // 按下去", 和九键那颗键与它的 _key9Host 是同一个规矩)。
            // **这一层不写尺寸**: 它跟着网格里那一格撑满, Border 照旧留自己的
            // 4 像素缝 —— 于是普通一格还是 112 见方、合成一块的还是照跨的那几行
            // 几列撑出来, 和套之前一格不差。
            var host = new Grid
            {
                RenderTransformOrigin = new Point(0.5, 0.5),
                Tag = LIFT_HOST,      // CellPop 抬 Z 序时要连它一起抬(见 LiftZ)
            };
            host.Children.Add(b);
            if (grid is Grid g)
            {
                // **摆在哪个矩形里, 摆法表说了算**(见 SetNav.TileOf)。
                // 普通一格就是 (i/3, i%3) 占 1×1; 合成一块的那些跨几行几列都写在表里,
                // 这里一句都不用特判 —— 长条、一大格、以后随便什么形状都是这四行。
                // (行 / 列 / 跨行 / 跨列挂在**外层**上 —— 位置是外层的事。)
                var t = SetNav.TileOf(page, i);
                Grid.SetRow(host, t.Row);          Grid.SetColumn(host, t.Col);
                Grid.SetRowSpan(host, t.Rows);     Grid.SetColumnSpan(host, t.Cols);
            }
            grid.Children.Add(host);
            boxes.Add(b);
            hosts.Add(host);
        }
        h.Boxes = boxes;
        h.Hosts = hosts;
        PaintPanel(h);
        h.Chrome?.Invoke();
    }

    /// <summary>
    /// 标题和页码点写的是**现在这一页**。它们挂在卡片外面: 卡片里那九格就是
    /// 九颗键那么大的 3×3, 标题挤进去就得把格子压扁。
    /// 页码点替换了原来那行"第 x / y 页 · LB / RB 翻页"的小字 —— 几页几个点,
    /// 第几页第几个亮, 不用字说。
    /// </summary>
    void PaintChrome()
    {
        var p = TopPage;
        if (p == null) return;
        SetTitle.Text = p.Title;
        PaintDots(SetDotsBar, PagesOf(p), p.Page);
    }

    /// <summary>底下那排页码点。只有一页就一个点都不摆。</summary>
    void PaintDots(StackPanel bar, int pages, int cur)
    {
        if (pages < 2)
        {
            bar.Children.Clear();
            return;
        }
        if (bar.Children.Count == pages)
        {
            for (int i = 0; i < pages; i++)
                ((Ellipse)bar.Children[i]).Fill = i == cur ? Accent : DotIdle;
            return;
        }
        bar.Children.Clear();
        for (int i = 0; i < pages; i++)
            bar.Children.Add(new Ellipse
            {
                Width = 7, Height = 7, Margin = new Thickness(3, 0, 3, 0),
                Fill = i == cur ? Accent : DotIdle,
            });
    }

    /// <summary>
    /// 外面那一层。里面只装一个 3×3 —— 每格就是九键上一颗键那么大(SET_CELL),
    /// 所以这一层正好是"九键那块 3×3"那么大的正方形。标题和页脚挂在外面
    /// (见 PaintChrome), 不然格子就得让位、被压扁。
    ///
    /// 【2.1.0 第四批: 这一层不再画框】以前它还描一圈细边(SET_PANEL_W)并留 2 的内边距,
    /// 于是九宫格外面多出一圈方框 —— 和卡片自己那道描边叠成两层, 而格子被压成 110,
    /// 跟九键那颗 112 的键差着 2, "九颗键变成九个设置格" 就还不算数。
    /// 边和边距撤掉之后: 这层正好 360×360, 里面九格**边长 112、缝 4、步长 120**,
    /// 和九键那九颗键一个像素都不差。
    /// 【2.1.1 再把底板也撤了】底板那一块 PanelBg 让九个格子看着像"被一个正方形
    /// 背景框住", 而九宫格那一面九颗键底下是没有板的。底下也不会有别的东西透出来:
    /// 2.1.3 起场上就只有这一页(见 ShowPage), 窗口F 那更是整套九键都被收掉。
    /// </summary>
    Border MakePanel(SetPage page, out Grid grid)
    {
        var panel = new Border
        {
            // 【不垫底板】2.1.1: 这一层以前刷一块 PanelBg, 于是九个格子外面又多出
            // 一个正方形背景框 —— 而九宫格那一面九颗键是**没有**这么一块板的。
            // 现在设置页 / 窗口F 就是"九颗键变成的九个格子", 底下什么都不垫。
            // 底下也不可能有别的东西透出来: 2.1.3 起同一时刻只有这一页被画出来,
            // F 那更是整套九键都被收掉(见 Render 里 Grid9 那一行)。
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(SET_PANEL_R),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        // 三列 + N 行: 不满九个的层(比如"JKL 位置"只有两格)也占满三行,
        // 卡片不会一会儿高一会儿矮。行数由摆法表推(见下面 SetNav.RowsOf)。
        // 行宽 / 列宽就写"一格 + 左右缝隙", 于是三格一排正好 360, 和九键那块一样大。
        grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (int i = 0; i < 3; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SET_CELL + SET_GAP * 2) });
        // 行数由摆法表里最深的那一行决定(见 SetNav.RowsOf)。长条**坐在格子里**,
        // 不是挂在格子下面另起一段 —— 它就是"7 8 9 三格合并成的那一格", 于是它和
        // 上面六格共用同一套坐标(第 3 行、横跨三列), 光标挪下去也是一个格子。
        // 同理, 铺满整个 3×3 的那一大格也让网格铺三行 —— 于是它正好是九键那块
        // 360×360; 只铺一行的话它就被压成一条扁条。
        //
        // 【2.1.31: 行数最少三行】这一摞**有多高**不该由"这一页有几格"决定 ——
        // 内容区(SetStage)是 Auto 的, 它上方是一条弹簧: 面板一矮弹簧就变长, 把整块
        // 内容往下推。而旧页那张照片是这个 Stage 的孩子, 会被一起推下去 —— 看着就是
        // "旧的一页先掉一截、再横着滑走"。恒按三行排, 每一页都占满九颗键那一块:
        // 换页 / 进层内容区一动不动, 格子也永远落在自己那一格上。
        // (真实每一页本来就是三行 —— 这条是把这条规矩钉死, 免得以后加一页短的就踩;
        //  uiprobe 的 SlideLevel 守着它。)
        int rows = Math.Max(3, SetNav.RowsOf(page));
        for (int i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SET_CELL + SET_GAP * 2) });

        panel.Child = grid;
        return panel;
    }

    /// <summary>一格。cell 为 null 就是"空位" —— 只留个淡淡的框占位, 不显示也不隐藏:
    /// 九个位置永远在同一处, 手柄靠方向记位置才不会乱。</summary>
    Border MakeCellBox(LayerHost h, SetPage page, SetCell cell, int i)
    {
        // 这一格占哪块矩形(见 SetNav.TileOf), 由此推出它的像素大小。
        // 「一格 = 一颗九键的键」: 1×1 就是 SET_CELL(112) 见方; 合成一块的 ——
        // 比如 3×3 那块 —— 就是三格连起来那么大(352), 于是它天然就是"几颗键并起来"。
        var t = SetNav.TileOf(page, i);
        double tileW = t.Cols * (SET_CELL + SET_GAP * 2) - SET_GAP * 2;
        double tileH = t.Rows * (SET_CELL + SET_GAP * 2) - SET_GAP * 2;
        bool big = t.MaxSide > 1;                  // 合过块的那一格
        var b = new Border
        {
            // **一格 = 一颗九键的键**: 边长 SET_CELL(112), 四面各留 SET_GAP(4) ——
            // 和九键那颗键(Box 的 Margin)一模一样。三格一排正好 120×3 = 360,
            // 也就是九键那块 3×3 的边长, 于是"设置里的格子"和"九键的键"在屏幕上
            // 一样大 —— 界面缩到 90% / 放大到 110%, 两边由同一个 LayoutTransform
            // 一起缩放, 大小关系不变。
            //
            // 【合过块的那格不写宽高】让它被网格按自己跨的那几行几列撑出来 ——
            // 写死 SET_CELL 的话它只剩 112 见方, 那"九格并一格"就白做了。
            Margin = new Thickness(SET_GAP),
            Background = cell == null ? Brushes.Transparent : Normal,
            BorderBrush = cell == null ? EmptyEdge : DimEdge,
            BorderThickness = new Thickness(1),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        if (!big) { b.Width = SET_CELL; b.Height = SET_CELL; }
        // 圆角: 方块那一档跟着九键那颗键走(_cellR), 圆形那一档按短边取 ——
        // 一格 112 见方就是正圆, 789 那条长条(352×112)就自己变成胶囊。
        ShapeBase(b, _cellR);
        if (cell == null) return b;

        double visFitW = 0, visFitH = 0;
        var g = new Grid();
        var sp = new StackPanel { Orientation = Orientation.Vertical,
                                  HorizontalAlignment = HorizontalAlignment.Center,
                                  VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            // 格子从 93×56 的扁条变成 112×112 的方块, 字也跟着大一号(14 → 16)。
            // 有些格子是**两个字一行**的(比如"收起之后"那四档: 复制 / 粘贴 / 回车),
            // TextBlock 认得换行符, TextAlignment 让每一行都居中。
            // 【合过块的那格是另一套】它装的是整段说明, 字号小一号(基准 13,
            // 放不下会自动再缩 —— 见 FitTitle)、不加粗、靠左排 ——
            // 加粗的 16 号字摆满一整块会糊成一片。
            FontSize = big ? SOLO_FS : 16,
            FontWeight = big ? FontWeights.Normal : FontWeights.SemiBold,
            Foreground = Fg,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = big ? TextAlignment.Left : TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            // 窗口F 那一格装的是一条快捷短语, 长短没个准 —— 不给上限的话
            // 一串长句子会横着溢出格子、盖到旁边那格上(TextBlock 自己不裁)。
            // 给足宽度让它**在格子里**折行(MaxHeight 掐到两行多一点), 再长就省略号。
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = tileW - (big ? 30 : 12),
            // 合过块的那格先不设 MaxHeight: 设了量出来的高度会被掐住,
            // 自动缩号那一趟就永远觉得"放得下"(见下面 FitTitle)。缩完再补上。
            //
            // 【普通格子给 CELL_TITLE_MAXH, 不是 62】2.1.25: 「收起之后」那四档是
            // 按**一步一行**写上去的(复制 / 粘贴 / 回车, 见 GameMode.Steps),
            // 16 号字行高约 21, 三行要 64 —— 掐在 62 上第三行正好被切掉, 于是后两档
            // 看着都"少一步", "复制+粘贴+回车"还跟"复制+粘贴"长得一模一样
            // (看着像功能被砍了)。现在给到 70, 装得下三行; 格子 112 高,
            // 底下那行小字也还有地方。
            MaxHeight = big ? double.PositiveInfinity : CELL_TITLE_MAXH,
        };
        // **定宽**: 只给 MaxWidth 的话 TextBlock 会照内容抻到最宽、一行到头才折,
        // 于是那整段说明全成了"一行撑到边"。写了 Width 它才知道在哪儿断行。
        if (big) title.Width = tileW - 30;
        sp.Children.Add(title);
        if (big)
        {
            // **字号自动缩**: 那一大格装的是一整段说明, 里面还带着几条路径 ——
            // 装在深一点的目录、或者哪天又加了一行, 写死的字号就会把最后一句切掉。
            // 先按基准字号量一次真排版, 塞不下就半号半号往下试, 直到放进格子。
            title.Text = cell.Title;
            visFitW = tileW - 30;
            visFitH = tileH - 28;
            FitTitle(title, visFitW, visFitH);
            title.MaxHeight = visFitH;
        }
        var note = new TextBlock
        {
            FontSize = 12, Foreground = Sub, Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = SET_CELL - 12, MaxHeight = 34,
        };
        sp.Children.Add(note);
        g.Children.Add(sp);
        // "现在生效的就是这一档": 右上角一枚**对勾小圆牌**。
        // 这个标记换了三版才定下来:
        //   ① 右上角 7 像素小圆点 —— 手柄离屏幕有段距离, 那么一个点既看不清
        //      也看不懂是什么意思, 还容易跟"光标停在哪一格"搞混(那是另一回事);
        //   ② 格子底下中央"使用中"三个小字 —— 意思明白了, 可 11 号字太小,
        //      隔远了还是糊;
        //   ③ 现在这个: 给足面积(24 见方的实心圆牌), 里面对勾。不写字也明白,
        //      而且它只占右上角一小块, 不跟正文抢地方。
        // 对勾的颜色用 OnAccent(按主题色亮度自己翻深/白), 不能写死成白色。
        var badge = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
            Background = Accent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 5, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,          // 别挡住"点这一格"
            Child = new Path
            {
                // 勾画在 24×24 的方块里, 笔画围出来的中心正好落在 12 —— 不写死
                // 尺寸的话 Path 会按几何自己的大小摆, 勾就偏到右下角去了。
                Width = 24, Height = 24, Stretch = Stretch.None,
                Data = Geometry.Parse("M7 12.5 L10.3 15.8 L17 8.4"),
                Stroke = OnAccent, StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            },
        };
        g.Children.Add(badge);
        // 左上角那枚小角标(窗口F 列表层写着 1~9: 这一格是哪个数字键)。
        // 设置页那些格子不写, 就整个收起来。
        var corner = new TextBlock
        {
            FontSize = 12, Foreground = Sub,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(7, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        g.Children.Add(corner);
        b.Child = g;
        b.Tag = new CellVis { Title = title, Note = note, Badge = badge, Corner = corner,
                             FitW = visFitW, FitH = visFitH };

        int slot = i;
        // 悬停出小说明那一套**整层停用**(2.0.1 起), 东西留着没拆 ——
        // 想加回来就把下面这一行放开(以及 Land / PopPage / FlipPage 里那几处)。
        //   b.MouseEnter += (_, _) => ArmSetTip(slot, true);
        b.PreviewMouseDown += (_, e) =>
        {
            if (h.Anim) return;
            var p = h.Top;
            if (p == null || !Landable(p, slot)) return;
            Land(h, p, slot);
            // 右键走 Pick2(窗口F 里是"编辑这一格") —— 手柄上有"长按 A", 鼠标
            // 没有长按这一手, 得给它单独留一条往里走的路。
            if (e.RightButton == MouseButtonState.Pressed) h.Pick2?.Invoke(slot);
            else h.Pick?.Invoke(slot);
        };
        return b;
    }

    /// <summary>合过块的那格自动缩号用: 记下量的时候用的宽高, 文字换了能照它再缩一次。</summary>
    sealed class CellVis
    {
        public TextBlock Title, Note, Corner;
        public UIElement Badge;
        public double FitW, FitH;      // 0 = 不用缩(普通一格)
    }

    void PaintPanel(LayerHost h)
    {
        var page = h.Top;
        var boxes = h.Boxes;
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            var vis = b.Tag as CellVis;
            var cell = CellAt(page, i);
            // 【不再有"被压着的层"】2.1.3: 以前 `top` 是"是不是最上面那一层"——
            // 压在下面的层不画光标(它整个被上面的纸盖着)。现在场上永远只有这一页,
            // 它就是最上面那一页, 于是这一问没有了。
            bool cursor = i == page.Sel;
            // 空位也要跟着高亮 —— 光标挪到哪儿都是"这一格被选中了", 有没有内容
            // 只决定按 A 干不干活, 不决定它亮不亮。
            if (cell == null)
            {
                b.BorderBrush = cursor ? ActiveEdge : EmptyEdge;
                b.BorderThickness = new Thickness(cursor ? 2 : 1);
                // 长条现在是**一格真格子**(带 Custom, 见窗口F 深层), 走下面那条路;
                // 这儿剩下的就是 4 / 6 那种真空位, 不填底色。
                b.Background = cursor ? Lit : Brushes.Transparent;
                b.Opacity = 1;
                continue;
            }
            bool off = cell.IsOff();
            bool on = !off && cell.IsOn();

            b.BorderBrush = cursor ? ActiveEdge : off ? EmptyEdge : DimEdge;
            b.BorderThickness = new Thickness(cursor ? 2 : 1);
            b.Background = cursor ? Lit : Normal;
            b.Opacity = off ? 0.45 : 1;
            if (vis == null) continue;
            if (vis.Title != null)
            {
                // 文字变了(比如输入习惯条数、绑到数字几)就再缩一次 ——
                // 长一点的串可能就得多折一行, 照旧的字号会溢出格子。
                if (vis.Title.Text != cell.Title)
                {
                    vis.Title.Text = cell.Title;
                    if (vis.FitH > 0) FitTitle(vis.Title, vis.FitW, vis.FitH);
                }
                vis.Title.Foreground = cursor ? FgHot : off ? Sub : Fg;
            }
            if (vis.Note != null)
            {
                vis.Note.Text = cell.Note;
                vis.Note.Foreground = cursor ? ActiveEdge : Sub;
            }
            // 左上角那个角标(窗口F 写着 1~9): 有没有内容由页面自己给, 没有就收起来
            if (vis.Corner != null)
            {
                string c = cell.Corner?.Invoke() ?? "";
                vis.Corner.Text = c;
                vis.Corner.Foreground = cursor ? ActiveEdge : Sub;
                vis.Corner.Visibility = c.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            // 右上角那枚对勾牌要不要露脸, 只看"这一档是不是正在用的那个"(on),
            // 跟光标在哪一格没关系 —— 那是另一件事。
            if (vis.Badge != null) vis.Badge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
        // 【不用再挑层】以前只有最上面那一层跟着光标浮/落(底下那些层正被压暗缩小,
        // 哪一格都不该浮起来); 现在场上就这一页, 它就是要浮的那一页。
        SyncCellPop(h, page, boxes);
    }

    /// <summary>
    /// 光标换格 = 换一格浮起来。老的那颗归位、新那颗浮起来 —— 和九键上"选中的
    /// 那颗始终浮着"是同一回事。
    /// 认的是"是不是同一肉眼可见的那一格", 所以重搭了面板(|= new Border)也会重新浮。
    /// </summary>
    void SyncCellPop(LayerHost h, SetPage page, List<Border> boxes)
    {
        Border hot = page.Sel >= 0 && page.Sel < boxes.Count ? boxes[page.Sel] : null;
        if (ReferenceEquals(hot, h.Cur)) return;
        if (h.Cur != null) CellRest(h.Cur);
        h.Cur = hot;
        if (hot != null) CellPop(hot, page, page.Sel);
    }

    /// <summary>把光标落到这一格上。</summary>
    void Land(LayerHost h, SetPage p, int i)
    {
        p.Sel = i;
        PaintPanel(h);
    }

    static int FirstLandable(SetPage p) => SetNav.FirstLandable(p);

    /// <summary>**回中**: 光标默认停在哪儿(算法在 SetNav.HomeSlot, fprobe 第 26 组守着)。</summary>
    static int HomeSlot(SetPage p) => SetNav.HomeSlot(p);

    /// <summary>
    /// 进一页 / 退一页 / 翻一页之后, 光标该落在哪一格。
    ///
    /// 【2.1.3: 摇杆正拖着就不回中】以前这三种场合一律回正中, 于是:
    ///   · 摇杆拖在 3 上按 A 进深层 -> 光标先跳到正中 5、再跳回你手里的 3, 中间闪一下;
    ///   · LB / RB 翻页时手没离开摇杆 -> 那一页也是先回中再跟手。
    /// 现在规矩改成一句话: **手还在摇杆上就听摇杆的, 没碰摇杆才回正中。**
    /// 落不到摇杆指的那一格(那一页没这么多格 / 那格是空位), 就还是回正中 ——
    /// 宁可落在正中, 也不能为了让不开的格子把光标卡在半路。
    ///
    /// <param name="home">这一页想指定的默认格(&lt; 0 就是按算法算出来的正中)。</param>
    /// </summary>
    static int EntrySlot(LayerHost h, SetPage p, int home = -1)
    {
        double sx = h.StickX, sy = h.StickY;
        // 光看 StickNav 不够: 动画演的那 180 毫秒里 LayerStick 被挡着不能改它,
        // 于是"你早松手了、它还记着推着"这种情形是真会发生的。位置是每帧都记的,
        // 所以按位置再量一次死区才是准的。
        if (sx * sx + sy * sy >= InputCore.DZ_N * InputCore.DZ_N)
        {
            int i = SetNav.StickSlot(p, InputCore.PointTo9(sx, sy) - 1);
            if (i >= 0 && SetNav.Landable(p, i)) return i;
        }
        return home >= 0 ? home : HomeSlot(p);
    }

    /// <summary>页码 / 光标别跑到范围外。</summary>
    static void Clamp(SetPage p) => SetNav.Clamp(p);

    /// <summary>
    /// 一格自己的变换层: 缩放 + 旋转。"光标落在这一格浮起来"和"按 A 按下去缩一下"
    /// **共用**它 —— 两套都动 ScaleX/Y, 各开一条动画就会互相打断。
    /// 和九键那颗键用同一副 TransformGroup(见 FxPop): [0]=缩放 [1]=旋转。
    /// </summary>
    // SET_POP 已搬进 FxKit.ScalePop(1.14)      // 选中的那一格放大多少(和九键焦点那颗一样)
    /// <summary>那一晃摆多少度。同上: 按格子大小反比缩放, 一大格才不会像整块在转。</summary>
    // CELL_WOBBLE 已搬进 FxKit.WobDeg(4.5)

    /// <summary>合过块的那格最长那条边的像素长度(1 格 = SET_CELL, 3 格 = 352)。</summary>
    static double TilePx(SetPage p, int slot) =>
        SetNav.TileOf(p, slot).MaxSide * (SET_CELL + SET_GAP * 2) - SET_GAP * 2;

    /// <summary>这一格"浮起来"放大多少。**按格子大小反比缩放**, 不写死两种倍数 ——
    /// 以后加任何形状的合块都不用再来这儿调一次。</summary>
    static double PopOf(SetPage p, int slot) => 1 + (FxKit.ScalePop - 1) * SET_CELL / TilePx(p, slot);

    /// <summary>那一晃摆多少度(见 CELL_WOBBLE)。</summary>
    static double WobOf(SetPage p, int slot) => FxKit.WobDeg * SET_CELL / TilePx(p, slot);

    /// <summary>
    /// 把一段长文字**缩到放得进格子**: 按基准字号量一次真排版, 塞不下就半号半号
    /// 往下试。写死字号的话, 装在深一点的目录(路径长、折的行多)或者哪天又加了
    /// 一行文案, 最后一句就被切掉了 —— 那一格又不会滚动。
    /// 量的是真实排版(微软雅黑、真实可用宽), 不是按字数估的。
    /// </summary>
    static void FitTitle(TextBlock t, double w, double h)
    {
        var box = new Size(Math.Max(10, w), double.PositiveInfinity);
        double fs = SOLO_FS;
        t.FontSize = fs;
        t.InvalidateMeasure();
        t.Measure(box);
        while (t.DesiredSize.Height > h && fs > SOLO_FS_MIN)
        {
            fs -= 0.5;
            t.FontSize = fs;
            t.InvalidateMeasure();
            t.Measure(box);
        }
    }

    bool CanNav(LayerHost h) => !h.Anim && h.Count > 0 && h.Panel != null && h.Active();

    /// <summary>十字键上下: 三列, 所以上下是 ±3。</summary>
    public void SettingsNav(int d) => MoveSel(_set, d * 3);

    /// <summary>十字键左右。</summary>
    public void SettingsNavH(int d) => MoveSel(_set, d);

    void MoveSel(LayerHost h, int d)
    {
        if (!CanNav(h)) return;
        var p = h.Top;
        // 场上就一格(「关于」那一大块): 十字键也没处可去 —— 直接吃掉。
        if (SetNav.SlotsOf(p) <= 1) return;
        int slots = SlotsOf(p);
        // 按**行列**挪, 不按"第几格 +3 / +1": 有长条的那层, 第三行三格是同一条,
        // 加加减减会把 5 号位往下的那一格算成"第 7 格"再绕回第 1 格 —— 而它其实
        // 该落在长条上。走行列就自然了: 往下推一行, 落点由 SlotAt 定(见 SetNav)。
        int rows = SetNav.RowsOf(p);
        int dr = Math.Abs(d) >= 3 ? Math.Sign(d) : 0;      // 上下(十字键上下是 ±3)
        int dc = dr == 0 ? Math.Sign(d) : 0;               // 左右
        int r0 = SetNav.RowOf(p, p.Sel), c0 = SetNav.ColOf(p, p.Sel);
        for (int k = 1; k <= rows + 2; k++)
        {
            int rr = r0 + dr * k, cc = c0 + dc * k;
            // 走出这一层了: 有多页就翻过去(和"更多标点"一个路子)
            if ((rr < 0 || rr >= rows || cc < 0 || cc > 2) && p.Nine && PagesOf(p) > 1)
            {
                FlipPage(h, (rr < 0 || cc < 0) ? -1 : 1);
                return;
            }
            int i = SetNav.SlotAt(p, ((rr % rows) + rows) % rows, ((cc % 3) + 3) % 3);
            if (i < 0 || i >= slots) continue;
            if (Landable(p, i)) { Land(h, p, i); return; }
        }
    }

    /// <summary>
    /// 左摇杆: **和九键选键完全同一套** —— 推哪个方向就选哪一格, 手一松回中
    /// 就落回正中那一格。九宫格里找东西全靠它, 不用一格格挪过去。
    /// </summary>
    public void SettingsStick(double nx, double ny) => LayerStick(_set, nx, ny);

    /// <summary>
    /// 左摇杆: **和九键选键完全同一套** —— 推哪个方向就选哪一格, 手一松回中
    /// 就落回正中那一格。九宫格里找东西全靠它, 不用一格格挪过去。
    /// </summary>
    void LayerStick(LayerHost h, double nx, double ny)
    {
        // 【位置必须抢在 CanNav 之前记】换页动画演的那 180 毫秒里这一摞不接操作,
        // 但 EntrySlot 要问"手还拖着吗" —— 那时候读到的必须是**最近一帧**的真值,
        // 否则它会拿着动画开始前的位置说话。
        h.StickX = nx; h.StickY = ny;
        if (!CanNav(h)) return;
        var p = h.Top;
        double r = Math.Sqrt(nx * nx + ny * ny);
        int i;
        if (r >= InputCore.DZ_N)
        {
            h.StickNav = true;                          // 推着: 推哪儿选哪儿
            // 长条那层: 往下推(7 / 8 / 9 哪个方向都算)选中的是底下那条 ——
            // 它虽然按 A 没反应, 但位置是占着的, 高亮和浮起都跟别的格子一样。
            i = SetNav.StickSlot(p, InputCore.PointTo9(nx, ny) - 1);
        }
        else if (h.StickNav)
        {
            // 松手**这一下**落回正中 —— 和九键"手一松回到 5 号键"同一个手感。
            // 只认这一下: 手一直没碰摇杆的时候不能每帧都往正中拽,
            // 否则十字键一格格挪过去的成果会被静止的摇杆当场顶掉。
            h.StickNav = false;
            // 松手落**正中** —— 走 HomeSlot(和进层 / 退上一层同一个规矩),
            // 不写死 4: 首页那种"三列往下排"的层不一定是九格, 写死会落到边上。
            i = HomeSlot(p);
        }
        else return;                                    // 摇杆一直没动: 别碰光标
        if (i < 0 || i >= SlotsOf(p)) return;           // 这一格不存在(比如本页只有三格)
        if (!Landable(p, i)) return;
        if (i != p.Sel) Land(h, p, i);
    }

    /// <summary>
    /// A: 有下一层就浮出来, 没有就把这件事办了。
    /// **两样都有的时候按序做完**: 先把这一格代表的值定下来(Act), 再浮出下一层 ——
    /// 比如「版本 → 预输入」是"切成预输入版" + "接着挑收起之后做哪几步"，
    /// 一步到位，不用先按一下生效再按一下进去。
    /// </summary>
    public void SettingsActivate() => LayerActivate(_set, RefreshSettings);

    /// <summary>
    /// A: 有下一层就浮出来, 没有就把这件事办了。
    /// **两样都有的时候按序做完**: 先把这一格代表的值定下来(Act), 再浮出下一层 ——
    /// 比如「版本 → 预输入」是"切成预输入版" + "接着挑收起之后做哪几步"，
    /// 一步到位，不用先按一下生效再按一下进去。
    /// </summary>
    void LayerActivate(LayerHost h, Action refresh)
    {
        if (!CanNav(h)) return;
        var p = h.Top;
        var cell = CellAt(p, p.Sel);
        // 【彩蛋】设置首页正中那个空位被按了一下。**必须插在下面那行"什么都不干
        // 就退出"之前** —— 空位走的就是 cell == null 这条路, 挪到后面就永远数不到
        // 20 下。只在设置那一摞的最底下那页算数(窗口F 也是九宫格, 别跟着一起数)。
        if (cell == null && ReferenceEquals(h, _set) && h.Count == 1 && p.Sel == HomeSlot(p))
            TapEggCenter();
        if (cell == null || cell.IsOff()) return;
        // 啥也不干的格子(空位、只给人看的长条): 按了就是没按, **连重画都别起** ——
        // 起了的话按一下 A 整页会闪一下, 看着像真干了点什么。
        if (cell.Act == null && cell.Child == null) return;
        var child = cell.Child?.Invoke();
        if (child != null)
        {
            cell.Act?.Invoke();
            PushPage(h, child, true);
            return;
        }
        cell.Act?.Invoke();
        refresh?.Invoke();
    }

    public bool SettingsBack(bool toRoot) => LayerBack(_set, toRoot, RefreshSettings);

    /// <summary>
    /// B。toRoot=false 退一层; toRoot=true(长按)一路退回最底下那页。
    /// 已经在最底下那页了就返回 false —— 让外面去把这一摞整个收掉。
    /// </summary>
    bool LayerBack(LayerHost h, bool toRoot, Action refresh)
    {
        if (h.Anim) return true;                // 动画中: 这次按键吃掉, 别叠加
        if (h.Count <= 1) return false;
        int target = toRoot ? 1 : h.Count - 1;
        // 中间那几页直接丢(它们没有 UI 了, 不像以前还得逐层从 Stage 里摘下来),
        // 只把最后露出来的那一页搭出来、演一次退回去的动画。
        h.Pages.RemoveRange(target, h.Count - target);
        // **顺序不能反**: 得先把新页面搭好(PopPage 里会重搭内容 + 重画),
        // 再让外面刷新 —— 否则 refresh 会把新内容画进"还没摘掉的那块旧面板"里。
        PopPage(h, true);
        refresh?.Invoke();
        ResetLayerCursor(h);
        return true;
    }

    /// <summary>现在叠了几层(1 = 只有首页)。外面拿它决定"B 按下要不要先等着"。</summary>
    public int SettingsDepth => _set.Count;

    /// <summary>LB / RB: 翻页。</summary>
    public void SettingsPage(int d) => LayerPage(_set, d);

    void LayerPage(LayerHost h, int d)
    {
        if (!CanNav(h) || d == 0) return;
        FlipPage(h, d);
    }

    /// <summary>
    /// LB / RB 翻一页。dir = +1 往下一页(RB), -1 往上一页(LB) —— 同时它也是
    /// 扑克牌那套动画的转向: 往下一页顺着"往里走"的方向转。
    /// <para>【以前这儿是全 Audience 唯一没有换页动画的地方】格子当场换掉, 除了
    /// 框外那对小牌自己撅一下, 屏幕上一丁点"翻过去了"的意思都没有。现在它
    /// 和其他换页走同一条路: 拍快照 → 演 → 收。</para>
    /// <para>【唯独这里不锁操作】见 <see cref="RiseIn"/> 的 hold 参数 —— 翻页是能
    /// 连着按的(按住 RB 会叠 coworker 连发), 锁 260 毫秒就成了每按一次要等
    /// 小半秒才认下一次, 手感全毁。动画照演, 后一次直接把没演完的那一场顶掉。</para>
    /// </summary>
    void FlipPage(LayerHost h, int dir)
    {
        var p = h.Top;
        int pages = PagesOf(p);
        if (pages <= 1) return;
        // 【上一场还没演完就别再叠一张快照】连着按 RB、或者摇杆一直推在那一页的
        // 边缘上时, 这里会被连着叫 —— 每叫一次就 RenderTargetBitmap 一整页的图,
        // 又贵又看得眼花。场上已经有旧页快照在演着, 就跳过这一次的动画,
        // 让内容直接换掉: 连着翻本来也不该一张一张慢慢演。
        // 平移那一套连按时, 除了不叠第二张快照, 还得照演进门 —— 见下面 RiseIn 那句。
        bool busy = h.Veil != null && h.Veil.Visibility == Visibility.Visible;
        bool slide = Sliding;
        var snap = busy ? null : Snapshot(h);
        p.Page = ((p.Page + dir) % pages + pages) % pages;
        // 翻页之后光标落哪儿, 和刚进这一页同一个规矩(见 EntrySlot):
        // 手还在摇杆上就跟着手走 —— 以前一律回中, 拖着摇杆翻页会看见光标在正中闪一下。
        p.Sel = EntrySlot(h, p);
        ResetCellScale(h);          // 上一次"按下去"可能停在某格上, 换页前收干净
        FillPanel(h);
        if (snap != null) ShowLeave(h, snap, dir); else HideVeil(h);
        // 【连着翻也要演进门, 只是不叠第二张快照】以前 busy 时连 RiseIn 都不喊,
        // 于是面板身上上一场没走完的那条动画继续慢慢往回滑 —— 而它这会儿已经换成
        // 下一页的内容了, 看着就是"下一页先在框外露出一大截, 再慢慢滑进来"。
        // 现在照演: ComeIn 会让它从**当前位置接着滑到位**(见 ComeIn 里 slide 那一支),
        // 不再被拽回框外从头冲。默认 / 旋转那两套的起点本来就在框里, 保持原样不演。
        if (snap != null || slide) RiseIn(h, h.Panel, dir, hold: false);
    }

    /// <summary>翻到背面(或者别处改了设置)之后, 按现在的真实状态重画当前这一页。</summary>
    void RefreshSettingsSide()
    {
        if (Host == null) return;
        RefreshPage(_set);
        // （这里原来是 ArmSetTip —— 悬停出小说明那一套，2.0.1 起整层停用。）
    }

    /// <summary>给外面(比如刚清完输入习惯)用的重画入口。</summary>
    public void RefreshSettings() => RefreshSettingsSide();

    /// <summary>设置面刚翻过来 / 每次重新进来: 光标回正中间那一格。</summary>
    public void ResetSettingsCursor() => ResetLayerCursor(_set);

    void ResetLayerCursor(LayerHost h)
    {
        if (h.Count == 0) return;
        Land(h, h.Top, EntrySlot(h, h.Top));
    }

    // ================= 窗口F: 那一摞页(2.1.1 搭的, 2.1.3 改成平级页) =================
    //
    // 长按九键上「快捷」那一格开出来。它和设置页**共用**上面那一整套代码:
    // 一格 = 一颗九键的键(112 + 缝 4)、摇杆推哪儿选哪儿、松手回中、LB / RB 翻页、
    // 换页时旧页留张快照陪着演一下。差别只有两处:
    //   · 它摆在**正面的键盘区**(QuickStage), 不是背面那张卡;
    //   · 三页各装什么(见下面三个 Page), 以及列表页左上角那排 1~9。
    //
    // 【位置就是数字】第 1 页那九格就是数字键 1~9 —— 写在哪一格, 长按数字几就发
    // 哪一条。不排序、删掉一条也不把后面的往前挪: 手柄是靠方向记位置的,
    // 位置天天在动就记不住。
    //
    // 一共 5 页 × 9 格 = 45 条。满了还往里塞的, 存盘时就地丢掉(见 QuickStore.Load)。

    /// <summary>深层正在改哪一格(绑数字层也要用它)。</summary>
    void EnsureVeil(LayerHost h)
    {
        if (h.Veil != null) return;
        h.VeilImg = new Image
        {
            Stretch = Stretch.Fill,
            // 【用共享的那一个实例, 别自己 new】平移那一场会把这层糊摘掉、演完再
            // 挂回去(见 SlideFlatOn / SlideFlatOff), 挂回去的就是 VeilBlur ——
            // 这里要是自己 new 一个, 摘一次之后前后就不是同一个东西了(参数虽一样,
            // 但两个实例各管一份, 以后改参数会漏改一处)。
            Effect = VeilBlur,
        };
        var shade = new Border { Background = VeilBrush };
        var g = new Grid();
        g.Children.Add(h.VeilImg);
        g.Children.Add(shade);
        h.Veil = new Border
        {
            Child = g, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            // 【2.1.31: 改成撑满整个 Stage, 不再恒按"九颗键那一块"360×360 摆】
            // 快照拍的就是整个 Stage, 而 Veil 以前固定 360×360 —— 于是设置那一摞
            // (Stage 384 宽)的照片被横向压掉 6%, 旧的一页一出场就比新的一页窄一截,
            // 看着像"缩了一下又滑走"。现在和 Stage 一样大, 照片 1:1 摆在原处,
            // 和快捷短语那一摞(Stage 正好就是页面那一块)完全一样。
            // (以前这里写死 360 是为了别让毛玻璃从卡片上下边探出来 —— 那时 Stage
            //  比卡片高; 现在 Stage 就是内容区那一块, 撑满不会探出去。)
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        if (h.Stage == null) return;
        h.Stage.Children.Add(h.Veil);
        // 毛玻璃是一块**矩形**, 而它盖着的那张卡片是圆角的 —— 不裁的话,
        // 从第二层起就能看见卡片四个圆角外面各多出一块"直角的小透明角",
        // 像是底下又垫了一整层方板子。按卡片的圆角裁掉这四个角。
        // 【钩子只挂一次】窗口F 每开一次就重搭一回(那一摞是现搭的), 而设置那一摞
        // 只在启动时搭一次 —— 不记一笔的话, F 开关十次就挂上十个 SizeChanged,
        // 每次改尺寸都要白裁十遍(顺带把已撤掉的那几摞的引用一直拽着不释放)。
        if (!h.VeilHooked)
        {
            h.VeilHooked = true;
            h.Stage.SizeChanged += (_, _) => ClipVeil(h);
        }
        ClipVeil(h);
    }

    /// <summary>把毛玻璃裁成卡片那个圆角矩形(尺寸就按卡片自己的来)。</summary>
    void ClipVeil(LayerHost h)
    {
        if (h.Veil == null) return;
        double w = h.Veil.ActualWidth > 1 ? h.Veil.ActualWidth : SET_CARD;
        double hh = h.Veil.ActualHeight > 1 ? h.Veil.ActualHeight : SET_CARD;
        h.Veil.Clip = new RectangleGeometry(new Rect(0, 0, w, hh), SET_PANEL_R, SET_PANEL_R);
    }

    /// <summary>
    /// 把这一页现在长什么样拍成一张图(快照自己不算在内)。尺寸为 0 就放弃。
    /// <para>【2.1.32: 改走 ShotPage】以前是直接 Render 这个舞台 —— 那样拍出来的图会
    /// 带着舞台"在它爹里的位置", 于是旧的一页天生比真身低一截(详见 <see cref="ShotPage"/>)。
    /// 现在和正面那一摞走同一条路: 拍整张窗口, 再裁出舞台这一块。</para>
    /// </summary>
    BitmapSource Snapshot(LayerHost h)
    {
        if (h.Stage == null) return null;
        return ShotPage(h.Stage, h.Veil);
    }

    /// <summary>
    /// 把旧页那张快照摆上来替它站着, 然后演"出门"。
    /// **必须立刻满不透明**: 旧页刚刚被摘掉, 这张图得无缝顶上去; 以前那層还是
    /// 可见的真身, 慢慢淡入才有意义(换页的当下场上等于空了一帧, 会闪一下)。
    /// </summary>
    void ShowLeave(LayerHost h, BitmapSource snap, int dir)
    {
        if (snap == null) { HideVeil(h); return; }
        EnsureVeil(h);
        // 【照片槽不许自己决定舞台有多高】槽里那张 Image 没有写死尺寸, 于是它按
        // **位图有多少像素**要地方 —— 而照片是从整张窗口裁下来的(见 ShotPage),
        // 像素数比舞台的布局尺寸大一个缩放倍数的零头(384 vs 360)。槽一出现就把
        // 舞台撑高 24, 那一页被居中往下推 12; 照片演完撤走, 舞台缩回 360, 那一页
        // 又弹回去 —— 用户看见的就是"平移到位了, 又往上挪一小段才停"。
        // 槽的大小写成舞台现在的大小: 照片照样铺满, 但撑不动任何东西。
        // (正面那一路的 FaceVeil 早就是这么干的, 见 ShowFacePhoto。)
        if (h.Stage != null && h.Stage.ActualWidth > 1 && h.Stage.ActualHeight > 1)
        {
            h.Veil.Width = h.Stage.ActualWidth;
            h.Veil.Height = h.Stage.ActualHeight;
        }
        ClipVeil(h);
        h.VeilImg.Source = snap;
        h.VeilImg.Visibility = Visibility.Visible;
        Panel.SetZIndex(h.Veil, 1);             // 新页那块压在 2 上(见 ShowPage)
        h.Veil.Visibility = Visibility.Visible;
        // 上一回演到一半就被打断了的话, 变换和透明还停在半路上 —— 先归位:
        // 不然这一张会从"上一张的收尾姿势"接着演。(连着按 RB 翻页时天天发生。)
        var (sc, tr, ro) = Parts(h.Veil);
        ResetParts(sc, tr, ro);
        h.Veil.BeginAnimation(OpacityProperty, null);
        h.Veil.Opacity = 1;
        SinkLeave(h, dir);
    }

    void HideVeil(LayerHost h)
    {
        if (h.Veil == null) return;
        // 已经在 hidden 的就别再叠一个渐隐动画上去了 —— 那是给"正在淡出"那一回用的,
        // 这里只是把它收起来。
        h.Veil.BeginAnimation(OpacityProperty, null);
        h.Veil.Opacity = 0;
        h.Veil.Visibility = Visibility.Collapsed;
        h.VeilImg.Source = null;      // 别一直拽着那张位图(每次换页都有一张新的)
    }

    // ---------- 一格的"小说明"(光标停一会儿才弹) ----------
    // **2.0.1 起整层停用**: 九个格子每一个都想说话, 停哪儿都弹一块, 反而烦。
    // 东西一行没拆 —— 想加回来只要把这个开关改成 true(以及放开 MakeCellBox 里
    // 那个 MouseEnter)。
    // **为什么是个开关而不是把调用点删了**: 上一版只摘了鼠标那几个调用点, 可
    // TickSetTip 自己每帧还会"看一眼光标在哪儿就 Arm 一下"(下面那行 _tipIdx != p.Sel),
    // 于是手柄把光标停在任意一格上, 停够一会儿照样弹 —— 关掉提示得先关掉这一条,
    // 光删鼠标那一路是关不干净的。
    // (用 static readonly 而不是 const: 留着下面那些代码还能编到, 哪天想加回来
    //  把这里改成 true 就行, 不用重新写一整段。)
    static readonly bool SET_TIP_ON = false;

    /// <summary>认住一格, 开始计时。手柄走光标和鼠标悬停走的是同一条路。</summary>
    void ArmSetTip(int idx, bool byMouse = false)
    {
        _tipMouse = byMouse;
        if (_tipIdx != idx)
        {
            _tipIdx = idx;
            _tipAt = DateTime.Now;      // 换了格就从头计时, 不接着上一格算
        }
        _tipShown = false;
        HideSetTip();
    }

    void HideSetTip()
    {
        _tipShown = false;
        if (SetTipBox != null) SetTipBox.Visibility = Visibility.Collapsed;
    }

    void TickSetTip()
    {
        if (!SET_TIP_ON) { HideSetTip(); return; }   // 整层停用(见 SET_TIP_ON 的说明)
        if (!Flipped || _setStack.Count == 0)
        {
            _tipIdx = -1; HideSetTip(); return;
        }
        var p = TopPage;
        if (!_tipMouse && _tipIdx != p.Sel) ArmSetTip(p.Sel);
        if (_tipShown || _tipIdx < 0) return;
        if ((DateTime.Now - _tipAt).TotalMilliseconds < SET_TIP_DELAY) return;
        var cell = CellAt(p, _tipIdx);
        string s = cell?.Tip?.Invoke() ?? "";
        if (s.Trim().Length == 0) { _tipShown = true; return; }   // 没说明就不弹空框
        var boxes = _set.Boxes;
        if (_tipIdx >= boxes.Count) return;
        ShowSetTip(boxes[_tipIdx], s);
    }

    void ShowSetTip(Border row, string text)
    {
        SetTipText.Text = text;
        SetTipBox.Visibility = Visibility.Visible;
        SetTipBox.UpdateLayout();     // 得先量出真实高度才知道摆哪儿、要不要翻到下面
        double bw = SetTipBox.ActualWidth, bh = SetTipBox.ActualHeight;
        var pt = row.TransformToVisual(SetTipLayer).Transform(new Point(0, 0));
        double x = Math.Min(pt.X + 18, Math.Max(0, SetTipLayer.ActualWidth - bw - 4));
        double y = pt.Y - bh - 6;                       // 先试着摆在这一格的上边
        if (y < 2) y = pt.Y + row.ActualHeight + 6;     // 顶到头了就翻到格子的下边
        y = Math.Min(y, Math.Max(0, SetTipLayer.ActualHeight - bh - 2));
        Canvas.SetLeft(SetTipBox, x);
        Canvas.SetTop(SetTipBox, y);
        _tipShown = true;
    }

    /// <summary>输入习惯那一栏的现况(每次重画时才现算, 不印在页面上)。</summary>
    string UseSummary()
    {
        int mine = T9Engine.MineCount;
        string s = T9Engine.UseCount > 0
            ? $"已经记住 {T9Engine.UseCount} 条"
              + (mine > 0 ? $"　其中你自己拼的词 {mine} 条" : "")
            : "还没有记住任何一条";
        if (Config.UseBroken)
            s += "　(本地那份读不出来, 这次先当没学过)";
        return s;
    }

    /// <summary>设置页显示用的"当前唤醒键"(每次现问, 改过之后跟着变)。</summary>
    string WakeKeys() => Host?.WakeKeys() ?? "L3 + R3";

    // (2.1.1 删掉了 static Button Btn(...) —— 那是 2.1.0 及更早那版窗口F 顶上
    //  「添加到短语 / 选择快捷键 / 保存」三个按钮的制造器。现在窗口F 是三层九宫格,
    //  格子统一走 SetNav.Cell, 不再有这种单独的小按钮, 留着就是死代码。)

    /// <summary>
    /// 给一颗长条键套上三段式贴图背景: 返回的 Grid 里, 贴图在最底层,
    /// 原来的内容(文字)盖在上面。贴图层和键是一对一的, 挂进 _skins,
    /// 之后每帧 Paint 按选中态换色。
    /// </summary>
}
