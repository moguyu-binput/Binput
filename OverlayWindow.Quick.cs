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
// ---- 快捷面板 —— IQuickUI 那套接口 ----
//
// 2.1.11 之前这些代码全在 OverlayWindow.xaml.cs 一个文件里(5117 行)。
// 这次只是按职责把它拆成几个 partial class 文件 —— 一行都没改,
// 编译出来的行为跟拆之前完全一样(改名/搬位置不影响任何逻辑)。

    int _qSlot = -1;

    /// <summary>F 这一摞现在接不接操作(录快捷键那一页不算那一摞的)。</summary>
    bool _qkOn;

    /// <summary>
    /// 窗口F 开着(连录快捷键那一页一起算)。这个要留给 <see cref="ApplyExStyle"/>:
    /// F 占着屏幕时窗口得能吃焦点, 否则鼠标点不动格子。
    /// </summary>
    bool _qkFocus;

    /// <summary>按下去真要干的事(由 InputCore 实现)。</summary>
    public IQuickHost QHost;

    // ---- IQuickUI: 给 InputCore 用的那一面 ----
    // 写成**显式实现**(void IQuickUI.Xxx): 这几个名字(Nav / Pick / Back...)太通用,
    // 不该混进窗口自己的方法堆里; 而且 core 那边本来就只认接口, 用不着公开。

    int IQuickUI.Depth => _qk.Count;
    string IQuickUI.Layer => _qk.Top?.Title ?? "";
    void IQuickUI.Nav(int d) => MoveSel(_qk, d * 3);
    void IQuickUI.NavH(int d) => MoveSel(_qk, d);
    void IQuickUI.Stick(double x, double y) => LayerStick(_qk, x, y);
    void IQuickUI.Page(int d) => LayerPage(_qk, d);
    void IQuickUI.Pick() => QuickPickSel();
    void IQuickUI.Deep() => QuickDeep();
    bool IQuickUI.Back(bool toRoot) => LayerBack(_qk, toRoot, RefreshQuickSide);
    void IQuickUI.Refresh() => RefreshQuickSide();

    /// <summary>弹掉最上面一层(绑好了、删掉了, 都往回退一层)。</summary>
    void IQuickUI.Pop()
    {
        if (_qk.Count > 1) PopPage(_qk, true);
    }

    /// <summary>
    /// 录快捷键那一页**进去**时叫一声: 那一页屏幕上摆的是全键盘(左右扩张出来),
    /// 这一摞得先整个让开。等扩张那一段演完, Render 自己会把它放回来。
    /// </summary>
    void IQuickUI.StageOff() { _qkOn = false; }

    /// <summary>
    /// 录快捷键那一页**退出来**时叫一声: 这一摞**立刻**回到台上, 不等收回演完。
    /// 【为什么进来要让开、出去反倒要抢回来】进来那一段是"全键盘撑开", 屏幕上
    /// 该看见的是键; 出去那一段是"从 640 收回 360", 屏幕上该看见的已经是这一摞了。
    /// 以前出去也走 StageOff, 于是演的这 230 毫秒里全键盘还顶在那儿被横向压到
    /// 0.5625 —— 用户原话"全键盘的按键会有个缩窄的画面出现"。
    /// Render 在 _expanding 期间不碰 _qkOn, 所以这里设的值能一直顶到演完。
    /// </summary>
    void IQuickUI.StageOn() { _qkOn = true; }

    /// <summary>
    /// 选中光标这一格。**列表层的空格子往下钻一层** —— 空格子本来就没东西可发,
    /// 按上去的意思多半是"往这一格写点什么", 直接浮出深层省一下。
    /// </summary>
    void QuickPickSel()
    {
        var p = _qk.Top;
        if (p != null && p.Title == InputCore.Q_LAYER_LIST
            && QuickStore.At(BaseOf(p) + p.Sel) == null)
        {
            QuickDeep();
            return;
        }
        LayerActivate(_qk, RefreshQuickSide);
    }

    /// <summary>
    /// 长按 A: 从列表层往下钻一层(改光标这一格)。只有列表层能钻 —— 深层里再长按
    /// 没有第二层可去。
    /// </summary>
    public void QuickDeep()
    {
        if (!CanNav(_qk)) return;
        var p = _qk.Top;
        if (p == null || p.Title != InputCore.Q_LAYER_LIST) return;
        int slot = BaseOf(p) + p.Sel;
        if (slot < 0 || slot >= QuickStore.MaxSlots) return;
        _qSlot = slot;
        // 深一层是"两行 + 底下那条长条", 长条占了原来第三行的位置 —— 于是整块
        // 面板的**正中是第二行中间那一格**(绑的数字), 光标就落在那儿(home: 4)。
        // 【页面里一律读 _qSlot, 不把 slot 抓进闭包】绑数字 / 解除绑定会把这一条
        // 搬到别的格子上(见 IQuickUI.Move), 抓死了的话重画出来还是搬之前那一格。
        PushPage(_qk, QuickDeepPage(), true, home: 4);
    }

    /// <summary>这一条搬到别处去了: 深层跟着改认那一格(退回 / 重画由 core 那边叫)。</summary>
    void IQuickUI.Move(int slot) { _qSlot = slot; }

    /// <summary>Y: 只在"绑数字"那层有意义(解除绑定), 别的层按了没反应。</summary>
    void IQuickUI.Y()
    {
        if (_qk.Top?.Title != InputCore.Q_LAYER_NUM) return;
        QHost?.Unbind(_qSlot);
        RefreshQuickSide();
    }

    // ---- 三层各装什么 ----

    /// <summary>
    /// 列表层: 5 页 × 9 格 = 45 格。第 1 页那九格左上角写着 1~9 —— 那一格就是
    /// 长按数字几会发出来的那条。空格子中央什么都不写, 但**照样能停能选中**
    /// (停上去长按就是"往这一格写东西")。
    /// </summary>
    Border QuickBar(int slot)
    {
        var it = QuickStore.At(slot);
        var tb = new TextBlock
        {
            Text = it == null ? "" : (it.IsPhrase ? it.Text : it.Show),
            FontSize = 15,
            Foreground = it == null ? Sub : (it.IsPhrase ? Fg : Pinyin),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = SET_CELL * 3 - 8,
            MaxHeight = SET_CELL - 10,
        };
        var b = new Border
        {
            Width = 3 * (SET_CELL + SET_GAP * 2) - SET_GAP * 2,   // 360 - 8
            Height = SET_CELL,
            Margin = new Thickness(SET_GAP),
            Background = Normal,
            BorderBrush = DimEdge,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,          // 写不下的那截就让它看不见, 别溢到别处
            Child = tb,
        };
        // 352×112, 圆形那一档按短边取 = 两头半圆的胶囊
        ShapeBase(b, _cellR);
        return b;
    }

    // ---- 搭 / 拆 / 重画 ----

    /// <summary>
    /// 窗口F 刚开: 把那一摞搭起来(每次开都现搭, 数据改过也能看见)。
    /// </summary>
    /// <param name="sx">开 F 这一刻左摇杆推到哪儿了(见下面那句为什么非得传进来)。</param>
    void BuildQuickSide(double sx = 0, double sy = 0)
    {
        _qk.Reset();
        // 【Reset() 会把"手推到哪儿"忘掉, 于是进 F 会闪一下正中】
        // 2.1.4 立过一条规矩: 手还在摇杆上就听摇杆的, 没碰才回正中(见 EntrySlot)。
        // 可"拖着摇杆按 A 进下一层"那三处补了, **从九键长按快捷格开 F 这一处漏了** ——
        // 快捷在正中间那套摆法看不出来(那时根本不用推摇杆, 真值本来就是 0);
        // JKL 在正中间那套摆法下快捷入口是**左上角那一格**(QuickCell = 1),
        // 得把摇杆推到左上并按住 450ms, 于是开 F 那一刻手在摇杆上 —— 而 Reset()
        // 刚把 StickX/Y 清成 0, EntrySlot 问"手还拖着吗"拿到的是"没碰",
        // 光标就先落正中, 下一帧才被 LayerStick 拽回左上角, 中间闪一下。
        // 把真值灌回去就没有这一下了(传的是 core 那一帧的 StickX/Y,
        // 见 Render 里的调用点)。
        _qk.StickX = sx;
        _qk.StickY = sy;
        _qk.Stage = QuickStage;
        _qk.Active = () => _qkOn;
        // 鼠标左键 = 手柄短按 A(见 QuickPickSel); 右键 = 手柄长按 A。
        // 鼠标没有"按住不放"这一手, 所以"改这一格"得靠右键 —— 两条路都留着。
        _qk.Pick = _ => QuickPickSel();
        _qk.Pick2 = _ => QuickDeep();
        _qk.Chrome = PaintQuickChrome;
        PushPage(_qk, QuickListPage(), false);
    }

    /// <summary>
    /// 框外面那两样: 左上角"现在在哪一层", 底带中间那排页码点。
    /// 和背面那套(PaintChrome)是同一回事 —— 卡里那九格就是九颗键那么大,
    /// 标题挤进去就得把格子压扁。
    /// </summary>
    void PaintQuickChrome()
    {
        var p = _qk.Top;
        if (p == null) return;
        QuickTitle.Text = p.Title;
        PaintDots(QuickDots, PagesOf(p), p.Page);
    }

    /// <summary>内容变了(写过、删过、绑过): 按现在的状态把每一层重画一遍。</summary>
    void RefreshQuickSide()
    {
        if (QHost == null) return;
        RefreshPage(_qk);
    }

    // ---------- 换页的过渡动画 ----------
    //
    // 【2.1.3: 不再一层压一层, 但那点"往里走了"的意思还留着】
    // 换页那 180 毫秒里, 旧页会留**一张快照**在场上替它演:
    // 新页从下面浮上来(0.92 倍 + 下移 26px + 淡入), 那张快照同时缩小、下沉、淡出。
    // 演完快照就撤了 —— 它不是摞在底下的又一页, 就是一个过渡的道具。
    // 于是"进更深一层"和"退回上一层"看着还是同一个动作的正反两面, 但背后
    // 场上从头到尾只有**一块**页面在被画(见 LayerHost 顶部那段说明)。
    //
    // 【以后要换动画的下手点】只要满足一条: 演完之后 Stage 里只剩新页。
    // 于是左右推进、交叉淡化、整块翻转、抽走……想换成什么都行, 改 ShowPage 那一处
    // (决定"拿什么当道具")和这里这一对(决定"怎么演")就够了。

}
