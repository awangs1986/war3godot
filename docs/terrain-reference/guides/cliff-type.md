# 悬崖类型

> 参考实现：`src/CliffBrushes.cs:11（CliffTypeIndex）`　对照结果：148/148（100%），覆盖 L、A、N 三个 tileset（完整表格见 `../results.md`）

## 原理
w3e 文件头有本图的悬崖类型列表，角点的 cliffTexture 是这个列表的**下标**。面板按钮的顺序是固定的“种类”，写入时要按 ID 查表。

## 一步步的数值推演
L 图列表 [CLdi, CLgr]，点第 1 个按钮（草地 CLgr）→ 写 1；A 图列表 [CAgr, CAdi]，点草地 CAgr → 写 0；N 图 [CNdi, CNsn]，第 1 个按钮 CNsn → 写 1。

## 对应代码
`src/CliffBrushes.cs:11（CliffTypeIndex）`。代码里每一段都有中文注释说明“为什么这样做”。

## 常见误区（对照你现在的写法）
- 直接把按钮序号当下标写进去，换 tileset 就错。
- 悬崖类型只写在“外扩一圈”，不随传播扩散。

## 练习题
1. B 图列表 [CBde, CBgr]，点第 1 个按钮会写几？
2. 为什么 cliffTexture = 15 表示“未设置”？
