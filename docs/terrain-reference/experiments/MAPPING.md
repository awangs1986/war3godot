# 实现改动 × 证据 × 用例 映射表

> 任务书 §五 要求「实现改动与对应证据、用例的映射表」。本表逐条给出：
> **实现改动 → 依据的证据 → 保护它的测试/用例 → 当前状态**。
> 证据等级沿用 `VERIFICATION-SUMMARY.md`：**A** = 官方存档/真机实测；**B** = 反编译（附行号）；`未验证` = 缺证据。

---

## 1. 已实现的改动

| # | 实现改动 | 文件·位置 | 依据证据 | 等级 | 保护它的测试/用例 | 状态 |
|---|---|---|---|---|---|---|
| 1 | **撤回** 纹理笔刷清除 blight（`c.Blight = false` 移除） | `W3Editor.Core/Services/TerrainBrush.cs` `Texture()` | `D0/D3/D5/D6` 四份存档 `blight=0`；`D0→D3` 实际是 `(33,31) tex 0→3`、`groundVar`、`layer`、`cliffVar/Type` ⇒ 旧结论建立在错误解码上 | A（否定） | `P0-4` 自检已改回 `状态=未决`；`texture` 用例只比 `tex` | ✅ 已撤回 |
| 2 | **同一笔画内同一角点只施加一次**（`BrushStroke.AppliedCorners`） | `TerrainBrush.Apply` | 官方 R1/R6：1 次按下-抬起 = 1 次施加；按住不连发；同角点抖动不额外施加；拖动不按帧累积 | A（既有登记） | `S_hold`、`S_jitter`（各 1/1 通过）；`raise` 单点用例 1/1 | ✅ |
| 3 | **水刷不再随机改写悬崖变体**（`randomizeVariation: false`） | `TerrainBrush.Cliff/Propagate/Water` | 官方 `Y7_sh1`/`Y7_sh2` 在 (31,32) 逐字节相同（`00200020405111`）⇒ 水刷不动 byte5 | A | `Y7_sh2` 的 `nochange`（`cvar` 必须在集合内不变）；清空允许列表后仍 `exit=0` | ✅ |
| 4 | **`op:none` 必须真实调用笔刷**（`RunOp` + `apply` 字段） | `W3EditorOfficialTest/Program.cs` | 任务书 §三 要求；旧实现用"不执行"冒充"执行后无变化" | — | 缺 `apply` ⇒ `exit=1`；`Y7_sh2`/`Y10_ramp_again` 真实执行后 1/1 | ✅ |
| 5 | **`nochange`（不应变化字段）检查** + 登记机制 | `Program.cs` | 任务书 §五 要求「检查不应变化的字段」 | — | 破坏测试：浅水写 blight / 翻转 ramp ⇒ `exit=1` | ✅ |
| 6 | **登记项损坏干净失败**（不再崩溃） | `Program.cs` 加载段校验 | 任务书 §五「登记值被篡改」 | — | 篡改 `x` 为字符串 ⇒ `exit=1` + 明确消息 | ✅ |
| 7 | **UI Shift 接入坡道移除**（`_brush.Shift`，仅 `Ramp` 生效） | `W3Editor/MainWindow.axaml.cs` 按下/移动/结束 | 官方 R23：Shift+坡道 = 移除（算法级 `Y10_ramp_made→Y10_ramp_shift` 9/9 ✓） | B + 算法级 A | `Y10_ramp_shift` 9/9；**UI 端到端未验证** | ⚠ 部分 |
| 8 | **Shift 与直线笔刷冲突消解**（坡道下 Shift 不作直线约束） | 同上（2D + GL 两条路径） | 同上（Shift 语义被坡道占用） | B | 编译通过；**交互行为未验证** | ⚠ 部分 |
| 9 | **笔画结束复位 `_brush.Shift`** | 同上（4 处 `CommitStroke` 后） | 防止"移除模式"粘住 | — | 编译通过；**交互行为未验证** | ⚠ 部分 |
| 10 | **刷新范围 = 足迹 ∪ 实际改动范围** | `EditHistory.PendingAffectedRegion` + `MainWindow.ApplyBrushAt` | 官方改动角点数远超足迹：`cliff_c+1_s3`=50、`P_c+2_s1_x3`=21、`P_c+2_s3_x3`=157 相关（足迹≈13/1/13） | A（计数证据） | 编辑器 `build exit=0`；**画面一致性未验证** | ⚠ 部分 |
| 11 | **统一 W3E 解析器**（权威字段表 + 头部偏移计算） | `docs/terrain-reference/tools/w3e_diff.py` | 权威字段表；`W3Editor.Core/Formats/W3EMap.cs:222-233` v11 分支逐位一致 | — | `D*` 计数=0、`C0→C1`/`C1→C2` 各 1 点 boundary | ✅ |
| 12 | **`skip` 显式跳过**（输入无法建模的用例） | `Program.cs` + `cases.json` | 撤销/右键路径无实测证据 ⇒ 不得假装通过 | — | `Y10_ramp_right`、`X1_restore` 出现在报告里且不计入通过 | ✅ |

---

## 2. 用来"证明测试会失败"的破坏实验（任务书 §五 验收）

