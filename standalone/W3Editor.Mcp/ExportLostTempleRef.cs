using System.Text.Json;
using SkiaSharp;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// Offline reference export from a map folder / .w3x — never loads HiveWE.
/// Usage: W3Editor.Mcp.exe --export-lt-ref [source] [outDir]
/// </summary>
internal static class ExportLostTempleRef
{
    public static int Run(string[] args)
    {
        string source = args.Length >= 2 ? args[1] : @"D:\maps\LostTemple";
        string outDir = args.Length >= 3 ? args[2] : @"D:\temp\cursor\hivewe\ref_lt";
        Directory.CreateDirectory(outDir);

        var session = new MapSession();
        session.Open(source);
        var map = session.Map ?? throw new InvalidOperationException("no w3e");
        float ox = map.CenterOffsetX;
        float oy = map.CenterOffsetY;
        int cw = map.Width;
        int ch = map.Height;

        float[] ground = new float[cw * ch];
        float[] water = new float[cw * ch];
        int[] layer = new int[cw * ch];
        int[] tex = new int[cw * ch];
        int[] groundVar = new int[cw * ch];
        int[] cliffVar = new int[cw * ch];
        int[] cliffTex = new int[cw * ch];
        // Use int[] for flags — System.Text.Json encodes byte[] as base64.
        int[] waterFlag = new int[cw * ch];
        int[] rampFlag = new int[cw * ch];
        int[] edgeFlag = new int[cw * ch];
        int[] blightFlag = new int[cw * ch];
        int[] boundaryFlag = new int[cw * ch];
        float minGh = float.MaxValue, maxGh = float.MinValue;

        for (int y = 0; y < ch; y++)
        for (int x = 0; x < cw; x++)
        {
            int i = y * cw + x;
            var c = map.Corners[i];
            ground[i] = c.GroundHeight;
            water[i] = c.WaterHeight;
            layer[i] = c.LayerHeight;
            tex[i] = c.GroundTexture;
            groundVar[i] = c.GroundVariation;
            cliffVar[i] = c.CliffVariation;
            cliffTex[i] = c.CliffTexture;
            waterFlag[i] = c.Water ? 1 : 0;
            rampFlag[i] = c.Ramp ? 1 : 0;
            edgeFlag[i] = c.MapEdge ? 1 : 0;
            blightFlag[i] = c.Blight ? 1 : 0;
            boundaryFlag[i] = c.Boundary ? 1 : 0;
            minGh = Math.Min(minGh, c.GroundHeight);
            maxGh = Math.Max(maxGh, c.GroundHeight);
        }

        // HiveWE tile space: (world - offset) / 128
        object TileOf(W3Placement p) => new
        {
            id = p.Id,
            variation = p.Variation,
            x = (p.X - ox) / 128f,
            y = (p.Y - oy) / 128f,
            z = p.Z / 128f,
            angle = p.Angle,
            scaleX = p.ScaleX,
            scaleY = p.ScaleY,
            scaleZ = p.ScaleZ,
            player = p.PlayerOwner,
            life = p.Life,
            w3 = new { p.X, p.Y, p.Z },
        };

        var doodads = session.Doodads.Select(TileOf).ToList();
        var units = session.Units.Select(TileOf).ToList();

        bool hasSkinIds = session.DoodadsDoc.HasSkinIds || session.UnitsDoc.HasSkinIds;
        File.WriteAllBytes(Path.Combine(outDir, "special_trailer.bin"), session.DoodadsDoc.SpecialTrailer ?? Array.Empty<byte>());

        var meta = new
        {
            source = Path.GetFullPath(source),
            exportedAt = DateTime.UtcNow.ToString("o"),
            tileset = map.Tileset.ToString(),
            version = map.Version,
            hasSkinIds,
            doodadFileVersion = session.DoodadsDoc.FileVersion,
            doodadSubversion = session.DoodadsDoc.Subversion,
            unitFileVersion = session.UnitsDoc.FileVersion,
            unitSubversion = session.UnitsDoc.Subversion,
            tiles = new { w = map.TileCountX, h = map.TileCountY },
            corners = new { w = cw, h = ch },
            offset = new[] { ox, oy },
            groundTilesets = map.GroundTilesets,
            cliffTilesets = map.CliffTilesets,
            groundHeightRange = new[] { minGh, maxGh },
            doodadCount = doodads.Count,
            unitCount = units.Count,
            note = "Positions x/y/z are HiveWE tile space: (w3 - offset) / 128. Do NOT open source in HiveWE; rebuild from this JSON.",
        };

        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(outDir, "meta.json"), JsonSerializer.Serialize(meta, opts));
        File.WriteAllText(Path.Combine(outDir, "doodads.json"), JsonSerializer.Serialize(doodads, opts));
        File.WriteAllText(Path.Combine(outDir, "units.json"), JsonSerializer.Serialize(units, opts));

