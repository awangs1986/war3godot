using W3Editor.Contracts;
using W3Editor.Formats;

namespace W3Editor.Modules.WorldData;

public sealed class RegionRecord : W3RegionRecord
{
    public override RegionRecord Clone() => (RegionRecord)MemberwiseClone();
}

public sealed class RegionsPart : IDocumentPart
{
    public int Version { get; set; } = 5;
    public List<RegionRecord> Regions { get; } = new();
    public string PartId => "regions";
    public bool IsDirty { get; private set; }
    public long ShapeFingerprint => Version;
    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;

    public int NextCreationNumber() => Regions.Count == 0
        ? 0
        : Regions.Max(region => region.CreationNumber) + 1;

    public string NextName()
    {
        for (int i = 0; ; i++)
        {
            string candidate = $"Region {i:000}";
            if (Regions.All(region => region.Name != candidate))
                return candidate;
        }
    }
}

internal sealed class RegionsCodec : IPartCodec
{
    public string PartId => "regions";
    public IReadOnlyList<string> FileNames { get; } = new[] { "war3map.w3r" };
    public int Order => 30;
    public bool CanHandle(MapFormatKind format) => format == MapFormatKind.War3Folder;

    public IDocumentPart? Read(IPartReadContext ctx)
    {
        byte[]? bytes = ctx.ReadFile("war3map.w3r");
        return bytes == null ? null : ReadBytes(bytes);
    }

    public void Write(IDocumentPart part, IPartWriteContext ctx)
    {
        if (part is not RegionsPart regions)
            throw new ArgumentException("Expected RegionsPart.", nameof(part));
        ctx.Emit("war3map.w3r", WriteBytes(regions));
    }

    internal static RegionsPart ReadBytes(byte[] bytes)
    {
        W3RegionsFile file = W3RegionsFile.LoadBytes(bytes);
        var result = new RegionsPart { Version = file.Version };
        foreach (W3RegionRecord source in file.Regions)
        {
            var region = new RegionRecord
            {
                Left = source.Left, Bottom = source.Bottom,
                Right = source.Right, Top = source.Top,
                Name = source.Name, CreationNumber = source.CreationNumber,
                WeatherId = source.WeatherId, AmbientId = source.AmbientId,
                R = source.R, G = source.G, B = source.B, A = source.A,
            };
            result.Regions.Add(region);
        }
        return result;
    }

    internal static byte[] WriteBytes(RegionsPart part)
    {
        var file = new W3RegionsFile { Version = part.Version };
        file.Regions.AddRange(part.Regions.Select(region => region.Clone()));
        return file.SaveBytes();
    }
}

internal sealed class RegionsSnapshotDelta : IUndoDelta
{
    private readonly RegionRecord[] _snapshot;
    public RegionsSnapshotDelta(RegionsPart part)
    {
        ShapeFingerprint = part.ShapeFingerprint;
        _snapshot = part.Regions.Select(region => region.Clone()).ToArray();
    }
    public long ShapeFingerprint { get; }
    public int ApproximateBytes => 32 + _snapshot.Length * 128;
    public IUndoDelta Apply(IDocumentPart part)
    {
        if (part is not RegionsPart regions)
            throw new ArgumentException("Regions delta applied to another part.", nameof(part));
        var inverse = new RegionsSnapshotDelta(regions);
        regions.Regions.Clear();
        regions.Regions.AddRange(_snapshot.Select(region => region.Clone()));
        regions.MarkDirty();
        return inverse;
    }
}
