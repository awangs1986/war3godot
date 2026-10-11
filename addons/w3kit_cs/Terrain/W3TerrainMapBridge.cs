using System.IO;
using Godot;

namespace Diablo1Like.W3.Terrain;

/// <summary>
/// Godot-side adapter for the engine-agnostic W3TerrainMap (W3TerrainCore).
/// Copies serialized W3LevelData / W3EMap resources into the shared model so
/// the runtime renderers consume the exact algorithm core the editor uses.
/// </summary>
public static class W3TerrainMapBridge
{
    public static W3TerrainMap FromGodotResource(GodotObject resource)
    {
        Variant versionValue = resource.Get(HasProperty(resource, "w3e_version") ? "w3e_version" : "version");
        var map = new W3TerrainMap
        {
            Version = versionValue.AsInt32(),
            Tileset = resource.Get("tileset").AsString() is { Length: > 0 } tile ? tile[0] : 'A',
            CustomTileset = resource.Get("custom_tileset").AsBool(),
            Width = resource.Get("width").AsInt32(),
            Height = resource.Get("height").AsInt32(),
        };
        Vector2 center = resource.Get("center_offset").AsVector2();
        map.CenterOffsetX = center.X;
        map.CenterOffsetY = center.Y;
        map.GroundTilesets.AddRange(resource.Get("ground_tilesets").AsStringArray());
        map.CliffTilesets.AddRange(resource.Get("cliff_tilesets").AsStringArray());
        if (!HasProperty(resource, "ground_heights"))
            return FromGodotCornerObjects(map, resource);
        float[] ground = resource.Get("ground_heights").AsFloat32Array();
        float[] waterHeights = resource.Get("water_heights").AsFloat32Array();
        byte[] flags = resource.Get("corner_flags").AsByteArray();
        byte[] groundTextures = resource.Get("ground_textures").AsByteArray();
        byte[] groundVariations = resource.Get("ground_variations").AsByteArray();
        byte[] cliffVariations = resource.Get("cliff_variations").AsByteArray();
        byte[] cliffTextures = resource.Get("cliff_textures").AsByteArray();
        byte[] layers = resource.Get("layer_heights").AsByteArray();
        int count = checked(map.Width * map.Height);
        if (ground.Length != count || waterHeights.Length != count || flags.Length != count ||
            groundTextures.Length != count || groundVariations.Length != count || cliffVariations.Length != count ||
            cliffTextures.Length != count || layers.Length != count)
            throw new InvalidDataException("W3LevelData corner arrays have inconsistent lengths.");
        map.Corners = new W3TerrainMap.Corner[count];
        for (int i = 0; i < count; i++)
        {
            map.Corners[i] = new W3TerrainMap.Corner
            {
                GroundHeight = ground[i],
                WaterHeight = waterHeights[i],
                MapEdge = (flags[i] & 1) != 0,
                Ramp = (flags[i] & 2) != 0,
                Blight = (flags[i] & 4) != 0,
                Water = (flags[i] & 8) != 0,
                Boundary = (flags[i] & 16) != 0,
                GroundTexture = groundTextures[i],
                GroundVariation = groundVariations[i],
                CliffVariation = cliffVariations[i],
                CliffTexture = cliffTextures[i],
                LayerHeight = layers[i],
            };
        }
        map.InitializeRenderCorners();
        return map;
    }

    private static W3TerrainMap FromGodotCornerObjects(W3TerrainMap map, GodotObject resource)
    {
        int count = checked(map.Width * map.Height);
        map.Corners = new W3TerrainMap.Corner[count];
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            GodotObject corner = resource.Call("get_corner", x, y).AsGodotObject()
                ?? throw new InvalidDataException($"Missing corner {x},{y}.");
            map.Corners[y * map.Width + x] = new W3TerrainMap.Corner
            {
                GroundHeight = (float)corner.Get("ground_height").AsDouble(),
                WaterHeight = (float)corner.Get("water_height").AsDouble(),
                MapEdge = corner.Get("map_edge").AsBool(),
                Ramp = corner.Get("ramp").AsBool(),
                Blight = corner.Get("blight").AsBool(),
                Water = corner.Get("water").AsBool(),
                Boundary = corner.Get("boundary").AsBool(),
                GroundTexture = (byte)corner.Get("ground_texture").AsInt32(),
                GroundVariation = (byte)corner.Get("ground_variation").AsInt32(),
                CliffVariation = (byte)corner.Get("cliff_variation").AsInt32(),
                CliffTexture = (byte)corner.Get("cliff_texture").AsInt32(),
                LayerHeight = (byte)corner.Get("layer_height").AsInt32(),
            };
        }
        map.InitializeRenderCorners();
        return map;
    }

    private static bool HasProperty(GodotObject resource, StringName property)
    {
        foreach (Godot.Collections.Dictionary entry in resource.GetPropertyList())
            if (entry.TryGetValue("name", out Variant name) && name.AsStringName() == property)
                return true;
        return false;
    }
}
