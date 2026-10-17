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
$cands = [U]::Kids($hPal) | ForEach-Object {
  $p = $_ -split '\|'; $r = $p[3] -split ','
  [pscustomobject]@{ H = [int64]$p[0]; Cls = $p[1]; Lbl = $p[2]; X = [int]$r[0]; Y = [int]$r[1] }
}
$lbls = @{}
$lbls['clf']   = ($cands | Where-Object { $_.Lbl -match '^应用悬崖' } | Select-Object -First 1).H
$lbls['size']  = ($cands | Where-Object { $_.Lbl -match '^尺寸' } | Sort-Object Y | Select-Object -First 1).H
$lbls['shape'] = ($cands | Where-Object { $_.Lbl -match '^形状' } | Sort-Object Y | Select-Object -First 1).H
# 地形面板的尺寸按钮：y 在 717..749 的 5 个未标注按钮（按 x 排序 = 尺寸 1..5）
$sizeBtns  = $cands | Where-Object { $_.Cls -eq 'Button' -and $_.Y -ge 717 -and $_.Y -le 749 } | Sort-Object X
# 形状按钮：y 在 779..811 的 2 个（第一个 = 圆形）
$shapeBtns = $cands | Where-Object { $_.Cls -eq 'Button' -and $_.Y -ge 779 -and $_.Y -le 811 } | Sort-Object X
function Buttons { $cands | Where-Object { $_.Cls -eq 'Button' -and $_.X -ge 500 -and $_.Y -ge 200 } }
function PickAx([string]$axis, [string]$want) {
  foreach ($c in (Buttons)) {
    $h = [int64]$c.H; $b = L $lbls[$axis]
    [WS]::SendMessage([IntPtr]$h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    if ((L $lbls[$axis]) -ne $b -and (L $lbls[$axis]) -match $want) { return $h }
  }
  return 0
}
# 顺序：尺寸 / 形状 先设，笔刷最后选（否则会被覆盖回"没有"）
# 尺寸/形状：直接点对应按钮（尺寸按钮 y=717..749 共 5 个，按 x 排序对应 1..5）
if ($sizeBtns -and $sizeBtns.Count -ge $Size) {
  $hS = $sizeBtns[$Size - 1].H
  [WS]::SendMessage([IntPtr][int64]$hS, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  Start-Sleep -Milliseconds 400
  Write-Host ("  尺寸按钮[{0}] → 标签 '{1}'" -f $Size, (L $lbls['size']))
} else { Write-Host "  WARN: 尺寸按钮不足" }
if ($shapeBtns -and $shapeBtns.Count -ge 1) {
  $hShape2 = $shapeBtns[0].H
  [WS]::SendMessage([IntPtr][int64]$hShape2, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  Start-Sleep -Milliseconds 400
  Write-Host ("  形状按钮[0] → 标签 '{0}'" -f (L $lbls['shape']))
} else { Write-Host "  WARN: 形状按钮不足" }
$hUp = PickAx 'clf' '升高一层'
Write-Host ("  升高一层: {0} → '{1}'" -f $hUp, (L $lbls['clf']))
ClickAt $PX $PY; Start-Sleep -Milliseconds 900
$hW = PickAx 'clf' '深水'
Write-Host ("  深水: {0} → '{1}'" -f $hW, (L $lbls['clf']))
ClickAt $PX $PY; Start-Sleep -Milliseconds 1200

# 单次另存为
# ---- 另存为（Reforged = Vista 对话框：WM_SETTEXT 无效，且整条路径会被 IME 转换 ⇒ 只打 ASCII 文件名）----
function Shot([string]$path) {
  Add-Type -AssemblyName System.Drawing, System.Windows.Forms -ErrorAction SilentlyContinue
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size); $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
$basename = [IO.Path]::GetFileName($Out)
[U]::SetForegroundWindow($hMain) | Out-Null; Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait("^s"); Start-Sleep -Seconds 3; Shot "D:\tools\exp\watersize\shot_after_ctrls.png"
for ($r = 0; $r -lt 4; $r++) {
  $ds = [U]::Top($Pid0) | Where-Object { $_ -match '#32770' -and $_ -notmatch '工具面板|进程' }
  if (-not $ds) { break }
  foreach ($d in $ds) {
    $title = ($d -split '\|')[2]; $k = [U]::Kids((HwndOf $d))
    $btns = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' } | ForEach-Object { ($_ -split '\|')[2] } | Where-Object { $_ }
    if ($title -match '另存为') { break }
    if ($btns -match '是' -and $btns -match '否') { $y = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '^是' } | Select-Object -First 1; ClickButton (HwndOf $y); Start-Sleep -Seconds 2 }
    elseif ($btns -match '确定') { $o = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '确定' } | Select-Object -First 1; ClickButton (HwndOf $o); Start-Sleep -Seconds 2 }
  }
}
$dlg = [U]::Top($Pid0) | Where-Object { $_ -match '另存为' } | Select-Object -First 1
if ($dlg) {
  $dr = ($dlg -split '\|')[3] -split ','
  $k2 = [U]::Kids((HwndOf $dlg))
  $ed = $k2 | Where-Object { ($_ -split '\|')[1] -eq 'Edit' } | Select-Object -First 1
  $er = ($ed -split '\|')[3] -split ','
  ClickAt ([int]$dr[0] + [int]$er[0] + 4) ([int]$dr[1] + [int]$er[1] + 6)
  Start-Sleep -Milliseconds 400
  [System.Windows.Forms.SendKeys]::SendWait("^a"); Start-Sleep -Milliseconds 150
  [System.Windows.Forms.SendKeys]::SendWait($basename); Start-Sleep -Milliseconds 400; [System.Windows.Forms.SendKeys]::SendWait("{ENTER}"); Start-Sleep -Seconds 1
  # 保存按钮实际在屏幕外（控件 rect 与真实对话框不一致）⇒ 用文件名框的 Enter 提交
  Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
  Start-Sleep -Seconds 4
  Shot "D:\tools\exp\watersize\shot_after_enter.png"
  # 若对话框仍在，再试一次点击（兜底）
  $dlg2 = [U]::Top($Pid0) | Where-Object { $_ -match '另存为' } | Select-Object -First 1
  if ($dlg2) {
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}"); Start-Sleep -Seconds 3
  }
}
Get-Process -Id $Pid0 -ErrorAction SilentlyContinue | Stop-Process -Force
$found = Get-ChildItem "D:\tools\exp" -Recurse -Filter $basename -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $found) { Write-Host "FAILED: no-output ($basename)"; exit 1 }
"OK: $($found.FullName)  size=$($found.Length)"
$w3e = [IO.Path]::ChangeExtension($found.FullName, '.w3e')
& $python -c "import mpyq; a=mpyq.MPQArchive(r'$($found.FullName)',listfile=False); open(r'$w3e','wb').write(a.read_file(b'war3map.w3e'))" 2>$null
if (Test-Path $w3e) { "w3e: $w3e  $((Get-Item $w3e).Length) B" } else { "w3e 抽取失败" }