# FBX Import Plugin (Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 W3Editor 加上最小 DLL 插件宿主，并交付示例 FBX 导入插件：结构检查、喷泉×10 自动缩放、外部库落盘 / 地图内嵌落盘、3D 视口预览。

**Architecture:** `W3Editor.PluginContracts` 定义零 UI 契约；编辑器启动时用 `AssemblyLoadContext` 扫 `plugins/**/*.dll` 并注入菜单。`FbxImportPlugin` 用 Assimp.NET 读 FBX，校验后按模式写入共享库或地图 `imported/`，再通过宿主 API 预览。

**Tech Stack:** .NET 8、Avalonia 11、Assimp.NET、现有 Silk.NET GL 视口。

## Global Constraints

- 响应语言：简体中文 UI / 报告文案。
- 插件只引用 `W3Editor.PluginContracts`，不得引用 Avalonia / Silk.NET / W3Editor.exe。
- 两种归属都做：外部模型 → `SharedModelsRoot/<id>/`；打包进地图 → `CurrentMapFolder/imported/<id>/`。
- 材质检查仅结构（槽位 / 512|1024 / 命名 / 路径存在），不做像素分析。
- 超限：包围盒最长边 > `10 * FountainEdge` 时均匀缩放到上限。
- 本版不写 `placements.bin` / doodad 列表；预览可替换，关图清预览。
- 本目录不是 Git 仓库，不创建提交。
- 每项行为先红灯验收再实现。

## File Map

| 路径 | 职责 |
|---|---|
| `standalone/W3Editor.PluginContracts/` | 契约：`IW3EditorPlugin`、`IEditorPluginContext`、预览/报告 DTO |
| `standalone/W3Editor/Plugins/PluginLoader.cs` | 扫 DLL、隔离加载、收集命令 |
| `standalone/W3Editor/Plugins/EditorPluginContext.cs` | 宿主上下文实现（文件框、菜单、预览、报告） |
| `standalone/W3Editor/Rendering/PluginPreviewPass.cs` | 单槽 GL 预览网格 |
| `standalone/W3Editor.Core/Services/EditorPaths.cs` | 增加 `PluginsDir`、`SharedModelsRoot` |
| `plugins/FbxImportPlugin/` | 示例插件：导入、校验、缩放、落盘 |
| `standalone/W3Editor.Mcp/PluginHostSelfTest.cs` | CLI：宿主加载自检 |
| `standalone/W3Editor.Mcp/FbxImportAccept.cs` | CLI：缩放 + 两种落盘验收 |

---

### Task 1: 插件契约与宿主红灯

**Files:**
- Create: `standalone/W3Editor.PluginContracts/W3Editor.PluginContracts.csproj`
- Create: `standalone/W3Editor.PluginContracts/IW3EditorPlugin.cs`
- Create: `standalone/W3Editor.PluginContracts/IEditorPluginContext.cs`
- Create: `standalone/W3Editor.PluginContracts/PluginDtos.cs`
- Create: `standalone/W3Editor.Mcp/PluginHostSelfTest.cs`
- Modify: `standalone/W3Editor.Mcp/Program.cs`
- Modify: `standalone/W3Editor.Mcp/W3Editor.Mcp.csproj`（引用 Contracts）

