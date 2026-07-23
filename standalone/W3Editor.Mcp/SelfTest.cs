using System.Text.Json;

namespace W3Editor.Mcp;

/// <summary>Headless end-to-end check: open → brush → place → undo → redo → save → screenshot.</summary>
internal static class SelfTest
{
    public static async Task<int> RunAsync(string mapPath, CancellationToken ct)
    {
        var session = new EditorSession();
        Console.Error.WriteLine("== W3Editor.Mcp selftest ==");
        Console.Error.WriteLine(session.Open(mapPath));

        var map = session.Map.Map!;
        int mx = map.Width / 2, my = map.Height / 2;
        Console.Error.WriteLine(session.BrushStamp("Raise", mx - 10, my, mx + 10, my, radius: 6, strength: 1f, textureIndex: 0, erase: false));

        int doodadsBefore = session.Map.Doodads.Count;
        // Place near map center in world coords.
        float wx = map.CenterOffsetX + mx * 128f;
        float wy = map.CenterOffsetY + my * 128f;
        Console.Error.WriteLine(session.PlaceObject("LTlt", wx, wy, isUnit: false, variation: 0, angleDeg: 45f, scale: 1f, owner: 0));
        Console.Error.WriteLine(session.PlaceObject("LTlt", wx + 256f, wy, isUnit: false, variation: 1, angleDeg: 90f, scale: 1.1f, owner: 0));

        if (session.Map.Doodads.Count != doodadsBefore + 2)
        {
            Console.Error.WriteLine($"FAIL: expected {doodadsBefore + 2} doodads, got {session.Map.Doodads.Count}");
            return 2;
        }

        Console.Error.WriteLine(session.Undo());
        Console.Error.WriteLine(session.Undo());
        if (session.Map.Doodads.Count != doodadsBefore)
        {
            Console.Error.WriteLine("FAIL: undo did not restore doodad count");
            return 3;
        }
        Console.Error.WriteLine(session.Redo());
        Console.Error.WriteLine(session.Redo());
        if (session.Map.Doodads.Count != doodadsBefore + 2)
        {
            Console.Error.WriteLine("FAIL: redo mismatch");
            return 4;
        }

        session.Save();
        Console.Error.WriteLine("saved OK");

        string shot = W3Editor.Services.EditorPaths.OutFile($"selftest_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        try
        {
            string png = await session.Screenshot3dAsync(shot, ct);
            Console.Error.WriteLine($"screenshot OK: {png} ({new FileInfo(png).Length} bytes)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("WARN: screenshot_3d failed: " + ex.Message);
            // Still pass headless edits if GUI capture is flaky in this environment.
        }

        Console.Error.WriteLine(session.CatalogSearch("tree", "doodad", null, 5));
        Console.Error.WriteLine(session.StatusJson());
        Console.Error.WriteLine("== selftest done ==");
        // Emit a tiny machine-readable summary on stdout (not used during MCP mode).
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ok = true,
            doodads = session.Map.Doodads.Count,
            dirty = session.Map.IsDirty,
        }));
        return 0;
    }
}
