using Diablo1Like.W3.Terrain;
using W3Editor.Formats;

namespace W3Editor.Rendering;

/// <summary>
/// Keeps a shared-core <see cref="W3TerrainMap"/> in sync with the editor's
/// authoritative <see cref="W3EMap"/>. The GL renderer and the Godot runtime
/// both consume the shared-core type, so all block-map rules run on one code
/// path; the editor map stays the single mutation target for brushes/undo.
/// </summary>
public static class TerrainMirror
{
    public static W3TerrainMap Create(W3EMap source)
    {
        var map = new W3TerrainMap
        {
            Version = source.Version,
            Tileset = source.Tileset,
            CustomTileset = source.CustomTileset,
            Width = source.Width,
            Height = source.Height,
            CenterOffsetX = source.CenterOffsetX,
            CenterOffsetY = source.CenterOffsetY,
        };
        map.GroundTilesets.Clear();
        map.GroundTilesets.AddRange(source.GroundTilesets);
        map.CliffTilesets.Clear();
        map.CliffTilesets.AddRange(source.CliffTilesets);
        map.Corners = new W3TerrainMap.Corner[source.Width * source.Height];
        for (int i = 0; i < map.Corners.Length; i++)
            map.Corners[i] = new W3TerrainMap.Corner();
        SyncRegion(source, map, 0, 0, source.Width, source.Height);
        return map;
    }

    /// <summary>Copies corner data for [x0,x1) x [y0,y1) from the editor map.</summary>
    public static void SyncRegion(W3EMap source, W3TerrainMap target, int x0, int y0, int x1, int y1)
    {
        x0 = Math.Clamp(x0, 0, source.Width);
        y0 = Math.Clamp(y0, 0, source.Height);
        x1 = Math.Clamp(x1, 0, source.Width);
        y1 = Math.Clamp(y1, 0, source.Height);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                W3Corner src = source.Corners[y * source.Width + x];
                W3TerrainMap.Corner dst = target.Corners[y * target.Width + x];
                dst.GroundHeight = src.GroundHeight;
                dst.WaterHeight = src.WaterHeight;
                dst.MapEdge = src.MapEdge;
                dst.Ramp = src.Ramp;
                dst.Blight = src.Blight;
                dst.Water = src.Water;
                dst.Boundary = src.Boundary;
                dst.GroundTexture = (byte)src.GroundTexture;
                dst.CliffVariation = (byte)src.CliffVariation;
                dst.GroundVariation = (byte)src.GroundVariation;
                dst.CliffTexture = (byte)src.CliffTexture;
                dst.LayerHeight = (byte)src.LayerHeight;
            }
        }
    }
}
