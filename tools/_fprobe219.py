# -*- coding: utf-8 -*-
# fprobe 新增第 31 组: 2.1.9 "从窗口F 翻进设置" 之后, 手柄归设置, 背后那摞 F 一次都不动。
#
# tools/fprobe/Program.cs 是 **LF**(不是 CRLF) —— 别在这儿转换行。
import io, os, sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = os.path.dirname(os.path.abspath(__file__))
P = os.path.join(ROOT, "fprobe", "Program.cs")
s = io.open(P, encoding="utf-8", newline="").read()
assert "\r" not in s, "fprobe/Program.cs 不是纯 LF"

if "31) 2.1.9" in s:
    raise SystemExit("!! 第 31 组已经写过了")

BLOCK = '''
    // ==================================================================
    Console.WriteLine("\\n=== 31) 2.1.9: 从窗口F 翻进设置 —— 手柄归设置, 背后那摞 F 一次都不动 ===");
    // 症状: 开窗口F -> 按 R3 翻到设置 -> 按一下 B, 退的却是**背后那一摞 F**
    // (再按一下就把 F 整个关了), 而屏幕上明明摆的是设置的九宫格 —— 手和眼对不上。
    // 规矩是"眼睛看着哪一面, 手就动哪一面": 设置盖在上面时 B / A / 十字键 /
    // LB RB 全归设置; 退到设置首页再按一下才翻回正面, 那时候 F 还停在进设置之前
    // 那一层(列表 / 深层 / 绑数字), 一层没退过 —— 这就是用户要的"从哪儿进设置,
    // 退出来回哪儿"。
    {
        var c = NewCore(true);
        ResetQuick();
        var t = T0;

        // 打开窗口F 并钻到深层 —— 停在"列表"那一层验不出"退了没有", 深一层才验得出
        c.QuickOpenPanel();
        SyncF(c);
        ((IQuickUI)c.QuickUI).Deep();
        Check("先开 F 并进到深层", c.QuickOpen && UI(c).Depth == 2, $"depth={UI(c).Depth}");

        // 翻到设置那一面(真程序里是 App 每帧同步的: _core.Settings = _win.Flipped)
        c.Settings = true;
        Check("设置盖在上面时 F 不算占着屏幕", !c.QuickMode, $"QuickMode={c.QuickMode}");

        int nav = 0, navH = 0, sPage = 0, sPick = 0, toggle = 0;
        var fx = new List<int>();
        c.SettingsNav = d => nav++;
        c.SettingsNavH = d => navH++;
        c.SettingsPage = d => sPage++;
        c.SettingsPick = () => sPick++;
        c.ToggleSettings = () => toggle++;
        c.PageFx = d => fx.Add(d);
        c.SettingsDepth = () => 1;                       // 设置停在首页

        int pops0 = UI(c).Pops, picks0 = UI(c).Picks, pages0 = UI(c).Pages;

        // 1) B: 在设置首页 -> 直接翻回正面。F 一层都不许退, 更不许被关掉
        Tap(c, ref t, GamepadInput.B, 40);
        Check("设置里按 B -> 翻回正面", toggle == 1, $"toggle={toggle}");
        Check("背后的 F 一层没退、也没被关掉",
              UI(c).Pops == pops0 && c.QuickOpen && UI(c).Depth == 2,
              $"pops {pops0}->{UI(c).Pops}, open={c.QuickOpen}, depth={UI(c).Depth}");

        // 2) A: 归设置那一格, 不许落到 F 上(以前走的是 HoldQuickA)
        Tap(c, ref t, GamepadInput.A, 40);
        Check("设置里按 A -> 走设置那一格", sPick == 1 && UI(c).Picks == picks0,
              $"sPick={sPick}, F 的 picks {picks0}->{UI(c).Picks}");

        // 3) LB / RB: 翻的是设置的页, 并顺带把框外那对小牌的动效踢一脚
        Tap(c, ref t, GamepadInput.LB, 40);
        Tap(c, ref t, GamepadInput.RB, 40);
        Check("设置里按 LB / RB -> 翻设置那一摞",
              sPage == 2 && UI(c).Pages == pages0,
              $"sPage={sPage}, F 的 pages {pages0}->{UI(c).Pages}");
        Check("翻页动效收到两下(-1 / +1)", fx.Count == 2 && fx[0] == -1 && fx[1] == 1,
              string.Join(",", fx));

        // 4) 十字键: 挪的是设置的光标
        Tap(c, ref t, GamepadInput.DPAD_DOWN, 40);
        Check("设置里按十字键 -> 挪设置的光标", nav == 1, $"nav={nav}, navH={navH}");

        // 5) 设置深处: B 按下先攒着(给长按留位置), 松手退一层 —— 同样不许碰到 F
        c.SettingsDepth = () => 2;
        c.SettingsBack = toRoot => true;                 // 设置那边退得动
        Tap(c, ref t, GamepadInput.B, 40);
        Check("设置深处按 B -> 退的是设置那一层",
              UI(c).Pops == pops0 && c.QuickOpen && UI(c).Depth == 2,
              $"pops={UI(c).Pops}, depth={UI(c).Depth}");
    }
'''

ANCHOR = '''    }
}
finally
{
    try { Directory.Delete(qdir, true); } catch { }
'''
if s.count(ANCHOR) != 1:
    raise SystemExit("!! finally 锚点出现 %d 次" % s.count(ANCHOR))

s = s.replace(ANCHOR, "    }\n" + BLOCK.lstrip("\n") + "}\nfinally\n{\n    try { Directory.Delete(qdir, true); } catch { }\n", 1)
assert "\r" not in s
io.open(P, "w", encoding="utf-8", newline="").write(s)
print("第 31 组已写入 fprobe/Program.cs (%d 行)" % s.count("\n"))
