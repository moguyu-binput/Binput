using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Binput;

// ================= 一层层九宫格: 页面放在哪儿、怎么串起来 =================
//
// 设置页那套"首页 → 二级 → 三级"的九宫格, 窗口F 从 2.1.1 起要**一模一样**
// 地用一回(长按快捷格 -> 九颗键翻成九个快捷按钮 -> 长按某个按钮再进编辑页)。
// 两套界面只是"摆在哪个容器里"不同: 设置摆在背面那张卡里, 窗口F 摆在正面键盘区。
// 于是把"这一摞页"自己的状态收在这里, 谁要用就抱一份走 —— 画格子、浮起来、
// 沉下去、翻页、回中那一整套代码跟着就都能共用, 不用照抄第二遍。
//
// 【2.1.3: 页面是平的, 不再一层压一层】
// 以前每一个栈层都**真的**挂着一块 Border 摆在 Stage 里: 上层盖着下层,
// 下层被缩到 0.94、压暗、还得垫一层把整个 Stage 拍成图糊掉的毛玻璃。
// 于是越往里, Stage 里同时活着的元素越多, 而且"底下那层还在"这件事在界面上
// 看得出来(去掉格子底板之后尤其明显)。
// 现在规矩改成: **同一时刻只有现在这一页被画出来**。老页只留**数据**在 Pages 里
// (退回上一层时要原样回到那一页), 画出来的东西只有 Panel / Grid / Boxes 这一套。
// 导航到更深一页 = "旧页出门、新页进门", 不是"往旧页上再摞一层"。
//
// 好处之一: 以后要换过渡动画, 契约只有一句 —— 演完之后场上只剩新页, 演的时候
// 旧页爱怎么用怎么用(留着真身也好、换成一张图也好)。详见 PushPage / PopPage。

/// <summary>一摞页(设置一份、窗口F 一份)。</summary>
sealed class LayerHost
{
    /// <summary>这一摞摆在哪个容器里(设置 = 背面的 SetStage, 窗口F = 正面的键盘区)。</summary>
    public Grid Stage;

    /// <summary>
    /// 走过的那一串页面, 最后面那个是**现在这一页**。
    /// 坑: 这里只有**数据**(标题、格子内容、光标停在第几格、第几页),
    /// 它们自己不带 UI —— 面板和格子不存在 Pages 里, 全摞只有**当前那一页**有一套。
    /// B 退一层就是弹出最后一个; 弹出之后必须把新露出来的那个**重新搭一遍**
    /// (你可能在更深的地方改过它上面写的值)。
    /// </summary>
    public readonly List<SetPage> Pages = new();

    /// <summary>现在画出来的那块面板(整个 Stage 里就这么一块)。</summary>
    public Border Panel;

    /// <summary>Panel 里装格子的那个容器。</summary>
    public Grid Grid;

    /// <summary>现在这一页那几个格子(画光标 / 按下去缩回去要用)。</summary>
    public List<Border> Boxes = new();

    /// <summary>
    /// 每格**外面**那层容器, 和 <see cref="Boxes"/> 一一对应(第 i 项就是第 i 格的外层)。
    /// <para>格子自己那套 RenderTransform 归"光标浮起 / 按下去"用(见 CellPop), 浮力
    /// 那一层水波只能挂在这外面 —— 两层的变换是叠上去的, 谁也不覆盖谁(和九键那颗键
    /// 与它的 _key9Host 是同一个规矩)。不用它的时候它是空的, 不影响布局。</para>
    /// </summary>
    public List<FrameworkElement> Hosts = new();

    /// <summary>过渡那 180 毫秒里"马上要走的那一页"摆在这儿(见 ShowVeil)。</summary>
    public Border Veil;
    public Image VeilImg;

    /// <summary>进出页的动画正在演: 这期间一律不接受操作, 免得按出半个状态。</summary>
    public bool Anim;

    /// <summary>现在浮着的那一格(光标所在的)。</summary>
    public Border Cur;

    /// <summary>A 正按着(那一格要缩下去)。</summary>
    public bool Down;

    /// <summary>左摇杆是不是正推着(松手那一下要落回正中)。</summary>
    public bool StickNav;

    /// <summary>
    /// 左摇杆**此刻**推到哪儿了(-1~1, 原始值)。
    /// 记它是因为换页 / 翻页那一下要回头问一句: 手还拖着吗?
    /// 拖着就落在摇杆指的那一格, 不许先回正中再跳过去(见 OverlayWindow.EntrySlot) ——
    /// 以前不问这一句, 于是"拖着摇杆按 A 进深层"会看见光标在正中闪一下。
    /// **每一帧都记**(哪怕这一帧不能操作), 否则动画演的 180 毫秒里握着的旧值说话。
    /// </summary>
    public double StickX, StickY;

    /// <summary>这一摞现在接不接操作(设置 = 翻到背面了吗, 窗口F = 面板开着吗)。</summary>
    public Func<bool> Active = () => false;

    /// <summary>鼠标左键点中第 i 格要干什么(手柄走 A, 鼠标走这个)。</summary>
    public Action<int> Pick;

    /// <summary>
    /// 鼠标**右键**点中第 i 格要干什么。手柄上"长按 A"能进深层, 鼠标没有长按这一手,
    /// 所以给它单独留一条路(窗口F 里就是"编辑这一格")。
    /// </summary>
    public Action<int> Pick2;

    /// <summary>毛玻璃那个 SizeChanged 钩子挂上了吗(见 OverlayWindow.EnsureVeil)。</summary>
    public bool VeilHooked;

    /// <summary>重搭完页面之后刷一下"框外面那几样"(设置 = 标题 + 页码点, 窗口F = 页码点)。</summary>
    public Action Chrome;

    public int Count => Pages.Count;
    public SetPage Top => Pages.Count == 0 ? null : Pages[^1];

    /// <summary>摇杆那一套作废: 忘了现在推到哪儿、也忘了"正推着"这件事。</summary>
    public void ForgetStick() { StickNav = false; StickX = 0; StickY = 0; }

    /// <summary>整摞撤掉(下次重新搭)。</summary>
    public void Reset()
    {
        Pages.Clear();
        Panel = null;
        Grid = null;
        Boxes = new List<Border>();
        Stage?.Children.Clear();
        Veil = null;
        VeilImg = null;
        // 【VeilHooked 不在这儿清】它记的是"SizeChanged 钩子挂在 Stage 上了吗",
        // 而 Stage(背面的 SetStage / 正面的 QuickStage)是窗口里那个不会换的容器 ——
        // 清掉的话下一回重搭又会挂一个上去, F 开关十次就挂上十个。
        Anim = false;
        Cur = null;
        Down = false;
        ForgetStick();
    }
}
