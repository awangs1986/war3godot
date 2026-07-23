namespace Diablo1Like.W3.Terrain;

/// <summary>Water.slk row for the map tileset (heights, tint colors, animation).</summary>
public sealed class WaterConfig
{
    public float HeightOffset = -0.7f;
    public string TextureFile = "ReplaceableTextures/Water/Water";
    public int TextureCount;
    public float TextureRate;
    // RGBA 0..1.
    public float[] ShallowMin = { 0.0f, 0.45f, 0.55f, 0.50f };
    public float[] ShallowMax = { 0.0f, 0.35f, 0.45f, 0.65f };
    public float[] DeepMin = { 0.0f, 0.20f, 0.35f, 0.75f };
    public float[] DeepMax = { 0.0f, 0.10f, 0.25f, 0.85f };

    /// <summary>Parses TerrainArt/Water.slk text for row "&lt;tileset&gt;Sha" (shared SD metadata, same as Warsmash/HiveWE on 1.32).</summary>
    public static WaterConfig Parse(string? slkText, char tileset)
    {
        var config = new WaterConfig();
        if (string.IsNullOrEmpty(slkText))
            return config;
        string key = $"{tileset}Sha";
        var headers = new Dictionary<int, string>();
        var row = new Dictionary<string, string>();
        int currentY = -1;
        void Commit()
        {
            if (row.TryGetValue("_id", out string? id) && id == key)
                config.ApplyRow(row);
        }
        foreach (string rawLine in slkText.Split('\n'))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (!line.StartsWith("C;", StringComparison.Ordinal))
                continue;
            int y = Slk.Int(line, "Y");
            int x = Slk.Int(line, "X");
            string value = Slk.Str(line, "K").Trim('"');
            if (y > 0 && y != currentY)
            {
                if (row.TryGetValue("_id", out string? id) && id == key)
                {
                    config.ApplyRow(row);
                    return config;
                }
                currentY = y;
                row.Clear();
            }
            if (x <= 0)
                continue;
            if (currentY == 1)
                headers[x] = value.ToLowerInvariant();
            else if (currentY > 1)
            {
                if (x == 1)
                    row["_id"] = value;
                row[headers.TryGetValue(x, out string? header) ? header : x.ToString()] = value;
            }
        }
        Commit();
        return config;
    }

    private void ApplyRow(Dictionary<string, string> row)
    {
        HeightOffset = Slk.Float(row, "height", HeightOffset);
        if (row.TryGetValue("texfile", out string? tex) && tex.Length > 0)
            TextureFile = tex.Replace('\\', '/');
        TextureCount = (int)Slk.Float(row, "numtex", TextureCount);
        TextureRate = Slk.Float(row, "texrate", TextureRate);
        ShallowMin = Color(row, "smin", ShallowMin);
        ShallowMax = Color(row, "smax", ShallowMax);
        DeepMin = Color(row, "dmin", DeepMin);
        DeepMax = Color(row, "dmax", DeepMax);
    }

    private static float[] Color(Dictionary<string, string> row, string prefix, float[] fallback) => new[]
    {
        Slk.Float(row, prefix + "_r", fallback[0] * 255f) / 255f,
        Slk.Float(row, prefix + "_g", fallback[1] * 255f) / 255f,
        Slk.Float(row, prefix + "_b", fallback[2] * 255f) / 255f,
        Slk.Float(row, prefix + "_a", fallback[3] * 255f) / 255f,
    };
}

/// <summary>
/// Engine-agnostic water plane data (corner water/ground heights + wet tiles),
/// mirroring addons/w3kit/w3_gpu_water.gd for the shared shader contract.
/// </summary>
public sealed class WaterLayout
{
    private readonly W3TerrainMap _map;

    public WaterLayout(W3TerrainMap map) => _map = map;

    /// <summary>Corner water heights in layer units (shader multiplies by tile size).</summary>
    public float[] BuildWaterHeightMap()
    {
        var heights = new float[_map.Width * _map.Height];
        BuildWaterHeightRegion(heights, 0, 0, _map.Width, _map.Height);
        return heights;
    }

    public void BuildWaterHeightRegion(float[] target, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
            target[y * _map.Width + x] = _map.GetCorner(x, y).WaterHeight;
    }

    /// <summary>Corner ground heights in layer units, including ramp-entrance +0.5 lift (depth shading).</summary>
    public float[] BuildGroundHeightMap()
    {
        var heights = new float[_map.Width * _map.Height];
        for (int y = 0; y < _map.Height; y++)
        for (int x = 0; x < _map.Width; x++)
            heights[y * _map.Width + x] = _map.FinalGroundHeightWithRamp(x, y);
        return heights;
    }

    /// <summary>Wet tile mask: any of the 4 corners has the water flag.</summary>
    public byte[] BuildExistsMap(out int wetTiles)
    {
        var exists = new byte[_map.TileWidth * _map.TileHeight];
        wetTiles = 0;
        for (int y = 0; y < _map.TileHeight; y++)
        {
            for (int x = 0; x < _map.TileWidth; x++)
            {
                bool wet = _map.GetCorner(x, y).Water || _map.GetCorner(x + 1, y).Water ||
                    _map.GetCorner(x, y + 1).Water || _map.GetCorner(x + 1, y + 1).Water;
                exists[y * _map.TileWidth + x] = wet ? (byte)1 : (byte)0;
                if (wet)
                    wetTiles++;
            }
        }
        return exists;
    }

    public void BuildExistsRegion(byte[] target, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                bool wet = _map.GetCorner(x, y).Water || _map.GetCorner(x + 1, y).Water ||
                    _map.GetCorner(x, y + 1).Water || _map.GetCorner(x + 1, y + 1).Water;
                target[y * _map.TileWidth + x] = wet ? (byte)1 : (byte)0;
            }
        }
    }
}
