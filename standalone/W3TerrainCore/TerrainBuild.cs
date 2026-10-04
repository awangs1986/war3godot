namespace Diablo1Like.W3.Terrain;

/// <summary>
/// One terrain build owns the snapshot, cliff schedule and ground coverage.
/// Render adapters consume this same result instead of independently selecting models.
/// Treat Map as read-only; build a new result after an authored edit.
/// </summary>
public sealed class TerrainBuild
{
    public W3TerrainMap Map { get; }
    private readonly WarsmashTerrainRules _rules;
    public IReadOnlyList<WarsmashTerrainRules.CliffPlacement> Cliffs { get; }
    public IReadOnlyList<WarsmashTerrainRules.CliffPlacement> MissingModels => _rules.MissingModels;
    private readonly int[] _cliffToGround;

    private TerrainBuild(W3TerrainMap source, Func<string, bool> modelAvailable, Func<int, CliffTypeInfo> cliffType)
    {
        Map = source.CreateSnapshot();
        CliffTypeInfo[] types = Enumerable.Range(0, Map.CliffTilesets.Count).Select(cliffType).ToArray();
        CliffTypeInfo TypeFor(int index) => index >= 0 && index < types.Length ? types[index] : CliffTypeInfo.Fallback;
        _cliffToGround = types.Select(type => Math.Max(0, Map.GroundTilesets.IndexOf(type.GroundTile))).ToArray();
        _rules = new WarsmashTerrainRules(Map, modelAvailable,
            texture => TypeFor(texture).CliffModelDir,
            texture => TypeFor(texture).RampModelDir);
        Cliffs = Array.AsReadOnly(_rules.RebuildCliffs().ToArray());
    }

    /// <param name="modelAvailable">Must confirm the model can be decoded, not only that a path exists.</param>
    public static TerrainBuild Create(W3TerrainMap source, Func<string, bool> modelAvailable,
        Func<int, CliffTypeInfo> cliffType) => new(source, modelAvailable, cliffType);

    public bool GroundExists(int x, int y) => _rules.GroundExists(x, y);

    public GroundLayout CreateGroundLayout(IReadOnlyList<bool> extended) =>
        new(Map, _rules, extended, _cliffToGround, Map.GroundTilesets.Count + 1);
}
