using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>Headless gate for the same MapSession/EditHistory path used by the GUI.</summary>
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

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        var session = new MapSession();
        var history = new EditHistory();
        session.Open(work);

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

        session.Save();
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
