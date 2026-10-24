# Grab the overlay window from screen (real DWM composition incl. acrylic blur).
# RTB self-screenshot renders the WPF visual tree only - acrylic needs a screen grab.
# Usage: powershell -File grab_overlay.ps1 -Theme 9 -Out path.png [-Extra @("--settings")]
param(
    [string]$Theme = "9",
    [string]$Out = "C:\Users\<你>\WorkBuddy\tools\shots\grab.png",
    [string[]]$Extra = @()
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

[Win]::SetProcessDPIAware() | Out-Null

$themeFile = Join-Path $env:LOCALAPPDATA "GamepadT9\theme.txt"
$old = if (Test-Path $themeFile) { Get-Content $themeFile -Raw } else { $null }
Set-Content -Path $themeFile -Value $Theme -NoNewline

$exe = "C:\Users\<你>\Binput\bin\Release\net8.0-windows\GamepadT9.exe"
$p = Start-Process -FilePath $exe -ArgumentList (@("--debug", "--game") + $Extra) -PassThru

try {
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 200
        $h = [Win]::FindWindow($null, "GamepadT9")
        if ($h -ne [IntPtr]::Zero) { break }
    }
    if ($h -eq [IntPtr]::Zero) { Write-Output "FAIL no window"; exit 1 }

    Start-Sleep -Milliseconds 2500

    $r = New-Object Win+RECT
    [Win]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $ht = $r.B - $r.T
    if ($w -le 0 -or $ht -le 0) { Write-Output "FAIL bad size $w x $ht"; exit 1 }

    $bmp = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output "OK $Out ${w}x${ht} at $($r.L),$($r.T)"
}
finally {
    if (!$p.HasExited) { $p.Kill() }
    if ($null -ne $old) { Set-Content -Path $themeFile -Value $old -NoNewline }
}
