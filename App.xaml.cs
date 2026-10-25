using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Binput;

public partial class App : Application, ISettingsHost
{
    InputCore _core;
    OverlayWindow _win;
    Forms.NotifyIcon _tray;
    DispatcherTimer _timer;
    bool _preview;                     // 预输入版(前台窗口 + 记事本预览框)
    double _scale = 1.0;
    DateTime? _imeFixAt;            // 唤出后延迟补做"抓焦点 + 切中文"的时间点
    DateTime? _hideAt;              // 预输入版收起: 结掉半截拼音后等一小会儿再真正收
    DateTime? _lostAt;              // 窗口被抢走激活后, 延后一小会儿再确认是不是"点到别处了"
    bool _flipAfterShow;            // 界面还没唤出时点了"设置": 等它 Show 出来再翻到背面
    DateTime _handoffUntil;         // 主动把焦点交给游戏之后, 这段时间里不算"点到别处"

    /// <summary>
    /// 鼠标点到别的窗口(典型是点了游戏): 界面虽然被盖住了, 但 Visible 还是 true,
    /// 这时候按 L3+R3 会变成"收起"而不是"唤出", 得先关一次再开一次才能用。
    /// 这里直接把状态拨回收起——不复制、不粘贴、不清记事本, 下次 L3+R3 直接唤回。
    /// </summary>
    void OnLostActivation()
    {
        if (!_preview || !_core.Visible || !_win.IsVisible) return;
        // **发快捷键是我们自己把焦点递过去的**, 不是"点到别处" —— 那一趟发完还要
        // 接着用键盘。不豁免的话, 预输入版发一次快捷键整个界面就被收起来了。
        if (DateTime.Now < _handoffUntil) return;
        // 延迟确认: 激活切换是异步的, 立刻去问前台是谁可能问到的还是自己
        _lostAt = DateTime.Now.AddMilliseconds(250);
    }

    void CheckDismiss()
    {
        IntPtr fore = Win32.GetForegroundWindow();
        if (fore == IntPtr.Zero || fore == _win.Handle) return;
        // 自己进程的窗口(托盘菜单 / 设置窗口 / 输入法候选窗)不算"点到别处"
        if (Win32.SameProcess(fore)) return;

        Log($"预输入版 · 点到别的窗口({Win32.Describe(fore)}) -> 键盘先收起(不复制), L3+R3 唤回");
        _win.Hide();
        _core.Visible = false;   // 逻辑上也拨回收起, 否则下次 L3+R3 变成"收起"
        _hideAt = null;
        _imeFixAt = null;
    }

    // ================= 单实例 =================
    // 双击 exe 开第二份会把配置写串、托盘上还冒出第二个图标, 所以只许跑一个:
    // 起不来就想办法把已经在跑的那个唤出来, 自己直接退出。
    const string MUTEX_NAME = @"Local\Binput_SingleInstance_v1";
    const string WAKE_MSG_NAME = "Binput_WakeUp_v1";
    static Mutex _mutex;
    static readonly uint WM_WAKEUP = Win32.RegisterWindowMessage(WAKE_MSG_NAME);

    void WakeRunningInstance()
    {
        if (WM_WAKEUP == 0) return;
        // 先按标题直接找那个窗口(界面藏起来时也能找到); 找不到再广播兜底
        IntPtr h = Win32.FindWindowOfOtherProcess("柄输");
        IntPtr target = h != IntPtr.Zero ? h : Win32.HWND_BROADCAST;
        Win32.SendMessageTimeout(target, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero,
            Win32.SMTO_ABORTIFHUNG, 800, out _);
    }

