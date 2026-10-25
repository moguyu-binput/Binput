using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;


namespace Binput;

/// <summary>
/// 内容区宽度 -> 框的宽度(换算见 OverlayWindow.FrameW)。给 XAML 那条绑定用的。
/// 以前这个换算是 C# 手算的, 而且**写了两遍**(无动画那一路一遍、有动画那一路又
/// 一遍)—— 现在只剩 FrameW 一处, 谁要改框的留白只改一个数, 六个元素一起跟着变。
/// 【必须是顶层类, 不能嵌在 OverlayWindow 里】XAML 里引用嵌套类要写
/// `OverlayWindow+FrameWConv`, 那种写法又脆又不直观。
/// </summary>
public class FrameWConv : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double w ? OverlayWindow.FrameW(w) : Binding.DoNothing;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException("框的宽度是从内容区宽度算出来的, 不能反过来写。");
}

public partial class OverlayWindow
{
// ---- 尺寸常量与窗口几何 —— 东西摆在哪、多大 ----
//
// 2.1.11 之前这些代码全在 OverlayWindow.xaml.cs 一个文件里(5117 行)。
// 这次只是按职责把它拆成几个 partial class 文件 —— 一行都没改,
// 编译出来的行为跟拆之前完全一样(改名/搬位置不影响任何逻辑)。

    bool _showKey9Num;

    // 只留字母时用的字号。九宫格一颗键是 112×112(360 的键盘区三等分再减边距),
    // 三个字母放到 28px 也才占一半宽, 所以给得起 —— 1.0.3 那版 18px 偏小, 字母缩在
    // 键中间一小撮, 隔着手柄距离看不清。4 个字母的键(7=PQRS / 9=WXYZ)小一号防贴边。
    const double KEY9_BIG = 28, KEY9_BIG4 = 24;

    /// <summary>
    /// 九宫格那一块的边长(布局单位, 缩放由 FlipHost 统一做)。
    /// 两处共用这一个数: 九键键盘区的宽高、以及切回九键时的宽高。
    /// </summary>
    const double KEY9_BOX = 360;

    /// <summary>
    /// 全键盘内容区的宽。2.1.0 起**高也必须是 360**(和九键一样)——
    /// 以前是 640×292, 比九键矮 68, 切过去上下两条边会往里缩, "四条边当出口"
    /// 这套规矩就破了(有边在缩就不叫"向外扩展")。现在只推左右两条边。
    /// </summary>
    const double KEY26_BOX_W = 640;

    // ==================================================================
    // 舞台(2.1.0): 窗口 = 内容 + 留白, **一次定死**
    // ------------------------------------------------------------------
    // 窗口不再跟着内容变(SizeToContent 撤掉了)。只要窗口尺寸还会变, 任何"往外扩"
    // 的动画都没有稳定基准 —— 演到一半窗口跟着变, 内容就在屏幕上乱窜。
    // 定死之后的规矩是:
    //   · 可见的卡片(Root / Back)**贴底居中**, 尺寸仍跟着内容走;
    //   · 窗口上面那片透明的地方就是留给预览框往上长的;
    //   · 九键那块离屏幕底的距离**恒定**, 打多少字都不动。
    // ==================================================================
    /// <summary>预览框**撑到 5 行**时的高度(含它自己那圈描边)。舞台按最坏情况留位。</summary>
    const double PREVIEW_BOX_H = 138;
    /// <summary>
    /// 卡片**下面**那一行状态行(滑行 / 符号分类名)的高度。**恒定**, 和 XAML 里
    /// RowStatus 的 Height 必须是同一个数: 它在卡片下面, 一变高矮卡片就上下跳;
    /// 而上面那些行随便收放都不影响卡片 —— 这是"能收的收、不能收的钉死"那条线。
    /// </summary>
    const double STATUS_ROW_H = 26;
    /// <summary>舞台四周再留一圈空。左右是给"往外扩"的动画留地方, 上面是余量。</summary>
    const double STAGE_PAD = 16;