**Interfaces:**
- Produces:
  ```csharp
  public interface IW3EditorPlugin {
      string Id { get; }
      string Name { get; }
      string Version { get; }
      void Register(IEditorPluginContext ctx);
  }
  public interface IEditorPluginContext {
      void AddMenuCommand(string menuPath, string id, Func<CancellationToken, Task> handler);
      Task<string?> PickOpenFileAsync(string title, params string[] filters);
      bool HasOpenMap { get; }
      string? CurrentMapFolder { get; }
      string SharedModelsRoot { get; }
      void PlacePreviewMesh(PluginMeshPreview mesh, string label);
      void ShowReport(string title, PluginValidationReport report);
  }
  public sealed class PluginMeshPreview {
      public float[] Positions = Array.Empty<float>(); // xyz interleaved
      public float[] Normals = Array.Empty<float>();
      public float[] Uv0 = Array.Empty<float>();
      public int[] Indices = Array.Empty<int>();
      public string? AlbedoPath;
  }
  public enum PluginIssueSeverity { Info, Warn, Error }
  public sealed class PluginValidationIssue {
      public PluginIssueSeverity Severity;
      public string Code = "";
      public string Message = "";
  }
  public sealed class PluginValidationReport {
      public string Ownership = ""; // "external" | "map-packed"
      public string TargetDir = "";
      public bool AutoScaled;
      public float ScaleFactor = 1f;
      public List<PluginValidationIssue> Issues = new();
  }
  ```
- Consumes: 无

- [ ] **Step 1: 建 Contracts 工程与 DTO/接口（仅契约，无宿主实现）**
- [ ] **Step 2: 写 `PluginHostSelfTest`**  
  期望：`PluginLoader.LoadAll(pluginsDir)` 尚不存在 → 编译失败或运行断言失败。  
  验收意图：放入一个最小假插件 DLL 后，应返回 `Count == 1` 且 `Id == "com.d1.test-plugin"`。
- [ ] **Step 3: 运行确认红灯**  
  `dotnet build standalone/W3Editor.Mcp/W3Editor.Mcp.csproj -c Release`  
  Expected: 因缺少 `PluginLoader` 失败，或 CLI `--plugin-host-selftest` 明确失败。

---

### Task 2: 最小宿主加载器 + 路径

**Files:**
- Create: `standalone/W3Editor/Plugins/PluginLoader.cs`
- Modify: `standalone/W3Editor.Core/Services/EditorPaths.cs`
- Modify: `standalone/W3Editor/W3Editor.csproj`（引用 Contracts）
- Modify: `standalone/W3Editor.Mcp/PluginHostSelfTest.cs`（可测的加载路径）

**Interfaces:**
- Produces:
  ```csharp
  public static class PluginLoader {
      public static IReadOnlyList<IW3EditorPlugin> LoadAll(string pluginsDir);
  }
  // EditorPaths
  public static string PluginsDir { get; }      // {AppContext.BaseDirectory}/plugins 或 W3EDITOR_PLUGINS_DIR
  public static string SharedModelsRoot { get; } // {OutDir}/shared_models 或 W3EDITOR_SHARED_MODELS
  ```

- [ ] **Step 1: 实现 `EditorPaths.PluginsDir` / `SharedModelsRoot`**
- [ ] **Step 2: 实现 `PluginLoader.LoadAll`**  
  - 递归 `*.dll`；跳过名字含 `W3Editor.PluginContracts` / `Assimp` 原生无关托管依赖可仍加载  
  - 每个 DLL 独立 `AssemblyLoadContext(isCollectible: false)`  
  - 找 `IW3EditorPlugin` 可实例化类型，`Activator.CreateInstance`  
  - 异常吞掉并 `Console.Error` 记录，继续下一个
- [ ] **Step 3: 最小测试插件源码生成在 selftest 临时目录并 `dotnet build -o`，再跑 `--plugin-host-selftest`**  
  Expected: `pass: true`, `loaded: 1`

---

### Task 3: 编辑器挂菜单与上下文

**Files:**
- Create: `standalone/W3Editor/Plugins/EditorPluginContext.cs`
- Modify: `standalone/W3Editor/MainWindow.axaml`（`工具` 下预留 `MenuPlugins`）
- Modify: `standalone/W3Editor/MainWindow.axaml.cs`（启动加载插件、注入菜单）
- Create: `standalone/W3Editor/Dialogs/PluginReportDialog.cs`（简单只读报告窗）

**Interfaces:**
- Produces: `EditorPluginContext` 绑定当前 `MainWindow` / `MapSession` / `GlView`
- Consumes: `PluginLoader`, `IEditorPluginContext`

