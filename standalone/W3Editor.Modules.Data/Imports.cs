using System.Text;
using W3Editor.Contracts;

namespace W3Editor.Modules.Data;

public sealed record ImportEntry(byte Flags, string Path);

public sealed class ImportsPart : IDocumentPart
{
    public string PartId => "imports";
    public uint Version { get; set; } = 1;
    public List<ImportEntry> Entries { get; } = new();
    public byte[] OriginalBytes { get; set; } = Array.Empty<byte>();
    public bool IsDirty { get; private set; }
    public long ShapeFingerprint => 0x494D504F525453;
    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;
    public byte[] Snapshot() => ImportBinary.Write(this, true);
    public void Restore(byte[] bytes) { ImportBinary.CopyInto(ImportBinary.Read(bytes), this); IsDirty = true; }
}

public sealed class ImportsDelta(ImportsPart part) : IUndoDelta
{
    private readonly byte[] _bytes = part.Snapshot();
    public long ShapeFingerprint => 0x494D504F525453;
    public int ApproximateBytes => _bytes.Length;
    public IUndoDelta Apply(IDocumentPart part)
    { if(part is not ImportsPart imports) throw new ArgumentException(); var inverse=new ImportsDelta(imports); imports.Restore(_bytes); return inverse; }
}

public sealed class ImportsCodec : IPartCodec
{
    public string PartId => "imports";
    public IReadOnlyList<string> FileNames { get; } = new[] { "war3map.imp" };
    public int Order => 41;
    public bool CanHandle(MapFormatKind format) => format is MapFormatKind.War3Folder or MapFormatKind.D1MapFolder;
    public IDocumentPart? Read(IPartReadContext ctx) => ctx.ReadFile("war3map.imp") is { } bytes ? ImportBinary.Read(bytes) : new ImportsPart();
    public void Write(IDocumentPart part, IPartWriteContext ctx)
    {
        if(part is not ImportsPart imports) throw new ArgumentException(); byte[] bytes=ImportBinary.Write(imports);
        ctx.Emit("war3map.imp",bytes); imports.OriginalBytes=bytes;
    }
}

public static class ImportBinary
{
    public static ImportsPart Read(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes,false); using var reader=new BinaryReader(stream,Encoding.UTF8);
        var part=new ImportsPart{Version=reader.ReadUInt32(),OriginalBytes=bytes.ToArray()};
        uint count=reader.ReadUInt32(); if(count>1_000_000) throw new InvalidDataException("Unreasonable import count.");
        for(uint i=0;i<count;i++) part.Entries.Add(new ImportEntry(reader.ReadByte(),CString(reader)));
        if(stream.Position!=stream.Length) throw new InvalidDataException("Trailing bytes in war3map.imp.");
        return part;
    }
    public static byte[] Write(ImportsPart part,bool force=false)
    {
        if(!force&&!part.IsDirty&&part.OriginalBytes.Length>0)return part.OriginalBytes.ToArray();
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream,Encoding.UTF8);
        writer.Write(part.Version); writer.Write((uint)part.Entries.Count);
        foreach(ImportEntry entry in part.Entries){writer.Write(entry.Flags);writer.Write(Encoding.UTF8.GetBytes(entry.Path.Replace('/','\\')));writer.Write((byte)0);}
        return stream.ToArray();
    }
    internal static void CopyInto(ImportsPart from,ImportsPart to)
    {to.Version=from.Version;to.Entries.Clear();to.Entries.AddRange(from.Entries);to.OriginalBytes=from.OriginalBytes;}
    private static string CString(BinaryReader r){var b=new List<byte>();byte x;while((x=r.ReadByte())!=0)b.Add(x);return Encoding.UTF8.GetString(b.ToArray());}
}
