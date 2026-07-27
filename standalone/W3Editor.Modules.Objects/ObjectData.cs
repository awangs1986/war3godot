using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using W3Editor.Contracts;

namespace W3Editor.Modules.Objects;

public enum ObjectKind { Units, Items, Abilities, Destructibles, Doodads, Upgrades, Buffs }

public static class ObjectKinds
{
    public sealed record Definition(ObjectKind Kind, string Title, string Extension, bool OptionalInts);

    public static readonly IReadOnlyList<Definition> All = new[]
    {
        new Definition(ObjectKind.Units, "单位", "w3u", false),
        new Definition(ObjectKind.Items, "物品", "w3t", false),
        new Definition(ObjectKind.Abilities, "技能", "w3a", true),
        new Definition(ObjectKind.Destructibles, "可破坏物", "w3b", false),
        new Definition(ObjectKind.Doodads, "装饰物", "w3d", true),
        new Definition(ObjectKind.Upgrades, "科技", "w3q", true),
        new Definition(ObjectKind.Buffs, "魔法效果", "w3h", false),
    };

    public static Definition Get(ObjectKind kind) => All.First(x => x.Kind == kind);
    public static string FileName(ObjectKind kind, bool skin) =>
        $"{(skin ? "war3mapSkin" : "war3map")}.{Get(kind).Extension}";
}

public sealed class ObjectModification
{
    public byte[] FieldId { get; set; } = new byte[4];
    public uint ValueType { get; set; }
    public uint LevelOrVariation { get; set; }
    public uint DataPointer { get; set; }
    public int IntValue { get; set; }
    public float FloatValue { get; set; }
    public string StringValue { get; set; } = "";
    public byte[] RawValue { get; set; } = Array.Empty<byte>();
    public bool ValueChanged { get; set; }
    public byte[] Terminator { get; set; } = new byte[4];

    public string FieldText
    {
        get => FourCC.Display(FieldId);
        set => FieldId = FourCC.Parse(value);
    }

    public string ValueText
    {
        get => ValueType switch
        {
            0 => IntValue.ToString(CultureInfo.InvariantCulture),
            1 or 2 => FloatValue.ToString("R", CultureInfo.InvariantCulture),
            _ => StringValue,
        };
        set
        {
            switch (ValueType)
            {
                case 0:
                    IntValue = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    break;
                case 1:
                case 2:
                    FloatValue = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    break;
                case 3:
                    StringValue = value;
                    break;
                default:
                    throw new InvalidDataException($"Unsupported object value type {ValueType}.");
            }
            ValueChanged = true;
        }
    }

    public ObjectModification Clone() => new()
    {
        FieldId = FieldId.ToArray(), ValueType = ValueType,
        LevelOrVariation = LevelOrVariation, DataPointer = DataPointer,
        IntValue = IntValue, FloatValue = FloatValue, StringValue = StringValue,
        RawValue = RawValue.ToArray(), ValueChanged = ValueChanged,
        Terminator = Terminator.ToArray(),
    };
}

public sealed class ObjectRecord
{
    public byte[] OriginalId { get; set; } = new byte[4];
    public byte[] ModifiedId { get; set; } = new byte[4];
    public uint SetCount { get; set; } = 1;
    public uint SetFlags { get; set; }
    public bool IsCustom { get; set; }
    public List<ObjectModification> Modifications { get; } = new();

    public string OriginalText { get => FourCC.Display(OriginalId); set => OriginalId = FourCC.Parse(value); }
    public string ModifiedText { get => FourCC.Display(ModifiedId); set => ModifiedId = FourCC.Parse(value); }
    public string DisplayId => IsCustom ? ModifiedText : OriginalText;

    public ObjectRecord Clone(bool custom = true) => new ObjectRecord
    {
        OriginalId = OriginalId.ToArray(), ModifiedId = ModifiedId.ToArray(),
        SetCount = SetCount, SetFlags = SetFlags, IsCustom = custom,
    }.Also(result => result.Modifications.AddRange(Modifications.Select(x => x.Clone())));
}

internal static class Fluent
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}

public sealed class ObjectDataFile
{
    public required string FileName { get; init; }
    public required bool OptionalInts { get; init; }
    public uint Version { get; set; } = 3;
    public List<ObjectRecord> OriginalTable { get; } = new();
    public List<ObjectRecord> CustomTable { get; } = new();
    public byte[] OriginalBytes { get; set; } = Array.Empty<byte>();
    public bool IsDirty { get; set; }
}

public sealed class ObjectDataPart : IDocumentPart
{
    private readonly Dictionary<string, ObjectDataFile> _files = new(StringComparer.OrdinalIgnoreCase);
    public string PartId => "objects";
    public bool IsDirty => _files.Values.Any(file => file.IsDirty);
    public long ShapeFingerprint => 0x4F424A45435453L; // "OBJECTS"; edits do not change address space.
    public IReadOnlyDictionary<string, ObjectDataFile> Files => _files;

