using System.Text;

namespace W3Compat.Formats;

/// <summary>一个放置的装饰物或单位，位置使用 W3 世界坐标。</summary>
public sealed class W3Placement
{
    public string Id = "";
    public int Variation;
    public float X, Y, Z;
    public float Angle;
    public float ScaleX = 1, ScaleY = 1, ScaleZ = 1;
    public string SkinId = "";
    public int Flags = 2;
    public int PlayerOwner;
    public int Life = 100;
    public bool IsUnit;
    public int CreationNumber;

    /// <summary>原始磁盘记录，写回时用于保留未建模字段。</summary>
    public byte[]? RawRecord;

    /// <summary>d1map 放置扩展负载。</summary>
    public byte[] NativeExt = Array.Empty<byte>();

    /// <summary>统一 d1map 放置流中的原始索引，负数代表新增放置。</summary>
    public int NativeOrder = -1;

    /// <summary>当前放置是否为起始点标记。</summary>
    public bool IsStartLocation => Id == "sloc";

    /// <summary>创建当前放置的浅副本。</summary>
    public W3Placement Clone() => (W3Placement)MemberwiseClone();
}

/// <summary>已解析的 war3map.doo 或 war3mapUnits.doo 文档。</summary>
public sealed class W3DooDocument
{
    public bool IsUnits;
    public int FileVersion = 8;
    public int Subversion = 11;
    public bool HasSkinIds;
    public List<W3Placement> Placements = new();

    /// <summary>装饰物文件的地形装饰物尾段，原样写回。</summary>
    public byte[] SpecialTrailer = Array.Empty<byte>();

    /// <summary>返回下一个可用的创建编号。</summary>
    public int NextCreationNumber()
    {
        int max = -1;
        foreach (W3Placement placement in Placements)
            max = Math.Max(max, placement.CreationNumber);
        return max + 1;
    }

    /// <summary>将地形装饰物尾段转换为普通放置记录。</summary>
    public IReadOnlyList<W3Placement> ReadSpecialPlacements(
        int terrainWidth,
        int terrainHeight,
        float centerOffsetX,
        float centerOffsetY,
        Func<int, int, float> getCornerWorldHeight,
        float tileSize = 128f)
    {
        var result = new List<W3Placement>();
        if (IsUnits || SpecialTrailer.Length == 0)
            return result;
        if (terrainWidth <= 0 || terrainHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(terrainWidth), "地形角点尺寸必须为正数");
        if (SpecialTrailer.Length < 8)
            throw new InvalidDataException("special doodad trailer is truncated");

        using var stream = new MemoryStream(SpecialTrailer, writable: false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != 0)
            throw new InvalidDataException("unsupported special doodad version");
        int count = reader.ReadInt32();
        if (count < 0 || count > (SpecialTrailer.Length - 8) / 16 ||
            SpecialTrailer.Length != 8 + count * 16)
            throw new InvalidDataException("invalid special doodad trailer");

        for (int i = 0; i < count; i++)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            int variation = reader.ReadInt32();
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int cornerX = Math.Clamp(x, 0, terrainWidth - 1);
            int cornerY = Math.Clamp(y, 0, terrainHeight - 1);
            result.Add(new W3Placement
            {
                Id = id,
                Variation = variation,
                X = centerOffsetX + x * tileSize,
                Y = centerOffsetY + y * tileSize,
                Z = getCornerWorldHeight(cornerX, cornerY),
                IsUnit = false,
                CreationNumber = int.MinValue + i,
                NativeExt = Encoding.ASCII.GetBytes("W3SD"),
            });
        }
        return result;
    }

    /// <summary>创建与空白地图匹配的装饰物文档。</summary>
    public static W3DooDocument CreateBlankDoodads(bool reforged) => new()
    {
        IsUnits = false,
        FileVersion = 8,
        Subversion = 11,
        HasSkinIds = reforged,
        SpecialTrailer = new byte[8],
    };

    /// <summary>创建与空白地图匹配的单位文档。</summary>
    public static W3DooDocument CreateBlankUnits(bool reforged) => new()
    {
        IsUnits = true,
        FileVersion = 8,
        Subversion = 11,
        HasSkinIds = reforged,
    };

    /// <summary>写出可保留原始未建模字段的 DOO 文件字节。</summary>
    public byte[] Write()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("W3do"));
        writer.Write(FileVersion);
        writer.Write(Subversion);
        writer.Write(Placements.Count);
        foreach (W3Placement placement in Placements)
        {
            if (IsUnits)
                WriteUnit(writer, placement);
            else
                WriteDoodad(writer, placement);
        }
        if (!IsUnits)
            writer.Write(SpecialTrailer.Length >= 8 ? SpecialTrailer : new byte[8]);
        return stream.ToArray();
    }

    private static void WriteFourcc(BinaryWriter writer, string id)
    {
        Span<byte> bytes = stackalloc byte[4];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i < id.Length ? id[i] : ' ');
        writer.Write(bytes);
    }

    private void WriteHead(BinaryWriter writer, W3Placement placement)
    {
        WriteFourcc(writer, placement.Id);
        writer.Write(placement.Variation);
        writer.Write(placement.X);
        writer.Write(placement.Y);
        writer.Write(placement.Z);
        writer.Write(placement.Angle);
        writer.Write(placement.ScaleX);
        writer.Write(placement.ScaleY);
        writer.Write(placement.ScaleZ);
        if (HasSkinIds)
            WriteFourcc(writer, string.IsNullOrEmpty(placement.SkinId) ? placement.Id : placement.SkinId);
        writer.Write((byte)placement.Flags);
    }

    private void WriteDoodad(BinaryWriter writer, W3Placement placement)
    {
        if (placement.RawRecord is not null)
        {
            byte[] raw = (byte[])placement.RawRecord.Clone();
            using var patchStream = new MemoryStream(raw);
            using var patchWriter = new BinaryWriter(patchStream);
            WriteHead(patchWriter, placement);
            patchWriter.Write((byte)placement.Life);
            BitConverter.GetBytes(placement.CreationNumber).CopyTo(raw, raw.Length - 4);
            writer.Write(raw);
            return;
        }

        WriteHead(writer, placement);
        writer.Write((byte)placement.Life);
        if (FileVersion >= 8)
        {
            writer.Write(-1);
            writer.Write(0);
        }
        writer.Write(placement.CreationNumber);
    }

    private void WriteUnit(BinaryWriter writer, W3Placement placement)
    {
        if (placement.RawRecord is not null)
        {
            byte[] raw = (byte[])placement.RawRecord.Clone();
            using var patchStream = new MemoryStream(raw);
            using var patchWriter = new BinaryWriter(patchStream);
            WriteHead(patchWriter, placement);
            patchWriter.Write(placement.PlayerOwner);
            BitConverter.GetBytes(placement.CreationNumber).CopyTo(raw, raw.Length - 4);
            writer.Write(raw);
            return;
        }

        WriteHead(writer, placement);
        writer.Write(placement.PlayerOwner);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(-1);
        writer.Write(-1);
        if (Subversion >= 11)
            writer.Write(0xFFFF);
        writer.Write(0);
        writer.Write(12500);
        writer.Write(-1f);
        writer.Write(1);
        if (Subversion >= 11)
        {
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        }
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(-1);
        writer.Write(-1);
        writer.Write(placement.CreationNumber);
    }
}

