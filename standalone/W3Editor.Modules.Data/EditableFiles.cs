using System.Text;
using W3Editor.Contracts;

namespace W3Editor.Modules.Data;

public sealed class EditableTextFile
{
    public required string Name { get; init; }
    public byte[] OriginalBytes { get; set; } = Array.Empty<byte>();
    public string Text { get; set; } = "";
    public bool Dirty { get; set; }
}

public class EditableFilesPart : IDocumentPart
{
    private readonly Dictionary<string, EditableTextFile> _files = new(StringComparer.OrdinalIgnoreCase);
    public EditableFilesPart(string partId) => PartId = partId;
    public string PartId { get; }
    public bool IsDirty => _files.Values.Any(file => file.Dirty);
    public long ShapeFingerprint => StringComparer.Ordinal.GetHashCode(PartId);
    public IReadOnlyDictionary<string, EditableTextFile> Files => _files;
    public void Add(EditableTextFile file) => _files[file.Name] = file;
    public EditableTextFile Ensure(string name) => _files.TryGetValue(name, out EditableTextFile? file)
        ? file : _files[name] = new EditableTextFile { Name = name };
    public void MarkDirty() { foreach (EditableTextFile file in _files.Values) file.Dirty = true; }
    public void ClearDirty() { foreach (EditableTextFile file in _files.Values) file.Dirty = false; }
    public Dictionary<string, byte[]> Snapshot() => _files.ToDictionary(
        pair => pair.Key, pair => TextFilesCodec.Bytes(pair.Value), StringComparer.OrdinalIgnoreCase);
    public void Restore(IReadOnlyDictionary<string, byte[]> snapshot)
    {
        _files.Clear();
        foreach ((string name, byte[] bytes) in snapshot)
            Add(TextFilesCodec.Parse(name, bytes).Also(file => file.Dirty = true));
    }
}

public sealed class EditableFilesDelta(EditableFilesPart part) : IUndoDelta
{
    private readonly Dictionary<string, byte[]> _snapshot = part.Snapshot();
    public long ShapeFingerprint { get; } = part.ShapeFingerprint;
    public int ApproximateBytes => _snapshot.Sum(x => x.Key.Length * 2 + x.Value.Length);
    public IUndoDelta Apply(IDocumentPart part)
    {
        if (part is not EditableFilesPart files) throw new ArgumentException("Expected editable files part.");
        var inverse = new EditableFilesDelta(files);
        files.Restore(_snapshot);
        return inverse;
    }
}

public sealed class TextFilesCodec : ITextPartCodec
{
    public TextFilesCodec(string partId, int order, params string[] names)
    { PartId = partId; Order = order; FileNames = names; }
    public string PartId { get; }
    public int Order { get; }
    public IReadOnlyList<string> FileNames { get; }
    public Encoding Encoding => new UTF8Encoding(false);
    public bool PreserveLineEndings => true;
    public bool CanHandle(MapFormatKind format) => format is MapFormatKind.War3Folder or MapFormatKind.D1MapFolder;
    public IDocumentPart? Read(IPartReadContext ctx)
    {
        var part = new EditableFilesPart(PartId);
        foreach (string name in FileNames)
            if (ctx.ReadFile(name) is { } bytes) part.Add(Parse(name, bytes));
        return part;
    }
    public void Write(IDocumentPart part, IPartWriteContext ctx)
    {
        if (part is not EditableFilesPart files || files.PartId != PartId) throw new ArgumentException($"Expected {PartId}.");
        foreach (EditableTextFile file in files.Files.Values)
        {
            byte[] bytes = Bytes(file);
            ctx.Emit(file.Name, bytes);
            file.OriginalBytes = bytes;
        }
    }
    internal static EditableTextFile Parse(string name, byte[] bytes) => new()
    { Name = name, OriginalBytes = bytes.ToArray(), Text = Decode(bytes) };
    internal static byte[] Bytes(EditableTextFile file) => !file.Dirty && file.OriginalBytes.Length > 0
        ? file.OriginalBytes.ToArray() : new UTF8Encoding(false).GetBytes(file.Text);
    public static EditableTextFile ParseForAcceptance(string name,byte[] bytes)=>Parse(name,bytes);
    public static byte[] BytesForAcceptance(EditableTextFile file)=>Bytes(file);
    private static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}

internal static class DataFluent
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