    /// <summary>舞台宽 = 最宽那个框(全键盘 664) + 外面那圈 + 左右留白。</summary>
    static double StageW => FrameW(KEY26_BOX_W) + OUTER_PAD_X * 2 + 2 + STAGE_PAD * 2;    // 726
    /// <summary>
    /// 舞台高 = 预览框(5 行) + 留给系统输入法那道空档(48) + 候选 46 + 拼音 34
    ///        + 框 420 + 框外状态行 28 + 外面那圈 + 上面留白。
    /// 九键 / 全键盘 / 设置三面共用这一个高 —— 翻面和换布局都不会再跳尺寸。
    /// </summary>
    static double StageH => PREVIEW_BOX_H + 48 + 46 + 34 + FrameH(KEY9_BOX) + 28
                          + OUTER_PAD_Y * 2 + 2 + STAGE_PAD;                               // 752

    /// <summary>
    /// 设置里**一格**的边长 —— 就是九键上一颗键的边长。
    /// 九键那块 3×3 是 360, 一格 120, 键自己四面各留 4 的缝(见 Box) → 112。
    /// 所以设置里的九个格子排出来正好又是 360 见方, 外面再套一圈描边就是卡片。
    /// **两边都在同一个 FlipHost 的 LayoutTransform 底下**: 界面缩到 90% 时,
    /// 键和设置格是同一个变换一起缩的, 只要布局单位相等, 屏幕上就永远相等
    /// —— 不用另外再乘一次缩放。
    /// </summary>
    // ==================================================================
    // 卡片**上面**那几行的高度(2.1.0 第四批): 一律**钉死**
    // ------------------------------------------------------------------
    // 这一摞(OverPanel)是**贴底**的: 往上一格一格摞, 卡片下面只有恒定 26 的状态行,
    // 所以"上面某行长高一截"挪的是**摞的顶边**, 卡片一像素不动 —— 这正是当初敢
    // 让它们自由伸缩的原因。可预览框就在摞的最顶上: 候选行 / 拼音行一出一收,
    // 被挪走的就是它 —— 表现是"打上拼音, 预览框跟着往上顶一截"。
    // 于是这几行改成**常驻**(内容是空的就真为空, 只是高度不让), 唯一还能动的是
    // 预览框自己那一行: 它要从 0 长出来, 而它长的时候动的是自己的**顶边**
    // (底边钉着), 看着才是"往上浮"。
    // ==================================================================
    /// <summary>候选行的高度。恒为 46 —— 没候选时它整行是空的, 看不见但占位。</summary>
    const double CAND_ROW_H = 46;
    /// <summary>拼音行的高度。同上, 恒为 34。</summary>
    const double PINYIN_ROW_H = 34;

    const double SET_GAP = 4;                             // 和九键那颗键一样的缝

    /// <summary>
    /// 设置里那张 3×3 卡片的边长。**和九键那块一样大(360)** ——
    /// 2.1.0 起"进设置"在视觉上只是"九颗键变成了九个设置格", 尺寸不该变。
    /// (以前是 366: 那时卡片比九键大一圈, 翻过去窗口就长一截。)
    /// </summary>
    const double SET_CARD = KEY9_BOX;                                              // 360
    /// <summary>
    /// 由卡片反算一格 —— **和九键那颗键一模一样的 112**。
    /// 以前多减了一层(外框 1 + 内边距 2, 于是 110), 那一层还顺手在九个格子外面
    /// 多画了一圈细方框: 进设置看着就不像"九颗键变成九个设置格", 而像"换了一块
    /// 带框的面板"。现在那一层剥掉(见 MakePanel), 3×3 每格 120 的步长 / 四面 4 的
    /// 缝 / 112 的边长, 三项全跟九键那九颗键对上。
    /// </summary>
    const double SET_CELL = (SET_CARD - SET_GAP * 6) / 3;                          // 112

    /// <summary>
    /// 格子的圆角。**恒等于九键那颗键的圆角** —— 设置页和窗口F 里那九个格子就是
    /// 九颗键变的, 圆角也该是同一个数(贴纸风 18, 其余 10)。
    /// 由 ApplyTheme 跟着主题改(见那里), 格子每次重搭都会照它来。
    /// </summary>
    double _cellR = 10;

