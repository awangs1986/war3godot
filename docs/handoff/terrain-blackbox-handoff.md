# 交接文档：war3godot 官方地形算法（黑盒实测 + 1.27 静态逆向）

> 下一次会话用途：补齐“仅1.27源码 / 未测”的项（陡坡放水、方形高度笔刷、连点命中点、水相关钳制），找贴图/荒芜/边界写入函数，解释 711 raw 差距；继续以教学方式（原理 → 推导 → 数据 → 改法）帮用户自己改 war3godot。
> 更新：2026-10-15（UTC+8）

## 1. 目标
- 项目 war3godot：Gitea http://gitea:3000/awangs/diablo1like（Godot 4.7 .NET / C# / .NET 8）。悬崖、地表接缝、海岸、高度、编辑器笔刷与原版不符。
- 用户要**学会算法本身**，自己改源码；我们只交付 `docs/`（文档 + 教学参考代码）。

## 2. 产物（直接读，不重复）
| 产物 | 位置 |
|---|---|
| PR #1：主文档 + 1.27 对照，最新 `0c966e9` | http://gitea:3000/awangs/diablo1like/pulls/1（分支 `docs/official-terrain-algorithms`） |
| PR #2：参考实现按 1.27 修正 + 流水线重构 + 验证强度表 | http://gitea:3000/awangs/diablo1like/pulls/2（分支 `docs/terrain-reference-127`，**叠在 PR #1 上**；PR #1 合并后把 base 改为 main）。提交 `3dfa3fe` 及之后（见 `git log`） |
| 主文档（12 章逐按钮；12.14.1 验证强度表；12.15 改法） | `docs/official-terrain-algorithms.md` |
| 1.27 函数地址与逐项结论 | `docs/classic-we-rea-comparison.md` |
| 参考实现 / 导读 / 结果 | `docs/terrain-reference/`（`src/HeightPipeline.cs` 是新的公共流水线；`results.md` 有按算法验证强度表、噪声分布表、“仅1.27源码”清单） |

## 3. 当前状态（数字以 `results.md` 为准）
- 不再给总匹配率，改为按算法 + 验证范围（`1.27源码+1.32存档验证` / `仅1.27源码` / `仅1.32存档`）。
- 流水线：高度计算 → 量化 trunc((world+2048)·4) → 坡度/高度钳制（MaxSlope 50°，610.2 raw）→ 返回实际改动区。升高、降低、整平、噪声、平滑都走这条；浅水/深水 = 写水 + 整平(s+2, h0) + 悬崖(s, T)。
- 噪声测试现在检查官方**和参考实现**输出的范围 + 量化格点（6 个存档 601/601），另做 k 分布统计（TV 与抽样波动同量级）。
- 悬崖连点测试现在比较层级 + 悬崖类型：158/178。
  - `P_c+2_s1_x3`：命中点由穷举推断（第 1 次在 (31,32)），21/21，**只说明假设自洽，不算验证**。
  - `P_c+2_s3_x3`：137/157（4 L + 16 ct）。±2 内穷举 3 次点击最好仍差 10，**未解释**；可能有拖动或多施加。重做流程见 `guides/cliff.md`“逐次记录真实命中角点”。
- 新增“已有浅水上放深水” `LS03_shallow → LS04_deep` 21/21；`DeepExtra`（this+0x31c）常数未读出，只知在 (38.4,166.4) world，参考取 128。
- 仅1.27源码 / 未实现 / 未测：方形整平·噪声·平滑；悬崖越界整笔拒绝；MaxHeight/MinHeight；钳制里的水下限（0x51dcb0/0x605fc0/0x605020，未实现）；悬崖按水夹层 0x5764a0（未实现）；悬崖后高度修正 0x57f270→0x57ef30（未实现）；陡坡放水（未测）；水面写到外扩一圈（1.32 实测）与 1.27 只写覆盖集合的差异。
- 1.27 未覆盖：贴图、荒芜写入函数未定位；边界只到 0x5f7d10（→0x5f8070/0x63c250）。
- **711 raw 差距**未解释（见主文档 12.14）。可能与层偏移常数 `DAT_0086b494` / `DAT_008a9764` 有关。

