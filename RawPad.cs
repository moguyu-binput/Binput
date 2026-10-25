using Windows.Gaming.Input;

namespace Binput;

/// <summary>
/// 备用手柄通道: 用 **<c>Windows.Gaming.Input</c>** —— 网页那个"手柄按键测试"
/// 背后用的就是同一套(浏览器的 HTML5 Gamepad API 在 Windows 上就是它实现的)。
///
/// <para>微软已经把按键归一化好了, 我们只管读, 不用管字节、用法号、报告格式 ——
/// <b>以后新手柄只要 Windows 认得, 这边一行都不用改</b>。</para>
///
/// <para>要扫**两张名单**, 这是被实测教训出来的:
/// <list type="bullet">
/// <item><c>Gamepad.Gamepads</c>: 现成的标准视图(A / B / X / Y / 十字键 / 扳机 /
///       摇杆全都归好一了)。实测 **Switch Pro 有时候只在这一张名单里**, 原始名单
///       是空的 —— 2.4.1 只扫了原始名单, 于是 Pro 连上了却完全没人读它。</item>
/// <item><c>RawGameController.RawGameControllers</c>: 原始视图, 能问到微软给每颗键
///       标的名字(LetterA / LeftBumper / Minus / Plus …)。有的手柄只在这一张里。</item>
/// </list>
/// 同一个手柄常常两张名单**都有** —— 必须去重, 而且要去得有讲究: 留原始那份,
/// 因为它带着**按钮标签**, 我们靠标签才知道"这颗是任天堂的 B 还是 Xbox 的 B"。</para>
///
/// <para>⚠ <b>PS5 DualSense 不在这两张名单里的任何一张</b>(微软一颗键都不认,
/// <c>FromGameController</c> 也返回 null)。那种设备交给 <see cref="HidPad"/>。</para>
/// </summary>
public static class RawPad
{
    sealed class Entry
    {
        public Gamepad Pad;                      // 标准视图(可能为 null)
        public RawGameController Raw;            // 原始视图(可能为 null)
        public string Name;
        public GameControllerButtonLabel[] Labels;   // 每颗键是什么, 只算一次
        public bool Swap;                        // A↔B、X↔Y 要换吗(任天堂布局)
        public bool[] Btn;
        public GameControllerSwitchPosition[] Sw;
        public double[] Ax;
    }

    static readonly List<Entry> _pads = new();
    static DateTime _nextScan;
    static string _name = "";
    static long _lastAt;

    public static void Start() { }      // 不用后台线程: 每帧问一次, 便宜得很
    public static void Stop() { }

    /// <summary>接的是哪一款(日志里用)。没接上时是空串。</summary>
    public static string Name =>
        DateTime.Now.Ticks - _lastAt < 300 * TimeSpan.TicksPerMillisecond ? _name : "";

    /// <summary>
    /// 读一次, **把所有认得的手柄并起来**。没接上就是 <c>Connected = false</c>。
    /// (哪一只说了算, 由 <see cref="PadBus"/> 去仲裁 —— 这里只管把数读全。)
    /// </summary>
    public static PadState GetState()
    {
        var now = DateTime.Now;
        // 定期重扫: 手柄可能是程序启动之后才连上的。
        // 而且实测这个接口的第一次枚举经常是**空的**(要等一会儿才吐设备),
        // 所以一台都没扫到的时候扫得更勤, 免得用户刚连上就以为不认。
        if (now >= _nextScan) Scan(now);

        ushort btn = 0;
        byte lt = 0, rt = 0;
        int lx = 0, ly = 0, rx = 0, ry = 0;
        long bestMag = 0;
        bool any = false;
        var names = new List<string>();

        foreach (var e in _pads)
        {
            if (!Read(e, out var st)) continue;
            any = true;
            if (!names.Contains(e.Name)) names.Add(e.Name);
            btn |= st.Buttons;
            if (st.LeftTrigger > lt) lt = st.LeftTrigger;
            if (st.RightTrigger > rt) rt = st.RightTrigger;
            long m = Math.Abs(st.LX) + Math.Abs(st.LY) + Math.Abs(st.RX) + Math.Abs(st.RY);
            if (m > bestMag) { bestMag = m; lx = st.LX; ly = st.LY; rx = st.RX; ry = st.RY; }
        }

        if (names.Count > 0) _name = string.Join(" + ", names);   // 接了好几只就都列出来
        return new PadState
        {
            Connected = any,
            Buttons = btn,
            LeftTrigger = lt, RightTrigger = rt,
            LX = (short)lx, LY = (short)ly, RX = (short)rx, RY = (short)ry,
        };
    }

