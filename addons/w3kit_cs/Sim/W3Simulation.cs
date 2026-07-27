using System;
using System.Collections.Generic;
using Godot;
using Diablo1Like.W3.Pathing;

namespace Diablo1Like.W3.Sim;

/// <summary>
/// 模拟层：拥有全部 W3SimUnit、动态碰撞索引以及一条共享的预算式寻路队列
/// （Warsmash CSimulation 的缩小版）。以固定 20 tick/秒在 _PhysicsProcess
/// 中推进；渲染层通过 interpolation_alpha() 在 prev/current tick 状态间插值。
/// 迁移自 addons/w3kit/sim/w3_sim.gd。公共成员保持 snake_case 以兼容
/// tools/*.gd 等仍在使用鸭子类型访问的调用点。
/// </summary>
[GlobalClass]
public partial class W3Simulation : Node
{
    [Signal]
    public delegate void UnitSpawnedEventHandler(W3SimUnit unit);

    [Signal]
    public delegate void UnitDiedEventHandler(W3SimUnit unit, W3SimUnit killer);

    [Signal]
    public delegate void UnitRemovedEventHandler(W3SimUnit unit);

    public const float Step = 1f / 20f;

    /// <summary>W3PathingGridBridge 或 null（null 表示只能直线移动）。</summary>
    public GodotObject? pathing_grid;
    public W3WorldCollisionBridge collision = new();
    public int tick_count;

    private readonly List<W3SimUnit> _units = new();
    private readonly Dictionary<int, W3SimUnit> _unitsById = new();
    private int _nextUnitId = 1;
    private float _tickAccum;

    private W3PathfindingBridge? _pathfinder;
    private readonly List<PathJob> _pathQueue = new();
    private W3SimUnit? _activePathUnit;

    /// <summary>确定性的战斗掷骰（固定种子；后续存读档时重新播种）。</summary>
    private readonly RandomNumberGenerator _rng = new();

    private struct PathJob
    {
        public W3SimUnit Unit;
        public Vector2 Start;
        public Vector2 Goal;
        public bool Smoothing;
    }

    public W3Simulation()
    {
        _rng.Seed = 0x1337;
    }

    public void setup(GodotObject? pPathingGrid)
    {
        pathing_grid = pPathingGrid;
        if (pathing_grid != null)
        {
            _pathfinder = new W3PathfindingBridge();
            _pathfinder.BindGrid(pathing_grid);
        }
        else
        {
            GD.PushWarning("W3Simulation: no pathing grid — units move in straight lines only");
        }
    }

    // --- 单位 ----------------------------------------------------------

    public W3SimUnit create_unit(string rawcode, Godot.Collections.Dictionary stats, Vector2 w3Pos, float facingDeg, int ownerId)
    {
        var unit = new W3SimUnit
        {
            sim = this,
            unit_id = _nextUnitId++,
            rawcode = rawcode,
            owner = ownerId,
        };
        unit.configure(stats);
        unit.pos = w3Pos;
        unit.prev_pos = w3Pos;
        unit.home = w3Pos;
        unit.facing = WrapF(facingDeg, 0f, 360f);
        unit.prev_facing = unit.facing;
        _units.Add(unit);
        _unitsById[unit.unit_id] = unit;
        collision.add_unit(unit);
        unit.Died += OnUnitDied;
        EmitSignal(SignalName.UnitSpawned, unit);
        return unit;
    }

    public void remove_unit(W3SimUnit? unit)
    {
        if (unit == null || !_unitsById.ContainsKey(unit.unit_id))
            return;
        if (!unit.is_dead())
            collision.remove_unit(unit);
        cancel_path_request(unit);
        _units.Remove(unit);
        _unitsById.Remove(unit.unit_id);
        EmitSignal(SignalName.UnitRemoved, unit);
    }

    /// <summary>GDScript/工具用：返回 Godot 数组（支持 .size()）。</summary>
    public Godot.Collections.Array<W3SimUnit> get_units()
    {
        var arr = new Godot.Collections.Array<W3SimUnit>();
        foreach (W3SimUnit u in _units)
            arr.Add(u);
        return arr;
    }

    /// <summary>C# 内部使用，避免 Godot 数组装箱开销。</summary>
    public IReadOnlyList<W3SimUnit> Units => _units;

    public W3SimUnit? get_unit(int unitId) => _unitsById.TryGetValue(unitId, out W3SimUnit? u) ? u : null;