## 4. 下一步
1. 先问用户“下一步计划”（用户说过要讲，还没讲）。
2. 黑盒补实验（需用户同意占用桌面）：陡坡放浅/深水；方形整平/噪声/平滑；连点逐次存档记录命中点；已有深水上放浅水。
3. Ghidra 读常数 `DAT_0086b494`、`DAT_008a9764`、`DAT_007ee13c`、`DAT_007cbd40`、`DAT_0086b618`、this+0x31c 来源，核对 (L−2)·128、181.02、128、DeepExtra。
4. 找贴图/荒芜写入函数（`CTerrain*.cpp` 字符串），读完边界 0x5f8070。
5. 实现水相关下限与悬崖后高度修正，加用例。
6. 追 711 raw 差距。

## 4.1 缺口 / 未定论清单（按优先级）

> 标签：`1.27源码+1.32存档验证` / `仅1.27源码` / `仅1.32存档` / `未测` / `假设`。P0 = 直接影响用户编辑器手感或常见地形；P1 = 边界情况；P2 = 完整性。

| # | 优先级 | 缺什么 | 当前证据 / 标签 | 对用户编辑器的影响 | 具体下一步 |
|---|---|---|---|---|---|
| 1 | P0 | 用户编辑器还没有统一高度流水线和脏区刷新 | 参考实现 `HeightPipeline.cs` 已有（`1.27源码+1.32存档验证`）；用户代码 `TerrainBrush.cs` 各笔刷各写各的 | 不做钳制，整平/平滑边缘与官方不符；不按“实际改动区”（含钳制波及的圈外点）重建网格/法线/水面，会留下接缝和旧顶点 | 按 `docs/terrain-reference/src/HeightPipeline.cs` 的 ①计算 ②量化 ③`SlopeClamp` ④返回改动区，改写用户的 Raise/Lower/Plateau/Noise/Smooth/Water；用④的集合外扩一圈刷新网格（对应 12.15 表） |
| 2 | P0 | 陡坡上放水 | `未测`；参考实现已让水走整平(s+2)+钳制+悬崖 | 海岸接缝最常出现在坡上放水；钳制会改岸边地面高度 | 黑盒：`P2_base2`（或 raise s8 山丘）上，`drv.tool('shallow')` s3 点一次 → `saveas('WS_sh')`；撤销后深水 → `saveas('WS_dp')`；`prep_fixtures.py` 加 case（fields L,water,w,h,ct），跑对照 |
| 3 | P0 | 高度钳制里的水相关下限 | `仅1.27源码`，未实现：`0x57f8f0` 里 `FUN_0051dcb0` → `0x605fc0` / `0x605020`，常数 `DAT_0086b494`、`DAT_0086b4a0`、`_DAT_007d1720` | 水边降低/平滑时地面可能被托住，不处理会让水底穿出水面 | Ghidra 反编译 `0x51dcb0`、`0x605fc0`、`0x605020`，读出上述常数；再做黑盒：水边 lower s3 多次，看地面最低停在哪 |
| 4 | P0 | 1.32 水面写到外扩一圈，1.27 源码只看到覆盖集合 | 外扩一圈：`仅1.32存档`（211/211）；1.27 `0x603cc0` 循环只写覆盖集合 | 水面范围差一圈就是海岸接缝 / 水面断边 | 反编译 `0x6049c0`（`0x603cc0` 写完后调用）看是否外扩；若 1.27 确实只写覆盖集合，则为重制版改动，以 1.32 为准 |
| 5 | P1 | 已有水上放深水的下压常数 DeepExtra（this+0x31c） | `1.27源码+1.32存档验证（1 例）`，常数只知在 (38.4, 166.4) world，参考取 128（猜测） | 在已有水上加深时层级算错，岸线会跳 | Ghidra 查写 `this+0x31c` 的位置（`FindUse.java 0x31c`，或 `0x56b790` 构造函数附近读 Water.slk 的代码）；黑盒补“已有深水上放浅水 / 深水”两例 |
| 6 | P1 | 悬崖笔刷按水夹层 + “夹后值改写 T”怪癖 | `仅1.27源码`，未实现：`0x574470` 调 `0x5764a0` 得 [下限,上限]，夹后写回 param_5 影响后续点 | 水边拉悬崖时层级与官方不同，悬崖/海岸模型选错 | 反编译 `0x5764a0`；黑盒：在浅水边缘用 −1 / −2 s3 各一次，逐点对照 |
| 7 | P1 | 悬崖改层后的高度上下限修正 | `仅1.27源码`，未实现：`0x575f70` → `0x57f270` → `0x57f2a0` → `0x57ef30`（cos 衰减拉回） | 高地形上 +2 悬崖可能把总高推过 MaxHeight，官方会压回 | 反编译 `0x57ef30` 写清衰减公式；黑盒：raise 到接近上限后 +2 悬崖 |
| 8 | P1 | 悬崖连点：`P_c+2_s1_x3` 是拟合假设，`P_c+2_s3_x3` 未解释 | s1：`假设`（穷举推断第 1 次落在 (31,32)，21/21 不算验证）；s3：137/157（4 L + 16 ct） | 用户若照这两例调传播逻辑会被误导 | 按 `guides/cliff.md`“逐次记录真实命中角点”：每次点击后 `saveas`，`w3e.py` 差分得命中点写 `cr_hits.txt`，填进 `cases.json` 的 `clicks` 字段重跑；s3 另试“按住不动 1 秒”区分是否有拖动施加 |
| 9 | P1 | 1.27 才有、1.32 未验证的行为：方形整平/噪声/平滑、悬崖目标越界整笔拒绝、MaxHeight/MinHeight 绝对上下限 | `仅1.27源码` | 重制版可能改过；用户若照抄可能与 1.32 不符 | 黑盒：方形 s3 整平/平滑/噪声各一次；层 15 上 +1、层 1 上 −2（区分拒绝 vs clamp：拒绝时整笔无变化，clamp 时周围会变）；raise 连点到顶看是否停在 1536 world |
| 10 | P1 | 711 raw 差距 | 未解释：某存档相邻角点差 711 > 610.2（主文档 12.3） | 若钳制理解有误，所有高度笔刷边缘都会偏 | 先确认这对角点是不是对角邻居（对角上限 tan50°·181.02 ≈ 862.9 raw，711 合法）或跨层级（钳制只比地面高度，不含层偏移）；用 `w3e.py` 打出该存档两点的 h、L 即可判定，再写进 12.14 |
| 11 | P2 | 噪声只能统计验证 | `1.27源码+1.32存档验证（统计）`：范围+量化逐点，k 分布 TV 与抽样波动同量级 | 影响小：随机笔刷本来逐点不可复现 | 反编译 `0x581460` 看用的是哪个 rand（CRT `rand()` 还是 Storm 随机），若是 CRT 可按种子复现；否则保持统计验证 |
| 12 | P2 | 贴图 / 荒芜 / 边界在 1.27 未定位 | `仅1.32存档`（51/51、13/13、13/13）；边界入口 `0x5f7d10` → `0x5f8070` / `0x63c250` | 贴图随机变体、荒芜外圈、边界都只靠黑盒，规则可能缺边角情况 | 用字符串 `CTerrainTexture`/`Blight`/`CTerrainBounds.cpp` 找 xref（`DumpRefs.java`），反编译 `0x5f8070`、`0x63c250`；贴图变体比例对照 `GroundLayout.cs`（12.15 的 91%/9%） |
| 13 | P2 | 参考实现没覆盖渲染端：地表贴图过渡（tile blending）、海岸透明渐变 / 接缝、悬崖模型选择（cliff mesh 按 4 角层差查模型）、悬崖与地面插值一致性 | `Rendering.cs` 只有自检（无官方数值）：插值裂缝 1.38 world、MX_quad 四掩码、LS04 岸边格数 | 这是用户“不同地形接缝 / 海岸接缝 / 悬崖缝”的直接来源，目前只有文档结论（12.13）没有可对照代码 | 1.27 里找悬崖模型名拼接（字符串 `Cliffs\\`、`AAAB` 这类 4 字母层差码）和地表 tile 索引表；做截图黑盒：同一格 4 角不同贴图 / 不同层差，各存档 + 正交顶视截图，写进 `Rendering.cs` 并加对照 |

