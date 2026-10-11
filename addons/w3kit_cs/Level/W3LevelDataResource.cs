using System;
using Diablo1Like.MapRuntime;
using Diablo1Like.W3.Paths;
using Godot;
using GArray = Godot.Collections.Array;

namespace Diablo1Like.W3.Level;

/// <summary>
/// 项目自有关卡资源：由 Importer 写入，运行时不再解析 W3E。
/// Export 字段名刻意使用 snake_case，与旧 GDS / GDScript 访问完全一致。
/// </summary>
[GlobalClass]
public partial class W3LevelDataResource : Resource
{
    public const float TileSize = 128f;
    public const int FormatVersionConst = 1;
    public const int FlagMapEdge = 1;
    public const int FlagRamp = 2;
    public const int FlagBlight = 4;
    public const int FlagWater = 8;
    public const int FlagBoundary = 16;

    private readonly GArray _cornerCache = new();

    // ---- 序列化字段（snake_case = GDScript 可见名）----
    [Export] public int format_version { get; set; } = FormatVersionConst;
    [Export] public string source_path { get; set; } = "";
    [Export] public string source_w3e_sha256 { get; set; } = "";
    [Export] public string imported_at_utc { get; set; } = "";
    [Export] public int w3e_version { get; set; } = 11;
    [Export] public string tileset { get; set; } = "L";
    [Export] public bool custom_tileset { get; set; }
    [Export] public string[] ground_tilesets { get; set; } = Array.Empty<string>();
    [Export] public string[] cliff_tilesets { get; set; } = Array.Empty<string>();
    [Export] public int width { get; set; }
    [Export] public int height { get; set; }
    [Export] public Vector2 center_offset { get; set; } = Vector2.Zero;

    [Export] public float[] ground_heights { get; set; } = Array.Empty<float>();
    [Export] public float[] water_heights { get; set; } = Array.Empty<float>();
    [Export] public byte[] corner_flags { get; set; } = Array.Empty<byte>();
    [Export] public byte[] ground_textures { get; set; } = Array.Empty<byte>();
    [Export] public byte[] ground_variations { get; set; } = Array.Empty<byte>();
    [Export] public byte[] cliff_variations { get; set; } = Array.Empty<byte>();
    [Export] public byte[] cliff_textures { get; set; } = Array.Empty<byte>();
    [Export] public byte[] layer_heights { get; set; } = Array.Empty<byte>();

    [Export] public byte[] shadow_bytes { get; set; } = Array.Empty<byte>();
    [Export] public byte[] pathing_bytes { get; set; } = Array.Empty<byte>();
    [Export] public byte[] map_info_bytes { get; set; } = Array.Empty<byte>();
    [Export] public int game_version_major { get; set; }
    [Export] public int game_version_minor { get; set; }
    [Export] public int game_version_patch { get; set; }
    [Export] public int game_version_build { get; set; }
    [Export] public byte[] doodad_bytes { get; set; } = Array.Empty<byte>();
    [Export] public byte[] unit_bytes { get; set; } = Array.Empty<byte>();
    [Export] public byte[] unit_override_bytes { get; set; } = Array.Empty<byte>();
    [Export] public byte[] doodad_override_bytes { get; set; } = Array.Empty<byte>();
    [Export] public byte[] destructable_override_bytes { get; set; } = Array.Empty<byte>();

    /// <summary>运行时地图目录提示（不序列化）。</summary>
    public string map_dir { get; set; } = "";

