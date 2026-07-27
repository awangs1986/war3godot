namespace Diablo1Like.W3.Terrain;

/// <summary>
/// Engine-agnostic ground packing shared by the Godot runtime renderer and the
/// editor GL renderer. Produces the exact per-tile 4-layer packing, per-corner
/// world heights and tile existence used by w3_ground_unified.gdshader.
/// Ground texture ownership follows HiveWE <c>real_tile_texture</c> (user
/// directed 2026-07-22; overrides Warsmash when they conflict).
/// Any behavior change here must keep both renderers pixel-equivalent.
/// </summary>
public sealed class GroundLayout
{
    public const int LayerStride = 32;
    public const int CellSize = 64;
    public const float EmptyLayer = 60000.0f;

    private readonly W3TerrainMap _map;
    private readonly WarsmashTerrainRules _rules;
    private readonly IReadOnlyList<bool> _extended;
    private readonly IReadOnlyList<int> _cliffToGround;
    private readonly int _layerCount;

    /// <param name="extended">Per ground layer (tilesets + trailing blight layer): atlas has the 16 extra variation cells.</param>
    /// <param name="cliffToGround">CliffTilesets index → GroundTilesets index (CliffTypes.slk groundTile).</param>
    /// <param name="layerCount">Ground tileset count + 1 (blight).</param>
    public GroundLayout(W3TerrainMap map, WarsmashTerrainRules rules, IReadOnlyList<bool> extended, IReadOnlyList<int> cliffToGround, int layerCount)
    {
        _map = map;
        _rules = rules;
        _extended = extended;
        _cliffToGround = cliffToGround;
        _layerCount = layerCount;
    }

    /// <summary>Corner world heights (Y units), Width*Height row-major.</summary>
    public float[] BuildHeightMap()
    {
        var heights = new float[_map.Width * _map.Height];
        BuildHeightRegion(heights, 0, 0, _map.Width, _map.Height);
        return heights;
    }

    public void BuildHeightRegion(float[] target, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
            target[y * _map.Width + x] = _map.FinalGroundHeightWithRamp(x, y) * W3TerrainMap.TileSize;
    }

    /// <summary>
    /// Lighting heights (world Y). HiveWE terrain.vert derives normals from the
    /// raw ground_heights buffer (corner_height only, no cliff layer / ramp
    /// lift, terrain.ixx upload_ground_heights) so cliff steps never darken the
    /// surrounding ground. Vertex positions keep BuildHeightMap.
    /// </summary>
    public float[] BuildNormalHeightMap()
    {
        var heights = new float[_map.Width * _map.Height];
        BuildNormalHeightRegion(heights, 0, 0, _map.Width, _map.Height);
        return heights;
    }

    public void BuildNormalHeightRegion(float[] target, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
            target[y * _map.Width + x] = _map.GetCorner(x, y).GroundHeight * W3TerrainMap.TileSize;
    }

    /// <summary>Tile existence (1 = draw ground), TileWidth*TileHeight row-major.</summary>
    public byte[] BuildExistsMap()
    {
        var exists = new byte[_map.TileWidth * _map.TileHeight];
        BuildExistsRegion(exists, 0, 0, _map.TileWidth, _map.TileHeight);
        return exists;
    }

    public void BuildExistsRegion(byte[] target, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
            target[y * _map.TileWidth + x] = _rules.GroundExists(x, y) ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Per-tile packed layers: 4 floats per tile (R base opaque, G/B/A overlays
    /// in ascending texture order, EmptyLayer when unused).
    /// </summary>
    public float[] BuildTileData()
    {
        var data = new float[_map.TileWidth * _map.TileHeight * 4];
        BuildTileRegion(data, 0, 0, _map.TileWidth, _map.TileHeight);
        return data;
    }

    public void BuildTileRegion(float[] target, int x0, int y0, int x1, int y1)
    {
        Span<float> packed = stackalloc float[4];
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int offset = (y * _map.TileWidth + x) * 4;
                if (!_rules.GroundExists(x, y))
                {
                    target[offset] = target[offset + 1] = target[offset + 2] = target[offset + 3] = EmptyLayer;
                    continue;
                }
                int bl = RealTileTexture(x, y);
                int br = RealTileTexture(x + 1, y);
                int tl = RealTileTexture(x, y + 1);
                int tr = RealTileTexture(x + 1, y + 1);
                var unique = new SortedSet<int> { bl, br, tl, tr };
                packed.Fill(EmptyLayer);
                int component = 0;
                foreach (int texture in unique)
                {
                    if (component == 0)
                        packed[component++] = texture * LayerStride + GetVariation(texture, _map.GetCorner(x, y).GroundVariation);
                    else
                    {
                        int bits = (br == texture ? 1 : 0) | (bl == texture ? 2 : 0) |
                            (tr == texture ? 4 : 0) | (tl == texture ? 8 : 0);
                        packed[component++] = texture * LayerStride + bits;
                    }
                }
                target[offset] = packed[0];
                target[offset + 1] = packed[1];
                target[offset + 2] = packed[2];
                target[offset + 3] = packed[3];
            }
        }
    }

