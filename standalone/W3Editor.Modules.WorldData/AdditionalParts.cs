using W3Editor.Contracts;

namespace W3Editor.Modules.WorldData;

public sealed class RawBundlePart(string partId, IReadOnlyDictionary<string, byte[]> files) : IDocumentPart
{
    public string PartId { get; } = partId;
    public Dictionary<string, byte[]> Files { get; } = files.ToDictionary(
        pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    public bool IsDirty { get; private set; }
    public long ShapeFingerprint => Files.Aggregate(17L, (hash, pair) => hash * 31 + pair.Value.Length);
    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;
}

internal sealed class RawBundleCodec(
    string partId,
    MapFormatKind format,
    int order,
    params string[] fileNames) : IPartCodec
{
    public string PartId => partId;
    public IReadOnlyList<string> FileNames { get; } = fileNames;
    public int Order => order;
    public bool CanHandle(MapFormatKind candidate) => candidate == format;

    public IDocumentPart? Read(IPartReadContext ctx)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in FileNames)
        {
            byte[]? bytes = ctx.ReadFile(name);
            if (bytes != null)
                files[name] = bytes;
        }
        return files.Count == 0 ? null : new RawBundlePart(partId, files);
    }

    public void Write(IDocumentPart part, IPartWriteContext ctx)
    {
        if (part is not RawBundlePart bundle || bundle.PartId != partId)
            throw new ArgumentException($"Expected raw part '{partId}'.", nameof(part));
        foreach ((string name, byte[] bytes) in bundle.Files)
            ctx.Emit(name, bytes);
    }
}

public sealed class CamerasPartModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        "part.cameras", "1.0.0", ContractVersion.Value, ModuleTier.Full,
        DisplayName: "摄像机数据 (war3map.w3c)");
    public void Register(IModuleContext ctx) => ctx.AddPartCodec(new RawBundleCodec(
        "cameras", MapFormatKind.War3Folder, 35, "war3map.w3c"));
    public void Dispose() { }
}

public sealed class D1MapPartModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        "part.d1map", "1.0.0", ContractVersion.Value, ModuleTier.Full,
        DisplayName: "d1map 原生数据");
    public void Register(IModuleContext ctx) => ctx.AddPartCodec(new RawBundleCodec(
        "d1map", MapFormatKind.D1MapFolder, 1,
        "map.json", "terrain.bin", "placements.bin"));
    public void Dispose() { }
}
