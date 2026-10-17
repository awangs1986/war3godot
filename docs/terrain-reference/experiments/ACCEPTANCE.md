# PR #6 修正任务书 · 验收报告

> 复现环境：Windows 11 26H1 · .NET SDK 8.0.423 · Python 3.12（`mpyq 0.2.5`）
> 仓库路径：`PR #6` 分支 `test/terrain-round13`
> 工具：`docs/terrain-reference/tools/w3e_diff.py`（唯一 W3E 解析/差分入口）
> 修正依据与撤回记录：`experiments/CORRECTIONS.md`

---

## A. 任务 1 · 统一 W3E 解析

**实现要点**
- 头部偏移**按 `version + 地面tileset列表长度 + 悬崖tileset列表长度` 计算**，不写死 69
  （`w3e_diff.py:parse_header`；当前 65×65 v11 用例算得 `off=69` ✓）
- 字段解码严格按权威表（`w3e_diff.py:Corner`）：
  `byte4` 低 4 位纹理 / `0x10` 坡道 / `0x20` 荒芜 / `0x40` 水 / `0x80` 边界；
  `byte5` 低 5 位地表变体 / 高 3 位悬崖变体；`byte6` 低 4 位层级 / 高 4 位悬崖类型
- 输出统一为 `坐标 + 字段名 + 前值 + 后值`；`--count` 输出标志置位数
- 文件字段 与 **编辑器进程内存字段** 的区分写在工具文件头注释里
  （内存记录 0x1c 字节、布局不同，见 `experiments/dynamic-memory-address-map.md`）

**验收 1：`D0/D3/D5/D6` 的荒芜计数必须为 0**

```
$ python tools/w3e_diff.py --count fixtures/D0.w3e fixtures/D3.w3e fixtures/D5.w3e fixtures/D6.w3e
D0.w3e: 65x65 v11 off=69  ramp=0, blight=0, water=0, boundary=0
D3.w3e: 65x65 v11 off=69  ramp=0, blight=0, water=0, boundary=0
D5.w3e: 65x65 v11 off=69  ramp=0, blight=0, water=0, boundary=0
D6.w3e: 65x65 v11 off=69  ramp=0, blight=0, water=0, boundary=0
```

⇒ **通过** ✓（四份全 0，与任务书一致）

**验收 2：`C0→C1` 的 boundary 变化必须为 1 点**

```
$ python tools/w3e_diff.py fixtures/C0.w3e fixtures/C1.w3e --only=boundary
# 差异条目 1（每条 = 一个角点的一个字段）
( 33, 30) boundary       False → True
```

⇒ **通过** ✓

**验收 3：`C1→C2` 的 boundary 变化必须为 1 点**

```
$ python tools/w3e_diff.py fixtures/C1.w3e fixtures/C2.w3e --only=boundary
# 差异条目 1（每条 = 一个角点的一个字段）
( 33, 30) boundary       True → False
```

⇒ **通过** ✓（同一角点，放置/移除对称）

---

## B. 任务 2 · 结论修正

| 项 | 处理 | 证据 |
|---|---|---|
| G8「荒芜位已定位 / 纹理清荒芜已定案」 | **撤回** ⇒ 状态 `未验证` | `D*` 四份存档 `blight=0`；`D0→D3` 实际是 `(33,31) tex 0→3`、`groundVar`、`layer`、`cliffVar/Type` |
| 实现里据此加的 `c.Blight = false` | **已移除** | `TerrainBrush.Texture()` 注释改为「是否清除 blight：未验证」 |
| `P0-4 纹理覆盖荒芜` 自检 | 由「A 级已定案」改回 **`状态=未决`** | `Program.cs` |
| G5 边界统计 | **修正**：混入的 `layer/cliffType/cliffVar` 差异剔除 ⇒ 放置 1 点、移除 1 点、**对称** | 验收 2 / 验收 3 |
| G1/G2 | 改为「**未发现独立的持久字段**」，发生位置与规则 `未验证`（等级由 A 降为推断） | `CORRECTIONS.md` §3 |
| 速览 / 矩阵 / 源码注释 | 已同步（速览顶部有作废横幅指向 `CORRECTIONS.md`） | `VERIFICATION-SUMMARY.md` |

