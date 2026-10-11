using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Diablo1Like.W3.Terrain;

namespace Diablo1Like.MapFormat;

/// <summary>
/// Native map folder format v1 (design: "semantics from the corner model,
/// container like glTF"). A map is a folder:
///
///   map.json        — manifest: format/version/name, texture id lists, legacy block
///   terrain.bin     — sectioned binary: INFO + CORN (16 bytes per corner)
///   placements.bin  — sectioned binary: PLCM records
///
/// Binary framing (both .bin files, little-endian):
///   u32 magic ("D1TE" terrain / "D1PL" placements), u32 container version,
///   then sections until EOF: [4CC tag][u32 payload length][payload].
/// Readers skip unknown sections but preserve them (re-emitted after known
/// sections on save). Serialization is deterministic: fixed field order,
/// fixed section order, no locale-dependent text.
/// </summary>
public static class NativeMapFormat
{
    public const int Version = 1;
    private const uint TerrainMagic = 0x4554_3144;    // "D1TE" little-endian
    private const uint PlacementsMagic = 0x4C50_3144; // "D1PL"

    public const string ManifestFile = "map.json";
    public const string TerrainFile = "terrain.bin";
    public const string PlacementsFile = "placements.bin";

    // Corner flag bits (CORN section).
    private const ushort FlagMapEdge = 1 << 0;
    private const ushort FlagRamp = 1 << 1;
    private const ushort FlagBlight = 1 << 2;
    private const ushort FlagWater = 1 << 3;
    private const ushort FlagBoundary = 1 << 4;

    public static bool IsNativeMapFolder(string dir) => File.Exists(Path.Combine(dir, ManifestFile));

    // ------------------------------------------------------------------
    // Save
    // ------------------------------------------------------------------

    public static void Save(NativeMap map, string dir)
    {
        // Serialize every component before touching the current generation.
        // A malformed placement, overflow, etc. therefore cannot create a
        // mixed map.json/terrain.bin/placements.bin generation.
        IReadOnlyList<(string Name, byte[] Data)> files = SerializeFiles(map);
        Directory.CreateDirectory(dir);
        WriteTransactionally(dir, files);
    }

    /// <summary>Serializes a complete native map without touching disk.</summary>
    public static IReadOnlyList<(string Name, byte[] Data)> SerializeFiles(NativeMap map) =>
        new (string Name, byte[] Data)[]
        {
            (ManifestFile, WriteManifest(map)),
            (TerrainFile, WriteTerrain(map)),
            (PlacementsFile, WritePlacements(map)),
        };

    private static void WriteTransactionally(
        string dir,
        IReadOnlyList<(string Name, byte[] Data)> files)
    {
        string token = $"{Environment.ProcessId}-{Guid.NewGuid():N}";
        var temps = new Dictionary<string, string>(StringComparer.Ordinal);
        var backups = new Dictionary<string, string>(StringComparer.Ordinal);
        var committed = new List<string>();
        try
        {
            foreach ((string name, byte[] data) in files)
            {
                string target = Path.Combine(dir, name);
                string temp = target + $".tmp-{token}";
                // Flush to disk before rename: the rename alone can be journaled
                // ahead of unflushed data blocks on power loss, leaving an empty
                // file under the final name after the backup is gone.
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(flushToDisk: true);
                }
                temps[target] = temp;
            }
            foreach ((string target, _) in temps)
            {
                if (!File.Exists(target))
                    continue;
                string backup = target + $".bak-{token}";
                File.Copy(target, backup);
                backups[target] = backup;
            }
            foreach ((string target, string temp) in temps)
            {
                File.Move(temp, target, overwrite: true);
                committed.Add(target);
            }
        }
        catch
        {
            for (int i = committed.Count - 1; i >= 0; i--)
            {
                string target = committed[i];
                if (backups.TryGetValue(target, out string? backup)
                    && File.Exists(backup))
                {
                    File.Move(backup, target, overwrite: true);
                    backups.Remove(target);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
            throw;
        }
        finally
        {
            foreach (string temp in temps.Values)
                if (File.Exists(temp))
                    File.Delete(temp);
            foreach (string backup in backups.Values)
                if (File.Exists(backup))
                    File.Delete(backup);
        }
    }

    private static byte[] WriteManifest(NativeMap map)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            var t = map.Terrain;
            w.WriteStartObject();
            w.WriteString("format", "d1map");
            w.WriteNumber("version", Version);
            w.WriteString("name", map.Name);
            w.WriteStartArray("groundTextures");
            foreach (string id in t.GroundTilesets) w.WriteStringValue(id);
            w.WriteEndArray();
            w.WriteStartArray("cliffTextures");
            foreach (string id in t.CliffTilesets) w.WriteStringValue(id);
            w.WriteEndArray();
            // War3-imported metadata needed to keep the old toolchain usable
            // during the dual-track era; native-authored maps may omit it.
            w.WriteStartObject("legacy");
            w.WriteString("tileset", t.Tileset.ToString());
            w.WriteBoolean("customTileset", t.CustomTileset);
            w.WriteNumber("w3eVersion", t.Version);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        return NormalizeNewlines(stream.ToArray());
    }

