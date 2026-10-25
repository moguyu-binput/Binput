using System;
using System.Windows;
using System.Windows.Media.Animation;


namespace Binput;

public partial class OverlayWindow
{
    /// <summary>
    /// **动效配方表** —— 所有"怎么动"的数字都写在这一处，别处只来查。
    ///
    /// 为什么要这一张表：以前这些数撒在七个地方各写各的，谁也不知道邻居写了什么。
    /// 结果是同一个"弹一下"在九键上是 1.14、在设置格里是 SET_POP(也是 1.14)、
    /// 在 ◀LB / RB▶ 小牌上是 1.20 —— 三个数各自演化，谁想改都得先问一遍"还有谁在用"。
    /// 现在**调手感的活儿只需要动这一个文件**，写完重编就行。
    ///
    /// 【立表时一个数都没改】全部照抄搬迁前的值(见下面每一组的标注)，
    /// 所以画面跟立表前一模一样 —— 这次纯粹是"把数字从各处搬到一块"。
    /// 只有 <see cref="WobBack1"/> / <see cref="WobBack2"/> 那两笔例外，下面有说明。
    ///
    /// 【单位是毫秒 / 倍数 / 度 / 像素，看名字后缀】
    ///   Ms… = 毫秒; Scale… = 缩放到几倍; Dy… = 纵向挪多少像素;
    ///   …Deg = 转多少度; At… = 关键帧落在第几毫秒。
    /// </summary>
    private static class FxKit
    {
        // ---------- 0. 三件小工具 ----------
        //
        // 全项目 99% 的缓动都是 CubicEase·EaseOut(起步快、停得稳)，只有翻面收拢那半圈
        // 用 EaseIn。以前每个动画方法开头都要 new 一遍并写明 EasingMode ——
        // 一律改成查这两个工厂，将来要换曲线(比如换成更"弹"的 BackEase)改这一处。

        /// <summary>收尾稳的那条曲线 —— 绝大多数场合用这条。</summary>
        public static CubicEase EaseOut() => new CubicEase { EasingMode = EasingMode.EaseOut };

        /// <summary>起步慢、越走越快的那条 —— 只有"要缩进去"的时候才用它。</summary>
        public static CubicEase EaseIn() => new CubicEase { EasingMode = EasingMode.EaseIn };

        /// <summary>
        /// 【只有平移那一套用这条】慢起 → 加速 → 减速 → 停, 两头都稳。
        /// <para>为什么不用 EaseOut: EaseOut 是**起步那一瞬最快**、越走越慢, 用在
        /// "浮起来/落下去"这种短距离上是对的(东西是弹出来的), 可平移要走整整一页宽
        /// —— 起步就窜出去, 看着是"唰地弹一下", 不是"推着走"。手划屏幕是**先推得动、
        /// 再滑一段、最后收住**, 这条曲线才是那个形状。</para>
        /// </summary>
        public static CubicEase EaseSlide() => new CubicEase { EasingMode = EasingMode.EaseInOut };

        /// <summary>毫秒 -> 时长。省掉每个方法里那句 FromMilliseconds。</summary>
        public static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