    public void AddFile(ObjectDataFile file) => _files[file.FileName] = file;

    public ObjectDataFile EnsureFile(ObjectKind kind, bool skin)
    {
        string name = ObjectKinds.FileName(kind, skin);
        if (_files.TryGetValue(name, out ObjectDataFile? file)) return file;
        file = new ObjectDataFile { FileName = name, OptionalInts = ObjectKinds.Get(kind).OptionalInts };
        _files[name] = file;
        return file;
    }

    public ObjectDataFile? GetFile(ObjectKind kind, bool skin) =>
        _files.GetValueOrDefault(ObjectKinds.FileName(kind, skin));

    public void Touch(ObjectDataFile file) { file.IsDirty = true; }
    public void MarkDirty() { foreach (ObjectDataFile file in _files.Values) file.IsDirty = true; }
    public void ClearDirty() { foreach (ObjectDataFile file in _files.Values) file.IsDirty = false; }

    public ObjectDataSnapshot Capture() => new(_files.ToDictionary(
        pair => pair.Key, pair => ObjectModificationBinary.Write(pair.Value),
        StringComparer.OrdinalIgnoreCase));

    public void Restore(ObjectDataSnapshot snapshot)
    {
        _files.Clear();
        foreach ((string name, byte[] bytes) in snapshot.Files)
        {
            bool optional = ObjectKinds.All.Any(def => name.EndsWith(def.Extension, StringComparison.OrdinalIgnoreCase) && def.OptionalInts);
            ObjectDataFile file = ObjectModificationBinary.Read(name, bytes, optional);
            file.IsDirty = true;
            _files[name] = file;
        }
    }
}

public sealed record ObjectDataSnapshot(IReadOnlyDictionary<string, byte[]> Files);

public sealed class ObjectDataSnapshotDelta(ObjectDataSnapshot snapshot) : IUndoDelta
{
    public long ShapeFingerprint => 0x4F424A45435453L;
    public int ApproximateBytes => snapshot.Files.Sum(x => x.Key.Length * 2 + x.Value.Length);
    public IUndoDelta Apply(IDocumentPart part)
    {
        if (part is not ObjectDataPart objects) throw new ArgumentException("Expected object data part.", nameof(part));
        ObjectDataSnapshot inverse = objects.Capture();
        objects.Restore(snapshot);
        return new ObjectDataSnapshotDelta(inverse);
    }
}

public sealed class ObjectDataCodec : IPartCodec
{
    public string PartId => "objects";
    public int Order => 45;
    public IReadOnlyList<string> FileNames { get; } = ObjectKinds.All
        .SelectMany(def => new[] { $"war3map.{def.Extension}", $"war3mapSkin.{def.Extension}" }).ToArray();
    public bool CanHandle(MapFormatKind format) => format is MapFormatKind.War3Folder or MapFormatKind.D1MapFolder;

    public IDocumentPart? Read(IPartReadContext ctx)
    {
        var part = new ObjectDataPart();
        foreach (ObjectKinds.Definition def in ObjectKinds.All)
        foreach (bool skin in new[] { false, true })
        {
            string name = ObjectKinds.FileName(def.Kind, skin);
            byte[]? bytes = ctx.ReadFile(name);
            if (bytes is null) continue;
            part.AddFile(ObjectModificationBinary.Read(name, bytes, def.OptionalInts));
        }
        return part;
    }

    public void Write(IDocumentPart part, IPartWriteContext ctx)
    {
        if (part is not ObjectDataPart objects) throw new ArgumentException("Expected object data part.", nameof(part));
        foreach (ObjectDataFile file in objects.Files.Values)
        {
            byte[] bytes = ObjectModificationBinary.Write(file);
            ctx.Emit(file.FileName, bytes);
            file.OriginalBytes = bytes.ToArray();
        }
    }
}

