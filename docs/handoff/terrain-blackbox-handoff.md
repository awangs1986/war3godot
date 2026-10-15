# 交接文档：war3godot 官方地形算法黑盒实测

> 下一次会话用途：继续用黑盒实验补齐 War3 1.32.10 官方世界编辑器地形按钮的未定论项，并以教学方式更新文档，让用户（个人开发者）真正学会算法原理，而不是只拿结论。

## 1. 目标
- 用户项目：war3godot（Gitea http://gitea:3000/awangs/diablo1like，Godot 4.7 .NET C#）。地形（悬崖、不同地表接缝、海岸、高度、编辑器笔刷）长期对不上原版。
- 目标：通过官方编辑器的**输入/输出黑盒实验**，把每个工具面板按钮的真实算法教给用户（原理 → 推导 → 实测数据 → 对应用户代码位置）。用户自己对照修改源码。

## 2. 已有产物（不在此重复，请直接读）
- 主文档：分支 `docs/official-terrain-algorithms` 上的 `docs/official-terrain-algorithms.md`，PR #1：http://gitea:3000/awangs/diablo1like/pulls/1
  - 第 12 章：每个按钮一节的实测定论（12.0–12.13）
  - 12.14：已定论 / 仍未定论清单（以它为准）
  - 12.15：P0/P1/P2 修改建议汇总表（对应 `TerrainBrush.cs`、`GroundLayout.cs` 等行号）
  - 第 9 章：实验方法
- 提交：`8363942`（第二轮），`2064cff`（第三轮：A 地形、坡道两层差/拐角、整平边缘、放水整平地面）

## 3. 关键发现速览（详情见文档对应节）
- 用户代码照抄 HiveWE/Warsmash（社区实现），连笔误一起抄（悬崖传播 `:458`/`:462`，边界 `:534`）。
- 升高/降低：单次施加 `0.05·s·cos(πd/2R)` 层，非按帧（12.1）。平滑 0.8/0.2，Shift 1.0/0.05（12.5）。噪声 ±0.05·k（12.4）。
- 悬崖传播：8 邻接层差 ≤ 2 的 BFS（12.6）；ct 写入本图悬崖列表下标而非按钮序号。
- 水：层级 d≤R，水面 d≤R+1 = h0+(L0−2)·512，地面 d≤R+2 整平为 h0（12.7）——海岸接缝主因之一。
- 坡道：只在层差=1 生效，3 个角；拐角斜外侧写 3×3（12.9）。
- 渲染：悬崖格用悬崖类型地表，并按 4 角掩码向邻格过渡半格（12.13）。
- 悬崖底边缝：悬崖顶点双线性 vs 地面三角插值不一致（`TerrainSurfaceGeometry.cs:21-24,43`）；水偏移写死 −0.7 应读 Water.slk。

## 4. 未定论项（下一步计划）
1. 整平边缘“半施加”的精确公式（经验：相机一侧外圈 ≥ h0−611，固定配方 100% 复现，见 12.3）。
2. A(−0.6) 与 L(−0.7) 水面常数、水色的逐像素截图比对；其他 tileset（B、N…）——需先标定“新建地图”对话框 tileset 列表坐标（`drv.newmap(ts_xy)`）。
3. 北侧坡道（相机遮挡未点中，可移动镜头后重测）。
4. 多纹理叠放：4 贴图同格、荒芜+水边等更多放大截图核对。
5. 完成后更新 12.x 和 12.14，提交到同一分支。

## 5. 实验环境（用户机 awangs-linux，所有命令在用户机执行）
- 目录 `~/w3_blackbox/`：
  - `drv.py`（python-xlib/XTEST 驱动：`tool()` 选按钮并只勾对应组、`pal()`、`apply()`、`drag()`、`undo()`、`saveas()`、`shot()`、`recenter()`、`newmap()`）
  - `w3e.py`（解析 war3map.w3e；`python3 w3e.py a.w3m b.w3m` 逐角点差分）、`absdump.py`、`fitsm.py`、`fld.py`
  - 批次脚本：`batch*.py`、`batchA.py`、`texA.py`、`scene.py`、`pl2.py`、`pl3.py`、`rp.py`、`rp2.py`、`tz.py`
  - `maps/`：约 200 份存档（`base*`、`LS00–12`、`A_*`、`P2_*`/`P3_*` 整平、`R1/R2/R3_*` 坡道、`TZ_scene`）；`shots/` 截图；`prefix/` Proton 前缀；`tools/`（rea、Ghidra 12.1.4、JDK21、Node22，`source tools/env.sh`）
- 启动编辑器：在 `<游戏目录>/_retail_/x86_64` 用 `Proton - Experimental` 的 `proton run "World Editor.exe" -launch`，`STEAM_COMPAT_DATA_PATH=~/w3_blackbox/prefix`。**必须带 `-launch`**，否则启动的是游戏。启动后关欢迎窗，确认标题为“魔兽争霸III地图编辑器”。
- **面板位置**：drv.py 写死工具面板客户区在 (1592,307)。若跑偏，`wmctrl -i -r <工具面板窗口id> -e 0,1592,307,-1,-1`（wmctrl 随后报告 1612,379 属正常）。用 `checked()` 验证勾选框读取正常后再跑实验。
- 三个勾选框（纹理/悬崖/高度）：纹理组与另两组可组合，每次实验确认只勾要测的那组（`tool()` 已处理）。
- 编辑器跑在用户正在用的桌面上，实验期间会移动鼠标；结束后关闭编辑器并 `wineserver -k`，用 `pgrep -af "World Editor|wineserver|drv.py"` 确认无残留。

## 6. 红线
- 不做内存 dump、不附加调试器、不脱壳（exe 磁盘代码加密，静态分析读不出算法），只看编辑器输入/输出。
- 资源包不整包拷贝；需要时在用户机只读提取小文件。
- 不改仓库源码，只改文档。实验数据留在 `~/w3_blackbox`，不删。
- 只推送到 Gitea，不往 GitHub 推任何东西。凭据（已脱敏，向用户确认）只放在单条 push URL 里，不写入文件或 git 配置；推完删临时目录并检查 shell 历史。

## 7. Suggested skills
- `handoff`（https://github.com/awangs1986/catskills/tree/main/skills/productivity/handoff，下次交接再用；只读）
- rea（https://github.com/morluto/rea，仅用于导入表/字符串等可读部分）
- 文档写作保持“教学式”：原理 → 推导 → 数据 → 改法。
