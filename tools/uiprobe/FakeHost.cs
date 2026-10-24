using Binput;

namespace Uiprobe;

internal sealed class FakeHost : ISettingsHost
{
	public FxStyle Which;

	public bool Preview { get; set; }

	public int PasteTier { get; set; }

	public int Theme { get; set; }

	public bool Key9Num { get; set; }

	public bool Swap15 { get; set; }

	public SlideMode SlideMode { get; set; }

	public int SlideDwell { get; set; }

	public int SlideRepeat { get; set; }

	public FxStyle FxStyle
	{
		get
		{
			return Which;
		}
		set
		{
			Which = value;
		}
	}

	public KeyShape KeyShape { get; set; }

	public string WakeSpec => "";

	public string SlideInfo()
	{
		return "uiprobe";
	}

	public void SetWake(string spec)
	{
	}

	public void WakeReset()
	{
	}

	public string WakeKeys()
	{
		return "L3 + R3";
	}

	// 2.3.3 加: 设置 → 键盘 → 「调整窗口」。探针里真开窗口, 就真走一遍:
	// 记下"被点过", 让 uiprobe 能盯着这一格确实进得去那一档。
	public int AdjustCalls;

	public void AdjustWindow()
	{
		AdjustCalls++;
	}

	public void OpenLog()
	{
	}

	public void OpenDataDir()
	{
	}

	public void ClearUse()
	{
	}

	public void ExportUse()
	{
	}

	public void ImportUse()
	{
	}

	public void ExitApp()
	{
	}

	public string About()
	{
		return "uiprobe";
	}
}
