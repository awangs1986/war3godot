using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

internal static class TerrainInteractionAccept
{
    public static int Run(string[] args)
    {
        W3EMap map = W3EMap.CreateBlank(32, 32);
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            map.GetCorner(x, y)!.GroundHeight = y * 10 + x;

        // HiveWE clips the footprint first, then captures the integer center of
        // that clipped area. At mouse (0,0), size 4 => clipped 3x3 center (1,1).
        float expectedPlateau = map.GetCorner(1, 1)!.GroundHeight;
        var settings = new BrushSettings { Op = BrushOp.Plateau, Radius = 4, Shape = BrushShape.Square };
        var stroke = new BrushStroke();
        TerrainBrush.Apply(map, 0, 0, settings, false, stroke, 0.5f);
        bool plateauCenter = Math.Abs(stroke.PlateauGround - expectedPlateau) < 1e-6f;
        bool plateauApplied = true;
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
            plateauApplied &= Math.Abs(map.GetCorner(x, y)!.GroundHeight - expectedPlateau) < 1e-6f;

        float distance = 30f * EditorCameraMath.TileSize;
        System.Numerics.Vector3 right = EditorCameraMath.KeyPanDelta(MathF.PI, distance, 1f, 0f, 1f);
        System.Numerics.Vector3 forward = EditorCameraMath.KeyPanDelta(MathF.PI, distance, 0f, -1f, 1f);
        bool hiveWePanSpeed = Math.Abs(right.Length() - 40f * EditorCameraMath.TileSize) < 0.01f
            && Math.Abs(forward.Length() - 40f * EditorCameraMath.TileSize) < 0.01f;
        var result = new { plateauCenter, plateauApplied, hiveWePanSpeed, pass = plateauCenter && plateauApplied && hiveWePanSpeed };
        string output = args.Length >= 2 ? Path.GetFullPath(args[1]) : Path.Combine(EditorPaths.OutDir, "terrain-interaction-accept.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        return result.pass ? 0 : 1;
    }
}
