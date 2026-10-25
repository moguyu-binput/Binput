$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class K {
  [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  public static string Title(IntPtr h){ var sb=new System.Text.StringBuilder(256); GetWindowText(h,sb,256); return sb.ToString(); }
}
"@

$root = "C:\Users\<你>\WorkBuddy"
$exe  = "$root\GamepadT9-Game-x64-selfcontained\GamepadT9.exe"

# 1. 档位设成 "复制 + 粘贴" (1 = CopyPaste)
$dir = "$env:LOCALAPPDATA\GamepadT9"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Set-Content -Path "$dir\mode.txt" -Value "1 1" -Encoding ASCII

# 2. 先把记事本开起来并顶到前台, 它会被当成"游戏窗口"记下来
$np = Start-Process notepad -PassThru
Start-Sleep -Milliseconds 1500
[Microsoft.VisualBasic.Interaction]::AppActivate($np.Id) | Out-Null
Start-Sleep -Milliseconds 500
[K]::SetForegroundWindow($np.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
Write-Host "记事本 hwnd=$($np.MainWindowHandle) title=$([K]::Title($np.MainWindowHandle))"

# 3. 启动游戏模式(带 --debug: 界面直接显示 + 键盘模拟手柄已激活)
$p = Start-Process $exe -ArgumentList "--debug","--game" -WorkingDirectory "$root\GamepadT9-Game-x64-selfcontained" -PassThru
Start-Sleep -Seconds 6

function Key($vk, $down) {
  if ($down) { [K]::keybd_event([byte]$vk, 0, 0, [UIntPtr]::Zero) }
  else       { [K]::keybd_event([byte]$vk, 0, 2, [UIntPtr]::Zero) }
  Start-Sleep -Milliseconds 60
}

# 4. 往记事本预览框里打几个数字(字母在中文输入法下会进 composition, 数字直接落成文本)
foreach ($c in @('1','2','3','4')) { Key ([int][char]$c) $true; Key ([int][char]$c) $false }
Start-Sleep -Milliseconds 600

# 5. Z + C = L3 + R3 -> 收起
Key 0x5A $true; Key 0x43 $true
Start-Sleep -Milliseconds 500
Key 0x5A $false; Key 0x43 $false

Start-Sleep -Seconds 3

Write-Host "=== 日志尾部 ==="
Get-Content "$dir\log.txt" -Tail 25

Write-Host "=== 剪贴板 ==="
Add-Type -AssemblyName System.Windows.Forms
try { Write-Host ("[{0}]" -f [System.Windows.Forms.Clipboard]::GetText()) } catch { Write-Host "读取失败: $_" }

Write-Host "=== 当前前台 ==="
$f = [K]::GetForegroundWindow()
Write-Host "$([K]::Title($f))"

Stop-Process -Name GamepadT9 -Force -ErrorAction SilentlyContinue
Stop-Process -Name notepad  -Force -ErrorAction SilentlyContinue