    /// <summary>设置层那张卡片的圆角。外面整页(Back)的圆角跟着主题走, 里面这层
    /// 不管哪套皮肤都是这一个数 —— 首页和深层长得一样才不会"换一层就变了个框"。
    ///
    /// 【描边粗细那个常量(原来是 1)撤了】2.1.0 第四批把这层的外框整个拿掉:
    /// 它在九宫格外面多画了一圈方框, 而且它的内边距把格子压成了 110(九键那颗键是 112)。
    /// 常量留着的话, 迟早有人照着它又把 BorderThickness 刷回去 —— 删掉是最保险的说明。</summary>
    const double SET_PANEL_R = 12;

    /// <summary>合过块的那格(长条 / 一大格)里正文的**基准**字号。
    /// 比格子的 16 小一号 —— 它要装十几行说明。放不下会再自动往下缩(见 FitTitle)。</summary>
    const double SOLO_FS = 13;
    /// <summary>自动缩号的下限。再小就认不出来了, 宁可让末尾截断。</summary>
    const double SOLO_FS_MIN = 9;

    // ==================================================================
    // 统一 UI 逻辑(2.1.0): 内容区 + 逻辑框
    // ------------------------------------------------------------------
    // 每一面都是**一块内容区**外面套**一个看不见的逻辑框**:
    //
    //      ┌────────── 框(看不见) ──────────┐
    //      │ 左上: "设置"二字(只有背面有)  │  ← 顶带 26
    //      │                                │
    //      │        内容区                  │  ← 九键 360×360 / 全键盘 640×360
    //      │                                │      / 设置卡片 360×360
    //      │ 左下: 说明小字   右下: 齿轮    │  ← 底带 34
    //      └────────────────────────────────┘
    //
    // 三条规矩:
    //   ① 框**只是个范围**, 不画出来。它的用处是给"小字 / 齿轮 / 标题"一个固定的家。
    //   ② 窗口变形状**只以四条边为基准往外扩**: 换全键盘是左右两条边各往外推 140,
    //      上下两条边钉着不动(所以全键盘的高必须等于九键的高)。
    //   ③ 小字永远在框的左下角、齿轮永远在框的右下角 —— 框变宽它们跟着到新的角上,
    //      但**相对框**永远在那两个角, 不会乱飘。
    // ==================================================================
    /// <summary>框在内容区左右各留多少。左右对称, 换全键盘时这两条不动。</summary>
    const double FRAME_PAD_X = 12;
    /// <summary>框在内容区上面留多少 —— 放"设置"二字(背面), 正面空着但占位。</summary>
    const double FRAME_PAD_TOP = 26;
    /// <summary>框在内容区下面留多少 —— 放说明小字(左)和齿轮 / 返回箭头(右)。</summary>
    const double FRAME_PAD_BOT = 34;

    /// <summary>框外再留一圈(到整页那道描边为止)。正反两面必须同一个数, 翻面才不跳。</summary>
    const double OUTER_PAD_X = 14, OUTER_PAD_Y = 10;   // = 正面 XAML 里 OuterGrid 的 Margin

    /// <summary>框的宽高。内容区是正方形时框就是 384×420(九键 / 设置), 全键盘 664×420。</summary>
    /// 【internal 是为了给下面的 FrameWConv 用】框比内容区宽多少**只在这里说一次**,
    /// XAML 里那条绑定跟着它, 免得"框的宽度"在 C# 里被手算成第二份。
    internal static double FrameW(double contentW) => contentW + FRAME_PAD_X * 2;
    static double FrameH(double contentH) => contentH + FRAME_PAD_TOP + FRAME_PAD_BOT;

    // 整页的大小 = 框 + 外面那圈留白 + **整页自己那圈描边**。最后这一项不能漏:
    // 可爱粉那套主题的描边是 4 而不是 1, 漏掉它的话整页的内容区就窄了 6、矮了 6,
    // 里面那 3×3 被挤掉一圈 —— 表现就是"每个格子只剩左边和上边两条线,
    // 右边和下边被挤没了"(描边被压到边框底下看不见)。
    // 高度**不跟着内容变**: 九键 / 全键盘 / 设置三面的框都是 420 高, 翻面和切布局
    // 都只动左右, 上下两条边钉死 —— 这就是"四条边当出口"里"上下不动"的那两条。
    static double BackBoxW(double bw) => FrameW(SET_CARD) + OUTER_PAD_X * 2 + 2 * bw;   // 414(bw=1)
    static double BackBoxH(double bw) => FrameH(SET_CARD) + OUTER_PAD_Y * 2 + 2 * bw;   // 442(bw=1)

