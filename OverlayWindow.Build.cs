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
// ---- 第一次把界面零件造出来(九键/26 键/候选行/拼音行) ----
//
// 2.1.11 之前这些代码全在 OverlayWindow.xaml.cs 一个文件里(5117 行)。
// 这次只是按职责把它拆成几个 partial class 文件 —— 一行都没改,
// 编译出来的行为跟拆之前完全一样(改名/搬位置不影响任何逻辑)。

    /// <summary>
    /// 贴纸风那四色, 按格子顺序轮换(相邻的不重色)。
    /// <para>【2.1.36】以前这串 switch 在下面抄了三份(26 键的每一颗、候选行、
    /// 候选网格各一份) —— 换配色要动三处, 漏一处就是"有的格子换了新的、有的还是旧的",
    /// 而且这种错只在贴纸皮上、只在真机上看得见。现在就这一份。</para>
    /// </summary>
    static string SkinTone(int i) => (i & 3) switch
    {
        0 => "pink", 1 => "mint", 2 => "cream", _ => "lavender",
    };

    readonly Border[] _key9 = new Border[10];
    /// <summary>
    /// 九键每颗键外面那层"抬层容器"。换键面(字母 / 数字 / 标点 / 滑行)时转的是它,
    /// 键自己那层留给"浮起来 / 按下去"那套动效 —— 两套动画各转各的, 抢同一个
    /// RenderTransform 会互相打断(键上一秒还在放大, 下一秒就被翻面压回去了)。
    /// 2.1.13 删掉"每颗键自己翻半圈"之后它一直空着, 2.1.36 起浮力那层水波挂在它上面。
    /// </summary>
    readonly Grid[] _key9Host = new Grid[10];

    /// <summary>
    /// "这一层是某颗键 / 某格的外层容器, 抬 Z 序的时候要连它一起抬"。
    /// <para>只抬里面的键是不够的: 它整层还压在邻居的容器下面, 放大了照样被盖住。
    /// 于是 FxPop / CellPop 抬 Z 序时顺手问一句"我爹是不是打了这个标记"。</para>
    /// <para>【不只九键用】候选网格那 40 格、设置与窗口F 的每一格外面也套了同样
    /// 一层(浮力要用), 打的也是这个标记 —— 见 LIFT_HOST 这个名字: 它说的是
    /// "抬谁", 不是"翻谁"。</para>
    /// </summary>
    const string LIFT_HOST = "lift";
    readonly TextBlock[] _key9Num = new TextBlock[10];
    readonly TextBlock[] _key9Sub = new TextBlock[10];
    /// <summary>九键每颗键上那块"果冻高光"。建键盘时就埋好, 平时透明。</summary>
    readonly Border[] _key9Gloss = new Border[10];

    // ---------- 皮肤贴图 ----------
    //
    // "可爱粉"主题的键不是纯色画出来的, 是贴图(见 SkinTex / SkinBg):
    //   九键  -> 花瓣贴图(petal_*, 整图铺, 键近似正方拉不坏)
    //   其他键 -> 三段式胶囊(key_*_L/M/R, 长条键拉了也不变形)
    //   轮盘  -> 圆片贴图(badge_*)
    // 贴图跟笔刷一样是"每帧 Paint 现刷"的: 换主题 / 换选中态都不用重建键盘。
    // 目录缺失时 SkinTex 返回 null, 全部自动退回纯色画法。
    /// <summary>长条键(26键/候选/格子/0键)的三段式贴图层, Border -> 它自己的背景层。</summary>
    readonly Dictionary<Border, SkinBg> _skins = new();
    /// <summary>九键花瓣键的普通配色(1~9 -> pink / mint / lavender), 选中统一变薄荷。</summary>
    static readonly string[] PetalColor =
        { "", "pink", "mint", "pink", "lavender", "mint", "lavender", "mint", "pink", "mint" };

    readonly Border[][] _en = new Border[Keyboard26.RowCount][];
    readonly TextBlock[][] _enText = new TextBlock[Keyboard26.RowCount][];

    readonly Border[] _candRow = new Border[InputCore.CandPerRow];
    readonly TextBlock[] _candText = new TextBlock[InputCore.CandPerRow];

    readonly Border[] _grid = new Border[40];
    readonly TextBlock[] _gridText = new TextBlock[40];
    /// <summary>
    /// 网格(标点 5×5 / 拼音整串)每格外面那层容器, 和 <see cref="_key9Host"/> 一个用处:
    /// 格子的 RenderTransform 归"光标浮起 / 按下去", 浮力那层水波挂在这外面。
    /// 不用的时候它是空的(只多一层不参与任何布局计算)。
    /// </summary>
    readonly Grid[] _gridHost = new Grid[40];


    readonly Border[] _py = new Border[InputCore.MaxPinyinPerRow];
    TextBlock _pyMore;                       // 拼音行末尾那个"还有更多"的 …
    readonly TextBlock[] _pyText = new TextBlock[InputCore.MaxPinyinPerRow];

    Ellipse _dot;
    Line _line, _crossH, _crossV;
    Border _key0;                        // 数字层的 0, 浮在 8 号键下方
    TextBlock _key0Text, _key0Sub;

    UIElement SkinWrap(Border owner, UIElement content, string color)
    {
        var bg = new SkinBg();
        bg.Set(color);
        _skins[owner] = bg;
        owner.Tag = color;             // 普通态就穿自己的颜色, 选中/点亮才换
        var g = new Grid();
        g.Children.Add(bg);
        g.Children.Add(content);
        return g;
    }

    // ---------- 构建 ----------
    void BuildKey9()
    {
        for (int i = 1; i <= 9; i++)
        {
            var b = Box(10);
            b.SetValue(PetalProperty, true);   // 可爱粉下它就是花瓣, 不跟着圆形风格变(见 ShapeRound)
            // 花瓣贴图的普通花色跟着 Tag 走(Paint 里读): 粉/薄荷/淡紫照着底板排
            b.Tag = PetalColor[i];
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var t1 = Text(24, Fg);
            var t2 = Text(11, Sub);
            t2.Margin = new Thickness(0, 2, 0, 0);
            sp.Children.Add(t1);
            sp.Children.Add(t2);

            // 果冻高光: 贴在键上半截的一块白色渐变。做法是"一直在、只是透明",
            // 而不是"切到贴纸风时才现加一层" —— 换主题不该重建整块键盘,
            // ApplyTheme 里只改它的 Opacity 就行。
            var gloss = new Border
            {
                Height = 40,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(18, 18, 7, 7),
                IsHitTestVisible = false,
                Opacity = 0,
                Background = Grad(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF),
                                  Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF))
            };
            var wrap = new Grid();
            wrap.Children.Add(gloss);
            wrap.Children.Add(sp);
            b.Child = wrap;

            var host = new Grid
            {
                RenderTransformOrigin = new Point(0.5, 0.5),
                Tag = LIFT_HOST,      // FxPop 抬 Z 序时要连它一起抬(见那里)
            };
            host.Children.Add(b);
            Grid9.Children.Add(host);
            _key9[i] = b; _key9Num[i] = t1; _key9Sub[i] = t2; _key9Gloss[i] = gloss;
            _key9Host[i] = host;
        }
    }

    /// <summary>
    /// 26 键全键盘。键位、宽度倍数全在 Keyboard26 里, 这里只负责把它们摆成七排。
    /// 一个"单位宽"是 KEY26_U 像素; 真键盘上 Tab / Caps / Shift / 回车 / 空格
    /// 都比字母键宽, 倍数照抄过来, 一眼就能看出是块键盘。
    /// </summary>
    // 2.1.0: 全键盘的高从 292 拉到 360(和九键等高), 键也跟着放大一圈。
    //
    // 【这两个数是量出来的, 不是挑出来的 —— 别再往上调】以前 U=41 / H=45 看着"差一点点
    // 就填满", 结果**左右两端和上下各被切掉一小截**。原因是每颗键四面各有 2 的缝
    // (KeyBox 的 Margin), 算宽高的时候漏了这一项:
    //   宽: 最宽那排 15.25 个单位 × 41 = 625, 加上 10 颗键 × 4 = **665 > 640**(超出 25)
    //   高: 七排 × (45 + 4) + 排间 7 × 6 = 343 + 42 = **385 > 360**(超出 25)
    // 两边都正好超出 25, 也就是上下左右各被吃掉 12 —— 正是"左右两端、上下一小点被切掉"。
    // 现在把缝算进去反推:
    //   宽: 15.25U + 40 ≤ 640  ->  U ≤ 39.3  ->  取 38(留一圈边, 最宽那排 619.5)
    //   高: 7 × (H + 4 + 6) ≤ 360  ->  H ≤ 41.4  ->  取 40(七排共 350)
    const double KEY26_U = 38;    // 标准键宽(像素, 未缩放)
    const double KEY26_H = 40;    // 键高

    void BuildKey26()
    {
        for (int row = 0; row < Keyboard26.RowCount; row++)
        {
            int n = Keyboard26.Len(row);
            _en[row] = new Border[n];
            _enText[row] = new TextBlock[n];
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3)
            };
            for (int col = 0; col < n; col++)
            {
                var k = Keyboard26.At(row, col);
                var b = KeyBox(KEY26_U * k.W, KEY26_H);
                // F 行 13 个键最窄, 字也要小一号; 宽键(Tab/Caps/Shift/空格)字小一点更好看
                double fs = row == 0 ? 10.5 : (k.W > 1.2 ? 12 : 14);
                var t = Text(fs, Fg);
                t.TextAlignment = TextAlignment.Center;
                // 贴纸风: 键底是三段式贴图胶囊, 文字浮在贴图上。四色按 行+列 轮换
                b.Child = SkinWrap(b, t, SkinTone(row + col));
                line.Children.Add(b);
                _en[row][col] = b;
                _enText[row][col] = t;
            }
            Grid26.Children.Add(line);
        }
        // 宽度**钉死成内容区的宽**, 不跟着 KeyArea 走: 换布局"左右扩张"是给这一层套
        // ScaleTransform 做出来的(见 FlipLayout), 它必须始终按 640 排好版,
        // 缩放只是把它压到当前宽度再放开 —— 不然七排会跟着 KeyArea 重新排,
        // 动画里就变成"键在挪位"而不是"整块撑开"。
        Grid26.Width = KEY26_BOX_W;
        Grid26.RenderTransform = new ScaleTransform(1, 1);
        // 缩放**按当前内容宽度给个初值**: 它现在由 OnContentW 回调驱动, 而回调只在
        // 值**变化**时才跑 —— 开局 ContentW 已经是默认的 360, 不钉这一下缩放会停在
        // 1(也就是按 640 排的那七排原样摆着), 直到第一次换布局才对上。
        SyncGrid26Scale();
    }

    void BuildCandRow()
    {
        for (int i = 0; i < InputCore.CandPerRow; i++)
        {
            var b = Box(8);
            var t = Text(17, Fg);
            b.Child = SkinWrap(b, t, SkinTone(i));
            CandRow.Children.Add(b);
            _candRow[i] = b; _candText[i] = t;
        }
    }

    void BuildCandGrid()
    {
        for (int i = 0; i < _grid.Length; i++)
        {
            // 不设固定高度: 25 格排 5 列 5 行, 由 UniformGrid 均分 360×360
            var b = Box(8);
            var t = Text(16, Fg);
            // 格子宽度由 UniformGrid 均分, 拼音整串比单个词长, 必须允许折行,
            // 不然 "shou'dao" 会被硬生生切掉尾巴
            t.TextWrapping = TextWrapping.Wrap;
            t.TextAlignment = TextAlignment.Center;
            b.Child = SkinWrap(b, t, SkinTone(i));
            // 外面再套一层(浮力挂它身上, 格子里那套变换留给"光标浮起 / 按下去")。
            // 这一层**不写任何尺寸**: 它跟着 UniformGrid 分到的那一格撑满, 里面的
            // Border 照旧留自己的 4 像素缝 —— 于是格子的位置和大小和套之前一格不差。
            var host = new Grid
            {
                RenderTransformOrigin = new Point(0.5, 0.5),
                Tag = LIFT_HOST,      // FxPop 抬 Z 序时要连它一起抬(见那里)
            };
            host.Children.Add(b);
            GridCand.Children.Add(host);
            _grid[i] = b; _gridText[i] = t; _gridHost[i] = host;
        }
    }

    void BuildStickLayer()
    {
        _line = new Line { Stroke = ActiveEdge, StrokeThickness = 1, Opacity = 0.55, StrokeDashArray = new DoubleCollection { 5, 4 } };
        _crossH = new Line { Stroke = Sub, StrokeThickness = 1, X1 = -7, X2 = 7 };
        _crossV = new Line { Stroke = Sub, StrokeThickness = 1, Y1 = -7, Y2 = 7 };
        _dot = new Ellipse { Width = 14, Height = 14, Fill = Accent, Opacity = 0.9 };
        StickLayer.Children.Add(_line);
        StickLayer.Children.Add(_crossH);
        StickLayer.Children.Add(_crossV);
        StickLayer.Children.Add(_dot);
    }

    void BuildPinyinRow()
    {
        for (int i = 0; i < InputCore.MaxPinyinPerRow; i++)
        {
            var b = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(9, 2, 9, 2),
                Margin = new Thickness(3, 2, 3, 2),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Visibility = Visibility.Collapsed
            };
            ShapeBase(b, 5);
            var t = Text(15, Pinyin);
            // 单个格子里也允许折行: WrapPanel 只管"格子之间"换行,
            // 万一单条拼音本身就超过 360px(比如三个音节的整串), 得靠它兜住
            t.TextWrapping = TextWrapping.Wrap;
            t.TextAlignment = TextAlignment.Center;
            b.Child = t;
            PinyinRow.Children.Add(b);
            _py[i] = b; _pyText[i] = t;
        }

        // 末尾的"…": 这一行摆不下的那些切分还有一堆时亮出来, 告诉你"不止眼前这几条"。
        // 以前行满没满全靠猜 —— 有时只有两种切分行里却空着一截, 有时后头藏着二十几条
        // 看着却跟只有两条一模一样, 不知道还能往下翻。
        // 它不做动画也不抢焦点, 纯粹是个标记; 真正的入口还是十字键 ↓(或 → 走到最后)。
        _pyMore = new TextBlock
        {
            Text = "…",
            FontSize = 17,
            Foreground = Sub,
            VerticalAlignment = VerticalAlignment.Center,
            // 和拼音格子对齐: 格子上下各有 2 的 Margin + 2 的 Padding
            Margin = new Thickness(4, 2, 2, 2),
            Visibility = Visibility.Collapsed,
        };
        PinyinRow.Children.Add(_pyMore);
    }

    /// <summary>数字层的 0: 按 L3(或十字键在 8 上再按一次下)选中, 浮在 8 号键正下方。</summary>
    ///
    /// 【2.1.24: 横向位置交给布局系统居中, 别再自己拿宽度算】
    /// 以前它是 WheelLayer(铺满键盘区的一张画布)上的一颗键, 每一帧由 Render
    /// 现算 `Canvas.Left = 键盘区宽/2 - 38` 摆到正中。毛病在"现算"这两个字上:
    /// Render 读到的宽度是**这一刻动画刚写进去**的值, 而屏幕上真正的排版要等
    /// **下一轮布局**才用得上 —— 于是这颗 0 永远是"按上一帧的宽度摆的", 慢半拍。
    /// 慢半拍本身不明显, 要命的是**每一帧宽度变化量不固定**(掉一帧就是两倍),
    /// 于是它先往右偏出去、再收回来、再偏出去 —— 用户原话: "0 会在切换动画
    /// 过程中往右不停闪好几次再稳定住"。
    ///
    /// 现在它挂在 KeyArea(那张 360×360 的格子)上: 横向居中、贴着底边、
    /// 底边再往下探 36(和以前的 `Top = 高 - 10` 一像素不差)。键盘区变宽变窄
    /// 时, 由布局系统自己把它摆回正中 —— 那一刻容器多宽、它就按多宽算,
    /// 不存在"读到的是旧数"这回事。加上卡片本来就是**居中**缩放的,
    /// 于是整场"左右缩窄"里它一动不动。
    void BuildZeroKey()
    {
        _key0 = new Border
        {
            Width = 76, Height = 46,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            // 底边压在九宫格下沿再往下探 36 —— 视觉上就是"8 的下面"
            Margin = new Thickness(0, 0, 0, -36),
            Background = Normal,
            BorderBrush = DimEdge,
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed
        };
        ShapeBase(_key0, 10);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _key0Text = Text(18, Fg);
        _key0Text.Text = "0";
        var sub = Text(10, Sub);
        // 2.3.3 起是 Y(以前写 L3)。这行小字是"要出 0 得按下哪一颗"的提示 ——
        // 数字层九颗键都是摇杆推出来的, 只有这颗 0 得另外按一下键, 不写就没人知道。
        sub.Text = "Y";
        sub.Margin = new Thickness(0, 1, 0, 0);
        sp.Children.Add(_key0Text);
        sp.Children.Add(sub);
        _key0.Child = SkinWrap(_key0, sp, "pink");
        _key0Sub = sub;
        // 【挂在键盘区上, 不挂轮盘】轮盘是张 Canvas, Canvas **不会**把子元素
        // 拉满宽(Canvas 只按子元素自己想要的尺寸摆), 所以"居中"得靠外层这张
        // KeyArea 的 Grid —— 它的 HorizontalAlignment=Center 是真居中。
        // ZIndex 仍给 99: 得压过九键 / 26 键(它们不透明), 又得让窗口F(150)盖住。
        KeyArea.Children.Add(_key0);
        Panel.SetZIndex(_key0, 99);
    }

    // ---- 按键形状 ----
    //
    // 每一颗键都记着"方块那一档下自己是几度圆角"(下面这个 BaseR), 换形状时按它回退。
    // 为什么不把那几个数抄到换形状那段里: 各处的老数值本来就不同 —— 九键 10、
    // 26 键 6、候选行和符号网格 8、拼音格 5、设置和窗口F 的格子跟着 _cellR ——
    // 抄一份过去就是"同一个概念两份拷贝", 加一处忘了同步, 切回方块时个别键还留着圆角。
    static readonly DependencyProperty BaseRProperty =
        DependencyProperty.RegisterAttached("BaseR", typeof(double), typeof(OverlayWindow),
                                            new PropertyMetadata(0.0));

    /// <summary>
    /// 这一颗是"可爱粉的花瓣键" —— 圆角永远跟着方块那一档, 不跟着圆形风格走。
    /// <para>只有九键那九颗打这个标记。花瓣是**整图铺在键底上**的, 归 Border 自己管,
    /// 圆角裁不到它头上也不该裁 —— 那一套就是花瓣的样子。</para>
    /// </summary>
    static readonly DependencyProperty PetalProperty =
        DependencyProperty.RegisterAttached("Petal", typeof(bool), typeof(OverlayWindow),
                                            new PropertyMetadata(false));

    /// <summary>"尺寸一变就重刷圆角"那个钩子挂上了吗(防重复挂, 见 ShapeBase)。</summary>
    static readonly DependencyProperty HookedProperty =
        DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(OverlayWindow),
                                            new PropertyMetadata(false));

    /// <summary>记下这颗键在"方块那一档"下的圆角, 并按当前形状刷一次。</summary>
    void ShapeBase(Border b, double radius)
    {
        b.SetValue(BaseRProperty, radius);
        // 【尺寸一落地就再刷一次】圆形那一档的圆角是按"实测量到的短边"算的, 而刚造出来
        // 那一刻还没排版(ActualWidth 是 0)。设置 / 窗口F 的格子是**每次进一层现搭**的
        // (见 FillPanel), 符号网格那 40 格更是开面板才显出来 —— 搭完没人再刷它们,
        // 于是"点进深层还是方的, 偶尔撞上一次 Settle 才变圆"。挂在尺寸上最省事也最准:
        // 谁排版完了谁触发一次, 以后改缩放也跟着走。
        // 【只挂一次】换主题会挨个再调一回 ShapeBase(见 ApplyTheme), 不记这一笔的话
        // 换十次主题就挂上十个钩子(LayerHost.VeilHooked 是同一个坑)。
        if (!(bool)b.GetValue(HookedProperty))
        {
            b.SetValue(HookedProperty, true);
            b.SizeChanged += OnKeySize;
        }
        ShapeRound(b);
    }

    /// <summary>尺寸定了(或者改了) —— 圆角按新的短边重算一遍。</summary>
    void OnKeySize(object sender, SizeChangedEventArgs e) => ShapeRound(sender as Border);

    /// <summary>
    /// 按当前形状给这颗键设圆角。圆形那一档取**短边的一半**(见
    /// <see cref="KeyShapes.RadiusOf"/>): 正方形自己变成正圆, 长条自己变成胶囊。
    /// <para>【量不到尺寸就留着方块那一档】那一帧还没排版(ActualWidth 是 0),
    /// <see cref="KeyShapes.RadiusOf"/> 这时候回 0 —— 不许拿它去把圆角归零, 否则
    /// 头一帧是方的、第二帧突然变圆, 就是一次肉眼可见的跳。</para>
    /// </summary>
    void ShapeRound(Border b)
    {
        if (b == null) return;
        // 【可爱粉的九键是唯一的例外, 规矩写在这里而不是写在遍历那一处】
        // 现在刷圆角的时机不止"换形状 / 换主题 / 布局落定"那一处了 —— 每颗键自己
        // 尺寸一变也会刷(见上面的 OnKeySize)。例外漏一处, 表现就是"某些时候九键
        // 忽然被改成了正圆, 换一次主题又变回花瓣"。
        bool petal = (bool)b.GetValue(PetalProperty) && SkinMode == 1;
        double r = !petal && _keyShape == KeyShape.Round
            ? KeyShapes.RadiusOf(b.ActualWidth, b.ActualHeight) : 0;
        b.CornerRadius = new CornerRadius(r > 0 ? r : (double)b.GetValue(BaseRProperty));
    }

    /// <summary>
    /// 换形状、或者布局刚落定(那时候才量得到每颗键真正的尺寸)时, 把所有已经搭好的
    /// 格子刷一遍。
    /// <para>可爱粉的九键是唯一例外: 花瓣是**整图铺在键底上**的, 归 Border 自己管,
    /// 圆角裁不到它头上也不该裁 —— 那一套就是花瓣的样子。其余(26 键 / 候选行 /
    /// 符号网格 / 设置 / 窗口F)在圆形下一律按短边取圆角。</para>
    /// </summary>
    void ApplyKeyShape()
    {
        // 九键照旧一起走 —— 花瓣那个例外由 ShapeRound 自己判(见那里), 这里不重复一遍。
        for (int k = 1; k <= 9; k++) ShapeRound(_key9[k]);
        if (_en != null)
            for (int r = 0; r < _en.Length; r++)
                if (_en[r] != null)
                    for (int c = 0; c < _en[r].Length; c++) ShapeRound(_en[r][c]);
        if (_candRow != null) foreach (var b in _candRow) ShapeRound(b);
        if (_grid != null) foreach (var b in _grid) ShapeRound(b);
        if (_py != null) foreach (var b in _py) ShapeRound(b);
        ShapeRound(_key0);
        // 设置和窗口F 的每一格 —— 789 合成那条长条、QuickBar 那条、以及「关于」
        // 并成的那一大块, 都是 Custom / 合块造出来的, 一样在 Boxes 里。
        foreach (var h in new[] { _set, _qk })
            if (h != null) foreach (var b in h.Boxes) ShapeRound(b);
    }

    Border Box(double radius)
    {
        var b = new Border
        {
            Background = Normal,
            Margin = new Thickness(4),
            BorderThickness = new Thickness(1),
            BorderBrush = DimEdge
        };
        ShapeBase(b, radius);
        return b;
    }

    /// <summary>
    /// 26 键全键盘的键。82 个键挤一行的话每个只有三四十像素宽,
    /// 所以这里的边距收到 2(普通 Box 是 4), 宽度按"单位宽 × 键的倍数"现算 ——
    /// Tab / Caps / Shift / 回车 / 空格本来就该比字母键宽, 照抄真键盘才认得出来。
    /// </summary>
    Border KeyBox(double w, double h)
    {
        var b = new Border
        {
            Width = w, Height = h,
            Background = Normal,
            Margin = new Thickness(2),
            BorderThickness = new Thickness(1),
            BorderBrush = DimEdge
        };
        ShapeBase(b, 6);
        return b;
    }

    static TextBlock Text(double size, Brush fg) => new()
    {
        FontSize = size,
        Foreground = fg,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    // ---------- 渲染 ----------
    // ================= 九键换"键面": 每颗单独翻一下, 像波纹荡开 =================
    //
    // 进滑行 / 进数字层 / 进标点层, 九颗键的**内容**都变了。以前是整块界面一起
    // 横着压扁再展开(那是翻设置面用的), 太重, 而且看不出"九颗键各自换了个面"。
    // 现在每颗自己转: X 方向缩到 0(看不见了)-> 换成新面 -> 展开回来。
    //
    // 波纹的顺序按**离左上角那颗的切比雪夫距离**分三波:
    //   第 0 波: 1
    //   第 1 波: 2 4 5        (和 1 相隔一格)
    //   第 2 波: 3 6 7 8 9
    // 从左上角荡开, 眼睛跟得上, 又不会慢到让人等 —— 三波走完约 190ms。
    //
    // 【关键: 动画不拦操作】整套只动 RenderTransform, 不设任何"动画中不接受输入"
    // 的闸门。按下 LB 的那一帧滑行就已经在工作了, 摇杆照样选键、照样记字母 ——
    // 翻转纯粹是看得见的那层皮。要是为了等动画而挡住输入, 进滑行就得"卡"一下,
    // 那才是真的难受。

}
