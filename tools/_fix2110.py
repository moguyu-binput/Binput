# -*- coding: utf-8 -*-
r"""2.1.10 源码补丁。

① 设置入口收敛: 只有九键首页 / 全键盘两处能按 R3 进设置, 齿轮也只在这两页露脸;
   顺手把 2.1.9 那句 QuickMode = QuickOpen && !Settings 退回 QuickOpen
   (设置和窗口F 不可能同时在, 那个扣减没用了)。
② RB 在"全部符号"页也要踢一下翻页动效 —— 它以前抄近路直接调了 NextPunctCategory。
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
SRC = r"C:\Users\<你>\Binput"


def crlf(lines):
    return "\r\n".join(lines)


def edit(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding="utf-8", newline="").read()
    if "\r\n" not in s:
        raise SystemExit("!! %s 不是 CRLF" % name)
    for old, new, tag in pairs:
        if old not in s:
            raise SystemExit("!! [%s] 匹配不上:\n%s" % (tag, old[:400]))
        if s.count(old) != 1:
            raise SystemExit("!! [%s] 匹配到 %d 处, 不敢改" % (tag, s.count(old)))
        s = s.replace(old, new, 1)
        print("  ok  %s" % tag)
    if s.count("\r\r\n"):
        raise SystemExit("!! %s 写出 \\r\\r\\n" % name)
    if s.count("\n") != s.count("\r\n"):
        raise SystemExit("!! %s 混进了裸 LF" % name)
    io.open(p, "w", encoding="utf-8", newline="").write(s)
    print("%s 写完 (%d 字节)" % (name, len(s.encode("utf-8"))))


# ---------------- InputCore.cs (CRLF) ----------------
edit("InputCore.cs", [

    # 1) QuickMode 退回 QuickOpen, 并在旁边立 SettingsAllowed
    (crlf([
        '    /// <summary>',
        '    /// F 占着屏幕 —— 这时候九键 / 候选那些都不该再响应。',
        '    /// <para>【2.1.9: 翻到设置面时 F 得让位】F 开着照样能按 R3 翻到设置, 那时候',
        '    /// 屏幕上摆的是**设置的九宫格**, 手柄该伺候设置。以前这一句只看 QuickOpen,',
        '    /// 于是"从窗口F 进设置、按一下 B"退的是**背后那一摞 F**(F 整个关掉),',
        '    /// A / 十字键 / LB RB 也一股脑跑到 F 上去了 —— 眼睛和手对不上。</para>',
        '    /// <para>想说"F 开着(不管有没有被设置盖住)"的地方请直接用',
        '    /// <see cref="QuickOpen"/> —— 典型是界面收起时把 F 一并关掉那两处。</para>',
        '    /// </summary>',
        '    public bool QuickMode => QuickOpen && !Settings;',
    ]),
     crlf([
        '    /// <summary>F 占着屏幕 —— 这时候九键 / 候选那些都不该再响应。</summary>',
        '    /// <para>【2.1.10: 又变回 QuickOpen 本身了】2.1.9 那句 && !Settings 是给',
        '    /// "开着窗口F 再翻进设置"准备的 —— 那种叠法已经不让发生了(见',
        '    /// <see cref="SettingsAllowed"/>), 设置和 F 不可能同时在, 那个扣减也就没用了。</para>',
        '    public bool QuickMode => QuickOpen;',
        '',
        '    /// <summary>',
        '    /// 【2.1.10】这一帧**能不能翻到设置面** —— 只有两处行: **九键首页**和',
        '    /// **全键盘**。其余一律不行: 全部符号那一大页、窗口F 那一摞(列表 /',
        '    /// 深层 / 绑数字 / 录快捷键)、数字九键… 统统按了没反应。',
        '    /// <para>为什么要收: 哪儿都能进设置, 就总得回答"退出来退到哪儿" ——',
        '    /// 2.1.9 那一整套"设置盖在上面时手柄归设置"的让位逻辑就是这么来的,',
        '    /// 绕了一大圈。不如一开始就只留两处, 进得去的界面都是**没有上一层**的,',
        '    /// 那套让位逻辑整个可以不要。</para>',
        '    /// <para>右下角那个齿轮也按它显隐(见 OverlayWindow.Render): 不画出来,',
        '    /// 鼠标也点不到, 不会出现"有个角在那儿按下去却不动"。</para>',
        '    /// </summary>',
        '    public bool SettingsAllowed =>',
        '        !Settings && !QuickOpen && !Punct',
        '        && (Layout == Layout.CN9 || Layout == Layout.EN26);',
     ]),
     "QuickMode 退回 + SettingsAllowed"),

    # 2) R3 短按加闸
    (crlf([
        '        if (!r3 && _prevR3 && !Resizing && !_r3WithL3',
        '            && (now - _r3At).TotalMilliseconds < R3_HOLD_MS)',
        '        {',
        '            ToggleSettings?.Invoke();',
        '            Dirty = true;',
        '        }',
    ]),
     crlf([
        '        if (!r3 && _prevR3 && !Resizing && !_r3WithL3',
        '            && (now - _r3At).TotalMilliseconds < R3_HOLD_MS)',
        '        {',
        '            // 【2.1.10: 只有九键首页 / 全键盘这两处翻得过去】',
        '            // 别的界面按了就是没反应; 齿轮那会儿也是收着的(见 SettingsAllowed),',
        '            // 所以"看得见的角"和"真能进的页"是一致的。',
        '            if (SettingsAllowed) { ToggleSettings?.Invoke(); Dirty = true; }',
        '        }',
     ]),
     "R3 短按加闸"),

    # 3) RB 在符号网格: 改走 OnShoulder, 好把右边那块牌踢一脚
    (crlf([
        '        if (Punct && Focus == FocusZone.CandGrid)',
        '        {',
        '            if (!_rbHeld) { _rbHeld = true; NextPunctCategory(+1); }',
        '            return;',
        '        }',
    ]),
     crlf([
        '        if (Punct && Focus == FocusZone.CandGrid)',
        '        {',
        '            // 【2.1.10: 走 OnShoulder, 别再抄近路】OnShoulder 头一件事就是',
        '            // 把框外那块 RB▶ 小牌踢一脚(PageFx)。以前这里直接调',
        '            // NextPunctCategory, 于是符号页里按 RB 类照换、**右边那块牌不跳**,',
        '            // 看着像 RB 失灵 —— 其实是漏了一次通知。',
        '            if (!_rbHeld) { _rbHeld = true; OnShoulder(+1); }',
        '            return;',
        '        }',
     ]),
     "RB 走 OnShoulder"),

    # 4) 几处 2.1.9 留下的注释: 现在 QuickMode == QuickOpen, 不用再挑
    (crlf([
        '        // 【2.1.9: 设置盖在上面 -> A 归设置】QuickMode 已经扣掉了设置面,',
        '        // 于是这一条自动落到 HoldRepeatA, 由它里面那个 Settings 分支接住',
        '        // (A = 确认光标这一项)。以前走的是 HoldQuickA, 按 A 动的是背后的 F。',
        '        if (QuickMode) HoldQuickA(btn, now);',
        '        else HoldRepeatA(btn, now);',
        '        // 这一条要的是"F 开着就别跑", 跟盖没盖住无关 —— 用 QuickOpen。',
        '        if (!QuickOpen) TickQuickHold(btn, now);',
    ]),
     crlf([
        '        // 窗口F 开着的时候 A 是"选中这一格", 不是打字。',
        '        // (设置面和 F 不会同时在 —— 见 SettingsAllowed, 所以这里不用挑判据。)',
        '        if (QuickMode) HoldQuickA(btn, now);',
        '        else HoldRepeatA(btn, now);',
        '        if (!QuickMode) TickQuickHold(btn, now);',
     ]),
     "A / 长按注释"),

    (crlf([
        '            // 【要的是 QuickOpen, 不是 QuickMode】"F 开着"就不能拿 L3 去出 0 ——',
        '            // 跟设置有没有盖在上面无关, 盖着的时候更不该出。',
        '            else if (Layout == Layout.NUM9 && !r3 && !QuickOpen)',
    ]),
     crlf([
        '            // "F 开着"就不能拿 L3 去出 0。',
        '            else if (Layout == Layout.NUM9 && !r3 && !QuickOpen)',
     ]),
     "L3 出 0 注释"),

    (crlf([
        '        // 【要的是 QuickOpen, 不是 QuickMode】同上: F 开着就不往外发回车,',
        '        // 不能因为翻到了设置面就把这只键放出来。',
        '        if (rt && !_prevRT && !QuickOpen)',
    ]),
     crlf([
        '        // F 开着就不往外发回车。',
        '        if (rt && !_prevRT && !QuickOpen)',
     ]),
     "RT 回车注释"),

    (crlf([
        '        // 【2.1.9: 设置盖在上面时, 翻的是设置那一摞】',
        '        // 以前窗口F 那一条排在前面, 于是"从 F 进设置、按 LB"翻的是**背后那一摞 F**。',
        '        // 设置在上就先伺候设置 —— 这和 B 那条是同一条规矩的两半:',
        '        // 在哪儿进设置, 就从哪儿退回去, 中间的 F 不许被顺手动。',
        '        // 设置页: LB / RB 是翻页(九宫格一页九个, 放不下就翻)。和"更多标点"一个路子。',
    ]),
     crlf([
        '        // 设置页排在最前面: 和 B 那条是同一条规矩的两半 —— 眼睛看着哪一面,',
        '        // 手就动哪一面。2.1.10 起设置和窗口F 不可能同时在(见 SettingsAllowed),',
        '        // 这个顺序就是留个保险: 万一将来又有地方叠上去, 也不会退回"翻背后那摞"。',
        '        // 设置页: LB / RB 是翻页(九宫格一页九个, 放不下就翻)。和"更多标点"一个路子。',
     ]),
     "OnShoulder 顺序注释"),

    (crlf([
        '        //',
        '        // 【2.1.9: 这一支必须排在窗口F **前面**】',
        '        // 以前 F 在前面, 于是"先开窗口F、再翻进设置、按一下 B"退的是背后那一摞 F',
        '        // (按两次就把 F 整个关了), 而屏幕上明明摆的是设置的九宫格 —— 手和眼对不上。',
        '        // 规矩是: **眼睛看着哪一面, B 就退哪一面**; 退到设置的首页再按一下才翻回',
        '        // 正面, 翻回去时 F 还停在进设置之前那一层(列表 / 深层 / 绑数字), 一次也不动。',
        '        if (Settings)',
    ]),
     crlf([
        '        //',
        '        // 【这一支排在窗口F 前面】"眼睛看着哪一面, B 就退哪一面"。',
        '        // 2.1.10 起两者不会同时在(见 SettingsAllowed), 顺序留着当保险。',
        '        if (Settings)',
     ]),
     "OnB 顺序注释"),
])

# ---------------- OverlayWindow.xaml.cs (CRLF) ----------------
edit("OverlayWindow.xaml.cs", [

    # 齿轮: 只在能进设置的那两页露脸
    (crlf([
        '    public void Render(InputCore c)',
        '    {',
        '        // 设置页的"悬停说明": 光标(或鼠标)在一行上停够了就浮出来。',
        '        // 放在最前面每帧问一次 —— 背面翻走时它会自己收掉。',
        '        TickSetTip();',
    ]),
     crlf([
        '    public void Render(InputCore c)',
        '    {',
        '        // 【2.1.10: 右下角齿轮只在"能进设置"的那两页露脸】',
        '        // 以前每个界面都有它 —— 哪儿都能翻到设置, 于是得回答"退出来退到哪儿",',
        '        // 2.1.9 那一整套让位逻辑就是这么绕出来的。现在只有九键首页和全键盘',
        '        // 进得去, 别的界面索性不画这个角: 看不见也就不会去按, 更不会',
        '        // "按了没反应"。判据见 InputCore.SettingsAllowed。',
        '        // 翻到背面那一瞬间 Settings 已经是 true 了, 所以这里把"正在设置里"',
        '        // 也算作留着 —— 不然齿轮会在翻面动画开始前先"啪"地消失一下。',
        '        GearBtn.Visibility = (c.Settings || c.SettingsAllowed)',
        '                             ? Visibility.Visible : Visibility.Collapsed;',
        '',
        '        // 设置页的"悬停说明": 光标(或鼠标)在一行上停够了就浮出来。',
        '        // 放在最前面每帧问一次 —— 背面翻走时它会自己收掉。',
        '        TickSetTip();',
     ]),
     "齿轮按 SettingsAllowed 显隐"),
])

print("\n搞定")
