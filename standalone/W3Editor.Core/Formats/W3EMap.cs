using System.Text;

namespace W3Editor.Formats;

/// <summary>One war3map.w3e tile corner (tilepoint).</summary>
public sealed class W3Corner
{
    /// <summary>Height in layer fractions: (raw - 8192) / 512.</summary>
    public float GroundHeight;
    public float WaterHeight;
    public bool MapEdge;
    public bool Ramp;
    public bool Blight;
    public bool Water;
    public bool Boundary;
    public int GroundTexture;
    public int CliffVariation;
    public int GroundVariation;
    public int CliffTexture;
    /// <summary>Cliff layer 0..15 — world Y uses (GroundHeight + LayerHeight - 2) * 128.</summary>
    public int LayerHeight = 2;

    public float WorldY(float tileSize = 128f) => (GroundHeight + LayerHeight - 2f) * tileSize;
}

/// <summary>New-map tileset choice: WE 新建地图对话框的图块集 + 初始纹理。</summary>
public sealed class NewMapSettings
{
    public char Tileset = 'L';
    public List<string> GroundTilesets = new();
    public List<string> CliffTilesets = new();
    /// <summary>Index into GroundTilesets used to fill the blank map.</summary>
    public int InitialTextureIndex;

    public static NewMapSettings LordaeronSummer => new()
    {
        Tileset = 'L',
        GroundTilesets = new List<string> { "Ldrt", "Ldro", "Ldrg", "Lrok", "Lgrs", "Lgrd" },
        CliffTilesets = new List<string> { "CLdi", "CLgr" },
        InitialTextureIndex = 4, // Lgrs
    };
}

/// <summary>
/// Parsed war3map.w3e environment. Byte-level port of addons/w3kit/w3e_map.gd
/// (v11 = 7-byte corners, v12+ = 8-byte corners with 16-bit texture+flags).
/// </summary>
public sealed class W3EMap
{
    public const float TileSize = 128f;
    private const int MaxTilesetCount = 64;
    private const int MaxCornerDimension = 4097;

    public int Version = 11;
    public char Tileset = 'L';
    public bool CustomTileset;
    public List<string> GroundTilesets = new();
    public List<string> CliffTilesets = new();
    /// <summary>Corner grid size: (map_width_tiles + 1) x (map_height_tiles + 1).</summary>
    public int Width;
    public int Height;
    public float CenterOffsetX;
    public float CenterOffsetY;
    /// <summary>Corners[y * Width + x] — row = Y (south→north), col = X (west→east).</summary>
    public W3Corner[] Corners = Array.Empty<W3Corner>();

    public int TileCountX => Math.Max(0, Width - 1);
    public int TileCountY => Math.Max(0, Height - 1);

    public W3Corner? GetCorner(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        return Corners[y * Width + x];
    }

    // ---- New blank map (HiveWE Terrain::save_blank, terrain.ixx) ----

    /// <summary>HiveWE/WE terrain size rules: each side 32..480 tiles, step 4.</summary>
    public const int MinTiles = 32;
    public const int MaxTiles = 480;
    public const int TileStep = 4;

    public static bool IsValidNewSize(int tiles) =>
        tiles >= MinTiles && tiles <= MaxTiles && tiles % TileStep == 0;

    /// <summary>
    /// HiveWE terrain.ixx variation_chances — weighted variation pick over
    /// 0..570 (out-of-table remainder falls through to 0).
    /// </summary>
    private static readonly (int Variation, int Chance)[] VariationChances =
    {
        (0, 85), (16, 85), (0, 85), (1, 10), (2, 4), (3, 1), (4, 85), (5, 10),
        (6, 4), (7, 1), (8, 85), (9, 10), (10, 4), (11, 1), (12, 85), (13, 10),
        (14, 4), (15, 1),
    };

    public static int RandomGroundVariation(Random rng)
    {
        int nr = rng.Next(0, 571) - 1;
        foreach (var (variation, chance) in VariationChances)
        {
            if (nr < chance)
                return variation;
            nr -= chance;
        }
        return 0;
    }

