param(
    [Parameter(Mandatory=$true)][string]$Title,
    [Parameter(Mandatory=$true)][string]$Output
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NativeWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);
}
'@

[NativeWindowCapture]::SetProcessDPIAware() | Out-Null
$process = Get-Process | Where-Object { $_.MainWindowTitle -eq $Title } | Select-Object -First 1
if (-not $process) {
    throw "Window not found: $Title"
}

$handle = $process.MainWindowHandle
[NativeWindowCapture]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 300

$rect = New-Object NativeWindowCapture+Rect
if (-not [NativeWindowCapture]::GetWindowRect($handle, [ref]$rect)) {
    throw "GetWindowRect failed for: $Title"
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "Invalid window bounds: ${width}x${height}"
}

$bitmap = New-Object Drawing.Bitmap($width, $height)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $deviceContext = $graphics.GetHdc()
    try {
        if (-not [NativeWindowCapture]::PrintWindow($handle, $deviceContext, 2)) {
            throw "PrintWindow failed for: $Title"
        }
    }
    finally {
        $graphics.ReleaseHdc($deviceContext)
    }
    $bitmap.Save($Output, [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

Get-Item -LiteralPath $Output | Select-Object FullName,Length,LastWriteTime
