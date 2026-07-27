using W3Editor.Contracts;
using W3Editor.Formats;

namespace W3Editor.Modules.Placement;

/// <summary>
/// Shared doo reader/writer. Skin-id presence depends on the map's Reforged-ness,
/// which the w3i tells us; without a mapinfo part we fall back to the classic
/// layout, matching what MapSession does today.
/// </summary>
public abstract class PlacementCodecBase : IPartCodec
{
    public abstract string PartId { get; }
    public abstract IReadOnlyList<string> FileNames { get; }
    protected abstract bool IsUnits { get; }

    /// <summary>After terrain (order 0): placements snap Z to terrain heights.</summary>
    public int Order => 10;

    public bool CanHandle(MapFormatKind format) => format == MapFormatKind.War3Folder;

    public IDocumentPart? Read(IPartReadContext ctx)
    {
        string fileName = FileNames[0];
        byte[]? bytes = ctx.ReadFile(fileName);
        if (bytes == null)
            return null;

        bool hasSkinIds = DetectSkinIds(ctx);
        W3DooDocument document = IsUnits
            ? W3DooFile.ParseUnitsDocument(bytes, hasSkinIds)
            : W3DooFile.ParseDoodadsDocument(bytes, hasSkinIds);
        return CreatePart(document);
    }

    public void Write(IDocumentPart part, IPartWriteContext ctx) =>
        ctx.Emit(FileNames[0], ((PlacementPartBase)part).Document.Write());

    protected abstract IDocumentPart CreatePart(W3DooDocument document);

    /// <summary>Reforged maps carry 4-byte skin ids after the object id.</summary>
    private static bool DetectSkinIds(IPartReadContext ctx)
    {
        byte[]? info = ctx.ReadFile("war3map.w3i");
        return info != null && W3MapInfoVersion.Parse(info).HasReforgedSkinIds;
    }
}

public sealed class DoodadsCodec : PlacementCodecBase
{
    public override string PartId => DoodadsPart.Id;
    public override IReadOnlyList<string> FileNames { get; } = new[] { "war3map.doo" };
    protected override bool IsUnits => false;
    protected override IDocumentPart CreatePart(W3DooDocument document) => new DoodadsPart(document);
}

public sealed class UnitsCodec : PlacementCodecBase
{
    public override string PartId => UnitsPart.Id;
    public override IReadOnlyList<string> FileNames { get; } = new[] { "war3mapUnits.doo" };
    protected override bool IsUnits => true;
    protected override IDocumentPart CreatePart(W3DooDocument document) => new UnitsPart(document);
}
