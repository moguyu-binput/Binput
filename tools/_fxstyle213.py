# -*- coding: utf-8 -*-
"""加一个设置项: 换页动效(默认 / 旋转)。

照 SlideMode 那一套的写法走一遍:
  FxStyle.cs(枚举) + Config(读写) + InputCore(字段) + ISettingsHost(接口)
  + App(加载/保存/日志) + OverlayWindow.Pages(首页那一格 + 往里钻一层)

顺带按用户要求挪首页: 动效插到正中(第 5 格), 唤醒挪到最后一格。
"""
import os, sys

sys.stdout.reconfigure(encoding="utf-8")
SRC = r"C:\Users\<你>\Binput"


def load(name):
    raw = open(os.path.join(SRC, name), "rb").read().decode("utf-8")
    assert "\r\r\n" not in raw, name + " 有 \\r\\r\\n"
    nl, crlf = raw.count("\n"), raw.count("\r\n")
    if nl == crlf and nl:
        return raw.replace("\r\n", "\n").split("\n"), "\r\n"
    if crlf == 0:
        return raw.split("\n"), "\n"
    raise SystemExit("%s 换行混杂: LF=%d CRLF=%d" % (name, nl - crlf, crlf))


def save(name, lines, eol):
    out = eol.join(lines)
    open(os.path.join(SRC, name), "wb").write(out.encode("utf-8"))
    print("  写入 %-30s %d 行 (%s)" % (name, len(lines),
                                        "CRLF" if eol == "\r\n" else "LF"))


def ins(name, lines, anchor, block, after=True):
    """在 anchor(可多行, 每行按内容比、忽略缩进)的前面或后面插一段。"""
    a = [x.strip() for x in anchor.rstrip("\n").split("\n")]
    hit = [i for i in range(len(lines) - len(a) + 1)
           if [x.strip() for x in lines[i:i + len(a)]] == a]
    if len(hit) != 1:
        raise SystemExit("%s 里锚点命中 %d 次: %s" % (name, len(hit), anchor[:60]))
    at = hit[0] + (len(a) if after else 0)
    lines[at:at] = block.split("\n")
    print("  %-30s 插入 %d 行于 %r %s" % (name, len(block.split("\n")),
                                           anchor.strip()[:36], "之后" if after else "之前"))
    return lines


def one(name, lines, old, new):
    o, n = old.rstrip("\n").split("\n"), new.rstrip("\n").split("\n")
    hit = [i for i in range(len(lines) - len(o) + 1) if lines[i:i + len(o)] == o]
    if len(hit) != 1:
        raise SystemExit("%s 里这段命中 %d 次: %s" % (name, len(hit), old[:70]))
    lines[hit[0]:hit[0] + len(o)] = n
    print("  %-30s 替换: %s" % (name, o[0].strip()[:48]))
    return lines


# ---------------- 1. 枚举 ----------------
ENUM = """namespace Binput;

/// <summary>
/// 换页面的动效风格 —— 设置首页「动效」那一格往里钻一层挑。
/// <para>这一项决定**所有"换一块内容"的动作**怎么演: 往设置里钻一层、退回上一层、
/// 九键换成标点那一面、按住 LB 进滑行、LB / RB 翻页 —— 全走同一套。
/// 九键 <-> 全键盘那种"左右撑开"不算换页, 不在这儿.</para>
/// <para>加一种新风格的步骤: 这里加一个成员, FxKit 里加一组数字,
/// 再到 OverlayWindow.Fx.cs 那两只柜子各挂一笔(见那里头的表)。</para>
/// </summary>
public enum FxStyle
{
    /// <summary>默认: 旧页缩小下沉淡出、新页放大浮起淡入。最省眼的一套。</summary>
    Plain = 0,
    /// <summary>扑克牌: 绕整块左下角旋转, 旧页顺时针转走、新页从另一边转回来。</summary>
    Spin = 1,
}
"""
p = os.path.join(SRC, "FxStyle.cs")
open(p, "wb").write(ENUM.replace("\n", "\r\n").encode("utf-8"))
print("  新建 %-30s %d 行 (CRLF)" % ("FxStyle.cs", ENUM.count("\n")))

