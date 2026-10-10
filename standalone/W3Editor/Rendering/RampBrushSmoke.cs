using System.Text.Json;
using Diablo1Like.W3.Terrain;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Rendering;

/// <summary>Ramp authoring and render-state regressions; no proprietary assets required.</summary>
internal static class RampBrushSmoke
{
    public static int Run()
    {
        var checks = new Dictionary<string, bool>();
        foreach (string direction in new[] { "east", "west", "north", "south" })
        foreach (int size in new[] { 1, 4 })
        {
            var map = W3EMap.CreateBlank(64, 64);
            for (int y = 24; y <= 40; y++)
            for (int x = 24; x <= 40; x++) map.GetCorner(x, y)!.LayerHeight = 3;
            (float x, float y) point = direction switch
            {
                "east" => (40.25f, 32.25f), "west" => (23.75f, 32.25f),
                "north" => (32.25f, 40.25f), _ => (32.25f, 23.75f),
            };
            Stamp(map, point.x, point.y, size);
            int count = 0;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++) if (map.GetCorner(x, y)!.Ramp) count++;
            checks[$"{direction}_size{size}_preservesFractionalDirection"] = count == size * 3;
            byte[] once = map.SaveBytes();
            Stamp(map, point.x, point.y, size);
            checks[$"{direction}_size{size}_repeatStable"] = once.SequenceEqual(map.SaveBytes());
            checks[$"{direction}_size{size}_roundTrip"] = once.SequenceEqual(W3EMap.LoadBytes(once).SaveBytes());
        }

