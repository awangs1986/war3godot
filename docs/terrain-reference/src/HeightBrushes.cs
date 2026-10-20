// HeightBrushes.cs —— 应用高度组：升高 / 降低 / 整平 / 噪声 / 平滑
// 每个笔刷只负责“①计算新高度”这一步，量化、钳制、记录改动区都交给 HeightPipeline。
// 导读：guides/raise-lower.md、plateau.md、noise.md、smooth.md
using static TerrainReference.HeightPipeline;
namespace TerrainReference;

public static class HeightBrushes
{
    static IEnumerable<(int x, int y)> Cover(int cx, int cy, int size, Shape shape) =>
        Footprint.Offsets(size, shape).Select(o => (cx + o.dx, cy + o.dy));

    // ---------------- 升高 / 降低 ----------------
    /// <summary>
    /// Δworld = s·6.4·cos(π/2·d/R)（6.4 world = 0.05 层），只取 d ≤ R；降低 / Shift 取负。每次按下只施加一次。
    /// 形状：1.27 源码里升高/降低强制圆形（0x57e700 的 bVar22）。
    /// 量化后：+76.8 → +76，−76.8 → −77（trunc 作用在 world+2048 上，等价于对增量向下取整）。
    /// </summary>
    public static HashSet<(int, int)> RaiseLower(W3eMap m, int cx, int cy, int size, int sign)
    {
        int R = Footprint.Radius(size);
        return Apply(m, Cover(cx, cy, size, Shape.Circle), (x, y) =>
        {
            double d = Footprint.Dist(x - cx, y - cy);
            if (R > 0 && d >= R) return null;           // d = R 处 cos = 0，不改
            float f = R == 0 ? 1f : (float)Math.Cos(Math.PI * d / (2 * R));
            return World(m[x, y].Height) + sign * size * 6.4f * f;
        });
    }

    // ---------------- 整平 ----------------
    /// <summary>
    /// 按下时捕获 h0（this+0x948），覆盖集合内全部写 h0，没有衰减；遵守形状。
    /// 边缘那一圈 h0−611 是流水线③坡度钳制的结果，不是笔刷本身。
    /// h0Captured：拖动时传入按下那一刻的值；浅水/深水调用时传入点击点地面高度。
    /// </summary>
    public static HashSet<(int, int)> Plateau(W3eMap m, int cx, int cy, int size, Shape shape = Shape.Circle, int? h0Captured = null)
    {
        float h0 = World(h0Captured ?? m[cx, cy].Height);
        return Apply(m, Cover(cx, cy, size, shape), (x, y) => h0);
    }

    // ---------------- 噪声 ----------------
    /// <summary>
    /// 0x581460：n = int(128/6.4)+1 = 21，Δ = (rand%21 − 10)·6.4 world，没有衰减；遵守形状。
    /// 官方随机序列无法复现，所以对照测试比较“范围 + 量化格点 + 分布”，见 Program.cs。
    /// </summary>
    public static HashSet<(int, int)> Noise(W3eMap m, int cx, int cy, int size, Random rng, Shape shape = Shape.Circle) =>
        Apply(m, Cover(cx, cy, size, shape), (x, y) => World(m[x, y].Height) + (rng.Next(21) - 10) * 6.4f);

    // ---------------- 平滑 ----------------
    /// <summary>
    /// 0x57e700 op 4：new = W1·h + W2·Σ(k·h邻)/Σk，核 k = [1,2,1; 2,0,2; 1,2,1]（中心 0），边缘缺的邻居不计入 Σk。
    /// 普通 W1=0.80 W2=0.20；Shift W1=1.00 W2=0.05。流水线①先算完再写，所以用的是施加前的值。遵守形状。
    /// </summary>
    static readonly float[] Kernel = { 1, 2, 1, 2, 0, 2, 1, 2, 1 };
    public static HashSet<(int, int)> Smooth(W3eMap m, int cx, int cy, int size, bool shift, Shape shape = Shape.Circle)
    {
        float w1 = shift ? 1.00f : 0.80f, w2 = shift ? 0.05f : 0.20f;
        return Apply(m, Cover(cx, cy, size, shape), (x, y) =>
        {
            float sum = 0, wsum = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    if (!m.Inside(x + i, y + j)) continue;
                    float k = Kernel[(j + 1) * 3 + (i + 1)];
                    wsum += k; sum += World(m[x + i, y + j].Height) * k;
                }
            return sum * w2 / wsum + World(m[x, y].Height) * w1;
        });
    }
}
