namespace Binput;

public enum Layout { CN9, EN26, NUM9 }
/// <summary>焦点所在的区域。决定 A / RT 的语义(键盘区=输入键, 候选区=上屏)。</summary>
public enum FocusZone { Keyboard, CandRow, CandGrid }
public enum NavMode { Stick, DPad }

public sealed class InputCore : IQuickHost
{
    // ---- 状态(UI 直接读) ----
    public Layout Layout = Layout.CN9;
    public FocusZone Focus = FocusZone.Keyboard;
    public NavMode Nav = NavMode.Stick;

    public int Key9 = 5;                 // CN9 / NUM9 光标, 1..9
    /// <summary>26 键全键盘的光标。默认停在 F 上(食指起始位)。</summary>
    public int Row26 = Keyboard26.HomeRow, Col26 = 4;
    public int CandIdx;                  // CandRow 光标
    public int GridIdx;                  // CandGrid 光标
    public int PinyinIdx;                // 当前选中的拼音切分

    public string Digits = "";

    // 所有可能的拼音切分。写成属性是为了每次**换一整份候选**都能顺手给 SegsStamp 加一
    // (见那里) —— 界面那边量的"一行摆得下几条"认的就是这个记号。
    List<string[]> _segs = new();
    public List<string[]> Segs
    {
        get => _segs;
        set { _segs = value ?? new List<string[]>(); SegsStamp++; }
    }

    /// <summary>
    /// <see cref="Segs"/> 每重新排一次就 +1 —— 给界面认"这份候选是不是还是刚才那一份"用。
    /// <para>【为什么需要它】界面量"拼音行一行摆得下几条"挺贵(要给每条拼音建
    /// FormattedText), 所以带缓存, 拿一把钥匙判断"这一帧能不能沿用上次的结果"。
    /// 那把钥匙以前是 <c>Digits | Segs.Count | 行宽 | 缩放</c> —— **四样里偏偏没有内容**。
    /// 可决定摆得下几条的正是"每条有多宽": 你发过几次 chu'ang'sheng 之后它就顶到了
    /// 排头, 比原来的 chuang'sheng 宽了两个字符, 而**条数一个没动**(实测同一串数字
    /// 前后都是 28 条) —— 钥匙完全一样, 于是沿用上一次的个数, 多出来的那格被顶出边界,
    /// 左右键却还在那个范围里转。见 coretest 【33】。</para>
    /// <para>按条数认不行, 就得按"这一份是不是刚才那一份"认。赋一次值加一, 比挨个比
    /// 拼音串省得多, 也不必去操心排序到底变了没有。</para>
    /// </summary>
    public int SegsStamp;

    public bool Punct;
    /// <summary>展开的候选网格里装的是拼音切分(而不是标点)。</summary>
    public bool GridPinyin;
    public bool Caps;
    /// <summary>
    /// Shift 有两条路并到同一个状态上:
    /// **按住手柄 LB** 就是按住 Shift(松手就掉) —— 按住 LB 打数字键,
    /// 出来的就是 @ # $ 那一档上档符号;
    /// **界面上按一下 ⇧** 是粘住(再按一下松开), 给"接下来一串都要大写"用。
    /// </summary>
    public bool ShiftHeld;      // LB 正按着
    public bool ShiftLatch;     // 界面上 ⇧ 粘住了
    public bool ShiftOn => ShiftHeld || ShiftLatch;

    public double StickX, StickY;        // 左摇杆点位, -1..1
    short _stickLX, _stickLY;            // 左摇杆这一帧的原始值(-32768..32767); ClearInput 问"手还推着吗"用

    // ---- 右摇杆: 九键翻成标点 ----
    /// <summary>九键这会儿是不是翻成了标点(推右摇杆时出现, 回中就翻回去)。</summary>
    public bool Punct9;
    /// <summary>标点层里摇杆指在哪一格(1..9)。0 = 还没推。</summary>
    public int Punct9Sel;
    /// <summary>在这一格上停了多久(0..1, 满了就出这个符号)。</summary>
    public double Punct9Progress;
    /// <summary>标点符号层退出后要还回去的那一面(全键盘下推右摇杆会临时回到九键)。</summary>
    Layout _beforePunct = Layout.CN9;

    // ---- 滑行输入 ----
    /// <summary>
    /// 滑行的开关(开 / 关)。设置页那个按钮一下一下地切。
    /// 开着的时候按住 LB, 摇杆停稳一格那个数字当场进拼音。
    /// </summary>
    public SlideMode SlideMode = SlideMode.Type;

    /// <summary>
    /// 换页面的动效风格(见 FxStyle)。界面每演一场都问一次当前值,
    /// 于是在设置里改完**下一笔立即生效**, 不用重开界面。
    /// </summary>
    public FxStyle FxStyle = FxStyle.Plain;

    /// <summary>滑行走没走起来(= 不是关着)。</summary>
    public bool SlideEnabled => SlideMode != SlideMode.Off;

    /// <summary>
    /// 滑行识别器。它自己不知道"打字"这回事 —— 记一个键就回调一次,
    /// 要不要把这个数字喂给拼音由外面决定(见 <see cref="OnSlideKey"/>)。
    /// </summary>
    public readonly SlideTracker Slide = new();

    /// <summary>
    /// 门槛多久才算"停住了"(毫秒)。**只从设置页那几档里挑**, 不再自动标定 ——
    /// 自动算出来的数看着客观, 但它改的是"你滑得对不对"的判断, 而那只有手知道。
    /// </summary>
    public int SlideDwellMs { get => Slide.DwellMs; set => Slide.DwellMs = value; }

    /// <summary>连发间隔: 认下一个键之后, 还停着不动的话每隔多久再给一个(Config.REPEAT_STEPS)。</summary>
    public int SlideRepeatMs { get => Slide.RepeatMs; set => Slide.RepeatMs = value; }

    bool _slideHeld;                     // LB 正按着且条件满足(在滑)

    /// <summary>
    /// 这一刻手柄上有哪几个键正按着(掩码和 <see cref="GamepadInput"/> 的按钮位一致)。
    /// 界面拿它做"按下去了"的视觉反馈: 焦点在键盘区时, 当前那颗键按下去会缩一下。
    /// 每帧都重写, 所以手柄拔掉/界面收起时自然归零, 不会卡在"一直按着"。
    /// </summary>
    public ushort HeldMask;

    /// <summary>系统输入法当前是中文还是英文(界面显示用, 由 Poll 定时刷新)。</summary>
    public bool ImeChinese;

    /// <summary>
    /// 预输入版: 界面是前台窗口, 上面有个记事本预览框, 字先打在这里, 收起时复制粘贴。
    /// 关掉就是直出版: 不抢焦点、不预览, 按键直接送到当前有焦点的窗口。
    /// 两个版本的打法完全一样 —— 九键只把数字解成拼音发给系统输入法(汉字和联想
    /// 都是系统的), 26 键只出纯英文。
    /// </summary>
    public bool Preview;
    /// <summary>
    /// SendPinyin 最近一次记下的那条 —— 存的是**带撇的切法**("chuang'sheng")。
    /// 用来判断"整串是不是已经记过了"。
    /// <para>这里不能存连写: 连写会把 chuang'sheng 和 chu'ang'sheng 认成同一条,
    /// 于是 LearnWhole 里"这次发的和上次是不是同一条"的判断跟着失真。</para>
    /// </summary>
    string _lastSent = "";

    // ---- 九宫格 1 与 5 的功能互换 ----
    /// <summary>
    /// 设置项: 九宫格左上角那颗和正中间那颗**换一下**。默认不换。
    /// <para>不换: 左上 = JKL(按 A 出 1), 中间 = 快捷(长按开窗口 F, 短按不响)。</para>
    /// <para>换: 左上 = 快捷, 中间 = JKL(按 A 还是出 1)。</para>
    /// 换的是**格子**, 不是数字 —— JKL 无论在哪颗上, 打出来的都还是数字 1,
    /// 所以拼音该怎么切一点没变, 照键上印的字母推就行。
    /// </summary>
    public bool Swap15;

    /// <summary>
    /// 这一刻"长按开窗口 F"的那一格。滑行时**恒为 5**: 摇杆绕圈取键靠的是
    /// 固定方位(左上 = JKL), 要是按着 LB 的同时这一格还在跟着设置漂,
    /// 你就得边滑边想"这会儿左上角到底是啥", 那没法用。
    /// </summary>
    public int QuickCell => Swap15 && !_slideHeld ? 1 : 5;

    /// <summary>
    /// 这一格按下 A 会往拼音里加哪个数字; 返回 0 = 这一格不打字(快捷格)。
    /// 除了 1 / 5 两颗, 其余七颗就是格子号本身; 1 / 5 里不是快捷的那颗出 JKL 的 1。
    /// 数字层(LT)不走这里 —— 那一层九颗就是 0~9。
    /// </summary>
    public int DigitOfCell(int cell)
    {
        if (cell == QuickCell) return 0;
        return cell is 1 or 5 ? 1 : cell;
    }

    /// <summary>
    /// 已经发给系统输入法的拼音(如 zhang'san'feng), 状态栏显示用。
    /// <para>做成属性是因为**归零 = 这个词拼完了**: 上屏、出符号、收起都会归零,
    /// 那一刻把整串拼起来记一次(见 LearnWhole)。按 Y 一个字一个字定出来的名字,
    /// 全靠这一下才能记住 —— SendPinyin 每次只记"这次发出去的那截"。</para>
    /// </summary>
    public string SentPinyin
    {
        get => _sentPinyin;
        set
        {
            if (value.Length == 0 && _sentPinyin.Length > 0) LearnWhole(_sentPinyin);
            _sentPinyin = value;
        }
    }
    string _sentPinyin = "";

    /// <summary>
    /// 一个词拼完了, 把整串(去掉撇号)记一次。
    /// <para>一次性就发完整串的那种, SendPinyin 已经记过了, 这里跳过 —— 不然等于记两次,
    /// 你的名字会莫名其妙涨得飞快。</para>
    /// </summary>
    void LearnWhole(string raw)
    {
        // 【2.1.36】整串**带着撇**记, 不再先 Replace 掉。
        // 以前抹成连写(paopaosao)再交给 BumpUse, 引擎那边只能拿 SplitKey 猜"块数
        // 最少那种" —— 于是你发的是 chu'ang'sheng, 被记成了 chuang'sheng。现在带着
        // 撇, 你亲手定的那一刀原样传过去, 一个猜字都不需要。
        if (raw.Length == 0) return;
        if (raw.Replace("'", "").Length < T9Engine.MYWORD_MINLEN) return;   // 太短的都是普通词
        if (raw == _lastSent) return;                       // 整串一次发完的, 已经记过
        T9Engine.BumpUse(raw);
        // 光记"用过几次"不够 —— 还得让它下次能进候选池。SentPinyin 是带撇号的
        // (pao'pao'sao), 撇号就是音节边界, 正好直接拆开交给 AddMine。
        T9Engine.AddMine(raw.Split('\''));
    }

    /// <summary>
    /// 这次归零**不算学过**: 收起时的补发、按 B 撤销, 都不是"你选了它"。
    /// 把整串先塞进 _lastSent, LearnWhole 就会跳过, 不用另外加开关。
    /// </summary>
    void DropSent()
    {
        // 和 LearnWhole 同一套口径: 留着撇, 才能跟 _lastSent 对得上
        if (_sentPinyin.Length > 0) _lastSent = _sentPinyin;
        SentPinyin = "";
    }

    public bool Visible = true;
    public bool Dirty = true;
    public bool NoAutoHide = true;       // 默认永不自动隐藏, 显隐只由唤出键控制
    public bool PadConnected;            // 手柄是否读到(XInput 或者 HID, UI 显示用)
    /// <summary>
    /// 这一帧的手柄是从哪条路来的(只进日志)。Xbox 手柄是 "XInput",
    /// PS5 手柄是 "DualSense" 之类; 都没接上时是空串。
    /// </summary>
    public string PadSource = "";
    /// <summary>
    /// **正在调窗口大小 / 摆位**(2.3.3 起从设置 → 键盘 → 「调整窗口」进来, 按 B 出去)。
    /// 以前是"按住 R3"临时进、松手就退; 现在进去就一直在这一档里。
    /// </summary>
    public bool Resizing;

    // ---- 自定义唤醒键 ----
    //
    // 空串 = 没自定义, 用默认的 L3+R3; 有值的话**就只认自定义的那一个**
    // (设置里定的优先, 两套同时生效会互相打架 —— 用户确认过的规矩):
    //   "kb:f5"        键盘 F1~F12 里的一颗(由 App 的低级键盘钩子接, 不在这里)
    //   "pad:Back"     手柄 Back / Start / Back+Start 三选一(Poll 的第 1 步接)
    // 退出是同一颗键(唤出/隐藏本来就是一颗键来回切)。
    //
    // **不是随便定的, 是清单里挑一个**。早先两路自由度都很高(键盘能录任意组合、
    // 手柄 16 颗键随便挑), 撞车概率也跟着高: 键盘那头 Ctrl / Alt / Win 这类修饰键
    // 在各种游戏里各有各的用处, 实测 Ctrl 组合根本唤不出来; 手柄那头更夸张 ——
    // 每一颗键在打字时都有活儿干(LB 滑行、RB 发拼音、A 按住连发……), 拿它们
    // 当唤出键等于给自己找不痛快。现在只留两头的"确定不跟自己打架"的那几个。
    public string WakeSpec = "";
    int _wakeFrames;                     // 组合已同时按住几帧(够两帧才认, 过滤抖动)
    bool _wakeLatch;                     // 这一下已经切过, 松开之后再按才算新的一下
    /// <summary>唤出这一串键的账还没结完: 组合齐过、现在还有没松手的。</summary>
    bool _wakeBusy;
    /// <summary>
    /// 唤醒键按着、却从来没凑齐过组合的时刻(诊断用)。
    /// <para>用户报"PS5 插线 + 游戏按唤醒键有时按不出来": 到底是没识别到, 还是识别到
    /// 了窗口没出来, 以前这条路一声不响只能干猜。现在切换时打一行日志(带"在听哪路"),
    /// 再加上这个探测器 —— 组合里的键按着却 600ms 没凑齐(读输入在抖, 帧跟不上)
    /// 也会喊一嗓子。</para>
    /// </summary>
    DateTime? _wakeNever;

    /// <summary>
    /// 手柄这一页的选项。**挑的是"打字时腾得出来"的那几颗**: Back / Start(Xbox 上那两个
    /// 小键), 加上 2.3.2 挪出来的 L3 / R3。
    /// <para>L3 / R3 能进这张表是因为它们**一路空出来的**: 2.3.2 之前 L3 是"换九键 /
    /// 全键盘"、R3 短按是"进设置", 两颗都有活干, 拿它们当唤出键必然打架。
    /// 这两桩活搬到 Y 上之后(L3 换布局 -&gt; Y 短按; 进设置 -&gt; Y 长按), 它们只剩
    /// "数字层出 0""按住调界面"这种**只在自己那一层里才发生**的活; 2.3.3 连那两桩
    /// 也归了 Y(数字层出 0 / 窗口F 解绑), 调界面则搬到设置页里 —— 于是这两颗现在
    /// **彻底闲着**, 拿去当唤出键不会跟任何一桩活抢。</para>
    /// <para>代价只有唤出本身那一条: 唤出那一串键按住期间整帧都不往下传, 所以选了
    /// L3 / R3 之后, 它们就只剩"叫出来 / 收起来"这一件事(别的活本来也已经搬走了)。
    /// 这是"一颗键两个主人"必然要付的, 反过来让唤出和打字互相打架更糟。</para>
    /// </summary>
    public static readonly string[] PadWakeOptions = { "Back", "Start", "Back+Start", "L3", "R3" };
    /// <summary>
    /// 键盘这一页的十二个选项: F1~F12。游戏里基本没人占这几个键, 撞车概率最低,
    /// 而且它们单独一颗就能当唤出键(不用带修饰键, 也就没有"Ctrl 组合唤不出来"那档事)。
    /// </summary>
    public static readonly string[] KeyWakeOptions =
    {
        "f1", "f2", "f3", "f4", "f5", "f6",
        "f7", "f8", "f9", "f10", "f11", "f12",
    };

    /// <summary>
    /// **现在这套唤出组合是不是正按着**(组合齐了没): 自定义了 pad: 就认 Back / Start,
    /// 定了键盘的那就不认手柄这一套, 什么都没定就认默认的 L3+R3。Poll 的第 1 步用它。
    /// </summary>
    bool WakeComboHeld(ushort btn, bool lt, bool rt)
    {
        if (IsPadWake()) return PadWakeHeld(WakeSpec.Substring(4), btn, false);
        // 键盘那一套由 App 的低级键盘钩子接(见 App.ReinstallKbHook), 手柄这边一律不认 ——
        // 定了键盘组合之后 L3+R3 也就作废了: 两套同时认, 用户按了半天不知道该算谁的。
        if (IsKbWake()) return false;
        return (btn & GamepadInput.L3) != 0 && (btn & GamepadInput.R3) != 0;
    }

