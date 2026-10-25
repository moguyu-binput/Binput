# -*- coding: utf-8 -*-
"""【临时】给唤出流程挂一个"透明审计": 400ms 兜底那一刻跑一次, 把两个数记进日志 ——

    1 卡片真渲染一遍读 alpha(RenderTargetBitmap, 得到的是 WPF 内容这一侧的真值)
    2 扫一遍视觉树, 谁的不透明度不在 1 就列出来

目的: 分清"背景变透"到底出在哪一层 ——
    · 内容 alpha 真的是 232(0xE8) 而屏幕上更透  -> 窗口级(Opacity / DWM 合成)
    · 内容 alpha 本身就低                        -> WPF 这一侧某处被改了
跑完看日志, 定位之后这个补丁要**整段摘掉**。
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
P = r"C:\Users\<你>\Binput\OverlayWindow.xaml.cs"

ANCHOR = "\r\n".join([
    '            if (Opacity < 1) { Opacity = 1; App.Log("唤出兜底: 透明度拉回 1"); }',
    '            NudgeComposition();',
    '        };',
    '        t.Start();',
    '    }',
    ''])
NEW_CALL = "\r\n".join([
    '            if (Opacity < 1) { Opacity = 1; App.Log("唤出兜底: 透明度拉回 1"); }',
    '            NudgeComposition();',
    '            AuditTransparency();',
    '        };',
    '        t.Start();',
    '    }',
    ''])

AUDIT = """
    /// <summary>【临时·定位"唤出后背景变透"用】报出卡片在屏幕上的位置, 再扫一遍视觉树。</summary>
    void AuditTransparency()
    {
        try
        {
            // 卡片在窗口里的位置(已经含 Scale), 加上窗口左上角 = 屏幕坐标。
            // 外面那个取样脚本就照这个点去读屏幕像素, 反推真实合成出来的 alpha。
            var box = CardRectInWindow();
            double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            App.Log($"透明审计: 卡片屏幕位置 {box.Left + Left:0},{box.Top + Top:0} {box.Width:0}x{box.Height:0}"
                    + $" dpi={dpi:0.##} Window.Opacity={Opacity:0.###}");
        }
        catch (System.Exception ex) { App.Log("透明审计(位置)失败: " + ex.Message); }

        var bad = new System.Collections.Generic.List<string>();
        void Walk(System.Windows.DependencyObject d)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (c is System.Windows.UIElement e && e.Opacity < 0.999)
                {
                    string nm = (c as System.Windows.FrameworkElement)?.Name;
                    bad.Add((string.IsNullOrEmpty(nm) ? c.GetType().Name : nm)
                            + "=" + e.Opacity.ToString("0.00"));
                }
                Walk(c);
            }
        }
        Walk(this);
        App.Log("透明审计: " + (bad.Count == 0 ? "视觉树里没有不满的"
                                : string.Join(" | ", bad)));
    }
"""


def main():
    s = io.open(P, encoding="utf-8", newline="").read()
    assert "\r\n" in s and s.count("\r\r\n") == 0 and s.count("\n") == s.count("\r\n")
    if "AuditTransparency" in s:
        raise SystemExit("!! 已经挂过了")
    if ANCHOR not in s:
        raise SystemExit("!! 锚点(兜底那几行)找不到")
    s = s.replace(ANCHOR, NEW_CALL, 1)

    # 审计方法插在 KickCompositionLater 之后(它的结尾就是上面那个 ANCHOR)
    lines = AUDIT.strip("\n").split("\n")
    s = s.replace(NEW_CALL, NEW_CALL + "\r\n".join(lines) + "\r\n", 1)

    io.open(P, "w", encoding="utf-8", newline="").write(s)
    t = io.open(P, encoding="utf-8", newline="").read()
    assert t.count("\r\r\n") == 0 and t.count("\n") == t.count("\r\n"), "!! 换行脏了"
    print("挂上了, 换行 CRLF=%d" % t.count("\r\n"))


if __name__ == "__main__":
    main()
