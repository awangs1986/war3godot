# texture-blight-experiment.ps1 — 纹理 × 荒芜 四组实验（A 级取证）
#
# 目的：回答"纹理覆盖是否会清除荒芜位（w3e byte4 & 0x20）"，并区分：
#   同纹理 / 异纹理 × 普通点击 / Shift 点击   ⇒ 共 4 组
#
# 强制协议（见 WINDOWS-EXPERIMENT-PIPELINE.md §10）：
#   · 一个编辑器会话**最多写盘 1 次**（1.32 编辑器短间隔重复保存会崩溃 ✗）
#   · 每个阶段 = 启动 → 打开地图 → 断言控件 → 落笔 → 保存 → 复制 → 抽 w3e → 关闭
#   · 任何阶段返回 FAILED/NEEDS_HUMAN ⇒ 立即停止，不再点任何窗口
#
# 用法：
#   .\texture-blight-experiment.ps1 -Stage BlightOnly     # 新图 → 荒芜@P → 保存（荒芜基线）
#   .\texture-blight-experiment.ps1 -Stage BlightThenTex  # 新图 → 荒芜@P → 泥土@P → 保存（同/异纹理覆盖）
#   .\texture-blight-experiment.ps1 -Stage BlightThenRock # 新图 → 荒芜@P → 岩石@P → 保存
#   .\texture-blight-experiment.ps1 -Stage TexTwice       # 新图 → 泥土@P → 泥土@P → 保存（同纹理重涂幂等性）
#   .\texture-blight-experiment.ps1 -Stage Report         # 汇总判定
#
# 依赖：D:\tools\menudrv.ps1、D:\tools\ui.ps1、tools/w3e_diff.py、mpyq

param(
  [Parameter(Mandatory)][ValidateSet('BlightOnly','BlightThenTex','BlightThenRock','TexTwice','Report')][string]$Stage,
  [string]$Work = "D:\tools\exp\texblight",
  [int]$PaintX = 380, [int]$PaintY = 400,     # 视口内的地图像素（需在图上）
  [int]$CornerHintX = 31, [int]$CornerHintY = 32  # 仅用于打印相对坐标
)

$ErrorActionPreference = 'Stop'
. "D:\tools\menudrv.ps1"
. "D:\tools\ui.ps1"
$py   = "D:\actions-runner\tools\python312\python.exe"
$tool = "D:\tools\pr6\docs\terrain-reference\tools\w3e_diff.py"
$save = "D:\tools\pr6\docs\terrain-reference\tools\save-map.ps1"
$exe  = "D:\games\W3R\war3r\Warcraft.III.R\_retail_\x86_64\World Editor.exe"
New-Item -ItemType Directory -Force -Path $Work | Out-Null

function Log($m) { Write-Host $m }

function Get-EditorPid {
  $p = Get-Process | Where-Object { $_.ProcessName -match 'World' } | Select-Object -First 1
  if ($p) { return $p.Id } else { return 0 }
}

function Wait-EditorWindow([int]$pid2, [int]$timeoutSec = 90) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
    $tops = [U]::Top($pid2)
    $os = $tops | Where-Object { $_ -match 'OsWindow' } | Select-Object -First 1
    if ($os) { Start-Sleep -Milliseconds 800; return (HwndOf $os) }
    Start-Sleep -Milliseconds 500
  }
  return [IntPtr]::Zero
}

function Find-Palette([int]$pid2) {
  $pal = [U]::Top($pid2) | Where-Object { $_ -match '工具面板' } | Select-Object -First 1
  if (-not $pal) { return [IntPtr]::Zero }
  return (HwndOf $pal)
}

function Labels($hPal) {
  $r = @{}
  foreach ($x in [U]::Kids($hPal)) {
    $p = $x -split '\|'; $t = $p[2]
    if ($t -match '^应用纹理') { $r['tex'] = [int64]$p[0] }
    if ($t -match '^应用悬崖') { $r['clf'] = [int64]$p[0] }
    if ($t -match '^应用高度') { $r['h'] = [int64]$p[0] }
    if ($t -match '^尺寸') { $r['size'] = [int64]$p[0] }
    if ($t -match '^形状') { $r['shape'] = [int64]$p[0] }
  }
  return $r
}
function L([int64]$h) { $sb = New-Object System.Text.StringBuilder 160; [U]::GetWindowText([IntPtr]$h, $sb, 160) | Out-Null; return $sb.ToString() }

