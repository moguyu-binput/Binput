# -*- coding: utf-8 -*-
r"""fprobe 第 31 组重写(2.1.10 设置入口收敛) + 新增第 32 组(符号页 RB 也要踢动效)。

fprobe/Program.cs 是 **LF**, 别写成 CRLF。
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
P = r"C:\Users\<你>\Binput\tools\fprobe\Program.cs"

s = io.open(P, encoding="utf-8", newline="").read()
if "\r\n" in s:
    raise SystemExit("!! fprobe/Program.cs 竟然是 CRLF, 先确认")
L = s.split("\n")

# 定位老的 31 组: 从 "=== 31)" 那行的上一行注释块开始, 到该块的收尾 "    }"
i0 = next(i for i, x in enumerate(L) if "=== 31)" in x) - 1
assert L[i0].strip().startswith("// ==="), L[i0]
i1 = next(i for i in range(i0, len(L)) if L[i] == "    }")
print("老 31 组: 行 %d..%d (1-based)" % (i0 + 1, i1 + 1))

NEW = r"""
// ==================================================================
// 右摇杆推到"更多"扇区(右下角)停够 700ms -> 唤出全部符号(和 coretest 同一套)。
void OpenPunctHere(InputCore c, ref DateTime t)
{
    double a = -60.0 * Math.PI / 180.0;
    short rx = (short)(Math.Cos(a) * 32767 * 0.9);
    short ry = (short)(Math.Sin(a) * 32767 * 0.9);
    for (int i = 0; i < 9; i++)
    {
        t = t.AddMilliseconds(120);                       // 9 x 120 = 1080ms > 700
        c.Poll(new PadState { Connected = true, Buttons = 0, RX = rx, RY = ry }, t);
    }
    t = t.AddMilliseconds(60);                            // 回中, 轮盘收起
    c.Poll(new PadState { Connected = true, Buttons = 0 }, t);
}

// ==================================================================
Console.WriteLine("\n=== 31) 2.1.10: 设置只从九键首页 / 全键盘进得去 ===");
// 以前哪儿都能按 R3 翻到设置, 于是"开窗口F -> 翻进设置 -> 按 B"退的是背后那摞 F,
// 2.1.9 只好加一整套"设置盖在上面时手柄归设置"的让位逻辑。现在干脆只留两处:
// 那两处**都没有上一层**, "退出来退到哪儿"这个问题就不存在了, 那套逻辑整个退回
// (QuickMode 又变回 QuickOpen 本身)。右下角齿轮也只在这两页画出来。
{
    var c = NewCore(true);
    ResetQuick();
    var t = T0;
    int toggle = 0;
    c.ToggleSettings = () => toggle++;

    // 1) 九键首页 —— 行
    Check("九键首页 -> 能进设置", c.SettingsAllowed, $"Layout={c.Layout}");
    Tap(c, ref t, GamepadInput.R3, 40);
    Check("九键首页 R3 短按 -> 翻到设置", toggle == 1, $"toggle={toggle}");

    // 2) 已经翻过去了就不再是"进得去"(再按 R3 是翻回来, 那是窗口的事)
    c.Settings = true;
    Check("已经在设置里 -> 不算能进", !c.SettingsAllowed, $"Settings={c.Settings}");
    c.Settings = false;

    // 3) 全键盘 —— 行
    c.Layout = Layout.EN26;
    Check("全键盘 -> 能进设置", c.SettingsAllowed, $"Layout={c.Layout}");
    Tap(c, ref t, GamepadInput.R3, 40);
    Check("全键盘 R3 短按 -> 翻到设置", toggle == 2, $"toggle={toggle}");

    // 4) 数字九键 —— 不行(用户点名的两处里没有它)
    c.Layout = Layout.NUM9;
    Check("数字九键 -> 进不去", !c.SettingsAllowed, $"Layout={c.Layout}");
    Tap(c, ref t, GamepadInput.R3, 40);
    Check("数字九键 R3 短按 -> 没反应", toggle == 2, $"toggle={toggle}");
    c.Layout = Layout.CN9;

    // 5) 全部符号那一大页 —— 不行
    OpenPunctHere(c, ref t);
    Check("唤出了全部符号", c.Punct && c.Focus == FocusZone.CandGrid,
          $"Punct={c.Punct}, Focus={c.Focus}");
    Check("全部符号 -> 进不去设置", !c.SettingsAllowed, $"Punct={c.Punct}");
    Tap(c, ref t, GamepadInput.R3, 40);
    Check("全部符号里 R3 短按 -> 没反应", toggle == 2, $"toggle={toggle}");
    Tap(c, ref t, GamepadInput.B, 40);                    // 退出符号页
    Check("按 B 退出了符号页", !c.Punct, $"Punct={c.Punct}");

    // 6) 窗口F 那一摞 —— 不行, 而且 F 自己一点不受影响
    c.QuickOpenPanel();
    SyncF(c);
    ((IQuickUI)c.QuickUI).Deep();
    int depth0 = UI(c).Depth;
    Check("窗口F 里 -> 进不去设置", !c.SettingsAllowed && c.QuickOpen,
          $"open={c.QuickOpen}, depth={UI(c).Depth}");
    Tap(c, ref t, GamepadInput.R3, 40);
    Check("窗口F 里 R3 短按 -> 没反应, F 也没被顺手关掉",
          toggle == 2 && c.QuickOpen && UI(c).Depth == depth0,
          $"toggle={toggle}, depth {depth0}->{UI(c).Depth}");

    // 7) 顺带: 2.1.9 那套让位逻辑退回去了 —— 设置和 F 不可能同时在,
    //    QuickMode 现在就是 QuickOpen 本身。
    c.Settings = true;
    Check("QuickMode 退回 QuickOpen 本身", c.QuickMode == c.QuickOpen,
          $"QuickMode={c.QuickMode}, QuickOpen={c.QuickOpen}");
    c.Settings = false;
}

// ==================================================================
Console.WriteLine("\n=== 32) 2.1.10: 符号页按 RB 也要把右边那块牌踢一脚 ===");
// RB 在符号网格里走的是 HoldRepeatRb 那一条, 它以前**直接调** NextPunctCategory,
// 绕过了 OnShoulder —— 而框外那对小牌的动效(PageFx)是 OnShoulder 头一句发的。
// 结果: 按 LB 左边跳、按 RB 右边不跳, 看着像 RB 失灵, 其实类照换。
{
    var c = NewCore(true);
    ResetQuick();
    var t = T0;
    var fx = new List<int>();
    c.PageFx = d => fx.Add(d);

    OpenPunctHere(c, ref t);
    Check("唤出了全部符号", c.Punct && c.Focus == FocusZone.CandGrid,
          $"Punct={c.Punct}, Focus={c.Focus}");

    string cat0 = c.PunctCatName;
    Tap(c, ref t, GamepadInput.RB, 40);
    Check("RB -> 换到下一类", c.PunctCatName != cat0, $"{cat0} -> {c.PunctCatName}");
    Check("RB -> 右边那块牌收到 +1", fx.Count == 1 && fx[0] == 1, string.Join(",", fx));

    Tap(c, ref t, GamepadInput.LB, 40);
    Check("LB -> 换回上一类", c.PunctCatName == cat0, $"{c.PunctCatName}");
    Check("LB -> 左边那块牌收到 -1", fx.Count == 2 && fx[1] == -1, string.Join(",", fx));
}""".strip("\n").split("\n")

L[i0:i1 + 1] = NEW
io.open(P, "w", encoding="utf-8", newline="").write("\n".join(L))
print("写入 %d 行新断言" % len(NEW))
