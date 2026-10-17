# water-size-experiment.ps1 — 官方 Reforged 编辑器地形实验（基线/落笔两用）
#
# 设计要点（来自本会话已确立的约束）：
#   1. 同一编辑器会话只允许保存 1 次（二次保存会崩）⇒ 基线与落笔必须分两次启动
#   2. Ctrl+N + Enter 得到的是「模板地图」而非空白图 ⇒ 必须用基线会话把模板差异抵消
#   3. 保存走已验证过的 save-map.ps1（含 警告/提示/另存为 处理）
#   4. 尺寸按钮 = 面板上 y∈[717,749] 的 5 个未标注按钮，按 x 排序对应 1..5
#   5. IME 会把路径转全角 ⇒ 交给 save-map.ps1 用 SetEdit 写（不经键盘）
param(
  [int]$Size = 3,
  [switch]$Strokes,
  [string]$Brush = '深水',        # 深水 / 浅水 / 升高一层 / 平滑
  [string]$Tag = 'exp',
  [string]$Dir = 'D:\tools\exp\watersize',
  [int]$PX = 420, [int]$PY = 420
)
$ErrorActionPreference = 'Continue'
. "D:\tools\menudrv.ps1"
. "D:\tools\ui.ps1"
$python = "D:\actions-runner\tools\python312\python.exe"
$exe = "D:\games\W3R\war3r\Warcraft.III.R\_retail_\x86_64\World Editor.exe"
New-Item -ItemType Directory -Force -Path $Dir | Out-Null
$Out = Join-Path $Dir ("$Tag.w3m")
Remove-Item $Out -Force -ErrorAction SilentlyContinue
Remove-Item ([IO.Path]::ChangeExtension($Out, '.w3e')) -Force -ErrorAction SilentlyContinue

Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class WS2 { [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l); }' -ErrorAction SilentlyContinue
function L([int64]$h) { $sb = New-Object System.Text.StringBuilder 160; [U]::GetWindowText([IntPtr]$h, $sb, 160) | Out-Null; return $sb.ToString() }

Get-Process | Where-Object { $_.ProcessName -match 'World' } | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Seconds 3
$np = Start-Process -FilePath $exe -ArgumentList '-launch' -WorkingDirectory (Split-Path $exe) -PassThru
$Pid0 = $np.Id
"  pid=$Pid0  tag=$Tag  brush=$Brush  size=$Size  strokes=$Strokes"
$os = $null
for ($i = 0; $i -lt 70; $i++) { $os = [U]::Top($Pid0) | Where-Object { $_ -match 'OsWindow' } | Select-Object -First 1; if ($os) { break }; Start-Sleep -Milliseconds 800 }
if (-not $os) { Write-Host "  FAILED: no-main-window"; exit 1 }
Start-Sleep -Seconds 2
$hMain = HwndOf $os
[U]::SetForegroundWindow($hMain) | Out-Null; Start-Sleep -Milliseconds 700
[System.Windows.Forms.SendKeys]::SendWait("^n"); Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}"); Start-Sleep -Seconds 3
"  title: " + ((Get-Process -Id $Pid0).MainWindowTitle)

$hPal = HwndOf ([U]::Top($Pid0) | Where-Object { $_ -match '工具面板' } | Select-Object -First 1)
if (-not $hPal) { Write-Host "  FAILED: no-palette"; exit 1 }
$cands = [U]::Kids($hPal) | ForEach-Object {
  $p = $_ -split '\|'; $r = $p[3] -split ','
  [pscustomobject]@{ H = [int64]$p[0]; Cls = $p[1]; Lbl = $p[2]; X = [int]$r[0]; Y = [int]$r[1] }
}
$lblSize  = ($cands | Where-Object { $_.Lbl -match '^尺寸' } | Sort-Object Y | Select-Object -First 1).H
$lblShape = ($cands | Where-Object { $_.Lbl -match '^形状' } | Sort-Object Y | Select-Object -First 1).H
$lblClf   = ($cands | Where-Object { $_.Lbl -match '^应用悬崖' } | Select-Object -First 1).H
$sizeBtns  = $cands | Where-Object { $_.Cls -eq 'Button' -and $_.Y -ge 717 -and $_.Y -le 749 } | Sort-Object X
$shapeBtns = $cands | Where-Object { $_.Cls -eq 'Button' -and $_.Y -ge 779 -and $_.Y -le 811 } | Sort-Object X

function ClickH([int64]$h) { [WS2]::SendMessage([IntPtr]$h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 400 }
if ($sizeBtns -and $sizeBtns.Count -ge $Size) { ClickH $sizeBtns[$Size - 1].H; "  尺寸 → '" + (L $lblSize) + "'" }
if ($shapeBtns -and $shapeBtns.Count -ge 1) { ClickH $shapeBtns[0].H; "  形状 → '" + (L $lblShape) + "'" }

if ($Strokes) {
  $cands2 = [U]::Kids($hPal) | ForEach-Object {
    $p = $_ -split '\|'; $r = $p[3] -split ','
    [pscustomobject]@{ H = [int64]$p[0]; Cls = $p[1]; Lbl = $p[2]; X = [int]$r[0]; Y = [int]$r[1] }
  }
  $pick = {
    param($want)
    foreach ($c in ($cands2 | Where-Object { $_.Cls -eq 'Button' -and $_.X -ge 500 -and $_.Y -ge 200 })) {
      $b = L $lblClf
      ClickH $c.H
      $now = L $lblClf
      if ($now -ne $b -and $now -match $want) { return $c.H }
    }
    return 0
  }
  $h = & $pick $Brush
  "  brush: '$Brush' → '" + (L $lblClf) + "'"
  if ($h) { ClickAt $PX $PY; Start-Sleep -Milliseconds 1200; "  clicked ($PX,$PY)" }
}

& "D:\tools\pr6\docs\terrain-reference\tools\save-map.ps1" -ProcId $Pid0 -AsPath $Out
"  save exit=$LASTEXITCODE"
Get-Process -Id $Pid0 -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not (Test-Path $Out)) { Write-Host "  FAILED: no-output"; exit 1 }
"  OK: $Out  size=$((Get-Item $Out).Length)"
$w3e = [IO.Path]::ChangeExtension($Out, '.w3e')
& $python -c "import mpyq; a=mpyq.MPQArchive(r'$Out',listfile=False); open(r'$w3e','wb').write(a.read_file(b'war3map.w3e'))" 2>$null
if (Test-Path $w3e) { "  w3e: $w3e  $((Get-Item $w3e).Length) B" } else { "  w3e 抽取失败" }
