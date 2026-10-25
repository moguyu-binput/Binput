# -*- coding: utf-8 -*-
"""uiprobe 加一组「圆形风格 · 深层页面」断言。二进制读写, 保 CRLF。"""
import sys

P = "tools/uiprobe/Program.cs"
b = open(P, "rb").read()

METHOD = '''
	// ---- 圆形风格: **深层**那些"点进去才现搭"的格子 ----
	//
	// 【用户报的就是这一组】设置 / 快捷短语 / 更多标点这些格子是**进一层现搭一批新的**
	// (见 FillPanel), 圆形风格下还是方的 —— 偶尔变圆, 是因为撞上一次 Settle
	// (换缩放 / 翻面)才被刷到。根因: 圆角按"实测量到的短边"算, 而格子造出来那一刻
	// 还没排版(ActualWidth 是 0), 于是留着方块那一档, 搭完又没人再刷它。
	// 修法是每颗键尺寸一落地就自己刷一次(见 ShapeBase 里那个 SizeChanged 钩子)。
	// 这一组验的是"刷到了没有", coretest 【35】验的只是"该算成多少"。
	private static void KeyRoundDeep()
	{
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Plain
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		_w.KeyShape = KeyShape.Round;
		Pump(200);
		object setEl = typeof(OverlayWindow).GetField("_set", BF).GetValue(_w);
		Type setTy = setEl.GetType();
		object qkEl = typeof(OverlayWindow).GetField("_qk", BF).GetValue(_w);
		Type qkTy = qkEl.GetType();

		// 1) 设置首页
		Call("ShowSide", true);
		Pump(900);
		List<Border> bs = (List<Border>)setTy.GetField("Boxes", BF).GetValue(setEl);
		int n = 0;
		string bad = BadRound(bs, out n);
		Check("设置首页: 每一格都是圆的(圆角 = 短边一半)", n > 0 && bad.Length == 0, $"(验了 {n} 格{bad})");

		// 2) 钻进设置的第二层 —— 那一批格子是**进了才现搭**的(用户报的就是它)
		bool deep = false;
		for (int i = 0; i < 9 && !deep; i++)
		{
			_w.SettingsActivate();
			Pump(700);
			deep = ((System.Collections.IList)setTy.GetField("Pages", BF).GetValue(setEl)).Count > 1;
			if (!deep)
			{
				_w.SettingsNavH(1);
				Pump(150);
			}
		}
		if (!deep)
		{
			Check("钻进了设置的第二层(才有得验)", false, "(九格都试过, 一层也没进去)");
		}
		else
		{
			bs = (List<Border>)setTy.GetField("Boxes", BF).GetValue(setEl);
			bad = BadRound(bs, out n);
			Check("设置第二层: 现搭的格子也是圆的", n > 0 && bad.Length == 0, $"(验了 {n} 格{bad})");
		}

		// 3) 窗口F(快捷短语): 列表层 -> 深层(底下那条长条是 352x112 的胶囊)
		Call("ShowSide", false);
		Pump(900);
		typeof(OverlayWindow).GetField("_qkOn", BF).SetValue(_w, true);
		Call("BuildQuickSide", 0.0, 0.0);
		Pump(700);
		bs = (List<Border>)qkTy.GetField("Boxes", BF).GetValue(qkEl);
		bad = BadRound(bs, out n);
		Check("快捷短语列表层: 每一格都是圆的", n > 0 && bad.Length == 0, $"(验了 {n} 格{bad})");
		_w.QuickDeep();
		Pump(700);
		bs = (List<Border>)qkTy.GetField("Boxes", BF).GetValue(qkEl);
		bad = BadRound(bs, out n);
		Check("快捷短语深层: 现搭的格子也是圆的", n > 0 && bad.Length == 0, $"(验了 {n} 格{bad})");

		// 4) 更多标点(符号网格那 40 格)
		typeof(OverlayWindow).GetField("_qkOn", BF).SetValue(_w, false);
		InputCore c = new InputCore
		{
			Layout = Layout.CN9,
			Focus = FocusZone.CandGrid,
			Punct = true,
			Visible = true
		};
		_w.Render(c);
		Pump(700);
		Border[] grid = (Border[])typeof(OverlayWindow).GetField("_grid", BF).GetValue(_w);
		bad = BadRound(grid, out n);
		Check("更多标点(符号网格): 每一格都是圆的", n > 0 && bad.Length == 0, $"(验了 {n} 格{bad})");
		_w.Close();
	}

	// 这一摞格子里"圆角不等于短边一半"的有哪些 —— 返回一行给人看的字(空 = 全都对)。
	private static string BadRound(System.Collections.Generic.IEnumerable<Border> bs, out int n)
	{
		n = 0;
		List<string> bad = new List<string>();
		foreach (Border b in bs)
		{
			if (b == null || b.Visibility != Visibility.Visible) continue;
			double s = Math.Min(b.ActualWidth, b.ActualHeight);
			if (s < 1.0) continue;
			n++;
			if (Math.Abs(b.CornerRadius.TopLeft - s / 2.0) > 0.6)
			{
				bad.Add($"{b.ActualWidth:0.#}x{b.ActualHeight:0.#} 圆角 {b.CornerRadius.TopLeft:0.#} 应为 {s / 2.0:0.#}");
			}
		}
		if (bad.Count == 0) return "";
		return "; " + string.Join("; ", bad);
	}
'''

SCENE_OLD = '\t\tScene("圆形键", KeyRound);\n'
SCENE_NEW = '\t\tScene("圆形键", KeyRound);\n\t\tScene("圆形键·深层", KeyRoundDeep);\n'
ANCHOR = "\tprivate static void RelSnapshot()\n"


def enc(s):
    return s.replace("\n", "\r\n").encode("utf-8")


if b.count(enc(SCENE_OLD)) != 1:
    print("FAIL scene 锚点命中 %d" % b.count(enc(SCENE_OLD)))
    sys.exit(1)
b = b.replace(enc(SCENE_OLD), enc(SCENE_NEW))

if b.count(enc(ANCHOR)) != 1:
    print("FAIL 方法锚点命中 %d" % b.count(enc(ANCHOR)))
    sys.exit(1)
b = b.replace(enc(ANCHOR), enc(METHOD + "\n") + enc(ANCHOR))

assert b.count(b"\r\r\n") == 0
open(P, "wb").write(b)
print("ok")
