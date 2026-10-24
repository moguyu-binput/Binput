using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Binput;

/// <summary>
/// 存档的读写。**一个文件装全部**: 设置 / 输入习惯 / 窗口F 的快捷都在这份
/// <c>Binput.save</c> 里, 整份用 Windows 账号密钥加密。
/// <para>2.1.5 之前是十来个 txt 散在数据目录里(每个开关一个文件), 2.1.5 合成一份:
/// 目录里只剩 <c>Binput.save</c>、它的一份 <c>.bak</c>、和明文日志 <c>log.txt</c>。</para>
/// <para><b>目录跟着程序走</b>: 数据存在程序所在文件夹的 <c>save\</c> 里 —— 整个文件夹
/// 拷到 U 盘就是一份完整的"带着习惯的柄输"。装在没写权限的地方(Program Files 之类)
/// 会自动退回 %LOCALAPPDATA%\Binput, 不会因此起不来。</para>
/// </summary>
public static class Config
{
    // ================= 目录 =================

    static string _dirOverride = null;
    static string _resolved = null;

    /// <summary>
    /// 数据目录。默认 = **程序所在文件夹里的 save**; 那儿写不了就退回
    /// %LOCALAPPDATA%\Binput(见 <see cref="ResolveDir"/>)。
    /// **和 exe 叫什么、放在哪个盘无关** —— 换包、覆盖 exe 都不会碰这里。
    /// </summary>
    public static string Dir => _dirOverride ?? (_resolved ??= ResolveDir());

    /// <summary>只给探针用: 把整份数据挪到一个临时文件夹, 免得测试动到真机上那份。</summary>
    public static void SetDirForTest(string dir)
    {
        _dirOverride = dir;
        _resolved = null;
        Forget();
        Directory.CreateDirectory(dir);
    }

    /// <summary>内存里那份缓存整个忘掉(下次要用会重新读盘)。</summary>
    static void Forget()
    {
        _loaded = false;
        _broken = false;
        _set.Clear();
        _use.Clear();
        _last.Clear();
        _quick = null;
    }

    /// <summary>
    /// 挑一个**真能写**的目录。光 CreateDirectory 成功不算数 —— 有些只读位置
    /// 会"成功"但一个字节也写不进去, 所以真写一个试试; 写不了就退回用户目录。
    /// 这样装在 Program Files 下也能起来(只是设置存到别处), 不会直接崩。
    /// </summary>
    static string ResolveDir()
    {
        try
        {
            var d = Path.Combine(AppContext.BaseDirectory, "save");
            Directory.CreateDirectory(d);
            var probe = Path.Combine(d, ".wtest");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return d;
        }
        catch { }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Binput");
    }

    public static string LogFile => Path.Combine(Dir, "log.txt");

    /// <summary>存档本体。后缀是自家起的, 系统里没有程序认得它 —— 双击只会问
    /// "用什么打开", 不会有人误当文本翻开。</summary>
    static string SaveFile => Path.Combine(Dir, "Binput.save");

    /// <summary>存档文件的完整路径(探针和排错时用)。</summary>
    public static string SavePath => SaveFile;

    // ================= 存档: 一份文件, 三段 =================
    //
    // 加密前长这样(和"导出备份"那份几乎同一个格式, 方便看懂):
    //     [set]    键=值, 一行一项(版本 / 档位 / 皮肤 / 九键数字 / JKL / 滑行 / 唤醒 / 缩放摆位)
    //     [use]    拼音连写  发出次数  最后一次是全局第几次输入
    //     [quick]  槽位(1~45)  phrase|key  内容
    // 整份 DPAPI 加密后落盘, 前面加 4 个字节的自家标记。

    const string SEG_SET = "[set]";
    const string SEG_USE = "[use]";
    const string SEG_QUICK = "[quick]";

    /// <summary>
    /// 自家标记。写盘时先落这 4 个字节, 读的时候靠它确认"这是我们家的存档"。
    /// 认不出来就当没有(不猜、不报错) —— 换台机器打开文件也解不开, 那和没有一样。
    /// </summary>
    static readonly byte[] MAGIC = { (byte)'B', (byte)'I', (byte)'N', (byte)'1' };

    /// <summary>2.1.5 以前那份 pinyin.txt / quick.txt 用的老标记。只迁移时认一次。</summary>
    static readonly byte[] OLD_MAGIC = { (byte)'G', (byte)'T', (byte)'U', (byte)'1' };

    // ---- 内存里那一份(启动读一次, 之后改内存 + 整份写回) ----
    static bool _loaded;
    static bool _broken;
    static readonly Dictionary<string, string> _set = new();
    static readonly Dictionary<string, int> _use = new();
    static readonly Dictionary<string, int> _last = new();
    static QuickItem[] _quick = null;

