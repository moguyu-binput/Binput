# -*- coding: utf-8 -*-
"""窗口F 那一段改成走真实入口(Render + QuickOpen)。"""
import sys

P = "tools/uiprobe/Program.cs"
b = open(P, "rb").read()

OLD = '''		// 3) 窗口F(快捷短语): 列表层 -> 深层(底下那条长条是 352x112 的胶囊)
		Call("ShowSide", false);
		Pump(900);
		typeof(OverlayWindow).GetField("_qkOn", BF).SetValue(_w, true);
		Call("BuildQuickSide", 0.0, 0.0);
		Pump(700);
'''

NEW = '''		// 3) 窗口F(快捷短语): 列表层 -> 深层(底下那条长条是 352x112 的胶囊)
		// 【走真实入口】QuickOpen 那一帧由 Render 自己把这一摞搭起来(见那里);
		// 直接调 BuildQuickSide 的话 QuickStage 还收着, 格子根本量不到尺寸。
		Call("ShowSide", false);
		Pump(900);
		InputCore q = new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			QuickOpen = true
		};
		_w.Render(q);
		Pump(700);
'''

o = OLD.replace("\n", "\r\n").encode("utf-8")
n = NEW.replace("\n", "\r\n").encode("utf-8")
if b.count(o) != 1:
    print("FAIL 命中 %d" % b.count(o))
    sys.exit(1)
b = b.replace(o, n)

OLD2 = '''		// 4) 更多标点(符号网格那 40 格)
		typeof(OverlayWindow).GetField("_qkOn", BF).SetValue(_w, false);
'''
NEW2 = '''		// 4) 更多标点(符号网格那 40 格)
'''
o = OLD2.replace("\n", "\r\n").encode("utf-8")
n = NEW2.replace("\n", "\r\n").encode("utf-8")
if b.count(o) != 1:
    print("FAIL2 命中 %d" % b.count(o))
    sys.exit(1)
b = b.replace(o, n)

assert b.count(b"\r\r\n") == 0
open(P, "wb").write(b)
print("ok")
