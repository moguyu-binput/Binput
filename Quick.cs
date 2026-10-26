using System;
using System.Collections.Generic;

namespace Binput
{
    /// <summary>窗口F 里一条记录的种类: 快捷短语(纯文本) / 快捷键(组合键)。</summary>
    public enum QKind { Phrase, Hotkey }

    /// <summary>
    /// 窗口F 的一条记录。**自己不带序号** —— 序号就是它所在的槽位(见 QuickStore)。
    /// </summary>
    public class QuickItem
    {
        public QKind Kind;
        public string Text = "";

        /// <summary>界面上显示的那串: 短语直接显示, 快捷键显示成 alt + r 这样。</summary>
        public string Show => IsPhrase ? Text : Text.Replace("+", " + ");
        public bool IsPhrase => Kind == QKind.Phrase;
    }

    /// <summary>
    /// 窗口F 的数据。**一共 45 个固定槽位**, 一块 3×3 翻五页正好装完 ——
    /// 槽位下标就是它在第几页第几格:
    ///     槽位 0~8   = 第 1 页九格 = 数字键 1~9(长按数字层里那个数字, 发的就是这一格)
    ///     槽位 9~44  = 第 2~5 页, 不占数字
    ///
    /// 【为什么改成"槽位"】以前是"每条自己记一个 Num 字段 + 按 Num 从小到大排序",
    /// 于是绑了号的条目会被 Sort() 拽到列表最前面, 位置天天在动, 手柄靠方向记位置
    /// 的人根本记不住。现在**位置就是数字**: 写在哪一格, 长按数字几就发哪一条;
    /// 不排序, 删掉中间一条也不会把后面的往前挪。
    ///
    /// 每条的文本长度不设上限 —— 存的就是几行文本, 没有理由给自己加限制。
    /// </summary>
    public static class QuickStore
    {
        /// <summary>5 页 × 9 格。</summary>
        public const int MaxSlots = 45;
        /// <summary>前九个槽位就是数字键 1~9。</summary>
        public const int NumSlots = 9;

        static readonly QuickItem[] _slot = new QuickItem[MaxSlots];

        /// <summary>45 个槽位(空位 = null)。导出 / 存盘按这个顺序写, 位置才不会乱。</summary>
        public static IReadOnlyList<QuickItem> Slots => _slot;

        public static bool Loaded { get; private set; }

        /// <summary>一共几条(空位不算)。</summary>
        public static int Count
        {
            get { int n = 0; foreach (var s in _slot) if (s != null) n++; return n; }
        }

        public static QuickItem At(int slot) => slot >= 0 && slot < MaxSlots ? _slot[slot] : null;

        public static bool Empty(int slot) => At(slot) == null;

        /// <summary>这一格对应哪个数字键(1~9)。第 2 页往后一律 0 = 不占数字。</summary>
        public static int NumOf(int slot) => slot >= 0 && slot < NumSlots ? slot + 1 : 0;

        /// <summary>数字键 n 在第几个槽位(不在 1~9 里就返回 -1)。</summary>
        public static int SlotOfNum(int n) => n >= 1 && n <= NumSlots ? n - 1 : -1;

        /// <summary>数字键 n 上绑的那条(没设就 null)。</summary>
        public static QuickItem ByNum(int n)
        {
            int s = SlotOfNum(n);
            return s < 0 ? null : _slot[s];
        }

        /// <summary>从 from 起第一个空位(全满返回 -1)。</summary>
        public static int FirstEmpty(int from = 0)
        {
            for (int i = Math.Max(0, from); i < MaxSlots; i++) if (_slot[i] == null) return i;
            return -1;
        }

        /// <summary>
        /// 读盘。**老存档会自动搬家**: 老格式里那个数字是"绑定的数字(0 = 没绑)",
        /// 现在它当成"槽位号 + 1"来读 —— 绑过 1~9 的落到第 1 页同一个位置上,
        /// 没绑的(0)按原来的先后顺序塞进第 2 页往后, 和以前打开 F 看到的顺序一致。
        /// 装不下的(45 条满了)直接丢掉, 并把剩下的写回去 —— 用户原话"超出部分直接删掉"。
        ///
        /// 搬过家(或者丢过条)**立刻写回**: 不写的话文件里还是老格式, 下一回读又要
        /// 再搬一次 —— 虽然搬出来的结果一样, 但那份存档永远是"上一代"的,
        /// 导出去给别人也还是老样子。
        /// </summary>
        public static void Load()
        {
            Array.Clear(_slot, 0, MaxSlots);
            int dropped = 0, moved = 0;
            foreach (var (num, kind, text) in Config.LoadQuick())
            {
                int slot;
                if (num >= 1 && num <= MaxSlots && _slot[num - 1] == null)
                    slot = num - 1;                      // 新格式: 数字就是槽位 + 1
                else
                    slot = FirstEmpty(NumSlots);         // 老格式的 0(没绑号), 或者撞车了
                if (slot < 0) { dropped++; continue; }
                if (slot != num - 1) moved++;            // 没落回原来那个数字上
                _slot[slot] = new QuickItem { Kind = kind, Text = text };
            }
            Loaded = true;
            if (dropped > 0)
                App.Log($"窗口F: 45 条装满了, 多出来的 {dropped} 条已经删掉");
            if (moved > 0)
                App.Log($"窗口F: 老存档的 {moved} 条搬到新格子里了");
            if (dropped > 0 || moved > 0) Save();
        }

        public static void Save() => Config.SaveQuick(_slot);