    /// <summary>
    /// Utf8JsonWriter's indented output uses Environment.NewLine before .NET 9,
    /// which would make map.json bytes differ between Windows and Linux writers.
    /// Raw CR/LF bytes are illegal inside JSON strings (they are always escaped),
    /// so rewriting CRLF to LF here cannot corrupt values.
    /// </summary>
    private static byte[] NormalizeNewlines(byte[] bytes)
    {
        int crlf = 0;
        for (int i = 0; i + 1 < bytes.Length; i++)
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n')
                crlf++;
        if (crlf == 0)
            return bytes;
        var result = new byte[bytes.Length - crlf];
        int o = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')
                continue;
            result[o++] = bytes[i];
        }
        return result;
    }

    private static byte[] WriteTerrain(NativeMap map)
    {
        var t = map.Terrain;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(TerrainMagic);
        w.Write((uint)Version);

        // INFO
        BeginSection(w, "INFO", 16);
        w.Write(t.Width);
        w.Write(t.Height);
        w.Write(t.CenterOffsetX);
        w.Write(t.CenterOffsetY);

        // CORN: 16 bytes per corner, row-major.
        BeginSection(w, "CORN", t.Corners.Length * 16);
        foreach (var c in t.Corners)
        {
            w.Write(c.GroundHeight);
            w.Write(c.WaterHeight);
            ushort flags = 0;
            if (c.MapEdge) flags |= FlagMapEdge;
            if (c.Ramp) flags |= FlagRamp;
            if (c.Blight) flags |= FlagBlight;
            if (c.Water) flags |= FlagWater;
            if (c.Boundary) flags |= FlagBoundary;
            w.Write(flags);
            w.Write(c.GroundTexture);
            w.Write(c.GroundVariation);
            w.Write(c.CliffTexture);
            w.Write(c.CliffVariation);
            w.Write(c.LayerHeight);
            w.Write((byte)0); // pad
        }

        WriteUnknownSections(w, map.UnknownTerrainSections);
        return stream.ToArray();
    }

    private static byte[] WritePlacements(NativeMap map)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(PlacementsMagic);
        w.Write((uint)Version);

        using var body = new MemoryStream();
        using (var b = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
        {
            b.Write(map.Placements.Count);
            foreach (var p in map.Placements)
            {
                b.Write((byte)(p.IsUnit ? 1 : 0));
                byte[] id = Encoding.UTF8.GetBytes(p.Id);
                if (id.Length > byte.MaxValue)
                    throw new InvalidDataException($"placement id too long: {p.Id}");
                b.Write((byte)id.Length);
                b.Write(id);
                b.Write(p.Variation);
                b.Write(p.X); b.Write(p.Y); b.Write(p.Z);
                b.Write(p.AngleRad);
                b.Write(p.ScaleX); b.Write(p.ScaleY); b.Write(p.ScaleZ);
                b.Write(p.Owner);
                b.Write(p.Flags);
                b.Write(p.RefId);
                if (p.Ext.Length > ushort.MaxValue)
                    throw new InvalidDataException($"placement ext too long: {p.Id}");
                b.Write((ushort)p.Ext.Length);
                b.Write(p.Ext);
            }
        }
        BeginSection(w, "PLCM", (int)body.Length);
        body.WriteTo(stream);

        WriteUnknownSections(w, map.UnknownPlacementSections);
        return stream.ToArray();
    }

    private static void BeginSection(BinaryWriter w, string tag, int length)
    {
        w.Write(Encoding.ASCII.GetBytes(tag));
        w.Write(length);
    }

    private static void WriteUnknownSections(BinaryWriter w, List<(string Tag, byte[] Data)> sections)
    {
        foreach ((string tag, byte[] data) in sections)
        {
            BeginSection(w, tag, data.Length);
            w.Write(data);
        }
    }

    // ------------------------------------------------------------------
    // Load
    // ------------------------------------------------------------------

    public static NativeMap Load(string dir)
    {
        var map = new NativeMap();
        ReadManifest(map, File.ReadAllBytes(Path.Combine(dir, ManifestFile)));
        ReadTerrain(map, File.ReadAllBytes(Path.Combine(dir, TerrainFile)));
        ReadPlacements(map, File.ReadAllBytes(Path.Combine(dir, PlacementsFile)));
        map.Terrain.InitializeRenderCorners();
        return map;
    }

    private static void ReadManifest(NativeMap map, byte[] bytes)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("map.json is not valid JSON", ex);
        }
        using var _ = doc;
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var format)
            || format.GetString() != "d1map")
            throw new InvalidDataException("map.json is not a d1map manifest");
        if (!root.TryGetProperty("version", out var versionProp)
            || versionProp.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException("map.json is missing a numeric \"version\"");
        int version = versionProp.GetInt32();
        if (version is < 1 or > Version)
            throw new InvalidDataException($"unsupported d1map version {version}");
        map.Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
        var t = map.Terrain;
        if (root.TryGetProperty("groundTextures", out var ground))
            foreach (var id in ground.EnumerateArray())
                t.GroundTilesets.Add(id.GetString() ?? "");
        if (root.TryGetProperty("cliffTextures", out var cliff))
            foreach (var id in cliff.EnumerateArray())
                t.CliffTilesets.Add(id.GetString() ?? "");
        if (root.TryGetProperty("legacy", out var legacy))
        {
            if (legacy.TryGetProperty("tileset", out var ts) && ts.GetString() is { Length: 1 } s)
                t.Tileset = s[0];
            if (legacy.TryGetProperty("customTileset", out var ct))
                t.CustomTileset = ct.GetBoolean();
            if (legacy.TryGetProperty("w3eVersion", out var wv))
                t.Version = wv.GetInt32();
        }
    }

    private static void ReadTerrain(NativeMap map, byte[] bytes)
    {
        var t = map.Terrain;
        bool sawInfo = false, sawCorners = false;
        foreach ((string tag, ReadOnlyMemory<byte> payload) in ReadSections(bytes, TerrainMagic, TerrainFile))
        {
            var span = payload.Span;
            switch (tag)
            {
                case "INFO":
                    if (sawInfo)
                        throw new InvalidDataException("terrain.bin: duplicate INFO section");
                    if (span.Length < 16)
                        throw new InvalidDataException("INFO section too short");
                    t.Width = BinaryPrimitives.ReadInt32LittleEndian(span);
                    t.Height = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
                    t.CenterOffsetX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span[8..]));
                    t.CenterOffsetY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span[12..]));
                    if (t.Width < 2 || t.Height < 2
                        || t.Width > W3TerrainMap.MaxCornerDimension || t.Height > W3TerrainMap.MaxCornerDimension)
                        throw new InvalidDataException($"invalid terrain dimensions {t.Width}x{t.Height}");
                    sawInfo = true;
                    break;

                case "CORN":
                {
                    if (sawCorners)
                        throw new InvalidDataException("terrain.bin: duplicate CORN section");
                    if (!sawInfo)
                        throw new InvalidDataException("CORN section before INFO");
                    int count = t.Width * t.Height;
                    if (span.Length != count * 16)
                        throw new InvalidDataException($"CORN length {span.Length} != {count}*16");
                    t.Corners = new W3TerrainMap.Corner[count];
                    for (int i = 0; i < count; i++)
                    {
                        var s = span.Slice(i * 16, 16);
                        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(s[8..]);
                        t.Corners[i] = new W3TerrainMap.Corner
                        {
                            GroundHeight = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(s)),
                            WaterHeight = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(s[4..])),
                            MapEdge = (flags & FlagMapEdge) != 0,
                            Ramp = (flags & FlagRamp) != 0,
                            Blight = (flags & FlagBlight) != 0,
                            Water = (flags & FlagWater) != 0,
                            Boundary = (flags & FlagBoundary) != 0,
                            GroundTexture = s[10],
                            GroundVariation = s[11],
                            CliffTexture = s[12],
                            CliffVariation = s[13],
                            LayerHeight = s[14],
                        };
                    }
                    sawCorners = true;
                    break;
                }

                default:
                    map.UnknownTerrainSections.Add((tag, payload.ToArray()));
                    break;
            }
        }
        if (!sawCorners)
            throw new InvalidDataException("terrain.bin has no CORN section");
    }

    private static void ReadPlacements(NativeMap map, byte[] bytes)
    {
        bool sawPlacements = false;
        foreach ((string tag, ReadOnlyMemory<byte> payload) in ReadSections(bytes, PlacementsMagic, PlacementsFile))
        {
            if (tag != "PLCM")
            {
                map.UnknownPlacementSections.Add((tag, payload.ToArray()));
                continue;
            }
            if (sawPlacements)
                throw new InvalidDataException("placements.bin: duplicate PLCM section");
            sawPlacements = true;

            var span = payload.Span;
            int offset = 0;
            RequirePlacementBytes(span, offset, 4, "PLCM count");
            int count = ReadI32(span, ref offset);
            if (count < 0)
                throw new InvalidDataException($"placements.bin: negative PLCM count {count}");
            for (int i = 0; i < count; i++)
            {
                string record = $"PLCM record {i}";
                RequirePlacementBytes(span, offset, 2, record);
                byte kind = span[offset++];
                if (kind > 1)
                    throw new InvalidDataException(
                        $"placements.bin: {record} has invalid kind {kind}");
                var p = new NativePlacement { IsUnit = kind == 1 };
                int idLen = span[offset++];
                RequirePlacementBytes(span, offset, idLen, $"{record} id");
                try
                {
                    p.Id = new UTF8Encoding(false, true)
                        .GetString(span.Slice(offset, idLen));
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidDataException(
                        $"placements.bin: {record} id is not valid UTF-8", ex);
                }
                offset += idLen;
                // Fixed part after id: variation(4) + 7 floats(28) + owner(1)
                // + flags(1) + refId(4) = 38 bytes; the 2-byte extension length
                // is checked separately below.
                RequirePlacementBytes(span, offset, 38, record);
                p.Variation = ReadI32(span, ref offset);
                p.X = ReadF32(span, ref offset);
                p.Y = ReadF32(span, ref offset);
                p.Z = ReadF32(span, ref offset);
                p.AngleRad = ReadF32(span, ref offset);
                p.ScaleX = ReadF32(span, ref offset);
                p.ScaleY = ReadF32(span, ref offset);
                p.ScaleZ = ReadF32(span, ref offset);
                p.Owner = span[offset++];
                p.Flags = span[offset++];
                p.RefId = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
                offset += 4;
                RequirePlacementBytes(span, offset, 2, $"{record} extension length");
                int extLen = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
                offset += 2;
                if (extLen > 0)
                {
                    RequirePlacementBytes(span, offset, extLen, $"{record} extension");
                    p.Ext = span.Slice(offset, extLen).ToArray();
                    offset += extLen;
                }
                map.Placements.Add(p);
            }
            if (offset != span.Length)
                throw new InvalidDataException(
                    $"placements.bin: PLCM has {span.Length - offset} trailing bytes");
        }
        if (!sawPlacements)
            throw new InvalidDataException("placements.bin has no PLCM section");
    }

    private static void RequirePlacementBytes(
        ReadOnlySpan<byte> span,
        int offset,
        int needed,
        string field)
    {
        if (offset < 0 || needed < 0 || offset > span.Length - needed)
            throw new InvalidDataException(
                $"placements.bin: truncated {field} at offset {offset}");
    }

    private static int ReadI32(ReadOnlySpan<byte> span, ref int offset)
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
        offset += 4;
        return v;
    }

    private static float ReadF32(ReadOnlySpan<byte> span, ref int offset) =>
        BitConverter.Int32BitsToSingle(ReadI32(span, ref offset));

    private static IEnumerable<(string Tag, ReadOnlyMemory<byte> Payload)> ReadSections(byte[] bytes, uint magic, string file)
    {
        if (bytes.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != magic)
            throw new InvalidDataException($"{file}: bad magic");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        if (version is < 1 or > Version)
            throw new InvalidDataException($"{file}: unsupported container version {version}");
        int offset = 8;
        while (offset < bytes.Length)
        {
            if (offset + 8 > bytes.Length)
                throw new InvalidDataException($"{file}: truncated section header at {offset}");
            string tag = Encoding.ASCII.GetString(bytes, offset, 4);
            int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4));
            offset += 8;
            if (length < 0 || offset + length > bytes.Length)
                throw new InvalidDataException($"{file}: section {tag} length {length} out of bounds");
            yield return (tag, bytes.AsMemory(offset, length));
            offset += length;
        }
    }
}