    /// <summary>
    /// 滑行时, 没被摇杆指到的那几颗要压多暗。
    /// 九颗键本来就长得一模一样, 只让那一颗变大, 余光里还是分不清;
    /// 旁边的一起沉下去, 浮着的那颗才真的跳出来。
    /// </summary>
    // （"滑行"标签早先要从拼音行右边匀一条缝出来, 那条缝把面板撑宽了一格多,
    //   现在标签改成居中, 谁都不用让位, 这个常量也就没了。）

    // 滑行时其余八颗压到多暗。**别再往下调了** —— 0.40 那版实测太狠:
    // 九颗键本来就长得一模一样, 全压成一团灰之后你连"哪颗是 JKL、哪颗是 ABC"
    // 都认不出来, 只剩下笔尖那颗亮着, 想往回收手都得先松手看一眼。
    // 现在的数只是把当前那颗衬出来, 旁边八颗的字仍然一眼能读。
    const double SLIDE_DIM = 0.72;

    /// <summary>
    /// 换主题: 把上面那几个笔刷整个换掉, 再把窗口自己的三块面板(正面 / 背面 / 候选面板)
    /// 的底和边刷一遍。字色(白 / 灰 / 琥珀)和摇杆那几个高亮不动 —— 它们在所有深底上都看得清。
    /// 已经画出来的键不用管: 每帧 Paint() 都会按新的笔刷重刷一遍。
    /// </summary>
    static void SetRowH(RowDefinition r, double h)
    {
        if (Math.Abs(r.Height.Value - h) > 0.5) r.Height = new GridLength(h);
    }

    /// <summary>
    /// 唤出键盘: 把自己顶成前台窗口, 再把焦点交给记事本预览框 ——
    /// 系统输入法只在真正有焦点的编辑框上工作, 拼音 composition 和候选都靠它。
    /// </summary>
    void SyncStageSize()
    {
        // 【2.1.18: 必须用**无限宽**单独量一趟, 不能直接读 DesiredSize】
        // DesiredSize 会被"当前可用空间"夹住 —— 而可用空间就是我们刚设置的窗口宽。
        // 舞台里有一行是拉满宽的, 于是窗口越宽它报得越宽; 窗口又是照它算的,
        // 于是一次 Settle 就长一截(PAD x 2), 来回翻几次界面一路往边上挪、
        // 窗口也越来越宽(实测: 每翻一次面 +32 像素, 永不收敛)。
        // 要的是"内容自己想要多大": 先用 Size.PositiveInfinity 量一趟把它问出来,
        // 之后 Settle 里的 UpdateLayout 会按真实可用空间再排一遍, 不留脏账。
        OverPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // DesiredSize 是**这一摞想要多大**, 不受"当前给了它多大"影响 ——
        // 所以内容被切的时候(可用空间不够)用它才量得出来真实需求。
        double w = OverPanel.DesiredSize.Width, h = OverPanel.DesiredSize.Height;
        if (w < 1 || h < 1) return;
        double needW = Math.Max(StageW, Math.Ceiling(w * Scale) + STAGE_PAD * 2);
        double needH = Math.Max(StageH, Math.Ceiling(h * Scale) + STAGE_PAD * 2);
        if (needW > Width + 0.5) Width = needW;
        if (needH > Height + 0.5) Height = needH;
    }

    /// <summary>
    /// 布局刚被改动过之后走这里: **先让布局落地, 再按新坐标摆窗口**。
    /// 换缩放 / 翻面 / 换布局这三种情况都适用 —— 它们的共同点是"刚改了会影响
    /// 布局的东西", 而 PositionBottomCenter 是照着卡片的实际位置算窗口位置的,
    /// 布局没落地就等于拿上一轮的位置算这一轮的窗口 -> 界面整体挪位。
    /// 脑袋里记住这一句: **任何"改了布局"的地方, 收尾都得走 Settle, 不能只 Reposition。**
    /// </summary>
    void Settle(bool growStage = true)
    {
        if (!IsLoaded) { PositionBottomCenter(); return; }
        UpdateLayout();                 // 让刚改的变换 / 宽度在这一刻真的排完版
        if (growStage) SyncStageSize();
        UpdateLayout();                 // 舞台撑大之后要再落一次
        // 到这一刻每颗键才有真正的尺寸 —— 圆形那一档要按短边算圆角, 必须量得到。
        // 换形状 / 换主题 / 换布局之后都靠这一句把新圆角刷上去。
        ApplyKeyShape();
        PositionBottomCenter();
    }

