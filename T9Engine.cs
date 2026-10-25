using System.IO;

namespace Binput;

/// <summary>
/// T9 九键拼音引擎: 数字串 -> 拼音切分 -> 排序
///
/// 注意: 这里**不出汉字**。汉字、词组、联想全部交给系统输入法,
/// 本引擎只负责"这串数字能切成哪些拼音"以及"哪种切分更像一句人话"。
/// 所以词库里只需要拼音键(是不是一个成词的读音), 汉字一个都用不上。
/// </summary>
public static class T9Engine
{
    // 409 个合法拼音音节(无声调)
    const string SYLLABLES =
        "a ai an ang ao ba bai ban bang bao bei ben beng bi bian biao bie bin bing bo bu ca cai can cang cao ce cen ceng cha chai chan chang chao che chen cheng chi chong chou chu chua chuai chuan chuang chui chun chuo ci cong cou cu cuan cui cun cuo da dai dan dang dao de dei den deng di dia dian diao die ding diu dong dou du duan dui dun duo e ei en eng er fa fan fang fei fen feng fo fou fu ga gai gan gang gao ge gei gen geng gong gou gu gua guai guan guang gui gun guo ha hai han hang hao he hei hen heng hong hou hu hua huai huan huang hui hun huo ji jia jian jiang jiao jie jin jing jiong jiu ju juan jue jun ka kai kan kang kao ke kei ken keng kong kou ku kua kuai kuan kuang kui kun kuo la lai lan lang lao le lei leng li lia lian liang liao lie lin ling liu long lou lu luan lun luo lv lve ma mai man mang mao me mei men meng mi mian miao mie min ming miu mo mou mu na nai nan nang nao ne nei nen neng ni nian niang niao nie nin ning niu nong nou nu nuan nuo nv nve o ou pa pai pan pang pao pei pen peng pi pian piao pie pin ping po pou pu qi qia qian qiang qiao qie qin qing qiong qiu qu quan que qun ran rang rao re ren reng ri rong rou ru rua ruan rui run ruo sa sai san sang sao se sen seng sha shai shan shang shao she shei shen sheng shi shou shu shua shuai shuan shuang shui shun shuo si song sou su suan sui sun suo ta tai tan tang tao te teng ti tian tiao tie ting tong tou tu tuan tui tun tuo wa wai wan wang wei wen weng wo wu xi xia xian xiang xiao xie xin xing xiong xiu xu xuan xue xun ya yan yang yao ye yi yin ying yo yong you yu yuan yue yun za zai zan zang zao ze zei zen zeng zha zhai zhan zhang zhao zhe zhei zhen zheng zhi zhong zhou zhu zhua zhuai zhuan zhuang zhui zhun zhuo zi zong zou zu zuan zui zun zuo";

    // 内置兜底词库: 只要拼音键, 不要汉字(汉字由系统输入法出)。
    // 正式使用会被 dict.txt 覆盖扩充(见 README 的"换词库"一节)。
    // 没有 dict.txt 时这些词拿不到真词频, 统一按 DEFAULT_FREQ 算 —— 比"有词频的"低,
    // 但仍然是成词, 排序上还是压过不成词的切分。
    const string PHRASES =
        "a ba bangmang bu buhao buxing buzhidao chifan da daye de dengyixia diannao duibuqi gan gao " +
        "gun guo hao haode hen hun huo jianpan jiayou jieshu jintian jiushi kaishi kan keyi kuaidian " +
        "lai laile le lihai ma meishi meiyou mingtian nali ni nihao nihaoma nimen niubi pengyou qu " +
        "shang shenme shijian shoubing shuijiao shuo shuru ta tuandui tuanzhan wanshang weishenme wo " +
        "women xia xian xianzai xiexie xin xing yin yiqi yixia you youxi zai zaijian zaima zaoshang " +
        "zenme zenmeban zhang zheli zhidao zoule ";

    /// <summary>内置兜底词给的词频。真词库一加载就被盖过去了。</summary>
    const int DEFAULT_FREQ = 100;

    // 音节热度。以前是手填的 40 多条, 现在改成**从拼音表自动统计**:
    // 把每条拼音按"块数最少"切开数一遍, 出现得越多分越高(见 BuildHot)。
    // 没在表里出现过的音节走 Weight 的兜底分, 天然排在后面。
    static readonly Dictionary<string, double> _hot = new();

    /// <summary>
    /// 用户挑过哪些切分: **拼音切法**(带撇, 如 "chuang'sheng") -> 选中发出的次数。
    /// <para>【2.1.36】以前这里的键是**连写**(把撇去掉的 chuangsheng)。连写分不出切法 ——
    /// chuang'sheng 和 chu'ang'sheng 拼起来是同一串 chuangsheng, 于是两种切法共用
    /// 一个计数器: 不管你发的是哪一条, 两条一起加分, "谁发得多谁排前面"在数据这一层
    /// 就不成立(用户连着报的三个现象都是这一个根)。现在一种切法一个身份。</para>
    /// <para>查词库仍然要用连写(词库里没有撇), 由 <see cref="Letters"/> 现算。</para>
    /// 见 Rank / BumpUse。
    /// </summary>
    static readonly Dictionary<string, int> _use = new();
    /// <summary>
    /// 每条切分**最后一次**是全局第几次输入。用来算"多久没用了"(见 LearnBonus)。
    /// 只记次数不记日期: 你打多少字才有一次衰减, 比"过了一天"更贴合实际。
    /// </summary>
    static readonly Dictionary<string, int> _last = new();
    /// <summary>全局输入序号。每发出去一条拼音就 +1, 是所有 _last 的参照点。</summary>
    static int _seq;
    /// <summary>上次存盘以来新增了多少条 —— 攒够 USE_SAVE_EVERY 条才写一次文件。</summary>
    static int _useNew;

