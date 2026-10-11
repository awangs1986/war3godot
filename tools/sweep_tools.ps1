# 串行跑遍 tools/*.gd，抓取每个脚本的首个错误行，用于甄别门面删除后遗留的 API 落差。
# 每个脚本带超时保护（脚本报错时 SceneTree 不 quit，会永久挂起）。
param([int]$TimeoutSec = 45, [string]$Out = "out\tool_sweep.txt")

$godot = "D:\godot\4.7.1\Godot_v4.7.1-stable_mono_win64_console.exe"
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
"" | Set-Content $Out

$scripts = Get-ChildItem tools -Filter *.gd | Sort-Object Name
$i = 0
foreach ($s in $scripts) {
    $i++
    $res = "res://tools/$($s.Name)"
    $tmp = Join-Path $env:TEMP ("sw_" + [IO.Path]::GetRandomFileName() + ".txt")
    $p = Start-Process -FilePath $godot -ArgumentList @("--headless", "--path", ".", "-s", $res) `
        -NoNewWindow -PassThru -RedirectStandardOutput $tmp -RedirectStandardError "$tmp.err"
    try { Wait-Process -Id $p.Id -Timeout $TimeoutSec -ErrorAction Stop } catch { }
    $to = $false
    if (-not $p.HasExited) { $to = $true; Stop-Process -Id $p.Id -Force; Start-Sleep -Milliseconds 200 }

    $all = @()
    if (Test-Path $tmp) { $all += Get-Content $tmp }
    if (Test-Path "$tmp.err") { $all += Get-Content "$tmp.err" }
    Remove-Item $tmp, "$tmp.err" -ErrorAction SilentlyContinue

    # 只关心解析错误/不存在的函数或属性这类"门面删除造成的 API 落差"
    $bad = $all | Where-Object {
        $_ -match "Parse Error|Nonexistent function|Nonexistent property|Invalid call|Invalid access|Cannot call|Too few arguments|Too many arguments|Invalid assignment|Identifier .* not declared"
    } | Select-Object -First 1

    $okLine = $all | Where-Object { $_ -match "_OK\b|_OK:|PASS:|OK$" } | Select-Object -First 1
    $status = if ($bad) { "APIERR" } elseif ($to) { "TIMEOUT" } elseif ($okLine) { "OK" } else { "DONE" }

    $line = "{0,3} {1,-8} {2,-42} {3}" -f $i, $status, $s.Name, $bad
    Write-Output $line
    Add-Content $Out $line
}
Add-Content $Out "SWEEP_COMPLETE"
Write-Output "SWEEP_COMPLETE"
