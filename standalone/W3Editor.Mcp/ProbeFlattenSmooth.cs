using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// CLI probe: does Plateau (整平) / Smooth (平滑) actually change corner data?
/// Scenarios:
///   A. Raise-mound (GroundHeight) + Plateau drag from flat edge  → expect flattened.
///   B. Raise-mound + Smooth (1 press stamp vs N drag stamps)     → expect tiny vs visible.
///   C. CliffRaise2 plateau (LayerHeight) + Plateau/Smooth        → expect NO change
///      (HiveWE/WE semantics: height tools touch GroundHeight only, not cliff layers).
/// </summary>
public static class ProbeFlattenSmooth
{
    public static Task<int> RunAsync(string[] args)
    {
        string outPath = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : @"D:\temp\cursor\w3editor\probe_flatten_smooth.json";
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var report = new Dictionary<string, object>
        {
            ["scenarioA_plateau_on_raised"] = ScenarioPlateau(),
            ["scenarioB_smooth_on_raised"] = ScenarioSmooth(),
            ["scenarioC_height_tools_on_cliff"] = ScenarioCliff(),
        };

        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outPath, json);
        Console.Error.WriteLine("wrote " + outPath);
        Console.WriteLine(json);
        return Task.FromResult(0);
    }

    private static W3EMap NewMap()
    {
        return W3EMap.CreateBlank(64, 64);
    }

    private static void Stroke(W3EMap map, BrushOp op, int x0, int y0, int x1, int y1,
        int radius, int repeats = 1)
    {
        var s = new BrushSettings { Op = op, Radius = radius };
        var stroke = new BrushStroke();
        int steps = Math.Max(1, Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))) * repeats;
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0 : i / (float)steps;
            int ix = x0 + (int)MathF.Round((x1 - x0) * t);
            int iy = y0 + (int)MathF.Round((y1 - y0) * t);
            TerrainBrush.Apply(map, ix, iy, s, erase: false, stroke,
                frameDelta: i == 0 ? 0.5f : 1f / 60f);
        }
    }

    private static float[] Row(W3EMap map, int y, int x0, int x1)
    {
        var row = new float[x1 - x0];
        for (int x = x0; x < x1; x++) row[x - x0] = map.GetCorner(x, y)!.GroundHeight;
        return row;
    }

    private static void RaiseMound(W3EMap map, int cx, int cy)
    {
        // ~1s of dragging in place: press 0.5 + 60 stamps of 1/60.
        var s = new BrushSettings { Op = BrushOp.Raise, Radius = 8 };
        var stroke = new BrushStroke();
        TerrainBrush.Apply(map, cx, cy, s, false, stroke, 0.5f);
        for (int i = 0; i < 60; i++)
            TerrainBrush.Apply(map, cx, cy, s, false, stroke, 1f / 60f);
    }

    private static object ScenarioPlateau()
    {
        var map = NewMap();
        RaiseMound(map, 32, 32);
        var before = Row(map, 32, 16, 48);

        // Plateau: press on flat ground at x=18, drag across the mound to x=46.
        Stroke(map, BrushOp.Plateau, 18, 32, 46, 32, radius: 8);
        var after = Row(map, 32, 16, 48);
        return new { before, after, changed = !before.SequenceEqual(after), maxAfter = after.Max() };
    }

    private static object ScenarioSmooth()
    {
        var map = NewMap();
        RaiseMound(map, 32, 32);
        float peakBefore = map.GetCorner(32, 32)!.GroundHeight;

        // Single press stamp only.
        var s = new BrushSettings { Op = BrushOp.Smooth, Radius = 8 };
        var stroke = new BrushStroke();
        TerrainBrush.Apply(map, 32, 32, s, false, stroke, 0.5f);
        float peakAfter1 = map.GetCorner(32, 32)!.GroundHeight;

        // 120 more stamps (≈2s of drag).
        for (int i = 0; i < 120; i++)
            TerrainBrush.Apply(map, 32, 32, s, false, stroke, 1f / 60f);
        float peakAfter120 = map.GetCorner(32, 32)!.GroundHeight;

        return new
        {
            peakBefore, peakAfter1, peakAfter120,
            deltaOneStamp = peakBefore - peakAfter1,
            deltaAfter120 = peakBefore - peakAfter120,
        };
    }

    private static object ScenarioCliff()
    {
        var map = NewMap();
        // CliffRaise2 plateau at center (user's usual test terrain).
        var s = new BrushSettings { Op = BrushOp.CliffRaise2, Radius = 8 };
        TerrainBrush.Apply(map, 32, 32, s, false, new BrushStroke(), 0.5f);

        int layerBefore = map.GetCorner(32, 32)!.LayerHeight;
        float groundBefore = map.GetCorner(32, 32)!.GroundHeight;

        Stroke(map, BrushOp.Plateau, 18, 32, 46, 32, radius: 8);
        for (int i = 0; i < 60; i++)
            TerrainBrush.Apply(map, 32, 32, new BrushSettings { Op = BrushOp.Smooth, Radius = 8 },
                false, new BrushStroke(), 1f / 60f);

        int layerAfter = map.GetCorner(32, 32)!.LayerHeight;
        float groundAfter = map.GetCorner(32, 32)!.GroundHeight;
        return new
        {
            layerBefore, layerAfter, groundBefore, groundAfter,
            layerChanged = layerBefore != layerAfter,
            note = "height tools must NOT touch LayerHeight (HiveWE semantics)",
        };
    }
}