    /// <summary>
    /// Flat blank map — field-for-field port of HiveWE Terrain::save_blank
    /// (v12, layer 2, no cliff texture, edge flags on the border corners,
    /// weighted random ground variation). Defaults to Lordaeron Summer grass.
    /// </summary>
    public static W3EMap CreateBlank(int widthTiles, int heightTiles, NewMapSettings? settings = null)
    {
        if (!IsValidNewSize(widthTiles) || !IsValidNewSize(heightTiles))
            throw new ArgumentOutOfRangeException(nameof(widthTiles),
                $"地图尺寸必须在 {MinTiles}–{MaxTiles} 之间且为 {TileStep} 的倍数（HiveWE/WE 规则），收到 {widthTiles}×{heightTiles}");

        settings ??= NewMapSettings.LordaeronSummer;
        var m = new W3EMap
        {
            Version = 12,
            Tileset = settings.Tileset,
            CustomTileset = true,
            GroundTilesets = new List<string>(settings.GroundTilesets),
            CliffTilesets = new List<string>(settings.CliffTilesets),
            Width = widthTiles + 1,
            Height = heightTiles + 1,
            CenterOffsetX = -widthTiles * TileSize / 2f,
            CenterOffsetY = -heightTiles * TileSize / 2f,
        };
        int grass = Math.Clamp(settings.InitialTextureIndex, 0, Math.Max(0, m.GroundTilesets.Count - 1));
        var rng = new Random();
        m.Corners = new W3Corner[m.Width * m.Height];
        for (int y = 0; y < m.Height; y++)
        {
            for (int x = 0; x < m.Width; x++)
            {
                m.Corners[y * m.Width + x] = new W3Corner
                {
                    GroundHeight = 0f,
                    WaterHeight = 0f,
                    MapEdge = x == 0 || x == m.Width - 1 || y == 0 || y == m.Height - 1,
                    GroundTexture = grass,
                    GroundVariation = RandomGroundVariation(rng),
                    CliffVariation = 0,
                    CliffTexture = 15,
                    LayerHeight = 2,
                };
            }
        }
        return m;
    }

    public static W3EMap Load(string path) => LoadBytes(File.ReadAllBytes(path));

    public static W3EMap LoadBytes(byte[] data)
    {
        if (data.Length < 21)
            throw new InvalidDataException($"w3e 文件过短 ({data.Length} 字节)");
        using var r = new BinaryReader(new MemoryStream(data));
        var magic = Encoding.ASCII.GetString(r.ReadBytes(4));
        if (magic != "W3E!")
            throw new InvalidDataException($"w3e 魔数错误 '{magic}'");
        var m = new W3EMap
        {
            Version = r.ReadInt32(),
            Tileset = (char)r.ReadByte(),
        };
        m.CustomTileset = r.ReadInt32() != 0;
        int ng = r.ReadInt32();
        if (ng < 0 || ng > MaxTilesetCount)
            throw new InvalidDataException($"地面图块集数量非法: {ng}");
        for (int i = 0; i < ng; i++)
            m.GroundTilesets.Add(Encoding.ASCII.GetString(r.ReadBytes(4)));
        int nc = r.ReadInt32();
        if (nc < 0 || nc > MaxTilesetCount)
            throw new InvalidDataException($"悬崖图块集数量非法: {nc}");
        for (int i = 0; i < nc; i++)
            m.CliffTilesets.Add(Encoding.ASCII.GetString(r.ReadBytes(4)));
        m.Width = r.ReadInt32();
        m.Height = r.ReadInt32();
        m.CenterOffsetX = r.ReadSingle();
        m.CenterOffsetY = r.ReadSingle();
        if (m.Width < 2 || m.Height < 2 || m.Width > MaxCornerDimension || m.Height > MaxCornerDimension)
            throw new InvalidDataException($"角点网格尺寸非法: {m.Width}x{m.Height}");
        long cornerBytes = m.Version >= 12 ? 8 : 7;
        long need = (long)m.Width * m.Height * cornerBytes;
        if (r.BaseStream.Length - r.BaseStream.Position < need)
            throw new InvalidDataException($"角点数据被截断: 需要 {need} 字节, 剩余 {r.BaseStream.Length - r.BaseStream.Position}");
        m.Corners = new W3Corner[m.Width * m.Height];
        for (int y = 0; y < m.Height; y++)
            for (int x = 0; x < m.Width; x++)
                m.Corners[y * m.Width + x] = ReadCorner(r, m.Version);
        return m;
    }

