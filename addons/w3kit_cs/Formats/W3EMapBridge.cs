using System;
using System.IO;
using Diablo1Like.MapRuntime;
using Diablo1Like.W3.Terrain;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Formats;

/// <summary>
/// war3map.w3e Godot 桥：解析/写出走 W3TerrainCore.W3TerrainMap，
/// 字段与 ramp 查询对齐旧 w3e_map.gd。
/// </summary>
[GlobalClass]
public partial class W3EMapBridge : W3PackedTerrainMap
{
    /// <summary>最近一次 load 失败原因（对齐 GDS last_error）。</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>供 GDScript 读取静态错误（勿再声明 last_error 属性，以免 CS0082）。</summary>
    public static string GetLastError() => LastError;

    public const float TILE_SIZE = TileSize;

    /// <summary>从字节加载；失败返回 null 并写入 LastError。</summary>
    public static W3EMapBridge? LoadBytesStatic(byte[]? data)
    {
        LastError = "";
        if (data == null || data.Length < 21)
        {
            LastError = $"file is too short ({data?.Length ?? 0} bytes)";
            return null;
        }

        try
        {
            W3TerrainMap map = W3TerrainMap.Parse(data);
            var bridge = new W3EMapBridge();
            bridge.PopulateFromCore(map);
            return bridge;
        }
        catch (Exception ex)
        {
            LastError = NormalizeError(ex);
            return null;
        }
    }

    public static W3EMapBridge? load_bytes(byte[]? data) => LoadBytesStatic(data);

    /// <summary>从磁盘路径加载。</summary>
    public static W3EMapBridge? LoadFileStatic(string path)
    {
        LastError = "";
        string global = ProjectSettings.GlobalizePath(path);
        if (!File.Exists(global))
        {
            LastError = $"file missing {path}";
            return null;
        }

        try
        {
            return LoadBytesStatic(File.ReadAllBytes(global));
        }
        catch (Exception ex)
        {
            LastError = NormalizeError(ex);
            return null;
        }
    }

    public static W3EMapBridge? load_file(string path) => LoadFileStatic(path);

    /// <summary>写出当前地图为 war3map.w3e 字节（含对角点缓存的现场修改）。</summary>
    public byte[] SaveBytes()
    {
        FlushCachedCornersToArrays();
        Core = W3TerrainMapBridge.FromGodotResource(this);
        Version = Core.Version;
        return Core.SaveBytes();
    }

    public byte[] save_bytes() => SaveBytes();

    /// <summary>静态写出：接受任意带 get_corner / 数组字段的 Godot 地图对象。</summary>
    public static byte[] SaveBytesStatic(GodotObject mapObject)
    {
        W3TerrainMap map = W3TerrainMapBridge.FromGodotResource(mapObject);
        return map.SaveBytes();
    }

    public static byte[] save_bytes_static(GodotObject mapObject) => SaveBytesStatic(mapObject);