    // ============================================================ 认设备
    static void Scan(DateTime now)
    {
        _pads.Clear();
        var covered = new List<Gamepad>();

        // ---- ① 原始名单(带着按钮标签, 优先) ----
        foreach (var c in RawGameController.RawGameControllers)
        {
            var e = new Entry
            {
                Raw = c,
                Pad = Gamepad.FromGameController(c),
                Name = string.IsNullOrEmpty(c.DisplayName) ? "手柄" : c.DisplayName,
                Labels = new GameControllerButtonLabel[c.ButtonCount],
                Btn = new bool[c.ButtonCount],
                Sw = new GameControllerSwitchPosition[c.SwitchCount],
                Ax = new double[c.AxisCount],
            };
            for (int i = 0; i < e.Labels.Length; i++) e.Labels[i] = c.GetButtonLabel(i);

            // ⚠ 有的设备(实测 **PS5 DualSense 就是**)虽然出现在原始名单里, 但微软
            //   一颗键都不认(标签全是 None), FromGameController 也返回 null。
            //   这种**必须跳过** —— 否则我们会拿着一个"永远全 0"的状态报"已连接",
            //   把后面真正能用的 HidPad 给挡住, 表现就是手柄彻底没反应。
            if (e.Pad == null && !e.Labels.Any(Useful)) continue;

            e.Swap = e.Labels.Any(IsNintendo);
            if (e.Pad != null) covered.Add(e.Pad);
            _pads.Add(e);
        }

        // ---- ② 标准名单里没被 ① 盖住的那几只 ----
        //     这一支拿不到标签, 认不出任天堂; 兜底问一句 HidPad(它能读到 VID/PID):
        //     系统里插着任天堂手柄、而这儿又是个没标签的标准手柄, 那八成就是它。
        bool nin = HidPad.SawNintendo;
        foreach (var g in Gamepad.Gamepads)
        {
            if (covered.Any(p => p == g)) continue;      // ① 里已经有了(那份带标签)
            _pads.Add(new Entry
            {
                Pad = g,
                Name = NameOf(g),
                Labels = Array.Empty<GameControllerButtonLabel>(),
                Swap = nin,
            });
        }

        _nextScan = now.AddSeconds(_pads.Count == 0 ? 0.5 : 2);
    }

    /// <summary>
    /// 任天堂的手柄要**按位置**对, 而微软是按符号标的: 底下那颗它标 LetterB、
    /// 右边那颗标 LetterA。我们要的是"底下那颗 = A"(和 Xbox 一样), 所以
    /// A↔B、X↔Y 换一下。判据用 Minus / Plus —— 这两颗标签只有任天堂家有。
    /// </summary>
    static bool IsNintendo(GameControllerButtonLabel l) =>
        l == GameControllerButtonLabel.Minus || l == GameControllerButtonLabel.Plus;

    /// <summary>标准手柄没 DisplayName, 只能回到原始名单里对一下是谁。</summary>
    static string NameOf(Gamepad g)
    {
        foreach (var c in RawGameController.RawGameControllers)
            if (Gamepad.FromGameController(c) == g && !string.IsNullOrEmpty(c.DisplayName))
                return c.DisplayName;
        return "手柄";
    }