    const int USE_SAVE_EVERY = 20;

    static readonly Dictionary<string, List<string>> _code2syl = new();

    /// <summary>
    /// 成词的拼音串 -> 词频("shoudao" 有, "shouda" 没有)。只要键, 不存汉字。
    /// <para>词频是 1.0.3 才接回来的。以前只要"成词"就一律 +1000, 词频不参与排序,
    /// 于是 7443 里 四个(15981) 会被 七个(2105) 压在后面 —— 平票只能靠音节热度(
    /// qi 89 &gt; si 85)决胜负, 跟真实常用度对不上。同一个拼音键对应多个汉字词时
    /// (是的 / 使得 都是 shide)取最高频那条 —— 代表"这个读音最常出现的那个词"。</para>
    /// </summary>
    static readonly Dictionary<string, int> _words = new();

    /// <summary>词库里最高的词频, 用来把词频归一化到 0~1。</summary>
    static int _maxFreq = 1;

    /// <summary>
    /// 九键每个数字上的字母。顺序按"这个字母开头的音节有多少"排, 常见的在前。
    /// <para><b>2.0.0 换过一次位: JKL 从 5 挪到了 1。</b>原因是滑行输入要用摇杆绕一圈,
    /// 而正中间那一格压根转不到 —— 它只能留给别的功能(现在那里是"长按开窗口F")。
    /// 挪去左上角最合适: j / k / l 在拼音里**只做声母**, 一个音节里只可能出现在开头,
    /// 永远不会排在中间, 所以换位置既不影响解析也不影响手感, 上手成本几乎为零。
    /// 左上角原本空着的那一格也终于派上用场了。</para>
    /// </summary>
    static readonly Dictionary<char, char[]> _letters = new()
    {
        ['1'] = new[] { 'j', 'k', 'l' },
        ['2'] = new[] { 'a', 'b', 'c' },
        ['3'] = new[] { 'd', 'e', 'f' },
        ['4'] = new[] { 'g', 'h', 'i' },
        ['6'] = new[] { 'm', 'n', 'o' },
        ['7'] = new[] { 'p', 'q', 'r', 's' },
        ['8'] = new[] { 't', 'u', 'v' },
        ['9'] = new[] { 'w', 'x', 'y', 'z' },
    };

