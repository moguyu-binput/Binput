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
// ---- 所有"怎么动" —— 缩放/旋转/位移/淡入淡出/翻面 ----
//
// 2.1.11 之前这些代码全在 OverlayWindow.xaml.cs 一个文件里(5117 行)。
// 这次只是按职责把它拆成几个 partial class 文件 —— 一行都没改,
// 编译出来的行为跟拆之前完全一样(改名/搬位置不影响任何逻辑)。

    static void ZoomTo(FrameworkElement e, double to)
    {
        if (e.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            e.RenderTransform = st;
        }
        var ease = FxKit.EaseOut();
        var span = FxKit.Ms(FxKit.MsZoom);
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(to, span) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(to, span) { EasingFunction = ease });
    }

    static (ScaleTransform, RotateTransform) CellTf(Border b)
    {
        if (b.RenderTransform is TransformGroup tg && tg.Children.Count == 2
            && tg.Children[0] is ScaleTransform s && tg.Children[1] is RotateTransform r)
            return (s, r);
        var g = new TransformGroup();
        var sc = new ScaleTransform(1, 1);
        var ro = new RotateTransform(0);
        g.Children.Add(sc);
        g.Children.Add(ro);
        b.RenderTransform = g;
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        return (sc, ro);
    }

    /// <summary>
    /// 把一颗键 / 一格的 Z 序抬到最上面(或落回来), **连它的外层一起**。
    /// <para>为什么有时候得抬两层: 只抬里面那颗是不够的 —— 它整层还压在邻居的容器
    /// 下面, 放大了照样被盖住。所以要问一句"我爹是不是专门用来抬层的那一层"
    /// (打了 <see cref="LIFT_HOST"/> 标记的: 九键的 _key9Host、网格与设置每一格
    /// 的外层), 是就两层一起抬。</para>
    /// <para>26 键的键装在每一排的 StackPanel 里, 同理那一整排也得抬, 不然还是被
    /// 上下排压住。</para>
    /// </summary>
    static void LiftZ(FrameworkElement b, bool up)
    {
        Panel.SetZIndex(b, up ? 10 : 0);
        if (VisualTreeHelper.GetParent(b) is StackPanel row) Panel.SetZIndex(row, up ? 10 : 0);
        else if (VisualTreeHelper.GetParent(b) is FrameworkElement host
                 && host.Tag as string == LIFT_HOST) Panel.SetZIndex(host, up ? 10 : 0);
    }

    /// <summary>
    /// 光标所在那一格**浮起来** —— 和九键盘上被摇杆选中那颗键一模一样:
    /// 冲过头一点再回落(那一下弹)、左右摆两下再停(那一晃)、最后停在放大后的尺寸上,
    /// 再压一道影子、把 Z 序抬起来。以前只是"路过命案场"般的弹一下就缩回原样,
    /// 跟九键那颗始终浮着的样子对不上。
    /// </summary>
    /// <summary>
    /// 浮起来的**倍数由格子大小反比算出来**(见 <see cref="PopOf"/>), 不写死 ——
    /// 112 的格子放大 1.14 只是探出 16; 352 的一大格放大 1.14 就是 401,
    /// 顶出卡片外头去了(卡片里只有 388)。按大小算, 一大格和一小格看着才是同一个动作。
    /// </summary>
    void CellPop(Border b, SetPage page, int slot)
    {
        LiftZ(b, true);
        b.Effect = FxShadow;
        var (sc, ro) = CellTf(b);
        var ease = FxKit.EaseOut();
        double pop = PopOf(page, slot);
        // 那一晃同理: 112 的格子摆 4.5 度只是"抖一下", 352 的一大格摆 4.5 度
        // 四角能甩出去十几像素, 看着像整块在转。
        double wob = WobOf(page, slot);

        for (int i = 0; i < 2; i++)
        {
            var sk = new DoubleAnimationUsingKeyFrames();
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(pop * FxKit.PopOver,
                FxKit.At(FxKit.PopAt0), ease));
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(pop * FxKit.PopBack,
                FxKit.At(FxKit.PopAt1)));
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(pop,
                FxKit.At(FxKit.PopAt2)));
            sc.BeginAnimation(i == 0 ? ScaleTransform.ScaleXProperty
                                     : ScaleTransform.ScaleYProperty, sk);
        }

        // 左右摆两下再停 —— 九键那颗键就是这么"晃"一下的, 设置页别两回事
        var rk = new DoubleAnimationUsingKeyFrames();
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(-wob,
            FxKit.At(FxKit.WobAt0), ease));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob * FxKit.WobBack1,
            FxKit.At(FxKit.WobAt1)));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(-wob * FxKit.WobBack2,
            FxKit.At(FxKit.WobAt2)));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(0,
            FxKit.At(FxKit.WobAt3)));
        ro.BeginAnimation(RotateTransform.AngleProperty, rk);
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
        var ease = FxKit.EaseOut();
        int side = delta < 0 ? -1 : 1;

        // 放大: 冲过头再回落。1.20 是"看得出在动"又不至于把牌甩出舞台的量,
        // 和九键那颗键(见 FxKit.ScalePop)比略大一号 —— 小牌本来就小, 不放大点看不出来。
        for (int i = 0; i < 2; i++)
        {
            var sk = new DoubleAnimationUsingKeyFrames();
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(FxKit.ScaleSidePop,
                FxKit.At(FxKit.MsSidePop0), ease));
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(FxKit.ScaleSideBack,
                FxKit.At(FxKit.MsSidePop1)));
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
                FxKit.At(FxKit.MsSidePop2)));
            sc.BeginAnimation(i == 0 ? ScaleTransform.ScaleXProperty
                                     : ScaleTransform.ScaleYProperty, sk);
        }

        // 转: 往翻的那一侧甩一下, 回摆两次停住(和 CellPop 那一晃同一条曲线)
        double wob = FxKit.SideDeg * side;
        var rk = new DoubleAnimationUsingKeyFrames();
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob,
            FxKit.At(FxKit.MsSideWob0), ease));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob * FxKit.SideWobBack1,
            FxKit.At(FxKit.WobAt1)));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob * FxKit.SideWobBack2,
            FxKit.At(FxKit.WobAt2)));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(0,
            FxKit.At(FxKit.WobAt3)));
        ro.BeginAnimation(RotateTransform.AngleProperty, rk);

        // 顶: 往翻的那一侧挪 7 像素再回来
        double push = FxKit.SidePushPx * side;
        var tk = new DoubleAnimationUsingKeyFrames();
        tk.KeyFrames.Add(new EasingDoubleKeyFrame(push,
            FxKit.At(FxKit.MsSidePop0), ease));
        tk.KeyFrames.Add(new EasingDoubleKeyFrame(0,
            FxKit.At(FxKit.MsSidePop2)));
        tr.BeginAnimation(TranslateTransform.XProperty, tk);
    }

    /// <summary>光标走了: 缩回原样、影子撤掉、Z 序落回来。</summary>
    void CellRest(Border b)
    {
        LiftZ(b, false);
        b.Effect = null;
        if (b.RenderTransform is not TransformGroup tg
            || tg.Children[0] is not ScaleTransform sc
            || tg.Children[1] is not RotateTransform ro) return;
        var ease = FxKit.EaseOut();
        var span = FxKit.Ms(FxKit.MsRest);
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, span) { EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, span) { EasingFunction = ease });
        ro.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, span) { EasingFunction = ease });
    }

    /// <summary>
    /// A 正按着: 光标那一格缩下去; 松手弹回**浮着的那个尺寸**(和敲九键那颗键同一个
    /// PRESS_SCALE(见 FxKit.ScalePress), 同一个"按下去快、回来稍慢"的手感)。
    /// </summary>
    void SetCellDown(LayerHost h, bool down)
    {
        var p = h.Top;
        if (p == null || h.Boxes.Count == 0) return;
        var boxes = h.Boxes;
        if (p.Sel < 0 || p.Sel >= boxes.Count) return;
        var (sc, _) = CellTf(boxes[p.Sel]);
        var ease = FxKit.EaseOut();
        // 按下去要跟手(短), 松手回来稍慢一点, 才像"弹回原位"
        var span = FxKit.Ms(down ? FxKit.MsDown : FxKit.MsUp);
        // 松手弹回**浮着的那个尺寸** —— 合过块的那格浮起来放得小(见 PopOf),
        // 弹回去也得是同一个数, 不然按一下 A 它会"变大一截"。
        double to = down ? FxKit.ScalePress : PopOf(p, p.Sel);
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, span) { EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, span) { EasingFunction = ease });
    }

    /// <summary>
    /// 换页之前把"按下去"那一下清干净。不清的话: 按着 A 翻出新一页, 松手时最上面
    /// 已经是新页了, 归位归到新页那一格 —— 老页那颗就永远缩在 0.88 上。
    /// </summary>
    void ResetCellScale(LayerHost h)
    {
        h.Down = false;
        h.Cur = null;                       // 格子马上要重搭了, 别再拽着旧的那一格
        foreach (var b in h.Boxes)
        {
            b.Effect = null;
            Panel.SetZIndex(b, 0);
            if (b.RenderTransform is not TransformGroup tg
                || tg.Children[0] is not ScaleTransform sc
                || tg.Children[1] is not RotateTransform ro) continue;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            sc.ScaleX = 1; sc.ScaleY = 1;
            ro.BeginAnimation(RotateTransform.AngleProperty, null);
            ro.Angle = 0;
        }
    }

    // ---------- 操作 ----------

    /// <summary>
    /// 动画演的时候不接受操作 —— 否则能按出"页面正在退、格子已经被改了"这种半个状态。
    /// h.Count > 0 === "有页面在身上"; 现在它与"有面板被画出来"是一回事(见 ShowPage)。
    /// </summary>
    // SET_ANIM_MS 已搬进 FxKit: 往里钻一层用 MsPageIn、旧页消散用 MsPageOut(两个都是 180)

    /// <summary>
    /// 面板那三种变换: 缩放 + 平移 + **旋转**, 一次备齐。
    /// <para>旋转是给"扑克牌"那一套换页动效用的(见 <see cref="SpinIn"/>)。它和
    /// CellTf / SideTf 那些"格子自己的"变换组不通用 —— 那些只有两零件。</para>
    /// </summary>
    static TransformGroup TfGroup(FrameworkElement b)
    {
        if (b.RenderTransform is TransformGroup tg && tg.Children.Count == 3) return tg;
        var g = new TransformGroup();
        g.Children.Add(new ScaleTransform(1, 1));
        g.Children.Add(new TranslateTransform(0, 0));
        g.Children.Add(new RotateTransform(0));
        b.RenderTransform = g;
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        return g;
    }

    static (ScaleTransform, TranslateTransform, RotateTransform) Parts(FrameworkElement b)
    {
        var g = TfGroup(b);
        return ((ScaleTransform)g.Children[0], (TranslateTransform)g.Children[1],
                (RotateTransform)g.Children[2]);
    }

    // ================= 两套换页动效 =================
    //
    // 「默认」= 旧的 Orin 那套: 缩小 + 下沉 + 淡入淡出。
    // 「旋转」= 扑克牌: 绕左下角转, 旧页转出去、新页转回来。
    // 两个都要能"只动 spirit level 上的数字", 所以摆在 FxKit 里那一组。
    //
    // dir 是这一场往哪个方向走: +1 = 往里钻 / 往下一页; -1 = 退回上一层 / 上一页。
    // 显示层不作记忆 —— 每一次换页都由调用方说清方向, 丢了就有可能为负那一场。

    /// <summary>
    /// 换页动效现在演的是哪一套。**每演一场问一次当前值** —— 于是设置里改完
    /// 下一个动作立刻就变了, 不用重开界面。
    /// </summary>
    bool Spinning => (Host?.FxStyle ?? FxStyle.Plain) == FxStyle.Spin;

    /// <summary>
    /// 换页动效现在演的是「平移」那一套吗(见 <see cref="FxStyle.Slide"/>)。
    /// 和 Spinning 一样**每演一场问一次**, 设置里改完下一场立刻就变。
    /// </summary>
    bool Sliding => (Host?.FxStyle ?? FxStyle.Plain) == FxStyle.Slide;

    /// <summary>
    /// 换页动效现在演的是「浮力」那一套吗(见 <see cref="FxStyle.Buoy"/>)。
    /// <para>【只管九键那一页"浮在水面上"那层常驻漂动, 换页它不管】ComeIn / GoOut
    /// 里只问 <see cref="Spinning"/> 和 <see cref="Sliding"/>, 两样都不是就走「默认」
    /// 那一条路 —— 于是浮力这一档的换页**天生就是默认那套**, 一处都不用改。</para>
    /// </summary>
    bool Buoying => (Host?.FxStyle ?? FxStyle.Plain) == FxStyle.Buoy;

    /// <summary>
    /// 平移那一趟横着走多远。见 FxKit 里 SlideRatio 那段: 两页并排贴着走, 走完
    /// 的行程正好是一页的宽度, 于是全程没有缝。
    /// <para>【两页必须走**同一个距离**, 所以这里不看元素自己的宽度】旧页是一张
    /// 照片(按拍照那一刻的尺寸)、新页是刚换上来的真身, 两者宽度本来就不一样
    /// (九键 360 / 设置 414)。各走各的宽的话, 一页滑得快一页滑得慢, 中间那道缝
    /// 会一路忽宽忽窄 —— 看着就不是"一整条在移动"。统一按**内容区宽度**走,
    /// 两页才是刚性并排。</para>
    /// <para>读 ContentW 而不是某个元素的 ActualWidth: 那是宽度唯一的源头(见
    /// xaml 里那一组绑定), 换面时它已经在动画起步前定好了, 两页读到的是同一个数。</para>
    /// </summary>
    double SlideSpan() => (ContentW > 1 ? ContentW : KEY9_BOX) * FxKit.SlideRatio;

    /// <summary>
    /// 上一场平移还在滑吗。连着翻页(或者摇杆一直推在翻页那一侧)时这里会连着被问,
    /// 场上已经有旧页的照片在演着 —— 那就别再拍一张、别再从头演一次(见各调用点)。
    /// <para>【只管平移那两套之外的不说】默认 / 旋转那两套的起点就在框里(缩放 / 转
    /// 个角度), 重演一次不刺眼, 保持它们原来的样子。</para>
    /// </summary>
    bool SlideBusy() => Sliding && DateTime.Now < _slideUntil;

    // ---------- 平移那一套的「屏幕框」: 滑出页面就不许再画 ----------
    //
    // 手机滑页的观感里, 屏幕外是**没有画面**的: 一页滑到边上就整块消失,
    // 而不是拖着一条尾巴飘到卡片外面的空白处。现在卡片外面那圈留白是能画东西的,
    // 于是旧页会一路滑到卡片外还看得见 —— 看着就是"页面散架飘出去了"。
    //
    // 于是演平移的这段时间里, 给"两页共同的爹"临时挂上裁剪(ClipToBounds):
    // 出了这个容器的边就不画。演完立刻撤。
    //
    // 【为什么是临时的, 不能常挂】只有平移要这一手。默认那套的下沉、旋转那套
    // 甩出去的那个角, 本来就画到框外面去了 —— 常挂着会把它们切掉一块。
    // 【为什么裁"爹"而不是裁卡片】卡片(Root)外面还挂着 ◀LB / RB▶ 两个翻页小牌,
    // 裁卡片会把那两个一起切没。两页摆在同一个容器里, 裁这个容器就只管住两页。

    // 【2.1.30: 屏幕框从"父容器的边"改成"页面这一块"】
    // 上一版挂的是 ClipToBounds —— 那是**父容器**的边, 而父容器(Frame / SetStage)
    // 比页面本身宽出一圈: 正面那个框左右各多 12, 背面那一格更是撑满了卡片里面。
    // 于是页面滑出去之后还能在那一圈里被看见 —— 观感就是"下一页从框外冒出来,
    // 再滑进来", 页多的时候连着翻尤其明显。
    // 现在显式画一个矩形: **就是页面这一块**, 不多不少。出这一块就不画。

    /// <summary>这一场临时挂上屏幕框的容器们, 连同它们原本的 Clip(演完好还回去)。</summary>
    readonly List<(UIElement Host, Geometry Old)> _clipHosts = new();

    /// <summary>
    /// 屏幕框在纵向上放行多少(见 <see cref="SlideClipOn"/>)。
    /// 给一个大数而不是"容器有多高": 容器里的东西本来就允许画到容器外面去(默认那套
    /// 的下沉、旋转那套甩出去的角都是), 按容器的高切会把它们一起切掉。
    /// </summary>
    const double CLR_BAND = 1e4;

    /// <summary>
    /// 给 page 所在的容器挂上屏幕框: 出了 page 这一块就不画。
    /// <para>【量的时候位移必须是零】量的是 page 此刻在容器里的位置, 而量完紧接着
    /// 就要把 page 摆到框外当起点 —— 所以调用点必须在动画起步之前(见 ComeIn /
    /// GoOut 里那两处, 都紧跟在 ResetParts 之后)。</para>
    /// </summary>
    void SlideClipOn(FrameworkElement page)
    {
        var host = page?.Parent as UIElement;
        if (host == null) return;
        // 尺寸还没量出来(刚搭起来还没排过版)就别挂 —— 挂上去是个 0 大小的框,
        // 整页都被裁没了。
        if (page.ActualWidth < 16 || page.ActualHeight < 16) return;
        // 旧页(那张照片)和新页(真身)并排待在同一个容器里, 两边都要挂框 —— 而它俩
        // 不一定一样大: 照片撑满整个 Stage, 真身只是页面那一块(见 EnsureVeil)。
        // 【取交集, 不取先来那个】谁先挂算谁的话, 框就会跟着照片宽出那一圈,
        // 页面滑出去之后还能在那一圈里被看见 —— 正是"从框外冒出来再滑进来"。
        var b = page.TransformToVisual(host)
                    .TransformBounds(new Rect(0, 0, page.ActualWidth, page.ActualHeight));
        // 【只在横向上挡, 纵向整条放行】2.1.31。
        // 平移只横着走 —— 纵向挡不住任何东西, 倒是会挡错人: "两页共同的爹"里往往
        // 还住着别人。正面那一格(Frame 384×420)上下各挂着一条顶带(26)和一条底带
        // (34), 底带上住着那排页码点(PunctDots)和右下角那个齿轮 —— 拿"页面那一块"
        // 上下一切, 它们就跟着这一整场一起不见了: 用户报的"翻页时底下那排小点跟着
        // 消失一下又冒出来"正是这么来的。快捷短语那一摞的爹是 QuickStage(里面只有
        // 两页), 切了也不露馅 —— 所以只有它一直正常。
        // 横向这一刀才是要紧的: 页面滑出这一块就该整块消失, 不许拖着尾巴飘到框外。
        var box = new Rect(b.X, -CLR_BAND, b.Width, CLR_BAND * 2);
        foreach (var x in _clipHosts)
        {
            if (!ReferenceEquals(x.Host, host)) continue;
            var old = host.Clip?.Bounds ?? Rect.Empty;
            host.Clip = new RectangleGeometry(old.IsEmpty ? box : Rect.Intersect(old, box));
            return;
        }
        _clipHosts.Add((host, host.Clip));
        host.Clip = new RectangleGeometry(box);
    }

    /// <summary>演完(或换回别的动效)把屏幕框撤掉, 还原各容器原本的 Clip。</summary>
    void SlideClipOff()
    {
        if (_clipHosts.Count == 0) return;
        foreach (var (host, old) in _clipHosts) host.Clip = old;
        _clipHosts.Clear();
    }

    /// <summary>这一场平移在跑的位移们。**所有**这些都不动了, 才轮到撤屏幕框。</summary>
    readonly List<TranslateTransform> _slideTrs = new();

    /// <summary>把这一条位移记进名单(同一个元素连着演两场的话是同一个对象, 不会记重)。</summary>
    void SlideWatch(TranslateTransform tr)
    {
        if (tr == null) return;
        if (!_slideTrs.Contains(tr)) _slideTrs.Add(tr);
    }

    /// <summary>
    /// 一场演完要不要撤屏幕框 —— 得**场上确实没人在滑了**才撤。
    /// <para>旧页出门和新页进门是同时起步的两条动画, 谁先结束不一定。先结束的那条
    /// 要是直接把框撤了, 另一条还没走完的那几十毫秒里画面就露在框外了 —— 而
    /// "页面从框外冒出来"正是 2.1.30 要修的那个毛病。</para>
    /// <para>【怎么才算"没人在滑"】看这些位移**身上还有没有动画在跑**, 不去看时间:
    /// 时钟有抖动(到点了 Completed 可能差几毫秒才喊), 而"还有没有动画"是板上钉钉的
    /// 事实。连着按的时候, 后一场会把前一场顶掉 —— 被顶掉的那条永远等不到自己的
    /// Completed(于是靠时间记账会越记越多、最后再也不撤), 可它身上确实已经没有动画
    /// 了, 这里照样认得出来。</para>
    /// </summary>
    void SlideClipOffIfIdle()
    {
        foreach (var t in _slideTrs)
            if (t.HasAnimatedProperties) return;      // 还有人在滑, 框不能撤
        _slideTrs.Clear();
        SlideClipOff();
    }

    /// <summary>当前这一场(旧页出门 + 新页进门)最晚演到什么时候。空场时为很早。</summary>
    DateTime _slideUntil = DateTime.MinValue;
    /// <summary>当前这一场是往哪个方向走的(见 ComeIn 的 dir)。</summary>
    int _slideDir;

    // ---------- 平移这一套的"实心": 旧页照片不许带糊、不许压暗 ----------
    //
    // 旧页那张照片天生带两级"处理": 一层高斯模糊(14)、一层压暗(跟主题走的半透明
    // 色)。那是 1.x 为了和"设置里钻深层那一口"对齐加的景深 —— 默认 / 旋转那两套里
    // 旧页是"化掉"的, 糊一点、暗一点正好。
    // 平移讲的是"两页是同一块实心的板子在移动", 旧页只是滑出了屏幕, 它没化也没暗。
    // 于是演平移的这一场把这两层摘掉, 演完还回去 —— 默认 / 旋转照旧。

    /// <summary>照片上那层模糊。摘掉和还原都用它, 于是两张照片(正面 / 设置那一摞)
    /// 前后是同一个样子, 不会因为还原成另一个实例而差一点。</summary>
    static readonly BlurEffect VeilBlur = new()
    { Radius = 14, KernelType = KernelType.Gaussian };

    /// <summary>这一场被摘掉糊 / 压暗的照片们, 演完照着这份名单还回去。</summary>
    readonly List<(Image Img, Border Shade)> _flatPhotos = new();

    /// <summary>一张"旧页照片"里那两样东西: 底下那张图 + 压在上面的压暗层。</summary>
    static (Image Img, Border Shade) PhotoParts(FrameworkElement photo)
    {
        // 正面那张是 Grid(图 + 压暗层直接是它的两个儿子); 设置那一摞是 Border
        // 裹着一个 Grid —— 两种都得认得出来。
        var panel = photo as Panel ?? (photo as Border)?.Child as Panel;
        if (panel == null) return (null, null);
        Image img = null;
        Border shade = null;
        foreach (var ch in panel.Children)
        {
            if (img == null && ch is Image i) img = i;
            else if (shade == null && ch is Border s) shade = s;
        }
        return (img, shade);
    }

    /// <summary>演平移的这一场: 把照片的糊和压暗摘掉(见上面那段)。</summary>
    void SlideFlatOn(FrameworkElement photo)
    {
        var (img, shade) = PhotoParts(photo);
        if (img == null && shade == null) return;
        _flatPhotos.Add((img, shade));
        if (img != null) img.Effect = null;
        if (shade != null) shade.Background = null;
    }

    /// <summary>演完把糊和压暗还回去。</summary>
    void SlideFlatOff()
    {
        if (_flatPhotos.Count == 0) return;
        foreach (var (img, shade) in _flatPhotos)
        {
            if (img != null) img.Effect = VeilBlur;
            if (shade != null) shade.Background = VeilBrush;
        }
        _flatPhotos.Clear();
    }

    /// <summary>收摊: 屏幕框撤掉, 照片的糊 / 压暗也一并还原。</summary>
    void SlideReset()
    {
        SlideClipOff();
        SlideFlatOff();
    }

    /// <summary>
    /// 把旋转和缩放的圆心都钉在这一块的**左下角**上。
    /// <para>不能去改 RenderTransformOrigin —— 那是这一组三种变换共用的原点,
    /// 而默认那一套的缩放得以中心起飞; 各表单 collocation 上各自的
    /// CenterX / CenterY, 两套互不打扰。</para>
    /// <para>两个都钉同一个点, 卡片左下角才是**真的钉死**: 缩放往这点收、
    /// 旋转绕这点转, 摞起来没有额外的位移。</para>
    /// </summary>
    static double PivotTo(FrameworkElement e, ScaleTransform sc, RotateTransform ro)
    {
        // 刚搭出来的面板还没排过版, 量到 0 就会绕左上角转(整块往上翻) —— 兜住。
        double h = e.ActualHeight > 1 ? e.ActualHeight : SET_CARD;
        sc.CenterX = 0; sc.CenterY = h;
        ro.CenterX = 0; ro.CenterY = h;
        return h;
    }

    /// <summary>把旋转 / 平移 / 缩放那些残留的动画撒手, 回到原状(下一场得从头演)。</summary>
    static void ResetParts(ScaleTransform sc, TranslateTransform tr, RotateTransform ro)
    {
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        tr.BeginAnimation(TranslateTransform.XProperty, null);
        tr.BeginAnimation(TranslateTransform.YProperty, null);
        ro.BeginAnimation(RotateTransform.AngleProperty, null);
        sc.ScaleX = 1; sc.ScaleY = 1; tr.X = 0; tr.Y = 0; ro.Angle = 0;
    }

    /// <summary>
    /// 扑克牌那一套的**转回来**: 从逆时针那侧的 SpinDeg 度处, 绕左下角顺时针转回正,
    /// 同时由透明转实、由 SpinShrink 放回到原大。
    /// <para>【2.1.16: 它只在"退回上一层"这一趟演】往里走那一趟的新页是**浮现**
    /// (见 <see cref="ComeIn"/>), 不转 —— 用户要的是"掀开上层、露出下层"那个层次,
    /// 两张牌一起扫反而看不出谁在上谁在下。于是角度不再乘 dir, 永远是同一条弧:
    /// 出去时旧页转到 -SpinDeg, 回来时上层就从 -SpinDeg 转回 0, 正好原路。</para>
    /// </summary>
    static void SpinIn(FrameworkElement e, ScaleTransform sc, TranslateTransform tr,
                       RotateTransform ro, TimeSpan begin, TimeSpan dur)
    {
        PivotTo(e, sc, ro);
        var ease = FxKit.EaseOut();
        // 晚起步的那一段用的是**本地值**, 所以先把姿势摆到起点, 别先以正位亮一下。
        ro.Angle = -FxKit.SpinDeg;
        sc.ScaleX = sc.ScaleY = FxKit.SpinShrink;
        ro.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-FxKit.SpinDeg, 0, dur) { EasingFunction = ease, BeginTime = begin });
        for (int i = 0; i < 2; i++)
            sc.BeginAnimation(i == 0 ? ScaleTransform.ScaleXProperty
                                     : ScaleTransform.ScaleYProperty,
                new DoubleAnimation(FxKit.SpinShrink, 1, dur) { EasingFunction = ease, BeginTime = begin });
    }

    /// <summary>
    /// 扑克牌那一套的**转出去**: 从正的姿态绕左下角**逆时针**转掉, 同时淡掉、往左下角收。
    /// <para>【2.1.16: 只在"往里走"这一趟演, 而且方向恒为逆时针】退回那一趟的旧页是
    /// **下沉**(见 <see cref="GoOut"/>)。方向不再跟着 dir 翻: 全界面"往下一层 /
    /// 下一页"都是同一个转法, 用户靠"转走了"认出自己在往里走, 不用记两套方向。</para>
    /// </summary>
    static void SpinOut(FrameworkElement e, ScaleTransform sc, TranslateTransform tr,
                        RotateTransform ro, TimeSpan dur)
    {
        PivotTo(e, sc, ro);
        var ease = FxKit.EaseOut();
        ro.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, -FxKit.SpinDeg, dur) { EasingFunction = ease });
        for (int i = 0; i < 2; i++)
            sc.BeginAnimation(i == 0 ? ScaleTransform.ScaleXProperty
                                     : ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, FxKit.SpinShrink, dur) { EasingFunction = ease });
    }

    // ================= 两条线共用的一套「怎么演」 =================
    //
    // 以前这段**抄了三遍**: RiseIn(往里钻一层) / PopSetPanel(进设置) / FaceIn(换屏),
    // 出门那边也抄了两遍: SinkLeave(层) / SinkFace(卡)。五份各自写着各自的数字,
    // 于是同一个"进来"在九键上是 0.965/170、在设置里是 0.92/180 —— 想换条曲线得
    // 挨个改, 漏一处就是"九键换了、设置里没换"。
    //
    // 现在收敛成一对 —— **GoOut(旧照片出门) / ComeIn(新的一页进门)**, 轻重两档的
    // 数字照旧摆在 FxKit 里。三条线(层 / 换面 / 换屏)共用这一对。
    // 以后加第三种风格(推 / 卷帘 / 堆叠): 改这两个方法里那一句 `if (Spinning)`
    // 就够, 全界面必然是同一种。

    /// <summary>
    /// 进门那一档的数字。<paramref name="heavy"/> = 往里钻一层(重, 0.92 / 26px);
    /// 换面 / 换屏是轻的那一档(0.965 / 7px) —— 换面只是"九颗键变成九个格子",
    /// 幅度该比"压进来一层"收敛些。
    /// </summary>
    static (double Scale, double Dy, double Ms) InKit(bool heavy) => heavy
        ? (FxKit.ScalePageIn, FxKit.DyPageIn, FxKit.MsPageIn)
        : (FxKit.ScaleFaceIn, FxKit.DyFaceIn, FxKit.MsFace);

    double ComeIn(FrameworkElement body, ScaleTransform sc, TranslateTransform tr,
                  RotateTransform ro, int dir, bool heavy, Action onDone = null)
    {
        // 【要看清上一场走到哪儿了, 得赶在 ResetParts 前面】下面那句会把位移归零
        // (那是给"别从上一场的收尾姿势接着来"用的), 一归零就问不出来了 ——
        // 而"从当前位置接着滑"要的正是这个数(见下面 slide 那一支)。
        // 动画还在跑的时候读 X 读到的就是它此刻的值, 不用另外记时钟。
        bool running = DateTime.Now < _slideUntil && _slideDir == dir;
        double was = tr?.X ?? 0;
        ResetParts(sc, tr, ro);     // 上一场没演完就别从它的收尾姿势接着来
        var ease = FxKit.EaseOut();
        var (s0, dy, ms) = InKit(heavy);
        // 【只有旋转那一套讲先后】旧页先转走、新的一页才浮出来(见 GoOut 里同一句)。
        // 「默认」和平移都是同时进出的 —— 平移尤其得同时: 它演的是"两页并排
        // 一起走", 错开半拍就成了"旧的走了、隔一会儿新的才来"。
        bool spin = Spinning, slide = Sliding;
        double span = spin ? FxKit.MsSpin : slide ? FxKit.MsSlide : ms;
        var begin = spin ? FxKit.Ms(span * FxKit.FxLag) : TimeSpan.Zero;
        var dur = FxKit.Ms(span * (spin ? FxKit.FxCut : 1.0));
        // 平移这一场里把屏幕框挂上(见 SlideClipOn); 换回别的动效时顺手把上一场
        // 可能残留的裁剪撤干净 —— 连按打断的那一场走不到收尾, 只有这条路能收。
        if (slide) SlideClipOn(body); else SlideReset();
        // 退回来(-1): 上一层绕左下角顺时针转回原位; 往里走(+1): 从里往外浮现。
        // 平移不从哪儿"浮", 它是从另一边**滑**到位: 往里走(+1)就从右边来,
        // 退回来(-1)就从左边来 —— 正好是旧页走的那一程的反面(见 GoOut)。
        if (slide)
        {
            double d = SlideSpan() * dir;
            // 【连按不重演】上一场还没滑完就别把这一页拽回框外、从头再冲进来一次 ——
            // 那正是"下一页先在框外露一下, 再滑进来"的来处(页一多、连着翻就天天撞上)。
            // 现在从它**此刻站的地方**接着滑到位, 剩下的路按同一个速度走完(时长按
            // 剩余距离等比缩短)。连着翻于是变成"一路往同一个方向流", 像手机上连翻。
            // 换方向时(刚往里走又马上退回)不接着 —— 那得从另一边进来, 照旧从头演。
            double from = d, rest = span;
            if (running && Math.Abs(was) > 0.5 && Math.Abs(was) < Math.Abs(d) - 0.5)
            {
                from = was;     // 上一场走到一半的那一点, 接着往同一方向滑完
                rest = Math.Max(FxKit.MsSlideRest, span * (Math.Abs(from) / Math.Abs(d)));
            }
            var durS = FxKit.Ms(rest);
            _slideUntil = DateTime.Now.Add(durS);
            _slideDir = dir;
            SlideWatch(tr);     // 撤屏幕框要等这一条也停下来(见 SlideClipOffIfIdle)
            tr.X = from;       // 动画还没起步那一瞬用的是本地值, 先摆到起点
            var xa = new DoubleAnimation(from, 0, durS) { EasingFunction = FxKit.EaseSlide(), BeginTime = begin };
            // 【收尾改挂位移那条】平移不走淡入(见下面), 而原来是靠淡入那条动画喊收尾的。
            // 撤屏幕框也在这儿: 得等**两页都演完**才撤(见 SlideClipOffIfIdle)。
            xa.Completed += (_, _) =>
            {
                // 演完把横向那一位也撒手归零: 不撒手的话它一直停在"有动画"的状态,
                // 屏幕框就永远撤不掉(见 SlideClipOffIfIdle); 留着值则是给下一场
                // 留了个"凭空偏半屏"的坑。
                tr.BeginAnimation(TranslateTransform.XProperty, null);
                tr.X = 0;
                _slideTrs.Remove(tr);
                SlideClipOffIfIdle();
                onDone?.Invoke();
            };
            tr.BeginAnimation(TranslateTransform.XProperty, xa);
            // 【平移这一套全程实心, 不淡入】手机滑页时新的一页是**整块从屏幕外推进来**
            // 的, 它一直都在, 只是刚才不在画面里 —— 不是从空气里浮出来的。
            // 淡入的话有那么一段是两层半透明叠着走, 接缝处糊成一团, 而且"新页先亮一下
            // 再从 0 爬上来"这件事本身就是一记闪。
            body.BeginAnimation(OpacityProperty, null);
            body.Opacity = 1;
            return span;
        }
        else if (spin && dir < 0)
            SpinIn(body, sc, tr, ro, begin, dur);
        else
        {
            sc.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(s0, 1, dur) { EasingFunction = ease, BeginTime = begin });
            sc.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(s0, 1, dur) { EasingFunction = ease, BeginTime = begin });
            tr.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(dy, 0, dur) { EasingFunction = ease, BeginTime = begin });
        }
        // 【有先后就必须先把不透明度压到 0】动画还没起步的那一段用的是**本地值**,
        // 不压的话新的一页会先整块亮一下, 再从 0 淡进来 —— 那就是一记闪。
        body.BeginAnimation(OpacityProperty, null);
        body.Opacity = 0;
        var oa = new DoubleAnimation(0, 1, dur) { EasingFunction = ease, BeginTime = begin };
        if (onDone != null) oa.Completed += (_, _) => onDone();
        body.BeginAnimation(OpacityProperty, oa);
        return span * (spin ? FxKit.FxLag + FxKit.FxCut : 1.0);
    }

    double GoOut(FrameworkElement photo, ScaleTransform sc, TranslateTransform tr,
                 RotateTransform ro, int dir, double ms, Action onDone = null)
    {
        var ease = FxKit.EaseOut();
        bool spin = Spinning, slide = Sliding;
        double span = spin ? FxKit.MsSpin : slide ? FxKit.MsSlide : ms;
        var dur = FxKit.Ms(span * (spin ? FxKit.FxCut : 1.0));
        // 屏幕框挂上(见 ComeIn 里同一句)。两页并排在同一个容器里时, 两边挂到的是
        // 同一个爹 —— SlideClipOn 认得出来, 不会挂重。
        if (slide) SlideClipOn(photo); else SlideReset();
        // 往里走(+1): 旧页绕左下角逆时针转出去; 退回来(-1)以及「默认」那一套: 缩小下沉。
        // 旋转那一套里"退回来"的旧页是**沉下去**, 让位给"转回来"的上一层 —— 一个沉一个转,
        // 谁走谁来一眼就分得清(以前两张牌一起扫, 层次是糊的)。
        // 平移不讲"谁转谁沉": 旧页一律朝"来的方向的反面"横着滑走 —— 往里走就往左,
        // 退回上一层就往右, 和新页进来的方向正好对上(见 ComeIn)。
        if (slide)
        {
            // 【平移这一套全程实心, 不淡出、不糊、不压暗】旧的一页是**滑到屏幕外**
            // 才看不见的, 不是化掉的 —— 它一路走出框, 被那个屏幕框裁掉(见 SlideClipOn)。
            // 淡出的话它人还在框里就已经半透明了, 于是看得见新页从它身子底下透出来,
            // 两层叠着走 —— 那不是滑页, 是"溶解"。糊和压暗同理: 那两层是给"化掉"
            // 那两套做景深用的, 实心滑走的板子不该糊也不该暗(见 SlideFlatOn)。
            SlideFlatOn(photo);
            // 记下这一场演到什么时候(连按判断用, 见 SlideBusy), 并把这条位移登记上
            // —— 撤屏幕框要等场上都没人在滑(见 SlideClipOffIfIdle)。
            var end = DateTime.Now.Add(dur);
            if (end > _slideUntil) _slideUntil = end;
            SlideWatch(tr);
            var xa = new DoubleAnimation(0, -SlideSpan() * dir, dur) { EasingFunction = FxKit.EaseSlide() };
            // 收尾一并挂在这条上: 演完把横向那一位撒手归零(照片这一趟是**藏起来**
            // 而不是消失, 下一回再拿出来时横偏还留在它身上 —— 那就是"新的一页
            // 凭空偏半屏"), 再把糊 / 压暗还回去、屏幕框撤了、喊调用方那一摊
            // (收起旧照片等)。原来这些挂在淡出那条动画上, 现在不走淡出了。
            xa.Completed += (_, _) =>
            {
                tr.BeginAnimation(TranslateTransform.XProperty, null);
                tr.X = 0;
                // 【撒手之后才能销账】动画演完是"停在终值"的状态, 只要没撒手, 它就
                // 还算"身上有动画"(见 SlideClipOffIfIdle) —— 不撒手就永远撤不掉框。
                _slideTrs.Remove(tr);
                SlideFlatOff();
                SlideClipOffIfIdle();
                onDone?.Invoke();
            };
            tr.BeginAnimation(TranslateTransform.XProperty, xa);
            photo.BeginAnimation(OpacityProperty, null);
            photo.Opacity = 1;
            return span;
        }
        else if (spin && dir > 0)
            SpinOut(photo, sc, tr, ro, dur);
        else
        {
            sc.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, FxKit.ScalePageOut, dur) { EasingFunction = ease });
            sc.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, FxKit.ScalePageOut, dur) { EasingFunction = ease });
            tr.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, FxKit.DyPageOut, dur) { EasingFunction = ease });
        }
        // (平移那一套在上面就 return 了 —— 它不走淡出, 收尾挂在位移那条上。)
        var oa = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        if (onDone != null) oa.Completed += (_, _) => onDone();
        photo.BeginAnimation(OpacityProperty, oa);
        return span * (spin ? FxKit.FxCut : 1.0);
    }

    /// <summary>
    /// 新页浮上来(重的一档: 往里钻一层 / 退回上一层)。
    /// </summary>
    /// <param name="hold">演的时候锁不锁住操作。默认是锁的(进层 / 退层 —— 这时候让
    /// 用户按出"页面正在退、格子已经被改了"这种半个状态是要出事的);
    /// LB / RB 翻页传 false: 那是个能连着按的动作, 锁住就没法连翻了。</param>
    void RiseIn(LayerHost h, Border panel, int dir, bool hold = true)
    {
        if (hold) h.Anim = true;
        var (sc, tr, ro) = Parts(panel);
        ComeIn(panel, sc, tr, ro, dir, heavy: true, hold ? () => h.Anim = false : null);
    }

    /// <summary>
    /// **进设置**那一下的动画: 最上面那一层轻一点、沉半格, 再浮回来 + 淡入。
    /// 和 RiseIn 是同一套语言, 幅度砍半 —— 进设置是"换面"(九颗键变成九个格子),
    /// 幅度该比"压进来一层"更轻; 而且 RiseIn 演的是 180ms, 这里 170ms 收尾更干脆。
    /// 【动画做在 panel 上, 别做在格子上】格子那个 TransformGroup 是"C 光标浮起 / 按下
    /// 去"共用的(见 CellPop / SetCellDown); 此处正好赶在 ResetSettingsCursor 把中心
    /// 那一格浮起来之前, 两套动画抢同一个 ScaleTransform, 中心格会当场抽搐一下。
    /// </summary>
    void PopSetPanel(int dir)
    {
        var panel = _set.Panel;
        if (panel == null) return;
        var (sc, tr, ro) = Parts(panel);
        ComeIn(panel, sc, tr, ro, dir, heavy: false);
    }
    /// <summary>
    /// **回九键**那一下的动画 —— 和 PopSetPanel 完全对称(同一组数字、同一时长):
    /// 正面那张卡片轻一点、沉半格, 再浮回来 + 淡入。
    /// <para>【做在**整张卡片** Root 上, 不是九颗键身上】以前这里是做在 Grid9 上的,
    /// 于是"进设置"动的是整张背面卡片、"回九键"动的却是里面那九颗 —— 两张牌
    /// 看着不是一回事。现在两边都是 Root / Back 这两张对等的大卡, 进出是对等的。
    /// <br/>**也不能做在九颗键各自的格子上**: 那一层的 TransformGroup 是"C 光标
    /// 浮起 / 按下去"共用的(见 CellPop / SetCellDown), 两套抢同一个 ScaleTransform,
    /// 中心格会当场抽搐。整张卡片自己没有任何别的变换, 套上去是安全的。</para>
    /// <para>本体就是"新面孔进门"那一套(见 <see cref="FaceIn"/>) —— 同一个元素、
    /// 同一组数字, 于是"翻到设置""翻回九键""九键换一种面孔"这三次动的完全一致。</para>
    /// </summary>
    void PopKeyPanel(int dir) => FaceIn(dir);


    /// <summary>
    /// 旧页那张快照出门: 缩小、下沉、淡出。**它是道具不是页面** —— 演完就撤,
    /// Stage 里从头到尾只有新页那一块。以前干这活的是 SinkUnder(把真的旧层留在
    /// 场上压暗), 那样每往里一层底下就多一块 —— 界面上永远摞着好几层。
    /// </summary>
    void SinkLeave(LayerHost h, int dir)
    {
        var under = h.Veil;
        if (under == null) return;
        var (sc, tr, ro) = Parts(under);
        GoOut(under, sc, tr, ro, dir, FxKit.MsPageOut, () => HideVeil(h));
    }

    // ---------- 换页过渡那张"旧页的快照" ----------
    //
    // 把刚要走的那一页拍成一张图、糊掉、压暗, 垫在新页底下陪着演 180 毫秒,
    // 演完收起来。**它不再是"压着的下一层"** —— 下一层已经跟着换页整个摘掉了。
    // 一次换页**只算这么一回**(逐帧模糊在 WPF 里很贵, 那是另一回事); 拍的时候
    // 这张快照自己要先藏起来, 否则会把上一回的自己也拍进去, 越叠越糊。

    // ---------- 九键换面(数字层 / 标点 / 滑行 / JKL 换位) ----------
    //
    // 九颗键该换一副面孔了。**不再一颗颗翻** —— 2.1.13 起它走和设置那一摞
    // 完全同一条规矩: 先给旧的拍张照, 真身上立刻换成新面孔, 旧照片在底下陪演
    // 一场再撤。以前靠"每颗键自己翻半圈、趁着压扁那一瞬把字换掉"糊过去,
    // 有了那张照片就不需要这个偷手了 —— 换的东西观众看不见, 他看见的是照片。
    //
    // 【这里所有的动画都不锁任何输入】九键那一面归 Render 管, 不归 LayerHost,
    // 没有 h.Anim 那道闸。于是按住 LB 滑行的过程中, 演动画和推摇杆互不干扰 ——
    // 这是用户特意要的: "最好还做成动画效果不影响摇杆操作的"。

    // (2.1.15: FACE_CORNER_R 撤了 —— 那原本是给"照片是整张卡那么大的方图"擦屁股的:
    //  不按卡片的圆角裁一下, 四个直角会从圆角外面透出来。现在照片就拍内容区那一块、
    //  摆在内容区的格子里, 它自己又逾不出这个框, 没有角要擦。)

    // (2.1.14: FACE_LET / FACE_NUM / FACE_PUNCT / FACE_SLIDE 这四个"面孔串"连同
    //  _faceNow / _faceInited 一起撤了 —— "这一屏是什么"现在由 ScreenId 答,
    //  是一坨有名字的纯数据(见 ScreenId.cs), 不再是一个靠首字母胡乱拼的串。)

    /// <summary>
    /// 给**内容区**这会儿长什么样拍张图。
    /// 【2.1.15: 不再是整张卡片】以前拍的是 Root —— 连同背景底板、描边一起进照片,
    /// 于是换面那一场是"整张卡掀掉换一张", 看着就是整个方框闪一下。现在只拍框里
    /// 那一块(KeyArea): 九颗键 / 5x5 网格 / 窗口F 那一摞都在它里面, 卡片自己不进照片。
    /// 尺寸量不出来(卡片正翻着背面、或者还没排过版)就拿不到, 那就干脆不演 ——
    /// 和 Layer 那边一个规矩, 宁可静态切。
    /// (照片槽 FaceVeil 是 KeyArea 的**兄弟**, 不在这一格里, 用不着先把它藏起来。)
    /// </summary>
    BitmapSource SnapshotFace() => ShotPage(KeyArea, FaceVeil);

    /// <summary>
    /// 把"页面那一块"原样拍成照片 —— 换面 / 翻页 / 进层那一瞬的旧面孔都出自这里。
    /// <para>【为什么要绕一圈拍整张窗口】<c>RenderTargetBitmap.Render(el)</c> 会把 el
    /// **在它爹里的位置**也一起算进位图: 实测把内容区整体下移 40, 拍出来的图里内容
    /// 也跟着下移 40(uiprobe 的 Diag12 钉着这条)。于是"页面那一块"天生就比它自己低
    /// 那么一截:
    ///   · 快捷短语那一摞的舞台正好摆在 (0,0) —— 偏移是 0, 所以它从来都正常;
    ///   · 设置那一摞的舞台在 y≈30、正面的内容区在 (12,26) —— 这两摞的旧照片都比
    ///     真身低 26~30。2.1.30 之前照片是糊的(高斯 14)看不出来, 一改成实心就成了
    ///     用户报的"旧的一页先掉一截、再横着滑走"。
    /// 窗口自己是视觉树的根, 永远在 (0,0): 拍整张窗口、再裁出页面那一块, 位置就是准的。
    /// 顺带还有个好处 —— 拍到的就是屏幕上真看见的那一块, 不会漏掉压在上面的东西。</para>
    /// <param name="page">要拍的那一块(正面的内容区 / 某一摞的舞台)。</param>
    /// <param name="veil">摆照片的那个槽: 它自己也长在框上, 别把它拍进去。</param>
    /// </summary>
    BitmapSource ShotPage(FrameworkElement page, FrameworkElement veil)
    {
        if (page == null) return null;
        double pw = page.ActualWidth, ph = page.ActualHeight;
        if (pw < 16 || ph < 16) return null;
        int rw = (int)Math.Round(ActualWidth), rh = (int)Math.Round(ActualHeight);
        if (rw < 4 || rh < 4) return null;
        var box = page.TransformToVisual(this).TransformBounds(new Rect(0, 0, pw, ph));
        int x = (int)Math.Round(box.X), y = (int)Math.Round(box.Y);
        int cw = (int)Math.Round(box.Width), chh = (int)Math.Round(box.Height);
        if (x < 0 || y < 0 || cw < 4 || chh < 4 || x + cw > rw || y + chh > rh) return null;
        // 【照片槽自己别进照片】连着按的时候上一张还在场上, 拍进去就是"照片里套着
        // 上一张照片"。拍完照原样还回去(它那一摊归 GoOut 管)。
        var was = veil?.Visibility;
        if (veil != null) veil.Visibility = Visibility.Collapsed;
        try
        {
            var full = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
            full.Render(this);
            return new CroppedBitmap(full, new Int32Rect(x, y, cw, chh));
        }
        catch { return null; }
        finally { if (veil != null && was != null) veil.Visibility = was.Value; }
    }

    /// <summary>把旧页面那张照片收起来(连带着把那张位图松手)。</summary>
    void HideFaceVeil()
    {
        if (FaceVeil == null) return;
        FaceVeil.BeginAnimation(OpacityProperty, null);
        FaceVeil.Opacity = 0;
        FaceVeil.Visibility = Visibility.Collapsed;
        if (FaceVeilImg != null) FaceVeilImg.Source = null;
    }

    /// <summary>把**一张已经拍好的旧照片**摆进内容区那一格(KeyArea 的下面), 立刻开始演"出门"。
    /// <para>和设置那一摞的 ShowLeave 是同一件事的两个入口 —— 那边只有来自 SetStage
    /// 的照片要走这条路, 进出设置、换标点这两处的照片要摆到**正面**这一格来, 走的
    /// 是这里。观感和那边完全对齐: 同一级高斯模糊、同一个压暗色。</para>
    /// <para>尺寸照内容区给。**用 Width 不用 ActualWidth**: 刚从背面翻回正面那一帧
    /// 还没排过版, 量出来是 0, 那就按九键那块 360 摆, 反正它也就这么大。</para>
    /// </summary>
    bool ShowFacePhoto(BitmapSource snap, int dir)
    {
        if (FaceVeil == null || FaceVeilImg == null || snap == null) return false;
        double w = KeyArea.Width, h = KeyArea.Height;
        if (double.IsNaN(w) || w < 16) { w = KEY9_BOX; h = KEY9_BOX; }
        FaceVeil.Width = w;
        FaceVeil.Height = h;
        // 压暗色跟着主题走(和 EnsureVeil 里那块压暗层是同一个笔刷实例)。
        if (FaceVeilShade != null) FaceVeilShade.Background = VeilBrush;
        FaceVeilImg.Source = snap;
        FaceVeil.Visibility = Visibility.Visible;
        FaceVeil.BeginAnimation(OpacityProperty, null);
        FaceVeil.Opacity = 1;

        var (vsc, vtr, vro) = Parts(FaceVeil);
        // 上一场没演完就别从它的收尾姿势接着来(连着按 LB 时最容易撞上)
        ResetParts(vsc, vtr, vro);
        GoOut(FaceVeil, vsc, vtr, vro, dir, FxKit.MsFace, HideFaceVeil);
        return true;
    }

    // ================= 换屏管线 =================
    //
    // 每一帧 Render 就问一句"这张卡片上摆的还是不是刚才那一屏", 换了就演一场。
    // 分成两半、中间夹着整个 Render, 这个次序**就是全部**, 别图省事合到一起:
    //
    //     ① TickScreen   —— Render 的**第一行**: 认人 + 拍照
    //     ② Render 照旧往下画 —— 写可见性、写字、排行
    //     ③ TickScreenFx —— Render 的**最后一行**: 照片出门 + 真身进门
    //
    // 为什么拍照必须赶在最前面: RenderTargetBitmap.Render() 会**强制立刻渲染**
    // 当前这棵树 —— 拍的是"拍照这一刻树上长什么样", 不是"上一帧的样子"。
    // 而"9 宫格 -> 5x5 网格"这一跳换的是 Visibility(第 148 行那两行), 等走到
    // 原来拍照的位置(第 246 行)面板早长出来了 —— 于是整整空了一个版本没人演。
    //
    // 这条次序一旦守住, 以后再往屏幕上加任何一种"新的一屏"都不用回头问它
    // 该不该有动画: 它自动就有。

    /// <summary>这一帧的身份(见 <see cref="ScreenId"/>)。</summary>
    ScreenFace _screenNow;
    /// <summary>已经画在屏幕上那一帧的身份。和它对不上就是换了一屏。</summary>
    ScreenFace _screenPrev;
    bool _screenInited;
    /// <summary>这一帧换了屏, 收尾时要给新的一屏演一场进门。</summary>
    bool _screenFx;
    int _screenFxDir;

    /// <summary>
    /// 【管线第 ① 步】**必须是 Render 里第一行做的事** —— 理由见上面那段。
    /// 每帧都调, 所以自己认得很清楚: 同屏就扭头走, 别每帧重开一次动画。
    /// </summary>
    void TickScreen(InputCore c)
    {
        _screenFx = false;
        _screenNow = ScreenId.Of(c);

        // 设置面不归这条管线: 卡片翻到背面去了, 那一格里摆什么都由 ShowSide 亲手安排
        // (进出那两张照片也在那儿), 这里一律不插手。
        // 头一次调是刚亮起来那一帧: 屏幕上还什么都没有, 不算"换过"。
        // 【窗口F 归这条管线】(ScreenFace 里 Quick 那一位): F 那一摞**内部**翻层
        // 有自己的动画, 这里只看"这一摞在不在台上" —— 于是开 / 关 F 自动就有过渡,
        // 不用在别处再抄一遍这套流水线。(以前这里写着 _qkOn 就跳过, 于是进出 F
        // 成为全界面唯一一处没有换页动画的地方。)
        bool skip = c.Settings || !_screenInited;
        _screenInited = true;
        if (skip) { _screenPrev = _screenNow; return; }

        if (_screenNow == _screenPrev) return;
        var from = _screenPrev;
        _screenPrev = _screenNow;
        if (!ScreenId.WorthFx(from, _screenNow))
        {
            App.Log("换屏(没演): " + ScreenId.Diff(from, _screenNow));
            return;
        }

        _screenFxDir = ScreenId.DirTo(from, _screenNow);
        App.Log("换屏: " + ScreenId.Name(from) + " -> " + ScreenId.Name(_screenNow)
                + " dir=" + _screenFxDir);
        // 照片摆上去、立刻开始演"旧的一页怎么走"。
        // 拿不到照片就记一笔 —— 第 ③ 步还是会只给新的一屏演个淡入, 不至于硬切。
        _screenFx = true;
        // 【平移连按不重复拍照】上一场还在滑就别再 RenderTargetBitmap 一整页的图
        // (又贵又花); 更要紧的是此刻真身正带着位移, 拍下来的照片摆回原位是**错开
        // 一截**的。内容照样立刻生效、新的一屏照样演进门(见 ComeIn 里 slide 那一支),
        // 只是没有旧页陪着走那一程 —— 连着换屏本来也不该一张一张慢慢演。
        // 默认 / 旋转那两套照旧每张都拍: 它们的旧页是"化掉"的, 少一张就少一层。
        if (SlideBusy())
            App.Log("  (连着换屏, 这回只演进门)");
        else if (!TakeFacePhoto())
            App.Log("  (整卡的照片没拍着, 这回只演进门)");
    }

    /// <summary>
    /// 给"屏幕上此刻这一副骨架"拍张照片垫在下面, 并立刻开始演它的出门。
    /// 调用点必须还没动过任何界面 —— 见 <see cref="TickScreen"/> 上那段。
    /// </summary>
    bool TakeFacePhoto()
    {
        var snap = SnapshotFace();
        if (snap == null) return false;
        return ShowFacePhoto(snap, _screenFxDir);
    }

    /// <summary>
    /// 【管线第 ③ 步】Render 画完之后才演 —— 这时候 Root 上已经是新的一屏了,
    /// 从半透明 / 从歪着的角度陪着转回来, 观众看见的是"新页跟着进来"。
    /// </summary>
    void TickScreenFx()
    {
        if (!_screenFx) return;
        _screenFx = false;
        FaceIn(_screenFxDir);
    }

    /// <summary>
    /// 新面孔进门 —— 扑克牌那套: 从反方向同样角度转回来。
    /// 【2.1.15: 动的改成 KeyArea, 不再是整张卡 Root】
    /// 以前整张卡在这一刻被整体淡出淡入(连底板和描边一起), 于是"进标点 /
    /// 退回九键"看着都是整个方框换了一遍, 还伴一记闪 —— 因为它就是把 那张
    /// 背景也拽进来重画了。现在只有框里那一块在动, 卡片本身从头到尾站着不动,
    /// 于是和设置页里"钻一层"那一 look 味完全一致。旋转那套同理: 圆心跟着变成
    /// 内容区的左下角, 转的也就只是框里的东西。
    /// </summary>
    void FaceIn(int dir)
    {
        var (sc, tr, ro) = Parts(KeyArea);
        ComeIn(KeyArea, sc, tr, ro, dir, heavy: false);
    }

    // (2.1.14 删: SinkFace 以前是"卡片那一款出门"。和 SinkLeave 逐行对照下来
    //  只有时长不一样(换面 170 / 压进来一层 180), 现在统一由 GoOut 接手 ——
    //  轻重两档的数字见 InKit 那一组。)

    // (2.1.14 删: FacePlain 以前是 SetFace9 拿不到照片时的退路。现在管线自己记着
    //  拍没拍着(_screenFx 照样立起来), 拿不到照片就只给新的一屏演个进门。)

    // (2.1.13 删掉的那一套: AnimateFlip9 / WaveOf / _keyFlipAt / FaceOf9 ——
    //  "每颗键自己翻半圈、趁压扁那一瞬把字换掉"。整块有了快照之后它就没用了:
    //  换的内容观众看不见, 他看见的先是那张旧面孔的照片。FxKit 里 MsFlipHalf /
    //  MsFlipStep 两个数也没人取了, 暂时留着没删 —— 真删反而多一片要动的地方。)

    /// <summary>
    /// 框外面那一对 ◀LB / RB▶ 该不该亮: **当前这一页能不能翻页**。
    ///
    /// 三处都问这一句, 答复也只有一个标准 —— 这一页的**页数 > 1**:
    ///   · 符号面板开着 -> 按符号的分类数算;
    ///   · 窗口F 开着   -> 按它当前那一页算(列表层五页);
    ///   · 翻到背面     -> 按设置当前那一层算(只有摆不下的那些层才有第二页)。
    /// 三处互斥, 所以顺序无所谓; 万一同时成立(不该发生)也只亮同一对小牌。
    /// 每帧问一次, 于是"翻到最后一页还有没有下一页"这种事不用记状态。
    /// </summary>
    Border _fxCur;
    string _fxId = "";
    /// <summary>_fxCur 这颗键当前是不是正被按着。按下/松开之间不能重跑弹起动画(见 FxSet)。</summary>
    bool _fxPressed;

    /// <summary>
    /// 哪些手柄键算"确认了光标选中的这一项"。**只有 A** —— 缩一下/变暗是在说
    /// "这一项被摁下去了", 那这个动作就得真的落在光标选中那一项身上。
    /// <para>1.0.8 里把 B / X / Y / LB / RB 也算进去了, 结果是按 X 退格、按 B 取消
    /// 时光标那颗键也跟着缩 —— 可那两下根本没碰它, 缩了纯属误导。其余几个同理:
    /// Y 是切分、LB/RB 是换切分和发拼音, 动的都不是光标下这一颗(九键上光标在键上,
    /// 拼音行/网格里的光标却在另一处), 一起缩就成了"按哪儿都在缩同一颗"。</para>
    /// <para>十字键更不算: 它是挪光标, 算进去的话按住方向键时每挪一格就重弹一次,
    /// 看着像抖。L3 / R3 也不算 —— 前者配 R3 是唤出/隐藏、单独按是切中英(松手才触发),
    /// 后者是设置和调大小, 都不是在敲键。</para>
    /// </summary>
    const ushort PRESS_MASK = GamepadInput.A;

    /// <summary>
    /// 按下时缩到多大。**比常规尺寸(1.0)略小** —— 键是"被摁进去"的, 不是"浮起来"，
    /// 所以落点要压在正常大小以下, 才和"选中放大"形成一对相反的手感。
    /// </summary>
    // PRESS_SCALE 已搬进 FxKit.ScalePress(0.88)

    /// <summary>动态效果关着时, 按下的那颗压到几成亮度代替缩放。</summary>
    // PRESS_DIM 已搬进 FxKit.DimPress(0.5)

    // 影子不做动画, 而且同时只有一颗键在用, 一个实例共用就行
    static readonly DropShadowEffect FxShadow = new()
    {
        Color = Colors.Black, BlurRadius = 11, ShadowDepth = 3, Direction = 300, Opacity = 0.55,
    };

    // 贴纸风的"浮起来"不是影子而是柔光圈: 白色光晕包着那颗键, 像贴纸被点亮。
    // ShadowDepth=0 光才从四周出来, 带偏移就变成一侧的投影了。
    static readonly DropShadowEffect FxGlow = new()
    {
        Color = Colors.White, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.95,
    };

    /// <summary>浮字模式的"描边": 黑色细晕把白字从任何亮度的背景里抠出来。
    /// 挂在容器(OuterGrid/Back)上, 整层文字一次搞定, 比挨个 TextBlock 挂便宜得多。</summary>
    static readonly DropShadowEffect FxOutline = new()
    {
        Color = Colors.Black, BlurRadius = 3.2, ShadowDepth = 0, Opacity = 0.95,
    };

    /// <summary>
    /// 指定"现在该浮起来的那颗键"。id 没变就不动; 传空串 + null 表示"没有"。
    /// <para><paramref name="down"/> 是这颗键正被按住(手柄某个键按着)。id 和 down
    /// 都没变才跳过 —— 按下/松开要能单独触发, 否则"按住不动"这段时间什么反馈都没有。</para>
    /// </summary>
    void FxSet(string id, Border b, double scale = FxKit.ScalePop, bool down = false)
    {
        bool sameId = id == _fxId;
        if (sameId && down == _fxPressed) return;
        if (!sameId)
        {
            if (_fxCur != null) FxRest(_fxCur);
            _fxId = id;
            _fxCur = b;
            if (b != null) FxPop(b, scale);
        }
        _fxPressed = down;
        // 焦点没换、只是按下/松开: 只改缩放, 不重跑弹起(重跑的话按住不动会一直弹)。
        // 焦点刚换过来时也走一遍 —— 极端情况下(按着键的同时焦点被换掉)新键该是缩小的。
        if (b != null && down) FxDown(b, true, scale);
        else if (sameId && b != null) FxDown(b, false, scale);
    }

    /// <summary>
    /// 按下/松开的缩放。按下时缩到比常规还小的 FxKit.ScalePress,
    /// 松开弹回"浮起来"的那个尺寸。按住期间只有开头这一次动画, 之后一直停在缩小态,
    /// 松手才回去 —— 和真键盘按下去的手感一样。
    /// </summary>
    void FxDown(Border b, bool down, double scale)
    {
        if (b.RenderTransform is not TransformGroup tg
            || tg.Children[0] is not ScaleTransform sc) return;

        var ease = FxKit.EaseOut();
        // 按下去要跟手(短), 松手回来稍慢一点, 才像"弹回原位"
        var span = FxKit.Ms(down ? FxKit.MsDown : FxKit.MsUp);
        double to = down ? FxKit.ScalePress : scale;
        sc.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(to, span) { EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(to, span) { EasingFunction = ease });
    }

    /// <summary>
    /// 弹起来: 先冲过头一点再回落(那一下"弹"), 同时左右摆两下再停(那一下"晃"),
    /// 最后停在放大后的尺寸上 —— 放大一点点, 刚好压住旁边的键, 看着就是浮在它们上面。
    /// 用关键帧而不是 Completed 里接第二段: 关键帧自己会停在该停的地方, 不用看动画
    /// 的完成通知(那个通知在窗口忙着的时候可能不来, 一不来键就永远卡在大一号上)。
    /// </summary>
    void FxPop(Border b, double scale)
    {
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        if (b.RenderTransform is not TransformGroup tg
            || tg.Children[0] is not ScaleTransform || tg.Children[1] is not RotateTransform)
        {
            tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(1, 1));
            tg.Children.Add(new RotateTransform(0));
            b.RenderTransform = tg;
        }
        var sc = (ScaleTransform)tg.Children[0];
        var ro = (RotateTransform)tg.Children[1];

        // Z 序必须抬起来, 否则它只是"变大了", 还是被邻居盖在下面, 看不出浮起来。
        LiftZ(b, true);
        b.Effect = (SkinOn && SkinTex.Available) ? FxGlow : FxShadow;

        var ease = FxKit.EaseOut();
        for (int i = 0; i < 2; i++)
        {
            var sk = new DoubleAnimationUsingKeyFrames();
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(scale * FxKit.PopOver,
                FxKit.At(FxKit.PopAt0), ease));
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(scale * FxKit.PopBack,
                FxKit.At(FxKit.PopAt1)));
            sk.KeyFrames.Add(new EasingDoubleKeyFrame(scale,
                FxKit.At(FxKit.PopAt2)));
            sc.BeginAnimation(i == 0 ? ScaleTransform.ScaleXProperty : ScaleTransform.ScaleYProperty, sk);
        }

        var rk = new DoubleAnimationUsingKeyFrames();
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(-FxKit.WobDeg,
            FxKit.At(FxKit.WobAt0), ease));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(FxKit.WobDeg * FxKit.WobBack1, FxKit.At(FxKit.WobAt1)));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(-FxKit.WobDeg * FxKit.WobBack2, FxKit.At(FxKit.WobAt2)));
        rk.KeyFrames.Add(new EasingDoubleKeyFrame(0, FxKit.At(FxKit.WobAt3)));
        ro.BeginAnimation(RotateTransform.AngleProperty, rk);
    }

    /// <summary>把浮着的那颗放回去: 缩回原样、撤掉影子、Z 序还原。</summary>
    void FxRest(Border b)
    {
        LiftZ(b, false);
        b.Effect = null;
        if (b.RenderTransform is not TransformGroup tg
            || tg.Children[0] is not ScaleTransform sc || tg.Children[1] is not RotateTransform ro) return;

        var ease = FxKit.EaseOut();
        var span = FxKit.Ms(FxKit.MsRest);
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, span) { EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, span) { EasingFunction = ease });
        ro.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, span) { EasingFunction = ease });
    }

    // ================= 浮力: 这一屏的"键"全都浮在水面上 =================
    //
    // 换页动效选了「浮力」之后, 屏幕上那一批键就常驻这一层水波: 全体放大一点、
    // 各自按自己的周期缓慢漂动(周期 3~4 秒、彼此错开相位), 靠边那几个会蹭出框外
    // 一点点 —— 那是"它们是浮在框上面的"唯一的证据。
    //
    // 【挂在**键外面那层容器**上, 不挂在键自己身上】键身上那套 TransformGroup 是
    // "光标浮起 / 按下去"专用的(见 FxPop / FxDown), 抢过去就两套互相打断 —— 键上一秒
    // 还在放大、下一秒就被水波拽回去, 光标一走更是直接被 FxRest 归到 1, 浮力那点
    // 放大就永久丢了。外面这层是键的**爹**, 爹的变换和儿子的变换是叠上去的,
    // 谁也不覆盖谁: 九键用的是 _key9Host(2.1.13 之后一直空着), 网格那 40 格和
    // 设置 / 窗口F 的每一格是 FillPanel 现套的(见 LayerHost.Hosts)。
    //
    // 【每一屏都漂, 全键盘那一屏除外】谁露在屏幕上谁漂 —— 九键那九颗、标点 5×5
    // 那 25 格、设置与窗口F 里的每一格(包括 789 合成的那条长条、"关于"并成的
    // 一大块)全都算。换屏自动换一批: 老的那批归位、新的一批起浮, 不用各处去记。
    // 全键盘那七排键太多太密(而且它的键装在 StackPanel 排里, 没有外层容器), 不做。
    //
    // 【换页那一套一个数都不动】浮力只在"常驻"这一层上; 进设置、换面、LB / RB 翻页
    // 照旧是「默认」那套浮起淡入 —— 换页只问"是不是旋转 / 是不是平移", 两样都不是
    // 就走默认那条路, 所以这一条是天生的, 不用加分支。

    /// <summary>
    /// 浮力那层水波此刻挂在哪些元素上(值没用, 当集合用)。
    /// <para>记着它是为了只在**名单变了**的那一帧动一次: 循环动画每帧重开一次,
    /// 那批键就变成一直在原地抖(和 FxSet 那套同款毛病)。</para>
    /// <para>名单里存的是**外层容器**, 于是换页重搭格子(新对象)会自动被认成"新的一批"
    /// —— 起浮那一下正好和换页演在一起。</para>
    /// </summary>
    readonly Dictionary<FrameworkElement, byte> _buoy = new();

    /// <summary>
    /// 每帧问一次: 这一屏该谁漂。名单没变就什么都不做(见 <see cref="_buoy"/>)。
    /// </summary>
    void SyncBuoy()
    {
        var want = Buoying ? BuoyTargets() : new List<(FrameworkElement El, double Px)>();

        // 先归位: 上一帧还在名单上、这一帧不在了的(换屏了 / 关掉浮力了 / 那一摞撤了)
        if (_buoy.Count > 0)
        {
            var keep = new HashSet<FrameworkElement>();
            foreach (var w in want) keep.Add(w.El);
            List<FrameworkElement> gone = null;
            foreach (var el in _buoy.Keys)
                if (!keep.Contains(el)) (gone ??= new List<FrameworkElement>()).Add(el);
            if (gone != null)
                foreach (var el in gone) { _buoy.Remove(el); BuoyOne(el, FxKit.BuoyRef, 1, false); }
        }
        if (want.Count == 0) return;

        for (int i = 0; i < want.Count; i++)
        {
            var (el, px) = want[i];
            if (_buoy.ContainsKey(el)) continue;
            _buoy[el] = 1;
            // 位号从 1 起 —— 下面拿它算"第几颗", 九键那九颗拿到的和以前一格不差
            BuoyOne(el, px, i + 1, true);
        }
    }

    /// <summary>
    /// 这一屏**露在屏幕上**的那批键(连着它们各自多大), 换屏就换一批。
    /// <para>【按"看得见"排先后, 不是按"谁更重要"】九键收起来(符号网格 / 全键盘 /
    /// 窗口F 盖上来)和翻到背面(设置), 那两种都不算 —— 翻到背面时 Grid9 自己的
    /// Visibility 还是 Visible(它只是跟着 Root 一起被收起来了), 光看它会以为九键
    /// 还在屏幕上, 于是一面看不见的水波在那里白漂。</para>
    /// </summary>
    List<(FrameworkElement El, double Px)> BuoyTargets()
    {
        var t = new List<(FrameworkElement El, double Px)>();
        // 九键那一面(含数字 / 标点 / 滑行那几副面孔 —— 它们都还是这九颗键)
        if (!Flipped && Grid9.Visibility == Visibility.Visible)
        {
            for (int i = 1; i <= 9; i++)
                BuoyAdd(_key9Host[i], t);
            return t;
        }
        // 窗口F 那一摞(它盖在键盘区上, 底下那套键此刻是收着的)
        //
        // 【认 _qkOn, 不认 _qk.Panel】那一摞的面板**一开机就搭好了**摆在 Stage 里
        // (随时能翻出来), 只看 Panel 会以为它一直在屏幕上 —— 于是九键永远排不到
        // 第一名, 水波全被那一摞抢走。认"这一摞此刻是不是在演"(见 Quick.cs)。
        if (_qkOn) { BuoyAddLayer(_qk, t); return t; }
        // 设置那一摞(背面)。同理认 Flipped: 面板是预先搭好的。
        if (Flipped) { BuoyAddLayer(_set, t); return t; }
        // 标点 5×5 / 拼音整串 那一整页网格(全键盘不算: 那七排没外层容器)
        if (!Flipped && CandPanel.Visibility == Visibility.Visible
            && Grid26.Visibility != Visibility.Visible)
        {
            for (int i = 0; i < _gridHost.Length; i++)
                BuoyAdd(_gridHost[i], t);
            return t;
        }
        return t;
    }

    /// <summary>一个元素加进名单 —— 量不出它多大(还没排过版)的先跳过, 下一帧再说。</summary>
    static void BuoyAdd(FrameworkElement el, List<(FrameworkElement El, double Px)> t)
    {
        if (el == null) return;
        double px = ElPx(el);
        if (px > 0) t.Add((el, px));
    }

    /// <summary>设置 / 窗口F 那一页的每一格, 连着它们各自多大(照摆法表算, 见 TilePx)。</summary>
    void BuoyAddLayer(LayerHost h, List<(FrameworkElement El, double Px)> t)
    {
        var p = h.Top;
        for (int i = 0; i < h.Hosts.Count; i++)
            t.Add((h.Hosts[i], p != null ? TilePx(p, i) : FxKit.BuoyRef));
    }

    /// <summary>
    /// 一个元素现在多大(拿**外层**的边长当基准 —— 它比看得见的那颗键大一圈缝)。
    /// <para>【量不到就回 0, 不许退回参考尺寸】标点网格那一整页是"要用才显示"的:
    /// 它露脸的**第一帧**那 40 格还没排过版, ActualWidth 是 0。这时候退回参考尺寸
    /// 会把 64 见方的小格当成 112 来漂 —— 4 像素, 两个邻居迎面一撞就贴上了。
    /// 回 0 的那一格这一帧先不漂, 下一帧(16 毫秒后)版面已经排好, 自然就补上了。</para>
    /// </summary>
    static double ElPx(FrameworkElement el)
    {
        double px = Math.Min(el.ActualWidth, el.ActualHeight);
        return px > 16 ? px : 0;
    }

    /// <summary>
    /// 给**一个**元素套上(或摘掉)那层水波。开 = 起浮 + 循环漂动; 关 = 归位。
    /// <para>漂多远 / 放大多少由 <c>px</c>(这一格多大)折算, 九键的键、设置的一格、
    /// 标点网格里的小格才看着是同一个动作(见 FxKit.BuoyScaleAt / BuoyAmpAt)。</para>
    /// </summary>
    void BuoyOne(FrameworkElement host, double px, int slot, bool on)
    {
        var span = FxKit.Ms(FxKit.MsBuoy);
        var ease = FxKit.EaseOut();
        // Parts 会顺手把 TransformGroup(缩放 / 位移 / 旋转)建好 —— 这层容器
        // 建界面时是空的, 第一次用到才补上, 和别处一个规矩。
        var (sc, tr, _) = Parts(host);
        double to = FxKit.BuoyScaleAt(px);
        // 缩放: 开就浮起来, 关就落回 1。这一条不循环 —— 它是"浮起来"这一件事,
        // 演完停在那儿; 循环的是下面那条位移。
        sc.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(on ? to : 1, span) { EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(on ? to : 1, span) { EasingFunction = ease });
        if (!on)
        {
            // 关: 位移回到正中, 循环动画一并撒手(不撒手它就一直停在"有动画"的
            // 状态, 下一回再开是接着上一场的数往下走, 起点就不对了)。
            tr.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, span) { EasingFunction = ease });
            tr.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, span) { EasingFunction = ease });
            return;
        }
        // 开: 各有自己的周期(在 BuoyMsMin ~ BuoyMsMax 之间各占一格, 超过九个就
        // 从头轮回 —— 标点网格有 25 格, 不轮回的话后面那几格要七八秒才荡一轮)和
        // 相位(黄金角 0.618 依次错开 —— 比"每颗加一点"更不像队形)。
        double t = (slot - 1) % 9;
        double ms = FxKit.BuoyMsMin + t * (FxKit.BuoyMsMax - FxKit.BuoyMsMin) / 8.0;
        double ph = (slot * 0.618) % 1.0;
        double dx = FxKit.BuoyAmpAt(px, FxKit.BuoyDx);
        double dy = FxKit.BuoyAmpAt(px, FxKit.BuoyDy);
        var wx = BuoyWave(dx, ms, ph);
        var wy = BuoyWave(dy, ms, ph + FxKit.BuoyLagY);
        // 【先"送到位"再接循环】循环波形的第 0 帧已经偏在起点上了(最多半个
        // 振幅), 当场接上去就是一记硬跳。于是先用同一条 260 毫秒的曲线把这一格从
        // 正中送到那个起点(这一小段顺带把"浮起来"演完), 到了才换上循环。
        double x0 = dx * Math.Sin(2 * Math.PI * ph);
        double y0 = dy * Math.Sin(2 * Math.PI * (ph + FxKit.BuoyLagY));
        var ax = new DoubleAnimation(x0, span) { EasingFunction = ease };
        var ay = new DoubleAnimation(y0, span) { EasingFunction = ease };
        int left = 2;
        void Land(object sender, EventArgs e)
        {
            if (--left > 0) return;
            // 这 260 毫秒里这一格要是不在名单上了(换屏了 / 关掉浮力了 / 连着改设置),
            // 就别再接上去了。
            if (!_buoy.ContainsKey(host)) return;
            tr.BeginAnimation(TranslateTransform.XProperty, wx);
            tr.BeginAnimation(TranslateTransform.YProperty, wy);
        }
        ax.Completed += Land;
        ay.Completed += Land;
        tr.BeginAnimation(TranslateTransform.XProperty, ax);
        tr.BeginAnimation(TranslateTransform.YProperty, ay);
    }

    /// <summary>
    /// 一条"浮在水面上"的位移曲线: 一个完整周期的正弦, 采 FxKit.BuoyKeys + 1 个点。
    /// <para>首尾落在**同一个数**上(第 0 帧和第 N 帧都是 <c>A·sin(2πφ)</c>), 所以
    /// 循环接得严丝合缝; 又因为一个周期下来均值是 0, 键是**围着自己原位**在漂 ——
    /// 这两条都要紧: 接不上就是一跳, 带偏移就是九宫格整体歪着漂(见 FxKit 里那段)。</para>
    /// </summary>
    static DoubleAnimationUsingKeyFrames BuoyWave(double amp, double ms, double phase)
    {
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        for (int k = 0; k <= FxKit.BuoyKeys; k++)
        {
            double p = k / (double)FxKit.BuoyKeys;
            a.KeyFrames.Add(new LinearDoubleKeyFrame(amp * Math.Sin(2 * Math.PI * (p + phase)),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * p))));
        }
        return a;
    }

}
