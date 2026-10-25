namespace Binput;

/// <summary>
/// 三条手柄通道的**合流 + 仲裁**。
///
/// <para>三条路各有各的地盘(谁都不认识全部手柄):
/// <list type="bullet">
/// <item><see cref="GamepadInput"/> —— XInput。只认 Xbox 原装和第三方手柄的 XInput 档。</item>
/// <item><see cref="RawPad"/> —— Windows.Gaming.Input。认得 Switch Pro 这类, 按键是
///       微软映射好的。<b>认不出 PS5 DualSense。</b></item>
/// <item><see cref="HidPad"/> —— 自己开 HID + HidP。专治微软不认的设备(PS5 就靠它)。</item>
/// </list></para>
///
/// <para><b>不能"谁先接上用谁"</b>: 那样前面那条只要报一句"我连上了", 后面那条就
/// 永远轮不到(2.4.1 的毛病: PS5 单独连能用, 多接一只就完全失效)。</para>
///
/// <para>也<b>不能简单地把三路并起来</b>: 那样两只手柄会一起说话 —— 一只手把摇杆
/// 搁在那儿, 另一只就不听使唤了; 更要命的是**卡住的摇杆**: 某只手柄的摇杆一直报
/// "推向右上角", 并起来就变成每帧都在推, 标点层被顶开, 表现就是"几只手柄全失效"。</para>
///
/// <para>所以是**仲裁**: <b>谁在动就听谁的</b>, 而且同一时刻只听一只。
/// 拿起哪只哪只能使, 放下换另一只也能接着使, 再换回来还行。</para>
/// </summary>
public static class PadBus
{
    /// <summary>摇杆"算推着了"的门槛。和 <c>InputCore.DZ</c> 同一个标准, 别各写各的。</summary>
    const int DZ = 9000;
    /// <summary>扳机算"在动"的最小量(0~255)。压一点点不算。</summary>
    const int TRIG_MIN = 40;
    /// <summary>
    /// 松手之后还认这只多久。太短的话两次按键之间(比如打完一个字松手那一下)
    /// 就会被别的手柄抢走, 于是两只手柄来回跳。
    /// </summary>
    const int GRACE_MS = 2000;

    static int _cur = -1;                  // 当前听谁的: 0=XInput 1=WinRT 2=HID; -1 = 没人动
    static long _lastAct;                  // 上一次"有人在动"的时刻
    static readonly bool[] _btnRest = new bool[3];   // 这只的按键**有没有松开过**
    static readonly bool[] _restL = new bool[3];     // 左摇杆有没有回中过
    static readonly bool[] _restR = new bool[3];     // 右摇杆有没有回中过

