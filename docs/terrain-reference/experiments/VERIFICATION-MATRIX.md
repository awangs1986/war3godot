# 1.32 验证矩阵（PR #1–#5 断言复核）

> 依据 `AGENTS.md` 的 Evidence discipline：每条结论必须有实测数据或可复核产物；
> 证据分级沿用三层：**A 官方存档已验证 / B 反编译·反汇编支持 / C 实现自检**。
> 本文件的"状态"只描述**证据强度**，不描述实现正确性。

生成方式（可复现）：
- 通读 PR #1–#5 的算法主张（PR#1 教学文档、PR#2 1.27 参考修正、PR#3 1.32 动态捕获、PR#4 实现重写、PR#5 实验规范）
- 对 `guides/*.md` 做证据关键词扫描（是否提及 1.32 / 实测·存档 / REA·反编译）
- 统计 `cases.json` 的 63 条用例按 op 分布
- 汇总 `experiments/FINDINGS.md` 的 R1–R37

---

## 1. 覆盖总览

### 1.1 cases.json（63 条）

| op | 用例数 | 其中无参考实现（仅官方字节） |
|---|---|---|
| cliff | 11 | 0 |
| ramp | 10 | 5 |
| raise | 8 | 3 |
| texture | 7 | 3 |
| noise | 6 | 0 |
| deep | 4 | 1 |
| none | 4 | 4 |
| lower | 2 | 0 |
| plateau | 2 | 0 |
| smooth | 2 | 0 |
| shallow | 2 | 0 |
| blight | 2 | 1 |
| smooth_shift | 1 | 0 |
| boundary | 1 | 0 |
| ramp_shift_remove | 1 | 1 |

### 1.2 guides 证据扫描（15 个算法文档）

| guide | 提到 1.32 | 提到实测/存档 | 提到 REA/反编译 | 判定 |
|---|---|---|---|---|
| blight | ✓ | ✓ | ✗ | 部分（实测 2 条，交互项缺） |
| boundary | ✓ | ✓ | ✗ | **仅"放置"，"移除"未测** |
| cliff-type | ✓ | ✓ | ✗ | 部分（ct 传播已由 B 级补强） |
| cliff | ✓ | ✓ | ✗ | 较全（11 条用例） |
| footprint | ✓ | ✓ | ✗ | **形状维度未系统覆盖** |
| **ground-transition** | ✗ | ✗ | ✗ | **无任何证据** |
| **height-interpolation** | ✗ | ✗ | ✗ | **无任何证据** |
| noise | ✓ | ✓ | ✗ | 较全（6 条） |
| plateau | ✓ | ✓ | ✗ | 部分（2 条） |
| raise-lower | ✓ | ✓ | ✗ | 较全（10 条含 Shift） |
| ramp | ✓ | ✓ | ✗ | 部分（形状/边界/凸凹角未闭环） |
| **shore** | ✗ | ✗ | ✗ | **无任何证据** |
| smooth | ✓ | ✓ | ✗ | 部分（2+1 条，核已验） |
| texture | ✓ | ✓ | ✗ | 部分（清 blight 仅有 B 级） |
| water | ✓ | ✓ | ✗ | 部分（深水再点未解释） |

---

## 2. 未在 1.32 验证的算法清单（本轮要补的对象）

### G1 · 地表过渡接缝 `ground-transition`
- 现状：`guides/ground-transition.md` 无 1.32 字样、无实测、无 REA
- 缺什么：过渡规则的**官方产物**（相邻不同 tileset/纹理的角点与 `real_tile_texture` 打包）或**反编译**（拼接/渲染路径）
- 补法：① Ghidra 分析拼接模块（`1.32-map-stitching.md` 涉及的函数）；② 官方存档对比：同图两处不同纹理接缝处的角点字段

### G2 · 高度插值 `height-interpolation`
- 现状：无 1.32、无实测、无 REA
- 缺什么：编辑器如何由角点高度生成插值面（中心点插值、斜坡判定），是否有**中心高度**独立字段
- 补法：① Ghidra 找高度管线（`raise/lower` 笔刷之外的插值函数）；② 存档侧检查是否存在中心高度字段（我们的 `W3eMap` 是否漏读）

