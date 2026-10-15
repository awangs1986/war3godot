# 对照结果

## 第五轮（按 1.27 源码修正）前后对比

| 算法 | 第四轮（黑盒拟合） | 第五轮（1.27 源码） | 改了什么 |
|---|---|---|---|
| 整平 | 91 / 93（97.8%） | 93 / 93（100%） | 加入 MaxSlope=50° 坡度钳制（610.2 raw，递归，包围矩形 x 外 y 内扫描）；遵守形状 |
| 平滑 | 106 / 111（95.5%） | 111 / 111（100%） | 9 点平均 → 1-2-1 核 /12（中心权 0），float32 + trunc；遵守形状 |
| 噪声（范围） | 47 / 47 | 47 / 47 | k = rand%21 − 10，遵守形状，写后钳制 |
| 悬崖（单次） | 150 / 150 | 150 / 150 | 目标越界整笔拒绝；传播写悬崖类型 + 随机变体 |
| 悬崖（连点） | 77 / 81（95.1%） | 77 / 81（95.1%） | 剩余 4 点是实验时光标偏移（见 guides/cliff.md） |
| 其他 10 项 | 100% | 100% | 未改 |

全部 39 个用例：第四轮 1579 / 1590（99.3%）→ 第五轮 1586 / 1590（99.7%）。

## 第五轮逐用例结果

| 算法 | 前 → 后 | 对照字段 | 匹配 / 相关角点 | 匹配率 |
|---|---|---|---|---|
| raise | base → raise_s1_c_h08 | h | 1 / 1 | 100.0% |
| raise | base → raise_s2_c_h08 | h | 1 / 1 | 100.0% |
| raise | base → raise_s3_c_h08 | h | 9 / 9 | 100.0% |
| raise | base → raise_s5_c_h08 | h | 45 / 45 | 100.0% |
| raise | B3 → raise_s8_c_h08 | h | 145 / 145 | 100.0% |
| lower | base → lower_s3_c_h08 | h | 9 / 9 | 100.0% |
| lower | base → raise_s3_c_shift | h | 9 / 9 | 100.0% |
| plateau | A_pl_base → A_pl_still | h | 45 / 45 | 100.0% |
| plateau | P2_base2 → P2_pl_0_60_0 | h | 48 / 48 | 100.0% |
| noise | base → noise_s5 | h(范围) | 47 / 47 | 100.0% |
| smooth | base2 → b2_smooth_s3 | h | 13 / 13 | 100.0% |
| smooth | base2 → b2_smooth_s5 | h | 49 / 49 | 100.0% |
| smooth | base2 → b2_smooth_s5_shift | h | 49 / 49 | 100.0% |
| cliff | base → cliff_c+1_s3 | L,ct | 50 / 50 | 100.0% |
| cliff | base → cliff_c+2_s3 | L,ct | 50 / 50 | 100.0% |
| cliff | base → cliff_c-1_s3 | L,ct | 50 / 50 | 100.0% |
| cliff_repeat | base → P_c+2_s1_x3 | L | 9 / 9 | 100.0% |
| cliff_repeat | base → P_c+2_s3_x3 | L | 68 / 72 | 94.4% |
| cliff_type | WA_base → WA_c+1_ctype0 | ct | 37 / 37 | 100.0% |
| cliff_type | WA_base → WA_c+1_ctype1 | ct | 37 / 37 | 100.0% |
| cliff_type | WL_base → WL_c+1_ctype0 | ct | 37 / 37 | 100.0% |
| cliff_type | WN_base → WN_c+1_ctype0 | ct | 37 / 37 | 100.0% |
| ramp | R1_base → R1_0_300 | ramp | 3 / 3 | 100.0% |
| ramp | R1_base → R1_-400_300 | ramp | 9 / 9 | 100.0% |
| ramp | R3_base → R3_370_0 | ramp | 3 / 3 | 100.0% |
| ramp | NP2_base → NP2_0_-350 | ramp | 3 / 3 | 100.0% |
| ramp | R2_base → R2_0_300 | ramp | 1 / 1 | 100.0% |
| shallow | base → water_shallow_s3 | L,water,w,h,ct | 63 / 63 | 100.0% |
| deep | base → water_deep_s3 | L,water,w,h,ct | 63 / 63 | 100.0% |
| shallow | A_raise3 → A_raise3_shallow | L,water,w,h,ct | 148 / 148 | 100.0% |
| deep | A_raise3 → A_raise3_deep | L,water,w,h,ct | 148 / 148 | 100.0% |
| texture | base → T_tex1_s3 | tex | 13 / 13 | 100.0% |
| texture | base → T_tex1_s3_q | tex | 25 / 25 | 100.0% |
| texture | base → T_tex2_s3 | tex | 13 / 13 | 100.0% |
| blight | base → T_blight_s3 | blight | 13 / 13 | 100.0% |
| boundary | base → T_bound_add_s3 | bound | 13 / 13 | 100.0% |
| footprint | base → cliff_c+1_s5 | L | 49 / 49 | 100.0% |
| footprint | base → cliff_c+1_s8_c | L | 149 / 149 | 100.0% |
| footprint | base → T_tex1_s3_q | tex | 25 / 25 | 100.0% |

### 按算法汇总

| 算法 | 匹配 / 相关角点 | 匹配率 |
|---|---|---|
| raise | 201 / 201 | 100.0% |
| lower | 18 / 18 | 100.0% |
| plateau | 93 / 93 | 100.0% |
| noise | 47 / 47 | 100.0% |
| smooth | 111 / 111 | 100.0% |
| cliff | 150 / 150 | 100.0% |
| cliff_repeat | 77 / 81 | 95.1% |
| cliff_type | 148 / 148 | 100.0% |
| ramp | 19 / 19 | 100.0% |
| shallow | 211 / 211 | 100.0% |
| deep | 211 / 211 | 100.0% |
| texture | 51 / 51 | 100.0% |
| blight | 13 / 13 | 100.0% |
| boundary | 13 / 13 | 100.0% |
| footprint | 223 / 223 | 100.0% |

### 渲染端子系统自检（官方存档里没有标准答案）

- 高度插值：在 `raise_s5_c_h08` 的山丘上，同一格中心“双线性”与“三角”最大相差 1.38 世界单位。这就是悬崖顶点和地面用不同插值时的裂缝大小。
- 地表过渡：`MX_quad` 格 (33,30) 的层 = tex1:mask0001 tex2:mask0010 tex3:mask0100 tex4:mask1000。4 种贴图各占一个角（掩码 0001/0010/0100/1000），按下标从小到大叠放，与截图 `MX_quad_zoom.jpg` 一致。
- 海岸：`LS04_deep`（L 图，水偏移 −0.7）里全水格 34 个、岸边格 38 个。岸边格就是需要按水深做透明渐变的格子。
