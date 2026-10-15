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

    // ---------------- 整平 ----------------
    /// <summary>
    /// 读按下那一刻中心角点的高度 h0，把 d ≤ R 内全部设为 h0，没有衰减。
    /// 注意：官方在相机一侧边缘还有一圈“半施加”（12.3），公式未定，这里故意不模拟。
    /// </summary>
    public static void Plateau(W3eMap m, int cx, int cy, int size)
    {
        int h0 = m[cx, cy].Height;
        foreach (var (dx, dy) in Footprint.Offsets(size, Shape.Circle))
            if (m.Inside(cx + dx, cy + dy)) m[cx + dx, cy + dy].Height = h0;
    }

    // ---------------- 噪声 ----------------
    /// <summary>
    /// d ≤ R 内每个角点独立加 k·0.05 层，k 是 −10..+10 的均匀整数，没有衰减。
    /// 随机数无法和官方逐点对上，所以对照测试只检查“改动范围”和“幅度上限”。
    /// </summary>
    public static void Noise(W3eMap m, int cx, int cy, int size, Random rng)
    {
        foreach (var (dx, dy) in Footprint.Offsets(size, Shape.Circle))
        {
            int k = rng.Next(-10, 11);
            if (m.Inside(cx + dx, cy + dy))
                m[cx + dx, cy + dy].Height += (int)Math.Floor(k * 0.05 * RawPerLayer);
        }
    }

    // ---------------- 平滑 ----------------
    /// <summary>
    /// 普通：h' = floor(0.8·h + 0.2·avg9)；Shift：h' = floor(1.0·h + 0.05·avg9)。
    /// avg9 是“自己 + 8 个邻居”共 9 点的平均（第四轮拟合更正：用 8 邻平均只有 50–65% 对得上，
    /// 用 9 点平均 + 向下取整，Shift 版 49/49 全对，普通版 92%，剩下的都只差 1 raw）。
    /// 为什么要“快照”：平均值必须取施加前的值；改用实时值匹配率掉到 50% 左右。
    /// 高度以 8192 为零点计算，否则 0.2·8192 会把整块地抬上天。
    /// </summary>
    public static void Smooth(W3eMap m, int cx, int cy, int size, bool shift)
    {
        var snap = m.Corners.Select(c => c.Height - 8192).ToArray();
        int H(int x, int y) => snap[y * m.Width + x];
        double wSelf = shift ? 1.0 : 0.8, wAvg = shift ? 0.05 : 0.2;
        foreach (var (dx, dy) in Footprint.Offsets(size, Shape.Circle))
        {
            int x = cx + dx, y = cy + dy;
            if (!m.Inside(x, y)) continue;
            double sum = 0; int n = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    if (m.Inside(x + i, y + j)) { sum += H(x + i, y + j); n++; }
            m[x, y].Height = 8192 + (int)Math.Floor(wSelf * H(x, y) + wAvg * (sum / n));
        }
    }
}
