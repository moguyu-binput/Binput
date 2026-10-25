# -*- coding: utf-8 -*-
"""圆形风格: 深层页面(设置 / 窗口F / 符号网格)的格子没被刷成圆的 —— 定点替换。
二进制读写, 保 CRLF。
"""
import io, sys

def load(p):
    with open(p, "rb") as f:
        return f.read()

def save(p, b):
    assert b.count(b"\r\r\n") == 0, "写出 \\r\\r\\n 了"
    with open(p, "wb") as f:
        f.write(b)

def sub(b, old, new, path, tag):
    o = old.replace("\n", "\r\n").encode("utf-8")
    n = new.replace("\n", "\r\n").encode("utf-8")
    if b.count(o) != 1:
        print("FAIL %s [%s] 命中 %d 次" % (path, tag, b.count(o)))
        sys.exit(1)
    print("ok   %s [%s]" % (path, tag))
    return b.replace(o, n)

# ---------- 1. Build.cs: 两个新附加属性 + ShapeBase 挂钩子 + ShapeRound 收规矩 ----------
p = "OverlayWindow.Build.cs"
b = load(p)

old = """    static readonly DependencyProperty BaseRProperty =
        DependencyProperty.RegisterAttached("BaseR", typeof(double), typeof(OverlayWindow),
                                            new PropertyMetadata(0.0));

    /// <summary>记下这颗键在"方块那一档"下的圆角, 并按当前形状刷一次。</summary>
    void ShapeBase(Border b, double radius)
    {
        b.SetValue(BaseRProperty, radius);
        ShapeRound(b);
    }
"""

new = """    static readonly DependencyProperty BaseRProperty =
        DependencyProperty.RegisterAttached("BaseR", typeof(double), typeof(OverlayWindow),
                                            new PropertyMetadata(0.0));

    /// <summary>
    /// 这一颗是"可爱粉的花瓣键" —— 圆角永远跟着方块那一档, 不跟着圆形风格走。
    /// <para>只有九键那九颗打这个标记。花瓣是**整图铺在键底上**的, 归 Border 自己管,
    /// 圆角裁不到它头上也不该裁 —— 那一套就是花瓣的样子。</para>
    /// </summary>
    static readonly DependencyProperty PetalProperty =
        DependencyProperty.RegisterAttached("Petal", typeof(bool), typeof(OverlayWindow),
                                            new PropertyMetadata(false));

    /// <summary>"尺寸一变就重刷圆角"那个钩子挂上了吗(防重复挂, 见 ShapeBase)。</summary>
    static readonly DependencyProperty HookedProperty =
        DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(OverlayWindow),
                                            new PropertyMetadata(false));

    /// <summary>记下这颗键在"方块那一档"下的圆角, 并按当前形状刷一次。</summary>
    void ShapeBase(Border b, double radius)
    {
        b.SetValue(BaseRProperty, radius);
        // 【尺寸一落地就再刷一次】圆形那一档的圆角是按"实测量到的短边"算的, 而刚造出来
        // 那一刻还没排版(ActualWidth 是 0)。设置 / 窗口F 的格子是**每次进一层现搭**的
        // (见 FillPanel), 符号网格那 40 格更是开面板才显出来 —— 搭完没人再刷它们,
        // 于是"点进深层还是方的, 偶尔撞上一次 Settle 才变圆"。挂在尺寸上最省事也最准:
        // 谁排版完了谁触发一次, 以后改缩放也跟着走。
        // 【只挂一次】换主题会挨个再调一回 ShapeBase(见 ApplyTheme), 不记这一笔的话
        // 换十次主题就挂上十个钩子(LayerHost.VeilHooked 是同一个坑)。
        if (!(bool)b.GetValue(HookedProperty))
        {
            b.SetValue(HookedProperty, true);
            b.SizeChanged += OnKeySize;
        }
        ShapeRound(b);
    }

    /// <summary>尺寸定了(或者改了) —— 圆角按新的短边重算一遍。</summary>
    void OnKeySize(object sender, SizeChangedEventArgs e) => ShapeRound(sender as Border);
"""
b = sub(b, old, new, p, "加属性 + ShapeBase 挂钩")