| 破坏 | 预期 | 实测 | 恢复后 |
|---|---|---|---|
| 浅水顺手 `Blight = true`（未登记） | 失败 | `exit=1`，`Y7_sh2: 字段 blight 在 (31,32) 被执行后改变（0 → 1）` | `exit=0` ✓ |
| 浅水顺手翻转 `Ramp`（未登记） | 失败 | `exit=1`，`字段 ramp … 0 → 1` | `exit=0` ✓ |
| 篡改登记项 `x` 为字符串 | 失败 | `exit=1`，`known-differences 条目损坏（case=cliff_repeat:P_c+2_s3_x3 field=L）… 登记值被篡改或字段类型错误` | `exit=0` ✓ |
| 错误输入（`Y7_sh2` 的 `apply` → `raise`） | 失败 | `exit=1`，`0 / 9`，9 处未登记差异 | `exit=0` ✓ |
| 缺失 `apply`（`op:none` 未声明真实笔刷） | 失败 | `exit=1`，四条例句均报「缺少 `apply`」 | 补 `apply`/`skip` 后 `exit=0` ✓ |

> 操作要求：破坏测试后必须 `dotnet build --no-incremental`，否则会复用旧 DLL 导致假"通过"。

---

## 3. 官方存档 → 用例 的映射（A 层回归）

| 存档 fixture | 对应的官方操作 | 用例 algo/op | 比对字段 | 结果 |
|---|---|---|---|---|
| `base → raise_s1/s2/s3/s5/s8_c_h08` | 升高一层（尺寸 1/2/3/5/8 圆形） | `raise / raise` | `h` | 1/1、1/1、9/9、45/45、145/145 ✓ |
| `base → cliff_c±1_s3`、`cliff_c+2_s3` | 升高/降低一、二层 | `cliff / cliff` | `L, ct` | 各 50/50 ✓ |
| `base → P_c+2_s1_x3` | 悬崖点 3 次（s1） | `cliff_repeat` | `L, ct` | 21/21 ✓ |
| `base → P_c+2_s3_x3` | 悬崖点 3 次（s3） | `cliff_repeat` | `L, ct` | **137/157**（58 登记差异：实现传播前缘少一环） |
| `Y7_sh1 → Y7_sh2` | 浅水再点 | `shallow`（`apply:["shallow"]`） | `L,water,w,h,ct` + `nochange` | 1/1 ✓ |
| `Y10_step_c2 → Y10_ramp_made` | 坡道单击 | `ramp` | `ramp` | **0/9**（坡道方向约定未验证） |
| `Y10_ramp_made → Y10_ramp_again` | 坡道再点 | `ramp`（`apply:["ramp"]`） | `ramp` + `nochange` | 1/1 ✓ |
| `Y10_ramp_made → Y10_ramp_shift` | Shift 点击移除 | `ramp_shift_remove` | `ramp` | **9/9** ✓ |
| `base → T_blight_s3`、`Y0_L_base → Y7_blight_s3` | 荒芜（尺寸 3） | `blight` | `blight` | 13/13、13/13 ✓ |
| `Y7_blight_tex1 → Y7_tex3` | 纹理覆盖 | `texture` | `tex` | 13/13 ✓ |
| `C0 → C1 → C2` | 边界放置 / 移除 | （由 `w3e_diff.py` 直接验证） | `boundary` | 各 **1** 点 ✓ |
| `D0 → D3 → D5 → D6` | （原以为是荒芜/纹理） | （由 `w3e_diff.py` 直接验证） | `blight` | 四份均 **0** ⇒ 旧结论撤回 ✓ |
| `H1 → J1`（round 8 真机） | **荒芜笔刷真实落笔** | 尚未入 `cases.json` | `blight` | **149 角点 0→1** ✓（并集，不能作单笔足迹） |

---

## 4. 未验证项与"缺什么"

| 项 | 缺的证据 | 取数路径 |
|---|---|---|
| 荒芜是否被纹理覆盖清除 | 「先画荒芜 → 确认 `byte4&0x20` → 同批角点覆盖纹理（同/异 × 普通/Shift）」四组，且需证明**纹理落笔集合 ∩ 荒芜集合 > 0** | 真机编辑器（当前受阻，见 §5） |
| 荒芜单笔足迹 | 单次点击的荒芜角点集合 | 同上（需先清空状态再单次点击） |
| 坡道方向约定 `(sx,sy)` | 坡道落笔后读回形状（3 共线 vs 3×3） | 真机（26 单击 + 3 拖拽未触发写盘） |
| `G1/G2` 过渡/插值的发生位置 | 渲染侧实现证据 | 反编译渲染模块（当前只到"未发现独立持久字段"） |
| 任务 6 UI 端到端 | Shift 点击后的存档差异 | 真机交互 |
| 任务 7 画面一致性 | 编辑后 vs 保存重开的截图比对 | 真机交互 |

---

## 5. 当前真机阻塞（实测数据）

| 观察 | 数据 |
|---|---|
| 调色板窗口 | hwnd `330048`，尺寸 501,226 → 757,956 |
| 子窗口总数 | 162（其中 Button 99） |
| **可见**按钮（矩形在窗口内） | **仅 16 个**，且恒为**悬崖组子模式**：荒芜 `3737526@689,322`、边界 `3541024@689,354`、去边界 `592218@689,386`、深水 `395644@578,523`、浅水 `330112@610,523`、斜坡 `526722@642,523`、平滑 `854408@519,625` |
| 纹理格句柄（round 7 探针记录的 `264760`=泥土 等） | 矩形 **`-9491,-9743,-9459,-9711`**（负坐标，属隐藏窗口）⇒ `BM_CLICK` 与坐标点击都无效 |
| 组下拉框 `330056`（5 项，rect `519,267,669,287`） | `CB_SETCURSEL + WM_COMMAND(CBN_SELCHANGE)` 无效；**真实点击 + 方向键选择**也无效；可见按钮集合恒为 16 |
| 结果 | 无法在自动化路径下选中"纹理"笔刷 ⇒ 任务 2 的四组覆盖实验暂不可做 |
