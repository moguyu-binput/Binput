namespace Binput;

/// <summary>
/// 收起键盘之后怎么处理记事本里的那一段话。
/// 每档都比前一档多发一步, 每一步之间都留延迟 —— 太快系统/游戏还没反应过来就丢键。
/// </summary>
public enum PasteTier
{
    /// <summary>只复制到剪贴板, 自己在游戏里按 Ctrl+V。</summary>
    CopyOnly = 0,
    /// <summary>复制 + 粘贴。</summary>
    CopyPaste = 1,
    /// <summary>复制 + 粘贴 + 回车(直接把这句话发出去)。</summary>
    CopyPasteEnter = 2,
    /// <summary>
    /// FF14: 复制 + 回车(先把聊天框叫出来) + 粘贴。
    /// 顺序就是这三个字, 一步都不能换 —— 先回车再复制的话, 聊天框弹出来的那一刻
    /// 剪贴板还是空的, 粘进去的是上一次的东西。
    /// </summary>
    FF14 = 3,
}

public static class GameMode
{
    public static bool Enabled;
    public static PasteTier Tier = PasteTier.CopyOnly;

    /// <summary>唤出键盘之前的前台窗口(就是游戏), 收起后要把焦点还给它。</summary>
    public static IntPtr TargetHwnd;

    // 每一步之间的延迟(毫秒)。游戏吃焦点、聊天框弹出都要时间, 太快会丢键。
    const int DELAY_FOCUS = 450;     // 顶完焦点之后等游戏把输入焦点真正拿到
    const int DELAY_STEP = 250;      // 每个动作之间
    const int DELAY_FF14_ENTER = 900;// FF14 的聊天框弹出是带动画的, 得等它真的能接字
    /// <summary>顶焦点最多试几次(每次间隔 DELAY_FOCUS), 总共不超过 1.4 秒。</summary>
    const int FOCUS_TRIES = 3;
    /// <summary>
    /// 送给游戏的按键要"按住"多久。游戏按帧采样键盘状态, 按下和松开落在同一帧里
    /// 就等于没按(见 TextOutput.KeySlow), 90ms 足够跨过 30fps 的一帧还有富余。
    /// </summary>
    const int KEY_HOLD = 90;

    public static string Name(PasteTier t) => t switch
    {
        PasteTier.CopyOnly => "仅复制",
        PasteTier.CopyPaste => "复制 + 粘贴",
        PasteTier.CopyPasteEnter => "复制 + 粘贴 + 回车",
        // 不写 "FF14" —— 档位的名字说的是它做哪几步, 不是给哪个游戏准备的
        PasteTier.FF14 => "回车 + 粘贴",
        _ => "?"
    };

    /// <summary>
    /// 设置里那一格上写的字。**两步一行**, 按顺序排 —— "复制, 粘贴, 回车" 就是
    /// 它真会做的事, 一格也不用再挂注脚。FF14 那一档的顺序是"复制 → 回车 → 粘贴"
    /// (回车是把聊天框叫出来), 所以这里照实写成三行。
    /// </summary>
    public static string Steps(PasteTier t) => t switch
    {
        PasteTier.CopyOnly => "复制",
        PasteTier.CopyPaste => "复制\n粘贴",
        PasteTier.CopyPasteEnter => "复制\n粘贴\n回车",
        PasteTier.FF14 => "复制\n回车\n粘贴",
        _ => Name(t)
    };

    public static string Tip(PasteTier t) => t switch
    {
        PasteTier.CopyOnly => "只复制到剪贴板，你自己 Ctrl+V",
        PasteTier.CopyPaste => "自动 Ctrl+V，但不发送",
        PasteTier.CopyPasteEnter => "自动 Ctrl+V 再回车，直接发送",
        PasteTier.FF14 => "先回车叫出聊天框，再 Ctrl+V —— FF14 用这个",
        _ => ""
    };

    /// <summary>
    /// 收起键盘: 记事本内容 -> 剪贴板 -> 焦点还给游戏 -> 按档位发键。
    /// 必须在 Hide 之后调用(焦点要先腾出来)。
    /// </summary>
    public static void Deliver(string text)
    {
        bool empty = string.IsNullOrEmpty(text);
        bool copied = false;

        if (!empty)
        {
            copied = Clipboard2.Set(text);
            App.Log($"预输入版 · 收起: {text.Length} 字 -> 剪贴板 {(copied ? "成功" : "失败")}");
        }
        else
        {
            App.Log("预输入版 · 收起: 记事本是空的, 只把焦点还回游戏");
        }

        var tier = (empty || !copied) ? PasteTier.CopyOnly : Tier;
        if (!empty && !copied)
            App.Log("!! 剪贴板没写进去, 降级成" + Name(PasteTier.CopyOnly) + "(自动粘贴会粘出别的东西)");

        if (TargetHwnd != IntPtr.Zero)
        {
            // 这里只是"先请求一次"。SetForegroundWindow 是异步生效的, 紧接着查前台
            // 常常还查不到, 报"失败"是假警报 —— 真正的确认在发键线程里(见 EnsureFocus)。
            Win32.ForceForeground(TargetHwnd);
            App.Log($"请求焦点还给游戏: {Win32.Describe(TargetHwnd)} (发键线程里再确认)");
        }
        else App.Log("!! 没记下游戏窗口, 焦点还不回去了");

        var th = new Thread(() => Run(tier)) { IsBackground = true };
        th.SetApartmentState(ApartmentState.STA);   // 走 SendInput, STA 更稳
        th.Start();
    }

