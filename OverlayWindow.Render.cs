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
// ---- 每一帧怎么画 —— Render / Paint / 摇杆 / 0 号键 ----
//
// 2.1.11 之前这些代码全在 OverlayWindow.xaml.cs 一个文件里(5117 行)。
// 这次只是按职责把它拆成几个 partial class 文件 —— 一行都没改,
// 编译出来的行为跟拆之前完全一样(改名/搬位置不影响任何逻辑)。

    bool PageTurnable(InputCore c, bool punctGrid)
    {
        // 判据一律是"这一页有几个页 > 1"。PagesOf 要读当前页的格子表,
        // 那一摞还没搭起来(Top 是 null)的时候直接当"不能翻"。
        if (punctGrid) return c.PunctCatCount > 1;
        if (_qkOn) { var p = _qk.Top; return p != null && PagesOf(p) > 1; }
        if (Flipped) { var p = _set.Top; return p != null && PagesOf(p) > 1; }
        return false;
    }

    public void Render(InputCore c)
    {
        // **【换屏管线第 ① 步: 认人 + 拍照】这一句必须是本方法里第一个动作** ——
        // 早于下面任何一次界面写入(Visibility / Text / 行数都算)。
        // 为什么这么要紧: RenderTargetBitmap.Render() 会**强制立刻渲染**当前这棵树,
        // 拍到的是"拍照这一刻树上长什么样", 而不是"上一帧的样子"。而
        // "9 宫格 -> 5x5 网格"这一跳换的恰恰是 Visibility(下面第 148 行那两行) ——
        // 拍照要是排在它后面, 面板早就长出来了, 只能拍到新面孔。
        // 见 OverlayWindow.Fx.cs 里 TICK 那一段, 那张图讲的就是这条次序。
        TickScreen(c);

        // 【2.1.10: 右下角齿轮只在"能进设置"的那两页露脸】
        // 以前每个界面都有它 —— 哪儿都能翻到设置, 于是得回答"退出来退到哪儿",
        // 2.1.9 那一整套让位逻辑就是这么绕出来的。现在只有九键首页和全键盘
        // 进得去, 别的界面索性不画这个角: 看不见也就不会去按, 更不会
        // "按了没反应"。判据见 InputCore.SettingsAllowed。
        // 翻到背面那一瞬间 Settings 已经是 true 了, 所以这里把"正在设置里"
        // 也算作留着 —— 不然齿轮会在翻面动画开始前先"啪"地消失一下。
        GearBtn.Visibility = (c.Settings || c.SettingsAllowed)
                             ? Visibility.Visible : Visibility.Collapsed;

        // 设置页的"悬停说明": 光标(或鼠标)在一行上停够了就浮出来。
        // 放在最前面每帧问一次 —— 背面翻走时它会自己收掉。
        TickSetTip();

        // 设置页里按 A 的那一下: 光标那一格跟着缩下去, 松手弹回。
        // 和键盘上"按 A 那颗键缩一下"是同一个 FxKit.ScalePress, 不然按设置和按键盘
        // 是两种手感。注意**只看 A**: 挪光标的十字键 / 摇杆不算, 退格的 X 也不算。
        bool setDown = c.Settings && !_setAnim && _setStack.Count > 0
                       && (c.HeldMask & GamepadInput.A) != 0;
        if (setDown != _setDown) { _setDown = setDown; SetCellDown(_set, setDown); }

        // 窗口F: 那一摞层(见上面"窗口F: 那一摞层"那段)。
        // **开的时候现搭、关的时候整摞撤掉** —— 数据(哪几格写了什么)是随时会变的,
        // 搭一次用一辈子就会看见旧内容。录快捷键那一页**不归这一摞**: 那时屏幕上
        // 摆的是全键盘(左右扩张出来), 这一摞得整个让开。
        _qkFocus = c.QuickOpen;
        // 【扩张动画演的过程中不许改 _qkOn】那一页进 / 出都是"左右扩张"演出来的
        // (见 FlipLayout), 演的中间若把这一摞放回来, 就会和正在收回的全键盘叠在一起。
        // 进出时由 IQuickUI.StageOff() 先把它收掉, 演完这一行再按真实状态接回去。
        if (!_expanding) _qkOn = c.QuickOpen && c.QEdit < 0;
        // 别把这个读漏了: FlipLayout 演完 mid() 要按它定目标宽度
        _layoutNow = c.Layout;
        // 把**这一帧摇杆推到哪儿**传给 BuildQuickSide: 那一摞刚 Reset 过,
        // 不灌回去的话进 F 会先落正中再跳到手底下(见那里的注释)。
        if (c.QuickOpen && _qk.Count == 0) BuildQuickSide(c.StickX, c.StickY);
        else if (!c.QuickOpen && _qk.Count > 0) _qk.Reset();
        QuickStage.Visibility = _qkOn ? Visibility.Visible : Visibility.Collapsed;
        QuickTitle.Visibility = _qkOn ? Visibility.Visible : Visibility.Collapsed;
        QuickDots.Visibility = _qkOn ? Visibility.Visible : Visibility.Collapsed;
        // F 里按 A 那一下: 光标那一格跟着缩下去, 和设置页 / 九键同一个手感
        bool qDown = _qkOn && !_qk.Anim && _qk.Count > 0
                     && (c.HeldMask & GamepadInput.A) != 0;
        if (qDown != _qk.Down) { _qk.Down = qDown; SetCellDown(_qk, qDown); }

        bool gridMode = c.Focus == FocusZone.CandGrid;
        bool en = c.Layout == Layout.EN26;
        // 符号面板(右摇轮盘"更多"唤出来的那一整页)开着吗。
        // 提到这儿是因为下面"九键 / 全键盘谁露脸"要用它: 那一页铺满整个键盘区,
        // 底下那套九键得**整层收掉** —— 符号面板 2.1.1 起不再垫底板(见 XAML 里
        // CandPanel 的注释), 九键还画着的话就是两层字叠在一起。
        bool punctGrid = gridMode && c.Punct;

        // 键盘区(内容区)尺寸: 九键 360×360 的正方形, 全键盘 640×360 ——
        // **高度两边一样**(2.1.0), 换布局时只有左右两条边往外推, 上下钉着不动。
        // 【设置面一律按九键宽算】2.1.25: 进设置**不换布局** —— 从全键盘进来时
        // c.Layout 还是 EN26, 认它就会把宽度一路拽回 640。可背面那张设置卡是
        // 写死的 414(见 SizeBackCard), 于是"卡片 414、卡片外面那四行 640",
        // 挂在四行左右两端的 LB / RB 小牌就跑到离卡片老远的地方去了 ——
        // 只有从全键盘进设置才看得见(从九键进两边都是 360)。
        // 翻面那一场收窄动画(Flip -> TweenWidth 640->360)自己会把宽度顺过去,
        // 这里只要别再拽回去就行; 从设置回全键盘时 Flipped 已经是 false, 照旧撑开。
        double kw = (en && !Flipped) ? KEY26_BOX_W : KEY9_BOX, kh = KEY9_BOX;
        // **"左右扩张"动画演的过程中, 这一整组宽度归动画管** —— mid() 一换布局,
        // Render 这一帧就会把宽度直接拽到目标值, 动画等于白演。
        if (!_expanding)
        {
            // 【宽度现在只写 ContentW 一个数】框和卡片外面那几行由 XAML 的绑定跟着,
            // 26 键的横向缩放由 OnContentW 同一帧跟着 —— 于是以前那三句判据里
            // "框的宽是不是也跟上了""是不是 NaN"这一类**补漏式检查**全都不用写了:
            // 绑定保证了它们永远一致, 不存在"漏掉一个"的可能。
            // (注释留个痕: 以前这里判的是 KeyArea.Width + Frame.Width, 而 KeyArea
            //  在 XAML 里写死 360, 九键模式下条件永远不成立, 于是框的宽一直是 NaN、
            //  卡片只有 390 而不是 414 —— 那次是"漏掉一个"的活标本。)
            if (Math.Abs(ContentW - kw) > 0.5) SetWidths(kw);
            if (Math.Abs(KeyArea.Height - kh) > 0.5) KeyArea.Height = kh;
            // 26 键那一层的横向缩放**每帧钉一次**:
            // 这一仗打在"窗口F 录快捷键"上 —— 那一页不是按 L3 切过去的, 而是
            // InputCore 直接把 Layout 改成了 EN26(QuickStartHotkey), 这条路不走
            // FlipLayout, 于是没人给 Grid26 那个 ScaleTransform 复位, 它还停在上一回
            // "全键盘缩回九键"的 360/640 = 0.5625 上: 七排键被挤成九键那么窄
            // (功能是好的, 命中判定按 layout 排的那套算)。与其在每个入口记得复位,
            // 不如让 Render 每帧把"该多少"钉在这儿。值没变时它自己会跳过,
            // 不会每帧都去动一次变换。
            SyncGrid26Scale();
        }

        // 预览框底下这一道空档**现在恒为 0**。
        //
        // 它本来是给系统输入法的候选窗留的: 九键拼拼音时要留"两行字"那么高
        // (48px), 不然候选窗(它跟着光标, 浮在预览框正下方)正好压在拼音那一行上。
        // 留是留对了, 可代价是预览框被顶得离拼音候选老远 —— 用户原话
        // "距离拼音候选又高了一行, 太远了就有点割裂"。
        //
        // 现在让**候选行自己那一格**来顶这份差事: 拼拼音的时候候选行本来就是空的
        // (CandRowItems 只在标点模式给内容, 其余时候五个格子全 Hidden, 只是行还
        // 占着那 46 —— 见下面"行高钉死"那条), 候选窗压在它上面谁也没挡着,
        // 而拼音行还在更下面, 照旧看得见。
        // 于是预览框整个落下来一行, 直接贴在候选行的上沿。
        {
            double padH = 0;
            if (Math.Abs(RowPad.Height.Value - padH) > 0.5)
                RowPad.Height = new GridLength(padH);
        }

        // 九键的摇杆线整层收掉: 焦点在哪纯看哪颗键凸出来(摇杆回中就是中间的 5
        // 凸出来), 那根线再画就成了多余的干扰。
        StickLayer.Visibility = Visibility.Collapsed;
        // F 开着的时候把键盘区整个让给它(九键 / 候选网格都不画了, 免得两层叠着)
        CandPanel.Visibility = gridMode && !c.QuickMode ? Visibility.Visible : Visibility.Collapsed;
        // 九键 / 全键盘谁露脸: 录快捷键时是全键盘(要看得见键才按得下去)。
        // F 那一摞开着的时候**两个都收掉** —— 那一摞就叠在键盘区上(QuickStage),
        // 底下这套九键再画着就是两层叠在一起, 而且它还显示的是字母不是快捷语,
        // 1.2.1 里那个"数字 ui 跑到背面去了、还是普通九键"就是这么来的。
        // (九键的摇杆线只在这底下、F 没开、也没滑的时候才画 —— 见上面 StickLayer。)
        // 换布局"左右扩张"演的时候, 这两行的可见性由 FlipLayout 按**目标**布局定死,
        // Render 不许跟着 mid() 改过来的 core 状态改 —— 改了就成了"先换脸再演",
        // 扩张的那一下看见的就不是即将撑开的那一套键。
        if (!_expanding && !_keyKeep)
        {
            Grid9.Visibility = (en || _qkOn || punctGrid) ? Visibility.Collapsed : Visibility.Visible;
            Grid26.Visibility = en ? Visibility.Visible : Visibility.Collapsed;
        }

        // 设置那一面现在是一层层九宫格(见下面"设置页"那一段), 不在这条渲染线上:
        // 它由 PushPage / PopPage 自己搭, 只在进出层和翻页时重画, 不用每帧刷。

        // 展开的符号面板(长按/停在轮盘"更多"上唤出来的那一整页)。
        // 这时候上面那 5 格标点**已经没人用了** —— 选符号全靠下面这一整页网格,
        // 那 5 个还是"单行候选"那会儿留下的, 留着只会让人以为上面那一排也能选。
        //
        // 这一行(RowHead)上现在摆三样东西, 一次只会用到一样:
        //   符号面板开着  -> 分类名("常用 / 中文 / 英文 / 数学")
        //   按住 LB 滑行  -> "滑行" 二字
        //   其余时候      -> 拼音候选那 5 格(没字的时候连格子一起藏掉)
        // 它们互斥: 符号面板里滑不起来(TickSlide 的 can 里有 !Punct), 滑行时九键
        // 没在打标点、候选格本来就是空的。所以共用一行谁也压不着谁, 也不必为了
        // 让位把谁收起来 —— 更不用像以前那样给状态字单独开一行(那一行空着的时候
        // 就是一道缝, 把"滑行"那俩字推得离拼音老远)。
        bool sliding = c.Slide.Active;
        CandRow.Visibility = punctGrid ? Visibility.Collapsed : Visibility.Visible;
        // 显隐挂在**外面那块板上**(SlideHintChip) —— 板要跟字一起出一起收, 只把里面的字藏了的话就剩一块空板在那儿。
        // 【2.1.8: 分类名这块牌子整个撤了】"现在在第几类"改由**符号网格正中间
        // 那一格**说(见 InputCore.GridItems)—— 眼睛本来就在网格上, 不用再往下
        // 瞟一行。牌子本身在 XAML 里也一并删了。
        // (2.1.1: 分类名右边那个"1/8"的数字页号撤了 —— 第几页由底带上那排点说。)
        // 底带那排点: 几个分类几个点, 第几个亮。和设置页 / 窗口F 同一个规矩。
        PunctDots.Visibility = punctGrid ? Visibility.Visible : Visibility.Collapsed;
        if (punctGrid) PaintDots(PunctDots, c.PunctCatCount, c.PunctCat);
        // **判据里不带 punctGrid 之外的条件**: 以前这儿还带着"有没有候选",
        // 于是刚按下 LB 那一刻(拼音还空着)"按下 LB 却什么提示都没有" ——
        // 这个坑踩过两回。现在只要按住 LB 就显示。
        // 【「已复制」和「滑行」同一处】关于页按 A 复制完, 这三个字在这行亮一秒
        // (见 CopyAbout)。它俩互斥 —— 复制的时候人在设置页里, 不可能同时在滑行。
        bool copied = DateTime.Now < _copyUntil;
        bool egging = DateTime.Now < _eggUntil;
        CopyChip.Visibility = copied ? Visibility.Visible : Visibility.Collapsed;
        // 彩蛋那句和上面两块同一块板、同一处位置: 这一行一次只摆一样东西,
        // 优先级 已复制 > 彩蛋 > 滑行(复制的时候人在设置页里, 不可能同时在滑行;
        // 而彩蛋本身就发生在设置页, 所以把它排在「滑行」前面)。
        EggChip.Visibility = (!copied && egging) ? Visibility.Visible : Visibility.Collapsed;
        // 【调窗口那一档(2.3.3)】这一行的优先级最高: 进去之后屏幕上没有任何别的
        // 变化(设置页翻走了、键盘一模一样), 不在这儿写一句就成了"点了没反应"。
        // 和「滑行」共用同一块板 —— 那一档里滑不起来, 谁也压不着谁。
        if (c.Resizing)
        {
            string rs = $"调窗口 {(int)(c.Scale * 100)}% · 十字键缩放 · B 退出";
            if (SlideHintText.Text != rs) SlideHintText.Text = rs;
            SlideHintChip.Visibility = Visibility.Visible;
        }
        else
        {
            if (SlideHintText.Text != "滑行") SlideHintText.Text = "滑行";
            SlideHintChip.Visibility = (!punctGrid && sliding && !copied && !egging)
                                       ? Visibility.Visible : Visibility.Collapsed;
        }

        // 【候选行 / 拼音行的高度: 钉死 46 / 34, 不再"有内容才占"】
        // 这两行搬到卡片**外面**之后我让它们"没内容就收成 0", 那是漏算了一头:
        // 这一摞(OverPanel)是**贴底**往上摞的, 行的高度一变, 挪的是**摞的顶边** ——
        // 顶边上站着的正是预览框。于是"打上拼音 -> 候选行撑开 -> 预览框往上顶一截",
        // 拼音一删又掉回来 —— 就是用户说的"预览框被候选框顶来顶去"。
        // 钉死之后: 上面那三行总高恒定 -> 预览框的**底边**钉死, 只有它自己长高时
        // 顶边往上走("浮出来"那一下), 别的谁也动不了它。
        // 空的候选格本来就是 Hidden(不是 Collapsed), 空的拼音存根 Collapsed,
        // 所以钉死之后也不会多出五个空壳。
        // **按名字取行, 别写 RowDefinitions[1]** —— 1.2.0 里在候选行上面插了
        // "窗口F 编辑条"那一行, 下标 1 就变成编辑条了: 候选行再也没被收起来,
        // 于是预览框下面一直挂着 5 个空候选格; 编辑条反倒被压成 0 高度,
        // 保存按钮完全看不见 —— 一次下标写死, 两个地方一起坏。

        // 左右翻页那两个(LB / RB 换页): **挂在框外面**。
        // 【2.1.3: 不再只有符号面板才有】翻页这件事全界面只有一种说法 ——
        // 底下那排页码点(在第几页) + 框外左右这对 ◀LB / RB▶(可以翻)。
        // 设置页那些带翻页的深层、窗口F 的列表页以前只有点、没有这对,
        // 于是"这页还能翻"这件事得猜 —— 现在三处都挂同一对。
        //   符号面板  = 符号的几个分类
        //   窗口F     = 列表层那五页
        //   设置页    = 某一层摆不下的那些(比如"设置门槛时间"的选项们)
        // 判据见 PageTurnable: **当前这一页的页数 > 1**。够简单才不会漏一处,
        // 也才不会出现"明明只有一页、左右还挂着俩箭头, 按下去没反应"。
        bool pageSide = PageTurnable(c, punctGrid);
        PagePlateL.Visibility = pageSide ? Visibility.Visible : Visibility.Collapsed;
        PagePlateR.Visibility = pageSide ? Visibility.Visible : Visibility.Collapsed;

        // 这一帧"该浮起来的那颗键"。几段里至多有一段会选中一颗, 谁选中谁说了算。
        string fxId = "";
        Border fxB = null;
        double fxS = FxKit.ScalePop;

        // 这一帧"确认键(A)"是不是正按着。按着的时候光标那一项会缩一下(见 FxSet)。
        // 只有 A 算(见 PRESS_MASK), 退格 / 取消 / 切分不缩 —— 它们没碰光标这一项。
        // 设置面不算 —— 那一面根本没有键盘。
        bool anyDown = !c.Settings && (c.HeldMask & PRESS_MASK) != 0;
        // 键盘区才用"变暗"这套反馈: 候选区/网格区里按 A 是"上屏", 键盘那颗
        // 缩了/暗了反而像被误碰。

        if (!en && !gridMode)
        {
            bool num = c.Layout == Layout.NUM9;
            bool punct = c.Punct9;
            // **九键该露哪一面**: 标点 > 数字 > 滑行 > 字母(见 TickScreen)。
            // 【已经没有"九键该换面了"这一步了】以前这里亲手拼一个键面串去问
            // SetFace9"要不要演一场"; 现在"这一屏是什么"由 ScreenId 在**本帧
            // 第一行**就认过了, 这里只管照着 _screenNow 把字写上去。
            // 顺便把优先级摆在明面上: punct(标点) > num(数字) > sliding(滑行)。
            bool fPunct = punct, fNum = !punct && num;
            // 正在画的这一道: 笔尖此刻压在九宫格的哪一格(0 = 摇杆还在中间死区,
            // -1 = 没在滑)。滑行时九宫格的"主角"只认它, 不认 Key9 —— 原来那颗
            // 光标键要是还亮着, 两颗同时亮就分不清哪个才是笔的位置。
            int sled = sliding ? c.Slide.Sector : -1;
            // 标点层里右摇杆指在哪一格(0 = 没推 / 正中间那格是空的)
            int psel = punct && c.Punct9Sel != InputCore.Punct9Blank ? c.Punct9Sel : 0;
            for (int i = 1; i <= 9; i++)
            {
                // **换了内容是立刻生效的** —— 外面有旧一屏那张照片顶着
                //  (见 TickScreen): 底下换成什么观众这会儿瞧不见, 他看见的先是
                //  那张已经走掉的那一屏。以前为了让九颗键"一格一格换过去"得牺牲
                //  一半时间有意画错(趁压扁那一瞬偷换), 现在不用了。
                // "1/5 互换"开着时, 左上角和正中间那两颗的字母对调。
                // **一按 LB 进滑行就立刻还原** —— 滑行靠的是摇杆方位(左上恒为 JKL),
                // 那时候再跟着设置漂, 你边滑还得边想"这会儿左上角是啥", 没法用。
                bool swap = c.Swap15 && !sliding;
                string sub = Key9Sub[swap && (i == 1 || i == 5) ? 6 - i : i];
                string sym = fPunct ? InputCore.PunctSymbols[i - 1] : "";
                // 数字层 -> 只显示数字; 普通九键 -> 按开关决定"数字+字母"还是"只有字母"
                bool only = !fNum && !fPunct && !Key9Num;
                // 空着的那个 TextBlock 必须**折叠**掉, 不能只是把 Text 清空:
                // TextBlock 就算没字也照样占着一整行的行高(24px 那个约 31px),
                // 竖着排在 StackPanel 里就会把另一行顶偏 —— 1.0.3 里字母看着"靠下",
                // 根子就在这儿。折叠掉之后剩下那一行才会真正落在格子正中。
                _key9Num[i].Text = fNum ? i.ToString() : fPunct ? sym : (only ? "" : i.ToString());
                _key9Num[i].Visibility = only ? Visibility.Collapsed : Visibility.Visible;
                // 标点比数字大一号(它就一个字, 占满整颗键才看得清); "更多"是两个字, 收小
                _key9Num[i].FontSize = fPunct ? (VisualLen(sym) > 1 ? 20 : 30) : 24;
                _key9Sub[i].Text = (fNum || fPunct) ? "" : sub;
                _key9Sub[i].Visibility = (fNum || fPunct) ? Visibility.Collapsed : Visibility.Visible;
                // 只留字母时把它当主文字: 亮色 + 选中高亮(不再用偏灰的 Sub 色),
                // 字号也放大。4 个字母的键(PQRS / WXYZ)小一号防贴边。
                // "快捷"是两个汉字, 视觉宽度和四个字母一样, 所以走 VisualLen 判。
                _key9Sub[i].FontSize = only ? (VisualLen(sub) >= 4 ? KEY9_BIG4 : KEY9_BIG) : 11;
                // 只有一行的时候那 2px 的上间距就是纯粹的偏心, 去掉
                _key9Sub[i].Margin = only ? new Thickness(0) : new Thickness(0, 2, 0, 0);
                bool on = c.Focus == FocusZone.Keyboard && c.Key9 == i;
                bool here = sled == i;
                // 标点层里高亮只跟右摇杆走 —— 左摇杆那颗光标这时候让给它,
                // 两颗一起亮就分不清"我要出的是哪个符号"了。
                if (punct) on = psel == i;
                // 笔真的落在某一格上时, 高亮跟着笔走; 它还停在正中间(sled == 0)
                // 就维持原样, 免得九颗键一起失焦, 看着像是卡住了。
                if (sled > 0) on = here;
                Paint(_key9[i], on, only ? _key9Sub[i] : _key9Num[i], only ? null : _key9Sub[i]);
                // 滑行时: 除了笔尖那颗, 其余一并压暗。九颗键长得一模一样,
                // 光让一颗变大在余光里还是不够跳, 旁边沉下去它才真站出来。
                // (以前这里还兼管"动态效果关着时按下去变暗"那一手 —— 那个开关
                //  早就撤了、恒开着, 那条永远走不到的路一并删掉。)
                _key9[i].Opacity = sled > 0 && !here ? SLIDE_DIM : 1;
                if (on) { fxId = "9:" + i; fxB = _key9[i]; fxS = FxKit.ScaleTile; }
                // 滑行时浮起来的换成笔尖那颗, 并且**每确定一个键就重跳一次**:
                // id 里捎上"已经记下几个", Seq 一变长 id 就跟着变, FxSet 认出是
                // 新的就会重跑弹起动画。同一格连发(打 66 / 777)也是一下一跳,
                // 不会闷在那儿让人纳闷刚才究竟记上没有。
                if (here)
                {
                    fxId = "slide:" + i + ":" + c.Slide.Seq.Length;
                    fxB = _key9[i];
                    fxS = FxKit.ScaleSlide;
                }
            }
            // 数字层的 0 也是一颗键, 选中它时浮起来的就是它
            if (c.Focus == FocusZone.Keyboard && c.Key9 == 0 && num)
            { fxId = "0"; fxB = _key0; fxS = FxKit.ScaleTile; }
        }
        // 【0 号键的显隐必须**每帧都问**, 不能挂在上头那个 !en && !gridMode 里】
        // 它只在数字层(NUM9)露脸; 而"离开数字层"最要紧的那一条路 —— 松开 LT
        // 还原成**全键盘** —— 走的恰恰是 en == true 那条分支, 上头整个块都被跳过。
        // 于是还回全键盘那一帧没人再管它: 它维持着上一帧的 Visible, 连坐标都是
        // 按九键 360 宽算出来的那个 142(360/2 - 38); 而键盘区这会儿已经撑到 640,
        // 142 在 640 里就是偏左那一块 —— 用户原话"数字模式 0 这个数字格, ui 会出现
        // 在全键盘的左下角"。
        // 摆到块外面之后: 只要不是数字层就当场收掉, 谁也别想赖在别的键盘上。
        // (换脸时那一层"旧照片"是 TickScreen 在本方法**第一行**拍的, 拍在收掉它
        //  之前, 所以数字层退场那一趟照样由照片陪着演完, 不会"啪"一下凭空消失。)
        UpdateZeroKey(c);
        // (以前这儿还连着一句"动态效果关着才去算摇杆线那几个点" —— 那个开关
        //  撤了之后这条永远不成立。UpdateStick 方法本身留着没动, 连同
        //  BuildStickLayer 画的那根虚线一起, 哪天要让它露脸接回来就行。)

        if (en && !gridMode)
        {
            bool shift = c.ShiftOn;
            for (int row = 0; row < Keyboard26.RowCount; row++)
                for (int col = 0; col < Keyboard26.Len(row); col++)
                {
                    var k = Keyboard26.At(row, col);
                    // 字符键显示"这一刻真会打出去的那个字": 按住 LB(Shift)时
                    // 数字键就变成 @ # $, 字母变大写; Caps 亮着时字母也是大写。
                    string s = k.Kind == KKind.Char
                        ? k.CharNow(shift, c.Caps).ToString()
                        : k.Label;
                    _enText[row][col].Text = s;
                    bool on = c.Focus == FocusZone.Keyboard && c.Row26 == row && c.Col26 == col;
                    // Caps / Shift / Ctrl / Alt / Win 粘住时整颗键点亮, 否则看不出状态
                    bool lit = k.Kind switch
                    {
                        KKind.Caps => c.Caps,
                        KKind.Shift => c.ShiftOn,
                        KKind.Mod => k.Vk == TextOutput.VK_CONTROL ? TextOutput.ModCtrl
                                   : k.Vk == TextOutput.VK_MENU ? TextOutput.ModAlt
                                   : TextOutput.ModWin,
                        _ => false,
                    };
                    Paint(_en[row][col], on, _enText[row][col], null, lit);
                    // 26 键的键比九键小得多(标准键 36px), 放大倍数要大一点才看得出"压住了旁边"
                    if (on) { fxId = "26:" + row + ":" + col; fxB = _en[row][col]; fxS = FxKit.ScaleKey26; }
                }
        }

        var rowItems = c.CandRowItems();
        // **候选行: 有候选才占那 46, 没候选就是 0**。以前这一行钉死 46 永远占着,
        // 于是绝大多数时候(没在拼拼音)九键上面凭空空出一条 —— 正是"9 键上面
        // 不要那段 UI 空白"要拿掉的东西。它现在在卡片**外面**, 收成 0 也不会
        // 挪动卡片(这一摞是贴底的), 所以放心收。
        SetRowH(RowCand, CAND_ROW_H);
        for (int i = 0; i < InputCore.CandPerRow; i++)
        {
            bool has = i < rowItems.Count;
            // **没内容的格子不再留一层半透明的空壳**。以前为了\"位置钉死\", 空的也画个
            // 0.35 的淡底撑在那儿 —— 于是绝大多数时候(九键不打标点, 这五行全是空的)
            // 候选行上就干巴巴摆着五个空圆角块, 看着像五个没用的格子。
            // 藏掉它们, 行还是那 46px, 只是不再有那层皮。
            //
            // 用 Hidden 而不是 Collapsed: UniformGrid 会把 Collapsed 的子项整条跳过再
            // 重新均分, 剩下的候选会被拉宽、排列也跟着挪位(铺了贴图时尤其难看);
            // Hidden 照旧占着那一格, 第几个候选就蹲第几个位置 —— 这条经验是从
            // GridCand 那 40 格身上学来的, 那儿也是这么写的。
            _candRow[i].Visibility = has ? Visibility.Visible : Visibility.Hidden;
            if (!has) continue;
            _candText[i].Text = rowItems[i];
            bool on = c.Focus == FocusZone.CandRow && c.CandIdx == i;
            Paint(_candRow[i], on, _candText[i], null);
            if (on) { fxId = "c:" + i; fxB = _candRow[i]; fxS = FxKit.ScalePop; }
        }

        if (gridMode)
        {
            var items = c.GridItems();
            // 装拼音时 5 列(每格 72px)放不下整串, 换成 3 列。
            // **行数也必须钉死**(拼音 / 符号一律 5 行): 以前不设行数, 40 格排 3 列
            // 就被均成 14 行, 每格只剩 25px 高 —— 长拼音折出来的第二行整个被裁在
            // 格子外, 看着就是"字母飘在上半截、看不清楚"。钉成 5 行后每格约 66px,
            // 放得下三行字; 超过一页的按 ↑↓ 翻页。
            int cols = c.GridColsNow;
            if (GridCand.Columns != cols) GridCand.Columns = cols;
            if (GridCand.Rows != c.GridRowsNow) GridCand.Rows = c.GridRowsNow;
            double fs = c.GridPinyin ? 14 : 16;
            // 网格遮住了键盘, 但按 A 是"上屏这一条", 反馈同样不能少
            for (int i = 0; i < _grid.Length; i++)
            {
                // 空的格子照"没内容"处理, 但位置照旧占着(否则格子会被拉大变形)。
                bool has = i < items.Count && items[i].Length > 0;
                // Hidden 而不是 Collapsed: 空的也要占格子, 否则格子会被拉大变形
                _grid[i].Visibility = has ? Visibility.Visible : Visibility.Hidden;
                if (!has) continue;
                _gridText[i].Text = items[i];
                // 【正中间那格是"标签格"】它装的是分类名(见 InputCore.GridCenter),
                // 不是可以按的符号: 字号小一档、底色走"半选中"那档(见 Paint 的 lit),
                // 长得还是一颗键, 但一眼能看出它跟旁边那 24 个不是一类东西。
                // 高亮 / 放大那套动效它照样有 —— 光标停在它上面是常态。
                bool label = punctGrid && i == c.GridCenter;
                // 整串特别长的话连三行都塞不下, 再缩一档字号兜住 ——
                // 缩到最后也是"看得清", 总好过被裁掉半截。
                double f = fs;
                if (c.GridPinyin && items[i].Length > 17) f = items[i].Length > 26 ? 11 : 12.5;
                if (label) f = 14;
                if (_gridText[i].FontSize != f) _gridText[i].FontSize = f;
                bool on = c.GridIdx == i;
                Paint(_grid[i], on, _gridText[i], null, label);
                if (on) { fxId = "g:" + i; fxB = _grid[i]; fxS = FxKit.ScaleTile; }
            }
        }

        // (2.1.1: 底带左边那行常驻灰字撤了, 见 XAML 里 FrameFoot 那处注释。
        //  底带现在只剩页码点 + 齿轮 / 返回箭头。)

        if (_preview)
            NoteHint.Visibility = NoteBox.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;

        // 先按每条拼音的真实宽度量一量"这一行放得下几个", 写回 core ——
        // 拼音短就多摆几个(比如 p q r s 能摆五六个), 拼音长就少摆几个。
        c.PinyinFit = MeasurePinyinFit(c);
        var pyItems = c.PinyinRowItems();
        int pyStart = c.PinyinRowStart();
        // 拼音行一直可选用(十字键左右换, RB 发), 给整行一个底好认。
        // 不再靠"焦点在拼音行"判断 —— 焦点从头到尾都在九宫格上。
        bool inPy = c.Segs.Count > 0;
        // **拼音行: 有拼音才占那 34, 没拼音就是 0**。和候选行同一条规矩 ——
        // 它在卡片**外面**, 收放都不动卡片, 所以能收就收。
        SetRowH(RowPinyin, PINYIN_ROW_H);
        // 网格展开着的时候光标在网格里, 拼音行就一把**高亮**都别留 ——
        // 留着的话行内最后那条和网格里选中的那条同时亮着, 看着像选中了两个。
        //
        // 但**底色必须一直留着** —— "有底"和"高亮"其实是两件事: 底色是
        // "这几个字母浮在哪儿"的那块地(底下压着浅色网页时, 全靠这层底把字
        // 托出来), 高亮才是"我选中了这条"。以前这俩共用一个开关(pyDim),
        // 于是展开全部候选的那一刻整行连底一起没了 —— 白底页面上那几个
        // 字母直接糊进背景里。现在分家: 底看 inPy, 高亮看 pyPick。
        bool pyDim = inPy;
        bool pyPick = inPy && !gridMode;
        for (int i = 0; i < InputCore.MaxPinyinPerRow; i++)
        {
            bool has = i < pyItems.Count;
            _py[i].Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (!has) continue;
            _pyText[i].Text = pyItems[i];
            // 未选中的给一层淡底(见上面 pyDim): 淡归淡, 它就是把字从背景里托出来的那块地;
            // 高亮(Active)才归"当前选中"管, 一行里只有一条会亮。
            bool on = pyPick && pyStart + i == c.PinyinIdx;
            _py[i].Background = on ? Active : (pyDim ? PinyinDim : Brushes.Transparent);
            _py[i].BorderBrush = on ? (pyDim ? Accent : ActiveEdge) : Brushes.Transparent;
            _pyText[i].Foreground = on ? FgHot : Pinyin;
        }
        // 展开的网格已经把剩下的都摊开摆着了, 这时候再挂个"还有更多"是废话 —— 收起来。
        bool more = pyPick && pyItems.Count > 0 && c.Segs.Count > pyItems.Count;
        _pyMore.Visibility = more ? Visibility.Visible : Visibility.Collapsed;

        // 一次只有一颗: 谁选中谁浮起来; 关着动态效果就传空, 顺手把还浮着的那颗放回去。
        // 按下时那颗再缩一下(按住一直缩着, 松手才弹回来)。
        FxSet(fxId, fxB, fxS, anyDown);

        // 浮力那一层水波(换页动效 = 浮力时才有): 九键露着就漂着, 一收就归位。
        // 挂在每颗键外面那层容器上, 和上面"浮起来 / 按下去"那套各转各的(见 SyncBuoy)。
        // 【摆在换屏管线第 ③ 步之前】漂动是常驻的背景动作, 换页那一场自己演自己的,
        // 谁先谁后都不影响; 摆在这儿是因为到这一行"这一屏到底是谁"已经定了。
        SyncBuoy();

        // **【换屏管线第 ③ 步】**这一帧新的一屏已经画完, 才轮到它进门。
        // 留在最后是为了让"内容立刻生效"这件事成立 —— 观众从头到尾看见的是
        // 上面那张照片, 真身在它底下偷偷换好, 再跟着照片一起动起来。
        TickScreenFx();
    }

    // 每个拼音格子除了文字本身还占这些: Padding 9+9 + Margin 3+3 + 边框 1+1
    const double PY_CHROME = 26;
    string _fitKey = ""; int _fitVal = InputCore.PinyinPerRow;

    /// <summary>
    /// 量一量拼音行这一排到底能放下几条。按每条拼音的真实宽度累加,
    /// 超过可用宽度就停 —— 所以 "p q r s" 能摆五六个, "shou'dao" 只能摆两三个。
    /// 结果写回 core.PinyinFit, LB/RB 就在这个范围内转。
    /// </summary>
    int MeasurePinyinFit(InputCore c)
    {
        if (c.Segs.Count == 0) return InputCore.PinyinPerRow;

        // 数字串 / **这一份候选的记号** / 行宽 / 缩放都没变就不用重新量(每帧量会一直产生垃圾)
        //
        // 【为什么这一位上是 SegsStamp 而不是 Segs.Count】摆得下几条取决于"每条拼音有
        // 多宽", 条数一样 ≠ 内容一样: 你常发 chu'ang'sheng 之后它就顶到了排头, 比原来
        // 的 chuang'sheng 宽两个字符, 而条数一个没动(同一串数字前后都是 28 条)。
        // 按条数认的话这一帧照样命中缓存, 于是拿上一次的个数去摆这一回的内容 ——
        // 多出来的那格被顶到边界外, 左右键还在那个范围里转。见 InputCore.SegsStamp
        // 的说明和 coretest 【33】。
        string key = c.Digits + "|" + c.SegsStamp + "|"
                   + (int)PinyinRow.ActualWidth + "|" + (int)(Scale * 100);
        if (key == _fitKey) return _fitVal;

        // ActualWidth 是布局单位(缩放走 LayoutTransform, 不换算), 字号也是布局单位, 两边一致
        double avail = PinyinRow.ActualWidth;
        if (avail < 40) avail = 360;

        var t0 = _pyText[0];
        var tf = new Typeface(t0.FontFamily, t0.FontStyle, t0.FontWeight, t0.FontStretch);
        double used = 0;
        int fit = 0;
        int n = c.Segs.Count;

        // 末尾那个"…"的宽度。WrapPanel 不管"会不会换行", 摆不下就真把你挤到下一行,
        // 所以摆完之后还得有东西在后面时, 这一段宽度必须提前扣掉。
        double moreW = 0;
        if (n > 1)
        {
            var fm = new FormattedText("…", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, 17, Fg, 1.0);
            moreW = fm.Width + 10;      // 左右 Margin(4+2) 再加一点余量
        }

        foreach (var s in c.Segs)
        {
            // 摆完这一条后面还有候选 -> 末尾要给"…"留一格
            bool tail = fit + 1 < n;
            if (fit + (tail ? 1 : 0) >= InputCore.MaxPinyinPerRow) break;
            var ft = new FormattedText(string.Join("'", s), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, t0.FontSize, Fg, 1.0);
            double w = ft.Width + PY_CHROME;
            if (used + w + (tail ? moreW : 0) > avail) break;
            used += w;
            fit++;
        }
        fit = Math.Max(1, fit);

        _fitKey = key; _fitVal = fit;
        return fit;
    }

    void Paint(Border b, bool on, TextBlock main, TextBlock sub, bool lit = false)
    {
        // 浮字下"粘住的修饰键"不能变白字(ActiveEdge 是白, 在透明底上会消失), 跟选中一样用蓝
        if (main != null) main.Foreground = on ? FgHot : lit ? (SkinMode == 3 ? FgHot : ActiveEdge) : Fg;
        if (sub != null) sub.Foreground = on ? ActiveEdge : Sub;

        // 贴纸风: 键底是贴图, 描边全部收掉(胶囊自带白边和投影)。普通=粉 / 选中=薄荷 /
        // 粘住的修饰键=淡紫 —— 三色已经足够把状态讲清楚, 再多就开始花了。
        //
        // 【圆形风格下三段式贴图让位】26 键 / 候选行 / 符号网格用的是 key_*_L/M/R
        // 那三张图拼出来的胶囊 —— 它是**子元素**, 而 Border 的圆角只裁自己的背景,
        // 裁不到子元素头上, 留着就是一整块方图露在圆外面。所以圆形时这几个一律
        // 退回纯色画法(可爱粉的配色 / 方格纸底纹照样在, 只是键底换成纯色圆)。
        // 九键的花瓣是整图铺在**键底**上的(归 Border 自己管), 而且可爱粉的九键
        // 在 ApplyKeyShape 里本来就没改圆角 —— 所以不用在这里特判它。
        //
        // 【清新(SkinMode==4)不碰贴图】它是一套**矢量**浅色主题: 键底走 Normal/Active/Lit
        // 那几支渐变笔刷, 圆角由 Border 自己裁 —— 于是方块 / 圆形两种按键风格都能直接
        // 套上, 不用像可爱粉那样在圆形下让位。可爱粉那套 key_*/petal_* 贴图虽然也在
        // skin\ 里, 清新下整套跳过(下面的门加一道 SkinMode == 1)。
        bool texDropped = _keyShape == KeyShape.Round && _skins.ContainsKey(b);
        if (SkinOn && SkinMode == 1 && SkinTex.Available && !texDropped)
        {
            // 浮字: "键"这个概念直接消失, 只留字符
            if (SkinMode == 3)
            {
                if (_skins.TryGetValue(b, out var bg3)) bg3.Hide();
                b.Background = Brushes.Transparent;
                b.BorderBrush = Brushes.Transparent;
                b.BorderThickness = new Thickness(0);
                return;
            }
            if (b.Tag is string petal)   // 九键花瓣 / 三段式胶囊: Tag 里存着它自己的花色
            {
                string own = petal;
                if (_skins.TryGetValue(b, out var bg))   // 三段式: 选中=薄荷, 粘住的修饰键=淡紫
                {
                    own = on ? "mint" : lit ? "lavender" : petal;
                    bg.Set(own);
                    bg.Show();
                    b.Background = Brushes.Transparent;
                    b.BorderBrush = Brushes.Transparent;
                    b.BorderThickness = new Thickness(0);
                }
                else                                      // 九键花瓣: 整图铺, 选中统一薄荷
                {
                    if (on || lit) own = "mint";
                    b.Background = SkinTex.Fill("petal_" + own);
                    b.BorderThickness = new Thickness(0);
                }
            }
            return;
        }
        // 纯色主题: 贴图层收起来, 回到原来的画法
        if (_skins.TryGetValue(b, out var plain)) plain.Hide();

        b.Background = on ? Active : lit ? Lit : Normal;
        b.BorderBrush = on ? ActiveEdge : lit ? LitEdge : DimEdge;
        // 贴纸风的边要粗一点才像糖果壳(细边在浅底上等于看不见)
        b.BorderThickness = new Thickness(SkinOn ? (on || lit ? 2.5 : 1.6) : (on || lit ? 1.5 : 1));
    }

    // ---------- 动态效果: 焦点那颗键"浮起来" ----------
    //
    // 界面上一次只有一颗键是浮着的。Render 每帧算出"该浮起来的那颗是谁"(一个字符串 id),
    // 和上一帧一样就什么都不做 —— 这套动画有 300ms, 每帧重开一次就变成一直在抖。
    void UpdateStick(InputCore c)
    {
        double w = Grid9.ActualWidth, h = Grid9.ActualHeight;
        if (w < 20 || h < 20) return;
        double cx = w / 2, cy = h / 2;
        double px = cx + c.StickX * cx * 0.92;
        double py = cy - c.StickY * cy * 0.92;

        Canvas.SetLeft(_dot, px - 7);
        Canvas.SetTop(_dot, py - 7);
        _line.X1 = cx; _line.Y1 = cy; _line.X2 = px; _line.Y2 = py;
        Canvas.SetLeft(_crossH, cx); Canvas.SetTop(_crossH, cy);
        Canvas.SetLeft(_crossV, cx); Canvas.SetTop(_crossV, cy);
    }

    // 【Render 里那条调用不许挪进 "!en && !gridMode" 那个块】它得每帧都跑 ——
    // 从数字层还原成全键盘那一帧, 那个块整个被跳过, 少这一句 0 号键就赖在全键盘上
    // 不走了(详见 Render 里那一处的注释)。
    void UpdateZeroKey(InputCore c, bool pressed = false)
    {
        bool num = c.Layout == Layout.NUM9;
        _key0.Visibility = num ? Visibility.Visible : Visibility.Collapsed;
        if (!num) return;

        // 【位置不在这儿摆】2.1.24 起那颗 0 挂在键盘区(那张 360×360 的格子)上、
        // 由**布局系统**居中(见 BuildZeroKey 里的注释)。
        // 以前是每帧 `Canvas.Left = 键盘区宽 / 2 - 38` 现算的。毛病不在公式, 在时差:
        // Render 读到的是这一刻动画刚写进去的宽度, 可屏幕上真正的排版要等**下一轮
        // 布局**才用得上 —— 于是它永远是"按上一帧的宽度摆的", 慢半拍。慢半拍本身
        // 不明显, 要命的是每帧的宽度变化量不固定(掉一帧就是两倍), 于是先往右偏出去、
        // 再收回来、再偏出去, 看着就是"0 在切换动画里往右闪好几下才稳住"。
        // 交给布局之后就没这个问题了: 容器这一轮排多宽, 它就按多宽居中, 不存在
        // "读到的是旧数"; 而卡片本来就是居中缩放的, 整场动画里它一像素都不动。
        bool on = c.Focus == FocusZone.Keyboard && c.Key9 == 0;
        Paint(_key0, on, _key0Text, _key0Sub);
        _key0.Opacity = (on && pressed) ? FxKit.DimPress : 1;
    }

    // ---------- Win32 ----------
}
