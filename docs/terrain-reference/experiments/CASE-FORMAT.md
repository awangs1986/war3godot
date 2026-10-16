# 用例格式规范（`cases.json` / 采集目录 / 差异表）

本文件规定"官方实验结果"如何落盘，以及 harness 能接受什么、缺什么会**直接判失败**。

---

## 1. `cases.json` 用例字段

### 1.1 必需字段（缺一 → harness 直接失败，退出码 1）

| 字段 | 类型 | 说明 |
|---|---|---|
| `algo` | string | 算法名（`raise`/`lower`/`plateau`/`smooth`/`noise`/`cliff`/`cliff_repeat`/`cliff_type`/`ramp`/`shallow`/`deep`/`water_on_water`/`texture`/`blight`/`boundary`/`footprint`） |
| `op` | string | 操作（`raise`/`smooth_shift`/`cliff`/`texture`…，与 `algo` 配对） |
| `before` | string | 操作前 fixture 名（`fixtures/<before>.w3e` 必须存在） |
| `after` | string | 操作后 fixture 名（官方编辑器存出，必须存在） |
| `size` | int | 笔刷尺寸（1/2/3/5/8） |
| `shape` | string | `circle` 或 `square` |
| `cx`, `cy` | int | 点击角点坐标 |

### 1.2 协议要求字段（缺 → 列入 A.7 缺口报告，不判失败；但**新用例应当补齐**）

| 字段 | 类型 | 说明 |
|---|---|---|
| `tool_label` | string | 编辑器里的**工具名**（中文 UI 名，如"升高地形"），用于确认按钮与算法对应 |
| `modifiers` | object | 修饰键：`{"shift":bool, "erase":bool, "right_click":bool}` |
| `steps` | array | 点击/拖动步骤：`[{"action":"down","x":31,"y":32,"t_ms":0},{"action":"up","t_ms":500}]`（坐标为主机屏幕相对格坐标） |
| `tileset` | string | 地图 tileset（如 `L`/`A`）+ `GroundTilesets`/`CliffTilesets` 列表 |
| `resource_version` | string | 客户端/资源版本（CASC build、`Data` 目录版本） |
| `editor_config` | object | 编辑器配置改动（`MiscData`/INI）；未改也写 `{"changed": false}` |

### 1.3 算法专用字段（按需）

| 字段 | 用于 | 说明 |
|---|---|---|
| `k` | `cliff` | 层级增量（+2/+1/0/−1/−2） |
| `ct` / `ct_id` | `cliff`/`shallow`/`deep` | 悬崖类型索引或 tileset 名（`ct_id` 优先，按 `CliffTilesets` 查表） |
| `clicks` | `cliff_repeat` | 相对点击序列 `[[0,0],[0,0],…]` |
| `repeat` | `cliff_repeat` | 同点重复次数（与 `clicks` 二选一） |
| `tex` | `texture` | 地面纹理编号 |
| `note` | 任意 | 备注（人工说明，仅记录） |

---

## 2. 采集目录规范（人工/脚本落盘）

每个用例一个目录，命名：`experiments/<用例号>-<算法>-<尺寸><形状>-<动作>/`

```
meta.json      # = cases.json 里该用例的条目（含 1.2 协议字段），外加采集时的原始记录
before.w3e     # 操作前
after.w3e      # 操作后（官方原样，禁止手工修正）
patch.md       # 人可读的逐字段差异表
fields.tsv     # 机器可读：case  x  y  field  official  actual  status  reason
shots/         # 需要看渲染时：同视角 before/after 截图（近景：悬崖底边/顶边/拐角/岸边）
```

`fields.tsv` 表头固定：

```
case	x	y	field	official	actual	status	reason
```

- `status`：`一致` / `未决` / `已验证` / `实现缺陷`
- 只有 `一致` 才能算通过在 A 层之外；`未决` 必须原样保留

---

## 3. `known-differences.json`（未决差异登记）

```json
{
  "_readme": ["…见文件内说明…"],
  "entries": [
    {
      "case": "cliff_repeat:P_c+2_s3_x3",
      "x": 31, "y": 33,
      "field": "L",
      "expected": 6, "actual": 4,
      "status": "未决",
      "reason": "疑似光标偏移或重复施加语义差异（官方存档显示外扩区层更高）",
      "evidence": "尚无实验确认；由 P0-1 与 P0-5 实验判定"
    }
  ]
}
```

规则：

1. 登记的差异在输出里**逐点列出**，状态保持 `未决`，**不判失败**，但也**不算通过**。
2. **未登记**的任何坐标字段差异 → harness 判失败（退出码 1）。
3. `known-differences.json` **缺失 → 直接失败**。
4. 登记项若本次未复现（已修复），输出 A.4 提醒，建议删除该登记。
5. `status` 三选一：`未决`（未做实验，禁止当通过）/ `已验证`（有官方存档实验，可据此改实现）/ `实现缺陷`（已确认是实现错误，待修）。

---

## 4. harness 的失败条件（硬失败）

| 条件 | 结果 |
|---|---|
| 缺 `cases.json` / `results.md` / `known-differences.json` | 失败 |
| 用例缺 1.1 任一必需字段 | 失败 |
| 用例引用的 `fixtures/<name>.w3e` 不存在 | 失败 |
| `results.md` 未解析出任何用例行 | 失败 |
| `results.md` 用例行数与 `cases.json` 不一致 | 失败 |
| `results.md` 里有 `cases.json` 没有的用例（预期用例缺失） | 失败 |
| 出现未登记的坐标字段差异 | 失败 |
| 噪声**覆盖区内**出现范围/量化越界（Δraw 偏离 25.6·k 超过 ±1） | 失败 |

**不**判失败、但必须出现在报告里：已登记未决差异、噪声覆盖区外的坡度后处理外扩、B 层未决断言、A.7 元数据缺口。