    /// <summary>
    /// 收到第二个实例的唤醒消息: 把界面唤出来。
    /// 已经开着就顶一下前台(多半是被别的窗口盖住了), 关着就当成"唤出键盘"。
    /// </summary>
    IntPtr OnWm(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (WM_WAKEUP != 0 && (uint)msg == WM_WAKEUP)
        {
            handled = true;
            Log("收到第二个实例的唤醒消息 -> 唤出界面");
            if (!_core.Visible) _core.Visible = true;
            else if (_win.IsVisible) _win.TakeForeground();
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    public static void Log(string msg)
    {
        try
        {
            // 日志跟着存档走(程序旁的 save\), 别再往 C 盘用户目录里塞 ——
            // 整个文件夹拷走就是一份完整的柄输, 排查问题时日志也就在手边。
            var file = Config.LogFile;
            Directory.CreateDirectory(Config.Dir);
            File.AppendAllText(file,
                $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}");
            TrimLog(file);
        }
        catch { /* 日志失败不能影响主流程 */ }
    }

    /// <summary>
    /// 日志上限(字节)。这是个桌面小工具, 不该在用户盘上攒出几十兆的文本。
    /// 150 KB 约合两天的量 —— 过线砍掉旧的一半, 于是它永远在 75~150 KB 之间晃。
    /// </summary>
    const long LOG_MAX = 150_000;

    /// <summary>
    /// 过线就把旧的前一半砍掉, 只留近期的那一半。排查问题看最近那段也就够了 ——
    /// 真要留全量, 那是"打开日志"按钮该干的事, 不是让它默默长到几十兆。
    /// </summary>
    static void TrimLog(string file)
    {
        if (new FileInfo(file).Length < LOG_MAX) return;
        var lines = File.ReadAllLines(file);
        File.WriteAllLines(file, lines.Skip(lines.Length / 2));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 已经有一个在跑了: 把它唤出来, 自己退出(在任何别的初始化之前就走,
        // 免得开出第二个托盘图标 / 第二个定时器)
        // 这把锁的名字是正式版的名字, 别动 —— 换成别的会和用户机器上已装的
        // 柄输同时跑起来, 那才是真出鬼。
        _mutex = new Mutex(true, MUTEX_NAME, out bool createdNew);
        if (!createdNew)
        {
            // 别用 Shutdown(): 这时候 base.OnStartup 还没跑, WPF 的 Application
            // 还没初始化完, 调它会抛异常(退出码变成 0xE0434352, 看着像崩了)。
            // 消息已经同步发出去了, 这里直接退就行。
            try { WakeRunningInstance(); }
            catch (Exception ex) { Log("唤醒已运行的实例失败: " + ex.Message); }
            Environment.Exit(0);
            return;
        }

        DispatcherUnhandledException += (_, a) =>
        { Log("UI 异常: " + a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            Log("非 UI 异常: " + a.ExceptionObject);

        base.OnStartup(e);

        try { Start(e); }
        catch (Exception ex) { Log("启动失败: " + ex); throw; }
    }

    void Start(StartupEventArgs e)
    {
        Log("启动, args=" + string.Join(" ", e.Args));

        // --preview: 预输入版。界面变成前台窗口, 带记事本预览框, 收起时复制粘贴。
        // 不加就是直出版: 不抢焦点, 按键直接送到当前有焦点的窗口。
        var savedMode = Config.LoadMode();
        _preview = e.Args.Contains("--preview", StringComparer.OrdinalIgnoreCase) || savedMode.Preview;
        GameMode.Tier = (PasteTier)Math.Clamp(savedMode.Tier, 0, 3);

        _core = new InputCore { Visible = false };

        _win = new OverlayWindow();
        _win.Host = this;
        // 装上窗口消息钩子: 第二个实例启动时会发唤醒消息过来(见 OnWm)
        HwndSource.FromHwnd(_win.Handle)?.AddHook(OnWm);
        // 拖右下角齿轮挪界面: 偏移必须回写 InputCore —— 手柄那边(R3 + 左摇杆)
        // 也在改同一对值, 只改窗口的会被下一帧的同步盖回去, 界面一跳就回原位。
        _win.Dragged += (ox, oy, done) =>
        {
            _core.OffsetX = ox;
            _core.OffsetY = oy;
            if (done) Config.SaveAll(_scale, ox, oy);
        };
        // 顺序要紧: 先把上次存的档位读回来, 再 ApplyPreview。
        // 反过来会丢设置 —— ApplyPreview 里会把"当前档位"一起写进 mode.txt,
        // 那时候 GameMode.Tier 还是默认的"仅复制", 于是每次开机都把用户选的
        // "复制+粘贴"抹成"仅复制"。
        GameMode.Tier = (PasteTier)Math.Clamp(savedMode.Tier, 0, 3);
        ApplyPreview(_preview);
        Log($"预输入版 {(_preview ? "开" : "关")} · 收起后: {GameMode.Name(GameMode.Tier)}");

        var saved = Config.LoadAll();    // 上次调过的大小和摆位
        _scale = saved.Scale;
        _core.Scale = saved.Scale;
        _core.OffsetX = saved.Ox;
        _core.OffsetY = saved.Oy;
        _win.SetScale(saved.Scale);
        _win.OffsetX = saved.Ox;
        _win.OffsetY = saved.Oy;
        // 手柄上调完(R3 松手)才落盘
        _core.LayoutSaved = (s, ox, oy) => Config.SaveAll(s, ox, oy);

        LoadDict();

        var use = Config.LoadUse(out var useLast);
        T9Engine.LoadUse(use, useLast);
        if (use.Count > 0) Log($"输入习惯: 读回 {use.Count} 条");
        // 文件在但解不开 —— 和"还没学过"是两回事。原文件会留着(改名成 .bad-…),
        // 不会拿现在这点新数据把它盖掉。
        if (Config.UseBroken)
            Log("输入习惯: ⚠ 本地那份读不出来(换了账号 / 文件被别的程序存坏过), "
                + "这次先当没学过; 下次存盘会把坏的那份改名留底, 不会盖掉");

        SetupTray();

        // 设置页的三个手柄动作。翻面这件事窗口自己也做(鼠标点齿轮),
        // 所以"当前是不是在设置面"每帧从窗口同步过去, 不在 InputCore 里另记一份。
        _core.ToggleSettings = () =>
        {
            if (_win.IsVisible) _win.Flip(!_win.Flipped);
            else OpenSettings();
        };
        _core.SettingsNav = d => _win.SettingsNav(d);
        _core.SettingsNavH = d => _win.SettingsNavH(d);
        _core.SettingsPick = () => _win.SettingsActivate();
        // 设置页是一层层九宫格(见 OverlayWindow 里那段): 摇杆选格、B 退层、LB/RB 翻页
        _core.SettingsStick = (x, y) => _win.SettingsStick(x, y);
        _core.SettingsBack = root => _win.SettingsBack(root);
        _core.SettingsDepth = () => _win.SettingsDepth;
        _core.SettingsPage = d => _win.SettingsPage(d);
        // 按了 LB / RB -> 框外面那对小牌跟着跳一下(见 OverlayWindow.PageTurnFx)
        _core.PageFx = d => _win.PageTurnFx(d);
        // L3 换九键 / 全键盘时也翻一下(和 R3 翻到设置是同一套动画)
        _core.FlipLayout = mid => _win.FlipLayout(mid);
        _win.LayoutOf = () => _core.Layout;

        // 窗口F(2.1.1 起是一层层九宫格, 和设置页同一套)。
        // **两边各拿一个接口**: 窗口拿 IQuickHost(按下去真要干的事, core 实现),
        // core 拿 IQuickUI(那一摞层怎么走, 窗口实现) —— 一个指针一条线, 不用
        // 为每一条操作单独接一根。
        _core.QuickUI = _win;
        _win.QHost = _core;
        // 短语直接用预输入版那个记事本预览框里的字 —— 不再另开输入窗口,
        // 字本来就在框里。直出版没有预览框(字直接打进游戏), 那边 QuickNoteText
        // 返回 null, "写入短语"这个动作就只给一句提示。
        _core.QuickNoteText = () => _win.Preview ? _win.NoteText : null;
        _core.QuickSetNote = s => _win.QuickPutNote(s);
        // 发快捷键之前把焦点还给游戏。不接 GameMode.Deliver 了 —— 那套"复制 → 粘贴
        // → 回车"是为"收起键盘"设计的, 在 F 里发短语会把焦点抢走, 预输入版一发现
        // 焦点不在自己身上就把界面收起来, 松 LT 那一帧就没机会还原布局了。
        _core.QuickFocusGame = () =>
        {
            var h = GameMode.TargetHwnd;
            if (h != IntPtr.Zero) Win32.ForceForeground(h);
            // 焦点是我们自己递过去的, 别让"点到别处 → 收起界面"把它当成用户跑了。
            // 给 1.5 秒: 发键本身还有约 0.26 秒的缓冲, 之后用户多半还要接着用键盘。
            _handoffUntil = DateTime.Now.AddMilliseconds(1500);
        };
        QuickStore.Load();
        Log($"窗口F: 读到 {QuickStore.Count} 条快捷");
        if (Config.QuickBroken)
            Log("窗口F: ⚠ 快捷那份读不出来, 下次保存会先把坏的那份改名留底, 不会盖掉");

        // 主题: 上次选的那套。必须在窗口搭好之后再刷, 那几块面板的底色是它刷的。
        // 【土豪金那一格】解锁标记得**先**读回来 —— ApplyTheme 里"没解锁就不许落在
        // 土豪金上"要靠它判断, 读晚了老存档里存的"第 10 套"会被退回原色。
        _win.LoadGoldOpen();
        _win.ApplyTheme(Config.LoadTheme());
        Log("主题: " + OverlayWindow.Themes[_win.Theme].Name
            + (_win.GoldOpen ? "(土豪金 已解锁)" : ""));

        // 动态效果恒开: 2.0.1 起这一档不再能关(设置页里那一项撤掉了),
        // 所以也不再读 fx.txt —— 老版本留在用户目录里的那份就让它躺着, 没人看它。

        _win.Key9Num = Config.LoadKey9Num();
        Log("九键数字: " + (_win.Key9Num ? "显示" : "隐藏(只留字母)"));

        _core.Swap15 = Config.LoadSwap15();
        Log("九键 1/5 互换: " + (_core.Swap15 ? "换(左上快捷 / 中间 JKL)" : "不换(左上 JKL / 中间快捷)"));

        // 滑行输入: 门槛是从你自己的记录里算出来的, 存下来的这份先用着,
        // 等记录攒够了可以在设置页里重算一次。
        var (slMode, slDwell, slRepeat) = Config.LoadSlide();
        _core.SlideMode = slMode;
        _core.SlideDwellMs = slDwell;
        _core.SlideRepeatMs = slRepeat;
        Log($"滑行输入: {SlideModeName(slMode)}, 触发 {slDwell}ms, 连发 {slRepeat}ms");

        var fxStyle = Config.LoadFxStyle();
        _core.FxStyle = fxStyle;
        Log("换页动效: " + FxStyleName(fxStyle));

        var keyShape = Config.LoadKeyShape();
        _win.KeyShape = keyShape;
        Log("按键形状: " + KeyShapes.Name(keyShape));

        _win.LostActivation += OnLostActivation;

        // --settings: 直接把设置页打开(调界面时省得去托盘里点)
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            OpenSettings();

        // 自定义唤醒键: 存档里有的话手柄键 InputCore 自己认, 键盘键在这里挂钩子
        var w = Config.LoadWake();
        if (w.Length > 0 && !InputCore.IsWakeSpec(w))
        {
            // 老版本那种"自由组合"(ctrl+f1 / lb+rb)现在不认了, 直接删掉退回 L3+R3 ——
            // 留着它最坏: 手柄那路不认、键盘那路也接不住, 就成了怎么都唤不出来。
            Log($"唤醒键: 存档里的 \"{w}\" 是老版本的自由组合, 已经删掉并退回 L3+R3");
            Config.SaveWake("");
            w = "";
        }
        _core.WakeSpec = w;
        _core.WakeChanged += () =>      // 录完新键立刻生效: 重装钩子 + 托盘那行字跟着改
        {
            ReinstallKbHook();
            UpdateTrayText();
        };
        ReinstallKbHook();
        Log("唤醒键: " + WakeKeys());

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += Tick;
        HidPad.Start();          // PS5 那一路: 后台自己找 HID 设备、自己读报告
        _timer.Start();
        Log("Start 走完, 消息循环即将接管");
    }

    void Tick(object sender, EventArgs e)
    {
        var now = DateTime.Now;
        // 三条路一起问、结果并起来(见 PadBus):
        //   XInput(Xbox) + Windows.Gaming.Input(Switch Pro 等) + HID+HidP(PS5)。
        // 不能"谁先接上用谁" —— 那样插两只手柄时前面那条会把后面整条挡死,
        // 表现就是"PS5 单独连能用, 多接一只就完全失效"。
        var st = PadBus.Read(out string src);
        _core.PadSource = src;
        // 设置面由窗口说了算(鼠标点齿轮也能翻), 每帧同步给输入核心 ——
        // 十字键在那一面是"上下选设置项", 不是方向键。
        _core.Settings = _win.Flipped;
        _core.Poll(st, now);
        ApplyWakeQueued();     // 键盘唤醒钩子判到的组合, 在这儿(主线程)真正切显隐

        // 输入习惯攒够 20 条写一次盘。每次按键都写太夸张, 只在退出时写又怕崩了丢掉。
        if (T9Engine.UseDirty) { Config.SaveUse(T9Engine.DumpUse(), T9Engine.DumpLast()); T9Engine.UseSaved(); }

        // 指针是不是停在右下角的齿轮上。界面默认整块鼠标穿透,
        // 只有这一刻临时撤掉穿透, 齿轮才点得到、拖得动。
        _win.HitTestTick();

        // 唤出之后的补做: 焦点再抓一次 + 系统输入法切中文
        if (_imeFixAt.HasValue && now >= _imeFixAt.Value)
        {
            _imeFixAt = null;
            _win.TakeForeground();
            _core.SyncIme();
            bool zh = ImeStatus.EnsureChinese();
            Log("预输入版 · 补做: 焦点=" + Win32.Describe(Win32.GetForegroundWindow())
                + " 系统输入法=" + (zh ? "中文" : "英文(切不过来, 手动切一下)"));
        }

        if (_lostAt.HasValue && now >= _lostAt.Value)
        {
            _lostAt = null;
            CheckDismiss();
        }

        if (Math.Abs(_win.Scale - _core.Scale) > 0.001
            || Math.Abs(_win.OffsetX - _core.OffsetX) > 0.5
            || Math.Abs(_win.OffsetY - _core.OffsetY) > 0.5)
        {
            _win.SetScale(_core.Scale);
            _win.OffsetX = _core.OffsetX;
            _win.OffsetY = _core.OffsetY;
            _win.Reposition();
        }

        if (_core.Visible)
        {
            if (!_win.IsVisible)
            {
                // 唤出之前先记下前台是谁 —— 收起时要把焦点还给它, 不然粘贴没地方去
                if (_preview)
                {
                    IntPtr fore = Win32.GetForegroundWindow();
                    if (fore != _win.Handle)
                    {
                        GameMode.TargetHwnd = fore;
                        Log($"记下游戏窗口: {Win32.Describe(fore)}");
                        // 权限不对等(UIPI)是"游戏里没反应"第一嫌疑, 唤出时就先喊一嗓子 ——
                        // 等到收起发键才发现就晚了, 白白试好几轮。
                        GameMode.LogIntegrity();
                    }
                }
                _win.Render(_core);      // 先把这一帧画好再露脸(见 ShowReady 的说明)
                // 抢前台要等窗口真正挪回屏幕上(ShowReady 头一两帧是停在屏幕外的):
                // 现在去抢的话, 抢的是一个看不见的窗口 —— 游戏那边白丢一次焦点,
                // 全屏游戏会因此黑一下, 看着又是一次"闪"。
                _win.OnShown = () =>
                {
                    if (!_preview) return;
                    _win.TakeForeground();
                    // 切输入法这件事要等窗口真的成了前台才能做: SetForegroundWindow 的
                    // 激活是异步的, 同一帧就去问"现在是不是中文"问到的还是游戏那边。
                    // 这里登记一个 200ms 后的补做, 不阻塞唤出(它同时也是兜底:
                    // 万一 OnShown 没来, 200ms 后也会抢一次)。
                    _imeFixAt = DateTime.Now.AddMilliseconds(200);
                };
                _win.ShowReady();
                if (_flipAfterShow) { _flipAfterShow = false; _win.Flip(true); }
            }
            _win.Render(_core);
        }
        else if (_win.IsVisible)
        {
            if (_preview)
            {
                // 先把输入法里挂着的半截拼音结掉, 再等 150ms 让记事本真的收到上屏的字,
                // 然后才 Hide + 复制粘贴。三件事必须这个顺序, 否则复制出来的是空的。
                if (_hideAt == null)
                {
                    _core.Flush();
                    _hideAt = DateTime.Now.AddMilliseconds(150);
                }
                else if (now >= _hideAt.Value)
                {
                    _hideAt = null;
                    string txt = _win.NoteText;
                    _win.Hide();
                    GameMode.Deliver(txt);     // 写剪贴板 -> 还焦点 -> 按档位发键
                    _win.NoteText = "";        // 发完就清空, 下次唤出是全新的一段话
                }
            }
            else
            {
                _win.Hide();
            }
        }
    }

    void LoadDict()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "dict.txt");
        if (!File.Exists(path))
        {
            Log("未找到 dict.txt, 只用内置演示词库");
            return;
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int n = T9Engine.LoadExternalDict(path);
        Log($"拼音表加载 {n} 条(共 {T9Engine.PhraseCount} 条), "
            + $"热度 {T9Engine.HotCount} 个音节, 耗时 {sw.ElapsedMilliseconds}ms");
    }

    void UpdateTrayText()
    {
        if (_tray == null) return;
        // 定了自定义就照实写 —— 托盘这行字是"界面藏起来之后"唯一还看得见的提示
        string how = _core == null ? "L3 + R3" : WakeKeys();
        _tray.Text = _preview
            ? $"柄输 — 预输入版 ({how} 唤出)"
            : $"柄输 — 直出版 ({how} 唤出)";
    }

    /// <summary>切版本: 预输入版(前台窗口 + 预览框 + 收起粘贴) ↔ 直出版(直接打进当前窗口)。</summary>
    void ApplyPreview(bool on)
    {
        _preview = on;
        GameMode.Enabled = on;
        _win.Preview = on;
        _core.SetPreview(on);
        // 预输入版必须真的拿到焦点, 系统输入法才肯在预览框里干活; 切过去时补做一次
        if (on && _win.IsVisible) _imeFixAt = DateTime.Now.AddMilliseconds(200);
        Config.SaveMode(on, (int)GameMode.Tier);
        UpdateTrayText();
        Log("版本 -> " + (on ? "预输入版" : "直出版"));
    }

    /// <summary>
    /// 从嵌入的 Binput.ico 读图标。图标是作为资源嵌进 exe 的, 不是散在外面的文件,
    /// 所以不会像 dict.txt 那样被漏拷。读不到就退回系统默认图标, 不影响启动。
    /// </summary>
    static Drawing.Icon LoadAppIcon()
    {
        try
        {
            var info = GetResourceStream(new Uri("pack://application:,,,/Binput.ico"));
            if (info == null)
            {
                Log("未找到嵌入图标资源, 用系统默认图标");
                return Drawing.SystemIcons.Application;
            }

            // ico 里虽然有 16~256 九档, 但 GDI+ 造 HICON 时只挑一档(默认挑最小的 16),
            // 高 DPI 下被放大就糊了。显式指定 32 作基准: 100% DPI 缩到 16 依然清楚,
            // 200% DPI 正好是 32, 两头都不吃亏。
            var ms = new MemoryStream();
            using (var s = info.Stream) s.CopyTo(ms);
            ms.Position = 0;

            Drawing.Icon ic;
            try { ic = new Drawing.Icon(ms, 32, 32); }
            catch { ms.Position = 0; ic = new Drawing.Icon(ms); }

            Log($"托盘图标: 已加载嵌入图标 {ic.Width}x{ic.Height}");
            return ic;
        }
        catch (Exception ex)
        {
            Log("图标加载失败: " + ex.Message);
        }
        return Drawing.SystemIcons.Application;
    }

    void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "柄输 — L3 + R3 唤出键盘",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        // 托盘菜单只留三项: 以前把七八个开关全堆在这里, 越加越杂。
        // 现在开关都进"设置"那一个页面里, 这里只留最高频的"显隐"。
        menu.Items.Add("显示 / 隐藏键盘", null, (_, _) => _core.Visible = !_core.Visible);
        menu.Items.Add("设置...", null, (_, _) => OpenSettings());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _core.Visible = !_core.Visible;
    }

    /// <summary>
    /// 打开设置。**不再单开一个窗口** —— 设置就画在这块界面的背面,
    /// 大小和键盘一模一样, 翻过去像同一张卡片转了个面。
    /// 界面还没唤出时先把它唤出来, 等 Show 完再翻(见 Tick 里的 _flipAfterShow)。
    /// </summary>
    void OpenSettings()
    {
        if (!_core.Visible || !_win.IsVisible)
        {
            _core.Visible = true;
            _flipAfterShow = true;
            Log("设置: 界面还没唤出, 先唤出再翻面");
            return;
        }
        _win.Flip(true);
        Log("设置: 翻到背面");
    }

    // ================= ISettingsHost =================
    // 设置页读写的是这几个属性, 真正的落盘 / 联动都在这里做, 页面本身不碰状态。

    public bool Preview
    {
        get => _preview;
        set { if (value != _preview) ApplyPreview(value); }
    }

    public int PasteTier
    {
        get => (int)GameMode.Tier;
        set
        {
            var t = (PasteTier)Math.Clamp(value, 0, 3);
            if (t == GameMode.Tier) return;
            GameMode.Tier = t;
            // 直出版也要存: 切回预输入版时档位得还是上次那个, 不能掉回"仅复制"
            Config.SaveMode(_preview, (int)t);
            Log("收起后 -> " + GameMode.Name(t));
        }
    }

    /// <summary>配色主题。存盘 + 立刻换色两件事都在这里, 页面只读写这个属性。</summary>
    public int Theme
    {
        get => _win.Theme;
        set
        {
            _win.ApplyTheme(value);
            Config.SaveTheme(_win.Theme);
            Log("主题 -> " + OverlayWindow.Themes[_win.Theme].Name);
        }
    }

    /// <summary>
    /// 九键按键上显不显示数字。写在窗口上(渲染要用), 落盘在这里做。
    /// 数字层不受它影响 —— 那一层本来就是要看数字。
    /// </summary>
    public bool Key9Num
    {
        get => _win.Key9Num;
        set
        {
            if (value == _win.Key9Num) return;
            _win.Key9Num = value;
            Config.SaveKey9Num(value);
            Log("九键数字 -> " + (value ? "显示" : "隐藏(只留字母)"));
        }
    }

    /// <summary>九宫格 1 / 5 互换。滑行时 InputCore 会自己把它压住, 这里照存不误。</summary>
    public bool Swap15
    {
        get => _core.Swap15;
        set
        {
            if (value == _core.Swap15) return;
            _core.Swap15 = value;
            Config.SaveSwap15(value);
            Log("九键 1/5 互换 -> " + (value ? "换（左上快捷，JKL 到中间）" : "不换（左上 JKL，中间快捷）"));
        }
    }

    // ---- 滑行输入 ----

    public SlideMode SlideMode
    {
        get => _core.SlideMode;
        set
        {
            if (value == _core.SlideMode) return;
            _core.SlideMode = value;
            Config.SaveSlide(value, _core.SlideDwellMs, _core.SlideRepeatMs);
            Log("滑行输入 -> " + SlideModeName(value));
        }
    }

    /// <summary>设置页那个按钮上的字。</summary>
    public static string SlideModeName(SlideMode m) => m == SlideMode.Off ? "关" : "开";

    /// <summary>
    /// 设置页那个按钮上的字。
    /// <para>【2.1.36】名字搬到 <see cref="FxStyles.Name"/> 去了 —— 和"第二行小字 /
    /// 底下长说明 / 存档解析"同住一张表(<see cref="FxStyles.All"/>)。以前这里是个
    /// switch, 加一种新风格忘了补这一处的话, 名字会静默退回 "默认" —— 界面上看着
    /// 好好的, 其实显示的根本不是你新加的那种。</para>
    /// </summary>
    public static string FxStyleName(FxStyle s) => FxStyles.Name(s);

    public FxStyle FxStyle
    {
        get => _core.FxStyle;
        set
        {
            if (value == _core.FxStyle) return;
            _core.FxStyle = value;
            Config.SaveFxStyle(value);
            Log("换页动效 -> " + FxStyleName(value));
        }
    }

    /// <summary>
    /// 按键的形状(见 KeyShape)。画键要用, 所以真值在窗口上; 落盘在这里做。
    /// 只改圆角 —— 格子尺寸一个都不动。
    /// </summary>
    public KeyShape KeyShape
    {
        get => _win.KeyShape;
        set
        {
            if (value == _win.KeyShape) return;
            _win.KeyShape = value;
            Config.SaveKeyShape(value);
            Log("按键形状 -> " + KeyShapes.Name(value));
        }
    }

    public int SlideDwell
    {
        get => _core.SlideDwellMs;
        set
        {
            // 只认档位: 设置页那几行给什么就存什么, 不在档位上的一律吸到最近的一档。
            int v = Config.NearestDwell(value);
            if (v == _core.SlideDwellMs) return;
            _core.SlideDwellMs = v;
            Config.SaveSlide(_core.SlideMode, v, _core.SlideRepeatMs);
            Log($"触发判定 -> {v}ms");
        }
    }

    public int SlideRepeat
    {
        get => _core.SlideRepeatMs;
        set
        {
            int v = Config.NearestRepeat(value);
            if (v == _core.SlideRepeatMs) return;
            _core.SlideRepeatMs = v;
            Config.SaveSlide(_core.SlideMode, _core.SlideDwellMs, v);
            Log($"连发判定 -> {v}ms");
        }
    }

    /// <summary>
    /// 给设置页显示的那段话。**只报当前门槛** ——
    /// 以前这里还报"已经滑了多少道、其中几道算数", 那套是给自动标定看的
    /// (2.1.5 把滑行的驻留记录整份撤了, 门槛改成你自己拨档), 数字没了源头就不摆了。
    /// </summary>
    public string SlideInfo() =>
        "按住 LB，左摇杆朝某个方向推过去**停一下**就是一个字母；摇杆绕一圈九个方向，"
      + "左上角那颗是 JKL。停不够久就当你是路过蹭过去的 —— 那个“够久”就是**触发判定**："
      + "调快 = 灵敏但容易多认（按 X 删一下就行），调慢 = 稳但可能漏。"
      + $"已经认下之后你还停着不动，它会按**连发判定**每隔一阵再给一个同方向的键"
      + $"（打 66 / 777 这类就靠它）。当前：触发 {_core.SlideDwellMs}ms、连发 {_core.SlideRepeatMs}ms。";

    /// <summary>设置页的"退出程序": 存完输入习惯再收摊。</summary>
    public void ExitApp()
    {
        Log("设置: 退出程序");
        SaveUse();
        Shutdown();
    }

    /// <summary>
    /// 设置 → 键盘 → 「调整窗口」: 先翻回正面(看着的是键盘, 缩放才看得出效果),
    /// 再进那一档。退出是按 B(见 InputCore 第 2.5 步 / EndResize)。
    /// </summary>
    public void AdjustWindow()
    {
        Log("设置: 进调窗口(缩放 / 摆位)");
        _core.BeginResize();
    }

    // ---- 自定义唤醒键 ----
    //
    // 手柄键在 InputCore.Poll 里接(第 1 步); 键盘键在这里接 —— 低级键盘钩子
    // (WH_KEYBOARD_LL)。**不吞键**: 钩子永远把消息往下游传, 游戏照样收到这个键,
    // 我们只是"顺带"看到它按下了。要是用 RegisterHotKey, 组合键会被全局抢走,
    // 游戏里那个键就废了 —— 唤出键和游戏键撞车的时候, 挨骂的是我们。
    // 钩子回调必须快, 所以里面只记账, 真正的切换动作回主循环(16ms Tick)再做。

    IntPtr _kbHook;
    // ⚠ **这个字段是救命的**: 传给 SetWindowsHookExW 的回调委托必须自己攥着一份引用。
    // 早先是直接写 SetWindowsHookExW(13, KbHookProc, ...) 的 —— 每次调用都会临时造一个
    // 委托对象, 除了 native 那边的函数指针之外没有任何地方引用它, 下一次 GC 就被收走了。
    // 系统那头的钩子还挂着(它不知道这边已经删了), 于是任何程序里按一下键, 回调就跳到
    // 一个已经失效的地址: 不抛异常、进不了 DispatcherUnhandledException, 整个进程当场
    // 没声没响地消失 —— "用键盘设唤醒键之后程序直接强退"的根子就在这儿。
    KbHookDelegate _kbProc;
    readonly HashSet<int> _kbDown = new();     // 当前按着的 VK(LL 钩子报的是 L/R 细分键)
    bool _wakeQueued;                          // 钩子里判到那颗键按下, 等 Tick 来切
    int _wakeVk;                               // 键盘唤醒键的 VK(0 = 没配键盘唤醒键)

    /// <summary>设置页里选了一颗唤醒键: 存盘、立刻生效。</summary>
    public void SetWake(string spec)
    {
        Log("唤醒键: 定成 " + (spec.Length == 0 ? "默认 L3+R3" : spec));
        Config.SaveWake(spec);
        _core.WakeSpec = spec;
        ReinstallKbHook();
        UpdateTrayText();
    }

    public void WakeReset()
    {
        Log("唤醒键: 恢复默认 L3+R3");
        Config.SaveWake("");
        _core.WakeSpec = "";
        ReinstallKbHook();
        UpdateTrayText();
    }

    /// <summary>
    /// 给设置页显示**现在定的是哪几个键**(只有键名, 不带句子) —— 按钮说明里那句
    /// "当前唤醒键为 X"要拼的是它。没自定义就写出厂那对。
    /// </summary>
    public string WakeSpec => _core?.WakeSpec ?? "";

    public string WakeKeys()
    {
        var s = _core.WakeSpec;
        if (!s.StartsWith("pad:", StringComparison.OrdinalIgnoreCase)
            && !s.StartsWith("kb:", StringComparison.OrdinalIgnoreCase))
            return "L3 + R3";
        var body = s.StartsWith("pad:", StringComparison.OrdinalIgnoreCase)
            ? s.Substring(4) : s.Substring(3);
        // 存的时候是小写 token("f7"), 显示时首字母抬起来就好读多了("F7" / "Back + Start")
        return string.Join(" + ", body.Split('+')
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Select(t => char.ToUpperInvariant(t[0]) + t[1..]));
    }

    /// <summary>按 WakeSpec 里存的键盘组合(重)装低级键盘钩子; 没配就卸掉。</summary>
    void ReinstallKbHook()
    {
        if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        _wakeVk = 0;
        _kbDown.Clear();

        var s = _core.WakeSpec;
        if (!s.StartsWith("kb:", StringComparison.OrdinalIgnoreCase)) return;

        // 键盘唤醒键就 F1~F12 这十二个(见 InputCore.KeyWakeOptions), 单颗 ——
        // 不用认什么组合、也不用认修饰键: 早先能录任意组合的时候, Ctrl/Alt/Win 这类
        // 在游戏里各有各的用处, 实测 Ctrl 组合压根唤不出来, 白折腾。
        var body = s.Substring(3);
        int vk = InputCore.VkOfWakeKey(body);
        if (vk == 0) { Log($"唤醒键: \"{body}\" 不是 F1~F12, 认不出"); GiveUpWake(); return; }
        _wakeVk = vk;

        _kbProc ??= KbHookProc;      // 引用留在字段里, 别让 GC 把它带走(见字段注释)
        _kbHook = SetWindowsHookExW(13 /*WH_KEYBOARD_LL*/, _kbProc, GetModuleHandleW(null), 0);
        if (_kbHook != IntPtr.Zero)
        {
            Log($"唤醒键: 键盘钩子已挂 (F{vk - 0x6F})");
            return;
        }
        Log($"唤醒键: 键盘钩子挂失败(GetLastError={Marshal.GetLastWin32Error()})");
        GiveUpWake();
    }

    /// <summary>
    /// 定了键盘组合却用不起来(键名认不出 / 钩子没挂上): **把自定义删掉, 退回 L3+R3**。
    /// 留着它是最坏的结果 —— 手柄那一路已经被"定了自定义就作废"那句话关掉了,
    /// 键盘这一路又接不住, 就成了怎么都唤不出来。宁可替用户退回出厂那一对(日志里写清楚)。
    /// </summary>
    void GiveUpWake()
    {
        Log("唤醒键: 这次的自定义用不起来, 已经删掉并退回 L3+R3。重新去设置里定一个试试。");
        Config.SaveWake("");
        _core.WakeSpec = "";
    }

    IntPtr KbHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        // 这个回调是**所有进程按下的每一个键**都要经过的。里面一旦有东西抛出来,
        // 是没人接得住的(它跑在系统塞过来的调用上, 不在我们的 try 链里) ——
        // 结果是整个进程跟着消失。所以整个判断包一层: 真出错这次不认就算了。
        try
        {
            if (code >= 0 && _wakeVk != 0)
            {
                int msg = wParam.ToInt32();
                int vk = Marshal.ReadInt32(lParam);          // KBDLLHOOKSTRUCT.vkCode 在头一个 int
                bool down = msg is 0x0100 or 0x0104;         // WM_KEYDOWN / WM_SYSKEYDOWN
                bool changed = down ? _kbDown.Add(vk) : _kbDown.Remove(vk);
                // 只在"这颗键真的新按下去"时判一次 —— 按住不放的连发消息不反复触发
                if (down && changed && vk == _wakeVk)
                    _wakeQueued = true;
            }
        }
        catch (Exception ex)
        {
            if (!_kbHookErr)
            {
                _kbHookErr = true;      // 每个键都来一次的话, 日志一秒能写几百行
                Log("键盘唤醒钩子出错(这一次没认出来): " + ex.Message);
            }
        }
        return CallNextHookEx(_kbHook, code, wParam, lParam);   // 永远不吞键
    }
    bool _kbHookErr;

    void ApplyWakeQueued()
    {
        if (!_wakeQueued) return;
        _wakeQueued = false;
        _core.Visible = !_core.Visible;
        _core.Dirty = true;
        Log("唤醒键(键盘): " + (_core.Visible ? "唤出" : "收起"));
    }

    [DllImport("user32.dll")]
    static extern IntPtr SetWindowsHookExW(int idHook, KbHookDelegate proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetModuleHandleW(string name);
    delegate IntPtr KbHookDelegate(int code, IntPtr wParam, IntPtr lParam);

    public void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            if (!File.Exists(Config.LogFile))
                File.WriteAllText(Config.LogFile, "(还没有日志)\r\n");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Config.LogFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("打开日志失败: " + ex.Message);
            System.Windows.MessageBox.Show("打不开日志: " + ex.Message,
                "柄输", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    public void OpenDataDir()
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Config.Dir) { UseShellExecute = true });
        }
        catch (Exception ex) { Log("打开数据目录失败: " + ex.Message); }
    }

