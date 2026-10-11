using Godot;
using W3Compat.Pathing;
using GVector2 = Godot.Vector2;
using NVector2 = System.Numerics.Vector2;

namespace Diablo1Like.W3.Pathing;

/// <summary>同步 Warsmash A* 的 Godot 桥。</summary>
[GlobalClass]
public partial class W3PathfindingProcessorBridge : RefCounted
{
    private W3PathfindingProcessor? _processor;

    public W3PathfindingProcessorBridge() { }

    public W3PathfindingProcessorBridge(GodotObject? gridBridge) => BindGrid(gridBridge);

    public void BindGrid(GodotObject? gridBridge)
    {
        if (gridBridge is W3PathingGridBridge bridge && bridge.CoreGrid != null)
            _processor = new W3PathfindingProcessor(bridge.CoreGrid);
        else
            _processor = null;
    }

    public static bool IsCollisionSizeBetterSuitedForCorners(float collisionSize) =>
        W3PathfindingProcessor.IsCollisionSizeBetterSuitedForCorners(collisionSize);

    public static bool is_collision_size_better_suited_for_corners(float collisionSize) =>
        IsCollisionSizeBetterSuitedForCorners(collisionSize);

    public bool pathable_between(float startX, float startZ, float endX, float endZ, int movement, float collisionSize) =>
        _processor?.PathableBetween(startX, startZ, endX, endZ, (W3MovementType)movement, collisionSize) ?? false;

    public Godot.Collections.Array<GVector2> find_path(
        float startX, float startZ, float goalX, float goalZ,
        int movement = 0, float collisionSize = 16f, bool allowSmoothing = true)
    {
        var result = new Godot.Collections.Array<GVector2>();
        if (_processor == null)
            return result;
        foreach (NVector2 p in _processor.FindPath(
                     startX, startZ, goalX, goalZ, (W3MovementType)movement, collisionSize, allowSmoothing))
            result.Add(new GVector2(p.X, p.Y));
        return result;
    }
}
