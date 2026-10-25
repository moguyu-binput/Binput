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
// ---- 设置里那些页面本身写了些什么(文案与格子) ----
//
// 2.1.11 之前这些代码全在 OverlayWindow.xaml.cs 一个文件里(5117 行)。
// 这次只是按职责把它拆成几个 partial class 文件 —— 一行都没改,
// 编译出来的行为跟拆之前完全一样(改名/搬位置不影响任何逻辑)。

    static readonly string[] VerNames = { "预输入版", "直出版" };
    static readonly string[] VerTips =
    {
        "字先打在界面的预览框里，收起时整段复制、按下面的档位送进游戏。游戏里用这个。",

        "按键直接送到当前有焦点的窗口，不抢焦点也不预览。适合在 Windows 里随手打几个字。",
    };

    // JKL 摆在哪一颗。写成两行各写一种摆法、选中哪个摆哪个, 而不是一个"按一下就切"
    // 的按钮 —— 按钮上不管写状态还是写动作, 都会被读反(写着"互换"其实还没换)。
    // 这两行跟"版本"那两行一个样子: 圆点在哪一行, 键盘就是那一行写的样子。
    static readonly string[] SwapNames =
    {
        "JKL 在正中间（手机九宫格那样，出厂这个）",
        "JKL 在左上角",
    };
    static readonly string[] SwapTips =
    {
        "就是普通手机九宫格的样子：JKL 在正中间那颗，「快捷」挪到左上角。"
        + "左上角那颗长按开窗口F —— 快捷挪到哪儿，开窗口F 就跟到哪儿。",

        "JKL 搬到左上角那颗，正中间让给「快捷」（长按开窗口F）。"
        + "j / k / l 在拼音里只做声母、不会排在音节中间，所以这么挪不影响"
        + "“这个字该按哪颗键”，照键上印的字母推就行。",
    };

    // 滑行门槛那几档, 一行一档(毫秒数跟着 Config.DWELL_STEPS)。
    // 【2.1.35 起默认改到 128】原来默认给 80("多认一个键按 X 就删, 漏一个整串得重滑"),
    // 用下来太多人嫌它毛躁 —— 蹭过去的格子老被算进去。底下这几句也跟着以"推荐那档"
    // 为坐标重写了一遍: 数字一挪, 以旧数为参照的话就全成了错的。
    // 两头(48 和 192)是拿来试边界的, 让你摸到自己能忍的上下限再往回挑。
    static readonly string[] DwellTips =
    {
        "最灵敏, 快的这一头。手刚一顿就记 —— 快是快, 但**蹭过去的格子几乎都会被算进去**, "
        + "得不停按 X 删。留着它主要是让你看看最灵敏能敏到什么程度: "
        + "试过之后往回挑一档, 就知道自己能接受多快。",

        "很灵敏。比推荐那档快四档, 滑得又碎又快、而且愿意多按几下 X 的人用它。",

        "**比推荐那档快三档**。多认一个键你按一下 X 就删了, 漏一个整串都得重滑 —— "
        + "要是觉得推荐那档偶尔把你的停顿吞了, 就从这里开始往下试。",

        "比推荐那档快两档。两者就差 32 毫秒, 多数人试不出区别; 嫌推荐那档有点敦, 先试这里。",

        "**按你攒下的记录算, 这一档最准** —— 那些数据里你真会停的最短是 124 毫秒、"
        + "蹭过去的最长是 110 毫秒, 判定落在两者之间就只有真停会被记。"
        + "推荐那档(128)偶尔漏的话, 直接退到这里。",

        "**推荐的一档, 也是出厂默认(128 毫秒)**。它比 112 慢一档: 记录里你真会停的最短 "
        + "是 124 毫秒, 到这一档刚好压在线上 —— 绝大多数人够用, 偶尔吞一下再推一次就回来了, "
        + "换来的好处是路过蹭到的格子基本不会被算进去。",

        "比推荐慢一档。**这一档往上开始有漏的风险**(记录里你真停的最短是 124 毫秒), "
        + "手速快的人会觉得“明明停了却没记上”。滑得慢、或者容易手抖的人反而合适。",

        "很慢。得刻意停住才记得上。多数人从这里开始会觉得“推过去了没反应”, "
        + "除非你滑得特别慢、或者手抖得厉害。",

        "更慢。差不多只有真把摇杆按在一个方向不动才记得上。拿它试边界正好: "
        + "要是这一档你还能滑顺, 说明你的手停得比自己以为的久得多。",

        "最慢, 慢的这一头。要顿一下半才认一个键 —— 漏得最多, 也几乎不会多认。"
        + "同样是留着让你摸上限的: 试完往回挑, 就知道该停在哪一档。",
    };

    // 【2.1.35】"快/慢"那串参照词全撤了: 十档排成一列时, 谁大谁小看数字就够,
    // 底下再挂"较快 / 较慢"之类的注脚, 是把已经明白的事又说一遍、还把格子挤窄。
    // 只留推荐那一档标个"推荐"。

    // ================= 设置页: 九宫格页面 =================
    //
    // 以前是一长条列表(二十来行, 得上下滚着找); 现在是一页页九宫格:
    //   首页    一行三格往下排, 格数不限
    //   二级起  固定 3×3, 超过九个就 LB / RB 翻页(和"更多标点"一个路子)
    //   还有下一级 -> 换到那一页, 以此类推。每页都是同一套画法、同一套操作。
    // 选格子用**左摇杆**, 和九键选键一模一样: 推哪个方向就选哪一格, 手一松回中
    // 就落回正中那格(见 SettingsStick)。十字键也能走, 留给不想推摇杆的时候。
    //
    // 【2.1.3: 换页不是"叠一层"】以前每往里一级就往卡片里多摞一块真身:
    // 新的从下面浮上来, 老的缩小、下沉、压暗留在底下, 中间再垫一层模糊快照 ——
    // 于是老的那几层在界面上一直还在(撤掉格子底板之后就特别明显)。
    // 现在每一页都是**平级的一整页**: 换页时旧页出门、新页进门, 场上从头到尾
    // 只有一块(见 LayerHost / ShowPage / SinkLeave)。

    // SetCell / SetPage 两个类型搬到 SetNav.cs 里了 —— 连同"摆几格 / 能不能停 /
    // 光标默认停哪儿(回中)"这一整套算法。那边是**纯算法不碰 WPF**, 于是
    // tools/fprobe 能把它编进去直接测(见 fprobe 第 26 组)。
    // 下面这些只是同名的转发, 好让这一文件里几百处调用点一行都不用改。

    SetPage PageRoot() => new SetPage
    {
        Title = "设置",
        Nine = false,
        Build = () => new List<SetCell>
        {
            Cell("版本", Host != null && Host.Preview ? "预输入" : "直出", child: PageVer,
                 tip: () => "字先去哪儿。预输入版打在界面上的预览框里, 收起时整段送进游戏;"
                            + "直出版直接发给当前窗口。九键只解拼音、26 键只出英文, 两个版本一样。"),
            Cell("皮肤", Themes.Length > 0 ? Themes[Math.Clamp(Host?.Theme ?? 0, 0, Themes.Length - 1)].Name : "",
                 child: PageSkin, tip: () => "换面板和按键的底色。只换颜色, 键位和用法一点不变。"),
            Cell("键盘", "九键 · 布局", child: PageKey,
                 tip: () => "九宫格长什么样: 键上要不要印数字、JKL 摆哪一颗、焦点要不要弹一下。"),
            Cell("滑行", Host != null && Host.SlideMode != SlideMode.Off ? "开着" : "关着", child: PageSlide,
                 tip: () => "按住 LB 推左摇杆画字: 摇杆绕一圈是九个方向, 停一下算一个键。"),
            // 中间这一格**先空着**。空位在设置页是正经状态: 能停能亮, 只是按 A 没反应
            // (见 SetNav.Landable / PaintPanel 里 cell == null 那一路)。
            null,
            Cell("动效", App.FxStyleName(Host?.FxStyle ?? FxStyle.Plain), child: PageFxStyle,
                 tip: () => "换页面那一整套动作的风格: 进设置、换九键那一面、LB / RB 翻页 "
                            + "都是它管。改完下一次换页立刻生效, 不用重开。"),
            Cell("维护", "习惯 · 日志", child: PageKeep,
                 tip: () => "你挑过的拼音会越用越靠前(存在本地), 想忘掉、想换机器带着走, "
                            + "都在这一层; 出问题先看日志: 启动、按键、贴没贴上都记着。"),
            Cell("退出", "结束程序", act: () => Host?.ExitApp(),
                 tip: () => "直接结束进程(和托盘菜单里的“退出”一样)。设置和输入习惯都存好了。"),
            Cell("唤醒", WakeKeys(), child: PageWake,
                 tip: () => "唤出 / 收起界面那一颗键。定了自定义之后默认的 L3+R3 就不再生效。"),
        },
    };

    // 「收起后」那一层不再单独摆一格 —— 那四档本来就是预输入版才有的东西,
    // 摆在版本这一层看着像第三种发行版。现在按「预输入」直接钻进去选:
    // 先把版本切成预输入, 再浮出那四档。
    // 换页动效那几档 —— 名字 / 第二行小字 / 底下那条长说明**全在 FxStyles.All 那一张
    // 表里**(见 FxStyle.cs)。加一种风格只改那一处, 这里一行都不用动: 摆几项由表的
    // 长度决定, 不用另外再数一遍。
    // 【2.1.36 之前】这里是 FxStyleNotes / FxStyleTips 两个数组, 和 App.FxStyleName 那个
    // switch、Config.LoadFxStyle 那条 if 链各写各的 —— 加一种风格要动六处, 漏一处
    // 就静默错(名字退回"默认"、存档退回 Plain、说明一片空白)。
    SetPage PageFxStyle() => new SetPage
    {
        Title = "换页动效",
        Build = () =>
        {
            var l = new List<SetCell>();
            for (int i = 0; i < FxStyles.Count; i++)
            {
                int k = i;
                var s = (FxStyle)k;
                l.Add(Cell(FxStyles.Name(s), FxStyles.Note(s),
                           act: () => { if (Host != null) Host.FxStyle = s; RefreshSettings(); },
                           on: () => (Host?.FxStyle ?? FxStyle.Plain) == s,
                           tip: () => FxStyles.Tip(s)));
            }
            return l;
        },
    };

    SetPage PageVer() => new SetPage
    {
        Title = "版本",
        Build = () => new List<SetCell>
        {
            Cell("预输入", "打在预览框", act: () => SetPreview(true), child: PageTier,
                 on: () => Host != null && Host.Preview, tip: () => VerTips[0]),
            Cell("直出", "发给当前窗口", act: () => SetPreview(false),
                 on: () => Host != null && !Host.Preview, tip: () => VerTips[1]),
        },
    };

    SetPage PageTier() => new SetPage
    {
        Title = "收起之后",
        Build = () =>
        {
            var l = new List<SetCell>();
            foreach (var t in new[] { PasteTier.CopyOnly, PasteTier.CopyPaste,
                                      PasteTier.CopyPasteEnter, PasteTier.FF14 })
            {
                var tier = t;
                l.Add(Cell(GameMode.Steps(tier), "", tip: () => GameMode.Tip(tier),
                           act: () => { if (Host != null) Host.PasteTier = (int)tier; RefreshSettings(); },
                           on: () => Host != null && Host.PasteTier == (int)tier));
            }
            return l;
        },
    };

    SetPage PageSkin() => new SetPage
    {
        Title = "皮肤",
        Build = () =>
        {
            var l = new List<SetCell>();
            for (int i = 0; i < Themes.Length; i++)
            {
                int k = i;
                // **土豪金没解锁 -> 那一格留空**(摆个 null, 跟首页正中那个空位一个做法)。
                // 空着是有意的: 一排方块里平白缺一块, 明摆着"这儿藏着个还没发现的",
                // 正好是彩蛋的意思。位置钉死在 GoldIdx(清新后面那一格), 以后加新
                // 皮肤往数组末尾追加, 别插到它前面。
                if (k == GoldIdx && !_goldOpen) { l.Add(null); continue; }
                // 只留文字, 不再挂色块: 三列里一格才一百来像素宽, 色块一占,
                // 名字就被挤成 11px 的小字; 而且主题名本身就说明它是哪套。
                l.Add(Cell(Themes[i].Name, "", tip: () => Themes[k].Name,
                           act: () => { if (Host != null) Host.Theme = k; RefreshSettings(); },
                           on: () => (Host?.Theme ?? 0) == k));
            }
            return l;
        },
    };

    SetPage PageKey() => new SetPage
    {
        Title = "键盘",
        Build = () => new List<SetCell>
        {
            Cell("九键数字", Key9Num ? "显示" : "隐藏", child: PageKey9Num,
                 tip: () => "隐藏(默认): 键上只留 ABC 那串字母。显示: 上面数字下面字母。"
                            + "按住 LT 进数字层时永远只显示数字。"),
            // 【这一行以前写反了】Swap15 = true 是"左上变快捷、JKL 挪到正中间"
            // (见 InputCore.QuickCell: 快捷格 = Swap15 ? 1 : 5, 键面 Key9Sub[6-i]),
            // 可这里写的是 Swap15 ? "左上" : "正中间" —— 于是格子里明明显示"正中间",
            // 外面这行灰字却报"左上", 跟着实际键盘也是对不上的。以 QuickCell 为准。
            Cell("按键风格", KeyShapes.Name(Host?.KeyShape ?? KeyShape.Square), child: PageKeyShape,
                 tip: () => "方块(默认): 一直以来的圆角方块。圆形: 正方形的键变成正圆、"
                            + "长条的键变成胶囊 —— 圆的直径就是原来那个方块的边长, 键盘一点"
                            + "没变大也没变小, 变的只有圆角。布局、窗口大小、动效都不受影响。"),
            Cell("JKL 位置", Host != null && Host.Swap15 ? "正中间" : "左上", child: PageSwap,
                 tip: () => "换的是格子不是数字: JKL 无论在哪颗上, 打出来的都还是 1。"
                            + "按住 LB 滑行时一律按「JKL 左上」来 —— 滑行靠摇杆转圈取键, "
                            + "那时候左上角必须恒为 JKL。"),
            // 【进去先翻回正面】看着的是键盘, 缩放 / 摆位才看得出效果 —— 不然调了半天,
            // 眼前还是设置页那九格。翻面归窗口自己管, 主程序那边只管"进那一档"。
            Cell("调整窗口", $"{(int)(Scale * 100)}%", act: () =>
                 {
                     if (Flipped) Flip(false);
                     Host?.AdjustWindow();
                 },
                 tip: () => "点进去调整个界面的大小和位置: **十字键上下**缩放(60%~180%), "
                            + "**左摇杆**挪位置, 调好按 **B** 存下并退出(中途不动就还是原样)。"
                            + "以前是按住 R3 调 —— 那颗键在摇杆底下, 手正推着摇杆选键的时候"
                            + "够不着, 而且得一直摁着, 一松手就退出去了。"),
        },
    };

    /// <summary>
    /// 按键风格: 摆几项由 <see cref="KeyShapes.All"/> 那张表说了算(和换页动效同一套
    /// 规矩) —— 名字 / 第二行小字 / 底下的长说明都从表里来, 加一档只要往表里补一条。
    /// </summary>
    SetPage PageKeyShape() => new SetPage
    {
        Title = "按键风格",
        Build = () =>
        {
            var l = new List<SetCell>();
            for (int i = 0; i < KeyShapes.Count; i++)
            {
                int k = i;
                var s = (KeyShape)k;
                l.Add(Cell(KeyShapes.Name(s), KeyShapes.Note(s),
                           act: () => { if (Host != null) Host.KeyShape = s; RefreshSettings(); },
                           on: () => (Host?.KeyShape ?? KeyShape.Square) == s,
                           tip: () => KeyShapes.Tip(s)));
            }
            return l;
        },
    };

    SetPage PageKey9Num() => new SetPage
    {
        Title = "九键数字",
        Build = () => new List<SetCell>
        {
            Cell("显示", "数字 + 字母", act: () => SetKey9Num(true),
                 on: () => Key9Num, tip: () => "键上上面数字、下面字母, 照着数字按就行。"),
            Cell("隐藏", "只留字母", act: () => SetKey9Num(false),
                 on: () => !Key9Num, tip: () => "键上只留 ABC 那串字母, 画面最干净。"),
        },
    };

    SetPage PageSwap() => new SetPage
    {
        Title = "JKL 的位置",
        Build = () =>
        {
            var l = new List<SetCell>();
            for (int i = 0; i < SwapNames.Length; i++)
            {
                int k = i;
                // 名字太长摆不进格子, 只留"正中间 / 左上角"那半句;
                // 底下原来还有一行"手机那样 / 老 T9 那样"的小字, 撤了 —— 格子自己
                // 说什么就看是什么, 再加注脚反而绕。
                string note = k == 0 ? "正中间" : "左上角";
                l.Add(Cell(note, "", tip: () => SwapTips[k],
                           act: () => { if (Host != null) Host.Swap15 = k == 0; RefreshSettings(); },
                           on: () => (k == 0) == (Host != null && Host.Swap15)));
            }
            return l;
        },
    };

    SetPage PageSlide() => new SetPage
    {
        Title = "滑行输入",
        // 【位置是特意摆的】开关横向摆(4 开 / 6 关, 左边那个是当前的下法),
        // 两项判定竖着摞在正上(2)和正下(8) —— 开关是成对的、一眼要看齐的东西;
        // 判定的两个数字要往里钻, 塞在上下的正中间摇杆一推就到, 不和开关争位置。
        Build = () => new List<SetCell>
        {
            null,                                   // 1 左上
            Cell("触发判定", "十档", child: PageDwell,
                 tip: () => "在一格上**停够这么久**才把它当成你要的那个键; 停不够就当你是路过蹭过去的。"
                            + "调快 = 灵敏但容易多认(按 X 删一下就行); 调慢 = 稳但可能漏。"),
            null,                                   // 3 右上
            Cell("开", "LB 画字", act: () => SetSlide(SlideMode.Type),
                 on: () => Host != null && Host.SlideMode != SlideMode.Off,
                 tip: () => "按住 LB, 左摇杆朝某个方向推过去停一下 —— 摇杆绕一圈就是九个方向, "
                            + "八个角上八个键, 左上角那颗是 JKL。推过去之后**停住**才算一个键。"),
            null,                                   // 5 正中
            Cell("关", "LB 还是 LB", act: () => SetSlide(SlideMode.Off),
                 on: () => Host != null && Host.SlideMode == SlideMode.Off,
                 tip: () => "关掉之后 LB 什么也不多, 摇杆只用来挪光标。"),
            null,                                   // 7 左下
            Cell("连发判定", "十二档", child: PageRepeat,
                 tip: () => "一个键已经被认下之后, **你还停着不动**, 它会每隔这么久再给你一个同样的键 —— "
                            + "打 66 / 777 这类就靠它。和「触发判定」是两回事: 那个管多久才认, 这个管认了之后隔多久重复。"
                            + "调小 = 一串字母出得快; 调大 = 不容易多出来。"),
            null,                                   // 9 右下
        },
    };

    /// <summary>
    /// 连发判定那十二档的行建议, 一条对一个档位(顺序同 Config.REPEAT_STEPS)。
    /// 这里的取舍和触发判定正好反过来: 连发**太快**的代价是多打一个字母(得按 X 删),
    /// 太慢的代价只是多停一会儿 —— 所以慢那一头的词写得比快那头宽容。
    /// </summary>
    static readonly string[] RepeatTips =
    {
        "最快, 快的这一头(200 毫秒)。一认下就马上接着蹦, 一串同字母几乎是一下子出来的。"
        + "打 777 很快, 但**手指稍微多停一点点就多一个** —— 留着它主要是让你看看最快能快到什么程度,"
        + "试过之后往回挑几档, 就知道该停在哪儿。",

        "很快。比推荐那档快五档, 手指停得特别稳又要连着打同字母的人才用得上。",

        "快。比推荐那档快四档。",

        "偏快。比推荐那档快三档。",

        "比推荐那档快两档。从这里往下都开始显得急: 一串同字母几乎不用等多久就全长出来了。",

        "比推荐那档快一档, 多数人体感差别不大 —— 觉得 320 稍微拖沓的人试这一档。",

        "**推荐的一档, 也是出厂默认(320 毫秒)**。多一个字母按一下 X 就删了, 慢一点也不过"
        + "多停一会儿, 所以取比正中间慢一点点的地方, 两边都留着退路。用着没毛病就不用动它。",

        "偏慢。如果你滑完一个字总发现尾巴上多出一个同字母, 先试这一档。",

        "慢。要实实在在按住不放才接着给。连着打同字母得有耐心, 但几乎不会多出来。",

        "很慢。停顿感明显: 一个出来之后要等一下才第二个。适合手容易抖、"
        + "或者推摇杆习惯划一下的人。",

        "更慢。差不多要把摇杆焊在那个方向上一个一个往外蹦。"
        + "除非你连发总是收不住, 一般到不了这一档。",

        "最慢, 慢的这一头(420 毫秒)。**这正是 2.1.34 以前写死的那个数** —— "
        + "如果你就是想要以前的手感, 挑它。",
    };

    SetPage PageRepeat() => new SetPage
    {
        Title = "连发判定",
        Build = () =>
        {
            var l = new List<SetCell>();
            for (int i = 0; i < Config.REPEAT_STEPS.Length; i++)
            {
                int k = i, ms = Config.REPEAT_STEPS[i];
                // 秒数本身就是说明, 底下不再挂"较快 / 较慢"那类注脚 —— 十二档排成一列,
                // 谁大谁小一眼就看得出来, 再写一遍等于把屏幕塞满。只有推荐那一档标出来。
                l.Add(Cell($"{ms} 毫秒", ms == Config.DEFAULT_REPEAT ? "推荐" : "",
                           tip: () => (k < RepeatTips.Length ? RepeatTips[k] : ""),
                           act: () => { if (Host != null) Host.SlideRepeat = ms; RefreshSettings(); },
                           on: () => Host != null && Config.RepeatIndex(Host.SlideRepeat) == k));
            }
            return l;
        },
    };

    SetPage PageDwell() => new SetPage
    {
        // 【2.1.34: 以前这里叫"判定"】现在还有个"连发判定", 两个名字必须能一眼分开:
        // 这个管多久才认下一格, 那个管认下之后隔多久重复。
        Title = "触发判定",
        Build = () =>
        {
            var l = new List<SetCell>();
            for (int i = 0; i < Config.DWELL_STEPS.Length; i++)
            {
                int k = i, ms = Config.DWELL_STEPS[i];
                // 同上: 秒数自己会说话, 只有推荐那一档底下写字, 其余留白。
                l.Add(Cell($"{ms} 毫秒", ms == Config.DEFAULT_DWELL ? "推荐" : "",
                           tip: () => (k < DwellTips.Length ? DwellTips[k] : ""),
                           act: () => { if (Host != null) Host.SlideDwell = ms; RefreshSettings(); },
                           on: () => Host != null && Config.DwellIndex(Host.SlideDwell) == k));
            }
            return l;
        },
    };

    SetPage PageWake() => new SetPage
    {
        Title = "唤醒键",
        Build = () => new List<SetCell>
        {
            Cell("键盘", "F1 ~ F12", child: PageWakeKb,
                 tip: () => "十二颗摆成两页, 摇杆或十字键挪, 按 A 就是它。"
                            + "只给这十二颗: 游戏里基本没人占它们, 撞车概率最低, "
                            + "而且单颗就能用, 不用带 Ctrl / Alt 那些修饰键。"
                            + "定了之后这颗键**全局有效、而且不会被吞** —— 游戏照样收得到它。"),
            Cell("手柄", "Back · Start · L3 · R3", child: PageWakePad,
                 tip: () => "五个选项: Back / Start / Back+Start / L3 / R3。"
                            + "L3 和 R3 是 2.3.2 才腾出来的 —— 以前 L3 是换九键/全键盘、"
                            + "R3 短按是进设置, 两颗都占着; 这两桩活搬到 Y 上之后才空出来。"
                            + "2.3.3 又把数字层出 0(归 Y)和按住 R3 调界面(搬到设置 → 键盘 → "
                            + "「调整窗口」)也挪走了, 于是这两颗现在彻底闲着, 拿去当唤出键"
                            + "不用让出任何一桩活。"),
            // 【判据不能写成 WakeKeys().StartsWith("L3")】选了 L3 当唤出键之后
            // WakeKeys() 也返回 "L3", 那样"默认"和"L3"两格会同时亮着, 看着像两个都选中了。
            // 默认就是"没自定义", 那就直接问 WakeSpec 空不空。
            Cell("默认", "L3 + R3", act: () => { Host?.WakeReset(); RefreshSettings(); },
                 on: () => string.IsNullOrEmpty(Host?.WakeSpec),
                 tip: () => "恢复为 L3 + R3(出厂那一对)。删掉自定义, 改回来立刻生效。"),
        },
    };

    SetPage PageWakeKb() => new SetPage
    {
        Title = "键盘唤醒键",
        Build = () =>
        {
            var l = new List<SetCell>();
            foreach (var k in InputCore.KeyWakeOptions)
            {
                string tok = k;
                l.Add(Cell(tok.ToUpperInvariant(), "", tip: () => $"用 {tok.ToUpperInvariant()} 唤出 / 收起界面。",
                           act: () => { Host?.SetWake("kb:" + tok); RefreshSettings(); },
                           on: () => string.Equals(Host?.WakeSpec, "kb:" + tok,
                                                   StringComparison.OrdinalIgnoreCase)));
            }
            return l;
        },
    };

    SetPage PageWakePad() => new SetPage
    {
        Title = "手柄唤醒键",
        Build = () =>
        {
            var l = new List<SetCell>();
            foreach (var o in InputCore.PadWakeOptions)
            {
                string opt = o;
                l.Add(Cell(opt, "", tip: () => $"用 {opt} 唤出 / 收起界面。"
                            + "唤出 / 收起用的这几个键**松开之前不会再当打字用**, "
                            + "不会刚叫出来就顺手打一个字。",
                           act: () => { Host?.SetWake("pad:" + opt); RefreshSettings(); },
                           on: () => string.Equals(Host?.WakeSpec, "pad:" + opt,
                                                   StringComparison.OrdinalIgnoreCase)));
            }
            return l;
        },
    };

    // 【2.1.19】以前「数据」单独占首页一格, 它就三格, 一个入口有点空;
    // 现在整页并进「维护」: 数据那三格排第一行, 原来这三格整体往下让一行。
    // 于是"我的东西"和"修东西"在同一层, 少一次钻进钻出。
    SetPage PageKeep() => new SetPage
    {
        Title = "维护",
        Build = () => new List<SetCell>
        {
            Cell("清除", "还得过一道", child: PageClearUse,
                 tip: () => "把记住的拼音顺序全忘掉, 回到字典原本的排法。快捷短语不受影响。"
                            + "**点完会再浮一层**: 左边是删、右边是不删, 手滑按错也删不掉。"),
            Cell("导出", "存成文件", act: () => Host?.ExportUse(),
                 tip: () => "存成一个文件, 换机器时带着走。"),
            Cell("导入", "并进来", act: () => Host?.ImportUse(),
                 tip: () => "两边是**合并**的: 习惯和窗口F 里的快捷一起走, 原来那份不会被盖掉。"),
            Cell("日志", "打开 log", act: () => Host?.OpenLog(),
                 tip: () => "打开 log.txt。出问题的时候这玩意儿最有用: 启动、按键、贴没贴上都记着。"),
            Cell("目录", "数据文件夹", act: () => Host?.OpenDataDir(),
                 tip: () => "打开程序旁边那个 save 文件夹 —— 设置和输入习惯都在里面那一\n份 Binput.save 里(整份加密, 双击打不开)。整个文件夹拷走就是一份完整的柄输。"),
            Cell("关于", "版本号", child: PageAbout,
                 tip: () => "版本号、用的是哪套发布、数据都存在哪儿 —— 整页就一大格, 按 A 复制走。"),
        },
    };

    /// <summary>
    /// 「清除习惯」的**确认页** —— 2.1.34 加的一道门槛。
    /// <para>以前「维护」那一页上点「清除」是立刻就忘的: 它和导出 / 导入 / 日志挤在
    /// 同一个九宫格里, 手滑按错一下就不可逆, 而且没有任何提示说"刚刚发生了什么"。
    /// 现在多走一层, 而且这一层**只有两个能按的格**, 其余七位连同正中间都是空的。</para>
    /// <list type="bullet">
    /// <item>**删除在左(4 号位)、取消在右(6 号位)** —— 照的是 Windows 上那种对话框
    /// 的老规矩: 干正事的那颗在左、退回去的那颗在右。</item>
    /// <item>正中那格原本摆着一句"推左留 · 推右删"的提示, 那是怕删错才加的; 2.1.35
    /// 把它去了 —— 两个按钮并排已经够明白, 中间夹一段字反而像第三个要按的东西。
    /// 现在松手回中落在空位上, 按 A 没反应, **不把摇杆明确推到左边就删不掉**。</item>
    /// </list>
    /// 页面用的是现成的九宫格那一套(同样的画法、同样的换页动效), 这里只是没给它内容。
    /// </summary>
    SetPage PageClearUse() => new SetPage
    {
        Title = "清除习惯",
        Build = () => new List<SetCell>
        {
            null,                                   // 1 左上
            null,                                   // 2 正上
            null,                                   // 3 右上
            Cell("删除", "全忘掉",
                 tip: () => "把记住的拼音顺序清空, 候选回到字典原本的排法(快捷短语不受影响)。"
                            + "**一旦忘了就找不回来。** 删完自动退回上一层。",
                 act: () => { Host?.ClearUse(); SettingsBack(false); RefreshSettings(); }),
            null,                                   // 5 正中: 空着
            Cell("取消", "什么都不动",
                 tip: () => "原样退回去, 你攒的那些顺序还是老样子。",
                 act: () => { SettingsBack(false); RefreshSettings(); }),
            null,                                   // 7 左下
            null,                                   // 8 正下
            null,                                   // 9 右下
        },
    };

    /// <summary>
    /// 「关于」那一页: **九格并成一大格**, 说明整段摆在里头, 按 A(或单击)复制。
    ///
    /// 以前是弹一个 Windows 消息框 —— 那一下整个界面让位给系统窗口: 手柄没法选
    /// (只能用鼠标去点那个"确定"), 半透明的游戏画面上一个系统框也很容易被挡住。
    /// 现在它就是设置页里的一页: 同一套配色、同一个返回手势(B / 长按 B),
    /// 复制完在卡片下面那行亮一下「已复制」, 全程不用离开手柄。
    ///
    /// 【只有一格, 于是光标恒在它上面】摇杆和十字键都推不动它(见 SetNav 里
    /// Solo 那几处), "选中"那一套动效就一直在 —— 这不是特例, 是"就一格"的必然。
    /// </summary>
    SetPage PageAbout() => new SetPage
    {
        Title = "关于",
        // **九格并成一大格**: 就这一行声明。
        // 摆在哪儿、摇杆落在哪儿、浮起来放大多少、字号多大, 
        // 全由 SetNav 按这个矩形推出来(见 SetNav.TileOf)。
        Layout = () => new List<Span> { new Span(0, 0, 3, 3) },
        Build = () => new List<SetCell>
        {
            Cell(Host?.About() ?? "柄输", "", act: CopyAbout),
        },
    };

    /// <summary>「已复制」那三个字亮多久。</summary>
    SetPage QuickListPage() => new SetPage
    {
        Title = InputCore.Q_LAYER_LIST,
        Nine = true,
        Rows = 3,
        Build = () =>
        {
            var list = new List<SetCell>();
            for (int i = 0; i < QuickStore.MaxSlots; i++)
            {
                int slot = i, num = i + 1;
                list.Add(Cell(QuickStore.At(slot)?.Show ?? "", "",
                    act: () => QHost?.Send(slot),
                    corner: i < QuickStore.NumSlots ? (() => num.ToString()) : null));
            }
            return list;
        },
    };

    /// <summary>
    /// 深层(长按列表里某一格浮出来):
    ///     1 写入短语   2 写入快捷键   3 删除
    ///     4 (空)       5 绑的数字     6 (空)
    ///     7 8 9 —— 三格合成**一条长条**, 里面写这一格现在存的是什么(见 <see cref="QuickBar"/>)。
    /// 4 和 6 是"能停能选中、按了没反应" —— 九个位置永远在同一处, 别的东西才不会乱跑。
    /// </summary>
    SetPage QuickDeepPage() => new SetPage
    {
        Title = InputCore.Q_LAYER_DEEP,
        Nine = true,
        Rows = 2,                        // 第三行让给底下那条长条
        // **第七格横跨整行** = "7 8 9 三格合成一条"。就这一行声明, 别的
        // (它占哪个矩形、光标怎么落到它上面)全由 SetNav 按这张表推出来。
        Layout = () => new List<Span>
        {
            new Span(0, 0), new Span(0, 1), new Span(0, 2),
            new Span(1, 0), new Span(1, 1), new Span(1, 2),
            new Span(2, 0, 1, 3),               // 7 8 9 合成的那条
        },
        Build = () =>
        {
            int slot = _qSlot;
            int num = QuickStore.NumOf(slot);
            return new List<SetCell>
            {
                Cell("写入短语", act: () => QHost?.WritePhrase(slot)),
                Cell("写入快捷键", act: () => QHost?.RecordHotkey(slot)),
                Cell("删除", act: () => QHost?.Delete(slot)),
                null,                                     // 4 号位: 空
                // 中间: 写现在绑在数字几。**没绑就整个空着**(用户原话"中间置空"),
                // 只留底下那行小灰字提示还能点 —— 小字不占位, 中间看着还是空的。
                Cell(num > 0 ? num.ToString() : "",
                     num > 0 ? "改绑" : "未绑",
                     child: QuickNumPage),                // 5 号位: 点一下 -> 九个数字
                null,                                     // 6 号位: 空
                // 7 号位: 底下那条长条(横跨整行), 摆的是 QuickBar 造出来的控件
                SetNav.Block(() => QuickBar(_qSlot)),
            };
        },
    };

    /// <summary>
    /// 绑数字层: 九个数字, 点一个就把这一格绑过去。**对面那一格有东西就互换**
    /// (见 QuickStore.BindNum) —— 不会凭空覆盖掉你原来那条。
    /// 现在绑着的那个数字右上角有一枚对勾牌。
    /// </summary>
    SetPage QuickNumPage() => new SetPage
    {
        Title = InputCore.Q_LAYER_NUM,
        Nine = true,
        Rows = 3,
        Build = () =>
        {
            int slot = _qSlot;
            var list = new List<SetCell>();
            for (int n = 1; n <= 9; n++)
            {
                int num = n;
                list.Add(Cell(num.ToString(),
                    QuickStore.At(num - 1) == null ? "空" : "换绑",
                    act: () => QHost?.Bind(slot, num),
                    on: () => QuickStore.NumOf(slot) == num));
            }
            return list;
        },
    };

    /// <summary>
    /// 深层底下那条长条 —— 原先是 7 / 8 / 9 三格, 合成一条: 里面写这一格现在存的
    /// 短语 / 快捷键, 居中, 长了就分行, 写不下就溢出(看不见也没关系)。
    /// **尺寸照着三格算**: 三格一排是 360(和九键那块一样宽), 长条就是 360 扣掉
    /// 左右各一道缝; 高度就是一格(112)。于是"三格并成一条"看着正好占满那一行。
    /// </summary>
}
