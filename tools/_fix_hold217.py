# -*- coding: utf-8 -*-
"""2.1.8(1): 修"唤出来背景莫名变透" —— 唤出时**不再动 Window.Opacity**。

【查出来的】用 PrintWindow 把窗口自己渲染的结果抓下来对比过:
第 1 次唤出和第 2 次唤出(中间真的收起过)抓到的图像**完全一样**
(正浓的面板像素都是 8862 个, 包围盒一模一样), 卡片底色也都是该有的浓度
(面板 0x14,0x14,0x20 套 0xE8 压在黑上 = (18,18,29), 实测正是这个数)。
也就是说 WPF 这一侧一点毛病都没有 —— 变的只能是**窗口合成那一层**。

【为什么是 Window.Opacity】窗口是 AllowsTransparency 的分层窗口, 屏幕上那一层
是 DWM 拿 WPF 提交的位图合成的。唤出流程里唯一会"整层换一张图"的动作就是
    ShowReady: Opacity = 0   ->   ReleaseHold: Opacity = 1
Opacity=0 那几帧 WPF 是**真往合成层提交过一张全透明的图**的; 等 Opacity 回到 1,
WPF 未必重新交一张新的, DWM 手里那张"全透明"的旧图就一直贴着 —— 表现正是
"唤出来背景变透, 退出程序再进才好"(新窗口没有历史透明帧)。

【怎么改】保险从**窗口**挪到**内容**上: FlipHost.Opacity。它是 WPF 自己画的一层,
不碰分层窗口的合成; 而窗口那几帧本来就在屏幕外(见 ShowReady), 看不见。
顺带把重合成催三次(归位当场 / 130ms / 400ms), 每催一次都连内容根一起重画。
"""
import io
import sys

sys.stdout.reconfigure(encoding="utf-8")
P = r"C:\Users\<你>\Binput\OverlayWindow.xaml.cs"

J = lambda ls: "\r\n".join(ls)