- [ ] **Step 1: `OnWindowOpened` 末尾调用 `LoadPlugins()`**  
  - `PluginLoader.LoadAll(EditorPaths.PluginsDir)`  
  - 新建 `EditorPluginContext(this)`  
  - 每个插件 `Register(ctx)`；失败记状态栏
- [ ] **Step 2: `AddMenuCommand` 解析 `工具(_T)/插件/<name>`，在 `MenuPlugins` 下加 `MenuItem`，Click 时 `await handler`**
- [ ] **Step 3: `PickOpenFileAsync` 用 Avalonia `StorageProvider`；`HasOpenMap`/`CurrentMapFolder` 来自 `_session`（仅 `IsFolder` 时给路径）**
- [ ] **Step 4: `ShowReport` 打开 `PluginReportDialog`**
- [ ] **Step 5: `PlacePreviewMesh` 先留空实现或抛 `NotImplementedException`（Task 5 补齐）——若抛异常，插件应捕获并写入报告**

---

### Task 4: FBX 插件核心（校验 / 缩放 / 双落盘）红→绿

**Files:**
- Create: `plugins/FbxImportPlugin/FbxImportPlugin.csproj`
- Create: `plugins/FbxImportPlugin/FbxImportPlugin.cs`
- Create: `plugins/FbxImportPlugin/FbxPipeline.cs`
- Create: `plugins/FbxImportPlugin/FountainReference.cs`
- Create: `plugins/FbxImportPlugin/MaterialStructureChecker.cs`
- Create: `standalone/W3Editor.Mcp/FbxImportAccept.cs`
- Modify: `standalone/W3Editor.Mcp/Program.cs`（`--fbx-import-accept`）
- Modify: `standalone/W3Editor.csproj` 或 solution 级：构建后把插件拷到 `W3Editor/bin/.../plugins/`

**Interfaces:**
- Produces:
  ```csharp
  public enum FbxOwnership { External, MapPacked }
  public static class FountainReference {
      // 首次实现时用 Lost Temple City_Fountain 实测最长边写入；注释测量方法。
      public const float EdgeWorldUnits = /* measured */;
      public const float MaxEdgeWorldUnits = EdgeWorldUnits * 10f;
  }
  public static class FbxPipeline {
      public static FbxImportResult Import(string fbxPath, FbxOwnership ownership, string targetRoot);
  }
  public sealed class FbxImportResult {
      public PluginMeshPreview Mesh = new();
      public PluginValidationReport Report = new();
      public string ModelId = "";
      public string OutputDir = "";
  }
  ```
- Consumes: Assimp.NET、Contracts DTO

- [ ] **Step 1: 量喷泉尺寸**  
  用现有 MDX 或摆放：Lost Temple 中心喷泉（`City_Fountain` / 对应 doodad）包围盒最长边 → 写入 `FountainReference.EdgeWorldUnits` 并注释来源。若暂时无法读 MDX，可用编辑器里该摆放的经验尺寸，但必须在常量旁写清「待 MDX 实测替换」。
- [ ] **Step 2: 写 `FbxImportAccept` 红灯**  
  - 生成两个合成场景：边长 `0.5*Max` 与 `2*Max` 的单位立方体 FBX（可用 Assimp 导出或手写极简 FBX/用 OBJ 再转；若 Assimp 写不便，accept 直接测 `FbxPipeline.ApplyScale(aabb)` 纯函数 + 落盘函数）。  
  - 断言：大模型 `AutoScaled==true` 且缩放后最长边 ≈ `MaxEdge`；小模型不缩放。  
  - 断言：External 写到 `shared_models/<id>/meta.json`；MapPacked 写到 `map/imported/<id>/meta.json`。
