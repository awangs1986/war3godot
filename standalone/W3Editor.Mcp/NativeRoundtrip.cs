using System.Text.Json;
using Diablo1Like.MapFormat;
using W3Editor.Formats;
using W3Editor.Rendering;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// Native format v1 acceptance: load a War3 folder map, convert to the native
/// model, save → load → save again, then byte-compare the two native outputs
/// (deterministic serialization) plus semantic spot checks.
/// Usage: --native-roundtrip [w3MapFolder] [outDir]
/// </summary>
internal static class NativeRoundtrip
{
    public static int Run(string[] args)
    {
        string root = GodotLauncher.FindProjectRoot()
            ?? throw new InvalidOperationException("project root (project.godot) not found");
        string src = args.Length >= 2 ? args[1] : Path.Combine(root, "war3", "maps", "LostTemple", "source");
        string outDir = args.Length >= 3 ? args[2] : Path.Combine(EditorPaths.OutDir, "native_roundtrip");

        var session = new MapSession();
        session.Open(src);
        var w3e = session.Map ?? throw new InvalidOperationException("map has no terrain");

        // War3 model → native model.
        var native = new NativeMap
        {
            Name = Path.GetFileName(Path.GetDirectoryName(src) ?? src),
            Terrain = TerrainMirror.Create(w3e),
        };
        foreach (W3Placement p in session.Doodads.Concat(session.Units))
        {
            native.Placements.Add(new NativePlacement
            {
                IsUnit = p.IsUnit,
                Id = p.Id,
                Variation = p.Variation,
                X = p.X, Y = p.Y, Z = p.Z,
                AngleRad = p.Angle,
                ScaleX = p.ScaleX, ScaleY = p.ScaleY, ScaleZ = p.ScaleZ,
                Owner = (byte)Math.Clamp(p.PlayerOwner, 0, 255),
                Flags = (byte)Math.Clamp(p.Flags, 0, 255),
                RefId = (uint)p.CreationNumber,
            });
        }

        string dir1 = Path.Combine(outDir, "v1");
        string dir2 = Path.Combine(outDir, "v2");
        foreach (string d in new[] { dir1, dir2 })
            if (Directory.Exists(d))
                Directory.Delete(d, recursive: true);

        NativeMapFormat.Save(native, dir1);
        NativeMap loaded = NativeMapFormat.Load(dir1);
        NativeMapFormat.Save(loaded, dir2);

        bool manifestEq = FilesEqual(dir1, dir2, NativeMapFormat.ManifestFile);
        bool terrainEq = FilesEqual(dir1, dir2, NativeMapFormat.TerrainFile);
        bool placementsEq = FilesEqual(dir1, dir2, NativeMapFormat.PlacementsFile);

        // Semantic spot checks: model survives the trip intact.
        var t0 = native.Terrain;
        var t1 = loaded.Terrain;
        bool cornersOk = t0.Width == t1.Width && t0.Height == t1.Height && t0.Corners.Length == t1.Corners.Length;
        double heightSum0 = 0, heightSum1 = 0;
        int flagBits0 = 0, flagBits1 = 0;
        if (cornersOk)
        {
            for (int i = 0; i < t0.Corners.Length; i++)
            {
                var a = t0.Corners[i];
                var b = t1.Corners[i];
                heightSum0 += a.GroundHeight;
                heightSum1 += b.GroundHeight;
                if (a.Water) flagBits0++;
                if (b.Water) flagBits1++;
                if (a.GroundHeight != b.GroundHeight || a.LayerHeight != b.LayerHeight
                    || a.GroundTexture != b.GroundTexture || a.GroundVariation != b.GroundVariation
                    || a.CliffTexture != b.CliffTexture || a.CliffVariation != b.CliffVariation
                    || a.Ramp != b.Ramp || a.Water != b.Water || a.Blight != b.Blight
                    || a.Boundary != b.Boundary || a.MapEdge != b.MapEdge)
                {
                    cornersOk = false;
                    break;
                }
            }
        }
        bool placementsOk = native.Placements.Count == loaded.Placements.Count;
        if (placementsOk)
        {
            for (int i = 0; i < native.Placements.Count; i++)
            {
                var a = native.Placements[i];
                var b = loaded.Placements[i];
                if (a.Id != b.Id || a.IsUnit != b.IsUnit || a.Variation != b.Variation
                    || a.X != b.X || a.Y != b.Y || a.Z != b.Z || a.AngleRad != b.AngleRad
                    || a.ScaleX != b.ScaleX || a.RefId != b.RefId)
                {
                    placementsOk = false;
                    break;
                }
            }
        }

        var result = new
        {
            source = src,
            nativeV1 = dir1,
            nativeV2 = dir2,
            corners = t0.Corners.Length,
            placements = native.Placements.Count,
            groundHeightSum = heightSum0,
            waterCorners = flagBits0,
            manifestByteEqual = manifestEq,
            terrainByteEqual = terrainEq,
            placementsByteEqual = placementsEq,
            cornersSemanticEqual = cornersOk && heightSum0 == heightSum1 && flagBits0 == flagBits1,
            placementsSemanticEqual = placementsOk,
            pass = manifestEq && terrainEq && placementsEq && cornersOk && placementsOk,
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "native_roundtrip.json"), json);
        Console.WriteLine(json);
        return result.pass ? 0 : 1;
    }

    private static bool FilesEqual(string dir1, string dir2, string name) =>
        File.ReadAllBytes(Path.Combine(dir1, name)).AsSpan()
            .SequenceEqual(File.ReadAllBytes(Path.Combine(dir2, name)));
}
