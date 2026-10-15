# 地形参考实现（教学用）

这是官方 War3 1.32.10 世界编辑器地形按钮的**可读版**参考实现。每个算法都用黑盒实验存档逐角点对照过。
它不是游戏代码，不追求性能，也不会被主工程引用。

## 运行

```bash
dotnet run --project docs/terrain-reference
```

输出每个对照用例的匹配率，并写入 `results.md`（已提交一份）。

## 目录

| 文件 | 内容 |
|---|---|
| `src/W3eMap.cs` | war3map.w3e 读取器 |
| `src/Footprint.cs` | 尺寸和形状 |
| `src/HeightBrushes.cs` | 升高、降低、整平、噪声、平滑 |
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