    /// <summary>从 W3E / 打包地形 duck-type 对象填充关卡数据。</summary>
    public static W3LevelDataResource FromW3e(GodotObject map)
    {
        var outLevel = new W3LevelDataResource
        {
            w3e_version = map.Get("version").AsInt32(),
            tileset = map.Get("tileset").AsString(),
            custom_tileset = map.Get("custom_tileset").AsBool(),
            ground_tilesets = map.Get("ground_tilesets").AsStringArray(),
            cliff_tilesets = map.Get("cliff_tilesets").AsStringArray(),
            width = map.Get("width").AsInt32(),
            height = map.Get("height").AsInt32(),
            center_offset = map.Get("center_offset").AsVector2(),
        };

        if (HasPackedArrays(map))
        {
            CopyPackedArrays(map, outLevel);
            return outLevel;
        }

        int count = checked(outLevel.width * outLevel.height);
        outLevel.ground_heights = new float[count];
        outLevel.water_heights = new float[count];
        outLevel.corner_flags = new byte[count];
        outLevel.ground_textures = new byte[count];
        outLevel.ground_variations = new byte[count];
        outLevel.cliff_variations = new byte[count];
        outLevel.cliff_textures = new byte[count];
        outLevel.layer_heights = new byte[count];

        for (int y = 0; y < outLevel.height; y++)
        {
            for (int x = 0; x < outLevel.width; x++)
            {
                GodotObject? corner = map.Call("get_corner", x, y).AsGodotObject();
                if (corner == null)
                    throw new InvalidOperationException($"Missing corner {x},{y}");
                int i = y * outLevel.width + x;
                outLevel.ground_heights[i] = (float)corner.Get("ground_height").AsDouble();
                outLevel.water_heights[i] = (float)corner.Get("water_height").AsDouble();
                byte flags = 0;
                if (corner.Get("map_edge").AsBool()) flags |= FlagMapEdge;
                if (corner.Get("ramp").AsBool()) flags |= FlagRamp;
                if (corner.Get("blight").AsBool()) flags |= FlagBlight;
                if (corner.Get("water").AsBool()) flags |= FlagWater;
                if (corner.Get("boundary").AsBool()) flags |= FlagBoundary;
                outLevel.corner_flags[i] = flags;
                outLevel.ground_textures[i] = (byte)Math.Clamp(corner.Get("ground_texture").AsInt32(), 0, 255);
                outLevel.ground_variations[i] = (byte)Math.Clamp(corner.Get("ground_variation").AsInt32(), 0, 255);
                outLevel.cliff_variations[i] = (byte)Math.Clamp(corner.Get("cliff_variation").AsInt32(), 0, 255);
                outLevel.cliff_textures[i] = (byte)Math.Clamp(corner.Get("cliff_texture").AsInt32(), 0, 255);
                outLevel.layer_heights[i] = (byte)Math.Clamp(corner.Get("layer_height").AsInt32(), 0, 255);
            }
        }

        return outLevel;
    }

    public static W3LevelDataResource from_w3e(GodotObject map) => FromW3e(map);

    public bool IsValid()
    {
        if (format_version != FormatVersionConst || width < 2 || height < 2)
            return false;
        int count = width * height;
        return ground_heights.Length == count
            && water_heights.Length == count
            && corner_flags.Length == count
            && ground_textures.Length == count
            && ground_variations.Length == count
            && cliff_variations.Length == count
            && cliff_textures.Length == count
            && layer_heights.Length == count;
    }

    public bool is_valid() => IsValid();

    public int TileCountX() => Math.Max(0, width - 1);
    public int tile_count_x() => TileCountX();
    public int TileCountY() => Math.Max(0, height - 1);
    public int tile_count_y() => TileCountY();

    public W3CornerData? GetCorner(int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return null;
        EnsureCornerCache();
        int i = y * width + x;
        if (_cornerCache[i].VariantType != Variant.Type.Nil)
            return _cornerCache[i].AsGodotObject() as W3CornerData;

        byte flags = corner_flags[i];
        var c = new W3CornerData
        {
            GroundHeight = ground_heights[i],
            WaterHeight = water_heights[i],
            MapEdge = (flags & FlagMapEdge) != 0,
            Ramp = (flags & FlagRamp) != 0,
            Blight = (flags & FlagBlight) != 0,
            Water = (flags & FlagWater) != 0,
            Boundary = (flags & FlagBoundary) != 0,
            GroundTexture = ground_textures[i],
            GroundVariation = ground_variations[i],
            CliffVariation = cliff_variations[i],
            CliffTexture = cliff_textures[i],
            LayerHeight = layer_heights[i],
        };
        _cornerCache[i] = c;
        return c;
    }

    public W3CornerData? get_corner(int x, int y) => GetCorner(x, y);

    public Vector3 CornerWorldPos(int x, int y) =>
        new(center_offset.X + x * TileSize, CornerWorldY(x, y), center_offset.Y + y * TileSize);

    public Vector3 corner_world_pos(int x, int y) => CornerWorldPos(x, y);