EDITS = [
    # 1) ShowReady: Opacity = 0 -> HoldFade(0)
    (J(['        Opacity = 0;                 // 双保险: 万一有那么一帧漏到屏幕上, 也是全透明']),
     J(['        // 保险挂在**内容**上, 不挂在窗口上 —— 见 HoldFade 那段说明。',
        '        HoldFade(0);'])),

    # 2) ReleaseHold: Opacity = 1 -> HoldFade(1)
    (J(['        _holdOff = false;',
        '        Opacity = 1;']),
     J(['        _holdOff = false;',
        '        HoldFade(1);'])),

    # 3) IsVisibleChanged(被收起): Opacity = 1 -> HoldFade(1)
    (J(['            _holdGen++;',
        '            _holdOff = false;',
        '            Opacity = 1;']),
     J(['            _holdGen++;',
        '            _holdOff = false;',
        '            HoldFade(1);            // 同上: 还内容, 不动窗口透明度'])),

    # 4) 新增 HoldFade(放在 ReleaseHold 前面)
    (J(['    /// <summary>窗口真挪回屏幕上那一刻跑一次(见 <see cref="ShowReady"/>), 用完即清。</summary>',
        '    public Action OnShown;']),
     J(['    /// <summary>',
        '    /// 唤出头一两帧把内容整个藏起来 / 再放出来(见 <see cref="ShowReady"/>)。',
        '    /// <para>**刻意不动 <c>Window.Opacity</c>** —— 窗口是 per-pixel-alpha 的分层窗口,',
        '    /// 屏幕上那一层是 DWM 拿 WPF 提交的位图合成的; 而 <c>Opacity = 0</c> 那几帧 WPF 是',
        '    /// 真交过一张**全透明**的图的, 等回到 1 时 WPF 未必再交一张新的, DWM 就一直贴着',
        '    /// 那张旧图 —— 表现正是"唤出来背景莫名变透, 退出程序再进才好"(新窗口没有历史',
        '    /// 透明帧)。实测过: 第 1 次和第 2 次唤出抓到的 WPF 渲染结果**完全一样**, 所以',
        '    /// 毛病只在合成这一层, 不在 WPF 画出来的内容上。</para>',
        '    /// <para>保险改挂在内容根 <c>FlipHost</c> 上: 那是 WPF 自己画的一层, 怎么改都是',
        '    /// 一次普通的重画, 不会去动分层窗口的合成。而且那几帧窗口本来就在屏幕外,',
        '    /// 内容藏不藏其实都看不见 —— 这一下纯粹是"万一漏一帧到屏幕上"的兜底。</para>',
        '    /// </summary>',
        '    void HoldFade(double v)',
        '    {',
        '        if (FlipHost != null) FlipHost.Opacity = v;',
        '    }',
        '',
        '    /// <summary>窗口真挪回屏幕上那一刻跑一次(见 <see cref="ShowReady"/>), 用完即清。</summary>',
        '    public Action OnShown;'])),

    # 5) KickCompositionLater: 催三次 + 连内容根一起拽
    (J(['    void KickCompositionLater()',
        '    {',
        '        var t = new System.Windows.Threading.DispatcherTimer',
        '        { Interval = TimeSpan.FromMilliseconds(400) };',
        '        t.Tick += (_, _) =>',
        '        {',
        '            t.Stop();',
        '            if (!IsVisible) return;',
        '            if (Opacity < 1) { Opacity = 1; App.Log("唤出兜底: 透明度拉回 1"); }',
        '            NudgeComposition();',
        '        };',
        '        t.Start();',
        '    }']),
     J(['    void KickCompositionLater()',
        '    {',
        '        // 催**三次**: 归位当场已经催过一次(NudgeComposition), 这里 130ms 和 400ms',
        '        // 各补一脚。合成层什么时候跟上来不归我们管, 多给两次机会最省事 ——',
        '        // 而且这两脚都会顺手把落在 1 以下的透明度拽回来(见 KickCompositionOnce)。',
        '        foreach (int ms in new[] { 130, 400 })',
        '        {',
        '            var t = new System.Windows.Threading.DispatcherTimer',
        '            { Interval = TimeSpan.FromMilliseconds(ms) };',
        '            t.Tick += (_, _) => { t.Stop(); KickCompositionOnce(); };',
        '            t.Start();',
        '        }',
        '    }',
        '',
        '    /// <summary>催促一次重合成, 顺手把落下去的透明度拽回 1(见 KickCompositionLater)。</summary>',
        '    void KickCompositionOnce()',
        '    {',
        '        if (!IsVisible) return;',
        '        if (Opacity < 1) { Opacity = 1; App.Log("唤出兜底: 窗口透明度拉回 1"); }',
        '        if (FlipHost != null && FlipHost.Opacity < 1)',
        '        {',
        '            FlipHost.Opacity = 1;',
        '            App.Log("唤出兜底: 内容透明度拉回 " + FlipHost.Opacity);',
        '        }',
        '        NudgeComposition();',
        '    }'])),

    # 6) NudgeComposition: 连内容根一起重画
    (J(['        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,',
        '            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);',
        '        InvalidateVisual();     // WPF 这一侧也重画一遍, 两边一起更新']),
     J(['        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,',
        '            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);',
        '        // WPF 这一侧也重画一遍, 两边一起更新。**内容根也要点名重画**:',
        '        // 只 InvalidateVisual 窗口自己时, 底下那一摞有可能被判定成"没变"而跳过,',
        '        // 于是催了半天催出来还是同一张图。',
        '        InvalidateVisual();',
        '        if (FlipHost != null) FlipHost.InvalidateVisual();'])),
]


def main():
    s = io.open(P, encoding="utf-8", newline="").read()
    assert "\r\n" in s and s.count("\r\r\n") == 0 and s.count("\n") == s.count("\r\n")
    for i, (old, new) in enumerate(EDITS, 1):
        if old not in s:
            raise SystemExit("!! 第 %d 处匹配不上:\n%s" % (i, old[:200]))
        if s.count(old) != 1:
            raise SystemExit("!! 第 %d 处不唯一(%d 个)" % (i, s.count(old)))
        s = s.replace(old, new, 1)
        print("  第 %d 处 OK" % i)
    io.open(P, "w", encoding="utf-8", newline="").write(s)
    t = io.open(P, encoding="utf-8", newline="").read()
    assert t.count("\r\r\n") == 0 and t.count("\n") == t.count("\r\n"), "!! 换行脏了"
    print("写完, CRLF=%d" % t.count("\r\n"))


if __name__ == "__main__":
    main()
