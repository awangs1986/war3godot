using Diablo1Like.MapRuntime;
using Diablo1Like.W3.Assets;
using Diablo1Like.W3.Formats;
using Diablo1Like.W3.IO;
using Diablo1Like.W3.Level;
using Diablo1Like.W3.Mdx;
using Diablo1Like.W3.Pathing;
using Diablo1Like.W3.World;
using Godot;

namespace Diablo1Like.Tests;

/// <summary>
/// 轻资产、确定性回归：解析器 / 寻路 / 导入。
/// 用法：godot --headless --path . res://tools/w3_regression.tscn
/// </summary>
public partial class W3Regression : Node
{
    private int _failures;

    /// <summary>回归用假采样器：仅实现 is_tile_walkable / can_step。</summary>
    private partial class OpenGridSampler : RefCounted
    {
        public bool is_tile_walkable(Vector2I tile) =>
            tile.X >= 0 && tile.Y >= 0 && tile.X < 16 && tile.Y < 16;

        public bool can_step(Vector2I fromTile, Vector2I toTile) =>
            is_tile_walkable(fromTile) && is_tile_walkable(toTile);
    }

    public override void _Ready()
    {
        var reader = new W3BinaryReaderBridge();
        reader.SetData(new byte[] { 1, 2 });
        Expect(reader.ReadU32() == 0 && reader.Failed, "binary reader rejects truncated scalar");
        Expect(!BlpDecoderBridge._valid_dimensions(100000, 100000), "BLP rejects oversized image allocation");
        Expect(W3MdxModel._bounded_count(1000, 4, 12, 2000000) == 0, "MDX rejects impossible element count");

        var strictWorld = new W3MapWorld();
        Expect(!strictWorld.allow_procedural_fallback, "runtime map loading is strict by default");
        strictWorld.Free();

        Expect(W3EMapBridge.LoadBytesStatic(System.Array.Empty<byte>()) == null, "W3E rejects empty input");
        W3EMapBridge valid = W3EMapBridge.MakeTestMap(8, 6);
        byte[] encoded = valid.SaveBytes();
        W3EMapBridge? decoded = W3EMapBridge.LoadBytesStatic(encoded);
        Expect(decoded != null, "W3E roundtrip parses");
        Expect(decoded != null && decoded.Width == 9 && decoded.Height == 7, "W3E dimensions roundtrip");

        byte[] truncated = encoded[..^1];
        Expect(W3EMapBridge.LoadBytesStatic(truncated) == null, "W3E rejects truncated corner grid");

        // HiveWE/Reforged v12：16-bit texture+flags（8 字节角点）
        W3EMapBridge v12 = W3EMapBridge.MakeTestMap(8, 6);
        v12.Version = 12;
        W3CornerData? probe = v12.GetCorner(3, 2);
        Expect(probe != null, "v12 probe corner exists");
        if (probe != null)
        {
            probe.GroundTexture = 33; // 需 6 bit，证明宽字段往返
            probe.Boundary = true;
        }
        W3EMapBridge? v12Decoded = W3EMapBridge.LoadBytesStatic(v12.SaveBytes());
        Expect(v12Decoded != null && v12Decoded.Version == 12, "W3E v12 roundtrip parses");
        W3CornerData? v12Probe = v12Decoded?.GetCorner(3, 2);
        Expect(
            v12Probe != null
                && v12Probe.GroundTexture == 33
                && v12Probe.Boundary
                && probe != null
                && v12Probe.LayerHeight == probe.LayerHeight,
            "W3E v12 wide texture+flags roundtrip");
        W3CornerData? v12Other = v12Decoded?.GetCorner(5, 4);
        W3CornerData? v11Other = decoded?.GetCorner(5, 4);
        Expect(
            v12Other != null
                && v11Other != null
                && Mathf.IsEqualApprox(v12Other.GroundHeight, v11Other.GroundHeight)
                && v12Other.Ramp == v11Other.Ramp,
            "W3E v12 and v11 agree on shared fields");

        var pf = new W3GridPathfinderBridge();
        pf.BindSampler(new OpenGridSampler());
        Expect(pf._h(Vector2I.Zero, new Vector2I(1, 1)) == 14, "A* octile heuristic matches diagonal cost");
        var route = pf.find_path(Vector2I.Zero, new Vector2I(3, 2));
        Expect(
            route.Count == 4 && route[0] == Vector2I.Zero && route[^1] == new Vector2I(3, 2),
            "A* returns optimal open-grid route");

        W3LevelDataResource level = W3LevelDataResource.FromW3e(decoded!);
        Expect(level.IsValid(), "W3LevelData accepts converted map");
        Expect(
            level.GetCorner(3, 2)!.GroundHeight == decoded!.GetCorner(3, 2)!.GroundHeight,
            "W3LevelData preserves corners");

        string sampleDir = ProjectSettings.GlobalizePath("res://testdata/hivewe_sample");
        const string output = "user://w3_pipeline_regression.res";
        int err = W3LevelImporterBridge.ImportToFile(sampleDir, output);
        Expect(err == (int)Error.Ok, "importer writes binary LevelData");
        var imported = ResourceLoader.Load(output, "", ResourceLoader.CacheMode.Ignore) as W3LevelDataResource;
        Expect(imported != null && imported.IsValid(), "imported LevelData reloads");
        Expect(imported != null && imported.source_w3e_sha256.Length == 64, "import stores source fingerprint");
        Expect(imported != null && imported.doodad_bytes.Length > 0, "import embeds doodad data");

        if (_failures > 0)
        {
            GD.PushError($"REGRESSION_FAILED: {_failures} failure(s)");
            GetTree().Quit(1);
            return;
        }

        GD.Print("REGRESSION_OK");
        GetTree().Quit(0);
    }

    private void Expect(bool condition, string label)
    {
        if (condition)
        {
            GD.Print("PASS: ", label);
            return;
        }

        _failures++;
        GD.PushError($"FAIL: {label}");
    }
}
