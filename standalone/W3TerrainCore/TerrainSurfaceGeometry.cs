using System.Numerics;

namespace Diablo1Like.W3.Terrain;

/// <summary>
/// HiveWE ground triangles and Warsmash cliff seam normals, converted to Y-up.
/// Shared by the editor and Godot.
/// </summary>
public static class TerrainSurfaceGeometry
{
    private static readonly Vector2[] TriangleCorners =
    {
        new(1, 1), new(0, 1), new(0, 0),
        new(0, 0), new(1, 0), new(1, 1),
    };

    /// <summary>Two triangles joined on BL–TR, matching HiveWE terrain.vert.</summary>
    public static ReadOnlySpan<Vector2> GroundTriangleCorners => TriangleCorners;

    /// <summary>Height on the two rendered BL–TR triangles, not a bilinear patch.</summary>
    public static float GroundHeight(float bl, float br, float tl, float tr, float x, float y) =>
        y >= x
            ? bl + (tr - tl) * x + (tl - bl) * y
            : bl + (br - bl) * x + (tr - br) * y;

    /// <summary>
    /// Shared cliff deformation after the model has been rotated to local Y-up.
    /// Retains the existing Warsmash 3×3 bilinear displacement convention and
    /// Warsmash boundary-normal convention. Placement/base-layer offsets are applied later.
    /// </summary>
    public static (Vector3 Position, Vector3 Normal) DeformCliffVertex(W3TerrainMap map,
        int tileX, int tileY, Vector3 localPosition, Vector3 modelNormal)
    {
        float x = Math.Clamp(localPosition.X / W3TerrainMap.TileSize, 0f, 2f);
        float y = Math.Clamp(localPosition.Z / W3TerrainMap.TileSize, 0f, 2f);
        int ix = Math.Min((int)x, 1), iy = Math.Min((int)y, 1);
        float tx = x - ix, ty = y - iy;
        float Height(int dx, int dy) => map.GetCorner(
            Math.Clamp(tileX + dx, 0, map.Width - 1),
            Math.Clamp(tileY + dy, 0, map.Height - 1)).GroundHeight;
        float bl = Height(ix, iy), br = Height(ix + 1, iy);
        float tl = Height(ix, iy + 1), tr = Height(ix + 1, iy + 1);
        float ground = (bl + (br - bl) * tx) * (1f - ty) + (tl + (tr - tl) * tx) * ty;
        Vector3 normal = CliffNormal(map, tileX, tileY, localPosition, modelNormal);
        localPosition.Y += ground * W3TerrainMap.TileSize;
        return (localPosition, normal);
    }

    /// <summary>
    /// Warsmash TerrainShaders.Cliffs: each model-space grid plane replaces the
    /// corresponding normal component with the raw terrain gradient (Y-up).
    /// At a grid corner all components match the ground shader, even when the
    /// authored cliff normal tilts toward the wall. Off-grid components retain
    /// the model's shape. Layer steps must not enter the height differences.
    /// </summary>
    public static Vector3 CliffNormal(W3TerrainMap map, int tileX, int tileY,
        Vector3 localPosition, Vector3 modelNormal)
    {
        bool edgeX = TryGridCoordinate(localPosition.X, out int gridX);
        bool edgeY = TryGridCoordinate(localPosition.Z, out int gridY);
        bool edgeHeight = TryGridCoordinate(localPosition.Y, out _);
        // Use the same snapped corner for both edge classification and the
        // stencil: tiny negative MDX coordinates must not select the prior tile.
        int x = Math.Clamp(edgeX ? tileX + gridX : (int)(tileX + localPosition.X / W3TerrainMap.TileSize), 0, map.Width - 1);
        int y = Math.Clamp(edgeY ? tileY + gridY : (int)(tileY + localPosition.Z / W3TerrainMap.TileSize), 0, map.Height - 1);
        float left = map.GetCorner(Math.Max(x - 1, 0), y).GroundHeight;
        float right = map.GetCorner(Math.Min(x + 1, map.Width - 1), y).GroundHeight;
        float down = map.GetCorner(x, Math.Max(y - 1, 0)).GroundHeight;
        float up = map.GetCorner(x, Math.Min(y + 1, map.Height - 1)).GroundHeight;
        Vector3 combined = modelNormal;
        if (edgeX) combined.X = left - right;
        if (edgeY) combined.Z = down - up;
        if (edgeHeight) combined.Y = 2f;
        return combined.LengthSquared() > 1e-12f ? Vector3.Normalize(combined) : Vector3.UnitY;
    }

    private static bool TryGridCoordinate(float position, out int coordinate)
    {
        coordinate = (int)MathF.Round(position / W3TerrainMap.TileSize);
        // Real classic CliffsAAAC0 vertices intended to sit at zero include
        // -0.00034/-0.00024 world units. Warsmash's exact equality misses them.
        // This tolerance is < 1/100000 tile; positions/UVs are never altered.
        return MathF.Abs(position - coordinate * W3TerrainMap.TileSize) <= 0.001f;
    }
}
