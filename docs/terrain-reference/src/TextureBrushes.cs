// TextureBrushes.cs —— 应用纹理组：贴图 / 荒芜 / 边界
// 导读：guides/texture.md、blight.md、boundary.md
namespace TerrainReference;

public static class TextureBrushes
{
    /// <summary>
    /// 覆盖集合内 groundTexture = 所选下标，并重新随机 groundVariation。
    /// 变体约 91% 落在 {0,4,8,12,16,17}，其余均匀落在 1..15（12.10）。
    /// </summary>
    public static void Texture(W3eMap m, int cx, int cy, int size, Shape shape, int tex, Random rng)
    {
        int[] common = { 0, 4, 8, 12, 16, 17 };
        foreach (var (dx, dy) in Footprint.Offsets(size, shape))
        {
            int x = cx + dx, y = cy + dy;
            if (!m.Inside(x, y)) continue;
            m[x, y].Texture = tex;
            m[x, y].GroundVariation = rng.NextDouble() < 0.91 ? common[rng.Next(common.Length)] : rng.Next(1, 16);
        }
    }

    /// <summary>
    /// 覆盖集合内设 blight 标志；水角点不接受荒芜（实测点在水中心无变化）。
    /// 官方还会在外围约 R+2 内随机改一部分贴图（不规则边缘），这里用 extraRadius 演示。
    /// </summary>
    public static void Blight(W3eMap m, int cx, int cy, int size, Shape shape, Random? rng = null)
    {
        foreach (var (dx, dy) in Footprint.Offsets(size, shape))
        {
            int x = cx + dx, y = cy + dy;
            if (m.Inside(x, y) && !m[x, y].Water) m[x, y].Blight = true;
        }
        if (rng == null) return;
        int R = Footprint.Radius(size) + 2;
        for (int dy = -R; dy <= R; dy++)
            for (int dx = -R; dx <= R; dx++)
                if (m.Inside(cx + dx, cy + dy) && rng.NextDouble() < 0.3) m[cx + dx, cy + dy].Texture = 1;
    }

    /// <summary>放置/移除边界：只改 boundary 标志，其余字段不动；放置再移除与原图逐字节相同。</summary>
    public static void Boundary(W3eMap m, int cx, int cy, int size, Shape shape, bool add)
    {
        foreach (var (dx, dy) in Footprint.Offsets(size, shape))
            if (m.Inside(cx + dx, cy + dy)) m[cx + dx, cy + dy].Boundary = add;
    }
}
