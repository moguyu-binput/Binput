# 真机冒烟: 用键盘模拟手柄(--debug), 走一遍"打数字 -> LB/RB 挑拼音 -> A 发给输入法"
# 键位: IJKL=左摇杆  方向键=十字键  空格=A  Esc=B  [ ]=LB/RB  Q=LT  E=RT  Z+C=唤出
$ErrorActionPreference = 'Continue'
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class K {
  [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public static string Title(IntPtr h){ var sb=new StringBuilder(256); GetWindowText(h,sb,256); return sb.ToString(); }
}
"@

$root = "C:\Users\<你>\WorkBuddy"
$exe  = "$root\GamepadT9-Game-x64-selfcontained\GamepadT9.exe"
$dir  = "$env:LOCALAPPDATA\GamepadT9"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$log  = "$dir\log.txt"
Set-Content -Path "$dir\mode.txt" -Value "1 0" -Encoding ASCII   # 游戏模式开 + 仅复制

$mark = (Get-Item $log -ErrorAction SilentlyContinue).Length      # 记录跑之前的日志长度

function Down($vk){ [K]::keybd_event([byte]$vk,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 45 }
function Up($vk)  { [K]::keybd_event([byte]$vk,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 45 }
function Tap($vk) { Down $vk; Up $vk }

# 左摇杆推到九宫格 n, 再按 A
function Press9($n) {
  $col = ($n - 1) % 3; $row = [math]::Floor(($n - 1) / 3)
  $xk = @(); if ($col -eq 0) { $xk += 0x4A } elseif ($col -eq 2) { $xk += 0x4C }
  $yk = @(); if ($row -eq 0) { $yk += 0x49 } elseif ($row -eq 2) { $yk += 0x4B }
  foreach ($k in ($xk + $yk)) { Down $k }
  Start-Sleep -Milliseconds 130
  Tap 0x20
  foreach ($k in ($xk + $yk)) { Up $k }
  Start-Sleep -Milliseconds 130
}

function Say($s) {
  $f = [K]::GetForegroundWindow()
  Write-Host ("{0}  [前台: {1}]" -f $s, [K]::Title($f))
}

$p = Start-Process $exe -ArgumentList "--debug","--game" -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 6
Say "启动完成"

Say "--- 打 7468326 (想要 shou'dao) ---"
foreach ($d in 7,4,6,8,3,2,6) { Press9 $d }

Say "--- RB: 换下一个切分 ---"
Tap 0xDD; Start-Sleep -Milliseconds 300
Say "--- LB: 换回来 ---"
Tap 0xDB; Start-Sleep -Milliseconds 300
Say "--- A: 整段发给输入法 ---"
Tap 0x20; Start-Sleep -Milliseconds 700

Say "--- 再打 7468326, 用 Y 只定住第一个字 ---"
foreach ($d in 7,4,6,8,3,2,6) { Press9 $d }
Tap 0x59; Start-Sleep -Milliseconds 500          # Y

Start-Sleep -Seconds 2
Write-Host "=== 本次新增的日志 ==="
$all = [System.IO.File]::ReadAllText($log, [System.Text.Encoding]::UTF8)
Write-Host $all.Substring([Math]::Min($mark, $all.Length))

Stop-Process -Name GamepadT9 -Force -ErrorAction SilentlyContinue