共 13 项。

## 5. 环境（用户机 awangs-linux，machineId `6af1add5-3c0e-4985-869e-5ad95c46e310`）
- `~/w3_blackbox/`：`drv.py`（XTEST 驱动；`tool()`、`apply()`、`saveas()`、`recenter()` 等）、`w3e.py`（`python3 w3e.py a.w3m b.w3m` 逐角点差分）、`prep_fixtures.py`（存档 → fixtures/cases.json）、批次脚本、`maps/`（约 300 份存档）、`shots/`、`prefix/`（Proton 前缀）、`tools/`（rea、Ghidra 12.1.4、JDK21、Node22、`tools/dotnet` = .NET 8 SDK、`tools/py/mpyq`；`source tools/env.sh`）。
  - 启动编辑器：游戏目录 `_retail_/x86_64` 下 `proton run "World Editor.exe" -launch`（**必须 `-launch`**，否则是游戏），`STEAM_COMPAT_DATA_PATH=~/w3_blackbox/prefix`；工具面板跑偏用 `wmctrl` 移回 (1592,307)。结束后 `wineserver -k`。详见主文档第 9 章。
- `~/w3_blackbox/classic/`：1.27 `WorldEdit.exe`、`WorldEditData_127.txt`、`we127.snap`、`trace_smooth.json`、Ghidra 项目 `gh/we127`、`scripts/`（DumpRefs/FindUse/Decomp/Dis.java）、`out/ out2/ out3/`（`out3/index.txt`）、`rd.py`（在 classic 目录运行）。
  - `analyzeHeadless gh we127 -process WorldEdit.exe -noanalysis -readOnly -scriptPath scripts -postScript Decomp.java 0x<addr>,... out4`
