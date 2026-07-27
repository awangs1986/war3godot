using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using W3Compat.Paths;

namespace Diablo1Like.W3.Paths;

/// <summary>
/// Godot 侧资产路径解析：读取配置、大小写不敏感目录行走与缓存。
/// 规范化规则来自 W3Compat.Paths.W3PathNorm。
/// </summary>
[GlobalClass]
public partial class W3PathResolverBridge : RefCounted
{
    // 静态缓存：GDS 每次 ClassDB.instantiate 新实例，必须跨实例共享
    private static readonly Dictionary<string, string> CiCache = new();
    private static readonly Dictionary<string, Dictionary<string, string>> DirCaseCache = new();

    /// <summary>解析当前资产根绝对路径。</summary>
    public string AssetRootPath()
    {
        if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
        {
            Node? node = tree.Root.GetNodeOrNull("W3AssetConfig");
            if (node != null)
            {
                string root = node.Get("asset_root").AsString();
                if (!string.IsNullOrEmpty(root))
                    return root;
            }
        }

        var cfg = new ConfigFile();
        if (cfg.Load("user://w3_asset_config.cfg") == Error.Ok)
        {
            string v = cfg.GetValue("w3", "asset_root", "").AsString();
            if (!string.IsNullOrEmpty(v))
                return v;
        }

        string? fromJson = ReadLocalPathsString("asset_root");
        if (!string.IsNullOrEmpty(fromJson))
            return fromJson;

        string fallback = ProjectSettings.GlobalizePath("res://war3/game");
        return Directory.Exists(fallback) ? fallback : string.Empty;
    }

    public string asset_root_path() => AssetRootPath();

    /// <summary>资产根是否存在。</summary>
    public bool HasRoot()
    {
        string r = AssetRootPath();
        return !string.IsNullOrEmpty(r) && Directory.Exists(r);
    }

    public bool has_root() => HasRoot();

    /// <summary>Reforged 缓存根路径。</summary>
    public string ReforgedCacheRootPath()
    {
        string? configured = ReadLocalPathsString("reforged_cache_root");
        if (!string.IsNullOrEmpty(configured))
            return configured;
        return ProjectSettings.GlobalizePath("res://war3/reforged");
    }

    public string reforged_cache_root_path() => ReforgedCacheRootPath();

    public bool HasReforgedCache() => Directory.Exists(ReforgedCacheRootPath());
    public bool has_reforged_cache() => HasReforgedCache();

    /// <summary>仅在 Reforged 缓存中解析。</summary>
    public string ResolveReforgedPath(string relPath)
    {
        if (string.IsNullOrEmpty(relPath) || !HasReforgedCache())
            return string.Empty;
        return ResolveCiCached(ReforgedCacheRootPath(), W3PathNorm.NormalizeRelative(relPath));
    }

    public string resolve_reforged_path(string relPath) => ResolveReforgedPath(relPath);

    /// <summary>游戏数据查找：Reforged 覆盖经典导出。</summary>
    public string ResolveGamePath(string relPath)
    {
        string hd = ResolveReforgedPath(relPath);
        return string.IsNullOrEmpty(hd) ? ResolvePath(relPath) : hd;
    }

    public string resolve_game_path(string relPath) => ResolveGamePath(relPath);

    /// <summary>在经典资产根中解析相对路径。</summary>
    public string ResolvePath(string relPath)
    {
        string root = AssetRootPath();
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(relPath))
            return string.Empty;

        string norm = W3PathNorm.NormalizeRelative(relPath);
        if (OS.GetName() == "Windows")
            return ResolveCiCached(root, norm);

        string full = Path.Combine(root, norm.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
            return full;

        string altNorm = W3PathNorm.TitleCaseSegments(norm);
        if (altNorm != norm)
        {
            string alt = Path.Combine(root, altNorm.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(alt))
                return alt;
        }

        return ResolveCiCached(root, norm);
    }

    public string resolve_path(string relPath) => ResolvePath(relPath);

    /// <summary>清空大小写解析缓存。</summary>
    public void ClearResolveCache()
    {
        CiCache.Clear();
        DirCaseCache.Clear();
    }

    public void clear_resolve_cache() => ClearResolveCache();

    private string ResolveCiCached(string root, string norm)
    {
        string key = W3PathNorm.CacheKey(root, norm);
        if (CiCache.TryGetValue(key, out string? cached))
            return cached;

        string[] parts = norm.Split('/');
        string cur = root;
        foreach (string part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            string dirKey = cur.Replace('\\', '/').ToLowerInvariant();
            if (!DirCaseCache.TryGetValue(dirKey, out Dictionary<string, string>? entries))
            {
                entries = new Dictionary<string, string>();
                if (!Directory.Exists(cur))
                {
                    CiCache[key] = string.Empty;
                    return string.Empty;
                }

                foreach (string entryPath in Directory.EnumerateFileSystemEntries(cur))
                {
                    string fn = Path.GetFileName(entryPath);
                    if (fn is "." or "..")
                        continue;
                    string lower = fn.ToLowerInvariant();
                    if (!entries.TryGetValue(lower, out string? previous) ||
                        W3PathNorm.CaseSpecificity(fn) > W3PathNorm.CaseSpecificity(previous))
                    {
                        entries[lower] = fn;
                    }
                }

                DirCaseCache[dirKey] = entries;
            }

            if (!entries.TryGetValue(part.ToLowerInvariant(), out string? matchName))
            {
                CiCache[key] = string.Empty;
                return string.Empty;
            }

            cur = Path.Combine(cur, matchName);
        }

        string result = File.Exists(cur) ? cur : string.Empty;
        CiCache[key] = result;
        return result;
    }

    private static string? ReadLocalPathsString(string key)
    {
        string path = ProjectSettings.GlobalizePath("res://war3/local_paths.json");
        if (!File.Exists(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty(key, out JsonElement el))
                return null;
            string configured = el.GetString() ?? string.Empty;
            if (string.IsNullOrEmpty(configured))
                return null;
            if (configured.StartsWith("res://") || configured.StartsWith("user://"))
                return ProjectSettings.GlobalizePath(configured).TrimEnd('/', '\\');
            return configured.TrimEnd('/', '\\');
        }
        catch
        {
            return null;
        }
    }
}
