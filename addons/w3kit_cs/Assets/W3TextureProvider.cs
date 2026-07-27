using System;
using System.Collections.Generic;
using System.IO;
using Diablo1Like.W3.Formats;
using Diablo1Like.W3.Paths;
using Diablo1Like.W3.Terrain;
using Godot;

namespace Diablo1Like.W3.Assets;

/// <summary>
/// 解析地图/资产根下的 BLP/PNG/DDS 贴图；地面贴图查 Terrain.slk（tileID→dir/file），
/// 悬崖→地面映射查 CliffTypes.slk（HiveWE/Warsmash groundTile）。
/// 迁移自 w3_texture_provider.gd，行为保持一致（含经典优先、Reforged 回退顺序）。
/// </summary>
[GlobalClass]
public partial class W3TextureProvider : RefCounted
{
    private static readonly Dictionary<string, string> BlightByTileset = new(StringComparer.Ordinal)
    {
        ["L"] = "TerrainArt/Blight/Lords_Blight.blp",
        ["F"] = "TerrainArt/Blight/Lordf_Blight.blp",
        ["W"] = "TerrainArt/Blight/Lordw_Blight.blp",
        ["B"] = "TerrainArt/Blight/Barrens_Blight.blp",
        ["A"] = "TerrainArt/Blight/Ashen_Blight.blp",
        ["C"] = "TerrainArt/Blight/Felwood_Blight.blp",
        ["N"] = "TerrainArt/Blight/North_Blight.blp",
        ["Y"] = "TerrainArt/Blight/Village_Blight.blp",
        ["X"] = "TerrainArt/Blight/Village_Blight.blp",
        ["V"] = "TerrainArt/Blight/Village_Blight.blp",
        ["Q"] = "TerrainArt/Blight/VillageFall_Blight.blp",
        ["D"] = "TerrainArt/Blight/Dungeon_Blight.blp",
        ["G"] = "TerrainArt/Blight/G_Blight.blp",
        ["Z"] = "TerrainArt/Blight/Ruins_Blight.blp",
        ["I"] = "TerrainArt/Blight/Ice_Blight.blp",
        ["J"] = "TerrainArt/Blight/DRuins_Blight.blp",
        ["O"] = "TerrainArt/Blight/Outland_Blight.blp",
        ["K"] = "TerrainArt/Blight/Citadel_Blight.blp",
    };

    private static readonly Dictionary<string, string> TilesetFolders = new(StringComparer.Ordinal)
    {
        ["L"] = "LordaeronSummer",
        ["F"] = "LordaeronFall",
        ["W"] = "LordaeronWinter",
        ["B"] = "Barrens",
        ["A"] = "Ashenvale",
        ["C"] = "Felwood",
        ["N"] = "Northrend",
        ["Y"] = "Cityscape",
        ["X"] = "Dalaran",
        ["V"] = "Village",
        ["Q"] = "VillageFall",
        ["D"] = "Dungeon",
        ["G"] = "Underground",
        ["Z"] = "Ruins",
        ["I"] = "Icecrown",
        ["J"] = "DalaranRuins",
        ["O"] = "Outland",
        ["K"] = "BlackCitadel",
    };

    private readonly W3PathResolverBridge _paths = new();
    private readonly Dictionary<string, Texture2D> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _cliffGroundById = new(StringComparer.Ordinal);
    private string _mapDir = "";

    /// <summary>TerrainArt/Terrain.slk 查询（tileID → 相对 BLP 路径）。GDS 侧以 provider.terrain_slk 直接访问。</summary>
    public W3TerrainSlkBridge TerrainSlk { get; } = new();

    public void SetMapDir(string path) => _mapDir = path.TrimEnd('/');

    public void set_map_dir(string path) => SetMapDir(path);

    public void LoadTerrainSlk(string assetRoot = "")
    {
        string root = string.IsNullOrEmpty(assetRoot) ? _paths.AssetRootPath() : assetRoot;
        TerrainSlk.LoadFromAssetRoot(root);
        LoadCliffGroundTiles(root);
    }

    public void load_terrain_slk(string assetRoot = "") => LoadTerrainSlk(assetRoot);

    public string GetCliffGroundTile(string cliffId) =>
        _cliffGroundById.TryGetValue(cliffId, out string? ground) ? ground : "";

