using Diablo1Like.W3.Terrain;
using W3Editor.Services;

namespace W3Editor.Rendering;

/// <summary>
/// Editor-side game asset locator/decoder for the built-in GL renderer.
/// Mirrors the Godot texture provider's resolution order: map folder first,
/// then the Reforged cache (`*_diffuse.dds`), then the classic export
/// (PNG sidecars written by tools/mpqtool; BLP itself is not decoded here).
/// </summary>
public sealed class EditorAssets
{
    public string? GameRoot { get; }
    public string? ReforgedRoot { get; }
    public string MapDir { get; set; } = "";

    public Dictionary<string, GroundTileInfo> TerrainTiles { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, CliffTypeInfo> CliffTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, RgbaImage?> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    public EditorAssets()
    {
        var projectRoot = GodotLauncher.FindProjectRoot();
        if (projectRoot == null)
            return;
        string game = Path.Combine(projectRoot, "war3", "game");
        string reforged = Path.Combine(projectRoot, "war3", "reforged");
        GameRoot = Directory.Exists(game) ? game : null;
        ReforgedRoot = Directory.Exists(reforged) ? reforged : null;

        string? terrainSlk = ResolveDataFile("TerrainArt/Terrain.slk");
        if (terrainSlk != null)
            foreach (var pair in TerrainSlkTables.ParseTerrain(File.ReadAllText(terrainSlk)))
                TerrainTiles[pair.Key] = pair.Value;
        string? cliffSlk = ResolveDataFile("TerrainArt/CliffTypes.slk");
        if (cliffSlk != null)
            foreach (var pair in TerrainSlkTables.ParseCliffTypes(File.ReadAllText(cliffSlk)))
                CliffTypes[pair.Key] = pair.Value;
    }

    public bool HasGameData => GameRoot != null && TerrainTiles.Count > 0;

    /// <summary>Ground tiles of one tileset (Terrain.slk order = WE palette order). Tile id first letter = tileset.</summary>
    public List<GroundTileInfo> GroundTilesFor(char tileset) =>
        TerrainTiles.Values.Where(t => t.Id.Length == 4 && t.Id[0] == tileset).ToList();

    /// <summary>Cliff types of one tileset (CliffTypes.slk order). Cliff id second letter = tileset.</summary>
    public List<CliffTypeInfo> CliffTypesFor(char tileset) =>
        CliffTypes.Values.Where(c => c.Id.Length == 4 && c.Id[1] == tileset).ToList();

    /// <summary>Data files (slk): Reforged cache first, then classic root.</summary>
    public string? ResolveDataFile(string relative)
    {
        foreach (string? root in new[] { ReforgedRoot, GameRoot })
        {
            if (root == null) continue;
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>Model files (mdx) live in the classic export only.</summary>
    public string? ResolveModel(string relative)
    {
        if (!string.IsNullOrEmpty(MapDir))
        {
            string local = Path.Combine(MapDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(local))
                return local;
        }
        if (GameRoot == null)
            return null;
        string path = Path.Combine(GameRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Resolves + decodes a texture by trying, in order: map-local file, the
    /// Reforged `_diffuse.dds`, the exact relative path, and the `.png` sidecar.
    /// </summary>
    public RgbaImage? LoadTexture(string classicRel)
    {
        string key = classicRel.Replace('\\', '/');
        if (_imageCache.TryGetValue(key, out RgbaImage? cached))
            return cached;
        RgbaImage? image = LoadTextureUncached(key);
        _imageCache[key] = image;
        return image;
    }

    private RgbaImage? LoadTextureUncached(string classicRel)
    {
        string stem = classicRel.Contains('.') ? classicRel[..classicRel.LastIndexOf('.')] : classicRel;
        var candidates = new List<(string? Root, string Rel)>();
        if (!string.IsNullOrEmpty(MapDir))
        {
            candidates.Add((MapDir, stem + ".dds"));
            candidates.Add((MapDir, stem + ".png"));
        }
        // HiveWE renders from the classic BLP atlases; the Reforged _diffuse
        // atlases carry different blend masks (blocky transitions). Classic
        // first keeps the editor pixel-aligned with HiveWE (user 2026-07-22).
        if (GameRoot != null)
        {
            candidates.Add((GameRoot, stem + ".png"));
            candidates.Add((GameRoot, stem + ".dds"));
        }
        if (ReforgedRoot != null)
        {
            candidates.Add((ReforgedRoot, stem + "_diffuse.dds"));
            candidates.Add((ReforgedRoot, stem + ".dds"));
        }
        foreach ((string? root, string rel) in candidates)
        {
            if (root == null) continue;
            RgbaImage? image = RgbaImage.LoadFile(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (image != null)
                return image;
        }
        return null;
    }

    /// <summary>Ground atlas for a tile fourcc (Terrain.slk lookup; checkerboard fallback).</summary>
    public RgbaImage LoadGroundAtlas(string tileId)
    {
        if (TerrainTiles.TryGetValue(tileId, out GroundTileInfo? info))
        {
            RgbaImage? image = LoadTexture(info.ClassicRelPath);
            if (image != null)
                return image;
        }
        return RgbaImage.Checkerboard(256);
    }

    /// <summary>Blight atlas for the map tileset letter (same table as the Godot provider).</summary>
    public RgbaImage LoadBlightAtlas(char tileset)
    {
        string rel = tileset switch
        {
            'L' => "TerrainArt/Blight/Lords_Blight.blp",
            'F' => "TerrainArt/Blight/Lordf_Blight.blp",
            'W' => "TerrainArt/Blight/Lordw_Blight.blp",
            'B' => "TerrainArt/Blight/Barrens_Blight.blp",
            'A' => "TerrainArt/Blight/Ashen_Blight.blp",
            'C' => "TerrainArt/Blight/Felwood_Blight.blp",
            'N' => "TerrainArt/Blight/North_Blight.blp",
            'Y' or 'X' or 'V' => "TerrainArt/Blight/Village_Blight.blp",
            'Q' => "TerrainArt/Blight/VillageFall_Blight.blp",
            'D' => "TerrainArt/Blight/Dungeon_Blight.blp",
            'G' => "TerrainArt/Blight/G_Blight.blp",
            'Z' => "TerrainArt/Blight/Ruins_Blight.blp",
            'I' => "TerrainArt/Blight/Ice_Blight.blp",
            'J' => "TerrainArt/Blight/DRuins_Blight.blp",
            'O' => "TerrainArt/Blight/Outland_Blight.blp",
            'K' => "TerrainArt/Blight/Citadel_Blight.blp",
            _ => "TerrainArt/Blight/Lords_Blight.blp",
        };
        return LoadTexture(rel) ?? RgbaImage.Checkerboard(256);
    }

    public CliffTypeInfo CliffTypeFor(W3TerrainMap map, int cliffTextureIndex)
    {
        string id = cliffTextureIndex >= 0 && cliffTextureIndex < map.CliffTilesets.Count
            ? map.CliffTilesets[cliffTextureIndex]
            : "CLdi";
        return CliffTypes.TryGetValue(id, out CliffTypeInfo? info) ? info : CliffTypeInfo.Fallback;
    }

    /// <summary>Cliff surface texture (Reforged Cliff0.dds, classic tileset-prefixed PNG fallback).</summary>
    public RgbaImage LoadCliffTexture(W3TerrainMap map, int cliffTextureIndex)
    {
        CliffTypeInfo type = CliffTypeFor(map, cliffTextureIndex);
        return LoadTexture($"{type.TextureDir}/{type.TextureFile}.blp")
            ?? LoadTexture($"{type.TextureDir}/{map.Tileset}_{type.TextureFile}.blp")
            ?? RgbaImage.Solid(64, 64, 120, 104, 84);
    }

    /// <summary>CliffTilesets index → GroundTilesets index (CliffTypes.slk groundTile column).</summary>
    public List<int> BuildCliffToGround(W3TerrainMap map)
    {
        var mapping = new List<int>();
        foreach (string cliffId in map.CliffTilesets)
        {
            string groundId = CliffTypes.TryGetValue(cliffId, out CliffTypeInfo? info) ? info.GroundTile : "";
            int index = map.GroundTilesets.IndexOf(groundId);
            mapping.Add(index >= 0 ? index : 0);
        }
        return mapping;
    }

    public WaterConfig LoadWaterConfig(char tileset)
    {
        string? path = ResolveDataFile("TerrainArt/Water.slk");
        return WaterConfig.Parse(path != null ? File.ReadAllText(path) : null, tileset);
    }

    /// <summary>Animated water frames (classic tier PNG sidecars, 128x128 RGBA).</summary>
    public List<RgbaImage> LoadWaterFrames(WaterConfig config)
    {
        var frames = new List<RgbaImage>();
        if (GameRoot == null)
            return frames;
        for (int frame = 0; frame < config.TextureCount; frame++)
        {
            string rel = $"{config.TextureFile}{frame:00}.png";
            RgbaImage? image = RgbaImage.LoadFile(Path.Combine(GameRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (image == null)
                return new List<RgbaImage>(); // all-or-nothing, same as the Godot loader
            frames.Add(image.Resized(128, 128));
        }
        return frames;
    }

    public void ClearCache() => _imageCache.Clear();
}
