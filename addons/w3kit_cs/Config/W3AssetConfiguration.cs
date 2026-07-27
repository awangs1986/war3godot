using System.IO;
using System.Text.Json;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Config;

/// <summary>Autoload：War3 资产根与默认地图路径配置。</summary>
[GlobalClass]
public partial class W3AssetConfiguration : Node
{
    public const string ConfigPath = "user://w3_asset_config.cfg";
    public const string LocalPaths = "res://war3/local_paths.json";
    private const string Section = "w3";

    [Signal]
    public delegate void ConfigChangedEventHandler();

    /// <summary>解压后的 War3 资产根绝对路径。</summary>
    public string AssetRoot { get; set; } = "";

    /// <summary>默认地图路径。</summary>
    public string DefaultMapPath { get; set; } = "";
    // Godot 会把 PascalCase 暴露为 snake_case（asset_root / default_map_path），勿再声明同名属性。

    public override void _Ready() => LoadConfig();

    public void LoadConfig()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) == Error.Ok)
        {
            AssetRoot = Abs(cfg.GetValue(Section, "asset_root", "").AsString());
            DefaultMapPath = Abs(cfg.GetValue(Section, "default_map_path", "").AsString());
        }

        if (string.IsNullOrEmpty(AssetRoot) || string.IsNullOrEmpty(DefaultMapPath))
            ApplyLocalPathsJson();

        if (string.IsNullOrEmpty(AssetRoot))
        {
            string g = Abs("res://war3/game");
            if (Directory.Exists(g))
                AssetRoot = g;
        }

        if (string.IsNullOrEmpty(DefaultMapPath))
        {
            string m = Abs("res://war3/maps/Adrenaline");
            if (Directory.Exists(m))
                DefaultMapPath = m;
        }
    }

    public void load_config() => LoadConfig();

    public void SaveConfig()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "asset_root", AssetRoot);
        cfg.SetValue(Section, "default_map_path", DefaultMapPath);
        cfg.Save(ConfigPath);
        EmitSignal(SignalName.ConfigChanged);
    }

    public void save_config() => SaveConfig();

    public void SetAssetRoot(string path)
    {
        AssetRoot = Abs(path);
        SaveConfig();
    }

    public void set_asset_root(string path) => SetAssetRoot(path);

    public void SetDefaultMapPath(string path)
    {
        DefaultMapPath = Abs(path);
        SaveConfig();
    }

    public void set_default_map_path(string path) => SetDefaultMapPath(path);

    public bool HasAssetRoot() =>
        !string.IsNullOrEmpty(AssetRoot) && Directory.Exists(AssetRoot);

    public bool has_asset_root() => HasAssetRoot();

    public string Resolve(string relPath)
    {
        if (string.IsNullOrEmpty(relPath) || !HasAssetRoot())
            return "";
        string norm = relPath.Replace('\\', '/');
        while (norm.StartsWith('/'))
            norm = norm[1..];
        string full = Path.Combine(AssetRoot, norm.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
            return full;
        return FindCi(AssetRoot, norm);
    }

    public string resolve(string relPath) => Resolve(relPath);

    public GDictionary CheckReport()
    {
        var msgs = new Godot.Collections.Array<string>();
        bool ok = true;
        if (string.IsNullOrEmpty(AssetRoot))
        {
            msgs.Add("asset_root is empty — set via W3AssetConfig or tools/check_asset_root.");
            ok = false;
        }
        else if (!Directory.Exists(AssetRoot))
        {
            msgs.Add($"asset_root does not exist: {AssetRoot}");
            ok = false;
        }
        else
        {
            msgs.Add($"asset_root OK: {AssetRoot}");
            foreach (string sub in new[] { "Units", "TerrainArt", "ReplaceableTextures", "Doodads" })
            {
                string p = Path.Combine(AssetRoot, sub);
                msgs.Add(Directory.Exists(p) ? $"  + {sub}/" : $"  - missing {sub}/");
            }
        }

        if (!string.IsNullOrEmpty(DefaultMapPath))
        {
            if (File.Exists(DefaultMapPath) || Directory.Exists(DefaultMapPath))
                msgs.Add($"default_map_path OK: {DefaultMapPath}");
            else
            {
                msgs.Add($"default_map_path missing: {DefaultMapPath}");
                ok = false;
            }
        }
        else
        {
            msgs.Add("default_map_path unset");
            ok = false;
        }

        return new GDictionary { ["ok"] = ok, ["messages"] = msgs };
    }

    public GDictionary check_report() => CheckReport();

    private void ApplyLocalPathsJson()
    {
        string path = ProjectSettings.GlobalizePath(LocalPaths);
        if (!File.Exists(path))
            return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (string.IsNullOrEmpty(AssetRoot) &&
                doc.RootElement.TryGetProperty("asset_root", out JsonElement ar))
                AssetRoot = Abs(ar.GetString() ?? "");
            if (string.IsNullOrEmpty(DefaultMapPath) &&
                doc.RootElement.TryGetProperty("default_map_path", out JsonElement mp))
                DefaultMapPath = Abs(mp.GetString() ?? "");
        }
        catch
        {
            // 忽略损坏的 local_paths.json
        }
    }

    private static string Abs(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        if (path.StartsWith("res://") || path.StartsWith("user://"))
            return ProjectSettings.GlobalizePath(path).TrimEnd('/', '\\');
        return path.TrimEnd('/', '\\');
    }

    private static string FindCi(string root, string rel)
    {
        string[] parts = rel.Split('/');
        string cur = root;
        foreach (string p in parts)
        {
            if (string.IsNullOrEmpty(p)) continue;
            if (!Directory.Exists(cur)) return "";
            string? match = null;
            foreach (string entry in Directory.EnumerateFileSystemEntries(cur))
            {
                string fn = Path.GetFileName(entry);
                if (string.Equals(fn, p, System.StringComparison.OrdinalIgnoreCase))
                {
                    match = fn;
                    break;
                }
            }

            if (match == null) return "";
            cur = Path.Combine(cur, match);
        }

        return File.Exists(cur) ? cur : "";
    }
}