    /// <summary>自定义的是一个手柄组合(开头 "pad:")。</summary>
    bool IsPadWake() => WakeSpec.StartsWith("pad:", StringComparison.OrdinalIgnoreCase);
    /// <summary>自定义的是一个键盘键(开头 "kb:")。</summary>
    bool IsKbWake() => WakeSpec.StartsWith("kb:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 手柄这套组合跟当前按键的关系。spec 只可能是 Back / Start / Back+Start。
    /// <param name="any">false = 组合齐了没(全按着才算); true = 组合里**有任何一个**还按着
    /// (松开一半时用, 见 <see cref="WakeKeysDown"/>)。</param>
    /// </summary>
    static bool PadWakeHeld(string spec, ushort btn, bool any)
    {
        bool back = (btn & GamepadInput.BACK) != 0;
        bool start = (btn & GamepadInput.START) != 0;
        bool l3 = (btn & GamepadInput.L3) != 0;
        bool r3 = (btn & GamepadInput.R3) != 0;
        bool needBack = spec.Contains("Back", StringComparison.OrdinalIgnoreCase);
        bool needStart = spec.Contains("Start", StringComparison.OrdinalIgnoreCase);
        bool needL3 = spec.Contains("L3", StringComparison.OrdinalIgnoreCase);
        bool needR3 = spec.Contains("R3", StringComparison.OrdinalIgnoreCase);
        if (!needBack && !needStart && !needL3 && !needR3) return false;   // 老存档, 一律不认
        bool Hit(bool need, bool on) => need ? on : false;
        return any ? Hit(needBack, back) || Hit(needStart, start) || Hit(needL3, l3) || Hit(needR3, r3)
                   : (!needBack || back) && (!needStart || start)
                     && (!needL3 || l3) && (!needR3 || r3);
    }

    /// <summary>
    /// 自定义的那几个键里**还有没松手的**(用于"松开一半先别放行")。
    /// <para>只对 pad: 自定义有意义 —— 默认那一对(L3 / R3)单独按一下各自有自己的活
    /// (2.3.3 之后两颗都闲着, 但保不齐以后又挂上什么), 那不能算进"还按着"里,
    /// 否则单按一下就被这一层吃掉了。</para>
    /// </summary>
    bool WakeKeysDown(ushort btn, bool lt, bool rt)
    {
        if (!IsPadWake()) return false;
        return PadWakeHeld(WakeSpec.Substring(4), btn, true);
    }

    /// <summary>
    /// 存档里那条是不是**现在还认的**唤醒键描述。更早的版本能存任意组合
    /// ("kb:ctrl+f1" / "pad:lb+rb"), 那些一律作废 —— 留着是最坏的结果: 手柄那一路
    /// 照旧不认、键盘那一路也接不住, 就成了怎么都唤不出来, 所以启动时替用户删掉。
    /// </summary>
    public static bool IsWakeSpec(string s)
    {
        if (s.StartsWith("kb:", StringComparison.OrdinalIgnoreCase))
            return VkOfWakeKey(s.Substring(3)) != 0;
        if (s.StartsWith("pad:", StringComparison.OrdinalIgnoreCase))
            return Array.Exists(PadWakeOptions,
                o => string.Equals(o, s.Substring(4), StringComparison.OrdinalIgnoreCase));
        return false;
    }

    /// <summary>
    /// 界面正翻在设置那一面。**状态以窗口为准, 每帧同步进来** —— 翻面不止手柄能翻,
    /// 鼠标点右下角的齿轮也能翻, 所以不能由输入这边自己记账。
    /// 在这一面十字键变成"上下选设置项", 不再是键盘的方向键。
    /// </summary>
    public bool Settings;
    /// <summary>
    /// **长按 Y**(半秒, 2.3.2 起; 2.3.3 从 1 秒缩到 500 毫秒): 进 / 出设置。以前是 R3 短按。
    /// <para>1 秒实测偏长: 想进设置的时候得在那儿摁着数一秒, 而"换布局"是松手才发生的,
    /// 于是每次进设置都先干等。500 毫秒够和"随便按一下"(七八十毫秒)拉开距离,
    /// 又不至于让人以为没按上。</para>
    /// </summary>
    public Action ToggleSettings;
    /// <summary>设置页里十字键上下走(九宫格里就是上下挪一行, 三列所以 ±3)。</summary>
    public Action<int> SettingsNav;
    /// <summary>设置页里十字键左右走。</summary>
    public Action<int> SettingsNavH;
    /// <summary>设置页里 A 确认当前选中的那一格。</summary>
    public Action SettingsPick;
    /// <summary>
    /// 设置页里**左摇杆**选格: 给的是归一化后的点位(-1..1)。和九键选键同一套 ——
    /// 推哪个方向就选哪一格, 回中落回正中那格。窗口那边自己算落在哪一格,
    /// 因为"这一页有几格、哪几格是空位"只有它知道。
    /// </summary>
    public Action<double, double> SettingsStick;
    /// <summary>
    /// 设置页里 B。参数 toRoot: true = 一路退回首页(长按), false = 退一层。
    /// **返回 true 表示这一下 B 被设置页吃掉了**(外面就别再去翻回键盘面)。
    /// </summary>
    public Func<bool, bool> SettingsBack;
    /// <summary>设置页现在叠了几层(1 = 只有首页)。B 按下时要不要等着看是不是长按, 靠它。</summary>
    public Func<int> SettingsDepth;
    /// <summary>设置页里 LB / RB 翻页(-1 / +1)。</summary>
    public Action<int> SettingsPage;
    /// <summary>
    /// **按了 LB / RB**(参数 -1 / +1)。唯一用处是让框外面那对 ◀LB / RB▶ 跟着
    /// 跳一下、转一下(见 OverlayWindow.PageTurnFx)—— 按下去得有个回响,
    /// 不然翻没翻过去全靠点数。至于这一下翻的到底是谁的页(设置 / 窗口F / 符号
    /// 分哪一类), 是 OnShoulder 下面的事, 跟这儿无关。
    /// </summary>
    public Action<int> PageFx;
    /// <summary>B 按多久算"长按"(毫秒)。长按 = 从设置深处一路退回首页。</summary>
    const int LONG_B_MS = 420;
    bool _bWasDown;                                  // 上一帧 B 是不是按着的
    DateTime _bDownAt;                               // B 按下的时刻
    bool _bLongFired;                                // 这一下 B 的长按已经用掉了

    // ---- Y 的五种活(2.3.3) ----
    //
    // Y 以前只有一桩活: 拼音切分。其余几桩散在别的手指够不着的地方 ——
    // L3(换九键 / 全键盘)和 R3 短按(进设置)都在摇杆底下, 左手正推着摇杆选键的时候
    // 根本够不着; 数字层出 0 也挂在 L3 上, 于是"按一下 L3 到底是出 0 还是换布局"
    // 全看当时在哪一层。2.3.2 把换布局和进设置搬到 Y 上, 2.3.3 再把出 0 / 解绑搬过来:
    //   · **拼音行里有得挑** -> Y 还是切分(要的是快, 按下就走, 不等松手)
    //   · **没有拼音可切**   -> 按一下 = 换九键 / 全键盘(原 L3 那桩)
    //                          按住够 Y_HOLD_MS = 进 / 出设置(原 R3 短按那桩)
    //   · **数字层**         -> 按一下出 0(原 L3 那桩, 右摇杆转圈那套照样能用)
    //   · **窗口F 绑数字**   -> 按一下解除绑定(原 L3 那桩)
    // 判据还是那一条: **这一下 Y 能不能切分**。能切分就绝不参与长按 —— 打字打一半
    // 手指在 Y 上多停了半秒, 不该把设置页弹出来; 反过来没拼音的时候按住 Y,
    // 也不该顺手把布局换了。
    const int Y_HOLD_MS = 500;
    bool _yWasDown; DateTime _yDownAt; bool _yLongFired;
    /// <summary>
    /// 这一下 Y 按下时已经**当场用掉了**(切分 / 数字层出 0): 不参与长按、松手也不补。
    /// </summary>
    bool _ySplit;
    /// <summary>这一下 Y 按下时没有拼音可切 —— 松手(短按)才去换布局。</summary>
    bool _yDefer;
    /// <summary>
    /// 读预览框(记事本)里现在的文本。**直出版没有预览框, 那时返回 null** ——
    /// "添加到短语 / 编辑短语"两个动作全靠它, 直出版下这两个动作直接不给用。
    /// </summary>
    public Func<string> QuickNoteText;
    /// <summary>
    /// 往预览框里塞一段文字并把焦点给它(编辑短语时"把这条短语放回框里接着改")。
    /// 由主程序接到窗口上; 没接(无头探针)就什么都不做。
    /// </summary>
    public Action<string> QuickSetNote;
    /// <summary>
    /// 发**快捷键**之前把焦点还给游戏。快捷键是 SendInput 发到前台窗口的,
    /// 焦点还停在我们自己的预览框上的话, 那串键就打进自己框里了。
    /// 由主程序接到 GameMode.TargetHwnd 上; 没接(无头探针)就什么都不做。
    /// </summary>
    public Action QuickFocusGame;
    /// <summary>
    /// 九键 ↔ 26 键换布局时翻一下。参数是"动画演到中点时真正换布局"的那个动作 ——
    /// 先压扁、换完再展开, 才有"这张卡翻过去"的感觉。
    /// 由主程序接到窗口上; 没接(比如无头测试)就直接换, 不演动画。
    /// </summary>
    public Action<Action> FlipLayout;
    bool _swapping;                  // 上面那段动画正在演, 这期间再按 L3 不理
    public double Scale = 1.0;           // 界面缩放, 调窗口那一档里十字键上下
    public double OffsetX, OffsetY;      // 界面摆位偏移, 调窗口那一档里左摇杆
    /// <summary>调完出去时回调: (scale, offX, offY) —— 主程序拿它落盘。</summary>
    public Action<double, double, double> LayoutSaved;

    // ---- 常量 ----
    // ---- 符号面板 ----
    /// <summary>
    /// 符号面板分几类, LB / RB 换类, 网格里选。
    /// 每类固定 24 个 —— 网格是 5 列 × 5 行, 正中间那格留空(摇杆松手的落点)。
    /// 只放常用的: 太冷门的符号(∮ ∽ ㈠ 之类)塞进来只会让翻找变难。
    /// 字形都挑微软雅黑一定有的区块(U+2000 标点 / U+2190 箭头 / U+2200 数学 /
    /// U+25A0 几何图形 / 带圈数字), 免得显示成方块。
    /// </summary>
    public static readonly string[] PunctCatNames =
        { "常用", "中文", "英文", "数学", "箭头", "数字", "单位", "图形" };

    public static readonly string[][] PunctCats =
    {
        // 常用: 中英混排, 打字时最高频的那批
        new[] { "，", "。", "！", "？", "、", "；", "：", "…",
                "（", "）", "《", "》", "【", "】", "“", "”",
                ",", ".", "!", "?", ";", ":", "@", "#" },

        // 中文标点
        new[] { "，", "。", "！", "？", "、", "；", "：", "…",
                "～", "—", "（", "）", "《", "》", "〈", "〉",
                "「", "」", "『", "』", "【", "】", "“", "”" },

        // 英文 / 半角符号
        new[] { ",", ".", "!", "?", ";", ":", "'", "\"",
                "(", ")", "[", "]", "{", "}", "<", ">",
                "/", "\\", "@", "#", "$", "%", "&", "*" },

        // 数学
        new[] { "＋", "－", "×", "÷", "＝", "≠", "≈", "≤",
                "≥", "＜", "＞", "±", "√", "∞", "∑", "∏",
                "∫", "∈", "⊂", "⊇", "∪", "∩", "∠", "⊥" },

        // 箭头 / 图形
        new[] { "←", "→", "↑", "↓", "↔", "↕", "↖", "↗",
                "↘", "↙", "▲", "▼", "◀", "▶", "△", "▽",
                "◁", "▷", "◆", "◇", "●", "○", "★", "☆" },

        // 数字
        new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
                "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨", "⑩",
                "½", "¼", "¾", "²" },

        // 单位 / 货币: 中文打字里常要的那一撮
        new[] { "℃", "℉", "°", "′", "″", "㎡", "㎏", "㎝",
                "㎞", "㎜", "㏄", "℡", "№", "¥", "￥", "＄",
                "$", "€", "£", "¢", "Ω", "©", "®", "™" },

        // 图形 / 表情
        new[] { "☺", "☻", "♠", "♥", "♦", "♣", "♩", "♪",
                "♫", "♬", "♭", "♮", "★", "☆", "☀", "☁",
                "☂", "☃", "♨", "⚐", "⚑", "☎", "☯", "⚡" },
    };

    /// <summary>
    /// 符号面板当前第几类。换过之后会一直记着, 下次开盘直接落在上次那一类 ——
    /// 连着输几个数学符号时不用每次都从头翻。
    /// </summary>
    public int PunctCat;

    public int PunctCatCount => PunctCats.Length;

    public string PunctCatName =>
        PunctCatNames[Math.Clamp(PunctCat, 0, PunctCatNames.Length - 1)];

    /// <summary>当前这一类的全部符号。</summary>
    public string[] PunctsNow() => PunctCats[Math.Clamp(PunctCat, 0, PunctCats.Length - 1)];

    /// <summary>
    /// 右摇杆推出来的九个标点。**下标 0 = 九宫格第 1 格(左上), 行优先**, 和左摇杆
    /// 选键用的是同一套 PointTo9 —— 推哪儿出哪儿, 不用另外记一套方位。
    /// 正中间那格(第 5 格)**留空**: 摇杆松手回中就落在这儿, 它要是有内容,
    /// 每次收手都会顺手打出一个标点。
    /// 右下角那格不是符号, 是"更多": 停够久唤出完整的标点候选框。
    /// <para>以前是浮在九宫格上的 6 个圆形扇区; 改成"九颗键自己翻成标点"之后
    /// 字大得多、位置也跟九键对得上, 不用再盯着中间那圈小圆片找。</para>
    /// </summary>
    public static readonly string[] PunctSymbols =
        { "，", "。", "？", "！", "", "…", "、", "：", "更多" };
    public const int Punct9More = 9;      // "更多"在第 9 格(右下角)
    public const int Punct9Blank = 5;     // 正中间留空

    // 26 键全键盘: 键位表搬到 Keyboard26.cs 去了(约 82 键的笔记本键盘,
    // 带数字行、F1~F12、Tab / Caps / Shift / Ctrl / Alt / Win / 回车 / 退格 / 方向键)。
    public const int CandPerRow = 5;
    /// <summary>
    /// 展开网格现在的列数。装拼音时降到 3 列 —— "shou'dao" 这种两个音节的串
    /// 在 5 列(每格 72px)里必然被切掉, 3 列(每格 120px)才放得下。
    /// </summary>
    public int GridColsNow => GridPinyin ? 3 : 5;

    /// <summary>
    /// 网格一页几行。**这是 1.2.0 修"拼音格子显示不全"的关键**:
    /// 以前行数不设上限, 40 格排 3 列就成了 14 行, 每格只剩 25px 高 ——
    /// 长拼音明明折了行, 第二行却整个被裁在格子外面, 看着就是"字母飘在上半截"。
    /// 现在行数据此固定: 拼音 3×5=15 格一页(每格约 66px 高, 放得下三行字)。
    /// 【2.1.7: 符号改成 5×5】以前是 5×8=40 格(正好装一类), 可摇杆在 8 行里
    /// 落点每档只有 25% 行程(一格约 1mm), 太密; 竖着压到 5 行之后横竖都是
    /// 20% 一档, 格子也从 72×45 变成 72×72。代价是一类从 40 个精简到 24 个。
    /// </summary>
    public int GridRowsNow => 5;
    /// <summary>一页几格。符号面板 24 个一页装得下, 只有拼音网格会分页。</summary>
    public int GridPageSize => GridColsNow * GridRowsNow;
    /// <summary>当前是第几页(0 起)。</summary>
    public int GridPage;
    /// <summary>一共几页。</summary>
    public int GridPageCount =>
        Math.Max(1, (GridTotal() + GridPageSize - 1) / GridPageSize);
    /// <summary>当前页第一条在"网格自己的序列"里是第几条。</summary>
    public int GridPageStart => GridPage * GridPageSize;

    /// <summary>
    /// 拼音网格里的第 0 格对应 <see cref="Segs"/> 的哪一条。
    /// <para>拼音行已经横排摆着前 <see cref="FitNow"/> 条, 网格**只收行里摆不下的那些**
    /// (下标 FitNow 往后)。以前网格是从第 0 条开始摆的, 于是行里那一批在下面又出现一遍,
    /// 同一屏看两份同样的拼音, 而光标还停在行内最后一条上 —— 两处都在高亮它。</para>
    /// </summary>
    public int GridOffset => GridPinyin ? Math.Min(FitNow, Segs.Count) : 0;
    /// <summary>拼音行还没量出宽度时的默认个数(界面一渲染就会被实际值覆盖)。</summary>
    public const int PinyinPerRow = 4;
    /// <summary>拼音行最多能摆几个格子(界面按这个数建控件, 量出来再多也不超)。</summary>
    public const int MaxPinyinPerRow = 8;

    /// <summary>
    /// 拼音行实际能放下几个 —— 由界面拿每条拼音的真实宽度量出来写回这里。
    /// 拼音短(比如 "p" "q" "r" "s")就多摆几个, 拼音长(比如 "shou'dao")
    /// 就少摆几个, 不再固定四个。
    /// </summary>
    public int PinyinFit = PinyinPerRow;

    /// <summary>真正生效的个数: 兜住"条数比格子少"和"还没量出来"两种情况。</summary>
    public int FitNow => Math.Clamp(PinyinFit, 1, Math.Max(1, Segs.Count));

    const int DZ = 9000;
    /// <summary>摇杆的死区(归一化后)。设置页的九宫格判"推没推"也用这一个, 和选键同标准。</summary>
    public const double DZ_N = DZ / 32768.0;
    const int REPEAT_DELAY = 300, REPEAT_RATE = 110;
    // 输入类连发要慢: 数字/字母/符号连着重复太快会一下子打出一长串, 一秒 2 次刚好
    const int SLOW_DELAY = 400, SLOW_RATE = 500;
    const int PUNCT9_REPEAT_MS = 500;    // 右摇杆停在某一格不动时, 每隔多久再出一个
    const int IDLE_HIDE_MS = 15000;
    const int PUNCT9_HOLD_MS = 400;      // 标点格停留多久算确认(长推才出, 蹭过去不算)
    const int PUNCT9_MORE_MS = 700;      // "更多"要停更久, 免得划过时误开标点框
    const double PUNCT9_MIN_R = 0.35;    // 小于这个半径视为回中, 标点层收起

    const double SCALE_MIN = 0.6, SCALE_MAX = 1.8, SCALE_RATE = 0.008;
    const double MOVE_RATE = 14;         // 摆位速度 px/帧(满推约 840 px/s)

    const int DIR_NONE = 0, DIR_UP = 1, DIR_DOWN = 2, DIR_LEFT = 3, DIR_RIGHT = 4;

    /// <summary>设置页里十字键上下连发的间隔。比挪光标慢 —— 列表一项一项看才不会划过头。</summary>
    const int SET_REPEAT_DELAY = 320, SET_REPEAT_RATE = 240;

    // ---- 内部 ----
    Layout _beforeNum = Layout.CN9;
    ushort _prevBtn;
    bool _prevLT, _prevRT, _prevL3, _prevR3;
    DateTime _imeNext;               // 下次去问系统输入法状态的时间(每帧问太勤, 250ms 一次够看)

    int _repeatDir; DateTime _repeatNext;
    int _dpadDir; DateTime _dpadNext;
    // X / RB / A 长按连发
    bool _xHeld; DateTime _xNext;
    bool _rbHeld, _rbSpace; DateTime _rbNext;
    bool _aHeld, _aRepeat; DateTime _aNext;
    DateTime _lastActivity = DateTime.Now;
    DateTime _punct9At, _punct9Next; bool _punct9Fired;

    /// <summary>
    /// 布局一变就要同步两件事(两个版本都一样):
    ///   1. TextOutput 走哪条路 —— 九键要把字母/数字发给系统输入法(虚拟键),
    ///      26 键必须是纯字母(UNICODE 直投, 绕开输入法)。
    ///   2. 系统输入法本身的中/英 —— 九键要中文态才出汉字, 26 键要英文态才是纯字母。
    /// 两边必须一起动, 否则就会出现"按字母唤出拼音条"这类串味。
    /// </summary>
    public void SyncIme()
    {
        var baseL = Layout == Layout.NUM9 ? _beforeNum : Layout;
        bool zh = baseL == Layout.CN9;
        TextOutput.ImeMode = zh;
        ImeStatus.SetChinese(zh);
    }

    /// <summary>
    /// 收起之前把半截状态结掉。不这么做会丢字 —— 两处:
    ///   1. 数字还没按切分键, 字母根本没发出去;
    ///   2. 字母发了但没上屏, 这时字还挂在输入法的 composition 里,
    ///      **不在记事本的 Text 里**(未确认的预编辑串不算文档内容), 直接读就是空的。
    /// 所以先补一次切分, 再发个空格让输入法上屏, 等一小会儿再读文本。
    /// </summary>
    public void Flush()
    {
        if (!Preview) return;
        // 什么都没有就别收尾 —— 不然 FinishSent 那个空格会凭空打出去一个
        if (Digits.Length == 0 && SentPinyin.Length == 0) return;
        // "还有没发出去的数字" = 收起时程序要替你做主选一种切分。
        // 只有这种情况下最后那截不是你选的, 才该整串都不算学过。
        bool auto = Digits.Length > 0;
        if (auto)
        {
            App.Log($"收起前补切分: 还剩 {Digits.Length} 位数字");
            // 发整段而不是只发头一个音节: 4864 该出 hu'ni 而不是只有 hu,
            // 第二个字凭空丢掉比"猜错"更糟
            // learn: false —— 收起时是程序替你补发的, 不算"你选了它"
            if (Segs.Count > 0) SendPinyin(Segs[SegIdxNow], false);
            else Digits = "";          // 解不出来就丢掉, 别一直挂着
        }
        // 整串每一个音节都是你自己定的(auto=false)时它就算学过, 该记 ——
        // 以前这里无条件 DropSent, 于是"按 Y 定完名字、上屏还没按就收起"
        // 也被判成没学过, 名字永远记不住, 下次还得再拼一遍。
        FinishSent(auto);
    }

    /// <summary>
    /// 切版本。两个版本的打法是一样的(九键只解拼音发给系统输入法, 26 键只出纯字母),
    /// 切换只是换掉"字往哪儿去": 预输入版打在自己的预览框里, 直出版直接打进当前窗口。
    /// </summary>
    public void SetPreview(bool on)
    {
        Preview = on;
        ClearInput();
        DropSent();
        Layout = Layout.CN9;
        _beforeNum = Layout.CN9;
        // 九键必须"发虚拟键": 字母要交给系统输入法做拼音 composition,
        // 空格要让它当"上屏第一个候选"。走 UNICODE 直投的话输入法接不到,
        // 空格就变成一个普通空格, 拼了一半的字永远上不了屏。
        // 切到 26 键时 SyncIme 会把它关掉(那边要的是纯字母)。
        TextOutput.ImeMode = true;
        Dirty = true;
    }