    public W3SimUnit? find_nearest_enemy(W3SimUnit unit, float radius)
    {
        W3SimUnit? best = null;
        float bestD2 = float.PositiveInfinity;
        foreach (W3SimUnit other in collision.EnumUnitsInRangeNative(unit.pos.X, unit.pos.Y, radius, unit))
        {
            if (other.owner == unit.owner)
                continue;
            float d2 = unit.pos.DistanceSquaredTo(other.pos);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = other;
            }
        }
        return best;
    }

    public float roll_damage(int dice, int sides, int plus)
    {
        int total = plus;
        for (int i = 0; i < dice; i++)
            total += _rng.RandiRange(1, Math.Max(1, sides));
        return total;
    }

    /// <summary>把出生点吸附到可行走地面（在格子上做螺旋搜索）。</summary>
    public Vector2 find_pathable_spawn(Vector2 w3Pos, float collisionSize, int maxCells = 20)
    {
        if (pathing_grid == null)
            return w3Pos;
        if (SpawnOk(w3Pos, collisionSize))
            return w3Pos;
        for (int radius = 1; radius < maxCells; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                        continue;
                    Vector2 candidate = w3Pos + new Vector2(dx * 32f, dy * 32f);
                    if (SpawnOk(candidate, collisionSize))
                        return candidate;
                }
            }
        }
        return w3Pos;
    }

    private bool SpawnOk(Vector2 p, float collisionSize)
    {
        if (!IsPathableFoot(pathing_grid, p.X, p.Y, collisionSize))
            return false;
        Vector2 rmin = new(p.X - collisionSize, p.Y - collisionSize);
        Vector2 rmax = new(p.X + collisionSize, p.Y + collisionSize);
        return !collision.intersects_anything_other_than(rmin, rmax, null);
    }

    // --- 寻路队列（CPathfindingProcessor.update） -------------------------

    public void request_path(W3SimUnit unit, Vector2 start, Vector2 goal, bool allowSmoothing)
    {
        cancel_path_request(unit);
        _pathQueue.Add(new PathJob { Unit = unit, Start = start, Goal = goal, Smoothing = allowSmoothing });
    }

    public void cancel_path_request(W3SimUnit unit)
    {
        if (_activePathUnit == unit)
        {
            _activePathUnit = null;
            _pathfinder?.cancel();
        }
        for (int i = _pathQueue.Count - 1; i >= 0; i--)
        {
            if (_pathQueue[i].Unit == unit)
                _pathQueue.RemoveAt(i);
        }
    }

    private void TickPathfinding()
    {
        if (_pathfinder == null)
            return;
        if (_activePathUnit == null && _pathQueue.Count > 0)
        {
            PathJob job = _pathQueue[0];
            _pathQueue.RemoveAt(0);
            _activePathUnit = job.Unit;
            _pathfinder.start_job(job.Start, job.Goal, _activePathUnit.collision_size, job.Smoothing);
        }
        if (_activePathUnit == null)
            return;
        Variant result = _pathfinder.update(); // 预算式；仍在搜索时为 Nil
        if (result.VariantType != Variant.Type.Nil)
        {
            W3SimUnit unit = _activePathUnit;
            _activePathUnit = null;
            if (_unitsById.ContainsKey(unit.unit_id) && !unit.is_dead())
                unit.on_path_found(result.AsVector2Array());
        }
    }

    // --- Tick 循环 -----------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        _tickAccum += (float)delta;
        // 限制补帧步数，避免长时间卡顿导致帧时间失控。
        int steps = 0;
        while (_tickAccum >= Step && steps < 8)
        {
            _tickAccum -= Step;
            steps++;
            step_once();
        }
        if (steps == 8)
            _tickAccum = 0f;
    }

    /// <summary>一次 50ms 的模拟 tick；公开供无头测试在没有物理循环时确定性驱动。</summary>
    public void step_once()
    {
        tick_count++;
        TickPathfinding();
        // 对副本迭代：尸体腐烂会在 tick 期间移除单位。
        foreach (W3SimUnit unit in _units.ToArray())
            unit.tick();
    }

    /// <summary>上一 tick 与当前 tick 之间的 0..1 插值进度。</summary>
    public float interpolation_alpha() => Mathf.Clamp(_tickAccum / Step, 0f, 1f);

    private void OnUnitDied(W3SimUnit unit, W3SimUnit killer)
    {
        EmitSignal(SignalName.UnitDied, unit, killer);
    }

    /// <summary>grid 通常是 W3PathingGridBridge；也兼容鸭子类型的 GodotObject。</summary>
    internal static bool IsPathableFoot(GodotObject? grid, float x, float y, float collisionSize)
    {
        if (grid == null)
            return true;
        if (grid is W3PathingGridBridge bridge)
            return bridge.is_pathable_foot(x, y, collisionSize);
        return grid.Call("is_pathable_foot", x, y, collisionSize).AsBool();
    }

    private static float WrapF(float value, float min, float max)
    {
        float range = max - min;
        if (Mathf.IsZeroApprox(range))
            return min;
        return value - range * MathF.Floor((value - min) / range);
    }
}
