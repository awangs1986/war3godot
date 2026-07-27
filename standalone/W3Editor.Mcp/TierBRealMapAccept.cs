using System.Text.Json;
using W3Editor.Contracts;
using W3Editor.Kernel;
using W3Editor.Modules.Terrain;
using W3Editor.Services;
using MapFormatKind = W3Editor.Contracts.MapFormatKind;

namespace W3Editor.Mcp;

/// <summary>
/// A3 acceptance against an extracted, genuinely structured War3 map rather
/// than synthetic extension payloads.  The source must contain triggers and
/// object data.  HiveWE's own test map is the canonical local fixture.
/// </summary>
internal static class TierBRealMapAccept
{
    private static readonly Dictionary<string, string[]> Ownership = new(StringComparer.Ordinal)
    {
        ["part.sounds"] = ["war3map.w3s"],
        ["part.imports"] = ["war3map.imp"],
        ["part.gameplay"] = ["war3mapMisc.txt", "war3mapSkin.txt"],
        ["part.triggerstrings"] = ["war3map.wts"],
        ["part.triggers"] = ["war3map.wtg", "war3map.wct", "war3map.j", "war3map.lua"],
        ["part.objects"] =
        [
            "war3map.w3u", "war3map.w3t", "war3map.w3a", "war3map.w3b",
            "war3map.w3d", "war3map.w3q", "war3map.w3h",
            "war3mapSkin.w3u", "war3mapSkin.w3t", "war3mapSkin.w3a",
            "war3mapSkin.w3b", "war3mapSkin.w3d", "war3mapSkin.w3q",
            "war3mapSkin.w3h",
        ],
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Usage: --tierb-real-map-accept <extracted-map-folder> [workDir]");

        string root = GodotLauncher.FindProjectRoot()
            ?? throw new InvalidOperationException("project root (project.godot) not found");
        string source = Path.GetFullPath(args[1]);
        string work = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : Path.Combine(EditorPaths.OutDir, "tierb_real_map_accept");
        string manifestPath = Path.Combine(root, "standalone", "modules.json");
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(source);
        Require(source, "war3map.w3e");
        Require(source, "war3map.wtg");
        Require(source, "war3map.w3u");

        ResetFolder(work);
        CopyFolder(source, work);
        bool injectedValidSound = !File.Exists(Path.Combine(work, "war3map.w3s"));
        if (injectedValidSound)
            File.WriteAllBytes(Path.Combine(work, "war3map.w3s"), CreateValidV1SoundFile());

        var before = Snapshot(work);
        bool structuredContent = before["war3map.wtg"].Length > 8
            && before["war3map.w3u"].Length >= 12
            && before["war3map.w3s"].Length > 8;

        bool tierBPartsClaimed;
        using (var host = new ModuleHost())
        {
            host.Load(manifestPath);
            MapDocument document = host.OpenDocument(work, MapFormatKind.War3Folder);
            tierBPartsClaimed = Ownership.Keys.All(id => document.GetPart(id["part.".Length..]) != null);
            TerrainPart terrain = document.GetPart<TerrainPart>()
                ?? throw new InvalidDataException("real map did not load terrain");
            int center = (terrain.Map.Height / 2) * terrain.Map.Width + terrain.Map.Width / 2;
            terrain.Map.Corners[center].GroundHeight += 0.125f;
            terrain.MarkDirty();
            document.Save();
        }

        var after = Snapshot(work);
        bool terrainChanged = !before["war3map.w3e"].SequenceEqual(after["war3map.w3e"]);
        var nonTerrainChanged = before.Keys
            .Where(name => !name.Equals("war3map.w3e", StringComparison.OrdinalIgnoreCase))
            .Where(name => !after.TryGetValue(name, out byte[]? bytes) || !before[name].SequenceEqual(bytes))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool allExceptTerrainByteIdentical = nonTerrainChanged.Length == 0
            && after.Keys.All(before.ContainsKey);

        bool removalMatrix = true;
        var removalCases = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach ((string moduleId, string[] ownedNames) in Ownership)
        {
            string caseDir = Path.Combine(Path.GetDirectoryName(work)!,
                "tierb_real_drop_" + moduleId.Replace('.', '_'));
            ResetFolder(caseDir);
            CopyFolder(work, caseDir);
            var expected = Snapshot(caseDir);
            string[] presentOwned = ownedNames.Where(expected.ContainsKey).ToArray();

            ModulesFile manifest = ModuleHost.ReadManifest(manifestPath);
            manifest.Modules.Single(module => module.Id == moduleId).Enabled = false;
            bool oneCase;
            using (var host = new ModuleHost())
            {
                host.Load(manifest);
                MapDocument document = host.OpenDocument(caseDir, MapFormatKind.War3Folder);
                bool becameUnknown = presentOwned.Length > 0 && presentOwned.All(name =>
                    document.UnknownParts.Any(part =>
                        part.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)
                        && part.Bytes.SequenceEqual(expected[name])));
                document.Save();
                bool survived = presentOwned.All(name =>
                    File.ReadAllBytes(Path.Combine(caseDir, name)).SequenceEqual(expected[name]));
                oneCase = becameUnknown && survived;
            }

            removalCases[moduleId] = oneCase;
            removalMatrix &= oneCase;
        }