old = """    void ShapeRound(Border b)
    {
        if (b == null) return;
        double r = _keyShape == KeyShape.Round
            ? KeyShapes.RadiusOf(b.ActualWidth, b.ActualHeight) : 0;
        b.CornerRadius = new CornerRadius(r > 0 ? r : (double)b.GetValue(BaseRProperty));
    }
"""

new = """    void ShapeRound(Border b)
    {
        if (b == null) return;
        // 【可爱粉的九键是唯一的例外, 规矩写在这里而不是写在遍历那一处】
        // 现在刷圆角的时机不止"换形状 / 换主题 / 布局落定"那一处了 —— 每颗键自己
        // 尺寸一变也会刷(见上面的 OnKeySize)。例外漏一处, 表现就是"某些时候九键
        // 忽然被改成了正圆, 换一次主题又变回花瓣"。
        bool petal = (bool)b.GetValue(PetalProperty) && SkinMode == 1;
        double r = !petal && _keyShape == KeyShape.Round
            ? KeyShapes.RadiusOf(b.ActualWidth, b.ActualHeight) : 0;
        b.CornerRadius = new CornerRadius(r > 0 ? r : (double)b.GetValue(BaseRProperty));
    }
"""
b = sub(b, old, new, p, "ShapeRound 收进花瓣例外")

old = """        if (SkinMode != 1)                       // 可爱粉: 九键保持花瓣
            for (int k = 1; k <= 9; k++) ShapeRound(_key9[k]);
"""
new = """        // 九键照旧一起走 —— 花瓣那个例外由 ShapeRound 自己判(见那里), 这里不重复一遍。
        for (int k = 1; k <= 9; k++) ShapeRound(_key9[k]);
"""
b = sub(b, old, new, p, "ApplyKeyShape 不再两处判花瓣")

old = """            var b = Box(10);
            // 花瓣贴图的普通花色跟着 Tag 走(Paint 里读): 粉/薄荷/淡紫照着底板排
"""
new = """            var b = Box(10);
            b.SetValue(PetalProperty, true);   // 可爱粉下它就是花瓣, 不跟着圆形风格变(见 ShapeRound)
            // 花瓣贴图的普通花色跟着 Tag 走(Paint 里读): 粉/薄荷/淡紫照着底板排
"""
b = sub(b, old, new, p, "九键打花瓣标记")
save(p, b)

# ---------- 2. Theme.cs: 换主题时"方块那一档"也要跟着更新 ----------
p = "OverlayWindow.Theme.cs"
b = load(p)

old = """        // 已经搭好的那些格子也跟着换圆角(它们不是每次都重搭的)
        foreach (var h in new[] { _set, _qk })
            foreach (var b in h.Boxes)
                b.CornerRadius = new CornerRadius(_cellR);
"""
new = """        // 已经搭好的那些格子也跟着换圆角(它们不是每次都重搭的)。
        // 【走 ShapeBase, 不直接赋 CornerRadius】它顺手把"方块那一档"也更新成新的
        // _cellR 并按当前形状刷一遍 —— 直接赋的话, 切回方块时这些格子留着上一套主题
        // 的圆角(可爱粉 18 / 纯色 10 两套数会串)。
        foreach (var h in new[] { _set, _qk })
            foreach (var b in h.Boxes)
                ShapeBase(b, _cellR);
"""
b = sub(b, old, new, p, "换主题: 格子走 ShapeBase")

old = """            _key9[k].CornerRadius = new CornerRadius(cute ? 18 : 10);
"""
new = """            ShapeBase(_key9[k], cute ? 18 : 10);   // 同上: 连"方块那一档"一起更新
"""
b = sub(b, old, new, p, "换主题: 九键走 ShapeBase")
save(p, b)

print("done")
