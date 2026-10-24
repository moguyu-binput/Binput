# -*- coding: utf-8 -*-
"""
2.1.12: 把撒在各处的动效数字收进 OverlayWindow.FxKit.cs。

纪律(踩过坑的老规矩):
- 所有 .cs 一律 CRLF。读的时候自己按 '\r\n' 切, 写完再 '\r\n'.join 回去,
  绝不让 Python 的通用换行插手(那会把 \r\r\n 和裸 LF 掺进来)。
- 按**行号**改写并校验该行必须含某个子串 —— 行号漂了会当场报错, 不会静默改错行。
- 片段替换(同一串在多处出现且替换方式一样)才用片段全量替换, 并且必须断言命中次数。
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "skin-lab", "GamepadT9-Game")

EASE = "        var ease = FxKit.EaseOut();"


def sx(err, txt):
    raise SystemExit("断言失败 %s\n  实际内容: %r" % (err, txt))


# ---------------- OverlayWindow.Fx.cs ----------------
FX = [
    # ZoomTo
    (32, "EasingMode.EaseOut", EASE),
    (33, "110", "        var span = FxKit.Ms(FxKit.MsZoom);"),
    # CellPop
    (71, "EasingMode.EaseOut", EASE),
    (80, "pop * 1.07", "            sk.KeyFrames.Add(new EasingDoubleKeyFrame(pop * FxKit.PopOver,"),
    (81, "85", "                FxKit.At(FxKit.PopAt0), ease));"),
    (82, "pop * 0.98", "            sk.KeyFrames.Add(new EasingDoubleKeyFrame(pop * FxKit.PopBack,"),
    (83, "155", "                FxKit.At(FxKit.PopAt1)));"),
    (85, "215", "                FxKit.At(FxKit.PopAt2)));"),
    (93, "80", "            FxKit.At(FxKit.WobAt0), ease));"),
    (94, "0.667", "        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob * FxKit.WobBack1,"),
    (95, "150", "            FxKit.At(FxKit.WobAt1)));"),
    (96, "0.311", "        rk.KeyFrames.Add(new EasingDoubleKeyFrame(-wob * FxKit.WobBack2,"),
    (97, "215", "            FxKit.At(FxKit.WobAt2)));"),
    (99, "290", "            FxKit.At(FxKit.WobAt3)));"),
    # PageTurnFx
    (139, "EasingMode.EaseOut", EASE),
    (143, "1.14", "        // 和九键那颗键(见 FxKit.ScalePop)比略大一号 —— 小牌本来就小, 不放大点看不出来。"),
    (147, "1.20", "            sk.KeyFrames.Add(new EasingDoubleKeyFrame(FxKit.ScaleSidePop,"),
    (148, "80", "                FxKit.At(FxKit.MsSidePop0), ease));"),
    (149, "0.94", "            sk.KeyFrames.Add(new EasingDoubleKeyFrame(FxKit.ScaleSideBack,"),
    (150, "150", "                FxKit.At(FxKit.MsSidePop1)));"),
    (152, "215", "                FxKit.At(FxKit.MsSidePop2)));"),
    (158, "14 *", "        double wob = FxKit.SideDeg * side;"),
    (161, "75", "            FxKit.At(FxKit.MsSideWob0), ease));"),
    (162, "-0.60", "        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob * FxKit.SideWobBack1,"),
    (163, "150", "            FxKit.At(FxKit.WobAt1)));"),
    (164, "0.28", "        rk.KeyFrames.Add(new EasingDoubleKeyFrame(wob * FxKit.SideWobBack2,"),
    (165, "215", "            FxKit.At(FxKit.WobAt2)));"),
    (167, "290", "            FxKit.At(FxKit.WobAt3)));"),
    (171, "7 *", "        double push = FxKit.SidePushPx * side;"),
    (174, "80", "            FxKit.At(FxKit.MsSidePop0), ease));"),
    (176, "215", "            FxKit.At(FxKit.MsSidePop2)));"),
    # CellRest
    (188, "EasingMode.EaseOut", EASE),
    (189, "150", "        var span = FxKit.Ms(FxKit.MsRest);"),
    # SetCellDown
    (197, "PRESS_SCALE", '    /// PRESS_SCALE(见 FxKit.ScalePress), 同一个"按下去快、回来稍慢"的手感)。'),
    (206, "EasingMode.EaseOut", EASE),
    (208, "80 : 190", "        var span = FxKit.Ms(down ? FxKit.MsDown : FxKit.MsUp);"),
    (211, "PRESS_SCALE", "        double to = down ? FxKit.ScalePress : PopOf(p, p.Sel);"),
    (245, "SET_ANIM_MS = 180",
     "    // SET_ANIM_MS 已搬进 FxKit: 往里钻一层用 MsPageIn、旧页消散用 MsPageOut(两个都是 180)"),
    # RiseIn
    (269, "EasingMode.EaseOut", EASE),
    (270, "SET_ANIM_MS", "        var dur = FxKit.Ms(FxKit.MsPageIn);"),
    (271, "0.92", "        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(FxKit.ScalePageIn, 1, dur) { EasingFunction = ease });"),
    (272, "0.92", "        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(FxKit.ScalePageIn, 1, dur) { EasingFunction = ease });"),
    (273, "26, 0", "        tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(FxKit.DyPageIn, 0, dur) { EasingFunction = ease });"),
    # PopSetPanel
    (292, "EasingMode.EaseOut", EASE),
    (293, "170", "        var dur = FxKit.Ms(FxKit.MsFace);"),
    (294, "0.965", "        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(FxKit.ScaleFaceIn, 1, dur) { EasingFunction = ease });"),
    (295, "0.965", "        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(FxKit.ScaleFaceIn, 1, dur) { EasingFunction = ease });"),
    (296, "7, 0", "        tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(FxKit.DyFaceIn, 0, dur) { EasingFunction = ease });"),
    # PopKeyPanel
    (310, "EasingMode.EaseOut", EASE),
    (311, "170", "        var dur = FxKit.Ms(FxKit.MsFace);"),
    (312, "0.965", "        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(FxKit.ScaleFaceIn, 1, dur) { EasingFunction = ease });"),
    (313, "0.965", "        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(FxKit.ScaleFaceIn, 1, dur) { EasingFunction = ease });"),
    (314, "7, 0", "        tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(FxKit.DyFaceIn, 0, dur) { EasingFunction = ease });"),
    # SinkLeave
    (329, "EasingMode.EaseOut", EASE),
    (330, "SET_ANIM_MS", "        var dur = FxKit.Ms(FxKit.MsPageOut);"),
    (331, "1, 0.94", "        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, FxKit.ScalePageOut, dur) { EasingFunction = ease });"),
    (332, "1, 0.94", "        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, FxKit.ScalePageOut, dur) { EasingFunction = ease });"),
    (333, "0, 10", "        tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, FxKit.DyPageOut, dur) { EasingFunction = ease });"),
    # 翻面
    (348, "FLIP_HALF_MS = 65", "    // FLIP_HALF_MS 已搬进 FxKit.MsFlipHalf(65)"),
    (350, "FLIP_STEP_MS = 32", "    // FLIP_STEP_MS 已搬进 FxKit.MsFlipStep(32)"),
    (372, "FLIP_STEP_MS", "            _keyFlipAt[i] = now.AddMilliseconds(WaveOf(i) * FxKit.MsFlipStep + FxKit.MsFlipHalf);"),
    (392, "FLIP_STEP_MS", "        double wait = wave * FxKit.MsFlipStep;"),
    (398, "new CubicEase", "        var eIn = FxKit.EaseIn();"),
    (399, "new CubicEase", "        var eOut = FxKit.EaseOut();"),
    (402, "FLIP_HALF_MS", "        sk.KeyFrames.Add(new EasingDoubleKeyFrame(0, FxKit.At(wait + FxKit.MsFlipHalf), eIn));"),
    (403, "FLIP_HALF_MS", "        sk.KeyFrames.Add(new EasingDoubleKeyFrame(1, FxKit.At(wait + FxKit.MsFlipHalf * 2), eOut));"),
    # 按下
    (439, "PRESS_SCALE = 0.88", "    // PRESS_SCALE 已搬进 FxKit.ScalePress(0.88)"),
    (442, "PRESS_DIM = 0.5", "    // PRESS_DIM 已搬进 FxKit.DimPress(0.5)"),
    (492, "1.14", "    void FxSet(string id, Border b, double scale = FxKit.ScalePop, bool down = false)"),
    (511, "PRESS_SCALE", "    /// 按下/松开的缩放。按下时缩到比常规还小的 FxKit.ScalePress,"),
    # FxDown
    (520, "EasingMode.EaseOut", EASE),
    (522, "80 : 190", "        var span = FxKit.Ms(down ? FxKit.MsDown : FxKit.MsUp);"),
    (523, "PRESS_SCALE", "        double to = down ? FxKit.ScalePress : scale;"),
    # FxPop
    (560, "EasingMode.EaseOut", EASE),
    (564, "1.07", "            sk.KeyFrames.Add(new EasingDoubleKeyFrame(scale * FxKit.PopOver,"),
    (565, "85", "                FxKit.At(FxKit.PopAt0), ease));"),
    (566, "0.98", "            sk.KeyFrames.Add(new EasingDoubleKeyFrame(scale * FxKit.PopBack,"),
    (567, "155", "                FxKit.At(FxKit.PopAt1)));"),
    (569, "215", "                FxKit.At(FxKit.PopAt2)));"),
    (574, "-4.5", "        rk.KeyFrames.Add(new EasingDoubleKeyFrame(-FxKit.WobDeg,"),
    (575, "80", "            FxKit.At(FxKit.WobAt0), ease));"),
    (576, "3.0", "        rk.KeyFrames.Add(new EasingDoubleKeyFrame(FxKit.WobDeg * FxKit.WobBack1, FxKit.At(FxKit.WobAt1)));"),
    (577, "-1.4", "        rk.KeyFrames.Add(new EasingDoubleKeyFrame(-FxKit.WobDeg * FxKit.WobBack2, FxKit.At(FxKit.WobAt2)));"),
    (578, "290", "        rk.KeyFrames.Add(new EasingDoubleKeyFrame(0, FxKit.At(FxKit.WobAt3)));"),
    # FxRest
    (594, "EasingMode.EaseOut", EASE),
    (595, "150", "        var span = FxKit.Ms(FxKit.MsRest);"),
]

# ---------------- OverlayWindow.xaml.cs ----------------
XAML = [
    (260, "340", "        double ms = on ? FxKit.MsNoteIn : FxKit.MsNoteOut;"),
    (314, "0.45", "        double v = k * FxKit.NoteMixStep + s * FxKit.NoteMixOut;                 // 两者掺一半"),
    (317, "0.28", "        NotePanel.Opacity = _noteOn ? Math.Clamp((p - FxKit.NoteInFrom) / FxKit.NoteInSpan, 0, 1)"),
    (318, "0.72", "                                    : 1 - Math.Clamp(p / FxKit.NoteOutSpan, 0, 1);"),
    (564, "EXPAND_MS = 230", "    // EXPAND_MS 已搬进 FxKit.MsExpand(230)"),
    (668, "EXPAND_MS", "            KeyFade(KeyLayerOn(), fadeKeys < 0.5 ? 1 : 0, fadeKeys, (int)(FxKit.MsExpand * FxKit.KeyFadePart));"),
    (670, "EasingMode.EaseOut", EASE),
    (671, "EXPAND_MS", "        var span = FxKit.Ms(FxKit.MsExpand);"),
    (716, "new CubicEase", "            { EasingFunction = FxKit.EaseOut() });"),
]

# ---------------- OverlayWindow.Layer.cs ----------------
LAYER = [
    (598, "SET_POP = 1.14", "    // SET_POP 已搬进 FxKit.ScalePop(1.14)      // 选中的那一格放大多少(和九键焦点那颗一样)"),
    (600, "CELL_WOBBLE = 4.5", "    // CELL_WOBBLE 已搬进 FxKit.WobDeg(4.5)"),
    (608, "SET_POP", "    static double PopOf(SetPage p, int slot) => 1 + (FxKit.ScalePop - 1) * SET_CELL / TilePx(p, slot);"),
    (611, "CELL_WOBBLE", "    static double WobOf(SetPage p, int slot) => FxKit.WobDeg * SET_CELL / TilePx(p, slot);"),
]

# ---------------- OverlayWindow.Render.cs ----------------
# 这几处的写法在文件里重复出现且替换成一样的结果, 用片段全量替换(带命中次数断言)
RENDER_FRAG = [
    ("PRESS_DIM", "FxKit.DimPress", 5),
    ("PRESS_SCALE", "FxKit.ScalePress", 1),
    ("fxS = 1.14;", "fxS = FxKit.ScalePop;", 2),   # 含 Render.cs:230 那句 double fxS = 1.14;
    ("fxS = 1.12;", "fxS = FxKit.ScaleTile;", 3),
    ("fxS = 1.22;", "fxS = FxKit.ScaleSlide;", 1),
    ("fxS = 1.20;", "fxS = FxKit.ScaleKey26;", 1),
]

FILES = [
    ("OverlayWindow.Fx.cs", FX, []),
    ("OverlayWindow.xaml.cs", XAML, []),
    ("OverlayWindow.Layer.cs", LAYER, []),
    ("OverlayWindow.Render.cs", [], RENDER_FRAG),
    ("OverlayWindow.FxKit.cs", [], []),   # 新文件, 只做换行规范化
]


def load(name):
    p = os.path.join(SRC, name)
    raw = open(p, "rb").read().decode("utf-8")
    n_crcrlf = raw.count("\r\r\n")
    if n_crcrlf:
        sx("%s 里有 %d 个 \\r\\r\\n" % (name, n_crcrlf), raw[:200])
    lines = raw.split("\r\n")
    return lines


def save(name, lines):
    p = os.path.join(SRC, name)
    out = "\r\n".join(lines)
    # 幂等规范成 CRLF: 先把一切换行压成 \n 再统统摊回来, 顺手清掉 \r\r\n
    out = out.replace("\r\n", "\n").replace("\r", "\n").replace("\n", "\r\n")
    open(p, "wb").write(out.encode("utf-8"))


def apply_exact(name, lines, rules, dry):
    got = 0
    for ln, must, new in rules:
        i = ln - 1
        if i < 0 or i >= len(lines):
            sx("%s 第 %d 行不存在(文件共 %d 行)" % (name, ln, len(lines)), "")
        if must not in lines[i]:
            sx("%s 第 %d 行不含 %r" % (name, ln, must), lines[i])
        if lines[i] == new:
            print("  !! %s:%d 已经是目标文本(重复运行过?)" % (name, ln))
        if dry:
            print("  %-28s %4d  %s" % (name, ln, lines[i].strip()[:70]))
            print("  %-28s %4d  -> %s" % ("", ln, new.strip()[:70]))
        else:
            lines[i] = new
        got += 1
    return got


def apply_frag(name, raw_lines, frag, dry):
    lines = raw_lines
    for old, new, cnt in frag:
        n = 0
        for i, L in enumerate(lines):
            if old in L:
                if dry:
                    print("  %-28s 片段 %r x%d" % (name, old, cnt))
                lines[i] = L.replace(old, new)
                n += 1
        if n != cnt:
            sx("%s 里 %r 命中 %d 次, 期望 %d 次" % (name, old, n, cnt), "")
    return lines, sum(c for _, _, c in frag)


def main():
    dry = "--apply" not in sys.argv
    print("== %s ==" % ("预演(加 --apply 才真改)" if dry else "正式改写"))
    total = 0
    for name, exact, frag in FILES:
        lines = load(name)
        if exact:
            total += apply_exact(name, lines, exact, dry)
        if frag:
            lines, n2 = apply_frag(name, lines, frag, dry)
            total += n2
        if not dry:
            save(name, lines)
    print("共处理 %d 处" % total)


if __name__ == "__main__":
    main()
