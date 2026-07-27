# 有超时保护的 Godot 无头脚本运行器：脚本报错时 SceneTree 不会 quit()，会永久挂起，
# 因此这里统一用 Start-Process + Wait-Process -Timeout，超时强杀并输出 TIMEOUT 标记。
param(
  [Parameter(Mandatory = $true)][string]$Script,
  [int]$TimeoutSec = 120,
  [string[]]$ScriptArgs = @()
)

$godot = "D:\godot\4.7.1\Godot_v4.7.1-stable_mono_win64_console.exe"
$out = Join-Path $env:TEMP ("gate_out_" + [System.IO.Path]::GetRandomFileName() + ".txt")
$argList = @("--headless", "--path", ".", "-s", $Script)
if ($ScriptArgs.Count -gt 0) { $argList += "--"; $argList += $ScriptArgs }

$p = Start-Process -FilePath $godot -ArgumentList $argList -NoNewWindow -PassThru `
  -RedirectStandardOutput $out -RedirectStandardError "$out.err"
try { Wait-Process -Id $p.Id -Timeout $TimeoutSec -ErrorAction Stop } catch { }
$timedOut = $false
if (-not $p.HasExited) { $timedOut = $true; Stop-Process -Id $p.Id -Force; Start-Sleep -Milliseconds 300 }

if (Test-Path $out) { Get-Content $out }
if (Test-Path "$out.err") { $e = Get-Content "$out.err"; if ($e) { Write-Output "--- stderr ---"; $e } }
Remove-Item $out, "$out.err" -ErrorAction SilentlyContinue

if ($timedOut) { Write-Output "GATE_RESULT=TIMEOUT($TimeoutSec s) $Script" }
else { Write-Output "GATE_RESULT=EXIT_$($p.ExitCode) $Script" }