/// <summary>war3map.doo 与 war3mapUnits.doo 的二进制读取器。</summary>
public static class W3DooFile
{
    private sealed class Reader
    {
        public Reader(byte[] data) => Data = data;
        public byte[] Data { get; }
        public int Position { get; private set; }
        public int Remaining => Data.Length - Position;
        public string Fourcc() { string value = Encoding.ASCII.GetString(Data, Position, 4); Position += 4; return value; }
        public int I32() { int value = BitConverter.ToInt32(Data, Position); Position += 4; return value; }
        public float F32() { float value = BitConverter.ToSingle(Data, Position); Position += 4; return value; }
        public byte U8() => Data[Position++];
        public void Skip(int count) => Position += count;
    }

    /// <summary>解析装饰物文件并返回放置记录。</summary>
    public static List<W3Placement> ParseDoodads(byte[] data, bool hasSkinIds) =>
        ParseDoodadsDocument(data, hasSkinIds).Placements;

    /// <summary>解析单位文件并返回放置记录。</summary>
    public static List<W3Placement> ParseUnits(byte[] data, bool hasSkinIds) =>
        ParseUnitsDocument(data, hasSkinIds).Placements;

    /// <summary>解析保留原始记录的装饰物文档。</summary>
    public static W3DooDocument ParseDoodadsDocument(byte[] data, bool hasSkinIds)
    {
        var document = new W3DooDocument { IsUnits = false, HasSkinIds = hasSkinIds, FileVersion = 8, Subversion = 11 };
        if (data.Length < 12)
            return document;
        var reader = new Reader(data);
        if (reader.Fourcc() != "W3do")
            return document;
        document.FileVersion = reader.I32();
        document.Subversion = reader.I32();
        int count = Math.Clamp(reader.I32(), 0, 100000);
        for (int i = 0; i < count; i++)
        {
            if (reader.Remaining < (hasSkinIds ? 46 : 42))
                break;
            int recordStart = reader.Position;
            var placement = new W3Placement { IsUnit = false, Id = reader.Fourcc() };
            placement.Variation = reader.I32();
            placement.X = reader.F32(); placement.Y = reader.F32(); placement.Z = reader.F32();
            placement.Angle = reader.F32();
            placement.ScaleX = reader.F32(); placement.ScaleY = reader.F32(); placement.ScaleZ = reader.F32();
            placement.SkinId = hasSkinIds ? reader.Fourcc() : placement.Id;
            placement.Flags = reader.U8();
            placement.Life = reader.U8();
            if (document.FileVersion >= 8)
            {
                if (reader.Remaining < 8)
                    break;
                reader.I32();
                int itemSets = Math.Clamp(reader.I32(), 0, 256);
                if (!SkipItemSets(reader, itemSets))
                    break;
            }
            if (reader.Remaining < 4)
                break;
            placement.CreationNumber = reader.I32();
            placement.RawRecord = data[recordStart..reader.Position];
            document.Placements.Add(placement);
        }
        if (reader.Remaining >= 4)
            document.SpecialTrailer = data[reader.Position..];
        return document;
    }

