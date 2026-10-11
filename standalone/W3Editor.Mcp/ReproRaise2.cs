using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// Repro: blank 64x64 → CliffRaise2 stamp at center (same TerrainBrush path as
/// the editor UI) → save to an ASCII folder for --open/--shot3d and HiveWE.
/// Usage: --repro-raise2 [outFolder] [radius]
/// </summary>
internal static class ReproRaise2
{
    public static int Run(string[] args)
    {
        string outFolder = args.Length >= 2 ? args[1] : @"D:\temp\cursor\w3editor\blocky_repro\raise2_map";
        int radius = args.Length >= 3 && int.TryParse(args[2], out int r) ? r : 8;
        Directory.CreateDirectory(outFolder);

        var map = W3EMap.CreateBlank(64, 64);
        var brush = new BrushSettings
        {
            Op = BrushOp.CliffRaise2,
            Radius = radius,
            Shape = BrushShape.Circle,
        };
        var stroke = new BrushStroke();
        int cx = map.Width / 2, cy = map.Height / 2;
        TerrainBrush.Apply(map, cx, cy, brush, erase: false, stroke, frameDelta: 0.5f);

        File.WriteAllBytes(Path.Combine(outFolder, "war3map.w3e"), map.SaveBytes());
        File.WriteAllBytes(Path.Combine(outFolder, "war3map.doo"),
            W3DooDocument.CreateBlankDoodads(reforged: false).Write());
        File.WriteAllBytes(Path.Combine(outFolder, "war3mapUnits.doo"),
            W3DooDocument.CreateBlankUnits(reforged: false).Write());

        int cliffCorners = 0;
        for (int y = 0; y < map.Height - 1; y++)
        for (int x = 0; x < map.Width - 1; x++)
        {
            int bl = map.GetCorner(x, y)!.LayerHeight;
            if (bl != map.GetCorner(x + 1, y)!.LayerHeight
                || bl != map.GetCorner(x, y + 1)!.LayerHeight
                || bl != map.GetCorner(x + 1, y + 1)!.LayerHeight)
                cliffCorners++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            folder = outFolder,
            center = new { x = cx, y = cy },
            radius,
            cliffTiles = cliffCorners,
        }));
        return 0;
    }
}
