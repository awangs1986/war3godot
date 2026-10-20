// HeightPipeline.cs —— 所有高度笔刷共用的一条流水线（1.27 源码 0x57e700 的骨架）
// 导读：guides/plateau.md（坡度钳制）、guides/smooth.md
//
//   ① 计算：对覆盖集合里每个点算出新的世界高度（float32），先全部放进缓冲；
//   ② 量化：raw = trunc((world + 2048)·4)，全部写回；
//   ③ 钳制：SlopeClamp —— MaxSlope 坡度 + MaxHeight/MinHeight 绝对上下限，递归；
//   ④ 记录：返回“实际被改动的角点”（含被钳制波及的圈外点），供撤销 / 重建网格用。
// 升高、降低、整平、噪声、平滑都只是①的公式不同；浅水/深水先写水，再调用这里的整平，再调用悬崖。
namespace TerrainReference;

public static class HeightPipeline
{
    // 编辑器内部：world = raw/4 − 2048（raw 8192 → 0）。float32 是官方精度，换成 double 会在 x.0 边界差 1。
    public static float World(int raw) => raw * 0.25f - 2048f;
    public static int Raw(float world) => (int)((world + 2048f) * 4f);   // (int) 向 0 截断，同 ftol

    public static float MaxSlopeDeg = 50f, MaxHeight = 1536f, MinHeight = -128f;   // MiscData

    /// <summary>①②③④。compute 返回 null 表示该点不写（例如地图外）。</summary>
    public static HashSet<(int x, int y)> Apply(W3eMap m, IEnumerable<(int x, int y)> cover, Func<int, int, float?> compute)
    {
        var before = new Dictionary<(int, int), int>();
        var buf = new List<(int x, int y, int raw)>();
        foreach (var (x, y) in cover)                       // ① 先算完（=用施加前的值）
        {
            if (!m.Inside(x, y)) continue;
            float? w = compute(x, y);
            if (w is null) continue;
            buf.Add((x, y, Raw(w.Value)));                  // ② 量化
        }
        // 记录前值：覆盖区 + 钳制可能波及的范围（钳制会逐级向外扩，这里按需懒记录）
        int Snap(int x, int y) { if (!before.ContainsKey((x, y))) before[(x, y)] = m[x, y].Height; return before[(x, y)]; }
        foreach (var (x, y, raw) in buf) { Snap(x, y); m[x, y].Height = raw; }
        SlopeClamp(m, buf.Select(b => (b.x, b.y)), Snap);   // ③
        return before.Where(kv => m[kv.Key.Item1, kv.Key.Item2].Height != kv.Value)   // ④
                     .Select(kv => kv.Key).ToHashSet();
    }

    /// <summary>
    /// 1.27 源码 0x57e3e0 → 0x57e600 → 0x57e440 / 0x57f8f0。
    ///   区间 = [MinHeight − off, MaxHeight − off] ∩ ⋂邻居 [h邻 − S, h邻 + S]，off = (L−2)·128，下限再取 min(·,0)；
    ///   S = tan(MaxSlope)·128（正交，50° → 152.55 world = 610.2 raw），对角 tan·181.02。MaxSlope ≥ 85 时不做坡度部分。
    /// 扫描：改动区包围矩形外扩一圈，x 外层、y 内层；越界就夹回并递归 8 邻居（深度防重入，同顶点字节 +0xb）。
    /// 未模拟：水相关下限（0x51dcb0 / 0x605fc0 / 0x605020）——【仅 1.27 源码，未实现】。
    /// </summary>
    public static void SlopeClamp(W3eMap m, IEnumerable<(int x, int y)> seeds, Func<int, int, int>? beforeWrite = null)
    {
        float tan = (float)Math.Tan(MaxSlopeDeg * Math.PI / 180.0);
        var depth = new Dictionary<(int, int), int>();
        void Visit(int x, int y, int d)
        {
            if (!m.Inside(x, y)) return;
            if (depth.TryGetValue((x, y), out int seen) && seen <= d) return;
            var c = m[x, y];
            float off = (c.Layer - 2) * 128f;
            float hi = MaxHeight - off, lo = Math.Min(MinHeight - off, 0f);
            if (MaxSlopeDeg < 85f)
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        if ((i == 0 && j == 0) || !m.Inside(x + i, y + j)) continue;
                        float s = tan * (i != 0 && j != 0 ? 181.02f : 128f);
                        float hn = World(m[x + i, y + j].Height);
                        hi = Math.Min(hi, hn + s); lo = Math.Max(lo, hn - s);
                    }
            if (hi < lo) (lo, hi) = (hi, lo);
            float w = World(c.Height);
            if (w >= lo && w <= hi) return;
            beforeWrite?.Invoke(x, y);
            c.Height = Raw(w < lo ? lo : hi);
            depth[(x, y)] = d + 1;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    if (i != 0 || j != 0) Visit(x + i, y + j, d + 1);
        }
        var list = seeds.ToList(); if (list.Count == 0) return;
        int x0 = list.Min(p => p.x) - 1, x1 = list.Max(p => p.x) + 1, y0 = list.Min(p => p.y) - 1, y1 = list.Max(p => p.y) + 1;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++) Visit(x, y, 0);
    }
}