- 跑参考实现：`cd docs/terrain-reference && PATH=~/w3_blackbox/tools/dotnet:$PATH dotnet run -c Release`，结果在 `bin/.../results.md`，拷回时保留文件头说明，删 bin/obj。
- 用户本地仓库 `/media/awang/108859E68859CABC/godot project/diablo1like`：`main` = Gitea main `7cda4d0`；旧历史在分支 `backup/pre-gitea-2026-10-15` 和 `stash@{0}`（“backup before update to gitea main 2026-10-15”）。**不要删**。
- `/tmp/w3nuget.config`：临时 NuGet 配置（仅 nuget.org 源），丢了重建。

## 6. 红线
- 1.32：不 dump 内存、不挂调试器、不脱壳，只黑盒。1.27（未加密）可静态分析。
- 资源包不整包拷贝；只改 `docs/`；`~/w3_blackbox` 不删。
- 只推 Gitea，不推 GitHub（catskills 只读）。凭据 **[已脱敏]**，向用户确认；只放单条 push URL / 单次 API 请求头，不写文件或 git 配置；推完删临时克隆、清 shell 历史。已建议用户改密码。
- 对用户：中文，每段结尾一个「喵！」。

## 7. Suggested skills
- `handoff`（https://github.com/awangs1986/catskills/tree/main/skills/productivity/handoff，只读）
- rea（https://github.com/morluto/rea）+ Ghidra headless：1.27 静态分析
- 教学式文档：原理 → 推导 → 数据 → 改法；每条结论标验证范围