    // ================= 主循环 =================
    public void Poll(PadState st, DateTime now)
    {
        _now = now;             // 记下这一帧(延迟发快捷键要用, 见 SendHotkey)
        // 系统输入法状态: 每 250ms 问一次前台窗口, 够跟上用户手动切换了
        // (两个版本都完全依赖系统输入法出字, 所以不问版本, 一直问)
        if (now >= _imeNext)
        {
            _imeNext = now.AddMilliseconds(250);
            bool zh = ImeStatus.Chinese();
            if (zh != ImeChinese) { ImeChinese = zh; Dirty = true; }
        }

        if (PadConnected != st.Connected)
        {
            PadConnected = st.Connected;
            Dirty = true;
            App.Log("手柄" + (st.Connected ? "已连接" : "断开")
                    + (string.IsNullOrEmpty(PadSource) ? "" : " (" + PadSource + ")"));
        }
        // 按住状态每帧无条件刷新。手柄一断(或者还没接上)这里就是 0,
        // 界面上的"按下去"效果自然消失, 不用专门在断开分支里清。
        HeldMask = st.Connected ? st.Buttons : (ushort)0;
        if (!st.Connected) return;

        var btn = st.Buttons;
        var pressed = (ushort)(btn & ~_prevBtn);
        bool lt = st.LeftTrigger > 100, rt = st.RightTrigger > 100;
        bool l3 = (btn & GamepadInput.L3) != 0, r3 = (btn & GamepadInput.R3) != 0;

        // 0. B 的长按: 设置页里是"一路退回首页"(见 OnB)。
        //    按下的那一刻记时间, 按够 LONG_B_MS 就触发一次, 之后松手不再补那一下短按。
        TickLongB(btn, now);

        // 0.5 Y 的长按: 没有拼音可切的时候, 按住够半秒是"进 / 出设置"(见 TickLongY)。
        //     排在按钮处理**之前** —— 它认的是"按住多久", 得每帧都看到按键状态,
        //     而按钮处理那一趟只在"按下那一帧"跑一次。
        TickLongY(btn, now);

        // 1. 唤出 / 隐藏(隐藏时也必须响应, 否则唤不出来)。
        //    自定义的手柄组合优先; 没定就用默认的 L3+R3。退出是同一颗键。
        //    "同时按住满 2 帧才认 + 松手才解锁"这两条不变, 只是"按的是哪几个"换成自己定的。
        //
        //    **这一串键按下到松开之间, 每一帧都到此为止**(return), 不再往下传给打字那些步骤 ——
        //    手柄上每一颗键同时也是功能键(LB 起滑行、RB 发拼音、A 走按住连发),
        //    不隔开的话从按下的**第一帧**起就在干别的事了 —— 唤出要够两帧才认,
        //    可滑行第一帧就起来了。最显眼的就是 LB+RB 唤出: 先把滑行开一下、松手再关掉,
        //    界面上那几行的高度跟着弹一下, 看着就是"刚出来闪一下"。
        //    注意 **满不完整 和 松没松开 是两件事**: 组合齐了才判切显隐,
        //    但只要这一串键里还有没松的, 这一帧就照样不往下传 —— LB+RB 的时候先松 RB,
        //    剩下的 LB 立刻起滑行, 闪的那一下仍然躲不掉(用户先松哪个手指是不一定的)。
        bool allWake = WakeComboHeld(btn, lt, rt);   // 组合齐了(这一帧可能正在切显隐)
        bool someWake = WakeKeysDown(btn, lt, rt);   // 组合里的键还有没松的
        // 账从"组合齐了"那一刻算起: 只按住 LB 不算(那是你要滑行), 齐了之后松到一半才算 ——
        // 那时候剩下的 LB 要是照常处理又会立刻起滑行, 闪的那一下还是躲不掉。
        if (allWake) _wakeBusy = true;
        if (allWake || (_wakeBusy && someWake))
        {
            if (allWake && ++_wakeFrames >= 2 && !_wakeLatch)
            {
                Visible = !Visible;
                _wakeLatch = true;
                _lastActivity = now;
                Dirty = true;
                // 【诊断】唤出/收起这一下打一行, 带"这一帧听的是哪一路" ——
                // 用户报"PS5 插线+游戏有时唤不出来 / 蓝牙完全无效", 有这行才知道
                // 到底是"没识别到"(日志里压根没有这行)还是"识别到了但窗口没出来"
                // (这行有, 但外面那句"屏幕外停...后归位"没有)。两种病治法不一样。
                App.Log($"唤醒键(手柄): {(Visible ? "唤出" : "收起")} · 在听 {PadSource}");
            }
            // 刚收起来的话摊子照收(数字层要还回去、窗口F 要关掉、调窗口那一档要结掉)
            if (!Visible)
            {
                if (Resizing) EndResize();
                if (_ltNum) { _ltNum = false; Layout = _beforeNum; SyncIme(); }
                Punct9Off();
                if (QuickOpen) QuickClose();
            }
            _prevBtn = btn; _prevLT = lt; _prevRT = rt; _prevL3 = l3; _prevR3 = r3;
            _wakeNever = null;
            return;
        }
        // 到这儿就是"组合没齐"了: 这一串键全松开的话, 把手势的账也一并抹掉,
        // 下一次按下又是全新的一下(只按住其中一颗算正常使用, 不走这里)。
        if (!someWake) _wakeBusy = false;
        _wakeFrames = 0;
        _wakeLatch = false;

        // 【诊断】组合里的键按着, 却从来没凑齐过 —— 读输入在抖(帧跟不上)时就是这个
        // 状态: 用户明明按住着, 却永远等不到"两帧都在", 表现就是"按唤醒没反应"。
        // 走到这儿说明 allWake 一次都没成立过(成立过的话 _wakeBusy 会把上面的分支接走)。
        if (someWake && !_wakeBusy)
        {
            if (_wakeNever == null) _wakeNever = now;
            else if ((now - _wakeNever.Value).TotalMilliseconds > 600)
            {
                App.Log($"唤醒键: 组合按了 {(int)(now - _wakeNever.Value).TotalMilliseconds}ms "
                      + $"也没凑齐两帧(在听 {PadSource})");
                _wakeNever = now;                 // 还按着就每秒报一次, 别只喊一次
            }
        }
        else _wakeNever = null;

        bool anyInput = pressed != 0 || lt || rt || l3 || r3 ||
                        Math.Sqrt((double)st.LX * st.LX + (double)st.LY * st.LY) >= DZ;
        if (anyInput) _lastActivity = now;

        // 1. 到点的快捷键补发(见 SendHotkey: 焦点刚要回来, 不能同一帧就发)
        if (_chordVk != 0 && now >= _chordAt)
        {
            // 【走 ChordRaw, 不走 Chord】这一串是发给**游戏**的命令, 不是写进预览框
            // 的字: 走 Chord 会被那道"先把框升起来再发键"的闸门接住, 于是发一次
            // 快捷键就凭空多出一个空预览框(见 TextOutput.ChordRaw)。
            TextOutput.ChordRaw(_chordMod, _chordVk);
            _chordVk = 0;
            Dirty = true;
        }

        // 2. 键盘隐藏时手柄完全不接管 —— 否则关掉 UI 还能往输入框里打空格和退格。
        //    但**数字层要在这里还回去**: 界面要是中途被收起(预输入版"点到别的窗口"
        //    会自动收), 松 LT 那一帧根本走不到下面的第 3 步, Layout 就永远停在 NUM9
        //    —— 1.2.1 里长按数字发短语会因为焦点被抢而收起界面, 于是"卡在数字层"。
        if (!Visible)
        {
            // 收起的瞬间这一档也得结掉(缩放 / 摆位存下来, 不然白调一场)。
            if (Resizing) EndResize();
            if (_ltNum) { _ltNum = false; Layout = _beforeNum; SyncIme(); }
                Punct9Off();
            if (QuickOpen) QuickClose();
            _prevBtn = btn; _prevLT = lt; _prevRT = rt; _prevL3 = l3; _prevR3 = r3;
            return;
        }

        // 2.5 调窗口大小 / 摆位(2.3.3)。**这一档期间别的按键一律不往下传** ——
        //     十字键和摇杆在这一档里是缩放和挪位置, A / X / 那些键还是打字那套,
        //     不隔开的话调个大小会顺手打出一片字。
        //     进这一档的入口是设置 → 键盘 → 「调整窗口」(见 BeginResize),
        //     出去是**按 B**; 界面被收起 / 手柄断线时也会自己结掉(见上面和 EndResize)。
        if (Resizing)
        {
            TickResize(st, btn);
            if ((pressed & GamepadInput.B) != 0) EndResize();
            _prevBtn = btn; _prevLT = lt; _prevRT = rt; _prevL3 = l3; _prevR3 = r3;
            return;
        }

        // 3. LT: 焦点在候选区时是"候选左移", 在键盘区才是数字层
        //    (否则每按一次 LT 都会把焦点状态搞乱, 数字层也没法选键)
        //    F 占着屏幕时 LT 不切数字层 —— 录快捷键那一页会被切成九键, 键就没法按了。
        if (lt && !_prevLT && (Focus == FocusZone.CandRow || Focus == FocusZone.CandGrid))
        {
            MoveCand(DIR_LEFT);
            Dirty = true;
        }
        else if (lt && !_prevLT)
        {
            if (!QuickOpen)
            {
                // 【2.1.23: 从**全键盘**进数字层也走"左右缩窄"那一场】
                // 以前是全键盘"啪"一下换成九键那个方框 —— 因为"左右扩张"那套
                // 只挂在 L3 换布局上, 而按住 LT 是直接改 Layout 的, 没人演这场。
                // (换脸那场动画也不归它: ScreenId.WorthFx 认"是不是全键盘",
                //  两边一不一样就不演 —— 所以以前这一趟是**干巴巴地瞬闪**。)
                // 现在只要这一趟真的换了宽度(全键盘 640 <-> 数字层 360), 就交给
                // 和 L3 同一个 FlipLayout; 九键(360) <-> 数字层(360)宽度没变,
                // 照旧直接换 —— 那一趟本来就有换脸那场动画顶着。
                void GoNum()
                {
                    _beforeNum = Layout; Layout = Layout.NUM9; SyncIme();
                    _ltNum = true;
                    Dirty = true;
                }
                var flip = FlipLayout;
                if (flip == null || Layout != Layout.EN26) GoNum();
                else flip(GoNum);
            }
        }
        else if (!lt && _prevLT && _ltNum)
        {
            // 松开还原同理: 还回**全键盘**那一趟(360 -> 640)才是"左右扩张",
            // 还回九键(360 -> 360)直接换就行。
            void BackNum()
            {
                _ltNum = false;
                Layout = _beforeNum; SyncIme();
                Dirty = true;
            }
            var flip = FlipLayout;
            if (flip == null || _beforeNum != Layout.EN26) BackNum();
            else flip(BackNum);
        }

        // 4. 【2.3.3: R3 这一颗彻底空出来了】以前"按住 R3 = 调界面"(缩放 / 摆位),
        //    那一桩搬到设置 → 键盘 → 「调整窗口」里去了: 按住不放才能调, 一松手就退出,
        //    想一边看一边挪得一直绷着手指; 而且 R3 在摇杆底下, 左手正推着摇杆选键的
        //    时候够不着。现在进去就一直在那一档里, 按 B 才出来(见第 2.5 步 / TickResize)。
        //    R3 现在只剩和 L3 配成默认的唤出组合, 以及设置里可以把它自己定成唤出键。

        // 4.5 滑行输入: LB 按下开始画、松开算画完一道。
        //     排在左摇杆**之前**: HandleStick 要认得出"这一帧正在滑", 好把摇杆让给滑行。
        TickSlide(st, now);

        // 5. 左摇杆
        HandleStick(st.LX, st.LY, now);

        // 6. 右摇杆: 数字层转圈输 0, 其他层是标点轮盘
        HandleRightStick(st.RX, st.RY, now);

        // 7. 十字键
        HandleDPad(btn, now);

        // 8. 26 键: **按住 LB 就是按住 Shift**。松手就掉, 所以按住 LB 打数字键
        //    出来的是 @ # $ 那一档上档符号, 打字母是大写 —— 跟真键盘的 Shift 一样。
        //    别的布局下 LB 另有用途(九键是候选左移), 这里只在 26 键键盘区生效。
        //    必须排在按钮处理**之前**: 同一帧里 LB 和 A 一起到的时候,
        //    OnA 得先看到 Shift 已经按下, 否则第一下打出来的还是小写。
        bool wantShift = (btn & GamepadInput.LB) != 0
                         && Layout == Layout.EN26
                         && Focus == FocusZone.Keyboard && !Punct;
        if (wantShift != ShiftHeld) { ShiftHeld = wantShift; Dirty = true; }

        // 9. 按钮: X / RB / A 走按住连发, 其余按下触发一次
        //    窗口F 开着的时候 A 是"选中这一格", 不是打字 —— 先让 F 截住。
        // 窗口F 开着的时候 A 是"选中这一格", 不是打字。
        // (设置面和 F 不会同时在 —— 见 SettingsAllowed, 所以这里不用挑判据。)
        if (QuickMode) HoldQuickA(btn, now);
        else HoldRepeatA(btn, now);
        if (!QuickMode) TickQuickHold(btn, now);
        if ((pressed & GamepadInput.B) != 0) OnB();
        // Y 有**五种**活(2.3.3), 判据只有一条: 这一下能不能切分(见 TickLongY 上面的说明)。
        //   能切分 -> 按下就切, 不等松手(打字要的是快), 也不进长按计时;
        //   不能   -> 数字层出 0 / 窗口F 里解绑(这两桩也是按下就走);
        //   其余   -> 先攒着: 松手 = 换九键 / 全键盘, 按住够半秒 = 进 / 出设置。
        // 录快捷键那一页 Y 是**保存**(上面那个"保存"按钮撤了(2.1.1), 保存归这一颗)。
        if ((pressed & GamepadInput.Y) != 0)
        {
            if (QEdit == 1) QuickEndHotkey(true);
            // 窗口F 绑数字那一层: **Y = 解除绑定**(2.3.3 起, 以前是 L3)。
            // 不用再在那一层里摆一颗"解除"的按钮给你挪过去再按 A, 一下就完事。
            else if (QuickMode)
            {
                if (QEdit < 0) { QuickUI?.Y(); Dirty = true; }
            }
            // 数字层: **Y = 0**(2.3.3 起, 以前是 L3)。右摇杆转圈出 0 那套照样能用,
            // 这一颗是给"手指不想离开键区"的时候留的。
            // 按下就走, 不等松手 —— 出个数字不该等; 也标上 _ySplit 免得这一下
            // 被长按计时接走(数字层本来就进不去设置, 标上是口径一致)。
            else if (Layout == Layout.NUM9)
            {
                TextOutput.Digit(0);
                _ySplit = true;
                Dirty = true;
            }
            else if (YCanSplit()) { _ySplit = true; OnY(); }
            else _yDefer = true;
        }
        if ((pressed & GamepadInput.LB) != 0) OnShoulder(-1);

        // 录快捷键那一页: X = 删掉刚记的最后一个键, RB = 记一个空格。
        // 这两个**都不许真发出去** —— 那一页的规矩就是"只记, 不发"。
        if (QEdit == 1)
        {
            if ((pressed & GamepadInput.X) != 0) QuickDropToken();
            if ((pressed & GamepadInput.RB) != 0) QuickAddToken("space");
        }
        else if (QuickOpen)
        {
            // F 的那一摞(列表 / 深层 / 绑数字): RB = 下一页, 和 LB 那边配一对。
            // **得在这儿单独接一趟** —— 下面那条 HoldRepeatRb 只在"没开 F"的时候跑,
            // 光靠它的话 F 里就只有 LB 能翻页, RB 按下去一点反应都没有。
            if ((pressed & GamepadInput.RB) != 0) OnShoulder(+1);
        }
        else
        {
            // X 是退格, RB 是发拼音 / 空格(见 HoldRepeatRb)
            HoldRepeat((btn & GamepadInput.X) != 0, ref _xHeld, ref _xNext, now, OnX);
            HoldRepeatRb(btn, now);
        }

        // 10. 【2.3.3: L3 这一颗彻底空出来了】它以前管三桩活, 一桩一桩都搬走了:
        //     换九键 / 全键盘 -> Y 短按(2.3.2); 数字层出 0 -> Y(2.3.3);
        //     窗口F 里解除绑定 -> Y(2.3.3)。那三桩全在摇杆底下这颗键上, 左手正推着
        //     摇杆选键的时候够不着, 而且"按一下到底是出 0 还是换布局"全看在哪一层。
        //     现在 L3 只剩和 R3 配成默认的唤出组合 —— 它也是设置里能挑的唤出键之一。
        //     (这一段不接任何东西: 唤出那一串由第 1 步接, 那儿按下期间整帧不往下传。)

        // 11. RT = 回车。以前是"短按候选右移 + 长按回车", 但候选右移有十字键和 RB,
        //     RT 单独留着反而没活干 —— 现在干脆一下就是回车, 右下角那根长按进度条
        //     也跟着一起删了。
        //     回车不隐藏界面: 显隐只由 L3+R3 控制, 发完还想接着打。
        //     **F 占着屏幕时按 RT 不能回车** —— 录快捷键那会儿一个键都不许往外发,
        //     真按个回车下去, 游戏里那句没打完的话就被发出去了。
        // F 开着就不往外发回车。
        if (rt && !_prevRT && !QuickOpen)
        {
            SentPinyin = "";
            TextOutput.Enter();
            Dirty = true;
        }

        // 12. 空闲自动隐藏(默认关: 显隐只由 L3+R3 控制)
        if (Visible && !NoAutoHide && (now - _lastActivity).TotalMilliseconds > IDLE_HIDE_MS)
        {
            Visible = false;
            Dirty = true;
        }

        _prevBtn = btn; _prevLT = lt; _prevRT = rt; _prevL3 = l3; _prevR3 = r3;
    }

    /// <summary>
    /// B 的长按(只有设置页认): 从两层深的菜单里一路退回首页, 不用一层层按。
    /// 按下那一下的"短按(退一层)"照旧先发生 —— 响应快比什么都重要;
    /// 长按住到点再补一次。已经在首页了就不动: 长按不该顺手把设置整个关掉。
    /// </summary>
    void TickLongB(ushort btn, DateTime now)
    {
        bool down = (btn & GamepadInput.B) != 0;
        if (!down)
        {
            // 松手才补那一下"点一下退一层"。按下那会儿不动是为了给长按留位置 ——
            // 以前按下即退一层, 长按需完落在那个退场动画上(见 SettingsBack 里的
            // _setAnim 短路), 常常被吃掉, 看着就成了"长按也是一层层退"。
            if (_bWasDown && _bDeferSet && !_bLongFired
                && SettingsBack?.Invoke(false) == true) Dirty = true;
            _bWasDown = false;
            _bLongFired = false;
            _bDeferSet = false;
            return;
        }
        if (!_bWasDown) { _bWasDown = true; _bDownAt = now; return; }
        if (_bLongFired || !Settings) return;
        if ((now - _bDownAt).TotalMilliseconds < LONG_B_MS) return;
        _bLongFired = true;
        _bDeferSet = false;
        // 长按: 一路退回首页, 中间不停(不管处在哪一层)。
        if (SettingsBack?.Invoke(true) == true) Dirty = true;
    }

    /// <summary>设置页里"B 按下"这一下先攒着, 等松手 / 长按再决定是退一层还是回首页。</summary>
    bool _bDeferSet;

    /// <summary>
    /// Y 的长按(**只在没有拼音可切、且界面亮着的时候**认): 按够 <see cref="Y_HOLD_MS"/> 进 / 出设置。
    /// 按下那一下不动 —— 松手才补"换九键 / 全键盘", 和 B 那套一个道理。
    /// <para>为什么不在按下那一刻就决定: 短按(换布局)和长按(进设置)是同一颗键,
    /// 谁先谁后只能等时间说话。好在换布局不是要紧事, 晚 1 秒才发生没人察觉;
    /// 而切分是要紧事 —— 所以**能切分的时候压根不走这条计时**(见 <see cref="_ySplit"/>)。</para>
    /// <para>【2.4.6】**界面收起时这一桩不认**: 它是"进 / 出设置", 不是唤醒键。
    /// 以前收起时按够半秒照样触发 <see cref="ToggleSettings"/> —— 而 App 那边窗口
    /// 不可见时走的是 <c>OpenSettings()</c>: 先把界面唤出来再翻面, 于是"除了唤醒键,
    /// 长按 Y 也能把软件唤醒出来"(2.4.6 用户报的 bug)。唤醒只归唤醒键那一桩
    /// (以及托盘菜单 / 第二个实例的消息, 那都是明示的点)。</para>
    /// </summary>
    void TickLongY(ushort btn, DateTime now)
    {
        // 收起时清跟踪状态再走: 不清的话"亮着时按下 Y、中途界面被收起(比如焦点
        // 被抢自动收)"会让 _yDownAt 变成陈旧时间, 下次亮起来一按就误判长按。
        if (!Visible)
        {
            _yWasDown = false; _yLongFired = false; _ySplit = false; _yDefer = false;
            return;
        }
        bool down = (btn & GamepadInput.Y) != 0;
        if (!down)
        {
            // 松手才补那一下"换九键 / 全键盘"(短按)。长按已经用掉的话就不补了 ——
            // 按住半秒进完设置, 松手又顺手把布局换了, 那是两件事混在一起。
            if (_yWasDown && _yDefer && !_yLongFired) SwapLayout();
            _yWasDown = false; _yLongFired = false; _ySplit = false; _yDefer = false;
            return;
        }
        if (!_yWasDown)
        {
            _yWasDown = true; _yDownAt = now;
            // 按下这一帧的"是切分还是攒着"由第 9 步填(_ySplit / _yDefer), 那儿排在这后面。
            return;
        }
        if (_yLongFired || _ySplit) return;          // 这一下是切分: 不参与长按
        if ((now - _yDownAt).TotalMilliseconds < Y_HOLD_MS) return;
        _yLongFired = true;
        _yDefer = false;
        // 【只有九键首页 / 全键盘这两处翻得过去】和以前 R3 短按那条规矩一致 ——
        // 进得去的界面都是没有上一层的, 不用回答"退出来退到哪儿"。
        if (SettingsAllowed) { ToggleSettings?.Invoke(); Dirty = true; }
    }

    /// <summary>
    /// 九键 ↔ 全键盘。**换布局的唯一入口** —— 以前挂在 L3 松手上(摇杆底下那颗,
    /// 手正推着摇杆选键的时候够不着), 2.3.2 起归 Y 短按。
    /// <para>换完顺带把系统输入法的中 / 英调到位: 九键要中文态才出汉字,
    /// 全键盘要英文态才是纯字母。</para>
    /// </summary>
    void SwapLayout()
    {
        // 数字九键那一层是"按住 LT 临时切过去的", 它没有自己的换布局;
        // 窗口F / 设置占着屏幕的时候换了当场看不见, 退出来才发现变了, 看着像随机出错。
        if (Layout == Layout.NUM9 || _swapping || QuickMode || Settings) return;
        void Swap()
        {
            Layout = Layout == Layout.CN9 ? Layout.EN26 : Layout.CN9;
            ClearInput();
            SyncIme();
            Dirty = true;
        }
        var flip = FlipLayout;
        if (flip == null) Swap();
        else
        {
            // 换布局这件事推到动画演到中点再做; 那之前界面还是旧布局, 才压得下去
            _swapping = true;
            flip(() => { Swap(); _swapping = false; });
        }
    }

    // ================= 调窗口大小 / 摆位 =================
    /// <summary>
    /// 进"调窗口"这一档(设置 → 键盘 → 「调整窗口」)。进去之后它就一直在,
    /// 不用一直摁着某颗键 —— 以前"按住 R3"那套得绷着手指, 想边看边挪很别扭。
    /// </summary>
    public void BeginResize()
    {
        if (Resizing) return;
        Resizing = true;
        Dirty = true;
    }

    /// <summary>
    /// 出这一档: **按 B**、或者界面被收起 / 手柄断线(见 Poll 里那两处)。
    /// 出去才落盘 —— 一帧写一次文件太夸张。
    /// </summary>
    public void EndResize()
    {
        if (!Resizing) return;
        Resizing = false;
        LayoutSaved?.Invoke(Scale, OffsetX, OffsetY);
        Dirty = true;
    }

    /// <summary>
    /// 这一档里每帧要做的事: 十字键上下缩放、左摇杆挪位置。
    /// 摆位按**摇杆推的幅度**走(满推一帧 14px, 约 840 px/s), 死区 0.3 以下不算 ——
    /// 摇杆物理回中总有点残余, 不卡死区的话松手了界面还在慢慢漂。
    /// </summary>
    void TickResize(PadState st, ushort btn)
    {
        if ((btn & GamepadInput.DPAD_UP) != 0)
            Scale = Math.Clamp(Scale + SCALE_RATE, SCALE_MIN, SCALE_MAX);
        else if ((btn & GamepadInput.DPAD_DOWN) != 0)
            Scale = Math.Clamp(Scale - SCALE_RATE, SCALE_MIN, SCALE_MAX);

        double lx = st.LX / 32768.0, ly = st.LY / 32768.0;
        if (Math.Abs(lx) > 0.3) OffsetX += lx * MOVE_RATE;
        if (Math.Abs(ly) > 0.3) OffsetY -= ly * MOVE_RATE;   // 上推 = 往上挪
        Dirty = true;
    }

    /// <summary>上一帧摇杆是不是"推着"的。用来认**松手那一帧**(见 HandleStick)。</summary>
    bool _stickWasActive;

