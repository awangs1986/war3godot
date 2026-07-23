using Diablo1Like.W3.Terrain;

namespace Diablo1Like.MapFormat;

/// <summary>
/// One placed object (doodad/destructible or unit) in the native map format.
/// Engine-facing model — no War3 wire-format knowledge lives here.
/// </summary>
public sealed class NativePlacement
{
    public bool IsUnit;
    /// <summary>Catalog key. War3-imported content keeps its rawcode; native content uses readable ids.</summary>
    public string Id = "";
    public int Variation;
    public float X, Y, Z;          // world units: X east, Y north, Z up
    public float AngleRad;
    public float ScaleX = 1, ScaleY = 1, ScaleZ = 1;
    public byte Owner;
    public byte Flags = 2;         // visibility/solid bits, same semantics as the editor model
    /// <summary>Stable per-map id (editor identity across sessions).</summary>
    public uint RefId;
    /// <summary>Opaque passthrough blob (forward compatibility / import fidelity). Empty for native content.</summary>
    public byte[] Ext = Array.Empty<byte>();
}

/// <summary>
/// In-memory native map: terrain corner grid + placements + manifest metadata.
/// Serialized by <see cref="NativeMapFormat"/> as a folder
/// (map.json / terrain.bin / placements.bin).
/// </summary>
public sealed class NativeMap
{
    public string Name = "";
    /// <summary>Corner-grid terrain model (the shared model all renderers consume).</summary>
    public W3TerrainMap Terrain = new();
    public List<NativePlacement> Placements = new();

    /// <summary>Unknown terrain.bin sections preserved on load (tag → payload), re-emitted on save.</summary>
    public List<(string Tag, byte[] Data)> UnknownTerrainSections = new();
    /// <summary>Unknown placements.bin sections preserved on load, re-emitted on save.</summary>
    public List<(string Tag, byte[] Data)> UnknownPlacementSections = new();
}
