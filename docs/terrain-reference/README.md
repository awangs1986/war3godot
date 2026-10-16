# 地形参考实现（教学用）

这是官方 War3 1.32.10 世界编辑器地形按钮的**可读版**参考实现。每个算法都用黑盒实验存档逐角点对照过；第五轮按经典 1.27 WorldEdit.exe 的 rea/Ghidra 反编译结果（`../classic-we-rea-comparison.md`）修正了整平（坡度钳制）、平滑（1-2-1 核）、形状、悬崖越界拒绝和传播写悬崖类型。
它不是游戏代码，不追求性能，也不会被主工程引用。

## 运行

```bash
dotnet run --project docs/terrain-reference
```

输出每个对照用例的匹配率和**按算法验证强度表**（1.27源码+1.32存档验证 / 仅1.27源码 / 仅1.32存档），写入 `bin/.../results.md`（已提交一份到本目录）。

## 目录

| 文件 | 内容 |
|---|---|
| `src/W3eMap.cs` | war3map.w3e 读取器 |
| `src/Footprint.cs` | 尺寸和形状 |
| `src/HeightPipeline.cs` | 高度流水线：计算 → 量化 → 坡度/高度钳制 → 实际改动区 |
| `src/HeightBrushes.cs` | 升高、降低、整平、噪声、平滑（只写“计算”这一步） |
| `src/CliffBrushes.cs` | 悬崖升降与传播、悬崖类型、浅水、深水、坡道 |
| `src/TextureBrushes.cs` | 贴图、荒芜、边界 |
| `src/Rendering.cs` | 高度插值、地表过渡、海岸 |
| `src/Program.cs` | 对照测试 |
| `cases.json` | 每个用例：前后存档、按钮、尺寸、点击角点 |
| `fixtures/*.w3e` | 从实验存档里抽出的 w3e（实验数据，不是游戏资源） |
| `guides/*.md` | 每个算法一篇导读 |

点击角点是从存档差分推出来的（实验时只知道屏幕坐标），坡道用的是写入那一列的中间点。

## 导读

[升高/降低](guides/raise-lower.md) · [整平](guides/plateau.md) · [噪声](guides/noise.md) · [平滑](guides/smooth.md) · [悬崖](guides/cliff.md) · [悬崖类型](guides/cliff-type.md) · [坡道](guides/ramp.md) · [浅水/深水](guides/water.md) · [贴图](guides/texture.md) · [荒芜](guides/blight.md) · [边界](guides/boundary.md) · [尺寸和形状](guides/footprint.md) · [高度插值](guides/height-interpolation.md) · [地表过渡](guides/ground-transition.md) · [海岸](guides/shore.md)

## 1.32 官方二进制实证文档

本目录另有一组**直接从 1.32.10.18820 编辑器二进制逆向**的文档，与本参考实现互为对照（参考实现 = 1.27 源码 + 1.32 存档对照；实证文档 = 1.32 内存捕获 + 反编译）：

| 文件 | 内容 |
|---|---|
| [`INDEX-1.32-capture.md`](INDEX-1.32-capture.md) | 总索引：功能 ↔ 函数地址 ↔ 常量；含与本文档的**差异复核点**与未决项 |
| [`1.32-brush-heights.md`](1.32-brush-heights.md) | 高度刷：升高/降低/整平/噪化/平滑 + 坡度后处理与 flood fill |
| [`1.32-brush-water-texture-cliff.md`](1.32-brush-water-texture-cliff.md) | 水 / 纹理 / 枯萎 / 边界 / 斜坡 / 悬崖 |
| [`1.32-map-stitching.md`](1.32-map-stitching.md) | 地图拼接：分块(4×4 tiles)、高度拼接、纹理接缝打包、悬崖面、水面 |
| [`1.32-dynamic-capture.md`](1.32-dynamic-capture.md) | 捕获方法与内存布局（加密 `.text` → 运行时解密） |
| `undead03_meta.json` + `img/` | 官方复杂地图 Undead03 的解码统计与可视化 |
