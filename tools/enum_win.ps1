# List top-level windows whose title contains Gamepad (diag: why FindWindow misses).
Add-Type -AssemblyName System.Windows.Forms
Set-Location "C:\Users\<你>\WorkBuddy"
$exe = "skin-lab\GamepadT9-Game\bin\Release\net8.0-windows\GamepadT9.exe"
$p = Start-Process -FilePath $exe -ArgumentList "--debug","--game" -PassThru
Start-Sleep -Seconds 4
$out = @()
foreach ($pr in (Get-Process GamepadT9 -ErrorAction SilentlyContinue)) {
    if ($pr.MainWindowHandle -ne 0) {
        $out += "pid=$($pr.Id) mainHwnd=$($pr.MainWindowHandle) title='$($pr.MainWindowTitle)'"
    } else {
        $out += "pid=$($pr.Id) mainHwnd=0 (no main window)"
    }
}
if (-not $out) { $out += "no GamepadT9 process" }
$out | Out-File -Encoding utf8 tools\_enum.txt
if (!$p.HasExited) { $p.Kill() }
