# W3EditorOfficialTest —— 官方存档逐坐标逐字段验证

用官方 1.32 编辑器存出的 `.w3e` fixture 驱动 `W3Editor.Core` 的 `TerrainBrush`，**按坐标逐字段**给出差异。

## 运行

```bash
# 默认从仓库内找 docs/terrain-reference（含 cases.json / fixtures / results.md / known-differences.json）
dotnet run

# 指定目录 + 差异明细条数上限
dotnet run -- docs/terrain-reference --diff-limit 200
```

> 依赖 `docs/terrain-reference/` 下的 `cases.json`、`fixtures/*.w3e`、`results.md`、`known-differences.json`。
> 若 `docs/terrain-reference/` 不存在（PR 叠加顺序未到位），可用第二个参数显式指向含这些文件的目录。

## 三层证据（分别统计，**不合并成单一还原率**）

| 层 | 含义 | 能否判"对" |
|---|---|---|
| **A 官方存档已验证** | 与官方 `.w3e` 逐坐标逐字段对照 | ✅ 唯一的判据 |
| **B 二进制分析支持** | 1.32 反编译得到的常量/行为断言（附函数地址） | ❌ 只是支持性证据 |
| **C 实现自检** | fixture 未覆盖的语义 + 待实验确认的行为快照 | ❌ 快照只记录现状 |

输出小节：

- `A.1` 按算法汇总（仅本层）
- `A.2` **差异明细**：`[用例] (x,y) 字段: 官方=… 用户=…`（逐点，最多 `--diff-limit` 条）
- `A.3` **已登记差异**：逐点列出、状态 `未决`
- `A.4` 登记项提醒（本次未复现）
- `A.5` **噪声**：覆盖区内改动 / 覆盖区内越界 / **覆盖区外（坡度后处理外扩，单独列出，不计噪声越界）** + Δraw 范围 + 量化合格率
- `A.6` 与参考实现 `results.md` 的逐用例对照（严格）
- `A.7` 实验元数据缺口（协议要求字段，不判失败）
- `B` / `B.1` / `B.2` / `B.3` 二进制分析支持断言（一致 / 不一致 / **已登记不一致** / 已核实说明 / 未决）
- `C.1` 语义自检 · `C.2` 待实验确认的行为快照（同点连点、坡道三邻域、纹理覆盖荒芜、水面叠加、层级极限）

## 退出码

- `0`：A 层无未登记差异、无硬失败，且 C 层语义自检全过、B 层无不一致
- `1`：任何硬失败（见 `docs/terrain-reference/experiments/CASE-FORMAT.md` §4）、语义自检失败、或 B 层出现"不一致"

**已登记的未决差异不会让运行失败**，但会在 `A.3` 逐点列出——它们不能被当作通过。

## 未决差异登记

`docs/terrain-reference/known-differences.json`

```bash
# 新增一处未决差异（必须带原因，禁止当通过）
# 例：cliff_repeat 的 20 点（4 个 L + 16 个 ct）
```

- 未登记差异 → 失败（强制"要么修、要么登记并说明原因"）
- 登记项状态：`未决` / `已验证` / `实现缺陷`
- 文件缺失 → 直接失败

## 与实验计划的关系

采集规范、P0/P1 实验清单、"复杂地形方块问题"专项小地图见：

- `docs/terrain-reference/experiments/PLAN.md`（实验清单与优先级）
- `docs/terrain-reference/experiments/CASE-FORMAT.md`（用例字段与失败条件）
- `docs/terrain-reference/experiments/FINDINGS.md`（**已做的验证结论总账**：REA 证据、官方存档反推的悬崖规则、20 点定论、仍未验证项）
