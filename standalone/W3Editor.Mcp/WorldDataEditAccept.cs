using System.Text.Json;
using W3Editor.Contracts;
using W3Editor.Formats;
using W3Editor.Kernel;
using W3Editor.Modules.Placement;
using W3Editor.Modules.Terrain;
using W3Editor.Modules.WorldData;
using W3Editor.Services;
using ContractMapFormat = W3Editor.Contracts.MapFormatKind;

namespace W3Editor.Mcp;

/// <summary>Headless gate for the same MapSession + MapDocument save path used by the GUI.</summary>
internal static class WorldDataEditAccept
{
    public static int Run(string[] args)
    {
        string root = GodotLauncher.FindProjectRoot()
            ?? throw new DirectoryNotFoundException("project root not found");
        string source = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(root, "testdata", "hivewe_sample");
        string work = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : Path.Combine(EditorPaths.OutDir, "worlddata_edit_accept");
        if (!File.Exists(Path.Combine(source, "war3map.w3e")))
            throw new FileNotFoundException("source map has no war3map.w3e", source);
        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        CopyTree(source, work);

        // Seed opening-state sidecars so the module document owns stale copies.
        // The regression only proves P0 if the second save could overwrite them.
        var seed = new MapSession();
        seed.Open(work);
        seed.EnsurePathing();
        seed.EnsureRegions().Regions.Add(new W3RegionRecord
        {
            Left = 0, Bottom = 0, Right = 128, Top = 128,
            Name = "Opening Snapshot Region", CreationNumber = 1,
        });
        seed.UpdateMapIdentity(new W3MapInfoIdentity
        {
            FormatVersion = 31,
            MapVersion = 1,
            EditorVersion = 1,
            GameVersionMajor = 1,
            Name = "Opening Snapshot Name",
            Author = "Acceptance",
        });
        seed.Save();

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        var session = new MapSession();
        var history = new EditHistory();
        session.Open(work);
        using var host = CreateWorldDataHost();
        MapDocument document = host.OpenDocument(
            work, ContractMapFormat.War3Folder, session.CaptureSavePayloads());

        W3PathingMap pathing = session.EnsurePathing();
        int cell = Math.Min(pathing.Cells.Length - 1, pathing.Width + 1);
        byte original = pathing.Cells[cell];
        byte edited = (byte)(original ^ W3PathingMap.Unwalkable);
        history.BeginPathingStroke(pathing);
        pathing.Cells[cell] = edited;
        history.CommitPathingStroke(pathing);
        checks["pathingCommandCreated"] = history.CanUndo;
        history.Undo(session);
        checks["pathingUndo"] = pathing.Cells[cell] == original && history.LastTouchedPathing;
        history.Redo(session);
        checks["pathingRedo"] = pathing.Cells[cell] == edited && history.LastTouchedPathing;
        session.MarkPathingDirty();

        W3RegionsFile regions = session.EnsureRegions();
        int creation = regions.NextCreationNumber();
        W3RegionRecord[] beforeAdd = regions.CloneRegions();
        regions.Regions.Add(new W3RegionRecord
        {
            Left = -512, Bottom = -256, Right = 384, Top = 640,
            Name = "Codex Acceptance Region", CreationNumber = creation,
            R = 84, G = 158, B = 146, A = 255,
        });
        history.Push(new RegionsSnapshotCommand(beforeAdd, regions.Regions));
        history.Undo(session);
        checks["regionAddUndo"] = regions.Regions.All(r => r.CreationNumber != creation)
            && history.LastTouchedRegions;
        history.Redo(session);
        checks["regionAddRedo"] = regions.Regions.Any(r => r.CreationNumber == creation)
            && history.LastTouchedRegions;

        W3RegionRecord target = regions.Regions.Single(r => r.CreationNumber == creation);
        W3RegionRecord[] beforeRename = regions.CloneRegions();
        target.Name = "Renamed Acceptance Region";
        history.Push(new RegionsSnapshotCommand(beforeRename, regions.Regions));
        history.Undo(session);
        checks["regionRenameUndo"] = regions.Regions.Single(r => r.CreationNumber == creation).Name
            == "Codex Acceptance Region";
        history.Redo(session);
        checks["regionRenameRedo"] = regions.Regions.Single(r => r.CreationNumber == creation).Name
            == "Renamed Acceptance Region";
        session.MarkRegionsDirty();

        W3MapInfoIdentity identity = session.MapIdentity
            ?? throw new InvalidDataException("seeded map identity was not loaded");
        identity.Name = "Saved Twice Acceptance Name";
        session.UpdateMapIdentity(identity);

        session.Save(document);
        long generation1 = document.Generation;
        checks["firstGenerationValid"] = AtomicFileSet.Verify(work).Ok;
        session.Save(document);
        long generation2 = document.Generation;
        checks["secondGenerationValid"] = AtomicFileSet.Verify(work).Ok;
        checks["singleCommitPerSave"] = generation2 == generation1 + 1;
        checks["wpmWritten"] = File.Exists(Path.Combine(work, "war3map.wpm"));
        checks["w3rWritten"] = File.Exists(Path.Combine(work, "war3map.w3r"));

        var reopened = new MapSession();
        reopened.Open(work);
        checks["pathingSaveReload"] = reopened.Pathing?.Cells[cell] == edited;
        W3RegionRecord? loaded = reopened.Regions?.Regions
            .SingleOrDefault(r => r.CreationNumber == creation);
        checks["regionSaveReload"] = loaded is
        {
            Name: "Renamed Acceptance Region",
            Left: -512, Bottom: -256, Right: 384, Top: 640,
            R: 84, G: 158, B: 146, A: 255,
        };
        checks["mapInfoDoubleSaveReload"] = reopened.MapIdentity?.Name
            == "Saved Twice Acceptance Name";

        int resizedWidth = NextValidSize(reopened.Map!.TileCountX);
        int resizedHeight = NextValidSize(reopened.Map.TileCountY);
        reopened.Resize(resizedWidth, resizedHeight);
        MapDocument resizedDocument = host.OpenDocument(
            work, ContractMapFormat.War3Folder, reopened.CaptureSavePayloads());
        checks["resizeRefreshesTerrainPart"] =
            resizedDocument.GetPart<TerrainPart>()?.Map.TileCountX == resizedWidth
            && resizedDocument.GetPart<TerrainPart>()?.Map.TileCountY == resizedHeight;
        checks["resizeRefreshesPathingPart"] =
            resizedDocument.GetPart<PathingPart>()?.Map.Width == resizedWidth * 4
            && resizedDocument.GetPart<PathingPart>()?.Map.Height == resizedHeight * 4;
        checks["pass"] = checks.Values.All(value => value);

        string json = JsonSerializer.Serialize(new
        {
            source,
            work,
            checks,
            pathingSize = reopened.Pathing == null ? null : $"{reopened.Pathing.Width}x{reopened.Pathing.Height}",
            regionCount = reopened.Regions?.Regions.Count ?? 0,
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        File.WriteAllText(Path.Combine(work, "worlddata-edit-accept.json"), json);
        return checks["pass"] ? 0 : 1;
    }

    private static ModuleHost CreateWorldDataHost()
    {
        var manifest = new ModulesFile
        {
            Modules = new List<ModuleEntry>
            {
                new() { Id = "part.terrain" },
                new() { Id = "part.doodads" },
                new() { Id = "part.units" },
                new() { Id = "part.pathing" },
                new() { Id = "part.regions" },
                new() { Id = "part.mapinfo" },
            },
        };
        var host = new ModuleHost();
        ModuleLoadReport report = host.Load(manifest, new[]
        {
            typeof(TerrainPartModule).Assembly,
            typeof(DoodadsPartModule).Assembly,
            typeof(PathingPartModule).Assembly,
        });
        if (report.Skipped.Count != 0 || report.Loaded.Count != manifest.Modules.Count)
        {
            host.Dispose();
            throw new InvalidOperationException("world-data acceptance modules did not load");
        }
        return host;
    }

    private static int NextValidSize(int current)
    {
        if (current < W3EMap.MinTiles)
            return W3EMap.MinTiles;
        if (current > W3EMap.MaxTiles)
            return W3EMap.MaxTiles;
        return current + W3EMap.TileStep <= W3EMap.MaxTiles
            ? current + W3EMap.TileStep
            : current - W3EMap.TileStep;
    }

    private static void CopyTree(string source, string destination)
    {
        foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
