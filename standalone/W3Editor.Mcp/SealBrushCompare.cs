using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// Seal: same-stroke GroundHeight dump for HiveWE 1:1 verify.
/// Usage: W3Editor.Mcp.exe --seal-brush-dump [outDir]
/// Creates 64-tile blank, Raise @ (32,32) size=4 frameDelta=0.5 once, writes heights + w3e.
/// </summary>
internal static class SealBrushCompare
{
    public static int Dump(string[] args)
    {
        string outDir = args.Length >= 2
            ? args[1]
            : @"D:\temp\cursor\w3editor\seal_brush";
        Directory.CreateDirectory(outDir);

        const int tiles = 64;
        const int mx = 32, my = 32;
        const int uiSize = 4;
        const float frameDelta = 0.5f;

        var map = W3EMap.CreateBlank(tiles, tiles);
        var settings = new BrushSettings
        {
            Op = BrushOp.Raise,
            Radius = uiSize,
            Shape = BrushShape.Circle,
        };
        var stroke = new BrushStroke();
        TerrainBrush.Apply(map, mx, my, settings, erase: false, stroke, frameDelta);

        string w3ePath = Path.Combine(outDir, "war3map.w3e");
        File.WriteAllBytes(w3ePath, map.SaveBytes());
        // Compare after w3e quantize (HiveWE seal path also loads saved w3e)
        map = W3EMap.Load(w3ePath);

        var heights = new float[map.Width * map.Height];
        for (int i = 0; i < map.Corners.Length; i++)
            heights[i] = map.Corners[i].GroundHeight;

        var meta = new
        {
            tool = "W3Editor",
            tiles,
            corners = new { w = map.Width, h = map.Height },
            mouseCorner = new { x = mx, y = my },
            uiSize,
            frameDelta,
            shape = "circle",
            op = "raise",
            maxAbs = heights.Max(MathF.Abs),
            nonzero = heights.Count(h => MathF.Abs(h) > 1e-8f),
            sampleCenter = map.GetCorner(mx, my)!.GroundHeight,
            w3e = w3ePath,
        };
        File.WriteAllText(Path.Combine(outDir, "meta.json"),
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(outDir, "heights.json"),
            JsonSerializer.Serialize(new { width = map.Width, height = map.Height, groundHeight = heights }));

        Console.Error.WriteLine(JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(outDir);
        return 0;
    }

    /// <summary>
    /// Compare two heights.json dumps (W3Editor vs HiveWE-parsed).
    /// Usage: --seal-brush-diff [a.json] [b.json] [out.json]
    /// </summary>
    public static int Diff(string[] args)
    {
        string aPath = args.Length >= 2 ? args[1] : @"D:\temp\cursor\w3editor\seal_brush\heights.json";
        string bPath = args.Length >= 3 ? args[2] : @"D:\temp\cursor\hivewe\seal_brush\heights.json";
        string outPath = args.Length >= 4 ? args[3] : @"D:\temp\cursor\w3editor\seal_brush\diff.json";

        using var aDoc = JsonDocument.Parse(File.ReadAllText(aPath));
        using var bDoc = JsonDocument.Parse(File.ReadAllText(bPath));
        int w = aDoc.RootElement.GetProperty("width").GetInt32();
        int h = aDoc.RootElement.GetProperty("height").GetInt32();
        if (w != bDoc.RootElement.GetProperty("width").GetInt32()
            || h != bDoc.RootElement.GetProperty("height").GetInt32())
            throw new InvalidOperationException("dimension mismatch");

        var a = aDoc.RootElement.GetProperty("groundHeight").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        var b = bDoc.RootElement.GetProperty("groundHeight").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        if (a.Length != b.Length)
            throw new InvalidOperationException("length mismatch");

        double sumAbs = 0, maxAbs = 0;
        int mismatched = 0;
        const float eps = 1e-5f;
        int worstI = -1;
        for (int i = 0; i < a.Length; i++)
        {
            float d = MathF.Abs(a[i] - b[i]);
            sumAbs += d;
            if (d > maxAbs) { maxAbs = d; worstI = i; }
            if (d > eps) mismatched++;
        }

        var report = new
        {
            a = aPath,
            b = bPath,
            corners = a.Length,
            mismatched,
            meanAbs = sumAbs / a.Length,
            maxAbs,
            worst = worstI < 0 ? null : new { i = worstI, x = worstI % w, y = worstI / w, a = a[worstI], b = b[worstI] },
            sealedOk = mismatched == 0 || maxAbs <= 1e-4f,
            note = "eps=1e-5; sealedOk if maxAbs<=1e-4",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.Error.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(outPath);
        return report.sealedOk ? 0 : 2;
    }

    /// <summary>Parse a saved HiveWE/W3 war3map.w3e into heights.json.</summary>
    public static int DumpW3e(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: --seal-dump-w3e <war3map.w3e|mapFolder> [outDir]");
            return 2;
        }
        string src = args[1];
        string outDir = args.Length >= 3 ? args[2] : Path.Combine(Path.GetDirectoryName(src)!, "seal_heights");
        Directory.CreateDirectory(outDir);
        string w3e = Directory.Exists(src) ? Path.Combine(src, "war3map.w3e") : src;
        var map = W3EMap.Load(w3e);
        var heights = map.Corners.Select(c => c.GroundHeight).ToArray();
        File.WriteAllText(Path.Combine(outDir, "heights.json"),
            JsonSerializer.Serialize(new { width = map.Width, height = map.Height, groundHeight = heights }));
        var meta = new
        {
            tool = "w3e-load",
            source = w3e,
            corners = new { w = map.Width, h = map.Height },
            maxAbs = heights.Max(MathF.Abs),
            nonzero = heights.Count(h => MathF.Abs(h) > 1e-8f),
        };
        File.WriteAllText(Path.Combine(outDir, "meta.json"),
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        Console.Error.WriteLine(JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(outDir);
        return 0;
    }
}
