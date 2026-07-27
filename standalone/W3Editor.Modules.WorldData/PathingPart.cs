using W3Editor.Contracts;
using W3Editor.Formats;

namespace W3Editor.Modules.WorldData;

public sealed class PathingPart : IDocumentPart
{
    public PathingPart(W3PathingMap map) => Map = map;

    public string PartId => "pathing";
    public W3PathingMap Map { get; }
    public bool IsDirty { get; private set; }
    public long ShapeFingerprint => ((long)Map.Width << 32) | (uint)Map.Height;
    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;
}

internal sealed class PathingCodec : IPartCodec
{
    public string PartId => "pathing";
    public IReadOnlyList<string> FileNames { get; } = new[] { "war3map.wpm" };
    public int Order => 20;
    public bool CanHandle(MapFormatKind format) => format == MapFormatKind.War3Folder;

    public IDocumentPart? Read(IPartReadContext ctx)
    {
        byte[]? bytes = ctx.ReadFile("war3map.wpm");
        return bytes == null ? null : new PathingPart(W3PathingMap.LoadBytes(bytes));
    }

    public void Write(IDocumentPart part, IPartWriteContext ctx)
    {
        if (part is not PathingPart pathing)
            throw new ArgumentException("Expected PathingPart.", nameof(part));
        ctx.Emit("war3map.wpm", pathing.Map.SaveBytes());
    }
}

internal sealed class PathingDelta : IUndoDelta
{
    private readonly int[] _indices;
    private readonly byte[] _values;

    public PathingDelta(long fingerprint, IReadOnlyDictionary<int, byte> values)
    {
        ShapeFingerprint = fingerprint;
        _indices = values.Keys.OrderBy(x => x).ToArray();
        _values = _indices.Select(index => values[index]).ToArray();
    }

    public long ShapeFingerprint { get; }
    public int ApproximateBytes => 32 + _indices.Length * 5;

    public IUndoDelta Apply(IDocumentPart part)
    {
        if (part is not PathingPart pathing)
            throw new ArgumentException("Pathing delta applied to another part.", nameof(part));
        var inverse = new Dictionary<int, byte>(_indices.Length);
        for (int i = 0; i < _indices.Length; i++)
        {
            int index = _indices[i];
            inverse[index] = pathing.Map.Cells[index];
            pathing.Map.Cells[index] = _values[i];
        }
        pathing.MarkDirty();
        return new PathingDelta(pathing.ShapeFingerprint, inverse);
    }
}
