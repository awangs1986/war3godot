# Round 14 反编译索引（REA / Ghidra 12.1.4）

来源：`D:\tools\we_decrypted.exe`（sha256 `2FF3ABA0…`）切出的最小 PE 切片，见各节 `切片` 列。
每行的"结论"已写入 `ALGORITHM-SPEC.md` 对应章节；此处给出**复现切片参数**与**关键片段**。

| 函数 | 切片文件 | VA 范围 | 规范章节 | 一句话结论 |
|---|---|---|---|---|
| `FUN_1403BE230` | `f_wr1.exe` | `0x1403BE230-0x1403BE600` | §47.1 | 唯一角点写入口；索引自校验 `rec[1]&0x3ffff`；整条 0x1c 覆盖写；**保留 `rec[0xB]`** |
| `FUN_140263B00` | `f_wr2.exe` | `0x140263B00-0x140263F00` | §47.2 | 脏块收集；`rec+0` = 打包世界坐标；**世界半径 = size×128+1.0**；脏块 4×4 格 / stride `0xFD8` |
| `FUN_14027F480` | `f_rval.exe` | `0x14027F480-0x14027F700` | §48 | 坡道合法性 = 2×2 四角签名命中表 `0x1412E3654`（16 行） |
| `FUN_14027EB00` | `f_cls.exe` | `0x14027EB00-0x14027EF50` | §49 | 字母分类器：`A/B`(无坡道位,t=0/1)、`L/R/S`(有坡道位,`27ECE0`=0/1/2)、`H`(有坡道位,t=1)、`!` |
| `FUN_14027EF50` | `f_t.exe` | `0x14027EF50-0x14027F170` | §50/§51 | `t` = 本格是否为 **3×3** 坡道邻域最高层；`rec.flags` **bit11=已算 / bit10=值** |
| `FUN_1402871F0` | `f_tex2b.exe` | `0x1402871F0-0x140287600` | §52.1 | 纹理 id→表索引；**id 0 无效**；表项 `0x28`；索引按首次使用顺序分配 |
| `FUN_140285BC0` | `f_bnd.exe` | `0x140285BC0-0x140285F00` | §52.2 | 边界批量分发（薄壳） |
| `FUN_140285C40` | `f_bnd2.exe` | `0x140285C40-0x140287000` | §53 | **渲染用**边界位图（`map+0x388`，4× 分辨率，bit4=`0x10`）；判定 `flags&0xc0` |
| `FUN_140286F40` | `f_tv.exe` | `0x140286F40-0x140287A00` | §54.1 | **44 字节 thunk**：版本门 `map[0xC0]==0x12` → `jmp 0x140145400`（`edx=0`） |
| `FUN_140145400` | `f_var.exe` | `0x140145400-0x140145E00` | §54.2 | 加权随机选择：项 `{id,weight}`；`forceMin100` 补到 100；**取模用 `total-1`** |

## 关键片段

### `FUN_140145400`（加权随机，§54）
```c
n = list->count;
total = Σ items[i].weight;                   /* items: 8B = {int id, int weight} */
if (forceMin100 && total < 100) total = 100;
if (total - 1 < 1) k = 1;
else k = rand() % (total - 1) + 1;           /* 结果 1..total-1 */
acc = 0;
for (i = 0; i < n; i++) { acc += items[i].weight; if (k <= acc) return items[i].id; }
return 0;
```

### `FUN_140286F40`（thunk，§54.1）
```asm
sub rsp,0x28 ; add rcx,0xC0 ; cmp dword [rcx],0x12 ; jz ok
mov ecx,0x57 ; call err ; xor al,al ; ret
ok: xor edx,edx ; add rsp,0x28 ; jmp 0x140145400
```

### `FUN_14027EB00`（字母分类，§49）
```c
if (rampBit == 0) return (t!=0) + 'A';       /* 'A'=65 / 'B'=66 */
if (t == 0) switch (FUN_14027ece0(x,y)) { case 0:'L'; case 1:'R'; case 2:'S'; default:'!'; }
return 'H';
```

> 完整伪代码可随时用同一切片参数重跑：`open_binary(切片) → procedure_pseudo_code(VA)`。
> 未入库的 4 个函数（`27ECE0` 正文等）见 `ALGORITHM-SPEC.md` §52.3 的遗留清单。

---

## 附件目录说明（更正）

`experiments/artifacts/manual-sessions/` 收录的是**全部手动会话存档**（含 round14 之前几轮的 B0/C0/D*/E0/G0/H1/J*/K*/M*/N*/P0/Q0/R*/S*/T*/V0 等），
共 47 份 `.w3m` 原件 + 提取的 `.w3e`，**不只本轮 10 张**。

本轮（round14）新增的 10 张为：
`W5_deep`、`W8_deep`、`S32_cliff_p1`、`V3d_ramp`、`V3d_ramp2`、`E0_cliff`、`E0b_cliff`、`MIX_water_cliff`、`TEX_same`、`TEX_same2`

---

## 全文反编译已入库（round14 追加）

以下 10 个 `.c` 均为 Ghidra 12.1.4 反编译原文转录（含切片参数与 Evidence ID 于文件头）：

| 文件 | 对应函数 |
|---|---|
| `ghidra_3be230.c` | 唯一角点写入口 |
| `ghidra_263b00.c` | 脏块收集 |
| `ghidra_27f480.c` | 坡道合法性（表 0x1412E3654） |
| `ghidra_27eb00.c` | 字母分类器 |
| `ghidra_27ef50.c` | `t` = 3×3 邻域最高层 + flags 缓存位 |
| `ghidra_2871f0.c` | 纹理 id→索引 |
| `ghidra_285bc0.c` | 边界批量分发 |
| `ghidra_285c40.c` | 渲染用边界位图 |
| `ghidra_140145400.c` | 加权随机选择 |
| `ghidra_286f40.c` | thunk（含全量汇编） |

> 仍缺正文（正文未读，非漏提交）：`FUN_14027ECE0`、收尾链 6 个、`26C340`、`30B120`。
