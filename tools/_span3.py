# -*- coding: utf-8 -*-
"""2.1.7 Span 重构 第三批: 窗口F 深层的长条改成 Span 声明 + 刷新时作废摆法缓存。"""
import io

P = r"C:\Users\<你>\Binput\OverlayWindow.xaml.cs"
s = io.open(P, encoding="utf-8", newline="").read()
assert "\r\r\n" not in s and s.count("\n") == s.count("\r\n")
L = s.split("\r\n")
n = 0


def find(sub, start=0):
    for i in range(start, len(L) - len(sub) + 1):
        if L[i:i + len(sub)] == sub:
            return i
    raise SystemExit("!! 找不到:\n" + "\n".join(sub))


def rep(old, new, tag):
    global n
    i = find(old)
    L[i:i + len(old)] = new
    n += 1
    print("  ok  %s (第 %d 行)" % (tag, i + 1))


# ---------- 窗口F 深层: Foot -> 第七格 + Layout ----------
rep(["        Title = InputCore.Q_LAYER_DEEP,",
     "        Nine = true,",
     "        Rows = 2,                        // 第三行让给底下那条长条",
     "        Foot = () => QuickBar(_qSlot),"],
    ["        Title = InputCore.Q_LAYER_DEEP,",
     "        Nine = true,",
     "        Rows = 2,                        // 第三行让给底下那条长条",
     "        // **第七格横跨整行** = \"7 8 9 三格合成一条\"。就这一行声明, 别的",
     "        // (它占哪个矩形、光标怎么落到它上面)全由 SetNav 按这张表推出来。",
     "        Layout = () => new List<Span>",
     "        {",
     "            new Span(0, 0), new Span(0, 1), new Span(0, 2),",
     "            new Span(1, 0), new Span(1, 1), new Span(1, 2),",
     "            new Span(2, 0, 1, 3),               // 7 8 9 合成的那条",
     "        },"],
    "QuickDeepPage Layout")

rep(["                null,                                     // 6 号位: 空",
     "            };",
     "        },",
     "    };"],
    ["                null,                                     // 6 号位: 空",
     "                // 7 号位: 底下那条长条(横跨整行), 摆的是 QuickBar 造出来的控件",
     "                SetNav.Block(() => QuickBar(_qSlot)),",
     "            };",
     "        },",
     "    };"],
    "QuickDeepPage 第七格")

# ---------- 格子表重搭了, 摆法缓存要作废 ----------
rep(["        var page = h.Top;",
     "        page.Cells = page.Build();",
     "        Clamp(page);"],
    ["        var page = h.Top;",
     "        page.Cells = page.Build();",
     "        page.Invalidate();              // 格子表变了, 摆法要重算",
     "        Clamp(page);"],
    "ShowPage")

rep(["        p.Cells = p.Build();",
     "        Clamp(p);"],
    ["        p.Cells = p.Build();",
     "        p.Invalidate();                 // 同上",
     "        Clamp(p);"],
    "RefreshPage")

# ---------- 按 A: 啥也不干的格子连重画都别起 ----------
rep(["        var cell = CellAt(p, p.Sel);",
     "        if (cell == null || cell.IsOff()) return;",
     "        var child = cell.Child?.Invoke();"],
    ["        var cell = CellAt(p, p.Sel);",
     "        if (cell == null || cell.IsOff()) return;",
     "        // 啥也不干的格子(空位、只给人看的长条): 按了就是没按, **连重画都别起** ——",
     "        // 起了的话按一下 A 整页会闪一下, 看着像真干了点什么。",
     "        if (cell.Act == null && cell.Child == null) return;",
     "        var child = cell.Child?.Invoke();"],
    "LayerActivate")

io.open(P, "w", encoding="utf-8", newline="").write("\r\n".join(L))
t = "\r\n".join(L)
assert "\r\r\n" not in t and t.count("\n") == t.count("\r\n"), "写坏了换行"
print("改了 %d 处; CRLF=%d CRCRLF=%d bareLF=%d"
      % (n, t.count("\r\n"), t.count("\r\r\n"), t.count("\n") - t.count("\r\n")))
