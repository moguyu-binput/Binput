# -*- coding: utf-8 -*-
"""清掉"动态效果总开关"的残留: Theme.cs 里那个恒为真的 Fx。

Fx 恒 true, 于是 Render.cs 里凡写过 "&& !Fx" 的分支永远不成立、
写过 "Fx ? a : b" 的永远取 a。留下真的那一支, 假的那一支删掉。

连带: keyDown / gDown 这两个变量只剩那些被删掉的地方在用, 一起去掉,
      否则编译器要说"赋了值没人用"。

纯文本替换 —— 每条都断言"原文原样出现且只出现一次", 找不到就整体退出。
"""
import os, sys

sys.stdout.reconfigure(encoding="utf-8")
SRC = r"C:\Users\<你>\Binput"


def load(name):
    raw = open(os.path.join(SRC, name), "rb").read().decode("utf-8")
    assert "\r\r\n" not in raw, name + " 里有 \\r\\r\\n"
    nl, crlf = raw.count("\n"), raw.count("\r\n")
    assert nl == crlf, "%s 不是纯 CRLF (裸 LF=%d)" % (name, nl - crlf)
    return raw.replace("\r\n", "\n").split("\n")


def save(name, lines):
    out = "\r\n".join(lines)
    assert "\r\r\n" not in out and out.count("\n") == out.count("\r\n")
    open(os.path.join(SRC, name), "wb").write(out.encode("utf-8"))
    print("  写入 %-30s %d 行" % (name, len(lines)))


def one(name, lines, old, new):
    o, n = old.split("\n"), new.split("\n")
    hit = [i for i in range(len(lines) - len(o) + 1) if lines[i:i + len(o)] == o]
    if len(hit) != 1:
        raise SystemExit("%s 里这段命中 %d 次(应为 1):\n%s" % (name, len(hit), old))
    lines[hit[0]:hit[0] + len(o)] = n
    print("  %-30s %s" % (name, o[0].strip()[:50]))
    return lines


R = load("OverlayWindow.Render.cs")

R = one("OverlayWindow.Render.cs", R,
        "        // 动态效果开着的时候九键的摇杆线整层收掉: 焦点在哪纯看哪颗键凸出来\n"
        "        // (摇杆回中就是中间的 5 凸出来), 那根线再画就成了多余的干扰。\n"
        "        StickLayer.Visibility = (!en && !gridMode && !c.Resizing && !Fx)\n"
        "            ? Visibility.Visible : Visibility.Collapsed;",
        "        // 九键的摇杆线整层收掉: 焦点在哪纯看哪颗键凸出来(摇杆回中就是中间的 5\n"
        "        // 凸出来), 那根线再画就成了多余的干扰。\n"
        "        StickLayer.Visibility = Visibility.Collapsed;")

R = one("OverlayWindow.Render.cs", R,
        "                // 动态效果关着时没有缩放, 改用\"按下去变暗\"表示 —— 效果开关\n"
        "                // 只该管有没有动画, 不该管有没有反馈。\n"
        "                // (以前这里把左上角那颗 1 压暗, 因为它当年没功能; 2.0.0 起 JKL 搬过去了,\n"
        "                //  九颗键全都有事可做, 也就没有要压暗的那颗了。)\n"
        "                // 滑行时反过来: 除了笔尖那颗, 其余一并压暗。只九颗键长得一模一样,\n"
        "                // 光让一颗变大在余光里还是不够跳, 旁边沉下去它才真站出来。\n"
        "                // 这一手只在动态效果开着时做 —— 关着的时候有一条摇杆线在画,\n"
        "                // 那一笔本身就是位置, 再压暗就过了。\n"
        "                _key9[i].Opacity = sled > 0 && Fx && !here ? SLIDE_DIM\n"
        "                                 : (on && !Fx && keyDown) ? FxKit.DimPress : 1;",
        "                // 滑行时: 除了笔尖那颗, 其余一并压暗。九颗键长得一模一样,\n"
        "                // 光让一颗变大在余光里还是不够跳, 旁边沉下去它才真站出来。\n"
        "                // (以前这里还兼管\"动态效果关着时按下去变暗\"那一手 —— 那个开关\n"
        "                //  早就撤了、恒开着, 那条永远走不到的路一并删掉。)\n"
        "                _key9[i].Opacity = sled > 0 && !here ? SLIDE_DIM : 1;")

R = one("OverlayWindow.Render.cs", R,
        "            UpdateZeroKey(c, !Fx && keyDown);",
        "            UpdateZeroKey(c);")

R = one("OverlayWindow.Render.cs", R,
        "        // 摇杆线还画着(动态效果关着)才去算那几个点的位置\n"
        "        if (!en && !gridMode && !Fx) UpdateStick(c);",
        "        // (以前这儿还连着一句\"动态效果关着才去算摇杆线那几个点\" —— 那个开关\n"
        "        //  撤了之后这条永远不成立。UpdateStick 方法本身留着没动, 连同\n"
        "        //  BuildStickLayer 画的那根虚线一起, 哪天要让它露脸接回来就行。)")

def drop(name, lines, target):
    """删掉内容等于 target(去掉两头空白后比)的那一行, 缩进不用管。"""
    hit = [i for i, l in enumerate(lines) if l.strip() == target.strip()]
    if len(hit) != 1:
        raise SystemExit("%s 里这行命中 %d 次(应为 1): %s" % (name, len(hit), target))
    lines.pop(hit[0])
    print("  %-30s 删行: %s" % (name, target.strip()[:58]))
    return lines


for t in (
        "_en[row][col].Opacity = (on && !Fx && keyDown) ? FxKit.DimPress : 1;",
        "_candRow[i].Opacity = (on && !Fx && keyDown) ? FxKit.DimPress : 1;",
        "_grid[i].Opacity = (on && !Fx && gDown) ? FxKit.DimPress : 1;",
        "bool keyDown = !gridMode && anyDown;",
        "bool gDown = !c.Settings && (c.HeldMask & PRESS_MASK) != 0;",
):
    R = drop("OverlayWindow.Render.cs", R, t)

R = one("OverlayWindow.Render.cs", R,
        "        FxSet(Fx ? fxId : \"\", Fx ? fxB : null, fxS, Fx && anyDown);",
        "        FxSet(fxId, fxB, fxS, anyDown);")

save("OverlayWindow.Render.cs", R)

T = load("OverlayWindow.Theme.cs")
T = one("OverlayWindow.Theme.cs", T,
        "    /// <summary>\n"
        "    /// 动态效果**恒开**, 这一档不再能关(设置页里那一项 2.0.1 撤掉了)。\n"
        "    /// 开的时候: 焦点那颗键会弹一下并浮起来(微微放大 + 一点影子, 压住旁边的键),\n"
        "    /// 九键的摇杆线整层收掉 —— 焦点在哪, 看哪颗键凸出来就知道。\n"
        "    /// <para>写成一个\"永远为真\"的属性而不是直接把判断删掉, 是为了让**摇杆线那套\n"
        "    /// 代码原样留着**: UpdateStick / StickLayer / 那根虚线都还在, 哪天要让它\n"
        "    /// 在别的场合露脸(比如滑行时也想看摇杆方位), 把这里改成条件即可。</para>\n"
        "    /// </summary>\n"
        "    public bool Fx => true;\n",
        "")
save("OverlayWindow.Theme.cs", T)

print("完成")