- [ ] **Step 3: 实现 `MaterialStructureChecker`**（槽位 / 512|1024 / 幂次 / 命名 / 缺失路径）
- [ ] **Step 4: 实现 `FbxPipeline.Import`**  
  - Assimp 导入，轴转到编辑器 +X东 +Y北 +Z上  
  - AABB → 缩放  
  - 检查 → 报告  
  - 写目录：`mesh.bin`（简单自描述：顶点数/索引/float 数组）或 `preview.obj` + 拷贝贴图 + `meta.json`  
  - `meta.json` 字段：`source`, `ownership`, `scaleFactor`, `autoScaled`, `issues[]`, `fountainMaxEdge`
- [ ] **Step 5: 插件 `Register` 两条菜单**，分别调 Import(External/MapPacked)；MapPacked 时若 `CurrentMapFolder==null` 则报告 Error。  
- [ ] **Step 6: 跑 `--fbx-import-accept`** Expected: `pass: true`

---

### Task 5: 视口预览通道

**Files:**
- Create: `standalone/W3Editor/Rendering/PluginPreviewPass.cs`
- Modify: `standalone/W3Editor/Rendering/GlTerrainScene.cs`（每帧画预览；`SetPluginPreview` / `ClearPluginPreview`）
- Modify: `standalone/W3Editor/Plugins/EditorPluginContext.cs`（实现 `PlacePreviewMesh`）
- Modify: `standalone/W3Editor/MainWindow.axaml.cs`（关图/换图清预览；自动开 3D）

**Interfaces:**
- Produces: `GlTerrainScene.SetPluginPreview(PluginMeshPreview, label)` / `ClearPluginPreview()`
- Consumes: `PluginMeshPreview`

- [ ] **Step 1: `PluginPreviewPass` 上传 VBO/IBO，简单着色器 + 可选 albedo 纹理；无贴图则灰棋盘**
- [ ] **Step 2: 放置位置 = 地图中心地面高度（`SampleHeight`）；替换旧预览**
- [ ] **Step 3: 无开图或未进 3D：自动 `Start3D()`；仍失败则报告提示**
- [ ] **Step 4: 手动冒烟**：放一个带贴图的小 FBX（或 accept 生成的立方体），确认视口可见；再导入替换；关图后预览消失、外部文件仍在。

---

### Task 6: 构建拷贝与全量回归

**Files:**
- Modify: `plugins/FbxImportPlugin/FbxImportPlugin.csproj`（`PostBuild` 拷到 W3Editor 输出 `plugins/`）
- Modify: 若有 `standalone/*.sln` 则加入新工程；否则文档写明构建顺序

- [ ] **Step 1: 配置插件 PostBuild → `$(SolutionDir)../W3Editor/bin/$(Configuration)/net8.0/plugins/`**（按实际输出路径校正）
- [ ] **Step 2: `dotnet build` W3Editor + 插件，零警告零错误**
- [ ] **Step 3: 回归**  
  - `--plugin-host-selftest`  
  - `--fbx-import-accept`  
  - `--d1map-accept`  
  - `--native-roundtrip`  
  - `--m2-accept`  
  Expected: 全部 `pass: true`
- [ ] **Step 4: 对照规格验收清单勾选**（两种菜单、两种落盘、缩放、结构 Warn/Error、预览替换）

---

## Spec Coverage Checklist

| 规格条目 | Task |
|---|---|
| 最小 DLL 宿主 / Contracts | 1–3 |
| 两条菜单（外部 / 打包） | 4 |
| 喷泉×10 缩放 + 制作者文案 | 4 |
| 结构材质检查 | 4 |
| 双落盘 + meta.json | 4 |
| 3D 预览单槽 / 关图清除 | 5 |
| 验收 CLI + 回归 | 1, 4, 6 |

## Self-review notes

- 无 TBD 步骤；喷泉常量允许「先写入测得值并注释」，但 Task 4 Step 1 必须产出具体数字。
- `PlacePreviewMesh` 在 Task 3 可暂空，Task 5 补齐；插件需容忍预览失败仍完成落盘。
- Contracts 程序集名固定为 `W3Editor.PluginContracts`（与规格一致）。