**新增的真机证据（round 8）**：荒芜笔刷确实会写 `byte4 & 0x20`

```
# 同一实例、同一协议（保存(id=4) + 每步复制+SHA256）
H1 → J1:  blight  False → True   × 149 个角点
J1 → J2:  0 条差异（同点再画一次 = 无操作）
```

> 注意：149 是**多次点击的并集**，不可当作单笔足迹；单笔足迹仍未验证。

---

## C. 任务 3 · `op:none` 必须真实调用

- `Program.cs` 的笔刷分派收敛为本地函数 `RunOp(string)`
- `op == "none"` 时**必须**从用例 `apply` 读出真实笔刷序列并逐个执行；缺 `apply` ⇒ `HardFail` ⇒ 非零退出
- 输入无法建模的用例改为**显式 `skip`**（带理由，不计入通过）：

| 用例 | 处理 |
|---|---|
| `Y7_sh1 → Y7_sh2` | `apply: ["shallow"]`（真实调用浅水） |
| `Y10_ramp_made → Y10_ramp_again` | `apply: ["ramp"]`（真实调用坡道） |
| `Y10_ramp_made → Y10_ramp_right` | `skip`：右键映射未实现、官方右键无实测 |
| `X1_base → X1_restore` | `skip`：撤销路径未建模 |

**运行结果**

```
| shallow | Y7_sh1 → Y7_sh2            | L,water,w,h,ct | 1 / 1 | 100.0% |
| ramp    | Y10_ramp_made → Y10_ramp_again | ramp       | 1 / 1 | 100.0% |
```

**验收：故意让这些操作改错字段 ⇒ 必须非零退出**

```
$ # 把 Y7_sh2 的真实输入改成 raise
- | shallow | Y7_sh1 → Y7_sh2 | … | 0 / 9 | 0.0% |
- [shallow:Y7_sh2] (30,31) h: 官方=8192 用户=8226
exit = 1  ✓
```

---

## D. 任务 4 · 笔画事件重放

**实现侧修复**：`BrushStroke.AppliedCorners`；`Apply` 在施加前
```csharp
if (!stroke.AppliedCorners.Add(ccy * map.Width + ccx)) return;   // 同一笔画内同一角点只施加一次
```
依据：官方实测 R1/R6（1 次按下-抬起 = 1 次施加；按住不连发；同角点抖动不额外施加；拖动不按帧累积）。

**测试侧输入**：用例字段 `path`（相对移动序列，浮点）+ `stroke`（`same` / `new`）。

**验收用例（官方字节直采，`no_reference: true`）**

| 用例 | 输入 | 期望 | 结果 |
|---|---|---|---|
| `S_hold` | 同坐标移动 5 次、同一笔画 | 与单击完全相同 | **1/1 通过** ✓ |
| `S_jitter` | 同角点抖动 5 次（±0.4 格）、同一笔画 | 与单击完全相同 | **1/1 通过** ✓ |

未覆盖：松开后再次点击的线性累加（缺两次点击的官方 fixture）；跨角点拖动后返回原角点（无官方输入记录 ⇒ `未决`）。

---

## E. 任务 5 · 不一致即失败

- 新增 `nochange`（"不应变化的字段"）检查：**未登记**副作用 ⇒ `HardFail`；已登记项（`known-differences.json.nochange`）⇒ 打印不判失败
- 登记项损坏（被篡改/类型错误）⇒ **干净失败**（不再是未捕获异常崩溃）
- 已登记差异仍逐点显示（58 点），并区分「无新增回归」与「全部行为正确」

**验收：四项故意破坏都必须失败**

| 破坏 | 结果 |
|---|---|
| 浅水顺手 `Blight = true`（未登记） | **exit=1** ✓ `Y7_sh2: 字段 blight 在 (31,32) 被执行后改变（0 → 1）` |
| 浅水顺手翻转 `Ramp`（未登记） | **exit=1** ✓ `字段 ramp 在 (31,32) 被执行后改变（0 → 1）` |
| 篡改一条登记的 `x` 为字符串 | **exit=1** ✓ `known-differences 条目损坏（case=cliff_repeat:P_c+2_s3_x3 field=L）… 登记值被篡改或字段类型错误` |
| 错误输入（`Y7_sh2` 的 `apply` 改成 `raise`） | **exit=1** ✓（9 处未登记差异） |
| 恢复后 | **exit=0** ✓（45/45、58 登记、0 未登记） |

