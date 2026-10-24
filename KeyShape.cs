namespace Binput;

/// <summary>
/// 按键的形状 —— 设置里「键盘 → 按键风格」那一格往里钻一层挑。
/// <para>它管的其实只有一件事: **圆角取多少**。圆形那一档一律取"短边的一半",
/// 于是正方形的键(九键那九颗、设置和窗口F 里的格子、「关于」并成的那一大块)
/// 自己变成正圆, 长条的键(789 合成那条、26 键里的空格 / Shift / 回车 / Tab,
/// 还有候选行和符号网格里那些扁格)自己变成胶囊 —— 不用给谁单独写一刀,
/// 同一条规矩把它们分开。</para>
/// <para>【格子尺寸一个都不动】直径就是原来那个方块的边长, 所以布局、窗口大小、
/// 缩放的锚点、换页动效全都不受影响 —— 变的只有圆角这一个数。这也是为什么
/// 四面的缝不能为了"圆挨得紧一点"去收: 九键那颗键是 112, 设置里的格子也是
/// 112(写死的), 两边必须一直一样大。</para>
/// </summary>
public enum KeyShape
{
    /// <summary>圆角方块。出厂默认, 一直以来的样子。</summary>
    Square = 0,
    /// <summary>
    /// 圆形: 正方形变正圆(直径 = 原来的边长), 长条变胶囊(两头各一个半圆,
    /// 半径 = 短边的一半)。<para>贴纸风那套三段式贴图会让位 —— 它是张方图,
    /// Border 的圆角裁不到子元素头上, 留着就会露在圆外面。可爱粉的九键例外:
    /// 花瓣是整图铺在底上的, 归 Border 自己管, 照旧。</para>
    /// </summary>
    Round = 1,
}

/// <summary>
/// 【加一种新形状, 一处登记】枚举上面加一个成员, 再往 <see cref="KeyShapes.All"/>
/// 里补一条 <see cref="KeyShapes.Info"/> —— 就这两处, 都在本文件里。
/// <para>和 <see cref="FxStyles"/> 是同一套规矩: 名字 / 第二行小字 / 底下的长说明
/// / 落盘解析, 全由这一张表推出来, 设置页摆几项由表的长度决定。漏一条的话
/// coretest 【35】会点名。</para>
/// </summary>
public static class KeyShapes
{
    /// <summary>一种形状的"人话"三件套: 格子里的大字、第二行小字、底下那条长说明。</summary>
    public readonly record struct Info(string Name, string Note, string Tip);

    /// <summary>
    /// **顺序必须和 <see cref="KeyShape"/> 的数值一致**(下标就是枚举值)。
    /// 加一种形状 = 这里加一条; 漏了会被 coretest 【35】点名。
    /// </summary>
    public static readonly Info[] All =
    {
        new Info(
            "方块",
            "圆角 · 方块",
            "一直以来的样子: 九颗键是圆角方块, 长条键是圆角长条。换成别的形状随时可以 "
            + "换回来, 格子大小和位置一格都不会动。"),

        new Info(
            "圆形",
            "正圆 · 胶囊",
            "圆的直径就是原来那个方块的边长, 所以键盘一点没变大也没变小, 只是方角修成了 "
            + "圆角 —— 正方形那几颗(九键、设置里的格子、「关于」那一大块)变成正圆, "
            + "长条那几条(789 合成的那条、全键盘的空格 / Shift / 回车、候选行和符号网格 "
            + "里那些扁格)变成两头半圆的胶囊。全键盘的字母键是 38×40, 差两个像素, "
            + "按短边取, 看着就是圆的。贴纸风那套贴图会让位给纯色圆 —— 它是方图, "
            + "裁不进圆里; 可爱粉的九键仍是花瓣。"),
    };

    /// <summary>表里有没有这一档(存档里可能存着认不出的数)。</summary>
    public static bool Has(KeyShape s) => (int)s >= 0 && (int)s < All.Length;

    /// <summary>取一档的完整信息。认不出就退回默认那档(不是 null, 调用方不必判空)。</summary>
    public static Info Of(KeyShape s) => Has(s) ? All[(int)s] : All[0];

    public static string Name(KeyShape s) => Of(s).Name;
    public static string Note(KeyShape s) => Of(s).Note;
    public static string Tip(KeyShape s) => Of(s).Tip;

    /// <summary>设置页摆几项 —— 由表的长度决定, 不再另写一个数组去数。</summary>
    public static int Count => All.Length;

    /// <summary>
    /// **圆形那一档的圆角取多少**: 短边的一半。正方形 -> 正圆, 长条 -> 胶囊。
    /// <para>【量不到尺寸就回 0 跳过】那一帧还没排版(ActualWidth 是 0)的时候,
    /// 不许退回某个"大概的参考值" —— 头一帧按错的圆角画出来的圆, 等真尺寸量到
    /// 再改, 就是一次肉眼可见的"先方后圆"。回 0 由调用方留着原样, 下一轮再刷。</para>
    /// </summary>
    public static double RadiusOf(double w, double h)
    {
        double s = Math.Min(w, h);
        return s > 0 ? s / 2 : 0;
    }

    /// <summary>
    /// 【给探针用】表有没有跟上枚举。返回空列表 = 齐的; 有内容就是"漏了哪一条"。
    /// <para>为什么要有它: 表和枚举是两个东西, C# 不会替你检查它们对不对得上。
    /// 少一条的话 <see cref="Of"/> 会退回默认那档 —— 界面上看着是"方块",
    /// 谁也不知道其实是新加的那种没登记。见 coretest 【35】。</para>
    /// </summary>
    public static List<string> Gaps()
    {
        var bad = new List<string>();
        foreach (KeyShape s in Enum.GetValues(typeof(KeyShape)))
        {
            if (!Has(s)) { bad.Add($"{(int)s} 在表里没有对应的一条"); continue; }
            var it = All[(int)s];
            if (string.IsNullOrWhiteSpace(it.Name)) bad.Add($"{s}: 缺名字");
            if (string.IsNullOrWhiteSpace(it.Note)) bad.Add($"{s}: 缺第二行小字");
            if (string.IsNullOrWhiteSpace(it.Tip)) bad.Add($"{s}: 缺底下的长说明");
        }
        return bad;
    }
}
