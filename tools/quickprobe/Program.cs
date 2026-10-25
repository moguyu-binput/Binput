using Binput;

// quickprobe —— 守 2.1.1 窗口F 那份**数据**的几条规矩(界面那一摞层归 fprobe)。
//
// 2.1.1 起窗口F 是 45 个固定槽位(5 页 × 9 格), 规矩跟以前完全不同:
//   1) **位置就是数字**: 槽位 0~8 就是数字键 1~9, 写在哪一格长按数字几就发哪一条。
//      不排序, 删掉一条也不把后面的往前挪 —— 手柄是靠方向记位置的。
//   2) 改绑 = 两格互换(对面有东西就换过来, 不覆盖)。
//   3) 解除绑定 = 搬到第 2 页往后第一个空位; 那边满了就不动。
//   4) 一共就 45 格: 装不下的(含老存档本来就超的)直接丢掉并写回。
//
// 和 useprobe 一样: 它要动存档, 所以整份挪到一个临时目录里跑 ——
// 真机上那份从头到尾不碰(比"先备份再还原"稳: 中途崩了也不会留下半截)。

int bad = 0;
void Ok(string what, bool ok, string detail = "")
{
    if (!ok) bad++;
    Console.WriteLine($"    {(ok ? "OK  " : "FAIL")} {what}"
        + (detail.Length > 0 ? "  — " + detail : ""));
}

string tmp = Path.Combine(Path.GetTempPath(), "quickprobe_" + Guid.NewGuid().ToString("N")[..8]);
Config.SetDirForTest(tmp);
string real = Config.SavePath;   // 2.1.5: Binput.save
Console.WriteLine($"本次用的假数据目录: {tmp}");
Console.WriteLine();

QuickItem A(int slot) => QuickStore.At(slot);
string T(int slot) => A(slot)?.Text ?? "<空>";
void Reset()
{
    QuickStore.Load();
    for (int i = 0; i < QuickStore.MaxSlots; i++) QuickStore.Remove(i);
}

// 2.1.5 起快捷不再单独一个文件, 直接往存档里写明文没有意义(读的时候会当作"不是我们家的")。
// 要造"某一格本来有东西"的现场, 走 QuickStore.Set 就是了 —— 它落的就是真存档。

// ---------- 场景1: 位置就是数字 ----------
Console.WriteLine("=== 场景1: 槽位 0~8 就是数字键 1~9 ===");
Reset();
Ok("一共 45 格", QuickStore.MaxSlots == 45 && QuickStore.NumSlots == 9);
for (int i = 0; i < 9; i++) QuickStore.Set(i, QKind.Phrase, "第" + (i + 1) + "格");
Ok("槽位 0 对应数字 1", QuickStore.NumOf(0) == 1);
Ok("槽位 8 对应数字 9", QuickStore.NumOf(8) == 9);
Ok("第 2 页往后不占数字", QuickStore.NumOf(9) == 0 && QuickStore.NumOf(44) == 0);
Ok("数字 4 上绑的是第 4 格那条", QuickStore.ByNum(4)?.Text == "第4格");
Ok("数字 n 的槽位就是 n-1", QuickStore.SlotOfNum(4) == 3 && QuickStore.SlotOfNum(0) == -1);

// ---------- 场景2: 不排序, 删掉也不往前挪 ----------
Console.WriteLine();
Console.WriteLine("=== 场景2: 删掉中间一条, 后面的不许往前挪 ===");
Reset();
for (int i = 0; i < 5; i++) QuickStore.Set(i, QKind.Phrase, "甲丙戊庚壬"[i].ToString());
int before = QuickStore.Count;
QuickStore.Remove(1);
Ok("条数 -1", QuickStore.Count == before - 1, $"{before} -> {QuickStore.Count}");
Ok("被删的那格空了", QuickStore.Empty(1));
Ok("前面那条还在原位", T(0) == "甲");
Ok("后面那几条**没**往前挪", T(2) == "戊" && T(3) == "庚" && T(4) == "壬",
   $"2/3/4 = {T(2)}/{T(3)}/{T(4)}");

// ---------- 场景3: 改绑 = 两格互换 ----------
Console.WriteLine();
Console.WriteLine("=== 场景3: 绑数字(对面有东西就互换) ===");
Reset();
QuickStore.Set(10, QKind.Phrase, "甲");       // 第 2 页第 2 格
Ok("甲 起初不占数字", QuickStore.ByNum(3) == null);
int to = QuickStore.BindNum(10, 3);
Ok("绑到数字 3: 返回槽位 2", to == 2);
Ok("甲 搬到第 3 格", T(2) == "甲");
Ok("原来那格空出来了", QuickStore.Empty(10));

QuickStore.Set(10, QKind.Phrase, "乙");
QuickStore.BindNum(10, 3);                    // 3 号位已被甲占 -> 互换
Ok("被占就互换: 乙 到了第 3 格", T(2) == "乙");
Ok("被占就互换: 甲 被换到第 2 页", T(10) == "甲", $"槽位 10 = {T(10)}");
Ok("两条都还在, 一条都没被覆盖", QuickStore.Count == 2);
Ok("绑到自己那一格 = 什么都没动", QuickStore.BindNum(2, 3) == 2 && T(2) == "乙");

