using System.Text.Json;
using Diablo1Like.MapFormat;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// End-to-end native editor acceptance:
/// War3 source → d1map → MapSession.Open → remove one doodad → save → undo
/// → save. The undo save must restore all three d1map files byte-for-byte.
/// Usage: --d1map-accept [sourceWar3Folder] [workDir]
/// </summary>
internal static class D1MapAccept
{
    public static int Run(string[] args)
    {
        string root = GodotLauncher.FindProjectRoot()
            ?? throw new InvalidOperationException("project root (project.godot) not found");
        string source = args.Length >= 2
            ? args[1]
            : Path.Combine(root, "war3", "maps", "LostTemple", "source");
        string work = args.Length >= 3
            ? args[2]
            : Path.Combine(EditorPaths.OutDir, "d1map_accept");

        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);

        var sourceSession = new MapSession();
        sourceSession.Open(source);
        sourceSession.DoodadsDoc.SpecialTrailer =
            BuildSpecialDoodadTrailer("ZZZZ", variation: 3, x: 2, y: 3);
        sourceSession.SaveNativeCopy(work, "d1map-accept");
        // Seed forward-compatible payloads. Byte restoration below therefore
        // also proves the editor preserves unknown sections and placement ext.
        NativeMap seeded = NativeMapFormat.Load(work);
        bool specialDoodadConverted = seeded.Placements.Any(p =>
            !p.IsUnit && p.Id == "ZZZZ" && p.Variation == 3
            && p.X == seeded.Terrain.CenterOffsetX + 2 * 128f
            && p.Y == seeded.Terrain.CenterOffsetY + 3 * 128f);
        seeded.UnknownTerrainSections.Add(("XTRN", new byte[] { 1, 3, 5, 7 }));
        seeded.UnknownPlacementSections.Add(("XPLC", new byte[] { 2, 4, 6 }));
        seeded.Placements.First(p => !p.IsUnit).Ext = new byte[] { 9, 8, 7, 6 };
        seeded.Terrain.Corners[0].GroundHeight = 0.1234567f;
        NativePlacement interleavedUnit = seeded.Placements.First(p => p.IsUnit);
        seeded.Placements.Remove(interleavedUnit);
        seeded.Placements.Insert(1, interleavedUnit);
        NativeMapFormat.Save(seeded, work);

        var original = Snapshot(work);
        var editor = new EditorSession();
        editor.Open(work);
        bool nativeUsesFullTextureLimit = editor.Map.MaxGroundTextures == 64;
        W3Placement victim = editor.Map.Doodads.First();
        int originalDoodads = editor.Map.Doodads.Count;

        using JsonDocument removed = JsonDocument.Parse(
            editor.RemoveObjects(victim.CreationNumber, null, null, 0f, "doodad"));
        bool removedExactlyOne =
            removed.RootElement.GetProperty("removed").GetInt32() == 1
            && editor.Map.Doodads.Count == originalDoodads - 1;
        editor.Save();
        bool placementsChanged = !original[NativeMapFormat.PlacementsFile]
            .AsSpan().SequenceEqual(File.ReadAllBytes(
                Path.Combine(work, NativeMapFormat.PlacementsFile)));

        editor.Undo();
        editor.Save();

        W3EMap terrain = editor.Map.Map
            ?? throw new InvalidOperationException("native editor map has no terrain");
        editor.History.BeginStroke(terrain);
        terrain.Corners[0].GroundHeight += 0.25f;
        editor.History.CommitStroke(terrain);
        if (!editor.History.CanUndo)
            throw new InvalidOperationException("terrain stroke was not recorded");
        editor.Map.MarkDirty();
        editor.Save();
        bool terrainChanged = !original[NativeMapFormat.TerrainFile]
            .AsSpan().SequenceEqual(File.ReadAllBytes(
                Path.Combine(work, NativeMapFormat.TerrainFile)));
        editor.Undo();
        editor.Save();

        var restored = Snapshot(work);
        bool manifestRestored = BytesEqual(original, restored, NativeMapFormat.ManifestFile);
        bool terrainRestored = BytesEqual(original, restored, NativeMapFormat.TerrainFile);
        bool placementsRestored = BytesEqual(original, restored, NativeMapFormat.PlacementsFile);

