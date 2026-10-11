# war3godot

**本项目必须使用 Warcraft III: Reforged 1.32.10.18820（补丁 1.32.10，构建 18820）的游戏资源。**

> **警告：不得随意更换资源版本，也不得混用不同版本的资源。** 其他版本尚未验证，贴图图集、模型或数据表的差异可能导致地形方块、悬崖/坡道接缝和资源加载错误。升级资源版本前必须完成兼容性验证，并同步更新项目的资源基线说明。

基于 **Godot 与 C#** 的 Warcraft III 地形渲染、地图编辑和模型动画工具集，为下一款游戏提供世界制作基础。具体玩法尚未确定；当前重点是地形编辑与运行时的一致性，以及材质、光照和 24 小时日夜变化。

仓库：[awangs1986/war3godot](https://github.com/awangs1986/war3godot) · [问题与需求](https://github.com/awangs1986/war3godot/issues)

## 核心能力

- **War3 地形**：角点高度、悬崖层高、地表纹理过渡、悬崖、坡道和水面。
- **地图编辑**：独立 W3Editor 与 Godot 地形工具，支持笔刷编辑、摆放、撤销重做及地图保存；共享 C# 地形核心。
- **模型与动画**：加载 MDX 模型、材质和骨骼动画，在 Godot 中使用 `Skeleton3D + Skin` 蒙皮。
- **材质与光照**：地面、悬崖、水和模型接入 Godot 场景受光；支持 Reforged 模型的 PBR 材质数据。
- **24 小时日夜变化**：Sky3D 驱动太阳、月亮和环境；自动时间推进需启用 `sky3d_time_progress`。统一游戏时钟、地图时段保存与编辑器时间预览仍待完善。
- **地图数据**：支持 War3 地图导入及项目自有地图格式；JASS 脚本不执行。

独立编辑器的 OpenGL 预览采用简化光照；Godot 是完整材质与场景光照的运行环境。复杂地形的视觉验收仍在进行。

## 环境与启动

使用 **Godot 4.7 .NET** 和 **.NET 8 SDK**；CI 对应 Godot 4.7.1 .NET。

```bash
git clone https://github.com/awangs1986/war3godot.git
cd war3godot
```

准备下述资源后，在仓库根目录启动 Godot：

```bash
godot --path .
```

默认场景为 `scenes/w3_lost_temple_preview.tscn`。也可以直接打开沙盒：

```bash
godot --path . res://scenes/w3_sandbox.tscn
```

启动独立地图编辑器：

```bash
dotnet run --project standalone/W3Editor/W3Editor.csproj -c Release
```

仓库保留早期城镇/地牢 greybox，作为玩法实验内容。

## Warcraft III 资源要求

资源基线为 **Warcraft III: Reforged 1.32.10.18820**（补丁 **1.32.10**，构建 **18820**）。2026-10-10 已核对安装记录、游戏程序与地图编辑器，三者版本一致。

自行准备游戏资源，并保留游戏内的相对目录结构：

| 目录 | 内容 |
| --- | --- |
| `war3/game/` | 导出的游戏数据，包括经典/SD 地形图集、SLK、悬崖与坡道模型及贴图 |
| `war3/reforged/` | Reforged HD 模型/材质路径所需的资源缓存 |
| `war3/maps/` | 本地地图与导入数据 |

地表纹理过渡仍需经典/SD 图集；纯 HD 导出不能替代这部分资源。资源文件不随仓库分发。

详细版本证据、目录和提取说明见 [资源指南](war3/README.md)。

机器路径请配置在 `war3/local_paths.json`（已被 Git 忽略）；可从
`war3/local_paths.example.json` 复制。未提供本地文件时使用模板中的相对路径。
CASC 提取工具必须通过 `--install` 显式指定上述版本的安装目录。

## 项目结构

| 路径 | 职责 |
| --- | --- |
| `standalone/W3TerrainCore/` | 共享地形数据、构建、坡道与悬崖规则、高度查询 |
| `standalone/W3Editor/` | 独立地图编辑器 |
| `standalone/W3Editor.Core/` | 编辑器资源、地图与编辑服务 |
| `addons/w3kit_cs/` | Godot 地形、MDX、材质和运行时适配 |
| `addons/w3_foundation/` | Godot 地形编辑插件 |
| `addons/sky_3d/` | 天空与日夜变化 |
| `scenes/`、`runtime/` | 预览场景和玩法实验 |

C# 工程名 `Diablo1Like.csproj`、程序集与命名空间沿用既有标识，项目展示名与 GitHub 仓库名统一为 `war3godot`。

## 开发检查入口

以下为已有检查命令；资源相关检查需要先配置本地资源。

```bash
dotnet run --project standalone/W3Editor/W3Editor.csproj -c Release -- --ramp-brush-smoke
dotnet run --project standalone/W3Editor/W3Editor.csproj -c Release -- --terrain-render-smoke
godot --headless --path . -s res://tools/check_asset_root.gd
godot --headless --path . res://tools/w3_ground_crosscheck.tscn -- --asset-free
godot --headless --path . res://tools/w3_regression.tscn
```

## 设计参考

本项目的地形渲染与地图编辑设计参考了 [HiveWE](https://github.com/stijnherfst/HiveWE) 和 [Warsmash](https://github.com/Retera/WarsmashModEngine)。

## 文档

- [资源版本与目录要求](war3/README.md)
- [世界制作的职责划分](CONTEXT.md)
