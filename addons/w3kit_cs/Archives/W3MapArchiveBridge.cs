using System;
using System.IO;
using Godot;
using Diablo1Like.W3.Paths;

namespace Diablo1Like.W3.Archives;

/// <summary>已解压地图目录 / war3map.w3e 源适配（不支持运行时 MPQ）。</summary>
[GlobalClass]
public partial class W3MapArchiveBridge : RefCounted
{
    public string MapDir { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string ExtractError { get; set; } = "";

    // GDS 字段别名
    public string map_dir { get => MapDir; set => MapDir = value; }
    public string source_path { get => SourcePath; set => SourcePath = value; }
    public string extract_error { get => ExtractError; set => ExtractError = value; }

    public bool Open(string path)
    {
        SourcePath = path ?? "";
        ExtractError = "";
        if (string.IsNullOrEmpty(path))
        {
            ExtractError = "empty path";
            return false;
        }

        if (Directory.Exists(path))
        {
            MapDir = path.TrimEnd('/', '\\');
            if (File.Exists(Path.Combine(MapDir, "war3map.w3e")))
                return true;
            ExtractError = $"war3map.w3e not found in folder: {MapDir}";
            return false;
        }

        if (!File.Exists(path))
        {
            ExtractError = $"file not found: {path}";
            return false;
        }

        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (ext == "w3e")
        {
            MapDir = Path.GetDirectoryName(path) ?? "";
            return true;
        }

        if (ext is "w3x" or "w3m" or "mpq")
        {
            ExtractError =
                "MPQ archives are not supported at runtime. Import the map to a .res level first (war3/README.md).";
            return false;
        }

        ExtractError = $"unsupported map type: {ext}";
        return false;
    }

    public bool open(string path) => Open(path);

    public byte[] ReadFile(string rel)
    {
        string norm = SafeRelativePath(rel);
        if (string.IsNullOrEmpty(norm) || string.IsNullOrEmpty(MapDir))
            return Array.Empty<byte>();
        string full = Path.Combine(MapDir, norm.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
            full = FindCi(MapDir, norm);
        if (string.IsNullOrEmpty(full) || !File.Exists(full))
            return Array.Empty<byte>();
        return File.ReadAllBytes(full);
    }

    public byte[] read_file(string rel) => ReadFile(rel);

    public bool HasFile(string rel)
    {
        string norm = SafeRelativePath(rel);
        if (string.IsNullOrEmpty(norm))
            return false;
        if (ReadFile(norm).Length > 0)
            return true;
        return File.Exists(Path.Combine(MapDir, norm.Replace('/', Path.DirectorySeparatorChar)));
    }

    public bool has_file(string rel) => HasFile(rel);

    public string ResolvePath(string rel)
    {
        string norm = SafeRelativePath(rel);
        if (string.IsNullOrEmpty(norm))
            return "";
        string full = Path.Combine(MapDir, norm.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
            return full;
        string ci = FindCi(MapDir, norm);
        if (!string.IsNullOrEmpty(ci))
            return ci;
        var resolver = new W3PathResolverBridge();
        return resolver.ResolveGamePath(norm);
    }

    public string resolve_path(string rel) => ResolvePath(rel);

    private static string SafeRelativePath(string rel)
    {
        string norm = (rel ?? "").Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(norm) || norm.StartsWith('/') || norm.Contains(':'))
            return "";
        var parts = new System.Collections.Generic.List<string>();
        foreach (string part in norm.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") return "";
            parts.Add(part);
        }

        return string.Join('/', parts);
    }

    private static string FindCi(string root, string rel)
    {
        string[] parts = rel.Split('/');
        string cur = root;
        foreach (string p in parts)
        {
            if (string.IsNullOrEmpty(p)) continue;
            if (!Directory.Exists(cur))
                return "";
            string? match = null;
            foreach (string entry in Directory.EnumerateFileSystemEntries(cur))
            {
                string fn = Path.GetFileName(entry);
                if (string.Equals(fn, p, StringComparison.OrdinalIgnoreCase))
                {
                    match = fn;
                    break;
                }
            }

            if (match == null) return "";
            cur = Path.Combine(cur, match);
        }

        return File.Exists(cur) || Directory.Exists(cur) ? cur : "";
    }
}
