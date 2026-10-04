using System.Buffers.Binary;
using System.Text;

namespace Diablo1Like.W3.Terrain;

/// <summary>
/// Pure C# representation of war3map.w3e. Field decoding follows Warsmash
/// War3MapW3e/Corner byte-for-byte (v11 7-byte corners) and HiveWE terrain.ixx
/// (v12 8-byte corners). This type is engine-agnostic: the Godot runtime and
/// the standalone editor renderer must both consume this exact model.
/// </summary>
public sealed class W3TerrainMap
{
    public const float TileSize = 128.0f;
    public const int MaxTilesets = 64;
    public const int MaxCornerDimension = 4097;

    public sealed class Corner
    {
        public float GroundHeight;
        public float WaterHeight;
        public bool MapEdge;
        public bool Ramp;
        public bool Blight;
        public bool Water;
        public bool Boundary;
        public byte GroundTexture;
        public byte CliffVariation;
        public byte GroundVariation;
        public byte CliffTexture;
        public byte LayerHeight;

        // Warsmash RenderCorner runtime state.
        internal bool RampSuppressed;
        /// <summary>Warsmash render-time validation; Ramp remains the saved authored flag.</summary>
        public bool EffectiveRamp => Ramp && !RampSuppressed;
        public bool Cliff;
        public bool Romp;
        public bool HideCliff;

        public float FinalGroundHeight => GroundHeight + LayerHeight - 2.0f;
        public float WorldHeight => FinalGroundHeight * TileSize;
    }

    public int Version { get; set; } = 11;
    public char Tileset { get; set; } = 'A';
    public bool CustomTileset { get; set; }
    public List<string> GroundTilesets { get; } = new();
    public List<string> CliffTilesets { get; } = new();
    public int Width { get; set; }
    public int Height { get; set; }
    public float CenterOffsetX { get; set; }
    public float CenterOffsetY { get; set; }
    public Corner[] Corners { get; set; } = Array.Empty<Corner>();

    public int TileWidth => Math.Max(0, Width - 1);
    public int TileHeight => Math.Max(0, Height - 1);

