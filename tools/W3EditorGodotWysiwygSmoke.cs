using System;
using System.Collections.Generic;
using System.Linq;
using Diablo1Like.MapFormat;
using Diablo1Like.MapRuntime;
using Diablo1Like.W3.Terrain;
using Godot;

namespace Diablo1Like.Tests;

/// <summary>
/// Cross-stack acceptance for the editor-to-Godot native map boundary.
/// The editor writes d1map through NativeMapFormat; this test loads the same
/// folder through the production Godot adapter and proves that no terrain or
/// placement semantics change before rendering.
/// </summary>
public partial class W3EditorGodotWysiwygSmoke : Node
{
    public override void _Ready()
    {
        int failures = 0;
        string folder = ReadMapFolder();
        if (string.IsNullOrWhiteSpace(folder))
        {
            GD.PushError("W3_EDITOR_GODOT_WYSIWYG_FAILED missing --map=<d1map-folder>");
            GetTree().Quit(2);
            return;
        }

        try
        {
            string absolute = ProjectSettings.GlobalizePath(folder);
            NativeMap expected = NativeMapFormat.Load(absolute);
            Godot.Collections.Dictionary loaded = new NativeMapLoader().LoadFolder(absolute);
            failures += Expect(loaded.ContainsKey("map"), "Godot loader returns map");
            failures += Expect(loaded.ContainsKey("placements"), "Godot loader returns placements");

            var packed = loaded["map"].AsGodotObject() as W3PackedTerrainMap;
            failures += Expect(packed != null, "map boundary is W3PackedTerrainMap");
            if (packed == null)
            {
                GetTree().Quit(1);
                return;
            }

            failures += Expect(packed.IsValid(), "packed terrain arrays are valid");
            W3TerrainMap actual = W3TerrainMapBridge.FromGodotResource(packed);
            failures += CompareTerrain(expected.Terrain, actual);
            failures += CompareLayout(expected.Terrain, actual);
            failures += ComparePlacements(expected.Placements, loaded["placements"].AsGodotArray());
        }
        catch (Exception ex)
        {
            failures++;
            GD.PushError($"W3 editor/Godot WYSIWYG smoke threw: {ex}");
        }

        if (failures == 0)
            GD.Print("W3_EDITOR_GODOT_WYSIWYG_OK");
        else
            GD.PushError($"W3_EDITOR_GODOT_WYSIWYG_FAILED count={failures}");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private static string ReadMapFolder()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--map=", StringComparison.Ordinal))
                return arg[6..].Trim('"');
        return "";
    }

    private static int CompareTerrain(W3TerrainMap expected, W3TerrainMap actual)
    {
        int failures = 0;
        failures += Expect(expected.Version == actual.Version, "terrain version");
        failures += Expect(expected.Tileset == actual.Tileset, "terrain tileset");
        failures += Expect(expected.CustomTileset == actual.CustomTileset, "custom tileset flag");
        failures += Expect(expected.Width == actual.Width && expected.Height == actual.Height, "corner dimensions");
        failures += Expect(expected.CenterOffsetX == actual.CenterOffsetX && expected.CenterOffsetY == actual.CenterOffsetY, "center offset");
        failures += Expect(expected.GroundTilesets.SequenceEqual(actual.GroundTilesets), "ground tilesets");
        failures += Expect(expected.CliffTilesets.SequenceEqual(actual.CliffTilesets), "cliff tilesets");
        failures += Expect(expected.Corners.Length == actual.Corners.Length, "corner count");
        if (expected.Corners.Length != actual.Corners.Length)
            return failures;

        for (int i = 0; i < expected.Corners.Length; i++)
        {
            W3TerrainMap.Corner a = expected.Corners[i];
            W3TerrainMap.Corner b = actual.Corners[i];
            bool equal = a.GroundHeight == b.GroundHeight
                && a.WaterHeight == b.WaterHeight
                && a.MapEdge == b.MapEdge
                && a.Ramp == b.Ramp
                && a.Blight == b.Blight
                && a.Water == b.Water
                && a.Boundary == b.Boundary
                && a.GroundTexture == b.GroundTexture
                && a.GroundVariation == b.GroundVariation
                && a.CliffVariation == b.CliffVariation
                && a.CliffTexture == b.CliffTexture
                && a.LayerHeight == b.LayerHeight;
            if (!equal)
                return failures + Expect(false, $"corner payload at index {i}");
        }
        return failures;
    }

    private static int CompareLayout(W3TerrainMap expected, W3TerrainMap actual)
    {
        GroundLayout left = BuildLayout(expected);
        GroundLayout right = BuildLayout(actual);
        int failures = 0;
        failures += Expect(left.BuildHeightMap().AsSpan().SequenceEqual(right.BuildHeightMap()), "render height map");
        failures += Expect(left.BuildNormalHeightMap().AsSpan().SequenceEqual(right.BuildNormalHeightMap()), "normal height map");
        failures += Expect(left.BuildExistsMap().AsSpan().SequenceEqual(right.BuildExistsMap()), "ground-exists map");
        failures += Expect(left.BuildTileData().AsSpan().SequenceEqual(right.BuildTileData()), "HiveWE-sealed tile packing");
        return failures;
    }

    private static GroundLayout BuildLayout(W3TerrainMap map)
    {
        var rules = new WarsmashTerrainRules(map, modelExists: _ => false);
        rules.RebuildCliffs();
        int layerCount = Math.Max(1, map.GroundTilesets.Count + 1);
        var extended = new bool[layerCount];
        var cliffToGround = new int[Math.Max(1, map.CliffTilesets.Count)];
        return new GroundLayout(map, rules, extended, cliffToGround, layerCount);
    }

    private static int ComparePlacements(
        IReadOnlyList<NativePlacement> expected,
        Godot.Collections.Array actual)
    {
        int failures = Expect(expected.Count == actual.Count, "placement count");
        if (expected.Count != actual.Count)
            return failures;
        for (int i = 0; i < expected.Count; i++)
        {
            NativePlacement a = expected[i];
            var b = actual[i].AsGodotObject() as W3PlacementData;
            bool equal = b != null
                && a.Id == b.Id
                && a.IsUnit == b.IsUnit
                && a.Variation == b.Variation
                && new Vector3(a.X, a.Z, a.Y) == b.Position
                && a.AngleRad == b.Angle
                && new Vector3(a.ScaleX, a.ScaleZ, a.ScaleY) == b.Scale
                && a.Owner == b.PlayerOwner
                && a.Flags == b.Flags
                && unchecked((int)a.RefId) == b.CreationNumber;
            if (!equal)
                return failures + Expect(false, $"placement payload at index {i}");
        }
        return failures;
    }

    private static int Expect(bool condition, string label)
    {
        if (condition)
            return 0;
        GD.PushError($"WYSIWYG assertion failed: {label}");
        return 1;
    }
}
