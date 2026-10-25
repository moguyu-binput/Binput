namespace Binput;

// ================= 这一屏是什么 =================
//
// 2.1.14 立起来的。以前"这一帧该不该演一场换页动画"由几处各判各的 —— 九键换一副
// 面孔的地方判一次, 符号网格要不要算换了一屏没人问。于是每加一种新的"屏"
// 就得回头补一处; 5x5 符号网格那一跳整整空了一个版本(它在别处写成"两个元素换班",
// 不在那条判断里)。
//
// 现在只回答一个问题: **这一帧 Root 那张卡片上摆的是什么**。答案是一坨纯数据,
// 不碰任何 WPF —— 于是 tools 下那七套探针(它们本来就编译 InputCore.cs)能把它拉
// 进去跑, 第一次让"某个场景漏了动画"这种事不用只靠眼睛盯。
//
// 【铁律: 只能记结构, 不能记内容】
// 候选那几个字、光标停在第几格, 每帧都在变 —— 记进来就成了一打字就转圈。
// 下面每个字段都是"屏幕上真换了一副骨架"才会变的那种。
//
// 至于演"旧的一页怎么走、新的一页怎么来", 不归这里管 —— 见 OverlayWindow.Fx.cs 那两套。
// 以后要加第三种风格(推 / 卷帘 / 堆叠), 那边加一对方法, 这里一个字都不用动。

/// <summary>
/// Root 这张卡片这一帧摆的是什么。**值相等 = 同一副骨架**。
/// </summary>
/// <param name="Layout">九键 / 全键盘 / 数字层。</param>
/// <param name="Punct9">九宫格是不是摆着那一面标点。</param>
/// <param name="Slide">是不是正在滑行。</param>
/// <param name="Swap15">JKL 那三颗是不是换成"左上恒 JKL"。</param>
/// <param name="Grid">焦点是不是落在网格上 —— 5x5 符号网格和拼音候选网格都算。</param>
/// <param name="PunctGrid">那个网格是不是符号网格。</param>
/// <param name="PunctCat">符号网格里现在是第几个分类。**不在网格里时恒为 0**
/// (这样只有"正开着网格换类"才记一笔 —— 网格里左右翻页就靠它)。</param>
/// <param name="GridCols">网格几列(拼音 3 / 符号 5)。列数一变就是另一种排布。</param>
/// <param name="Quick">窗口F 那一摞是不是正摆在台上(长按「快捷」开出来的那一页)。
/// 它和上面那些一样是**骨架**: F 里翻到第几页归那一摞自己管, 这里只看它开着没有。</param>
public readonly record struct ScreenFace(
    Layout Layout,
    bool Punct9,
    bool Slide,
    bool Swap15,
    bool Grid,
    bool PunctGrid,
    int PunctCat,
    int GridCols,
    bool Quick);

public static class ScreenId
{
    /// <summary>从 InputCore 身上读出这一屏的骨架。</summary>
    public static ScreenFace Of(InputCore c)
    {
        bool grid = c.Focus == FocusZone.CandGrid;
        return new ScreenFace(
            Layout: c.Layout,
            Punct9: c.Punct9,
            Slide: c.Slide.Active,
            Swap15: c.Swap15,
            Grid: grid,
            PunctGrid: grid && c.Punct,
            PunctCat: grid ? c.PunctCat : 0,
            // 【2.1.16: 列数按"是符号还是拼音"推, 不读 c.GridColsNow】
            // 那个数跟着 GridPinyin 走, 而 GridPinyin 比 Focus 晚一帧才翻过来 ——
            // 进符号网格那一瞬会连着认出两副骨架(日志里同一个网格名连着出现两次),
            // 于是同一趟连演两场, 看着就是"闪两下"。
            GridCols: grid ? (c.Punct ? 5 : 3) : c.GridColsNow,
            Quick: c.QuickOpen);
    }

    /// <summary>
    /// 字母九键那一面是**根**: 从根出去算往里走(+1)、回到根算退回来(-1)。
    /// 于是按住 LB 进滑行是一进、松开回字母是一退, 正反两面自动成对,
    /// 不用在哪里再记一笔状态。
    /// </summary>
    public static bool IsRoot(ScreenFace f) =>
        f.Layout == Layout.CN9 && !f.Punct9 && !f.Slide && !f.Grid && !f.Quick;

