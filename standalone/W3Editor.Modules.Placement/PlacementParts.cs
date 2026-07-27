using W3Editor.Contracts;
using W3Editor.Formats;

namespace W3Editor.Modules.Placement;

/// <summary>
/// Shared implementation for the doodad and unit parts — both are a
/// <see cref="W3DooDocument"/> and differ only in filename and flag.
///
/// Fidelity note: unmodelled fields (item drop tables, hero stats, abilities)
/// live in <see cref="W3Placement.RawRecord"/> and are re-emitted verbatim.
/// That is deliberately preferred over modelling every w3u field — the same
/// strategy the object-table modules will use.
/// </summary>
public abstract class PlacementPartBase : IDocumentPart
{
    protected PlacementPartBase(W3DooDocument document) => Document = document;

    public W3DooDocument Document { get; }
    public List<W3Placement> Placements => Document.Placements;

    public abstract string PartId { get; }
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Deliberately NOT derived from the placement count: a placement delta is
    /// a whole-list snapshot that addresses nothing by index, and changing the
    /// count is the normal outcome of an edit. Folding the count in here would
    /// make every add/remove invalidate its own undo entry.
    /// </summary>
    public long ShapeFingerprint => Document.IsUnits ? 2L : 1L;

    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;

    /// <summary>Full-list snapshot delta. Placement counts are small relative to corner grids.</summary>
    public PlacementDelta Snapshot() =>
        new(ShapeFingerprint, Placements.Select(p => p.Clone()).ToList());
}

public sealed class DoodadsPart : PlacementPartBase
{
    public const string Id = "doodads";
    public DoodadsPart(W3DooDocument document) : base(document) { }
    public override string PartId => Id;
}

public sealed class UnitsPart : PlacementPartBase
{
    public const string Id = "units";
    public UnitsPart(W3DooDocument document) : base(document) { }
    public override string PartId => Id;
}

/// <summary>Restores a full placement list. Apply returns the inverse snapshot.</summary>
public sealed class PlacementDelta : IUndoDelta
{
    private readonly List<W3Placement> _placements;

    public PlacementDelta(long shapeFingerprint, List<W3Placement> placements)
    {
        ShapeFingerprint = shapeFingerprint;
        _placements = placements;
    }

    public long ShapeFingerprint { get; }

    /// <summary>Rough per-record cost, so the history's byte budget sees this too.</summary>
    public int ApproximateBytes =>
        64 + _placements.Sum(p => 96 + (p.RawRecord?.Length ?? 0) + p.NativeExt.Length);

    public IUndoDelta Apply(IDocumentPart part)
    {
        var placementPart = (PlacementPartBase)part;
        var inverse = new PlacementDelta(
            placementPart.ShapeFingerprint,
            placementPart.Placements.Select(p => p.Clone()).ToList());
        placementPart.Placements.Clear();
        placementPart.Placements.AddRange(_placements.Select(p => p.Clone()));
        return inverse;
    }
}