    /// <summary>
    /// HiveWE <c>Terrain::real_tile_texture</c> (terrain.ixx) — user-directed
    /// authority over Warsmash for ground transition packing (2026-07-22).
    /// Nearby romp OR (nearby cliff AND this corner is not ramp) → this corner's
    /// cliff_texture mapped through CliffTypes.slk groundTile; else blight/ground.
    /// </summary>
    public int RealTileTexture(int x, int y)
    {
        // OR cliff/romp flags on the four tiles that meet at this corner
        // (tile BLs at (x,y), (x-1,y), (x,y-1), (x-1,y-1)).
        bool aRomp = false;
        bool aCliff = false;
        void AccTile(int tx, int ty)
        {
            if (!_map.ContainsTile(tx, ty))
                return;
            W3TerrainMap.Corner t = _map.GetCorner(tx, ty);
            aRomp |= t.Romp;
            aCliff |= t.Cliff;
        }
        AccTile(x, y);
        if (x > 0) AccTile(x - 1, y);
        if (y > 0) AccTile(x, y - 1);
        if (x > 0 && y > 0) AccTile(x - 1, y - 1);

        W3TerrainMap.Corner own = _map.GetCorner(x, y);
        if (aRomp || (aCliff && !own.Ramp))
        {
            // HiveWE reads the AUTHORED corner_cliff_texture; RebuildCliffs'
            // ramp branch (Warsmash-style) stamps its resolved texture onto
            // Corner.CliffTexture before this runs, so the live corner value
            // would silently deviate from the seal on ramp-bearing maps.
            int texture = _rules.AuthoredCliffTexture(x, y);
            if (texture == 15)
                texture -= 14;
            return texture >= 0 && texture < _cliffToGround.Count ? _cliffToGround[texture] : 0;
        }
        if (own.Blight)
            return _layerCount - 1;
        return own.GroundTexture;
    }

    /// <summary>Maps the authored ground variation onto a sub-image index (deco cells 16..31, full blend cell 15).</summary>
    public int GetVariation(int texture, int variation)
    {
        bool extended = texture >= 0 && texture < _extended.Count && _extended[texture];
        if (extended)
            return variation <= 15 ? 16 + variation : variation == 16 ? 15 : 0;
        return variation == 0 ? 0 : 15;
    }

    /// <summary>
    /// Slices one 64x64 RGBA8 sub-cell out of a ground atlas (Warsmash layout:
    /// 4x4 blend cells, extended atlases add 4x4 variation cells to the right).
    /// Returns a CellSize*CellSize*4 buffer (transparent when the cell is absent).
    /// </summary>
    public static byte[] SliceCell(ReadOnlySpan<byte> rgba, int sourceWidth, int sourceHeight, int sub, bool extended)
    {
        var output = new byte[CellSize * CellSize * 4];
        if (sub >= 16 && !extended)
            return output;
        int blockX = sub >= 16 ? 4 : 0;
        int local = sub >= 16 ? sub - 16 : sub;
        int size = sourceHeight / 4;
        int sx = (blockX + local % 4) * size;
        int sy = (local / 4) * size;
        if (size <= 0 || sx + size > sourceWidth || sy + size > sourceHeight)
            return output;
        if (size == CellSize)
        {
            for (int y = 0; y < CellSize; y++)
                rgba.Slice(((sy + y) * sourceWidth + sx) * 4, CellSize * 4)
                    .CopyTo(output.AsSpan(y * CellSize * 4));
            return output;
        }
        // Bilinear resample to 64x64 (matches Godot Image.Resize bilinear closely
        // enough for blend masks; ground atlases are normally already 64px cells).
        for (int y = 0; y < CellSize; y++)
        {
            float v = (y + 0.5f) * size / CellSize - 0.5f;
            int v0 = Math.Clamp((int)MathF.Floor(v), 0, size - 1);
            int v1 = Math.Min(v0 + 1, size - 1);
            float fv = Math.Clamp(v - v0, 0f, 1f);
            for (int x = 0; x < CellSize; x++)
            {
                float u = (x + 0.5f) * size / CellSize - 0.5f;
                int u0 = Math.Clamp((int)MathF.Floor(u), 0, size - 1);
                int u1 = Math.Min(u0 + 1, size - 1);
                float fu = Math.Clamp(u - u0, 0f, 1f);
                int di = (y * CellSize + x) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    float p00 = rgba[((sy + v0) * sourceWidth + sx + u0) * 4 + channel];
                    float p10 = rgba[((sy + v0) * sourceWidth + sx + u1) * 4 + channel];
                    float p01 = rgba[((sy + v1) * sourceWidth + sx + u0) * 4 + channel];
                    float p11 = rgba[((sy + v1) * sourceWidth + sx + u1) * 4 + channel];
                    float top = p00 + (p10 - p00) * fu;
                    float bottom = p01 + (p11 - p01) * fu;
                    output[di + channel] = (byte)Math.Clamp((int)MathF.Round(top + (bottom - top) * fv), 0, 255);
                }
            }
        }
        return output;
    }
}
