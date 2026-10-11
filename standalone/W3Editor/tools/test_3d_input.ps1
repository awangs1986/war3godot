# End-to-end 3D viewport input test: drives REAL mouse events through Win32
# and verifies the editor camera reacts (rotate / pan / zoom).
$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class M {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    public const uint MOVE = 0x0001, MIDDLEDOWN = 0x0020, MIDDLEUP = 0x0040,
                      RIGHTDOWN = 0x0008, RIGHTUP = 0x0010, WHEEL = 0x0800;
}
"@

$exe = "C:\projects\war3godot\standalone\W3Editor\bin\Release\net8.0\W3Editor.exe"
$camLog = "C:\projects\war3godot\standalone\W3Editor\selftest_caminput.txt"
Remove-Item $camLog -ErrorAction SilentlyContinue

$proc = Start-Process -FilePath $exe -ArgumentList '--open','"D:\maps\WC2Orc14.w3x"','--view3d','--camlog',"`"$camLog`"" -PassThru
try {
    # Wait for the map + GL viewport (camlog appears once the timer runs).
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path $camLog)) {
        if ((Get-Date) -gt $deadline) { throw "editor never reached 3D state" }
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Milliseconds 1500
    [M]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null

    # Viewport center: window is 1480x920 centered; middle column holds the 3D view.
    $cx = [int]([M]::GetSystemMetrics(0) / 2)
    $cy = [int]([M]::GetSystemMetrics(1) / 2)

    function Read-Cam { (Get-Content $camLog -Raw -ErrorAction SilentlyContinue) }
    function Drag([uint32]$down, [uint32]$up, [int]$dx, [int]$dy) {
        [M]::SetCursorPos($cx, $cy) | Out-Null; Start-Sleep -Milliseconds 120
        [M]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero);   Start-Sleep -Milliseconds 120
        for ($i = 1; $i -le 10; $i++) {
            [M]::SetCursorPos($cx + [int]($dx * $i / 10), $cy + [int]($dy * $i / 10)) | Out-Null
            Start-Sleep -Milliseconds 30
        }
        Start-Sleep -Milliseconds 120
        [M]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero);      Start-Sleep -Milliseconds 400
    }

    $s0 = Read-Cam
    Drag ([M]::MIDDLEDOWN) ([M]::MIDDLEUP) 200 -120         # middle-drag = rotate
    $s1 = Read-Cam
    Drag ([M]::RIGHTDOWN) ([M]::RIGHTUP) 150 80             # right-drag = pan
    $s2 = Read-Cam
    [M]::SetCursorPos($cx, $cy) | Out-Null; Start-Sleep -Milliseconds 120
    [M]::mouse_event([M]::WHEEL, 0, 0, 480, [UIntPtr]::Zero) # wheel = zoom in
    Start-Sleep -Milliseconds 500
    $s3 = Read-Cam

    Write-Output "STATE0(init)  : $s0"
    Write-Output "STATE1(rotate): $s1"
    Write-Output "STATE2(pan)   : $s2"
    Write-Output "STATE3(zoom)  : $s3"

    $ok = ($s1 -ne $s0) -and ($s2 -ne $s1) -and ($s3 -ne $s2)
    Write-Output ($(if ($ok) { "RESULT: PASS - all three camera ops responded to real mouse input" }
                    else     { "RESULT: FAIL - some state did not change" }))
}
finally {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}
