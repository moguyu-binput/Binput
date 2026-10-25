namespace Binput;

/// <summary>
/// 换页面的动效风格 —— 设置首页「动效」那一格往里钻一层挑。
/// <para>这一项决定**所有"换一块内容"的动作**怎么演: 往设置里钻一层、退回上一层、
/// 九键换成标点那一面、按住 LB 进滑行、LB / RB 翻页 —— 全走同一套。
/// 九键 <-> 全键盘那种"左右撑开"不算换页, 不在这儿.</para>
/// <para>加一种新风格的步骤: 这里加一个成员, FxKit 里加一组数字,
/// 再到 OverlayWindow.Fx.cs 那两只柜子各挂一笔(见那里头的表)。</para>
/// </summary>
public enum FxStyle
{
    /// <summary>默认: 旧页缩小下沉淡出、新页放大浮起淡入。最省眼的一套。</summary>
    Plain = 0,
    /// <summary>扑克牌: 绕整块左下角旋转, 旧页顺时针转走、新页从另一边转回来。</summary>
    Spin = 1,
    /// <summary>
    /// 平移: 手机上左右滑页那样。旧的一页横着滑走、一边滑一边淡掉, 新的一页
    /// 紧贴着它从另一边滑进来、由透明转实 —— 两页**并排一起走**, 看着是一整条
    /// 内容在移动。退回上一层时整个动作反过来(旧页往右走、新页从左边来)。
    /// <para>和「旋转」用在**完全相同的一批过渡**上 —— 两套只是"怎么走"不同。</para>
    /// </summary>
    Slide = 2,
    /// <summary>
    /// 浮力: **换页那一整套照搬「默认」** —— 进设置、换面、LB / RB 翻页全都还是
    /// 旧的缩小下沉 / 浮起淡入, 一个数都不改。
    /// <para>它只加一样东西: 九键那一页的九颗键从此**常驻浮在水面上** —— 全体放大
    /// 一点、各自按自己的周期(3~4 秒一轮)错开相位缓慢漂动, 靠边那几颗会蹭出框外
    /// 一点点, 表示它们是浮在框上面的。光标照旧: 摇杆停在哪颗, 哪颗就在这一层
    /// 之上再放大一档、跳一下。</para>
    /// </summary>
    Buoy = 3,
}

/// <summary>
/// 【加一种新风格, 从六处变成一处】枚举上面加一个成员, 再往 <see cref="FxStyles.All"/>
/// 里补一条 <see cref="FxStyles.Info"/> —— 就这两处, 都在本文件里。
/// <para>以前是六处: 这里的枚举、<c>App.FxStyleName</c> 那个 switch、
/// <c>Config.LoadFxStyle</c> 那条手写 if 链、设置页里 <c>FxStyleNotes</c> 和
/// <c>FxStyleTips</c> 两个数组, 外加设置页摆几项由数组**长度**决定。
/// 漏掉任何一处都不报错: 名字静默退回"默认"、存档静默退回 Plain、说明是空的 ——
/// 都是"看着能用、其实不是你加的那一种", 只有真机上看得出来。</para>
/// <para>现在后四处全由这一张表推出来: 名字 / 说明 / 落盘解析读表, 设置页摆几项
/// 由表的长度决定。剩下的两处(枚举成员、表里那条)**必须**一起出现 ——
/// 而且 coretest 【34】会拿枚举值挨个比对这张表, 少一条就红。</para>
/// </summary>
public static class FxStyles
{
    /// <summary>一种风格的"人话"三件套: 格子里的大字、第二行小字、底下那条长说明。</summary>
    public readonly record struct Info(string Name, string Note, string Tip);