    // ================= 左摇杆 =================
    void HandleStick(short rawX, short rawY, DateTime now)
    {
        if (Resizing) return;
        // 【摇杆真值每一帧都记, 不管走哪条分支】ClearInput 要问一句"手还推着吗"
        // (见那儿的光标归位)。滑行 / 窗口F / 设置那几条分支都会提前 return,
        // 不在这儿记的话那些窗口里摇杆的真值就冻住了 —— 关 F 那一刻拿到的是
        // 进 F 之前的旧值, 会误判成"没推", 光标照样闪一下。
        _stickLX = rawX; _stickLY = rawY;
        // 设置那一面: 摇杆是"在九宫格里选格" —— 和九键选键同一套(推方向选格、回中落回
        // 正中)。以前这一面摇杆干脆不接管, 结果九宫格里找一格只能拿十字键一格格挪。
        if (Settings)
        {
            SettingsStick?.Invoke(rawX / 32768.0, rawY / 32768.0);
            return;
        }

        // 滑行输入接管中: 这时候左摇杆是**一支笔**, 不是选键的光标。
        // 一定要在下面那些分支之前拦掉 —— 否则你画一趟不仅出了滑行序列,
        // 九宫格那颗焦点也被拖着走了一圈, 松手时停在最后一格上,
        // 接着按 A 会莫名其妙多打一个键。
        if (_slideHeld)
        {
            // 摇杆线照样画出来 —— 你在地上画的这一笔得看得见去向,
            // 否则只剩下"影子"那串字在跳, 眼睛跟不上手。
            double sx = rawX / 32768.0, sy = rawY / 32768.0;
            bool act2 = Math.Sqrt(sx * sx + sy * sy) >= DZ_N;
            //
            // 【滑行里"松手回中"也得认边沿, 而且得在这儿认】
            // 以前这一段直接 return, 连 _stickWasActive 都没碰 —— 于是从按下 LB
            // 那一刻起, "上一帧摇杆是不是推着的"就冻在进滑行之前的旧值上了。
            // 后果有两层:
            //   ① 按住 LB 期间松开摇杆, 九宫格那颗焦点**不回中** —— 它一直亮在进
            //      滑行前推着的那个方向上, 而手早就松了(用户原话: "松开摇杆就不会
            //      自动回中了");
            //   ② 连"松 LB 那一帧"的回中也跟着错: 那一帧走的是下面那条老路, 认的
            //      是这个**过期**的旧状态, 于是该回中的时候不回、不该回的时候回。
            // 边沿必须**每一帧**都在记, 不管是哪条分支把它 return 掉的 —— 这就是
            // 下面那条老路里那句注释说的"认电平的话十字键等于白按"的同一件事。
            bool rel2 = _stickWasActive && !act2;
            _stickWasActive = act2;
            if (act2) Nav = NavMode.Stick;
            //
            // 【死区里一律归零】以前这一层直接把摇杆**原始值**抄给 StickX / Y,
            // 死区那道闸在这儿是跳过去的 —— 摇杆物理回中总有一点残余(零点几毫米),
            // 平时被死区抹成 0, 滑行里却当成真位移画了出来: 笔尖停在偏出中心一点的
            // 地方, 看着就像"中心位置被挪过"。
            StickX = act2 ? sx : 0;
            StickY = act2 ? sy : 0;
            Slide.Update(StickX, StickY, now);
            // 松手就落回正中那格 —— 和九键"松手回 5"同一个规矩: 手一松界面回到一个
            // 确定的地方。十字键选的格不许被拽回去(见下面那条老路里的同一条判据)。
            if (rel2 && Nav == NavMode.Stick && Focus == FocusZone.Keyboard) Key9 = 5;
            Dirty = true;
            return;
        }

        // 窗口F 那一摞: 摇杆就是"在这一层的九宫格里选格", **和九键选键一模一样** ——
        // 推着就跟着走, 手一松回中就落回正中。列表层、深层、绑数字层全是这一套。
        // 录快捷键那一页(QEdit >= 0)不归它管 —— 那一页摇杆要在全键盘上挪光标,
        // 下面那条老路本来就是干这个的。
        if (QuickMode && QEdit < 0)
        {
            QuickUI?.Stick(rawX / 32768.0, rawY / 32768.0);
            return;
        }

        double nx = rawX / 32768.0, ny = rawY / 32768.0;
        double r = Math.Sqrt(nx * nx + ny * ny);
        bool active = r >= DZ_N;
        // **松手那一帧**。摇杆是**每一帧**都走到这儿的(不是只在动的时候),
        // 所以"回中"这种事必须认**边沿**: 认电平的话, 用十字键把光标挪到别的
        // 格上, 下一帧就被拽回正中了 —— 十字键等于白按。
        bool released = _stickWasActive && !active;
        _stickWasActive = active;

        if (active) Nav = NavMode.Stick;
        StickX = active ? nx : 0;
        StickY = active ? ny : 0;
        Dirty = true;

        if (Focus == FocusZone.Keyboard)
        {
            if (Layout == Layout.EN26)
            {
                if (active) TryRepeat(DirOf(nx, ny), now, Move26);
                else _repeatDir = DIR_NONE;
            }
            // 关键: 十字键模式下摇杆回中不能把光标拽回 5, 否则十字键选完就被重置
            else if (Nav == NavMode.Stick)
            {
                int k = active ? PointTo9(nx, ny) : 5;
                if (k != Key9) Key9 = k;
            }
        }
        else if (Punct && Focus == FocusZone.CandGrid)
        {
            // 符号网格: 摇杆**直接落点** —— 推到哪一格就是哪一格(见 PointToGrid)。
            // 以前在这儿摇杆是"当方向键连发", 一格一格挪过去; 二十几个符号
            // 那么挪太慢, 而且它跟九键上"推哪儿选哪儿"是两套手感。
            // 【正中间那格是"标签格"】它装的是分类名, 按 A 不会打出东西; 光标
            // 停在它上面是**常态** —— 进面板 / 换了类 / 松手, 三处都落在它上。
            if (active)
            {
                int k = PointToGrid(nx, ny, GridColsNow, GridRowsNow);
                if (k >= 0 && k < GridCount() && k != GridIdx)
                {
                    GridIdx = k;
                    Dirty = true;
                }
            }
            else if (released && GridIdx != GridCenter)
            {
                // 松手**回中**: 落回正中间那个标签格。和九键"松手回 5"同一个
                // 规矩 —— 手一松界面就回到一个确定的地方, 不用记刚才停在哪儿。
                GridIdx = GridCenter;
                Dirty = true;
            }
        }
        else
        {
            if (active) TryRepeat(DirOf(nx, ny), now, MoveCand);
            else _repeatDir = DIR_NONE;
        }
    }

    /// <summary>
    /// 摇杆点位 -> 3×3 里的哪一格(1..9, 左上起、行优先)。只落在 3×3 内: 数字层的 0
    /// 走 L3, 不让摇杆"推多远"来决定, 否则选 8 时稍用力就变 0, 给最高频的操作加了个
    /// 隐形门槛。
    /// <para>设置页的九宫格也用这一个(见 <see cref="SettingsStick"/>) —— 两处必须是
    /// 同一套手感, 不然九宫格里找一格和键盘上选键就成了两回事。</para>
    /// </summary>
    public static int PointTo9(double nx, double ny)
    {
        int col = Math.Clamp((int)Math.Floor((nx + 1) / 2 * 3), 0, 2);
        int row = Math.Clamp((int)Math.Floor((1 - ny) / 2 * 3), 0, 2);
        return row * 3 + col + 1;
    }

    /// <summary>
    /// 摇杆的**圆形**范围 -> 正方形。手柄摇杆推到底是圆的(|(x,y)| &lt;= 1),
    /// 45 度推满每个轴只到 0.707 —— 摆 5×5 要够到角上那格得两个轴同时超过 0.6,
    /// 那块地方只有普通格的七分之一, 表现就是"明明推到角上了, 光标却进不去"。
    /// 按方向等比撑到方阵边界之后, 45 度推满就是 (1,1), 四角随推随到。
    /// (九键那 3×3 的阈值只有 1/3, 斜推本来就够得着, 所以 PointTo9 不动它 ——
    /// 那儿改了会把手感一起改掉。)
    /// </summary>
    public static void Square(double nx, double ny, out double sx, out double sy)
    {
        double r = Math.Sqrt(nx * nx + ny * ny);
        double m = Math.Max(Math.Abs(nx), Math.Abs(ny));
        if (r < 1e-9 || m < 1e-9) { sx = 0; sy = 0; return; }
        double k = r / m;                       // 圆 -> 方的拉伸比(1 .. 1.414)
        sx = Math.Clamp(nx * k, -1, 1);
        sy = Math.Clamp(ny * k, -1, 1);
    }

    /// <summary>
    /// 摇杆点位 -> cols×rows 网格里的第几格(0 起, 左上起、行优先)。
    /// 就是 PointTo9 把 3 换成参数, 另外先走一遍方形映射(见 <see cref="Square"/>) ——
    /// 于是 5×5 / 5×8 这种密网格也推得到每一个格子, 四角不必"斜着使劲"。
    /// </summary>
    public static int PointToGrid(double nx, double ny, int cols, int rows)
    {
        Square(nx, ny, out var sx, out var sy);
        int col = Math.Clamp((int)Math.Floor((sx + 1) / 2 * cols), 0, cols - 1);
        int row = Math.Clamp((int)Math.Floor((1 - sy) / 2 * rows), 0, rows - 1);
        return row * cols + col;
    }

    static int DirOf(double nx, double ny) =>
        Math.Abs(nx) > Math.Abs(ny)
            ? (nx > 0 ? DIR_RIGHT : DIR_LEFT)
            : (ny > 0 ? DIR_UP : DIR_DOWN);

    void TryRepeat(int dir, DateTime now, Action<int> move)
    {
        if (dir == DIR_NONE) return;
        if (dir != _repeatDir)
        {
            _repeatDir = dir;
            _repeatNext = now.AddMilliseconds(REPEAT_DELAY);
            move(dir);
            return;
        }
        if (now >= _repeatNext)
        {
            move(dir);
            _repeatNext = now.AddMilliseconds(REPEAT_RATE);
        }
    }

    /// <summary>按住不放: 立即触发一次, 停 REPEAT_DELAY 后按 REPEAT_RATE 连发。</summary>
    void HoldRepeat(bool held, ref bool state, ref DateTime next, DateTime now, Action act)
    {
        if (!held) { state = false; return; }
        if (!state) { state = true; next = now.AddMilliseconds(REPEAT_DELAY); act(); return; }
        if (now >= next) { act(); next = now.AddMilliseconds(REPEAT_RATE); }
    }

    /// <summary>
    /// RB: 有待发的拼音就把它发出去(点按), 没有就是空格(可长按连发)。
    /// 这样"发出拼音 -> 再按一下上屏第一个候选"是同一个键连着按两下, 手不用换位置。
    /// </summary>
    void HoldRepeatRb(ushort btn, DateTime now)
    {
        if ((btn & GamepadInput.RB) == 0) { _rbHeld = false; return; }

        // 设置页: RB = 下一页(LB 那边走 OnShoulder)。以前 RB 整个被下面"发拼音 /
        // 空格"那一段吃掉, 于是只有 LB 能翻页。不连发 —— 按住会一路翻过去。
        // 窗口F 那一摞也一样(五页翻着找那一条)。
        if (Settings || QuickOpen)
        {
            if (!_rbHeld) { _rbHeld = true; OnShoulder(+1); }
            return;
        }

        // 符号面板开着(展开成网格时): RB = 下一类符号。不连发(按住会一路翻过去),
        // 也不是空格 —— 单行符号框里 RB 还是空格, 那边才需要它。
        // 设置页翻页: 比符号面板优先。RB 平时是"发拼音 / 空格", 在设置页里没有拼音
        // 可发, 让它去当"下一页"正好 —— 但 RB 是走 HoldRepeatRb 那条路的(见那里),
        // 那边必须同样认这一档, 否则就出现"只有 LB 能翻页, RB 没反应"。
        if (Punct && Focus == FocusZone.CandGrid)
        {
            // 【2.1.10: 走 OnShoulder, 别再抄近路】OnShoulder 头一件事就是
            // 把框外那块 RB▶ 小牌踢一脚(PageFx)。以前这里直接调
            // NextPunctCategory, 于是符号页里按 RB 类照换、**右边那块牌不跳**,
            // 看着像 RB 失灵 —— 其实是漏了一次通知。
            if (!_rbHeld) { _rbHeld = true; OnShoulder(+1); }
            return;
        }

        if (!_rbHeld)
        {
            _rbHeld = true;
            _rbSpace = RbIsSpace();          // 按下瞬间定死这次按住是干嘛的
            _rbNext = now.AddMilliseconds(REPEAT_DELAY);
            // 空格=上屏, 拼音串到此结束。整串都是你自己定的 -> 该记(见 FinishSent)
            if (_rbSpace) FinishSent(false);
            else SendSelected();                                     // 发出光标选中的那条拼音
            Dirty = true;
            return;
        }
        if (!_rbSpace) return;               // 发拼音不连发, 否则一按就飞过去
        if (now >= _rbNext)
        {
            FinishSent(false);
            _rbNext = now.AddMilliseconds(REPEAT_RATE);
            Dirty = true;
        }
    }

    /// <summary>
    /// 是不是"系统输入法的候选窗正开着": 拼音已经发出去了, 而且这会儿没有正在拼的数字。
    /// 这时候十字键就变成键盘方向键(见 DpadMove)。
    /// </summary>
    public bool ImePicking =>
        Focus == FocusZone.Keyboard && Digits.Length == 0 && SentPinyin.Length > 0;

    /// <summary>把光标选中的那条拼音发给输入法(网格里就是网格光标那一条)。</summary>
    void SendSelected()
    {
        if (Segs.Count == 0) return;
        SendPinyin(Segs[SegIdxNow]);
    }

    /// <summary>
    /// A: 数字层 / 26 键字母 / 标点候选框里长按连发, 一秒 2 次。
    /// 中文九键的拼音键故意不连发——连按 2 在 T9 里是"下一个字母", 不是"再输一个 2"。
    /// </summary>
    void HoldRepeatA(ushort btn, DateTime now)
    {
        if (Resizing) { _aHeld = false; return; }
        if ((btn & GamepadInput.A) == 0)
        {
            if (_aHeld) ReleaseQuickHold();     // 松手: 短按的那个数字该补发了
            _aHeld = false;
            return;
        }
        if (!_aHeld) MarkPressDown(now);        // 按下: 长按判定从这一刻开始计时

        // 设置页: A 是"确认这一项"。不连发 —— 连着按会把单选/按钮点好几次。
        if (Settings)
        {
            if (!_aHeld) { _aHeld = true; SettingsPick?.Invoke(); Dirty = true; }
            return;
        }

        if (!_aHeld)
        {
            _aHeld = true;
            _aRepeat = ACanRepeat();
            _aNext = now.AddMilliseconds(SLOW_DELAY);
            OnA();
            return;
        }
        if (!_aRepeat || now < _aNext) return;

        // 每次都重新判断: 比如候选词上屏后焦点会跳回键盘区, 语境变了就停
        if (!ACanRepeat()) { _aRepeat = false; return; }
        OnA();
        _aNext = now.AddMilliseconds(SLOW_RATE);
    }

    /// <summary>
    /// 窗口F 里的 A。**列表层要分短按和长按**, 所以按下先攒着:
    ///   松手(短按)  -> 选中这一格(有东西就发出去, 空格子就浮出深层)
    ///   按够 450ms  -> **进深层**(改这一格)
    /// 先发再进深层就成了"发一条短语、顺手弹个编辑层", 那不是你要的 —— 所以只能二选一。
    /// 深层往里(编辑层 / 绑数字层)没有长按这一说, 按下去立刻响应。
    /// 录快捷键那一页 A 是"记下这一颗键"(Press26 里拦掉了往外发)。
    /// 一律**不连发**: 连着按会把同一格删两次 / 绑两次。
    /// </summary>
    void HoldQuickA(ushort btn, DateTime now)
    {
        bool down = (btn & GamepadInput.A) != 0;

        if (QEdit == 1)
        {
            if (down && !_aHeld) { _aHeld = true; Press26(); }
            else if (!down) _aHeld = false;
            return;
        }

        if (!down)
        {
            if (_qaWasDown && !_qaFired) QuickUI?.Pick();
            _qaWasDown = false;
            _qaFired = false;
            Dirty = true;
            return;
        }
        if (!_qaWasDown)
        {
            _qaWasDown = true;
            _qaAt = now;
            if ((QuickUI?.Depth ?? 0) > 1) { _qaFired = true; QuickUI?.Pick(); }
            Dirty = true;
            return;
        }
        if (_qaFired) return;
        if ((now - _qaAt).TotalMilliseconds < QUICK_HOLD_MS) return;
        _qaFired = true;
        QuickUI?.Deep();
        Dirty = true;
    }

    bool ACanRepeat()
    {
        if (Punct) return Focus != FocusZone.Keyboard;   // 标点框里连着输同一个符号
        if (Focus != FocusZone.Keyboard) return false;   // 文字候选上屏一次就清空了, 不连发
        // 数字层里这个数字绑了快捷短语: 连发必须停掉 —— 按住它是"要发短语",
        // 不是"要多打几个 3"。判定放在 TickQuickHold, 松手短按才补一个数字。
        if (Layout == Layout.NUM9) return QuickStore.ByNum(Key9) == null;
        // 26 键: 字符 / 空格 连发; 退格和方向键也连发(删一串、挪光标都要按住);
        // 大小写切换、Shift、修饰键、标点是开面板, 都不能连
        if (Layout == Layout.EN26)
        {
            var k = Keyboard26.At(Row26, Col26);
            if (k.Kind is KKind.Char or KKind.Space) return true;
            if (k.Kind == KKind.Vk)
                return k.Vk is TextOutput.VK_BACK or TextOutput.VK_LEFT
                             or TextOutput.VK_RIGHT or TextOutput.VK_UP
                             or TextOutput.VK_DOWN;
            return false;
        }
        return false;
    }

    bool RbIsSpace()
    {
        // 九键: 还有没发出去的拼音, RB 就是"把它发出去" —— 发完 Digits 就空了,
        // 再按一下 RB 才是空格, 正好是"上屏系统输入法第一个候选", 连着按很顺手
        if (Digits.Length > 0 && Segs.Count > 0) return false;
        if (Layout == Layout.EN26) return true;   // 英文模式 RB 恒为空格
        return CandRowCount() == 0;               // 九键: 只有候选全空才当空格
    }

    // ================= 滑行输入 =================


    /// <summary>
    /// 滑行输入的门: 什么时候能开始滑、什么时候算画完一道。
    /// 认准九键中文 + 界面在前 + 没翻到设置页 + F 没占着屏幕。
    /// 26 键下 LB 是 Shift、符号面板里 LB 是换类, 一概不跟它们抢。
    /// </summary>
    void TickSlide(PadState st, DateTime now)
    {

        bool lb = (st.Buttons & GamepadInput.LB) != 0;
        // !Punct **必须有**：符号面板开着的时候 LB 是"换分类"（RB 也是），
        // 漏了这一条的话，你在"更多标点"里按 LB 翻页会**同时**起一次滑行 ——
        // 界面那边跟着把拼音行让出一条缝、又把"滑行"牌子挂出来，整页抖一下，
        // 看着就像翻页闪了一下；按 RB 翻页就不会（它不碰滑行）。2.0.0-beta2 实测出来的。
        bool can = SlideEnabled && Visible && st.Connected
                   && Layout == Layout.CN9
                   && !Settings && !Resizing && !QuickMode && !Punct;
        if ((lb && can) == _slideHeld) return;

        _slideHeld = lb && can;
        if (_slideHeld)
        {
            Slide.Begin(now);
            Slide.OnCommit = OnSlideKey;
        }
        else
        {
            Slide.Finish(now);
            Slide.OnCommit = SlideTracker.Noop;
        }
        Dirty = true;
    }

    /// <summary>
    /// 打字模式下摇杆在一个方向停稳了: 那个数字**当场进拼音**, 不等松手。
    /// <para>好处是手还悬在空中的时候候选栏已经在跟着变了 —— 滑到一半发现拼错了
    /// 抬手就能看见, 而不是松手之后才看到一串不对的东西。</para>
    /// <para>滑错了怎么办? 右手 X 退格一个数字一个数字地删, 和原来打错一模一样。
    /// 这只是"换个办法输数字", 拼音、候选、RB 上屏整套都还是原来那套。</para>
    /// </summary>
    void OnSlideKey(int k)
    {
        // 滑的过程中被切成别的布局了(比如同时按了 L3): 剩下的方向就不该再打字
        if (Layout != Layout.CN9) return;
        if (k < 1 || k > 9) return;
        if (Digits.Length >= 16) return;           // 和按 A 那条路同一个上限
        Digits += (char)('0' + k);
        RefreshCands();
        Dirty = true;
    }

    // ================= 右摇杆 =================
    void HandleRightStick(short rawX, short rawY, DateTime now)
    {
        // 设置页里不出标点层: 那一面在选设置项, 九键翻成标点只会挡住。
        // F 占着屏幕时也一样 —— 标点会往游戏里输符号, 那会儿正在记快捷键, 一个键都不许发。
        if (Settings || QuickMode) { Punct9Off(); return; }

        // 【2.1.17: 展开的符号网格开着的时候, 标点轮盘让位给网格】
        // 网格里右摇杆干的是"推到哪一格就选哪一格"(见 HandleStick 里 CandGrid 那一支),
        // 可轮盘这一支照样每帧在算: 手推着走, 半径一旦掉到 PUNCT9_MIN_R(0.35)以下就把
        // 标点九键层关一回, 推出来再开 —— 一趟摇下来能开关十几回。而网格开着的时候
        // 九键那一面根本不露脸, 屏幕上什么都没变, 却给换屏管线一笔笔记了"换了一屏",
        // 每笔都演一场**换页动画** —— 用户原话: "进标点页面动右摇杆会有切换页面的
        // 动画, 但是页面没有切换"。
        // 更糟的是它还夹带两件真事: Punct9Off 会把 Layout 还原回 _beforePunct(从全键盘
        // 开的符号网格会被它切成全键盘 —— 那一次是真翻页), 又把 _punct9Fired 清掉
        // (在"更多"那格长推够久会把已经打开的网格再开一次, 光标被拽回正中间)。
        if (Punct && Focus == FocusZone.CandGrid) return;

        double nx = rawX / 32768.0, ny = rawY / 32768.0;
        double r = Math.Sqrt(nx * nx + ny * ny);
        Punct9Update(nx, ny, r, now);
    }

    /// <summary>
    /// 标点层: 推右摇杆 -> 九颗键翻成标点, 在某一格**长推**够久就出那个符号, 回中翻回去。
    /// 取格用的是 <see cref="PointTo9"/>, 和左摇杆选键同一套 —— 推哪儿亮哪儿,
    /// 不用另外记"轮盘是逆时针排的"这回事。
    /// </summary>
    void Punct9Update(double nx, double ny, double r, DateTime now)
    {
        if (r < PUNCT9_MIN_R) { Punct9Off(); return; }
        Punct9On();

        int sec = PointTo9(nx, ny);                  // 1..9
        if (sec != Punct9Sel)                        // 换格就重新计时, 防止划过就误输入
        {
            Punct9Sel = sec;
            _punct9At = now;
            _punct9Fired = false;
        }
        // 正中间那格是空的: 停多久都不出东西(那就是松手回中落的地方)
        if (sec == Punct9Blank) { Punct9Progress = 0; Dirty = true; return; }

        bool more = sec == Punct9More;
        int hold = more ? PUNCT9_MORE_MS : PUNCT9_HOLD_MS;
        Punct9Progress = Math.Min(1, (now - _punct9At).TotalMilliseconds / hold);

        if (!_punct9Fired && Punct9Progress >= 1)
        {
            _punct9Fired = true;
            if (more) OpenPunct();                   // 开面板只触发一次
            else
            {
                SentPinyin = "";                     // 出符号 = 这一句拼音到这里为止
                TextOutput.Text(PunctSymbols[sec - 1]);
                _punct9Next = now.AddMilliseconds(PUNCT9_REPEAT_MS);
            }
        }
        else if (_punct9Fired && !more && now >= _punct9Next)
        {
            // 一直停在这一格: 继续出同一个符号(比如连着打 …), 一秒 2 次
            SentPinyin = "";
            TextOutput.Text(PunctSymbols[sec - 1]);
            _punct9Next = now.AddMilliseconds(PUNCT9_REPEAT_MS);
        }
        Dirty = true;
    }

