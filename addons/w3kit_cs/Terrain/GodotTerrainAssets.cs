using System;
using System.Collections.Generic;
using System.IO;
using Diablo1Like.W3.Mdx;
using Godot;

namespace Diablo1Like.W3.Terrain;

/// <summary>Godot asset adapter for the shared terrain build. Uses the shared SLK parser.</summary>
internal sealed class GodotTerrainAssets
{
    private readonly W3TerrainMap _map;
    private readonly Func<string, string> _resolve;
    private readonly Dictionary<string, CliffTypeInfo> _types;
    private readonly Dictionary<string, W3MdxModel?> _models = new(StringComparer.OrdinalIgnoreCase);

    public GodotTerrainAssets(W3TerrainMap map, Func<string, string> resolve)
    {
        _map = map;
        _resolve = resolve;
        string path = resolve("TerrainArt/CliffTypes.slk");
        _types = File.Exists(path) ? TerrainSlkTables.ParseCliffTypes(File.ReadAllText(path)) : new(StringComparer.OrdinalIgnoreCase);
    }

    public CliffTypeInfo TypeFor(int texture)
    {
        string id = texture >= 0 && texture < _map.CliffTilesets.Count ? _map.CliffTilesets[texture] : "CLdi";
        return _types.GetValueOrDefault(id, CliffTypeInfo.Fallback);
    }

    public W3MdxModel? LoadModel(string relative)
    {
        if (_models.TryGetValue(relative, out W3MdxModel? model)) return model;
        string path = _resolve(relative);
        model = File.Exists(path) ? W3MdxModel.LoadFile(path) : null;
        bool drawable = false;
        if (model != null)
            foreach (Variant value in model.geosets)
                if (value.AsGodotObject() is W3MdxGeosetData g && (g.lod == 0 || g.lod == -1)
                    && g.vertices.Length >= 3 && g.faces.Length >= 3)
                    drawable = true;
        if (!drawable) model = null;
        _models[relative] = model;
        return model;
    }

    public TerrainBuild Build() => TerrainBuild.Create(_map, path => LoadModel(path) != null, TypeFor);
}
