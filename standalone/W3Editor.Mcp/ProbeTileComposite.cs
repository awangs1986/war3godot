using System.Text.Json;
using Diablo1Like.W3.Terrain;
using SkiaSharp;
using W3Editor.Rendering;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// CPU re-composite of packed ground tiles using the exact HiveWE terrain.frag
/// math. Splits "packing data wrong" from "GL sampling wrong".
/// Usage: --probe-tile-composite [mapFolder] [tx0] [ty0] [tx1] [ty1] [outDir]
/// </summary>
internal static class ProbeTileComposite
{
    public static int Run(string[] args)
    {
        string mapPath = args.Length >= 2 ? args[1] : @"D:\temp\cursor\w3editor\blocky_repro\raise2_map";
        int tx0 = args.Length >= 3 ? int.Parse(args[2]) : 36;
        int ty0 = args.Length >= 4 ? int.Parse(args[3]) : 28;
        int tx1 = args.Length >= 5 ? int.Parse(args[4]) : 44;
        int ty1 = args.Length >= 6 ? int.Parse(args[5]) : 36;
        string outDir = args.Length >= 7 ? args[6] : @"D:\temp\cursor\w3editor\blocky_repro\tile_composite";
        Directory.CreateDirectory(outDir);

        var session = new MapSession();
        session.Open(mapPath);
        var mirror = TerrainMirror.Create(session.Map!);
        var assets = new EditorAssets();
        var cliffToGround = assets.BuildCliffToGround(mirror);
        var rules = new WarsmashTerrainRules(
            mirror,
            _ => true,
            t => assets.CliffTypeFor(mirror, t).CliffModelDir,
            t => assets.CliffTypeFor(mirror, t).RampModelDir,
            (tag, authored) => Math.Clamp(authored, 0, WarsmashTerrainRules.CliffVariationMaximum.GetValueOrDefault(tag, 0)));
        rules.RebuildCliffs();

        var layerIds = new List<string>(mirror.GroundTilesets) { "__BLIGHT__" };
        var extended = new List<bool>();
        var cellCache = new Dictionary<int, byte[]>();
        var atlases = new List<RgbaImage>();
        foreach (string id in layerIds)
        {
            RgbaImage atlas = id == "__BLIGHT__" ? assets.LoadBlightAtlas(mirror.Tileset) : assets.LoadGroundAtlas(id);
            atlases.Add(atlas);
            extended.Add(atlas.Width >= atlas.Height * 2);
        }
        var layout = new GroundLayout(mirror, rules, extended, cliffToGround, layerIds.Count);
        float[] pack = layout.BuildTileData();

        byte[] Cell(int packedLayer)
        {
            if (!cellCache.TryGetValue(packedLayer, out byte[]? cell))
            {
                int tex = packedLayer / GroundLayout.LayerStride;
                int sub = packedLayer % GroundLayout.LayerStride;
                cell = GroundLayout.SliceCell(atlases[tex].Pixels, atlases[tex].Width, atlases[tex].Height, sub, extended[tex]);
                cellCache[packedLayer] = cell;
            }
            return cell;
        }

        int size = GroundLayout.CellSize;
        int tilesX = tx1 - tx0 + 1, tilesY = ty1 - ty0 + 1;
        var stitched = new SKBitmap(tilesX * size, tilesY * size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var info = new List<object>();

        for (int ty = ty0; ty <= ty1; ty++)
        for (int tx = tx0; tx <= tx1; tx++)
        {
            int offset = (ty * mirror.TileWidth + tx) * 4;
            float[] layers = { pack[offset], pack[offset + 1], pack[offset + 2], pack[offset + 3] };
            bool exists = rules.GroundExists(tx, ty);
            info.Add(new
            {
                tx, ty, exists,
                r = layers[0], g = layers[1], b = layers[2], a = layers[3],
                corners = new[]
                {
                    layout.RealTileTexture(tx, ty), layout.RealTileTexture(tx + 1, ty),
                    layout.RealTileTexture(tx, ty + 1), layout.RealTileTexture(tx + 1, ty + 1),
                },
            });
            for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                // v_local: x east, y north; uv = (x, 1-y) → image row from top.
                float lx = (px + 0.5f) / size;
                float ly = 1f - (py + 0.5f) / size;
                float u = lx, v = 1f - ly;
                int sx = Math.Clamp((int)(u * size), 0, size - 1);
                int sy = Math.Clamp((int)(v * size), 0, size - 1);
                int si = (sy * size + sx) * 4;

                // HiveWE terrain.frag: top layer first, under-composite down to base.
                float cr = 0, cg = 0, cb = 0, ca = 0;
                for (int k = 3; k >= 0; k--)
                {
                    if (layers[k] > GroundLayout.EmptyLayer - 1f)
                        continue;
                    byte[] cell = Cell((int)layers[k]);
                    float lr = cell[si] / 255f, lg = cell[si + 1] / 255f, lb = cell[si + 2] / 255f, la = cell[si + 3] / 255f;
                    cr = lr + (cr - lr) * ca;
                    cg = lg + (cg - lg) * ca;
                    cb = lb + (cb - lb) * ca;
                    ca = la + (ca - la) * ca;
                }
                var color = exists
                    ? new SKColor((byte)(cr * 255), (byte)(cg * 255), (byte)(cb * 255), 255)
                    : new SKColor(24, 24, 32, 255);
                // stitched image: row 0 = northmost tile for natural map orientation
                int ox = (tx - tx0) * size + px;
                int oy = (ty1 - ty) * size + py;
                stitched.SetPixel(ox, oy, color);
            }
        }

        string png = Path.Combine(outDir, $"composite_{tx0}_{ty0}_{tx1}_{ty1}.png");
        using (var img = SKImage.FromBitmap(stitched))
        using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
        using (var fs = File.Create(png))
            data.SaveTo(fs);
        File.WriteAllText(Path.Combine(outDir, "tiles.json"),
            JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(png);
        return 0;
    }
}