    /// <summary>
    /// 三条路一起问一次, 仲裁出一个状态。
    /// <paramref name="src"/> 是给日志看的名字, 比如 "XInput + Switch Pro · 在用 Switch Pro"。
    /// </summary>
    public static PadState Read(out string src)
    {
        var x = Safe(() => GamepadInput.GetStateAny());
        var r = Safe(() => RawPad.GetState());
        var h = Safe(() => HidPad.GetState());

        var s = new[] { x, r, h };
        long now = Environment.TickCount64;

        // ---- ① 先认"哪些输入是可信的" ----
        // 卡住的输入有个共同点: 它**从没回过中**。真摇杆总会有松手回中的时候,
        // 真按键也总有松开的时候。一次都没见过它回中 / 松开, 那就是卡住了 ——
        // 这种输入一律不信(既不参与仲裁, 也不往外传)。
        var act = new bool[3];
        for (int i = 0; i < 3; i++)
        {
            // ⚠ 只在这一路**真的连着**的时候才记账。不然手柄没连上时状态是全 0,
            //   会被记成"它回中过" —— 等一只摇杆有漂移的手柄连上来, 那道闸就白设了。
            if (!s[i].Connected) continue;

            if (s[i].Buttons == 0) _btnRest[i] = true;
            if (Hypot(s[i].LX, s[i].LY) < DZ) _restL[i] = true;
            if (Hypot(s[i].RX, s[i].RY) < DZ) _restR[i] = true;

            bool btn = _btnRest[i] && s[i].Buttons != 0;
            bool trig = s[i].LeftTrigger >= TRIG_MIN || s[i].RightTrigger >= TRIG_MIN;
            bool stick = (_restL[i] && Hypot(s[i].LX, s[i].LY) >= DZ)
                       || (_restR[i] && Hypot(s[i].RX, s[i].RY) >= DZ);
            act[i] = btn || trig || stick;
        }

        // ---- ② 仲裁: 谁在动就听谁的 ----
        if (_cur >= 0 && act[_cur]) _lastAct = now;                 // 还是它在动, 继续听它
        else
        {
            int k = -1;
            for (int i = 0; i < 3; i++) if (act[i]) { k = i; break; }
            if (k >= 0) { _cur = k; _lastAct = now; }               // 换人了
            else if (_cur >= 0 && now - _lastAct > GRACE_MS) _cur = -1;   // 都松手够久了, 回到"谁先动谁上"
        }

        bool any = x.Connected || r.Connected || h.Connected;
        src = Join(x.Connected ? "XInput" : "",
                   r.Connected ? (string.IsNullOrEmpty(RawPad.Name) ? "WinRT" : RawPad.Name) : "",
                   h.Connected ? HidPad.Name : "");
        if (_cur >= 0) src += " · 在用 " + Who(_cur);

        // ---- ③ 出状态 ----
        if (_cur >= 0)
        {
            // 只听这一只。按键 / 摇杆各自过一遍"可信"那道闸。
            var a = s[_cur];
            return new PadState
            {
                Connected = any,
                Buttons = _btnRest[_cur] ? a.Buttons : (ushort)0,
                LeftTrigger = a.LeftTrigger, RightTrigger = a.RightTrigger,
                LX = _restL[_cur] ? a.LX : (short)0,
                LY = _restL[_cur] ? a.LY : (short)0,
                RX = _restR[_cur] ? a.RX : (short)0,
                RY = _restR[_cur] ? a.RY : (short)0,
            };
        }

        // 没人动: 按键**并起来**(这样拿起任意一只按一下就能接管),
        // 摇杆一律 0 —— 幽灵摇杆就是这么挡掉的。
        return new PadState
        {
            Connected = any,
            Buttons = (ushort)((_btnRest[0] ? x.Buttons : 0) | (_btnRest[1] ? r.Buttons : 0)
                             | (_btnRest[2] ? h.Buttons : 0)),
            LeftTrigger = Max3(x.LeftTrigger, r.LeftTrigger, h.LeftTrigger),
            RightTrigger = Max3(x.RightTrigger, r.RightTrigger, h.RightTrigger),
            LX = 0, LY = 0, RX = 0, RY = 0,
        };
    }

    /// <summary>哪一路在用(日志里好看一点)。</summary>
    static string Who(int i) => i switch
    {
        0 => "XInput",
        1 => string.IsNullOrEmpty(RawPad.Name) ? "WinRT" : RawPad.Name,
        _ => string.IsNullOrEmpty(HidPad.Name) ? "HID" : HidPad.Name,
    };

    /// <summary>
    /// 一路读炸了不能把整帧带下水: 每帧的 Tick 就靠这一行活着, 一崩整个输入全停。
    /// </summary>
    static PadState Safe(Func<PadState> f)
    {
        try { return f(); }
        catch { return default; }
    }

    static double Hypot(short a, short b) => Math.Sqrt((double)a * a + (double)b * b);

    static byte Max3(byte a, byte b, byte c) => (byte)Math.Max(Math.Max(a, b), c);

    static string Join(params string[] parts) =>
        string.Join(" + ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
