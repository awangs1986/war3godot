# W3Editor CLI/IPC 补足规格（供自动化回归使用）

> 目的：让地形笔刷的**行为**可以在无人工介入下被验证（我们目前卡在"点不到 3D 视口里的地形"）。
> 本文只描述**接口契约**，不涉及算法改动。
> 所有路径、坐标、字段都写成**可直接断言**的形式。

---

## 0. 现状实测（为什么要补）

| 能力 | 现状 | 实测证据 |
|---|---|---|
| `--open <map.w3m>` | **可用** ✓ | 打开后解包到 `war3\maps\<名>\source`，`--view3d --shot3d out.png` 出图 1.4 MB、exit=0 |
| `--new 65x65xL` | **不可用** ✗ | 窗口标题仍是 `未打开地图`、工具栏无 `打开/保存/导出`、地形区空白；两次尝试（含以 `D:\godot project\diablo1like` 为工作目录）均失败 |
| `--brushop Ramp` | 可用（只设枚举） | `Enum.TryParse<BrushOp>` 成功 |
| **施加笔刷笔画** | **不存在** ✗ | CLI 无参数；IPC 只有 `ping/status/open_map/screenshot/ui_screenshot`（`MainWindow.Ipc.cs:73-79`） |
| **切换 2D/3D** | **仅有启动参数** `--view3d` ✗ | 运行期无命令；GUI 入口是右上「3D 视口」按钮 |
| **保存** | 仅 GUI（`Ctrl+S` / 工具栏「保存」）✗ | 用 `SendKeys` 不可靠：一次出现 `●` 未保存标记 |
| **撤销/重做** | 仅 GUI ✗ | 无命令 |
| **状态查询** | 弱 ✗ | `status` 未给出 view 模式、笔刷参数、脏标记、统计 |

**结论**：缺的是「**按角点坐标施加笔画**」和「**确定性地保存/查询状态**」两类命令。
有这两类，我就能在 CI 里直接断言 `w3e` 字节，而不必模拟鼠标（模拟鼠标正是当前失败的原因）。

---

## 1. 必须补的最小命令集（P0）

### 1.1 `--new` 修复（bug）

```
W3Editor.exe --new 65x65xL --save-to D:\out\demo\war3map.w3e --exit-after
```

- **要求**
  1. `--new WxH[xTileset]` 必须真正创建地图（当前静默失败）
  2. `_session.CreateNew(...)` 抛异常时**不得吞掉**：打印 `{"ok":false,"error":"..."}` 到 stdout 并以**非 0** 退出
  3. 打印创建出的 w3e 绝对路径（见 1.4 `--save-to`）

- **为什么**：现在无法在无人工的情况下得到一个"平地基线图"，所有实验都要用户先手动开图。

### 1.2 `--script <file.json>`：批量动作（P0 核心）

一条命令跑完"建图/开图 → 设笔刷 → 按角点落笔 → 保存 → 退出"，并输出机器可读结果。

```jsonc
// actions.json
{
  "open": "D:\\maps\\M0.w3m",          // 或 "new": {"w":65,"h":65,"tileset":"L"}
  "view": "3d",                         // "2d" | "3d"，运行期切换（不是启动参数）
  "brush": { "op": "Ramp", "radius": 1, "shape": "Circle" },
  "actions": [
    { "op": "stroke", "corners": [[29,31]], "modifiers": [] },          // 单点落笔
    { "op": "stroke", "corners": [[29,31]], "modifiers": ["Shift"] },   // Shift 落笔（移除）
    { "op": "undo" },
    { "op": "redo" },
    { "op": "screenshot", "path": "D:\\out\\shot.png" }
  ],
  "saveTo": "D:\\out\\result.w3e",
  "report": "D:\\out\\report.json"
}
```

```
W3Editor.exe --script D:\out\actions.json
```

- **输出**（stdout 与 `--report` 同内容）

