using System.Text.Json;
using Diablo1Like.W3.Terrain;
using W3Editor.Rendering;

namespace W3Editor.Mcp;

/// <summary>
/// Debug probe: does the atlas the editor actually loads carry the classic
/// blend-mask alpha in cells 0..15? Blocky transitions == opaque overlays.
/// Usage: --probe-atlas-alpha [tileId ...] (default Ldrt Lgrs)
/// </summary>
internal static class ProbeAtlasAlpha
{
    public static int Run(string[] args)
    {
        var tileIds = args.Length >= 2 ? args[1..] : new[] { "Ldrt", "Lgrs" };
        var assets = new EditorAssets();
        var report = new List<object>();
        foreach (string id in tileIds)
        {
            RgbaImage atlas = assets.LoadGroundAtlas(id);
            bool extended = atlas.Width >= atlas.Height * 2;
            var cells = new List<object>();
            for (int sub = 0; sub < 16; sub++)
            {
                byte[] cell = GroundLayout.SliceCell(atlas.Pixels, atlas.Width, atlas.Height, sub, extended);
                long sum = 0;
                int min = 255, max = 0, partial = 0;
                double wx = 0, wy = 0;
                int size = GroundLayout.CellSize;
                for (int i = 3; i < cell.Length; i += 4)
                {
                    int a = cell[i];
                    sum += a;
                    if (a < min) min = a;
                    if (a > max) max = a;
                    if (a > 8 && a < 248) partial++;
                    int px = (i / 4) % size, py = (i / 4) / size;
                    wx += a * (px + 0.5);
                    wy += a * (py + 0.5);
                }
                // Alpha centroid in cell-normalized image coords (0,0 = top-left).
                double cx = sum > 0 ? wx / sum / size : 0.5;
                double cy = sum > 0 ? wy / sum / size : 0.5;
                cells.Add(new
                {
                    sub, min, max,
                    avg = (int)(sum / (cell.Length / 4)),
                    partialPct = 100.0 * partial / (cell.Length / 4),
                    centroidX = Math.Round(cx, 3),
                    centroidY = Math.Round(cy, 3),
                });
            }
            string? resolved = null;
            if (assets.TerrainTiles.TryGetValue(id, out var info))
                resolved = ResolveActualFile(assets, info.ClassicRelPath);
            report.Add(new { tileId = id, file = resolved, atlas.Width, atlas.Height, extended, cells });
        }
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static string? ResolveActualFile(EditorAssets assets, string classicRel)
    {
        string stem = classicRel.Contains('.') ? classicRel[..classicRel.LastIndexOf('.')] : classicRel;
        foreach ((string? root, string rel) in new (string?, string)[]
        {
            (assets.GameRoot, stem + ".png"),
            (assets.GameRoot, stem + ".dds"),
            (assets.ReforgedRoot, stem + "_diffuse.dds"),
            (assets.ReforgedRoot, stem + ".dds"),
        })
        {
            if (root == null) continue;
            string path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