    /// <summary>
    /// **顺序必须和 <see cref="FxStyle"/> 的数值一致**(下标就是枚举值)。
    /// 加一种风格 = 这里加一条; 漏了会被 coretest 【34】点名。
    /// </summary>
    public static readonly Info[] All =
    {
        new Info(
            "默认",
            "浮起 · 淡入",
            "旧的一页缩小下沉淡出, 新的一页放大浮起来淡入。最省眼睛的一套, "
            + "一直以来的样子 —— 拿不准就先用它。"),

        new Info(
            "旋转",
            "扑克牌 · 旋转",
            "整块绕左下角转: 旧的一页顺时针转出去一边转一边淡掉, 新的从另一边 "
            + "转回来、由透明转实。两页之间始终差着一个不大的角度, 像手指捻开 "
            + "一把扑克牌。按 B 退回上一层时整个动作反过来, 逆时针转回去。"),

        new Info(
            "平移",
            "手机 · 平移",
            "手机上左右滑页那样: 旧的一页横着往左滑走、一边滑一边淡掉, 新的一页 "
            + "紧贴着它从右边滑进来、由透明转实 —— 两页并排一起走, 看着是一整条 "
            + "内容在移动。按 B 退回上一层时整个反过来: 旧页往右走、新页从左边来。 "
            + "和「旋转」用在完全相同的一批过渡上, 只是走法不同。"),

        new Info(
            "浮力",
            "水面 · 浮力",
            "换页那一套**照搬「默认」** —— 进设置、换九键那一面、LB / RB 翻页, 还是 "
            + "原来那个浮起淡入, 一点没改。多的只有**屏幕上那一批格子从此浮在水面上**: "
            + "九颗键、标点 5×5 那 25 格、设置和窗口F 里的每一格(789 合起来那条长条、"
            + "「关于」并成的那一大块也算), 全体放大一点, 各自按自己的周期(三到四秒 "
            + "一轮)错开相位慢慢漂 —— 不是一块板在晃, 是每个各自在荡。靠边那几个会蹭出 "
            + "框外一点点, 那是它们浮在框上面的意思。格子越大漂得越含蓄(一大块只放大 "
            + "1.03), 小格子才不会撞到邻居。摇杆停在哪格, 哪格照旧在这一层之上再放大 "
            + "一档、跳一下。全键盘那七排不做 —— 键太多太密。"),
    };

    /// <summary>表里有没有这一档(存档里可能存着认不出的数)。</summary>
    public static bool Has(FxStyle s) => (int)s >= 0 && (int)s < All.Length;

    /// <summary>取一档的完整信息。认不出就退回默认那档(不是 null, 调用方不必判空)。</summary>
    public static Info Of(FxStyle s) => Has(s) ? All[(int)s] : All[0];

    public static string Name(FxStyle s) => Of(s).Name;
    public static string Note(FxStyle s) => Of(s).Note;
    public static string Tip(FxStyle s) => Of(s).Tip;

    /// <summary>设置页摆几项 —— 由表的长度决定, 不再另写一个数组去数。</summary>
    public static int Count => All.Length;

    /// <summary>
    /// 【给探针用】表有没有跟上枚举。返回空列表 = 齐的; 有内容就是"漏了哪一条"。
    /// <para>为什么要有它: 表和枚举是两个东西, C# 不会替你检查它们对不对得上。
    /// 少一条的话 <see cref="Of"/> 会退回默认那档 —— 界面上看着是"默认",
    /// 谁也不知道其实是新加的那种没登记。见 coretest 【34】。</para>
    /// </summary>
    public static List<string> Gaps()
    {
        var bad = new List<string>();
        foreach (FxStyle s in Enum.GetValues(typeof(FxStyle)))
        {
            if (!Has(s)) { bad.Add($"{(int)s} 在表里没有对应的一条"); continue; }
            var it = All[(int)s];
            if (string.IsNullOrWhiteSpace(it.Name)) bad.Add($"{s}: 缺名字");
            if (string.IsNullOrWhiteSpace(it.Note)) bad.Add($"{s}: 缺第二行小字");
            if (string.IsNullOrWhiteSpace(it.Tip)) bad.Add($"{s}: 缺底下的长说明");
        }
        return bad;
    }
}
