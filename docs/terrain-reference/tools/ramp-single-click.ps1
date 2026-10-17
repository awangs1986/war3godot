# ramp-single-click.ps1 — 坡道**单次点击**独立取证（审查建议的下一步）
#
# 两种模式（都是「1 个编辑器会话 / 1 次写盘」）：
#   -Mode Spacing   : 在相距 DX 像素的两点各「升高一层」，用于测出 **每角点占多少像素**
#   -Mode SubCorner : 先在 P 点「升高一层」造台阶，再在 P+(DX,DY) **单击一次**「斜坡」
#
# 用法：
#   .\ramp-single-click.ps1 -Mode Spacing -GDx 40 -Out D:\tools\exp\rampsingle\spacing.w3m
#   .\ramp-single-click.ps1 -Mode SubCorner -DX -5 -DY -5 -Out ...\sub_m5_m5.w3m
#
# 结果：保存后自动抽 w3e，并打印 layer/ramp 的绝对坐标，便于换算成"相对点击角点"。

param(
  [Parameter(Mandatory)][ValidateSet('Spacing','SubCorner')][string]$Mode,
  [string]$Out = "",
  [int]$PX = 420, [int]$PY = 420,       # 基准点（屏幕像素）
  [int]$GDx = 40,                        # Spacing 模式：第二点的横向偏移像素
  [int]$DX = 0, [int]$DY = 0,            # SubCorner 模式：斜坡单击相对基准点的偏移像素
  [switch]$RampAfter                        # Spacing 模式下：两处台阶后再单点一次斜坡（用于造 before/after 配对）
)

$ErrorActionPreference = 'Stop'
. "D:\tools\menudrv.ps1"
. "D:\tools\ui.ps1"
$python = "D:\actions-runner\tools\python312\python.exe"
$exe  = "D:\games\W3R\war3r\Warcraft.III.R\_retail_\x86_64\World Editor.exe"
if (-not $Out) { $Out = "D:\tools\exp\rampsingle\$Mode.w3m" }
$dir = Split-Path $Out -Parent
New-Item -ItemType Directory -Force -Path $dir | Out-Null

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public class RS {
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
'@ -ErrorAction SilentlyContinue

function L([int64]$h) { $sb = New-Object System.Text.StringBuilder 160; [U]::GetWindowText([IntPtr]$h, $sb, 160) | Out-Null; return $sb.ToString() }

# 关闭已有实例（保证 1 存档/会话）
Get-Process | Where-Object { $_.ProcessName -match 'World' } | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Seconds 3

$np = Start-Process -FilePath $exe -ArgumentList '-launch' -WorkingDirectory (Split-Path $exe) -PassThru
$Pid0 = $np.Id
$os = $null
for ($i = 0; $i -lt 70; $i++) {
  $os = [U]::Top($Pid0) | Where-Object { $_ -match 'OsWindow' } | Select-Object -First 1
  if ($os) { break }
  Start-Sleep -Milliseconds 800
}
if (-not $os) { Write-Host "FAILED: 主窗口未出现"; exit 1 }
Start-Sleep -Seconds 2
[U]::SetForegroundWindow((HwndOf $os)) | Out-Null; Start-Sleep -Milliseconds 700
[System.Windows.Forms.SendKeys]::SendWait("^n"); Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}"); Start-Sleep -Seconds 3

$hPal = HwndOf ([U]::Top($Pid0) | Where-Object { $_ -match '工具面板' } | Select-Object -First 1)
$lbls = @{}
foreach ($x in [U]::Kids($hPal)) {
  $p = $x -split '\|'
  if ($p[2] -match '^应用悬崖') { $lbls['clf'] = [int64]$p[0] }
  if ($p[2] -match '^尺寸') { $lbls['size'] = [int64]$p[0] }
  if ($p[2] -match '^形状') { $lbls['shape'] = [int64]$p[0] }
}
function Buttons { [U]::Kids($hPal) | Where-Object { $p = $_ -split '\|'; if ($p[1] -ne 'Button') { return $false }; $r = $p[3] -split ','; ([int]$r[0] -ge 500 -and [int]$r[1] -ge 200) } }

# 先用「尺寸/形状」再选笔刷（否则会被遍历按钮覆盖 ✗）
function PickAx([string]$axis, [string]$want) {
  foreach ($x in (Buttons)) {
    $h = [int64](($x -split '\|')[0]); $b = L $lbls[$axis]
    [RS]::SendMessage([IntPtr]$h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 140
    if ((L $lbls[$axis]) -ne $b -and (L $lbls[$axis]) -match $want) { return $h }
  }
  return 0
}
$null = PickAx 'size' '尺寸: 1$'
$null = PickAx 'shape' '圆形'
Write-Host ("  尺寸='{0}' 形状='{1}'" -f (L $lbls['size']), (L $lbls['shape']))

# 造台阶
$hRaise = PickAx 'clf' '升高一层'
Write-Host ("  升高一层={0} → '{1}'" -f $hRaise, (L $lbls['clf']))
ClickAt $PX $PY; Start-Sleep -Milliseconds 800
if ($Mode -eq 'Spacing') {
  ClickAt ($PX + $GDx) $PY; Start-Sleep -Milliseconds 800
  Write-Host "  已在 P 与 P+$GDx 各升高一层"
  if ($RampAfter) {
    $hRamp2 = PickAx 'clf' '斜坡'
    Write-Host ("  斜坡={0} → '{1}'" -f $hRamp2, (L $lbls['clf']))
    ClickAt $PX $PY; Start-Sleep -Milliseconds 1500
    Write-Host "  已在 P 单点一次斜坡（与 spacing 图构成 before/after）"
  }
} else {
  $hRamp = PickAx 'clf' '斜坡'
  Write-Host ("  斜坡={0} → '{1}'" -f $hRamp, (L $lbls['clf']))
  # **单次**点击
  ClickAt ($PX + $DX) ($PY + $DY); Start-Sleep -Milliseconds 1500
  Write-Host ("  已单击斜坡 @ ({0},{1})" -f ($PX + $DX), ($PY + $DY))
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
      SetEdit ([IntPtr](($edits | Where-Object { $_.Top -gt 300 } | Select-Object -First 1).H)) $Out
      Start-Sleep -Milliseconds 700
      ClickButton (HwndOf $sv); Start-Sleep -Seconds 3
    } elseif ($btns -match '是' -and $btns -match '否') {
      $y = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '^是' } | Select-Object -First 1
      ClickButton (HwndOf $y); Start-Sleep -Seconds 2
    } elseif ($btns -match '确定') {
      $o = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '确定' } | Select-Object -First 1
      ClickButton (HwndOf $o); Start-Sleep -Seconds 2
    }
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
p = sys.argv[1]
h, c = W.load(p); w = h["w"]
ramp = sorted((i % w, i // w) for i in range(w * h["h"]) if c[i].ramp)
hi = sorted((i % w, i // w, c[i].layer) for i in range(w * h["h"]) if c[i].layer != 2)
print("  抬高角点(layer!=2) =", hi)
print("  ramp 点数 =", len(ramp), " 坐标 =", ramp[:24])
'@
$code | Set-Content (Join-Path $dir "_an.py") -Encoding utf8NoBOM
& $python (Join-Path $dir "_an.py") $w3e
Write-Host ("OK: {0}  size={1}" -f $Out, (Get-Item $Out).Length)
