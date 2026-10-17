# save-map.ps1 — 带白名单的保存/另存为助手（避免"覆盖请求关不掉"再次发生）
#
# 背景：2026-10-17 的自动化里，`另存为` 覆盖已存在文件时会弹出
#       「确认另存为」（是/否）对话框；当时的纪律是"只自动点『仅确定』、是/否 一律停手"，
#       结果该对话框一直挂着，最后需要人工点击 ✗。
#
# 本脚本把「已知安全的确认」列入白名单自动处理：
#   · 提示「该地图仍使用默认名…」        → 确定
#   · 警告「开始点无效 / 不再作为对战地图」→ 确定
#   · 「确认另存为」/ 文本含「覆盖」「已存在」→ **是**（目标路径是我们自己指定的 ✓）
#   · 其它任何 是/否/取消 组合             → 不点，返回 NEEDS_HUMAN（并打印按钮文本）
#
# 用法：
#   .\save-map.ps1 -Pid 27644                       # 普通保存：菜单 ID 4
#   .\save-map.ps1 -Pid 27644 -AsPath D:\tools\exp\X1.w3m   # 另存为：菜单 ID 5 + 覆盖确认
#
# 返回：SAVED / NEEDS_HUMAN / FAILED:<原因>

param(
  [Parameter(Mandatory)][int]$Pid,
  [string]$AsPath,                # 为空 ⇒ 用「保存」(id=4)；非空 ⇒ 用「另存为」(id=5) 写到该路径
  [int]$Retries = 6
)

$ErrorActionPreference = 'Stop'
. "D:\tools\menudrv.ps1"
. "D:\tools\ui.ps1"

function TopList { [U]::Top($Pid) }
function ButtonsOf($h) { ([U]::Kids($h) | Where-Object { ($_ -split '\|')[1] -eq 'Button' }) }
function TextOf($b) { ($b -split '\|')[2] }
function HwndOfRow($b) { HwndOf $b }
function TitleOf($d) { ($d -split '\|')[2] }

function HandleOne {
  param($dlg)
  $h = HwndOf $dlg
  $title = TitleOf $dlg
  $btns = ButtonsOf $h
  $texts = @($btns | ForEach-Object { TextOf $_ })
  $joined = ($texts -join '/')

  # 需要拿到对话框正文才能判「覆盖」，故读取其子 Static 文本
  $statics = ([U]::Kids($h) | Where-Object { ($_ -split '\|')[1] -eq 'Static' } | ForEach-Object { ($_ -split '\|')[2] }) -join ' '
  $isOverwrite = ($joined -match '是' -and $joined -match '否') -and ($statics -match '覆盖|已存在|existing|overwrite' -or $title -match '确认|覆盖')

  if ($isOverwrite) {
    $yes = $btns | Where-Object { (TextOf $_) -match '^是' } | Select-Object -First 1
    if ($yes) { ClickButton (HwndOfRow $yes); return @{ act = 'clicked'; what = "覆盖确认 → 是（$title）" } }
  }
  if (-not ($joined -match '是|否|取消')) {
    $ok = $btns | Where-Object { (TextOf $_) -match '确定' } | Select-Object -First 1
    if ($ok) { ClickButton (HwndOfRow $ok); return @{ act = 'clicked'; what = "确定（$title）" } }
  }
  return @{ act = 'human'; what = "未识别的确认框：$title  按钮=$joined  文本=$statics" }
}

# ---- 触发保存 ----
$id = if ($AsPath) { 5 } else { 4 }
[U]::PostMessage([IntPtr][int64](Get-Process -Id $Pid).MainWindowHandle, 0x0111, [IntPtr]$id, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2

for ($i = 0; $i -lt $Retries; $i++) {
  $dlgs = TopList | Where-Object { $_ -match '#32770' -and $_ -notmatch '工具面板' }
  if (-not $dlgs) {
    if ($AsPath) {
      if (Test-Path -LiteralPath $AsPath) { return "SAVED:$AsPath" }
    } else { return 'SAVED' }
    Start-Sleep -Milliseconds 800; continue
  }
  foreach ($d in $dlgs) {
    $title = TitleOf $d
    if ($title -match '另存为') {
      # 文件名 Edit = 纵坐标 > 300 的那个（top=95 的是地址栏）
      $k = [U]::Kids((HwndOf $d))
      $edits = $k | Where-Object { ($_ -split '\|')[1] -eq 'Edit' } | ForEach-Object {
        $p = $_ -split '\|'; $r = $p[3] -split ','
        [pscustomobject]@{ H = [int64]$p[0]; Top = [int]$r[1] }
      } | Sort-Object Top
      $fileEdit = $edits | Where-Object { $_.Top -gt 300 } | Select-Object -First 1
      $save = $k | Where-Object { ($_ -split '\|')[1] -eq 'Button' -and ($_ -split '\|')[2] -match '保存' } | Select-Object -First 1
      if ($fileEdit -and $save) {
        SetEdit ([IntPtr]$fileEdit.H) $AsPath
        Start-Sleep -Milliseconds 600
        ClickButton (HwndOf $save)
        Start-Sleep -Seconds 2
        continue
      }
      return "NEEDS_HUMAN:另存为对话框结构不符（Edits=$($edits.Count)）"
    }
    $r = HandleOne $d
    if ($r.act -eq 'human') { return "NEEDS_HUMAN:$($r.what)" }
    Write-Host "  $($r.what)"
    Start-Sleep -Milliseconds 600
  }
}
return 'FAILED:重试次数用尽（对话框仍在）'
