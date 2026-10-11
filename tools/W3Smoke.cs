using Diablo1Like.W3.Archives;
using Diablo1Like.W3.Formats;
using Diablo1Like.W3.Pathing;
using Diablo1Like.W3.Terrain;
using Godot;

namespace Diablo1Like.Tests;

/// <summary>
/// 0.1 验收冒烟：W3E + 高度采样 + A* + 地图归档打开。
/// 用法：godot --headless --path . res://tools/w3_smoke.tscn
/// </summary>
public partial class W3Smoke : Node
{
    public override void _Ready()
    {
        GD.Print("=== W3 0.1 smoke ===");
        string path = ProjectSettings.GlobalizePath("res://testdata/hivewe_sample/war3map.w3e");
        W3EMapBridge? map = W3EMapBridge.LoadFileStatic(path) ?? W3EMapBridge.MakeTestMap(12, 12);
        if (map == null)
        {
            Fail("W3E parse failed");
            return;
        }

        GD.Print($"W3E ok: corners={map.Width}x{map.Height} tiles={map.TileCountX()}x{map.TileCountY()} tileset={map.Tileset}");
        var samp = new W3HeightSamplerBridge();
        samp.BindMap(map);
        var mid = new Vector2I(map.TileCountX() / 2, map.TileCountY() / 2);
        Vector3 c = samp.TileCenterWorld(mid);
        GD.Print($"mid tile center={c}");
        if (!samp.IsTileWalkable(mid))
        {
            Fail("mid tile not walkable");
            return;
        }

        var pf = new W3GridPathfinderBridge();
        pf.BindSampler(samp);
        var pathTiles = pf.find_path(mid, mid + new Vector2I(3, 2));
        GD.Print($"path len={pathTiles.Count}");
        if (pathTiles.Count == 0)
        {
            Fail("pathfinding failed");
            return;
        }

        var arch = new W3MapArchiveBridge();
        string sample = ProjectSettings.GlobalizePath("res://testdata/hivewe_sample");
        bool ok = arch.Open(sample);
        GD.Print($"archive open={ok} dir={arch.MapDir} err={arch.ExtractError}");
        if (!ok)
        {
            Fail($"archive open failed: {arch.ExtractError}");
            return;
        }

        GD.Print("SMOKE_OK");
        GetTree().Quit(0);
    }

    private void Fail(string message)
    {
        GD.PushError(message);
        GetTree().Quit(1);
    }
}
