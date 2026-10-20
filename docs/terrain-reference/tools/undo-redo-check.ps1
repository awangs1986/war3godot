# undo-redo-check.ps1 — 撤销 / 重做 端到端（1 会话 / 1 写盘，计数断言与相机无关）
#
# 流程：新建图 → 升高一层@P → 斜坡单击@P（应得 9 点）→ [Undo] 或 [Undo→Redo] → 另存为 → 统计 ramp 点数
#
#   -Mode Undo → 期望 ramp = 0   （撤销掉斜坡这一笔）
#   -Mode Redo → 期望 ramp = 9   （撤销后再重做，恢复）
#
# 用法：.\undo-redo-check.ps1 -Mode Undo -Out D:\tools\exp\undoredo\undo.w3m

param(
  [Parameter(Mandatory)][ValidateSet('Undo','Redo')][string]$Mode,
  [string]$Out = "",
  [int]$PX = 420, [int]$PY = 420
)
$ErrorActionPreference = 'Stop'
. "D:\tools\menudrv.ps1"
. "D:\tools\ui.ps1"
$python = "D:\actions-runner\tools\python312\python.exe"
$exe = "D:\games\W3R\war3r\Warcraft.III.R\_retail_\x86_64\World Editor.exe"
if (-not $Out) { $Out = "D:\tools\exp\undoredo\$Mode.w3m" }
$dir = Split-Path $Out -Parent
New-Item -ItemType Directory -Force -Path $dir | Out-Null

Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class UR { [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l); }' -ErrorAction SilentlyContinue
function L([int64]$h) { $sb = New-Object System.Text.StringBuilder 160; [U]::GetWindowText([IntPtr]$h, $sb, 160) | Out-Null; return $sb.ToString() }

Get-Process | Where-Object { $_.ProcessName -match 'World' } | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Seconds 3
$np = Start-Process -FilePath $exe -ArgumentList '-launch' -WorkingDirectory (Split-Path $exe) -PassThru
$Pid0 = $np.Id
$os = $null
for ($i = 0; $i -lt 70; $i++) { $os = [U]::Top($Pid0) | Where-Object { $_ -match 'OsWindow' } | Select-Object -First 1; if ($os) { break }; Start-Sleep -Milliseconds 800 }
if (-not $os) { Write-Host "FAILED: 无主窗口"; exit 1 }
Start-Sleep -Seconds 2
$hMain = HwndOf $os
[U]::SetForegroundWindow($hMain) | Out-Null; Start-Sleep -Milliseconds 700
[System.Windows.Forms.SendKeys]::SendWait("^n"); Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}"); Start-Sleep -Seconds 3

$hPal = HwndOf ([U]::Top($Pid0) | Where-Object { $_ -match '工具面板' } | Select-Object -First 1)
$lbls = @{}
foreach ($x in [U]::Kids($hPal)) { $p = $x -split '\|'
  if ($p[2] -match '^应用悬崖') { $lbls['clf'] = [int64]$p[0] }
  if ($p[2] -match '^尺寸') { $lbls['size'] = [int64]$p[0] }
  if ($p[2] -match '^形状') { $lbls['shape'] = [int64]$p[0] } }
function Buttons { [U]::Kids($hPal) | Where-Object { $p = $_ -split '\|'; if ($p[1] -ne 'Button') { return $false }; $r = $p[3] -split ','; ([int]$r[0] -ge 500 -and [int]$r[1] -ge 200) } }
function PickAx([string]$axis, [string]$want) {
  foreach ($x in (Buttons)) { $h = [int64](($x -split '\|')[0]); $b = L $lbls[$axis]
    [UR]::SendMessage([IntPtr]$h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 140
    if ((L $lbls[$axis]) -ne $b -and (L $lbls[$axis]) -match $want) { return $h } }
  return 0
}
$null = PickAx 'size' '尺寸: 1$'
$null = PickAx 'shape' '圆形'
$null = PickAx 'clf' '升高一层'
ClickAt $PX $PY; Start-Sleep -Milliseconds 800
$hR = PickAx 'clf' '斜坡'
Write-Host ("  斜坡={0} → '{1}'" -f $hR, (L $lbls['clf']))
ClickAt $PX $PY; Start-Sleep -Milliseconds 1500
Write-Host "  已单击斜坡（应为 9 点）"

# 撤销 / 重做
[U]::SetForegroundWindow($hMain) | Out-Null; Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait("^z"); Start-Sleep -Seconds 2
Write-Host "  已 Ctrl+Z"
if ($Mode -eq 'Redo') {
  [System.Windows.Forms.SendKeys]::SendWait("^y"); Start-Sleep -Seconds 2
  Write-Host "  已 Ctrl+Y"
}

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
$w3e = [IO.Path]::ChangeExtension($Out, '.w3e')
& $python -c "import mpyq; a=mpyq.MPQArchive(r'$Out',listfile=False); open(r'$w3e','wb').write(a.read_file(b'war3map.w3e'))" 2>$null
$code = @'
import sys, os
sys.path.insert(0, r"D:\tools\pr6\docs\terrain-reference\tools")
import w3e_diff as W
h,c=W.load(sys.argv[1]); w=h["w"]
ramp=sorted((i%w,i//w) for i in range(w*h["h"]) if c[i].ramp)
lay=sorted({c[i].layer for i in range(w*h["h"])})
print("  layer 集合 =", lay)
print("  ramp 点数 =", len(ramp), " 坐标 =", ramp[:12])
'@
$code | Set-Content (Join-Path $dir "_an.py") -Encoding utf8NoBOM
& $python (Join-Path $dir "_an.py") $w3e
