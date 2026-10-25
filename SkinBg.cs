using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Binput;

/// <summary>
/// 三段式贴图背景: 左端帽 - 中段 - 右端帽。
/// <para>键的贴图是一张 237×96 的圆角胶囊。整张拉到 180×33 的空格键上时, 圆角会被
/// 拉成半椭圆, 高光也会跟着扁掉 —— 一眼就看出"被拉过"。所以这里切成三段:
/// 两个端帽(各 48px, 圆角所在)按高度等比放, 中间那 12px 是纯色渐变带, 随便拉多长
/// 都还是原样。跟 CSS 的 border-image / 九宫格拉伸是同一个套路。</para>
/// 贴图缺失时自己变透明, 界面退回纯色画法, 不会开天窗。
/// </summary>
sealed class SkinBg : Grid
{
    readonly Image _l, _m, _r;
    string _color = "";

    public SkinBg()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _l = Cap(0); _r = Cap(2);
        _m = new Image { Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(_m, 1);

        Children.Add(_l); Children.Add(_m); Children.Add(_r);
        IsHitTestVisible = false;
        // 端帽宽度永远等于"半个键高"(就是胶囊那个圆角的半径), 键多高它就多宽
        SizeChanged += (_, _) =>
        {
            double cap = ActualHeight * 0.5;
            if (cap <= 0 || double.IsNaN(cap)) return;
            _l.Width = cap; _r.Width = cap;
        };
    }

    static Image Cap(int col)
    {
        var im = new Image
        {
            Stretch = Stretch.Fill,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(im, col);
        return im;
    }

    /// <summary>换成某个颜色的贴图(pink / mint / cream / lavender)。</summary>
    public void Set(string color)
    {
        if (_color == color) return;
        _color = color;
        _l.Source = SkinTex.Img("key_" + color + "_L");
        _m.Source = SkinTex.Img("key_" + color + "_M");
        _r.Source = SkinTex.Img("key_" + color + "_R");
        Visibility = (_l.Source != null) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>整套皮肤关掉(切回纯色主题)时把贴图层收起来。</summary>
    public void Hide() => Visibility = Visibility.Collapsed;

    public void Show() => Visibility = (_l.Source != null) ? Visibility.Visible : Visibility.Collapsed;
}
