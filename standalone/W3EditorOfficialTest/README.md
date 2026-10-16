# W3EditorOfficialTest —— TerrainBrush 官方算法无头验证

用**官方 1.32 存档 fixture**（`docs/terrain-reference/fixtures/*.w3e`，由 1.32 官方编辑器刷出）
逐角点验证 `W3Editor.Core/Services/TerrainBrush.cs`（官方 1:1 版）。

## 运行

```bash
dotnet run --project standalone/W3EditorOfficialTest [-- <terrain-reference 目录>]
# 默认目录：仓库内 docs/terrain-reference（PR#2 分支；main 上若无则传路径）
```

退出码 0 = 全部通过（45 用例与参考实现 results.md 逐用例一致 + 边缘 sanity 全过）。

## 验证方式（与参考实现完全同构）

1. 每个用例：`A=before.w3e`（官方存档），`B=after.w3e`（官方刷完的存档）；
   `P = W3EMap.LoadBytes(A.SaveBytes())`（v11 往返字节精确 → 干净副本）。
2. 用**用户的 `TerrainBrush.Apply`** 在 P 上施加同一次笔刷（单击语义；`cliff_repeat` 按 cases.json 的
   clicks 逐次施加；噪声单次施加用固定种子，分布另跑 200 种子统计）。
3. 逐角点比较 P vs B（按算法选字段：h/L/ct/w/water/tex/ramp/blight/bound；噪声比较
   范围+量化对双方各自成立 + k 分布的总变差距离）。
4. 与参考实现 `results.md` 的逐用例数字逐一对照——**必须完全相同**
   （参考实现本身已在同一组 fixture 上验证过，见 `docs/terrain-reference/results.md`）。

## 2026-10-15 验证结果

- **45/45 用例与参考实现完全一致**（逐角点）：
  raise 201/201、lower 18/18、plateau 93/93、smooth 111/111、noise 601/601、cliff 150/150、
  cliff_repeat 158/178（剩余 20 字段为已知未解释项，与参考实现相同）、cliff_type 148/148、
  shallow 211/211、deep 211/211、water_on_water 21/21、footprint 223/223、ramp 19/19、
  texture 51/51、blight 13/13、boundary 13/13。
- **边缘 sanity 10/10 过**：悬崖 T 越界整笔拒绝（字节全等）、L=0 放浅水（水旗生效、悬崖步被拒、
  水面=点击点地面总高）、坡道层差 2 无操作、尺寸 1 升高只改中心 +25 raw、方形整平在地图角不越界、
  升高强制圆形（Square 设置下 d=√5 点不动）。
- 验证中抓到 1 个真实 bug：`Water()` 传给整平步的 h0 缺 ×128（层分数当世界单位用），
  平地（h=0）时隐蔽、非零高度放水上整片 49 角点错——已修复（`TerrainBrush.cs` 的 `Water` 用
  `WorldOf(c)` 取 h0）。

## 与 fixture 覆盖的边界

- fixture 未覆盖的语义（拒绝/无操作/边界）由本文"边缘 sanity"补足，期望值来自
  1.27 源码 + 1.32 存档验证的公式（`docs/official-terrain-algorithms.md` §8/§12.15）。
- 噪声为随机算法：官方存档本身只验证"范围+量化"，分布对齐用 200 种子 TV ≤ 均匀抽样量级判定。
