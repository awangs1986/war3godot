// CliffBrushes.cs —— 应用悬崖组：悬崖升降与传播 / 悬崖类型 / 浅水 / 深水 / 坡道
// 导读：guides/cliff.md、cliff-type.md、water.md、ramp.md
namespace TerrainReference;

public static class CliffBrushes
{
    /// <summary>
    /// 悬崖类型：面板按钮是固定的“种类”，写进 w3e 的却是本图悬崖列表的下标。
    /// 例：L 图列表 [CLdi, CLgr]，点“草地悬崖”写 1；A 图列表 [CAgr, CAdi]，点草地写 0。
    /// </summary>
    public static int CliffTypeIndex(W3eMap m, string cliffId) => m.CliffIds.IndexOf(cliffId);

    // ---------------- 悬崖 −2..+2 ----------------
    public const int MaxLayer = 15;   // this+0x988
    static readonly Random VariantRng = new(1);

    /// <summary>
    /// 1.27 源码 0x574470（CTerrainCliff.cpp）：
    /// 1) 按下时 T = L(中心) + Δ（0x574a50 按钮→Δ），存进 this+0x94c，拖动全程用它；
    /// 2) T &lt; 0 或 T &gt; MaxLayer → 整笔什么都不做（是“拒绝”，不是 clamp）；
    /// 3) 覆盖集合（圆：dist²/(s−1)² ≤ 1；方形：全部）里层级或悬崖类型不同的点：写层级 T、类型、随机变体，
    ///    顶点字节 +0xb 置 1（传播时不会再碰这些点）；0x5748c0 把类型+变体写到 3×3 外扩；
    /// 4) 0x573950 → 0x573b40 → 0x573990：层差传播（见 Propagate）。
    /// 官方第 3 步还会按水把每点夹到 [水决定的下限, MaxLayer]（0x5764a0），且夹后的值会改写 T，本实现未模拟。
    /// </summary>
    public static void Cliff(W3eMap m, int cx, int cy, int size, Shape shape, int k, int cliffType)
    {
        int T = m[cx, cy].Layer + k;
        if (T < 0 || T > MaxLayer) return;                         // 整笔拒绝
        var cover = Footprint.Offsets(size, shape).ToList();
        foreach (var (dx, dy) in Footprint.Dilate(cover))
            if (m.Inside(cx + dx, cy + dy)) m[cx + dx, cy + dy].CliffTexture = cliffType;
        var brushed = new HashSet<(int, int)>();
        foreach (var (dx, dy) in cover)
        {
            int x = cx + dx, y = cy + dy;
            if (!m.Inside(x, y)) continue;
            m[x, y].Layer = T; m[x, y].CliffTexture = cliffType;
            m[x, y].CliffVariation = VariantRng.Next(8);
            brushed.Add((x, y));
        }
        Propagate(m, brushed, cliffType);
    }