# 在调色板里按"点一下就改变标签"的方式找出目标按钮（每次启动句柄都不同，必须现场发现）
function Find-Brush($hPal, $lbls, [string]$wantLabel, [string]$axis) {
  $hits = @()
  foreach ($x in [U]::Kids($hPal)) {
    $p = $x -split '\|'
    if ($p[1] -ne 'Button') { continue }
    $r = $p[3] -split ','
    if ([int]$r[0] -lt 500 -or [int]$r[1] -lt 200) { continue }
    $h = [int64]$p[0]
    $before = L $lbls[$axis]
    [U]::SendMessage([IntPtr]$h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    $after = L $lbls[$axis]
    if ($after -ne $before -and $after -match [regex]::Escape($wantLabel)) { $hits += $h }
  }
  return $hits
}
function Find-Shape($hPal, $lbls, [string]$want) {
  foreach ($x in [U]::Kids($hPal)) {
    $p = $x -split '\|'
    if ($p[1] -ne 'Button') { continue }
    $r = $p[3] -split ','
    if ([int]$r[0] -lt 500 -or [int]$r[1] -lt 200) { continue }
    $h = [int64]$p[0]
    $b = L $lbls['shape']
    [U]::SendMessage([IntPtr]$h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    if ((L $lbls['shape']) -ne $b -and (L $lbls['shape']) -match $want) { return $h }
  }
  return 0
}
function Find-Size1($hPal, $lbls) {
  foreach ($x in [U]::Kids($hPal)) {
    $p = $x -split '\|'
    if ($p[1] -ne 'Button') { continue }
    $r = $p[3] -split ','
    if ([int]$r[0] -lt 500 -or [int]$r[1] -lt 200) { continue }
    $h = [int64]$p[0]
    $b = L $lbls['size']
    [U]::SendMessage([IntPtr]$h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    if ((L $lbls['size']) -ne $b -and (L $lbls['size']) -match '尺寸: 1$') { return $h }
  }
  return 0
}

function Open-Map([int]$pid2, [string]$path) {
  [U]::PostMessage([IntPtr][int64](Get-Process -Id $pid2).MainWindowHandle, 0x0111, [IntPtr]64188, [IntPtr]::Zero) | Out-Null
  Start-Sleep -Seconds 2
  for ($i = 0; $i -lt 4; $i++) {
    $d = [U]::Top($pid2) | Where-Object { $_ -match '#32770' -and $_ -match '打开|选择' } | Select-Object -First 1
    if (-not $d) { Start-Sleep -Milliseconds 800; continue }
    $k = [U]::Kids((HwndOf $d))
    $edits = $k | Where-Object { ($_ -split '\|')[1] -eq 'Edit' } | ForEach-Object {
      $p = $_ -split '\|'; $r = $p[3] -split ','
      [pscustomobject]@{ H = [int64]$p[0]; Top = [int]$r[1] }
    } | Sort-Object Top
    $fe = $edits | Where-Object { $_.Top -gt 300 } | Select-Object -First 1
    $btn = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '^打开' } | Select-Object -First 1
    if ($fe -and $btn) {
      SetEdit ([IntPtr]$fe.H) $path
      Start-Sleep -Milliseconds 600
      ClickButton (HwndOf $btn)
      Start-Sleep -Seconds 3
      return $true
    }
    return $false
  }
  return $false
}

function Extract-W3e([string]$w3m, [string]$tag) {
  $out = Join-Path $Work "$tag.w3e"
  & $py -c "import mpyq; a=mpyq.MPQArchive(r'$w3m',listfile=False); open(r'$out','wb').write(a.read_file(b'war3map.w3e'))" 2>$null
  if (Test-Path $out) { return $out } else { return $null }
}

function Finish([int]$pid2, [string]$verdict) {
  Get-Process -Id $pid2 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 2
  Log "VERDICT: $verdict"
  exit 0
}

# ---------------- Report ----------------
if ($Stage -eq 'Report') {
  Log "=== 纹理 × 荒芜 实验结果 ==="
  $map = @{ BlightOnly='荒芜@P'; BlightThenTex='荒芜@P → 泥土@P'; BlightThenRock='荒芜@P → 岩石@P'; TexTwice='泥土@P ×2' }
  foreach ($k in @('BlightOnly','BlightThenTex','BlightThenRock','TexTwice')) {
    $f = Join-Path $Work "$k.w3e"
    if (Test-Path $f) { $c = & $py $tool --count $f 2>&1 | Select-Object -Last 1; Log ("  {0} ({1}): {2}" -f $k, $map[$k], ($c -replace '^.*?: ', '')) }
    else { Log "  $k 缺（该阶段未跑）" }
  }
  $code = @'
import sys, os
sys.path.insert(0, r"D:\tools\pr6\docs\terrain-reference\tools")
import w3e_diff as W
D = r"D:\tools\exp\texblight"
def ld(n):
    p=os.path.join(D,n+".w3e")
    return W.load(p) if os.path.exists(p) else None
def cmp(a,b):
    A=ld(a); B=ld(b)
    if not A or not B: return None
    ha,ca=A; hb,cb=B; w=ha["w"]
    bl={ (i%w,i//w) for i in range(w*ha["h"]) if cb[i].blight and not ca[i].blight }
    cl={ (i%w,i//w) for i in range(w*ha["h"]) if ca[i].blight and not cb[i].blight }
    tx={ (i%w,i//w) for i in range(w*ha["h"]) if ca[i].tex!=cb[i].tex }
    gv={ (i%w,i//w) for i in range(w*ha["h"]) if ca[i].groundVar!=cb[i].groundVar }
    return dict(blight_set=len(bl), blight_clear=len(cl), tex=len(tx), gvar=len(gv),
                overlap=len(tx & cl), texture_corners=sorted(tx)[:8])
print("A) 荒芜基线 BlightOnly: 见上（blight 应 > 0）")
print("B) 荒芜→泥土 BlightThenTex  vs  BlightOnly:", cmp("BlightOnly","BlightThenTex"))
print("C) 荒芜→岩石 BlightThenRock vs  BlightOnly:", cmp("BlightOnly","BlightThenRock"))
print("D) 同纹理重涂 TexTwice: 与 BlightOnly 无关；幂等性比较见 TexTwice 自身 blight 计数")
'@
  $code | Set-Content (Join-Path $Work "_cmp.py") -Encoding utf8NoBOM
  & $py (Join-Path $Work "_cmp.py")
  exit 0
}

# ---------------- 每个阶段：启动 → 打开 → 落笔 → 保存 ----------------
$pid2 = Get-EditorPid
if ($pid2 -ne 0) {
  Log "发现已有编辑器 pid=$pid2 —— 按协议先关闭（保证 1 存档/会话）"
  Get-Process -Id $pid2 | Stop-Process -Force
  Start-Sleep -Seconds 3
}
Log "启动编辑器（阶段 $Stage）…"
$np = Start-Process -FilePath $exe -ArgumentList '-launch' -WorkingDirectory (Split-Path $exe) -PassThru
$pid2 = $np.Id
$hMain = Wait-EditorWindow $pid2 90
if ($hMain -eq [IntPtr]::Zero) { Finish $pid2 "FAILED: 编辑器 90s 内未出现主窗口" }
Log "  编辑器 pid=$pid2 主窗口=$hMain"

$hPal = [IntPtr]::Zero
for ($i = 0; $i -lt 20; $i++) { $hPal = Find-Palette $pid2; if ($hPal -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 500 }
if ($hPal -eq [IntPtr]::Zero) { Finish $pid2 "FAILED: 未找到调色板" }
$lbls = Labels $hPal
if (-not $lbls['tex']) { Finish $pid2 "FAILED: 调色板里没有 应用纹理 标签" }
Log "  调色板=$hPal"

# 打开目标地图（Base 阶段：新图；其它阶段：打开上一阶段产物）
# 每阶段都从「新建地图」开始（避开"打开地图"文件对话框与多次保存两个坑）
[U]::SetForegroundWindow($hMain) | Out-Null; Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait("^n"); Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}"); Start-Sleep -Seconds 3
Log "  已新建地图"

# 选择笔刷（每阶段不同）
$seq = switch ($Stage) {
  'BlightOnly'     { @('荒芜地表') }
  'BlightThenTex'  { @('荒芜地表','泥土') }
  'BlightThenRock' { @('荒芜地表','岩石') }
  'TexTwice'       { @('泥土','泥土') }
}
foreach ($sel in $seq) {
  $hits = Find-Brush $hPal $lbls $sel 'tex'
  if ($hits.Count -eq 0) { Finish $pid2 "FAILED: 找不到纹理刷「$sel」" }
  $h2 = $hits[0]
  [U]::SendMessage([IntPtr]$h2, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  Start-Sleep -Milliseconds 400
  $s1 = Find-Size1 $hPal $lbls
  if ($s1) { [U]::SendMessage([IntPtr]$s1, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 250 }
  $c1 = Find-Shape $hPal $lbls '圆形'
  if ($c1) { [U]::SendMessage([IntPtr]$c1, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 250 }
  Log "  选「$sel」  tex=$(L $lbls['tex'])  尺寸=$(L $lbls['size'])  形状=$(L $lbls['shape'])"
  ClickAt $PaintX $PaintY
  Start-Sleep -Milliseconds 1200
  Log "  已点击 ($PaintX,$PaintY)"
}

# 保存（1 次/会话）
$target = Join-Path $Work (@{ Base='B0'; Blight='B1'; SameTex='B2'; DiffTex='B3' }[$Stage] + '.w3m')
if ($true) {
  $r = & $save -ProcId $pid2 -AsPath $target 2>&1 | Select-Object -Last 1
} else {
  # 已打开的地图：用「保存」(id=4) 覆盖原文件，然后复制成目标名
  $r = & $save -ProcId $pid2 2>&1 | Select-Object -Last 1
  if ($r -like 'SAVED*') { Copy-Item $srcMap $target -Force }
}
Log "  保存结果: $r"
if ($r -notlike 'SAVED*') { Finish $pid2 "FAILED: 保存未成功（$r）" }

if (-not (Test-Path $target)) { Finish $pid2 "FAILED: 目标文件不存在 $target" }
$sha = (Get-FileHash $target).Hash
$w3e = Extract-W3e $target ($Stage)
if (-not $w3e) { Finish $pid2 "FAILED: 抽取 w3e 失败" }
$cnt = & $py $tool --count $w3e 2>&1 | Select-Object -Last 1
Log "  $target  size=$((Get-Item $target).Length)  sha256=$($sha.Substring(0,16))"
Log "  $cnt"
Finish $pid2 "OK: $Stage 完成（$target）"