        /// <summary>往这一格写一条(空格子 = 新增, 有内容 = 覆盖)。</summary>
        public static void Set(int slot, QKind kind, string text)
        {
            if (slot < 0 || slot >= MaxSlots) return;
            _slot[slot] = new QuickItem { Kind = kind, Text = text ?? "" };
            Save();
        }

        /// <summary>清空这一格。位置**留着** —— 删掉一条不该把后面的往前挪。</summary>
        public static void Remove(int slot)
        {
            if (slot < 0 || slot >= MaxSlots || _slot[slot] == null) return;
            _slot[slot] = null;
            Save();
        }

        /// <summary>
        /// 45 个槽位**全清空**(2.4.7「清除数据」用 —— 用户要求连快捷短语一起清,
        /// 以前「清除」只清输入习惯、快捷原样留着)。清完立刻写盘。
        /// </summary>
        public static void ClearAll()
        {
            for (int i = 0; i < MaxSlots; i++) _slot[i] = null;
            Save();
        }

        /// <summary>两格互换内容(改绑数字用: 目标格有东西就换过来)。</summary>
        public static void Swap(int a, int b)
        {
            if (a < 0 || a >= MaxSlots || b < 0 || b >= MaxSlots || a == b) return;
            (_slot[a], _slot[b]) = (_slot[b], _slot[a]);
            Save();
        }

        /// <summary>
        /// 把这一格的内容**绑/换绑到数字 n**(1~9)。目标格有东西就互换。
        /// 返回这条现在所在的槽位(= n - 1), 失败返回 -1。
        /// </summary>
        public static int BindNum(int slot, int n)
        {
            int to = SlotOfNum(n);
            if (to < 0 || slot < 0 || slot >= MaxSlots) return -1;
            Swap(slot, to);
            return to;
        }

        /// <summary>
        /// 解除绑定(L3): 这一格的内容搬到**第 2 页往后第一个空位**, 数字位就空出来了。
        /// 返回新的槽位; 第 2 页往后全满(没地方搬)就返回 -1, 什么都不动。
        /// </summary>
        public static int Unbind(int slot)
        {
            if (slot < 0 || slot >= MaxSlots) return -1;
            if (slot >= NumSlots) return slot;              // 本来就不占数字
            if (_slot[slot] == null) return slot;
            int to = FirstEmpty(NumSlots);
            if (to < 0) return -1;
            Swap(slot, to);
            return to;
        }

        /// <summary>
        /// 把导入的那批**并进来**(不覆盖这台机器上现有的)。
        /// 【2.4.7】**原槽位空着就归位** —— 导入文件里记着每条原来在哪个槽位
        /// (见 Config.ExportAll / ImportAll: Num = 槽位 + 1), 绑在数字几的回到
        /// 数字几。以前这儿把槽位号扔了、一律塞第 2 页往后, 用户报: "导入后绑定
        /// 数字的全丢了, 第 1 页空一片还要重绑; 45 个满了还少一页"(绑数字的被
        /// 挤去第 2 页往后, 那 36 格满了就直接把剩下的丢掉, 哪怕第 1 页还空着)。
        /// 原槽位被这台机器上的条目占了(合并进非空数据时才会), 才往后找空位。
        /// <para>【2.4.7 补丁】**归位这条路不查重** —— 按内容去重会把用户绑在
        /// 几处的同一条短语当成重复项跳过后面的: 实测用户 1/5/10 号位各绑了一条
        /// "辛苦了", 导入后 5 号和 10 号双双消失(导出 10 条只进来 8 条)。
        /// 同一条出现在几个位置是用户自己的数据, 导入必须原样还原。</para>
        /// <para>查重只留给**兜底那条路**(没有原槽位 / 原槽位被别的内容占了):
        /// 内容和这台机器上已有的完全一样才跳过 —— 重复导同一个文件不会翻倍,
        /// 也不会把本机已有的短语再抄一份(quickprobe 场景 8 守着这条)。</para>
        /// </summary>
        public static int Merge(IEnumerable<(int Num, QKind Kind, string Text)> src)
        {
            int n = 0;
            if (src == null) return 0;
            // 按槽位号从小到大过一遍: 绑数字的(第 1 页)先归位, 后面的再说
            var list = src.Where(x => !string.IsNullOrWhiteSpace(x.Text))
                          .OrderBy(x => x.Num).ToList();
            foreach (var (num, kind, text) in list)
            {
                var body = text.Replace("\n", " ").Replace("\r", "").Trim();
                if (body.Length == 0) continue;
                int want = num >= 1 && num <= MaxSlots ? num - 1 : -1;   // 导出时记着的原槽位
                int slot;
                if (want >= 0 && _slot[want] == null)
                {
                    slot = want;                       // 原槽位空着 -> 归位(数字绑定也保住,
                                                       // 内容和别处重复也照归 —— 那是用户绑的)
                }
                else
                {
                    // 没有原槽位(num=0 的老条目), 或者原槽位被这台机器别的内容占了:
                    // 往后找空位, 并且**不抢 1~9**(FirstEmpty(NumSlots)), 老规矩不变。
                    // 只有这条路查重: 一模一样的(种类 + 内容都相同)才跳过。
                    bool dup = false;
                    foreach (var s in _slot)
                        if (s != null && s.Kind == kind && s.Text == body) { dup = true; break; }
                    if (dup) continue;
                    slot = FirstEmpty(NumSlots);
                }
                if (slot < 0) break;                   // 45 个全满了
                _slot[slot] = new QuickItem { Kind = kind, Text = body };
                n++;
            }
            if (n > 0) Save();
            return n;
        }
    }
}