    /// <summary>
    /// 0x573990：检查点 (x,y) 的 8 个邻居，邻居层级夹到 [L−2, L+2]（再夹到 0..MaxLayer）；
    /// 变了就同时写入**所选悬崖类型和随机变体**，深度字节记为 d+1，然后以邻居为中心递归。
    /// 已经被笔刷写过（字节 1）或在更浅深度处理过的点不再改。
    /// 起点：改动区包围矩形外扩一圈，x 外层、y 内层，每点以深度 1 调用。
    /// 为什么是 8 邻接：连点两次 +2 得到的是方环（切比雪夫距离），不是菱形。
    /// </summary>
    static void Propagate(W3eMap m, HashSet<(int x, int y)> brushed, int cliffType)
    {
        if (brushed.Count == 0) return;
        var depth = brushed.ToDictionary(p => p, _ => 1);
        void Visit(int x, int y, int d)
        {
            int L = m[x, y].Layer;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    int nx = x + i, ny = y + j;
                    if ((i == 0 && j == 0) || !m.Inside(nx, ny)) continue;
                    if (depth.TryGetValue((nx, ny), out int seen) && !(d < seen)) continue;
                    int nL = m[nx, ny].Layer, t = nL;
                    if (t > L + 2) t = L + 2;
                    if (t < L - 2) t = L - 2;
                    t = Math.Clamp(t, 0, MaxLayer);
                    if (t == nL) continue;
                    m[nx, ny].Layer = t;
                    if (cliffType != -1) m[nx, ny].CliffTexture = cliffType;   // 传播点也染悬崖类型
                    m[nx, ny].CliffVariation = VariantRng.Next(8);
                    depth[(nx, ny)] = d + 1;
                    Visit(nx, ny, d + 1);
                }
        }
        int x0 = brushed.Min(p => p.x) - 1, x1 = brushed.Max(p => p.x) + 1, y0 = brushed.Min(p => p.y) - 1, y1 = brushed.Max(p => p.y) + 1;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (m.Inside(x, y)) Visit(x, y, 1);
    }

    // ---------------- 浅水 / 深水 ----------------
    /// <summary>
    /// 读中心原层级 L0、原高度 h0，然后：
    ///  - 覆盖集合（d ≤ R）：层级 L0 − depth（浅水 1，深水 2），设 water 标志；
    ///  - 外扩一圈：waterHeight = h0 + (L0 − 2)·512，悬崖贴图 = 当前悬崖类型；
    ///  - d ≤ R+2：groundHeight = h0（先把岸边整平，水深才只取决于降了几层）。
    /// </summary>
    public static void Water(W3eMap m, int cx, int cy, int size, int depth, int cliffType)
    {
        int L0 = m[cx, cy].Layer, h0 = m[cx, cy].Height, R = Footprint.Radius(size);
        var cover = Footprint.Offsets(size, Shape.Circle).ToList();
        for (int dy = -(R + 2); dy <= R + 2; dy++)
            for (int dx = -(R + 2); dx <= R + 2; dx++)
                if (dx * dx + dy * dy <= (R + 2) * (R + 2) && m.Inside(cx + dx, cy + dy))
                    m[cx + dx, cy + dy].Height = h0;
        foreach (var (dx, dy) in Footprint.Dilate(cover))
            if (m.Inside(cx + dx, cy + dy))
            {
                m[cx + dx, cy + dy].WaterHeight = h0 + (L0 - 2) * 512;
                m[cx + dx, cy + dy].CliffTexture = cliffType;
            }
        foreach (var (dx, dy) in cover)
            if (m.Inside(cx + dx, cy + dy))
            {
                m[cx + dx, cy + dy].Layer = Math.Max(0, L0 - depth);
                m[cx + dx, cy + dy].Water = true;
            }
    }

    // ---------------- 坡道 ----------------
    /// <summary>
    /// 只在点击处（低地角点 c）紧挨着“高 1 层”的悬崖边时生效：
    ///  - 正好一个 4 邻居高 1 层 → 沿垂直悬崖边的方向写 3 个角：c 外侧一个、c、高侧一个；
    ///  - 没有 4 邻居更高、但有一个斜向邻居高 1 层（台地凸角）→ 写以 c 为中心的 3×3；
    ///  - 层差为 2 或点在高处 → 什么也不做（实测 58 个位置全部无变化）。
    /// 层级不变，只写 ramp 标志；坡道模型宽度由渲染端推断。
    /// </summary>
    public static void Ramp(W3eMap m, int cx, int cy)
    {
        int L = m[cx, cy].Layer;
        var dirs4 = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        var high4 = dirs4.Where(d => m.Inside(cx + d.Item1, cy + d.Item2) && m[cx + d.Item1, cy + d.Item2].Layer == L + 1).ToList();
        bool anyHigher4 = dirs4.Any(d => m.Inside(cx + d.Item1, cy + d.Item2) && m[cx + d.Item1, cy + d.Item2].Layer > L);
        if (high4.Count == 1)
        {
            var (dx, dy) = high4[0];
            foreach (int t in new[] { -1, 0, 1 })
                if (m.Inside(cx + t * dx, cy + t * dy)) m[cx + t * dx, cy + t * dy].Ramp = true;
            return;
        }
        if (anyHigher4) return;
        var diag = new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) }
            .Where(d => m.Inside(cx + d.Item1, cy + d.Item2) && m[cx + d.Item1, cy + d.Item2].Layer == L + 1).ToList();
        if (diag.Count == 1)
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    if (m.Inside(cx + i, cy + j)) m[cx + i, cy + j].Ramp = true;
    }
}