        // HiveWE's L-corner completion writes only Ramp, not CliffTexture.
        foreach (var c in new[] { (h: 1, v: 1, tx: 5, ty: 3), (h: 1, v: -1, tx: 5, ty: 5),
                                  (h: -1, v: 1, tx: 5, ty: 5), (h: 1, v: 1, tx: 3, ty: 5) })
        {
            var map = W3EMap.CreateBlank(32, 32);
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++) { map.GetCorner(x, y)!.LayerHeight = 2; map.GetCorner(x, y)!.CliffTexture = 1; }
            map.GetCorner(4, 4)!.LayerHeight = 3;
            map.GetCorner(4, 4)!.CliffTexture = 0;
            if (c.tx == 5 && c.ty == 3) { map.GetCorner(4, 3)!.Ramp = true; map.GetCorner(4, 2)!.Ramp = true; }
            else if (c.h == 1 && c.v == -1) { map.GetCorner(4, 5)!.Ramp = true; map.GetCorner(4, 6)!.Ramp = true; }
            else if (c.h == -1) { map.GetCorner(5, 4)!.Ramp = true; map.GetCorner(6, 4)!.Ramp = true; }
            else { map.GetCorner(3, 4)!.Ramp = true; map.GetCorner(2, 4)!.Ramp = true; }
            Stamp(map, 4 + c.h * 0.25f, 4 + c.v * 0.25f, 1);
            var target = map.GetCorner(c.tx, c.ty)!;
            checks[$"L_{c.h}_{c.v}_{c.tx}_{c.ty}_flagOnly"] = target.Ramp && target.CliffTexture == 1;
        }

        int mismatches = 0, suppressed = 0;
        bool authoredStable = true, repeatStable = true, heightsMatch = true, stepsMatch = true;
        int affectedHeights = 0, affectedSteps = 0;
        var random = new Random(20261005);
        for (int scenario = 0; scenario < 256; scenario++)
        {
            var source = new W3TerrainMap { Width = 6, Height = 6 };
            source.GroundTilesets.Add("Ldrt"); source.CliffTilesets.Add("CLdi");
            source.Corners = Enumerable.Range(0, 36).Select(_ => new W3TerrainMap.Corner
            {
                LayerHeight = (byte)(random.Next(10) < 8 ? 2 : 2 + random.Next(3)), Ramp = random.Next(2) == 1,
                GroundHeight = random.Next(5) * 0.125f,
            }).ToArray();
            bool[] expected = WarsmashEffectiveRamps(source);
            byte[] bytes = source.SaveBytes();
            var build = TerrainBuild.Create(source, _ => true, _ => CliffTypeInfo.Fallback);
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != build.Map.Corners[i].EffectiveRamp) mismatches++;
                if (source.Corners[i].Ramp && !expected[i]) suppressed++;
            }
            authoredStable &= bytes.SequenceEqual(source.SaveBytes()) && bytes.SequenceEqual(build.Map.SaveBytes());
            var again = TerrainBuild.Create(build.Map, _ => true, _ => CliffTypeInfo.Fallback);
            repeatStable &= again.Map.Corners.Select(c => c.EffectiveRamp).SequenceEqual(expected)
                && again.Cliffs.SequenceEqual(build.Cliffs);
            bool Entrance(int x, int y, bool[] flags)
            {
                if (x < 0 || y < 0 || x >= 5 || y >= 5) return false;
                int bl = y * 6 + x, br = bl + 1, tl = bl + 6, tr = tl + 1;
                return flags[bl] && flags[br] && flags[tl] && flags[tr]
                    && !(source.Corners[bl].LayerHeight == source.Corners[tr].LayerHeight
                        && source.Corners[tl].LayerHeight == source.Corners[br].LayerHeight);
            }
            float Height(int x, int y, bool[] flags)
            {
                var corner = source.GetCorner(x, y);
                for (int ty = y - 1; ty <= y; ty++)
                for (int tx = x - 1; tx <= x; tx++)
                {
                    if (!Entrance(tx, ty, flags)) continue;
                    int min = new[] { source.GetCorner(tx, ty).LayerHeight, source.GetCorner(tx + 1, ty).LayerHeight,
                        source.GetCorner(tx, ty + 1).LayerHeight, source.GetCorner(tx + 1, ty + 1).LayerHeight }.Min();
                    if (corner.LayerHeight == min) return corner.FinalGroundHeight + 0.5f;
                }
                return corner.FinalGroundHeight;
            }
            bool[] authored = source.Corners.Select(c => c.Ramp).ToArray();
            var sampler = new W3HeightSampler(build.Map);
            for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
            {
                float height = Height(x, y, expected);
                heightsMatch &= MathF.Abs(sampler.CornerWorldY(x, y) - height * W3TerrainMap.TileSize) < 0.0001f;
                if (height != Height(x, y, authored)) affectedHeights++;
                foreach (var delta in new[] { (x: 1, y: 0), (x: -1, y: 0), (x: 0, y: 1), (x: 0, y: -1) })
                {
                    int tx = x + delta.x, ty = y + delta.y;
                    if (!source.ContainsTile(tx, ty)) continue;
                    int from = y * 6 + x, to = ty * 6 + tx;
                    int dh = Math.Abs(source.Corners[from].LayerHeight - source.Corners[to].LayerHeight);
                    bool step = dh <= 1 || (dh <= 2 && (expected[from] || expected[to]));
                    stepsMatch &= sampler.CanStep(x, y, tx, ty) == step;
                    if (step != (dh <= 1 || (dh <= 2 && (authored[from] || authored[to])))) affectedSteps++;
                }
            }
            for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
            {
                int bl = y * 6 + x, br = bl + 1, tl = bl + 6, tr = tl + 1;
                bool entrance = expected[bl] && expected[br] && expected[tl] && expected[tr]
                    && !(source.Corners[bl].LayerHeight == source.Corners[tr].LayerHeight
                        && source.Corners[tl].LayerHeight == source.Corners[br].LayerHeight);
                if (build.Map.IsRampEntrance(x, y) != entrance) mismatches++;
            }
        }
        checks["invalidRampsMatchWarsmashOrderedRules"] = mismatches == 0 && suppressed > 0;
        checks["renderSuppressionDoesNotChangeSavedMap"] = authoredStable;
        checks["rebuildRecomputesSuppressionFromAuthoredFlags"] = repeatStable;
        checks["heightQueriesUseEffectiveRamps"] = heightsMatch && affectedHeights > 0;
        checks["stepQueriesUseEffectiveRamps"] = stepsMatch && affectedSteps > 0;
        bool pass = checks.Values.All(v => v);
        Console.WriteLine(JsonSerializer.Serialize(new { pass, mismatches, suppressed, affectedHeights, affectedSteps, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return pass ? 0 : 1;
    }

    private static void Stamp(W3EMap map, float x, float y, int size) => TerrainBrush.Apply(map, x, y,
        new BrushSettings { Op = BrushOp.Ramp, Radius = size, Shape = BrushShape.Square }, false, new BrushStroke());

    // Independent transcription of Terrain.java's DISALLOW_HEIGHT_3_RAMPS
    // state transitions. Model availability cannot change these transitions.
    private static bool[] WarsmashEffectiveRamps(W3TerrainMap map)
    {
        int w = map.Width, h = map.Height;
        int[] levels = map.Corners.Select(c => (int)c.LayerHeight).ToArray();
        bool[] flags = map.Corners.Select(c => c.Ramp).ToArray();
        bool Cliff(int x, int y) { int i = y * w + x; return levels[i] != levels[i + 1] || levels[i] != levels[i + w] || levels[i] != levels[i + w + 1]; }
        for (int x = 0; x < w - 1; x++)
        for (int y = 0; y < h - 1; y++)
        {
            if (!Cliff(x, y)) continue;
            int bl = y * w + x, br = bl + 1, tl = bl + w, tr = tl + 1;
            bool down = levels[tl] >= levels[bl] && levels[tr] >= levels[br];
            bool left = levels[br] >= levels[bl] && levels[tr] >= levels[tl];
            if ((down && y == 0) || (!down && y >= h - 2) || (left && x == 0) || (!left && x >= w - 2)) continue;
            bool vertical = flags[bl] != flags[br] && flags[tl] != flags[tr];
            bool horizontal = flags[bl] != flags[tl] && flags[br] != flags[tr];
            if (!vertical && !horizontal) continue;
            if ((vertical && Cliff(x, y + (down ? -1 : 1))) || (horizontal && Cliff(x + (left ? -1 : 1), y))) continue;
            int min = Math.Min(Math.Min(levels[bl], levels[br]), Math.Min(levels[tl], levels[tr]));
            if (levels[tl] - min > 1) flags[tl] = false;
            else if (levels[tr] - min > 1) flags[tr] = false;
            else if (levels[br] - min > 1) flags[br] = false;
            else if (levels[bl] - min > 1) flags[bl] = false;
        }
        return flags;
    }
}