    /// <summary>
    /// 用 Windows 账号密钥加密(DPAPI)。
    /// <para>为什么需要它: 这份文件里是你常打的词、自己按 Y 拼出来的名字和网名,
    /// 属于隐私 —— 明文放着, 谁打开数据目录都能一眼看完。</para>
    /// <para>为什么不是自己弄个密码: 密钥由 Windows 按**当前 Windows 账号**管着,
    /// **不在文件里**。于是别人登录另一个账号、或者把文件拷到别的机器上, 看到的
    /// 就是一堆解不开的乱码; 而你自己在这台机器上照常用, 也不用记任何密码。</para>
    /// </summary>
    static byte[] Protect(byte[] plain) =>
        ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);

    static byte[] Unprotect(byte[] enc) =>
        ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);

    static bool HasMagic(byte[] raw, byte[] magic)
    {
        if (raw.Length < magic.Length) return false;
        for (int i = 0; i < magic.Length; i++)
            if (raw[i] != magic[i]) return false;
        return true;
    }

    static byte[] Pack(string text)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        byte[] enc = null;
        try { enc = Protect(plain); } catch { }
        if (enc == null) return plain;          // 极少数环境 DPAPI 不可用: 明文也要保住
        var buf = new byte[MAGIC.Length + enc.Length];
        MAGIC.CopyTo(buf, 0);
        enc.CopyTo(buf, MAGIC.Length);
        return buf;
    }

    /// <summary>
    /// 落盘。**不直接往目标文件上写**: 先写 .tmp, 把上一份好的复制成 .bak,
    /// 最后把 .tmp 挪过去 —— 同盘 Move 是替换式的, 要么旧的全在、要么新的全在。
    /// 以前 WriteAllBytes 是"先清成 0 字节再慢慢写", 写到一半进程被杀就剩个半截
    /// 文件, 下次再也读不出来。
    /// </summary>
    static bool WriteGuarded(string path, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            try { if (File.Exists(path)) File.Copy(path, path + ".bak", true); } catch { }
            File.Move(tmp, path, true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>往日志里记一行(给"文件被改名留底"这种要说清楚的事用)。</summary>
    static void Note(string s)
    {
        try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\n"); }
        catch { }
    }

    // ---- 读 ----

    static void Ensure()
    {
        if (_loaded) return;
        _loaded = true;
        _broken = false;
        try
        {
            if (!File.Exists(SaveFile)) { MigrateOld(); return; }
            var raw = File.ReadAllBytes(SaveFile);
            if (!HasMagic(raw, MAGIC)) return;              // 不是我们家的, 当空白
            string text;
            try { text = Encoding.UTF8.GetString(Unprotect(raw[MAGIC.Length..])); }
            catch { _broken = true; return; }               // 解开了但内容是乱码 = 坏
            Parse(text);
        }
        catch { }
    }

    /// <summary>存档解不开。**下一次写盘会先把坏的那份改名留底**, 不拿新数据盖掉。</summary>
    public static bool UseBroken { get { Ensure(); return _broken; } }
    /// <summary>同上(老版本习惯和快捷是两个文件, 现在合成一份, 两个名字说的是一件事)。</summary>
    public static bool QuickBroken => UseBroken;

    /// <summary>导入 / 清除之后: 文件已经归我们管了, 不用再当"坏文件"供着。</summary>
    public static void ClearBroken() { _broken = false; }

    /// <summary>
    /// 把内存里那份忘掉, 下次要用重新读盘。**只给探针用**: 它要造"文件被弄坏了"
    /// 这类现场 —— 直接改文件的话, 缓存还记着上一次读到的好数据, 看不出变化。
    /// </summary>
    public static void Reload() => Forget();

    static void Parse(string text)
    {
        int seg = -1;
        foreach (var line in text.Split('\n'))
        {
            var s = line.Trim('\r', ' ', '\t');
            if (s.Length == 0 || s[0] == '#') continue;
            if (s.Equals(SEG_SET, StringComparison.OrdinalIgnoreCase)) { seg = 0; continue; }
            if (s.Equals(SEG_USE, StringComparison.OrdinalIgnoreCase)) { seg = 1; continue; }
            if (s.Equals(SEG_QUICK, StringComparison.OrdinalIgnoreCase)) { seg = 2; continue; }
            if (seg == 0)
            {
                int i = s.IndexOf('=');
                if (i > 0) _set[s[..i].Trim()] = s[(i + 1)..];
            }
            else if (seg == 1) ParseUseLine(s);
            else if (seg == 2) ParseQuickLine(s);
        }
    }

    /// <summary>解析一行习惯。默认写进内存那一份; 导入时传外面的字典, 免得污染。</summary>
    static void ParseUseLine(string s) => ParseUseLine(s, _use, _last);

    static void ParseUseLine(string s, Dictionary<string, int> use, Dictionary<string, int> last)
    {
        var p = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2) return;
        if (!int.TryParse(p[1], out int n) || n <= 0) return;
        use[p[0]] = n;
        last[p[0]] = p.Length >= 3 && int.TryParse(p[2], out int l) ? l : -1;
    }

    /// <summary>同上: 默认写内存那一份, 导入时写到外面那份。</summary>
    static void ParseQuickLine(string s) => ParseQuickLine(s, ref _quick);

    static void ParseQuickLine(string s, ref QuickItem[] slots)
    {
        // 内容是最后一段, 里面可能有空格(短语就是句子), 所以只切前两刀
        int i1 = s.IndexOf(' ');
        if (i1 < 0) return;
        int i2 = s.IndexOf(' ', i1 + 1);
        if (i2 < 0) return;
        if (!int.TryParse(s[..i1], out int num)) return;
        var kind = s.Substring(i1 + 1, i2 - i1 - 1) == "key" ? QKind.Hotkey : QKind.Phrase;
        var body = s[(i2 + 1)..];
        if (body.Length == 0) return;
        slots ??= new QuickItem[MaxQuickSlot];
        int slot = Math.Clamp(num, 1, MaxQuickSlot) - 1;
        slots[slot] = new QuickItem { Kind = kind, Text = body };
    }

    // ---- 写 ----

    /// <summary>整份写回。改了内存里任何一处, 最后都要走这一下。</summary>
    static void Flush()
    {
        try
        {
            if (_broken) { ParkBroken(); _broken = false; }
            WriteGuarded(SaveFile, Pack(Build()));
        }
        catch { }
    }

    static string Build()
    {
        var sb = new StringBuilder();
        sb.Append("# 柄输 存档(整份加密): 设置 + 输入习惯 + 窗口F 的快捷\n");
        sb.Append("# 里面是你常打的词、自己拼的名字和常发的整句话, 别随手发给别人。\n\n");
        sb.Append(SEG_SET).Append('\n');
        foreach (var kv in _set)
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
        sb.Append('\n').Append(SEG_USE).Append('\n');
        sb.Append("# 拼音连写 / 发出次数 / 最后一次是全局第几次输入\n");
        foreach (var kv in _use)
            if (kv.Key.Length > 0 && kv.Value > 0)
                sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(' ')
                  .Append(_last.TryGetValue(kv.Key, out int l) ? l : 0).Append('\n');
        sb.Append('\n').Append(SEG_QUICK).Append('\n');
        sb.Append("# 槽位(1~45, 前九个就是数字键 1~9) / 种类(phrase|key) / 内容\n");
        if (_quick != null)
            for (int i = 0; i < _quick.Length; i++)
                if (_quick[i] != null) AppendQuickLine(sb, i, _quick[i]);
        return sb.ToString();
    }

    /// <summary>导出用的那一行(明文备份): 槽位 + 种类 + 内容。</summary>
    static void AppendQuickLine(StringBuilder sb, int slot, QuickItem it) =>
        sb.Append(slot + 1).Append(' ')
          .Append(it.Kind == QKind.Hotkey ? "key" : "phrase").Append(' ')
          .Append(OneLine(it.Text)).Append('\n');

    static string OneLine(string s) => (s ?? "").Replace("\n", " ").Replace("\r", "");

    /// <summary>
    /// 读不出来的那份先改名留着(不删、不盖)。**最多留两份** —— 不然每坏一次
    /// 攒一份, 目录又回到从前那个样子。
    /// </summary>
    static void ParkBroken()
    {
        try
        {
            if (!File.Exists(SaveFile)) return;
            string dir = Path.GetDirectoryName(SaveFile);
            string name = Path.GetFileName(SaveFile);
            var olds = Directory.GetFiles(dir, name + ".bad-*").OrderBy(x => x).ToList();
            while (olds.Count >= 2) { File.Delete(olds[0]); olds.RemoveAt(0); }
            string dst = SaveFile + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Move(SaveFile, dst);
            Note("数据保险: 读不出来的那份已改名留底 -> " + dst);
        }
        catch { }
    }

    // ---- 设置的键值 ----

    static string Get(string key) { Ensure(); return _set.TryGetValue(key, out var v) ? v : ""; }
    /// <summary>开关。没存过就给默认; 存了就认 "0" 以外的一切为真。</summary>
    static bool Flag(string key, bool def) { var s = Get(key); return s.Length == 0 ? def : s != "0"; }
    static int Num(string key, int def) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    static double Dbl(string key, double def) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    static void Put(string key, string v) { Ensure(); _set[key] = v; }

    // ================= 设置 =================

    /// <summary>读 (是否预输入版, 收起档位)。没存过 = 直出版 + 只复制。</summary>
    public static (bool Preview, int Tier) LoadMode()
    {
        Ensure();
        return (Flag("preview", false), Math.Clamp(Num("tier", 0), 0, 3));
    }

    public static void SaveMode(bool preview, int tier)
    {
        Put("preview", preview ? "1" : "0");
        Put("tier", Math.Clamp(tier, 0, 3).ToString(CultureInfo.InvariantCulture));
        Flush();
    }

    /// <summary>读 (缩放, 偏移X, 偏移Y)。</summary>
    public static (double Scale, double Ox, double Oy) LoadAll()
    {
        Ensure();
        return (Dbl("scale", 1.0), Dbl("ox", 0), Dbl("oy", 0));
    }

    public static void SaveAll(double scale, double ox, double oy)
    {
        Put("scale", scale.ToString(CultureInfo.InvariantCulture));
        Put("ox", ox.ToString(CultureInfo.InvariantCulture));
        Put("oy", oy.ToString(CultureInfo.InvariantCulture));
        Flush();
    }

    /// <summary>配色主题(下标)。</summary>
    public static int LoadTheme() { Ensure(); return Math.Max(0, Num("theme", 0)); }

    public static void SaveTheme(int i)
    {
        Put("theme", Math.Clamp(i, 0, 999).ToString(CultureInfo.InvariantCulture));
        Flush();
    }

    /// <summary>
    /// 土豪金**解锁了没有**(彩蛋, 下标见 OverlayWindow.GoldIdx)。
    /// 默认没解锁 —— 得在设置首页连点 100 下才开; **开了就一直是开的**(存盘),
    /// 关掉软件再开, 设置 → 皮肤里那格还在。
    /// </summary>
    public static bool LoadGoldOpen() => Flag("gold_open", false);

    public static void SaveGoldOpen(bool on) { Put("gold_open", on ? "1" : "0"); Flush(); }

    /// <summary>
    /// 九键按键上显不显示数字。**默认是"不显示"** —— 键上那串字母才是九键的真信息
    /// (跟手机九宫格一个道理), 数字只是给刚上手的人对位用的, 而且数字一大就把
    /// 字母挤成小字。想要数字的人在设置里打开。
    /// </summary>
    public static bool LoadKey9Num() => Flag("key9num", false);

    public static void SaveKey9Num(bool on) { Put("key9num", on ? "1" : "0"); Flush(); }

    /// <summary>
    /// 九宫格里 **1 和 5 这两颗的功能换不换**。默认是"不换":
    /// 左上角 JKL、正中间快捷。**按住 LB 进滑行时这一项一律不生效**(见
    /// InputCore.QuickCell) —— 滑行靠摇杆绕圈取键, 那时候左上必须是 JKL。
    /// </summary>
    // 【2.1.6 起出厂是"JKL 在正中间"】也就是手机九宫格那个样子(true)。
    // 早先默认的是"JKL 在左上角"(false), 那是为滑行留的位置 —— 滑行靠摇杆转圈
    // 取键, 正中间摇杆够不到(松手回中就在那儿), 于是把 JKL 挪去了左上。
    // 后来滑行改成"一按住 LB 就自动切回左上布局"(`QuickCell` 里那个 !_slideHeld),
    // 平时摆哪颗都行, 那就该回到大家最熟的手机九宫格。
    // 只改**新装**那份默认值: 老存档里明明白白写着 swap15, 读到什么就是什么。
    public static bool LoadSwap15() => Flag("swap15", true);

    public static void SaveSwap15(bool on) { Put("swap15", on ? "1" : "0"); Flush(); }

    // ---- 自定义唤醒键 ----
    //
    // 存的是一条描述, 空 = 没自定义(用默认的 L3+R3):
    //   "kb:f7"        键盘 F1~F12 里的一颗(见 InputCore.KeyWakeOptions)
    //   "pad:Back"     手柄 Back / Start / Back+Start(见 InputCore.PadWakeOptions)

    public static string LoadWake() => Get("wake");

    public static void SaveWake(string spec) { Put("wake", spec ?? ""); Flush(); }

    // ================= 滑行输入 =================

    /// <summary>
    /// **推荐这一档, 也是出厂默认**(128)。
    /// 以前默认给的是 80, 那是"多认一个键你按一下 X 就删了、漏一个整串都得重滑"
    /// 这条算出来的 —— 宁滥勿缺。实际用下来太多人嫌它毛躁: 蹭过去的格子老被算进去。
    /// 128 退两档, 代价是有一点点漏(记录里你真会停的最短是 124 毫秒, 刚好压在线上),
    /// 但即便是漏, 推回去再滑一下就回来了, 比不停按 X 省心。
    /// </summary>
    public const int DEFAULT_DWELL = 128;

    /// <summary>
    /// 门槛只能在这几档里挑。**不做自动标定** —— 从记录里算出来的那个数看着客观,
    /// 可它改的是"你滑得对不对"的判断, 而滑得对不对只有手知道;
    /// 你自己滑两下就知道该用哪一档, 比任何算法都快。
    /// <para>128 往上按现有数据**会开始漏**(真停最短 124ms), 但漏不漏跟手速有关,
    /// 有人就是滑得慢, 所以照样给出来 —— 不合适换回来就是了, 一秒钟的事。</para>
    /// <para>两头那几档(48 和 160 往上)是**拿来试边界的**, 让你摸到自己能忍的
    /// 上下限, 摸完往回挑就知道该停在哪儿。</para>
    /// </summary>
    public static readonly int[] DWELL_STEPS = { 48, 64, 80, 96, 112, 128, 144, 160, 176, 192 };

    // ================= 连发判定 =================
    //
    // 和上面那个是**两件事**, 别混:
    //   触发判定(DWELL_STEPS)  在一格上停多久才"认下"这个键 —— 管灵敏度
    //   连发判定(REPEAT_STEPS) 认下之后, 手还停着不动的话, 每隔多久再给一个 —— 管重复速度
    // 打 "爸" 靠前者, 打 "66" / "777" 靠后者。以前后者是写死的 420 毫秒,
    // 2.1.34 起和前者一样能自己拨档。

    /// <summary>
    /// **推荐这一档, 也是出厂默认**(320)。取比正中间慢一点点的地方: 多一个字母
    /// 按一下 X 就删了(成本 1), 而太快的话"明明只想打一个却出来三个"更难收拾;
    /// 慢的代价只是多停一会儿。
    /// </summary>
    public const int DEFAULT_REPEAT = 320;

    /// <summary>
    /// 连发间隔的十二档: **200 起步、420 封顶, 每档整 20 毫秒**。
    /// 2.1.34 那版为了凑十档让最后一档跨了 40(…360, 400), 看着别扭; 改成整 20 一档
    /// 之后不仅读着顺, 420 还正好落在尾上 —— 那正是 2.1.34 以前写死的那个数,
    /// 想要旧手感的人有得挑。
    /// </summary>
    public static readonly int[] REPEAT_STEPS =
        { 200, 220, 240, 260, 280, 300, 320, 340, 360, 380, 400, 420 };

    /// <summary>把任意毫秒数吸到最近的一档(存过旧值 / 手改过配置时兜底)。</summary>
    public static int NearestRepeat(int ms)
    {
        int best = DEFAULT_REPEAT, gap = int.MaxValue;
        foreach (int d in REPEAT_STEPS)
        {
            int g = Math.Abs(d - ms);
            if (g < gap) { gap = g; best = d; }
        }
        return best;
    }

    /// <summary>某一档在数组里的下标(设置页拿它点亮圆点)。</summary>
    public static int RepeatIndex(int ms)
    {
        for (int i = 0; i < REPEAT_STEPS.Length; i++)
            if (REPEAT_STEPS[i] == ms) return i;
        return Array.IndexOf(REPEAT_STEPS, NearestRepeat(ms));
    }

    /// <summary>
    /// 把任意毫秒数吸到最近的一档。老配置里可能存着标定给出的怪数字(比如 115),
    /// 界面上那几行里没有它, 圆点就没地方亮 —— 必须落回某一档。
    /// </summary>
    public static int NearestDwell(int ms)
    {
        int best = DEFAULT_DWELL, gap = int.MaxValue;
        foreach (int d in DWELL_STEPS)
        {
            int g = Math.Abs(d - ms);
            if (g < gap) { gap = g; best = d; }
        }
        return best;
    }

    /// <summary>某一档在数组里的下标(设置页拿它点亮圆点)。</summary>
    public static int DwellIndex(int ms)
    {
        for (int i = 0; i < DWELL_STEPS.Length; i++)
            if (DWELL_STEPS[i] == ms) return i;
        return Array.IndexOf(DWELL_STEPS, NearestDwell(ms));
    }

    public static (SlideMode Mode, int Dwell, int Repeat) LoadSlide()
    {
        Ensure();
        var mode = Num("slide", (int)SlideMode.Type) == (int)SlideMode.Off
            ? SlideMode.Off : SlideMode.Type;
        return (mode,
                NearestDwell(Math.Clamp(Num("dwell", DEFAULT_DWELL), 30, 600)),
                NearestRepeat(Math.Clamp(Num("repeat", DEFAULT_REPEAT), 100, 1000)));
    }

    public static void SaveSlide(SlideMode mode, int dwell, int repeat)
    {
        Put("slide", ((int)mode).ToString(CultureInfo.InvariantCulture));
        Put("dwell", Math.Clamp(dwell, 30, 600).ToString(CultureInfo.InvariantCulture));
        Put("repeat", Math.Clamp(repeat, 100, 1000).ToString(CultureInfo.InvariantCulture));
        Flush();
    }

    /// <summary>
    /// 读换页动效(见 FxStyle)。没存过 / 存了个不认得的数 = 默认那套。
    /// <para>【2.1.36】以前这里是一条手写 if 链, 每加一种风格就得来补一行 —— 忘了补
    /// 的话新风格**静默退回默认**: 界面上明明选了它, 重开又变回去了, 还找不出为什么。
    /// 现在"这个数认不认得"由 <see cref="FxStyles.Has"/> 一句话说了算 —— 它问的是
    /// <see cref="FxStyles.All"/> 那张表, 和名字 / 说明是同一份, 不可能只对上一半。</para>
    /// </summary>
    public static FxStyle LoadFxStyle()
    {
        Ensure();
        int n = Num("fxstyle", (int)FxStyle.Plain);
        return FxStyles.Has((FxStyle)n) ? (FxStyle)n : FxStyle.Plain;
    }

    public static void SaveFxStyle(FxStyle s)
    {
        Put("fxstyle", ((int)s).ToString(CultureInfo.InvariantCulture));
        Flush();
    }

    /// <summary>
    /// 读按键形状(见 KeyShape)。没存过 / 存了个不认得的数 = 方块那一档。
    /// <para>和 <see cref="LoadFxStyle"/> 同一条规矩: "这个数认不认得"由
    /// <see cref="KeyShapes.Has"/> 说了算, 它问的是 <see cref="KeyShapes.All"/> 那张表 ——
    /// 和名字 / 说明是同一份, 不可能只对上一半。</para>
    /// </summary>
    public static KeyShape LoadKeyShape()
    {
        Ensure();
        int n = Num("keyshape", (int)KeyShape.Square);
        return KeyShapes.Has((KeyShape)n) ? (KeyShape)n : KeyShape.Square;
    }

    public static void SaveKeyShape(KeyShape s)
    {
        Put("keyshape", ((int)s).ToString(CultureInfo.InvariantCulture));
        Flush();
    }

    // ================= 输入习惯 =================

    /// <summary>习惯记多少条封顶。**没有上限的话它会一直长** —— 打过的每种切法
    /// 都记一条, 一年能攒出上万条。超了就按"用得少 + 最久没用"淘汰一批。</summary>
    public const int MAX_USE = 8000;

    /// <summary>淘汰时保留多少(留点余量, 免得每次写盘都淘汰)。</summary>
    const int USE_KEEP = 7200;

    public static Dictionary<string, int> LoadUse(out Dictionary<string, int> last)
    {
        Ensure();
        last = new Dictionary<string, int>(_last);
        return new Dictionary<string, int>(_use);
    }

    public static void SaveUse(Dictionary<string, int> d, Dictionary<string, int> last)
    {
        Ensure();
        _use.Clear();
        _last.Clear();
        if (d != null)
            foreach (var kv in d)
            {
                if (kv.Key.Length == 0 || kv.Value <= 0) continue;
                _use[kv.Key] = kv.Value;
                _last[kv.Key] = last != null && last.TryGetValue(kv.Key, out var l) ? l : 0;
            }
        TrimUse();
        Flush();
    }

    /// <summary>
    /// 超过 <see cref="MAX_USE"/> 就砍到 <see cref="USE_KEEP"/>。
    /// 排序认"发出次数"再认"最后一次是第几次输入" —— 又少用又久没碰的先走。
    /// </summary>
    static void TrimUse()
    {
        if (_use.Count <= MAX_USE) return;
        var order = _use.Keys
            .OrderBy(k => _use[k])
            .ThenBy(k => _last.TryGetValue(k, out var l) ? l : 0)
            .Take(_use.Count - USE_KEEP);
        foreach (var k in order.ToList()) { _use.Remove(k); _last.Remove(k); }
        Note($"输入习惯: {MAX_USE} 条封顶, 淘汰掉最冷的一批, 现在 {_use.Count} 条");
    }

    // ================= 窗口F 的快捷 =================

    /// <summary>窗口F 一共几格(5 页 × 9)。</summary>
    public const int MaxQuickSlot = 45;

    /// <summary>一条快捷最长多少字。短语本来就是一句话, 200 字够长了。</summary>
    const int MAX_QUICK_TEXT = 200;

    public static List<(int Num, QKind Kind, string Text)> LoadQuick()
    {
        Ensure();
        var res = new List<(int, QKind, string)>();
        if (_quick == null) return res;
        for (int i = 0; i < _quick.Length; i++)
            if (_quick[i] != null) res.Add((i + 1, _quick[i].Kind, _quick[i].Text));
        return res;
    }

    public static void SaveQuick(IReadOnlyList<QuickItem> slots)
    {
        Ensure();
        _quick = new QuickItem[MaxQuickSlot];
        if (slots != null)
            for (int i = 0; i < Math.Min(slots.Count, MaxQuickSlot); i++)
            {
                var it = slots[i];
                if (it == null) continue;                   // 空位: 位置由别的行自己带着
                var t = OneLine(it.Text);
                if (t.Length > MAX_QUICK_TEXT) t = t[..MAX_QUICK_TEXT];
                _quick[i] = new QuickItem { Kind = it.Kind, Text = t };
            }
        Flush();
    }

    // ================= 导入导出(换机器用) =================
    //
    // 导出格式(明文, 换机器要能读):
    //     [use]    下面是 拼音 次数 最后一次
    //     [quick]  下面是 槽位 种类 内容
    // **和存档内部那三段是两回事**: 这一份永远明文、永远这个格式, 存档内部怎么
    // 存都跟它无关 —— 所以存档改成一份文件, 导出导入一点没变。

    /// <summary>
    /// 导出到你自己挑的路径。**刻意写明文**: 这是给"换机器"用的, 写成密文对方那台
    /// 机器解不开(密钥绑在你这个 Windows 账号上, 不会跟着文件走)。
    /// 习惯和快捷一起走 —— 换台机器不用重新录。
    /// </summary>
    public static bool ExportAll(string path, Dictionary<string, int> use,
                                 Dictionary<string, int> last, IReadOnlyList<QuickItem> quick)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("# 柄输 备份(明文): 输入习惯 + 窗口F 的快捷短语 / 快捷键\n");
            sb.Append("# 里面是你常打的词、自己拼的名字和常发的整句话, 别随手发给别人。\n");
            sb.Append(SEG_USE).Append('\n');
            if (use != null)
                foreach (var kv in use)
                    if (kv.Key.Length > 0 && kv.Value > 0)
                        sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(' ')
                          .Append(last != null && last.TryGetValue(kv.Key, out var l) ? l : 0).Append('\n');
            sb.Append(SEG_QUICK).Append('\n');
            if (quick != null)
                for (int i = 0; i < quick.Count; i++)
                    if (quick[i] != null) AppendQuickLine(sb, i, quick[i]);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 读一份备份。段标记认得就分段读, 没有(很老的备份)就整份当输入习惯。
    /// 返回有没有读到东西。
    /// </summary>
    public static bool ImportAll(string path, out Dictionary<string, int> use,
                                 out Dictionary<string, int> last,
                                 out List<(int Num, QKind Kind, string Text)> quick)
    {
        use = new Dictionary<string, int>();
        last = new Dictionary<string, int>();
        quick = new List<(int, QKind, string)>();
        try
        {
            if (!File.Exists(path)) return false;
            string text = File.ReadAllText(path);
            bool segmented = text.Contains(SEG_USE) || text.Contains(SEG_QUICK);

            int seg = 0;                       // 0 = 习惯, 1 = 快捷
            var useLines = new StringBuilder();
            var quickLines = new StringBuilder();
            foreach (var line in text.Split('\n'))
            {
                var s = line.Trim('\r', ' ', '\t');
                if (s.Equals(SEG_USE, StringComparison.OrdinalIgnoreCase)) { seg = 0; continue; }
                if (s.Equals(SEG_QUICK, StringComparison.OrdinalIgnoreCase)) { seg = 1; continue; }
                if (s.Length == 0 || s[0] == '#') continue;
                if (segmented)
                {
                    if (seg == 0) useLines.Append(s).Append('\n');
                    else quickLines.Append(s).Append('\n');
                }
                else useLines.Append(s).Append('\n');
            }
            // 读进**临时**的一份: 导入是"告诉你读到了什么", 怎么合并由外面说了算 ——
            // 直接写进内存那份的话, 下一次 SaveUse 会把它盖成"只剩这一份"。
            var tu = new Dictionary<string, int>();
            var tl = new Dictionary<string, int>();
            QuickItem[] tq = null;
            foreach (var line in useLines.ToString().Split('\n'))
            {
                var s = line.Trim('\r');
                if (s.Length > 0) ParseUseLine(s, tu, tl);
            }
            foreach (var line in quickLines.ToString().Split('\n'))
            {
                var s = line.Trim('\r');
                if (s.Length == 0) continue;
                ParseQuickLine(s, ref tq);
            }
            if (tu.Count > 0) { use = tu; last = tl; }
            if (tq != null)
                for (int i = 0; i < tq.Length; i++)
                    if (tq[i] != null) quick.Add((i + 1, tq[i].Kind, tq[i].Text));
            return use.Count > 0 || quick.Count > 0;
        }
        catch { return false; }
    }

    // ================= 头一次用: 从老目录搬一次 =================
    //
    // 2.1.5 之前, 设置和习惯散在 %LOCALAPPDATA%\GamepadT9 下的十来个 txt 里。
    // 新存档还没有的时候去那儿读一次, 搬进来 —— 你攒的习惯不用从头再来。
    // **只搬这一次**(之后 <c>Binput.save</c> 存在了就不再看老目录), 而且**老文件
    // 一个都不删**(删什么是你的事)。等哪天没人用 2.1.4 了, 整段可以直接删掉。

    static void MigrateOld()
    {
        if (_dirOverride != null) return;      // 探针的临时目录: 别去动真机上那份
        try
        {
            var old = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamepadT9");
            if (!Directory.Exists(old)) return;

            // 设置: 那几个"一个开关一个文件"的 txt
            bool got = false;
            got |= CopyKey("mode.txt", 2, (a, b) => { Put("preview", a != "0" ? "1" : "0");
                                             Put("tier", int.TryParse(b, out var t) ? Math.Clamp(t, 0, 3).ToString() : "0"); });
            got |= CopyKey("theme.txt", "theme");
            got |= CopyKey("key9num.txt", "key9num");
            got |= CopyKey("swap15.txt", "swap15");
            got |= CopyRaw("wake.txt", "wake");
            got |= CopyKey("slide.txt", 2, (a, b) => {
                Put("slide", a == "0" ? "0" : ((int)SlideMode.Type).ToString());
                Put("dwell", int.TryParse(b, out var d) ? NearestDwell(Math.Clamp(d, 30, 600)).ToString()
                                                        : DEFAULT_DWELL.ToString());
            });
            got |= CopyKey("scale.txt", 3, (a, b, c) => { Put("scale", a); Put("ox", b); Put("oy", c); });

            // 习惯 + 快捷: 老格式也是 DPAPI 加密的, 头是 GTU1
            int nu = 0;
            foreach (var line in (ReadOld(Path.Combine(old, "pinyin.txt")) ?? "").Split('\n'))
            {
                var s = line.Trim('\r', ' ', '\t');
                if (s.Length == 0 || s[0] == '#') continue;
                int before = _use.Count;
                ParseUseLine(s);
                if (_use.Count > before) nu++;
            }
            int nq = 0;
            foreach (var line in (ReadOld(Path.Combine(old, "quick.txt")) ?? "").Split('\n'))
            {
                var s = line.Trim('\r', ' ', '\t');
                if (s.Length == 0 || s[0] == '#') continue;
                _quick ??= new QuickItem[MaxQuickSlot];
                int before = _quick.Count(x => x != null);
                ParseQuickLine(s);
                if (_quick.Count(x => x != null) > before) nq++;
            }

            if (got || nu > 0 || nq > 0)
            {
                Note($"数据搬家: 从老目录(%LOCALAPPDATA%\\GamepadT9)搬来 {nu} 条习惯、{nq} 条快捷");
                Flush();
            }
        }
        catch { }
    }

    /// <summary>读老目录里某个 txt(密文 / 明文都认)。没有或解不开就 null。</summary>
    static string ReadOld(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var raw = File.ReadAllBytes(path);
            if (HasMagic(raw, OLD_MAGIC))
            {
                try { return Encoding.UTF8.GetString(Unprotect(raw[OLD_MAGIC.Length..])); }
                catch { return null; }
            }
            return Encoding.UTF8.GetString(raw);
        }
        catch { return null; }
    }

    /// <summary>老目录里那个文件在, 就把它的内容当成某个键的值。搬到了返回 true。</summary>
    static bool CopyRaw(string file, string key)
    {
        var t = ReadOld(OldPath(file));
        if (t == null) return false;
        Put(key, t.Trim());
        return true;
    }

    /// <summary>只取第一段(一个值就是一个开关的那种老文件)。</summary>
    static bool CopyKey(string file, string key) => CopyKey(file, 1, a => Put(key, a.Trim()));

    static bool CopyKey(string file, int parts, Action<string> one)
    {
        var p = OldParts(file);
        if (p == null || p.Length < 1) return false;
        one(p[0]);
        return true;
    }

    static bool CopyKey(string file, int parts, Action<string, string> two)
    {
        var p = OldParts(file);
        if (p == null || p.Length < 2) return false;
        two(p[0], p[1]);
        return true;
    }

    static bool CopyKey(string file, int parts, Action<string, string, string> three)
    {
        var p = OldParts(file);
        if (p == null || p.Length < 3) return false;
        three(p[0], p[1], p[2]);
        return true;
    }

    /// <summary>老目录里某个 txt 按空白切成几段。文件不在 / 解不开就 null。</summary>
    static string[] OldParts(string file)
    {
        var t = ReadOld(OldPath(file));
        return t == null ? null
            : t.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    static string OldPath(string file) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamepadT9", file);
}
