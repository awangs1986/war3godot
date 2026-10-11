using System.Text;
using W3Editor.Contracts;

namespace W3Editor.Modules.Data;

public sealed class SoundRecord
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string EaxEffect { get; set; } = "DefaultEAXON";
    public uint Flags { get; set; }
    public uint FadeInRate { get; set; }
    public uint FadeOutRate { get; set; }
    public uint Volume { get; set; } = 127;
    public float Pitch { get; set; } = 1;
    public float PitchVariance { get; set; }
    public uint Priority { get; set; }
    public uint Channel { get; set; }
    public float MinDistance { get; set; }
    public float MaxDistance { get; set; } = 10000;
    public float DistanceCutoff { get; set; } = 3000;
    public float ConeInside { get; set; }
    public float ConeOutside { get; set; }
    public uint ConeOutsideVolume { get; set; } = 127;
    public float ConeX { get; set; }
    public float ConeY { get; set; }
    public float ConeZ { get; set; }
    public List<object> Extended { get; } = new();
    public SoundRecord Clone() => SoundBinary.ReadRecord(SoundBinary.WriteRecord(this, 3), 3);
}

public sealed class SoundsPart : IDocumentPart
{
    public string PartId => "sounds";
    public uint Version { get; set; } = 1;
    public List<SoundRecord> Sounds { get; } = new();
    public byte[] OriginalBytes { get; set; } = Array.Empty<byte>();
    public bool IsDirty { get; private set; }
    public long ShapeFingerprint => 0x5333534F554E44;
    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;
    public byte[] Snapshot() => SoundBinary.Write(this, force: true);
    public void Restore(byte[] bytes) { SoundBinary.CopyInto(SoundBinary.Read(bytes), this); IsDirty = true; }
}

public sealed class SoundsDelta(SoundsPart part) : IUndoDelta
{
    private readonly byte[] _bytes = part.Snapshot();
    public long ShapeFingerprint => 0x5333534F554E44;
    public int ApproximateBytes => _bytes.Length;
    public IUndoDelta Apply(IDocumentPart part)
    {
        if (part is not SoundsPart sounds) throw new ArgumentException("Expected sounds part.");
        var inverse = new SoundsDelta(sounds); sounds.Restore(_bytes); return inverse;
    }
}

public sealed class SoundsCodec : IPartCodec
{
    public string PartId => "sounds";
    public IReadOnlyList<string> FileNames { get; } = new[] { "war3map.w3s" };
    public int Order => 40;
    public bool CanHandle(MapFormatKind format) => format is MapFormatKind.War3Folder or MapFormatKind.D1MapFolder;
    public IDocumentPart? Read(IPartReadContext ctx) => ctx.ReadFile("war3map.w3s") is { } bytes ? SoundBinary.Read(bytes) : new SoundsPart();
    public void Write(IDocumentPart part, IPartWriteContext ctx)
    {
        if (part is not SoundsPart sounds) throw new ArgumentException("Expected sounds part.");
        byte[] bytes = SoundBinary.Write(sounds); ctx.Emit("war3map.w3s", bytes); sounds.OriginalBytes = bytes;
    }
}

public static class SoundBinary
{
    public static SoundsPart Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Encoding.UTF8);
        var part = new SoundsPart { Version = reader.ReadUInt32(), OriginalBytes = bytes.ToArray() };
        if (part.Version is not (1 or 2 or 3)) throw new InvalidDataException($"Unsupported w3s version {part.Version}.");
        uint count = reader.ReadUInt32(); if (count > 1_000_000) throw new InvalidDataException("Unreasonable sound count.");
        for (uint i = 0; i < count; i++) part.Sounds.Add(ReadRecord(reader, part.Version));
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing bytes in w3s.");
        return part;
    }
    internal static SoundRecord ReadRecord(byte[] bytes, uint version)
    { using var s = new MemoryStream(bytes); using var r = new BinaryReader(s, Encoding.UTF8); return ReadRecord(r, version); }
    private static SoundRecord ReadRecord(BinaryReader r, uint version)
    {
        var sound = new SoundRecord
        {
            Name = C(r), File = C(r), EaxEffect = C(r), Flags = r.ReadUInt32(), FadeInRate = r.ReadUInt32(),
            FadeOutRate = r.ReadUInt32(), Volume = r.ReadUInt32(), Pitch = r.ReadSingle(), PitchVariance = r.ReadSingle(),
            Priority = r.ReadUInt32(), Channel = r.ReadUInt32(), MinDistance = r.ReadSingle(), MaxDistance = r.ReadSingle(),
            DistanceCutoff = r.ReadSingle(), ConeInside = r.ReadSingle(), ConeOutside = r.ReadSingle(),
            ConeOutsideVolume = r.ReadUInt32(), ConeX = r.ReadSingle(), ConeY = r.ReadSingle(), ConeZ = r.ReadSingle(),
        };
        if (version >= 2)
        {
            sound.Extended.Add(C(r)); sound.Extended.Add(C(r)); sound.Extended.Add(C(r)); sound.Extended.Add(r.ReadUInt32());
            sound.Extended.Add(C(r)); sound.Extended.Add(r.ReadUInt32()); sound.Extended.Add(C(r)); sound.Extended.Add(r.ReadUInt32());
            sound.Extended.Add(C(r)); sound.Extended.Add(C(r)); sound.Extended.Add(C(r)); sound.Extended.Add(C(r));
            if (version >= 3) sound.Extended.Add(r.ReadUInt32());
        }
        return sound;
    }
    public static byte[] Write(SoundsPart part, bool force = false)
    {
        if (!force && !part.IsDirty && part.OriginalBytes.Length > 0) return part.OriginalBytes.ToArray();
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(part.Version); writer.Write((uint)part.Sounds.Count);
        foreach (SoundRecord sound in part.Sounds) writer.Write(WriteRecord(sound, part.Version));
        return stream.ToArray();
    }
    internal static byte[] WriteRecord(SoundRecord s, uint version)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.UTF8);
        C(w,s.Name); C(w,s.File); C(w,s.EaxEffect); w.Write(s.Flags); w.Write(s.FadeInRate); w.Write(s.FadeOutRate);
        w.Write(s.Volume); w.Write(s.Pitch); w.Write(s.PitchVariance); w.Write(s.Priority); w.Write(s.Channel);
        w.Write(s.MinDistance); w.Write(s.MaxDistance); w.Write(s.DistanceCutoff); w.Write(s.ConeInside); w.Write(s.ConeOutside);
        w.Write(s.ConeOutsideVolume); w.Write(s.ConeX); w.Write(s.ConeY); w.Write(s.ConeZ);
        if (version >= 2)
        {
            object[] defaults = { "", "", "", 0u, "", 0u, "", 0u, "", "", "", "", 0u };
            int total = version >= 3 ? 13 : 12;
            for (int i=0;i<total;i++) { object value=i<s.Extended.Count?s.Extended[i]:defaults[i]; if(value is string text) C(w,text); else w.Write(Convert.ToUInt32(value)); }
        }
        return stream.ToArray();
    }
    internal static void CopyInto(SoundsPart from, SoundsPart to)
    { to.Version=from.Version; to.Sounds.Clear(); to.Sounds.AddRange(from.Sounds); to.OriginalBytes=from.OriginalBytes; }
    private static string C(BinaryReader r) { var b=new List<byte>(); byte x; while((x=r.ReadByte())!=0)b.Add(x); return Encoding.UTF8.GetString(b.ToArray()); }
    private static void C(BinaryWriter w,string s) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
}
