# water-size-experiment.ps1 — 封口实验 #8：水笔刷「整平 size+2 / 悬崖 size」在 1.32 上是否成立
#
# 流程：新建图 → 面板设 尺寸/形状 → 升高一层@P → 深水@P → 另存为 → 抽 w3e 统计 L/ct 环半径
# 判据：#8 成立 ⇒ L 环半径 ≈ size+2-1，ct 环半径 ≈ size-1（再经 3×3 扩展）
#
# 用法：.\water-size-experiment.ps1 -Size 3 -Out D:\tools\exp\watersize\w_s3.w3m

param(
  [int]$Size = 3,
  [string]$Out = "",
  [int]$PX = 420, [int]$PY = 420
)
$ErrorActionPreference = 'Continue'
. "D:\tools\menudrv.ps1"
. "D:\tools\ui.ps1"
$python = "D:\actions-runner\tools\python312\python.exe"
$exe = "D:\games\W3R\war3r\Warcraft.III.R\_retail_\x86_64\World Editor.exe"
if (-not $Out) { $Out = "D:\tools\exp\watersize\w_s$Size.w3m" }
$dir = Split-Path $Out -Parent
New-Item -ItemType Directory -Force -Path $dir | Out-Null

Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class WS { [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l); }' -ErrorAction SilentlyContinue
function L([int64]$h) { $sb = New-Object System.Text.StringBuilder 160; [U]::GetWindowText([IntPtr]$h, $sb, 160) | Out-Null; return $sb.ToString() }

Get-Process | Where-Object { $_.ProcessName -match 'World' } | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Seconds 3
$np = Start-Process -FilePath $exe -ArgumentList '-launch' -WorkingDirectory (Split-Path $exe) -PassThru
$Pid0 = $np.Id
"pid=$Pid0"
$os = $null
for ($i = 0; $i -lt 70; $i++) { $os = [U]::Top($Pid0) | Where-Object { $_ -match 'OsWindow' } | Select-Object -First 1; if ($os) { break }; Start-Sleep -Milliseconds 800 }
if (-not $os) { Write-Host "FAILED: no-main-window"; exit 1 }
Start-Sleep -Seconds 2
$hMain = HwndOf $os
[U]::SetForegroundWindow($hMain) | Out-Null; Start-Sleep -Milliseconds 700
[System.Windows.Forms.SendKeys]::SendWait("^n"); Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}"); Start-Sleep -Seconds 3

$hPal = HwndOf ([U]::Top($Pid0) | Where-Object { $_ -match '工具面板' } | Select-Object -First 1)
if (-not $hPal) { Write-Host "FAILED: 无工具面板"; exit 1 }
$lbls = @{}
foreach ($x in [U]::Kids($hPal)) {
  $p = $x -split '\|'
  if ($p[2] -match '^应用悬崖') { $lbls['clf'] = [int64]$p[0] }
  if ($p[2] -match '^尺寸') { $lbls['size'] = [int64]$p[0] }
  if ($p[2] -match '^形状') { $lbls['shape'] = [int64]$p[0] }
}
function Buttons { [U]::Kids($hPal) | Where-Object { $p = $_ -split '\|'; if ($p[1] -ne 'Button') { return $false }; $r = $p[3] -split ','; ([int]$r[0] -ge 500 -and [int]$r[1] -ge 200) } }
function PickAx([string]$axis, [string]$want) {
  foreach ($x in (Buttons)) {
    $h = [int64](($x -split '\|')[0]); $b = L $lbls[$axis]
    [WS]::SendMessage([IntPtr]$h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 140
    if ((L $lbls[$axis]) -ne $b -and (L $lbls[$axis]) -match $want) { return $h }
  }
  return 0
}
# 顺序：尺寸 / 形状 先设，笔刷最后选（否则会被覆盖回"没有"）
# 尺寸：改用面板上的 updown 控件点"上箭头"逐步加（尺寸按钮组不在 x>=500 区域内）
$ud = [U]::Kids($hPal) | Where-Object { ($_ -split '\|')[1] -eq 'msctls_updown32' } | Select-Object -First 1
if ($ud) {
  $uh = HwndOf $ud
  $cur = 1
  if ((L $lbls['size']) -match '(\d+)') { $cur = [int]$Matches[1] }
  for ($k = $cur; $k -lt $Size; $k++) {
    [WS]::SendMessage([IntPtr]$uh, 0x0111, [IntPtr]1, [IntPtr]::Zero) | Out-Null   # WM_COMMAND SB_LINEUP 的等效点击
    Start-Sleep -Milliseconds 200
    [WS]::SendMessage([IntPtr]$uh, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 250
  }
  Write-Host ("  尺寸(updown 点击后): '{0}'" -f (L $lbls['size']))
} else {
  Write-Host "  WARN: 未找到 updown 控件"
}
$hShape = PickAx 'shape' '圆形'
Write-Host ("  形状: {0} → '{1}'" -f $hShape, (L $lbls['shape']))
$hUp = PickAx 'clf' '升高一层'
Write-Host ("  升高一层: {0} → '{1}'" -f $hUp, (L $lbls['clf']))
ClickAt $PX $PY; Start-Sleep -Milliseconds 900
$hW = PickAx 'clf' '深水'
Write-Host ("  深水: {0} → '{1}'" -f $hW, (L $lbls['clf']))
ClickAt $PX $PY; Start-Sleep -Milliseconds 1200

# 单次另存为
[U]::PostMessage([IntPtr][int64](Get-Process -Id $Pid0).MainWindowHandle, 0x0111, [IntPtr]5, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
for ($round = 0; $round -lt 6; $round++) {
  $ds = [U]::Top($Pid0) | Where-Object { $_ -match '#32770' -and $_ -notmatch '工具面板|进程' }
  if (-not $ds) { break }
  foreach ($d in $ds) {
    $title = ($d -split '\|')[2]; $k = [U]::Kids((HwndOf $d))
    $btns = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' } | ForEach-Object { ($_ -split '\|')[2] } | Where-Object { $_ }
    if ($title -match '另存为') {
      $edits = $k | Where-Object { ($_ -split '\|')[1] -eq 'Edit' } | ForEach-Object { $p = $_ -split '\|'; $r = $p[3] -split ','; [pscustomobject]@{ H = [int64]$p[0]; Top = [int]$r[1] } } | Sort-Object Top
      $sv = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '保存' } | Select-Object -First 1
      SetEdit ([IntPtr](($edits | Where-Object { $_.Top -gt 300 } | Select-Object -First 1).H)) $Out; Start-Sleep -Milliseconds 700
      ClickButton (HwndOf $sv); Start-Sleep -Seconds 3
    } elseif ($btns -match '是' -and $btns -match '否') { $y = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '^是' } | Select-Object -First 1; ClickButton (HwndOf $y); Start-Sleep -Seconds 2 }
    elseif ($btns -match '确定') { $o = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '确定' } | Select-Object -First 1; ClickButton (HwndOf $o); Start-Sleep -Seconds 2 }
  }
}
Get-Process -Id $Pid0 -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not (Test-Path $Out)) { Write-Host "FAILED: 未生成 $Out"; exit 1 }
"OK: $Out  size=$((Get-Item $Out).Length)"
$w3e = [IO.Path]::ChangeExtension($Out, '.w3e')
& $python -c "import mpyq; a=mpyq.MPQArchive(r'$Out',listfile=False); open(r'$w3e','wb').write(a.read_file(b'war3map.w3e'))" 2>$null
"w3e: $w3e  $((Get-Item $w3e).Length) B"
