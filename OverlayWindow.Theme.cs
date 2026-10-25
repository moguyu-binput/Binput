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
// ---- 配色 / 皮肤 / 字体色 —— 界面长什么颜色 ----
//
// 2.1.11 之前这些代码全在 OverlayWindow.xaml.cs 一个文件里(5117 行)。
// 这次只是按职责把它拆成几个 partial class 文件 —— 一行都没改,
// 编译出来的行为跟拆之前完全一样(改名/搬位置不影响任何逻辑)。

    // ---- 主题 ----
    //
    // 一套主题就是一组纯色: 面板底 / 键底 / 边框 / 选中 / 选中边 / 半选中 / 半选中边。
    // **透明度不跟着主题变** —— 面板统一套 0xE8(和原色一样透), 其余全不透明,
    // 换的只有色相。叠在游戏画面上"透多少"是另一件事, 不该被主题改掉。
    // 饱和度都压得比较低: 界面是压在游戏画面上的, 太艳会把画面完全盖掉, 也晃眼。

    /// <summary>
    /// 一套主题。Panel 是不透明底色, 用的时候统一套 0xE8。
    /// <para>Skin: 0 = 原来那套纯色深底(七个色就是全部); 1 = "可爱粉"贴纸风 ——
    /// 七个色只当基础值用, 真正出效果的是 ApplyTheme 里另外铺的渐变 / 厚边 / 圆角 /
    /// 果冻高光 / 方格纸底纹。加进来是第 9 套, 设置页那排方块自动多一个。</para>
    /// </summary>
    public sealed record UiTheme(string Name, Color Panel, Color Key, Color Edge,
                                 Color Sel, Color SelEdge, Color Lit, Color LitEdge,
                                 int Skin = 0);

    static Color C(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);

    /// <summary>
    /// **土豪金在 <see cref="Themes"/> 里的下标: 钉死在"清新后面那一格"**。
    /// <para>位置固定是有意的 —— 用户要的就是"以后加新皮肤, 土豪金还在老位置上"。
    /// 所以规矩是: **以后加新皮肤一律往数组末尾追加, 不许插在这一格前面**。
    /// 理由是这个下标要落盘(存档里就一个整数), 一挪, 老用户存的那份"第 10 套"
    /// 就指到别的皮肤上去了。</para>
    /// <para>没解锁的时候, 这一格在设置 → 皮肤那页里**留空**(见 PageSkin)。空着
    /// 反而像"这儿藏着个还没发现的", 正好是彩蛋的意思。</para>
    /// </summary>
    public const int GoldIdx = 10;

    public static readonly UiTheme[] Themes =
    {
        // 原色: 一直用的那个偏蓝紫的深底
        new("原色", C(0x14,0x14,0x20), C(0x1E,0x1E,0x28), C(0x3C,0x3C,0x48),
                   C(0x0C,0x44,0x7C), C(0x85,0xB7,0xEB), C(0x1C,0x34,0x54), C(0x56,0x86,0xB8)),
        new("灰",   C(0x1C,0x1C,0x1C), C(0x26,0x26,0x26), C(0x44,0x44,0x44),
                   C(0x3E,0x3E,0x44), C(0xB4,0xB4,0xC0), C(0x2E,0x2E,0x34), C(0x76,0x76,0x84)),
        new("黑",   C(0x08,0x08,0x0A), C(0x12,0x12,0x16), C(0x2A,0x2A,0x32),
                   C(0x22,0x22,0x2C), C(0xA0,0xA0,0xB0), C(0x1A,0x1A,0x22), C(0x64,0x64,0x78)),
        new("红",   C(0x20,0x12,0x14), C(0x2A,0x18,0x1A), C(0x4A,0x24,0x28),
                   C(0x52,0x1C,0x22), C(0xCE,0x8A,0x8A), C(0x36,0x16,0x1A), C(0x9C,0x58,0x5E)),
        new("蓝",   C(0x0E,0x16,0x26), C(0x18,0x20,0x2E), C(0x28,0x40,0x5E),
                   C(0x14,0x40,0x70), C(0x8A,0xB4,0xE0), C(0x1C,0x2C,0x48), C(0x5A,0x80,0xB8)),
        new("绿",   C(0x10,0x1C,0x14), C(0x18,0x26,0x1E), C(0x24,0x44,0x2C),
                   C(0x1C,0x48,0x2A), C(0x8A,0xC4,0x9A), C(0x16,0x30,0x1E), C(0x5A,0x8A,0x6A)),
        new("黄",   C(0x1E,0x1A,0x10), C(0x28,0x24,0x14), C(0x48,0x3E,0x20),
                   C(0x46,0x38,0x12), C(0xD8,0xC0,0x78), C(0x30,0x26,0x10), C(0xA0,0x8A,0x50)),
        new("粉",   C(0x20,0x14,0x1C), C(0x2A,0x1C,0x26), C(0x48,0x2C,0x3E),
                   C(0x4A,0x22,0x40), C(0xD8,0x98,0xC0), C(0x32,0x18,0x26), C(0xA0,0x5A,0x88)),
        // 可爱粉: 唯一一套浅色贴纸风(Skin=1)。上面这七个色只作基础值,
        // 渐变面板 / 厚糖果边 / 果冻键 / 方格纸底都在 ApplyTheme 的 Skin 分支里现铺。
        //
        // 浮字(Skin=3) 那套在 1.0.4 撤掉了 —— 它连键都没了, 在游戏画面上根本看不清。
        // 玻璃(Skin=2) 在 2.1.27 连代码带贴图整套删掉了: 它那层"烘焙白化"透出来的
        // 仍是清晰画面, 离 iOS 那种磨砂透差得远, 而系统模糊那条路一直没在真机上验过。
        // 哪天真做出像样的了, 得整个重做(旧贴图已经不在 skin\ 里了)。
        new("可爱粉", C(0xF6,0xE1,0xEE), C(0xFF,0xFF,0xFF), C(0xFF,0x9E,0xC6),
                     C(0xA9,0xEC,0xCE), C(0x35,0xB2,0x8B), C(0xFF,0xD9,0xE9), C(0xFF,0x8C,0xBE), 1),
        // 清新: 第二套浅色, 但走**矢量**而不是贴纸(Skin=4)。奶油面板 + 豆绿选中 +
        // 奶黄粘住的修饰键, 四角放雏菊 / 云 / 小熊。键底是 Normal/Active/Lit 那几支
        // 渐变笔刷, 圆角由 Border 自己裁 —— 方块 / 圆形两种按键风格都能直接套上,
        // 不像可爱粉那套三段式贴图在圆形下要让位(见 Paint 的门)。七个色同样只作
        // 基础值, 真正的渐变 / 厚边 / 角饰都在 ApplyTheme 的 fresh 分支里现铺。
        new("清新", C(0xF5,0xEE,0xD6), C(0xFF,0xFD,0xF0), C(0xD8,0xC8,0x8E),
                   C(0xA8,0xD8,0xA0), C(0x5C,0x9E,0x4A), C(0xFF,0xF1,0xC0), C(0xE8,0xB8,0x4E), 4),
        // 土豪金: 彩蛋皮肤, 下标见 GoldIdx(钉死在清新后面)。七个色同样只作基础值,
        // 真正出效果的是 ApplyCore 里 gold 那一支: 香槟金 -> 深金的大落差面板渐变 +
        // 磨砂金键 + 深棕描边 + 四角金饰(元宝 / 金砖 / 钻石 / 宝石)。
        // 走矢量不碰贴图, 所以方 / 圆两种按键风格都直接套得上。
        new("土豪金", C(0xF8,0xE2,0x8C), C(0xFF,0xF8,0xDC), C(0x9A,0x68,0x0A),
                     C(0xFF,0xD8,0x5A), C(0x8A,0x5A,0x00), C(0xFF,0xEE,0xA8), C(0xB8,0x7A,0x00), 5),
    };

    /// <summary>土豪金那一套(就是 Themes[GoldIdx], 取个别名, 省得到处写下标)。</summary>
    static readonly UiTheme Gold = Themes[GoldIdx];

    /// <summary>
    /// 土豪金**解锁了没有**。没解锁时: 设置 → 皮肤那一页里它那格留空(见 PageSkin),
    /// 也选不到它(见 ApplyTheme)。
    /// <para>达成 100 连打由 <see cref="UnlockGold"/> 打开, **一开就永久开** —— 落在
    /// 存档里(Config 的 gold_open), 关掉软件再开还在, 不用重新连点。</para>
    /// <para>【为什么默认是"没解锁"】探针里建窗口不走 App 启动那段, 于是永远从
    /// "没解锁"起步 —— 断言才是确定的, 不会因为你本机存过一次就变。</para>
    /// </summary>
    bool _goldOpen;

    /// <summary>土豪金解锁了没有(没解锁那格留空、也选不到)。</summary>
    public bool GoldOpen => _goldOpen;

    /// <summary>
    /// 拨一下"解锁了没有"这个开关。**只动内存, 一行盘都不写** —— 写盘是
    /// <see cref="UnlockGold"/> 和 App 启动那一处的事。探针走这个(不该改真存档)。
    /// </summary>
    void SetGoldOpen(bool on)
    {
        if (_goldOpen == on) return;
        _goldOpen = on;
        // 正站在土豪金上又被锁回去(只有探针会这么干): 退回原色, 免得停在选不中的那套
        if (!on && Theme == GoldIdx) ApplyTheme(0);
    }

    /// <summary>
    /// 彩蛋: **永久解锁土豪金, 并且立刻切过去**(下标跟着落盘, 下次开还是它)。
    /// 已经解锁的再走一遍只是"切过去", 不重复写盘(连点 200 次不会反复折腾存档)。
    /// </summary>
    public void UnlockGold()
    {
        if (!_goldOpen)
        {
            SetGoldOpen(true);
            Config.SaveGoldOpen(true);
            App.Log("彩蛋: 土豪金 已永久解锁");
        }
        if (Theme != GoldIdx)
        {
            // 走 App 那个入口: 换完色顺手就把下标存了(存盘只有那一处)。
            if (Application.Current is App a) a.Theme = GoldIdx;
            else ApplyTheme(GoldIdx);           // 探针里没有 App 实例, 直接铺色
        }
        // 上面可能把卡片那圈描边换成了别的粗细(土豪金跟贴纸风一样是厚边), 尺寸要重算
        if (Flipped) { SizeBackCard(); PositionBottomCenter(); }
    }

    /// <summary>启动时把"解锁了没有"读回来。**只有 App 该调它** —— 探针不读真存档。</summary>
    public void LoadGoldOpen() { SetGoldOpen(Config.LoadGoldOpen()); }

    /// <summary>当前主题(下标)。换主题走 ApplyTheme, 别直接改。</summary>
    public int Theme { get; private set; }


    /// <summary>
    /// 九键按键上显不显示数字。
    /// 开着: 上面数字(24px) + 下面字母(11px), 和以前一样。
    /// 关着: 只留字母, 字号放大 —— 键上那串字母才是九键真正的信息, 数字只是给
    /// 刚上手的人对位用的, 没了它字母正好能占满整颗键。
    /// **数字层(按住 LT)永远只显示数字**, 不受这一项影响 —— 那一层就是要看数字。
    /// 直接赋值就行, 按钮上的字会跟着变。
    /// </summary>
    public bool Key9Num
    {
        get => _showKey9Num;
        set
        {
            if (_showKey9Num == value) return;
            _showKey9Num = value;
        }
    }

    /// <summary>
    /// 按键的形状(见 KeyShape)。
    /// <para>圆形那一档: 圆角一律取**短边的一半** —— 正方形的键自己变成正圆(直径
    /// 就是原来那个方块的边长), 长条的键自己变成胶囊(两头各半个圆)。不用给谁单独
    /// 写一刀: 九键那九颗、设置里的格子、「关于」那一大块是正圆; 789 合成那条、
    /// 全键盘的空格 / Shift / 回车、候选行和符号网格里那些扁格是胶囊。</para>
    /// <para>【格子尺寸一个都不动】所以布局、窗口大小、缩放的锚点、换页动效全都
    /// 不受影响 —— 变的只有圆角这一个数。这也是为什么四面的缝不能为了"圆挨得紧
    /// 一点"去收: 九键那颗键是 112, 设置里的格子也是写死的 112, 两边必须一直一样大。</para>
    /// <para>赋值即生效 —— 已经搭好的那些格子会一起刷一遍(见 ApplyKeyShape)。</para>
    /// </summary>
    public KeyShape KeyShape
    {
        get => _keyShape;
        set
        {
            if (_keyShape == value) return;
            _keyShape = value;
            ApplyKeyShape();
        }
    }
    KeyShape _keyShape = KeyShape.Square;
    /// <summary>
    /// 换皮肤(下标)。**凡是走这里的都会落盘** —— 落盘不在这里做, 在 App 的 Theme
    /// setter 里(它调完本函数再 `Config.SaveTheme`)。
    /// </summary>
    public void ApplyTheme(int i)
    {
        Theme = Math.Clamp(i, 0, Themes.Length - 1);
        // 土豪金没解锁就不许落在它身上(老存档 / 手滑都能撞上) —— 退回原色。
        if (Theme == GoldIdx && !_goldOpen) Theme = 0;
        ApplyCore(Themes[Theme]);
    }

    /// <summary>
    /// 把 t 这一套铺到界面上。**落不落盘由调用方决定**, 这里只管铺 ——
    /// 于是黄金(不在 Themes 里、不该存)和用户挑的皮肤(该存)能走同一套铺色代码。
    /// </summary>
    void ApplyCore(UiTheme t)
    {
        int skin = t.Skin;
        SkinMode = skin;
        SkinOn = skin >= 1;
        bool cute = skin == 1;
        bool fresh = skin == 4;
        bool gold = skin == 5;
        // 浅色贴纸系(可爱粉 / 清新 / 黄金)共用一套壳: 厚糖果边 + 更圆的圆角 + 白纸预览框。
        // 三套真正的色相各自在自己分支里铺, light 只管"长得是不是浅色那一拨"。
        bool light = cute || fresh || gold;

        // 毛玻璃的压暗色跟着面板本色走(见 VeilC): 换主题时那层"糊掉的面板"也得换色,
        // 否则可爱粉那套上还是压着一层深底, 跟粉边对不上。
        VeilC = Color.FromArgb(0x55, t.Panel.R, t.Panel.G, t.Panel.B);
        VeilBrush.Color = VeilC;

        // 字色: 这几个笔刷是"一个实例到处引用"的(建界面那一刻 TextBlock 拿走的就是它们),
        // 所以换主题只改笔刷自己的 Color, 不用挨个 TextBlock 重刷一遍 ——
        // 浅色皮肤上必须换成深字, 否则白字压在粉底上等于没有。
        if (skin == 3)          // 浮字: 没有任何底, 纯白字, 靠描边晕从画面里"抠"出来
        {
            Fg.Color        = C(0xFF,0xFF,0xFF);
            FgHot.Color     = C(0x9E,0xDC,0xFF);
            Sub.Color       = Color.FromArgb(0xA6, 0xFF,0xFF,0xFF);
            Accent.Color    = C(0x9E,0xDC,0xFF);
            AccentC         = C(0x9E,0xDC,0xFF);
            Pinyin.Color    = C(0xBF,0xE6,0xFF);
            PinyinDim.Color = Color.FromArgb(0x28, 0xFF,0xFF,0xFF);
        }
        else
        {
            // 黄金: 金底比粉底 / 奶油底都亮, 字压到近黑的深棕才拉得开
            Fg.Color        = cute ? C(0x4A,0x3B,0x48) : fresh ? C(0x4A,0x40,0x2A)
                            : gold ? C(0x3E,0x2A,0x08) : C(0xE8,0xE8,0xF0);
            FgHot.Color     = cute ? C(0x1F,0x5A,0x46) : fresh ? C(0x2E,0x6B,0x3E)
                            : gold ? C(0x4A,0x30,0x00) : C(0xE6,0xF1,0xFB);
            Sub.Color       = cute ? C(0x9A,0x86,0x98) : fresh ? C(0x9A,0x86,0x5E)
                            : gold ? C(0x8A,0x62,0x18) : C(0x7A,0x7A,0x8C);
            Accent.Color    = cute ? C(0xE2,0x55,0x9A) : fresh ? C(0x5C,0x9E,0x4A)
                            : gold ? C(0x9A,0x5A,0x00) : C(0xFA,0xC7,0x75);
            AccentC         = cute ? C(0xE2,0x55,0x9A) : fresh ? C(0x5C,0x9E,0x4A)
                            : gold ? C(0x9A,0x5A,0x00) : C(0xFA,0xC7,0x75);
            Pinyin.Color    = cute ? C(0x2E,0x7F,0xC1) : fresh ? C(0x3E,0x7A,0x2E)
                            : gold ? C(0x7A,0x4E,0x00) : C(0x9F,0xC7,0xF5);
            PinyinDim.Color = cute ? C(0xFF,0xE8,0xF3) : fresh ? C(0xE8,0xE0,0xC8)
                            : gold ? C(0xF6,0xE4,0xA8) : C(0x26,0x22,0x30);
        }

        // 压在主题色上的那个颜色(对勾)跟着翻一次: 亮黄 / 浅蓝 -> 深勾,
        // 宝蓝 / 桃红 -> 白勾。0.55 这条线是按上面那四个色试出来的。
        OnAccent.Color = Luma(AccentC) > 0.55 ? C(0x1A,0x1A,0x20) : C(0xFF,0xFF,0xFF);

        if (gold)
        {
            // 黄金: 面板是"香槟金 -> 深金"的大落差渐变(要的就是金闪闪), 键是磨砂金,
            // 选中那颗亮一档、粘住的修饰键再亮一档, 描边一律深棕/古铜(金边上压金边
            // 等于没边)。面板 alpha 仍套 0xE8 —— "透多少"是压在游戏画面上那件事。
            // 键底走渐变笔刷(不是贴图), 所以方块 / 圆形两种按键风格都直接适配。
            PanelBg    = Grad(Color.FromArgb(0xE8, 0xFF,0xF3,0xC4), Color.FromArgb(0xE8, 0xE6,0xAE,0x30));
            Normal     = Grad(C(0xFF,0xF8,0xDC), C(0xEE,0xC8,0x50));
            Active     = Grad(C(0xFF,0xEE,0x9A), C(0xF0,0xB0,0x20));
            ActiveEdge = new SolidColorBrush(C(0x8A,0x5A,0x00));
            ActiveEdgeC = C(0x8A,0x5A,0x00);
            Lit        = Grad(C(0xFF,0xF2,0xB4), C(0xE8,0xB0,0x30));
            LitEdge    = new SolidColorBrush(C(0x9A,0x62,0x00));
            DimEdge    = new SolidColorBrush(C(0xC8,0x9A,0x20));
            // 按钮: 奶金底 + 古铜边, 配深棕字
            BtnBg.Color  = C(0xFF,0xF4,0xD0);
            BtnEdge.Color = C(0xC8,0x9A,0x20);
        }
        else if (cute)
        {
            // 贴纸风: 面板粉白渐变、键是果冻白、选中薄荷、粘住的修饰键粉。
            // 面板 alpha 仍然套 0xE8 —— "透多少"是压在游戏画面上那件事, 不跟着主题变。
            PanelBg    = Grad(Color.FromArgb(0xE8, 0xFF,0xFA,0xFC), Color.FromArgb(0xE8, 0xF6,0xD8,0xEA));
            Normal     = Grad(C(0xFF,0xFF,0xFF), C(0xFF,0xE6,0xF0));
            Active     = Grad(C(0xC9,0xF5,0xE0), C(0x8C,0xE0,0xB8));
            ActiveEdge = new SolidColorBrush(C(0x35,0xB2,0x8B));
            ActiveEdgeC = C(0x35,0xB2,0x8B);
            Lit        = Grad(C(0xFF,0xEA,0xF3), C(0xFF,0xC5,0xDC));
            LitEdge    = new SolidColorBrush(C(0xFF,0x8C,0xBE));
            DimEdge    = new SolidColorBrush(C(0xFF,0x9E,0xC6));
            // 按钮: 浅粉果冻底 + 粉描边, 配深棕字。原来那颗深紫底会直接吃掉字。
            BtnBg.Color  = C(0xFF,0xF2,0xF7);
            BtnEdge.Color = C(0xFF,0x9E,0xC6);
        }
        else if (fresh)
        {
            // 清新: 奶油面板 + 豆绿选中 + 奶黄粘住的修饰键。面板 alpha 仍套 0xE8。
            // 键底走渐变笔刷(不是贴图), 圆角由 Border 自己裁 —— 方 / 圆两种按键
            // 风格都直接套上, 不像可爱粉那套三段式贴图在圆形下要让位(见 Paint)。
            PanelBg    = Grad(Color.FromArgb(0xE8, 0xF7,0xF0,0xD8), Color.FromArgb(0xE8, 0xED,0xE2,0xBE));
            Normal     = Grad(C(0xFF,0xFD,0xF0), C(0xF6,0xEE,0xD4));
            Active     = Grad(C(0xC8,0xE8,0xC0), C(0x9C,0xD0,0x94));
            ActiveEdge = new SolidColorBrush(C(0x5C,0x9E,0x4A));
            ActiveEdgeC = C(0x5C,0x9E,0x4A);
            Lit        = Grad(C(0xFF,0xF1,0xC0), C(0xF0,0xD8,0x86));
            LitEdge    = new SolidColorBrush(C(0xE8,0xB8,0x4E));
            DimEdge    = new SolidColorBrush(C(0xD8,0xC8,0x8E));
            // 按钮: 奶白底 + 暖棕边, 配深棕字。
            BtnBg.Color  = C(0xFF,0xF8,0xE8);
            BtnEdge.Color = C(0xD8,0xC8,0x8E);
        }
        else if (skin == 3)
        {
            // 浮字: 一切底都消失。键画成透明, 选中候选/行用一层极淡的天蓝示意。
            // 底和边都给到能"在亮背景上也认得出"的程度, 再淡就等于没选。
            PanelBg    = Brushes.Transparent;
            Normal     = Brushes.Transparent;
            Active     = new SolidColorBrush(Color.FromArgb(0x48, 0x7A,0xD8,0xFF));
            ActiveEdge = new SolidColorBrush(Color.FromArgb(0x99, 0x9E,0xDC,0xFF));
            ActiveEdgeC = C(0x9E,0xDC,0xFF);
            Lit        = new SolidColorBrush(Color.FromArgb(0x22, 0xFF,0xFF,0xFF));
            LitEdge    = new SolidColorBrush(Color.FromArgb(0x50, 0xFF,0xFF,0xFF));
            DimEdge    = new SolidColorBrush(Color.FromArgb(0x30, 0xFF,0xFF,0xFF));
            BtnBg.Color   = Color.FromArgb(0x00, 0xFF,0xFF,0xFF);
            BtnEdge.Color = Color.FromArgb(0x40, 0xFF,0xFF,0xFF);
        }
        else
        {
            PanelBg = new SolidColorBrush(Color.FromArgb(0xE8, t.Panel.R, t.Panel.G, t.Panel.B));
            Normal = new SolidColorBrush(t.Key);
            DimEdge = new SolidColorBrush(t.Edge);
            Active = new SolidColorBrush(t.Sel);
            ActiveEdge = new SolidColorBrush(t.SelEdge);
            ActiveEdgeC = t.SelEdge;
            Lit = new SolidColorBrush(t.Lit);
            LitEdge = new SolidColorBrush(t.LitEdge);
            BtnBg.Color = Color.FromRgb(0x33, 0x33, 0x4A);
            BtnEdge.Color = t.Edge;
        }

        // 三块面板: 贴纸风更圆 + 更厚的糖果边(厚边会占掉几像素, 窗口跟着长一点 ——
        // 这套主题本来就不跟缩放, 多这几像素无所谓)。浮字连边都没有。
        var edge = skin == 3 ? Brushes.Transparent : new SolidColorBrush(t.Edge);
        double r = skin == 3 ? 22 : light ? 20 : 14, bw = skin == 3 ? 0 : light ? 4 : 1;
        foreach (var p in new[] { Root, Back, CandPanel })
        {
            p.Background = PanelBg;
            p.BorderBrush = edge;
            p.CornerRadius = new CornerRadius(r);
            p.BorderThickness = new Thickness(bw);
        }
        // 设置页 + 窗口F 那几层面板: **没有底板、没有边**。
        // 2.1.0 第四批先撤了外框(它以前是 12 圆角 + 1 细线, 在九宫格外面多画一圈方框,
        // 而且内边距把格子挤成 110 —— 九键那颗键是 112); 2.1.1 再把底板也撤了:
        // 那一块 PanelBg 让九个格子看着像"被一个正方形背景框住", 而九宫格那一面
        // 并没有这么一块板。换主题时更不能在这里把 Background / BorderThickness
        // 刷回去 —— 那样"切一次主题又长回来"。
        // 格子的圆角跟着九键那颗键走(见 _cellR)。
        _cellR = light ? 18 : 10;
        // 【每摞只有一块面板】2.1.3 起不再"一层一块"; 设置和窗口F 各抱一块,
        // 见 LayerHost 顶部那段说明。
        foreach (var p in new[] { _set.Panel, _qk.Panel })
        {
            if (p == null) continue;
            p.Background = Brushes.Transparent;
            p.BorderBrush = Brushes.Transparent;
            p.BorderThickness = new Thickness(0);
        }
        // 已经搭好的那些格子也跟着换圆角(它们不是每次都重搭的)。
        // 【走 ShapeBase, 不直接赋 CornerRadius】它顺手把"方块那一档"也更新成新的
        // _cellR 并按当前形状刷一遍 —— 直接赋的话, 切回方块时这些格子留着上一套主题
        // 的圆角(可爱粉 18 / 纯色 10 两套数会串)。
        foreach (var h in new[] { _set, _qk })
            foreach (var b in h.Boxes)
                ShapeBase(b, _cellR);
        // 浮字: 给整层内容挂一圈深色描边晕(黑, blur2.5, 不偏移) —— 所有文字一次搞定,
        // 挂容器上比挨个 TextBlock 挂便宜得多, 后加的字也不会漏。
        if (OuterGrid != null) OuterGrid.Effect = skin == 3 ? FxOutline : null;
        if (skin == 3 && !ReferenceEquals(Back, Root)) Back.Effect = FxOutline;
        // 摇杆那根虚线是在 BuildStickLayer 里一次性赋的 Stroke, 要手动跟上
        if (_line != null) _line.Stroke = ActiveEdge;

        // 九键: 更圆; 花瓣贴图自带果冻高光, 矢量那层高光在贴纸风下关掉
        for (int k = 1; k <= 9; k++)
        {
            if (_key9[k] == null) continue;
            ShapeBase(_key9[k], light ? 18 : 10);   // 同上: 连"方块那一档"一起更新
            if (_key9Gloss[k] != null) _key9Gloss[k].Opacity = 0;
        }
        // 换主题会把上面那些圆角刷回"方块那一档", 所以最后得按当前形状再刷一遍
        // (圆形时按短边取; 可爱粉的九键在里面有例外, 保持花瓣自己的圆角)。
        ApplyKeyShape();

        // 底纹层: 可爱粉铺方格纸+火花; 清新不铺方格纸, 只在四角放雏菊/云/小熊;
        // 纯色 / 浮字整个关掉
        if (SkinDeco != null)
        {
            SkinDeco.Opacity = light ? (cute ? 0.95 : gold ? 1.0 : 0.9) : 0;
            SkinDeco.Background = gold ? GoldWash() : cute ? GridPaper() : null;
        }
        // 三套角饰是兄弟(Canvas), 谁亮谁灭跟着皮肤切 —— 可爱粉的粉色火花 /
        // 清新的雏菊云朵小熊 / 土豪金的元宝金砖钻石宝石。后两套第一次切到才现搭。
        if (CuteSparks != null) CuteSparks.Visibility = cute ? Visibility.Visible : Visibility.Collapsed;
        if (FreshDeco != null)
        {
            FreshDeco.Visibility = fresh ? Visibility.Visible : Visibility.Collapsed;
            if (fresh) BuildFreshDeco();
        }
        if (GoldDeco != null)
        {
            GoldDeco.Visibility = gold ? Visibility.Visible : Visibility.Collapsed;
            if (gold) BuildGoldDeco();
        }
        // 预览框(预输入版): 深色主题里是深色纸, 贴纸风 / 清新翻白纸, 浮字全透明
        NotePanel.Background  = skin == 3 ? Brushes.Transparent
                              : new SolidColorBrush(gold ? Color.FromArgb(0xF4, 0xFF, 0xF9, 0xE4)
                                                         : light ? Color.FromArgb(0xF4, 0xFF, 0xFF, 0xFF)
                                                                 : C(0x1B, 0x10, 0x17));
        NotePanel.BorderBrush = skin == 3 ? Brushes.Transparent
                              : new SolidColorBrush(cute ? C(0xFF, 0x9E, 0xC6)
                                                         : fresh ? C(0xD8, 0xC8, 0x8E)
                                                                 : gold ? C(0xC8, 0x9A, 0x20)
                                                                        : C(0x5A, 0x4A, 0x22));
        NoteBox.Foreground    = skin == 3 ? Fg : new SolidColorBrush(cute ? C(0x4A,0x3B,0x48)
                                                                            : fresh ? C(0x4A,0x40,0x2A)
                                                                                    : gold ? C(0x3E,0x2A,0x08)
                                                                                           : C(0xF2,0xEF,0xE4));
        NoteBox.CaretBrush    = skin == 3 ? Accent : new SolidColorBrush(cute ? C(0xE2,0x55,0x9A)
                                                                                : fresh ? C(0x5C,0x9E,0x4A)
                                                                                        : gold ? C(0x9A,0x5A,0x00)
                                                                                               : C(0xFA,0xC7,0x75));
        NoteHint.Foreground   = skin == 3 ? Sub : new SolidColorBrush(cute ? C(0xB0,0x9C,0xAC)
                                                                            : fresh ? C(0x9A,0x86,0x5E)
                                                                                    : gold ? C(0x8A,0x62,0x18)
                                                                                           : C(0x78,0x70,0x6A));
        // 齿轮 / 返回箭头: 直接跟着 Sub 走(Sub 的颜色上面已经换过了)。
        // (2.1.1 撤了底带那两行小灰字 —— 正面的 StatusText 和背面的 BackFoot,
        //  见 XAML 里那两处注释: 每时每刻都在, 真要看又读不完, 平时纯碍眼。)
        GearIcon.Fill = Sub;

        // 正好停在设置面: 那几层面板的底色/边也要换成新的, 否则它用的还是旧笔刷
        RefreshSettingsSide();
        // **整页的尺寸也要跟着重算**: 上面那个循环刚把整页那圈描边换成了新的粗细
        // (可爱粉 4、其余 1), 而整页是按"内容 + 描边"定死的尺寸。不重算的话,
        // 从别的皮肤切到可爱粉的**那一瞬间**, 那圈变粗的边当场吃掉内容区 ——
        // 表现就是"格子只剩左边和上边两条线", 要退回九键再进设置(那才会重算)才恢复。
        if (Flipped) { SizeBackCard(); PositionBottomCenter(); }

    }

    /// <summary>竖向渐变(上 -> 下)。贴纸风的面板和果冻键都靠它出"鼓起来"的感觉。</summary>
    static Brush Grad(Color top, Color bottom) =>
        new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));

    /// <summary>
    /// 方格纸底纹: 16px 一格, DrawingBrush 平铺 —— 纯矢量, 不带任何图片文件,
    /// 面板多大都不会糊。它就是"文字底下那一层"的质感来源。
    /// </summary>
    static DrawingBrush GridPaper()
    {
        var g = new GeometryGroup();
        g.Children.Add(new LineGeometry(new Point(0, 0), new Point(16, 0)));
        g.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 16)));
        return new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing
            {
                Geometry = g,
                Pen = new Pen(new SolidColorBrush(Color.FromArgb(0x34, 0xFF, 0x9E, 0xC6)), 1)
            }
        };
    }

    /// <summary>清新的角饰搭过没有(只搭一次, 换主题再切回清新不重搭)。</summary>
    bool _freshDecoBuilt;

    /// <summary>
    /// 清新的角饰: 全是矢量形状(Ellipse), 不带任何图片文件 —— 缩放不糊, 想改色
    /// 就改这里。挂在 FreshDeco 那张 Canvas 上, 它又是 SkinDeco 最底层, 所以
    /// 装饰铺在内容下面: 九键 / 全键盘摆中间, 四角那圈留白里才看得见它们。
    /// <para>【只搭一次】ApplyTheme 每次切主题都会跑一遍, 这里拿 _freshDecoBuilt
    /// 挡住, 免得每切一次就往 FreshDeco 里再摞一份一样的。</para>
    /// </summary>
    void BuildFreshDeco()
    {
        if (_freshDecoBuilt || FreshDeco == null) return;
        _freshDecoBuilt = true;
        FreshDeco.Children.Clear();

        // 左上: 一簇两朵雏菊
        var d1 = Daisy(28);
        Canvas.SetLeft(d1, 5); Canvas.SetTop(d1, 5);
        var d2 = Daisy(18);
        Canvas.SetLeft(d2, 30); Canvas.SetTop(d2, 4);
        // 右上: 一朵云
        var cl = Cloud(50);
        Canvas.SetRight(cl, 8); Canvas.SetTop(cl, 7);
        // 左下: 一朵雏菊
        var d3 = Daisy(22);
        Canvas.SetLeft(d3, 7); Canvas.SetBottom(d3, 7);
        // 右下: 一只小熊脸
        var br = Bear(34);
        Canvas.SetRight(br, 9); Canvas.SetBottom(br, 7);

        foreach (var e in new UIElement[] { d1, d2, cl, d3, br }) FreshDeco.Children.Add(e);
    }

    /// <summary>一朵雏菊: 6 片白花瓣 + 黄花蕊。size = 整朵占的边长。</summary>
    static Canvas Daisy(double s)
    {
        var c = new Canvas { Width = s, Height = s, IsHitTestVisible = false };
        double cx = s / 2, cy = s / 2;
        double pr = s * 0.17;            // 花瓣半径
        double dist = s * 0.24;          // 花瓣中心离花蕊的距离
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI * i / 3;
            var p = new Ellipse { Width = pr * 2, Height = pr * 2, Fill = Brushes.White };
            Canvas.SetLeft(p, cx + Math.Cos(a) * dist - pr);
            Canvas.SetTop(p, cy + Math.Sin(a) * dist - pr);
            c.Children.Add(p);
        }
        double cr = s * 0.12;
        var ctr = new Ellipse { Width = cr * 2, Height = cr * 2, Fill = new SolidColorBrush(C(0xF5, 0xD5, 0x47)) };
        Canvas.SetLeft(ctr, cx - cr); Canvas.SetTop(ctr, cy - cr);
        c.Children.Add(ctr);
        return c;
    }

    /// <summary>一朵云: 几团重叠的白色圆 blob。</summary>
    static Canvas Cloud(double s)
    {
        var c = new Canvas { Width = s, Height = s * 0.66, IsHitTestVisible = false };
        void blob(double lx, double ly, double bw, double bh)
        {
            var e = new Ellipse { Width = bw, Height = bh, Fill = Brushes.White };
            Canvas.SetLeft(e, lx); Canvas.SetTop(e, ly); c.Children.Add(e);
        }
        blob(s * 0.00, s * 0.26, s * 0.40, s * 0.40);
        blob(s * 0.26, s * 0.06, s * 0.44, s * 0.48);
        blob(s * 0.54, s * 0.20, s * 0.40, s * 0.42);
        blob(s * 0.14, s * 0.30, s * 0.58, s * 0.34);
        return c;
    }

    /// <summary>一只小熊脸: 棕色脸 + 两只耳 + 浅色口鼻 + 两眼 + 鼻头。</summary>
    static Canvas Bear(double s)
    {
        var c = new Canvas { Width = s, Height = s, IsHitTestVisible = false };
        var fur   = new SolidColorBrush(C(0xB0, 0x82, 0x5C));
        var dark  = new SolidColorBrush(C(0x5A, 0x3A, 0x20));
        var light = new SolidColorBrush(C(0xF2, 0xD8, 0xC0));
        void ell(double lx, double ly, double w, double h, Brush f)
        {
            var e = new Ellipse { Width = w, Height = h, Fill = f };
            Canvas.SetLeft(e, lx); Canvas.SetTop(e, ly); c.Children.Add(e);
        }
        ell(s * 0.04, s * 0.00, s * 0.36, s * 0.36, fur);   // 左耳
        ell(s * 0.60, s * 0.00, s * 0.36, s * 0.36, fur);   // 右耳
        ell(s * 0.07, s * 0.16, s * 0.86, s * 0.80, fur);   // 脸
        ell(s * 0.28, s * 0.50, s * 0.44, s * 0.34, light); // 口鼻
        ell(s * 0.25, s * 0.40, s * 0.10, s * 0.12, dark); // 左眼
        ell(s * 0.65, s * 0.40, s * 0.10, s * 0.12, dark); // 右眼
        ell(s * 0.45, s * 0.55, s * 0.10, s * 0.08, dark); // 鼻头
        return c;
    }

    // ---------- 黄金: 角饰 + 100 分徽标 ----------
    //
    // 和清新那套一个写法: 全是矢量形状(Polygon / Ellipse), 不带任何图片文件 ——
    // 缩放不糊, 想改色就改这里这几个笔刷。
    static readonly SolidColorBrush GOLD_HI   = new(C(0xFF, 0xF6, 0xC8));
    static readonly SolidColorBrush GOLD_MID  = new(C(0xF2, 0xC4, 0x3C));
    static readonly SolidColorBrush GOLD_LO   = new(C(0xC8, 0x88, 0x10));
    static readonly SolidColorBrush GOLD_EDGE = new(C(0x8A, 0x5A, 0x00));
    static readonly SolidColorBrush GEM_A     = new(C(0x7E, 0xE8, 0xF2));   // 钻石: 冰蓝
    static readonly SolidColorBrush GEM_B     = new(C(0x3C, 0xA8, 0xD8));
    static readonly SolidColorBrush GEM_C     = new(C(0x5A, 0x86, 0xE8));   // 宝石: 蓝宝
    static readonly SolidColorBrush GEM_D     = new(C(0x2E, 0x52, 0xA8));

    /// <summary>黄金的角饰搭过没有(只搭一次)。</summary>
    bool _goldDecoBuilt;

    void BuildGoldDeco()
    {
        if (_goldDecoBuilt || GoldDeco == null) return;
        _goldDecoBuilt = true;
        GoldDeco.Children.Clear();

        var ing = Yuanbao(40);                      // 左上: 一只金元宝
        Canvas.SetLeft(ing, 3); Canvas.SetTop(ing, 5);
        var brk = Brick(46);                        // 右上: 一块金砖
        Canvas.SetRight(brk, 8); Canvas.SetTop(brk, 7);
        var dia = Diamond(26);                      // 左下: 一颗钻石
        Canvas.SetLeft(dia, 7); Canvas.SetBottom(dia, 7);
        var gem = Gem(30);                          // 右下: 一颗蓝宝石
        Canvas.SetRight(gem, 9); Canvas.SetBottom(gem, 7);
        var s1 = GoldSpark(15);                     // 两颗小金星点缀
        Canvas.SetLeft(s1, 42); Canvas.SetTop(s1, 8);
        var s2 = GoldSpark(11);
        Canvas.SetRight(s2, 44); Canvas.SetBottom(s2, 12);

        foreach (var e in new UIElement[] { ing, brk, dia, gem, s1, s2 }) GoldDeco.Children.Add(e);
    }

    /// <summary>
    /// 一只金元宝: 中间鼓起的"肚子" + 两头各挑起一只圆翅 + 底下兜一道船底。
    /// <para>用 <see cref="Path"/> 的三次贝塞尔画轮廓, **不是多边形**: 元宝认的就是
    /// 这三段弧度 —— 翅尖往上那一挑、翅和肚子之间那道凹、肚子顶上那个鼓包。
    /// 拿直线去拼, 出来就是一块石头(上一版那个"狗头金"就是这么来的)。</para>
    /// </summary>
    static Canvas Yuanbao(double s)
    {
        var c = new Canvas { Width = s, Height = s * 0.80, IsHitTestVisible = false };

        // 上亮下暗的纵向渐变: 肚子得是"顶上被光照着", 才鼓得起来
        var g = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        g.GradientStops.Add(new GradientStop(C(0xFF, 0xF8, 0xD8), 0.00));
        g.GradientStops.Add(new GradientStop(C(0xF6, 0xCF, 0x52), 0.42));
        g.GradientStops.Add(new GradientStop(C(0xC8, 0x88, 0x10), 1.00));

        // 从左下出发绕一圈: 船底 -> 右翅 -> 凹 -> 肚子 -> 凹 -> 左翅 -> 回起点
        var fig = new PathFigure { StartPoint = new Point(0.09 * s, 0.62 * s), IsClosed = true };
        fig.Segments.Add(new BezierSegment(new Point(0.24 * s, 0.76 * s),     // 船底
                                           new Point(0.76 * s, 0.76 * s),
                                           new Point(0.91 * s, 0.62 * s), true));
        fig.Segments.Add(new BezierSegment(new Point(1.00 * s, 0.52 * s),     // 右翅: 往上挑
                                           new Point(1.00 * s, 0.30 * s),
                                           new Point(0.88 * s, 0.25 * s), true));
        fig.Segments.Add(new BezierSegment(new Point(0.80 * s, 0.23 * s),     // 翅尖内收 -> 落进那道凹
                                           new Point(0.79 * s, 0.38 * s),
                                           new Point(0.74 * s, 0.46 * s), true));
        fig.Segments.Add(new BezierSegment(new Point(0.70 * s, 0.20 * s),     // 肚子右半: 拱到顶
                                           new Point(0.63 * s, 0.06 * s),
                                           new Point(0.50 * s, 0.05 * s), true));
        fig.Segments.Add(new BezierSegment(new Point(0.37 * s, 0.06 * s),     // 肚子左半
                                           new Point(0.30 * s, 0.20 * s),
                                           new Point(0.26 * s, 0.46 * s), true));
        fig.Segments.Add(new BezierSegment(new Point(0.21 * s, 0.38 * s),     // 左翅(右翅的镜像)
                                           new Point(0.20 * s, 0.23 * s),
                                           new Point(0.12 * s, 0.25 * s), true));
        fig.Segments.Add(new BezierSegment(new Point(0.00 * s, 0.30 * s),
                                           new Point(0.00 * s, 0.52 * s),
                                           new Point(0.09 * s, 0.62 * s), true));

        c.Children.Add(new Path
        {
            Fill = g, Stroke = GOLD_EDGE, StrokeThickness = 1.2,
            Data = new PathGeometry { Figures = { fig } },
        });

        // 肚子上那块高光 + 两只翅尖各点一点反光(位置都往里收着放, 别探出轮廓)
        var hi = new Ellipse { Width = s * 0.24, Height = s * 0.12, Fill = GOLD_HI, Opacity = 0.85 };
        Canvas.SetLeft(hi, s * 0.37); Canvas.SetTop(hi, s * 0.14);
        c.Children.Add(hi);
        foreach (double x in new[] { 0.06, 0.86 })
        {
            var w = new Ellipse { Width = s * 0.12, Height = s * 0.09, Fill = GOLD_HI, Opacity = 0.5 };
            Canvas.SetLeft(w, s * x); Canvas.SetTop(w, s * 0.29);
            c.Children.Add(w);
        }
        return c;
    }

    /// <summary>一块金砖(梯形锭): 亮顶面 + 中色正面 + 侧面暗一档。</summary>
    static Canvas Brick(double s)
    {
        var c = new Canvas { Width = s, Height = s * 0.60, IsHitTestVisible = false };
        var front = new Polygon { Fill = GOLD_MID, Stroke = GOLD_EDGE, StrokeThickness = 1.0 };
        front.Points = new PointCollection
        {
            new Point(0.06 * s, 0.28 * s), new Point(0.94 * s, 0.28 * s),
            new Point(0.94 * s, 0.56 * s), new Point(0.06 * s, 0.56 * s),
        };
        var top = new Polygon { Fill = GOLD_HI, Stroke = GOLD_EDGE, StrokeThickness = 1.0 };
        top.Points = new PointCollection
        {
            new Point(0.16 * s, 0.04 * s), new Point(0.84 * s, 0.04 * s),
            new Point(0.94 * s, 0.28 * s), new Point(0.06 * s, 0.28 * s),
        };
        c.Children.Add(front); c.Children.Add(top);
        var lo = new Polygon { Fill = GOLD_LO, Opacity = 0.55 };   // 正面下半截压暗, 出体积
        lo.Points = new PointCollection
        {
            new Point(0.06 * s, 0.44 * s), new Point(0.94 * s, 0.44 * s),
            new Point(0.94 * s, 0.56 * s), new Point(0.06 * s, 0.56 * s),
        };
        c.Children.Add(lo);
        return c;
    }

    /// <summary>一颗钻石: 冠部(桌面上方) + 亭部(下面那个尖) + 一道腰线。</summary>
    static Canvas Diamond(double s)
    {
        var c = new Canvas { Width = s, Height = s, IsHitTestVisible = false };
        var pav = new Polygon { Fill = GEM_B, Stroke = GOLD_EDGE, StrokeThickness = 1.0 };
        pav.Points = new PointCollection
        {
            new Point(0.08 * s, 0.42 * s), new Point(0.92 * s, 0.42 * s), new Point(0.50 * s, 0.98 * s),
        };
        var crown = new Polygon { Fill = GEM_A, Stroke = GOLD_EDGE, StrokeThickness = 1.0 };
        crown.Points = new PointCollection
        {
            new Point(0.30 * s, 0.12 * s), new Point(0.70 * s, 0.12 * s),
            new Point(0.92 * s, 0.42 * s), new Point(0.08 * s, 0.42 * s),
        };
        var table = new Polygon { Fill = Brushes.White, Opacity = 0.75 };
        table.Points = new PointCollection
        {
            new Point(0.36 * s, 0.02 * s), new Point(0.64 * s, 0.02 * s),
            new Point(0.70 * s, 0.12 * s), new Point(0.30 * s, 0.12 * s),
        };
        c.Children.Add(pav); c.Children.Add(crown); c.Children.Add(table);
        return c;
    }

    /// <summary>一颗蓝宝石: 六边形切面, 中间再压一块浅色台面。</summary>
    static Canvas Gem(double s)
    {
        var c = new Canvas { Width = s, Height = s, IsHitTestVisible = false };
        var body = new Polygon { Fill = GEM_C, Stroke = GOLD_EDGE, StrokeThickness = 1.0 };
        body.Points = new PointCollection
        {
            new Point(0.50 * s, 0.04 * s), new Point(0.92 * s, 0.28 * s), new Point(0.92 * s, 0.68 * s),
            new Point(0.50 * s, 0.96 * s), new Point(0.08 * s, 0.68 * s), new Point(0.08 * s, 0.28 * s),
        };
        var face = new Polygon { Fill = GEM_D, Opacity = 0.65 };
        face.Points = new PointCollection
        {
            new Point(0.50 * s, 0.04 * s), new Point(0.92 * s, 0.28 * s),
            new Point(0.50 * s, 0.52 * s), new Point(0.08 * s, 0.28 * s),
        };
        c.Children.Add(body); c.Children.Add(face);
        var hi = new Ellipse { Width = s * 0.20, Height = s * 0.14, Fill = Brushes.White, Opacity = 0.7 };
        Canvas.SetLeft(hi, s * 0.22); Canvas.SetTop(hi, s * 0.30);
        c.Children.Add(hi);
        return c;
    }

    /// <summary>一颗四角小星(金闪闪的那种反光)。</summary>
    static Canvas GoldSpark(double s)
    {
        var c = new Canvas { Width = s, Height = s, IsHitTestVisible = false };
        var p = new Polygon { Fill = GOLD_HI, Opacity = 0.95 };
        p.Points = new PointCollection
        {
            new Point(0.50 * s, 0.00 * s), new Point(0.60 * s, 0.40 * s), new Point(1.00 * s, 0.50 * s),
            new Point(0.60 * s, 0.60 * s), new Point(0.50 * s, 1.00 * s), new Point(0.40 * s, 0.60 * s),
            new Point(0.00 * s, 0.50 * s), new Point(0.40 * s, 0.40 * s),
        };
        c.Children.Add(p);
        return c;
    }

    /// <summary>
    /// 金底那层斜纹: 12px 一格、45° 的细白线平铺 —— 纯矢量, 面板多大都不糊,
    /// 就是"金属拉丝"那一层质感。和 GridPaper 一个套路, 只是斜着来。
    /// </summary>
    static DrawingBrush GoldWash()
    {
        var g = new GeometryGroup();
        g.Children.Add(new LineGeometry(new Point(0, 12), new Point(12, 0)));
        return new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 12, 12),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing
            {
                Geometry = g,
                Pen = new Pen(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), 1.6)
            }
        };
    }

    // ---------- 笔刷 ----------
    // 不再是 readonly: 换主题要整组换掉。色值取自 Themes, 见上面的表。
    static Brush PanelBg = new SolidColorBrush(Color.FromArgb(0xE8, 0x14, 0x14, 0x20));
    static Brush Normal = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x28));
    static Brush Active = new SolidColorBrush(Color.FromRgb(0x0C, 0x44, 0x7C));
    static Brush ActiveEdge = new SolidColorBrush(Color.FromRgb(0x85, 0xB7, 0xEB));
    /// <summary>修饰键"粘住"时的底色: 比选中(Active)暗一档, 比普通键亮一档。</summary>
    static Brush Lit = new SolidColorBrush(Color.FromRgb(0x1C, 0x34, 0x54));
    static Brush LitEdge = new SolidColorBrush(Color.FromRgb(0x56, 0x86, 0xB8));
    static Brush DimEdge = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x48));
    /// <summary>九宫格里"空位"的框: 比常态更淡, 看得出这儿还有个位置, 又不去抢注意力。</summary>
    static Brush EmptyEdge = new SolidColorBrush(Color.FromArgb(0x30, 0x7A, 0x7A, 0x8C));
    /// <summary>
    /// 进下一层时垫在中间的"毛玻璃"压暗色。**取当前主题的面板色**, 不是纯黑 ——
    /// 以前是 0x70 的黑, 在可爱粉(浅色面板 + 粉边)那套上就是一块灰疙瘩贴着粉边,
    /// 跟边框明显是两种东西; 换成面板本色之后, 它就是"同一块面糊掉了", 看不出接缝。
    /// </summary>
    static Color VeilC = Color.FromArgb(0x55, 0x14, 0x14, 0x20);
    static readonly SolidColorBrush VeilBrush = new(VeilC);
    // 跟着主题走的两个色(见 ApplyTheme)。不是 readonly —— 换主题时改的是它们的 .Color。
    static Color AccentC = Color.FromRgb(0xFA, 0xC7, 0x75);
    static Color ActiveEdgeC = Color.FromRgb(0x85, 0xB7, 0xEB);
    // 这六个写成 SolidColorBrush(而不是 Brush)是为了换主题时能直接改它们的 .Color ——
    // 建界面时各处 TextBlock 拿走的是同一个笔刷实例, 改颜色 = 所有文字一起换,
    // 不用去遍历整棵视觉树。readonly 没关系: 不许换"笔刷", 但可以换"笔刷的颜色"。
    static readonly SolidColorBrush Fg = new(Color.FromRgb(0xE8, 0xE8, 0xF0));
    static readonly SolidColorBrush FgHot = new(Color.FromRgb(0xE6, 0xF1, 0xFB));
    static readonly SolidColorBrush Sub = new(Color.FromRgb(0x7A, 0x7A, 0x8C));
    static readonly SolidColorBrush Accent = new(Color.FromRgb(0xFA, 0xC7, 0x75));
    /// <summary>
    /// 压在主题色底上的那个颜色 —— 现在只有"使用中"那枚对勾小圆牌用它。
    /// 主题色有亮有暗(默认亮黄、玻璃是宝蓝、浮字是浅蓝、可爱粉是桃红),
    /// 勾写死成白色的话, 亮黄和浅蓝那两套上等于白勾压亮底, 一个字都看不见。
    /// 所以按主题色自己的亮度翻: 亮底用深勾、暗底用白勾(见 ApplyTheme)。
    /// </summary>
    static readonly SolidColorBrush OnAccent = new(Color.FromRgb(0x1A, 0x1A, 0x20));
    /// <summary>0..1, 越大越亮。按视觉权重算(绿最亮、蓝最暗), 不是三个数取平均。</summary>
    static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
    static readonly SolidColorBrush Pinyin = new(Color.FromRgb(0x9F, 0xC7, 0xF5));
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
    /// <summary>页码点没被选中那几个: 比 Sub 再淡一点, 免得跟亮的那个抢眼。</summary>
    static readonly SolidColorBrush DotIdle = new(Color.FromRgb(0x4A, 0x4A, 0x5C));
    // 设置页那几个按钮(打开日志 / 打开数据目录 / 退出程序 / 动态效果 ...)。
    // 底和边必须是"跟着主题走的共享实例": 写死深紫底的话, 贴纸风把字色换成深棕之后
    // 就是深底压深字 —— 整颗按钮变成一块黑疙瘩, 一个字也看不见。
    static readonly SolidColorBrush BtnBg = new(Color.FromRgb(0x33, 0x33, 0x4A));
    static readonly SolidColorBrush BtnEdge = new(Color.FromRgb(0x3C, 0x3C, 0x48));

    /// <summary>当前这套是不是贴纸风(Skin=1)。Paint 里改键的边框粗细要用。</summary>
    static bool SkinOn;
    /// <summary>当前皮肤档: 0 纯色 / 1 贴纸 / 3 浮字。Paint 等处按档分流。</summary>
    static int SkinMode;

    // 数字层(按住 LT)时副标题是空的 —— 那时候九宫格就是纯数字, 底下不写字母。
    // 1 号位也是空的: 九键里 1 没有功能了(切分统一归 Y), 写着"，"会让人以为它能打标点。
    /// <summary>
    /// 九宫格每颗键下面那行小字(字母)。**下标 = 键上的数字**, 0 号不用。
    /// <para>2.0.0 起 JKL 在<b>左上角那颗 1</b> 上, 正中间那颗 5 改写了"快捷"两个字 ——
    /// 滑行输入要用摇杆绕一圈选字母, 而正中间摇杆永远转不到(那就是"松手回中"的位置),
    /// 与其让 JKL 躺在一个够不着的地方, 不如把它搬到左上角、把中心让给常用的面板。
    /// 加上 j / k / l 在拼音里只做声母、不会出现在音节中间, 换位置几乎不影响手感。</para>
    /// </summary>
    static readonly string[] Key9Sub = { "", "JKL", "ABC", "DEF", "GHI", "快捷", "MNO", "PQRS", "TUV", "WXYZ" };

    /// <summary>显示宽度: 中日韩字按两格算, 其余一格。给"字号该放大到多少"用。</summary>
    static int VisualLen(string s)
    {
        int n = 0;
        foreach (var c in s) n += c >= 0x2E80 ? 2 : 1;
        return n;
    }

}
