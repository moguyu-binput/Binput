namespace Binput;

/// <summary>
/// 设置窗口和主程序之间的接口。
/// 以前托盘菜单里塞了一堆开关, 越加越杂 —— 现在都搬到设置页里,
/// 而且以后还会继续加项, 用接口比在构造函数里塞一串委托好扩:
/// 新增一项只要在这里加一个属性/方法 + 页面上加一个控件。
/// </summary>
public interface ISettingsHost
{
    /// <summary>
    /// 版本: true = 预输入版(前台窗口 + 记事本预览框, 收起时复制粘贴),
    /// false = 直出版(不抢焦点, 按键直接送到当前有焦点的窗口)。
    /// 两个版本的打法一样, 只是"字先去哪儿"不同。
    /// </summary>
    bool Preview { get; set; }
    /// <summary>预输入版收起后的粘贴档位(见 PasteTier)。直出版用不上。</summary>
    int PasteTier { get; set; }
    /// <summary>配色主题(下标, 见 OverlayWindow.Themes)。换它就整块界面换色。</summary>
    int Theme { get; set; }
    /// <summary>
    /// 九键按键上显不显示数字。关掉就只留 ABC / DEF 那串字母(字号也会放大),
    /// 数字层(按住 LT)永远只显示数字, 不受这一项影响。
    /// </summary>
    bool Key9Num { get; set; }
    /// <summary>
    /// 九宫格左上角和正中间那两颗**对调**: 开 = 左上变「快捷」、JKL 挪到中间。
    /// 只对普通九键有效 —— 按住 LB 滑行时这一项一律不生效(摇杆方位不能跟着漂)。
    /// </summary>
    bool Swap15 { get; set; }

    // ---- 滑行输入 ----
    /// <summary>
    /// 滑行开不开: 关 / 开(打字 —— 真的把数字打进拼音)。
    /// 设进来的值就是设置页那个按钮一下一下切出来的那一档。
    /// </summary>
    SlideMode SlideMode { get; set; }
    /// <summary>
    /// 在一格停够多少毫秒才把它当成"你要的这个键"。
    /// **只从 <see cref="Config.DWELL_STEPS"/> 那几档里挑** —— 不再自动标定,
    /// 那是手感问题, 自己拨一档比算法快。
    /// </summary>
    int SlideDwell { get; set; }
    /// <summary>
    /// 认下一个键之后还停着不动的话, 每隔多少毫秒再给一个同样的键(打 66 / 777)。
    /// **和 <see cref="SlideDwell"/> 是两件事**: 那个管"多久才认", 这个管"认了之后
    /// 隔多久重复"。同样只从 <see cref="Config.REPEAT_STEPS"/> 那几档里挑
    /// (2.1.34 起; 以前这一项是写死的 420 毫秒, 界面上动不了)。
    /// </summary>
    int SlideRepeat { get; set; }
    /// <summary>给设置页显示的那段话(滑了多少道、其中几道算数、现在是哪一档)。</summary>
    string SlideInfo();

    /// <summary>
    /// 换页面的动效风格(见 FxStyle): 默认 = 浮起淡入, 旋转 = 扑克牌那样绕
    /// 左下角转。所有"换一块内容"的地方(进层 / 退层 / 换九键那一面 / 翻页)
    /// 都按这一档演, 只有九键 <-> 全键盘的左右撑开不受它管。
    /// </summary>
    FxStyle FxStyle { get; set; }

    /// <summary>
    /// 按键的形状(见 KeyShape): 方块 = 一直那套圆角方块; 圆形 = 正方形的键变正圆
    /// (直径就是原来那个方块的边长)、长条的键变胶囊。
    /// **格子尺寸一个都不动**, 变的只有圆角, 所以布局和窗口大小不受影响。
    /// </summary>
    KeyShape KeyShape { get; set; }

    // ---- 自定义唤醒键 ----
    //
    // 两边都是**清单里挑一个**, 不给自由组合: 键盘 F1~F12, 手柄 Back / Start / Back+Start。
    // 更早的版本是随便定的(键盘能录任意组合、手柄 16 颗键随便挑), 撞车概率太高 ——
    // 修饰键在游戏里各有各的用处, 手柄上每颗键在打字时都有自己的活儿。
    /// <summary>存的那条描述("kb:f7" / "pad:Back+Start", 空串 = 默认的 L3+R3)。</summary>
    string WakeSpec { get; }
    /// <summary>定一个唤醒键。spec 同上; 传空串就是恢复默认的 L3+R3。</summary>
    void SetWake(string spec);
    /// <summary>删掉自定义, 回到默认的 L3+R3。</summary>
    void WakeReset();
    /// <summary>现在定的是哪几个键(只有键名, 不带句子): "L3 + R3" / "F7" / "Back + Start"。</summary>
    string WakeKeys();

    /// <summary>
    /// 进"调窗口大小 / 摆位"那一档(2.3.3)。进去之后十字键上下缩放、左摇杆挪位置,
    /// **按 B 出来**并落盘。以前是"按住 R3"临时进、松手就退, 那颗键在摇杆底下,
    /// 左手正推着摇杆选键的时候够不着, 而且想边看边挪得一直绷着手指。
    /// <para>翻回正面(看着键盘才好调)是窗口自己的事, 这一头只管"进那一档"。</para>
    /// </summary>
    void AdjustWindow();

    void OpenLog();
    void OpenDataDir();
    /// <summary>清掉学来的输入习惯(哪些拼音切分你挑过), 回到出厂排序。</summary>
    void ClearUse();
    /// <summary>把学来的输入习惯导出到一个文件(换机器 / 重装前留一份)。</summary>
    void ExportUse();
    /// <summary>从以前导出的那个文件把输入习惯并回来。</summary>
    void ImportUse();
    /// <summary>
    /// 退出程序, 结束进程。
    /// 不叫 Exit 是故意的: Application 自己就有个 Exit 事件, 同名会把事件顶掉(CS0108)。
    /// </summary>
    void ExitApp();
    /// <summary>关于页那段说明(版本 / 词库 / 路径), 由主程序拼好给过来。</summary>
    string About();
}
