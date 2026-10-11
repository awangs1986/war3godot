# d1map End-to-End Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** W3Editor 直接编辑保存 d1map，Godot 通过同一文件夹格式直接预览。

**Architecture:** 编辑器暂时保留 `W3EMap/W3Placement` 作为编辑模型，以
`NativeMapBridge` 隔离格式转换；Godot 通过 C# 加载 d1map，并把共享
`W3TerrainMap` 交给现有 C# 地形渲染器。格式契约位于 `W3TerrainCore`。

**Tech Stack:** .NET 8、C#、Avalonia、Godot 4.7 C#/GDScript、现有 CLI 验收。

## Global Constraints

- d1map 是编辑器与 Godot 的唯一新格式契约。
- 不让 W3TerrainCore 依赖 Avalonia、Godot 或 W3Editor.Core。
- 现有 War3 打开/保存和 terrain/model 行为不得回退。
- 每项行为先有失败验收，再写实现。
- 本目录不是 Git 仓库，不创建提交。

---

### Task 1: 编辑器 d1map 失败验收

**Files:**
- Create: `standalone/W3Editor.Mcp/D1MapAccept.cs`
- Modify: `standalone/W3Editor.Mcp/Program.cs`

**Interfaces:**
- Consumes: `NativeMapFormat.Save/Load`、`EditorSession`、`EditHistory`
- Produces: CLI `--d1map-accept [sourceWar3Folder] [workDir]`

- [ ] 写命令：先生成 d1map，再调用尚未支持的 `MapSession.Open(d1mapDir)`。
- [ ] 运行命令并确认以“找不到 war3map.w3e”失败，而不是测试代码错误。

### Task 2: 编辑器 d1map 打开与保存

**Files:**
- Create: `standalone/W3Editor.Core/Services/NativeMapBridge.cs`
- Modify: `standalone/W3Editor.Core/Services/MapSession.cs`
- Modify: `standalone/W3Editor/MainWindow.axaml.cs`
- Modify: `standalone/W3Editor/MainWindow.Ipc.cs`

**Interfaces:**
- Produces: `MapFormatKind`、`MapSession.FormatKind`、
  `NativeMapBridge.LoadIntoSession`/`CaptureFromSession`

- [ ] 实现角点和摆放的双向无损字段映射。
- [ ] `MapSession.Open` 优先检测 `NativeMapFormat.IsNativeMapFolder`。
- [ ] `MapSession.Save` 对 d1map 调用 `NativeMapFormat.Save`，不写 War3 文件。
- [ ] GUI/IPC 接受 d1map 文件夹或 `map.json`。
- [ ] 运行 `--d1map-accept`，确认删摆放后文件变化、undo 后三文件字节恢复。
- [ ] 运行 `--m2-accept` 与 `--native-roundtrip` 防止旧轨道回退。

### Task 3: Godot d1map 失败验收

**Files:**
- Create: `tools/native_map_smoke/NativeMapSmoke.cs`（或项目既有 headless 测试入口）
- Modify: `scripts/w3/w3_sandbox.gd`（仅增加测试退出/日志条件时）

**Interfaces:**
- Consumes: `--map=<folder>`
- Produces: 可从进程退出码和日志判定的 headless 验收

- [ ] 用 Task 2 产物启动 Godot headless 沙盒。
- [ ] 确认当前失败原因是 `W3MapArchive` 找不到 `war3map.w3e`。

### Task 4: Godot C# 原生加载适配

**Files:**
- Create: `addons/w3kit_cs/Native/NativeMapLoader.cs`
- Create: `addons/w3kit/native_map_adapter.gd`
- Modify: `addons/w3kit/w3_map_world.gd`

**Interfaces:**
- Produces: `NativeMapLoader.LoadFolder(string)`、`TerrainMap`、Godot 可读 placements
- Consumes: `NativeMapFormat.Load`

- [ ] C# 加载器读取 d1map，并返回共享 `W3TerrainMap`。
- [ ] 给现有 C# terrain/cliff renderer 增加直接接收共享模型的入口，避免重新编码。
- [ ] GDScript 世界节点识别 d1map，编排 terrain/water/catalog/placements。
- [ ] 运行 headless 验收，确认日志包含角点数、摆放数并正常退出。

### Task 5: 预览链收敛

**Files:**
- Modify: `standalone/W3Editor.Core/Services/GodotLauncher.cs`
- Modify: `standalone/W3Editor/MainWindow.axaml.cs`

**Interfaces:**
- `LaunchPreview/StartEmbeddedPreview` 仅传 `--map=<d1map folder>`

- [ ] d1map 会话直接预览。
- [ ] War3 会话预览前提示/生成 d1map 临时产物，不生成 `.res`。
- [ ] 移除 GUI 对 `ExportResAsync` 的入口；保留旧导入脚本供独立迁移工具使用。

### Task 6: 全量验证

- [ ] 构建 `W3TerrainCore`、`W3Editor.Core`、`W3Editor`、`W3Editor.Mcp`、Godot 主工程。
- [ ] 运行 `--d1map-accept`、`--native-roundtrip`、`--m2-accept`。
- [ ] 运行 W3Editor `--placetest --foresttest`。
- [ ] 运行 Godot headless d1map smoke，并检查退出码与成功日志。
- [ ] 对照设计逐条确认，无未实现入口或静默回退。