    /// <summary>
    /// 进标点层。**全键盘下要临时回到九键** —— 标点就摆在九颗键上, 26 键那七排
    /// 没地方摆。这一手跟按住 LT 进数字层是一个路子(记下原来哪一面, 退出时还回去)。
    /// </summary>
    void Punct9On()
    {
        if (Punct9) return;
        Punct9 = true;
        _beforePunct = Layout;
        if (Layout == Layout.EN26) { Layout = Layout.CN9; SyncIme(); }
        Dirty = true;
    }

    void Punct9Off()
    {
        if (!Punct9) return;
        Punct9 = false;
        Punct9Sel = 0;
        Punct9Progress = 0;
        _punct9Fired = false;
        if (Layout != _beforePunct) { Layout = _beforePunct; SyncIme(); }
        Dirty = true;
    }

    /// <summary>右下角"更多"扇区: 直接打开展开的标点候选框。
    /// 不再先停在单行——单行那 5 个轮盘上本来就有, 再走一遍单行纯属多一步。</summary>
    void OpenPunct()
    {
        Punct = true;
        GridPinyin = false;
        GridPage = 0;
        Focus = FocusZone.CandGrid;
        CandIdx = 0;
        GridIdx = GridCenter;      // 落在正中间那个"标签格"上(见 GridCenter)
        Dirty = true;
    }


    // ================= 十字键 =================
    void HandleDPad(ushort btn, DateTime now)
    {
        if (Resizing) return;            // 调界面时十字键只管缩放

        int dir = DIR_NONE;
        if ((btn & GamepadInput.DPAD_UP) != 0) dir = DIR_UP;
        else if ((btn & GamepadInput.DPAD_DOWN) != 0) dir = DIR_DOWN;
        else if ((btn & GamepadInput.DPAD_LEFT) != 0) dir = DIR_LEFT;
        else if ((btn & GamepadInput.DPAD_RIGHT) != 0) dir = DIR_RIGHT;

        if (dir == DIR_NONE) { _dpadDir = DIR_NONE; return; }

        // 窗口F 那一摞: 十字键和设置页一个规矩 —— 上下是 ±3(三列), 左右是 ±1。
        // 连发比挪光标慢一号(SET_REPEAT_*), 否则一按就划过好几格看不清。
        // 录快捷键那一页(QEdit >= 0)不归它管 —— 那一页十字键要在全键盘上挪光标。
        if (QuickMode && QEdit < 0)
        {
            int qd = dir == DIR_UP ? -1 : dir == DIR_DOWN ? 1 : 0;
            int qh = dir == DIR_LEFT ? -1 : dir == DIR_RIGHT ? 1 : 0;
            void Go() { if (qd != 0) QuickUI?.Nav(qd); else QuickUI?.NavH(qh); }
            if (dir != _dpadDir)
            {
                _dpadDir = dir;
                _dpadNext = now.AddMilliseconds(SET_REPEAT_DELAY);
                Go();
            }
            else if (now >= _dpadNext)
            {
                Go();
                _dpadNext = now.AddMilliseconds(SET_REPEAT_RATE);
            }
            Dirty = true;
            return;
        }

        // 翻到了设置那一面: 十字键是"选设置项", 和键盘方向键没关系了。
        // 连发比挪光标慢一号(SET_REPEAT_*), 否则一按就划过好几项看不清。
        // 上下 -> 组之间跳; 左右 -> 组内走(主题那八个色块)。
        if (Settings)
        {
            int d = dir == DIR_UP ? -1 : dir == DIR_DOWN ? 1 : 0;
            int h = dir == DIR_LEFT ? -1 : dir == DIR_RIGHT ? 1 : 0;
            if (dir != _dpadDir)
            {
                _dpadDir = dir;
                _dpadNext = now.AddMilliseconds(SET_REPEAT_DELAY);
                if (d != 0) SettingsNav?.Invoke(d); else SettingsNavH?.Invoke(h);
                Dirty = true;
            }
            else if (now >= _dpadNext)
            {
                if (d != 0) SettingsNav?.Invoke(d); else SettingsNavH?.Invoke(h);
                _dpadNext = now.AddMilliseconds(SET_REPEAT_RATE);
                Dirty = true;
            }
            return;
        }

        if (dir != _dpadDir)
        {
            _dpadDir = dir;
            _dpadNext = now.AddMilliseconds(REPEAT_DELAY);
            DpadMove(dir);
        }
        else if (now >= _dpadNext)
        {
            DpadMove(dir);
            _dpadNext = now.AddMilliseconds(REPEAT_RATE);
        }
    }

    /// <summary>发一个方向键。</summary>
    static void Arrow(int dir) => TextOutput.Arrow(dir switch
    {
        DIR_LEFT => TextOutput.VK_LEFT,
        DIR_RIGHT => TextOutput.VK_RIGHT,
        DIR_UP => TextOutput.VK_UP,
        _ => TextOutput.VK_DOWN,
    });

    /// <summary>
    /// 九键正拼着拼音、而且拼音行里真的有好几种切分可以挑 —— 只有这时候十字键才归拼音。
    /// 拼音发出去之后(键盘区、数字已清空)就不算了, 那时十字键回到方向键。
    /// </summary>
    public bool PinyinPicking =>
        Focus == FocusZone.Keyboard && !Punct && Digits.Length > 0 && Segs.Count > 1;

    /// <summary>
    /// 十字键的语义, 一句话: **只有挑拼音的时候才挑拼音, 其余一律是键盘方向键。**
    ///
    /// 以前九键拼数字时左右是换拼音切分、26 键时四个方向是挪光标, 结果打完一串字
    /// 想回头改中间某个字, 只能按住 X 一路退格删过去 —— 删多了还得重打。
    /// 现在除了"拼音行正摆着好几种切分"这一种情况, 上下左右都直接发方向键,
    /// 光标能顺着已经上屏的字随便挪, 挪到哪儿就改哪儿。
    /// 代价是 26 键只能靠摇杆选键 —— 键盘上本来也有方向键, 不吃亏。
    /// </summary>
    void DpadMove(int dir)
    {
        // 展开的拼音网格 / 符号面板 / 候选行: 十字键就是在这几格里挪
        if (Focus != FocusZone.Keyboard) { MoveCand(dir); Dirty = true; return; }

        // 拼音行有得挑: 左右换切分, 下 = 展开全部拼音
        if (PinyinPicking) { MovePinyinDpad(dir); Dirty = true; return; }

        Arrow(dir);
        Dirty = true;
    }

    void MovePinyinDpad(int dir)
    {
        // 下 = 一次看完所有切分。原来是"上", 但拼音行下面就是九宫格,
        // 顺着"往下看更多"的方向更顺手 —— 上在拼音行里没地方去, 空着。
        if (dir == DIR_DOWN) { OpenPinyinGrid(); return; }
        if (dir == DIR_UP) return;
        MovePinyin(dir);
    }

    void Move26(int dir)
    {
        switch (dir)
        {
            case DIR_UP: if (Row26 == 0) return; Row26--; break;
            case DIR_DOWN: if (Row26 >= Keyboard26.RowCount - 1) return; Row26++; break;
            case DIR_LEFT: if (Col26 == 0) return; Col26--; break;
            case DIR_RIGHT: if (Col26 >= Keyboard26.Len(Row26) - 1) return; Col26++; break;
        }
        Col26 = Math.Min(Col26, Keyboard26.Len(Row26) - 1);
    }

    /// <summary>一页里最后一行的起始格子下标。</summary>
    static int LastRowStart(int n, int cols)
    {
        int rows = Math.Max(1, (n + cols - 1) / cols);
        return (rows - 1) * cols;
    }

    void MoveCand(int dir)
    {
        if (Focus == FocusZone.CandRow)
        {
            int n = CandRowCount();
            if (dir == DIR_LEFT) { CandIdx = Math.Max(0, CandIdx - 1); return; }
            if (dir == DIR_RIGHT)
            {
                // 走到最后一个还要往右 -> 展开完整候选(并排在键盘右边, 不覆盖九键)
                if (CandIdx >= n - 1) { Focus = FocusZone.CandGrid; GridIdx = CandIdx; }
                else CandIdx++;
                return;
            }
            // 上下都回九键。原来"下"直接展开全部候选太突兀, 展开统一走"右越界"
            if (dir == DIR_UP || dir == DIR_DOWN) { Focus = FocusZone.Keyboard; return; }
        }
        else
        {
            int n = GridCount();
            int cols = GridColsNow;
            int col = GridIdx % cols, row = GridIdx / cols;
            int rows = Math.Max(1, (n + cols - 1) / cols);
            switch (dir)
            {
                case DIR_LEFT: col = Math.Max(0, col - 1); break;
                case DIR_RIGHT: if (GridIdx >= n - 1) return; col++; break;

                // 上下走到这一页的边上就翻页(只在拼音网格会分出多页)。
                // 翻过去仍停在同一列 —— 眼睛跟着那一列走, 不至于翻完找不到光标。
                case DIR_UP:
                    if (row > 0) { row--; break; }
                    if (GridPage <= 0) return;
                    GridPage--;
                    GridIdx = Math.Min(LastRowStart(GridCount(), cols) + col, GridCount() - 1);
                    Dirty = true;
                    return;

                case DIR_DOWN:
                    if (row < rows - 1) { row++; break; }
                    if (GridPage + 1 >= GridPageCount) return;
                    GridPage++;
                    GridIdx = Math.Min(col, GridCount() - 1);
                    Dirty = true;
                    return;
            }
            int idx = row * cols + col;
            GridIdx = idx >= n ? n - 1 : idx;
        }
    }

    /// <summary>拼音行: 十字键左右在同一数字串的多种切分间切换(chao / bian / chan-jian)。</summary>
    void MovePinyin(int dir)
    {
        if (Punct || Segs.Count <= 1) return;
        // LB/RB 只在"这一行看得见的几个"里转, 不再横向滚动 ——
        // 否则窗口跟着光标滑, 光标永远停不到最后一个, "再按一次 RB 展开全部"就永远触发不了。
        int fit = FitNow;
        if (PinyinIdx >= fit) PinyinIdx = fit - 1;
        if (dir == DIR_LEFT) PinyinIdx = (PinyinIdx - 1 + fit) % fit;
        else if (dir == DIR_RIGHT)
        {
            // 在这一行最后一个上还要往右 -> 展开全部切分。
            // 行里放不下的那些, 全在展开的网格里, 所以不会漏掉任何一种读音。
            if (PinyinIdx >= fit - 1 && Segs.Count > fit)
            {
                OpenPinyinGrid();
                return;
            }
            PinyinIdx = (PinyinIdx + 1) % fit;
        }
        else return;
        Dirty = true;
    }

    /// <summary>LT / RT: 焦点还在键盘区就先跳进候选行, 之后左右移动候选。</summary>
    void TriggerMoveCand(int dir)
    {
        if (CandRowCount() == 0) return;
        if (Focus == FocusZone.Keyboard) { Focus = FocusZone.CandRow; CandIdx = 0; Dirty = true; return; }
        MoveCand(dir);
        Dirty = true;
    }

    /// <summary>
    /// 符号面板换类。LB / RB 专职干这个 —— 面板开着的时候 RB 不再是"发送/空格",
    /// 因为这时候没有拼音可发、也没有候选要上屏, 拿它换类正好不浪费。
    /// </summary>
    public void NextPunctCategory(int delta)
    {
        if (!Punct) return;
        int n = PunctCats.Length;
        PunctCat = ((PunctCat + delta) % n + n) % n;
        GridIdx = GridCenter;      // 换完类光标回到正中间(那里正写着新的分类名)
        CandIdx = 0;
        Dirty = true;
    }

    void OnShoulder(int delta)
    {
        // 框外面那对 ◀LB / RB▶ 跟着跳一下(见 OverlayWindow.PageTurnFx)。
        // 放在最前面: "这一下翻的是谁的页"是下面的事, 而那对小牌该不该亮
        // (PageTurnable)跟归谁无关 —— 真没翻动时它本来就是收着的, 跳也看不见。
        PageFx?.Invoke(delta);

        // 设置页排在最前面: 和 B 那条是同一条规矩的两半 —— 眼睛看着哪一面,
        // 手就动哪一面。2.1.10 起设置和窗口F 不可能同时在(见 SettingsAllowed),
        // 这个顺序就是留个保险: 万一将来又有地方叠上去, 也不会退回"翻背后那摞"。
        // 设置页: LB / RB 是翻页(九宫格一页九个, 放不下就翻)。和"更多标点"一个路子。
        if (Settings) { SettingsPage?.Invoke(delta); return; }
        // 窗口F: LB / RB 是翻页(一页九格, 五页装 45 条)。和设置页同一套。
        if (QuickMode && QEdit < 0) { QuickUI?.Page(delta); return; }
        // 符号面板开着: LB / RB 是换类, 别的都不干(九键的"RB 发送"也要让路)
        if (Punct) { NextPunctCategory(delta); return; }
        // 26 键: LB 不再是"切大小写"了 —— 它现在就是 Shift(按住就打大写 / @ # $),
        // 状态在 Poll 里每帧跟着 LB 走。要锁大写请按键盘上的 Caps 键。
        // 九键: LB/RB 都不管换切分了 —— 十字键左右换, RB 去干"发送拼音 / 空格"。
    }

    // ================= 按键动作 =================
    void OnA()
    {
        // A 只负责"输入九宫格上光标所在的数字", 不做拼音确认。
        // 确认拼音用 RB —— 打完数字光标停在中间的 5 上, 拿 A 确认就会顺手多打一个 5,
        // 刚打好的拼音跟着就变了, 这是当初最别扭的地方。
        if (Focus == FocusZone.Keyboard)
        {
            if (Layout == Layout.CN9)
            {
                // 快捷那一格**没有字母**, 长按它才是开窗口F(见 TickQuickHold)。
                // 这里短按必须干脆不响 —— 摇杆松手自己就回到中间, 要是"回中 + A"
                // 也算一个键, 滑完习惯性按一下 A 就会多打一个数字, 刚拼好的拼音跟着就变了。
                // 空着比误触好 —— 这是当初把 1 号键留白时定下的规矩, 现在挪到中间那颗上;
                // 开了"1/5 互换"之后它又回到左上角, 判据也跟着走(见 DigitOfCell)。
                int d9 = DigitOfCell(Key9);
                if (d9 != 0 && Digits.Length < 16) { Digits += d9; RefreshCands(); }
            }
            else if (Layout == Layout.NUM9)
            {
                // 这个数字绑了快捷短语: 先压住不发, 等 TickQuickHold 判你是"点一下"
                // 还是"按住"。按住 → 发那条短语; 点一下 → ReleaseQuickHold 补一个数字。
                if (QuickStore.ByNum(Key9) != null)
                {
                    _numPending = Key9;
                    Dirty = true;
                    return;
                }
                TextOutput.Digit(Key9);
            }
            else
            {
                Press26();
            }
        }
        else Commit();
        Dirty = true;
    }

    /// <summary>
    /// 26 键全键盘按下一个键。按种类分流:
    /// 字符键走 HitChar(没粘修饰键就是 UNICODE 直投, 保证是纯字符、不惊动输入法);
    /// 功能键走虚拟键; Caps / Shift / Ctrl / Alt / Win 只改状态, 不往外发东西。
    /// </summary>
    void Press26()
    {
        var k = Keyboard26.At(Row26, Col26);

        // 录快捷键: **一个键都不往外发**, 只把键名记进 QBuf。
        // 否则你按 Alt 是想记下它, 系统却真的把 Alt 按下去了 —— 切走的可能是游戏。
        if (QEdit == 1)
        {
            QuickAddToken(TokenOf26(k));
            return;
        }

        switch (k.Kind)
        {
            case KKind.Char:
                TextOutput.HitChar(k.CharNow(ShiftOn, Caps),
                                   TextOutput.ModCtrl, TextOutput.ModAlt, TextOutput.ModWin);
                // Shift 粘住时是"一次性"的: 打完一个键就松, 跟真键盘按住 Shift 打一串
                // 不一样 —— 手柄上 LB 是按得住的, 那个才是"一串大写"的用法。
                ShiftLatch = false;
                break;

            case KKind.Vk:
                TextOutput.Hit(k.Vk, TextOutput.ModCtrl, TextOutput.ModAlt, TextOutput.ModWin);
                ShiftLatch = false;
                break;

            case KKind.Space:
                TextOutput.Space();
                ShiftLatch = false;
                break;

            case KKind.Caps:
                Caps = !Caps;
                break;

            case KKind.Shift:
                ShiftLatch = !ShiftLatch;
                break;

            case KKind.Mod:
                // 再按一下同一个修饰键 = 松手(免得粘住了没地方取消)
                if (k.Vk == TextOutput.VK_CONTROL) TextOutput.ModCtrl = !TextOutput.ModCtrl;
                else if (k.Vk == TextOutput.VK_MENU) TextOutput.ModAlt = !TextOutput.ModAlt;
                else if (k.Vk == TextOutput.VK_LWIN) TextOutput.ModWin = !TextOutput.ModWin;
                break;

            case KKind.Punct:
                // 符号面板和拼音网格**互斥**: 两个都开着的话, 界面画的是符号
                // (GridItems 里 Punct 优先), Commit 却按拼音网格那条路走 ——
                // 拿格子的下标去 Segs 里取, 轻则发错拼音, 重则下标越界直接崩。
                // 换布局虽然也会 ClearInput, 但这条规矩该由"开面板"这件事自己保证。
                Punct = true;
                GridPinyin = false;
                EnterCandRow();
                break;
        }
        Dirty = true;
    }