        // Compact binary-ish JSON arrays for terrain (smaller than pretty).
        var terrain = new Dictionary<string, object>
        {
            ["corners"] = new { w = cw, h = ch },
            ["offset"] = new[] { ox, oy },
            ["groundHeight"] = ground,
            ["waterHeight"] = water,
            ["layerHeight"] = layer,
            ["groundTexture"] = tex,
            ["groundVariation"] = groundVar,
            ["cliffVariation"] = cliffVar,
            ["cliffTexture"] = cliffTex,
            ["water"] = waterFlag,
            ["ramp"] = rampFlag,
            ["mapEdge"] = edgeFlag,
            ["blight"] = blightFlag,
            ["boundary"] = boundaryFlag,
            ["groundTilesets"] = map.GroundTilesets,
            ["cliffTilesets"] = map.CliffTilesets,
            ["tileset"] = map.Tileset.ToString(),
        };
        File.WriteAllText(Path.Combine(outDir, "terrain.json"), JsonSerializer.Serialize(terrain));

        WriteHeightPng(Path.Combine(outDir, "heightmap.png"), ground, cw, ch, minGh, maxGh);
        WriteMinimapPng(Path.Combine(outDir, "minimap_ref.png"), tex, waterFlag.Select(v => (byte)v).ToArray(), cw, ch, map.GroundTilesets.Count);

        // Sidecar map files for materialize (not opened in HiveWE as the original path).
        string side = Path.Combine(outDir, "sidecars");
        Directory.CreateDirectory(side);
        string srcFolder = session.IsFolder ? session.SourcePath : Path.GetDirectoryName(source)!;
        if (Directory.Exists(srcFolder))
        {
            foreach (string f in Directory.EnumerateFiles(srcFolder))
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith("war3map", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("war3campaign", StringComparison.OrdinalIgnoreCase))
                {
                    if (name is "war3map.w3e" or "war3map.doo" or "war3mapUnits.doo")
                        continue; // rebuilt from JSON
                    File.Copy(f, Path.Combine(side, name), overwrite: true);
                }
            }
        }

        Console.Error.WriteLine($"export ok → {outDir}");
        Console.Error.WriteLine($"corners {cw}x{ch} doodads={doodads.Count} units={units.Count}");
        Console.WriteLine(outDir);
        return 0;
    }

    static void WriteHeightPng(string path, float[] gh, int w, int h, float min, float max)
    {
        float span = Math.Max(1e-4f, max - min);
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float t = (gh[y * w + x] - min) / span;
            byte v = (byte)Math.Clamp((int)(t * 255f), 0, 255);
            // PNG row 0 = north visually: flip Y for conventional top=north
            bmp.SetPixel(x, h - 1 - y, new SKColor(v, v, v));
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
    }

    static void WriteMinimapPng(string path, int[] tex, byte[] water, int w, int h, int texCount)
    {
        // Simple palette by texture index; water = blue.
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            SKColor c;
            if (water[i] != 0)
                c = new SKColor(40, 90, 180);
            else
            {
                int t = texCount <= 0 ? 0 : tex[i] % Math.Max(1, texCount);
                c = t switch
                {
                    0 => new SKColor(140, 120, 70),
                    1 => new SKColor(110, 95, 55),
                    2 => new SKColor(90, 80, 50),
                    3 => new SKColor(100, 100, 100),
                    4 => new SKColor(60, 130, 50),
                    5 => new SKColor(50, 110, 45),
                    _ => new SKColor(80, 80, 80),
                };
            }
            bmp.SetPixel(x, h - 1 - y, c);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
    }
}