### G3 · 海岸 `shore`
- 现状：无任何证据
- 缺什么：水陆边界如何自动生成斜坡/海岸线；是否由水笔刷自动附加
- 补法：① Ghidra 分析水刷 `FUN_140314880` 的邻近逻辑；② 官方存档：在斜坡临水处落笔，看是否额外写 ramp 旗

### G4 · 足印形状 `footprint`（圆/方/菱形）
- 现状：用例名里出现 `_c_`（圆）与 `_q_`（方），但**没有系统对照**
- 缺什么：方形与圆形足印的逐角点集合差、对角线形状是否存在
- 补法：官方存档：同位置同尺寸分别用圆/方落笔，逐角点比对

### G5 · 边界带"移除" `boundary`（remove 路径）
- 现状：仅 `base -> T_bound_add_s3`（放置）
- 缺什么：移除（Shift）时的清除范围与放置是否对称
- 补法：官方存档：放置后 Shift 落笔，逐角点比对

### G6 · 坡道边界/凸凹角（R36、R37）
- 现状：R36 已降级（所引函数是悬崖函数）；R37 明确"未取得实测"
- 缺什么：地图边缘处坡道行为、凸角外侧（恰 2 个高 4 邻）、凹口几何
- 补法：**Ghidra 反编译 `FUN_14027d450`（已具备，2606 条指令）**逐分支读判定 → 再用官方存档验证关键分支

### G7 · 深水再点（R19，17 点未解释）
- 现状：官方 23 点变化（cv 20 / L 5 / water 5 / ct 7），机制未知
- 缺什么：水刷在"已是深水"处的分支行为
- 补法：**Ghidra 反编译 `FUN_140314880`** + 存档逐点比对

### G8 · 荒芜 × 纹理交互（R11）
- 现状：语义已由 **B 级**证实（`ghidra_286a60.c:190 uVar6 = 0xfffb` → 清 bit2，`:192` 应用），但**缺官方存档实测**
- 补法：官方存档：先荒芜 → 同角点纹理覆盖 → 检查 blight 位

---

## 3. 已有 B 级证据、可直接升级为"已验证"的条目（本轮已用 Ghidra 取得）

| 断言 | 证据（行号级） | 新状态 |
|---|---|---|
| 坡道 `invert` 语义：`bit1 == invert` 跳过 | `ghidra_27d450.c:229` | **B 级已验证** |
| `invert=0 → & 0xfffd`（清 ramp 旗 = Shift 移除） | `ghidra_27d450.c:237` | **B 级已验证** |
| `invert=1 → | 2`（置 ramp 旗 = 创建） | `ghidra_27d450.c:240` | **B 级已验证** |
| 坡道跨度规则 `max-min < 2`（层高差 ≤1） | `ghidra_27d450.c:196-205` | **B 级已验证** |
| 每角点只处理一次（`alStack_58[idx]=1` 记账） | `ghidra_27d450.c:245` | **B 级已验证** |
| 角点回写索引掩码 `0x3ffff` | `ghidra_27d450.c:243` | **B 级已验证** |
| 纹理刷清 blight（`& 0xfffb` on `[+0xc]`） | `ghidra_286a60.c:190,192` | **B 级已验证**（实测仍缺，见 G8） |
| 纹理刷 NOTH：`0xffbf` / `| 0x40` 二分支 | `ghidra_286a60.c:166-171` | **B 级已验证** |
| 纹理索引写 byte[8] 低 4 位；`byte[9]` 低 5 位 | `ghidra_286a60.c:174,183,188` | **B 级已验证** |

---

## 4. 复现命令

```cmd
:: 1) 抓解密内存（编辑器运行时）
python tools\win_capture\dump_pe.py "World Editor.exe" tools\win_capture
:: 2) 用 dump 重建"可分析的 PE"
python tools\win_capture\rehydrate.py
:: 3) Ghidra headless 定向反编译（-noanalysis，仅目标函数）
set JAVA_HOME=D:\tools\jdk-21\jdk-21.0.12.1+1
D:\tools\ghidra_12.1.4_PUBLIC\support\analyzeHeadless.bat D:\tools\gh_proj rea ^
  -import D:\tools\we_decrypted.exe -noanalysis ^
  -scriptPath D:\tools\gh_scripts -postScript DumpFunc.java -deleteProject
```

产物：`_dump/ghidra_27d450.c`、`ghidra_286a60.c`、`ghidra_26c5c0.c`、`ghidra_26c810.c`