    public Corner GetCorner(int x, int y) => Corners[(y * Width) + x];
    public bool ContainsCorner(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public bool ContainsTile(int x, int y) => x >= 0 && y >= 0 && x < TileWidth && y < TileHeight;

    /// <summary>Copies authored data only. Derived render flags belong to a build.</summary>
    public W3TerrainMap CreateSnapshot()
    {
        var copy = new W3TerrainMap
        {
            Version = Version, Tileset = Tileset, CustomTileset = CustomTileset,
            Width = Width, Height = Height, CenterOffsetX = CenterOffsetX, CenterOffsetY = CenterOffsetY,
            Corners = Corners.Select(c => new Corner
            {
                GroundHeight = c.GroundHeight, WaterHeight = c.WaterHeight,
                MapEdge = c.MapEdge, Ramp = c.Ramp, Blight = c.Blight, Water = c.Water, Boundary = c.Boundary,
                GroundTexture = c.GroundTexture, GroundVariation = c.GroundVariation,
                CliffTexture = c.CliffTexture, CliffVariation = c.CliffVariation, LayerHeight = c.LayerHeight,
            }).ToArray(),
        };
        copy.GroundTilesets.AddRange(GroundTilesets);
        copy.CliffTilesets.AddRange(CliffTilesets);
        return copy;
    }

    public static W3TerrainMap LoadFile(string path) => Parse(File.ReadAllBytes(path));

    public static W3TerrainMap Parse(ReadOnlySpan<byte> bytes)
    {
        var reader = new SpanReader(bytes);
        if (reader.ReadFourCc() != "W3E!")
            throw new InvalidDataException("war3map.w3e has an invalid magic value.");

        var map = new W3TerrainMap
        {
            Version = reader.ReadInt32(),
            Tileset = (char)reader.ReadByte(),
            CustomTileset = reader.ReadInt32() != 0,
        };

        int groundCount = reader.ReadCount(MaxTilesets, "ground tilesets");
        for (int i = 0; i < groundCount; i++)
            map.GroundTilesets.Add(reader.ReadFourCc());
        int cliffCount = reader.ReadCount(MaxTilesets, "cliff tilesets");
        for (int i = 0; i < cliffCount; i++)
            map.CliffTilesets.Add(reader.ReadFourCc());

        map.Width = reader.ReadInt32();
        map.Height = reader.ReadInt32();
        map.CenterOffsetX = reader.ReadSingle();
        map.CenterOffsetY = reader.ReadSingle();
        if (map.Width < 2 || map.Height < 2 || map.Width > MaxCornerDimension || map.Height > MaxCornerDimension)
            throw new InvalidDataException($"Invalid W3E corner dimensions {map.Width}x{map.Height}.");

        bool wide = map.Version >= 12;
        int cornerCount = checked(map.Width * map.Height);
        reader.Require(checked(cornerCount * (wide ? 8 : 7)));
        map.Corners = new Corner[cornerCount];
        for (int i = 0; i < cornerCount; i++)
        {
            short encodedGround = reader.ReadInt16();
            ushort waterAndEdge = reader.ReadUInt16();
            var corner = new Corner
            {
                GroundHeight = (encodedGround - 8192) / 512.0f,
                WaterHeight = ((waterAndEdge & 0x3FFF) - 8192) / 512.0f,
                MapEdge = (waterAndEdge & 0x4000) != 0,
            };
            if (wide)
            {
                // HiveWE terrain.ixx v12: 16-bit field, 6-bit texture, flags at bits 6-9.
                ushort textureAndFlags = reader.ReadUInt16();
                corner.GroundTexture = (byte)(textureAndFlags & 0x3F);
                corner.Ramp = (textureAndFlags & 0x40) != 0;
                corner.Blight = (textureAndFlags & 0x80) != 0;
                corner.Water = (textureAndFlags & 0x100) != 0;
                corner.Boundary = (textureAndFlags & 0x200) != 0;
            }
            else
            {
                byte textureAndFlags = reader.ReadByte();
                corner.GroundTexture = (byte)(textureAndFlags & 0x0F);
                corner.Ramp = (textureAndFlags & 0x10) != 0;
                corner.Blight = (textureAndFlags & 0x20) != 0;
                corner.Water = (textureAndFlags & 0x40) != 0;
                corner.Boundary = (textureAndFlags & 0x80) != 0;
            }
            byte variation = reader.ReadByte();
            byte cliffAndLayer = reader.ReadByte();
            corner.CliffVariation = (byte)((variation & 0xE0) >> 5);
            corner.GroundVariation = (byte)(variation & 0x1F);
            corner.CliffTexture = (byte)((cliffAndLayer & 0xF0) >> 4);
            corner.LayerHeight = (byte)(cliffAndLayer & 0x0F);
            map.Corners[i] = corner;
        }
        map.InitializeRenderCorners();
        return map;
    }

    public byte[] SaveBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("W3E!"));
        writer.Write(Version);
        writer.Write((byte)Tileset);
        writer.Write(CustomTileset ? 1 : 0);
        writer.Write(GroundTilesets.Count);
        foreach (string id in GroundTilesets)
            writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write(CliffTilesets.Count);
        foreach (string id in CliffTilesets)
            writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write(Width);
        writer.Write(Height);
        writer.Write(CenterOffsetX);
        writer.Write(CenterOffsetY);
        bool wide = Version >= 12;
        foreach (Corner c in Corners)
        {
            writer.Write((short)(c.GroundHeight * 512f + 8192));
            int waterAndEdge = (int)(c.WaterHeight * 512f) + 8192;
            if (c.MapEdge) waterAndEdge |= 0x4000;
            writer.Write((ushort)waterAndEdge);
            if (wide)
            {
                int tf = c.GroundTexture & 0x3F;
                if (c.Ramp) tf |= 0x40;
                if (c.Blight) tf |= 0x80;
                if (c.Water) tf |= 0x100;
                if (c.Boundary) tf |= 0x200;
                writer.Write((ushort)tf);
            }
            else
            {
                int tf = c.GroundTexture & 0x0F;
                if (c.Ramp) tf |= 0x10;
                if (c.Blight) tf |= 0x20;
                if (c.Water) tf |= 0x40;
                if (c.Boundary) tf |= 0x80;
                writer.Write((byte)tf);
            }
            writer.Write((byte)(((c.CliffVariation & 7) << 5) | (c.GroundVariation & 0x1F)));
            writer.Write((byte)(((c.CliffTexture & 0x0F) << 4) | (c.LayerHeight & 0x0F)));
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Recomputes Warsmash RenderCorner flags (Cliff). Must run after any edit
    /// that changes layer heights, before cliff placement or ground packing.
    /// </summary>
    public void InitializeRenderCorners()
    {
        foreach (Corner corner in Corners)
        {
            corner.RampSuppressed = false;
            corner.Cliff = false;
            corner.Romp = false;
            corner.HideCliff = false;
        }
        for (int y = 0; y < TileHeight; y++)
        {
            for (int x = 0; x < TileWidth; x++)
            {
                Corner bl = GetCorner(x, y);
                Corner br = GetCorner(x + 1, y);
                Corner tl = GetCorner(x, y + 1);
                Corner tr = GetCorner(x + 1, y + 1);
                bl.Cliff = bl.LayerHeight != br.LayerHeight || bl.LayerHeight != tl.LayerHeight || bl.LayerHeight != tr.LayerHeight;
            }
        }
    }

    public bool IsRampEntrance(int x, int y)
    {
        if (!ContainsTile(x, y))
            return false;
        Corner bl = GetCorner(x, y);
        Corner br = GetCorner(x + 1, y);
        Corner tl = GetCorner(x, y + 1);
        Corner tr = GetCorner(x + 1, y + 1);
        return bl.EffectiveRamp && br.EffectiveRamp && tl.EffectiveRamp && tr.EffectiveRamp &&
            !(bl.LayerHeight == tr.LayerHeight && tl.LayerHeight == br.LayerHeight);
    }

    public float FinalGroundHeightWithRamp(int x, int y)
    {
        Corner corner = GetCorner(x, y);
        float value = corner.FinalGroundHeight;
        for (int tileY = y - 1; tileY <= y; tileY++)
        {
            for (int tileX = x - 1; tileX <= x; tileX++)
            {
                if (!IsRampEntrance(tileX, tileY))
                    continue;
                int baseLayer = Math.Min(
                    Math.Min(GetCorner(tileX, tileY).LayerHeight, GetCorner(tileX + 1, tileY).LayerHeight),
                    Math.Min(GetCorner(tileX, tileY + 1).LayerHeight, GetCorner(tileX + 1, tileY + 1).LayerHeight));
                if (corner.LayerHeight == baseLayer)
                    return value + 0.5f;
            }
        }
        return value;
    }

    private ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public SpanReader(ReadOnlySpan<byte> data) { _data = data; _offset = 0; }
        public void Require(int count)
        {
            if (count < 0 || _offset > _data.Length - count)
                throw new EndOfStreamException("Unexpected end of W3E data.");
        }
        public byte ReadByte() { Require(1); return _data[_offset++]; }
        public short ReadInt16() { Require(2); short value = BinaryPrimitives.ReadInt16LittleEndian(_data[_offset..]); _offset += 2; return value; }
        public ushort ReadUInt16() { Require(2); ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_offset..]); _offset += 2; return value; }
        public int ReadInt32() { Require(4); int value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]); _offset += 4; return value; }
        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
        public string ReadFourCc() { Require(4); string value = Encoding.ASCII.GetString(_data.Slice(_offset, 4)); _offset += 4; return value; }
        public int ReadCount(int maximum, string label)
        {
            int value = ReadInt32();
            if (value < 0 || value > maximum)
                throw new InvalidDataException($"Invalid {label} count {value}.");
            return value;
        }
    }
}
