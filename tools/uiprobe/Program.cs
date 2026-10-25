using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Binput;

// 【这一份是 2026-10-12 从编译好的 uiprobe.dll 反编译救回来的 —— 原文件被一次
//  误操作清空了(tools/ 不在 git 里, 没有别的副本)。逻辑、断言、输出一字不差,
//  但**原来的注释全丢了**: 反编译只能还原代码, 还原不了注释。
//  于是下面这份比原先长(3800 行 vs 2200 行)、缩进是制表符、也没有那些
//  "为什么这么写"的说明。以后改到哪一块, 顺手把那块的说明补回去。
//  教训: 动这种"没有版本控制的大文件", 先 cp 一份再改。】

namespace Uiprobe;

internal static class Program
{
	private record Snap(double Left, double Top, double W, double H, double FaceX, double FaceY);

	private const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private const BindingFlags BFS = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	private const int ROUNDS = 3;

	private const double TOL_DRIFT = 2.0;

	private const double TOL_FACE = 12.0;

	private static int _fail;

	private static OverlayWindow _w;

	private static void Check(string what, bool ok, string detail = "")
	{
		Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}{((detail.Length > 0) ? ("  " + detail) : "")}");
		if (!ok)
		{
			_fail++;
		}
	}

	private static void Pump(int ms)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		DateTime dateTime = DateTime.Now.AddMilliseconds(ms);
		while (DateTime.Now < dateTime)
		{
			DispatcherFrame f = new DispatcherFrame();
			((DispatcherObject)_w).Dispatcher.BeginInvoke((DispatcherPriority)4, (Delegate)(Action)delegate
			{
				f.Continue = false;
			});
			Dispatcher.PushFrame(f);
			Thread.Sleep(16);
		}
	}

	private static object Call(string name, params object[] args)
	{
		MethodInfo methodInfo = typeof(OverlayWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingMethodException("OverlayWindow 上没有方法 " + name);
		ParameterInfo[] parameters = methodInfo.GetParameters();
		object[] array = new object[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			if (i < args.Length)
			{
				array[i] = args[i];
				continue;
			}
			if (!parameters[i].HasDefaultValue)
			{
				throw new ArgumentException($"{name}: 第 {i + 1} 个参数 {parameters[i].Name} 没默认值, 调用方只给了 {args.Length} 个");
			}
			array[i] = parameters[i].DefaultValue;
		}
		return methodInfo.Invoke(_w, array);
	}

	private static Snap Grab()
	{
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		FrameworkElement frameworkElement = (FrameworkElement)typeof(OverlayWindow).GetField("Back", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		if (frameworkElement.Visibility != 0)
		{
			frameworkElement = (FrameworkElement)typeof(OverlayWindow).GetField("Root", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		}
		Point val = frameworkElement.PointToScreen(new Point(0.0, 0.0));
		return new Snap(_w.Left, _w.Top, _w.ActualWidth, _w.ActualHeight, val.X + frameworkElement.ActualWidth / 2.0, val.Y + frameworkElement.ActualHeight / 2.0);
	}

	private static void PinyinBg()
	{
		Console.WriteLine("=== 拼音行的底 ===");
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
		Border[] py = (Border[])typeof(OverlayWindow).GetField("_py", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		object value = typeof(OverlayWindow).GetField("Active", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
		InputCore c = new InputCore
		{
			Layout = Layout.CN9,
			Focus = FocusZone.Keyboard,
			Visible = true
		};
		MethodInfo method = typeof(InputCore).GetMethod("DebugRefreshCands", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		MethodInfo method2 = typeof(InputCore).GetMethod("DebugOpenGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		MethodInfo method3 = typeof(InputCore).GetMethod("DebugCloseGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		string text = "7264";
		foreach (char c2 in text)
		{
			InputCore inputCore = c;
			ReadOnlySpan<char> readOnlySpan = inputCore.Digits;
			char reference = c2;
			inputCore.Digits = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
			method.Invoke(c, null);
		}
		c.PinyinFit = 2;
		Brush now = null;
		string text2 = Show("没展开");
		Brush brush = now;
		method2.Invoke(c, null);
		string text3 = Show("展开着");
		Brush brush2 = now;
		method3.Invoke(c, null);
		string text4 = Show("收起后");
		Brush brush3 = now;
		Check("有拼音 -> 拼音行格子是有底的(不是全透明)", Has(text2) && Has(text4), $"({text2} / {text4})");
		Check("展开全部候选时, 底没被一起收掉", Has(text3), "(展开着 = " + text3 + ")");
		Check("展开时高亮让给网格(行内第一条不再亮)", brush2 != value);
		Check("网格收起后高亮回到行内第一条", brush3 == value && brush == brush3);
		_w.Close();
		static bool Has(string s)
		{
			if (s.Length == 9)
			{
				return !s.StartsWith("#00");
			}
			return false;
		}
		string Show(string tag)
		{
			_w.Render(c);
			Pump(120);
			now = py[0].Background;
			string text5 = ((now is SolidColorBrush solidColorBrush) ? $"#{solidColorBrush.Color.A:X2}{solidColorBrush.Color.R:X2}{solidColorBrush.Color.G:X2}{solidColorBrush.Color.B:X2}" : (((object)now)?.ToString() ?? "null"));
			Console.WriteLine($"  {tag,-7} 拼音行第 1 格 = {text5,-11} 可见={py[0].Visibility}");
			return text5;
		}
	}

	private static void NumZero()
	{
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_021c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_0226: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== 数字层那颗 0 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Plain
			},
			LayoutOf = () => Layout.EN26
		};
		_w.Show();
		Pump(300);
		Border key0 = (Border)typeof(OverlayWindow).GetField("_key0", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement ka = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		InputCore c = new InputCore
		{
			Layout = Layout.EN26,
			Focus = FocusZone.Keyboard,
			Visible = true
		};
		string text = Show("全键盘");
		c.Layout = Layout.NUM9;
		string text2 = Show("按住 LT");
		c.Layout = Layout.EN26;
		string text3 = Show("松开 LT");
		Check("全键盘上不该挂着那颗 0", text == "Collapsed", "(" + text + ")");
		Check("按住 LT 进数字层, 0 出来了", text2 == "Visible", "(" + text2 + ")");
		Check("松开 LT 还原全键盘, 0 跟着收回去", text3 == "Collapsed", "(" + text3 + ")");
		_w.Render(c);
		Pump(200);
		c.Layout = Layout.NUM9;
		_w.Render(c);
		Pump(60);
		Point val = key0.TransformToVisual(_w).Transform(new Point(0.0, 0.0));
		double x = val.X;
		val = ka.TransformToVisual(_w).Transform(new Point(0.0, 0.0));
		double num = val.X + ka.ActualWidth / 2.0;
		double value = x + 38.0 - num;
		Console.WriteLine("  切进第一帧  0号键X=" + x.ToString("0") + " 键盘区中心=" + num.ToString("0") + " 居中偏差=" + value.ToString("0"));
		Check("切进数字层的第一帧, 0 就落在键盘区正中(不在右下角闪)", Math.Abs(value) < 1.0, $"(偏差 = {value:0})");
		_w.Close();
		string Show(string tag)
		{
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_006f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
			_w.Render(c);
			Pump(120);
			_w.Render(c);
			Pump(120);
			string text4 = key0.Visibility.ToString();
			Point val2 = key0.TransformToVisual(_w).Transform(new Point(0.0, 0.0));
			double x2 = val2.X;
			val2 = ka.TransformToVisual(_w).Transform(new Point(0.0, 0.0));
			double num2 = val2.X + ka.ActualWidth / 2.0;
			Console.WriteLine($"  {tag,-9} 0 号键 = {text4,-9} 键盘区宽={ka.Width,5:0} 0号键X={x2,6:0} 键盘区中心={num2,6:0} 居中偏差={x2 + 38.0 - num2,5:0}");
			return text4;
		}
	}

	private static void NumLayerFx()
	{
		Console.WriteLine("=== 按住 LT 进 / 出数字层 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Plain
			},
			LayoutOf = () => Layout.EN26
		};
		_w.Show();
		Pump(300);
		FrameworkElement obj = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		InputCore core = new InputCore
		{
			Layout = Layout.EN26,
			Focus = FocusZone.Keyboard,
			Visible = true
		};
		core.FlipLayout = delegate(Action mid)
		{
			_w.FlipLayout(mid);
		};
		_w.LayoutOf = () => core.Layout;
		_w.Render(core);
		Pump(200);
		double width = obj.Width;
		Console.WriteLine($"  全键盘    布局={core.Layout,-5} 键盘区宽={width,5:0}");
		DateTime now = DateTime.Now;
		core.Poll(new PadState
		{
			Connected = true,
			LeftTrigger = byte.MaxValue
		}, now);
		double width2 = obj.Width;
		Layout layout = core.Layout;
		_w.Render(core);
		Pump(80);
		double width3 = obj.Width;
		_w.Render(core);
		Pump(400);
		double width4 = obj.Width;
		Console.WriteLine($"  按住 LT   布局={layout,-5} 刚按下={width2,5:0} 演到一半={width3,5:0} 演完={width4,5:0}");
		core.Poll(new PadState
		{
			Connected = true,
			LeftTrigger = 0
		}, now.AddSeconds(1.0));
		double width5 = obj.Width;
		_w.Render(core);
		Pump(400);
		double width6 = obj.Width;
		Console.WriteLine($"  松开 LT   布局={core.Layout,-5} 刚松开={width5,5:0} 演完={width6,5:0}");
		Check("按住 LT: 进的是数字层", layout == Layout.NUM9, $"({layout})");
		Check("按住 LT: 宽度是**演**过去的(刚按下还停在 640 附近)", width2 > 600.0, $"(刚按下 = {width2:0})");
		Check("按住 LT: 演出**中途**那一帧卡在 360 和 640 之间(不是瞬闪)", width3 > 370.0 && width3 < 630.0, $"(演到一半 = {width3:0})");
		Check("按住 LT: 演完收到九键的 360", Math.Abs(width4 - 360.0) < 2.0, $"({width4:0})");
		Check("松开 LT: 还原回全键盘", core.Layout == Layout.EN26, $"({core.Layout})");
		Check("松开 LT: 宽度是**演**回去的(刚松开还停在 360 附近)", width5 < 400.0, $"(刚松开 = {width5:0})");
		Check("松开 LT: 演完撑回全键盘的 640", Math.Abs(width6 - 640.0) < 2.0, $"({width6:0})");
		_w.Close();
	}

	private static void ZeroFlicker()
	{
		Console.WriteLine("=== 数字层的 0: 动画全程不许晃 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Plain
			},
			LayoutOf = () => Layout.EN26
		};
		_w.Show();
		Pump(300);
		Border key0 = (Border)typeof(OverlayWindow).GetField("_key0", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement ka = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		InputCore core = new InputCore
		{
			Layout = Layout.EN26,
			Focus = FocusZone.Keyboard,
			Visible = true
		};
		core.FlipLayout = delegate(Action mid)
		{
			_w.FlipLayout(mid);
		};
		_w.LayoutOf = () => core.Layout;
		_w.Render(core);
		Pump(200);
		DateTime now = DateTime.Now;
		core.Poll(new PadState
		{
			Connected = true,
			LeftTrigger = byte.MaxValue
		}, now);
		double num = 1000000000.0;
		double num2 = -1000000000.0;
		double num3 = 0.0;
		for (int i = 0; i < 24; i++)
		{
			_w.Render(core);
			Pump(16);
			double num4 = X() + 38.0 - Mid();
			num = Math.Min(num, num4);
			num2 = Math.Max(num2, num4);
			num3 = Math.Max(num3, Math.Abs(num4));
		}
		Console.WriteLine($"  按住 LT  24 帧里居中偏差 {num:0} ~ {num2:0}, 最大 |偏差| = {num3:0.0}");
		Check("按住 LT 缩窄动画全程, 0 都钉在键盘区正中(不往右闪)", num3 < 1.0, $"(最大偏差 = {num3:0.0})");
		Check("数字层里那颗 0 是露着的", key0.Visibility == Visibility.Visible, $"({key0.Visibility})");
		core.Poll(new PadState
		{
			Connected = true,
			LeftTrigger = 0
		}, now.AddSeconds(1.0));
		_w.Render(core);
		Pump(400);
		Console.WriteLine($"  松开 LT  布局={core.Layout} 0号键={key0.Visibility} 键盘区宽={ka.Width:0}");
		Check("松开 LT 后 0 收掉", key0.Visibility == Visibility.Collapsed, $"({key0.Visibility})");
		_w.Close();
		double Mid()
		{
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			Point val = ka.TransformToVisual(_w).Transform(new Point(0.0, 0.0));
			return val.X + ka.ActualWidth / 2.0;
		}
		double X()
		{
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			Point val2 = key0.TransformToVisual(_w).Transform(new Point(0.0, 0.0));
			return val2.X;
		}
	}

	private static void TierSteps()
	{
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== 收起之后 四档: 三步都能显示全 ===");
		FieldInfo field = typeof(OverlayWindow).GetField("CELL_TITLE_MAXH", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		double num = ((field == null) ? 0.0 : ((double)field.GetValue(null)));
		TextBlock textBlock = new TextBlock();
		textBlock.FontSize = 16.0;
		textBlock.FontWeight = FontWeights.SemiBold;
		textBlock.Text = GameMode.Steps(PasteTier.CopyPasteEnter);
		textBlock.TextWrapping = TextWrapping.Wrap;
		textBlock.TextAlignment = TextAlignment.Center;
		textBlock.Measure(new Size(100.0, double.PositiveInfinity));
		Size desiredSize = textBlock.DesiredSize;
		double height = desiredSize.Height;
		Console.WriteLine($"  三步那格要 {height:0.0} 像素高, 标题框上限 {num:0}");
		Check("「复制 / 粘贴 / 回车」三行装得进格子(第三行不会被切掉)", height > 0.0 && height <= num, $"(要 {height:0.0}, 上限 {num:0})");
		PasteTier[] array = new PasteTier[4]
		{
			PasteTier.CopyOnly,
			PasteTier.CopyPaste,
			PasteTier.CopyPasteEnter,
			PasteTier.FF14
		};
		foreach (PasteTier pasteTier in array)
		{
			int num2 = GameMode.Steps(pasteTier).Split('\n').Length;
			int num3 = pasteTier switch
			{
				PasteTier.CopyOnly => 1, 
				PasteTier.CopyPaste => 2, 
				PasteTier.CopyPasteEnter => 3, 
				PasteTier.FF14 => 3, 
				_ => 0, 
			};
			Check($"档位「{GameMode.Name(pasteTier)}」写上 {num2} 步 = 真做 {num3} 步", num2 == num3, "(" + GameMode.Steps(pasteTier).Replace("\n", " → ") + ")");
		}
	}

	private static void DeliverGate()
	{
		Console.WriteLine("=== 收起之后那几步: 不许被闸门扣住 ===");
		typeof(InputCore).GetProperty("NoSend", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(null, true);
		TextOutput.Hold = () => true;
		int pending = TextOutput.Pending;
		typeof(GameMode).GetMethod("CtrlV", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
		typeof(GameMode).GetMethod("Enter", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, new object[1] { "探针" });
		int pending2 = TextOutput.Pending;
		TextOutput.Hold = null;
		typeof(InputCore).GetProperty("NoSend", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(null, false);
		Console.WriteLine($"  队里 {pending} -> {pending2}");
		Check("粘贴 / 回车这两步不进闸门的队列(不会被扣着一直不发)", pending2 == pending, $"(队里 {pending} -> {pending2})");
	}

	private static void NoteRise()
	{
		Console.WriteLine("=== 预览框升起这一场: 演完撒手 + 补发扣着的键 ===");
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
		_w.Preview = true;
		Pump(200);
		typeof(InputCore).GetProperty("NoSend", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(null, true);
		MethodInfo gate = typeof(OverlayWindow).GetMethod("NoteGate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		TextOutput.Hold = () => (bool)gate.Invoke(_w, null);
		TextOutput.Letter('n');
		int pending = TextOutput.Pending;
		bool flag = (bool)typeof(OverlayWindow).GetField("_noteAnim", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Console.WriteLine($"  框还没升: 队里 {pending}, _noteAnim={flag}");
		Check("框还没升到位的时候, 键被闸门扣在队里", pending > 0 && flag, $"(队里 {pending}, _noteAnim={flag})");
		for (int i = 0; i < 12; i++)
		{
			Pump(120);
		}
		int pending2 = TextOutput.Pending;
		bool flag2 = (bool)typeof(OverlayWindow).GetField("_noteAnim", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		double y = ((TranslateTransform)typeof(OverlayWindow).GetField("_noteTt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w)).Y;
		double opacity = FE("NotePanel").Opacity;
		Console.WriteLine($"  演完: 队里 {pending2}, _noteAnim={flag2}, 位移 Y={y:0.0}, 不透明度={opacity:0.00}");
		Check("这一场演完自己撒手(_noteAnim 归位)", !flag2, $"(_noteAnim={flag2})");
		Check("演完把扣着的键补发出去了(不许一直压在队里)", pending2 == 0, $"(队里还剩 {pending2})");
		Check("演完框停在位(位移归零、完全不透明)", Math.Abs(y) <= 0.5 && opacity > 0.99, $"(Y={y:0.0}, 不透明度={opacity:0.00})");
		TextOutput.Hold = null;
		typeof(InputCore).GetProperty("NoSend", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(null, false);
		_w.Close();
	}

	private static double LbGap(bool fromEn26)
	{
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Plain
			},
			LayoutOf = () => fromEn26 ? Layout.EN26 : Layout.CN9
		};
		_w.Show();
		Pump(300);
		InputCore core = new InputCore
		{
			Layout = (fromEn26 ? Layout.EN26 : Layout.CN9),
			Focus = FocusZone.Keyboard,
			Visible = true
		};
		_w.LayoutOf = () => core.Layout;
		_w.Render(core);
		Pump(200);
		Call("ShowSide", true);
		Pump(400);
		for (int i = 0; i < 6; i++)
		{
			_w.Render(core);
			Pump(50);
		}
		FrameworkElement frameworkElement = (FrameworkElement)typeof(OverlayWindow).GetField("Back", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement frameworkElement2 = (FrameworkElement)typeof(OverlayWindow).GetField("PageSides", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Point val = frameworkElement.TransformToVisual(_w).Transform(new Point(0.0, 0.0));
		double x = val.X;
		val = frameworkElement2.TransformToVisual(_w).Transform(new Point(0.0, 0.0));
		double num = x - val.X;
		Console.WriteLine($"  {(fromEn26 ? "从全键盘进" : "从九键进  ")}: 卡片宽={frameworkElement.ActualWidth,5:0} 牌子层宽={frameworkElement2.ActualWidth,5:0} 左端到牌子层左端={num,5:0}");
		_w.Close();
		return num;
	}

	private static void En26SetWidth()
	{
		Console.WriteLine("=== 从全键盘进设置: 卡片外面那几行不许还停在 640 ===");
		double num = LbGap(fromEn26: false);
		double num2 = LbGap(fromEn26: true);
		Check("从全键盘进设置, LB/RB 到卡片的距离和从九键进一模一样", Math.Abs(num - num2) <= 2.0, $"(九键 {num:0} / 全键盘 {num2:0})");
	}

	private static FrameworkElement FE(string name)
	{
		return (FrameworkElement)typeof(OverlayWindow).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
	}

	private static double Num(string name)
	{
		Type typeFromHandle = typeof(OverlayWindow);
		object obj = typeFromHandle.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(_w) ?? typeFromHandle.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(_w);
		if (obj != null)
		{
			return Convert.ToDouble(obj);
		}
		return double.NaN;
	}

	private static Rect InWin(FrameworkElement e)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		GeneralTransform generalTransform = e.TransformToVisual(_w);
		Point val = generalTransform.Transform(new Point(0.0, 0.0));
		Point val2 = generalTransform.Transform(new Point(e.ActualWidth, e.ActualHeight));
		return new Rect(val, val2);
	}

	private static Point ScrCen(FrameworkElement e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		Rect val = InWin(e);
		return _w.PointToScreen(new Point((val.Left + val.Right) / 2.0, (val.Top + val.Bottom) / 2.0));
	}

	// ---- 按键形状 ----
	//
	// 圆形那一档: 圆角 = 短边的一半 —— 九键 112 见方 -> 正圆(半径 56), 全键盘字母键
	// 38×40 -> 19, 空格 190×40 -> 两头半圆的胶囊(20)。**格子尺寸一个都不动**,
	// 所以这一组只验圆角, 顺手再量一次尺寸确认键盘没变大。
	//
	// 【为什么 coretest 验过了这里还要验】coretest 【35】验的是"圆角该算成多少"
	// 那个函数; 这一组验的是**有没有真的刷到每一颗键上** —— 切形状 -> Settle ->
	// 从真的 Border 上把 CornerRadius 读回来。中间任何一环没接上(忘了刷、刷的
	// 时机不对、尺寸还没量到), 都是"算对了、界面上却还是方的", 只有开窗口才看得见。
	static void KeyRound()
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
		Border[] k9 = (Border[])typeof(OverlayWindow).GetField("_key9", BF).GetValue(_w);

		_w.KeyShape = KeyShape.Square;
		Call("Settle", true);
		Check("方块: 九键还是原来那 10 度圆角", Math.Abs(k9[5].CornerRadius.TopLeft - 10.0) < 0.5, $"(圆角 {k9[5].CornerRadius.TopLeft:0.##})");

		_w.KeyShape = KeyShape.Round;
		Call("Settle", true);
		Check("圆形: 九键 112 见方 -> 半径 56, 是正圆", Math.Abs(k9[5].CornerRadius.TopLeft - 56.0) < 0.5, $"(圆角 {k9[5].CornerRadius.TopLeft:0.##})");
		Check("圆形: 九键量到的尺寸还是 112 —— 直径 = 原来的边长, 键盘没变大", Math.Abs(k9[5].ActualWidth - 112.0) < 1.0 && Math.Abs(k9[5].ActualHeight - 112.0) < 1.0, $"({k9[5].ActualWidth:0.#}×{k9[5].ActualHeight:0.#})");

		_w.KeyShape = KeyShape.Square;
		Call("Settle", true);
		Check("切回方块: 九键回到 10 度圆角, 不留着圆的", Math.Abs(k9[5].CornerRadius.TopLeft - 10.0) < 0.5, $"(圆角 {k9[5].CornerRadius.TopLeft:0.##})");

		// 可爱粉那一趟: 九键是**花瓣贴图**整图铺在键底上的, 圆角裁不到它头上也不该裁
		// —— 圆形风格下它是唯一的例外, 得保持花瓣自己那 18 度圆角。
		_w.KeyShape = KeyShape.Round;
		_w.ApplyTheme(8);          // Themes[8] = 可爱粉(贴纸风)
		Pump(200);
		Check("可爱粉: 九键仍是花瓣的 18 度圆角, 不被改成正圆", Math.Abs(k9[5].CornerRadius.TopLeft - 18.0) < 0.5, $"(圆角 {k9[5].CornerRadius.TopLeft:0.##})");
		_w.ApplyTheme(0);
		Pump(200);
		Check("换回纯色主题: 九键跟着回到正圆 56", Math.Abs(k9[5].CornerRadius.TopLeft - 56.0) < 0.5, $"(圆角 {k9[5].CornerRadius.TopLeft:0.##})");

		// 全键盘那一趟: 字母键和宽键得分开验 —— 宽键做不成圆, 只能是胶囊
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Plain
			},
			LayoutOf = () => Layout.EN26
		};
		_w.Show();
		Pump(300);
		// 全键盘那七排要**画过一遍**才真的排上版 —— 没 Render 的话那一层还是收着的,
		// 键的 ActualWidth 是 0, 圆角就按"量不到尺寸"留着方块那一档(这正是要验的东西)
		_w.Render(new InputCore
		{
			Layout = Layout.EN26,
			Focus = FocusZone.Keyboard,
			Visible = true
		});
		Pump(200);
		Border[][] en = (Border[][])typeof(OverlayWindow).GetField("_en", BF).GetValue(_w);
		Border letter = null, space = null;
		for (int r = 0; r < en.Length; r++)
		{
			if (en[r] == null) continue;
			for (int c = 0; c < en[r].Length; c++)
			{
				KKey k = Keyboard26.At(r, c);
				if (letter == null && k.W == 1.0) letter = en[r][c];
				if (space == null && k.Kind == KKind.Space) space = en[r][c];
			}
		}
		if (letter == null || space == null)
		{
			Check("在全键盘里找得到一颗字母键和空格键", false, "");
			return;
		}
		_w.KeyShape = KeyShape.Square;
		Call("Settle", true);
		Check("方块: 全键盘的键还是原来那 6 度圆角", Math.Abs(letter.CornerRadius.TopLeft - 6.0) < 0.5, $"(圆角 {letter.CornerRadius.TopLeft:0.##})");

		_w.KeyShape = KeyShape.Round;
		Call("Settle", true);
		Check("圆形: 字母键 38×40 -> 按短边取 19", Math.Abs(letter.CornerRadius.TopLeft - 19.0) < 0.5, $"(圆角 {letter.CornerRadius.TopLeft:0.##})");
		Check("圆形: 空格 190×40 -> 胶囊, 半径 20(不是正圆)", Math.Abs(space.CornerRadius.TopLeft - 20.0) < 0.5, $"(圆角 {space.CornerRadius.TopLeft:0.##}, 键 {space.Width:0.#}×{space.Height:0.#})");
	}


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
		bs = (List<Border>)qkTy.GetField("Boxes", BF).GetValue(qkEl);
		bad = BadRound(bs, out n);
		Check("快捷短语列表层: 每一格都是圆的", n > 0 && bad.Length == 0, $"(验了 {n} 格{bad})");
		_w.QuickDeep();
		Pump(700);
		bs = (List<Border>)qkTy.GetField("Boxes", BF).GetValue(qkEl);
		bad = BadRound(bs, out n);
		Check("快捷短语深层: 现搭的格子也是圆的", n > 0 && bad.Length == 0, $"(验了 {n} 格{bad})");

		// 4) 更多标点(符号网格那 40 格)
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

	// ---- 取窗口里那个元素的两个小抄 ----
	// 实例字段(XAML 里 x:Name 生成的那些)用 El, 静态字段(比如 SkinMode)用 Sg。
	private static T El<T>(string name) where T : class
	{
		return (T)typeof(OverlayWindow).GetField(name, BF).GetValue(_w);
	}

	private static T Sg<T>(string name)
	{
		return (T)typeof(OverlayWindow).GetField(name, BFS).GetValue(null);
	}

	// ---- 土豪金(彩蛋皮肤, 下标钉死在「清新」后面那一格) ----
	//
	// 【这几条只有开一个真窗口才验得到】它**没解锁**的时候: 设置 → 皮肤那一页它那格
	// 是空的、硬选也选不中; 解锁之后那一格才写着「土豪金」, 下标才真的落在它身上
	// (要存盘的就是这个数)。位置钉死是有意的 —— 用户要的就是"以后加新皮肤它还在
	// 老地方", 所以头一条守着"它前面那套必须是清新、它自己必须是第 GoldIdx 格"。
	// 四角金饰和方 / 圆两种按键风格同样是七套逻辑探针够不着的(它们压根不建窗口)。
	private static void GoldSkin()
	{
		_w = new OverlayWindow
		{
			Host = new FakeHost { Which = FxStyle.Plain },
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Canvas deco = El<Canvas>("GoldDeco");
		int theme0 = _w.Theme;
		Check("平时: 金饰那一层是收着的", deco.Visibility == Visibility.Collapsed, "");

		// 【位置钉死】清新后面那一格永远是土豪金。以后加新皮肤一律往数组末尾追加,
		// 不许插到它前面: 这个下标是要落盘的, 一挪老存档存的"第 10 套"就指错人了。
		Check("土豪金的位置钉死在「清新」后面那一格",
			OverlayWindow.Themes[OverlayWindow.GoldIdx - 1].Name == "清新"
			&& OverlayWindow.Themes[OverlayWindow.GoldIdx].Name == "土豪金",
			$"(第 {OverlayWindow.GoldIdx - 1} 套「{OverlayWindow.Themes[OverlayWindow.GoldIdx - 1].Name}」"
			+ $" / 第 {OverlayWindow.GoldIdx} 套「{OverlayWindow.Themes[OverlayWindow.GoldIdx].Name}」)");

		// 没解锁: 那一格留空(一排方块里平白缺一块 = "这儿藏着个还没发现的")
		SetPage skin = (SetPage)Call("PageSkin");
		List<SetCell> cells = skin.Build();
		Check("没解锁: 皮肤那一页**总格数不变**, 但土豪金那一格是空的",
			cells.Count == OverlayWindow.Themes.Length && cells[OverlayWindow.GoldIdx] == null,
			$"(摆了 {cells.Count} 项, 第 {OverlayWindow.GoldIdx} 格"
			+ (cells[OverlayWindow.GoldIdx] == null ? "是空的" : "不是空的") + ")");
		_w.ApplyTheme(OverlayWindow.GoldIdx);
		Pump(200);
		Check("没解锁: 硬点土豪金那一套 -> 落回原色(选不中)",
			_w.Theme == 0 && Sg<int>("SkinMode") != 5,
			$"(Theme {_w.Theme}, SkinMode {Sg<int>("SkinMode")})");

		// 解锁: 走 SetGoldOpen —— 只拨内存里那个开关, **不碰存档**(探针不该改真存档)
		Call("SetGoldOpen", true);
		_w.ApplyTheme(OverlayWindow.GoldIdx);
		Pump(300);
		Check("解锁 + 选中: 走的是矢量那一档(SkinMode=5, 不碰三段式贴图)",
			Sg<int>("SkinMode") == 5, $"(SkinMode {Sg<int>("SkinMode")})");
		Check("解锁 + 选中: 四角金饰真的搭出来了",
			deco.Visibility == Visibility.Visible && deco.Children.Count >= 4,
			$"({deco.Children.Count} 个形状)");
		Check("解锁: 主题下标真的落在土豪金上(要存盘的就是这个数)",
			_w.Theme == OverlayWindow.GoldIdx, $"(Theme {_w.Theme})");

		// 元宝: 认的是那三段弧度(翅尖一挑 / 中间一道凹 / 肚子鼓起来), 所以轮廓得是
		// 贝塞尔的 Path —— 拿多边形直线拼, 出来就是一块石头(上一版那个"狗头金")。
		Canvas ingot = deco.Children[0] as Canvas;
		Check("金块已换成金元宝: 第一只金饰走贝塞尔轮廓(不是多边形)",
			ingot != null && ingot.Children.Count >= 4
			&& ingot.Children[0] is System.Windows.Shapes.Path,
			$"(第一只金饰是 {(ingot == null ? "没有" : ingot.Children[0].GetType().Name)})");

		// 解锁之后: 皮肤那一页它那一格不再是空的
		cells = ((SetPage)Call("PageSkin")).Build();
		SetCell gc = cells[OverlayWindow.GoldIdx];
		Check("解锁后: 皮肤那一页土豪金那一格写着「土豪金」",
			gc != null && gc.Title == "土豪金" && cells.Count == OverlayWindow.Themes.Length,
			$"(第 {OverlayWindow.GoldIdx} 格「{gc?.Title}」)");

		// 方 / 圆两种按键风格都直接套上: 它跟清新一样是渐变键底, 圆角仍归 ShapeRound 管
		Border[] k9 = (Border[])typeof(OverlayWindow).GetField("_key9", BF).GetValue(_w);
		_w.KeyShape = KeyShape.Round;
		Call("Settle", true);
		Check("土豪金 + 圆形按键: 九键照样是正圆(半径 56)", Math.Abs(k9[5].CornerRadius.TopLeft - 56.0) < 0.5,
			$"(圆角 {k9[5].CornerRadius.TopLeft:0.##})");
		_w.KeyShape = KeyShape.Square;
		Call("Settle", true);
		// 【土豪金跟清新一样属"浅色那一拨"的壳: 厚边 + 更圆的键角 18】不是 10 ——
		// ShapeBase(_key9[k], light ? 18 : 10) 里那个 light 是 cute || fresh || gold。
		Check("土豪金 + 方形按键: 九键是更圆的 18 度(和清新一拨)", Math.Abs(k9[5].CornerRadius.TopLeft - 18.0) < 0.5,
			$"(圆角 {k9[5].CornerRadius.TopLeft:0.##})");

		// 背面(设置页)也看得见: 皮肤那页就在这儿翻出来的
		Call("ShowSide", true);
		Pump(900);
		Check("翻到背面(设置页): 金饰还在", El<Canvas>("GoldDeco").Visibility == Visibility.Visible, "");

		// 手动挑了别的皮肤 -> 土豪金让位(它只是解锁了, 不是钉住不让换)
		_w.ApplyTheme(theme0);
		Pump(300);
		Check("手动挑回原来那套: 土豪金让位", Sg<int>("SkinMode") != 5,
			$"(SkinMode {Sg<int>("SkinMode")})");
		// 换主题必须连"方块那一档圆角"一起更新(走 ShapeBase), 否则土豪金的 18 度会
		// 留在纯色那套上(2.1.36 踩过: 18 / 10 两套数串)
		Check("挑回纯色那套: 九键跟着回到 10 度圆角(不留土豪金的 18)",
			Math.Abs(k9[5].CornerRadius.TopLeft - 10.0) < 0.5,
			$"(圆角 {k9[5].CornerRadius.TopLeft:0.##})");
		_w.Close();
	}

	// ---- 彩蛋: 连点设置首页正中那个空位 ----
	//
	// 【走真实入口】翻面用 ShowSide, 按 A 用 SettingsActivate —— 直接调 TapEggCenter
	// 等于绕开了"空位按下去到底走哪条路"这件事, 就成了摆设断言(见 uiprobe 两条命)。
	private static void EggTap()
	{
		_w = new OverlayWindow
		{
			Host = new FakeHost { Which = FxStyle.Plain },
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(900);

		object setEl = typeof(OverlayWindow).GetField("_set", BF).GetValue(_w);
		Type setTy = setEl.GetType();
		Border chip = El<Border>("EggChip");
		TextBlock txt = El<TextBlock>("EggHintText");

		// 光标得真停在正中那个空位上(不然下面点的就是别的格子)
		SetPage top = (SetPage)setTy.GetProperty("Top").GetValue(setEl);
		Check("进设置: 光标停在正中那个空位上(才有的可验)", top != null && top.Sel == 4,
			$"(Sel {top?.Sel})");

		// 1) 一下: 什么都不冒
		_w.SettingsActivate();
		Check("连点 1 下: 什么都不冒(提示还收着)", txt.Text.Length == 0, $"(“{txt.Text}”)");

		// 2) 攒到 20 / 40 / 60 / 80: 按序冒那四句
		string[] want = { "累不累啊...", "还在点吗...", "拇指酸了吧...", "没东西的...别点了..." };
		for (int s = 1; s <= 4; s++)
		{
			for (int i = (s == 1) ? 19 : 20; i > 0; i--) _w.SettingsActivate();
			Check($"连点 {s * 20} 下: 冒出「{want[s - 1]}」", txt.Text == want[s - 1], $"(实为“{txt.Text}”)");
		}
		// 3) 攒到 100: 冒「100连打达成！」并且解锁土豪金
		for (int i = 0; i < 20; i++) _w.SettingsActivate();
		Check("连点 100 下: 冒出「100连打达成！」", txt.Text == "100连打达成！", $"(实为“{txt.Text}”)");
		// 100 下 = 永久解锁 + 立刻切过去(要存盘的就是这个下标, 重开还在)
		Check("连点 100 下: 土豪金**解锁**了", (bool)typeof(OverlayWindow).GetField("_goldOpen", BF).GetValue(_w), "");
		Check("连点 100 下: 主题真的切到土豪金(第 GoldIdx 套)",
			_w.Theme == OverlayWindow.GoldIdx && Sg<int>("SkinMode") == 5,
			$"(Theme {_w.Theme}, SkinMode {Sg<int>("SkinMode")})");

		// 4) 那句话真的摆在「滑行」二字那一格上, 且一秒之内看得见
		_w.Render(new InputCore { Layout = Layout.CN9, Visible = true });
		Check("那句话真的亮在状态行上(和「滑行」同一处)", chip.Visibility == Visibility.Visible, "");

		// 5) 停手超过 1 秒 -> 从头再数(要的是"连打", 不是"一共按了多少下")
		// 【显隐是每帧在 Render 里判的】Pump 只走消息循环、不会自己画一帧,
		// 所以等完一秒得自己 Render 一次才问得到"它灭了没有"。
		Pump(1300);
		_w.Render(new InputCore { Layout = Layout.CN9, Visible = true });
		Check("亮够一秒就收(提示自己灭了)", chip.Visibility == Visibility.Collapsed, "");
		for (int i = 0; i < 20; i++) _w.SettingsActivate();
		Check("停手超过 1 秒: 重新从 20 下数起(又是第一句)", txt.Text == want[0], $"(实为“{txt.Text}”)");
		_w.Close();
	}

	// ---- 调窗口大小 / 摆位(2.3.3) ----
	//
	// 以前这一档是"按住 R3"临时进、松手就退 —— 那颗键在摇杆底下, 手正推着摇杆
	// 选键的时候够不着, 而且想边看边挪得一直绷着手指。现在入口是设置 → 键盘 →
	// 「调整窗口」, 进去就一直在那一档里, 按 B 出来。
	// 【走真实入口】翻面用 ShowSide、按 A 用 SettingsActivate, 而且**按名字找那一格**
	// (不写死下标) —— 直接去调那一格的 Act 等于绕开"这一格按下去走哪条路",
	// 就成了摆设断言(见 uiprobe 两条命)。
	private static void ResizeMode()
	{
		Console.WriteLine("=== 调窗口: 设置 → 键盘 → 「调整窗口」 ===");
		FakeHost host = new FakeHost { Which = FxStyle.Plain };
		_w = new OverlayWindow
		{
			Host = host,
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(900);

		object setEl = typeof(OverlayWindow).GetField("_set", BF).GetValue(_w);
		Type setTy = setEl.GetType();
		SetPage home = (SetPage)setTy.GetProperty("Top").GetValue(setEl);
		List<SetCell> homeCells = (home.Cells == null || home.Cells.Count == 0) ? home.Build() : home.Cells;

		// 首页里「键盘」那一格(按名字找)
		int kbd = -1;
		for (int i = 0; i < homeCells.Count; i++)
		{
			if (homeCells[i] != null && homeCells[i].Title == "键盘") kbd = i;
		}
		Check("设置首页里找得到「键盘」那一格", kbd >= 0, $"(第 {kbd} 格)");
		home.Sel = kbd;
		_w.SettingsActivate();
		Pump(900);
		SetPage key = (SetPage)setTy.GetProperty("Top").GetValue(setEl);
		Check("按 A -> 进了「键盘」那一页", key != null && key.Title == "键盘",
			$"(“{key?.Title}”)");

		// 那一页里的「调整窗口」
		int adj = -1;
		SetCell cell = null;
		for (int j = 0; j < key.Cells.Count; j++)
		{
			if (key.Cells[j] != null && key.Cells[j].Title == "调整窗口")
			{
				adj = j;
				cell = key.Cells[j];
			}
		}
		Check("键盘那一页里有「调整窗口」这一格", cell != null, $"(第 {adj} 格)");
		Check("那一格第二行写着当前缩放(多少%)",
			cell != null && cell.Note.Length > 0 && cell.Note.EndsWith("%"), $"(“{cell?.Note}”)");

		key.Sel = adj;
		_w.SettingsActivate();
		Pump(300);
		Check("按 A -> 主程序那一头收到了(进那一档)", host.AdjustCalls == 1, $"({host.AdjustCalls} 次)");
		// 进去的同时要翻回正面: 看着的是键盘, 缩放 / 摆位才看得出效果
		Check("进去的同时翻回正面(不然眼前还是设置页)", !_w.Flipped, $"(Flipped {_w.Flipped})");

		// 调窗口那一档: 状态行那一块板上得写出"怎么调 / 怎么退" —— 这一档里屏幕上
		// 没有别的变化(键盘一模一样), 不写一句就成了"点了没反应"。
		Border chip = El<Border>("SlideHintChip");
		TextBlock txt = El<TextBlock>("SlideHintText");
		InputCore c = new InputCore { Layout = Layout.CN9, Visible = true };
		c.BeginResize();
		_w.Render(c);
		Check("进那一档: 状态行亮出「调窗口 … B 退出」",
			chip.Visibility == Visibility.Visible
			&& txt.Text.Contains("调窗口") && txt.Text.Contains("B"), $"(“{txt.Text}”)");
		c.EndResize();
		_w.Render(c);
		Check("退出那一档: 那一句收回去(「滑行」那块板还归滑行)",
			txt.Text == "滑行" && chip.Visibility == Visibility.Collapsed, $"(“{txt.Text}”)");
		_w.Close();
	}

	private static void RelSnapshot()
	{
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_044d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0452: Unknown result type (might be due to invalid IL or missing references)
		//IL_0456: Unknown result type (might be due to invalid IL or missing references)
		//IL_045b: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_06bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_06dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_06e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_0853: Unknown result type (might be due to invalid IL or missing references)
		//IL_08a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_08a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_08cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_08d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0906: Unknown result type (might be due to invalid IL or missing references)
		//IL_090b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0931: Unknown result type (might be due to invalid IL or missing references)
		//IL_0936: Unknown result type (might be due to invalid IL or missing references)
		//IL_0943: Unknown result type (might be due to invalid IL or missing references)
		//IL_0948: Unknown result type (might be due to invalid IL or missing references)
		//IL_0978: Unknown result type (might be due to invalid IL or missing references)
		//IL_097d: Unknown result type (might be due to invalid IL or missing references)
		//IL_098a: Unknown result type (might be due to invalid IL or missing references)
		//IL_098f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0fb1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0fb6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0fc2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0fc7: Unknown result type (might be due to invalid IL or missing references)
		//IL_1008: Unknown result type (might be due to invalid IL or missing references)
		//IL_100d: Unknown result type (might be due to invalid IL or missing references)
		//IL_1019: Unknown result type (might be due to invalid IL or missing references)
		//IL_101e: Unknown result type (might be due to invalid IL or missing references)
		//IL_107d: Unknown result type (might be due to invalid IL or missing references)
		//IL_1082: Unknown result type (might be due to invalid IL or missing references)
		//IL_108e: Unknown result type (might be due to invalid IL or missing references)
		//IL_1093: Unknown result type (might be due to invalid IL or missing references)
		//IL_10c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_10c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_10d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_10d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_1229: Unknown result type (might be due to invalid IL or missing references)
		//IL_122e: Unknown result type (might be due to invalid IL or missing references)
		//IL_127e: Unknown result type (might be due to invalid IL or missing references)
		//IL_1283: Unknown result type (might be due to invalid IL or missing references)
		//IL_128c: Unknown result type (might be due to invalid IL or missing references)
		//IL_1291: Unknown result type (might be due to invalid IL or missing references)
		//IL_1296: Unknown result type (might be due to invalid IL or missing references)
		//IL_12ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_12b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_1447: Unknown result type (might be due to invalid IL or missing references)
		//IL_144c: Unknown result type (might be due to invalid IL or missing references)
		//IL_1c2e: Unknown result type (might be due to invalid IL or missing references)
		//IL_1c33: Unknown result type (might be due to invalid IL or missing references)
		//IL_1c40: Unknown result type (might be due to invalid IL or missing references)
		//IL_1c45: Unknown result type (might be due to invalid IL or missing references)
		//IL_1c7e: Unknown result type (might be due to invalid IL or missing references)
		//IL_1c83: Unknown result type (might be due to invalid IL or missing references)
		//IL_177a: Unknown result type (might be due to invalid IL or missing references)
		//IL_177f: Unknown result type (might be due to invalid IL or missing references)
		//IL_178b: Unknown result type (might be due to invalid IL or missing references)
		//IL_1790: Unknown result type (might be due to invalid IL or missing references)
		//IL_1cdf: Unknown result type (might be due to invalid IL or missing references)
		//IL_1ce4: Unknown result type (might be due to invalid IL or missing references)
		//IL_1cf1: Unknown result type (might be due to invalid IL or missing references)
		//IL_1cf6: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== 关系快照(验关系, 不验数字) ===");
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
		InputCore c = new InputCore
		{
			Layout = Layout.CN9,
			Focus = FocusZone.Keyboard,
			Visible = true
		};
		MethodInfo method = typeof(InputCore).GetMethod("DebugRefreshCands", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		string text = "7264";
		foreach (char c2 in text)
		{
			InputCore inputCore = c;
			ReadOnlySpan<char> readOnlySpan = inputCore.Digits;
			char reference = c2;
			inputCore.Digits = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
			method.Invoke(c, null);
		}
		c.PinyinFit = 2;
		_w.LayoutOf = () => c.Layout;
		_w.Render(c);
		Pump(250);
		_w.Render(c);
		Pump(250);
		FrameworkElement e = FE("KeyArea");
		FrameworkElement frameworkElement = FE("CandRow");
		FrameworkElement frameworkElement2 = FE("PinyinRow");
		FrameworkElement frameworkElement3 = FE("StatusDock");
		FrameworkElement e2 = FE("Root");
		FrameworkElement[] array = new FrameworkElement[3] { frameworkElement, frameworkElement2, frameworkElement3 };
		string[] array2 = new string[3] { "候选行", "拼音行", "状态行" };
		Console.WriteLine("  -- 左右对齐(三行 vs 键盘区) --");
		Rect val = InWin(e);
		int num = 0;
		for (int j = 0; j < array.Length; j++)
		{
			Rect val2 = InWin(array[j]);
			Console.WriteLine($"  {array2[j]}  可见={array[j].Visibility,-9} 宽={val2.Width,6:0} 左={val2.Left,7:0} 右={val2.Right,7:0}   键盘区 宽={val.Width,6:0} 左={val.Left,7:0} 右={val.Right,7:0}");
			if (array[j].Visibility == Visibility.Visible && !(val2.Width < 1.0))
			{
				num++;
				Check(array2[j] + "左右两头对齐到键盘区(同宽同心)", Math.Abs(val2.Left - val.Left) <= 0.5 && Math.Abs(val2.Right - val.Right) <= 0.5, $"(左差 {val2.Left - val.Left:0.0}, 右差 {val2.Right - val.Right:0.0})");
			}
		}
		Check("这三行至少得有两行露着(不然这条等于没验)", num >= 2, $"(露着 {num} 行)");
		Rect workArea = SystemParameters.WorkArea;
		Rect val3 = InWin(e2);
		double value = Num("_anchorDrop");
		double value2 = Num("OffsetX");
		double value3 = Num("OffsetY");
		Console.WriteLine($"  -- 摆位明细 --  窗口 L={_w.Left:0} T={_w.Top:0} {_w.ActualWidth:0}x{_w.ActualHeight:0}; 工作区 {workArea.Left:0},{workArea.Top:0} {workArea.Width:0}x{workArea.Height:0}; 偏移 {value2:0},{value3:0}; _anchorDrop={value:0}");
		Console.WriteLine($"                  卡片在窗口里 左={val3.Left:0} 上={val3.Top:0} 宽={val3.Width:0} 高={val3.Height:0} 底={val3.Bottom:0}");
		Matrix transformToDevice = PresentationSource.FromVisual(_w).CompositionTarget.TransformToDevice;
		Point val4 = _w.PointToScreen(new Point(0.0, val3.Bottom));
		double y = val4.Y;
		Rect val5 = SystemParameters.WorkArea;
		double num2 = (val5.Bottom - 24.0) * transformToDevice.M22;
		Console.WriteLine($"  -- 贴底 --  卡片底边在屏幕 y={y:0}, 该落 y={num2:0} (这台 {transformToDevice.M11:0.00}x, 折回去是 DIP {y / transformToDevice.M22:0})");
		Check("卡片底边贴在离屏幕底 24 的地方", Math.Abs(y - num2) <= 2.0, $"(差 {(y - num2) / transformToDevice.M22:0.0} DIP)");
		Border[] array3 = (Border[])typeof(OverlayWindow).GetField("_key9", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Console.WriteLine("  -- 九宫格 --");
		List<Rect> list = new List<Rect>();
		for (int k = 1; k <= 9; k++)
		{
			list.Add(InWin(array3[k]));
		}
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
		for (int l = 0; l < list.Count; l++)
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(21, 6);
			defaultInterpolatedStringHandler.AppendLiteral("  第");
			defaultInterpolatedStringHandler.AppendFormatted(l + 1);
			defaultInterpolatedStringHandler.AppendLiteral("颗  左=");
			val5 = list[l];
			defaultInterpolatedStringHandler.AppendFormatted(val5.Left, 7, "0");
			defaultInterpolatedStringHandler.AppendLiteral(" 上=");
			val5 = list[l];
			defaultInterpolatedStringHandler.AppendFormatted(val5.Top, 7, "0");
			defaultInterpolatedStringHandler.AppendLiteral(" ");
			defaultInterpolatedStringHandler.AppendLiteral("宽=");
			val5 = list[l];
			defaultInterpolatedStringHandler.AppendFormatted(val5.Width, 5, "0");
			defaultInterpolatedStringHandler.AppendLiteral(" 中心=(");
			val5 = list[l];
			double left = val5.Left;
			val5 = list[l];
			defaultInterpolatedStringHandler.AppendFormatted(left + val5.Width / 2.0, "0");
			defaultInterpolatedStringHandler.AppendLiteral(",");
			val5 = list[l];
			double top = val5.Top;
			val5 = list[l];
			defaultInterpolatedStringHandler.AppendFormatted(top + val5.Height / 2.0, "0");
			defaultInterpolatedStringHandler.AppendLiteral(")");
			Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		Console.WriteLine($"  键盘区  左={val.Left,7:0} 上={val.Top,7:0} 宽={val.Width,5:0} 中心=({val.Left + val.Width / 2.0:0},{val.Top + val.Height / 2.0:0})");
		double wmin = list.Min((Rect r) => r.Width);
		List<double> list2 = (from r in list
			where r.Width <= wmin + 0.5
			select r.Width).ToList();
		double num3 = list2.Max() - list2.Min();
		Console.WriteLine($"  键宽 {wmin:0.0} ~ {list.Max((Rect r) => r.Width):0.0} (其中 {list.Count - list2.Count} 颗正在放大, 剩下 {list2.Count} 颗彼此差 {num3:0.0})");
		Check("九颗键一样宽(除了正在『浮起来』那颗是故意放大的)", num3 <= 0.5, $"(其余 {list2.Count} 颗最宽 - 最窄 = {num3:0.0})");
		List<double> list3 = (from v in list.Select((Rect r) => Math.Round(r.Left + r.Width / 2.0, 1)).Distinct()
			orderby v
			select v).ToList();
		List<double> list4 = (from v in list.Select((Rect r) => Math.Round(r.Top + r.Height / 2.0, 1)).Distinct()
			orderby v
			select v).ToList();
		double num4 = ((list3.Count == 3) ? Math.Abs(list3[1] - list3[0] - (list3[2] - list3[1])) : 999.0);
		double num5 = ((list4.Count == 3) ? Math.Abs(list4[1] - list4[0] - (list4[2] - list4[1])) : 999.0);
		Console.WriteLine($"  中心列 x={string.Join(",", list3.Select((double v) => v.ToString("0")))} (间距差 {num4:0.0}); 中心排 y={string.Join(",", list4.Select((double v) => v.ToString("0")))} (间距差 {num5:0.0})");
		Check("九颗键排成整齐的三竖列(间距一样)", list3.Count == 3 && num4 <= 0.5, $"({list3.Count} 列, 间距差 {num4:0.0})");
		Check("九颗键排成整齐的三横排(间距一样)", list4.Count == 3 && num5 <= 0.5, $"({list4.Count} 排, 间距差 {num5:0.0})");
		val5 = list[4];
		double left2 = val5.Left;
		val5 = list[4];
		int ok;
		if (Math.Abs(left2 + val5.Width / 2.0 - (val.Left + val.Width / 2.0)) <= 0.5)
		{
			val5 = list[4];
			double top2 = val5.Top;
			val5 = list[4];
			ok = ((Math.Abs(top2 + val5.Height / 2.0 - (val.Top + val.Height / 2.0)) <= 0.5) ? 1 : 0);
		}
		else
		{
			ok = 0;
		}
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(25, 4);
		defaultInterpolatedStringHandler.AppendLiteral("(5 号键中心 (");
		val5 = list[4];
		double left3 = val5.Left;
		val5 = list[4];
		defaultInterpolatedStringHandler.AppendFormatted(left3 + val5.Width / 2.0, "0");
		defaultInterpolatedStringHandler.AppendLiteral(",");
		val5 = list[4];
		double top3 = val5.Top;
		val5 = list[4];
		defaultInterpolatedStringHandler.AppendFormatted(top3 + val5.Height / 2.0, "0");
		defaultInterpolatedStringHandler.AppendLiteral(") ");
		defaultInterpolatedStringHandler.AppendLiteral("vs 键盘区中心 (");
		defaultInterpolatedStringHandler.AppendFormatted(val.Left + val.Width / 2.0, "0");
		defaultInterpolatedStringHandler.AppendLiteral(",");
		defaultInterpolatedStringHandler.AppendFormatted(val.Top + val.Height / 2.0, "0");
		defaultInterpolatedStringHandler.AppendLiteral("))");
		Check("中间那颗(第 5 颗)正落在键盘区的中心", (byte)ok != 0, defaultInterpolatedStringHandler.ToStringAndClear());
		Console.WriteLine("  -- 缩放不动点(九键那颗 5) --");
		Border e3 = array3[5];
		MethodInfo method2 = typeof(OverlayWindow).GetMethod("Reposition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		double num6 = 0.0;
		double num7 = 0.0;
		double num8 = 0.0;
		double num9 = 0.0;
		double num10 = 0.0;
		int num11 = 0;
		int num12 = 0;
		for (int m = 0; m <= 12; m++)
		{
			double num13 = 1.0 + (double)m * 0.05;
			_w.SetScale(num13);
			method2?.Invoke(_w, null);
			Pump(50);
			Point val6 = ScrCen(e3);
			if (m == 0)
			{
				num6 = val6.X;
				num7 = val6.Y;
			}
			double value4 = (val6.X - num6) / transformToDevice.M11;
			double value5 = (val6.Y - num7) / transformToDevice.M22;
			OverlayWindow w = _w;
			val5 = InWin(e2);
			val4 = w.PointToScreen(new Point(0.0, val5.Bottom));
			double num14 = val4.Y / transformToDevice.M22;
			val5 = SystemParameters.WorkArea;
			bool flag = Math.Abs(num14 - val5.Bottom) <= 1.5;
			Console.WriteLine($"  s={num13:0.00} 窗口 {_w.ActualWidth:0}x{_w.ActualHeight:0} L={_w.Left:0} T={_w.Top:0}  偏离=({value4:0.0},{value5:0.0}) 卡片底边={num14:0} {(flag ? "(贴住了, 被夹)" : "")}");
			num8 = Math.Max(num8, Math.Abs(value4));
			if (!flag)
			{
				num11++;
				num9 = Math.Max(num9, Math.Abs(value5));
				continue;
			}
			num12++;
			double val7 = num10;
			val5 = SystemParameters.WorkArea;
			num10 = Math.Max(val7, Math.Abs(num14 - val5.Bottom));
		}
		Check("没被屏幕夹住的那几档, 那颗 5 在屏幕上一像素都不许动", num11 >= 2 && num9 <= 1.0, $"(没被夹的有 {num11} 档, 其中最大动 {num9:0.0} DIP)");
		Check("被屏幕夹住的那几档, 卡片底边正压在屏幕底边上(不许夹一半)", num10 <= 1.5, $"(被夹 {num12} 档, 离屏幕底边最多 {num10:0.0} DIP)");
		Check("整趟下来横向一像素都不许错", num8 <= 1.0, $"(最大 {num8:0.0} DIP)");
		_w.SetScale(1.0);
		method2?.Invoke(_w, null);
		Pump(150);
		Console.WriteLine("  -- 内容区宽度: 一个源头, 六处跟随 --");
		typeof(OverlayWindow).GetProperty("LayoutOf")?.SetValue(_w, (Func<Layout>)(() => c.Layout));
		(Layout, double)[] array4 = new(Layout, double)[2]
		{
			(Layout.EN26, 640.0),
			(Layout.CN9, 360.0)
		};
		for (int i = 0; i < array4.Length; i++)
		{
			var (layout, num15) = array4[i];
			c.Layout = layout;
			_w.FlipLayout(delegate
			{
			});
			for (int n = 0; n < 8; n++)
			{
				_w.Render(c);
				Pump(120);
			}
			_w.Render(c);
			Pump(250);
			double contentW = _w.ContentW;
			bool flag2 = (bool)typeof(OverlayWindow).GetField("_flipL", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
			bool flag3 = (bool)typeof(OverlayWindow).GetField("_expanding", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
			double width = FE("Frame").Width;
			double width2 = FE("CandRow").Width;
			double width3 = FE("PinyinRow").Width;
			double width4 = FE("StatusDock").Width;
			double width5 = FE("NotePanel").Width;
			Rect val8 = InWin(e);
			Rect val9 = InWin(FE("Frame"));
			Console.WriteLine($"  {layout,-5} ContentW={contentW:0} 框={width:0} 候选={width2:0} 拼音={width3:0} 状态={width4:0} 预览={width5:0} | _flipL={flag2} _expanding={flag3}");
			Check($"切到 {layout}: 内容区宽度到位", Math.Abs(contentW - num15) <= 1.0, $"({contentW:0} 该是 {num15:0})");
			Check($"切到 {layout}: 六个地方宽度一起跟上(框 = 内容 + 24, 其余 = 内容)", Math.Abs(width - (num15 + 24.0)) <= 1.0 && Math.Abs(width2 - num15) <= 1.0 && Math.Abs(width3 - num15) <= 1.0 && Math.Abs(width4 - num15) <= 1.0 && Math.Abs(width5 - num15) <= 1.0, $"(框 {width:0} / 候选 {width2:0} / 拼音 {width3:0} / 状态 {width4:0} / 预览 {width5:0})");
			Check($"切到 {layout}: 框和键盘区同心(框只是左右各宽一圈)", Math.Abs(val9.Left + val9.Width / 2.0 - (val8.Left + val8.Width / 2.0)) <= 0.5, $"(框中心 {val9.Left + val9.Width / 2.0:0} vs 键盘区中心 {val8.Left + val8.Width / 2.0:0})");
			Check($"切到 {layout}: 动画演完当场撒手(不能挂着 _flipL 等 1000ms 看门狗)", !flag2 && !flag3, $"(_flipL={flag2}, _expanding={flag3})");
		}
		Console.WriteLine("  -- 换布局往返 --");
		val5 = InWin(e);
		double width6 = val5.Width;
		Point val10 = ScrCen(e3);
		c.Layout = Layout.EN26;
		for (int num16 = 0; num16 < 4; num16++)
		{
			_w.Render(c);
			Pump(200);
		}
		val5 = InWin(e);
		double width7 = val5.Width;
		c.Layout = Layout.CN9;
		for (int num17 = 0; num17 < 6; num17++)
		{
			_w.Render(c);
			Pump(200);
		}
		_w.Render(c);
		Pump(300);
		val5 = InWin(e);
		double width8 = val5.Width;
		Point val11 = ScrCen(e3);
		double num18 = Math.Abs(val11.X - val10.X);
		double num19 = Math.Abs(val11.Y - val10.Y);
		Console.WriteLine($"  键盘区宽 {width6:0} -> 全键盘 {width7:0} -> 回来 {width8:0}; 那颗 5 回来时偏 X {num18:0.0} / Y {num19:0.0}");
		Check("九键 -> 全键盘 -> 九键, 键盘区宽度回到原样", Math.Abs(width8 - width6) <= 1.0, $"({width6:0} -> {width7:0} -> {width8:0})");
		Check("走完这一来回, 九键那颗 5 回到屏幕上原处", num18 <= 1.0 && num19 <= 1.0, $"(偏 X {num18:0.0} / Y {num19:0.0})");
		_w.Close();
	}

	private static void One(string title, FxStyle which)
	{
		Console.WriteLine("=== " + title + " ===");
		FakeHost host = new FakeHost
		{
			Which = which
		};
		_w = new OverlayWindow
		{
			Host = host,
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Snap snap = Grab();
		Console.WriteLine($"  开机            L={snap.Left,7:0} T={snap.Top,7:0} Win={snap.W:0}x{snap.H:0} 卡片中心@({snap.FaceX,6:0},{snap.FaceY,6:0})");
		List<Snap> list = new List<Snap>();
		List<Snap> list2 = new List<Snap>();
		for (int i = 1; i <= 3; i++)
		{
			Call("ShowSide", true);
			Pump(500);
			Snap snap2 = Grab();
			list2.Add(snap2);
			Console.WriteLine($"  #{i} 进设置       L={snap2.Left,7:0} T={snap2.Top,7:0} Win={snap2.W:0}x{snap2.H:0} 卡片中心@({snap2.FaceX,6:0},{snap2.FaceY,6:0})");
			Call("ShowSide", false);
			Pump(500);
			Snap snap3 = Grab();
			list.Add(snap3);
			Console.WriteLine($"  #{i} 回九键       L={snap3.Left,7:0} T={snap3.Top,7:0} Win={snap3.W:0}x{snap3.H:0} 卡片中心@({snap3.FaceX,6:0},{snap3.FaceY,6:0})");
		}
		Check("九键那一面几趟下来位置不漂", Range(list.Select((Snap x) => x.Left)) <= 2.0 && Range(list.Select((Snap x) => x.Top)) <= 2.0, $"(X 抖 {Range(list.Select((Snap x) => x.Left)):0}, Y 抖 {Range(list.Select((Snap x) => x.Top)):0})");
		Check("设置那一面几趟下来位置不漂", Range(list2.Select((Snap x) => x.Left)) <= 2.0 && Range(list2.Select((Snap x) => x.Top)) <= 2.0, $"(X 抖 {Range(list2.Select((Snap x) => x.Left)):0}, Y 抖 {Range(list2.Select((Snap x) => x.Top)):0})");
		bool ok = Range(from x in list.Concat(list2)
			select x.W) < 0.5 && Range(from x in list.Concat(list2)
			select x.H) < 0.5;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(6, 2);
		defaultInterpolatedStringHandler.AppendLiteral("(最终 ");
		defaultInterpolatedStringHandler.AppendFormatted(list[list.Count - 1].W, "0");
		defaultInterpolatedStringHandler.AppendLiteral("x");
		defaultInterpolatedStringHandler.AppendFormatted(list[list.Count - 1].H, "0");
		defaultInterpolatedStringHandler.AppendLiteral(")");
		Check("窗口尺寸不跟着翻面长", ok, defaultInterpolatedStringHandler.ToStringAndClear());
		double num = Math.Abs(list.Average((Snap x) => x.FaceX) - list2.Average((Snap x) => x.FaceX));
		double num2 = Math.Abs(list.Average((Snap x) => x.FaceY) - list2.Average((Snap x) => x.FaceY));
		Check("正反两面的卡片停在屏幕同一处", num <= 12.0 && num2 <= 12.0, $"(差 {num:0}, {num2:0})");
		_w.Close();
		static double Range(IEnumerable<double> v)
		{
			return v.Max() - v.Min();
		}
	}

	private static void Diag2()
	{
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0267: Unknown result type (might be due to invalid IL or missing references)
		//IL_0271: Unknown result type (might be due to invalid IL or missing references)
		//IL_0276: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d6: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== DIAG: 每一页撑多高 / 内容区在哪儿 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(400);
		Call("ShowSide", true);
		Pump(900);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		List<SetPage> list = (List<SetPage>)type.GetField("Pages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		int[] array = new int[4] { 9, 3, 6, 1 };
		foreach (int num in array)
		{
			list.Clear();
			list.Add(Make(num));
			Call("ShowPage", value, true, -1, 1);
			Pump(700);
			FrameworkElement frameworkElement = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			FrameworkElement frameworkElement2 = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 8);
			defaultInterpolatedStringHandler.AppendLiteral("  ");
			defaultInterpolatedStringHandler.AppendFormatted(num, 2);
			defaultInterpolatedStringHandler.AppendLiteral(" 格: Stage ");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualWidth, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral("x");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualHeight, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" 顶=");
			Point val = frameworkElement.TranslatePoint(new Point(0.0, 0.0), _w);
			defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" | 面板 ");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualWidth, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral("x");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualHeight, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" 顶=");
			val = frameworkElement2.TranslatePoint(new Point(0.0, 0.0), _w);
			defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" | 第 1 格顶=");
			val = ((List<Border>)type.GetField("Boxes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value))[0].TranslatePoint(new Point(0.0, 0.0), _w);
			defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
			Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		_w.Close();
		static SetPage Make(int n)
		{
			return new SetPage
			{
				Title = "T" + n,
				Nine = true,
				Rows = 3,
				Build = delegate
				{
					List<SetCell> list2 = new List<SetCell>();
					for (int j = 0; j < n; j++)
					{
						list2.Add(SetNav.Cell("格" + j));
					}
					return list2;
				}
			};
		}
	}

	private static void Diag()
	{
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		//IL_019b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0308: Unknown result type (might be due to invalid IL or missing references)
		//IL_0312: Unknown result type (might be due to invalid IL or missing references)
		//IL_0317: Unknown result type (might be due to invalid IL or missing references)
		//IL_080c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0816: Unknown result type (might be due to invalid IL or missing references)
		//IL_081b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0345: Unknown result type (might be due to invalid IL or missing references)
		//IL_034f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0354: Unknown result type (might be due to invalid IL or missing references)
		//IL_0576: Unknown result type (might be due to invalid IL or missing references)
		//IL_0580: Unknown result type (might be due to invalid IL or missing references)
		//IL_0585: Unknown result type (might be due to invalid IL or missing references)
		//IL_08c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_08ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_08d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0928: Unknown result type (might be due to invalid IL or missing references)
		//IL_0932: Unknown result type (might be due to invalid IL or missing references)
		//IL_0937: Unknown result type (might be due to invalid IL or missing references)
		//IL_05b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_05bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_05c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b3a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b44: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b49: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b5f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b69: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b6e: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== DIAG: 旧页照片 vs 新页真身 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(400);
		Call("ShowSide", true);
		Pump(900);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		FrameworkElement frameworkElement = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		FrameworkElement frameworkElement2 = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		Console.WriteLine($"Stage {frameworkElement.ActualWidth:0.#}x{frameworkElement.ActualHeight:0.#}");
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(19, 3);
		defaultInterpolatedStringHandler.AppendLiteral("Panel ");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualWidth, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral("x");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualHeight, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral("  相对Stage的Y=");
		Point val = frameworkElement2.TranslatePoint(new Point(0.0, 0.0), frameworkElement);
		defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
		Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		SetPage setPage = new SetPage
		{
			Title = "多页",
			Nine = true,
			Rows = 3
		};
		setPage.Build = delegate
		{
			List<SetCell> list = new List<SetCell>();
			for (int m = 0; m < 12; m++)
			{
				list.Add(SetNav.Cell("格" + m));
			}
			return list;
		};
		((List<SetPage>)type.GetField("Pages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value)).Add(setPage);
		MethodInfo method = typeof(OverlayWindow).GetMethod("FlipPage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		Console.WriteLine($"  多页层页数 = {SetNav.PagesOf(setPage)}");
		method.Invoke(_w, new object[2] { value, 1 });
		for (int i = 1; i <= 8; i++)
		{
			Pump(35);
			FrameworkElement frameworkElement3 = (FrameworkElement)type.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			FrameworkElement frameworkElement4 = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			FrameworkElement frameworkElement5 = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			double num;
			if (frameworkElement3 == null)
			{
				num = -999.0;
			}
			else
			{
				val = frameworkElement3.TranslatePoint(new Point(0.0, 0.0), _w);
				num = val.Y;
			}
			double num2 = num;
			double num3;
			if (frameworkElement4 == null)
			{
				num3 = -999.0;
			}
			else
			{
				val = frameworkElement4.TranslatePoint(new Point(0.0, 0.0), _w);
				num3 = val.Y;
			}
			double num4 = num3;
			Console.WriteLine($"  [翻页] t={i * 35,3}ms Stage={frameworkElement5.ActualWidth:0.#}x{frameworkElement5.ActualHeight:0.#} 旧页 顶={num2:0.#} 高={frameworkElement3?.ActualHeight ?? (-1.0):0.#} X={SlideX(frameworkElement3),7:0.#} | 新页 顶={num4:0.#} 高={frameworkElement4?.ActualHeight ?? (-1.0):0.#} X={SlideX(frameworkElement4),7:0.#} | 顶差={num2 - num4:0.#}");
		}
		Pump(700);
		_ = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		Call("ShowPage", value, true, -1, 1);
		for (int j = 1; j <= 10; j++)
		{
			Pump(35);
			FrameworkElement frameworkElement6 = (FrameworkElement)type.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			FrameworkElement frameworkElement7 = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			double num5;
			if (frameworkElement6 != null)
			{
				val = frameworkElement6.TranslatePoint(new Point(0.0, 0.0), _w);
				num5 = val.Y;
			}
			else
			{
				num5 = -999.0;
			}
			double num6 = num5;
			double num7;
			if (frameworkElement7 != null)
			{
				val = frameworkElement7.TranslatePoint(new Point(0.0, 0.0), _w);
				num7 = val.Y;
			}
			else
			{
				num7 = -999.0;
			}
			double num8 = num7;
			Console.WriteLine($"  [设置] t={j * 35,3}ms  旧页 X={SlideX(frameworkElement6),7:0.#} Y={SlideY(frameworkElement6),6:0.#} 缩放={SlideS(frameworkElement6),5:0.###} 顶={num6:0.#} | 新页 X={SlideX(frameworkElement7),7:0.#} Y={SlideY(frameworkElement7),6:0.#} 缩放={SlideS(frameworkElement7),5:0.###} 顶={num8:0.#} 高={frameworkElement7?.ActualHeight ?? (-1.0):0.#} | 差Y={num6 - num8:0.#}");
		}
		Pump(600);
		FrameworkElement frameworkElement8 = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement frameworkElement9 = (FrameworkElement)typeof(OverlayWindow).GetField("FaceVeil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		InputCore inputCore = new InputCore
		{
			Layout = Layout.CN9,
			Focus = FocusZone.Keyboard,
			Visible = true
		};
		_w.Render(inputCore);
		Pump(250);
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(18, 2);
		defaultInterpolatedStringHandler.AppendLiteral("  九键面   键盘区顶=");
		val = frameworkElement8.TranslatePoint(new Point(0.0, 0.0), _w);
		defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" 窗口高=");
		defaultInterpolatedStringHandler.AppendFormatted(_w.ActualHeight, "0.#");
		Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		inputCore.Punct9 = true;
		_w.Render(inputCore);
		for (int k = 1; k <= 8; k++)
		{
			Pump(35);
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(38, 7);
			defaultInterpolatedStringHandler.AppendLiteral("  [换面] t=");
			defaultInterpolatedStringHandler.AppendFormatted(k * 35, 3);
			defaultInterpolatedStringHandler.AppendLiteral("ms 键盘区顶=");
			val = frameworkElement8.TranslatePoint(new Point(0.0, 0.0), _w);
			defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" X=");
			defaultInterpolatedStringHandler.AppendFormatted(SlideX(frameworkElement8), 7, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" | 照片顶=");
			val = frameworkElement9.TranslatePoint(new Point(0.0, 0.0), _w);
			defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" X=");
			defaultInterpolatedStringHandler.AppendFormatted(SlideX(frameworkElement9), 7, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" 高=");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement9.ActualHeight, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" 窗口高=");
			defaultInterpolatedStringHandler.AppendFormatted(_w.ActualHeight, "0.#");
			Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		Pump(600);
		FrameworkElement frameworkElement10 = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement frameworkElement11 = (FrameworkElement)typeof(OverlayWindow).GetField("FaceVeil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		object obj = Call("SnapshotFace");
		Console.WriteLine($"KeyArea {frameworkElement10.ActualWidth:0.#}x{frameworkElement10.ActualHeight:0.#}  FaceVeil(演之前) {frameworkElement11.ActualWidth:0.#}x{frameworkElement11.ActualHeight:0.#}");
		Call("ShowFacePhoto", obj, 1);
		Call("FaceIn", 1);
		for (int l = 1; l <= 10; l++)
		{
			Pump(35);
			double value2 = SlideX(frameworkElement11);
			double value3 = SlideX(frameworkElement10);
			Point val2 = frameworkElement11.TranslatePoint(new Point(0.0, 0.0), _w);
			Point val3 = frameworkElement10.TranslatePoint(new Point(0.0, 0.0), _w);
			Console.WriteLine($"  t={l * 35,3}ms  旧页 X={value2,7:0.#} 左上角=({val2.X:0.#},{val2.Y:0.#}) ({frameworkElement11.ActualWidth:0.#}x{frameworkElement11.ActualHeight:0.#}) | 新页 X={value3,7:0.#} 左上角=({val3.X:0.#},{val3.Y:0.#}) | 差=({val2.X - val3.X:0.#},{val2.Y - val3.Y:0.#})");
		}
		_w.Close();
	}

	private static double SlideX(FrameworkElement e)
	{
		if (!(e?.RenderTransform is TransformGroup transformGroup))
		{
			return 0.0;
		}
		foreach (Transform child in transformGroup.Children)
		{
			if (child is TranslateTransform translateTransform)
			{
				return translateTransform.X;
			}
		}
		return 0.0;
	}

	private static double SlideY(FrameworkElement e)
	{
		if (!(e?.RenderTransform is TransformGroup transformGroup))
		{
			return 0.0;
		}
		foreach (Transform child in transformGroup.Children)
		{
			if (child is TranslateTransform translateTransform)
			{
				return translateTransform.Y;
			}
		}
		return 0.0;
	}

	private static double SlideS(FrameworkElement e)
	{
		if (!(e?.RenderTransform is TransformGroup transformGroup))
		{
			return 1.0;
		}
		foreach (Transform child in transformGroup.Children)
		{
			if (child is ScaleTransform scaleTransform)
			{
				return scaleTransform.ScaleY;
			}
		}
		return 1.0;
	}

	private static (double panel, double veil) SlidePos()
	{
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		FrameworkElement e = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		FrameworkElement frameworkElement = (FrameworkElement)type.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		return (panel: SlideX(e), veil: (frameworkElement == null) ? 0.0 : SlideX(frameworkElement));
	}

	private static void SlideSettle()
	{
		Console.WriteLine("=== 手机: 翻页照旧平移 / 换面改淡入淡出 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(700);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		SetPage setPage = new SetPage
		{
			Title = "多页",
			Nine = true,
			Rows = 3
		};
		setPage.Build = delegate
		{
			List<SetCell> list = new List<SetCell>();
			for (int i = 0; i < 12; i++)
			{
				list.Add(SetNav.Cell("格" + i));
			}
			return list;
		};
		Call("PushPage", value, setPage, true, -1, -1);
		Pump(700);
		Check("翻页·站上多页那层(不然下面全是摆设)", SetNav.PagesOf(setPage) >= 2 && ((List<SetPage>)type.GetField("Pages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value)).Count >= 2, $"(这一层 {SetNav.PagesOf(setPage)} 页, 栈里 {((List<SetPage>)type.GetField("Pages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value)).Count} 页)");
		FrameworkElement frameworkElement = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		Call("FlipPage", value, 1);
		Pump(110);
		var (num, num2) = SlidePos();
		Check("翻页·往里走: 新的一页从右边滑进来", num > 8.0, $"(新页 X={num:0})");
		Check("翻页·往里走: 旧的一页往左滑走", num2 < -8.0, $"(旧页 X={num2:0})");
		FrameworkElement frameworkElement2 = (FrameworkElement)type.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		Check("翻页·演到半路, 旧页还是实心的(不淡出)", frameworkElement2.Opacity > 0.999, $"(Opacity {frameworkElement2.Opacity:0.###})");
		Check("翻页·演到半路, 新页已经是实心的(不淡入)", frameworkElement.Opacity > 0.999, $"(Opacity {frameworkElement.Opacity:0.###})");
		var (ok, ok2) = SlideFlat();
		Check("翻页·演到半路, 旧页照片是没糊的", ok, "(Effect 还挂着 = 又走回景深那套了)");
		Check("翻页·演到半路, 旧页照片是没压暗的", ok2, "(压暗层还铺着 = 旧页比新页暗一截)");
		var (num3, flag) = SlideClip();
		Check("翻页·演的时候屏幕框挂上了", num3 > 0 && flag, $"(裁着 {num3} 个容器, 挂上 Clip={flag})");
		double num4 = SlideClipW();
		Check("翻页·屏幕框就是页面这一块(不是父容器那一圈)", num4 > 0.0 && Math.Abs(num4 - frameworkElement.ActualWidth) < 3.0, $"(框宽 {num4:0.#} vs 页面宽 {frameworkElement.ActualWidth:0.#})");
		Pump(700);
		var (flag2, flag3) = SlideFlat();
		Check("翻页·演完把糊和压暗还回去(默认/旋转那两套还要用)", !flag2 && !flag3, $"(糊回来了={!flag2}, 压暗回来了={!flag3})");
		double item = SlidePos().panel;
		Check("翻页·演完横偏撒手归零(不许残留半屏)", Math.Abs(item) < 0.5, $"(新页 X={item:0.##})");
		Call("FlipPage", value, -1);
		Pump(110);
		var (num5, num6) = SlidePos();
		Check("翻页·退回: 新的一页改从左边来", num5 < -8.0, $"(新页 X={num5:0})");
		Check("翻页·退回: 旧的一页改往右走", num6 > 8.0, $"(旧页 X={num6:0})");
		Pump(700);
		Check("翻页·退回演完也归零", Math.Abs(SlidePos().panel) < 0.5, $"(新页 X={SlidePos().panel:0.##})");
		int item2 = SlideClip().n;
		Check("翻页·演完屏幕框撤掉(别把下一场也裁了)", item2 == 0, $"(还裁着 {item2} 个容器)");
		FrameworkElement frameworkElement3 = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement e = (FrameworkElement)typeof(OverlayWindow).GetField("FaceVeil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Call("ShowSide", false);
		Pump(110);
		double num7 = SlideX(frameworkElement3);
		double num8 = SlideX(e);
		Check("换面·退回: 新的一页改从左边来", num7 < -8.0, $"(新页 X={num7:0})");
		Check("换面·退回: 旧的一页改往右走", num8 > 8.0, $"(旧页 X={num8:0})");
		Pump(700);
		Check("换面·退回演完也归零", Math.Abs(SlideX(frameworkElement3)) < 0.5, $"(新页 X={SlideX(frameworkElement3):0.##})");
		Check("换面·演完这一页是实心的", frameworkElement3.Opacity > 0.999, $"(Opacity {frameworkElement3.Opacity:0.###})");
		int item3 = SlideClip().n;
		Check("换面·演完屏幕框撤掉(别把下一场也裁了)", item3 == 0, $"(还裁着 {item3} 个容器)");
		_w.Close();
	}

	private static (int n, bool allOn) SlideClip()
	{
		IEnumerable obj = (IEnumerable)typeof(OverlayWindow).GetField("_clipHosts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		int num = 0;
		bool item = true;
		foreach (object item2 in obj)
		{
			num++;
			if (((UIElement)item2.GetType().GetField("Item1").GetValue(item2))?.Clip == null)
			{
				item = false;
			}
		}
		return (n: num, allOn: item);
	}

	private static double SlideClipW()
	{
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		foreach (object item in (IEnumerable)typeof(OverlayWindow).GetField("_clipHosts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w))
		{
			UIElement uIElement = (UIElement)item.GetType().GetField("Item1").GetValue(item);
			if (uIElement?.Clip != null)
			{
				Rect bounds = uIElement.Clip.Bounds;
				return bounds.Width;
			}
		}
		return -1.0;
	}

	private static (bool flat, bool shade) SlideFlat()
	{
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		Image image = type.GetField("VeilImg", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value) as Image;
		FrameworkElement obj = type.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value) as FrameworkElement;
		Border border = null;
		if (obj is Border { Child: Grid child } && child.Children.Count > 1)
		{
			border = child.Children[1] as Border;
		}
		bool item = image == null || image.Effect == null;
		bool item2 = border == null || border.Background == null;
		return (flat: item, shade: item2);
	}

	private static void SlideResume()
	{
		Console.WriteLine("=== 平移: 连着翻页不重演 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(700);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement frameworkElement = (FrameworkElement)value.GetType().GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		Check("连按这一场有对象可演(面板搭起来了)", frameworkElement != null);
		Call("RiseIn", value, frameworkElement, 1, false);
		Pump(140);
		double num = SlideX(frameworkElement);
		Check("第一场演到一半, 新页还在半路上", num > 20.0 && num < 340.0, $"(新页 X={num:0})");
		Call("RiseIn", value, frameworkElement, 1, false);
		double num2 = SlideX(frameworkElement);
		Check("连按第二场: 从当前位置接着滑, 不被拽回框外", Math.Abs(num2 - num) < 60.0, $"(上一站 {num:0} -> 这一场起步 {num2:0})");
		Check("连按第二场: 起步点确实还在框里那半边", num2 < num + 1.0, $"(起步 {num2:0})");
		Pump(500);
		Check("连按演完照样归零", Math.Abs(SlideX(frameworkElement)) < 0.5, $"(新页 X={SlideX(frameworkElement):0.##})");
		int item = SlideClip().n;
		Check("连按演完屏幕框也撤干净", item == 0, $"(还裁着 {item} 个容器)");
		_w.Close();
	}

	private static void SlideLevel()
	{
		//IL_01ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0223: Unknown result type (might be due to invalid IL or missing references)
		//IL_022d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0232: Unknown result type (might be due to invalid IL or missing references)
		//IL_0511: Unknown result type (might be due to invalid IL or missing references)
		//IL_051b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0520: Unknown result type (might be due to invalid IL or missing references)
		//IL_053d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0547: Unknown result type (might be due to invalid IL or missing references)
		//IL_054c: Unknown result type (might be due to invalid IL or missing references)
		//IL_076a: Unknown result type (might be due to invalid IL or missing references)
		//IL_076f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0774: Unknown result type (might be due to invalid IL or missing references)
		//IL_07d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_07c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_07d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_07e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_07f9: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== 平移: 每一页一样高 + 翻页不下移 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(800);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		List<SetPage> list = (List<SetPage>)type.GetField("Pages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		List<double> list2 = new List<double>();
		List<double> list3 = new List<double>();
		List<double> list4 = new List<double>();
		int[] array = new int[3] { 9, 4, 1 };
		Point val;
		for (int j = 0; j < array.Length; j++)
		{
			int i = array[j];
			list.Clear();
			SetPage setPage = new SetPage
			{
				Title = "T" + i,
				Nine = true,
				Rows = 3
			};
			setPage.Build = delegate
			{
				List<SetCell> list7 = new List<SetCell>();
				for (int l = 0; l < i; l++)
				{
					list7.Add(SetNav.Cell("格" + l));
				}
				return list7;
			};
			list.Add(setPage);
			Call("ShowPage", value, true, -1, 1);
			Pump(600);
			FrameworkElement frameworkElement = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			List<Border> list5 = (List<Border>)type.GetField("Boxes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			list2.Add(frameworkElement.ActualHeight);
			val = frameworkElement.TranslatePoint(new Point(0.0, 0.0), _w);
			list3.Add(val.Y);
			val = list5[0].TranslatePoint(new Point(0.0, 0.0), _w);
			list4.Add(val.Y);
		}
		Check("每一页都撑到一样高(不满九个也不许缩)", Span(list2) < 0.5, $"(9/4/1 格各 {list2[0]:0.#} / {list2[1]:0.#} / {list2[2]:0.#})");
		Check("每一页的内容区顶边钉在同一处(不许往下掉)", Span(list3) < 0.5, $"(顶边 {list3[0]:0.#} / {list3[1]:0.#} / {list3[2]:0.#})");
		Check("每一页的第一格都落在自己那一格上", Span(list4) < 0.5, $"(第一格顶 {list4[0]:0.#} / {list4[1]:0.#} / {list4[2]:0.#})");
		list.Clear();
		SetPage setPage2 = new SetPage
		{
			Title = "多页",
			Nine = true,
			Rows = 3
		};
		setPage2.Build = delegate
		{
			List<SetCell> list6 = new List<SetCell>();
			for (int k = 0; k < 12; k++)
			{
				list6.Add(SetNav.Cell("格" + k));
			}
			return list6;
		};
		list.Add(setPage2);
		Call("ShowPage", value, true, -1, 1);
		Pump(600);
		Call("FlipPage", value, 1);
		Pump(120);
		FrameworkElement frameworkElement2 = (FrameworkElement)type.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		FrameworkElement obj = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		val = frameworkElement2.TranslatePoint(new Point(0.0, 0.0), _w);
		double y = val.Y;
		val = obj.TranslatePoint(new Point(0.0, 0.0), _w);
		double y2 = val.Y;
		Check("翻到半路: 旧页和新页顶边齐平(没往下掉一截)", Math.Abs(y - y2) < 1.5, $"(顶边差 {y - y2:0.#})");
		FrameworkElement frameworkElement3 = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		Check("翻到半路: 旧页照片是 1:1 摆在原处的(没被压窄)", Math.Abs(frameworkElement2.ActualWidth - frameworkElement3.ActualWidth) < 1.5 && Math.Abs(frameworkElement2.ActualHeight - frameworkElement3.ActualHeight) < 1.5, $"(照片 {frameworkElement2.ActualWidth:0.#}x{frameworkElement2.ActualHeight:0.#} vs 舞台 {frameworkElement3.ActualWidth:0.#}x{frameworkElement3.ActualHeight:0.#}");
		Pump(700);
		FrameworkElement frameworkElement4 = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		UIElement uIElement = (UIElement)(object)frameworkElement4.Parent;
		InputCore inputCore = new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			Punct = true
		};
		inputCore.Focus = FocusZone.CandGrid;
		_w.Render(inputCore);
		Pump(700);
		FrameworkElement frameworkElement5 = (FrameworkElement)typeof(OverlayWindow).GetField("PunctDots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Rect val2 = frameworkElement5.TransformToVisual(uIElement).TransformBounds(new Rect(0.0, 0.0, frameworkElement5.ActualWidth, frameworkElement5.ActualHeight));
		Check("符号面板那排页码点翻页前是看得见的", frameworkElement5.Visibility == Visibility.Visible && val2.Height > 1.0);
		inputCore.NextPunctCategory(1);
		_w.Render(inputCore);
		Pump(35);
		Rect val3 = uIElement.Clip?.Bounds ?? Rect.Empty;
		Check("演平移时屏幕框还留着底带(那排点不会被切掉)", !val3.IsEmpty && val3.Contains(val2.TopLeft) && val3.Contains(val2.BottomRight), $"(框 {val3.Width:0.#}x{val3.Height:0.#} @y{val3.Y:0.#}, 点在 y{val2.Y:0.#})");
		Check("屏幕框横向还是只挡页面那一块(不许滑到框外还看得见)", Math.Abs(val3.Width - frameworkElement4.ActualWidth) < 3.0, $"(框宽 {val3.Width:0.#} vs 页面宽 {frameworkElement4.ActualWidth:0.#})");
		Pump(700);
		Check("演完屏幕框撤掉, 那排点还在", uIElement.Clip == null && frameworkElement5.Visibility == Visibility.Visible);
		InputCore c = new InputCore
		{
			Layout = Layout.CN9,
			Focus = FocusZone.CandGrid,
			Punct = true,
			Visible = true
		};
		_w.Render(c);
		Pump(250);
		FrameworkElement frameworkElement6 = (FrameworkElement)typeof(OverlayWindow).GetField("PunctDots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Check("符号面板那排页码点是显示着的(才有的可验)", frameworkElement6.Visibility == Visibility.Visible);
		Call("SnapshotFace");
		Check("拍完照那排点还在(那排点本来就在拍照范围外)", frameworkElement6.Visibility == Visibility.Visible);
		_w.Close();
		static double Span(List<double> v)
		{
			return v.Max() - v.Min();
		}
	}

	private static void Diag3()
	{
		Console.WriteLine("=== DIAG3: 真实每一页几行 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(800);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		_ = (List<SetPage>)value.GetType().GetField("Pages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		string[] array = new string[15]
		{
			"PageRoot", "PageVer", "PageFxStyle", "PageTier", "PageSkin", "PageKey", "PageKey9Num", "PageSwap", "PageSlide", "PageDwell",
			"PageWake", "PageWakeKb", "PageWakePad", "PageKeep", "PageAbout"
		};
		foreach (string text in array)
		{
			MethodInfo method = typeof(OverlayWindow).GetMethod(text, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				Console.WriteLine("  " + text + ": 没这个方法");
				continue;
			}
			SetPage setPage = (SetPage)method.Invoke(_w, null);
			setPage.Cells = setPage.Build();
			setPage.Invalidate();
			Console.WriteLine($"  {text,-14} Nine={setPage.Nine,-5} 格={setPage.Cells.Count,2} 行={SetNav.RowsOf(setPage)} 页数={SetNav.PagesOf(setPage)}");
		}
		_w.Close();
	}

	private static void Diag4()
	{
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f4: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== DIAG4: 真实进层(根 -> 动效) ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(800);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		FrameworkElement frameworkElement = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		FrameworkElement frameworkElement2 = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(26, 6);
		defaultInterpolatedStringHandler.AppendLiteral("  根页: Stage ");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualWidth, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral("x");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualHeight, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" 顶=");
		Point val = frameworkElement.TranslatePoint(new Point(0.0, 0.0), _w);
		defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" | 面板 ");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualWidth, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral("x");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualHeight, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" 顶=");
		val = frameworkElement2.TranslatePoint(new Point(0.0, 0.0), _w);
		defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
		Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		SetPage setPage = (SetPage)Call("PageFxStyle");
		Call("PushPage", value, setPage, true, -1);
		for (int i = 1; i <= 8; i++)
		{
			Pump(35);
			FrameworkElement frameworkElement3 = (FrameworkElement)type.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			FrameworkElement frameworkElement4 = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			FrameworkElement frameworkElement5 = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			double num;
			if (frameworkElement3 != null)
			{
				val = frameworkElement3.TranslatePoint(new Point(0.0, 0.0), _w);
				num = val.Y;
			}
			else
			{
				num = -999.0;
			}
			double num2 = num;
			double num3;
			if (frameworkElement4 != null)
			{
				val = frameworkElement4.TranslatePoint(new Point(0.0, 0.0), _w);
				num3 = val.Y;
			}
			else
			{
				num3 = -999.0;
			}
			double num4 = num3;
			Console.WriteLine($"  t={i * 35,3}ms Stage {frameworkElement5.ActualWidth:0.#}x{frameworkElement5.ActualHeight:0.#} | 旧页 顶={num2:0.#} 高={frameworkElement3?.ActualHeight ?? (-1.0):0.#} X={SlideX(frameworkElement3),7:0.#} | 新页 顶={num4:0.#} 高={frameworkElement4?.ActualHeight ?? (-1.0):0.#} X={SlideX(frameworkElement4),7:0.#} | 顶差={num2 - num4:0.#}");
		}
		_w.Close();
	}

	private static void Diag6()
	{
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0142: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		//IL_0185: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0206: Unknown result type (might be due to invalid IL or missing references)
		//IL_020b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0350: Unknown result type (might be due to invalid IL or missing references)
		//IL_0355: Unknown result type (might be due to invalid IL or missing references)
		//IL_035a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0374: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0400: Unknown result type (might be due to invalid IL or missing references)
		//IL_0410: Unknown result type (might be due to invalid IL or missing references)
		//IL_0430: Unknown result type (might be due to invalid IL or missing references)
		//IL_0432: Unknown result type (might be due to invalid IL or missing references)
		//IL_0434: Unknown result type (might be due to invalid IL or missing references)
		//IL_0439: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_04bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0517: Unknown result type (might be due to invalid IL or missing references)
		//IL_0521: Unknown result type (might be due to invalid IL or missing references)
		//IL_0526: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== DIAG6: 符号网格换分类 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		FrameworkElement frameworkElement = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement frameworkElement2 = (FrameworkElement)typeof(OverlayWindow).GetField("FaceVeil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement frameworkElement3 = (FrameworkElement)typeof(OverlayWindow).GetField("PunctDots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		InputCore inputCore = new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			Punct = true
		};
		inputCore.Focus = FocusZone.CandGrid;
		_w.Render(inputCore);
		Pump(700);
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 7);
		defaultInterpolatedStringHandler.AppendLiteral("  网格开: KeyArea 顶=");
		Point val = frameworkElement.TranslatePoint(new Point(0.0, 0.0), _w);
		defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" 底=");
		val = frameworkElement.TranslatePoint(new Point(0.0, 0.0), _w);
		defaultInterpolatedStringHandler.AppendFormatted(val.Y + frameworkElement.ActualHeight, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" ");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualWidth, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral("x");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualHeight, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" | 点 顶=");
		val = frameworkElement3.TranslatePoint(new Point(0.0, 0.0), _w);
		defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" 见=");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement3.Visibility);
		defaultInterpolatedStringHandler.AppendLiteral(" 个数=");
		defaultInterpolatedStringHandler.AppendFormatted(((StackPanel)frameworkElement3).Children.Count);
		Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		UIElement uIElement = (UIElement)(object)frameworkElement.Parent;
		Console.WriteLine($"  爹 = {(uIElement as FrameworkElement)?.Name ?? "?"}  {(uIElement as FrameworkElement)?.ActualWidth:0.#}x{(uIElement as FrameworkElement)?.ActualHeight:0.#}");
		Rect val2 = frameworkElement3.TransformToVisual(uIElement).TransformBounds(new Rect(0.0, 0.0, frameworkElement3.ActualWidth, frameworkElement3.ActualHeight));
		Console.WriteLine($"  点在爹里的位置 = {val2}");
		inputCore.NextPunctCategory(1);
		_w.Render(inputCore);
		Pump(35);
		Rect val3 = uIElement.Clip?.Bounds ?? Rect.Empty;
		Console.WriteLine($"  演的时候屏幕框 = {val3}");
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(21, 2);
		defaultInterpolatedStringHandler.AppendLiteral("  >> 点在框里吗 = ");
		defaultInterpolatedStringHandler.AppendFormatted(val3.Contains(val2.TopLeft) && val3.Contains(val2.BottomRight));
		defaultInterpolatedStringHandler.AppendLiteral(" | 相交 = ");
		Rect val4 = Rect.Intersect(val3, val2);
		defaultInterpolatedStringHandler.AppendFormatted(!val4.IsEmpty);
		Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		for (int i = 1; i <= 10; i++)
		{
			_w.Render(inputCore);
			Pump(35);
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(48, 11);
			defaultInterpolatedStringHandler.AppendLiteral("  t=");
			defaultInterpolatedStringHandler.AppendFormatted(i * 35, 3);
			defaultInterpolatedStringHandler.AppendLiteral("ms KeyArea 顶=");
			val = frameworkElement.TranslatePoint(new Point(0.0, 0.0), _w);
			defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" X=");
			defaultInterpolatedStringHandler.AppendFormatted(SlideX(frameworkElement), 7, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" | 照片 顶=");
			val = frameworkElement2.TranslatePoint(new Point(0.0, 0.0), _w);
			defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" X=");
			defaultInterpolatedStringHandler.AppendFormatted(SlideX(frameworkElement2), 7, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" ");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualWidth, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral("x");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualHeight, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" 见=");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.Visibility);
			defaultInterpolatedStringHandler.AppendLiteral(" | 点=");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement3.Visibility, -9);
			defaultInterpolatedStringHandler.AppendLiteral(" 个数=");
			defaultInterpolatedStringHandler.AppendFormatted(((StackPanel)frameworkElement3).Children.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" 类=");
			defaultInterpolatedStringHandler.AppendFormatted(inputCore.PunctCat);
			Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		_w.Close();
	}

	private static void Diag7()
	{
		//IL_0277: Unknown result type (might be due to invalid IL or missing references)
		//IL_0281: Unknown result type (might be due to invalid IL or missing references)
		//IL_0286: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0303: Unknown result type (might be due to invalid IL or missing references)
		//IL_0308: Unknown result type (might be due to invalid IL or missing references)
		//IL_0460: Unknown result type (might be due to invalid IL or missing references)
		//IL_046a: Unknown result type (might be due to invalid IL or missing references)
		//IL_046f: Unknown result type (might be due to invalid IL or missing references)
		//IL_04e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f1: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== DIAG7: 真实每一页摆上去有多高 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(800);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		string[] array = new string[15]
		{
			"PageRoot", "PageVer", "PageFxStyle", "PageTier", "PageSkin", "PageKey", "PageKey9Num", "PageSwap", "PageSlide", "PageDwell",
			"PageWake", "PageWakeKb", "PageWakePad", "PageKeep", "PageAbout"
		};
		foreach (string text in array)
		{
			MethodInfo method = typeof(OverlayWindow).GetMethod(text, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				Console.WriteLine("  " + text + ": 没这个方法");
				continue;
			}
			SetPage setPage = (SetPage)method.Invoke(_w, null);
			Call("PushPage", value, setPage, false, -1);
			Pump(300);
			FrameworkElement frameworkElement = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			FrameworkElement frameworkElement2 = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 9);
			defaultInterpolatedStringHandler.AppendLiteral("  ");
			defaultInterpolatedStringHandler.AppendFormatted<string>(text, -14);
			defaultInterpolatedStringHandler.AppendLiteral(" Stage ");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualWidth, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral("x");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualHeight, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" 顶=");
			Point val = frameworkElement.TranslatePoint(new Point(0.0, 0.0), _w);
			defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" | 面板 ");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualWidth, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral("x");
			defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualHeight, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" 顶=");
			val = frameworkElement2.TranslatePoint(new Point(0.0, 0.0), _w);
			defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
			defaultInterpolatedStringHandler.AppendLiteral(" | 行=");
			defaultInterpolatedStringHandler.AppendFormatted(SetNav.RowsOf(setPage));
			defaultInterpolatedStringHandler.AppendLiteral(" 页数=");
			defaultInterpolatedStringHandler.AppendFormatted(SetNav.PagesOf(setPage));
			Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
			if (SetNav.PagesOf(setPage) > 1)
			{
				typeof(OverlayWindow).GetMethod("FlipPage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(_w, new object[2] { value, 1 });
				Pump(400);
				FrameworkElement frameworkElement3 = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
				FrameworkElement frameworkElement4 = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(39, 8);
				defaultInterpolatedStringHandler.AppendLiteral("      └ 第 ");
				defaultInterpolatedStringHandler.AppendFormatted(setPage.Page + 1);
				defaultInterpolatedStringHandler.AppendLiteral(" 页: Stage ");
				defaultInterpolatedStringHandler.AppendFormatted(frameworkElement4.ActualWidth, "0.#");
				defaultInterpolatedStringHandler.AppendLiteral("x");
				defaultInterpolatedStringHandler.AppendFormatted(frameworkElement4.ActualHeight, "0.#");
				defaultInterpolatedStringHandler.AppendLiteral(" 顶=");
				val = frameworkElement4.TranslatePoint(new Point(0.0, 0.0), _w);
				defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
				defaultInterpolatedStringHandler.AppendLiteral(" | 面板 ");
				defaultInterpolatedStringHandler.AppendFormatted(frameworkElement3.ActualWidth, "0.#");
				defaultInterpolatedStringHandler.AppendLiteral("x");
				defaultInterpolatedStringHandler.AppendFormatted(frameworkElement3.ActualHeight, "0.#");
				defaultInterpolatedStringHandler.AppendLiteral(" 顶=");
				val = frameworkElement3.TranslatePoint(new Point(0.0, 0.0), _w);
				defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
				defaultInterpolatedStringHandler.AppendLiteral(" | 行=");
				defaultInterpolatedStringHandler.AppendFormatted(SetNav.RowsOf(setPage));
				Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		_w.Close();
	}

	private static void Diag8()
	{
		Console.WriteLine("=== DIAG8: 换屏前后卡片挪没挪 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		FrameworkElement ka = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement cand = (FrameworkElement)typeof(OverlayWindow).GetField("CandRow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		InputCore inputCore = new InputCore
		{
			Layout = Layout.CN9,
			Visible = true
		};
		_w.Render(inputCore);
		Pump(500);
		Say("九键");
		inputCore.Punct9 = true;
		inputCore.Focus = FocusZone.CandGrid;
		inputCore.Punct = true;
		_w.Render(inputCore);
		Pump(500);
		Say("开符号网格");
		inputCore.Punct9 = false;
		inputCore.Focus = FocusZone.Keyboard;
		inputCore.Punct = false;
		_w.Render(inputCore);
		Pump(500);
		Say("关符号网格");
		Call("ShowSide", true);
		Pump(900);
		Say("进设置");
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		SetPage setPage = (SetPage)Call("PageFxStyle");
		Call("PushPage", value, setPage, true, -1);
		Pump(900);
		Say("设置 -> 动效");
		_w.Close();
		void Say(string tag)
		{
			Console.WriteLine($"  {tag,-22} KeyArea顶={Top():0.#} 窗口 {_w.ActualWidth:0.#}x{_w.ActualHeight:0.#} 顶={_w.Top:0.#} | 候选行见={cand.Visibility,-9} 高={cand.ActualHeight:0.#}");
		}
		double Top()
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			Point val = ka.TranslatePoint(new Point(0.0, 0.0), _w);
			return val.Y;
		}
	}

	[STAThread]
	private static int Main(string[] argv)
	{
		Console.SetOut(new StreamWriter(Console.OpenStandardOutput())
		{
			AutoFlush = true
		});
		Application application = new Application
		{
			ShutdownMode = ShutdownMode.OnExplicitShutdown
		};
		string text = Path.Combine(Path.GetTempPath(), "uiprobe");
		Directory.CreateDirectory(text);
		Config.SetDirForTest(text);
		if (argv.Length != 0 && argv[0] == "rel")
		{
			RelSnapshot();
			application.Shutdown();
			return _fail;
		}
		if (argv.Length != 0 && argv[0] == "diag")
		{
			try
			{
				Diag2();
			}
			catch (Exception ex)
			{
				Console.WriteLine("  CRASH  Diag2: " + ex);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag3")
		{
			try
			{
				Diag3();
			}
			catch (Exception ex2)
			{
				Console.WriteLine("  CRASH  Diag3: " + ex2);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag4")
		{
			try
			{
				Diag4();
			}
			catch (Exception ex3)
			{
				Console.WriteLine("  CRASH  Diag4: " + ex3);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag5")
		{
			try
			{
				Diag();
			}
			catch (Exception ex4)
			{
				Console.WriteLine("  CRASH  Diag: " + ex4);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag6")
		{
			try
			{
				Diag6();
			}
			catch (Exception ex5)
			{
				Console.WriteLine("  CRASH  Diag6: " + ex5);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag7")
		{
			try
			{
				Diag7();
			}
			catch (Exception ex6)
			{
				Console.WriteLine("  CRASH  Diag7: " + ex6);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag8")
		{
			try
			{
				Diag8();
			}
			catch (Exception ex7)
			{
				Console.WriteLine("  CRASH  Diag8: " + ex7);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag9")
		{
			try
			{
				Diag9();
			}
			catch (Exception ex8)
			{
				Console.WriteLine("  CRASH  Diag9: " + ex8);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag10")
		{
			try
			{
				Diag10();
			}
			catch (Exception ex9)
			{
				Console.WriteLine("  CRASH  Diag10: " + ex9);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag11")
		{
			try
			{
				Diag11();
			}
			catch (Exception ex10)
			{
				Console.WriteLine("  CRASH  Diag11: " + ex10);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag12")
		{
			try
			{
				Diag12();
			}
			catch (Exception ex11)
			{
				Console.WriteLine("  CRASH  Diag12: " + ex11);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag13")
		{
			try
			{
				Diag13();
			}
			catch (Exception ex12)
			{
				Console.WriteLine("  CRASH  Diag13: " + ex12);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag14")
		{
			try
			{
				Diag14();
			}
			catch (Exception ex13)
			{
				Console.WriteLine("  CRASH  Diag14: " + ex13);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag15")
		{
			try
			{
				Diag15();
			}
			catch (Exception ex14)
			{
				Console.WriteLine("  CRASH  Diag15: " + ex14);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag16")
		{
			try
			{
				Diag16();
			}
			catch (Exception ex15)
			{
				Console.WriteLine("  CRASH  Diag16: " + ex15);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag17")
		{
			try
			{
				Diag17();
			}
			catch (Exception ex16)
			{
				Console.WriteLine("  CRASH  Diag17: " + ex16);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		if (argv.Length != 0 && argv[0] == "diag18")
		{
			try
			{
				Diag18();
			}
			catch (Exception ex17)
			{
				Console.WriteLine("  CRASH  Diag18: " + ex17);
				_fail++;
			}
			application.Shutdown();
			return 0;
		}
		// 单跑新增那一组(改土豪金 / 彩蛋时不用等全套三分钟)
		if (argv.Length != 0 && argv[0] == "gold")
		{
			Scene("土豪金", GoldSkin);
			application.Shutdown();
			return _fail;
		}
		if (argv.Length != 0 && argv[0] == "egg")
		{
			Scene("彩蛋·连点", EggTap);
			application.Shutdown();
			return _fail;
		}
		// 单跑「调窗口」那一组(2.3.3)
		if (argv.Length != 0 && argv[0] == "resize")
		{
			Scene("调窗口", ResizeMode);
			application.Shutdown();
			return _fail;
		}
		Scene("拼音行底", PinyinBg);
		Scene("数字0", NumZero);
		Scene("数字层动效", NumLayerFx);
		Scene("零闪", ZeroFlicker);
		Scene("档位", TierSteps);
		Scene("投递门", DeliverGate);
		Scene("提示升起", NoteRise);
		Scene("全键盘设置宽", En26SetWidth);
		Scene("关系快照", RelSnapshot);
		Scene("圆形键", KeyRound);
		Scene("圆形键·深层", KeyRoundDeep);
		Scene("土豪金", GoldSkin);
		Scene("彩蛋·连点", EggTap);
		Scene("调窗口", ResizeMode);
		if (argv.Length == 0 || !(argv[0] == "plain"))
		{
			Scene("默认(朴素)", delegate
			{
				One("默认(朴素)", FxStyle.Plain);
			});
		}
		if (argv.Length == 0 || !(argv[0] == "spin"))
		{
			Scene("旋转", delegate
			{
				One("旋转", FxStyle.Spin);
			});
		}
		if (argv.Length == 0 || !(argv[0] == "slide"))
		{
			Scene("手机", delegate
			{
				One("手机", FxStyle.Slide);
			});
		}
		if (argv.Length == 0 || !(argv[0] == "slide"))
		{
			Scene("手机·收敛", SlideSettle);
		}
		if (argv.Length == 0 || !(argv[0] == "slide"))
		{
			Scene("手机·接着滑", SlideResume);
		}
		if (argv.Length == 0 || !(argv[0] == "slide"))
		{
			Scene("预览框下沉·按LB", Diag16);
		}
		if (argv.Length == 0 || !(argv[0] == "slide"))
		{
			Scene("浮力·键漂页面不漂", Diag17);
		}
		if (argv.Length == 0 || !(argv[0] == "slide"))
		{
			Scene("浮力·每屏都漂", Diag18);
		}
		if (argv.Length != 0 && argv[0] == "sl")
		{
			Scene("手机·进层", SlideLevel);
			application.Shutdown();
			return _fail;
		}
		if (argv.Length != 0 && argv[0] == "sp")
		{
			Scene("手机·照片", SlidePhoto);
			application.Shutdown();
			return _fail;
		}
		if (argv.Length != 0 && argv[0] == "fs")
		{
			Scene("换面不挪", FlipSteady);
			application.Shutdown();
			return _fail;
		}
		if (argv.Length != 0 && argv[0] == "sr")
		{
			Scene("手机·接着滑", SlideResume);
			application.Shutdown();
			return _fail;
		}
		if (argv.Length != 0 && argv[0] == "ss")
		{
			Scene("手机·收敛", SlideSettle);
			application.Shutdown();
			return _fail;
		}
		if (argv.Length == 0 || !(argv[0] == "slide"))
		{
			Scene("手机·进层", SlideLevel);
		}
		if (argv.Length == 0 || !(argv[0] == "slide"))
		{
			Scene("手机·照片", SlidePhoto);
		}
		if (argv.Length == 0 || !(argv[0] == "slide"))
		{
			Scene("换面不挪", FlipSteady);
		}
		application.Shutdown();
		Console.WriteLine((_fail == 0) ? "全部通过" : $"有 {_fail} 条没过");
		return (_fail != 0) ? 1 : 0;
		static void Scene(string tag, Action body)
		{
			Console.WriteLine("--- 场景 " + tag + " ---");
			int fail = _fail;
			try
			{
				body();
			}
			catch (Exception ex18)
			{
				_fail++;
				Console.WriteLine($"  CRASH  {tag}: {ex18.GetType().Name}: {ex18.Message}");
				string stackTrace = ex18.StackTrace;
				Console.WriteLine("         " + ((stackTrace == null) ? null : stackTrace.Split('\n')[0]?.Trim()));
				try
				{
					_w?.Close();
				}
				catch
				{
				}
			}
			Console.WriteLine($"  ({tag}: {((fail == _fail) ? "全过" : $"{_fail - fail} 条没过")})");
		}
	}

	private static void SlidePhoto()
	{
		Console.WriteLine("=== 平移: 旧页照片和真身严丝合缝(不许先掉一截) ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		FrameworkElement el = FE("KeyArea");
		FE("FaceVeil");
		InputCore inputCore = new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			Punct = true,
			Focus = FocusZone.CandGrid
		};
		_w.Render(inputCore);
		Pump(700);
		BitmapSource a = Crop(el);
		inputCore.NextPunctCategory(1);
		_w.Render(inputCore);
		Pump(15);
		BitmapSource b = ((Image)FE("FaceVeilImg")).Source as BitmapSource;
		var (num, num2) = BestShift(a, b);
		Check("正面: 旧页照片就摆在真身原处(不用挪就对得上)", num == 0 && num2 == 0, $"(对得最齐要挪 ({num},{num2}); 逐像素差 {Diff(a, b):P2})");
		Pump(700);
		Call("ShowSide", true);
		Pump(800);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		FrameworkElement el2 = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		SetPage setPage = (SetPage)Call("PageFxStyle");
		BitmapSource a2 = Crop(el2);
		Call("PushPage", value, setPage, true, -1);
		Pump(15);
		BitmapSource b2 = ((Image)type.GetField("VeilImg", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value))?.Source as BitmapSource;
		var (num3, num4) = BestShift(a2, b2);
		Check("设置: 旧页照片就摆在真身原处(不用挪就对得上)", num3 == 0 && num4 == 0, $"(对得最齐要挪 ({num3},{num4}); 逐像素差 {Diff(a2, b2):P2})");
		_w.Close();
	}

	private static void FlipSteady()
	{
		Console.WriteLine("=== 换面: 九键 <-> 设置 全程不许挪 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		FrameworkElement root = FE("Root");
		FrameworkElement back = FE("Back");
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		FrameworkElement st = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		FrameworkElement pn = (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		Call("ShowSide", true);
		Pump(900);
		Call("ShowSide", false);
		Pump(900);
		(string, bool)[] array = new(string, bool)[2]
		{
			("进设置", true),
			("回九键", false)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, bool) tuple = array[i];
			string item = tuple.Item1;
			bool item2 = tuple.Item2;
			double[] array2 = Sample();
			List<double> list = new List<double>();
			List<double> list2 = new List<double>();
			List<double> list3 = new List<double>();
			Call("ShowSide", item2);
			for (int j = 0; j < 40; j++)
			{
				Pump(10);
				double[] array3 = Sample();
				list.Add(array3[3]);
				list2.Add(array3[2]);
				if (!double.IsNaN(array3[4]))
				{
					list3.Add(array3[4]);
				}
			}
			Pump(600);
			double[] array4 = Sample();
			Check("换面·" + item + ": 舞台从头到尾一样高(照片槽不许撑高它)", Sp(list) < 0.5, $"(高 {Sp(list):0.#} 跨度; 采到 {list.Count} 帧)");
			Check("换面·" + item + ": 内容区顶边钉死不动", Sp(list2) < 0.5, $"(顶边跨度 {Sp(list2):0.#})");
			if (list3.Count > 0)
			{
				Check("换面·" + item + ": 那一页自己的顶边也钉死不动", Sp(list3) < 0.5, $"(面板顶跨度 {Sp(list3):0.#}, 采到 {list3.Count} 帧)");
			}
			Check("换面·" + item + ": 窗口在屏幕上没挪位(卡片顶 + 窗口顶都原地)", Math.Abs(array4[0] - array2[0]) < 0.5 && Math.Abs(array4[1] - array2[1]) < 0.5, $"(窗口顶 {array2[0]:0.#}->{array4[0]:0.#}, 卡片顶 {array2[1]:0.#}->{array4[1]:0.#})");
		}
		_w.Close();
		double[] Sample()
		{
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_0058: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0094: Unknown result type (might be due to invalid IL or missing references)
			FrameworkElement el = ((back.Visibility == Visibility.Visible) ? back : root);
			double[] array5 = new double[5];
			Point val = Scr(_w);
			array5[0] = val.Y;
			val = Scr(el);
			array5[1] = val.Y;
			val = Scr(st);
			array5[2] = val.Y;
			array5[3] = st.ActualHeight;
			double num;
			if (pn.Visibility != 0)
			{
				num = double.NaN;
			}
			else
			{
				val = Scr(pn);
				num = val.Y;
			}
			array5[4] = num;
			return array5;
		}
		static double Sp(List<double> v)
		{
			return v.Max() - v.Min();
		}
	}

	private static BitmapSource Crop(FrameworkElement el)
	{
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		if (el == null)
		{
			return null;
		}
		int num = (int)Math.Round(el.ActualWidth);
		int num2 = (int)Math.Round(el.ActualHeight);
		int num3 = (int)Math.Round(_w.ActualWidth);
		int num4 = (int)Math.Round(_w.ActualHeight);
		if (num < 4 || num2 < 4)
		{
			return null;
		}
		Rect val = el.TransformToVisual(_w).TransformBounds(new Rect(0.0, 0.0, el.ActualWidth, el.ActualHeight));
		int num5 = (int)Math.Round(val.X);
		int num6 = (int)Math.Round(val.Y);
		if (num5 < 0 || num6 < 0 || num5 + num > num3 || num6 + num2 > num4)
		{
			return null;
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(num3, num4, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(_w);
		return new CroppedBitmap(renderTargetBitmap, new Int32Rect(num5, num6, num, num2));
	}

	private static (int dx, int dy) BestShift(BitmapSource a, BitmapSource b)
	{
		if (a == null || b == null)
		{
			return (dx: 999, dy: 999);
		}
		int pixelWidth = a.PixelWidth;
		int pixelHeight = a.PixelHeight;
		if (pixelWidth != b.PixelWidth || pixelHeight != b.PixelHeight)
		{
			return (dx: 999, dy: 999);
		}
		byte[] array = new byte[pixelWidth * pixelHeight * 4];
		byte[] array2 = new byte[pixelWidth * pixelHeight * 4];
		a.CopyPixels(array, pixelWidth * 4, 0);
		b.CopyPixels(array2, pixelWidth * 4, 0);
		int item = 999;
		long num = long.MaxValue;
		for (int i = -40; i <= 40; i++)
		{
			long num2 = 0L;
			int num3 = 0;
			for (int j = 0; j < pixelHeight; j++)
			{
				int num4 = j + i;
				if (num4 < 0 || num4 >= pixelHeight)
				{
					continue;
				}
				for (int k = 0; k < pixelWidth; k++)
				{
					if (Far(array, array2, (j * pixelWidth + k) * 4, (num4 * pixelWidth + k) * 4))
					{
						num2++;
					}
				}
				num3++;
			}
			if (!((double)num3 < (double)pixelHeight * 0.8) && num2 < num)
			{
				num = num2;
				item = i;
			}
		}
		int item2 = 999;
		num = long.MaxValue;
		for (int l = -40; l <= 40; l++)
		{
			long num5 = 0L;
			int num6 = 0;
			for (int m = 0; m < pixelWidth; m++)
			{
				int num7 = m + l;
				if (num7 < 0 || num7 >= pixelWidth)
				{
					continue;
				}
				for (int n = 0; n < pixelHeight; n++)
				{
					if (Far(array, array2, (n * pixelWidth + m) * 4, (n * pixelWidth + num7) * 4))
					{
						num5++;
					}
				}
				num6++;
			}
			if (!((double)num6 < (double)pixelWidth * 0.8) && num5 < num)
			{
				num = num5;
				item2 = l;
			}
		}
		return (dx: item2, dy: item);
	}

	private static bool Far(byte[] a, byte[] b, int i, int j)
	{
		if (Math.Abs(a[i] - b[j]) <= 24 && Math.Abs(a[i + 1] - b[j + 1]) <= 24 && Math.Abs(a[i + 2] - b[j + 2]) <= 24)
		{
			return Math.Abs(a[i + 3] - b[j + 3]) > 24;
		}
		return true;
	}

	private static int[] Ink(BitmapSource b, bool byColumn)
	{
		int pixelWidth = b.PixelWidth;
		int pixelHeight = b.PixelHeight;
		byte[] array = new byte[pixelWidth * pixelHeight * 4];
		b.CopyPixels(array, pixelWidth * 4, 0);
		int[] array2 = new int[byColumn ? pixelWidth : pixelHeight];
		for (int i = 0; i < pixelHeight; i++)
		{
			for (int j = 0; j < pixelWidth; j++)
			{
				if (array[(i * pixelWidth + j) * 4 + 3] > 8)
				{
					array2[byColumn ? j : i]++;
				}
			}
		}
		return array2;
	}

	private static double Diff(BitmapSource a, BitmapSource b)
	{
		if (a == null || b == null)
		{
			return 1.0;
		}
		if (a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight)
		{
			return 1.0;
		}
		int pixelWidth = a.PixelWidth;
		int pixelHeight = a.PixelHeight;
		int num = pixelWidth * pixelHeight;
		byte[] array = new byte[num * 4];
		byte[] array2 = new byte[num * 4];
		a.CopyPixels(array, pixelWidth * 4, 0);
		b.CopyPixels(array2, pixelWidth * 4, 0);
		int num2 = 0;
		for (int i = 0; i < num; i++)
		{
			int num3 = i * 4;
			if (Math.Abs(array[num3] - array2[num3]) > 24 || Math.Abs(array[num3 + 1] - array2[num3 + 1]) > 24 || Math.Abs(array[num3 + 2] - array2[num3 + 2]) > 24 || Math.Abs(array[num3 + 3] - array2[num3 + 3]) > 24)
			{
				num2++;
			}
		}
		return (double)num2 / (double)num;
	}

	private static void Diag9()
	{
		Console.WriteLine("=== DIAG9: 屏幕框 vs 真身 —— 切掉多少 = 看着下移多少 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(800);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		List<SetPage> obj = (List<SetPage>)type.GetField("Pages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		obj.Clear();
		SetPage setPage = new SetPage
		{
			Title = "多页",
			Nine = true,
			Rows = 3
		};
		setPage.Build = delegate
		{
			List<SetCell> list = new List<SetCell>();
			for (int i = 0; i < 12; i++)
			{
				list.Add(SetNav.Cell("格" + i));
			}
			return list;
		};
		obj.Add(setPage);
		Call("ShowPage", value, true, -1, 1);
		Pump(600);
		Call("FlipPage", value, 1);
		Pump(35);
		Measure("设置那一摞·翻页", (FrameworkElement)type.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value), (FrameworkElement)type.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value), (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value));
		Pump(900);
		Call("ShowSide", false);
		Pump(800);
		_w.Render(new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			Punct = false
		});
		Pump(300);
		InputCore c = new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			Punct = true,
			Focus = FocusZone.CandGrid
		};
		_w.Render(c);
		Pump(35);
		FrameworkElement frameworkElement = (FrameworkElement)typeof(OverlayWindow).GetField("KeyArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement photo = (FrameworkElement)typeof(OverlayWindow).GetField("FaceVeil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Measure("正面·换面", photo, frameworkElement, (FrameworkElement)(object)frameworkElement.Parent);
		Pump(900);
		_w.Close();
	}

	private static void Diag13()
	{
		Console.WriteLine("=== DIAG13: 设置那一摞, 照片 vs 真身, 差在哪 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		Call("ShowSide", true);
		Pump(800);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		FrameworkElement el = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		SetPage setPage = (SetPage)Call("PageFxStyle");
		BitmapSource bitmapSource = Crop(el);
		Save(bitmapSource, "G_设置真身");
		Call("PushPage", value, setPage, true, -1);
		Pump(15);
		BitmapSource b = ((Image)type.GetField("VeilImg", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value))?.Source as BitmapSource;
		Save(b, "H_设置照片");
		Console.WriteLine($"  逐像素差 {Diff(bitmapSource, b):P2}   {Where(bitmapSource, b)}");
		_w.Close();
	}

	private static string Where(BitmapSource a, BitmapSource b)
	{
		if (a == null || b == null)
		{
			return "(有张图没拿到)";
		}
		if (a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight)
		{
			return "(尺寸都不一样)";
		}
		int pixelWidth = a.PixelWidth;
		int pixelHeight = a.PixelHeight;
		int num = pixelWidth * pixelHeight;
		byte[] array = new byte[num * 4];
		byte[] array2 = new byte[num * 4];
		a.CopyPixels(array, pixelWidth * 4, 0);
		b.CopyPixels(array2, pixelWidth * 4, 0);
		int num2 = int.MaxValue;
		int num3 = int.MaxValue;
		int num4 = -1;
		int num5 = -1;
		int num6 = 0;
		int[] rows = new int[pixelHeight];
		for (int i = 0; i < pixelHeight; i++)
		{
			for (int j = 0; j < pixelWidth; j++)
			{
				int num7 = (i * pixelWidth + j) * 4;
				if (Math.Abs(array[num7] - array2[num7]) > 24 || Math.Abs(array[num7 + 1] - array2[num7 + 1]) > 24 || Math.Abs(array[num7 + 2] - array2[num7 + 2]) > 24 || Math.Abs(array[num7 + 3] - array2[num7 + 3]) > 24)
				{
					num6++;
					rows[i]++;
					if (j < num2)
					{
						num2 = j;
					}
					if (j > num4)
					{
						num4 = j;
					}
					if (i < num3)
					{
						num3 = i;
					}
					if (i > num5)
					{
						num5 = i;
					}
				}
			}
		}
		if (num6 == 0)
		{
			return "一模一样";
		}
		IEnumerable<string> values = from y in (from y in Enumerable.Range(0, pixelHeight)
				orderby rows[y] descending
				select y).Take(4)
			select $"第{y}行({rows[y]})";
		return $"差的像素 {num6} 个, 挤在 x{num2}..{num4} y{num3}..{num5}; 最多: {string.Join(" ", values)}";
	}

	private static void Save(BitmapSource b, string name)
	{
		if (b == null)
		{
			Console.WriteLine("  " + name + ": 没图");
			return;
		}
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(b));
		Directory.CreateDirectory("D:\\_diag");
		using FileStream stream = new FileStream(Path.Combine("D:\\_diag", name + ".png"), FileMode.Create);
		pngBitmapEncoder.Save(stream);
	}

	private static void Diag12()
	{
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_016c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== DIAG12: 拍照会不会把元素在爹里的位置算进去 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		FrameworkElement frameworkElement = FE("KeyArea");
		_w.Render(new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			Punct = true,
			Focus = FocusZone.CandGrid
		});
		Pump(600);
		Visual visual = (Visual)(object)frameworkElement.Parent;
		Rect val = frameworkElement.TransformToVisual(visual).TransformBounds(new Rect(0.0, 0.0, frameworkElement.ActualWidth, frameworkElement.ActualHeight));
		Console.WriteLine("  内容区在爹里的位置: " + val.ToString());
		Shot(frameworkElement, "E_原位");
		frameworkElement.Margin = new Thickness(0.0, 40.0, 0.0, 0.0);
		_w.UpdateLayout();
		Pump(80);
		val = frameworkElement.TransformToVisual(visual).TransformBounds(new Rect(0.0, 0.0, frameworkElement.ActualWidth, frameworkElement.ActualHeight));
		Console.WriteLine("  下移 40 之后:       " + val.ToString());
		Shot(frameworkElement, "F_下移40");
		_w.Close();
	}

	private static void Diag11()
	{
		Console.WriteLine("=== DIAG11: 照片 vs 真身 —— 像素级 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		FrameworkElement el = FE("KeyArea");
		FrameworkElement el2 = FE("FaceVeil");
		InputCore inputCore = new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			Punct = true,
			Focus = FocusZone.CandGrid
		};
		_w.Render(inputCore);
		Pump(700);
		Console.WriteLine("--- 正面·换标点分类 ---");
		Shot(el, "A_正面真身");
		inputCore.NextPunctCategory(1);
		_w.Render(inputCore);
		Pump(15);
		Shot(el2, "B_正面照片");
		Call("ShowSide", true);
		Pump(800);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type type = value.GetType();
		FrameworkElement el3 = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		SetPage setPage = (SetPage)Call("PageFxStyle");
		Console.WriteLine("--- 设置·进第二层 ---");
		Shot(el3, "C_设置真身");
		Call("PushPage", value, setPage, true, -1);
		Pump(15);
		Shot((FrameworkElement)type.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value), "D_设置照片");
		_w.Close();
	}

	private static void Shot(FrameworkElement el, string name)
	{
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		if (el == null)
		{
			Console.WriteLine("  " + name + ": 元素没拿到");
			return;
		}
		int num = (int)Math.Round(el.ActualWidth);
		int num2 = (int)Math.Round(el.ActualHeight);
		if (num < 4 || num2 < 4)
		{
			Console.WriteLine($"  {name}: 尺寸 {num}x{num2} 太小");
			return;
		}
		int num3 = (int)Math.Round(_w.ActualWidth);
		int num4 = (int)Math.Round(_w.ActualHeight);
		Rect val = el.TransformToVisual(_w).TransformBounds(new Rect(0.0, 0.0, el.ActualWidth, el.ActualHeight));
		int num5 = (int)Math.Round(val.X);
		int num6 = (int)Math.Round(val.Y);
		if (num5 < 0 || num6 < 0 || num5 + num > num3 || num6 + num2 > num4)
		{
			Console.WriteLine($"  {name}: 跑出窗口外了 ({num5},{num6}) {num}x{num2}");
			return;
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(num3, num4, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(_w);
		CroppedBitmap croppedBitmap = new CroppedBitmap(renderTargetBitmap, new Int32Rect(num5, num6, num, num2));
		Console.WriteLine($"  {name}: 窗口里 @({num5},{num6}) {num}x{num2}  {Edges(croppedBitmap)}");
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(croppedBitmap));
		Directory.CreateDirectory("D:\\_diag");
		using FileStream stream = new FileStream(Path.Combine("D:\\_diag", name + ".png"), FileMode.Create);
		pngBitmapEncoder.Save(stream);
	}

	private static string Edges(BitmapSource b)
	{
		int pixelWidth = b.PixelWidth;
		int pixelHeight = b.PixelHeight;
		byte[] array = new byte[pixelWidth * pixelHeight * 4];
		b.CopyPixels(array, pixelWidth * 4, 0);
		int num = -1;
		int value = -1;
		int num2 = -1;
		int num3 = -1;
		for (int i = 0; i < pixelHeight; i++)
		{
			for (int j = 0; j < pixelWidth; j++)
			{
				if (array[(i * pixelWidth + j) * 4 + 3] > 8)
				{
					if (num < 0)
					{
						num = i;
					}
					value = i;
					if (num2 < 0 || j < num2)
					{
						num2 = j;
					}
					if (j > num3)
					{
						num3 = j;
					}
				}
			}
		}
		return $"内容: 上{num} 下{value} 左{num2} 右{num3}";
	}

	private static double NoteSinkSlide(FxStyle fx, int atMs, bool verbose)
	{
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = fx
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		_w.Preview = true;
		Pump(200);
		_w.NoteText = "你好啊";
		for (int i = 0; i < 12; i++)
		{
			Pump(80);
		}
		InputCore inputCore = new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			Focus = FocusZone.Keyboard
		};
		_w.Render(inputCore);
		Pump(80);
		double left = _w.Left;
		TranslateTransform translateTransform = (TranslateTransform)typeof(OverlayWindow).GetField("_noteTt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		FrameworkElement e = FE("KeyArea");
		FrameworkElement frameworkElement = FE("FaceVeil");
		if (verbose)
		{
			Console.WriteLine($"  起手: Left={left,7:0.#} 宽={_w.ActualWidth:0.#} 高={_w.ActualHeight:0.#}");
		}
		_w.NoteText = "";
		double num = 0.0;
		double value = 0.0;
		string value2 = "";
		for (int j = 0; j < 40; j++)
		{
			int num2 = j * 16;
			if (atMs >= 0 && num2 >= atMs && !inputCore.Slide.Active)
			{
				inputCore.Slide.Begin(DateTime.Now);
			}
			_w.Render(inputCore);
			Pump(16);
			double num3 = Math.Abs(_w.Left - left);
			if (num3 > num)
			{
				num = num3;
				value = num2;
				value2 = $"Left={_w.Left,7:0.#} 宽={_w.ActualWidth:0.#} 高={_w.ActualHeight:0.#} 键区X={SlideX(e),6:0.#} 照片槽={frameworkElement.Visibility} 槽宽={frameworkElement.ActualWidth,5:0.#} 槽高={frameworkElement.ActualHeight,5:0.#} 沉Y={translateTransform.Y,5:0.#}";
			}
			if (verbose && j % 3 == 0)
			{
				Console.WriteLine($"  t={num2,3}ms Left={_w.Left,7:0.#} (差 {_w.Left - left,6:0.#}) 键区X={SlideX(e),6:0.#} 照片槽={frameworkElement.Visibility} 沉Y={translateTransform.Y,5:0.#} 滑行={inputCore.Slide.Active}");
			}
		}
		if (num > 0.6)
		{
			Console.WriteLine($"  !! 最大偏移 {num:0.#}px @ {value}ms: {value2}");
		}
		if (verbose)
		{
			Console.WriteLine($"  -> 最大偏移 {num:0.#}px");
		}
		_w.Close();
		return num;
	}

	private static void Diag16()
	{
		Console.WriteLine("=== DIAG16: 预览框沉下去的途中按 LB 进滑行, 窗口挪不挪 ===");
		(FxStyle, string)[] array = new(FxStyle, string)[3]
		{
			(FxStyle.Plain, "默认"),
			(FxStyle.Spin, "旋转"),
			(FxStyle.Slide, "手机")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(FxStyle, string) tuple = array[i];
			Console.WriteLine("  --- 风格 " + tuple.Item2 + " ---");
			int[] array2 = new int[4] { 32, 96, 160, -1 };
			foreach (int num in array2)
			{
				double num2 = NoteSinkSlide(tuple.Item1, num, num == 96);
				string text = ((num < 0) ? "不按 LB(对照)" : $"沉到 {num}ms 按 LB");
				Check(tuple.Item2 + " · " + text + ": 窗口不许挪位", num2 < 0.6, $"(最大偏移 {num2:0.#}px)");
			}
		}
	}

	private static void Diag17()
	{
		Console.WriteLine("=== DIAG17: 浮力 —— 九颗键各自漂, 窗口 / 卡片不许跟着挪 ===");
		FakeHost fakeHost = new FakeHost
		{
			Which = FxStyle.Buoy
		};
		_w = new OverlayWindow
		{
			Host = fakeHost,
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		InputCore c = new InputCore
		{
			Layout = Layout.CN9,
			Focus = FocusZone.Keyboard,
			Visible = true
		};
		_w.Render(c);
		Pump(400);
		Grid[] array = (Grid[])typeof(OverlayWindow).GetField("_key9Host", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Snap snap = Grab();
		double num = 0.0;
		double num2 = 0.0;
		double num3 = 0.0;
		double num4 = 0.0;
		double num5 = 0.0;
		for (int i = 0; i < 40; i++)
		{
			_w.Render(c);
			Pump(50);
			Snap snap2 = Grab();
			num = Math.Max(num, Math.Abs(snap2.Left - snap.Left));
			num2 = Math.Max(num2, Math.Abs(snap2.Top - snap.Top));
			num3 = Math.Max(num3, Math.Abs(snap2.FaceX - snap.FaceX));
			num3 = Math.Max(num3, Math.Abs(snap2.FaceY - snap.FaceY));
			double num6 = 0.0;
			double num7 = 1000000000.0;
			double num8 = -1000000000.0;
			for (int j = 1; j <= 9; j++)
			{
				(double, double, double) tuple = Tf(array[j]);
				num6 = Math.Max(num6, Math.Abs(tuple.Item1) + Math.Abs(tuple.Item2));
				num7 = Math.Min(num7, tuple.Item1);
				num8 = Math.Max(num8, tuple.Item1);
			}
			num4 = Math.Max(num4, num6);
			num5 = Math.Max(num5, num8 - num7);
		}
		Console.WriteLine($"  两秒里: 键漂出去最远 {num4:0.#}px, 同一时刻九颗之间最差 {num5:0.#}px");
		Check("浮力: 九颗键确实在漂", num4 > 0.5, $"(最远 {num4:0.#}px)");
		Check("浮力: 不是一块板在晃(九颗各有相位)", num5 > 0.5, $"(最差 {num5:0.#}px)");
		Check("浮力: 窗口不跟着挪", num < 0.6 && num2 < 0.6, $"(Left 抖 {num:0.#}, Top 抖 {num2:0.#})");
		Check("浮力: 卡片不跟着挪", num3 < 0.6, $"(中心抖 {num3:0.#})");
		fakeHost.Which = FxStyle.Plain;
		_w.Render(c);
		Pump(800);
		double num9 = 0.0;
		double num10 = 0.0;
		for (int k = 1; k <= 9; k++)
		{
			(double, double, double) tuple2 = Tf(array[k]);
			num9 = Math.Max(num9, Math.Abs(tuple2.Item1) + Math.Abs(tuple2.Item2));
			num10 = Math.Max(num10, Math.Abs(tuple2.Item3 - 1.0));
		}
		Check("关掉浮力: 九颗键回正中", num9 < 0.05, $"(残留 {num9:0.###}px)");
		Check("关掉浮力: 缩放回到 1", num10 < 0.005, $"(残留 {num10:0.####})");
		fakeHost.Which = FxStyle.Buoy;
		_w.Render(c);
		Pump(500);
		Call("ShowSide", true);
		_w.Render(c);
		Pump(800);
		double num11 = 0.0;
		for (int l = 1; l <= 9; l++)
		{
			(double, double, double) tuple3 = Tf(array[l]);
			num11 = Math.Max(num11, Math.Abs(tuple3.Item1) + Math.Abs(tuple3.Item2));
		}
		Check("进设置: 水波跟着停", num11 < 0.05, $"(残留 {num11:0.###}px)");
		Call("ShowSide", false);
		_w.Render(c);
		Pump(800);
		Snap snap3 = Grab();
		Check("浮力里翻回九键: 窗口还在原处", Math.Abs(snap3.Left - snap.Left) < 0.6 && Math.Abs(snap3.Top - snap.Top) < 0.6, $"(Δ {snap3.Left - snap.Left:0.#}, {snap3.Top - snap.Top:0.#})");
		double num12 = 0.0;
		for (int m = 1; m <= 9; m++)
		{
			(double, double, double) tuple4 = Tf(array[m]);
			num12 = Math.Max(num12, Math.Abs(tuple4.Item1) + Math.Abs(tuple4.Item2));
		}
		Check("翻回九键: 水波重新漂起来", num12 > 0.5, $"(最远 {num12:0.#}px)");
		_w.Close();
		static (double X, double Y, double S) Tf(Grid g)
		{
			TransformGroup obj = g.RenderTransform as TransformGroup;
			ScaleTransform scaleTransform = (ScaleTransform)obj.Children[0];
			TranslateTransform translateTransform = (TranslateTransform)obj.Children[1];
			return (X: translateTransform.X, Y: translateTransform.Y, S: scaleTransform.ScaleX);
		}
	}

	private static void Diag18()
	{
		Console.WriteLine("=== DIAG18: 浮力铺开 —— 九键 / 标点 5×5 / 设置 每屏都在漂 ===");
		FakeHost host = new FakeHost
		{
			Which = FxStyle.Buoy
		};
		_w = new OverlayWindow
		{
			Host = host,
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		InputCore c = new InputCore
		{
			Layout = Layout.CN9,
			Focus = FocusZone.Keyboard,
			Visible = true
		};
		_w.Render(c);
		Pump(400);
		Grid[] array = (Grid[])typeof(OverlayWindow).GetField("_key9Host", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Grid[] array2 = (Grid[])typeof(OverlayWindow).GetField("_gridHost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		List<FrameworkElement> list = new List<FrameworkElement>();
		for (int i = 1; i <= 9; i++)
		{
			list.Add(array[i]);
		}
		Batch("九键那九颗", list);
		c.Focus = FocusZone.CandGrid;
		c.Punct = true;
		_w.Render(c);
		Pump(700);
		List<FrameworkElement> list2 = new List<FrameworkElement>();
		for (int j = 0; j < 25; j++)
		{
			list2.Add(array2[j]);
		}
		Batch("标点 5×5", list2);
		c.Focus = FocusZone.Keyboard;
		c.Punct = false;
		_w.Render(c);
		Pump(300);
		Call("ShowSide", true);
		_w.Render(c);
		Pump(700);
		List<FrameworkElement> list3 = (List<FrameworkElement>)value.GetType().GetField("Hosts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		Check("设置页: 每格外面都套了一层", list3.Count >= 9, $"({list3.Count} 格)");
		Batch("设置那一页", list3);
		// 「关于」那一页 = 九格并成的一大块(SetNav 里 new Span(0, 0, 3, 3))。
		// 它和 789 合成的长条走的是同一条路(都是 MaxSide = 3 -> TilePx = 352):
		// 浮起来必须**克制** —— 352 的块放大 1.10 就是四边各顶出 17px, 那不是浮起来,
		// 是把卡片撑破。这一条守的就是"大块按边长反比收"。
		object pageAbout = Call("PageAbout");
		Call("PushPage", value, pageAbout, false, -1);
		_w.Render(c);
		Pump(700);
		List<FrameworkElement> list4 = (List<FrameworkElement>)value.GetType().GetField("Hosts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
		Check("关于: 九格并成一大块(只有一格)", list4.Count == 1, $"({list4.Count} 格)");
		(double, double, double) tuple2 = Watch(list4);
		Check("浮力 · 关于那一大块: 也在漂", tuple2.Item1 > 0.5, $"(最远 {tuple2.Item1:0.##}px)");
		Check("浮力 · 关于那一大块: 浮起但克制(不许撑破卡片)",
			  tuple2.Item3 > 0.005 && tuple2.Item3 < 0.06, $"(+{tuple2.Item3 * 100:0.#}%)");
		Call("ShowSide", false);
		_w.Render(c);
		Pump(700);
		Settled("设置那批", list3);
		Settled("网格那批", list2);
		(double, double, double) tuple = Watch(list);
		Check("翻回九键: 九键重新漂起来", tuple.Item1 > 0.5, $"(最远 {tuple.Item1:0.##}px)");
		_w.Close();
		void Batch(string tag, List<FrameworkElement> els)
		{
			var (num11, num12, num13) = Watch(els);
			Console.WriteLine($"  {tag}: 漂出去最远 {num11:0.##}px, 同一时刻最差 {num12:0.##}px, 浮起 {num13 * 100.0:0.#}%");
			Check("浮力 · " + tag + ": 在漂", num11 > 0.5, $"(最远 {num11:0.##}px)");
			Check("浮力 · " + tag + ": 各漂各的", num12 > 0.5, $"(最差 {num12:0.##}px)");
			Check("浮力 · " + tag + ": 浮起来了", num13 > 0.005, $"(+{num13 * 100.0:0.#}%)");
		}
		void Settled(string tag, List<FrameworkElement> els)
		{
			var (num2, _, num3) = Watch(els);
			Check("换屏 · " + tag + ": 归位", num2 < 0.05 && num3 < 0.005, $"(残留 {num2:0.###}px, 缩放 {num3:0.####})");
		}
		static (double X, double Y, double S) Tf(FrameworkElement g)
		{
			if (!(g?.RenderTransform is TransformGroup transformGroup) || transformGroup.Children.Count < 2)
			{
				return (X: 0.0, Y: 0.0, S: 1.0);
			}
			TranslateTransform translateTransform = transformGroup.Children[1] as TranslateTransform;
			ScaleTransform scaleTransform = transformGroup.Children[0] as ScaleTransform;
			return (X: translateTransform?.X ?? 0.0, Y: translateTransform?.Y ?? 0.0, S: scaleTransform?.ScaleX ?? 1.0);
		}
		(double amp, double spread, double grow) Watch(List<FrameworkElement> els)
		{
			double num4 = 0.0;
			double num5 = 0.0;
			double num6 = 0.0;
			for (int k = 0; k < 40; k++)
			{
				_w.Render(c);
				Pump(50);
				double num7 = 0.0;
				double num8 = 1000000000.0;
				double num9 = -1000000000.0;
				double num10 = 0.0;
				foreach (FrameworkElement el in els)
				{
					(double, double, double) tuple3 = Tf(el);
					num7 = Math.Max(num7, Math.Abs(tuple3.Item1) + Math.Abs(tuple3.Item2));
					num8 = Math.Min(num8, tuple3.Item1);
					num9 = Math.Max(num9, tuple3.Item1);
					num10 = Math.Max(num10, Math.Abs(tuple3.Item3 - 1.0));
				}
				num4 = Math.Max(num4, num7);
				num5 = Math.Max(num5, num9 - num8);
				num6 = Math.Max(num6, num10);
			}
			return (amp: num4, spread: num5, grow: num6);
		}
	}

	private static void Diag15()
	{
		Console.WriteLine("=== DIAG15: 多档缩放下换面, 窗口 / 卡片 / 内容区挪不挪 ===");
		double[] array = new double[4] { 0.75, 1.0, 1.25, 1.5 };
		foreach (double num in array)
		{
			_w = new OverlayWindow
			{
				Host = new FakeHost
				{
					Which = FxStyle.Slide
				},
				LayoutOf = () => Layout.CN9
			};
			_w.Show();
			_w.SetScale(num);
			Pump(300);
			FrameworkElement root = FE("Root");
			FrameworkElement back = FE("Back");
			FrameworkElement ka = FE("KeyArea");
			object value = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
			Type type = value.GetType();
			FrameworkElement st = (FrameworkElement)type.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
			Call("ShowSide", true);
			Pump(900);
			double[] array2 = S();
			Call("ShowSide", false);
			Pump(900);
			double[] array3 = S();
			Console.WriteLine($"  s={num:0.##} 进设置后(窗口顶/卡顶/内容区顶)={array2[0]:0.#}/{array2[1]:0.#}/{array2[2]:0.#}  回九键={array3[0]:0.#}/{array3[1]:0.#}/{array3[2]:0.#}  | 差 Δ窗口={array3[0] - array2[0]:0.#} Δ卡={array3[1] - array2[1]:0.#} Δ内容区={array3[2] - array2[2]:0.#}");
			_w.Close();
			double[] S()
			{
				//IL_0045: Unknown result type (might be due to invalid IL or missing references)
				//IL_004a: Unknown result type (might be due to invalid IL or missing references)
				//IL_0056: Unknown result type (might be due to invalid IL or missing references)
				//IL_005b: Unknown result type (might be due to invalid IL or missing references)
				//IL_0067: Unknown result type (might be due to invalid IL or missing references)
				//IL_006c: Unknown result type (might be due to invalid IL or missing references)
				FrameworkElement el = ((back.Visibility == Visibility.Visible) ? back : root);
				FrameworkElement el2 = ((back.Visibility == Visibility.Visible) ? st : ka);
				double[] array4 = new double[3];
				Point val = Scr(_w);
				array4[0] = val.Y;
				val = Scr(el);
				array4[1] = val.Y;
				val = Scr(el2);
				array4[2] = val.Y;
				return array4;
			}
		}
	}

	private static void Diag14()
	{
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e8: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("=== DIAG14: 九键 <-> 设置 换面, 逐帧屏幕坐标 ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		FrameworkElement root = FE("Root");
		FrameworkElement back = FE("Back");
		FrameworkElement ka = FE("KeyArea");
		object setEl = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type ty = setEl.GetType();
		Call("ShowSide", true);
		Pump(900);
		FrameworkElement frameworkElement = (FrameworkElement)ty.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl);
		FrameworkElement frameworkElement2 = (FrameworkElement)ty.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl);
		Image image = (Image)ty.GetField("VeilImg", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl);
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(55, 6);
		defaultInterpolatedStringHandler.AppendLiteral("  [基线·设置页静置] Stage高=");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualHeight, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" 宽=");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement.ActualWidth, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" | Panel高=");
		defaultInterpolatedStringHandler.AppendFormatted(frameworkElement2.ActualHeight, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" 在Stage内y=");
		Point val = frameworkElement2.TranslatePoint(new Point(0.0, 0.0), frameworkElement);
		defaultInterpolatedStringHandler.AppendFormatted(val.Y, "0.#");
		defaultInterpolatedStringHandler.AppendLiteral(" | 上一张照片位图=");
		defaultInterpolatedStringHandler.AppendFormatted((image?.Source as BitmapSource)?.PixelWidth ?? (-1));
		defaultInterpolatedStringHandler.AppendLiteral("x");
		defaultInterpolatedStringHandler.AppendFormatted((image?.Source as BitmapSource)?.PixelHeight ?? (-1));
		Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		Call("ShowSide", false);
		Pump(900);
		Console.WriteLine("--- A 九键 -> 设置 ---");
		Shoot("起步前");
		Call("ShowSide", true);
		for (int i = 1; i <= 40; i++)
		{
			Pump(10);
			Shoot($"t={i * 10}ms");
		}
		Pump(600);
		Shoot("静置后");
		Console.WriteLine("--- B 设置 -> 九键 ---");
		Call("ShowSide", false);
		for (int j = 1; j <= 40; j++)
		{
			Pump(10);
			Shoot($"t={j * 10}ms");
		}
		Pump(600);
		Shoot("静置后");
		_w.Close();
		void Shoot(string tag)
		{
			//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00db: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0093: Unknown result type (might be due to invalid IL or missing references)
			//IL_0098: Unknown result type (might be due to invalid IL or missing references)
			//IL_011b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0120: Unknown result type (might be due to invalid IL or missing references)
			//IL_0160: Unknown result type (might be due to invalid IL or missing references)
			//IL_0165: Unknown result type (might be due to invalid IL or missing references)
			//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
			//IL_01aa: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e3: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_024d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0252: Unknown result type (might be due to invalid IL or missing references)
			//IL_033a: Unknown result type (might be due to invalid IL or missing references)
			//IL_033f: Unknown result type (might be due to invalid IL or missing references)
			FrameworkElement frameworkElement3 = (FrameworkElement)ty.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl);
			FrameworkElement frameworkElement4 = (FrameworkElement)ty.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl);
			FrameworkElement frameworkElement5 = (FrameworkElement)ty.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl);
			double num;
			Point val2;
			if (frameworkElement4 == null || frameworkElement3 == null)
			{
				num = double.NaN;
			}
			else
			{
				val2 = frameworkElement4.TranslatePoint(new Point(0.0, 0.0), frameworkElement3);
				num = val2.Y;
			}
			double value = num;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler2 = new DefaultInterpolatedStringHandler(89, 13);
			defaultInterpolatedStringHandler2.AppendLiteral("  ");
			defaultInterpolatedStringHandler2.AppendFormatted<string>(tag, -9);
			defaultInterpolatedStringHandler2.AppendLiteral(" 窗口Top=");
			val2 = Scr(_w);
			defaultInterpolatedStringHandler2.AppendFormatted(val2.Y, 7, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" | Root顶=");
			double value2;
			if (root.Visibility != 0)
			{
				value2 = double.NaN;
			}
			else
			{
				val2 = Scr(root);
				value2 = val2.Y;
			}
			defaultInterpolatedStringHandler2.AppendFormatted(value2, 7, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" | Back顶=");
			double value3;
			if (back.Visibility != 0)
			{
				value3 = double.NaN;
			}
			else
			{
				val2 = Scr(back);
				value3 = val2.Y;
			}
			defaultInterpolatedStringHandler2.AppendFormatted(value3, 7, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" | KeyArea顶=");
			double value4;
			if (ka.Visibility != 0)
			{
				value4 = double.NaN;
			}
			else
			{
				val2 = Scr(ka);
				value4 = val2.Y;
			}
			defaultInterpolatedStringHandler2.AppendFormatted(value4, 7, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" | Stage顶=");
			double value5;
			if (frameworkElement3 == null || frameworkElement3.Visibility != 0)
			{
				value5 = double.NaN;
			}
			else
			{
				val2 = Scr(frameworkElement3);
				value5 = val2.Y;
			}
			defaultInterpolatedStringHandler2.AppendFormatted(value5, 7, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" 高=");
			defaultInterpolatedStringHandler2.AppendFormatted(frameworkElement3?.ActualHeight ?? 0.0, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" | Panel顶=");
			double value6;
			if (frameworkElement4 == null || frameworkElement4.Visibility != 0)
			{
				value6 = double.NaN;
			}
			else
			{
				val2 = Scr(frameworkElement4);
				value6 = val2.Y;
			}
			defaultInterpolatedStringHandler2.AppendFormatted(value6, 7, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" 高=");
			defaultInterpolatedStringHandler2.AppendFormatted(frameworkElement4?.ActualHeight ?? 0.0, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" 在Stage内y=");
			defaultInterpolatedStringHandler2.AppendFormatted(value, 6, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" | Veil=");
			defaultInterpolatedStringHandler2.AppendFormatted(frameworkElement5?.Visibility.ToString().Substring(0, 4));
			defaultInterpolatedStringHandler2.AppendLiteral(" 高=");
			defaultInterpolatedStringHandler2.AppendFormatted((frameworkElement5 != null && frameworkElement5.Visibility == Visibility.Visible) ? frameworkElement5.ActualHeight : 0.0, "0.#");
			defaultInterpolatedStringHandler2.AppendLiteral(" 顶=");
			double value7;
			if (frameworkElement5 == null || frameworkElement5.Visibility != 0)
			{
				value7 = double.NaN;
			}
			else
			{
				val2 = Scr(frameworkElement5);
				value7 = val2.Y;
			}
			defaultInterpolatedStringHandler2.AppendFormatted(value7, "0.#");
			Console.WriteLine(defaultInterpolatedStringHandler2.ToStringAndClear());
		}
	}

	private static void Diag10()
	{
		Console.WriteLine("=== DIAG10: 逐帧屏幕坐标(窗口 / 卡片 / 内容区 / 旧页照片) ===");
		_w = new OverlayWindow
		{
			Host = new FakeHost
			{
				Which = FxStyle.Slide
			},
			LayoutOf = () => Layout.CN9
		};
		_w.Show();
		Pump(300);
		FrameworkElement card = FE("Root");
		FrameworkElement body = FE("KeyArea");
		FrameworkElement fv = FE("FaceVeil");
		Trace("正面·九键->符号面板", () => fv, body, card, delegate
		{
			_w.Render(new InputCore
			{
				Layout = Layout.CN9,
				Visible = true,
				Punct = true,
				Focus = FocusZone.CandGrid
			});
		});
		Pump(700);
		InputCore c = new InputCore
		{
			Layout = Layout.CN9,
			Visible = true,
			Punct = true,
			Focus = FocusZone.CandGrid
		};
		_w.Render(c);
		Pump(700);
		Trace("正面·换标点分类", () => fv, body, card, delegate
		{
			c.NextPunctCategory(1);
			_w.Render(c);
		});
		Pump(700);
		Call("ShowSide", true);
		Pump(800);
		object setEl = typeof(OverlayWindow).GetField("_set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_w);
		Type ty = setEl.GetType();
		FrameworkElement card2 = (FrameworkElement)ty.GetField("Stage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl);
		SetPage fx = (SetPage)Call("PageFxStyle");
		Trace("设置·进第二层", () => (FrameworkElement)ty.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl), () => (FrameworkElement)ty.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl), card2, delegate
		{
			Call("PushPage", setEl, fx, true, -1);
		});
		Pump(700);
		Trace("设置·翻页", () => (FrameworkElement)ty.GetField("Veil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl), () => (FrameworkElement)ty.GetField("Panel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(setEl), card2, delegate
		{
			Call("FlipPage", setEl, 1);
		});
		Pump(700);
		_w.Close();
	}

	private static void Trace(string name, Func<FrameworkElement> photo, FrameworkElement body, FrameworkElement card, Action fire)
	{
		Trace(name, photo, () => body, card, fire);
	}

	private static void Trace(string name, Func<FrameworkElement> photo, Func<FrameworkElement> body, FrameworkElement card, Action fire)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("--- " + name + " ---");
		Point val = Scr(_w);
		double y = val.Y;
		double actualHeight = _w.ActualHeight;
		val = Scr(card);
		double y2 = val.Y;
		val = Scr(body());
		double y3 = val.Y;
		Console.WriteLine($"  演之前: 窗口Top={y:0.#} 高={actualHeight:0.#} | 卡顶={y2:0.#} | 内容区顶={y3:0.#}");
		fire();
		for (int i = 1; i <= 7; i++)
		{
			Pump(12);
			val = Scr(_w);
			double y4 = val.Y;
			double actualHeight2 = _w.ActualHeight;
			val = Scr(card);
			double y5 = val.Y;
			val = Scr(body());
			double y6 = val.Y;
			FrameworkElement frameworkElement = photo();
			double num;
			if (frameworkElement != null && frameworkElement.Visibility == Visibility.Visible)
			{
				val = Scr(frameworkElement);
				num = val.Y;
			}
			else
			{
				num = double.NaN;
			}
			double num2 = num;
			Console.WriteLine($"  t={i * 12,3}ms 窗口Top={y4:0.#}(Δ{D(y4 - y)}) 高={actualHeight2:0.#}(Δ{D(actualHeight2 - actualHeight)}) | 卡顶={y5:0.#}(Δ{D(y5 - y2)}) | 内容区顶={y6:0.#}(Δ{D(y6 - y3)}) | 照片顶={(double.IsNaN(num2) ? "   --" : $"{num2:0.#}(Δ{D(num2 - y3)})")}");
		}
	}

	private static string D(double v)
	{
		if (!(Math.Abs(v) < 0.5))
		{
			return v.ToString("0.#");
		}
		return "0";
	}

	private static Point Scr(FrameworkElement el)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		Point result = el.PointToScreen(new Point(0.0, 0.0));
		if (el.RenderTransform is TransformGroup transformGroup)
		{
			foreach (Transform child in transformGroup.Children)
			{
				if (child is TranslateTransform translateTransform)
				{
					result.X = result.X - translateTransform.X;
					result.Y = result.Y - translateTransform.Y;
				}
			}
		}
		return result;
	}

	private static void Measure(string name, FrameworkElement photo, FrameworkElement body, FrameworkElement host)
	{
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		Console.WriteLine("--- " + name + " ---");
		if (photo == null || body == null || host == null)
		{
			Console.WriteLine("  元素没拿到");
			return;
		}
		Rect val = LayoutBox(photo, host);
		Rect val2 = LayoutBox(body, host);
		Rect val3 = host.Clip?.Bounds ?? Rect.Empty;
		Console.WriteLine($"  两页的爹    {host.ActualWidth:0.#}x{host.ActualHeight:0.#}");
		Console.WriteLine($"  旧页照片    x{val.X:0.#} y{val.Y:0.#} {val.Width:0.#}x{val.Height:0.#}");
		Console.WriteLine($"  新页真身    x{val2.X:0.#} y{val2.Y:0.#} {val2.Width:0.#}x{val2.Height:0.#}");
		Console.WriteLine("  当前屏幕框  " + (val3.IsEmpty ? "(没挂上)" : $"x{val3.X:0.#} y{val3.Y:0.#} {val3.Width:0.#}x{val3.Height:0.#}"));
		if (!val3.IsEmpty)
		{
			double v = val3.Y - val2.Y;
			double v2 = val2.Y + val2.Height - (val3.Y + val3.Height);
			double v3 = val3.X - val2.X;
			double v4 = val2.X + val2.Width - (val3.X + val3.Width);
			Console.WriteLine($"  >> 这一版把真身切掉: 上{Pos(v)} 下{Pos(v2)} 左{Pos(v3)} 右{Pos(v4)}");
		}
		double v5 = val.Y - val2.Y;
		double v6 = val2.Y + val2.Height - (val.Y + val.Height);
		double v7 = val.X - val2.X;
		double v8 = val2.X + val2.Width - (val.X + val.Width);
		Console.WriteLine($"  >> 若按 2.1.30(框=照片那一块, 上下也切): 上{Pos(v5)} 下{Pos(v6)} 左{Pos(v7)} 右{Pos(v8)}");
	}

	private static string Pos(double v)
	{
		if (!(v > 0.5))
		{
			return "0";
		}
		return v.ToString("0.#");
	}

	private static Rect LayoutBox(FrameworkElement el, UIElement host)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		Rect result = el.TransformToVisual(host).TransformBounds(new Rect(0.0, 0.0, el.ActualWidth, el.ActualHeight));
		if (el.RenderTransform is TransformGroup transformGroup)
		{
			foreach (Transform child in transformGroup.Children)
			{
				if (child is TranslateTransform translateTransform)
				{
					result.X = result.X - translateTransform.X;
					result.Y = result.Y - translateTransform.Y;
				}
			}
		}
		return result;
	}
}