    /// <summary>构建无版权字节的过程测试图（对齐 GDS make_test_map）。</summary>
    public static W3EMapBridge MakeTestMap(int tilesX = 24, int tilesY = 24)
    {
        LastError = "";
        int width = tilesX + 1;
        int height = tilesY + 1;
        var map = new W3TerrainMap
        {
            Version = 11,
            Tileset = 'L',
            CustomTileset = false,
            Width = width,
            Height = height,
            CenterOffsetX = -0.5f * tilesX * TileSize,
            CenterOffsetY = -0.5f * tilesY * TileSize,
            Corners = new W3TerrainMap.Corner[width * height],
        };
        map.GroundTilesets.AddRange(new[] { "Ldrt", "Ldro", "Ldrg", "Lrok" });
        map.CliffTilesets.Add("CLdi");

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = new W3TerrainMap.Corner
                {
                    GroundTexture = (byte)((x + y) % 4),
                    LayerHeight = 2,
                    GroundHeight = 0f,
                };
                if (y > tilesY * 0.55f && x > 4 && x < tilesX - 4)
                    c.LayerHeight = 4;
                if (y == (int)(tilesY * 0.55f) && x > 4 && x < tilesX - 4)
                {
                    c.Ramp = true;
                    c.LayerHeight = 3;
                    c.GroundHeight = 0.5f;
                }
                float hx = MathF.Sin(x * 0.35f) * MathF.Cos(y * 0.28f) * 0.35f;
                c.GroundHeight += hx;
                map.Corners[y * width + x] = c;
            }
        }

        map.InitializeRenderCorners();
        var bridge = new W3EMapBridge();
        bridge.PopulateFromCore(map);
        return bridge;
    }

    public static W3EMapBridge make_test_map(int tilesX = 24, int tilesY = 24) =>
        MakeTestMap(tilesX, tilesY);

    /// <summary>Warsmash 部分斜坡过渡查询，字段对齐 GDS get_ramp_transition。</summary>
    public GDictionary GetRampTransition(int x, int y)
    {
        EnsureCoreLoaded();
        var empty = new GDictionary();
        if (x < 0 || y < 0 || x >= TileCountX() || y >= TileCountY())
            return empty;

        W3TerrainMap.Corner c00 = Core!.GetCorner(x, y);
        W3TerrainMap.Corner c10 = Core.GetCorner(x + 1, y);
        W3TerrainMap.Corner c01 = Core.GetCorner(x, y + 1);
        W3TerrainMap.Corner c11 = Core.GetCorner(x + 1, y + 1);

        if (!TileHasLayerCliff(x, y) || IsRampEntrance(x, y))
            return empty;

        bool vertical = c00.Ramp != c10.Ramp && c01.Ramp != c11.Ramp;
        bool horizontal = c00.Ramp != c01.Ramp && c10.Ramp != c11.Ramp;
        if (!vertical && !horizontal)
            return empty;

        int basel = Min4(c00.LayerHeight, c10.LayerHeight, c01.LayerHeight, c11.LayerHeight);
        int[] heights =
        {
            c01.LayerHeight - basel, c11.LayerHeight - basel,
            c10.LayerHeight - basel, c00.LayerHeight - basel,
        };
        foreach (int h in heights)
        {
            if (h > 1)
                return empty;
        }

        bool facingDown = c01.LayerHeight >= c00.LayerHeight && c11.LayerHeight >= c10.LayerHeight;
        bool facingLeft = c10.LayerHeight >= c00.LayerHeight && c11.LayerHeight >= c01.LayerHeight;
        int blockedX = x + (facingLeft ? -1 : 1);
        int blockedY = y + (facingDown ? -1 : 1);
        if (vertical && TileHasLayerCliff(x, blockedY))
            return empty;
        if (horizontal && TileHasLayerCliff(blockedX, y))
            return empty;

        var placement = new Vector2I(
            x + (horizontal && facingLeft ? -1 : 0),
            y + (vertical && facingDown ? -1 : 0));
        var hideNeighbor = new Vector2I(
            x + (horizontal ? (facingLeft ? -1 : 1) : 0),
            y + (vertical ? (facingDown ? -1 : 1) : 0));
        if (placement.X < 0 || placement.Y < 0 || placement.X >= TileCountX() || placement.Y >= TileCountY())
            return empty;
        if (hideNeighbor.X < 0 || hideNeighbor.Y < 0 ||
            hideNeighbor.X >= TileCountX() || hideNeighbor.Y >= TileCountY())
            return empty;

        string tag = "";
        W3TerrainMap.Corner[] ordered = { c01, c11, c10, c00 };
        for (int i = 0; i < heights.Length; i++)
        {
            int height = heights[i];
            W3TerrainMap.Corner corner = ordered[i];
            if (corner.Ramp)
                tag += height == 0 ? "L" : (height == 1 ? "H" : "X");
            else
                tag += (char)('A' + height);
        }

        return new GDictionary
        {
            ["tag"] = tag,
            ["base"] = basel,
            ["vertical"] = vertical,
            ["horizontal"] = horizontal,
            ["source"] = new Vector2I(x, y),
            ["placement"] = placement,
            ["hide_neighbor"] = hideNeighbor,
        };
    }

    public GDictionary get_ramp_transition(int x, int y) => GetRampTransition(x, y);

    public bool RampTransitionCoversTile(int x, int y)
    {
        for (int sy = y - 1; sy <= y + 1; sy++)
        {
            for (int sx = x - 1; sx <= x + 1; sx++)
            {
                GDictionary transition = GetRampTransition(sx, sy);
                if (transition.Count == 0)
                    continue;
                if (transition["source"].AsVector2I() == new Vector2I(x, y) ||
                    transition["hide_neighbor"].AsVector2I() == new Vector2I(x, y))
                    return true;
            }
        }
        return false;
    }

    public bool ramp_transition_covers_tile(int x, int y) => RampTransitionCoversTile(x, y);

    public bool TileHasLayerCliff(int x, int y)
    {
        if (x < 0 || y < 0 || x >= TileCountX() || y >= TileCountY())
            return false;
        EnsureCoreLoaded();
        W3TerrainMap.Corner c00 = Core!.GetCorner(x, y);
        W3TerrainMap.Corner c10 = Core.GetCorner(x + 1, y);
        W3TerrainMap.Corner c01 = Core.GetCorner(x, y + 1);
        W3TerrainMap.Corner c11 = Core.GetCorner(x + 1, y + 1);
        int height = c00.LayerHeight;
        return c10.LayerHeight != height || c01.LayerHeight != height || c11.LayerHeight != height;
    }

    public bool tile_has_layer_cliff(int x, int y) => TileHasLayerCliff(x, y);

    /// <summary>只把已缓存（可能被脚本改过）的角点刷回打包数组。</summary>
    private void FlushCachedCornersToArrays()
    {
        int count = Width * Height;
        if (GroundHeights.Length != count)
            return;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                W3CornerData? c = PeekCornerCache(x, y);
                if (c == null)
                    continue;
                int i = y * Width + x;
                GroundHeights[i] = c.GroundHeight;
                WaterHeights[i] = c.WaterHeight;
                byte flags = 0;
                if (c.MapEdge) flags |= 1;
                if (c.Ramp) flags |= 2;
                if (c.Blight) flags |= 4;
                if (c.Water) flags |= 8;
                if (c.Boundary) flags |= 16;
                CornerFlags[i] = flags;
                GroundTextures[i] = (byte)Math.Clamp(c.GroundTexture, 0, 255);
                GroundVariations[i] = (byte)Math.Clamp(c.GroundVariation, 0, 255);
                CliffVariations[i] = (byte)Math.Clamp(c.CliffVariation, 0, 255);
                CliffTextures[i] = (byte)Math.Clamp(c.CliffTexture, 0, 255);
                LayerHeights[i] = (byte)Math.Clamp(c.LayerHeight, 0, 255);
            }
        }
    }

    private static int Min4(int a, int b, int c, int d) =>
        Math.Min(Math.Min(a, b), Math.Min(c, d));

    private static string NormalizeError(Exception ex)
    {
        string msg = ex.Message;
        if (msg.Contains("Unexpected end", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("truncated", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("End of stream", StringComparison.OrdinalIgnoreCase) ||
            ex is EndOfStreamException)
            return $"truncated or oversized corner grid: {msg}";
        if (msg.Contains("invalid magic", StringComparison.OrdinalIgnoreCase))
            return msg;
        return msg;
    }
}