    public string get_cliff_ground_tile(string cliffId) => GetCliffGroundTile(cliffId);

    private void LoadCliffGroundTiles(string assetRoot)
    {
        _cliffGroundById.Clear();
        if (string.IsNullOrEmpty(assetRoot))
            return;
        string path = Path.Combine(assetRoot, "TerrainArt", "CliffTypes.slk");
        if (!File.Exists(path))
            path = FindCi(assetRoot, "TerrainArt/CliffTypes.slk");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            GD.PushWarning($"W3TextureProvider: CliffTypes.slk missing under {assetRoot}");
            return;
        }
        string text = File.ReadAllText(path);
        foreach ((string id, CliffTypeInfo info) in TerrainSlkTables.ParseCliffTypes(text))
        {
            if (!string.IsNullOrEmpty(info.GroundTile))
                _cliffGroundById[id] = info.GroundTile;
        }
        GD.Print($"W3TextureProvider: loaded {_cliffGroundById.Count} cliff ground mappings");
    }

    // ---- 贴图/图像查询 --------------------------------------------------

    public Texture2D GetTexture(string relOrAbs)
    {
        string key = relOrAbs.Replace('\\', '/').ToLowerInvariant();
        if (_cache.TryGetValue(key, out Texture2D? cached))
            return cached;
        Image? img = LoadImage(relOrAbs);
        Texture2D tex;
        if (img != null)
        {
            // LINEAR_WITH_MIPMAPS 地形滤镜需要 mip；Reforged DDS 已带压缩 mip 链，
            // 重新生成在 Godot 里非法且会丢弃原始链。
            if (!img.IsCompressed() && !img.HasMipmaps() && img.GetWidth() >= 64 && img.GetHeight() >= 64)
                img.GenerateMipmaps();
            tex = ImageTexture.CreateFromImage(img);
        }
        else
        {
            tex = FallbackTexture();
        }
        _cache[key] = tex;
        return tex;
    }

    public Texture2D get_texture(string relOrAbs) => GetTexture(relOrAbs);

    public Texture2D GetGroundTexture(string tilesetId)
    {
        string rel = TerrainSlk.ResolveBlpRel(tilesetId);
        foreach (string candidate in GroundCandidates(rel, tilesetId))
        {
            if (ExistsAny(candidate) || ExistsAny(GetBasename(candidate) + ".png"))
                return GetTexture(candidate);
        }
        return GetTexture(rel);
    }

    public Texture2D get_ground_texture(string tilesetId) => GetGroundTexture(tilesetId);

    /// <summary>经典 Warsmash 地面切片用的原始 RGBA Image：无 mip，不走 ImageTexture。</summary>
    public Image? GetGroundImage(string tilesetId)
    {
        string rel = TerrainSlk.ResolveBlpRel(tilesetId);
        foreach (string candidate in GroundCandidates(rel, tilesetId))
        {
            if (ExistsAny(candidate) || ExistsAny(GetBasename(candidate) + ".png"))
            {
                Image? img = LoadImage(candidate);
                if (img != null)
                {
                    PrepareEditableImage(img);
                    return img;
                }
            }
        }
        Image? fallback = LoadImage(rel);
        if (fallback != null)
            PrepareEditableImage(fallback);
        return fallback;
    }

    public Image? get_ground_image(string tilesetId) => GetGroundImage(tilesetId);

    public Image? GetBlightImage(string tilesetLetter)
    {
        string rel = GetBlightRel(tilesetLetter);
        foreach (string candidate in GroundCandidatesBase(rel))
        {
            Image? img = LoadImage(candidate);
            if (img != null)
            {
                PrepareEditableImage(img);
                return img;
            }
        }
        return null;
    }

    public Image? get_blight_image(string tilesetLetter) => GetBlightImage(tilesetLetter);

    public string GetBlightRel(string tilesetLetter)
    {
        string key = tilesetLetter.Length > 0 ? tilesetLetter[..1] : "L";
        return BlightByTileset.TryGetValue(key, out string? rel) ? rel : BlightByTileset["L"];
    }

    public string get_blight_rel(string tilesetLetter) => GetBlightRel(tilesetLetter);

    public string ResolvePath(string relPath) => _paths.ResolvePath(relPath);

    public string resolve_path(string relPath) => ResolvePath(relPath);

    private static void PrepareEditableImage(Image image)
    {
        // DDS 解码器保留 BC 压缩；地形图集在 CPU 上切片/缩放，Godot 要求先显式解压。
        if (image.IsCompressed())
            image.Decompress();
        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);
    }

    /// <summary>
    /// 经典 BLP/PNG 优先，Reforged `name_diffuse.dds` 兜底——与编辑器 EditorAssets
    /// 和 HiveWE 经典 GroundTexture 顺序一致。Reforged 0-15 过渡格的 alpha 蒙版不同，
    /// 混用来源会破坏与编辑器/HiveWE 的像素一致性（2026-07-23，按用户要求统一）。
    /// </summary>
    private IEnumerable<string> GroundCandidatesBase(string classicRel)
    {
        yield return classicRel;
        if (_paths.HasReforgedCache())
            yield return GetBasename(classicRel) + "_diffuse.dds";
    }

    /// <summary>地面/悬崖贴图专用：在基础候选之后再补 PNG sidecar 与按字母猜的目录。</summary>
    private IEnumerable<string> GroundCandidates(string classicRel, string tilesetId)
    {
        foreach (string candidate in GroundCandidatesBase(classicRel))
            yield return candidate;
        yield return GetBasename(classicRel) + ".png";
        string folderGuess = TilesetFolder(tilesetId.Length > 0 ? tilesetId[..1] : "L");
        yield return $"TerrainArt/{folderGuess}/{tilesetId}.blp";
    }

    private static string TilesetFolder(string letter) =>
        TilesetFolders.TryGetValue(letter, out string? folder) ? folder : "LordaeronSummer";

    private Image? LoadImage(string relOrAbs)
    {
        string norm = relOrAbs.Replace('\\', '/');
        var paths = new List<string>();
        bool isAbsolute = norm.StartsWith('/') || (norm.Length > 2 && norm[1] == ':');
        if (isAbsolute)
        {
            paths.Add(norm);
        }
        else
        {
            if (!string.IsNullOrEmpty(_mapDir))
            {
                paths.Add(PathJoin(_mapDir, norm));
                paths.Add(PathJoin(_mapDir, GetBasename(norm) + ".png"));
            }
            if (_paths.HasRoot())
            {
                string resolved = _paths.ResolveGamePath(norm);
                if (!string.IsNullOrEmpty(resolved))
                {
                    paths.Add(resolved);
                    paths.Add(GetBasename(resolved) + ".png");
                }
                string root = _paths.AssetRootPath();
                paths.Add(PathJoin(root, norm));
                paths.Add(PathJoin(root, GetBasename(norm) + ".png"));
            }
        }
        foreach (string p in paths)
        {
            Image? img = BlpDecoderBridge.LoadImage(p);
            if (img != null)
                return img;
        }
        return null;
    }

    private bool ExistsAny(string rel)
    {
        string norm = rel.Replace('\\', '/');
        string png = GetBasename(norm) + ".png";
        if (!string.IsNullOrEmpty(_mapDir))
        {
            if (File.Exists(PathJoin(_mapDir, norm)) || File.Exists(PathJoin(_mapDir, png)))
                return true;
        }
        if (_paths.HasRoot())
        {
            if (!string.IsNullOrEmpty(_paths.ResolveGamePath(norm)))
                return true;
            if (!string.IsNullOrEmpty(_paths.ResolveGamePath(png)))
                return true;
            string root = _paths.AssetRootPath();
            if (File.Exists(PathJoin(root, norm)) || File.Exists(PathJoin(root, png)))
                return true;
        }
        return false;
    }

    private static Texture2D FallbackTexture()
    {
        var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                bool on = ((x >> 3) + (y >> 3)) % 2 == 0;
                img.SetPixel(x, y, on ? new Color(0.4f, 0.55f, 0.3f) : new Color(0.3f, 0.42f, 0.22f));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static string PathJoin(string root, string rel) =>
        string.IsNullOrEmpty(root) ? rel : $"{root.TrimEnd('/')}/{rel.TrimStart('/')}";

    /// <summary>Godot get_basename() 等价：去掉最后一个扩展名，保留目录部分。</summary>
    private static string GetBasename(string path)
    {
        int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        int dot = path.LastIndexOf('.');
        return dot > slash ? path[..dot] : path;
    }

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
