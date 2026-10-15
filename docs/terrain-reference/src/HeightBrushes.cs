// HeightBrushes.cs —— 应用高度组：升高 / 降低 / 整平 / 噪声 / 平滑
// 导读：guides/raise-lower.md、plateau.md、noise.md、smooth.md
namespace TerrainReference;

public static class HeightBrushes
{
    const double RawPerLayer = 512.0;

    // ---------------- 升高 / 降低 ----------------
    /// <summary>
    /// 每次按下只施加一次（不乘帧时间）。中心增量 0.05·s 层，按余弦衰减到 d = R 为 0。
    /// 为什么是余弦：s5、s8 的每个角点对 cos(πd/2R) 拟合误差都 ≤ 1 raw，线性衰减对不上。
    /// 为什么向下取整：0.05×3×512 = 76.8，实测 76；0.05×1×512 = 25.6，实测 25。
    /// </summary>
    public static void RaiseLower(W3eMap m, int cx, int cy, int size, int sign)
    {
        int R = Footprint.Radius(size);
        foreach (var (dx, dy) in Footprint.Offsets(size, Shape.Circle)) // 方形对高度类无效
        {
            double d = Footprint.Dist(dx, dy);
            if (R > 0 && d >= R) continue;           // d = R 处衰减为 0，所以实际是 d < R
            double falloff = R == 0 ? 1.0 : Math.Cos(Math.PI * d / (2 * R));
            // 向 −∞ 取整：升高 76.8→76，降低 −76.8→−77（实测降低 s3 中心正是 −77）
            int delta = (int)Math.Floor(sign * 0.05 * size * RawPerLayer * falloff);
            int x = cx + dx, y = cy + dy;
            if (m.Inside(x, y)) m[x, y].Height += delta;
        }
    }

    // ---------------- 高度的内部表示（1.27 源码 0x57e700） ----------------
    // 编辑器在 float32 世界单位里算：world = raw/4 − 2048（raw 8192 → 0），写回 raw = trunc((world+2048)·4)。
    static float World(int raw) => raw * 0.25f - 2048f;
    static int Raw(float world) => (int)((world + 2048f) * 4f);   // C# 的 (int) 就是向 0 截断，同 ftol

    // ---------------- 整平 ----------------
    /// <summary>
    /// 1.27 源码：按下时把中心高度存进 this+0x948（h0），覆盖集合内全部写 h0，没有衰减；遵守形状（方形可用）。
    /// 写完后统一做一次“坡度钳制”（SlopeClamp），整平边缘那一圈 h0−611 就是它产生的，不是笔刷本身的衰减。
    /// </summary>
    public static void Plateau(W3eMap m, int cx, int cy, int size, Shape shape = Shape.Circle, int? h0Captured = null)
    {
        int h0 = h0Captured ?? m[cx, cy].Height;   // 拖动时传入按下那一刻的 h0
        var touched = new List<(int, int)>();
        foreach (var (dx, dy) in Footprint.Offsets(size, shape))   // 官方循环顺序：x 外层、y 内层
            if (m.Inside(cx + dx, cy + dy)) { m[cx + dx, cy + dy].Height = h0; touched.Add((cx + dx, cy + dy)); }
        SlopeClamp(m, touched);
    }

    // ---------------- 噪声 ----------------
    /// <summary>
    /// 1.27 源码 0x581460：n = int(128/6.4)+1 = 21，Δ = (rand%21 − 10)·6.4 world = k·25.6 raw，k ∈ −10..10，没有衰减。
    /// 遵守形状。随机数无法和官方逐点对上，对照测试只检查“改动范围”和“幅度上限”。
    /// </summary>
    public static void Noise(W3eMap m, int cx, int cy, int size, Random rng, Shape shape = Shape.Circle)
    {
        var touched = new List<(int, int)>();
        foreach (var (dx, dy) in Footprint.Offsets(size, shape))
        {
            int k = rng.Next(21) - 10;
            int x = cx + dx, y = cy + dy;
            if (!m.Inside(x, y)) continue;
            m[x, y].Height = Raw(World(m[x, y].Height) + k * 6.4f);
            touched.Add((x, y));
        }
        SlopeClamp(m, touched);
    }