    public float CornerWorldY(int x, int y)
    {
        W3CornerData? c = GetCorner(x, y);
        if (c == null)
            return 0f;
        float yWorld = c.WorldY(TileSize);
        for (int tileY = y - 1; tileY <= y; tileY++)
        {
            for (int tileX = x - 1; tileX <= x; tileX++)
            {
                if (!IsRampEntrance(tileX, tileY))
                    continue;
                W3CornerData? c00 = GetCorner(tileX, tileY);
                W3CornerData? c10 = GetCorner(tileX + 1, tileY);
                W3CornerData? c01 = GetCorner(tileX, tileY + 1);
                W3CornerData? c11 = GetCorner(tileX + 1, tileY + 1);
                if (c00 == null || c10 == null || c01 == null || c11 == null)
                    continue;
                int basel = Math.Min(
                    Math.Min(c00.LayerHeight, c10.LayerHeight),
                    Math.Min(c01.LayerHeight, c11.LayerHeight));
                if (c.LayerHeight == basel)
                    return yWorld + TileSize * 0.5f;
            }
        }
        return yWorld;
    }

    public float corner_world_y(int x, int y) => CornerWorldY(x, y);

    public bool IsRampEntrance(int x, int y)
    {
        if (x < 0 || y < 0 || x >= TileCountX() || y >= TileCountY())
            return false;
        W3CornerData? c00 = GetCorner(x, y);
        W3CornerData? c10 = GetCorner(x + 1, y);
        W3CornerData? c01 = GetCorner(x, y + 1);
        W3CornerData? c11 = GetCorner(x + 1, y + 1);
        if (c00 == null || c10 == null || c01 == null || c11 == null)
            return false;
        if (!(c00.Ramp && c10.Ramp && c01.Ramp && c11.Ramp))
            return false;
        return !(c00.LayerHeight == c11.LayerHeight && c01.LayerHeight == c10.LayerHeight);
    }

    public bool is_ramp_entrance(int x, int y) => IsRampEntrance(x, y);

    /// <summary>按相对路径取出嵌入的地图附属字节（对齐 archive.read_file）。</summary>
    public byte[] ReadFile(string rel)
    {
        string key = (rel ?? "").Replace('\\', '/').ToLowerInvariant();
        return key switch
        {
            "war3map.shd" => shadow_bytes,
            "war3map.wpm" => pathing_bytes,
            "war3map.w3i" => map_info_bytes,
            "war3map.doo" => doodad_bytes,
            "war3mapunits.doo" => unit_bytes,
            "war3map.w3u" => unit_override_bytes,
            "war3map.w3d" => doodad_override_bytes,
            "war3map.w3b" => destructable_override_bytes,
            _ => Array.Empty<byte>(),
        };
    }

    public byte[] read_file(string rel) => ReadFile(rel);

    public string ResolvePath(string rel) =>
        new W3PathResolverBridge().ResolveGamePath(rel);

    public string resolve_path(string rel) => ResolvePath(rel);

    private void EnsureCornerCache()
    {
        int count = width * height;
        if (_cornerCache.Count == count)
            return;
        _cornerCache.Resize(count);
        for (int i = 0; i < count; i++)
            _cornerCache[i] = default;
    }

    private static bool HasPackedArrays(GodotObject map)
    {
        Variant gh = map.Get("ground_heights");
        if (gh.VariantType == Variant.Type.Nil)
            return false;
        float[] arr = gh.AsFloat32Array();
        int w = map.Get("width").AsInt32();
        int h = map.Get("height").AsInt32();
        return arr.Length == w * h && w >= 2;
    }

    private static void CopyPackedArrays(GodotObject map, W3LevelDataResource dst)
    {
        dst.ground_heights = map.Get("ground_heights").AsFloat32Array();
        dst.water_heights = map.Get("water_heights").AsFloat32Array();
        dst.corner_flags = map.Get("corner_flags").AsByteArray();
        dst.ground_textures = map.Get("ground_textures").AsByteArray();
        dst.ground_variations = map.Get("ground_variations").AsByteArray();
        dst.cliff_variations = map.Get("cliff_variations").AsByteArray();
        dst.cliff_textures = map.Get("cliff_textures").AsByteArray();
        dst.layer_heights = map.Get("layer_heights").AsByteArray();
    }
}