```jsonc
{
  "ok": true,
  "openedFrom": "D:\\maps\\M0.w3m",
  "view": "3d",
  "brush": { "op": "Ramp", "radius": 1, "shape": "Circle" },
  "savedW3e": "D:\\out\\result.w3e",
  "savedW3eSha256": "…",
  "map": { "width": 65, "height": 65, "rampCount": 9, "blightCount": 0, "waterCount": 0, "boundaryCount": 0 },
  "actions": [
    { "index": 0, "op": "stroke", "changedCorners": 1 },
    { "index": 1, "op": "stroke", "changedCorners": 1 },
    { "index": 2, "op": "undo", "undoneCommands": 1 },
    { "index": 3, "op": "redo", "redoneCommands": 1 }
  ],
  "errors": []
}
```

- **失败**：`"ok": false`，`errors: [{"index":2,"message":"..."}]`，**exit code = 1**
- **语义要求**
  - `corners` 是 **w3e 角点坐标**（不是屏幕像素）——这是最关键的一条
  - 同一 `stroke` 内多个角点视为**一笔**（与鼠标按下→移动→抬起等价）
  - `modifiers` 支持 `"Shift"` / `"Ctrl"`，必须与 GUI 路径**走同一处判定**（`TerrainBrush.ShiftFor`）
  - `view:"3d"` 时必须真正切到 3D 并且笔画经由 **3D 代码路径**（`GlView` 的那条），而不是退化成 2D 直写

### 1.3 `--script` 内的 `view` 运行期切换

```
{"op":"view","mode":"3d"}    // 或 {"op":"view","mode":"2d"}
```

- **为什么**：`--view3d` 只能启动时切一次；我需要"2D 造台阶 → 切 3D → Shift 移除"在同一次运行里完成。

### 1.4 `--save-to <path>`：确定性写盘

```
{"saveTo":"D:\\out\\result.w3e"}
```

- 直接把 `war3map.w3e` 写到指定路径（**不需要**经过 MPQ 打包，也不要弹任何对话框）
- 同时把该文件 `sha256` 写进报告 —— 我据此做逐字节断言

### 1.5 `--state` / `{"op":"state"}`：无副作用查询

```jsonc
{
  "map": { "path": "...", "width":65, "height":65, "dirty": false,
           "counts": {"ramp":9,"blight":0,"water":0,"boundary":0},
           "layers":[2,3] },
  "view": "2d",
  "brush": { "op":"Ramp", "radius":1, "shape":"Circle", "textureIndex":0 },
  "history": { "undoDepth": 3, "redoDepth": 0 }
}
```

- **为什么**：现在只能靠标题里的 `●` 猜脏标记 ✗；`history` 深度可直接验证撤销/重做 ✓

---

## 2. 建议补的服务端能力（P1，IPC 同构）

把 1.2 的动作集在 IPC 上同构暴露（命令名一致，便于脚本二选一）：

```
POST /cmd {"cmd":"script","actions":{...}}          // 同 1.2 的 payload
POST /cmd {"cmd":"stroke","corners":[[29,31]],"modifiers":["Shift"],"brush":{"op":"Ramp","radius":1}}
POST /cmd {"cmd":"view","mode":"3d"}
POST /cmd {"cmd":"save_to","path":"D:\\out\\x.w3e"}
POST /cmd {"cmd":"state"}
```

> 现状 `MainWindow.Ipc.cs:73-79` 只有 `ping/status/open_map/screenshot/ui_screenshot`；
> 建议**保持现有命令不变**，只追加以上 5 个。

---

## 3. 我拿到这些命令后会跑的验收（现在跑不了的部分）

