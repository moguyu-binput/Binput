using System.Runtime.InteropServices;

namespace Binput;

/// <summary>XInput 手柄状态</summary>
public struct PadState
{
    public bool Connected;
    public ushort Buttons;
    public byte LeftTrigger, RightTrigger;
    public short LX, LY, RX, RY;
}

public static class GamepadInput
{
    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern int XInputGetState14(int dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    static extern int XInputGetState9(int dwUserIndex, out XINPUT_STATE pState);

    public static PadState GetState(int index = 0)
    {
        int rc = TryGet(XInputGetState14, index, out var st);
        if (rc != 0) rc = TryGet(XInputGetState9, index, out st);
        if (rc != 0) return new PadState { Connected = false };

        return new PadState
        {
            Connected = true,
            Buttons = st.Gamepad.wButtons,
            LeftTrigger = st.Gamepad.bLeftTrigger,
            RightTrigger = st.Gamepad.bRightTrigger,
            LX = st.Gamepad.sThumbLX,
            LY = st.Gamepad.sThumbLY,
            RX = st.Gamepad.sThumbRX,
            RY = st.Gamepad.sThumbRY,
        };
    }

    /// <summary>
    /// 四个槽位扫一遍, 谁连着用谁。以前写死只读 0 号槽位 —— 插两只 Xbox 手柄的时候,
    /// 后接的那只如果正好占了 0 号槽位, 先接的那只就"失灵"了。
    /// </summary>
    public static PadState GetStateAny()
    {
        for (int i = 0; i < 4; i++)
        {
            var s = GetState(i);
            if (s.Connected) return s;
        }
        return new PadState { Connected = false };
    }

    delegate int GetStateFn(int index, out XINPUT_STATE state);
    static int TryGet(GetStateFn fn, int index, out XINPUT_STATE st)
    {
        try { return fn(index, out st); }
        catch (DllNotFoundException) { st = default; return 1; }
        catch (EntryPointNotFoundException) { st = default; return 1; }
    }

    // ---- 按钮位 ----
    public const ushort DPAD_UP = 0x0001, DPAD_DOWN = 0x0002, DPAD_LEFT = 0x0004, DPAD_RIGHT = 0x0008;
    public const ushort START = 0x0010, BACK = 0x0020, L3 = 0x0040, R3 = 0x0080;
    public const ushort LB = 0x0100, RB = 0x0200;
    public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
}

/// <summary>
/// 左摇杆 8 向"拨动"识别: 推离中心再回中 = 一次输入
/// 方向到键位的映射保持手机九宫格的空间关系(上=2, 左上=1, 右=6 ...)
/// </summary>
public sealed class StickFlick
{
    const int DEADZONE = 9000;      // 略大于系统默认 7849, 抗漂移
    const int STABLE_FRAMES = 2;    // 方向需连续稳定, 抗抖动

    // 按角度 0,45,90,135,180,225,270,315 排列(0 = 右)
    static readonly int[] SectorKey = { 6, 3, 2, 1, 4, 7, 8, 9 };

    bool _armed;
    int _dir = -1;
    int _stable;

    /// <summary>返回 1-9 表示拨动触发的键位, 0 表示无事件</summary>
    public int Poll(short x, short y)
    {
        double r = Math.Sqrt((double)x * x + (double)y * y);
        if (r < DEADZONE)
        {
            if (_armed && _stable >= STABLE_FRAMES)
            {
                _armed = false;
                _stable = 0;
                return SectorKey[_dir];
            }
            _armed = false;
            _stable = 0;
            return 0;
        }

        int dir = SectorOf(x, y);
        if (_armed && dir == _dir) _stable++;
        else { _armed = true; _dir = dir; _stable = 1; }
        return 0;
    }

    static int SectorOf(short x, short y)
    {
        // Y 轴向上为正
        double ang = Math.Atan2(y, x) * 180.0 / Math.PI;
        if (ang < 0) ang += 360.0;
        return (int)Math.Floor((ang + 22.5) / 45.0) % 8;
    }
}
