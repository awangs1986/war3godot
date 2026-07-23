using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// M2 acceptance chain (GOALS.md 中期里程碑 M2)：
///   copy map → delete one tree → save (v1) → undo → save again (v2)
///   → v2 must byte-match the original doo (undo really restored the tree).
/// Godot/HiveWE then open the v1 folder to confirm the tree is gone.
/// Usage: --m2-accept [sourceMapFolder] [workFolder]
/// </summary>
internal static class M2Accept
{
    public static int Run(string[] args)
    {
        string root = GodotLauncher.FindProjectRoot()
            ?? throw new InvalidOperationException("project root (project.godot) not found");
        string src = args.Length >= 2 ? args[1] : Path.Combine(root, "war3", "maps", "LostTemple", "source");
        string work = args.Length >= 3 ? args[2] : Path.Combine(root, "war3", "maps", "M2Accept", "source");

        // Fresh copy of the map folder.
        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        foreach (string file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(work, Path.GetFileName(file)));

        var session = new EditorSession();
        Console.Error.WriteLine(session.Open(work));
        var map = session.Map;
        byte[] originalDoo = File.ReadAllBytes(Path.Combine(work, "war3map.doo"));
        string unitsPath = Path.Combine(work, "war3mapUnits.doo");
        byte[] originalUnits = File.Exists(unitsPath) ? File.ReadAllBytes(unitsPath) : Array.Empty<byte>();

        // Pick a tree near the map center so the Godot shot can frame it.
        W3Placement? tree = map.Doodads
            .Where(p => p.Id.StartsWith("LT", StringComparison.Ordinal) || p.Id == "ATtr" || p.Id == "LTlt")
            .OrderBy(p => MathF.Abs(p.X) + MathF.Abs(p.Y))
            .FirstOrDefault();
        if (tree == null)
        {
            Console.Error.WriteLine("FAIL: no tree found");
            return 1;
        }
        int before = map.Doodads.Count;
        var treeInfo = new { tree.Id, tree.X, tree.Y, tree.CreationNumber };
        Console.Error.WriteLine($"tree: {JsonSerializer.Serialize(treeInfo)} of {before} doodads");

        // Delete through the session API (undo stack), save v1. kind=doodad:
        // creation numbers are per-file sequences and collide with units.
        string removed = session.RemoveObjects(tree.CreationNumber, null, null, 128f, kind: "doodad");
        Console.Error.WriteLine("remove: " + removed);
        int removedCount;
        using (var doc = JsonDocument.Parse(removed))
            removedCount = doc.RootElement.GetProperty("removed").GetInt32();
        bool removedExactlyOne = removedCount == 1;
        session.Save();
        byte[] v1 = File.ReadAllBytes(Path.Combine(work, "war3map.doo"));
        var v1Doc = W3DooFile.ParseDoodadsDocument(v1, session.Map.DoodadsDoc.HasSkinIds);
        bool v1Ok = v1Doc.Placements.Count == before - 1;

        // Keep a copy of the deleted state for Godot/HiveWE inspection.
        string deletedCopy = Path.Combine(Path.GetDirectoryName(work)!, "deleted_v1");
        if (Directory.Exists(deletedCopy))
            Directory.Delete(deletedCopy, recursive: true);
        Directory.CreateDirectory(deletedCopy);
        foreach (string file in Directory.GetFiles(work))
            File.Copy(file, Path.Combine(deletedCopy, Path.GetFileName(file)));

        // Undo, save v2, byte-compare doo against the original.
        string undo = session.Undo();
        Console.Error.WriteLine("undo: " + undo);
        bool undoCountOk = session.Map.Doodads.Count == before;
        session.Save();
        byte[] v2 = File.ReadAllBytes(Path.Combine(work, "war3map.doo"));
        bool bytesOk = v2.AsSpan().SequenceEqual(originalDoo);
        // Units must be untouched through the whole chain (delete was doodad-scoped).
        byte[] unitsAfter = File.Exists(unitsPath) ? File.ReadAllBytes(unitsPath) : Array.Empty<byte>();
        bool unitsUntouched = unitsAfter.AsSpan().SequenceEqual(originalUnits);

        var result = new
        {
            tree = treeInfo,
            doodadsBefore = before,
            removedExactlyOne,
            savedAfterDeleteCount = v1Doc.Placements.Count,
            deleteSaveOk = v1Ok,
            undoCountOk,
            undoSaveByteEqualsOriginal = bytesOk,
            unitsDooUntouched = unitsUntouched,
            deletedMapFolder = deletedCopy,
            restoredMapFolder = work,
            pass = removedExactlyOne && v1Ok && undoCountOk && bytesOk && unitsUntouched,
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        string outJson = Path.Combine(EditorPaths.OutDir, "m1m2", "m2_accept.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outJson)!);
        File.WriteAllText(outJson, json);
        Console.WriteLine(json);
        return result.pass ? 0 : 1;
    }
}
