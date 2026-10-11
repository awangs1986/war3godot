using System;
using Diablo1Like.W3.Terrain;
using Godot;
using GArray = Godot.Collections.Array;

namespace Diablo1Like.MapRuntime;

/// <summary>
/// 强类型打包地形图：供 d1map / NativeMapLoader 直接交给渲染与采样，
/// 不再经 GDS Dictionary 重组为 W3EMap。
/// </summary>
[GlobalClass]
public partial class W3PackedTerrainMap : RefCounted
{
    public const float TileSize = W3TerrainMap.TileSize;

    /// <summary>算法核心；子类（W3EMapBridge）可在写出前替换。</summary>
    protected W3TerrainMap? Core;
    private readonly GArray _cornerCache = new();

    public int Version { get; set; } = 11;
    public string Tileset { get; set; } = "L";
    public bool CustomTileset { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Vector2 CenterOffset { get; set; } = Vector2.Zero;

    // GDS duck-typing 字段别名
    public int width { get => Width; set => Width = value; }
    public int height { get => Height; set => Height = value; }
    public int version { get => Version; set => Version = value; }
    public string tileset { get => Tileset; set => Tileset = value; }
    public bool custom_tileset { get => CustomTileset; set => CustomTileset = value; }
    public Vector2 center_offset { get => CenterOffset; set => CenterOffset = value; }
    public string[] ground_tilesets { get => GroundTilesets; set => GroundTilesets = value; }
    public string[] cliff_tilesets { get => CliffTilesets; set => CliffTilesets = value; }
    public string[] GroundTilesets { get; set; } = Array.Empty<string>();
    public string[] CliffTilesets { get; set; } = Array.Empty<string>();
    public float[] GroundHeights { get; set; } = Array.Empty<float>();
    public float[] WaterHeights { get; set; } = Array.Empty<float>();
    public byte[] CornerFlags { get; set; } = Array.Empty<byte>();
    public byte[] GroundTextures { get; set; } = Array.Empty<byte>();
    public byte[] GroundVariations { get; set; } = Array.Empty<byte>();
    public byte[] CliffVariations { get; set; } = Array.Empty<byte>();
    public byte[] CliffTextures { get; set; } = Array.Empty<byte>();
    public byte[] LayerHeights { get; set; } = Array.Empty<byte>();

    // GDS / W3TerrainMapBridge Get() 字段别名
    public float[] ground_heights { get => GroundHeights; set => GroundHeights = value; }
    public float[] water_heights { get => WaterHeights; set => WaterHeights = value; }
    public byte[] corner_flags { get => CornerFlags; set => CornerFlags = value; }
    public byte[] ground_textures { get => GroundTextures; set => GroundTextures = value; }
    public byte[] ground_variations { get => GroundVariations; set => GroundVariations = value; }
    public byte[] cliff_variations { get => CliffVariations; set => CliffVariations = value; }
    public byte[] cliff_textures { get => CliffTextures; set => CliffTextures = value; }
    public byte[] layer_heights { get => LayerHeights; set => LayerHeights = value; }

    /// <summary>从 NativeMap 核心模型填充本对象。</summary>
    public void PopulateFromCore(W3TerrainMap map)
    {
        Core = map;
        Version = map.Version;
        Tileset = map.Tileset.ToString();
        CustomTileset = map.CustomTileset;
        Width = map.Width;
        Height = map.Height;
        CenterOffset = new Vector2(map.CenterOffsetX, map.CenterOffsetY);
        GroundTilesets = map.GroundTilesets.ToArray();
        CliffTilesets = map.CliffTilesets.ToArray();

        int count = checked(map.Width * map.Height);
        GroundHeights = new float[count];
        WaterHeights = new float[count];
        CornerFlags = new byte[count];
        GroundTextures = new byte[count];
        GroundVariations = new byte[count];
        CliffVariations = new byte[count];
        CliffTextures = new byte[count];
        LayerHeights = new byte[count];
        _cornerCache.Resize(count);
        for (int i = 0; i < count; i++)
        {
            W3TerrainMap.Corner c = map.Corners[i];
            GroundHeights[i] = c.GroundHeight;
            WaterHeights[i] = c.WaterHeight;
            byte flags = 0;
            if (c.MapEdge) flags |= 1;
            if (c.Ramp) flags |= 2;
            if (c.Blight) flags |= 4;
            if (c.Water) flags |= 8;
            if (c.Boundary) flags |= 16;
            CornerFlags[i] = flags;
            GroundTextures[i] = c.GroundTexture;
            GroundVariations[i] = c.GroundVariation;
            CliffVariations[i] = c.CliffVariation;
            CliffTextures[i] = c.CliffTexture;
            LayerHeights[i] = c.LayerHeight;
            _cornerCache[i] = default;
        }
    }

    /// <summary>
    /// Creates an engine-agnostic snapshot for renderers and samplers. This is
    /// the strongly typed production boundary used by Godot; it avoids
    /// rebuilding the same map through dynamic Godot property lookup.
    /// </summary>
    public W3TerrainMap CreateCoreSnapshot()
    {
        if (!IsValid())
            throw new InvalidOperationException(
                $"Packed terrain arrays are inconsistent for {Width}x{Height} corners.");

        var map = new W3TerrainMap
        {
            Version = Version,
            Tileset = string.IsNullOrEmpty(Tileset) ? 'A' : Tileset[0],
            CustomTileset = CustomTileset,
            Width = Width,
            Height = Height,
            CenterOffsetX = CenterOffset.X,
            CenterOffsetY = CenterOffset.Y,
            Corners = new W3TerrainMap.Corner[checked(Width * Height)],
        };
        map.GroundTilesets.AddRange(GroundTilesets);
        map.CliffTilesets.AddRange(CliffTilesets);
        for (int i = 0; i < map.Corners.Length; i++)
        {
            byte flags = CornerFlags[i];
            map.Corners[i] = new W3TerrainMap.Corner
            {
                GroundHeight = GroundHeights[i],
                WaterHeight = WaterHeights[i],
                MapEdge = (flags & 1) != 0,
                Ramp = (flags & 2) != 0,
                Blight = (flags & 4) != 0,
                Water = (flags & 8) != 0,
                Boundary = (flags & 16) != 0,
                GroundTexture = GroundTextures[i],
                GroundVariation = GroundVariations[i],
                CliffVariation = CliffVariations[i],
                CliffTexture = CliffTextures[i],
                LayerHeight = LayerHeights[i],
            };
        }
        map.InitializeRenderCorners();
        return map;
    }

    public bool IsValid()
    {
        if (Width < 2 || Height < 2)
            return false;
        int count = Width * Height;
        return GroundHeights.Length == count
            && WaterHeights.Length == count
            && CornerFlags.Length == count
            && GroundTextures.Length == count
            && GroundVariations.Length == count
            && CliffVariations.Length == count
            && CliffTextures.Length == count
            && LayerHeights.Length == count;
    }

    public bool is_valid() => IsValid();

    public int TileCountX() => Math.Max(0, Width - 1);
    public int tile_count_x() => TileCountX();
    public int TileCountY() => Math.Max(0, Height - 1);
    public int tile_count_y() => TileCountY();

    public W3CornerData? GetCorner(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return null;
        EnsureCore();
        int i = y * Width + x;
        if (_cornerCache[i].VariantType != Variant.Type.Nil)
            return _cornerCache[i].AsGodotObject() as W3CornerData;

        W3TerrainMap.Corner c = Core!.GetCorner(x, y);
        var data = new W3CornerData
        {
            GroundHeight = c.GroundHeight,
            WaterHeight = c.WaterHeight,
            MapEdge = c.MapEdge,
            Ramp = c.Ramp,
            Blight = c.Blight,
            Water = c.Water,
            Boundary = c.Boundary,
            GroundTexture = c.GroundTexture,
            GroundVariation = c.GroundVariation,
            CliffVariation = c.CliffVariation,
            CliffTexture = c.CliffTexture,
            LayerHeight = c.LayerHeight,
        };
        _cornerCache[i] = data;
        return data;
    }

    public W3CornerData? get_corner(int x, int y) => GetCorner(x, y);

    /// <summary>窥视缓存，不分配新角点对象。</summary>
    protected W3CornerData? PeekCornerCache(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return null;
        int i = y * Width + x;
        if (i >= _cornerCache.Count || _cornerCache[i].VariantType == Variant.Type.Nil)
            return null;
        return _cornerCache[i].AsGodotObject() as W3CornerData;
    }

    public Vector3 CornerWorldPos(int x, int y) =>
        new(CenterOffset.X + x * TileSize, CornerWorldY(x, y), CenterOffset.Y + y * TileSize);

    public Vector3 corner_world_pos(int x, int y) => CornerWorldPos(x, y);

    public float CornerWorldY(int x, int y)
    {
        EnsureCore();
        int cx = Math.Clamp(x, 0, Width - 1);
        int cy = Math.Clamp(y, 0, Height - 1);
        return Core!.FinalGroundHeightWithRamp(cx, cy) * TileSize;
    }

    public float corner_world_y(int x, int y) => CornerWorldY(x, y);

    public bool IsRampEntrance(int x, int y)
    {
        EnsureCore();
        return Core!.IsRampEntrance(x, y);
    }

    public bool is_ramp_entrance(int x, int y) => IsRampEntrance(x, y);

    private void EnsureCore()
    {
        if (Core != null)
            return;
        Core = W3TerrainMapBridge.FromGodotResource(this);
    }

    /// <summary>供子类（W3EMapBridge）在访问算法核心前确保已构建。</summary>
    protected void EnsureCoreLoaded() => EnsureCore();

    /// <summary>丢弃缓存角点对象，下次 get_corner 从数组/核心重建。</summary>
    protected void InvalidateCornerCache()
    {
        int n = _cornerCache.Count;
        for (int i = 0; i < n; i++)
            _cornerCache[i] = default;
    }
}