        /// <summary>毫秒 -> 关键帧时刻。</summary>
        public static KeyTime At(double ms) => KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms));

        // ---------- 1. 按一颗键：下去、回来、弹起、那一晃 ----------
        //
        // 九键 / 26 键 / 符号格 / 设置里的格子，全套都走这一段。
        // 幅度分四档(一颗键多大、它在哪种面板里): 见 ScaleTile / ScalePop / ScaleKey26 /
        // ScaleSlide —— 面板越小、元件越密，浮起来那点余量越少，所以 26 键得 1.20、
        // 九键只要 1.12; 滑行笔尖要压住周围八颗，给到 1.22。

        /// <summary>按下去: 要跟手，所以短。</summary>
        public const double MsDown = 80;

        /// <summary>松手回来: 比按下去慢一倍多 —— 那才像"弹回原位"，不像"闪回去"。</summary>
        public const double MsUp = 190;

        /// <summary>光标走开了，那颗键缩回原样。</summary>
        public const double MsRest = 150;

        /// <summary>边角齿轮这类"通用缩放"(<c>ZoomTo</c>)。</summary>
        public const double MsZoom = 110;

        /// <summary>按下去缩到多大。<b>比 1 小</b> —— 键是被"摁进去"的，不是浮起来的。</summary>
        public const double ScalePress = 0.88;

        /// <summary>动态效果关着时用"压暗"代替缩放，压到几成亮度。</summary>
        public const double DimPress = 0.5;

        /// <summary>基准那一档：候选格、设置里的格子、以及没特别指定的一切都用这个。</summary>
        public const double ScalePop = 1.14;

        /// <summary>九键上一颗键、符号格里一格、数字层的 0 —— 这些彼此贴得很近，浮太多会撞。</summary>
        public const double ScaleTile = 1.12;

        /// <summary>26 键一颗。键小排密，得浮得多一点才看得出来。</summary>
        public const double ScaleKey26 = 1.20;

        /// <summary>滑行模式笔尖那颗。周围八颗都压暗了，它得再突出一号。</summary>
        public const double ScaleSlide = 1.22;

        // 弹起来那三帧：先冲过头，再回落一点，最后停在目标尺寸。
        // 冲过头是"弹"的来源 —— 直接滑到目标值看着像被拽过去的。
        /// <summary>第一帧冲过头到目标值的几倍。</summary>
        public const double PopOver = 1.07;

        /// <summary>第二帧回落越过目标一点点。</summary>
        public const double PopBack = 0.98;

        /// <summary>这三帧分别落在第几毫秒。</summary>
        public const double PopAt0 = 85, PopAt1 = 155, PopAt2 = 215;

        // 那一晃：左右摆两下再停。角度基准见 WobDeg，格子越大摆得越少(见 WobOf)，
        // 因为同样的度数在大格子上四角甩出去更远，看着像整块在转。
        /// <summary>摆多少度 —— 按"一颗九键的键"这个尺寸定的基准值。</summary>
        public const double WobDeg = 4.5;

        /// <summary>第二下摆回多少(相对第一下的比例)。</summary>
        public const double WobBack1 = 0.667;

        /// <summary>第三下再摆回多少。</summary>
        public const double WobBack2 = 0.311;

        /// <summary>那一晃四帧的时刻。</summary>
        public const double WobAt0 = 80, WobAt1 = 150, WobAt2 = 215, WobAt3 = 290;

        // ---------- 2. ◀LB / RB▶ 那对小牌 ----------
        //
        // 和上面"那一晃"是同一条曲线的另一个抄本 —— 以前两处各写各的数字，
        // 结果弹的那三帧差了 5 毫秒(这里 80/150/215，格子那边 85/155/215)。
        // 差得极小，先照抄保留(值不变)，等真调手感时再决定要不要合成一组。

        /// <summary>小牌弹三帧的时刻。</summary>
        public const double MsSidePop0 = 80, MsSidePop1 = 150, MsSidePop2 = 215;

        /// <summary>小牌甩出去那一下的时刻 —— 比 <see cref="WobAt0"/> 早 5 毫秒。</summary>
        public const double MsSideWob0 = 75;

        /// <summary>小牌弹到多大。比九键那颗(1.14)大一号 —— 牌本来就小。</summary>
        public const double ScaleSidePop = 1.20;

        /// <summary>冲过头之后回落过界到多少。</summary>
        public const double ScaleSideBack = 0.94;

        /// <summary>往翻的那一侧甩多少度(乘 dir 决定左右)。</summary>
        public const double SideDeg = 14;

        /// <summary>回摆两下的比例。</summary>
        public const double SideWobBack1 = -0.60;

        /// <summary>第三下。</summary>
        public const double SideWobBack2 = 0.28;

        /// <summary>往翻的那一侧顶出去几个像素 —— 光放大看着像原地胖了一下。</summary>
        public const double SidePushPx = 7;

        // ---------- 3. 面板进出场 ----------
        //
        // 三套：(a) 设置里往里钻一层 RiseIn / 快照出门 SinkLeave；
        //       (b) 进设置、回九键这两个"换面" PopSetPanel / PopKeyPanel。
        // (b) 的幅度砍半 —— 换面是"九颗键变成九个格子"，该比"压进来一层"轻。

        /// <summary>往里钻一层：从下面浮上来。</summary>
        public const double MsPageIn = 180;

        /// <summary>旧页那张快照消散。</summary>
        public const double MsPageOut = 180;

        /// <summary>换面(进设置 / 回九键)：比<｜hy_place▁holder▁no▁813｜>压进来一层短 10 毫秒，收得干脆些。</summary>
        public const double MsFace = 170;

        /// <summary>新页从多小浮到原大。</summary>
        public const double ScalePageIn = 0.92;

        /// <summary>新页从下面多远处浮上来。</summary>
        public const double DyPageIn = 26;

        /// <summary>换面时缩小多少 —— <see cref="ScalePageIn"/> 的一半，是"轻"的那档。</summary>
        public const double ScaleFaceIn = 0.965;

        /// <summary>换面时下沉多少。</summary>
        public const double DyFaceIn = 7;

        /// <summary>旧页快照缩到多小。</summary>
        public const double ScalePageOut = 0.94;

        /// <summary>旧页快照下沉多少。</summary>
        public const double DyPageOut = 10;

        // ---------- 3b. 扑克牌旋转(换页动效 = 旋转时用的那一组) ----------
        //
        // 绕**整块的左下角**转: 旧页朝要去的方向转出去、一边转一边淡掉,
        // 新页从反方向的同样角度转回来、由透明转实。两页之间始终差着 SpinDeg,
        // 像手指捻开一把扑克牌 —— 所以这个角**不能大**, 大了就成了"翻牌"。
        //
        // 想换手感改这里四个数就够, 全界面一起变。

        /// <summary>转一整场多久。比浮起那一套(180)长 —— 旋转扫过的行程长得多,
        /// 同样 180 会显得赶。</summary>
        public const double MsSpin = 260;

        /// <summary>两页之间差几度。**18 是照监听器算出来的上限**: 绕左下角顺时针
        /// 18° 时, 左上角向右甩出 442·sin18 ≈ 137px, 而卡片两侧各只留了 156;
        /// 再往上顶到 20° 就要出界被切了。右下角往下掉的那一截靠 SpinShrink 收。</summary>
        public const double SpinDeg = 18;

        /// <summary>转的同时按这个比例往左下角收一点。纯旋转会把右下角往下甩出
        /// 128px(414·sin18), 舞台底下留不了那么多 —— 收一下正好塞回去,
        /// 也顺了"牌离手越远看着越小"这本来的样子。</summary>
        public const double SpinShrink = 0.94;

        // ---------- 3c. 旋转那一套的「先后」 ----------
        //
        // 2.1.16 起旋转不再是"两张牌一起扫", 而是**先掀开上层、下层再浮出来**:
        //   往里走(= 逆时针转走)  ->  新的一页从里往外浮现
        //   退回来(= 当前页下沉)  ->  上一层绕左下角顺时针转回来
        // 既然有先后, 就得说清"新的一页晚多久起步"和"每一场自己演多久"。
        // 两个数都是**占整场时长的比例**: 总时长 = (FxLag + FxCut) × MsSpin,
        // 取 0.35 + 0.8 = 1.15 倍 —— 约 300 毫秒, 和改动前那一场(260)几乎一样长,
        // 只是中间多出"掀开了才看见下面"的那一拍。

        /// <summary>新的一页晚几成起步(旧页先走那一段)。</summary>
        public const double FxLag = 0.35;

        /// <summary>有先后之后, 每一场自己占整场时长的几成。</summary>
        public const double FxCut = 0.8;

        // ---------- 3d. 平移(换页动效 = 平移时用的那一组) ----------
        //
        // 手机滑页那样: 旧的一页横着滑走、新的一页紧贴着它从另一边滑进来,
        // 两页**并排一起走**(同一时刻、同一速度), 所以看着是一整条内容在移动。
        // 往里走: 旧页往左走、新页从右边来; 退回上一层: 整个反过来。
        //
        // 【滑多远 = 一页的宽度, 不是"滑出窗口"】这一点是本套唯一要紧的设计:
        // 两页是并排的, 旧页右边缘和新页左边缘始终贴在一起 —— 贴着走的行程
        // 正好是**一页自己的宽度**, 于是全程没有缝。要是照"滑出屏幕"去算
        // (窗口比页面宽出那一大圈留白也得滑完), 两页中间会豁开一道空白,
        // 看着就成了"旧页走了、隔一会儿新页才来"。
        //
        // 想换手感改这两个数就够, 全界面一起变。

        /// <summary>滑一整场多久。和旋转那一套(260)同量级 —— 滑过的行程比
        /// 旋转长, 短了会显得"唰"一下就过去了。</summary>
        public const double MsSlide = 280;

        /// <summary>滑多远, 按**一页自己的宽度**的几成算。1.0 = 两页紧贴(无缝,
        /// 就是手机上那个样子); 大于 1 会在两页之间豁开一道缝。</summary>
        public const double SlideRatio = 1.0;

        /// <summary>连着翻页时"把剩下的路滑完"最少给多少毫秒。
        /// <para>连按不重演(见 ComeIn 里 slide 那一支): 新的一页从**现在站的地方**
        /// 接着滑到位, 时长按剩余距离等比缩短。可剩得太少的时候等比一算就剩二三十
        /// 毫秒 —— 那不是"快", 是一闪, 眼睛根本跟不上。给个下限, 最短也走完这么久。</para>
        /// </summary>
        public const double MsSlideRest = 110;

        // ---------- 3e. 浮力(换页动效 = 浮力时, 九键那一页常驻的那层水波) ----------
        //
        // 【这一组不管换页】换页那一整套**照搬「默认」**(见 FxStyle.Buoy): 进设置、
        // 换面、LB / RB 翻页还是旧的缩小下沉 / 浮起淡入, 一个数都不改。这一组只管
        // "这一屏的键浮在水面上"那层常驻漂动。
        //
        // 【每一屏都漂, 全键盘那一屏除外】九键那九颗、标点 5×5 那 25 格、设置与窗口F
        // 里的每一格(含 789 合起来那条长条、"关于"并成的一大块)全都漂 —— 谁露在
        // 屏幕上谁漂, 换屏自动换一批(见 Fx.cs 的 BuoyTargets)。全键盘那七排键太多
        // 太密, 不做。
        //
        // 【漂动围着自己的原位, 不许带直流偏移】波形是**纯正弦**(见 Fx.cs 里
        // BuoyWave), 一个周期下来均值正好是 0 —— 于是每颗键围着自己该在的地方漂,
        // 九宫格不会整体歪掉。
        // 【开浮力时不直接接循环】纯正弦的第 0 帧已经偏在 A·sin(2πφ) 上了, 直接接
        // 就是"啪"地偏出去半个振幅。所以先跑 260 毫秒"从正中送到波形起点"那一小段,
        // 到了才接上循环(见 BuoySet) —— 这一小段同时也把"浮起来"演完了。
        // (试过另一种: 波形减掉起点, 首尾都落在 0。那样开 / 关确实一跳都没有,
        //  可减掉的那个数是个**直流偏移** —— 每颗键从此永久偏向一边最多 4 像素,
        //  九颗偏的方向还各不一样, 整个九宫格看着是散的。稳态的歪比开场的跳要命得多。)
        // 【九颗键各有自己的周期和相位】周期一样就成了一块板在平移, 那是"晃界面"
        // 不是"浮水面"; 上下那一路再比左右晚四分之一轮, 合成出来是个扁椭圆。

        /// <summary>浮起来放大到几倍。比光标浮起(1.12)小一点 —— 它是**常驻**的,
        /// 九颗键天天顶着这个尺寸, 大一分就糊成一片。</summary>
        public const double BuoyScale = 1.10;

        /// <summary>左右漂多少像素。</summary>
        public const double BuoyDx = 4;

        /// <summary>上下起伏多少像素。比左右小 —— 浮在水面上是横着荡, 不是上下颠。</summary>
        public const double BuoyDy = 3;

        /// <summary>漂一轮最短多久(毫秒)。九颗键的周期落在这一区间里, 各占一格。</summary>
        public const double BuoyMsMin = 3000;

        /// <summary>漂一轮最长多久(毫秒)。</summary>
        public const double BuoyMsMax = 4000;

        /// <summary>一轮采几个关键帧。一轮三四秒, 两点之间还隔着近 300 毫秒 ——
        /// 12 个足够平滑, 再多只是白添关键帧。</summary>
        public const int BuoyKeys = 12;

        /// <summary>上下那一路比左右晚几分之一轮。差开这四分之一, 两条直线合成出
        /// 一个扁椭圆, 才像浮在水面上(同相的话是一条斜线, 看着是"被推着走")。</summary>
        public const double BuoyLagY = 0.25;

        /// <summary>开 / 关浮力时"起浮 / 归位"用多久。</summary>
        public const double MsBuoy = 260;

        /// <summary>
        /// 算浮力的**参考边长**: 一颗九键的键、设置里普通一格, 都是 112。
        /// <para>上面那三个数(放大多少、左右漂多远、上下起伏多少)都是照"112 见方"
        /// 这一档写的; 别的尺寸由下面两个函数折算, 于是九键的键、设置的一格、
        /// 标点网格里的小格, 看着是同一个动作。</para>
        /// </summary>
        public const double BuoyRef = 112;

        /// <summary>
        /// px 见方的格子, 浮起来放大到几倍。
        /// <para>【比参考尺寸大的格子按边长反比收】352 的一大格("关于"那一页)要是
        /// 也放大 1.10, 就是四边各顶出 17 像素 —— 那不是浮起来, 是把卡片撑破。
        /// 收完只剩 1.03, 四边各探 5 像素, 和 112 那颗键探出去的量一个量级。</para>
        /// <para>【比参考尺寸小的不放大】小格子本来就挤, 再按反比给它放得更大就撞上了,
        /// 一律按 1.10 算 —— 64 见方的标点格只探 3.2, 四周那 8 像素的缝够用。</para>
        /// </summary>
        public static double BuoyScaleAt(double px) =>
            1 + (BuoyScale - 1) * Math.Min(1, BuoyRef / Math.Max(px, 16));

        /// <summary>
        /// px 见方的格子漂多远(px = 这一格**外层的边长**, 比看得见的那颗键大一圈)。
        /// <para>比参考尺寸小的格子按边长收: 标点网格一格只有 64 见方、四周 8 像素
        /// 的缝, 照 112 那颗键漂 4 像素, 两个邻居迎面一撞就贴上了。收到 2.3 就一直是
        /// "隔着一条缝在荡"。</para>
        /// <para>大的那头不放大(乘数封顶 1) —— 一大格本来就宽, 4 像素在它身上只是
        /// 极慢的一点偏, 正好。</para>
        /// </summary>
        public static double BuoyAmpAt(double px, double amp) =>
            amp * Math.Min(1, Math.Max(px, 16) / BuoyRef);

        // ---------- 4. 翻面(九键 ↔ 数字层 ↔ 符号) ----------

        /// <summary>单颗键翻半张脸的时长(缩到 0 / 从 0 展开，各占一次)。</summary>
        public const double MsFlipHalf = 65;

        /// <summary>每一波比上一波晚多久起步 —— 九颗键按对角线分三波，看着像掀过去。</summary>
        public const double MsFlipStep = 32;

        // ---------- 5. 26 键展开 / 收窄 ----------

        /// <summary>整块内容区从九键宽撑到全键盘宽。</summary>
        public const double MsExpand = 230;

        /// <summary>那套键自己的淡入淡出占上面那个时长的几成 —— 比框稍早收，别拖尾。</summary>
        public const double KeyFadePart = 0.8;

        // ---------- 6. 预览框(XML 出入场) ----------
        //
        // 这一处是自己逐帧插值的(GridLength 没法用 DoubleAnimation)，所以曲线也是手写的:
        // SmoothStep(两头慢中间快)掺 CubicEase·EaseOut(停得稳)，各占一半。

        /// <summary>从九键方框里升出来。</summary>
        public const double MsNoteIn = 340;

        /// <summary>沉回方框里。</summary>
        public const double MsNoteOut = 260;

        /// <summary>曲线里 SmoothStep 占几成。</summary>
        public const double NoteMixStep = 0.45;

        /// <summary>曲线里 EaseOutCubic 占几成。</summary>
        public const double NoteMixOut = 0.55;

        /// <summary>淡入从第几成进度才开始(那一段它正压在拼音行上，得是全透明的)。</summary>
        public const double NoteInFrom = 0.28;

        /// <summary>淡入在多长的进度里从不透明 0 爬到 1。</summary>
        public const double NoteInSpan = 0.42;

        /// <summary>淡出在前几成进度里走完。</summary>
        public const double NoteOutSpan = 0.72;
    }

    /// <summary>
    /// 预览框出入场那条**掺出来**的缓动曲线: SmoothStep 和 CubicEase·EaseOut 各掺一半
    /// (比例见 FxKit.NoteMixStep / NoteMixOut)。
    /// 为什么不是现成的某一条: EaseOutCubic 起步太猛(第一帧就吃掉一半行程), 看着像"弹"
    /// 而不是"浮"; 纯 SmoothStep 收尾又飘。掺起来才是"起步不猛、收尾不飘"。
    /// 这一条以前写在 NoteTick 里按帧算, 现在做成 EasingFunction —— 曲线**一模一样**,
    /// 但从此可以被打断、可以被时钟准时收尾, 不用再靠"1 秒保险"兜底。
    /// </summary>
    sealed class NoteEase : EasingFunctionBase
    {
        protected override double EaseInCore(double p)
        {
            double k = p * p * (3 - 2 * p);             // SmoothStep: 两头慢、中间快
            double s = 1 - Math.Pow(1 - p, 3);          // CubicEase · EaseOut: 停得稳
            return k * FxKit.NoteMixStep + s * FxKit.NoteMixOut;
        }

        protected override Freezable CreateInstanceCore() => new NoteEase();
    }

}
