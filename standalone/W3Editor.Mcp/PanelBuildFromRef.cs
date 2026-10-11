using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// Panel-equivalent Lost Temple build from offline ref JSON (same spirit as HiveWE panel flow):
///   1) map_new 160
///   2) set tileset list to LT ids
///   3) brush height → cliff/water → texture
///   4) place_object for units/doodads
/// Does NOT open original LostTemple and does NOT corner-copy / materialize war3map.*.
/// Usage: W3Editor.Mcp.exe --panel-build-lt-ref [refDir] [mapName]
/// </summary>
internal static class PanelBuildFromRef
{
    static readonly string[] LtGround =
        ["Ldrt", "Ldro", "Ldrg", "Ysqd", "Yrtl", "Lgrs", "Yhdg"];

    static readonly string[] LtCliff = ["CLdi", "CLgr", "CYsq"];

    public static int Run(string[] args)
    {
        string refDir = args.Length >= 2 ? args[1] : @"D:\temp\cursor\hivewe\ref_lt";
        string mapName = args.Length >= 3 ? args[2] : "LostTemple_W3Editor_Panel";
        string reportDir = Path.Combine(@"D:\temp\cursor\w3editor", "lt_panel_build");
        Directory.CreateDirectory(reportDir);

        var terrain = JsonDocument.Parse(File.ReadAllText(Path.Combine(refDir, "terrain.json"))).RootElement;
        var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(refDir, "meta.json"))).RootElement;
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var doodads = JsonSerializer.Deserialize<List<PlaceDto>>(File.ReadAllText(Path.Combine(refDir, "doodads.json")), jsonOpts) ?? [];
        var units = JsonSerializer.Deserialize<List<PlaceDto>>(File.ReadAllText(Path.Combine(refDir, "units.json")), jsonOpts) ?? [];

        int cw = terrain.GetProperty("corners").GetProperty("w").GetInt32();
        int ch = terrain.GetProperty("corners").GetProperty("h").GetInt32();
        int tiles = cw - 1;
        float ox = terrain.GetProperty("offset")[0].GetSingle();
        float oy = terrain.GetProperty("offset")[1].GetSingle();
        float[] gh = ReadFloat(terrain, "groundHeight", cw * ch);
        int[] layer = ReadInt(terrain, "layerHeight", cw * ch);
        int[] tex = ReadInt(terrain, "groundTexture", cw * ch);
        int[] water = ReadInt(terrain, "water", cw * ch);
        int[] ramp = ReadInt(terrain, "ramp", cw * ch);

        var session = new EditorSession();
        Console.Error.WriteLine(session.CreateNew(mapName, tiles, tiles, 'L', "Lgrs"));
        var map = session.Map.Map ?? throw new InvalidOperationException("no map");

        // Tile Setter equivalent: lock palette to LT fourccs + original offset for world placements.
        map.GroundTilesets = LtGround.ToList();
        map.CliffTilesets = LtCliff.ToList();
        map.CenterOffsetX = ox;
        map.CenterOffsetY = oy;
        map.Version = meta.TryGetProperty("version", out var ver) ? ver.GetInt32() : 12;
        session.Map.MarkDirty();

        int step = 2;
        int stamps = 0;
        var gaps = new List<string>();

        // --- 3a height first ---
        Console.Error.WriteLine("phase height...");
        for (int y = 0; y < ch; y += step)
        for (int x = 0; x < cw; x += step)
        {
            float g = gh[y * cw + x];
            if (MathF.Abs(g) < 0.08f) continue;
            // Single moderated stamp — Raise+CliffRaise previously overshot (built max ~20 vs ref ~7.5).
            string op = g > 0 ? "Raise" : "Lower";
            float strength = Math.Clamp(MathF.Abs(g) * 0.45f, 0.15f, 0.9f);
            session.BrushStamp(op, x, y, null, null, radius: 2, strength: strength, textureIndex: 0, erase: false);
            stamps++;
        }

        // --- 3b cliffs + water + ramp ---
        Console.Error.WriteLine("phase cliff/water...");
        for (int y = 0; y < ch; y += step)
        for (int x = 0; x < cw; x += step)
        {
            int L = layer[y * cw + x];
            int delta = L - 2;
            string cop = delta > 0 ? "CliffRaise1" : "CliffLower1";
            for (int k = 0; k < Math.Abs(delta); k++)
            {
                session.BrushStamp(cop, x, y, null, null, radius: 2, strength: 1f, textureIndex: 0, erase: false);
                stamps++;
            }
        }
        for (int y = 0; y < ch; y += step)
        for (int x = 0; x < cw; x += step)
        {
            if (water[y * cw + x] == 0) continue;
            // Prefer deep when water sits well above ground in ref final terms.
            float finalG = gh[y * cw + x] + layer[y * cw + x] - 2f;
            string wop = finalG < 0.5f ? "WaterDeep" : "WaterShallow";
            session.BrushStamp(wop, x, y, null, null, radius: 2, strength: 1f, textureIndex: 0, erase: false);
            stamps++;
        }
        int rampStamps = 0;
        for (int y = 0; y < ch; y += step)
        for (int x = 0; x < cw; x += step)
        {
            if (ramp[y * cw + x] == 0) continue;
            session.BrushStamp("Ramp", x, y, null, null, radius: 2, strength: 1f, textureIndex: 0, erase: false);
            stamps++;
            rampStamps++;
        }

        // --- 3c texture by LT index ---
        Console.Error.WriteLine("phase texture...");
        for (int y = 0; y < ch; y += step)
        for (int x = 0; x < cw; x += step)
        {
            int ti = Math.Clamp(tex[y * cw + x], 0, LtGround.Length - 1);
            session.BrushStamp("Texture", x, y, null, null, radius: 2, strength: 1f, textureIndex: ti, erase: false);
            stamps++;
        }

        // --- 4 placements (world coords from ref.w3) ---
        Console.Error.WriteLine($"phase place units={units.Count} doodads={doodads.Count}...");
        int placedU = 0, placedD = 0, placeFail = 0;
        string? firstPlaceErr = null;
        if (units.Count > 0 && string.IsNullOrWhiteSpace(units[0].Id))
            gaps.Add("BUG: units.json deserialized with empty Id (JSON casing) — fixed via PropertyNameCaseInsensitive.");
        foreach (var u in units)
        {
            try
            {
                float wx = u.W3?.X ?? (u.X * 128f + ox);
                float wy = u.W3?.Y ?? (u.Y * 128f + oy);
                float ang = u.Angle * 180f / MathF.PI;
                session.PlaceObject(u.Id, wx, wy, isUnit: true, u.Variation, ang, u.ScaleX, u.Player);
                placedU++;
            }
            catch (Exception ex)
            {
                placeFail++;
                firstPlaceErr ??= $"unit {u.Id}: {ex.Message}";
            }
        }
        foreach (var d in doodads)
        {
            try
            {
                float wx = d.W3?.X ?? (d.X * 128f + ox);
                float wy = d.W3?.Y ?? (d.Y * 128f + oy);
                float ang = d.Angle * 180f / MathF.PI;
                session.PlaceObject(d.Id, wx, wy, isUnit: false, d.Variation, ang, d.ScaleX, 0);
                placedD++;
            }
            catch (Exception ex)
            {
                placeFail++;
                firstPlaceErr ??= $"doodad {d.Id}: {ex.Message}";
            }
        }
        if (firstPlaceErr != null)
            gaps.Add("First placement error: " + firstPlaceErr);

        session.Save();
        string folder = session.Map.SourcePath;

        // Copy deliverable to D:\maps for side-by-side with HiveWE WIP
        string outMaps = @"D:\maps\LostTemple_W3Editor_Panel";
        try
        {
            if (Directory.Exists(outMaps))
                Directory.Delete(outMaps, recursive: true);
            CopyDir(folder, outMaps);
        }
        catch (Exception ex)
        {
            gaps.Add($"Could not mirror to {outMaps}: {ex.Message}");
            outMaps = folder;
        }

        // Height compare vs ref JSON
        int cmpExit = RunHeightCompare(refDir, Path.Combine(outMaps, "war3map.w3e"), Path.Combine(reportDir, "height_compare"));
        string cmpSummaryPath = Path.Combine(reportDir, "height_compare", "height_compare_summary.json");
        JsonElement? cmp = null;
        if (File.Exists(cmpSummaryPath))
            cmp = JsonDocument.Parse(File.ReadAllText(cmpSummaryPath)).RootElement.Clone();

        // Capability gaps (honest inventory for high-precision LT)
        gaps.AddRange(BuildGapList(session, stamps, rampStamps, placedU, placedD, placeFail, units.Count, doodads.Count, cmpExit, cmp));

        var report = new
        {
            builtAt = DateTime.UtcNow.ToString("o"),
            method = "panel-equivalent: CreateNew → set tilesets → BrushStamp height/cliff/water/ramp/texture → PlaceObject",
            reference = Path.GetFullPath(refDir),
            mapFolder = folder,
            mirroredTo = outMaps,
            stamps,
            rampStamps,
            placedUnits = placedU,
            placedDoodads = placedD,
            placeFailures = placeFail,
            expectedUnits = units.Count,
            expectedDoodads = doodads.Count,
            heightCompareExit = cmpExit,
            heightCompare = cmp,
            gaps,
            note = "Not RebuildLostTempleDraft / not materialize-ref. Brush sampling step=2 — intentional coarse panel rebuild.",
        };
        string reportJson = Path.Combine(reportDir, "panel_build_report.json");
        File.WriteAllText(reportJson, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.Error.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(reportJson);
        return 0;
    }

    static List<string> BuildGapList(
        EditorSession session, int stamps, int rampStamps,
        int placedU, int placedD, int placeFail, int expectU, int expectD,
        int cmpExit, JsonElement? cmp)
    {
        var g = new List<string>
        {
            "No HiveWE-style localhost IPC control server on the Avalonia UI process (MCP is headless EditorSession, not live UI brushes).",
            "BrushStamp strength clamped to 0..1 with no multi-frame deform continuum — tall groundHeight needs many Raise repeats (approximation).",
            "No per-stamp brush noUndo / undo-stack purge for mass automation (History records every stroke → memory/perf risk at full density).",
            "Texture brush uses palette index only — no tile_id string; must keep GroundTilesets order identical to ref or indices drift.",
            "Cliff brush is relative raise/lower/level only — no cliff_id / cliff texture paint matching CLdi/CLgr/CYsq per-corner cliffTexture.",
            "Water tools are Shallow/Deep presets — no direct WaterHeight / water_offset edit matching HiveWE cell water height.",
            "Ramp brush exists but fidelity of ramp flags vs LT (corner_romp / special cliff transitions) is unverified.",
            "No special doodads / war3map.doo special trailer pipeline in PlaceObject path.",
            "PlaceObject does not round-trip full unit fields (items, abilities, gold, mana, random, waygate, item tables).",
            "CreateNew does not write war3map.w3i / triggers / cameras / regions / sounds / pathing .wpm from LT sidecars.",
            "No pathing-map editor sync after mass brush (war3map.wpm may be missing or stale vs HiveWE blit pathing).",
            "No Tile Setter MCP tool yet (tileset list was set in-process on W3EMap — UI Tile Setter exists on MapSession but not exposed as MCP set_tileset).",
            "No autosave/checkpoint resume driver equivalent to HiveWE panel_zero_build.py (single-shot CLI).",
            "3D screenshot / minimap export not required for this run; visual parity tools exist but were not gated.",
            $"Placement coverage: units {placedU}/{expectU}, doodads {placedD}/{expectD}, failures={placeFail}.",
            $"Brush stamps executed: {stamps} (step=2 grid — not every corner).",
        };
        if (cmpExit != 0 && cmp != null)
            g.Add($"Height acceptance meanRel={cmp.Value.GetProperty("relativeError_meanAbsOverRefSpan")} (need ≤0.15).");
        else if (cmp != null)
            g.Add($"Height acceptance PASS meanRel={cmp.Value.GetProperty("relativeError_meanAbsOverRefSpan")}.");
        if (rampStamps == 0)
            g.Add("Zero ramp stamps — either ref has no ramp flags on step grid or Ramp op ineffective.");
        // Catalog gaps: sample failed ids already counted as placeFail
        _ = session;
        return g;
    }

    static int RunHeightCompare(string refDir, string builtW3e, string outDir)
    {
        string py = @"D:\temp\cursor\hivewe\compare_height.py";
        if (!File.Exists(py) || !File.Exists(builtW3e))
            return 2;
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "python",
            ArgumentList =
            {
                py,
                "--ref-json", Path.Combine(refDir, "terrain.json"),
                "--built-w3e", builtW3e,
                "--out", outDir,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(120_000);
        Console.Error.WriteLine(p.StandardOutput.ReadToEnd());
        Console.Error.WriteLine(p.StandardError.ReadToEnd());
        return p.ExitCode;
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
    }

    static float[] ReadFloat(JsonElement t, string key, int n)
    {
        var a = t.GetProperty(key).EnumerateArray().Select(e => e.GetSingle()).ToArray();
        if (a.Length != n) throw new InvalidDataException($"{key} len {a.Length} != {n}");
        return a;
    }

    static int[] ReadInt(JsonElement t, string key, int n)
    {
        var a = t.GetProperty(key).EnumerateArray().Select(e => e.GetInt32()).ToArray();
        if (a.Length != n) throw new InvalidDataException($"{key} len {a.Length} != {n}");
        return a;
    }

    sealed class PlaceDto
    {
        public string Id { get; set; } = "";
        public int Variation { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Angle { get; set; }
        public float ScaleX { get; set; } = 1;
        public float ScaleY { get; set; } = 1;
        public float ScaleZ { get; set; } = 1;
        public int Player { get; set; }
        public int Life { get; set; } = 100;
        public W3Dto? W3 { get; set; }
    }

    sealed class W3Dto
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }
}
