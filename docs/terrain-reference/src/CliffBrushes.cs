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
    /// <summary>
    /// 1) 目标层 T = clamp(L(中心)+k, 0, 15)，覆盖集合内层级 = T；
    /// 2) 悬崖贴图写到覆盖集合“外扩一圈”；
    /// 3) 传播：任意两个 8 邻接角点层差不能 > 2，超了就把外侧拉到“内侧 ∓ 2”，继续 BFS。
    /// 为什么是 8 邻接：连点两次 +2 得到的是方环（切比雪夫距离），不是菱形。
    /// </summary>
    public static void Cliff(W3eMap m, int cx, int cy, int size, Shape shape, int k, int cliffType)
    {
        int T = Math.Clamp(m[cx, cy].Layer + k, 0, 15);
        var cover = Footprint.Offsets(size, shape).ToList();
        foreach (var (dx, dy) in Footprint.Dilate(cover))
            if (m.Inside(cx + dx, cy + dy)) m[cx + dx, cy + dy].CliffTexture = cliffType;
        var queue = new Queue<(int, int)>();
        foreach (var (dx, dy) in cover)
        {
            int x = cx + dx, y = cy + dy;
            if (!m.Inside(x, y)) continue;
            m[x, y].Layer = T; queue.Enqueue((x, y));
        }
        Propagate(m, queue);
    }

    static void Propagate(W3eMap m, Queue<(int x, int y)> queue)
    {
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            int L = m[x, y].Layer;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int nx = x + i, ny = y + j;
                    if ((i == 0 && j == 0) || !m.Inside(nx, ny)) continue;
                    int nL = m[nx, ny].Layer;
                    if (nL > L + 2) { m[nx, ny].Layer = L + 2; queue.Enqueue((nx, ny)); }
                    else if (nL < L - 2) { m[nx, ny].Layer = L - 2; queue.Enqueue((nx, ny)); }
                }
        }
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
