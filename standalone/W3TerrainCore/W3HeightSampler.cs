namespace Diablo1Like.W3.Terrain;

/// <summary>
/// Heightfield queries using the rendered ground topology, plus walkability helpers.
/// Cliff model interiors are not represented by this heightfield query.
/// </summary>
public sealed class W3HeightSampler
{
    private readonly W3TerrainMap _map;

    /// <summary>绑定地形图。</summary>
    public W3HeightSampler(W3TerrainMap map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

    /// <summary>当前绑定的地形图。</summary>
    public W3TerrainMap Map => _map;

    /// <summary>Triangulated heightfield Y (including ramp lift), not a cliff-mesh raycast.</summary>
    public float SampleY(float worldX, float worldZ)
    {
        float lx = (worldX - _map.CenterOffsetX) / W3TerrainMap.TileSize;
        float ly = (worldZ - _map.CenterOffsetY) / W3TerrainMap.TileSize;
        int x0 = (int)MathF.Floor(lx);
        int y0 = (int)MathF.Floor(ly);
        float tx = Math.Clamp(lx - x0, 0f, 1f);
        float ty = Math.Clamp(ly - y0, 0f, 1f);
        float h00 = CornerWorldY(x0, y0);
        float h10 = CornerWorldY(x0 + 1, y0);
        float h01 = CornerWorldY(x0, y0 + 1);
        float h11 = CornerWorldY(x0 + 1, y0 + 1);
        return TerrainSurfaceGeometry.GroundHeight(h00, h10, h01, h11, tx, ty);
    }

    /// <summary>世界坐标 → 瓦片索引（向下取整）。</summary>
    public (int X, int Y) WorldToTile(float worldX, float worldZ)
    {
        float lx = (worldX - _map.CenterOffsetX) / W3TerrainMap.TileSize;
        float ly = (worldZ - _map.CenterOffsetY) / W3TerrainMap.TileSize;
        return ((int)MathF.Floor(lx), (int)MathF.Floor(ly));
    }

    /// <summary>瓦片中心世界坐标（Y 为采样高度）。</summary>
    public (float X, float Y, float Z) TileCenterWorld(int tileX, int tileY)
    {
        float wx = _map.CenterOffsetX + (tileX + 0.5f) * W3TerrainMap.TileSize;
        float wz = _map.CenterOffsetY + (tileY + 0.5f) * W3TerrainMap.TileSize;
        return (wx, SampleY(wx, wz), wz);
    }

    /// <summary>四角均非边界/地图边时视为可走。</summary>
    public bool IsTileWalkable(int tileX, int tileY)
    {
        if (!_map.ContainsTile(tileX, tileY))
            return false;

        for (int oy = 0; oy < 2; oy++)
        {
            for (int ox = 0; ox < 2; ox++)
            {
                W3TerrainMap.Corner c = _map.GetCorner(tileX + ox, tileY + oy);
                if (c.Boundary || c.MapEdge)
                    return false;
            }
        }

        return true;
    }

    /// <summary>层高差与坡道规则下的一步可达性。</summary>
    public bool CanStep(int fromX, int fromY, int toX, int toY)
    {
        if (!IsTileWalkable(toX, toY))
            return false;
        if (!_map.ContainsCorner(fromX, fromY) || !_map.ContainsCorner(toX, toY))
            return false;

        W3TerrainMap.Corner from = _map.GetCorner(fromX, fromY);
        W3TerrainMap.Corner to = _map.GetCorner(toX, toY);
        int dh = Math.Abs(from.LayerHeight - to.LayerHeight);
        if (dh <= 1)
            return true;
        if (from.Ramp || to.Ramp)
            return dh <= 2;
        return false;
    }

    /// <summary>角点世界 Y（含坡道入口抬升）。</summary>
    public float CornerWorldY(int x, int y)
    {
        int cx = Math.Clamp(x, 0, _map.Width - 1);
        int cy = Math.Clamp(y, 0, _map.Height - 1);
        return _map.FinalGroundHeightWithRamp(cx, cy) * W3TerrainMap.TileSize;
    }

}
