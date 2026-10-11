using System.Text.Json;
using W3Editor.Formats;

namespace W3Editor.Mcp;

/// <summary>
/// Build a new map folder purely from --export-lt-ref output (JSON + sidecars).
/// Never touches HiveWE. Does not open the original map path.
/// Usage: W3Editor.Mcp.exe --materialize-ref [refDir] [outDir]
/// </summary>
internal static class MaterializeFromRef
{
    public static int Run(string[] args)
    {
        string refDir = args.Length >= 2 ? args[1] : @"D:\temp\cursor\hivewe\ref_lt";
        string outDir = args.Length >= 3 ? args[2] : @"D:\maps\LostTemple_Built";
        Directory.CreateDirectory(outDir);

        using var terrainDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(refDir, "terrain.json")));
        var t = terrainDoc.RootElement;
        int cw = t.GetProperty("corners").GetProperty("w").GetInt32();
        int ch = t.GetProperty("corners").GetProperty("h").GetInt32();
        float ox = t.GetProperty("offset")[0].GetSingle();
        float oy = t.GetProperty("offset")[1].GetSingle();

        int version = 11;
        bool hasSkinIds = false;
        int doodadFileVersion = 8, doodadSubversion = 11;
        int unitFileVersion = 8, unitSubversion = 11;
        string metaPath = Path.Combine(refDir, "meta.json");
        if (File.Exists(metaPath))
        {
            using var metaDoc = JsonDocument.Parse(File.ReadAllText(metaPath));
            var root = metaDoc.RootElement;
            if (root.TryGetProperty("version", out var verEl))
                version = verEl.GetInt32();
            if (root.TryGetProperty("hasSkinIds", out var skinEl))
                hasSkinIds = skinEl.GetBoolean();
            if (root.TryGetProperty("doodadFileVersion", out var dfv))
                doodadFileVersion = dfv.GetInt32();
            if (root.TryGetProperty("doodadSubversion", out var dsv))
                doodadSubversion = dsv.GetInt32();
            if (root.TryGetProperty("unitFileVersion", out var ufv))
                unitFileVersion = ufv.GetInt32();
            if (root.TryGetProperty("unitSubversion", out var usv))
                unitSubversion = usv.GetInt32();
        }

        var map = new W3EMap
        {
            Version = version,
            Tileset = t.GetProperty("tileset").GetString()?[0] ?? 'L',
            CustomTileset = true,
            Width = cw,
            Height = ch,
            CenterOffsetX = ox,
            CenterOffsetY = oy,
            GroundTilesets = t.GetProperty("groundTilesets").EnumerateArray().Select(e => e.GetString()!).ToList(),
            CliffTilesets = t.GetProperty("cliffTilesets").EnumerateArray().Select(e => e.GetString()!).ToList(),
            Corners = new W3Corner[cw * ch],
        };

        float[] gh = ReadFloatArray(t, "groundHeight", cw * ch);
        float[] wh = ReadFloatArray(t, "waterHeight", cw * ch);
        int[] layer = ReadIntArray(t, "layerHeight", cw * ch);
        int[] tex = ReadIntArray(t, "groundTexture", cw * ch);
        int[] gvar = ReadIntArray(t, "groundVariation", cw * ch);
        int[] cvar = ReadIntArray(t, "cliffVariation", cw * ch);
        int[] ctex = ReadIntArray(t, "cliffTexture", cw * ch);
        int[] water = ReadIntArray(t, "water", cw * ch);
        int[] ramp = ReadIntArray(t, "ramp", cw * ch);
        int[] edge = ReadIntArray(t, "mapEdge", cw * ch);
        int[] blight = ReadIntArray(t, "blight", cw * ch);
        int[] boundary = ReadIntArray(t, "boundary", cw * ch);

        for (int i = 0; i < cw * ch; i++)
        {
            map.Corners[i] = new W3Corner
            {
                GroundHeight = gh[i],
                WaterHeight = wh[i],
                LayerHeight = layer[i],
                GroundTexture = tex[i],
                GroundVariation = gvar[i],
                CliffVariation = cvar[i],
                CliffTexture = ctex[i],
                Water = water[i] != 0,
                Ramp = ramp[i] != 0,
                MapEdge = edge[i] != 0,
                Blight = blight[i] != 0,
                Boundary = boundary[i] != 0,
            };
        }

        File.WriteAllBytes(Path.Combine(outDir, "war3map.w3e"), map.SaveBytes());

        var doodads = JsonSerializer.Deserialize<List<PlaceDto>>(File.ReadAllText(Path.Combine(refDir, "doodads.json")))
            ?? new();
        var units = JsonSerializer.Deserialize<List<PlaceDto>>(File.ReadAllText(Path.Combine(refDir, "units.json")))
            ?? new();

        var doodDoc = new W3DooDocument
        {
            IsUnits = false,
            FileVersion = doodadFileVersion,
            Subversion = doodadSubversion,
            HasSkinIds = hasSkinIds,
            SpecialTrailer = File.Exists(Path.Combine(refDir, "special_trailer.bin"))
                ? File.ReadAllBytes(Path.Combine(refDir, "special_trailer.bin"))
                : new byte[8],
        };
        int cn = 0;
        foreach (var p in doodads)
        {
            var pl = ToPlacement(p, ox, oy, isUnit: false);
            pl.CreationNumber = cn++;
            doodDoc.Placements.Add(pl);
        }
        File.WriteAllBytes(Path.Combine(outDir, "war3map.doo"), doodDoc.Write());

        var unitDoc = new W3DooDocument
        {
            IsUnits = true,
            FileVersion = unitFileVersion,
            Subversion = unitSubversion,
            HasSkinIds = hasSkinIds,
        };
        foreach (var p in units)
        {
            var pl = ToPlacement(p, ox, oy, isUnit: true);
            pl.CreationNumber = cn++;
            unitDoc.Placements.Add(pl);
        }
        File.WriteAllBytes(Path.Combine(outDir, "war3mapUnits.doo"), unitDoc.Write());

        // Sidecars from export (w3i, strings, etc.) — still not opening HiveWE on the original.
        string side = Path.Combine(refDir, "sidecars");
        if (Directory.Exists(side))
        {
            foreach (string f in Directory.EnumerateFiles(side))
            {
                string name = Path.GetFileName(f);
                if (name is "war3map.w3e" or "war3map.doo" or "war3mapUnits.doo")
                    continue;
                File.Copy(f, Path.Combine(outDir, name), overwrite: true);
            }
        }

        File.WriteAllText(Path.Combine(outDir, "BUILT_FROM_REF.txt"),
            $"Materialized from {refDir} at {DateTime.UtcNow:o}\n" +
            $"corners={cw}x{ch} doodads={doodads.Count} units={units.Count}\n" +
            "Source original map was NOT loaded into HiveWE.\n");

        Console.Error.WriteLine($"materialize ok → {outDir}");
        Console.Error.WriteLine($"doodads={doodads.Count} units={units.Count}");
        Console.WriteLine(outDir);
        return 0;
    }

    sealed class PlaceDto
    {
        public string id { get; set; } = "";
        public int variation { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float angle { get; set; }
        public float scaleX { get; set; } = 1;
        public float scaleY { get; set; } = 1;
        public float scaleZ { get; set; } = 1;
        public int player { get; set; }
        public int life { get; set; } = 100;
    }

    static W3Placement ToPlacement(PlaceDto p, float ox, float oy, bool isUnit) => new()
    {
        Id = p.id,
        Variation = p.variation,
        X = p.x * 128f + ox,
        Y = p.y * 128f + oy,
        Z = p.z * 128f,
        Angle = p.angle,
        ScaleX = p.scaleX,
        ScaleY = p.scaleY,
        ScaleZ = p.scaleZ,
        PlayerOwner = p.player,
        Life = p.life <= 0 ? 100 : p.life,
        IsUnit = isUnit,
        SkinId = p.id,
    };

    static float[] ReadFloatArray(JsonElement t, string name, int n)
    {
        var a = new float[n];
        int i = 0;
        foreach (var e in t.GetProperty(name).EnumerateArray())
            a[i++] = e.GetSingle();
        return a;
    }

    static int[] ReadIntArray(JsonElement t, string name, int n)
    {
        var a = new int[n];
        if (!t.TryGetProperty(name, out var prop))
            return a;
        int i = 0;
        foreach (var e in prop.EnumerateArray())
            a[i++] = e.GetInt32();
        return a;
    }

    static byte[] ReadByteArray(JsonElement t, string name, int n)
    {
        var a = new byte[n];
        if (!t.TryGetProperty(name, out var prop))
            return a;
        int i = 0;
        foreach (var e in prop.EnumerateArray())
            a[i++] = (byte)e.GetInt32();
        return a;
    }
}
