// Footprint.cs —— 尺寸与形状：笔刷到底覆盖哪些角点（导读 guides/footprint.md）
namespace TerrainReference;

public enum Shape { Circle, Square }

public static class Footprint
{
    // 官方只有 5 档尺寸。R = s − 1 是“半径”（单位：格）。
    // 为什么是 s−1：实测 s2 改 5 个角（十字），只有 R=1、d≤1 才是 5 个点。
    public static readonly int[] Sizes = { 1, 2, 3, 5, 8 };
    public static int Radius(int size) => size - 1;

    /// <summary>
    /// 覆盖集合：圆形取 dx²+dy² ≤ R²，方形取 max(|dx|,|dy|) ≤ R。
    /// 中心永远是鼠标下最近的角点，所以偶数尺寸也不会偏半格。
    /// </summary>
    public static IEnumerable<(int dx, int dy)> Offsets(int size, Shape shape)
    {
        int R = Radius(size);
        for (int dy = -R; dy <= R; dy++)
            for (int dx = -R; dx <= R; dx++)
                if (shape == Shape.Square || dx * dx + dy * dy <= R * R)
                    yield return (dx, dy);
    }

    /// <summary>
    /// “外扩一圈”：把覆盖集合用 3×3 膨胀一次。悬崖贴图、水面高度都写到这里。
    /// 为什么用 3×3 而不是 R+1 的圆：s3 实测 37 个点，正好等于 13 点圆膨胀 3×3 的点数。
    /// </summary>
    public static HashSet<(int, int)> Dilate(IEnumerable<(int dx, int dy)> set)
    {
        var o = new HashSet<(int, int)>();
        foreach (var (dx, dy) in set)
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++) o.Add((dx + i, dy + j));
        return o;
    }

    public static double Dist(int dx, int dy) => Math.Sqrt(dx * dx + dy * dy);
}