    // ---------------- 平滑 ----------------
    /// <summary>
    /// 1.27 源码（0x57e700 op 4）：先把整个覆盖区的新值算进缓冲（this+0x1c4，相当于“快照”），再统一写回。
    ///   new = trunc( W1·h + W2·Σ(k·h邻)/Σk )，核 k = [1,2,1; 2,0,2; 1,2,1]（中心权 0！）。
    ///   普通 W1=0.80 W2=0.20；Shift W1=1.00 W2=0.05（WorldEditData.txt 的 SmoothBrushWeights*）。
    /// 地图边缘缺的邻居不参与，分母 Σk 只累加实际存在的权重。全程 float32。
    /// 以前的“9 点平均”只是近似：中心权 0 + 正交 2 / 对角 1 才是原式。
    /// </summary>
    static readonly float[] Kernel = { 1, 2, 1, 2, 0, 2, 1, 2, 1 };
    public static void Smooth(W3eMap m, int cx, int cy, int size, bool shift, Shape shape = Shape.Circle)
    {
        float w1 = shift ? 1.00f : 0.80f, w2 = shift ? 0.05f : 0.20f;
        var buf = new List<(int x, int y, int raw)>();
        foreach (var (dx, dy) in Footprint.Offsets(size, shape))
        {
            int x = cx + dx, y = cy + dy;
            if (!m.Inside(x, y)) continue;
            float sum = 0, wsum = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    if (!m.Inside(x + i, y + j)) continue;
                    float k = Kernel[(j + 1) * 3 + (i + 1)];
                    wsum += k; sum += World(m[x + i, y + j].Height) * k;
                }
            buf.Add((x, y, Raw(sum * w2 / wsum + World(m[x, y].Height) * w1)));
        }
        foreach (var (x, y, raw) in buf) m[x, y].Height = raw;   // 全部算完再写 = 用的是施加前的值
        SlopeClamp(m, buf.Select(b => (b.x, b.y)));
    }

    // ---------------- 坡度 / 高度钳制（1.27 源码 0x57e3e0 → 0x57e600 → 0x57e440 / 0x57f8f0） ----------------
    /// <summary>
    /// 每次高度笔刷写回后，对每个改动点求允许区间，越界就夹回，并递归复查它的 8 个邻居：
    ///   区间 = [MinHeight − off, MaxHeight − off] ∩ ⋂邻居 [h邻 − S, h邻 + S]
    ///   off = (L−2)·128（层级换算成世界高度），MaxHeight=1536、MinHeight=−128（MiscData），下限再取 min(·,0)；
    ///   S = tan(MaxSlope)·128（正交，50° → 152.55 world = 610.2 raw），对角 S = tan·181.02。
    /// 只在 MaxSlope &lt; 85 时启用坡度部分。顶点字节 +0xb 记录递归深度防止重入。
    /// 官方另有一段“水下限”判断（0x51dcb0/0x605fc0），本参考实现未模拟。
    /// 例：整平到 9111 后圈外点 8223 低于 9111−610.2，被抬到 trunc(…)=8500。
    /// </summary>
    public static void SlopeClamp(W3eMap m, IEnumerable<(int x, int y)> seeds,
        float maxSlopeDeg = 50f, float maxHeight = 1536f, float minHeight = -128f)
    {
        float tan = (float)Math.Tan(maxSlopeDeg * Math.PI / 180.0);
        var depth = new Dictionary<(int, int), int>();
        void Visit(int x, int y, int d)
        {
            if (!m.Inside(x, y)) return;
            if (depth.TryGetValue((x, y), out int seen) && seen <= d) return;
            var c = m[x, y];
            float off = (c.Layer - 2) * 128f;
            float hi = maxHeight - off, lo = Math.Min(minHeight - off, 0f);
            if (maxSlopeDeg < 85f)
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
            c.Height = Raw(w < lo ? lo : hi);
            depth[(x, y)] = d + 1;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    if (i != 0 || j != 0) Visit(x + i, y + j, d + 1);
        }
        // 0x57e3e0 → 0x57e600：取改动区的包围矩形（0x56f720，比改动点外扩一圈），x 外层、y 内层逐点检查。
        // 所以圈外的邻居会先被“抬”上来（8415→8500），轮到圈内边缘点时它只需再让 1 raw（9111→9110）。
        var list = seeds.ToList(); if (list.Count == 0) return;
        int x0 = list.Min(p => p.x) - 1, x1 = list.Max(p => p.x) + 1, y0 = list.Min(p => p.y) - 1, y1 = list.Max(p => p.y) + 1;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++) Visit(x, y, 0);
    }
}
