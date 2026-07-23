# FBX 导入插件（基础版）设计

日期：2026-07-23  
范围：W3Editor 插件宿主最小通路 + 示例 FBX 导入插件（结构检查、比例提醒、超限自动缩放、3D 视口预览）

## 已确认决策

| 项 | 选择 |
|---|---|
| 导入落点 | W3Editor 3D 视口（场景摆放预览） |
| 资源归属 | 两种都做：外部公共模型 / 打包进当前地图 |
| 插件深度 | 最小宿主：扫 `plugins/*.dll`，菜单命令；校验与缩放都在示例插件内 |
| 材质检查 | 结构检查（槽位 / 分辨率 / 命名），不做像素内容分析 |
| 技术方案 | Assimp.NET 读 FBX；视口用简化 mesh 预览（不转 MDX） |

## §1 插件宿主（最小）

### 目标

编辑器能在不改主工程业务代码的情况下，通过外部 DLL 扩展菜单命令。本次只打通「加载 → 注册命令 → 调用」；面板、资源钩子、设置页等完整契约留待后续。

### 布局

```
standalone/
  W3Editor.PluginContracts/     # 零 UI 依赖的契约程序集（插件只引用它）
  W3Editor/                       # 宿主：扫描、加载、菜单注入、上下文实现
plugins/
  FbxImportPlugin/                # 示例插件工程
    → 构建输出到 W3Editor 旁的 plugins/FbxImportPlugin.dll
```

### 契约

```csharp
public interface IW3EditorPlugin
{
    string Id { get; }       // 如 "com.d1.fbx-import"
    string Name { get; }     // 显示名
    string Version { get; }
    void Register(IEditorPluginContext ctx);
}

public interface IEditorPluginContext
{
    void AddMenuCommand(string menuPath, string id, Func<CancellationToken, Task> handler);
    Task<string?> PickOpenFileAsync(string title, params string[] filters);
    bool HasOpenMap { get; }
    string? CurrentMapFolder { get; }          // 地图目录；非文件夹地图则为 null
    string SharedModelsRoot { get; }           // 外部公共模型根目录
    // 将预览网格放入当前 3D 场景中心（或相机前方地面）
    void PlacePreviewMesh(PluginMeshPreview mesh, string label);
    void ShowReport(string title, PluginValidationReport report);
}
```

- 宿主启动时扫描 `{AppDir}/plugins/**/*.dll`（跳过非托管与契约程序集自身）。
- 对每个 DLL：`AssemblyLoadContext` 隔离加载；查找 `IW3EditorPlugin` 实现；调用 `Register`。
- 单个插件失败：记日志 + 状态栏提示，不阻断编辑器。
- 菜单路径约定：`工具(_T)/插件/...`；示例插件注册两条命令：
  - `工具(_T)/插件/导入 FBX（外部模型）…`
  - `工具(_T)/插件/导入 FBX（打包进地图）…`

### 非目标（本版不做）

- 插件热重载、依赖解析（NuGet）、沙箱权限、设置持久化、多语言。

## §2 导入 / 缩放 / 材质检查

### 两种资源归属（都做）

对应魔兽的两类用法：

| 模式 | 含义 | 落盘位置 | 地图如何引用 |
|---|---|---|---|
| **外部模型**（公共引用） | 共享资源库里的候选模型，多图可指同一份 | `{SharedModelsRoot}/<modelId>/`（mesh 快照 + 贴图副本 + `meta.json`） | 地图只记相对/逻辑 id（本版先预览；正式 catalog 挂接留后续） |
| **打包进地图**（地图内嵌） | 资源跟着当前地图走 | `{CurrentMapFolder}/imported/<modelId>/` | 地图私有；换图不带走 |

共用同一条校验/缩放管线；差别只在「写到哪、报告里怎么标注归属」。

约束：

- **外部模型**：不要求已打开地图也可导入到共享库；但若要进 3D 预览，仍需已开图 + 3D 视口。
- **打包进地图**：必须已打开**文件夹地图**（d1map 或解压 War3 目录）。`.w3x` 单文件未解压时拒绝并提示先另存为文件夹/d1map。
- `modelId`：默认取 FBX 文件名（去扩展名、净化非法路径字符）；冲突时追加短哈希。
- 贴图：把检查时解析到的存在文件拷进目标目录；缺失贴图只报错不阻断灰模预览。
- 本版**不**改 `placements.bin` / doodad 列表；两种模式都先「落盘 + 视口预览」。正式摆放引用下一步再接。

### 流程

1. 用户选菜单（外部 / 打包进地图）→ 选 `.fbx`（可选同目录贴图）。
2. Assimp 加载场景：网格、材质槽、贴图路径。
3. 计算世界空间 AABB（合并所有 mesh，应用节点变换）。
4. 与参考上限比较：
   - **参考体**：Lost Temple 喷泉（`City_Fountain` / 地图中心喷泉道具）在编辑器单位下的包围盒最长边 `F`。
   - **上限**：`MaxEdge = 10 * F`。
   - 若导入模型最长边 `L > MaxEdge`，统一乘以 `MaxEdge / L`，并在报告中写明原始尺寸、缩放系数、建议制作者按喷泉 1× 为基准建模。
