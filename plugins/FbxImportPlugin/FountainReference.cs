namespace FbxImportPlugin;

/// <summary>
/// Lost Temple center fountain (City_Fountain) world-space reference size.
/// Measurement (2026-07-23): loaded
/// <c>war3/game/Doodads/Cityscape/Props/City_Fountain/City_Fountain.mdx</c>
/// via <c>W3Editor.Rendering.MdxStaticModel.LoadFile</c>, merged GEOS positions AABB;
/// extents ≈ (302.100, 317.647, 215.333), longest edge = 317.647 W3 world units
/// (TileSize = 128). MaxEdge = 10× this value for auto-scale.
/// </summary>
public static class FountainReference
{
    public const float EdgeWorldUnits = 317.647f;
    public const float MaxEdgeWorldUnits = EdgeWorldUnits * 10f;
}
