using System.Collections.Generic;
using Godot;
using W3Compat.Pathing;
using GVector2 = Godot.Vector2;
using NVector2 = System.Numerics.Vector2;

namespace Diablo1Like.W3.Pathing;

/// <summary>分帧预算寻路的 Godot 桥。</summary>
[GlobalClass]
public partial class W3PathfindingBridge : RefCounted
{
    private W3Pathfinding? _pathfinding;

    public W3PathfindingBridge() { }

    public W3PathfindingBridge(GodotObject? gridBridge) => BindGrid(gridBridge);

    public void BindGrid(GodotObject? gridBridge)
    {
        if (gridBridge is W3PathingGridBridge bridge && bridge.CoreGrid != null)
            _pathfinding = new W3Pathfinding(bridge.CoreGrid);
        else
            _pathfinding = null;
    }

    /// <summary>GDS 别名：GDScript 无法调用带参构造，改用 new() + bind_grid()。</summary>
    public void bind_grid(GodotObject? gridBridge) => BindGrid(gridBridge);

    public static bool IsCollisionSizeBetterSuitedForCorners(float collisionSize) =>
        W3Pathfinding.IsCollisionSizeBetterSuitedForCorners(collisionSize);

    public static bool is_collision_size_better_suited_for_corners(float collisionSize) =>
        IsCollisionSizeBetterSuitedForCorners(collisionSize);

    public bool is_active() => _pathfinding?.IsActive ?? false;
    public void cancel() => _pathfinding?.Cancel();

    public void start_job(GVector2 start, GVector2 goal, float collisionSize, bool allowSmoothing) =>
        _pathfinding?.StartJob(ToCore(start), ToCore(goal), collisionSize, allowSmoothing);

    /// <summary>进行中返回 default，完成返回路径点数组。</summary>
    public Variant update()
    {
        if (_pathfinding == null)
            return default;
        List<NVector2>? result = _pathfinding.Update();
        if (result == null)
            return default;
        return ToGodot(result);
    }

    public GVector2[] find_naive_slow_path(GVector2 start, GVector2 goal, float collisionSize, bool allowSmoothing)
    {
        if (_pathfinding == null)
            return System.Array.Empty<GVector2>();
        return ToGodot(_pathfinding.FindNaiveSlowPath(ToCore(start), ToCore(goal), collisionSize, allowSmoothing));
    }

    private static NVector2 ToCore(GVector2 v) => new(v.X, v.Y);

    private static GVector2[] ToGodot(List<NVector2> list)
    {
        var arr = new GVector2[list.Count];
        for (int i = 0; i < list.Count; i++)
            arr[i] = new GVector2(list[i].X, list[i].Y);
        return arr;
    }
}
