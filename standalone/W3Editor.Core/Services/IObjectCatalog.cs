namespace W3Editor.Services;

/// <summary>
/// Placeable-object catalog consumed by palettes, placement tools and the
/// doodad renderer. Implementations: <see cref="ObjectCatalog"/> (War3 SLK
/// tables) and <see cref="JsonObjectCatalog"/> (native catalog.json).
/// </summary>
public interface IObjectCatalog
{
    IReadOnlyList<DoodadTypeInfo> Doodads { get; }
    IReadOnlyList<UnitTypeInfo> Units { get; }

    DoodadTypeInfo? FindDoodad(string id);
    UnitTypeInfo? FindUnit(string id);

    /// <summary>Model relative path for a placement, or "" when unknown.</summary>
    string ResolveModelRel(string id, bool isUnit, int variation);
    float ModelScale(string id, bool isUnit);
    /// <summary>Replaceable texture stem for destructibles (trees), "" when none.</summary>
    string DestructibleTexFile(string id);
}