    private static W3Corner ReadCorner(BinaryReader r, int version)
    {
        var c = new W3Corner
        {
            GroundHeight = (r.ReadInt16() - 8192) / 512f,
        };
        ushort waterAndEdge = r.ReadUInt16();
        c.WaterHeight = ((waterAndEdge & 0x3FFF) - 8192) / 512f;
        c.MapEdge = (waterAndEdge & 0x4000) != 0;
        if (version >= 12)
        {
            // HiveWE terrain.ixx v12: 16-bit field, 6-bit texture, flags at bits 6-9.
            ushort tf = r.ReadUInt16();
            c.GroundTexture = tf & 0x3F;
            c.Ramp = (tf & 0x40) != 0;
            c.Blight = (tf & 0x80) != 0;
            c.Water = (tf & 0x100) != 0;
            c.Boundary = (tf & 0x200) != 0;
        }
        else
        {
            // Classic 8-bit field (Warsmash Corner.java): 4-bit texture, flags at bits 4-7.
            byte tf = r.ReadByte();
            c.GroundTexture = tf & 0x0F;
            c.Ramp = (tf & 0x10) != 0;
            c.Blight = (tf & 0x20) != 0;
            c.Water = (tf & 0x40) != 0;
            c.Boundary = (tf & 0x80) != 0;
        }
        byte variation = r.ReadByte();
        c.CliffVariation = (variation & 0xE0) >> 5;
        c.GroundVariation = variation & 0x1F;
        byte cliffLayer = r.ReadByte();
        c.CliffTexture = (cliffLayer & 0xF0) >> 4;
        c.LayerHeight = cliffLayer & 0x0F;
        return c;
    }

    public byte[] SaveBytes()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("W3E!"));
        w.Write(Version);
        w.Write((byte)Tileset);
        w.Write(CustomTileset ? 1 : 0);
        w.Write(GroundTilesets.Count);
        foreach (var id in GroundTilesets)
            w.Write(Encoding.ASCII.GetBytes(id));
        w.Write(CliffTilesets.Count);
        foreach (var id in CliffTilesets)
            w.Write(Encoding.ASCII.GetBytes(id));
        w.Write(Width);
        w.Write(Height);
        w.Write(CenterOffsetX);
        w.Write(CenterOffsetY);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var c = Corners[y * Width + x];
                w.Write((short)(c.GroundHeight * 512f + 8192));
                int we = (int)(c.WaterHeight * 512f) + 8192;
                if (c.MapEdge) we |= 0x4000;
                w.Write((ushort)we);
                if (Version >= 12)
                {
                    int tf16 = c.GroundTexture & 0x3F;
                    if (c.Ramp) tf16 |= 0x40;
                    if (c.Blight) tf16 |= 0x80;
                    if (c.Water) tf16 |= 0x100;
                    if (c.Boundary) tf16 |= 0x200;
                    w.Write((ushort)tf16);
                }
                else
                {
                    int tf = c.GroundTexture & 0x0F;
                    if (c.Ramp) tf |= 0x10;
                    if (c.Blight) tf |= 0x20;
                    if (c.Water) tf |= 0x40;
                    if (c.Boundary) tf |= 0x80;
                    w.Write((byte)tf);
                }
                w.Write((byte)(((c.CliffVariation & 7) << 5) | (c.GroundVariation & 0x1F)));
                w.Write((byte)(((c.CliffTexture & 0x0F) << 4) | (c.LayerHeight & 0x0F)));
            }
        }
        return ms.ToArray();
    }
}
