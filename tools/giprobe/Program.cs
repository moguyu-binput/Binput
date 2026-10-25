using Windows.Gaming.Input;

namespace Giprobe;

/// <summary>
/// 探 <c>Windows.Gaming.Input</c> —— 网页那个"手柄按键测试"在 Windows 上走的就是
/// 这套(浏览器的 HTML5 Gamepad API 实现)。看它认不认 DualSense / Switch Pro,
/// 认了的话就不用自己啃 HID 描述符和用法号了。
/// </summary>
static class Program
{
    static void Main(string[] argv)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.WriteLine("=== giprobe: Windows.Gaming.Input(网页同款接口) ===\n");

        int sec = argv.Length > 0 && int.TryParse(argv[0], out int n0) ? n0 : 6;

        Console.WriteLine("--- Gamepad(标准手柄视图)---");
        var pads = Gamepad.Gamepads.ToList();
        Console.WriteLine($"  数量: {pads.Count}");
        for (int i = 0; i < pads.Count; i++)
        {
            var r = pads[i].GetCurrentReading();
            Console.WriteLine($"  [{i}] 键={r.Buttons}  左({r.LeftThumbstickX:F2},{r.LeftThumbstickY:F2}) "
                + $"右({r.RightThumbstickX:F2},{r.RightThumbstickY:F2}) "
                + $"LT={r.LeftTrigger:F2} RT={r.RightTrigger:F2}");
        }

        Console.WriteLine("\n--- RawGameController(能看到设备名和每颗键的标签)---");
        var raws = RawGameController.RawGameControllers.ToList();
        Console.WriteLine($"  数量: {raws.Count}");
        for (int i = 0; i < raws.Count; i++)
        {
            var c = raws[i];
            Console.WriteLine($"  [{i}] {c.DisplayName}  按钮 {c.ButtonCount} 轴 {c.AxisCount} 开关 {c.SwitchCount}");
            var gp = Gamepad.FromGameController(c);
            Console.WriteLine($"      -> Gamepad.FromGameController: {(gp == null ? "null(转不出标准手柄视图)" : "有了!")}");
            if (gp != null) { var gr = gp.GetCurrentReading(); Console.WriteLine($"         键={gr.Buttons} 左({gr.LeftThumbstickX:F2},{gr.LeftThumbstickY:F2}) 右({gr.RightThumbstickX:F2},{gr.RightThumbstickY:F2}) LT={gr.LeftTrigger:F2} RT={gr.RightTrigger:F2}"); }
            if (c.ButtonCount > 0)
                Console.WriteLine($"      按钮标签类型: {c.GetButtonLabel(0).GetType().FullName}");
            for (ushort b = 0; b < c.ButtonCount; b++)
                Console.Write($"     {b}:{c.GetButtonLabel(b)}");
            if (c.ButtonCount > 0) Console.WriteLine();
            var ax = new double[c.AxisCount]; var bt = new bool[c.ButtonCount]; var sw = new GameControllerSwitchPosition[c.SwitchCount];
            c.GetCurrentReading(bt, sw, ax);
            for (int a = 0; a < ax.Length; a++)
                Console.Write($"     轴{a}={ax[a]:F3}");
            if (c.AxisCount > 0) Console.WriteLine();
            for (ushort s = 0; s < c.SwitchCount; s++)
                Console.Write($"     开关{s}:{c.GetSwitchKind(s)}");
            if (c.SwitchCount > 0) Console.WriteLine();
        }

        if (pads.Count == 0 && raws.Count == 0)
        {
            Console.WriteLine("\n一只都没看见 —— 那这条路不通。");
            return;
        }

        Console.WriteLine("\n--- 原始手柄的四个轴(对比用, 现在推一下摇杆)---");
        var rc = RawGameController.RawGameControllers.ToList();
        var rax = rc.Select(x => new double[x.AxisCount]).ToList();
        var rbt = rc.Select(x => new bool[x.ButtonCount]).ToList();
        var rsw = rc.Select(x => new GameControllerSwitchPosition[x.SwitchCount]).ToList();
        var end0 = DateTime.Now.AddSeconds(sec);
        while (DateTime.Now < end0)
        {
            for (int k = 0; k < rc.Count; k++)
            {
                rc[k].GetCurrentReading(rbt[k], rsw[k], rax[k]);
                var lab = new List<string>();
                for (int b = 0; b < rbt[k].Length; b++)
                    if (rbt[k][b]) lab.Add(b + ":" + rc[k].GetButtonLabel(b));
                Console.WriteLine($"  [{k}] {rc[k].DisplayName} 轴: {string.Join(" ", rax[k].Select(v => v.ToString("F3")))}"
                    + (lab.Count > 0 ? "  按着: " + string.Join(",", lab) : ""));
            }
            Thread.Sleep(400);
        }
        Console.WriteLine($"\n--- 实按 {sec} 秒(现在按几颗键试试)---");
        var seen = new List<string>();
        var end = DateTime.Now.AddSeconds(sec);
        string last = "";
        while (DateTime.Now < end)
        {
            foreach (var p in AllGamepads())
            {
                var r = p.GetCurrentReading();
                string txt = $"键={r.Buttons} 左摇杆({r.LeftThumbstickX:F2},{r.LeftThumbstickY:F2}) 右摇杆({r.RightThumbstickX:F2},{r.RightThumbstickY:F2}) LT={r.LeftTrigger:F2} RT={r.RightTrigger:F2}";
                if (txt != last)
                {
                    last = txt;
                    Console.WriteLine("  " + txt);
                    if (r.Buttons != GamepadButtons.None && !seen.Contains(r.Buttons.ToString()))
                        seen.Add(r.Buttons.ToString());
                }
            }
            Thread.Sleep(80);
        }
        Console.WriteLine($"\n读到 {seen.Count} 种组合: {string.Join(", ", seen)}");
    }

    /// <summary>和主程序 RawPad 一样的两条路: 现成的标准手柄 + 原始手柄转出来的。</summary>
    static IEnumerable<Gamepad> AllGamepads()
    {
        foreach (var g in Gamepad.Gamepads) yield return g;
        foreach (var c in RawGameController.RawGameControllers)
        {
            var g = Gamepad.FromGameController(c);
            if (g != null) yield return g;
        }
    }
}
