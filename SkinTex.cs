using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Binput;

/// <summary>
/// 皮肤贴图。"可爱粉"这套不是纯色画出来的, 而是真贴图: 键 / 圆片都是事先画好的
/// PNG, 运行时从程序目录下的 skin\ 里读。
/// <para>图丢了也不崩: 读不到就返回 null, 界面自动退回纯色画法。</para>
/// </summary>
static class SkinTex
{
    static readonly Dictionary<string, BitmapImage> Cache = new();

    /// <summary>skin 目录。和 exe 放一起, 便于自己换图 / 加皮肤。</summary>
    static string Dir => Path.Combine(AppContext.BaseDirectory, "skin");

    /// <summary>贴图目录在不在。不在就整套皮肤降级成纯色。</summary>
    public static bool Available => Directory.Exists(Dir);

    public static BitmapImage Img(string name)
    {
        if (Cache.TryGetValue(name, out var hit)) return hit;
        BitmapImage img = null;
        try
        {
            var p = Path.Combine(Dir, name + ".png");
            if (File.Exists(p))
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(p, UriKind.Absolute);
                // 整张读进内存再显示: 文件在程序目录里, 一直被 exe 占着不放会挡住替换
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.EndInit();
                b.Freeze();
                img = b;
            }
        }
        catch { img = null; }
        Cache[name] = img;
        return img;
    }

    /// <summary>铺满的画刷。用于"整体等比"的图(花瓣键、圆片)—— 长条键别用它, 圆角会被拉成椭圆。</summary>
    public static Brush Fill(string name)
    {
        var img = Img(name);
        return img == null ? null : new ImageBrush(img) { Stretch = Stretch.Fill };
    }
}
