# Launches GetMan, waits for the window, and saves a PNG of it.
# Usage: powershell -File tools/capture.ps1 [-Out shot.png] [-Delay 8]
param(
    [string]$Exe = "$PSScriptRoot\..\src\GetMan\bin\Debug\net9.0-windows\GetMan.exe",
    [string]$Out = "$env:TEMP\getman-shot.png",
    [int]$Delay = 8,
    [switch]$KeepOpen,
    # Window-relative point to park the pointer over before capturing, so hover states show.
    # The pointer is moved only (never clicked) and is put back where it was afterwards.
    [int]$HoverX = 0,
    [int]$HoverY = 0
)

Add-Type -AssemblyName System.Drawing, System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Cap {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[void][Win32Cap]::SetProcessDPIAware()

$proc = Start-Process $Exe -PassThru
$deadline = (Get-Date).AddSeconds($Delay + 12)
while ((Get-Date) -lt $deadline -and $proc.MainWindowHandle -eq 0) {
    Start-Sleep -Milliseconds 400
    $proc.Refresh()
}
Start-Sleep -Seconds $Delay

$h = $proc.MainWindowHandle
if ($h -eq 0) { Write-Error "No main window appeared"; exit 1 }

# The first handle can belong to a transient window - wait for a real sized one.
$probe = New-Object Win32Cap+RECT
$tries = 0
while ($tries -lt 25) {
    $proc.Refresh()
    $h = $proc.MainWindowHandle
    [void][Win32Cap]::GetWindowRect($h, [ref]$probe)
    if (($probe.Right - $probe.Left) -gt 500 -and ($probe.Bottom - $probe.Top) -gt 400) { break }
    Start-Sleep -Milliseconds 500
    $tries++
}

[void][Win32Cap]::ShowWindow($h, 3)      # maximize so the whole layout is visible
[void][Win32Cap]::SetForegroundWindow($h)
Start-Sleep -Seconds 2

$r = New-Object Win32Cap+RECT
[void][Win32Cap]::GetWindowRect($h, [ref]$r)

$savedCursor = New-Object Win32Cap+POINT
$moved = $false
if ($HoverX -gt 0 -or $HoverY -gt 0) {
    [void][Win32Cap]::GetCursorPos([ref]$savedCursor)
    [void][Win32Cap]::SetCursorPos(($r.Left + $HoverX), ($r.Top + $HoverY))
    $moved = $true
    Start-Sleep -Milliseconds 900   # let the hover animation settle
}
$w = $r.Right - $r.Left
$hgt = $r.Bottom - $r.Top

$bmp = New-Object System.Drawing.Bitmap($w, $hgt, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)

# PW_RENDERFULLCONTENT (2) captures the window even when it is occluded.
$hdc = $g.GetHdc()
$ok = [Win32Cap]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)

if (-not $ok) {
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $hgt)))
}
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

if ($moved) { [void][Win32Cap]::SetCursorPos($savedCursor.X, $savedCursor.Y) }

Write-Output "saved $Out ($w x $hgt)"

if (-not $KeepOpen) {
    [void]$proc.CloseMainWindow()
    Start-Sleep -Seconds 2
    if (-not $proc.HasExited) { $proc.Kill() }
}
