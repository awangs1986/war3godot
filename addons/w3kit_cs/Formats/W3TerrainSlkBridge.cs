using System;
using System.Collections.Generic;
using System.IO;
using Diablo1Like.W3.Terrain;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Formats;

/// <summary>TerrainArt/Terrain.slk 桥：解析在 W3TerrainCore.TerrainSlkTables。</summary>
[GlobalClass]
public partial class W3TerrainSlkBridge : RefCounted
{
    private Dictionary<string, GroundTileInfo> _byId =
        new(StringComparer.Ordinal);

    /// <summary>从资产根加载 Terrain.slk（大小写不敏感路径）。</summary>
    public void LoadFromAssetRoot(string assetRoot)
    {
        _byId.Clear();
        if (string.IsNullOrEmpty(assetRoot))
            return;

        string path = Path.Combine(assetRoot, "TerrainArt", "Terrain.slk");
        if (!File.Exists(path))
            path = FindCi(assetRoot, "TerrainArt/Terrain.slk");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            GD.PushWarning($"W3TerrainSlk: Terrain.slk missing under {assetRoot}");
            return;
        }

        string text = File.ReadAllText(path);
        _byId = TerrainSlkTables.ParseTerrain(text);
        GD.Print($"W3TerrainSlk: loaded {_byId.Count} tiles from {path}");
    }

    public void load_from_asset_root(string assetRoot) => LoadFromAssetRoot(assetRoot);

    /// <summary>tileID → 相对 BLP 路径。</summary>
    public string ResolveBlpRel(string tileId)
    {
        if (_byId.TryGetValue(tileId, out GroundTileInfo? info))
            return info.ClassicRelPath;
        return $"TerrainArt/{tileId}.blp";
    }

    public string resolve_blp_rel(string tileId) => ResolveBlpRel(tileId);

    /// <summary>调试：返回 id→{dir,file} 字典。</summary>
    public GDictionary Snapshot()
    {
        var d = new GDictionary();
        foreach ((string id, GroundTileInfo info) in _byId)
        {
            d[id] = new GDictionary
            {
                ["dir"] = info.Dir,
                ["file"] = info.File,
            };
        }
        return d;
    }

    public GDictionary snapshot() => Snapshot();

    private static string FindCi(string root, string rel)
    {
        string[] parts = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string cur = root;
        foreach (string part in parts)
        {
            if (!Directory.Exists(cur))
                return "";
            string? match = null;
            foreach (string entry in Directory.EnumerateFileSystemEntries(cur))
            {
                string name = Path.GetFileName(entry);
                if (name.Equals(part, StringComparison.OrdinalIgnoreCase))
                {
                    match = entry;
                    break;
                }
            }
            if (match == null)
                return "";
            cur = match;
        }
        return cur;
    }
}