    /// <summary>主程序(实现了 ISettingsHost), 背面的设置项直接读写它。</summary>
    void PositionBottomCenter()
    {
        if (ActualWidth <= 0) return;

        // 唤出头一两帧: 停在屏幕外(见 ShowReady)。这时候 SizeChanged 也会跑到这里,
        // 不能让它把窗口拽回屏幕上 —— 那就白停了。
        if (_holdOff)
        {
            Left = -(ActualWidth + 4000);
            Top = -(ActualHeight + 4000);
            return;
        }

        var area = SystemParameters.WorkArea;
        var r = CardRectInWindow();
        var k = KeyRectInWindow();          // 缩放的**不动点**: 内容区的正中心
        double cx = k.Left + k.Width / 2, cy = k.Top + k.Height / 2;
        double left, top;

        // 【不动点是"九键那颗 5", 不是卡片底边】
        // 以前摆的是**卡片**: 卡片横向居中、底边离屏幕底 24。卡片底边钉住了,
        // 放大时卡片就只能往上(和往两边)长 —— 于是中心那颗 5 一路往上跑,
        // 看着就是"放大往上方移动、缩小往下方移动"(用户原话)。
        // 现在反过来: **先定住中心那颗 5, 四条边爱长哪儿长哪儿**。
        //
        // 那"中心该停在屏幕的哪个高度"? 用 _anchorDrop 这把尺子量:
        // 它是"内容中心到卡片底边的距离", **只在第一回真的量到卡片时记一次**。
        // 记一次就够 —— 那一时刻卡片正是贴底的(离屏幕底 24), 所以刚装好、还没
        // 调过缩放的时候, 界面跟以前一模一样; 之后不管怎么调, 动的是四条边,
        // 中心那颗 5 停在原处不动。
        bool measured = (Flipped ? Back.ActualWidth : Root.ActualWidth) > 1;
        if (_anchorDrop <= 1 && measured && k.Width > 1)
            _anchorDrop = r.Bottom - cy;
        if (_anchorDrop > 1)
        {
            left = area.Left + area.Width / 2 - cx + OffsetX;
            top = area.Bottom - 24 - _anchorDrop - cy + OffsetY;
        }
        else
        {
            // 还没量出来(头一帧 / 卡片还没上架): 退回"卡片贴底居中",
            // 别在这种时候把界面摆到屏幕外头去。
            left = area.Left + (area.Width - r.Width) / 2 - r.Left + OffsetX;
            top = area.Bottom - 24 - r.Bottom + OffsetY;
        }

        // 夹的时候还是**按卡片夹**: 四面各留多少透明留白, 就允许探出去多少。
        // (缩放大到卡片快占满屏幕时, 这一夹会把它往上推 —— 那是必须的:
        // 总不能让键跑出屏幕外。中心那颗 5 从那一刻起才开始跟着动。)
        Left = Math.Clamp(left, area.Left - r.Left,
                          Math.Max(area.Left, area.Right - ActualWidth + (ActualWidth - r.Right)));
        Top = Math.Clamp(top, area.Top - r.Top,
                         Math.Max(area.Top, area.Bottom - ActualHeight + (ActualHeight - r.Bottom)));
    }

    /// <summary>
    /// "内容中心到卡片底边"的距离(屏幕像素)。**只认第一回量到的那个值** ——
    /// 它决定了不动点(九键那颗 5)停在屏幕的哪个高度, 之后就不许再变了,
    /// 变了就等于把不动点挪了地方。详见 PositionBottomCenter 里那段。
    /// </summary>
    double _anchorDrop;