    public string About()
    {
        var dictPath = Path.Combine(AppContext.BaseDirectory, "dict.txt");
        string dict = File.Exists(dictPath)
            ? $"已加载（{new FileInfo(dictPath).Length / 1024} KB，"
              + $"{T9Engine.PhraseCount} 条拼音，热度 {T9Engine.HotCount} 个音节）"
            : "没找到 dict.txt，用内置那几十条顶着";
        return
            "柄输 Binput — 手柄九键输入法\n" +
            $"当前版本: {(_preview ? "预输入版" : "直出版")}（设置里可切换）\n" +
            $"拼音表: {dict}\n" +
            $"输入习惯: {T9Engine.UseCount} 条（挑中并按 RB 发出的切分会记下来，下次自动排前）\n" +
            "存盘时已加密（绑你的 Windows 账号，双击打开是乱码）\n" +
            $"程序目录: {AppContext.BaseDirectory}\n" +
            // 日志就在数据目录里(Config.LogFile = Dir\log.txt), 不用再单列一行 ——
            // 三条路径占的是六行里的三行, 并成两行之后整段才塞得进那一大格。
            $"数据目录: {Config.Dir}（日志同目录）\n\n" +
            "制作人：莫古雨\n" +
            "主页：https://space.bilibili.com/45454732\n" +
            "献给所有手柄玩家";
    }