public static class ObjectModificationBinary
{
    public static ObjectDataFile Read(string name, byte[] bytes, bool optionalInts)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (stream.Length < 12) throw new InvalidDataException($"{name}: object table is shorter than 12 bytes.");
        var file = new ObjectDataFile
        {
            FileName = name, OptionalInts = optionalInts,
            Version = reader.ReadUInt32(), OriginalBytes = bytes.ToArray(),
        };
        if (file.Version is not (1 or 2 or 3))
            throw new InvalidDataException($"{name}: unsupported object table version {file.Version}.");
        ReadTable(reader, file, file.OriginalTable, custom: false);
        ReadTable(reader, file, file.CustomTable, custom: true);
        if (stream.Position != stream.Length)
            throw new InvalidDataException($"{name}: {stream.Length - stream.Position} trailing bytes after object tables.");
        return file;
    }

    private static void ReadTable(BinaryReader reader, ObjectDataFile file, List<ObjectRecord> target, bool custom)
    {
        uint count = ReadCount(reader, file.FileName, "object");
        for (uint i = 0; i < count; i++)
        {
            var record = new ObjectRecord
            {
                OriginalId = ReadExact(reader, 4), ModifiedId = ReadExact(reader, 4), IsCustom = custom,
            };
            if (file.Version >= 3)
            {
                record.SetCount = reader.ReadUInt32();
                record.SetFlags = reader.ReadUInt32();
            }
            uint mods = ReadCount(reader, file.FileName, "modification");
            for (uint j = 0; j < mods; j++) record.Modifications.Add(ReadModification(reader, file.OptionalInts));
            target.Add(record);
        }
    }

    private static ObjectModification ReadModification(BinaryReader reader, bool optionalInts)
    {
        var mod = new ObjectModification { FieldId = ReadExact(reader, 4), ValueType = reader.ReadUInt32() };
        if (optionalInts)
        {
            mod.LevelOrVariation = reader.ReadUInt32();
            mod.DataPointer = reader.ReadUInt32();
        }
        long start = reader.BaseStream.Position;
        switch (mod.ValueType)
        {
            case 0: mod.IntValue = reader.ReadInt32(); break;
            case 1:
            case 2: mod.FloatValue = reader.ReadSingle(); break;
            case 3: mod.StringValue = ReadCString(reader); break;
            default: throw new InvalidDataException($"Unknown object modification value type {mod.ValueType}.");
        }
        long end = reader.BaseStream.Position;
        reader.BaseStream.Position = start;
        mod.RawValue = ReadExact(reader, checked((int)(end - start)));
        mod.Terminator = ReadExact(reader, 4);
        return mod;
    }

    public static byte[] Write(ObjectDataFile file, bool force = false)
    {
        if (!force && !file.IsDirty && file.OriginalBytes.Length > 0) return file.OriginalBytes.ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(file.Version);
        WriteTable(writer, file, file.OriginalTable);
        WriteTable(writer, file, file.CustomTable);
        return stream.ToArray();
    }

    private static void WriteTable(BinaryWriter writer, ObjectDataFile file, IReadOnlyList<ObjectRecord> table)
    {
        writer.Write((uint)table.Count);
        foreach (ObjectRecord record in table)
        {
            writer.Write(FourCC.Normalize(record.OriginalId));
            writer.Write(FourCC.Normalize(record.ModifiedId));
            if (file.Version >= 3) { writer.Write(record.SetCount); writer.Write(record.SetFlags); }
            writer.Write((uint)record.Modifications.Count);
            foreach (ObjectModification mod in record.Modifications)
            {
                writer.Write(FourCC.Normalize(mod.FieldId));
                writer.Write(mod.ValueType);
                if (file.OptionalInts) { writer.Write(mod.LevelOrVariation); writer.Write(mod.DataPointer); }
                if (!mod.ValueChanged && mod.RawValue.Length > 0) writer.Write(mod.RawValue);
                else WriteValue(writer, mod);
                writer.Write(FourCC.Normalize(mod.Terminator));
            }
        }
    }

    private static void WriteValue(BinaryWriter writer, ObjectModification mod)
    {
        switch (mod.ValueType)
        {
            case 0: writer.Write(mod.IntValue); break;
            case 1:
            case 2: writer.Write(mod.FloatValue); break;
            case 3: writer.Write(Encoding.UTF8.GetBytes(mod.StringValue)); writer.Write((byte)0); break;
            default: throw new InvalidDataException($"Unknown object modification value type {mod.ValueType}.");
        }
    }

    private static uint ReadCount(BinaryReader reader, string name, string label)
    {
        uint value = reader.ReadUInt32();
        if (value > 1_000_000) throw new InvalidDataException($"{name}: unreasonable {label} count {value}.");
        return value;
    }

    private static byte[] ReadExact(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        return bytes.Length == count ? bytes : throw new EndOfStreamException();
    }

    private static string ReadCString(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        while (true)
        {
            int value = reader.BaseStream.ReadByte();
            if (value < 0) throw new EndOfStreamException();
            if (value == 0) break;
            bytes.WriteByte((byte)value);
            if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Object string is unreasonably large.");
        }
        byte[] raw = bytes.ToArray();
        try { return new UTF8Encoding(false, true).GetString(raw); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(raw); }
    }
}

public static class FourCC
{
    public static byte[] Parse(string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text ?? "");
        return Normalize(bytes);
    }
    public static byte[] Normalize(byte[] bytes)
    {
        var result = new byte[4];
        Array.Copy(bytes, result, Math.Min(4, bytes.Length));
        return result;
    }
    public static string Display(byte[] bytes) => new(Normalize(bytes).Select(value =>
        value is >= 32 and <= 126 ? (char)value : value == 0 ? '·' : '?').ToArray());
}