    /// <summary>
    /// 卡片在**窗口**里的那一块(视觉坐标, 已经把 Scale 算进去了)。
    /// 卡片在舞台上的位置是被上面那几行(预览框 / 候选 / 拼音)和下面那行状态行
    /// 一起挤出来的, 既不在正中也不贴底 —— 只能量, 不能想当然。
    ///
    /// 【别用 TranslatePoint(xxx, this)】: 直接量到 Window 上, LayoutTransform
    /// 那一段算不对(量出来又是另一个数), 只能自己推 —— 见 RectOfIn。
    /// </summary>
    Rect CardRectInWindow()
    {
        var card = Flipped ? (FrameworkElement)Back : Root;
        double cw = card.ActualWidth, ch = card.ActualHeight;
        if (cw <= 1 || ch <= 1)
        {
            // 卡片还没排过版(典型: 头一回翻到设置, Back 从没上架过)。
            // 以前这一支返回的是"整个窗口那么大" —— 于是窗口按 726 宽去居中,
            // 而真正的卡片只有 414, 界面当场往左上角挪了 (726-414)/2 = 156 像素,
            // 而且再也没人把它摆回来(见 SetScale 那条注释)。
            // 退到一个**量纲对**的数: 卡片 = 内容区 + 框 + 外面那圈, 用当前内容宽度算。
            double kw = KeyArea.Width, kh = KeyArea.Height;
            if (double.IsNaN(kw) || kw < 1) kw = KEY9_BOX;
            if (double.IsNaN(kh) || kh < 1) kh = KEY9_BOX;
            double bw = FrameW(kw) + OUTER_PAD_X * 2 + 2, bh = FrameH(kh) + OUTER_PAD_Y * 2 + 2;
            return new Rect((ActualWidth - bw * Scale) / 2, ActualHeight - bh * Scale,
                            bw * Scale, bh * Scale);
        }
        return RectOfIn(card, cw, ch);
    }

    /// <summary>
    /// **内容区**(九键那个正方形 / 全键盘)在窗口里的那一块 —— 缩放的不动点
    /// 就是它的正中心, 也就是九键那颗 5 的位置。
    /// 设置那一面量不到它(KeyArea 躲在 Root 里, 而那一面 Root 是 Collapsed),
    /// 退回整张卡片: 设置九宫格在卡片里本来也是居中的, 差不了几像素。
    /// </summary>
    Rect KeyRectInWindow()
    {
        if (!Flipped)
        {
            // 【2.1.36: 正面不许再量 KeyArea 本人 —— 换屏动画就挂在它身上】
            // 换屏那一场(见 Fx.cs 的 FaceIn)动的是 KeyArea 的 RenderTransform:
            // 缩放 / 旋转 / 横移。TranslatePoint 照的是**这一瞬的变换**, 于是谁在
            // 那一场演到一半的时候摆窗口, 谁量到的就是一个正在半路上的锚点,
            // 反推出的 Left 也就跟着歪 —— 而且是**永久**的: 那一场演完没有第二次
            // Settle 把它摆回来, 整块界面就停在挪歪的地方了。
            //
            // 真机上怎么撞上的: 预览框沉下去要 260 毫秒, 而这 260 毫秒里按下 LB
            // 会起一场换屏(进滑行)。框沉完的收尾(NoteArrive)正好落在那一场中间,
            // 它调的 Settle 于是照着"正横移着的键区"摆了一次窗口 —— 实测往左跳
            // 174 ~ 332 像素(平移那套横移大, 跳得最狠; 旋转 3 像素; 默认 1 像素)。
            //
            // 改用**框(Frame)**: 它是 KeyArea 的爹, 自己不参与任何动画。
            // 内容区在框里左右各 FRAME_PAD_X(所以横向是居中的)、上面 FRAME_PAD_TOP,
            // 照这三个常量推回去就是内容区那一块 —— 别写成"框中心往上 4"那种心算,
            // 那样 FRAME_PAD 一动这里就错, 而且看不出那个 4 是从哪儿来的。
            double fw = Frame.ActualWidth, fh = Frame.ActualHeight;
            if (fw > 1 && fh > 1)
            {
                var fr = RectOfIn(Frame, fw, fh);
                double kw2 = KeyArea.ActualWidth, kh2 = KeyArea.ActualHeight;
                if (kw2 <= 1) kw2 = fw - FRAME_PAD_X * 2;
                if (kh2 <= 1) kh2 = fh - FRAME_PAD_TOP - FRAME_PAD_BOT;
                return new Rect(fr.Left + FRAME_PAD_X * Scale,
                                fr.Top + FRAME_PAD_TOP * Scale,
                                kw2 * Scale, kh2 * Scale);
            }
            double kw = KeyArea.ActualWidth, kh = KeyArea.ActualHeight;
            if (kw > 1 && kh > 1) return RectOfIn(KeyArea, kw, kh);
            return CardRectInWindow();
        }
        // 【背面也得用**内容区**当锚点, 不能退回整张卡】
        // 以前翻到设置这一面量不到 KeyArea, 就退回整张卡片 —— 而正面用的是
        // "内容区的正中心"。两个锚点差着"顶带比底带矮的那几像素", 于是每翻一次
        // 面, 窗口就在屏幕上挪 6 像素: 进设置往上跳一下、回九键再跳回来(用户
        // 报的"到位了又往上挪一小段"里有一半是这个)。
        // 背面也有自己的内容区(SetStage), 位置和正面的 KeyArea 一格不差 ——
        // 用它, 两面同一个规矩, 翻面时窗口一像素都不动。
        double sw = SetStage.ActualWidth, sh = SetStage.ActualHeight;
        if (sw > 1 && sh > 1) return RectOfIn(SetStage, sw, sh);
        return CardRectInWindow();
    }

