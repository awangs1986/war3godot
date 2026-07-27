namespace W3Editor.Contracts;

// ---------------------------------------------------------------------------
// Standard capabilities. These are in the contract rather than left to ad-hoc
// ICapabilities conventions because they are the high-traffic cross-module
// channels: without them every placement/region/copy-paste module would invent
// its own selection model and they would not interoperate.
// ---------------------------------------------------------------------------

/// <summary>A selectable thing, identified by part + stable id within that part.</summary>
public readonly record struct SelectionRef(string PartId, long Id);

/// <summary>Shared selection set (doodads, units, regions, …).</summary>
public interface ISelection
{
    IReadOnlyCollection<SelectionRef> Items { get; }
    bool IsEmpty { get; }
    bool Contains(SelectionRef item);

    void Clear();
    void Set(IEnumerable<SelectionRef> items);
    void Add(IEnumerable<SelectionRef> items);
    void Remove(IEnumerable<SelectionRef> items);

    event Action? SelectionChanged;
}

/// <summary>Result of a viewport pick.</summary>
public readonly record struct PickHit(
    bool Hit,
    float WorldX,
    float WorldY,
    float WorldZ,
    SelectionRef? Object);

/// <summary>Screen → world queries. Provided by the renderer module.</summary>
public interface IPickService
{
    /// <summary>Ray-casts against terrain; Object is null for a ground hit.</summary>
    PickHit PickGround(float screenX, float screenY);

    /// <summary>Ray-casts against placed objects first, terrain second.</summary>
    PickHit PickObject(float screenX, float screenY);

    /// <summary>Terrain height (world Z) at a world XY.</summary>
    float GroundHeightAt(float worldX, float worldY);
}

/// <summary>Viewport camera control. Provided by the renderer module.</summary>
public interface ICameraService
{
    (float X, float Y, float Z) Position { get; }
    void LookAt(float worldX, float worldY, float distance);
    void FrameWholeMap();
    void Reset();
}

/// <summary>
/// A place assets come from: extracted War3 dirs, an asset pack, a plain
/// folder, a future CASC reader. Sources are consulted in Priority order.
/// </summary>
public interface IAssetSource
{
    string Id { get; }
    /// <summary>Lower is consulted first.</summary>
    int Priority { get; }
    bool TryOpen(string relativePath, out Stream stream);
    bool Exists(string relativePath);
}

/// <summary>Read-only Warcraft installation/cache data used by editor modules for SLK/INI metadata.</summary>
public interface IWarcraftDataSource
{
    bool TryRead(string relativePath, out byte[] bytes);
}

public enum WarcraftObjectKind { Units, Items, Abilities, Destructibles, Doodads, Upgrades, Buffs }

public sealed record WarcraftObjectDefinition(string Id, string Name, string Group);
public sealed record WarcraftFieldDefinition(
    string Id, string Field, string DisplayName, string Type, string Category, string Repeat);

/// <summary>
/// Shared, cached Warcraft object database. Placement palettes, object editor
/// and previews must consume this service instead of parsing SLKs independently.
/// </summary>
public interface IWarcraftObjectDatabase
{
    IReadOnlyList<WarcraftObjectDefinition> Objects(WarcraftObjectKind kind);
    IReadOnlyList<WarcraftFieldDefinition> Fields(WarcraftObjectKind kind);
}

/// <summary>
/// Renderer notification sink. Tools report a touched rect; the shell forwards
/// it here so only that region re-uploads instead of a full terrain rebuild
/// (external review 4.27).
/// </summary>
public interface IRendererSync
{
    void InvalidateTerrain(int x0, int y0, int x1, int y1);
    void InvalidatePlacements();
    void InvalidateAll();
}

/// <summary>Terrain-owned sampling used by the pathing module after a stroke.</summary>
public interface ITerrainPathingSource
{
    byte GetTerrainPathing(IMapDocument document, int pathingX, int pathingY);
}

/// <summary>Pathing-owned rebuild of the static terrain bits in a touched area.</summary>
public interface IPathingBlitService
{
    void RebuildTerrainPathing(
        IMapDocument document,
        IEditTransaction transaction,
        int terrainX0,
        int terrainY0,
        int terrainX1,
        int terrainY1);
}

/// <summary>
/// Long-running work reporting, for command handlers. Keeps progress UI out of
/// the module and cancellation honest.
/// </summary>
public interface IProgressSink
{
    void Report(double fraction01, string message);
    bool IsCancellationRequested { get; }
}