5. 结构检查（见下）→ 生成 `PluginValidationReport`（含归属模式与目标路径）。
6. 按模式写入目标目录（mesh 中间快照 + 贴图 + `meta.json`：源路径、缩放、检查摘要、归属）。
7. 缩放过的网格交给宿主 `PlacePreviewMesh`；报告对话框列出通过项 / 警告 / 错误。

### 喷泉参考尺寸

- 基础版把 `F` 固化为常量（首次实现时从 Lost Temple 喷泉 MDX/摆放实测写入，单位与编辑器世界一致：W3 世界单位，`TileSize = 128`）。
- 常量放在示例插件内：`FountainReference.MaxEdgeWorldUnits = 10 * F`，并附注释说明来源与测量方法。
- 后续可改为读目录里的参考模型；本版不依赖运行时再量一次。

### 制作者比例提示（报告固定文案）

- 推荐：道具以 Lost Temple 喷泉为 1× 视觉锚点；大型建筑/地标不超过喷泉的 10×。
- 坐标：+X 东、+Y 北、+Z 上（与编辑器 / d1map 一致）；导入时若 Assimp 轴约定不同，插件负责转到该空间。
- 贴图：面向未来自有规范；本版检查规则对齐魔兽 HD / 项目 `ASSET_PACK_SPEC` 的 PBR 三件套约定。

### 结构检查规则（本版）

对每个材质 / 贴图引用：

| 检查 | 级别 | 规则 |
|---|---|---|
| 有 albedo/diffuse 槽或贴图 | Error（无可渲染色）/ Warn | 至少一张颜色贴图或顶点色 |
| normal 槽 | Warn | 建议提供 |
| ORM / metallic-roughness 槽 | Warn | 建议提供（可拆成 metal/rough） |
| 分辨率 | Warn | 宽高为 512 或 1024；否则提示「建议 512/1024」 |
| 幂次方 | Warn | 非 2 的幂则提示 |
| 命名 | Info | 若文件名含 `_albedo`/`_diffuse`、`_normal`、`_orm`/`_mrao` 记为符合约定；否则提示推荐命名 |
| 路径缺失 | Error | 材质引用了磁盘上不存在的贴图 |

不做：法线内容校验、ORM 通道直方图、UV 重叠分析、骨骼/动画检查。

### 报告结构

```text
标题: FBX 导入结果 — <文件名>
归属: 外部模型 | 打包进地图
目标: <写出目录>
摘要: 通过 / 有警告 / 有错误；是否自动缩放（系数）
尺寸: 原始 AABB · 缩放后 AABB · 上限(喷泉×10)
比例指引: （固定制作者说明）
明细: [Error|Warn|Info] 材质名 · 规则 · 说明
```

## §3 视口预览与验收

### 视口预览

- 需要已打开地图且 3D 视口可用；否则提示先开图并打开 3D。
- 预览对象：
  - 合并后的静态三角网格（位置/法线/UV0）；
  - 有 albedo 则试绑第一张；失败则灰棋盘占位；
  - 放在地图中心或当前相机注视点的地面高度上；
  - 不写入 `placements.bin` / doodad 列表（本版是落盘素材 + 预览，不是正式摆放）。
- 同一会话再次导入：替换上一次插件预览实例（只保留一个「插件预览」槽），避免堆叠。
- 关闭地图或切换地图时清除预览；**磁盘上的外部库文件保留**；地图内嵌文件随地图目录保留。

### 失败与边界

| 情况 | 行为 |
|---|---|
| 非 FBX / Assimp 打开失败 | 报告 Error，不落盘、不放置 |
| 空网格 | 报告 Error，不落盘、不放置 |
| 打包模式但无文件夹地图 | 报告 Error，提示另存为文件夹/d1map |
| 仅警告 | 仍落盘 + 放置 + 自动缩放（若需要） |
| 有 Error（如贴图全缺且无法渲染） | 仍可落盘灰模并预览，报告标失败摘要 |
| 插件 DLL 缺失 Assimp 原生库 | 加载插件时失败并提示 |

### 验收清单

- [ ] 无 `plugins` 目录时编辑器正常启动。
- [ ] 放入示例插件 DLL 后，菜单出现两条导入命令（外部 / 打包进地图）。
- [ ] 外部模式：写出到共享模型根目录，含 `meta.json`；开图时视口可预览。
- [ ] 打包模式：写出到当前地图 `imported/<id>/`；关编辑器后文件仍在地图目录。
- [ ] 导入小于上限的 FBX：视口可见模型，报告无自动缩放。
- [ ] 导入大于喷泉×10 的 FBX：自动缩小到上限，报告写明系数与制作者比例说明。
- [ ] 缺 normal/ORM 或非 512/1024：报告 Warn；缺关键贴图路径：Error。
- [ ] 再次导入替换旧预览；关图清除预览，外部库文件仍在。
- [ ] Release 构建 W3Editor + 插件无警告无错误。

### 后续（明确不在本版）

- 把外部模型挂进 `IObjectCatalog` / 把地图内嵌编入摆放列表；转 glTF/MDX；完整插件 API；热重载；像素级材质校验。
