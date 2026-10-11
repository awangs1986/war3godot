using System;
using Diablo1Like.MapFormat;
using Diablo1Like.W3.Terrain;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.MapRuntime;

/// <summary>
/// Godot boundary adapter for d1map. Binary/JSON parsing stays in
/// W3TerrainCore; this class only marshals the loaded model into Variants
/// consumed by the existing runtime presentation layer.
/// </summary>
[GlobalClass]
public partial class NativeMapLoader : RefCounted
{
    public GDictionary TryLoadFolder(string folder)
    {
        try
        {
            return LoadFolder(folder);
        }
        catch (Exception ex)
        {
            return new GDictionary { ["error"] = ex.Message };
        }
    }

    public GDictionary try_load_folder(string folder) => TryLoadFolder(folder);

    public GDictionary LoadFolder(string folder)
    {
        NativeMap native = NativeMapFormat.Load(ProjectSettings.GlobalizePath(folder));
        W3TerrainMap map = native.Terrain;
        int count = checked(map.Width * map.Height);
        var groundHeights = new float[count];
        var waterHeights = new float[count];
        var flags = new byte[count];
        var groundTextures = new byte[count];
        var groundVariations = new byte[count];
        var cliffVariations = new byte[count];
        var cliffTextures = new byte[count];
        var layers = new byte[count];
        for (int i = 0; i < count; i++)
        {
            W3TerrainMap.Corner corner = map.Corners[i];
            groundHeights[i] = corner.GroundHeight;
            waterHeights[i] = corner.WaterHeight;
            byte value = 0;
            if (corner.MapEdge) value |= 1;
            if (corner.Ramp) value |= 2;
            if (corner.Blight) value |= 4;
            if (corner.Water) value |= 8;
            if (corner.Boundary) value |= 16;
            flags[i] = value;
            groundTextures[i] = corner.GroundTexture;
            groundVariations[i] = corner.GroundVariation;
            cliffVariations[i] = corner.CliffVariation;
            cliffTextures[i] = corner.CliffTexture;
            layers[i] = corner.LayerHeight;
        }

        var placements = new GArray();
        foreach (NativePlacement placement in native.Placements)
        {
            placements.Add(new GDictionary
            {
                ["id"] = placement.Id,
                ["is_unit"] = placement.IsUnit,
                ["variation"] = placement.Variation,
                // War3 X/Y/Z-up → Godot X/Y-up/Z.
                ["position"] = new Vector3(placement.X, placement.Z, placement.Y),
                ["angle"] = placement.AngleRad,
                ["scale"] = new Vector3(
                    placement.ScaleX, placement.ScaleZ, placement.ScaleY),
                ["player_owner"] = (int)placement.Owner,
                ["flags"] = (int)placement.Flags,
                ["creation_number"] = unchecked((int)placement.RefId),
            });
        }

        return new GDictionary
        {
            ["name"] = native.Name,
            ["version"] = map.Version,
            ["tileset"] = map.Tileset.ToString(),
            ["custom_tileset"] = map.CustomTileset,
            ["width"] = map.Width,
            ["height"] = map.Height,
            ["center_offset"] = new Vector2(map.CenterOffsetX, map.CenterOffsetY),
            ["ground_tilesets"] = map.GroundTilesets.ToArray(),
            ["cliff_tilesets"] = map.CliffTilesets.ToArray(),
            ["ground_heights"] = groundHeights,
            ["water_heights"] = waterHeights,
            ["corner_flags"] = flags,
            ["ground_textures"] = groundTextures,
            ["ground_variations"] = groundVariations,
            ["cliff_variations"] = cliffVariations,
            ["cliff_textures"] = cliffTextures,
            ["layer_heights"] = layers,
            ["placements"] = placements,
        };
    }

    public GDictionary load_folder(string folder) => LoadFolder(folder);
}
