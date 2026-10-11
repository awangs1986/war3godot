using System.Text.Json;
using Diablo1Like.W3.Terrain;
using W3Editor.Formats;
using W3Editor.Rendering;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// Seal GroundLayout.RealTileTexture + tile packing against HiveWE terrain.ixx.
/// Usage:
///   --seal-ground-tex [mapFolder] [outDir]
/// </summary>
internal static class SealGroundTextures
{
    public static int Run(string[] args)
    {
        string mapPath = args.Length >= 2
            ? args[1]
            : "";
        string outDir = args.Length >= 3
            ? args[2]
            : @"D:\temp\cursor\w3editor\seal_ground_tex";
        Directory.CreateDirectory(outDir);

        var session = new MapSession();
        if (!string.IsNullOrEmpty(mapPath) && (Directory.Exists(mapPath) || File.Exists(mapPath)))
        {
            session.Open(mapPath);
        }
        else
        {
            // Deterministic cliff plateau: layer seam + mixed ground so HiveWE vs
            // Warsmash ownership diverges on the cliff perimeter.
            session.CreateNew("SealGroundTex", 64, 64);
            var map0 = session.Map!;
            // Plateau layer=4 with explicit cliff tex 0. Surround stays cliff tex 15
            // so HiveWE (own tex → 15→1) and Warsmash (neighbor tex → 0) diverge
            // on the +x/+y skirt outside cliff BLs.
            for (int y = 28; y <= 36; y++)
            for (int x = 28; x <= 36; x++)
            {
                var c = map0.GetCorner(x, y)!;
                c.LayerHeight = 4;
                c.CliffTexture = 0;
            }
            for (int y = 30; y <= 34; y++)
            for (int x = 30; x <= 34; x++)
                map0.GetCorner(x, y)!.GroundTexture =
                    (byte)Math.Min(1, Math.Max(0, map0.GroundTilesets.Count - 1));
            for (int y = 26; y <= 38; y++)
            for (int x = 26; x <= 38; x++)
            {
                if (x >= 28 && x <= 36 && y >= 28 && y <= 36) continue;
                var c = map0.GetCorner(x, y)!;
                c.GroundTexture = 0;
                c.CliffTexture = 15;
            }

            // Partial ramp fixture: left authored corners are ramp-marked and
            // low, right corners are one layer higher. RebuildCliffs resolves
            // a ramp and stamps a texture onto live corners; HiveWE ownership
            // must still read the authored pre-rebuild values.
            int rx = 44, ry = 30;
            // HiveWE update_ramp writes a 3-corner line: high origin followed
            // by two low corners, with Ramp true on all three.
            for (int y = ry - 2; y <= ry + 2; y++)
            for (int x = rx - 2; x <= rx + 4; x++)
            {
                var c = map0.GetCorner(x, y)!;
                c.LayerHeight = 2;
                c.Ramp = false;
                c.CliffTexture = 0;
            }
            map0.GetCorner(rx, ry)!.LayerHeight = 3;
            for (int x = rx; x <= rx + 2; x++)
            {
                var c = map0.GetCorner(x, ry)!;
                c.Ramp = true;
                c.CliffTexture = x == rx ? 1 : 0;
            }
        }

        var src = session.Map!;
        var mirror = TerrainMirror.Create(src);
        byte[] authoredCliffTextures = mirror.Corners.Select(c => c.CliffTexture).ToArray();
        var assets = new EditorAssets();
        if (!assets.HasGameData)
        {
            Console.Error.WriteLine("{\"error\":\"EditorAssets missing game data (war3/game Terrain.slk)\"}");
            return 3;
        }

        var cliffToGround = assets.BuildCliffToGround(mirror);
        var rules = new WarsmashTerrainRules(
            mirror,
            _ => true,
            texture => assets.CliffTypeFor(mirror, texture).CliffModelDir,
            texture => assets.CliffTypeFor(mirror, texture).RampModelDir,
            (tag, authored) => Math.Clamp(authored, 0, WarsmashTerrainRules.CliffVariationMaximum.GetValueOrDefault(tag, 0)));
        IReadOnlyList<WarsmashTerrainRules.CliffPlacement> cliffPlacements = rules.RebuildCliffs();

        int layerCount = mirror.GroundTilesets.Count + 1;
        var extended = new bool[layerCount];
        var layout = new GroundLayout(mirror, rules, extended, cliffToGround, layerCount);

        int cw = mirror.Width, ch = mirror.Height;
        var ours = new int[cw * ch];
        var golden = new int[cw * ch];
        var warsmash = new int[cw * ch];
        int mismatched = 0;
        int vsWarsmash = 0;
        int worst = -1;
        for (int y = 0; y < ch; y++)
        for (int x = 0; x < cw; x++)
        {
            int i = y * cw + x;
            ours[i] = layout.RealTileTexture(x, y);
            golden[i] = HiveWeRealTileTexture(mirror, cliffToGround, layerCount, x, y, authoredCliffTextures);
            warsmash[i] = WarsmashRealTileTexture(mirror, cliffToGround, layerCount, x, y);
            if (ours[i] != golden[i])
            {
                mismatched++;
                if (worst < 0) worst = i;
            }
            if (ours[i] != warsmash[i])
                vsWarsmash++;
        }

        // Full 4-layer packing vs HiveWE-golden corner textures (same sort+bitset).
        float[] tileData = layout.BuildTileData();
        var goldenPack = new float[mirror.TileWidth * mirror.TileHeight * 4];
        int tileMismatch = 0;
        int tilesCompared = 0;
        Span<float> packed = stackalloc float[4];
        for (int ty = 0; ty < mirror.TileHeight; ty++)
        for (int tx = 0; tx < mirror.TileWidth; tx++)
        {
            int offset = (ty * mirror.TileWidth + tx) * 4;
            if (!rules.GroundExists(tx, ty))
            {
                goldenPack[offset] = goldenPack[offset + 1] = goldenPack[offset + 2] = goldenPack[offset + 3] = GroundLayout.EmptyLayer;
                continue;
            }
            tilesCompared++;
            int bl = HiveWeRealTileTexture(mirror, cliffToGround, layerCount, tx, ty, authoredCliffTextures);
            int br = HiveWeRealTileTexture(mirror, cliffToGround, layerCount, tx + 1, ty, authoredCliffTextures);
            int tl = HiveWeRealTileTexture(mirror, cliffToGround, layerCount, tx, ty + 1, authoredCliffTextures);
            int tr = HiveWeRealTileTexture(mirror, cliffToGround, layerCount, tx + 1, ty + 1, authoredCliffTextures);
            var unique = new SortedSet<int> { bl, br, tl, tr };
            packed.Fill(GroundLayout.EmptyLayer);
            int component = 0;
            foreach (int texture in unique)
            {
                if (component == 0)
                    packed[component++] = texture * GroundLayout.LayerStride
                        + layout.GetVariation(texture, mirror.GetCorner(tx, ty).GroundVariation);
                else
                {
                    int bits = (br == texture ? 1 : 0) | (bl == texture ? 2 : 0) |
                        (tr == texture ? 4 : 0) | (tl == texture ? 8 : 0);
                    packed[component++] = texture * GroundLayout.LayerStride + bits;
                }
            }
            goldenPack[offset] = packed[0];
            goldenPack[offset + 1] = packed[1];
            goldenPack[offset + 2] = packed[2];
            goldenPack[offset + 3] = packed[3];
            for (int k = 0; k < 4; k++)
            {
                if (MathF.Abs(tileData[offset + k] - goldenPack[offset + k]) > 0.01f)
                {
                    tileMismatch++;
                    break;
                }
            }
        }

        int cliffCorners = 0;
        for (int y = 0; y < mirror.TileHeight; y++)
        for (int x = 0; x < mirror.TileWidth; x++)
            if (mirror.GetCorner(x, y).Cliff) cliffCorners++;
        int rampPlacements = cliffPlacements.Count(item => item.IsRamp);

        var report = new
        {
            map = session.SourcePath ?? "(blank SealGroundTex)",
            corners = cw * ch,
            cliffTiles = cliffCorners,
            rampPlacements,
            tilesCompared,
            realTileMismatched = mismatched,
            realTileWorst = worst < 0 ? null : new { i = worst, x = worst % cw, y = worst / cw, ours = ours[worst], golden = golden[worst] },
            packTileMismatched = tileMismatch,
            differsFromWarsmashCorners = vsWarsmash,
            sealedOk = mismatched == 0 && tileMismatch == 0 && rampPlacements > 0,
            authority = "HiveWE terrain.ixx real_tile_texture + update_ground_textures (user 2026-07-22)",
            note = "Golden is an independent line-faithful transcription of HiveWE; Warsmash delta proves cliff ownership change is active.",
        };
        File.WriteAllText(Path.Combine(outDir, "seal_ground_tex.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(outDir, "real_tile_ours.json"),
            JsonSerializer.Serialize(new { width = cw, height = ch, realTile = ours }));
        File.WriteAllText(Path.Combine(outDir, "real_tile_golden.json"),
            JsonSerializer.Serialize(new { width = cw, height = ch, realTile = golden }));
        File.WriteAllText(Path.Combine(outDir, "real_tile_warsmash.json"),
            JsonSerializer.Serialize(new { width = cw, height = ch, realTile = warsmash }));
        File.WriteAllText(Path.Combine(outDir, "pack_ours.json"),
            JsonSerializer.Serialize(new { width = mirror.TileWidth, height = mirror.TileHeight, pack = tileData }));
        File.WriteAllText(Path.Combine(outDir, "pack_golden.json"),
            JsonSerializer.Serialize(new { width = mirror.TileWidth, height = mirror.TileHeight, pack = goldenPack }));

        Console.Error.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(Path.Combine(outDir, "seal_ground_tex.json"));
        return report.sealedOk ? 0 : 2;
    }

    /// <summary>Line-faithful HiveWE Terrain::real_tile_texture (terrain.ixx).</summary>
    public static int HiveWeRealTileTexture(
        W3TerrainMap map,
        IReadOnlyList<int> cliffToGround,
        int layerCount,
        int x,
        int y,
        IReadOnlyList<byte>? authoredCliffTextures = null)
    {
        bool aRomp = false, aCliff = false;
        void Acc(int tx, int ty)
        {
            if (!map.ContainsTile(tx, ty)) return;
            var t = map.GetCorner(tx, ty);
            aRomp |= t.Romp;
            aCliff |= t.Cliff;
        }
        Acc(x, y);
        if (x > 0) Acc(x - 1, y);
        if (y > 0) Acc(x, y - 1);
        if (x > 0 && y > 0) Acc(x - 1, y - 1);

        var own = map.GetCorner(x, y);
        if (aRomp || (aCliff && !own.Ramp))
        {
            int index = y * map.Width + x;
            int texture = authoredCliffTextures != null && index >= 0 && index < authoredCliffTextures.Count
                ? authoredCliffTextures[index]
                : own.CliffTexture;
            if (texture == 15) texture -= 14;
            return texture >= 0 && texture < cliffToGround.Count ? cliffToGround[texture] : 0;
        }
        if (own.Blight) return layerCount - 1;
        return own.GroundTexture;
    }

    /// <summary>Prior Warsmash-style RealTileTexture (GroundLayout.bak) for delta evidence.</summary>
    public static int WarsmashRealTileTexture(
        W3TerrainMap map, IReadOnlyList<int> cliffToGround, int layerCount, int x, int y)
    {
        for (int xOffset = -1; xOffset <= 0; xOffset++)
        {
            for (int yOffset = -1; yOffset <= 0; yOffset++)
            {
                int tx = x + xOffset;
                int ty = y + yOffset;
                if (!map.ContainsCorner(tx, ty))
                    continue;
                W3TerrainMap.Corner corner = map.GetCorner(tx, ty);
                if (corner.Cliff && map.ContainsTile(tx, ty))
                {
                    W3TerrainMap.Corner bl = corner;
                    W3TerrainMap.Corner br = map.GetCorner(tx + 1, ty);
                    W3TerrainMap.Corner tl = map.GetCorner(tx, ty + 1);
                    W3TerrainMap.Corner tr = map.GetCorner(tx + 1, ty + 1);
                    if (bl.Ramp && br.Ramp && tl.Ramp && tr.Ramp && !bl.Romp && !br.Romp && !tl.Romp && !tr.Romp)
                        goto afterCliff;
                }
                if (corner.Romp || corner.Cliff)
                {
                    int cliffTexture = corner.CliffTexture == 15 ? 1 : corner.CliffTexture;
                    return cliffTexture >= 0 && cliffTexture < cliffToGround.Count ? cliffToGround[cliffTexture] : 0;
                }
            }
        }
        afterCliff:
        W3TerrainMap.Corner own = map.GetCorner(x, y);
        if (own.Blight) return layerCount - 1;
        return own.GroundTexture;
    }
}