    static void Run(PasteTier tier)
    {
        try { ToForeground(tier); }
        catch (Exception ex) { App.Log("发键线程异常: " + ex.Message); }
    }

    /// <summary>
    /// 焦点顶回游戏, 最多试 FOCUS_TRIES 次。返回"最后到底在不在游戏上"。
    /// 顶不动也照样往下发 —— 有些全屏游戏 GetForegroundWindow 报出来的不是主窗口句柄,
    /// 但键其实收得到, 这时候硬不发反而什么都做不成。
    /// </summary>
    static bool EnsureFocus()
    {
        if (TargetHwnd == IntPtr.Zero)
        {
            App.Log("!! 没记下游戏窗口(唤出时前台不是游戏?), 焦点没法还");
            return false;
        }
        if (!Win32.IsWindow(TargetHwnd))
        {
            App.Log($"!! 记下的游戏窗口已经不在了: {Win32.Describe(TargetHwnd)}");
            return false;
        }

        for (int i = 0; i < FOCUS_TRIES; i++)
        {
            var now = Win32.GetForegroundWindow();
            if (now == TargetHwnd) return true;
            App.Log($"焦点第 {i + 1} 次顶回(现在前台 {Win32.Describe(now)})");
            Win32.ForceForeground(TargetHwnd);
            Thread.Sleep(DELAY_FOCUS);
        }
        return Win32.GetForegroundWindow() == TargetHwnd;
    }

    /// <summary>
    /// 权限(UIPI)检查。**"记事本里好好的、游戏里一点反应没有"就是这个原因**:
    /// 游戏要是以管理员身份运行的, 而柄输不是, 我们的完整性级别比它低,
    /// Windows 会**静默丢掉**发过去的每一个键 —— 不报错, 直接丢。
    /// 实测 FF14 就是这么卡的: 柄输改成以管理员身份运行之后立刻全好。
    /// 唤出时和发键前各查一次, 早早在日志里喊出来。
    /// </summary>
    public static void LogIntegrity()
    {
        int mine = Win32.IntegrityOfPid((uint)Environment.ProcessId);
        int theirs = Win32.IntegrityOf(TargetHwnd);
        string s = $"完整性: 柄输={Win32.IntegrityName(mine)}(0x{mine:X}) "
                 + $"游戏={Win32.IntegrityName(theirs)}(0x{theirs:X})";
        if (mine < 0 || theirs < 0)
            App.Log(s + " —— 有一边没读出来, 这次判断不了(不影响发键)");
        else if (mine < theirs)
            App.Log("!! " + s + " —— 权限比游戏低, Windows 会静默丢掉发过去的按键。"
                  + " 请把柄输改成以管理员身份运行, 或者别用管理员身份开游戏。");
        else
            App.Log(s + " —— 一样高, 不会被 UIPI 拦");
    }

    static void ToForeground(PasteTier tier)
    {
        bool focus = EnsureFocus();
        LogIntegrity();
        App.Log($"发键前前台: {Win32.Describe(Win32.GetForegroundWindow())}"
              + $" · 档位 {Name(tier)}"
              + (focus ? "" : " · !! 焦点没在游戏上(照样发, 看游戏收不收)"));

        switch (tier)
        {
            case PasteTier.CopyOnly:
                App.Log("档位=仅复制: 不发键");
                break;

            case PasteTier.CopyPaste:
                CtrlV();
                break;

            case PasteTier.CopyPasteEnter:
                CtrlV();
                Thread.Sleep(DELAY_STEP);
                Enter("已发 回车(发送)");
                break;

            case PasteTier.FF14:
                // 复制是 Deliver 里第一件做的事, 到这里剪贴板里已经是这句话了。
                // 剩下两步: 回车把聊天框叫出来 -> 等它真的能接字 -> Ctrl+V。
                Enter("FF14: 已发 回车(叫出聊天框)");
                Thread.Sleep(DELAY_FF14_ENTER);
                CtrlV();
                break;
        }
    }

    /// <summary>发一次回车。系统没收下的话基本就是权限问题(见 LogIntegrity)。</summary>
    static void Enter(string what)
    {
        // gated: false —— 这是发给**游戏**的命令, 不过"预览框上升"那道闸门
        // (见 TextOutput.ChordSlow 那条注释)。回车本身被 IsEditing 排除、不走闸门,
        // 这里照样写明白, 免得哪天有人把它和"往框里打字"那一套混起来。
        bool ok = TextOutput.KeySlow(TextOutput.VK_RETURN, KEY_HOLD, gated: false);
        App.Log(what + (ok ? "" : " —— !! SendInput 被系统拒收(多半是权限不够)")
                + ", 前台=" + Win32.Describe(Win32.GetForegroundWindow()));
    }

    /// <summary>
    /// Ctrl+V。走慢速版: 修饰键按下、敲 V、松开之间都留一点时间 ——
    /// 游戏是按帧采样键盘状态的, 一路连发过去它很可能整帧都没采到(见 ChordSlow)。
    /// </summary>
    static void CtrlV()
    {
        // gated: false —— 同上(见 Enter)。**这一处正是"档位少做一步"那个坑**:
        // 收起键盘时预览框正在沉下去, 过闸门的话 Ctrl+V 会被扣进队列等"框升到位",
        // 而框再也不会升 —— 于是粘贴这一步永远发不出去。
        bool ok = TextOutput.ChordSlow(TextOutput.VK_CONTROL, (ushort)'V', KEY_HOLD, 60,
                                       gated: false);
        App.Log("已发 Ctrl+V" + (ok ? "" : " —— !! SendInput 被系统拒收(多半是权限不够)"));
    }
}