# ---------------- 2. Config ----------------
C, e = load("Config.cs")
C = ins("Config.cs", C,
        "    public static void SaveSlide(SlideMode mode, int dwell)\n"
        "    {\n"
        "        Put(\"slide\", ((int)mode).ToString(CultureInfo.InvariantCulture));\n"
        "        Put(\"dwell\", Math.Clamp(dwell, 30, 600).ToString(CultureInfo.InvariantCulture));\n"
        "        Flush();\n"
        "    }",
        "\n    /// <summary>读换页动效(见 FxStyle)。没存过 = 默认那套。</summary>\n"
        "    public static FxStyle LoadFxStyle()\n"
        "    {\n"
        "        Ensure();\n"
        "        return Num(\"fxstyle\", (int)FxStyle.Plain) == (int)FxStyle.Spin\n"
        "            ? FxStyle.Spin : FxStyle.Plain;\n"
        "    }\n"
        "\n"
        "    public static void SaveFxStyle(FxStyle s)\n"
        "    {\n"
        "        Put(\"fxstyle\", ((int)s).ToString(CultureInfo.InvariantCulture));\n"
        "        Flush();\n"
        "    }",
        after=True)
save("Config.cs", C, e)

# ---------------- 3. InputCore ----------------
I, e = load("InputCore.cs")
I = ins("InputCore.cs", I, "public SlideMode SlideMode = SlideMode.Type;",
        "\n    /// <summary>\n"
        "    /// 换页面的动效风格(见 FxStyle)。界面每演一场都问一次当前值,\n"
        "    /// 于是在设置里改完**下一笔立即生效**, 不用重开界面。\n"
        "    /// </summary>\n"
        "    public FxStyle FxStyle = FxStyle.Plain;")
save("InputCore.cs", I, e)

# ---------------- 4. ISettingsHost ----------------
H, e = load("ISettingsHost.cs")
H = ins("ISettingsHost.cs", H, "    string SlideInfo();",
        "\n    /// <summary>\n"
        "    /// 换页面的动效风格(见 FxStyle): 默认 = 浮起淡入, 旋转 = 扑克牌那样绕\n"
        "    /// 左下角转。所有\"换一块内容\"的地方(进层 / 退层 / 换九键那一面 / 翻页)\n"
        "    /// 都按这一档演, 只有九键 <-> 全键盘的左右撑开不受它管。\n"
        "    /// </summary>\n"
        "    FxStyle FxStyle { get; set; }")
save("ISettingsHost.cs", H, e)

# ---------------- 5. App ----------------
A, e = load("App.xaml.cs")
A = one("App.xaml.cs", A,
        "        Log($\"滑行输入: {SlideModeName(slMode)}, 门槛 {slDwell}ms\");",
        "        Log($\"滑行输入: {SlideModeName(slMode)}, 门槛 {slDwell}ms\");\n"
        "\n"
        "        var fxStyle = Config.LoadFxStyle();\n"
        "        _core.FxStyle = fxStyle;\n"
        "        Log(\"换页动效: \" + FxStyleName(fxStyle));")

A = ins("App.xaml.cs", A, "    public static string SlideModeName(SlideMode m) => m == SlideMode.Off ? \"关\" : \"开\";",
        "\n    /// <summary>设置页那个按钮上的字。</summary>\n"
        "    public static string FxStyleName(FxStyle s) => s == FxStyle.Spin ? \"旋转\" : \"默认\";")

A = ins("App.xaml.cs", A, "    public int SlideDwell\n    {",
        "    public FxStyle FxStyle\n"
        "    {\n"
        "        get => _core.FxStyle;\n"
        "        set\n"
        "        {\n"
        "            if (value == _core.FxStyle) return;\n"
        "            _core.FxStyle = value;\n"
        "            Config.SaveFxStyle(value);\n"
        "            Log(\"换页动效 -> \" + FxStyleName(value));\n"
        "        }\n"
        "    }\n",
        after=False)
save("App.xaml.cs", A, e)