    /// <summary>退出时把输入习惯存下来。改动攒够 20 条时 tick 里也会存一次。</summary>
    void SaveUse()
    {
        if (T9Engine.UseCount == 0) return;
        Config.SaveUse(T9Engine.DumpUse(), T9Engine.DumpLast());
        T9Engine.UseSaved();
    }

    /// <summary>设置页的"清除输入习惯": 文件里清掉, 内存里也清掉。</summary>
    public void ClearUse()
    {
        T9Engine.ClearUse();
        Config.SaveUse(T9Engine.DumpUse(), T9Engine.DumpLast());
        Log("输入习惯: 已清除");
        _win.RefreshSettings();
    }

    /// <summary>
    /// 设置页的"导出数据": 输入习惯 + 窗口F 的快捷**一起**存一份明文到你自己挑的地方
    /// (换机器 / 重装前留底)。以前只导习惯, 换台机器快捷得一个个重新录。
    /// </summary>
    public void ExportUse()
    {
        int n = T9Engine.UseCount;
        int q = QuickStore.Count;
        if (n == 0 && q == 0)
        {
            MessageBox.Show("现在还没有输入习惯、也没有存任何快捷，没什么可导出的。",
                "柄输", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出数据（输入习惯 + 快捷）",
            FileName = $"柄输-备份-{DateTime.Now:yyyyMMdd}.txt",
            DefaultExt = ".txt",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        // 直出版那套界面是置顶窗, 不挂到它上面的话对话框会被游戏画面盖住,
        // 看着就像点了没反应
        if (dlg.ShowDialog(_win) != true) return;

        if (Config.ExportAll(dlg.FileName, T9Engine.DumpUse(), T9Engine.DumpLast(), QuickStore.Slots))
        {
            Log($"导出: 习惯 {n} 条 + 快捷 {q} 条 -> {dlg.FileName}");
            MessageBox.Show($"已经导出到：\n{dlg.FileName}\n\n"
                + $"输入习惯 {n} 条，快捷短语 / 快捷键 {q} 条。\n\n"
                + "注意：这个文件是明文的（写成密文别的机器就读不懂了），"
                + "里面是你常打的词、自己拼的名字和常发的整句话，别随手发给别人。",
                "柄输", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            Log("导出: 失败 " + dlg.FileName);
            MessageBox.Show("写不进这个文件，换一个地方再试（比如桌面）。",
                "柄输", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 设置页的"导入数据": 从以前导出的备份把输入习惯**和**快捷都并回来
    /// (不覆盖这台机器上现有的)。老备份(只有习惯、没有段标记)也认。
    /// </summary>
    public void ImportUse()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入数据（输入习惯 + 快捷）",
            DefaultExt = ".txt",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog(_win) != true) return;

        if (!Config.ImportAll(dlg.FileName, out var data, out _, out var quick))
        {
            MessageBox.Show("这个文件里没读到东西 —— 可能拿错文件了，或者文件坏了。",
                "柄输", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        int changed = T9Engine.MergeUse(data);
        int added = QuickStore.Merge(quick);      // 重复的不加, 新来的不抢 1~9
        Config.ClearBroken();                     // 这份数据归我们管了, 不用再当坏文件留着
        SaveUse();                                // 习惯那边写盘(快捷在 Merge 里已经存了)
        Log($"导入: 习惯 {data.Count} 条(变动 {changed}) + 快捷新增 {added} 条 <- {dlg.FileName}");
        _win.RefreshSettings();
        MessageBox.Show($"输入习惯读进来 {data.Count} 条，其中 {changed} 条是新的或者打得更多；\n"
            + $"快捷新增 {added} 条（重复的和这台机器上已有的不会动）。\n\n"
            + $"现在一共：习惯 {T9Engine.UseCount} 条，快捷 {QuickStore.Count} 条。\n\n"
            + "（原来那台机器上学的没有丢，两边是并在一起的。）",
            "柄输", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _timer?.Stop();
        HidPad.Stop();
        SaveUse();
        if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
        base.OnExit(e);
    }
}