> 踩坑记录：**增量构建会复用旧 DLL**，破坏测试后必须 `dotnet build --no-incremental`。

**该检查当场发现并已修复的真实缺陷**：水刷复用 `Cliff(...)` 时会 `CliffVariation = Rng.Next(4)` ⇒ 随机改写 byte5 悬崖变体 ✗。
修复：`Cliff`/`Propagate` 增加 `randomizeVariation`，`Water` 传 `false`；清空 `nochange` 登记后重跑仍 `exit=0` ✓（证明不是被允许列表掩盖）。

---

## F. 任务 6 · UI Shift / 右键

**代码接线**
1. 按下时：`_brush.Shift = shiftDown && _brush.Op == BrushOp.Ramp;`（只对坡道生效）
2. 冲突消解：坡道笔刷下 Shift **不再**作直线约束（官方 R23：Shift+坡道 = 移除）；2D 与 GL 两条路径都改
3. 笔画结束：4 处 `CommitStroke` 之后 `_brush.Shift = false;`

**编译验证**：`cd standalone/W3Editor && dotnet build -v q` → **exit=0** ✓
**算法级证据**：`Y10_ramp_made → Y10_ramp_shift` = **9/9 100%** ✓
**UI 端到端：未验证** ✗（需交互式：普通点击建立 ramp → 保存取证集合 A；Shift 同点点击 → 保存取证集合 B；断言 B 中 ramp 位 1→0 且其它字段不变；右键同点断言 0 变化）

---

## G. 任务 7 · 按实际改动范围刷新

**证据（传播远超足迹）**

| 用例 | 官方改动角点 | 足迹 |
|---|---|---|
| `base → cliff_c+1_s3` | **50** | ≈13 |
| `base → P_c+2_s1_x3` | **21** | 1 |
| `base → P_c+2_s3_x3` | **157 相关 / 137 已实现对** | ≈13 |

**修复**
- `EditHistory.PendingAffectedRegion(map)`：与 `BeginStroke` 快照差分得到**笔画进行中**的实际改动范围（含传播/坡度写入）
- `ApplyBrushAt`：刷新区域 = 足迹 ∪ 实际改动范围
- 撤销记录本就基于整图快照差分 ⇒ 「撤销覆盖实际变化」原本已满足 ✓

**验证**：编辑器 `build exit=0` ✓；回归 `exit=0` ✓
**画面一致性：未验证** ✗（需交互式比对"编辑后"与"保存重开"的画面）

---

## H. 总回归基线（每次提交前必跑）

```
$ cd standalone/W3EditorOfficialTest
$ dotnet build --no-incremental -v q          → exit=0
$ dotnet run --no-build
A 官方存档已验证：用例 65，逐用例与参考一致 45/45，已登记差异 58 点，未登记差异 0 点
B 二进制分析支持：断言 10 条 —— 一致 8、不一致 0、已登记不一致 0、未决 0
C 实现自检：语义自检 0 项失败；行为快照仅记录现状，不作为通过依据
exit=0
```

---

## I. 仍未完成（缺证据，不得升级为结论）

| 项 | 状态 | 缺什么 |
|---|---|---|
| G8 荒芜是否被纹理覆盖清除 | `未验证` | 需「先画荒芜并确认 `byte4&0x20`，再在同批角点覆盖纹理（同/异 × 普通/Shift）」，且须证明**纹理落笔集合 ∩ 荒芜集合 > 0** |
| 荒芜单笔足迹 | `未验证` | 现有 149 点是多次点击并集 |
| 坡道方向约定 `(sx,sy)` | `未验证` | 未取得可信存档对照 |
| 任务 6 UI 端到端 | `未验证` | 交互式取证 |
| 任务 7 画面一致性 | `未验证` | 交互式截图比对 |

**当前真机阻塞**：调色板当前仅 16 个可见控件，纹理格句柄（`264760`=泥土 等）的矩形是**负坐标**（属隐藏窗口），`BM_CLICK` 与坐标点击均无效 ✗；
下一步需逐组（下拉框 `330056` idx 0..4）找出"点击纹理格后 `应用纹理:` 标签确实变化"的那一组，再执行四组覆盖实验。