    /// <summary>
    /// 把一整段切分(可以多个音节)发给系统输入法, 音节之间用撇隔开。
    /// 撇必须用虚拟键发: 走 UNICODE 输入法收不到, 起不到分隔作用。
    /// 已经发过拼音时先在前面补一个撇, 接成 shou'dao 这种完整串。
    /// 为什么必须先切分再发 —— 九键一个按键对应 3~4 个字母, 按下去的那一刻我们根本
    /// 不知道该发 a 还是 b, 所以做不到"实时显示在输入法上"。只能等一串数字凑成一个
    /// 音节、切分定下来之后再整段投递。
    /// </summary>
    void SendPinyin(string[] syls, bool learn = true)
    {
        if (syls == null || syls.Length == 0) return;
        // Segment 解不出来时会把原始数字当"音节"塞进来, 那种不能当拼音发出去 ——
        // 数字键在输入法里是"选第 N 个候选", 发过去就等于误选词
        foreach (var y in syls)
            if (y.Length == 0 || !y.All(char.IsAsciiLetter)) return;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < syls.Length; i++)
        {
            bool need = i > 0 || SentPinyin.Length > 0;
            if (need) { TextOutput.Ascii('\''); sb.Append('\''); }
            foreach (char c in syls[i]) TextOutput.Ascii(c);
            sb.Append(syls[i]);
        }
        SentPinyin += sb.ToString();
        Digits = "";
        // 自学: 只有"你主动发出去了"才算认了这条切分。收起时的补发是程序替你做的,
        // 不算(见 Flush), 否则"默认项自己被反复加分"会把真正学来的选择淹掉。
        // 注意这里记的是**这一次发出去的那截**: 按 Y 一个字一个字定时每次只有一个
        // 音节, 整串(zhangsanfeng)是等这个词拼完时由 LearnWhole 补记的。
        // 记的是**这一刀**("chuang'sheng"), 不是这一串字母("chuangsheng") ——
        // 去掉撇的话, chu'ang'sheng 会和 chuang'sheng 共用同一个计数器,
        // "哪个发得多哪个排前面"就成了永远做不到的空话(2.1.36 连报三次的根)。
        if (learn) { _lastSent = KeyOfSplit(syls); T9Engine.BumpUse(_lastSent); }
        RefreshCands();
        Focus = FocusZone.Keyboard;
        GridPinyin = false;
        Dirty = true;
        // 这里故意不写日志: 每发一个音节就一行, 打一句话能攒几十行, 把真正有用的
        // (唤出/收起/切版本)那几行埋掉了。拼音发没发出去看界面状态栏就知道。
    }

    /// <summary>
    /// 切分: 把当前数字串的头一个音节解成字母发给系统输入法, 剩下的数字继续打。
    /// 由 Y 触发(九宫格上的 1 键现在没有功能)。
    /// 用它的场景 —— 整段联想里所有拼音都不是我要的, 那就先定住第一个字,
    /// 系统会接着把第二个字的可能读音重新列出来(剩下的数字重新切分)。
    /// </summary>
    void Split()
    {
        if (Segs.Count == 0) return;
        // 网格展开着就切网格里光标那条, 不是拼音行那条 —— 见 SegIdxNow 的说明
        var syls = Segs[SegIdxNow];
        if (syls.Length == 0) return;

        string first = syls[0];
        if (first.Length == 0 || !first.All(char.IsAsciiLetter)) return;
        int len = Math.Min(T9Engine.ToT9(first).Length, Digits.Length);
        // SendPinyin 里会把 Digits 清空, 剩下的那截必须先留出来再发
        string rest = Digits.Substring(len);

        SendPinyin(new[] { first });

        Digits = rest;                  // 没吃掉的数字继续打, RefreshCands 会重新切分
        RefreshCands();
        Dirty = true;
    }

    /// <summary>展开成网格, 一次看完所有可能的拼音切分。</summary>
    void OpenPinyinGrid()
    {
        if (Segs.Count <= FitNow) return;   // 一行就放得下, 没什么可展开的
        // 和符号面板互斥。两个都开着的话, 界面画的是符号(GridItems 里 Punct 优先),
        // Commit 却按拼音网格那条路走 —— 拿格子下标去 Segs 里取, 发错拼音还算轻的,
        // 拼音一条都没有时直接 Segs[0] 越界崩掉。现在这条路由 MovePinyin /
        // MovePinyinDpad 进来, 它们都先看了 !Punct, 但这条规矩该由这里自己守:
        // 后面谁再加一条入口, 不该再有机会把它摆成两个网格同时开。
        if (Punct) return;
        GridPinyin = true;
        GridPage = 0;                    // 每次展开都从第一页看起
        Focus = FocusZone.CandGrid;
        // 网格只装行里放不下的那几条(下标从 FitNow 起), 所以第一格就是行外第一条。
        // 以前这里填 PinyinIdx —— 那指的是行内最后一条, 在网格里根本不存在,
        // 结果网格第一格高亮的其实是另一条, 看着像"跳了一格"。
        GridIdx = 0;
        Dirty = true;
    }

    /// <summary>
    /// 一句拼音说到头了, 该上屏了。**归零这一步顺带决定整串算不算学过** ——
    /// 见 <see cref="SentPinyin"/> / LearnWhole 的说明。
    /// <para><paramref name="auto"/>: 最后一截是程序替你补发的(不是你选的),
    /// 那整串就不算学过 —— 1.0.9 以前这里**无条件**不算, 于是"按 Y 把名字
    /// 一个字一个字定完、还没按上屏就收起"也被抹掉, 名字永远记不住。</para>
    /// <para>**没挂着拼音的时候照样要把空格发出去**: RB 在"没有待发拼音"时就是
    /// 一个普通空格(打英文、在游戏里跳一下、在输入法候选上选字都要靠它)。
    /// 1.0.9 把它和"上屏"合成一条路之后, 早退的那句把空格也一起吞了 ——
    /// 结果没打拼音时按 RB 一个空格都打不出来。</para>
    /// </summary>
    void FinishSent(bool auto)
    {
        if (SentPinyin.Length > 0)
        {
            App.Log($"收尾: 输入法里还挂着 {SentPinyin.Length} 字{(auto ? "(程序补发, 不算学过)" : "")}");
            if (auto) DropSent();      // 最后一截是程序替你补的 -> 整串不算
            else SentPinyin = "";      // 每一个音节都是你自己定的 -> 归零时记一次
        }
        TextOutput.Space();        // 不发真键由 TextOutput 的闸门管, 这里不重复判断
    }

    // ---- 探针专用入口(tools/gridprobe / tools/mineprobe 用) ----
    // 这几个都是纯状态推演, 外面没有任何理由直接调, 也不值得为它们放宽可见性。
    // 刻意留着: "行和网格不许重复""拼过的整词要能进候选"这类规则只有断言能守住,
    // 而断言就得能摆状态。internal + 没有任何命令行参数能触达, 对用户是不可见的。
    internal void DebugRefreshCands() => RefreshCands();
    internal void DebugOpenGrid() => OpenPinyinGrid();
    internal void DebugCloseGrid() => CloseGrid();

    /// <summary>收起展开的网格。分页状态一并清掉, 下次展开重新从第一页看。</summary>
    void CloseGrid()
    {
        GridPinyin = false;
        GridPage = 0;
        Focus = FocusZone.Keyboard;
    }
    internal void DebugSplit() => Split();
    /// <summary>模拟"打开展开的符号面板"。</summary>
    internal void DebugOpenPunct() => OpenPunct();
    /// <summary>模拟"按 RB / 空格上屏": 整串是你自己定的, 该记。</summary>
    internal void DebugCommitSent() => FinishSent(false);
    internal void DebugFlush() => Flush();
    /// <summary>
    /// 探针专用: 置 true 后**一个键都不往外发**(闸门在 TextOutput.SendOne,
    /// 那是 SendInput 唯一的出口)。探针跑在命令行里, 真发键会打到当时有焦点的
    /// 那个窗口上 —— 你在写文档, 跑一趟探针就往文档里打一串拼音。
    /// 正常运行时恒为 false。
    /// </summary>
    internal static bool NoSend { get; set; }

    /// <summary>
    /// 只发头一个音节, 剩下的数字留给下一个字。**这是切分的唯一入口** ——
    /// 九宫格上的 1 键已经没有功能了(见 OnA), 它不受焦点限制, 也不用在九宫格上找键:
    /// 正在拼音行里挑拼音的时候, 发现整段联想里没有想要的, 直接按 Y 就只定住第一个字,
    /// 剩下的数字会重新切分, 第二个字的读音自己冒出来。
    /// </summary>
    /// <summary>
    /// 这一下 Y 该不该当切分用 —— 判据就是**拼音行里有没有得挑**。
    /// <para>有拼音的时候 Y 必须是切分, 而且**立刻**生效(不等松手): 拼到一半发现
    /// 整段联想里没有要的字, 按下 Y 定住头一个音节那一刻就得有回响。</para>
    /// <para>反过来, 没拼音的时候 Y 就归"换布局 / 进设置"那两桩(见 <see cref="TickLongY"/>),
    /// 于是这颗键在两种场合下各干各的, 不会互相抢。</para>
    /// </summary>
    bool YCanSplit()
    {
        // 设置面 / 窗口F / 符号面板占着屏幕的时候, Y 不该去动拼音 ——
        // 那时候拼音行根本不在屏幕上, 按了也是白按。
        if (Settings || QuickMode || Punct) return false;
        return Segs.Count > 0 && Digits.Length > 0;
    }

    void OnY()
    {
        Split();
        Dirty = true;
    }

    void OnB()
    {
        // 窗口F: B 是"退出一层" —— 绑数字中 → 退回深层; 深层 → 退回列表;
        // 已经在列表里 → 关掉 F 回九键。录快捷键中 → 放弃这次录的(不撤输入法,
        // 那会让预览框里的字跟着没, 只把记下的这串丢掉)。
        if (QEdit >= 0) { QuickEndHotkey(false); return; }

        // 设置页: B 是"退一层"(长按一路退回首页, 见 TickLongB)。
        // 已经在首页了就翻回正面 —— 和 R3 短按一样, 多一条退路, 不至于卡在设置里。
        // **不在第二层以下时, "按下去"这一下先不动**: 要留位置给长按,
        // 等松手(短按, 退一层)或按够 420ms(长按, 直接回首页)再决定。
        // 只有在首页时B 才是立刻翻回正面 —— 那儿没有"再退一层"这一说。
        //
        // 【这一支排在窗口F 前面】"眼睛看着哪一面, B 就退哪一面"。
        // 2.1.10 起两者不会同时在(见 SettingsAllowed), 顺序留着当保险。
        if (Settings)
        {
            if ((SettingsDepth?.Invoke() ?? 0) > 1) { _bDeferSet = true; return; }
            if (SettingsBack?.Invoke(false) == true) { Dirty = true; return; }
            ToggleSettings?.Invoke();
            return;
        }
        if (QuickOpen)
        {
            if (QuickUI?.Back(false) != true) QuickClose();
            else Dirty = true;
            return;
        }

        if (Focus == FocusZone.Keyboard)
        {
            // 拼音已经发给输入法了, B 要负责把它撤掉(ESC 让输入法收 composition)
            if (Digits.Length == 0 && SentPinyin.Length > 0)
            {
                TextOutput.Escape();
                DropSent();                 // 你把它撤掉了, 不算学过
                Dirty = true;
                return;
            }
            ClearInput();
            return;
        }
        if (Focus == FocusZone.CandGrid && GridPinyin)
        {
            // 拼音展开网格退回键盘区, 不清掉数字(拼音行还在下面接着显示)
            // 网格里选的是行外那些条目, 拼音行摆不下它们, 所以高亮折回行内原来那条
            GridPinyin = false;
            Focus = FocusZone.Keyboard;
            PinyinIdx = Math.Min(PinyinIdx, FitNow - 1);
            Dirty = true;
            return;
        }
        if (Focus == FocusZone.CandGrid)
        {
            // 标点框是直接展开的, B 就整个退掉; 文字候选的展开退回单行继续选
            if (Punct) ClearInput();
            else { Focus = FocusZone.CandRow; Dirty = true; }
            return;
        }
        // 从候选行回键盘区: 必须走 ClearInput, 否则 Punct 留着, 候选行会一直显示标点
        ClearInput();
    }

    /// <summary>
    /// X = 退一格。**只删, 不退界面** —— 退出一律交给 B。
    /// 以前它会顺手把焦点拨回九宫格, 结果在"全部拼音"/"全部符号"里按一下 X
    /// 就把整个列表关掉了, 想接着挑还得重新展开一遍。
    /// </summary>
    void OnX()
    {
        if (Digits.Length > 0)
        {
            Digits = Digits[..^1];
            RefreshCands();
        }
        else
        {
            // 拼音已经发给输入法了, 退格删的是输入法里的字母, 这边显示要跟着退
            if (SentPinyin.Length > 0) SentPinyin = SentPinyin[..^1];
            TextOutput.Backspace();
        }

        if (GridPinyin)
        {
            // 全部拼音网格: 删完留在原地接着挑。数字删光了才真的没得挑, 那时候才退。
            if (GridCount() == 0) { GridPinyin = false; Focus = FocusZone.Keyboard; }
            else GridIdx = Math.Clamp(GridIdx, 0, GridCount() - 1);
        }
        else if (Punct)
        {
            // 全部符号: 光标和分类都不动, 删完接着挑下一个符号
        }
        else
        {
            Focus = FocusZone.Keyboard;
            Punct = false;
            CandIdx = 0;
            GridIdx = 0;
        }
        Dirty = true;
    }

    void Commit()
    {
        // 拼音展开网格: 选中哪一种切分, 就按那一种整段发过去
        if (GridPinyin)
        {
            // Segs.Count > 0 不是多余的保险: GridCount() 在 Punct 开着时算的是
            // 符号个数, 跟 Segs 没关系。少了这一条, "网格开着但一条拼音都没有"
            // 的状态下按 A 就是 Segs[0] —— 索引越界, 程序直接崩。
            if (Segs.Count > 0 && GridIdx >= 0 && GridCount() > 0) SendPinyin(Segs[SegIdxNow]);
            GridPinyin = false;
            Focus = FocusZone.Keyboard;
            Dirty = true;
            return;
        }

        // 九键不再自己出汉字 —— 那活整个交给系统输入法了, 这里只剩符号要上屏
        if (!Punct) return;

        var list = PunctsNow();
        // 网格正中间那格是"标签格"(装分类名, 见 GridCenter), 落在那儿按 A 就是没按
        int i = Focus == FocusZone.CandRow ? CandIdx : PunctIdxOfGrid(GridIdx);
        if (i < 0 || i >= list.Length) return;

        SentPinyin = "";                 // 出符号 = 这一句拼音到这里为止
        TextOutput.Text(list[i]);
        // 标点候选不退出: 书名号这种要连着输两个, 一次就退太烦。按 B 才退出
        Dirty = true;
    }

    /// <summary>
    /// 数字串 -> 拼音行。九键只负责把数字解成拼音(汉字和联想由系统输入法给),
    /// 所以这里只算切分, 一个词都不查。
    /// </summary>
    void RefreshCands()
    {
        Segs = CompleteSegs(Digits);
        // 一个完整音节都凑不出来时(最典型就是刚按下第一个数字键)不能留空:
        // 补上"拼了一半"的候选。不然每打一个字, 头两三个按键拼音行都是一片空白,
        // 等于盲打 —— 按 7 得 p q r s, 按 94 得 yi zi xi, 再按 9 得 yi'w zi'w xi'w。
        if (Segs.Count == 0) Segs = PartialSegs(Digits);
        // 还是空的: 说明这串数字怎么切都不是音节(连按四次 4, g/h/i 组不出任何拼音)。
        // 这时候宁可给一串"没意义的字母"也不能让拼音行消失 —— 空行比怪串难受,
        // 而且系统输入法还会再猜一次(gg 它能猜出"哥哥"), 未必真没用。
        if (Segs.Count == 0) Segs = LetterSegs(Digits);
        // 只按下了一颗键: 一颗键上三四个字母, 光靠它本来就说不清你要哪个;
        // 上面三档又都只肯给"能成音节的"那几种, 于是 2 只给 a、3 只给 e、6 只给 o ——
        // 而 7 那种键上没有单字母音节的走的却是另一条路, p q r s 四个全摆出来。
        // 同一个动作两种结果, 看着就像"有的键显示不全", 所以这里统一补一遍。
        if (Digits.Length == 1) Segs = OneKeySegs(Digits, Segs);

        PinyinIdx = 0;
    }

    /// <summary>
    /// 只按下一颗键时, 把这一颗键上**剩下的字母**也补进候选。
    /// <para>顺序: 上面算出来的(能成音节的那些)照旧排前面 —— 保住"按 2 出来是 a"
    /// 的手感, 别让默认项悄悄换了人; 补进来的字母顺着键上印的顺序排在后面</para>
    /// <para>LettersOf 给的是引擎自己那一份(按"多少音节以它开头"排的),
    /// **先 Clone 再排序** —— 直接排会改掉引擎里的顺序, 别处跟着变。</para>
    /// </summary>
    static List<string[]> OneKeySegs(string digits, List<string[]> segs)
    {
        var res = new List<string[]>(segs);
        var have = new HashSet<string>();
        foreach (var s in segs)
            if (s.Length == 1) have.Add(s[0]);
        var letters = (char[])T9Engine.LettersOf(digits[0]).Clone();
        if (letters.Length == 0) return res;
        Array.Sort(letters);           // abc / def / tuv …, 顺着键上印的顺序
        foreach (char c in letters)
        {
            string s = c.ToString();
            if (have.Add(s)) res.Add(new[] { s });
        }
        return res;
    }

    /// <summary>九键保留多少种拼音切分。再多就用展开的网格看。</summary>
    const int SegMax = 28;
    /// <summary>"拼了一半"的候选最多留几条(见 CompleteSegs 里 PartTails 那段)。</summary>
    const int PartMax = 12;
    /// <summary>同一个首音节下面, "拼了一半"的尾巴最多给几种切法(po'kan'mo'j /
    /// po'lan'mo'j / po'kao'mo'j …)。给几种是为了"按完这一下不至于只剩一条"。</summary>
    const int PartTailWays = 3;
    /// <summary>"拼了一半"时, 前面那段最多取几种完整切分去接后缀(4×4=16 条, 再多就翻不完)。</summary>
    const int HeadMax = 4;
    /// <summary>同一个"第一个字"最多留几条尾巴(首音节多的时候会自动往下让)。</summary>
    const int PerHeadMax = 3;

    /// <summary>
    /// 一条切分的**去重键** —— 就是"真发给输入法那一串", 所以**带分隔符**。
    /// <para><see cref="SendPinyin"/> 是一个音节一个音节发的, 中间真发一个撇:
    /// paohun 和 pa'o'hun 到输入法那里是两个不同的串, 出来的字也不一样。
    /// 所以只要**切法**不同, 就是两条不同的候选, 一条都不能省。</para>
    /// <para>【2.1.19 以前这个键是不带撇的字母串】当时的理由是"摆两条出来只会让人
    /// 以为是两个不同的词"。可撇是真的发出去的, 于是撞车的两条里后到的那条被整条
    /// 丢掉 —— 表现就是: 长串打到后面, 由前两个字母组成的那条读音在候选里彻底找不到
    /// (按 726 只给 pao, 想让第一个字是 pa 的人一条都选不出来)。</para>
    /// </summary>
    static string KeyOfSplit(string[] syls) => string.Join('\'', syls);

    /// <summary>
    /// 数字串能切出的完整拼音切分: 只要全字母的(解不出来时 Segment 会塞裸数字),
    /// 按整个切分串去重, 再按(音节少、像话)排序。
    /// </summary>
    static List<string[]> CompleteSegs(string digits)
    {
        _tailCache.Clear();          // 尾巴那张缓存只对这一串数字有效
        _partCache.Clear();          // 同上 —— 换数字串就整个作废
        if (digits.Length == 0) return new List<string[]>();
        var raw = T9Engine.Segment(digits, 40);

        var seen = new HashSet<string>();
        var list = new List<string[]>();
        // 你自己拼出来的整词**先入场**。Segment 的 beam 是按"块数少 + 音节热"裁的,
        // 整串成不成词它不看, 于是名字/网名这种冷门组合常常连池子都进不来 ——
        // 记多少次都没用, 只能每次再按三遍 Y。见 T9Engine.AddMine 的说明。
        // 只管"让它进得了候选", 排第几仍然由 Rank 说了算。
        foreach (var m in T9Engine.MineOf(digits))
        {
            if (m.Length == 0) continue;
            if (seen.Add(KeyOfSplit(m))) list.Add(m);
        }
        foreach (var s in raw)
        {
            if (s.Length == 0) continue;
            // 裸数字不能当拼音发: 数字键在输入法里是"选第 N 个候选", 发出去等于误选词
            if (!s.All(y => y.Length > 0 && y.All(char.IsAsciiLetter))) continue;
            if (seen.Add(KeyOfSplit(s))) list.Add(s);
        }
        // 分数优先, 块数只作为"多切一块扣一笔"的软惩罚(见 T9Engine.RankOrder)。
        // 以前是"块数少优先"硬分档: 那样你按 Y 拼出来的名字永远翻不了身 ——
        // bi'er'bo(3 块, 1522 分) 打多少次都排在 che'san(2 块, 272 分) 后面。
        // 块数还是要算的, 不然音节分累加会让 ci'er'an 这类碎切分冒头,
        // 只是从"一票否决"改成"扣一笔分", 真高分能翻过去、噪声翻不过去。
        int minBlocks = list.Count > 0 ? list.Min(s => s.Length) : 0;
        var ranked = list.OrderByDescending(s => T9Engine.RankOrder(s, minBlocks))
                         .ThenBy(s => s.Length).ToList();

        // ---- 补齐"第一个字": 每个可能的首音节都至少露一次脸 ----
        // Segment 那道 beam 是按**整串**分数裁的。串一长, 它每一层只留四十条,
        // 于是冷门的第一个音节(pao)在第一层还活着, 走到第三四层就被热门的
        // (san / ran / rao, 它们后面能接出更高分的整串)整条挤掉了 ——
        // 你在候选里从头翻到尾也找不到 pao 开头的那一条, 只能删掉重打。
        // 第一个字选不出来, 后面拼得再准都没用, 所以这里按首音节逐个补齐:
        // 每一个可能的首音节各留几条, 尾巴用剩下那截的最优切分。
        var pool = new List<string[]>();
        var poolSeen = new HashSet<string>();
        // 哪几条是"拼了一半"的(尾巴上挂的是猜出来的字母), 收尾时按它把它们压到最后
        var partTail = new HashSet<string>();
        var heads = FirstSyllables(digits);
        // 每个首音节留几条。可能的首音节越多, 每条留得越少 —— 名额就这么多,
        // 全给一种拼法, 别的就又露不出脸了。
        int per = heads.Count <= 9 ? PerHeadMax : heads.Count <= 14 ? 2 : 1;
        // 先让**每个**首音节都有一条(这是底线), 再回头给热门的补第二条第三条。
        for (int round = 0; round < per; round++)
        {
            foreach (var (syl, len) in heads)
            {
                if (pool.Count >= SegMax) break;
                int want = round + 1;
                int n = CountHead(pool, syl);
                if (n >= want) continue;
                // 池子里本来就有的先算上(它们排位靠前, 优先占名额)
                foreach (var s in ranked)
                {
                    if (n >= want) break;
                    if (s.Length > 0 && s[0] == syl && poolSeen.Add(KeyOfSplit(s)))
                    { pool.Add(s); n++; }
                }
                if (n >= want) continue;
                // 还差, 就强制"第一块是这个音节"再切一次尾巴
                foreach (var t in TailsFor(digits, len, per))
                {
                    if (n >= want) break;
                    var seg = new[] { syl }.Concat(t).ToArray();
                    if (!seg.All(y => y.Length > 0 && y.All(char.IsAsciiLetter))) continue;
                    if (poolSeen.Add(KeyOfSplit(seg))) { pool.Add(seg); n++; }
                }
                // 【2.1.19】走到这儿还是一条都没有: 剩下那截压根凑不出完整音节 ——
                // 按 7264 想让第一个字是 pan, 可 4 上那三个字母一个音节都组不成,
                // TailsFor 只能给空, 于是候选里从头翻到尾都见不到 pan 打头的一条。
                // 那就照 PartialSegs 的路子补一条"拼了一半"的: 首音节 + 剩下那截
                // 按下去的字母。按 Y 定住第一个字吃的正是 syls[0], 有了它这件事才有着落。
                // **一个首音节只补一条、也只在它一条都没有时补** —— 这尾巴是猜的,
                // 铺开了就成噪声了。
                // 另外只在**三位数往上**才补: 两位的短串整串都拼不出音节时,
                // 下面 PartialSegs / LetterSegs 那两档兜底给的信息比它全得多
                // (27 能摆出 bp bq br bs cp … 一整排), 别抢在它们前头。
                // n >= want 才停: 首音节那几轮里, 前面两档(现成的 / 再切一次尾巴)
                // 都给不出来的时候, 就多补几种"拼一半"的切法, 不是一条到顶。
                if (n >= want || digits.Length < 3 || partTail.Count >= PartMax) continue;
                foreach (var t in PartTails(digits, len))
                {
                    if (n >= want) break;
                    var seg = new[] { syl }.Concat(t).ToArray();
                    if (!seg.All(y => y.Length > 0 && y.All(char.IsAsciiLetter))) continue;
                    if (poolSeen.Add(KeyOfSplit(seg)))
                    { pool.Add(seg); partTail.Add(KeyOfSplit(seg)); n++; }
                }
            }
        }
        // 补齐完还有空位, 再按分数把其余的填进来
        foreach (var s in ranked)
        {
            if (pool.Count >= SegMax) break;
            if (poolSeen.Add(KeyOfSplit(s))) pool.Add(s);
        }
        // 【2.1.19】"拼了一半"的那几条一律压到**最后**, 不跟真候选抢位置。
        // 原因: 打分是拿**字母串**去查词库的(词库里的键本来就没有撇), 而它们尾巴上
        // 挂的是猜出来的一个字母 —— zhan'g 会被当成 zhan + g = "zhang" 去查,
        // 张 是个高频词, 一下就把真正学会的 zhang'san'feng 顶到第二去了(mineprobe 抓的)。
        // 它们是"让你至少找得到这条首音节"的兜底, 排在末尾正好: 拼音行摆不下时,
        // 它们本来就是走"展开全部"才看得到的那几条。
        var body = pool.Where(s => !partTail.Contains(KeyOfSplit(s)))
                       .OrderByDescending(s => T9Engine.RankOrder(s, minBlocks))
                       .ThenBy(s => s.Length)
                       .Take(Math.Max(1, SegMax - partTail.Count))
                       .ToList();
        body.AddRange(pool.Where(s => partTail.Contains(KeyOfSplit(s)))
                          .OrderByDescending(s => T9Engine.RankOrder(s, minBlocks))
                          .ThenBy(s => s.Length));
        return body;
    }

    static int CountHead(List<string[]> pool, string syl)
    {
        int n = 0;
        foreach (var s in pool) if (s.Length > 0 && s[0] == syl) n++;
        return n;
    }

    /// <summary>
    /// 数字串开头那些**能成音节**的前缀: 726 -> (pan,3) (pao,3) (ran,3) …
    /// 拼音最长六位(如 zhuang), 所以只看前六个数字。
    /// </summary>
    static List<(string Syl, int Len)> FirstSyllables(string digits)
    {
        var res = new List<(string, int)>();
        // **长的排在前面**。这一条顺序不是随便定的: "pa"(两位)和 "pao"(三位)
        // 是同一串字母的两种切法, 而 KeyOf 按字母串去重, 谁先到谁留下。
        // 让长音节先走, 留下的是块数更少、更像人话的那条(pao'hun 而不是 pa'o'hun);
        // 反过来的话, 先处理 "pa" 会把 "pao" 这一整条顶掉 —— 你在候选里就再也
        // 选不到 pao 这个第一字了(coretest【2】当年就是这么挂的)。
        for (int L = Math.Min(6, digits.Length); L >= 1; L--)
        {
            string code = digits.Substring(0, L);
            foreach (var s in T9Engine.SyllablesOf(code)) res.Add((s, L));
        }
        return res;
    }

    /// <summary>
    /// 吃掉开头 len 个数字之后, 剩下那截的最优切分(最多 k 条)。
    /// 同一段尾巴会被反复要(好几个首音节长度相同), 所以按长度缓存一下。
    /// </summary>
    static List<string[]> TailsFor(string digits, int len, int k)
    {
        if (!_tailCache.TryGetValue(len, out var t))
        {
            t = len >= digits.Length
                ? new List<string[]> { Array.Empty<string>() }
                : T9Engine.Segment(digits.Substring(len), k);
            _tailCache[len] = t;
        }
        return t;
    }

    /// <summary>
    /// 吃掉开头 len 个数字之后, 剩下那截**拼一半**的尾巴(只给一条)。
    /// 只在"剩下那截凑不出完整音节"时用它兜底: 7264 想要 pan 打头, 4 上三个字母
    /// 一个音节都组不成 —— 那就把那截按下去的字母原样接上(pan'g)。
    /// <para>【2.1.21】这条尾巴必须**把剩下那截整个表示出来**, 一位都不能少。
    /// 以前只接"剩下那截头一个数字推出的一个字母", 于是出了两个毛病:
    ///   ① **连按同一个键, 拼音行一动不动** —— 761 / 7611 / 76111 摆出来的全是
    ///      po'j, 看不见自己按了几下, 可退格得一下一下按三回;
    ///   ② **已经定下来的中间音节被整段丢掉** —— po'kan'mo 再按一下直接缩成 po'j。
    /// 根子是同一个: 那截尾巴没把剩下的数字表示完, 于是"多按的那几下"在界面上
    /// 等于不存在。现在分两步: 先在剩下那截里切出**最长能完整切出来的一段**,
    /// 再把真正剩下的那几个数字各取一个字母接上 ——
    /// 76126661 -> po'kan'mo'j, 7611 -> po'jj, 76111 -> po'jjj。</para>
    /// </summary>
    static List<string[]> PartTails(string digits, int len)
    {
        if (!_partCache.TryGetValue(len, out var t))
        {
            t = BuildPartTail(digits, len);
            _partCache[len] = t;
        }
        return t;
    }

    static List<string[]> BuildPartTail(string digits, int len)
    {
        string rest = len >= digits.Length ? "" : digits.Substring(len);
        if (rest.Length == 0) return new List<string[]>();

        // ① 剩下那截里, 最长能完整切出来的一段(整截都能切就整截都要)。
        //    这一步是"不坍缩"的关键: po'kan'mo 后面再按一下, 剩下那截是 126661,
        //    它前五个数字还切得出 kan'mo —— 中间这些已经定下来的音节必须留着。
        //    同一个长度常常有好几种切法(kan'mo / lan'mo / kao'mo), 都留着:
        //    按这一下之前能挑十几条、按完只剩一条, 那也是一种割裂。
        var res = new List<string[]>();
        var seen = new HashSet<string>();
        for (int cut = rest.Length; cut >= 1; cut--)
        {
            foreach (var s0 in T9Engine.Segment(rest.Substring(0, cut), PartTailWays))
            {
                if (s0.Length == 0) continue;
                if (!s0.All(y => y.Length > 0 && y.All(char.IsAsciiLetter))) continue;
                int used = s0.Sum(y => T9Engine.ToT9(y).Length);

                // ② 真正剩下的那几个数字, 各取一个字母原样接上。
                //    一个都不能少: 少接一个, 用户就数不出自己按了几下 ——
                //    连按三下 jkl, 拼音行必须从 po'j 变到 po'jj 再变到 po'jjj。
                var sb = new System.Text.StringBuilder();
                bool ok = true;
                for (int i = used; i < rest.Length; i++)
                {
                    char c = FirstLetterOf(rest[i]);
                    if (c == '\0') { ok = false; break; }   // 5 键上没有字母, 认了
                    sb.Append(c);
                }
                if (!ok) continue;
                var seg = sb.Length > 0 ? s0.Append(sb.ToString()).ToArray() : s0;
                if (seen.Add(KeyOfSplit(seg))) res.Add(seg);
            }
            if (res.Count > 0) break;
        }

        // 剩下那截**一个音节都切不出来**: 那就整截都按字母拼上。
        // 7264 想要 pan 打头, 可 4 上那三个字母一个音节都组不成 ——
        // pan'g / pao'g / san'g 那几条全是从这条路来的。少了这一手,
        // 7264 的候选会从 13 条掉到 7 条(只剩整串能切的 pang / rang / sang)。
        if (res.Count == 0)
        {
            var all = new System.Text.StringBuilder();
            for (int i = 0; i < rest.Length; i++)
            {
                char c = FirstLetterOf(rest[i]);
                if (c == '\0') return new List<string[]>();   // 5 键上没有字母, 认了
                all.Append(c);
            }
            if (all.Length > 0) res.Add(new[] { all.ToString() });
        }
        return res;
    }

    /// <summary>一颗数字键上"最像是话"的那个字母: 先看它能不能当某个音节的开头
    /// (Prefixes), 不行就退回键上的字母表 —— Prefixes 会把 i / u / v 整个漏掉,
    /// 而键上真有它们, 硬拼那一路得靠字母表兜住。</summary>
    static char FirstLetterOf(char digit)
    {
        var pre = T9Engine.Prefixes(digit.ToString());
        if (pre.Count > 0 && pre[0].Length > 0) return pre[0][0];
        var l = T9Engine.LettersOf(digit);
        return l.Length > 0 ? l[0] : '\0';
    }
    // 同 _tailCache: 一次 CompleteSegs 之内有效
    static readonly Dictionary<int, List<string[]>> _partCache = new();
    // 一次 CompleteSegs 之内有效 —— 换数字串就整个作废, 由 CompleteSegs 开头清掉
    static readonly Dictionary<int, List<string[]>> _tailCache = new();

    /// <summary>
    /// 拼到一半的切分: 前面已经能定下来的音节 + 剩下那几个数字"各取一个字母"的种种可能。
    /// 打 7 -> p q r s;  打 94 -> yi zi xi;  再打 9 -> yi'w zi'w xi'w …
    /// 最后那一段还不是完整音节, 但发出去输入法照样认(它会自己往下补), 所以能选能发。
    /// </summary>
    static List<string[]> PartialSegs(string digits)
    {
        if (digits.Length == 0) return new List<string[]>();

        // 取"最长的、能完整切出来的那一段"当头; 一个都切不出来就从头开始拼
        var heads = new List<string[]> { Array.Empty<string>() };
        for (int cut = digits.Length - 1; cut >= 1; cut--)
        {
            var h = CompleteSegs(digits.Substring(0, cut));
            if (h.Count == 0) continue;
            heads = h.Take(HeadMax).ToList();
            break;
        }

        var seen = new HashSet<string>();
        var res = new List<string[]>();
        foreach (var h in heads)
        {
            int used = h.Sum(y => T9Engine.ToT9(y).Length);
            string rest = digits.Substring(Math.Min(used, digits.Length));
            if (rest.Length == 0) continue;
            foreach (var p in T9Engine.Prefixes(rest))
            {
                var seg = h.Append(p).ToArray();
                if (seen.Add(KeyOfSplit(seg))) res.Add(seg);
            }
        }
        // 和 CompleteSegs 同一套排序: 块数只作软惩罚, 不用它硬分档
        int minBlocks = res.Count > 0 ? res.Min(s => s.Length) : 0;
        return res.OrderByDescending(s => T9Engine.RankOrder(s, minBlocks))
                  .ThenBy(s => s.Length)
                  .Take(SegMax).ToList();
    }

    /// <summary>
    /// 最后一档兜底: 整串数字怎么切都不是音节时, 直接把按键上的字母硬拼出来。
    /// 44 -> gg gh gi hg hh hi …;  4444 同理。目的只有一个: 拼音行不能空。
    /// </summary>
    static List<string[]> LetterSegs(string digits)
    {
        if (digits.Length == 0) return new List<string[]>();
        return T9Engine.LetterCombos(digits, SegMax)
                       .Select(s => new[] { s })
                       .ToList();
    }

    void EnterCandRow()
    {
        if (CandRowCount() == 0) return;
        Focus = FocusZone.CandRow;
        CandIdx = 0;
    }

    public void ClearInput()
    {
        Digits = "";
        Segs = new List<string[]>();
        Punct = false;
        GridPinyin = false;
        GridPage = 0;
        CandIdx = 0;
        GridIdx = 0;
        PinyinIdx = 0;
        DropSent();
        Focus = FocusZone.Keyboard;
        // 【摇杆推着的时候, 光标不归位】2.4.7 用户报: "长按数字发快捷短语, 左摇杆
        // 瞬间闪回中心, 再回到正在推的地方"。根因就在这一行: 发短语(预输入版)要走
        // QuickClose -> ClearInput, 无条件把 Key9 按回 5; 而摇杆还推着, 下一帧
        // HandleStick 又把它拽回摇杆指着的那一格 —— 中间闪一下。
        // 摇杆没推时这一行和 HandleStick 每帧做的事本来一样(见 "active ? PointTo9 : 5"),
        // 留着只为兜住"摇杆不碰 Key9"的布局(26 键): 换回九键时光标照旧落正中。
        if (Math.Sqrt((double)_stickLX * _stickLX + (double)_stickLY * _stickLY) < DZ) Key9 = 5;
        Drop26Mods();
        Dirty = true;
    }

    /// <summary>
    /// 26 键的 Shift / Ctrl / Alt / Win 全部松开。
    /// 退出符号面板、换布局、切版本这些"重来一遍"的时刻都要掉一次 ——
    /// 否则粘住的 Ctrl 会一直挂着, 之后按什么键都变成快捷键。
    /// </summary>
    void Drop26Mods()
    {
        ShiftLatch = false;
        ShiftHeld = false;
        TextOutput.ClearMods();
    }

    // ================= 窗口F: 快捷短语 / 快捷键 (2.1.1 重构) =================
    //
    // 长按九键上「快捷」那一格开出来。界面那一头是**一层层九宫格**(和设置页同一套
    // 代码, 见 LayerHost):
    //     列表层   5 页 × 9 格 = 45 条, 第 1 页那九格左上角写着 1~9
    //     深层     长按某一格浮出来: 写入短语 / 写入快捷键 / 删除 / 绑的数字
    //     数字层   再往下: 九个数字, 点一个就绑过去
    //
    // 【位置就是数字】第 1 页那九格就是数字键 1~9 —— 写在哪一格, 长按数字几就发
    // 哪一条。不排序, 删掉一条也不把后面的往前挪: 手柄是靠方向记位置的,
    // 位置天天在动就记不住。
    //
    // 这一节只管"按了什么键该干什么"; 那一摞层长什么样、光标停在哪一格,
    // 全在窗口那边(见 IQuickUI)。

    /// <summary>窗口F 开着(列表层 / 深层 / 数字层 / 录快捷键, 全算)。</summary>
    public bool QuickOpen;
    /// <summary>-1 = 不在录; 1 = 正在录快捷键(屏幕上摆着全键盘, 一个键都不往外发)。</summary>
    public int QEdit = -1;
    /// <summary>深层 / 录键针对哪一个槽位(0~44)。</summary>
    public int QSlot = -1;
    /// <summary>已经记下来的那串键(alt+r 这种)。</summary>
    public string QBuf = "";

    /// <summary>F 占着屏幕 —— 这时候九键 / 候选那些都不该再响应。</summary>
    /// <para>【2.1.10: 又变回 QuickOpen 本身了】2.1.9 那句 && !Settings 是给
    /// "开着窗口F 再翻进设置"准备的 —— 那种叠法已经不让发生了(见
    /// <see cref="SettingsAllowed"/>), 设置和 F 不可能同时在, 那个扣减也就没用了。</para>
    public bool QuickMode => QuickOpen;

    /// <summary>
    /// 【2.1.10】这一帧**能不能翻到设置面** —— 只有两处行: **九键首页**和
    /// **全键盘**。其余一律不行: 全部符号那一大页、窗口F 那一摞(列表 /
    /// 深层 / 绑数字 / 录快捷键)、数字九键… 统统按了没反应。
    /// <para>为什么要收: 哪儿都能进设置, 就总得回答"退出来退到哪儿" ——
    /// 2.1.9 那一整套"设置盖在上面时手柄归设置"的让位逻辑就是这么来的,
    /// 绕了一大圈。不如一开始就只留两处, 进得去的界面都是**没有上一层**的,
    /// 那套让位逻辑整个可以不要。</para>
    /// <para>右下角那个齿轮也按它显隐(见 OverlayWindow.Render): 不画出来,
    /// 鼠标也点不到, 不会出现"有个角在那儿按下去却不动"。</para>
    /// </summary>
    public bool SettingsAllowed =>
        !Settings && !QuickOpen && !Punct
        && (Layout == Layout.CN9 || Layout == Layout.EN26);

    /// <summary>界面那一摞层(窗口实现, App 接线)。</summary>
    public IQuickUI QuickUI;

    /// <summary>
    /// 三层的名字。窗口把最上面那层的 Title 报上来, 就是这三个之一 ——
    /// 拿它认"现在在哪一层"(比如 L3 只在数字层里是解除绑定)。
    /// </summary>
    public const string Q_LAYER_LIST = "快捷", Q_LAYER_DEEP = "编辑", Q_LAYER_NUM = "绑数字";

    /// <summary>
    /// 有没有预览框可以往里塞字。**直出版没有预览框** —— "写入短语"这个动作全靠它,
    /// 没有的话按下去只给一句提示(发短语不受影响, 两个版本都行)。
    /// </summary>
    public bool HasNote => QuickNoteText?.Invoke() != null;

    /// <summary>
    /// 长按多久算"要开窗口F" / "要进深层"。比连发的 400ms 门槛略高一点, 免得手重一点就误触。
    /// </summary>
    const int QUICK_HOLD_MS = 450;

    DateTime _quickAt;
    bool _quickFired;          // 这次按下已经用掉了(开过 F / 发过短语), 松手不再补数字
    int _numPending;           // 数字层: 按下的是哪个数字, 还没决定是"出数字"还是"发短语"
    Layout _qWasLayout;        // 录快捷键前是哪个布局(录完还原, 免得被留在全键盘上)
    bool _ltNum;               // 这次按住 LT 真的切到数字层了吗(没切就别在松手时还原)
    string _qNoteSaved = "";   // 录快捷键前预览框里原来的字(退出时还回去)
    bool _qNoteHas;            // 真有预览框吗(直出版没有, 那时不碰它)
    // 窗口F 里 A 的账。**列表层的 A 按下先攒着**: 松手(短按)才发, 按够 QUICK_HOLD_MS
    // 是"进深层"。先发再进深层就成了"发一条短语、顺手弹个编辑层", 那不是你要的。
    // 深层往里(编辑层 / 数字层)没有长按这一说, 按下去立刻响应。
    bool _qaWasDown, _qaFired;
    DateTime _qaAt;
    // 快捷键要"先关窗口、把焦点还给游戏、再发键", 三步之间得留出时间差:
    // SetForegroundWindow 是异步生效的, 同一帧里紧接着 SendInput 那一串键
    // 大概率还落在我们自己的预览框上。所以记一笔, 到点在 Poll 里补发。
    ushort _chordMod, _chordVk;
    DateTime _chordAt;
    DateTime _now;             // 这一帧的时间(探针灌的是自己编的时间轴)

    /// <summary>打开窗口F。每次开都重读一次, 别的数据通道改了也能看到。</summary>
    public void QuickOpenPanel()
    {
        if (!QuickStore.Loaded) QuickStore.Load();
        QuickOpen = true;
        QEdit = -1;
        QSlot = -1;
        QBuf = "";
        // 开 F 的那一下 A 还按着 —— 这一下记成"已经用掉了"。不记的话, 后面那个
        // "按住 450ms 进深层"的判据会接住同一只手, 刚开列表就顺势钻进编辑层。
        _qaWasDown = true;
        _qaFired = true;
        // F 开着的时候 HoldRepeatA 根本不跑, _aHeld 就冻在"开 F 那一下"的状态上;
        // 不在这儿清掉, 待会儿进录键页第一下 A 会被当成"上一次还按着"给吞掉。
        _aHeld = false;
        Focus = FocusZone.Keyboard;
        ClearInput();
        Dirty = true;
    }

    public void QuickClose()
    {
        // 录到一半被关掉: 先撤录键那一页。撤完再 StageOff 一声 —— 那一页退出时
        // 会 StageOn(让它就地接回这一摞), 可这儿是要把 F **整个**关掉,
        // 那一摞不该再在台上露脸。
        if (QEdit >= 0) { QuickEndHotkey(false); QuickUI?.StageOff(); }
        QuickOpen = false;
        QSlot = -1;
        QBuf = "";
        _qaWasDown = false;
        _qaFired = false;
        // 录键途中被关掉(比如发快捷键)也得把预览框还原, 否则里面留着 alt+r
        if (_qNoteHas) { QuickSetNote?.Invoke(_qNoteSaved); _qNoteHas = false; }
        Focus = FocusZone.Keyboard;
        ClearInput();
        Dirty = true;
    }

    // ---- IQuickHost: 界面上那几颗按钮真要干的事 ----
    // 显式实现(void IQuickHost.Xxx): Send / Delete 这种名字太通用, 不该混进
    // 输入核心自己的方法堆里 —— 窗口那边本来就只认接口。

    void IQuickHost.Send(int slot) => QuickSendSlot(slot);
    void IQuickHost.WritePhrase(int slot) => QuickWritePhrase(slot);
    void IQuickHost.RecordHotkey(int slot) => QuickStartHotkey(slot);
    void IQuickHost.Delete(int slot) => QuickDeleteSlot(slot);
    void IQuickHost.Bind(int slot, int num) => QuickBindSlot(slot, num);
    void IQuickHost.Unbind(int slot) => QuickUnbindSlot(slot);

    /// <summary>列表层短按 A: 把这一格的东西发出去(空格子什么也不做)。</summary>
    void QuickSendSlot(int slot)
    {
        var it = QuickStore.At(slot);
        if (it == null) return;
        QuickSend(it);
    }

    /// <summary>
    /// 深层 1 号键"写入短语": 把**预览框里现在的文字**整段写进这一格。
    /// 不再另开一个输入窗口 —— 字本来就在框里, 再开一个框等于让你把同一段话打两遍。
    /// 预览框里的字**留着不动**, 所以连按两下会写出两条一样的, 那是你的选择。
    /// </summary>
    void QuickWritePhrase(int slot)
    {
        string raw = QuickNoteText?.Invoke();
        if (raw == null)                       // 直出版没有预览框, 这个动作不给用
        {
            App.Log("窗口F: 直出版没有预览框, 写短语请切到预输入版");
            Dirty = true;
            return;
        }
        string text = raw.Trim();
        if (text.Length == 0)
        {
            App.Log("窗口F: 预览框是空的, 没东西可写");
            Dirty = true;
            return;
        }
        QuickStore.Set(slot, QKind.Phrase, text);
        App.Log($"窗口F: 第 {slot + 1} 格写入短语 {text.Length} 字");
        // 【必须叫一声 Refresh】光设 Dirty 只重画"框外面那几样", 深层里那几个格子
        // 和底下那条长条是搭面板时照当时的内容造出来的 —— 不重搭的话, 写进去的
        // 那条要等退出再进(整摞重新搭一遍)才看得见。
        QuickUI?.Refresh();
        Dirty = true;                          // 留在这一层, 好让你看见底下那条长条变了
    }

    /// <summary>
    /// 深层 2 号键"写入快捷键": 摆出全键盘录一串键。
    /// 全键盘**左右扩张**出来 —— 和九键按 L3 切全键盘是同一个动作, 不是"换一张脸"。
    /// 上面那个"保存"按钮撤了, 保存改成按 **Y**(见 QuickEndHotkey)。
    /// </summary>
    void QuickStartHotkey(int slot)
    {
        QSlot = slot;
        QBuf = QuickStore.At(slot) is { } it && !it.IsPhrase ? it.Text : "";
        _qWasLayout = Layout;
        // 记下的键**写进预览框**里(像真在打字: shift -> 自动加 + -> r), 不再挂在那行
        // 小灰字上 —— 那行字太小, 记了三四个键就挤成一团看不清。
        // 框里原来那句话先存着, 退出时原样还回去(直出版没有预览框, 那时不碰)。
        string was = QuickNoteText?.Invoke();
        _qNoteHas = was != null;
        _qNoteSaved = was ?? "";
        if (_qNoteHas) QuickSetNote?.Invoke(QBuf);

        void Enter()
        {
            QEdit = 1;
            Layout = Layout.EN26;
            QuickUI?.StageOff();               // 那一摞先让开, 位置给全键盘
            // 开这一页的那一下 A 还按在手上(就是刚才在深层里选中"写入快捷键"那一下)。
            // 装成"还按着", 那一页就等这一下松开才肯记键 —— 不然你按一下菜单,
            // 它顺手就把 A 自己记成快捷键的第一个键了。
            _aHeld = true;
            ClearInput();
            SyncIme();
            Dirty = true;
        }
        var flip = FlipLayout;
        if (flip == null) Enter(); else flip(Enter);
    }

    /// <summary>退出录键页。save=true 且真记下了键才落盘。</summary>
    public void QuickEndHotkey(bool save)
    {
        if (QEdit < 0) return;
        string text = QBuf.Trim();
        int slot = QSlot;

        void Leave()
        {
            QEdit = -1;
            Layout = _qWasLayout;
            // 【出去要走 StageOn, 不是 StageOff】这一摞立刻回台上: 收回这 230 毫秒
            // 里若还让全键盘顶着, 它会被横向压到 360/640, 就是"键缩窄了一下"。
            QuickUI?.StageOn();
            if (_qNoteHas) { QuickSetNote?.Invoke(_qNoteSaved); _qNoteHas = false; }
            ClearInput();
            SyncIme();
            Dirty = true;
        }
        var flip = FlipLayout;
        if (flip == null) Leave(); else flip(Leave);

        QSlot = -1;
        QBuf = "";
        if (save && text.Length > 0 && slot >= 0)
        {
            QuickStore.Set(slot, QKind.Hotkey, text);
            App.Log($"窗口F: 第 {slot + 1} 格保存快捷键 {text.Length} 字");
        }
        else
        {
            App.Log("窗口F: 退出录键" +
                (text.Length > 0 ? $"(没保存) {text.Length} 字" : "(一个键都没记)"));
        }
        QuickUI?.Refresh();
        Dirty = true;
    }

    /// <summary>深层 3 号键"删除": 清空这一格。**位置留着** —— 后面的不往前挪。</summary>
    void QuickDeleteSlot(int slot)
    {
        if (QuickStore.Empty(slot)) return;
        QuickStore.Remove(slot);
        App.Log($"窗口F: 删掉第 {slot + 1} 格");
        QuickUI?.Pop();                        // 删完退回列表
        QuickUI?.Refresh();
        Dirty = true;
    }

    /// <summary>数字层: 把这一格绑到数字 num(1~9)。对面那一格有东西就**互换**。</summary>
    void QuickBindSlot(int slot, int num)
    {
        int to = QuickStore.BindNum(slot, num);
        if (to < 0) return;
        App.Log($"窗口F: 绑到数字 {num}");
        // 这一条已经搬到数字 {num} 那一格了 —— 深层跟着改认那一格, 不然它还钉在
        // 原来那个(现在空了的)格子上: 底下那条长条成了空的, 接着改就改错了地方。
        QuickUI?.Pop();
        QuickUI?.Move(to);
        QuickUI?.Refresh();
        Dirty = true;
    }

    /// <summary>
    /// 绑数字层按 **Y** = 解除绑定(2.3.3 之前是 L3): 这一格的内容搬到**第 2 页往后
    /// 第一个空位**, 数字位就空出来了。和九键数字层里"Y 出 0"是同一个手势 ——
    /// 不用在浮层里再摆一颗"解除绑定"的按钮给你先用 ↓ 挪过去、再按 A。
    /// </summary>
    void QuickUnbindSlot(int slot)
    {
        int to = QuickStore.Unbind(slot);
        if (to < 0)
        {
            App.Log("窗口F: 第 2 页往后都满了, 没地方挪, 没解除");
            Dirty = true;
            return;
        }
        App.Log($"窗口F: 解除绑定, 挪到第 {to + 1} 格");
        QuickUI?.Pop();                        // 退回深层, 并且跟着这一条走(见 Move)
        QuickUI?.Move(to);
        QuickUI?.Refresh();
        Dirty = true;
    }

    /// <summary>唤醒键存盘之后通知 App 一声 —— 键盘钩子要按新键重装。</summary>
    public Action WakeChanged;

    /// <summary>
    /// 追加一个键名(录快捷键用)。记完立刻同步进预览框 —— 框里那串就是你要保存的东西,
    /// 按 shift 打进 5 个字母、再按 r 自动补一个 + 号接在后面, 跟真在打字一样。
    /// </summary>
    void QuickAddToken(string token)
    {
        if (QBuf.Length == 0) QBuf = token;
        else
        {
            // 修饰键别重复记两遍(按住 shift 时每帧都会来一次)
            var parts = new List<string>(QBuf.Split('+'));
            if (parts.Contains(token)) return;
            QBuf += "+" + token;
        }
        if (_qNoteHas) QuickSetNote?.Invoke(QBuf);
        Dirty = true;
    }

    void QuickDropToken()
    {
        int i = QBuf.LastIndexOf('+');
        QBuf = i < 0 ? "" : QBuf[..i];
        if (_qNoteHas) QuickSetNote?.Invoke(QBuf);
        Dirty = true;
    }

    /// <summary>
    /// 发出一条。**要不要关掉窗口F, 两个版本不一样** —— 直出版是"打出去就完事",
    /// 面板留着还能接着发下一条; 预输入版发完要回去接着打字(或者让键落到游戏里),
    /// 面板挡在键盘区上就必须收掉。
    ///
    /// · 直出版: 快捷键、短语**都不关面板**。
    /// · 预输入版: 快捷键**关面板** → 焦点还给游戏 → 发键, 预览框里有没有字都不管
    ///   (不复制、不清空、不替你发出去, 就当没看见); 短语**接进预览框**并关面板。
    ///
    /// 以前预输入版发短语走"收起后"那套档位(复制 → Ctrl+V → 回车), 那套是为
    /// "收起键盘"设计的, 在 F 里用会顺手把焦点抢走、甚至把界面收起来 ——
    /// 1.2.1 里长按数字发短语就是这么既发不出去、又卡死在数字层的。现在不碰它了。
    /// </summary>
    public void QuickSend(QuickItem it)
    {
        if (it == null) return;

        if (it.Kind == QKind.Hotkey)
        {
            // 预输入版: 键要落到游戏里, 面板和预览框都得让开(预览框里的字不动)
            if (Preview) QuickClose();
            SendHotkey(it.Text);
            return;
        }

        App.Log($"窗口F: 发短语 {it.Text.Length} 字");
        if (Preview)
        {
            string cur = QuickNoteText?.Invoke() ?? "";
            QuickClose();                          // 先关 F, 接下来是用九键接着打
            QuickSetNote?.Invoke(cur + it.Text);
            App.Log("窗口F: 预输入版 -> 接在预览框里(不复制不发送)");
        }
        else
        {
            TextOutput.Text(it.Text);              // 直出版: 直接投字, 面板留着
            Dirty = true;
        }
    }

    /// <summary>
    /// 快捷键: 把 "alt+r" 这种拆成修饰键 + 主键发出去。
    /// **不在这一帧直接发** —— 刚把焦点要回来, SetForegroundWindow 还没生效,
    /// 这一串键会打进我们自己的预览框。记一笔, 到点(约 260ms 后)在 Poll 里补发。
    /// </summary>
    void SendHotkey(string spec)
    {
        bool ctrl = false, alt = false, win = false, shift = false;
        ushort vk = 0;
        foreach (var p in spec.Split('+'))
        {
            var t = p.Trim().ToLowerInvariant();
            if (t == "ctrl") ctrl = true;
            else if (t == "alt") alt = true;
            else if (t == "win") win = true;
            else if (t == "shift") shift = true;
            else if (t.Length == 1) vk = VkOfChar(t[0]);
            else vk = VkOfName(t);
        }
        if (vk == 0) { App.Log($"窗口F: 快捷键 \"{spec}\" 里没有主键, 没发"); return; }
        App.Log($"窗口F: 发快捷键 {spec}");
        QuickFocusGame?.Invoke();
        _chordMod = ModMask(ctrl, alt, win, shift);
        _chordVk = vk;
        // 用**这一帧的时间**记, 不用 DateTime.Now: 探针灌的是自己编的时间轴,
        // 混进真的系统时间就永远等不到"到点"那一帧, 也就测不到这一条。
        _chordAt = _now.AddMilliseconds(CHORD_DELAY_MS);
        Dirty = true;
    }

    /// <summary>发快捷键前留给"焦点回到游戏"的缓冲。</summary>
    const int CHORD_DELAY_MS = 260;

    static ushort ModMask(bool ctrl, bool alt, bool win, bool shift)
    {
        ushort m = 0;
        if (ctrl) m |= TextOutput.VK_CONTROL;
        if (alt) m |= TextOutput.VK_MENU;
        if (win) m |= TextOutput.VK_LWIN;
        if (shift) m |= TextOutput.VK_SHIFT;
        return m;
    }

    static ushort VkOfChar(char c)
    {
        if (c >= 'a' && c <= 'z') return (ushort)('A' + (c - 'a'));
        if (c >= 'A' && c <= 'Z') return (ushort)c;
        if (c >= '0' && c <= '9') return (ushort)c;
        if (c >= '①' && c <= '⑨') return (ushort)('1' + (c - '①'));
        return (ushort)char.ToUpperInvariant(c);
    }

    static ushort VkOfName(string t) => t switch
    {
        "space" => TextOutput.VK_SPACE,
        "enter" => TextOutput.VK_RETURN,
        "esc" => TextOutput.VK_ESCAPE,
        "tab" => TextOutput.VK_TAB,
        "up" => TextOutput.VK_UP,
        "down" => TextOutput.VK_DOWN,
        "left" => TextOutput.VK_LEFT,
        "right" => TextOutput.VK_RIGHT,
        // 录键页能记下来的修饰键 / Caps —— 当主键用(单个修饰键当唤醒键很不合理,
        // 但用户明说要支持), 也让解析别在"录下来什么就存什么"上出岔子。
        "shift" => 0x10,
        "ctrl" => 0x11,
        "alt" => 0x12,
        "win" => 0x5B,
        "caps" => 0x14,
        "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
        "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
        "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
        _ => 0,
    };

    /// <summary>
    /// 唤醒键存档里那一截("f7")换成 VK。只认 F1~F12 —— 键盘唤醒键就这十二个选项
    /// (见 <see cref="KeyWakeOptions"/>), 认不出来返回 0, App 那边会退回 L3+R3。
    /// </summary>
    public static int VkOfWakeKey(string t)
    {
        var s = t.Trim();
        if (s.Length < 2 || (s[0] != 'f' && s[0] != 'F')) return 0;
        if (!int.TryParse(s[1..], out int n) || n < 1 || n > 12) return 0;
        return 0x6F + n;        // VK_F1 = 0x70
    }

    /// <summary>26 键上这一颗在"录快捷键"时该记成什么。</summary>
    static string TokenOf26(KKey k)
    {
        switch (k.Kind)
        {
            case KKind.Char: return k.CharNow(false, false).ToString().ToLowerInvariant();
            case KKind.Space: return "space";
            case KKind.Caps: return "caps";
            case KKind.Shift: return "shift";
            case KKind.Mod:
                if (k.Vk == TextOutput.VK_CONTROL) return "ctrl";
                if (k.Vk == TextOutput.VK_MENU) return "alt";
                if (k.Vk == TextOutput.VK_LWIN) return "win";
                return "mod";
            case KKind.Vk:
                for (int f = 1; f <= 12; f++) if (k.Vk == 0x6F + f) return "f" + f;
                return k.Vk switch
                {
                    TextOutput.VK_RETURN => "enter",
                    TextOutput.VK_TAB => "tab",
                    TextOutput.VK_UP => "up",
                    TextOutput.VK_DOWN => "down",
                    TextOutput.VK_LEFT => "left",
                    TextOutput.VK_RIGHT => "right",
                    _ => k.Label.ToLowerInvariant(),
                };
            default: return k.Label.ToLowerInvariant();
        }
    }

    /// <summary>
    /// 长按计时。两件事都在这里判:
    ///   1) 九宫格<b>快捷那一格</b>(默认正中间的 5, 开了互换是左上角的 1)
    ///      长按 → 开窗口F(短按照旧没反应, 所以从它上面经过一下不会被误触);
    ///   2) 数字层里按住一个**绑了短语**的数字 → 到点就发那条短语(而不是连发数字)。
    /// F 里那个"长按 A 进深层"不在这儿 —— 那归 HoldQuickA(那一摞已经接管 A 了)。
    /// </summary>
    void TickQuickHold(ushort btn, DateTime now)
    {
        bool aDown = (btn & GamepadInput.A) != 0;

        // 1) 九宫格的快捷格
        if (aDown && !QuickMode && !Settings && !Punct
            && Layout == Layout.CN9 && Focus == FocusZone.Keyboard && Key9 == QuickCell)
        {
            if (!_quickFired && (now - _quickAt).TotalMilliseconds >= QUICK_HOLD_MS)
            {
                _quickFired = true;
                App.Log("窗口F: 长按" + (QuickCell == 5 ? "中心键" : "左上角") + "打开");
                QuickOpenPanel();
            }
            return;
        }

        // 2) 数字层: 按下时先不出数字, 等一等看你是"点一下"还是"按住"
        if (_numPending != 0)
        {
            if (!aDown) return;              // 松手在 ReleaseQuickHold 里补一个数字
            if ((now - _quickAt).TotalMilliseconds >= QUICK_HOLD_MS)
            {
                var q = QuickStore.ByNum(_numPending);
                _numPending = 0;
                _quickFired = true;
                if (q != null) QuickSend(q);
            }
        }
    }

    /// <summary>松手: 短按的那个数字补发出来(长按已经发过短语的就不补了)。</summary>
    void ReleaseQuickHold()
    {
        if (_numPending != 0)
        {
            int d = _numPending;
            _numPending = 0;
            if (!_quickFired) TextOutput.Digit(d);
        }
        _quickFired = false;
        _quickAt = default;
    }

    /// <summary>按下 A 的那一刻记个时间(长按判定要用)。</summary>
    void MarkPressDown(DateTime now) { _quickAt = now; _quickFired = false; }

    // ================= UI 辅助 =================
    // 九键自己不出汉字了, 所以"候选行 / 网格"里装的只剩两类东西:
    // 符号面板(Punct)和展开的拼音切分(GridPinyin)。
    public int CandRowCount() => Punct ? Math.Min(CandPerRow, PunctsNow().Length) : 0;
    /// <summary>网格里一共几条(不分页)。</summary>
    public int GridTotal() => Punct ? GridPageSize
                                   : GridPinyin ? Math.Max(0, Segs.Count - GridOffset)
                                   : 0;
    /// <summary>**当前这一页**有几条。光标和界面都用这个 —— 分页后它不再是总数。</summary>
    public int GridCount() => Math.Clamp(GridTotal() - GridPageStart, 0, GridPageSize);

    public List<string> CandRowItems() =>
        Punct ? PunctsNow().Take(CandRowCount()).ToList() : new List<string>();

    /// <summary>
    /// 符号网格**正中间那一格**: 它是一颗真格子(会画出来、有底色、光标能停、
    /// 选中时也跟着放大跳一下), 但装的**不是符号**, 而是当前分类的名字
    /// (常用 / 中文 / 英文 / 数学 / 箭头 / 数字 / 单位 / 图形)。
    /// <para>为什么不空着了: 空着是怕"松手就落在那儿, 放个符号等于每次收手
    /// 顺手打出一个来"。装个**标签**就没这毛病 —— 按 A 什么也不会发生
    /// (见 <see cref="PunctIdxOfGrid"/>)。而"现在在第几类"这件事本来就该
    /// 写在眼睛正看着的地方: 以前是单独挂一块牌子在卡片**下面**, 现在就在
    /// 正中间, 挪都不用挪眼。</para>
    /// <para>光标**默认就停在它上面** —— 进面板、换了类、松手回中, 三处都
    /// 落在它上, 和九键那颗"5"是同一个规矩(见 HandleStick 里的松手分支)。</para>
    /// </summary>
    public int GridCenter => (GridRowsNow / 2) * GridColsNow + GridColsNow / 2;

    /// <summary>网格第 i 格装的是第几个符号。**正中间那格返回 -1** —— 它装的是
    /// 分类名(见 <see cref="GridCenter"/>), 不是符号, 所以按 A 什么也不打。</summary>
    public int PunctIdxOfGrid(int i)
    {
        int c = GridCenter;
        return i < c ? i : i > c ? i - 1 : -1;
    }

    public List<string> GridItems()
    {
        if (Punct)
        {
            // 一页 25 格 = 24 个符号 + 正中间那格写**当前分类名**(见 GridCenter)。
            var src = PunctsNow();
            var res = new List<string>();
            for (int i = 0; i < GridPageSize; i++)
            {
                int k = PunctIdxOfGrid(i);
                // k < 0 就是正中间那一格 —— 它是"标签格", 不装符号。
                res.Add(k < 0 ? PunctCatName : k < src.Length ? src[k] : "");
            }
            return res;
        }
        // 拼音网格只列"行里放不下"的那几条, 不重复拼音行已经摆着的那批。
        // 再按当前页切一刀 —— 一页只摆 15 条, 多了翻页(格子才不会压扁)。
        if (GridPinyin)
            return Segs.Skip(GridOffset + GridPageStart).Take(GridPageSize)
                       .Select(s => string.Join("'", s)).ToList();
        return new List<string>();
    }

    /// <summary>网格第 i 格对应 <see cref="Segs"/> 的哪一条(网格有 GridOffset 的偏移)。</summary>
    public int SegIdxOfGrid(int i) =>
        Math.Clamp(GridOffset + GridPageStart + i, 0, Math.Max(0, Segs.Count - 1));

    /// <summary>
    /// 光标**此刻**选中的是 <see cref="Segs"/> 的哪一条。发拼音(RB)、切分(Y)、
    /// 上屏(A)、收起补发 —— 凡是"对光标选中的那条动手"的地方全走这一处。
    /// <para>不许各自去读 <see cref="PinyinIdx"/>: 网格展开时光标在网格里, 而网格
    /// 只装"拼音行摆不下"的那几条(下标得加 <see cref="GridOffset"/>), 直接读
    /// PinyinIdx 读到的是**行内最后一条** —— 1.0.8 里在全部拼音网格里按 Y,
    /// 切出来的却是拼音行最后那条的头一个音节, 根子就在这儿。</para>
    /// </summary>
    public int SegIdxNow => Math.Clamp(
        (GridPinyin && Focus == FocusZone.CandGrid) ? SegIdxOfGrid(GridIdx) : PinyinIdx,
        0, Math.Max(0, Segs.Count - 1));

    /// <summary>拼音行: 同一数字串的多种切分, 横向排列, 十字键左右选。</summary>
    public List<string> PinyinRowItems()
    {
        if (Punct || Segs.Count == 0) return new List<string>();
        int start = PinyinRowStart(), n = Segs.Count;
        var res = new List<string>();
        for (int i = start; i < Math.Min(start + FitNow, n); i++)
        {
            var s = Segs[i];
            // 用撇连接, 和真正发给输入法的串一模一样 —— 用户看到什么就会发出什么
            res.Add(s.Length == 0 ? "" : string.Join("'", s));
        }
        return res;
    }

    /// <summary>拼音行的显示窗口起点。现在固定从头显示, 不再滚动(见 MovePinyin 里的说明)。</summary>
    public int PinyinRowStart() => 0;

    public string StatusDisplay()
    {
        // 调窗口那一档: 整套提示换成这一档的(十字键 / 摇杆在这一档里是缩放和摆位,
        // 不是选键; 出去是 B, 不是 X), 照着别的提示按会什么都不发生。
        if (Resizing)
            return $"调窗口 · {(int)(Scale * 100)}% · 十字键上下缩放 · 左摇杆挪位置 · B 存好退出";
        string nav = Nav == NavMode.Stick ? "摇杆" : "十字键";
        string tail = PadConnected ? nav : nav + " · 未检测到手柄";

        // 窗口F: 这一层跟打字那套完全不一样(没有拼音、没有候选), 提示必须整套换掉,
        // 否则状态栏还写着"十字键=方向键", 你照着按却什么都没发生 —— 1.2.0 里
        // 就是这么让人以为"点了没反应"的。
        if (QEdit >= 0)
        {
            string keys = QBuf.Length > 0 ? " → " + QBuf : "";
            return $"录快捷键{keys} · 摇杆/十字键选键 · A 记下 · X 删最后一个 · Y 保存 · B 放弃";
        }
        if (QuickMode)
        {
            string layer = QuickUI?.Layer ?? Q_LAYER_LIST;
            if (layer == Q_LAYER_NUM)
                return "绑数字 · 摇杆/十字键挑 1~9 · A 定下 · Y 解除绑定 · B 返回";
            if (layer == Q_LAYER_DEEP)
                return "改这一格 · 1 写短语 · 2 写快捷键 · 3 删除 · 中间改绑数字 · B 返回";
            // 列表层: 短按是"发", 长按是"改" —— 这一条必须写出来, 不然没人知道
            // 还有长按这一回事。
            string t2 = "摇杆/十字键选格 · A 发出 · 长按 A 改这一格 · LB/RB 翻页 · B 关闭";
            return HasNote ? $"快捷 · {t2}"
                           : $"快捷 · {t2} · 直出版没有预览框, 写短语用不了";
        }

        // 符号面板: 得说清楚现在是第几类, 不然按 LB/RB 换过去用户不知道换到哪了
        if (Punct)
            return $"符号 · {PunctCatName} ({PunctCat + 1}/{PunctCatCount}) · LB/RB 换类 · A 输入 · X 退格 · B 返回 · {tail}";

        // 两个版本的打法完全一样(九键只解拼音、26 键只出英文), 只有"字去哪儿"不同,
        // 所以这里只用一个前缀区分, 剩下共用一套提示。
        string mode = Preview ? "预输入" : "直出";
        string zh = ImeChinese ? "中" : "英";

        if (Layout == Layout.NUM9) return $"{mode} · 数字 · 十字键=方向键 · {tail}";
        if (Layout == Layout.EN26)
        {
            string st = "";
            if (Caps) st += " 大写";
            if (ShiftOn) st += " Shift";
            if (TextOutput.ModCtrl) st += " Ctrl";
            if (TextOutput.ModAlt) st += " Alt";
            if (TextOutput.ModWin) st += " Win";
            // 26 键的选键只剩摇杆了(十字键改成方向键, 好挪已经打出来的字)
            return $"{mode} · 英文{st} · 摇杆选键 · LB=Shift(打 @#) · 十字键=方向键 · {tail}";
        }

        // 网格只收"拼音行放不下的那几条", 所以这里报的是网格里真正的条数。
        // 分了页就把"第几页"也写出来 —— 不然翻到第二页会以为只有这几条。
        if (Focus == FocusZone.CandGrid && GridPinyin)
        {
            string pg = GridPageCount > 1 ? $" (第 {GridPage + 1}/{GridPageCount} 页)" : "";
            return $"更多拼音 {GridTotal()} 种{pg} · 十字键选 · ↑↓ 翻页 · RB 发出 · Y 切头 · B 返回 · {tail}";
        }

        // 符号面板: 摇杆直接落点(不再一格一格挪), LB / RB 换分类
        if (Punct && Focus == FocusZone.CandGrid)
            return $"全部符号 · {PunctCatName} {PunctsNow().Length} 个 · 摇杆选 · "
                 + $"LB/RB 换类 · A 上屏 · B 返回 · {tail}";

        string d = Digits.Length > 0 ? " [" + Digits + "]" : "";
        string py = SentPinyin.Length > 0 ? " 已发:" + SentPinyin : "";
        // 正拼着拼音: 十字键这时候归拼音(左右换切分), 提示要跟着变
        if (PinyinPicking)
            return $"拼音 {PinyinIdx + 1}/{Segs.Count}{d} · ←→ 换切分 ↓ 全部 · RB 发出 · Y 切头";
        if (ImePicking)
            return $"选词中{py} · 十字键=方向键 · RB 上屏 · X 退";
        return $"九键({zh}){d} · 十字键=方向键(挪光标) · 摇杆选键 · X 退格{py}";
    }
}
