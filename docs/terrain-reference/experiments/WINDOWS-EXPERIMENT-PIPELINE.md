# Windows 真机实验流水线（可复现）

> 目的：在 **Windows 原生** 环境完成"落笔 → 保存 → 抽 w3e → 逐字节对照"，用于补齐 G5/G6/G8。
> 全部通过 **Win32 消息**驱动，不依赖键盘盲打或坐标点菜单（本环境两者都不可靠）。

## 1. 启动

```powershell
$exe = "D:\games\W3R\war3r\Warcraft.III.R\_retail_\x86_64\World Editor.exe"
Start-Process -FilePath $exe -ArgumentList '-launch' -WorkingDirectory (Split-Path $exe)
```
- **必须带 `-launch`**：不带参数会在启动后立即退出（实测）
- 本机 EULA 已接受 ⇒ 不再弹欢迎框；工具面板会自动出现

## 2. 新建地图（零坐标）

```powershell
. D:\tools\menudrv.ps1        # GetMenu/GetMenuItemInfo/PostMessage 封装
$h = (Get-Process -Id $pid).MainWindowHandle
[M]::PostMessage($h, 0x0111, [IntPtr]1, [IntPtr]::Zero)   # 文件→创建新地图(N) 用 Ctrl+N 亦可
# 「创建新地图」对话框底部的确定按钮在 1280x800 下**不可见** ⇒ 用 Enter 接受默认 64×64
```

## 3. 保存地图（关键：三层对话框 + 一个陷阱）

菜单 ID（`GetMenuItemInfo` 读出的真实值）：

| 菜单项 | id |
|---|---|
| 保存地图(S) | **4** |
| 地图另存为...(A) | **5** |
| 荒芜地表(H)（察看菜单） | **517** |
| 工具→刷子大小(B) / 刷子形状(A) | 1282 / 1283 |
| 文件→创建新地图(N) | 0（用 Ctrl+N） |

流程：

1. `PostMessage(hMain, WM_COMMAND=0x0111, 5, 0)` → 触发另存为
2. **警告「有玩家的开始点未被放置。是否自动放置开始点？」**
   - 点 **否** ⇒ **保存被取消**（陷阱，实测）
   - 点 **是** ⇒ 继续（开始点与 w3e 无关，安全）
3. **提示「该地图仍使用默认名…」** → 点「确定」
4. `另存为` 对话框（class `#32770`）：
   - 文件名 `Edit`：对其 `WM_SETTEXT (0x000C)` 写完整路径
   - `保存(&S)` 按钮：`BM_CLICK (0x00F5)`
5. 标题栏变为 `[D:/tools/exp/E0.w3m]` 即成功

## 4. 抽取 w3e（mpyq，无需 listfile）

```python
import mpyq
a = mpyq.MPQArchive(r"D:\tools\exp\E0.w3m", listfile=False)
data = a.read_file(b'war3map.w3e')     # 29,644 字节，头 57334521 0b000000 = "W3E!" + version 11
```
注意：`a.files` 在 `listfile=False` 时为 `None`，**不能** `len()`；直接 `read_file` ✓

## 5. 实测踩坑清单

| 现象 | 原因 / 处理 |
|---|---|
| 不带 `-launch` 启动即退出 | 必须加 `-launch` |
| 点坐标 y=58 打开了「打开文档」 | 菜单栏坐标映射不可靠 ⇒ **一律用菜单 ID + WM_COMMAND** |
| `Ctrl+S` 后无文件、出现两个 ghost「警告」窗口 | 编辑器进入僵死态（窗口不响应 WM_CLOSE）⇒ 按 pid 强杀重启 |
| 另存为时选「否」 | 保存被**取消**，必须选「是」 |
| `mpyq` 报 `len(None)` | 用 `listfile=False` 时 `files` 为 None，直接 `read_file` |

## 6. 已产出

- `D:\tools\exp\E0.w3m`（16KB，64×64 新图）
- `D:\tools\exp\E0.w3e`（29,644 字节，version 11）⇒ 可直接与编辑后版本逐字节对照
