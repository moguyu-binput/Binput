namespace Binput;

/// <summary>
/// 滑行输入的开关。就两档 —— 开(打字)和关。
/// <para>beta 那版中间还夹过一档 Shadow(影子: 只显示不打字, 专门给标定攒样本)。
/// 它已经被砍掉了: 滑出来的那串数字和拼音行里显示的是同一串, 再挂一条"影子"出来
/// 纯属自己跟自己重复, 而为了显示它还得让整个面板多长出一格。标定改成直接从
/// 打字过程里取样(见 InputCore.TickSlide 里 _refBad 那段)。</para>
/// </summary>
public enum SlideMode
{
    /// <summary>关。LB 还是原来那个 LB, 手柄上多不出任何东西。</summary>
    Off = 0,
    /// <summary>开。每停稳一格就往拼音里加一个数字。</summary>
    Type = 1,
}

/// <summary>
/// 一格驻留下来之后是怎么算的。
/// Keep = 待够久了, 记进序列里了
/// Drop = 没待够, 当你是路过蹭过去的, 丢掉
/// Free = 松手那一下兜底补上的(最后一格)。**不进标定统计** ——
///        它是"落点免费"的优惠, 驻留可能只有三四十毫秒, 拿它去统计会把
///        "Keep 最短多久"那条线整个拉塌, 阈值就跟着崩到没法用了。
/// </summary>
public enum SlideVerdict { Keep, Drop, Free }

public readonly struct SlideStep
{
    public readonly int Key;
    public readonly int Ms;
    public readonly SlideVerdict Verdict;
    public SlideStep(int key, int ms, SlideVerdict v) { Key = key; Ms = ms; Verdict = v; }

    public string Tag => Verdict switch
    {
        SlideVerdict.Keep => "KEEP",
        SlideVerdict.Free => "FREE",
        _ => "DROP",
    };
}

/// <summary>
/// 滑行输入的识别器: 吃摇杆点位, 吐出一串数字。
/// <para>它不知道什么是"输入", 只管把摇杆画出来的路径算成一串数字放在 <see cref="Seq"/> 里。
/// 这串数字怎么用由外面决定: 影子模式下只拿它显示和记录, 正式模式才喂给拼音。
/// 分开的好处是这份逻辑能被探针单独拎出来测, 不用真的去按手柄。</para>
/// <para>判定只看**方向 + 在这个方向上待多久**, 完全不看推了多远 ——
/// 早期试过"只有推到外圈才算数", 结果用户从 9 滑到 4 习惯沿着外围绕,
/// 一路蹭过的 3、2、1 全被当成真的键, 那版直接废了。</para>
/// </summary>
public sealed class SlideTracker
{
    /// <summary>推出多远才算离开死区。摇杆有物理回中抖动, 太小会把"松手"判成"还在某个方向"。</summary>
    public double MinR = 0.5;

    /// <summary>
    /// 在一个方向上待够这么久, 才把它记进序列。
    /// **从设置页的几档里挑**(见 Config.DWELL_STEPS), 不再自动标定 ——
    /// 早期版本会拿你滑的记录反推一个数, 可那 12 条样本永远攒不够,
    /// 按钮按了没反应; 而且滑得顺不顺是手感, 自己拨两档比算法快得多。
    /// </summary>
    public int DwellMs = 80;

    /// <summary>
    /// 在一个方向上已经认下之后, **接着连发**的间隔(连着记同一个键, 打 66 / 777 这类)。
    /// **和 <see cref="DwellMs"/> 是两件事**: DwellMs 是"多久才认它", 这是"认下之后
    /// 每隔多久再给一个" —— 前者管灵敏度, 后者管重复速度, 从设置页各自拨各自的档
    /// (见 Config.REPEAT_STEPS)。
    /// </summary>
    public int RepeatMs = 300;

    public bool Active;

    /// <summary>
    /// 每确定一个键就回调一次(参数是那个数字)。打字模式拿它把数字喂进拼音 ——
    /// **边滑边进**, 不用等松手: 手还在画的时候候选栏已经在跟着变了,
    /// 滑到一半发现拼错了能马上知道, 而不是松手后才看到一串不对的东西。
    /// 影子模式外层不挂它, 于是这个类和"要不要真的打字"完全没有关系。
    /// </summary>
    public Action<int> OnCommit = Noop;

    /// <summary>什么都不做的那份。给影子模式用(那里不挂回调)。</summary>
    public static readonly Action<int> Noop = _ => { };

    /// <summary>这次滑出来的数字串("396" 这样)。</summary>
    public string Seq => _seq;

    /// <summary>当前所在的方向, 1..9。<b>0 = 还在死区里</b>, -1 = 压根没在滑。</summary>
    public int Sector = -1;

    /// <summary>当前这一格的驻留进度 0..1, 界面拿它画那个"再停一下就记了"的环。</summary>
    public double Progress;

