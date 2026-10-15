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