| # | 场景 | 断言 |
|---|---|---|
| 1 | **3D + Shift 移除** | `new 65x65xL` → 2D `CliffRaise1@(31,32)` → `view 3d` → `Ramp@(31,32)`（ramp 9）→ `Shift+Ramp@(31,32)` → `saveTo` ⇒ **ramp == 0** |
| 2 | **2D/3D 一致性** | 同样动作分别在 `view:"2d"` 与 `view:"3d"` 执行 ⇒ 两次 `w3e` **逐字节相同** |
| 3 | **撤销/重做** | `Ramp@(31,32)` → `undo` ⇒ ramp==0；`redo` ⇒ ramp==9 |
| 4 | **保存→重开** | `saveTo` → 重新 `open` 该 w3e → `saveTo` ⇒ 两次 sha256 相同 |
| 5 | **坡道 4 亚格** | （若 `stroke` 支持 `subCorner:[fx,fy]`）4 个亚格各一次 ⇒ 集合相同（我已有真机证据，可用于回归） |
| 6 | **平地不写 ramp** | `new` → `Ramp@(31,32)` ⇒ ramp == 0 |

> 其中 #1 #2 #6 是审查列出的 P0/P2 缺口，目前**只能靠人工点 3D 视口**。

---

## 4. 实现落点建议（最小改动）

| 需求 | 建议位置 |
|---|---|
| `--script` 解析与批处理 | `MainWindow.axaml.cs` 现有 CLI 段（`1238-1420` 附近），复用已有的 `--open/--brushop/--view3d/--shot` 分支 |
| `view` 运行期切换 | 复用右上「3D 视口」按钮的 handler（找到其 `Click` 逻辑直接调用） |
| `stroke` 按角点落笔 | 直接调 `TerrainBrush.Apply(map, cx, cy, brush, erase, stroke, frameDelta)`（**3D 分支要设 `GlView.StrokeShift` 或直接走 `ApplyBrushAt` 的 3D 入口**，确保与 GUI 同路径） |
| 修饰键判定 | 必须复用 `TerrainBrush.ShiftFor(op, modifierDown)`，禁止另写一份 |
| `saveTo` | 复用 `_session.Save()` 的 w3e 序列化（`W3EMap.SaveBytes()`），直接 `File.WriteAllBytes` |
| `state` | 复用 `RefreshFromSession()` 里已有的统计（`rampCount` 等）与 `_history` 的深度 |

---

## 5. 一条硬要求（避免"接口有了但语义跑了"）

`--script` 里的 `stroke` **必须**走与鼠标完全相同的落笔入口（同一个 `ApplyBrushAt`/`BrushStroke`），
并且**受同样的整笔去重约束**（`BrushStroke.AppliedCorners`）。
否则我拿到的证据只能证明"这条 CLI 能写文件"，**不能**证明"3D 视口里的 Shift 有效" ✓

---

## 6. 验收命令（你给我命令后我立刻跑）

```powershell
# 场景 1：3D + Shift 移除
W3Editor.exe --script D:\tools\exp\script\s1_3d_shift.json
python D:\tools\pr6\docs\terrain-reference\tools\w3e_diff.py --count D:\out\s1.w3e
# 期望：ramp=0

# 场景 2：2D/3D 一致
W3Editor.exe --script D:\tools\exp\script\s2a_2d.json
W3Editor.exe --script D:\tools\exp\script\s2b_3d.json
# 期望：两个 w3e 文件 sha256 相同
```

---

## 7. 我已备好的 4 份 `actions.json` 样例（命令补齐后我直接跑）

| 文件 | 场景 |
|---|---|
| `D:\tools\exp\script\s1_3d_shift.json` | 2D 造台阶 → 切 3D → Ramp 建坡道 → **Shift+Ramp 移除** ⇒ 期望 `ramp==0` |
| `s2a_2d.json` / `s2b_3d.json` | 同样动作分别在 2D/3D 执行 ⇒ 期望两个 w3e **sha256 相同** |
| `s3_undo_redo.json` | Ramp → `state` → `undo` → `state` → `redo` → `state` ⇒ 期望 ramp 9→0→9，且 `history.undoDepth` 相应变化 |

> 这 4 份只用到 P0 的 5 条命令（`new`/`view`/`stroke`+`modifiers`/`undo|redo`/`saveTo`/`state`）。
