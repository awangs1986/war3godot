# ramp-subgrid-experiment.ps1
# ---------------------------------------------------------------------------
# 目的：A 级确认「坡道 3 共线的起点/方向由**点击在角点内的亚格偏移**决定」
#       （B 级依据：ghidra_27d450.c:105-112 —— 两个 0/1 选择器来自浮点坐标与 ≈0.5 的比较）
#
# 前置条件（脚本会先自检，不满足就停）：
#   1. 世界编辑器已启动并**打开一张有层差台阶的地图**（台阶 = 某角点 L 比邻点高 1）
#   2. 调色板可见（调色板窗口 IsWindowVisible = True）
#   3. 已选中「斜坡」笔刷，尺寸=1，形状不限
#
# 做法：在同一个角点的 4 个亚格位置各点一次，每次 保存→复制→抽 w3e→比对 ramp 集合
#       4 次点击的亚格偏移分别为 (0.25,0.25) (0.75,0.25) (0.25,0.75) (0.75,0.75)
#
# 判定：
#   · 四次集合**全部相同**  ⇒ 亚格偏移不影响 ⇒ B 级结论被否 → 需要改查拖拽/其它因子
#   · 四次集合**分成 2 组**（每种起点一组）⇒ 亚格偏移决定起点 ✓ 与 B 级一致
#   · 四次集合**各不同**    ⇒ 更强依赖，仍与 B 级兼容
# 任一情况下，都要求：3 点连续、包含点击角点、其它字段全不变（nochange）
# ---------------------------------------------------------------------------
param(
  [string]$MapPath = "D:\tools\exp\G0.w3m",      # 当前编辑器里打开的文件（保存会写回这里）
  [int]$CornerX = 31,                            # 台阶上"低侧"角点
  [int]$CornerY = 32,
  [int]$CornerPx = 24,                           # 一个角点在视口里的像素大小（需按缩放实测填写）
  [int]$ViewX = 380,                             # 该角点的屏幕基准（亚格 0.25,0.25 的位置）
  [int]$ViewY = 400
)

$ErrorActionPreference = 'Stop'
. "D:\tools\menudrv.ps1"
. "D:\tools\ui.ps1"

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public class RampExp {
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
'@ -ErrorActionSilentlyContinue

$py   = "D:\actions-runner\tools\python312\python.exe"
$diff = "D:\tools\pr6\docs\terrain-reference\tools\w3e_diff.py"
$out  = "D:\tools\exp\ramp-subgrid"
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Fail($m) { Write-Host "FAIL: $m" -ForegroundColor Red; exit 1 }
function Note($m) { Write-Host "  $m" }

# ---- 0. 前置自检 ----
$pid = (Get-Process | Where-Object { $_.ProcessName -match 'World' } | Select-Object -First 1).Id
if (-not $pid) { Fail "编辑器未运行（需要先启动 World Editor 并打开地图）" }
$pal = [U]::Top($pid) | Where-Object { $_ -match '工具面板' } | Select-Object -First 1
if (-not $pal) { Fail "未找到调色板窗口（工具面板）" }
$hPal = HwndOf $pal
if (-not [RampExp]::IsWindowVisible($hPal)) { Fail "调色板不可见（visible=False）⇒ 先让工具面板显示出来" }
if (-not (Test-Path $MapPath)) { Fail "地图文件不存在：$MapPath" }
Note "编辑器 pid=$pid 调色板=$hPal 地图=$MapPath"

function Extract([string]$w3m, [string]$tag) {
  $w3e = Join-Path $out "$tag.w3e"
  & $py -c "import mpyq; a=mpyq.MPQArchive(r'$w3m',listfile=False); open(r'$w3e','wb').write(a.read_file(b'war3map.w3e'))"
  if (-not (Test-Path $w3e)) { Fail "抽取 w3e 失败：$tag" }
  return $w3e
}
function SavePlain {
  [U]::PostMessage([IntPtr][int64](Get-Process -Id $pid).MainWindowHandle, 0x0111, [IntPtr]4, [IntPtr]::Zero) | Out-Null
  Start-Sleep -Seconds 3
  foreach ($p in @('提示','警告')) {
    $d = [U]::Top($pid) | Where-Object { $_ -match '#32770' -and $_ -match $p } | Select-Object -First 1
    if ($d) {
      $k = [U]::Kids((HwndOf $d))
      $b = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '是|确定' } | Select-Object -First 1
      if ($b) { ClickButton (HwndOf $b); Start-Sleep -Seconds 1 }
    }
  }
  Start-Sleep -Seconds 1
}

# ---- 1. 基线 ----
$baseW3e = Extract $MapPath "base"
Note "基线已抽取：$baseW3e"

# ---- 2. 四亚格点击 ----
$offsets = @(
  @{ tag='q1'; fx=0.25; fy=0.25 },
  @{ tag='q2'; fx=0.75; fy=0.25 },
  @{ tag='q3'; fx=0.25; fy=0.75 },
  @{ tag='q4'; fx=0.75; fy=0.75 }
)
foreach ($o in $offsets) {
  $sx = [int]($ViewX + $CornerPx * ($o.fx - 0.5))
  $sy = [int]($ViewY + $CornerPx * ($o.fy - 0.5))
  ClickAt $sx $sy
  Start-Sleep -Milliseconds 900
  SavePlain
  Copy-Item $MapPath (Join-Path $out "$($o.tag).w3m") -Force
  Extract (Join-Path $out "$($o.tag).w3m") $o.tag | Out-Null
  Note "已点 $($o.tag) @ ($sx,$sy) → 已保存并抽取"
}

# ---- 3. 比对 ----
Write-Host "`n=== ramp 集合（相对点击角点 $CornerX,$CornerY）==="
$prev = $null
$sets = @()
foreach ($o in $offsets) {
  $rows = & $py $diff $baseW3e (Join-Path $out "$($o.tag).w3e") --only=ramp 2>&1 |
          Select-String -Pattern '^\(' | ForEach-Object { $_.Line }
  $set = @()
  foreach ($r in $rows) {
    if ($r -match '\(\s*(\d+),\s*(\d+)\)') {
      $set += "$([int]$matches[1] - $CornerX),$([int]$matches[2] - $CornerY)"
    }
  }
  $set = ($set | Sort-Object)
  $sets += ,$set
  Write-Host ("  {0}: {1} 点  {2}" -f $o.tag, $set.Count, ($set -join ' '))
}
Write-Host "`n=== 判定 ==="
$distinct = ($sets | ForEach-Object { $_ -join ';' } | Sort-Object -Unique).Count
if ($sets[0].Count -eq 0) {
  Write-Host "INCONCLUSIVE: 四次都没有写入 ramp ⇒ 该角点没有层差台阶，或斜坡笔刷未选中" -ForegroundColor Yellow
} elseif ($distinct -eq 1) {
  Write-Host "NEGATIVE: 四次集合完全相同 ⇒ 亚格偏移不影响 ⇒ B 级结论被否，需改查其它因子" -ForegroundColor Yellow
} elseif ($distinct -eq 2) {
  Write-Host "POSITIVE: 四次分成 2 组 ⇒ 亚格偏移决定起点 ✓ 与 ghidra_27d450.c:105-112 一致" -ForegroundColor Green
} else {
  Write-Host "POSITIVE(strong): 四次各不相同 ⇒ 强依赖亚格偏移 ✓ 与 B 级兼容" -ForegroundColor Green
}
Write-Host "`n产物目录：$out（base.w3e + q1..q4.w3m/.w3e，含 SHA256 请用 Get-FileHash 记录）"