// ---------- 场景4: 解除绑定 ----------
Console.WriteLine();
Console.WriteLine("=== 场景4: Y 解除绑定(2.3.3 起, 以前是 L3) ===");
Reset();
QuickStore.Set(2, QKind.Phrase, "甲");
int moved = QuickStore.Unbind(2);
Ok("搬到第 2 页往后第一个空位(槽位 9)", moved == 9, $"返回 {moved}");
Ok("数字位空出来了", QuickStore.Empty(2) && QuickStore.ByNum(3) == null);
Ok("那条还在, 只是挪了位置", T(9) == "甲");
Ok("本来就不占数字的, 解除 = 原地不动", QuickStore.Unbind(9) == 9 && T(9) == "甲");
Ok("空格子解除也不炸", QuickStore.Unbind(20) == 20);

Reset();
for (int i = 9; i < 45; i++) QuickStore.Set(i, QKind.Phrase, "占" + i);   // 第 2~5 页全满
QuickStore.Set(2, QKind.Phrase, "甲");
Ok("第 2 页往后全满: 解除失败(返回 -1)", QuickStore.Unbind(2) == -1);
Ok("失败了就什么都不动", T(2) == "甲");

// ---------- 场景5: 45 格封顶, 塞不下就不塞 ----------
Console.WriteLine();
Console.WriteLine("=== 场景5: 45 格封顶 ===");
Reset();
for (int i = 0; i < 45; i++) QuickStore.Set(i, QKind.Phrase, "第" + (i + 1) + "条");
Ok("45 格全填上就是 45 条", QuickStore.Count == 45, $"实际 {QuickStore.Count}");
Ok("前 45 条一条不少", T(0) == "第1条" && T(44) == "第45条");
QuickStore.Set(99, QKind.Phrase, "越界");     // 没有第 100 格
Ok("越界的那一格写不进去", QuickStore.Count == 45 && !QuickStore.At(44).Text.Contains("越界"));
Ok("存盘再读回来还是 45 条", Config.LoadQuick().Count == 45,
   $"存档里还有 {Config.LoadQuick().Count} 条");

// ---------- 场景7: 存盘是密文, 往返一致 ----------
Console.WriteLine();
Console.WriteLine("=== 场景7: 落盘 ===");
Reset();
QuickStore.Set(0, QKind.Phrase, "上交任务");
QuickStore.Set(4, QKind.Hotkey, "alt+r");
QuickStore.Set(20, QKind.Phrase, new string('啊', 300));   // 超过 200 字那条会被截
QuickStore.Save();
var raw = File.ReadAllBytes(real);
Ok("文件头是自家的密文标记 BIN1", raw.Length >= 4
   && raw[0] == 'B' && raw[1] == 'I' && raw[2] == 'N' && raw[3] == '1');
string asText = System.Text.Encoding.UTF8.GetString(raw);
Ok("明文里搜不到那条短语", !asText.Contains("上交任务"));
Ok("明文里搜不到 'phrase'", !asText.Contains("phrase"));

QuickStore.Load();
Ok("往返: 条数一致", QuickStore.Count == 3, $"读回 {QuickStore.Count} 条");
Ok("往返: 位置一致", T(0) == "上交任务" && T(4) == "alt+r");
Ok("一条短语 200 字封顶(写了 300 只留 200)", T(20).Length == 200, $"实际 {T(20).Length} 字");
Ok("往返: 中间那些空格还是空的", QuickStore.Empty(1) && QuickStore.Empty(19));
Ok("往返: 种类一致", A(0).IsPhrase && !A(4).IsPhrase);
Ok("快捷键显示成 alt + r", A(4).Show == "alt + r", $"显示成 \"{A(4).Show}\"");

// ---------- 场景8: 导入只往第 2 页往后塞, 不抢已经设好的 1~9 ----------
Console.WriteLine();
Console.WriteLine("=== 场景8: 导入(不覆盖这台机器上现有的) ===");
Reset();
QuickStore.Set(0, QKind.Phrase, "本机1号");
int added = QuickStore.Merge(new[]
{
    (1, QKind.Phrase, "外来1号"),        // 第 1 页那个位置本机占了 -> 只能往后塞
    (0, QKind.Phrase, "外来短语"),
    (0, QKind.Phrase, "本机1号"),        // 一模一样 -> 跳过
    (0, QKind.Hotkey, "ctrl+s"),
});
Ok("加进来 3 条(重复那条跳过)", added == 3, $"实际 {added}");
Ok("本机 1 号那条没被抢掉", T(0) == "本机1号");
Ok("新来的全在第 2 页往后", T(9) == "外来1号" && T(10) == "外来短语" && T(11) == "ctrl+s",
   $"9/10/11 = {T(9)}/{T(10)}/{T(11)}");

// ---------- 收摊 ----------
Console.WriteLine();
try { Directory.Delete(tmp, true); } catch { }
Console.WriteLine("(临时目录已删除, 真实的快捷文件没动过)");
Console.WriteLine();
Console.WriteLine(bad == 0 ? "全部通过" : $"!! {bad} 条没过");
return bad == 0 ? 0 : 1;