    /// <summary>解析保留原始记录的单位文档。</summary>
    public static W3DooDocument ParseUnitsDocument(byte[] data, bool hasSkinIds)
    {
        var document = new W3DooDocument { IsUnits = true, HasSkinIds = hasSkinIds, FileVersion = 8, Subversion = 11 };
        if (data.Length < 12)
            return document;
        var reader = new Reader(data);
        if (reader.Fourcc() != "W3do")
            return document;
        document.FileVersion = reader.I32();
        document.Subversion = reader.I32();
        int count = Math.Clamp(reader.I32(), 0, 100000);
        for (int i = 0; i < count; i++)
        {
            if (reader.Remaining < (hasSkinIds ? 54 : 50))
                break;
            int recordStart = reader.Position;
            var placement = new W3Placement { IsUnit = true, Id = reader.Fourcc() };
            placement.Variation = reader.I32();
            placement.X = reader.F32(); placement.Y = reader.F32(); placement.Z = reader.F32();
            placement.Angle = reader.F32();
            placement.ScaleX = reader.F32(); placement.ScaleY = reader.F32(); placement.ScaleZ = reader.F32();
            placement.SkinId = hasSkinIds ? reader.Fourcc() : placement.Id;
            placement.Flags = reader.U8();
            placement.PlayerOwner = reader.I32();
            if (reader.Remaining < 2)
                break;
            reader.U8(); reader.U8();
            if (reader.Remaining < 8)
                break;
            reader.I32(); reader.I32();
            if (document.Subversion >= 11)
            {
                if (reader.Remaining < 4)
                    break;
                reader.I32();
            }
            if (reader.Remaining < 4 || !SkipItemSets(reader, Math.Clamp(reader.I32(), 0, 256)))
                break;
            if (reader.Remaining < 12)
                break;
            reader.I32(); reader.F32(); reader.I32();
            if (document.Subversion >= 11)
            {
                if (reader.Remaining < 12)
                    break;
                reader.I32(); reader.I32(); reader.I32();
            }
            if (reader.Remaining < 4 || !SkipInventory(reader, Math.Clamp(reader.I32(), 0, 256)))
                break;
            if (reader.Remaining < 4 || !SkipAbilities(reader, Math.Clamp(reader.I32(), 0, 256)))
                break;
            if (reader.Remaining < 4 || !SkipRandom(reader, reader.I32()))
                break;
            if (reader.Remaining < 12)
                break;
            reader.I32(); reader.I32();
            placement.CreationNumber = reader.I32();
            placement.RawRecord = data[recordStart..reader.Position];
            document.Placements.Add(placement);
        }
        return document;
    }

    private static bool SkipItemSets(Reader reader, int itemSets)
    {
        for (int set = 0; set < itemSets; set++)
        {
            if (reader.Remaining < 4)
                return false;
            int count = Math.Clamp(reader.I32(), 0, 256);
            if (reader.Remaining < count * 8)
                return false;
            reader.Skip(count * 8);
        }
        return true;
    }

    private static bool SkipInventory(Reader reader, int count)
    {
        if (reader.Remaining < count * 8)
            return false;
        reader.Skip(count * 8);
        return true;
    }

    private static bool SkipAbilities(Reader reader, int count)
    {
        if (reader.Remaining < count * 12)
            return false;
        reader.Skip(count * 12);
        return true;
    }

    private static bool SkipRandom(Reader reader, int randomType)
    {
        if (randomType == 0)
        {
            if (reader.Remaining < 4)
                return false;
            reader.Skip(4);
        }
        else if (randomType == 1)
        {
            if (reader.Remaining < 8)
                return false;
            reader.Skip(8);
        }
        else if (randomType == 2)
        {
            if (reader.Remaining < 4)
                return false;
            int count = Math.Clamp(reader.I32(), 0, 256);
            if (reader.Remaining < count * 8)
                return false;
            reader.Skip(count * 8);
        }
        return true;
    }
}