        bool pass = structuredContent && tierBPartsClaimed && terrainChanged
            && allExceptTerrainByteIdentical && removalMatrix;
        var result = new Dictionary<string, object?>
        {
            ["source"] = source,
            ["work"] = work,
            ["sourceKind"] = "extracted structured War3 map",
            ["validW3sAddedBecauseSourceHadNone"] = injectedValidSound,
            ["structuredTriggerObjectSoundContent"] = structuredContent,
            ["tierBPartsClaimed"] = tierBPartsClaimed,
            ["terrainChanged"] = terrainChanged,
            ["allExceptW3eByteIdentical"] = allExceptTerrainByteIdentical,
            ["nonTerrainChangedFiles"] = nonTerrainChanged,
            ["tierBRemovalCases"] = removalCases,
            ["tierBRemovalMatrix"] = removalMatrix,
            ["pass"] = pass,
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(work, "tierb-real-map-accept.json"), json);
        Console.WriteLine(json);
        return pass ? 0 : 1;
    }

    private static byte[] CreateValidV1SoundFile()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(1); // HiveWE-supported w3s version
        writer.Write(1); // sound count
        WriteCString(writer, "A3AcceptanceSound");
        WriteCString(writer, "Sound\\Interface\\Warning.wav");
        WriteCString(writer, "DefaultEAXON");
        writer.Write(0u);       // flags
        writer.Write(10u);      // fade in
        writer.Write(10u);      // fade out
        writer.Write(127u);     // volume
        writer.Write(1.0f);     // pitch
        writer.Write(0.0f);     // pitch variance
        writer.Write(0u);       // priority
        writer.Write(0u);       // channel
        writer.Write(0.0f);     // min distance
        writer.Write(10000.0f); // max distance
        writer.Write(3000.0f);  // cutoff
        writer.Write(0.0f);     // cone inside
        writer.Write(0.0f);     // cone outside
        writer.Write(127u);     // cone outside volume
        writer.Write(0.0f); writer.Write(0.0f); writer.Write(0.0f);
        return stream.ToArray();
    }

    private static void WriteCString(BinaryWriter writer, string value)
    {
        writer.Write(System.Text.Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    private static void Require(string folder, string name)
    {
        if (!File.Exists(Path.Combine(folder, name)))
            throw new InvalidDataException($"real-map fixture is missing {name}: {folder}");
    }

    private static Dictionary<string, byte[]> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Name: Path.GetRelativePath(folder, path).Replace('\\', '/')))
            .Where(file => !AtomicFileSet.IsKernelFile(file.Name)
                && !Path.GetFileName(file.Name).Equals("tierb-real-map-accept.json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                file => file.Name,
                file => File.ReadAllBytes(file.Path),
                StringComparer.OrdinalIgnoreCase);

    private static void ResetFolder(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, file);
            string target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
