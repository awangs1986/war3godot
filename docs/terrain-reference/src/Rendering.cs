// Rendering.cs —— 渲染端子系统：高度插值 / 地表过渡接缝 / 海岸
// 这些不写进 w3e，官方编辑器存档里没有“标准答案”，所以对照测试做的是结构性自检。
// 导读：guides/height-interpolation.md、ground-transition.md、shore.md
namespace TerrainReference;

public static class Rendering
{
    // ---------------- 高度插值 ----------------
    /// <summary>角点的世界高度：地面原始高度 + 层级。单位：世界坐标（1 层 = 128）。</summary>
    public static double GroundZ(Corner c) => (c.Height - 8192) / 4.0 + (c.Layer - 2) * 128.0;

    /// <summary>
    /// 一个格子内任意点 (u,v∈[0,1]) 的高度。地面和悬崖必须用“同一种”插值，
    /// 否则悬崖底边与地面会裂缝（你的仓库：悬崖双线性、地面三角，见 12 章）。
    /// 官方格子是两个三角形，这里给出两种写法供对比。
    /// </summary>
    public static double Bilinear(double z00, double z10, double z01, double z11, double u, double v)
        => z00 * (1 - u) * (1 - v) + z10 * u * (1 - v) + z01 * (1 - u) * v + z11 * u * v;

    public static double Triangle(double z00, double z10, double z01, double z11, double u, double v)
        => u + v <= 1 ? z00 + (z10 - z00) * u + (z01 - z00) * v      // 左下三角
                      : z11 + (z01 - z11) * (1 - u) + (z10 - z11) * (1 - v); // 右上三角

    // ---------------- 地表过渡 ----------------
    /// <summary>
    /// 一个渲染格（以 4 个角点为四角）的贴图层：按贴图下标从小到大，每层一个 4 位掩码
    /// （bit0=左下, bit1=右下, bit2=左上, bit3=右上）。悬崖格的 4 个角一律视为悬崖类型对应的贴图，
    /// 并且这个“替换”也参与邻格的掩码计算，所以悬崖草地会向外溢出半格。
    /// </summary>
    public static List<(int tex, int mask)> GroundLayers(W3eMap m, int x, int y, Func<int, int>? cliffTex = null)
    {
        var cs = new[] { m[x, y], m[x + 1, y], m[x, y + 1], m[x + 1, y + 1] };
        bool isCliffCell = cs.Select(c => c.Layer).Distinct().Count() > 1;
        int TexOf(Corner c) => isCliffCell && cliffTex != null ? cliffTex(c.CliffTexture) : c.Texture;
        var layers = new List<(int, int)>();
        foreach (int t in cs.Select(TexOf).Distinct().OrderBy(t => t))
        {
            int mask = 0;
            for (int i = 0; i < 4; i++) if (TexOf(cs[i]) == t) mask |= 1 << i;
            layers.Add((t, mask));
        }
        return layers;
    }

    // ---------------- 海岸 ----------------
    /// <summary>
    /// 水面世界高度 = (waterHeight − 8192)/4 + 水偏移·128，水偏移来自 Water.slk（L −0.7、A −0.6 …）。
    /// 水深 = 水面 − 地面；岸边角点的水深 ≈ 0，渲染时透明度从 0 开始渐变，所以看不到硬边。
    /// </summary>
    public static double WaterZ(Corner c, double waterOffsetLayers) => (c.WaterHeight - 8192) / 4.0 + waterOffsetLayers * 128.0;
    public static double Depth(Corner c, double off) => WaterZ(c, off) - GroundZ(c);
}
