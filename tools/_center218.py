# -*- coding: utf-8 -*-
"""2.1.8(2): 符号面板**正中间那一格**改成"标签格"。

用户原话: "标签页中心的按钮也要有个格子，并且自动回中，也有高亮动画，怎么忘了这个；
把常用 中文英文这文字提示，也写在中心的格子上吧，同时删除页面下面的显示吧"。

以前那一格是**留空**的(摇杆松手就落在那儿, 放符号会顺手打出一个来)。现在:
  · 它是一颗真格子 —— 会画出来、有底色、有高亮动画(和别的格同一套)
  · 上面写**当前分类名**(常用 / 中文 / 英文 / 数学 / 箭头 / 数字 / 单位 / 图形)
  · 光标**默认就停在它上面**: 进面板、换类、松手回中, 三处都落在它上(和九键那颗 5 同理)
  · 按 A 什么也不打(它装的不是符号, Commit 里 PunctIdxOfGrid 返回 -1)
  · 卡片**下面**那块"分类名"牌子删掉 —— 字已经在正中间了, 不用再往下瞟一行

【换行】InputCore.cs / OverlayWindow.xaml.cs 是 CRLF, **OverlayWindow.xaml 是 LF** ——
两种都要断言, 别串。
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
R = r"C:\Users\<你>\Binput"
CRLF = "\r\n"


def crlf(ls):
    return CRLF.join(ls)


def lf(ls):
    return "\n".join(ls)


JOBS = [
    # ---------------- InputCore.cs (CRLF) ----------------
    ("InputCore.cs", True, [
        # 1) GridCenter 的说明重写
        (crlf([
            '    /// <summary>',
            '    /// 符号网格**正中间那一格**留空 —— 和九宫格标点层正中间留空一个道理:',
            '    /// 摇杆松手 / 轻轻一推就落在正中, 它要是放了东西, 每次收手都会顺手打出一个',
            '    /// 符号出来。于是 5×5 的 25 格里只有 24 个装符号(见 PunctCats)。',
            "    /// </summary>"]),
         crlf([
            '    /// <summary>',
            '    /// 符号网格**正中间那一格**: 它是一颗真格子(会画出来、有底色、光标能停、',
            '    /// 选中时也跟着放大跳一下), 但装的**不是符号**, 而是当前分类的名字',
            '    /// (常用 / 中文 / 英文 / 数学 / 箭头 / 数字 / 单位 / 图形)。',
            '    /// <para>为什么不空着了: 空着是怕"松手就落在那儿, 放个符号等于每次收手',
            '    /// 顺手打出一个来"。装个**标签**就没这毛病 —— 按 A 什么也不会发生',
            '    /// (见 <see cref=\"PunctIdxOfGrid\"/>)。而"现在在第几类"这件事本来就该',
            '    /// 写在眼睛正看着的地方: 以前是单独挂一块牌子在卡片**下面**, 现在就在',
            '    /// 正中间, 挪都不用挪眼。</para>',
            '    /// <para>光标**默认就停在它上面** —— 进面板、换了类、松手回中, 三处都',
            '    /// 落在它上, 和九键那颗"5"是同一个规矩(见 HandleStick 里的松手分支)。</para>',
            "    /// </summary>"])),

        # 2) PunctIdxOfGrid 的说明
        (crlf(['    /// <summary>网格第 i 格装的是第几个符号。正中间那格返回 -1(它是空的)。</summary>']),
         crlf(['    /// <summary>网格第 i 格装的是第几个符号。**正中间那格返回 -1** —— 它装的是',
               "    /// 分类名(见 <see cref=\"GridCenter\"/>), 不是符号, 所以按 A 什么也不打。</summary>"])),

        # 3) GridItems: 正中间那格写分类名
        (crlf([
            '            // 一页 25 格, 正中间那格空着(见 GridCenter)——',
            '            // 于是 24 个符号落在其余 24 格上。',
            '            var src = PunctsNow();',
            '            var res = new List<string>();',
            '            for (int i = 0; i < GridPageSize; i++)',
            '            {',
            '                int k = PunctIdxOfGrid(i);',
            '                res.Add(k >= 0 && k < src.Length ? src[k] : \"\");',
            '            }',
            "            return res;"]),
         crlf([
            '            // 一页 25 格 = 24 个符号 + 正中间那格写**当前分类名**(见 GridCenter)。',
            '            var src = PunctsNow();',
            '            var res = new List<string>();',
            '            for (int i = 0; i < GridPageSize; i++)',
            '            {',
            '                int k = PunctIdxOfGrid(i);',
            '                // k < 0 就是正中间那一格 —— 它是"标签格", 不装符号。',
            '                res.Add(k < 0 ? PunctCatName : k < src.Length ? src[k] : \"\");',
            '            }',
            "            return res;"])),

        # 4) 打开符号面板: 光标落在中心
        (crlf([
            '        Punct = true;',
            '        GridPinyin = false;',
            '        GridPage = 0;',
            '        Focus = FocusZone.CandGrid;',
            '        CandIdx = 0;',
            '        GridIdx = 0;',
            "        Dirty = true;"]),
         crlf([
            '        Punct = true;',
            '        GridPinyin = false;',
            '        GridPage = 0;',
            '        Focus = FocusZone.CandGrid;',
            '        CandIdx = 0;',
            '        GridIdx = GridCenter;      // 落在正中间那个"标签格"上(见 GridCenter)',
            "        Dirty = true;"])),

        # 5) 换类: 光标也回中心
        (crlf([
            '        PunctCat = ((PunctCat + delta) % n + n) % n;',
            '        GridIdx = 0;',
            "        CandIdx = 0;"]),
         crlf([
            '        PunctCat = ((PunctCat + delta) % n + n) % n;',
            '        GridIdx = GridCenter;      // 换完类光标回到正中间(那里正写着新的分类名)',
            "        CandIdx = 0;"])),

        # 6) 认"松手那一帧"
        (crlf([
            '        double nx = rawX / 32768.0, ny = rawY / 32768.0;',
            '        double r = Math.Sqrt(nx * nx + ny * ny);',
            "        bool active = r >= DZ_N;"]),
         crlf([
            '        double nx = rawX / 32768.0, ny = rawY / 32768.0;',
            '        double r = Math.Sqrt(nx * nx + ny * ny);',
            '        bool active = r >= DZ_N;',
            '        // **松手那一帧**。摇杆是**每一帧**都走到这儿的(不是只在动的时候),',
            '        // 所以"回中"这种事必须认**边沿**: 认电平的话, 用十字键把光标挪到别的',
            '        // 格上, 下一帧就被拽回正中了 —— 十字键等于白按。',
            '        bool released = _stickWasActive && !active;',
            "        _stickWasActive = active;"])),

        # 7) 符号网格那条分支: 中心格可以停 + 松手回中
        (crlf([
            '            // 符号网格: 摇杆**直接落点** —— 推到哪一格就是哪一格(见 PointToGrid)。',
            '            // 以前在这儿摇杆是"当方向键连发", 一格一格挪过去; 二十几个符号',
            '            // 那么挪太慢, 而且它跟九键上"推哪儿选哪儿"是两套手感。',
            '            // 【落在正中间那格就当没推】那一格是空的(见 GridCenter), 站上去',
            '            // 什么也不会发生, 不如干脆别过去。',
            '            if (active)',
            '            {',
            '                int k = PointToGrid(nx, ny, GridColsNow, GridRowsNow);',
            '                if (k >= 0 && k < GridCount() && PunctIdxOfGrid(k) >= 0 && k != GridIdx)',
            '                {',
            '                    GridIdx = k;',
            '                    Dirty = true;',
            '                }',
            "            }"]),
         crlf([
            '            // 符号网格: 摇杆**直接落点** —— 推到哪一格就是哪一格(见 PointToGrid)。',
            '            // 以前在这儿摇杆是"当方向键连发", 一格一格挪过去; 二十几个符号',
            '            // 那么挪太慢, 而且它跟九键上"推哪儿选哪儿"是两套手感。',
            '            // 【正中间那格是"标签格"】它装的是分类名, 按 A 不会打出东西; 光标',
            '            // 停在它上面是**常态** —— 进面板 / 换了类 / 松手, 三处都落在它上。',
            '            if (active)',
            '            {',
            '                int k = PointToGrid(nx, ny, GridColsNow, GridRowsNow);',
            '                if (k >= 0 && k < GridCount() && k != GridIdx)',
            '                {',
            '                    GridIdx = k;',
            '                    Dirty = true;',
            '                }',
            '            }',
            '            else if (released && GridIdx != GridCenter)',
            '            {',
            '                // 松手**回中**: 落回正中间那个标签格。和九键"松手回 5"同一个',
            '                // 规矩 —— 手一松界面就回到一个确定的地方, 不用记刚才停在哪儿。',
            '                GridIdx = GridCenter;',
            '                Dirty = true;',
            "            }"])),

        # 8) Commit 那句注释
        (crlf(['        // 网格里正中间那格是空的(见 PunctIdxOfGrid), 落在那儿按 A 就是没按']),
         crlf(['        // 网格正中间那格是"标签格"(装分类名, 见 GridCenter), 落在那儿按 A 就是没按'])),

        # 9) _stickWasActive 字段
        (crlf(['    // ================= 左摇杆 =================']),
         crlf(['    /// <summary>上一帧摇杆是不是"推着"的。用来认**松手那一帧**(见 HandleStick)。</summary>',
               '    bool _stickWasActive;',
               '',
               "    // ================= 左摇杆 ================="])),
    ]),

    # ---------------- OverlayWindow.xaml.cs (CRLF) ----------------
    ("OverlayWindow.xaml.cs", True, [
        # 1) 卡片下面那块"分类名"牌子: 不再显示
        (crlf([
            '        // 显隐挂在**外面那块板上**(SlideHintChip / GridCatChip) —— 板要跟字一起出一起收, 只把里面的字藏了的话就剩一块空板在那儿。',
            '        GridCatChip.Visibility = punctGrid ? Visibility.Visible : Visibility.Collapsed;',
            "        if (punctGrid) GridCatName.Text = c.PunctCatName;"]),
         crlf([
            '        // 显隐挂在**外面那块板上**(SlideHintChip) —— 板要跟字一起出一起收, 只把里面的字藏了的话就剩一块空板在那儿。',
            '        // 【2.1.8: 分类名这块牌子整个撤了】"现在在第几类"改由**符号网格正中间',
            '        // 那一格**说(见 InputCore.GridItems)—— 眼睛本来就在网格上, 不用再往下',
            "        // 瞟一行。牌子本身在 XAML 里也一并删了。"])),

        # 2) 网格渲染: 正中间那格按"标签格"画
        (crlf([
            '                // 【正中间那格是空的】符号网格特意留它出来(见 InputCore.GridCenter),',
            '                // 于是 items[i] 会是空串 —— 那种格子照"没内容"处理, 但位置照旧占着。',
            '                bool has = i < items.Count && items[i].Length > 0;',
            '                // Hidden 而不是 Collapsed: 空的也要占格子, 否则格子会被拉大变形',
            '                _grid[i].Visibility = has ? Visibility.Visible : Visibility.Hidden;',
            '                if (!has) continue;',
            "                _gridText[i].Text = items[i];"]),
         crlf([
            '                // 空的格子照"没内容"处理, 但位置照旧占着(否则格子会被拉大变形)。',
            '                bool has = i < items.Count && items[i].Length > 0;',
            '                // Hidden 而不是 Collapsed: 空的也要占格子, 否则格子会被拉大变形',
            '                _grid[i].Visibility = has ? Visibility.Visible : Visibility.Hidden;',
            '                if (!has) continue;',
            '                _gridText[i].Text = items[i];',
            '                // 【正中间那格是"标签格"】它装的是分类名(见 InputCore.GridCenter),',
            '                // 不是可以按的符号: 字号小一档、底色走"半选中"那档(见 Paint 的 lit),',
            '                // 长得还是一颗键, 但一眼能看出它跟旁边那 24 个不是一类东西。',
            '                // 高亮 / 放大那套动效它照样有 —— 光标停在它上面是常态。',
            "                bool label = punctGrid && i == c.GridCenter;"])),

        (crlf([
            '                if (c.GridPinyin && items[i].Length > 17) f = items[i].Length > 26 ? 11 : 12.5;',
            '                if (_gridText[i].FontSize != f) _gridText[i].FontSize = f;',
            '                bool on = c.GridIdx == i;',
            "                Paint(_grid[i], on, _gridText[i], null);"]),
         crlf([
            '                if (c.GridPinyin && items[i].Length > 17) f = items[i].Length > 26 ? 11 : 12.5;',
            '                if (label) f = 14;',
            '                if (_gridText[i].FontSize != f) _gridText[i].FontSize = f;',
            '                bool on = c.GridIdx == i;',
            "                Paint(_grid[i], on, _gridText[i], null, label);"])),
        # 3) 启动时给"分类名"那几个字配笔刷那一段也要跟着删
        (crlf([
            '        // 符号面板那几处新字(分类名 / 左右翻页箭头)用界面自己的笔刷 ——',
            '        // 换主题改的是笔刷实例里的颜色, 这里拿住引用就能跟着换, 不用每帧重刷。',
            '        GridCatName.Foreground = Fg;',
            '        // (2.1.1: 分类名右边那个数字页号 GridCatIdx 撤了, 见 XAML 里那处注释。)',
            '        // "滑行"二字跟符号面板的分类名**同一个笔刷**: 它在 XAML 里写的是浅色,',
            '        // 换到可爱粉(浅底深字)那套就成了白字压粉底 —— 几乎看不见。',
            '        // 分类名走的是 Fg(换主题会跟着变深), 所以那套皮肤下"常用/更多"是黑的,',
            '        // 滑行也照它来。']),
         crlf([
            '        // 【2.1.8: 分类名那块牌子撤了】它原来在这儿领一份 Fg 笔刷。现在"第几类"',
            '        // 写在符号网格正中间那一格上, 那个字走的是网格自己的 Paint(见 Render),',
            '        // 换主题跟着笔刷走, 这儿不用再管。',
            '        // "滑行"二字: 它在 XAML 里写的是浅色, 换到可爱粉(浅底深字)那套就成了',
            '        // 白字压粉底 —— 几乎看不见, 所以也换成 Fg(换主题会跟着变深)。'])),
    ]),

    # ---------------- OverlayWindow.xaml (**LF**) ----------------
    ("OverlayWindow.xaml", False, [
        (lf([
            '            <Border x:Name="GridCatChip" Visibility="Collapsed"',
            '                    IsHitTestVisible="False" Background="#C20E0C16"',
            '                    CornerRadius="8" Padding="12,2,12,3"',
            '                    HorizontalAlignment="Center" VerticalAlignment="Center">',
            '                <!-- 【2.1.1: 分类名右边那个数字页号("1/8")撤了】',
            '                     在第几页、一共几页, 由**底带上那排点**说(见 FrameFoot 里的',
            '                     PunctDots)—— 和设置页、窗口F 同一个规矩, 别的地方都不写数字页号。 -->',
            '                <TextBlock x:Name="GridCatName" Text="常用"',
            '                           FontSize="17" FontWeight="SemiBold"',
            '                           Foreground="#E8E8F0" VerticalAlignment="Center"',
            '                           HorizontalAlignment="Center"/>',
            '            </Border>']),
         lf([
            '            <!-- 【2.1.8: 分类名这块牌子撤了】"现在在第几类"改由**符号网格正中间',
            '                 那一格**说(见 InputCore.GridItems)—— 眼睛本来就在网格上。 -->'])),

        # StatusDock 上面那段说明里也别再提"分类名"了
        (lf(['            <!-- **滑行 / 分类名这两个字, 底下垫一块深色半透明圆角板**。']),
         lf(['            <!-- **「滑行」「已复制」这两个字, 底下垫一块深色半透明圆角板**。'])),
    ]),
]


def main():
    for name, is_crlf, edits in JOBS:
        p = os.path.join(R, name)
        s = io.open(p, encoding="utf-8", newline="").read()
        if is_crlf:
            assert "\r\n" in s and s.count("\r\r\n") == 0 \
                and s.count("\n") == s.count("\r\n"), "%s 不是干净的 CRLF" % name
        else:
            assert "\r" not in s, "%s 不是 LF" % name
        for i, (old, new) in enumerate(edits, 1):
            if old not in s:
                raise SystemExit("!! %s 第 %d 处匹配不上:\n%s" % (name, i, old[:220]))
            if s.count(old) != 1:
                raise SystemExit("!! %s 第 %d 处不唯一(%d)" % (name, i, s.count(old)))
            s = s.replace(old, new, 1)
        io.open(p, "w", encoding="utf-8", newline="").write(s)
        t = io.open(p, encoding="utf-8", newline="").read()
        assert t.count("\r\r\n") == 0
        print("%-24s 改了 %d 处" % (name, len(edits)))

    # 复查: 分类名牌子相关的引用一个都不许剩
    for name in ("OverlayWindow.xaml", "OverlayWindow.xaml.cs"):
        s = io.open(os.path.join(R, name), encoding="utf-8", newline="").read()
        for dead in ("GridCatChip", "GridCatName"):
            if dead in s:
                raise SystemExit("!! %s 里还留着 %s" % (name, dead))
    print("复查: 分类名牌子的引用已清干净")


if __name__ == "__main__":
    main()