    static T9Engine()
    {
        foreach (var s in SYLLABLES.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            _code2syl.GetOrAdd(ToT9(s)).Add(s);

        // 按"多少个音节以它开头"给每个键上的字母排个序, 兜底候选才不会出些稀奇古怪的
        var head = new Dictionary<char, int>();
        foreach (var s in SYLLABLES.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            head[s[0]] = head.GetValueOrDefault(s[0]) + 1;
        foreach (char d in _letters.Keys.ToList())
            _letters[d] = _letters[d].OrderByDescending(c => head.GetValueOrDefault(c)).ToArray();

        // 按长度分桶, 统计音节热度时不用每次都试 1~6 个字符去字典里查
        foreach (var s in SYLLABLES.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (s.Length <= 6) _sylByLen[s.Length].Add(s);

        foreach (var k in PHRASES.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            _words[k] = DEFAULT_FREQ;

        // 没有 dict.txt 时也得有个热度表, 那就先用内置那几十条顶着
        BuildHot();
        // 最高词频**内置那几十条也要算进去**。以前只在 LoadExternalDict 里更新,
        // 于是没有 dict.txt 时它一直停在 1, Norm() 会把 50000 归一化成
        // ln(50001)/ln(2)=15.6 倍 —— 自造词那一档直接飙到一万三千分,
        // 把真词库里的常用词全压在下面(实测: 探针没加载词库时 pa'pao'sao 13113 分)。
        _maxFreq = Math.Max(1, _words.Values.Count > 0 ? _words.Values.Max() : 1);
    }

    /// <summary>按长度分桶的音节表。下标 = 音节长度(1~6), 0 号不用。</summary>
    static readonly HashSet<string>[] _sylByLen =
        { new(), new(), new(), new(), new(), new(), new() };

    static List<string> GetOrAdd(this Dictionary<string, List<string>> d, string k)
    {
        if (!d.TryGetValue(k, out var v)) d[k] = v = new List<string>();
        return v;
    }

    /// <summary>拼音 -> T9 数字串, 如 hao -> 426。JKL 从 2.0.0 起是 1 键(见 _letters)。</summary>
    public static string ToT9(string syllable)
    {
        var sb = new System.Text.StringBuilder(syllable.Length);
        foreach (var c in syllable)
            sb.Append(c switch
            {
                >= 'a' and <= 'c' => '2', >= 'd' and <= 'f' => '3',
                >= 'g' and <= 'i' => '4', >= 'j' and <= 'l' => '1',
                >= 'm' and <= 'o' => '6', >= 'p' and <= 's' => '7',
                >= 't' and <= 'v' => '8', >= 'w' and <= 'z' => '9',
                _ => '0'
            });
        return sb.ToString();
    }

    /// <summary>某个音节的分(20~100)。表里没有的走兜底 10+长度, 天然垫底。</summary>
    public static double HotOf(string syl) => _hot.TryGetValue(syl, out var w) ? w : 10 + syl.Length;

    static double Weight(string syl) => HotOf(syl);

    /// <summary>
    /// 音节热度的平均水平(表里是 20~100)。Rank 里每多切一块就扣掉一个这个数,
    /// 好让"切成几个字"这件事本身不再加减分 —— 见 Rank 里的说明。
    /// </summary>
    const double SYL_AVG = 60;

    /// <summary>词库里有没有这个拼音连写(如 "shoudao")。</summary>
    public static bool HasPhrase(string key) => key.Length > 0 && _words.ContainsKey(key);

    /// <summary>这个拼音连写的词频。不成词就是 0。</summary>
    public static int FreqOf(string key) =>
        key.Length > 0 && _words.TryGetValue(key, out int f) ? f : 0;

    /// <summary>
    /// 一组音节的**身份**: 带撇连起来("chuang'sheng")。学习和保送全用它当键。
    /// <para>不能用去撇的连写当身份: chuang'sheng 和 chu'ang'sheng 会并成同一条,
    /// 两种切法从此同分、同命运, 谁也超不过谁。</para>
    /// </summary>
    public static string KeyOf(string[] syls) =>
        syls == null || syls.Length == 0 ? "" : string.Join('\'', syls);

    /// <summary>
    /// 把 <see cref="KeyOf"/> 的身份还原成**连写**("chuangsheng") —— 查词库用这个。
    /// </summary>
    public static string Letters(string key) =>
        string.IsNullOrEmpty(key) ? "" : key.Replace("'", "");

    /// <summary>这条切分你亲自发出去过几次(见 BumpUse)。没发过就是 0。</summary>
    public static int UseOf(string key) =>
        key.Length > 0 && _use.TryGetValue(key, out int n) ? n : 0;

    // ---- "你用得多"这一层(1.0.7 重做) ----
    // 词库词频是**所有人**的统计: 它决定了 shi'de(28566) 天然压过 qi'ge(2105),
    // 这条没错 —— 大多数时候你要的就是"使得"。但每个人打的字不一样,
    // 你要是天天打"七个", 每次按到 7443 都先蹦出"使得"就很烦。
    //
    // 这一层以前是"乘在词频上"(×2 的次数次方), 实测下来有两个毛病:
    //   1. 涨得太慢 —— 每次只有 47 分, 从零追一个打满的对手要 14 次, 太久了;
    //   2. 封顶之后"你用过几次"这个信息被彻底抹平, 于是两个都打满的词分不出
    //      高下, 谁在上面全看切成几个字 —— 谁也翻不动谁(6263 打 50 次也追不上)。
    // 现在改成**单独一笔加分**, 不掺进词频里, 三条规矩:
    const double SPRINT = 150;   // ① 开头涨得快: 打 4 次就基本到顶, 不用打十几次
    const int SPRINT_N = 3;      //    (从第 2 次起算, 见 LearnBonus)
    const double TAIL = 3;       // ② 之后每次只涨一点, 但**不封顶**: 打得多仍然占优
    const double DECAY = 2;      // ③ 越久没用扣得越多(每落后一次输入), 扣到 0 为止
    // ③ 是"新词凭什么翻上去"的关键: ①② 让两个词的分数本来就贴得很近,
    // 老词久不用让出一点位置, 新词打 4 次就够; 而老词你天天打它就不掉, 理应在前面。
    // 顺带也解决了"一个词永久霸着第一名" —— 不用了它自己会沉下去。

    /// <summary>
    /// "我用过"这一笔加多少分。没发过就是 0。见上面 SPRINT / TAIL / DECAY 三条。
    /// </summary>
    public static double LearnBonus(string key)
    {
        int n = UseOf(key);
        if (n <= 0) return 0;
        // 第 1 次**不给冲刺分**, 只给那点细水长流。手滑发错一遍不该立刻改排名 ——
        // "拼两次才算数"这条规矩(MYWORD_MIN)本来就是防这个的, 冲刺分得跟着它走,
        // 不然一次 150 分太猛, 名字拼一遍就冒到第一了。
        double s = SPRINT * Math.Min(n - 1, SPRINT_N) + TAIL * n;
        int lag = _seq - (_last.TryGetValue(key, out int l) ? l : _seq);
        s -= DECAY * lag;
        return s > 0 ? s : 0;   // 扣到 0 为止: 不至于比"从没用过"还低
    }

    // ---- 你自己拼出来的词(姓名 / 网名 / 术语) ----
    // 这种整串词库里一定没有。按 Y 一个字一个字定完之后, 光记"你发过"还是不够:
    // 不成词的切分最多只能拿前缀那 600 分, 永远压不过成词档的 700+, 于是下次
    // 打同样的数字串, 你要的名字照样排在一堆乱切的东西后面, 还得再按一遍 Y。
    // 所以给它单独开一档: 拼够 MYWORD_MIN 次就当它是个词, 直接进成词那一档。
    /// <summary>拼过这么多次才算"你自己的词"。1 次太随意, 打错一遍就多一条脏数据。</summary>
    public const int MYWORD_MIN = 2;
    /// <summary>整串至少这么长才记。两三个字母的基本都是普通词, 词库里有。</summary>
    public const int MYWORD_MINLEN = 4;
    /// <summary>
    /// 你自己的词按多少词频起步。50000 是这么定的: 换算过来是 1262 分(加上音节分
    /// 约 1400), 而你的名字在同一串数字里的对手通常是"前面几个字凑巧成词"那种
    /// (78674264 的对手是 sun'shang, 词频 9018, 1319 分)—— 起步就得压过它,
    /// 不然你拼了两遍还是排第二, 等于没记住。
    /// 上限仍然是"使得"(28566, 带音节分约 1486)这一档: 真常用的词压不过,
    /// 要压过它得再拼三次 —— 你自己的词该排第一, 但不该第一天就霸榜。
    /// </summary>
    const int MYWORD_FREQ = 50000;

    public static bool IsMine(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        // 词库里没有撇, 按连写问; "这条是不是你的"则按这条切法自己的次数算
        string flat = Letters(key);
        return flat.Length >= MYWORD_MINLEN && !HasPhrase(flat) && UseOf(key) >= MYWORD_MIN;
    }

    /// <summary>
    /// 这一刀**切法**是不是你自己拼出来的(见 <see cref="_mine"/>)。
    /// <para>【排序一律用这个, 不用 <see cref="IsMine"/>】IsMine 问的是"这串字母
    /// 是不是你的词", 而排序要问的是"**这一刀**是不是你切的" —— 两者不是一回事:
    /// chuang'sheng 和 chu'ang'sheng 拼起来都是 chuangsheng, 按连写判后者也算
    /// "你的词", 于是它跟着一起拿到成词档那 700 多分, 连"多切一块扣 300"的惩罚
    /// 也被一道豁免(见 RankOrder)—— 打两遍之后碎的那条反而登顶, 而且从此打多少
    /// 遍都翻不回来: 学习加分记的也是连写, 两条拿的一样多, 差距是死的。
    /// 这就是用户报的那个"chuang'sheng 打几次也上不去"。</para>
    /// <para>IsMine 仍然留着: 设置页数"你有几个自己的词"问的确实是**词**,
    /// 按连写数才对。</para>
    /// </summary>
    public static bool IsMineSplit(string[] syls)
    {
        if (syls == null || syls.Length == 0) return false;
        string digits = string.Concat(syls.Select(ToT9));
        if (digits.Length == 0 || !_mine.TryGetValue(digits, out var list)) return false;
        foreach (var m in list)
            if (m.Length == syls.Length && m.SequenceEqual(syls)) return true;
        return false;
    }

    // 你自己的词按固定的 MYWORD_FREQ 起步, 它自己的"你用得多不多、多近用过"
    // 那部分走 LearnBonus, 和词库词共用同一条曲线 —— 不再单独乘一遍。

    /// <summary>你现在有多少个自己的词。给设置页显示。</summary>
    public static int MineCount => _use.Count(kv => IsMine(kv.Key));

    // ---- 你自己拼出来的整词: 候选池的"保送名额" ----
    //
    // **光记"你用过几次"是不够的 —— 那条切分得先在候选池里才行。**
    // Segment 是 beam search: 每一位只留 40 条, 排序只看"块数少 + 音节热",
    // 整串成不成词它一概不管。726726726 那种一串数字有 4×4×4 种三块切法时,
    // 只有音节热度最高的 40 条能进池子, pao'pao'sao 这种冷门组合**连候选都进不去**。
    // 于是按 Y 一个字一个字拼出来的名字/网名, 学习分再高也白搭 ——
    // 实测: --use=paopaosao*2 之后 --find=pao'pao'sao 仍然是【不在】,
    // 下次打同样的数字串还得再按三遍 Y。Rank / LearnBonus 压根没机会起作用。
    //
    // 所以给"拼够 MYWORD_MIN 次的整词"开一条后门: 下次打同样的数字串,
    // 直接把它塞进候选池(见 InputCore.CompleteSegs)。分数仍走 Rank 那套
    // (IsMine 的成词档), 保送只解决"进不去池子", 不额外加分。
    static readonly Dictionary<string, List<string[]>> _mine = new();

    /// <summary>这个数字串上你拼出过哪些整词(音节已经切开)。没有就是空。</summary>
    public static IEnumerable<string[]> MineOf(string digits) =>
        digits.Length > 0 && _mine.TryGetValue(digits, out var l)
            ? l : Enumerable.Empty<string[]>();

    /// <summary>
    /// 记住一条"我按 Y 一个字一个字定出来的整词", 下次打同样的数字串保送进候选。
    /// 传进来的必须是**已经切开**的音节数组(pao / pao / sao); 连写由这里自己拼。
    /// </summary>
    public static bool AddMine(string[] syls)
    {
        if (syls == null || syls.Length < 2) return false;   // 单音节不需要保送
        if (syls.Any(y => y.Length == 0 || !y.All(char.IsAsciiLetter))) return false;

        string key = KeyOf(syls);
        if (Letters(key).Length < MYWORD_MINLEN) return false;
        if (HasPhrase(Letters(key))) return false;   // 词库里本来就有, Segment 会给
        if (UseOf(key) < MYWORD_MIN) return false;   // 还没拼够次数, 不作数

        var digits = string.Concat(syls.Select(ToT9));
        if (digits.Length == 0 || digits.Any(c => c < '2' || c > '9')) return false;

        if (!_mine.TryGetValue(digits, out var list)) _mine[digits] = list = new List<string[]>();
        // 【2.1.36】以前这里还要比"谁的块数更少"才决定留谁 —— 那是给 SplitKey 兜底的:
        // 那时 BumpUse 手上只有一串连写, 只能猜"最少块数那种最像人话"。而这一猜正是
        // 把这一刀张冠李戴的那一步: 你发的是 chu'ang'sheng, 它猜成 chuang'sheng。
        // 现在传进来的就是你亲手定的那一刀, 按身份去重即可, 不再比块数。
        string who = KeyOf(syls);
        if (list.Any(s => KeyOf(s) == who)) return false;
        list.Add(syls);
        return true;
    }

    /// <summary>
    /// 拼音连写 -> 若干种音节切法。paopaosao -> [pao, pao, sao]。最多 4 种, 单音节的不算。
    /// <para>**块数最少的排在最前**: 同一串字母往往不止一种切法 —— paopaosao 既能切成
    /// pao'pao'sao(3 块), 也能切成 pa'o'pa'o'sa'o(6 块), 后者每个音节都合法却根本不是
    /// 人要打的东西。</para>
    /// <para>【2.1.36】**只给老存档迁移用**(见 <see cref="MigrateKey"/>)。以前它还兼着
    /// "BumpUse 攒够次数时猜你切的是哪一种" —— 那是把 chu'ang'sheng 记成 chuang'sheng
    /// 的直接原因。现在学习和保送全程带着切法走, 一个猜字都不需要。</para>
    /// <para>**刻意不走 Segment** —— 那条路正是会被 beam 裁掉的那条(见 _mine 的说明),
    /// 拿它来还原等于什么都没修。这里按音节表直接对字母做 DFS: 精确、不截断,
    /// 而且目标串是固定的, 规模很小。</para>
    /// </summary>
    static List<string[]> SplitKey(string key)
    {
        var all = new List<string[]>();
        if (key.Length < 2 || !key.All(char.IsAsciiLetter)) return all;

        var path = new List<string>();
        void Dfs(int pos)
        {
            if (all.Count >= 64) return;
            if (pos == key.Length) { if (path.Count >= 2) all.Add(path.ToArray()); return; }
            for (int L = 1; L <= 6 && pos + L <= key.Length; L++)
            {
                string s = key.Substring(pos, L);
                if (!_sylByLen[L].Contains(s)) continue;
                path.Add(s);
                Dfs(pos + L);
                path.RemoveAt(path.Count - 1);
            }
        }
        Dfs(0);
        return all.OrderBy(s => s.Length).Take(4).ToList();
    }

    /// <summary>开机把"自己的词"从学习表里捞回来, 重建保送索引。见 <see cref="_mine"/>。</summary>
    public static void RebuildMine()
    {
        _mine.Clear();
        foreach (var kv in _use)
        {
            if (!IsMine(kv.Key)) continue;
            // 键本身就是带撇的切法, 拆开即可 —— 不再走 SplitKey 去猜"最少块数那种"
            var syls = kv.Key.Split('\'');
            if (syls.Length >= 2) AddMine(syls);
        }
    }

    // 成词加分区间。下限 700 必须压得住"不成词"那一档的天花板
    // (最长有词前缀最多 PREFIX_CAP=600, 音节热度最多 100×块数),
    // 上限 1500 用来拉开常用词和冷门词 —— 1.0.2 之前是固定 +1000, 常用度完全不起作用。
    const double WORD_BASE = 700, WORD_SPAN = 800;
    /// <summary>"最长有词前缀"最多加多少。不封顶的话超长切分会反超成词。</summary>
    const double PREFIX_CAP = 600;

    /// <summary>词频归一化到 0~1(取对数: 词频是长尾分布, 线性会把绝大多数压成 0)。
    /// 传进来的可以是"词频 × 个人加成"之后的热度 —— 那时会算出 &gt;1, 正好就是
    /// "你常用的词能越过 1500 那条线"的意思, 不截。</summary>
    static double Norm(double freq) => Math.Log(1 + freq) / Math.Log(1 + _maxFreq);

    /// <summary>
    /// 给一种切分打分, 用来给"拼音联想"排序。
    /// 这不是在出词 —— 汉字还是系统输入法给的, 这里只是判断
    /// "这串拼音到底像不像一句人话", 好把 shou'dao 这种排到 shou'da'o 前面。
    /// 之前实测过: 数字串的歧义是数据问题, 靠词库命中率能砍掉 87%。
    ///
    /// 分数分三层, 层次不能乱:
    ///   +700~1500 / 前缀最多 +600   整串(或前缀)像不像话 + 有多常用
    ///   +20~100                     音节常不常见        —— 通用热度, 所有人一样
    ///   +0~160                      你以前挑过哪种      —— 只有你自己
    /// 最后一层故意压在最下面: 它只在"几种切法都挺像话"时才起作用,
    /// 也就是通用拼音表帮不上忙的那个区间, 学不出 sho'u'dao 这种鬼东西。
    /// </summary>
    public static double Rank(string[] syls)
    {
        if (syls == null || syls.Length == 0) return 0;
        string key = KeyOf(syls);      // 身份: 带撇, 一种切法一个
        string flat = Letters(key);    // 查词库要用连写(词库里没撇)
        double s = 0;

        if (HasPhrase(flat)) s += WORD_BASE + WORD_SPAN * Norm(FreqOf(flat));
        // 【按切法判, 不按连写判】见 IsMineSplit 的说明: 用 IsMine(key) 的话,
        // 同一串字母切得更碎的那几条会跟着一起领这笔"成词"分。
        else if (IsMineSplit(syls)) s += WORD_BASE + WORD_SPAN * Norm(MYWORD_FREQ);
        else
        {
            // 退而求其次: 最长有词的前缀越长越好(shou'dao'le 里 shou'dao 有词)
            for (int n = syls.Length - 1; n >= 1; n--)
            {
                if (HasPhrase(string.Concat(syls.Take(n))))
                { s += Math.Min(120.0 * n, PREFIX_CAP); break; }
            }
        }
        // 音节热度照常累加, 但每多切一块就扣掉一个"平均音节"。
        // 不扣的话, 切成三块天然比两块高 84 分(bi'er'bo vs che'san 实测值)——
        // "这个词是几个字"跟"你想不想打它"没关系, 不该混进排名里。
        // 扣完之后三块和两块站在同一条线上, 谁在前面只看词频和你用得多不多。
        foreach (var y in syls) s += Weight(y);
        s -= SYL_AVG * (syls.Length - 1);
        // 你用过 -> 单独一笔加分。前 4 次陡涨, 之后缓涨不封顶, 久不用会回落。
        // 【2.1.36】不再乘 LearnShare: 学习本来就按切法记(_use 的键带撇),
        // 这一笔天然只加在你亲手发过的那一刀上, 用不着再按比例分配给谁。
        s += LearnBonus(key);
        return s;
    }

    /// <summary>
    /// 每多切出一块要扣多少排序分 —— "块数少更像话"这条启发式的分量。
    /// <para>它不能是 0: Rank 里虽然已经扣掉了"平均音节"让块数不再天然加分,
    /// 但切得碎的词可以挑一堆**比平均更常见**的音节来凑, 实测仍能白赚一两百分,
    /// 纯按分数排的话 243726 会冒出 ci'er'an、suo'si'a 这种硬凑的碎块。</para>
    /// <para>它也不能是"硬分档"(以前的做法): 那样你按 Y 拼出来的名字永远翻不了身 ——
    /// bi'er'bo 是 3 块、1522 分, che'san 是 2 块、272 分, 差 1250 分却只能排在后面。
    /// 300 落在 208(噪声)和 1250(真需求)中间, 两边都照顾到。</para>
    /// </summary>
    const double BLOCK_PENALTY = 300;

    /// <summary>
    /// 候选排序实际用的分数 = Rank 减去"比这串数字的最少块数多了几块"的惩罚。
    /// <para>**你自己拼出来的词不罚**: 那是你按 Y 一个字一个字定过、
    /// 而且重复拼过至少两次的, 比"块数少更像话"这条通用启发式可信得多。
    /// 不豁免的话, 名字碰到同串里任何一个凑巧成词的对手就又掉下去了。</para>
    /// <para>豁免按**切法**认(见 <see cref="IsMineSplit"/>): 按连写认的话,
    /// 你定的是 chuang'sheng, 连 chu'ang'sheng 也一起免了罚 —— 它基础分本来就
    /// 高一百多, 免罚之后立刻翻到你那条上面去。</para>
    /// </summary>
    public static double RankOrder(string[] syls, int minBlocks)
    {
        if (syls == null || syls.Length == 0) return 0;
        double s = Rank(syls);
        if (syls.Length > minBlocks && !IsMineSplit(syls))
            s -= BLOCK_PENALTY * (syls.Length - minBlocks);
        return s;
    }

    // ---------- 输入习惯(自学习) ----------

    /// <summary>
    /// 用户把这条切分发给输入法了 —— 记一次。只有"发出去"才算确认。
    /// <para>传进来的 key 必须是**带撇的切法**("chuang'sheng"), 见 <see cref="KeyOf"/>。
    /// 以前可以传连写, 内部再跑 SplitKey 猜"最少块数那种" —— 那是整个 2.1.36 连着
    /// 报三次的根: 你发的那一刀被猜成了另一刀。</para>
    /// </summary>
    public static void BumpUse(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _seq++;                                     // 全局步数往前走一格
        int now = _use.TryGetValue(key, out int n) ? n + 1 : 1;
        _use[key] = now;
        _last[key] = _seq;                          // 这条现在是"最近"用过的
        _useNew++;
        // 刚拼够 MYWORD_MIN 次 -> 补一张"保送进候选"的票(见 AddMine)。
        // 只在正好够的那一次做, 之后每隔一次都跑是白费, 而且它早进索引了。
        // 保送的就是这条 key 原本的切法 —— 照着撇切开即可, 一个字都不用猜。
        if (now == MYWORD_MIN)
        {
            var syls = key.Split('\'');
            if (syls.Length >= 2) AddMine(syls);
        }
    }

    /// <summary>
    /// 开机时读回上次存下来的输入习惯。last 是"最后一次是全局第几次输入",
    /// 老版本的文件没有这一列, 那种按"就当是刚用过"处理 —— 不能让老数据
    /// 一开机就被扣一波。
    /// </summary>
    /// <summary>
    /// 存档里的键还原成**切法身份**。2.1.36 以前存的是连写(没有撇), 那种连当初怎么
    /// 切的都没记, 只能猜"块数最少那种最像人话" —— 猜错的概率不小, 但比一升级就把
    /// 几千条积累全作废强得多。新格式(本来带撇)原样返回。
    /// </summary>
    static string MigrateKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Contains('\'')) return key;                    // 已经是切法身份
        if (!key.All(char.IsAsciiLetter)) return key;           // 哨兵之类, 照原样留着
        var guess = SplitKey(key);
        return guess.Count > 0 ? KeyOf(guess[0]) : key;         // 猜不出来就保持原样
    }

    public static void LoadUse(Dictionary<string, int> data, Dictionary<string, int> last = null)
    {
        // 【2.1.36 老存档迁移】先把连写的老键翻成切法键。同一个新键可能被好几条老键
        // 翻到(psalms 和 psalm's 都会翻成前者), 取次数高的那条 —— 这里是"还原",
        // 不是"合并", 相加会凭空把冷词抬热。
        var use = new Dictionary<string, int>();
        foreach (var kv in data)
        {
            if (kv.Key.Length == 0 || kv.Value <= 0) continue;
            string k = MigrateKey(kv.Key);
            if (k.Length == 0) continue;
            if (!use.TryGetValue(k, out int had) || kv.Value > had) use[k] = kv.Value;
        }
        foreach (var kv in use) _use[kv.Key] = kv.Value;

        _last.Clear();
        _seq = 0;
        if (last != null)
            foreach (var kv in last)
            {
                if (kv.Key.Length == 0 || kv.Value < 0) continue;   // -1 = 老文件没记过
                _last[MigrateKey(kv.Key)] = kv.Value;
                if (kv.Value > _seq) _seq = kv.Value;
            }
        // 序号接着上次往下走。不接着的话重启后"最后一次用过"会算成比现在还新,
        // 衰减变成负数, 等于反过来给老数据加分。
        foreach (var k in _use.Keys)
            if (!_last.ContainsKey(k)) _last[k] = _seq;
        _useNew = 0;
        // 学习表读回来了, "自己的词"的保送索引也照着重建一遍 ——
        // 不重建的话上次拼好的名字这次开机又进不了候选池(见 _mine 的说明)。
        RebuildMine();
    }

    /// <summary>
    /// 把导入的那份习惯**并进**现有的(不是替换 —— 替换会把这台机器上学的全抹掉)。
    /// <para>同一条取次数高的那个, 不相加: 两台机器各打过几次, 加起来会凭空把
    /// 冷词抬成热词; 取高值才等于"你在哪边打得更多就用哪边的数"。</para>
    /// <para>新进来的条记成"刚用过"(last = 当前步数): 导入文件里的序号是那台机器
    /// 的步数, 跟这边的对不上, 照搬会算出负的衰减, 等于反过来给它加分。</para>
    /// </summary>
    /// <returns>实际变动了多少条(新增的 + 次数被抬高的)。</returns>
    public static int MergeUse(Dictionary<string, int> data)
    {
        if (data == null) return 0;
        int changed = 0;
        foreach (var kv in data)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
            string k = MigrateKey(kv.Key);          // 别处导来的可能还是老格式
            if (k.Length == 0) continue;
            if (!_use.TryGetValue(k, out int cur) || kv.Value > cur)
            {
                _use[k] = kv.Value;
                changed++;
            }
            if (!_last.ContainsKey(k)) _last[k] = _seq;
        }
        if (changed > 0)
        {
            RebuildMine();      // 导入的名字也得保送进候选池, 不然白导
            _useNew++;          // 让"攒够 20 条就存盘"把它带上去
        }
        return changed;
    }

    public static Dictionary<string, int> DumpUse() => new(_use);
    /// <summary>存盘用: 每条最后一次是全局第几次输入。</summary>
    public static Dictionary<string, int> DumpLast() => new(_last);

    public static void ClearUse()
    {
        _use.Clear(); _last.Clear(); _mine.Clear(); _seq = 0; _useNew = 0;
    }

    /// <summary>学到了多少条。给设置页显示用。</summary>
    public static int UseCount => _use.Count;

    /// <summary>攒够这么多条就该写一次盘了(每次按键都写文件太夸张)。</summary>
    public static bool UseDirty => _useNew >= USE_SAVE_EVERY;
    public static void UseSaved() => _useNew = 0;

    // ---------- 音节热度: 从拼音表统计 ----------

    /// <summary>
    /// 数一遍拼音表里每个音节出现了多少次, 换算成 20~100 的权重。
    ///
    /// 关键决定: 每条拼音只按**块数最少**的那一种切法来数。
    /// 试过"所有合法切法都算"和"按切分概率加权", 结果都被 `duo -> du+o` 这类
    /// 歧义带偏 —— `o` 会顶到第一名(几千条), 可它单独成词在中文里其实很少见。
    /// 只数最少块数那条, `duo` 就还是 `duo`, `o` 掉回它该在的位置。
    /// 再往上做 EM 迭代反而会发散(正的分数会让切分越切越碎), 所以就一轮, 不复迭。
    ///
    /// 结果抽查: shi 100 / yi 98 / bu 95 / de 91 / pao 65 > pou 42 / den 27。
    /// </summary>
    public static void BuildHot()
    {
        _hot.Clear();
        var cnt = new Dictionary<string, int>();
        foreach (var key in _words.Keys)
        {
            foreach (var syl in FewestBlocks(key))
                cnt[syl] = cnt.GetValueOrDefault(syl) + 1;
        }
        if (cnt.Count == 0) return;
        double max = cnt.Values.Max();
        double lg = Math.Log(1 + max);
        foreach (var kv in cnt)
            _hot[kv.Key] = 20 + 80 * (Math.Log(1 + kv.Value) / lg);
    }

    /// <summary>
    /// 把一条拼音串按"音节块数最少"切开。切不开(不该发生)返回空。
    /// 平手时取长的那块 —— 和 Segment 排序时"块数优先"的取向一致。
    /// </summary>
    static List<string> FewestBlocks(string key)
    {
        var byLen = _sylByLen;
        int n = key.Length;
        var prev = new int[n + 1];
        var best = new double[n + 1];
        Array.Fill(best, double.NegativeInfinity);
        best[0] = 0;

        for (int i = 1; i <= n; i++)
        {
            for (int L = 1; L <= 6 && i - L >= 0; L++)
            {
                if (best[i - L] < -1e17) continue;
                if (!byLen[L].Contains(key.Substring(i - L, L))) continue;
                double v = best[i - L] - 100 + L;      // 每多一块罚 100, 长的音节略占优
                if (v > best[i]) { best[i] = v; prev[i] = i - L; }
            }
        }
        var res = new List<string>();
        if (best[n] < -1e17) return res;
        for (int i = n; i > 0; i = prev[i])
            res.Add(key.Substring(prev[i], i - prev[i]));
        res.Reverse();
        return res;
    }

    /// <summary>
    /// 数字串 -> 所有"拼了一半的音节前缀"。比如 7 -> p q r s, 94 -> xi yi zi。
    /// 存在的意义: 按第一个键的时候(7)还凑不出任何完整音节, 拼音行如果只显示
    /// 完整切分就是一片空白, 用户看不到自己按出了什么。有了它, 每按一个数字
    /// 都能立刻看到"目前可能的字母", 一路拼下去不会盲打。
    /// 返回的串长度 == digits.Length(每个数字各贡献一个字母), 按字母序排。
    /// </summary>
    public static List<string> Prefixes(string digits)
    {
        var res = new List<string>();
        if (digits.Length == 0) return res;

        var seen = new HashSet<string>();
        foreach (var kv in _code2syl)
        {
            // ToT9 是一字母一数字, 所以"编码以 digits 开头"就是"拼音以这串字母开头"
            if (kv.Key.Length < digits.Length) continue;
            if (!kv.Key.StartsWith(digits, StringComparison.Ordinal)) continue;
            foreach (var syl in kv.Value)
            {
                if (syl.Length < digits.Length) continue;
                if (seen.Add(syl.Substring(0, digits.Length)))
                    res.Add(syl.Substring(0, digits.Length));
            }
        }
        res.Sort(StringComparer.Ordinal);      // p q r s / w x y z, 顺着键上的字母顺序
        return res;
    }

    /// <summary>
    /// 一段数字 -> 它解出来的那些完整音节。比如 "726" -> pan pao ran rao san sao …
    /// 解不出来给空表(比如 "111" 或者半截的 "72")。
    /// <para>存在的意义见 InputCore 里"第一个字"那一段: 长串打到后面, 冷门的
    /// 第一音节会被热门的挤出候选池, 得靠它把可能性一个个点回来。</para>
    /// </summary>
    public static List<string> SyllablesOf(string code) =>
        _code2syl.TryGetValue(code, out var v) ? new List<string>(v) : new List<string>();

    /// <summary>九键数字 -> 键上的字母(按常见度排)。1 / 0 没有字母, 返回空。</summary>
    public static char[] LettersOf(char digit) =>
        _letters.TryGetValue(digit, out var v) ? v : Array.Empty<char>();

    /// <summary>
    /// 数字串 -> 一条条"就按键上的字母硬拼"的串, 比如 44 -> gg gh gi hg hh hi ...
    /// 只在没有一种切分能成音节时才用它兜底 —— 比如连按四次 4, g/h/i 怎么组都不是音节,
    /// Prefixes 会一条都给不出来, 拼音行就空了。空行比"给一串没意义的字母"难受得多,
    /// 而且系统输入法自己还会再猜一次(gg 它能猜出"哥哥"), 未必真的没用。
    /// 按常见度顺序生成, 取够 max 条就停, 所以位数再多也不会炸。
    /// </summary>
    public static List<string> LetterCombos(string digits, int max = 12)
    {
        var res = new List<string>();
        if (digits.Length == 0) return res;

        var sets = new List<char[]>();
        foreach (char d in digits)
        {
            var l = LettersOf(d);
            if (l.Length == 0) return res;      // 含 1 / 0 这种没有字母的键, 认了
            sets.Add(l);
        }

        var sb = new System.Text.StringBuilder(digits.Length);
        void Dfs(int i)
        {
            if (res.Count >= max) return;
            if (i == sets.Count) { res.Add(sb.ToString()); return; }
            foreach (char c in sets[i]) { sb.Append(c); Dfs(i + 1); sb.Length--; }
        }
        Dfs(0);
        return res;
    }

    /// <summary>数字串 -> 若干种拼音切分, 按(块数少, 权重高)排序</summary>
    public static List<string[]> Segment(string digits, int topk = 8)
    {
        int n = digits.Length;
        var best = new List<Seg>[n + 1];
        best[0] = new List<Seg> { new(0, 0, Array.Empty<string>()) };

        for (int i = 1; i <= n; i++)
        {
            var cands = new List<Seg>();
            for (int L = 1; L <= 6 && i - L >= 0; L++)
            {
                var prev = best[i - L];
                if (prev == null) continue;
                var code = digits.Substring(i - L, L);
                if (!_code2syl.TryGetValue(code, out var syls)) continue;
                foreach (var syl in syls)
                    foreach (var p in prev)
                        cands.Add(new Seg(p.Score + Weight(syl), p.Blocks + 1, p.Path.Append(syl).ToArray()));
            }
            if (cands.Count == 0)
            {
                // 该位无法构成音节: 保留占位继续, 但重罚
                var prev = best[i - 1];
                if (prev != null)
                    foreach (var p in prev)
                        cands.Add(new Seg(p.Score - 50, p.Blocks + 1,
                            p.Path.Append(digits[i - 1].ToString()).ToArray()));
            }
            if (cands.Count > 0)
            {
                cands.Sort((a, b) => a.Blocks != b.Blocks
                    ? a.Blocks.CompareTo(b.Blocks)
                    : b.Score.CompareTo(a.Score));
                best[i] = cands.Count > 40 ? cands.GetRange(0, 40) : cands;
            }
        }

        if (best[n] == null) return new List<string[]>();
        var top = best[n].Take(topk).Select(s => s.Path).ToList();
        return top;
    }

    /// <summary>
    /// 外部词库: 每行一条成词的拼音串, 带词频就写 "shoudao&lt;TAB&gt;1234",
    /// 只有拼音键也认(那是 1.0.2 及更早的旧格式, 按 DEFAULT_FREQ 算)。
    /// 更早的 "pinyin=词1,词2" 也认 —— 等号后面那半截丢掉。
    /// </summary>
    public static int LoadExternalDict(string path)
    {
        int n = 0;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
            var key = line.Split('=')[0].Trim();
            if (key.Length == 0) continue;

            // 词频在 tab 后面, 没有就退回默认频 —— 还是成词, 只是排得靠后一点
            int freq = DEFAULT_FREQ;
            var parts = key.Split('\t', ' ');
            if (parts.Length >= 2)
            {
                key = parts[0];
                if (int.TryParse(parts[1], out int f) && f > 0) freq = f;
            }
            // 同一个拼音键出现多次(不该有, 但防一手)取最高频
            if (!_words.TryGetValue(key, out int old) || freq > old) { _words[key] = freq; n++; }
        }
        _maxFreq = Math.Max(1, _words.Values.Count > 0 ? _words.Values.Max() : 1);
        BuildHot();     // 拼音表变了, 热度表跟着重算
        return n;
    }

    /// <summary>一共多少条拼音(内置兜底 + 外部拼音表)。</summary>
    public static int PhraseCount => _words.Count;
    /// <summary>热度表里多少个音节拿到了分数(剩下的走兜底)。</summary>
    public static int HotCount => _hot.Count;

    readonly record struct Seg(double Score, int Blocks, string[] Path);
}
