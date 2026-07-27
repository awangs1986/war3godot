using Godot;
using W3Compat.Pathing;
using Diablo1Like.W3.Formats;

namespace Diablo1Like.W3.Pathing;

/// <summary>WPM 寻路 + HeightSampler Y 贴合的 Godot 门面桥。</summary>
[GlobalClass]
public partial class W3WarsmashPathfinderBridge : RefCounted
{
    private GodotObject? _sampler;
    private W3PathingGridBridge? _pathingGrid;
    private W3PathfindingProcessorBridge? _processor;
    private W3GridPathfinderBridge? _tileFallback;
    public string source_label { get; private set; } = string.Empty;

    public W3WarsmashPathfinderBridge() { }

    public W3WarsmashPathfinderBridge(GodotObject? sampler, GodotObject? wpm = null)
    {
        Setup(sampler, wpm);
    }

    public void Setup(GodotObject? sampler, GodotObject? wpm = null)
    {
        _sampler = sampler;
        Vector2 center = Vector2.Zero;
        GodotObject? map = sampler?.Get("map").AsGodotObject();
        if (map != null)
        {
            center = new Vector2(
                map.Get("center_offset").AsVector2().X,
                map.Get("center_offset").AsVector2().Y);
        }
        if (wpm != null)
        {
            _pathingGrid = W3PathingGridBridge.FromWpm(wpm, center);
            source_label = "wpm";
        }
        else
        {
            _pathingGrid = W3PathingGridBridge.FromSampler(sampler, center);
            source_label = "synthesized";
        }
        _processor = new W3PathfindingProcessorBridge(_pathingGrid);
        _tileFallback = new W3GridPathfinderBridge(sampler);
    }

    public Godot.Collections.Array<Vector3> find_world_path(Vector3 from, Vector3 to, float collisionSize = 16f)
    {
        var outList = new Godot.Collections.Array<Vector3>();
        if (_processor == null || _pathingGrid == null)
            return FallbackWorld(from, to);
        Godot.Collections.Array<Vector2> pts = _processor.find_path(
            from.X, from.Z, to.X, to.Z, (int)W3PathingGridBridge.MovementType.Foot, collisionSize, true);
        if (pts.Count == 0)
            return FallbackWorld(from, to);
        foreach (Vector2 p in pts)
        {
            float y = _sampler?.Call("sample_y", p.X, p.Y).AsSingle() ?? 0f;
            outList.Add(new Vector3(p.X, y, p.Y));
        }
        return outList;
    }

    public Godot.Collections.Array<Vector2I> find_path(Vector2I start, Vector2I goal)
    {
        if (_sampler == null)
            return new Godot.Collections.Array<Vector2I>();
        Vector3 a = _sampler.Call("tile_center_world", start).AsVector3();
        Vector3 b = _sampler.Call("tile_center_world", goal).AsVector3();
        Godot.Collections.Array<Vector3> worldPath = find_world_path(a, b);
        var tiles = new Godot.Collections.Array<Vector2I>();
        Vector2I last = new(-999999, -999999);
        foreach (Vector3 p in worldPath)
        {
            Vector2I t = _sampler.Call("world_to_tile", p.X, p.Z).AsVector2I();
            if (t != last)
            {
                tiles.Add(t);
                last = t;
            }
        }
        if (tiles.Count == 0 && _tileFallback != null)
            return _tileFallback.find_path(start, goal);
        return tiles;
    }

    private Godot.Collections.Array<Vector3> FallbackWorld(Vector3 from, Vector3 to)
    {
        var outList = new Godot.Collections.Array<Vector3>();
        if (_sampler == null || _tileFallback == null)
            return outList;
        Vector2I st = _sampler.Call("world_to_tile", from.X, from.Z).AsVector2I();
        Vector2I gt = _sampler.Call("world_to_tile", to.X, to.Z).AsVector2I();
        Godot.Collections.Array<Vector2I> tilePath = _tileFallback.find_path(st, gt);
        foreach (Vector2I t in tilePath)
            outList.Add(_sampler.Call("tile_center_world", t).AsVector3());
        return outList;
    }

    public static W3WarsmashPathfinderBridge LoadForMap(GodotObject? sampler, GodotObject? archive)
    {
        GodotObject? wpm = null;
        if (archive != null)
        {
            byte[] bytes = archive.Call("read_file", "war3map.wpm").AsByteArray();
            if (bytes.Length == 0)
            {
                string mapDir = archive.Get("map_dir").AsString();
                if (!string.IsNullOrEmpty(mapDir))
                {
                    string p = mapDir.PathJoin("war3map.wpm");
                    if (Godot.FileAccess.FileExists(p))
                        wpm = W3PathingMapBridge.LoadFileStatic(p);
                }
            }
            else
            {
                wpm = W3PathingMapBridge.LoadBytesStatic(bytes);
            }
        }
        return new W3WarsmashPathfinderBridge(sampler, wpm);
    }

    public static W3WarsmashPathfinderBridge load_for_map(GodotObject? sampler, GodotObject? archive) =>
        LoadForMap(sampler, archive);
}
