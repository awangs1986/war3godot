using Diablo1Like.MapFormat;
using Diablo1Like.W3.Terrain;
using W3Editor.Formats;
using W3Editor.Rendering;

namespace W3Editor.Services;

/// <summary>
/// Narrow compatibility bridge used while the editor still edits W3EMap and
/// W3Placement directly. It contains no file I/O.
/// </summary>
public static class NativeMapBridge
{
    public sealed record EditorModel(
        W3EMap Terrain,
        W3DooDocument Doodads,
        W3DooDocument Units);

    public static EditorModel ToEditorModel(NativeMap source)
    {
        W3TerrainMap terrain = source.Terrain;
        var map = new W3EMap
        {
            Version = terrain.Version,
            Tileset = terrain.Tileset,
            CustomTileset = terrain.CustomTileset,
            GroundTilesets = new List<string>(terrain.GroundTilesets),
            CliffTilesets = new List<string>(terrain.CliffTilesets),
            Width = terrain.Width,
            Height = terrain.Height,
            CenterOffsetX = terrain.CenterOffsetX,
            CenterOffsetY = terrain.CenterOffsetY,
            Corners = new W3Corner[terrain.Corners.Length],
        };
        for (int i = 0; i < terrain.Corners.Length; i++)
            map.Corners[i] = ToEditorCorner(terrain.Corners[i]);

        var doodads = new W3DooDocument { IsUnits = false };
        var units = new W3DooDocument { IsUnits = true };
        for (int i = 0; i < source.Placements.Count; i++)
        {
            W3Placement converted = ToEditorPlacement(source.Placements[i], i);
            (converted.IsUnit ? units : doodads).Placements.Add(converted);
        }
        return new EditorModel(map, doodads, units);
    }

    public static NativeMap FromEditorModel(
        string name,
        W3EMap terrain,
        IEnumerable<W3Placement> doodads,
        IEnumerable<W3Placement> units,
        NativeMap? template = null)
    {
        var result = new NativeMap
        {
            Name = name,
            Terrain = TerrainMirror.Create(terrain),
        };
        if (template != null)
        {
            result.UnknownTerrainSections.AddRange(
                template.UnknownTerrainSections.Select(CopySection));
            result.UnknownPlacementSections.AddRange(
                template.UnknownPlacementSections.Select(CopySection));
        }
        IEnumerable<W3Placement> ordered = doodads.Concat(units)
            .Select((placement, sequence) => (placement, sequence))
            .OrderBy(item => item.placement.NativeOrder < 0
                ? int.MaxValue
                : item.placement.NativeOrder)
            .ThenBy(item => item.sequence)
            .Select(item => item.placement);
        foreach (W3Placement placement in ordered)
            result.Placements.Add(ToNativePlacement(placement));
        return result;
    }

    private static W3Corner ToEditorCorner(W3TerrainMap.Corner source) => new()
    {
        GroundHeight = source.GroundHeight,
        WaterHeight = source.WaterHeight,
        MapEdge = source.MapEdge,
        Ramp = source.Ramp,
        Blight = source.Blight,
        Water = source.Water,
        Boundary = source.Boundary,
        GroundTexture = source.GroundTexture,
        GroundVariation = source.GroundVariation,
        CliffTexture = source.CliffTexture,
        CliffVariation = source.CliffVariation,
        LayerHeight = source.LayerHeight,
    };

    private static W3Placement ToEditorPlacement(
        NativePlacement source,
        int nativeOrder) => new()
    {
        IsUnit = source.IsUnit,
        Id = source.Id,
        Variation = source.Variation,
        X = source.X,
        Y = source.Y,
        Z = source.Z,
        Angle = source.AngleRad,
        ScaleX = source.ScaleX,
        ScaleY = source.ScaleY,
        ScaleZ = source.ScaleZ,
        PlayerOwner = source.Owner,
        Flags = source.Flags,
        CreationNumber = unchecked((int)source.RefId),
        NativeExt = source.Ext.ToArray(),
        NativeOrder = nativeOrder,
    };

    private static NativePlacement ToNativePlacement(W3Placement source) => new()
    {
        IsUnit = source.IsUnit,
        Id = source.Id,
        Variation = source.Variation,
        X = source.X,
        Y = source.Y,
        Z = source.Z,
        AngleRad = source.Angle,
        ScaleX = source.ScaleX,
        ScaleY = source.ScaleY,
        ScaleZ = source.ScaleZ,
        Owner = (byte)Math.Clamp(source.PlayerOwner, 0, byte.MaxValue),
        Flags = (byte)Math.Clamp(source.Flags, 0, byte.MaxValue),
        RefId = unchecked((uint)source.CreationNumber),
        Ext = source.NativeExt.ToArray(),
    };

    private static (string Tag, byte[] Data) CopySection((string Tag, byte[] Data) section) =>
        (section.Tag, section.Data.ToArray());
}
