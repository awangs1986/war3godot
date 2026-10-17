# 动态内存验证：REA 静态地址 = 运行时地址（1.32.10.18820）

> 目的：确认 REA 反汇编里的地址在**运行期**是否就是同一处代码/数据；并记录内容级读取受阻的确切原因。
> 方法特点：**零风险**——只读 `/proc/<pid>/maps`，不需要任何调试器；不改变编辑器状态。
> 结论分级：本文只给出**地址空间**层面的实证；**行为级**（写入了哪些角点、`invert` 取值）**尚未取得**运行期证据。

## 1. 目的与做法

目的：确认 REA（静态反汇编）里的地址在**运行时**是否就是同一处代码/数据——这是"内存读取"要回答的第一件事。

做法（零风险，不需要任何调试器）：正常启动 `World Editor.exe` 后，读 `/proc/<pid>/maps` 的段映射，
与磁盘 PE 的节表（ImageBase / 节 RVA / VirtualSize）逐项对齐。

## 2. 实测结果

| live 映射（`/proc/<pid>/maps`） | 权限 | 文件偏移 | 大小 | 对应 PE 节（磁盘头） |
|---|---|---|---|---|
| `0x140000000 – 0x1417b0000` | `r-xp` | 0 | 24256 KB | `.text`(RVA `0x1000`,19045KB) + `.rdata`(RVA `0x129b000`,5221KB) |
| `0x1417b0000 – 0x141a70000` | `rw-s` | `0x17b0000` | 2816 KB | `.data`(RVA `0x17b5000`, 期望地址 `0x1417b5000` ✓ 落在该段内) |
| `0x141a70000 – 0x141c44000` | `r--p` | `0x1a70000` | 1872 KB | `.pdata`/`_RDATA`/`.rsrc`/`.reloc` |

- PE **ImageBase = `0x140000000`**，live `.text` 起始 **正好 `0x140000000`** ⇒ **没有重定位偏移（ASLR 未生效）**
- ⇒ **REA 静态地址 = 运行时地址**，`FUN_14026c5c0`/`FUN_14026a480`/`FUN_14027d450`/`FUN_1403be230`
  等都**无需 rebase** 即可用于运行期断点/读取。此前这只是"推断"，现在有**运行期段映射证据**。

## 3. 供未来核对的目标字节（磁盘镜像，RVA `0x27d450` = `FUN_14027d450` 入口）

```
5341bb334ae1dc002fda5601081fd369
```
（若将来能读运行内存，应与该序列逐字节相同；不同即说明运行期被补丁。）

## 4. 内容级读取被阻断（含确切 errno）

| 通道 | 结果 | 说明 |
|---|---|---|
| `/proc/<pid>/mem` | `PermissionError [Errno 13] denied` | Yama `ptrace_scope=1` + 无 `CAP_SYS_PTRACE` |
| `/proc/<pid>/map_files/140000000-1417b0000` | `PermissionError [Errno 1] Operation not permitted` | memfd 映射需 `CAP_SYS_ADMIN` |
| `winedbg 0xd4`（FIFO 批命令） | 首次可 attach；**退出/超时即连带杀死编辑器**（实测 3 次） | 必须 `detach` 后才能 `quit`；批处理 stdout 缓冲到退出才刷 |
| `winedbg --command ... 0xd8` | `can't debug its own process` | 该 pid 已不存在 |
| `gdb --args wine "World Editor.exe"` | 断点解析为 `0x4026c810`（= 截断 `0x100000000`） | gdb 默认架构 `i386`；`set architecture i386:x86-64` 后解析正确为 `0x14026c5c0` ✓ |
| 同上 + `set follow-fork-mode child` | 追到 `wine64-preloader`，但**窗口属于另一个未追踪实例**（Proton 用 `start.exe /exec` 起子进程） | 点击落在残留实例上 ⇒ 0 命中 |
| **坡道笔刷入口** | **本机 UI 无入口** | 地形面板只有 纹理/悬崖/高度/尺寸/形状；面板下拉仅 地形/装饰物/单位/区域/镜头（无"杂项"）⇒ 坡道内存取证**不可驱动** |

## 5. 解除阻断的三种具体做法（供下次选用）

1. **让编辑器成为 gdb 的直接 64 位子进程**：绕开 Proton 与 `start.exe`，用
   `gdb --args <proton>/files/lib/wine/x86_64-unix/wine64 "World Editor.exe"`，
   且工作目录设为 `_retail_/x86_64`（相对名不走 `start.exe`）；启动后 `set architecture i386:x86-64`，
   等窗口出现再 `break`，并确保**系统里没有其它编辑器实例**（否则 X 事件会送给残留实例）。
2. **放开 Yama**：`sudo sysctl -w kernel.yama.ptrace_scope=0` 后用 `gdb -p <pid>` 直接 attach（最简单）。
3. **补上坡道入口**：把 `MiscBrushes:Ramp` 的面板加进编辑器 UI（或直接用扩展面板脚本），
   否则坡道的运行期取证只能靠存档字节（现状）。

## 6. 对既有结论的影响

- ✅ 增强：所有 REA 函数地址 **确认为运行地址**，`known-differences` 与反汇编引用的地址可直接用于断点。
- ⚠ 不变：坡道形状规则、Shift 掩码来源、`FUN_14026e050` 语义、层的后处理链路 **仍缺运行期证据**；
  本轮的坡道/水内存取证**未取得**（原因见 5.4，均为环境性阻断，不是"做过且没差异"）。
- 🔒 纪律：不把"地址映射已验证"写成"行为已在内存中验证"。

## 7. 据此修掉的缺口：Shift 移除坡道

证据双源一致后实施（**只用实测 + REA，不推算形状规则**）：

- 实测：`Y10_ramp_made → Y10_ramp_shift` 恰好 **9 点 `ramp` 1→0**，其余字段 0 变化（同组角点）
- REA：`FUN_14027d450` 的 `invert` 语义 = `bit==invert` 跳过；`invert=1 → or 0x0002`（置位）、`invert=0 → and 0xfffd`（清位）
  ⇒ Shift 对应 `invert=0`（**清位**）

改动（`TerrainBrush.Ramp`）：
- 新增 `remove` 参数，写入值由 `true` 改为 `target = !remove`
- 分派 `case BrushOp.Ramp: Ramp(m, cx, cy, s.Shift);`

回归结果：`ramp_shift_remove` 用例 **0/9 → 9/9**；`known-differences` 登记数 **67 → 58**（删掉该 9 条），
`未登记差异 0`、退出码 0。

**仍未修（等补测，不猜）**：
- 坡道形状（`Y9_ramp_n0/n1` 0/3、`n3` 3/9、`Y10_ramp_made` 0/9）——缺"3 共线 vs 3×3 判定分界"与凸角/凹角/边缘实测
- 深水再点（`Y7_dp2` 0/17）——官方 23 点变化的机制未解释