# ---------------- 6. Pages ----------------
P, e = load("OverlayWindow.Pages.cs")
P = one("OverlayWindow.Pages.cs", P,
        "            Cell(\"唤醒\", WakeKeys(), child: PageWake,\n"
        "                 tip: () => \"唤出 / 收起界面那一颗键。定了自定义之后默认的 L3+R3 就不再生效。\"),\n"
        "            Cell(\"数据\", \"习惯 · 搬家\", child: PageData,\n"
        "                 tip: () => \"你挑过的拼音会越用越靠前(存在本地); 换机器可以导出来带走。\"),\n"
        "            Cell(\"维护\", \"日志 · 关于\", child: PageKeep,\n"
        "                 tip: () => \"出问题先看日志: 启动、按键、贴没贴上都记着。\"),\n"
        "            Cell(\"退出\", \"结束程序\", act: () => Host?.ExitApp(),\n"
        "                 tip: () => \"直接结束进程(和托盘菜单里的“退出”一样)。设置和输入习惯都存好了。\"),",
        "            Cell(\"动效\", App.FxStyleName(Host?.FxStyle ?? FxStyle.Plain), child: PageFxStyle,\n"
        "                 tip: () => \"换页面那一整套动作的风格: 进设置、换九键那一面、LB / RB 翻页 \"\n"
        "                            + \"都是它管。改完下一次换页立刻生效, 不用重开。\"),\n"
        "            Cell(\"数据\", \"习惯 · 搬家\", child: PageData,\n"
        "                 tip: () => \"你挑过的拼音会越用越靠前(存在本地); 换机器可以导出来带走。\"),\n"
        "            Cell(\"维护\", \"日志 · 关于\", child: PageKeep,\n"
        "                 tip: () => \"出问题先看日志: 启动、按键、贴没贴上都记着。\"),\n"
        "            Cell(\"退出\", \"结束程序\", act: () => Host?.ExitApp(),\n"
        "                 tip: () => \"直接结束进程(和托盘菜单里的“退出”一样)。设置和输入习惯都存好了。\"),\n"
        "            Cell(\"唤醒\", WakeKeys(), child: PageWake,\n"
        "                 tip: () => \"唤出 / 收起界面那一颗键。定了自定义之后默认的 L3+R3 就不再生效。\"),")

P = ins("OverlayWindow.Pages.cs", P, "    SetPage PageVer() => new SetPage",
        "    // 换页动效那几种。跟着 FxStyle 的顺序 —— 加一种就在这儿补一条。\n"
        "    // note 是格子里的第二行小字, tip 是底下那条长说明。\n"
        "    static readonly string[] FxStyleNotes =\n"
        "    {\n"
        "        \"浮起 · 淡入\",\n"
        "        \"扑克牌 · 旋转\",\n"
        "    };\n"
        "    static readonly string[] FxStyleTips =\n"
        "    {\n"
        "        \"旧的一页缩小下沉淡出, 新的一页放大浮起来淡入。最省眼睛的一套, \"\n"
        "        + \"一直以来的样子 —— 拿不准就先用它。\",\n"
        "\n"
        "        \"整块绕左下角转: 旧的一页顺时针转出去一边转一边淡掉, 新的从另一边 \"\n"
        "        + \"转回来、由透明转实。两页之间始终差着一个不大的角度, 像手指捻开 \"\n"
        "        + \"一把扑克牌。按 B 退回上一层时整个动作反过来, 逆时针转回去。\",\n"
        "    };\n"
        "\n"
        "    SetPage PageFxStyle() => new SetPage\n"
        "    {\n"
        "        Title = \"换页动效\",\n"
        "        Build = () =>\n"
        "        {\n"
        "            var l = new List<SetCell>();\n"
        "            for (int i = 0; i < FxStyleNotes.Length; i++)\n"
        "            {\n"
        "                int k = i;\n"
        "                var s = (FxStyle)k;\n"
        "                l.Add(Cell(App.FxStyleName(s), FxStyleNotes[k],\n"
        "                           act: () => { if (Host != null) Host.FxStyle = s; RefreshSettings(); },\n"
        "                           on: () => (Host?.FxStyle ?? FxStyle.Plain) == s,\n"
        "                           tip: () => FxStyleTips[k]));\n"
        "            }\n"
        "            return l;\n"
        "        },\n"
        "    };\n",
        after=False)
save("OverlayWindow.Pages.cs", P, e)

print("完成")
