using System.Text;
using War3Net.IO.Mpq;

namespace W3Editor.Services;

/// <summary>
/// Offline .w3x/.w3m unpacker (War3Net pure-C# MPQ reader). The Godot runtime
/// still never touches MPQ archives — the editor extracts once to a folder and
/// everything downstream (open/save/export/preview) works on that folder.
/// </summary>
public static class W3xImporter
{
    /// <summary>
    /// war3map.* files every WC3 map may carry; used when the archive has no
    /// (listfile). Extraction is best-effort per file.
    /// </summary>
    private static readonly string[] KnownFiles =
    {
        "war3map.w3e", "war3map.w3i", "war3map.wpm", "war3map.doo", "war3mapUnits.doo",
        "war3map.shd", "war3map.mmp", "war3map.wts", "war3map.j", "war3map.lua",
        "war3map.w3u", "war3map.w3t", "war3map.w3a", "war3map.w3b", "war3map.w3d",
        "war3map.w3q", "war3map.w3h", "war3map.imp", "war3map.wct", "war3map.wtg",
        "war3mapMisc.txt", "war3mapSkin.txt", "war3mapExtra.txt",
        "war3mapMap.blp", "war3mapPreview.tga", "war3map.w3c", "war3map.w3r", "war3map.w3s",
        "conversation.json",
    };

    /// <summary>Default extraction folder: &lt;repo&gt;/war3/maps/&lt;name&gt;/source, else sibling of the archive.</summary>
    public static string DefaultDestination(string archivePath)
    {
        var stem = SanitizeName(Path.GetFileNameWithoutExtension(archivePath));
        var root = GodotLauncher.FindProjectRoot();
        return root != null
            ? Path.Combine(root, "war3", "maps", stem, "source")
            : Path.Combine(Path.GetDirectoryName(archivePath) ?? ".", stem + "_extracted");
    }

    /// <summary>Extracts the archive; returns (destination folder, extracted file count).</summary>
    public static (string Folder, int Files) Extract(string archivePath, string? destination = null)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"找不到地图文件：{archivePath}");
        var dest = destination ?? DefaultDestination(archivePath);
        Directory.CreateDirectory(dest);

        using var archive = MpqArchive.Open(archivePath);
        var names = CollectFileNames(archive);
        int extracted = 0;
        foreach (var name in names)
        {
            try
            {
                using var input = archive.OpenFile(name);
                var outPath = SafeJoin(dest, name);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                using var output = File.Create(outPath);
                input.CopyTo(output);
                extracted++;
            }
            catch
            {
                // Per-file corruption / encryption must not kill the whole import.
            }
        }
        if (extracted == 0 || !File.Exists(Path.Combine(dest, "war3map.w3e")))
            throw new InvalidDataException(
                $"解包失败：未能从 {Path.GetFileName(archivePath)} 提取 war3map.w3e（共提取 {extracted} 个文件）。");
        return (dest, extracted);
    }

    private static List<string> CollectFileNames(MpqArchive archive)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Prefer the archive's own (listfile): it also covers imported custom assets.
        try
        {
            if (archive.FileExists(ListFile.FileName))
            {
                using var s = archive.OpenFile(ListFile.FileName);
                using var reader = new StreamReader(s, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    var name = line.Trim();
                    if (name.Length == 0 || name.StartsWith('(')) continue;
                    if (seen.Add(name) && archive.FileExists(name))
                        names.Add(name);
                }
            }
        }
        catch
        {
            // No/broken listfile → fall through to the known-name probe.
        }

        foreach (var name in KnownFiles)
            if (seen.Add(name) && archive.FileExists(name))
                names.Add(name);
        return names;
    }

    /// <summary>Joins an archive-internal path under root, rejecting traversal ("..", rooted paths).</summary>
    private static string SafeJoin(string root, string archiveName)
    {
        var rel = archiveName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootFull = Path.GetFullPath(root + Path.DirectorySeparatorChar);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"归档内路径非法：{archiveName}");
        return full;
    }

    private static string SanitizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch);
        var s = sb.ToString().Trim();
        return s.Length == 0 ? "map" : s;
    }
}