    /// <summary>这一次滑动里每一格的账, 松手时整批交给外面去存。</summary>
    public List<SlideStep> Steps => _steps;

    string _seq = "";
    readonly List<SlideStep> _steps = new();
    int _prev;                  // 上一帧在哪个扇区(0 = 死区)
    DateTime _at;               // 进入当前扇区的时刻
    bool _fired;                // 当前扇区已经记进序列了
    DateTime _repeatNext;       // 连发的下一次
    DateTime _start;

    /// <summary>按下 LB: 新的一次滑动开始。</summary>
    public void Begin(DateTime now)
    {
        _seq = "";
        _steps.Clear();
        _prev = 0;
        _fired = false;
        _at = now;
        _start = now;
        Sector = -1;
        Progress = 0;
        Active = true;
    }

    /// <summary>摇杆动了。nx / ny 是 -1..1 的归一化点位, 向上为正。</summary>
    public void Update(double nx, double ny, DateTime now)
    {
        if (!Active) return;

        double r = Math.Sqrt(nx * nx + ny * ny);
        int sec = r >= MinR ? SectorOf(nx, ny) : 0;

        if (sec != _prev)
        {
            LeavePrev(now);                 // 先把上一格的账结了
            _prev = sec;
            _at = now;
            _fired = false;
            _repeatNext = default;
        }

        Sector = sec;

        // 只有"摇杆还在正中间"这一个方向不参与。
        // 早先左上角那个 1 也被剔掉了(那时候 1 号键在九键拼音里没功能),
        // 但 2.0.0 已经把 JKL 挪到了左上角那一格 —— 八个方向现在**都有字母**,
        // 一圈转下来 a~z 一个不落。这一改是把当初那条"绕开 1"的规矩撤了。
        if (sec == 0) { Progress = 0; return; }

        int ms = (int)(now - _at).TotalMilliseconds;
        Progress = Math.Min(1, ms / (double)DwellMs);

        if (!_fired && ms >= DwellMs)
        {
            Commit(sec);
            _repeatNext = now.AddMilliseconds(RepeatMs);
        }
        else if (_fired && now >= _repeatNext)
        {
            Commit(sec);
            _repeatNext = now.AddMilliseconds(RepeatMs);
        }
    }

    /// <summary>
    /// 松开 LB: 收尾。**松手就是松手, 不再送最后一格**。
    /// <para>2.1.34 以前这里会把最后停着的那格无条件补上(Free 那一档) —— 理由是
    /// "你要的键肯定在最后停着的地方, 手已经停在那儿了, 只差多停一会儿才够阈值"。
    /// 听着贴心, 用起来别扭: ① 想"路过一格不留痕"做不到了, 只要最后停在任何一格上
    /// 都会被补一个键, 于是不得不刻意回到死区再松手; ② 手松开和摇杆回中不是同一件事,
    /// 松手瞬间摇杆常常还在某个方向上, 补出来的那个键往往不是原意; ③ 这一格白来的,
    /// 连发的规矩对它失效, 同一趟里两种判据。
    /// 现在和中间经过的格一个规矩: **只有住够了的才记账**, 没住够就丢(Drop)。</para>
    /// </summary>
    public void Finish(DateTime now)
    {
        if (Active && _prev != 0)
        {
            int ms = (int)(now - _at).TotalMilliseconds;
            _steps.Add(new SlideStep(_prev, ms, _fired ? SlideVerdict.Keep : SlideVerdict.Drop));
        }
        Active = false;
        Sector = -1;
        Progress = 0;
        _prev = 0;
        _fired = false;
    }

    /// <summary>这次滑动从按下到松手一共多久, 记录里顺手写下来。</summary>
    public int ElapsedMs(DateTime now) => (int)(now - _start).TotalMilliseconds;

    void LeavePrev(DateTime now)
    {
        if (_prev == 0) return;                 // 死区不记账
        int ms = (int)(now - _at).TotalMilliseconds;
        _steps.Add(new SlideStep(_prev, ms, _fired ? SlideVerdict.Keep : SlideVerdict.Drop));
    }

    void Commit(int key)
    {
        _seq += (char)('0' + key);
        _fired = true;
        OnCommit(key);
    }

    /// <summary>
    /// 摇杆方向 -> 九宫格的键。八个 45 度扇区正好对上外圈那八颗键。
    /// 转不转都是这套标准分布, ±22.5 度的容错对手柄来说很宽。
    /// </summary>
    public static int SectorOf(double nx, double ny)
    {
        double ang = Math.Atan2(ny, nx) * 180.0 / Math.PI;      // -180..180, 0 = 正右
        int idx = (int)Math.Floor((((ang + 360) % 360) + 22.5) / 45) % 8;
        //            0°→6   45°→3   90°→2   135°→1   180°→4   225°→7   270°→8   315°→9
        return idx switch
        {
            0 => 6, 1 => 3, 2 => 2, 3 => 1,
            4 => 4, 5 => 7, 6 => 8, _ => 9,
        };
    }
}
