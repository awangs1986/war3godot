using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Diablo1Like.W3.Archives;
using Diablo1Like.W3.Formats;
using Godot;
using W3Compat.Formats;

namespace Diablo1Like.W3.Level;

/// <summary>把 War3 地图源转为项目自有 W3LevelDataResource（含 .res 写出）。</summary>
[GlobalClass]
public partial class W3LevelImporterBridge : RefCounted
{
    /// <summary>最近一次失败原因。</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>供 GDScript 读取静态错误（避免 CS0082 属性别名）。</summary>
    public static string GetLastError() => LastError;

    /// <summary>从目录 / .w3e 构建关卡资源；失败返回 null。</summary>
    public static W3LevelDataResource? Build(string sourcePath)
    {
        LastError = "";
        var archive = new W3MapArchiveBridge();
        if (!archive.Open(sourcePath))
            return Fail($"cannot open source: {archive.ExtractError}");

        byte[] w3eBytes = archive.ReadFile("war3map.w3e");
        if (w3eBytes.Length == 0 &&
            Path.GetExtension(sourcePath).Equals(".w3e", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(sourcePath))
        {
            w3eBytes = File.ReadAllBytes(sourcePath);
        }

        if (w3eBytes.Length == 0)
            return Fail("war3map.w3e is missing");

        W3EMapBridge? map = W3EMapBridge.LoadBytesStatic(w3eBytes);
        if (map == null)
            return Fail($"W3E parse failed: {W3EMapBridge.GetLastError()}");

        W3LevelDataResource level = W3LevelDataResource.FromW3e(map);
        level.source_path = sourcePath;
        level.source_w3e_sha256 = Sha256Hex(w3eBytes);
        level.imported_at_utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

        level.map_info_bytes = archive.ReadFile("war3map.w3i");
        W3MapInfoVersion info = W3MapInfoVersion.Parse(level.map_info_bytes);
        level.game_version_major = info.GameVersionMajor;
        level.game_version_minor = info.GameVersionMinor;
        level.game_version_patch = info.GameVersionPatch;
        level.game_version_build = info.GameVersionBuild;

        level.shadow_bytes = archive.ReadFile("war3map.shd");
        level.pathing_bytes = archive.ReadFile("war3map.wpm");
        level.doodad_bytes = archive.ReadFile("war3map.doo");
        level.unit_bytes = archive.ReadFile("war3mapUnits.doo");
        level.unit_override_bytes = archive.ReadFile("war3map.w3u");
        level.doodad_override_bytes = archive.ReadFile("war3map.w3d");
        level.destructable_override_bytes = archive.ReadFile("war3map.w3b");
        return level;
    }

    public static W3LevelDataResource? build(string sourcePath) => Build(sourcePath);

    /// <summary>构建并压缩保存为二进制 .res；返回 Godot Error 码。</summary>
    public static int ImportToFile(string sourcePath, string outputPath)
    {
        W3LevelDataResource? level = Build(sourcePath);
        if (level == null)
            return (int)Error.FileCorrupt;

        string ext = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        if (ext != "res")
        {
            LastError = "output must use binary .res format";
            return (int)Error.InvalidParameter;
        }

        string absOutput = ProjectSettings.GlobalizePath(outputPath);
        string? dir = Path.GetDirectoryName(absOutput);
        if (!string.IsNullOrEmpty(dir))
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                LastError = $"cannot create output directory: {dir} ({ex.Message})";
                return (int)Error.Failed;
            }
        }

        Error err = ResourceSaver.Save(level, outputPath, ResourceSaver.SaverFlags.Compress);
        if (err != Error.Ok)
            LastError = $"ResourceSaver failed with error {(int)err}";
        return (int)err;
    }

    public static int import_to_file(string sourcePath, string outputPath) =>
        ImportToFile(sourcePath, outputPath);

    private static W3LevelDataResource? Fail(string message)
    {
        LastError = message;
        GD.PushError($"W3LevelImporter: {message}");
        return null;
    }

    private static string Sha256Hex(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