        // Force the third target to reject replacement. The first two files
        // must remain at the previous generation (all-or-nothing save).
        string placementsPath = Path.Combine(work, NativeMapFormat.PlacementsFile);
        string heldPlacements = placementsPath + ".held";
        File.Move(placementsPath, heldPlacements);
        Directory.CreateDirectory(placementsPath);
        bool saveFailed = false;
        bool transactionRolledBack;
        try
        {
            terrain.Corners[0].GroundHeight += 1f;
            editor.Map.MarkDirty();
            try
            {
                editor.Save();
            }
            catch (IOException)
            {
                saveFailed = true;
            }
            catch (UnauthorizedAccessException)
            {
                saveFailed = true;
            }
            transactionRolledBack = saveFailed
                && original[NativeMapFormat.ManifestFile].AsSpan().SequenceEqual(
                    File.ReadAllBytes(Path.Combine(work, NativeMapFormat.ManifestFile)))
                && original[NativeMapFormat.TerrainFile].AsSpan().SequenceEqual(
                    File.ReadAllBytes(Path.Combine(work, NativeMapFormat.TerrainFile)))
                && original[NativeMapFormat.PlacementsFile].AsSpan().SequenceEqual(
                    File.ReadAllBytes(heldPlacements));
        }
        finally
        {
            Directory.Delete(placementsPath);
            File.Move(heldPlacements, placementsPath);
        }

        bool rejectsMissingPlacementSection = RejectsPlacements(
            work, BuildPlacementContainer("JUNK", Array.Empty<byte>()));
        bool rejectsNegativePlacementCount = RejectsPlacements(
            work, BuildPlacementContainer("PLCM", BitConverter.GetBytes(-1)));
        bool rejectsTruncatedPlacement = RejectsPlacements(
            work, BuildPlacementContainer("PLCM", BitConverter.GetBytes(1)));
        bool rejectsTrailingPlacementBytes = RejectsPlacements(
            work, BuildPlacementContainer("PLCM", new byte[] { 0, 0, 0, 0, 1 }));

        bool pass = nativeUsesFullTextureLimit && removedExactlyOne && placementsChanged
            && terrainChanged
            && specialDoodadConverted
            && manifestRestored && terrainRestored && placementsRestored
            && transactionRolledBack
            && rejectsMissingPlacementSection && rejectsNegativePlacementCount
            && rejectsTruncatedPlacement && rejectsTrailingPlacementBytes;
        var result = new
        {
            source,
            work,
            originalDoodads,
            nativeUsesFullTextureLimit,
            removedExactlyOne,
            placementsChanged,
            terrainChanged,
            specialDoodadConverted,
            manifestRestored,
            terrainRestored,
            placementsRestored,
            transactionRolledBack,
            rejectsMissingPlacementSection,
            rejectsNegativePlacementCount,
            rejectsTruncatedPlacement,
            rejectsTrailingPlacementBytes,
            pass,
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(Path.Combine(work, "d1map_accept.json"), json);
        Console.WriteLine(json);
        return pass ? 0 : 1;
    }

    private static bool RejectsPlacements(string dir, byte[] invalid)
    {
        string path = Path.Combine(dir, NativeMapFormat.PlacementsFile);
        byte[] original = File.ReadAllBytes(path);
        try
        {
            File.WriteAllBytes(path, invalid);
            try
            {
                NativeMapFormat.Load(dir);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }
        finally
        {
            File.WriteAllBytes(path, original);
        }
    }

    private static byte[] BuildPlacementContainer(string tag, byte[] payload)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(0x4C50_3144u); // D1PL
        writer.Write(NativeMapFormat.Version);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(tag));
        writer.Write(payload.Length);
        writer.Write(payload);
        return ms.ToArray();
    }

    private static byte[] BuildSpecialDoodadTrailer(
        string id,
        int variation,
        int x,
        int y)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(0); // special doodad format version
        writer.Write(1);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(id));
        writer.Write(variation);
        writer.Write(x);
        writer.Write(y);
        return ms.ToArray();
    }

    private static Dictionary<string, byte[]> Snapshot(string dir) =>
        new[]
        {
            NativeMapFormat.ManifestFile,
            NativeMapFormat.TerrainFile,
            NativeMapFormat.PlacementsFile,
        }.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(dir, name)));

    private static bool BytesEqual(
        IReadOnlyDictionary<string, byte[]> left,
        IReadOnlyDictionary<string, byte[]> right,
        string name) =>
        left[name].AsSpan().SequenceEqual(right[name]);
}