    // ============================================================ 读一帧
    static bool Read(Entry e, out PadState st)
    {
        st = default;
        if (e.Pad == null && e.Raw == null) return false;

        ushort btn = 0;
        byte lt = 0, rt = 0;
        short lx = 0, ly = 0, rx = 0, ry = 0;

        // ---- 标准视图(归一化好的) ----
        if (e.Pad != null)
        {
            var r = e.Pad.GetCurrentReading();
            var b = e.Swap ? FaceSwap(r.Buttons) : r.Buttons;
            if ((b & GamepadButtons.A) != 0) btn |= GamepadInput.A;
            if ((b & GamepadButtons.B) != 0) btn |= GamepadInput.B;
            if ((b & GamepadButtons.X) != 0) btn |= GamepadInput.X;
            if ((b & GamepadButtons.Y) != 0) btn |= GamepadInput.Y;
            if ((b & GamepadButtons.LeftShoulder) != 0) btn |= GamepadInput.LB;
            if ((b & GamepadButtons.RightShoulder) != 0) btn |= GamepadInput.RB;
            if ((b & GamepadButtons.LeftThumbstick) != 0) btn |= GamepadInput.L3;
            if ((b & GamepadButtons.RightThumbstick) != 0) btn |= GamepadInput.R3;
            if ((b & GamepadButtons.View) != 0) btn |= GamepadInput.BACK;    // NS Pro 的 −
            if ((b & GamepadButtons.Menu) != 0) btn |= GamepadInput.START;   // NS Pro 的 +
            if ((b & GamepadButtons.DPadUp) != 0) btn |= GamepadInput.DPAD_UP;
            if ((b & GamepadButtons.DPadDown) != 0) btn |= GamepadInput.DPAD_DOWN;
            if ((b & GamepadButtons.DPadLeft) != 0) btn |= GamepadInput.DPAD_LEFT;
            if ((b & GamepadButtons.DPadRight) != 0) btn |= GamepadInput.DPAD_RIGHT;
            if (r.LeftTrigger > 0) lt = (byte)(r.LeftTrigger * 255);
            if (r.RightTrigger > 0) rt = (byte)(r.RightTrigger * 255);
            lx = ToShort(r.LeftThumbstickX); ly = ToShort(r.LeftThumbstickY);
            rx = ToShort(r.RightThumbstickX); ry = ToShort(r.RightThumbstickY);
        }

        // ---- 原始视图: 按微软给的标签再问一遍, 和上面取并集 ----
        if (e.Raw != null)
        {
            e.Raw.GetCurrentReading(e.Btn, e.Sw, e.Ax);
            for (int i = 0; i < e.Btn.Length; i++)
            {
                if (!e.Btn[i]) continue;
                var lab = e.Swap ? FlipLabel(e.Labels[i]) : e.Labels[i];
                switch (lab)
                {
                    case GameControllerButtonLabel.LetterA:
                    case GameControllerButtonLabel.XboxA: btn |= GamepadInput.A; break;
                    case GameControllerButtonLabel.LetterB:
                    case GameControllerButtonLabel.XboxB: btn |= GamepadInput.B; break;
                    case GameControllerButtonLabel.LetterX:
                    case GameControllerButtonLabel.XboxX: btn |= GamepadInput.X; break;
                    case GameControllerButtonLabel.LetterY:
                    case GameControllerButtonLabel.XboxY: btn |= GamepadInput.Y; break;
                    case GameControllerButtonLabel.LeftBumper:
                    case GameControllerButtonLabel.XboxLeftBumper: btn |= GamepadInput.LB; break;
                    case GameControllerButtonLabel.RightBumper:
                    case GameControllerButtonLabel.XboxRightBumper: btn |= GamepadInput.RB; break;
                    case GameControllerButtonLabel.LeftStickButton:
                    case GameControllerButtonLabel.XboxLeftStickButton: btn |= GamepadInput.L3; break;
                    case GameControllerButtonLabel.RightStickButton:
                    case GameControllerButtonLabel.XboxRightStickButton: btn |= GamepadInput.R3; break;
                    case GameControllerButtonLabel.Minus:
                    case GameControllerButtonLabel.XboxBack: btn |= GamepadInput.BACK; break;
                    case GameControllerButtonLabel.Plus:
                    case GameControllerButtonLabel.XboxStart: btn |= GamepadInput.START; break;
                    // Switch Pro 的 ZL / ZR 是开关量(压不了一半), 按住就当压到底
                    case GameControllerButtonLabel.LeftTrigger: lt = 255; break;
                    case GameControllerButtonLabel.RightTrigger: rt = 255; break;
                }
            }
            // ⚠ **标准视图给了数就别再拿原始轴去补**。这一句踩过坑: 有些设备的原始轴是
            //   **0~1**(中心 0.5)而不是 -1~1, 拿 ToShort 一乘就成了 16384 —— 于是手一松
            //   (标准视图回 0)它就把摇杆补成"推向右上角", 推着的时候反而正常。
            //   标准视图是微软归一化好的, 它给了数就听它的; 只有**没有**标准视图的
            //   设备(只在原始名单里露面的)才轮到原始轴。
            if (e.Pad == null)
            {
                if (e.Ax.Length >= 2) { lx = ToShort(e.Ax[0]); ly = ToShort(e.Ax[1]); }
                if (e.Ax.Length >= 4) { rx = ToShort(e.Ax[2]); ry = ToShort(e.Ax[3]); }
            }
        }

        _lastAt = DateTime.Now.Ticks;
        _name = e.Name;
        st = new PadState
        {
            Connected = true,
            Buttons = btn,
            LeftTrigger = lt, RightTrigger = rt,
            LX = lx, LY = ly, RX = rx, RY = ry,
        };
        return true;
    }

    /// <summary>A↔B、X↔Y 对换(只动这四颗; 十字键 / 肩键不动)。</summary>
    public static GamepadButtons FaceSwap(GamepadButtons b)
    {
        var r = b & ~(GamepadButtons.A | GamepadButtons.B | GamepadButtons.X | GamepadButtons.Y);
        if ((b & GamepadButtons.A) != 0) r |= GamepadButtons.B;
        if ((b & GamepadButtons.B) != 0) r |= GamepadButtons.A;
        if ((b & GamepadButtons.X) != 0) r |= GamepadButtons.Y;
        if ((b & GamepadButtons.Y) != 0) r |= GamepadButtons.X;
        return r;
    }

    /// <summary>标签版的对换(任天堂那四颗字母键)。</summary>
    public static GameControllerButtonLabel FlipLabel(GameControllerButtonLabel l) => l switch
    {
        GameControllerButtonLabel.LetterA => GameControllerButtonLabel.LetterB,
        GameControllerButtonLabel.LetterB => GameControllerButtonLabel.LetterA,
        GameControllerButtonLabel.LetterX => GameControllerButtonLabel.LetterY,
        GameControllerButtonLabel.LetterY => GameControllerButtonLabel.LetterX,
        _ => l,
    };

    /// <summary>这个标签我们认得吗(认不得的整台设备就当"微软没映射", 交给 HidPad)。</summary>
    static bool Useful(GameControllerButtonLabel l) => l switch
    {
        GameControllerButtonLabel.None => false,
        _ => true,
    };

    static short ToShort(double v) => (short)Math.Clamp(v * 32767, -32768, 32767);
}