    /// <summary>
    /// 把某个元素从 OverPanel 的坐标换算成**窗口的视觉坐标**(已经乘过 Scale)。
    /// 两步: 先问它在 OverPanel 里的位置(同一个变换底下, 这一步是准的),
    /// 再加上 OverPanel 自己"横向居中 + 纵向贴底"那点偏移, 最后乘 Scale。
    /// 注意那点偏移要拿**未缩放的舞台尺寸**算 —— 见函数里那段。
    /// <para>【别在动画演到一半的时候来量】TranslatePoint 照的是**当前这一瞬的
    /// 变换**, 而换页动画靠的就是 RenderTransform。谁在演期间调 PositionBottomCenter,
    /// 谁就会把界面摆到一个歪的位置上去 —— 靠**改调用顺序**躲开(见 ShowSide),
    /// 别指望在里头临时把变换摘掉: 那个缓存同一个调用里刷不掉。</para>
    /// </summary>
    Rect RectOfIn(FrameworkElement e, double w, double h)
    {
        double pw = OverPanel.ActualWidth > 1 ? OverPanel.ActualWidth : ActualWidth;
        double ph = OverPanel.ActualHeight > 1 ? OverPanel.ActualHeight : ActualHeight;
        var p = e.TranslatePoint(new Point(0, 0), OverPanel);

        // 【这里以前差了一个 Scale —— "缩放中心怎么改都改不回来"的根子就在这一行】
        // 缩放是挂在 FlipHost 上的 **LayoutTransform**。它的语义不是"排好版再放大",
        // 而是: 子元素先在一个**缩过的空间**里排版(舞台尺寸 ÷ Scale), 排完再整体
        // 乘 Scale 贴回窗口 —— 所以贴底居中那点偏移必须用**未缩放**的舞台尺寸来算。
        //
        // 以前直接拿 ActualWidth / ActualHeight(那已经是**放大后**的窗口尺寸)去算,
        // 结果算出来的位置比真实位置多出 (宽×(s-1)/2, 高×(s-1)):
        // s=1.2、舞台 726×752 的时候就是横偏 73、纵偏 151 像素。
        // 而窗口位置是照这个错数**反推**的(top = 目标高度 - cy), 错多少界面就往
        // 反方向挪多少 —— 于是"越放大越往左上方跑", 看着活像"以右下角为中心在放大"。
        // 偏偏 s=1 那一档两个尺寸相等、偏差为 0, 所以平时一点毛病没有, 一动缩放就露馅。
        double bw = ActualWidth / Scale, bh = ActualHeight / Scale;
        double x = ((bw - pw) / 2 + p.X) * Scale;
        double y = ((bh - ph) + p.Y) * Scale;
        return new Rect(x, y, w * Scale, h * Scale);
    }


    // ================= 切换（正面键盘 / 背面设置）=================
    /// <summary>现在是不是切到了背面(设置)。</summary>
}