    /// <summary>
    /// 从 <paramref name="from"/> 走到 <paramref name="to"/> 这一趟该往哪个方向:
    /// +1 往里(下一层 / 下一页) / -1 退回上一层 / 上一页。
    /// <para>【2.1.16: 光看"是不是根"不够了】旋转那一套新动效里, +1 和 -1 演的是
    /// **两套不同的动作**(往里 = 转走 + 浮现; 往回 = 下沉 + 转回来)。以前只问
    /// "终点是不是根", 于是网格里按 LB 往回翻一类也被判成 +1 —— 明明在往回走,
    /// 却演"转出去", 方向感整个反了。现在多认一种: 网格里换分类, 跟着"往第几个"
    /// 走, 循环翻页(最后一类跳回第 0 类)也算得对。</para>
    /// </summary>
    public static int DirTo(ScreenFace from, ScreenFace to)
    {
        // 网格里左右换类: 看分类号往哪边挪。**跨过头 = 循环**,
        // 最后一类 -> 第 0 类算往前, 反过来算往回。
        if (from.Grid && to.Grid && from.PunctGrid == to.PunctGrid && from.PunctCat != to.PunctCat)
        {
            int d = to.PunctCat - from.PunctCat;
            if (d == 1) return 1;
            if (d == -1) return -1;
            return d > 1 ? -1 : 1;
        }
        return IsRoot(to) ? -1 : 1;
    }

    /// <summary>
    /// 这一帧和上一帧之间, 值不值得演一场。
    /// <para>唯一的例外是九键 &lt;-&gt; 全键盘: 那一场是"左右扩张"的专场
    /// (见 FlipLayout), 再来一层旋转 / 浮起就是两套动画摞着演。</para>
    /// </summary>
    public static bool WorthFx(ScreenFace from, ScreenFace to)
    {
        if (from == to) return false;
        // (只问"是不是全键盘"两边一不一样 —— CN9 <-> NUM9 是换了一副面孔, 照演。)
        if ((from.Layout == Layout.EN26) != (to.Layout == Layout.EN26)) return false;
        return true;
    }

    // ---------- 下面两个给日志和探针看 ----------

    /// <summary>一句话说清这一屏。排障时看日志比看八个字段好认。</summary>
    public static string Name(ScreenFace f)
    {
        if (f.Grid)
            return f.PunctGrid ? "符号网格·" + f.PunctCat : "拼音网格";
        // 【F 先看】F 开着的时候 Root 里那九颗键整层收着(见 Render), 屏幕上摆的
        // 就是这一摞, 说别的都没意义。
        if (f.Quick) return "窗口F";
        string s = f.Layout switch
        {
            Layout.EN26 => "全键盘",
            Layout.NUM9 => "九键·数字",
            _ => "九键",
        };
        if (f.Layout != Layout.EN26)
        {
            if (f.Punct9) return "九键·标点";
            if (f.Slide) return "九键·滑行";
            if (f.Swap15) s += "·换15";
        }
        return s;
    }

    /// <summary>两个字段字段名对 printers 看的形状: "Layout CN9->EN26"。</summary>
    public static string Diff(ScreenFace from, ScreenFace to)
    {
        var sb = new System.Text.StringBuilder();
        if (from.Layout != to.Layout) sb.Append("Layout ").Append(from.Layout).Append("->").Append(to.Layout).Append(' ');
        if (from.Punct9 != to.Punct9) sb.Append("Punct9 ").Append(from.Punct9).Append("->").Append(to.Punct9).Append(' ');
        if (from.Slide != to.Slide) sb.Append("Slide ").Append(from.Slide).Append("->").Append(to.Slide).Append(' ');
        if (from.Swap15 != to.Swap15) sb.Append("Swap15 ").Append(from.Swap15).Append("->").Append(to.Swap15).Append(' ');
        if (from.Grid != to.Grid) sb.Append("Grid ").Append(from.Grid).Append("->").Append(to.Grid).Append(' ');
        if (from.PunctGrid != to.PunctGrid) sb.Append("PunctGrid ").Append(from.PunctGrid).Append("->").Append(to.PunctGrid).Append(' ');
        if (from.PunctCat != to.PunctCat) sb.Append("PunctCat ").Append(from.PunctCat).Append("->").Append(to.PunctCat).Append(' ');
        if (from.GridCols != to.GridCols) sb.Append("GridCols ").Append(from.GridCols).Append("->").Append(to.GridCols).Append(' ');
        if (from.Quick != to.Quick) sb.Append("Quick ").Append(from.Quick).Append("->").Append(to.Quick).Append(' ');
        return sb.ToString().Trim();
    }
}
