using System.Numerics;

namespace Diablo1Like.W3.Terrain;

/// <summary>
/// HiveWE terrain.vert/cliff.vert surface conventions, converted to Y-up.
/// Shared by the editor and Godot; see docs/TERRAIN_CLIFF_CROSSCHECK_2026-10-04.md.
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
    /// HiveWE normal convention. Placement/base-layer offsets are applied later.
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
    /// Deform a cliff's rotated model normal using the same raw-height central
    /// differences as the ground shader. Layer steps must not enter this normal.
    /// localPosition and modelNormal are already rotated to Y-up world axes.
    /// </summary>
    public static Vector3 CliffNormal(W3TerrainMap map, int tileX, int tileY,
        Vector3 localPosition, Vector3 modelNormal)
    {
        int x = Math.Clamp((int)(tileX + localPosition.X / W3TerrainMap.TileSize), 0, map.Width - 1);
        int y = Math.Clamp((int)(tileY + localPosition.Z / W3TerrainMap.TileSize), 0, map.Height - 1);
        float left = map.GetCorner(Math.Max(x - 1, 0), y).GroundHeight;
        float right = map.GetCorner(Math.Min(x + 1, map.Width - 1), y).GroundHeight;
        float down = map.GetCorner(x, Math.Max(y - 1, 0)).GroundHeight;
        float up = map.GetCorner(x, Math.Min(y + 1, map.Height - 1)).GroundHeight;
        Vector3 terrain = Vector3.Normalize(new Vector3(left - right, 2f, down - up));
        // HiveWE: normalize(rotated_normal.xy + terrain_normal.xy,
        //                   rotated_normal.z * terrain_normal.z), Z-up → Y-up.
        Vector3 combined = new(modelNormal.X + terrain.X, modelNormal.Y * terrain.Y,
            modelNormal.Z + terrain.Z);
        return combined.LengthSquared() > 1e-12f ? Vector3.Normalize(combined) : Vector3.UnitY;
    }
}
