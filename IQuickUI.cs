namespace Binput;

// ================= 窗口F: 界面那一摞页对外的一面 =================
//
// 2.1.1 起窗口F 和设置页共用同一套九宫格页面(见 LayerHost): 两套只是摆的地方
// 不同(设置摆在背面那张卡里, F 摆在正面的键盘区), 画格子 / 浮起来 / 沉下去 /
// 翻页 / 回中全是一份代码。(2.1.3 起这些页面**是平级的**, 不再一层摞一层 ——
// 对外这一套接口一个字没变。)于是 F 这边对外也就照抄设置页那份"接口"的样子:
//
//   InputCore 只认这一个接口, 不认 OverlayWindow ——
//   手柄上按了什么由 core 判, 判完叫这里的一句; 这一摞现在长什么样(有几层、
//   光标在哪一格)由窗口自己记, core 不用知道格子的存在。

/// <summary>窗口F 那一摞页面的操作面(由 OverlayWindow 实现, InputCore 拿着用)。</summary>
public interface IQuickUI
{
    /// <summary>现在叠了几层: 1 = 只有列表层。</summary>
    int Depth { get; }

    /// <summary>
    /// 最上面那一层的名字 —— "快捷" / "编辑" / "绑数字"。
    /// core 拿它认"现在在哪一层"(比如 Y 只在"绑数字"那层是解除绑定)。
    /// </summary>
    string Layer { get; }

    /// <summary>十字键上下(三列, 所以是 ±3)。</summary>
    void Nav(int d);

    /// <summary>十字键左右。</summary>
    void NavH(int d);

    /// <summary>左摇杆: 和九键选键同一套(推哪选哪、松手回中)。</summary>
    void Stick(double x, double y);

    /// <summary>A 短按: 选中光标这一格。</summary>
    void Pick();

    /// <summary>A 长按: 从列表层往下钻一层(改光标这一格)。</summary>
    void Deep();

    /// <summary>B。已经在最底下那层了就返回 false —— 让外面把 F 整个关掉。</summary>
    bool Back(bool toRoot);

    /// <summary>LB / RB: 翻页。</summary>
    void Page(int d);

    /// <summary>内容变了, 按现在的状态把每一层重画一遍。</summary>
    void Refresh();

    /// <summary>弹掉最上面一层。</summary>
    void Pop();

    /// <summary>
    /// 这一条**搬到别的格子去了**(绑数字 / 解除绑定都会搬), 深层跟着改认那一格。
    /// 不改的话深层还钉在原来那格上 —— 可那条东西已经不在那儿了, 底下那条长条
    /// 就成了空的, 接着改也就改错了地方。
    /// </summary>
    void Move(int slot);

    /// <summary>
    /// Y。只在"绑数字"那层有意义(解除绑定), 其余层什么都不做。
    /// (2.3.3 之前这一桩挂在 L3 上, 和九键数字层里"L3 出 0"是同一个手势。)
    /// </summary>
    void Y();

    /// <summary>
    /// 录快捷键那一页**进去**时叫一声: 那一页屏幕上摆的是全键盘(左右扩张出来),
    /// 这一摞得先整个让开。演完之后窗口自己会按真实状态把它放回来。
    /// </summary>
    void StageOff();

    /// <summary>
    /// 录快捷键那一页**退出来**时叫一声: 这一摞**立刻**回到台上, 不等"左右扩张"
    /// 演完 —— 演的这 230 毫秒里键盘区正在从 640 收回到 360, 要是还让全键盘顶在
    /// 那儿, 它会被整体横向缩到 360/640, 看着就是"键被挤窄了一下再变回九键"。
    /// 现在改成就地接回这一摞, 只有框的左右两条边在收。
    /// </summary>
    void StageOn();
}
