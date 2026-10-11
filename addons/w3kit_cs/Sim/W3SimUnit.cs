using System;
using System.Collections.Generic;
using Godot;
using W3Compat.Sim;
using Diablo1Like.W3.Pathing;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Sim;

/// <summary>
/// 单个模拟地面单位：只含状态与行为，不持有场景节点；迁移自
/// addons/w3kit/sim/w3_sim_unit.gd。移动是 Warsmash CBehaviorMove 的移植
/// （转向速率、推进窗口、预算式重寻路）；近战战斗是简化版 CBehaviorAttack
/// （追击、冷却挥砍、WC3 骰子伤害）。渲染层只读取本状态，绝不写入。
/// 公共成员保持 snake_case（与旧 GDScript 属性同名），因为 tools/*.gd 等
/// 仍通过 "." 动态访问（Object.Get/Call 鸭子类型）。
/// </summary>
[GlobalClass]
public partial class W3SimUnit : RefCounted, ISimCollisionUnit
{
    public const int StateIdle = 0;
    public const int StateMove = 1;
    public const int StateAttack = 2;
    public const int StateDead = 3;

    // 供 GDScript/工具以实例属性形式读取状态常量（C# const 不会被 Godot
    // 属性系统暴露，因此用只读属性包一层；勿再新增同名 get_/set_ 方法，以免 CS0082）。
    public int STATE_IDLE => StateIdle;
    public int STATE_MOVE => StateMove;
    public int STATE_ATTACK => StateAttack;
    public int STATE_DEAD => StateDead;

    private const float Step = 1f / 20f; // WarsmashConstants.SIMULATION_STEP_TIME
    private const int AcquireScanTicks = 10; // 空闲目标扫描间隔 0.5s
    private const float LeashRange = 1500f;
    private const int CorpseTicksConst = 8 * 20;
    public int CORPSE_TICKS => CorpseTicksConst;

    [Signal]
    public delegate void DamagedEventHandler(W3SimUnit unit, float amount, W3SimUnit attacker);

    [Signal]
    public delegate void DiedEventHandler(W3SimUnit unit, W3SimUnit killer);

    [Signal]
    public delegate void AttackSwingEventHandler(W3SimUnit unit, W3SimUnit target);

    public W3Simulation sim = null!; // 由 W3Simulation.create_unit 注入的反向引用
    public int unit_id = -1;
    public string rawcode = "";
    public int owner = 0;
    public bool auto_acquire = false;

    // 战斗/移动数值（见 W3ObjectCatalog.unit_combat_stats）。
    public float max_hp = 100f;
    public float hp = 100f;
    public float speed = 270f;
    public float turn_rate = 0.6f; // 每模拟 tick 的弧度
    public float prop_window_deg = 60f;
    public float collision_size = 32f;
    public float acquire_range = 500f;
    public float attack_range = 90f;
    public float attack_cooldown = 1.5f;
    public int damage_dice = 1;
    public int damage_sides = 4;
    public int damage_plus = 1;

    // W3 地面平面状态；prev_* 是上一 tick 的值，供渲染插值。
    public Vector2 pos = Vector2.Zero;
    public Vector2 prev_pos = Vector2.Zero;
    public float facing = 270f;
    public float prev_facing = 270f;
    public Vector2 home = Vector2.Zero;

    public int state = StateIdle;
    /// <summary>本 tick 推进窗口是否允许前进（渲染层映射为 Walk 序列）。</summary>
    public bool is_moving = false;
    /// <summary>是否处于目标攻击范围内（渲染层映射为 Attack 序列；attack_swing 用于重新同步真实挥砍）。</summary>
    public bool is_attacking = false;

    // CBehaviorMove 状态。
    private Vector2 _moveGoal = Vector2.Zero;
    private List<Vector2>? _path; // null 表示尚未请求寻路
    private int _searchCycles;
    private bool _firstPathfindJob;
    private bool _pathfindingActive;
    private bool _pathfindingFailedGiveUp;
    private int _giveUpUntilTick;
    private bool _returningHome;

    // 攻击状态。
    private W3SimUnit? _attackTarget;
    private int _swingTicksLeft;
    private int _corpseTicksLeft;
    private int _acquireScanAt;

    // --- ISimCollisionUnit -------------------------------------------------

    public int UnitId => unit_id;
    public float PosX => pos.X;
    public float PosY => pos.Y;
    public float CollisionSize => collision_size;
    public bool IsDead => is_dead();

    public void configure(GDictionary stats)
    {
        max_hp = Mathf.Max(1f, OF(stats, "hp", 100f));
        hp = max_hp;
        speed = Mathf.Max(1f, OF(stats, "speed", 270f));
        turn_rate = Mathf.Clamp(OF(stats, "turn_rate", 0.6f), 0.01f, Mathf.Tau);
        prop_window_deg = Mathf.Clamp(OF(stats, "prop_window_deg", 60f), 1f, 180f);
        collision_size = Mathf.Max(0f, OF(stats, "collision", 32f));
        acquire_range = Mathf.Max(0f, OF(stats, "acquire_range", 500f));
        attack_range = Mathf.Max(8f, OF(stats, "attack_range", 90f));
        attack_cooldown = Mathf.Max(0.1f, OF(stats, "attack_cooldown", 1.5f));
        damage_dice = Math.Max(0, OI(stats, "damage_dice", 1));
        damage_sides = Math.Max(1, OI(stats, "damage_sides", 4));
        damage_plus = Math.Max(0, OI(stats, "damage_plus", 1));
    }

    private static float OF(GDictionary d, string key, float fallback) =>
        d.TryGetValue(key, out Variant v) ? v.AsSingle() : fallback;

    private static int OI(GDictionary d, string key, int fallback) =>
        d.TryGetValue(key, out Variant v) ? v.AsInt32() : fallback;

    public bool is_dead() => state == StateDead;

    public Vector2 rect_min() => new(pos.X - collision_size, pos.Y - collision_size);

    public Vector2 rect_max() => new(pos.X + collision_size, pos.Y + collision_size);

    /// <summary>碰撞圆之间的距离（>= 0）；WC3 的射程判定基于矩形，圆近似在我们的方形占地下误差不超过一格。</summary>
    public float range_to(W3SimUnit other) =>
        Mathf.Max(0f, pos.DistanceTo(other.pos) - collision_size - other.collision_size);

    // --- 指令 ----------------------------------------------------------

    public void order_move(Vector2 point)
    {
        if (is_dead())
            return;
        _attackTarget = null;
        _returningHome = false;
        state = StateMove;
        ResetMove(point);
    }

    public void order_attack(W3SimUnit? target)
    {
        if (is_dead() || target == null || target == this || target.is_dead())
            return;
        _attackTarget = target;
        _returningHome = false;
        state = StateAttack;
        ResetMove(target.pos);
    }

    public void stop()
    {
        if (is_dead())
            return;
        _attackTarget = null;
        _returningHome = false;
        state = StateIdle;
        CancelMove();
    }

    /// <summary>调试/编辑器用：瞬间移动（同时重置渲染插值）。</summary>
    public void teleport(Vector2 point)
    {
        if (is_dead())
            return;
        stop();
        Vector2 oldMin = rect_min();
        Vector2 oldMax = rect_max();
        pos = point;
        prev_pos = point;
        home = point;
        sim.collision.translate_unit(this, oldMin, oldMax);
    }

    // --- 伤害 ------------------------------------------------------------

    public void apply_damage(float amount, W3SimUnit? attacker)
    {
        if (is_dead())
            return;
        hp -= amount;
        EmitSignal(SignalName.Damaged, this, amount, attacker!);
        if (hp <= 0f)
        {
            hp = 0f;
            Die(attacker);
            return;
        }
        // AI 单位站桩时的 WC3 式反击；正在归位的单位忽略反击，否则会在
        // 归位边缘被无限拉扯。
        if (auto_acquire && !_returningHome && _attackTarget == null
            && attacker != null && !attacker.is_dead())
        {
            order_attack(attacker);
        }
    }

    private void Die(W3SimUnit? killer)
    {
        state = StateDead;
        is_moving = false;
        _attackTarget = null;
        CancelMove();
        _corpseTicksLeft = CorpseTicksConst;
        sim.collision.remove_unit(this);
        EmitSignal(SignalName.Died, this, killer!);
    }

    // --- Tick --------------------------------------------------------------

    public void tick()
    {
        prev_pos = pos;
        prev_facing = facing;
        is_moving = false;
        is_attacking = false;
        switch (state)
        {
            case StateDead:
                _corpseTicksLeft--;
                if (_corpseTicksLeft <= 0)
                    sim.remove_unit(this);
                break;
            case StateIdle:
                TickIdle();
                break;
            case StateMove:
                if (TickMove(_moveGoal))
                {
                    state = StateIdle;
                    _returningHome = false;
                }
                break;
            case StateAttack:
                TickAttack();
                break;
        }
        if (_swingTicksLeft > 0)
            _swingTicksLeft--;
    }

    private void TickIdle()
    {
        if (!auto_acquire || sim.tick_count < _acquireScanAt)
            return;
        _acquireScanAt = sim.tick_count + AcquireScanTicks;
        W3SimUnit? enemy = sim.find_nearest_enemy(this, acquire_range);
        if (enemy != null)
            order_attack(enemy);
    }

    private void TickAttack()
    {
        W3SimUnit? target = _attackTarget;
        if (target == null || target.is_dead())
        {
            _attackTarget = null;
            if (auto_acquire)
                ReturnHome();
            else
                state = StateIdle;
            return;
        }
        if (auto_acquire && pos.DistanceTo(home) > LeashRange)
        {
            ReturnHome();
            return;
        }
        if (range_to(target) <= attack_range)
        {
            CancelMoveKeepState();
            is_attacking = true;
            float goalAngle = AngleTo(target.pos);
            float absDelta = TurnToward(goalAngle);
            if (absDelta < prop_window_deg && _swingTicksLeft <= 0)
            {
                _swingTicksLeft = (int)MathF.Round(attack_cooldown / Step, MidpointRounding.AwayFromZero);
                EmitSignal(SignalName.AttackSwing, this, target);
                target.apply_damage(sim.roll_damage(damage_dice, damage_sides, damage_plus), this);
            }
            return;
        }
        // 追击：目标偏离原请求点太远时重新寻路。
        if (target.pos.DistanceTo(_moveGoal) > 128f)
            ResetMove(target.pos);
        TickMove(target.pos);
    }

    private void ReturnHome()
    {
        _attackTarget = null;
        _returningHome = true;
        state = StateMove;
        hp = max_hp; // 经典归位重置生命值，避免营地被慢慢磨死
        ResetMove(home);
    }

    // --- 移动（CBehaviorMove 移植） ------------------------------------

    private void ResetMove(Vector2 goal)
    {
        _moveGoal = goal;
        _path = null;
        _searchCycles = 0;
        _firstPathfindJob = false;
        _pathfindingFailedGiveUp = false;
        _giveUpUntilTick = 0;
        CancelPathfindRequest();
    }

    private void CancelMove()
    {
        _path = null;
        CancelPathfindRequest();
    }

    private void CancelMoveKeepState()
    {
        // 已进入攻击范围：丢弃路径点但保留 ATTACK 状态。
        _path = null;
        CancelPathfindRequest();
    }

    private void CancelPathfindRequest()
    {
        if (_pathfindingActive)
        {
            sim.cancel_path_request(this);
            _pathfindingActive = false;
        }
    }

    /// <summary>朝 goal 走一个 tick；返回 true 表示本次移动已完成。</summary>
    private bool TickMove(Vector2 goal)
    {
        if (_pathfindingFailedGiveUp)
            return true;
        if (_path == null && !_pathfindingActive)
        {
            if (sim.pathing_grid == null)
            {
                _path = new List<Vector2>(); // 没有网格：直线走向目标
            }
            else
            {
                sim.request_path(this, pos, goal, true);
                _pathfindingActive = true;
                _firstPathfindJob = true;
            }
        }
        // 等待共享寻路队列时仍可原地转向。
        Vector2 currentTarget = CurrentWaypoint(goal);
        Vector2 deltaVec = currentTarget - pos;
        if (deltaVec.IsZeroApprox())
        {
            if (_path != null && !_pathfindingActive)
            {
                if (_path.Count == 0)
                    return true;
                _path.RemoveAt(0);
            }
            return false;
        }
        float goalAngle = AngleTo(currentTarget);
        float absDelta = TurnToward(goalAngle);

        bool blockedByGiveUpDelay = sim.tick_count < _giveUpUntilTick;
        if (blockedByGiveUpDelay || _path == null || _pathfindingActive || absDelta >= prop_window_deg)
            return false;

        float continueDistance = speed * Step;
        Vector2 current = pos;
        while (continueDistance > 0f)
        {
            bool done;
            Vector2 next;
            float travelDistance = (currentTarget - current).Length();
            if (travelDistance <= continueDistance)
            {
                next = currentTarget;
                continueDistance -= travelDistance;
                done = true;
            }
            else
            {
                float radianFacing = Mathf.DegToRad(facing);
                next = current + new Vector2(MathF.Cos(radianFacing), MathF.Sin(radianFacing)) * continueDistance;
                continueDistance = 0f;
                done = false;
            }
            if (PointPathable(next))
            {
                SetPoint(next);
                is_moving = true;
                current = next;
                if (done)
                {
                    if (travelDistance > 0f)
                        _searchCycles = 0;
                    if (_path!.Count == 0)
                        return true;
                    _path.RemoveAt(0);
                    currentTarget = CurrentWaypoint(goal);
                    Vector2 nextDelta = currentTarget - current;
                    if (nextDelta.IsZeroApprox() && _path.Count == 0)
                        return true;
                    goalAngle = AngleTo(currentTarget);
                    absDelta = TurnToward(goalAngle);
                    if (absDelta >= prop_window_deg)
                        return false;
                }
            }
            else
            {
                // 前方被挡（墙体或其他单位）：重新寻路。
                if (!_pathfindingActive && sim.pathing_grid != null)
                {
                    sim.request_path(this, pos, goal, _searchCycles < 4);
                    _pathfindingActive = true;
                    _searchCycles++;
                }
                return false;
            }
        }
        return false;
    }

    private Vector2 CurrentWaypoint(Vector2 goal)
    {
        if (_path == null || _path.Count == 0)
            return goal;
        return _path[0];
    }

    /// <summary>静态网格 + 动态单位碰撞，对应 Warsmash 的移动步检查：
    /// isPathable(...) &amp;&amp; !intersectsAnythingOtherThan(tempRect, unit)。</summary>
    private bool PointPathable(Vector2 p)
    {
        if (sim.pathing_grid != null && !W3Simulation.IsPathableFoot(sim.pathing_grid, p.X, p.Y, collision_size))
            return false;
        Vector2 rmin = new(p.X - collision_size, p.Y - collision_size);
        Vector2 rmax = new(p.X + collision_size, p.Y + collision_size);
        return !sim.collision.intersects_anything_other_than(rmin, rmax, this);
    }

    private void SetPoint(Vector2 next)
    {
        Vector2 oldMin = rect_min();
        Vector2 oldMax = rect_max();
        pos = next;
        sim.collision.translate_unit(this, oldMin, oldMax);
    }

    private float AngleTo(Vector2 point)
    {
        Vector2 d = point - pos;
        float a = Mathf.RadToDeg(MathF.Atan2(d.Y, d.X));
        return a < 0f ? a + 360f : a;
    }

    /// <summary>朝 goal_angle 最多转 turn_rate；返回转向前的 |delta|
    /// （Warsmash 用转向前的偏差来判定是否允许推进）。</summary>
    private float TurnToward(float goalAngle)
    {
        float delta = AngleDelta(goalAngle, facing);
        float absDelta = MathF.Abs(delta);
        if (absDelta <= 1f && absDelta != 0f)
        {
            facing = goalAngle;
        }
        else if (absDelta > 0f)
        {
            float angleToAdd = SignF(delta) * Mathf.RadToDeg(turn_rate);
            if (absDelta < MathF.Abs(angleToAdd))
                angleToAdd = delta;
            facing = WrapF(facing + angleToAdd, 0f, 360f);
        }
        return absDelta;
    }

    private static float SignF(float v) => v > 0f ? 1f : (v < 0f ? -1f : 0f);

    private static float WrapF(float value, float min, float max)
    {
        float range = max - min;
        if (Mathf.IsZeroApprox(range))
            return min;
        return value - range * MathF.Floor((value - min) / range);
    }

    private static float AngleDelta(float goalAngle, float from)
    {
        float delta = goalAngle - from;
        if (delta < -180f)
            delta = 360f + delta;
        if (delta > 180f)
            delta = -360f + delta;
        return delta;
    }

    /// <summary>CBehaviorMove.pathFound。</summary>
    public void on_path_found(Vector2[] waypoints)
    {
        _pathfindingActive = false;
        var pathArr = new List<Vector2>(waypoints);
        _path = pathArr;
        if (_firstPathfindJob)
        {
            _firstPathfindJob = false;
            SmoothInitialPath(pathArr);
        }
        else if (pathArr.Count == 0 || _searchCycles > 6)
        {
            if (_searchCycles > 9)
                _pathfindingFailedGiveUp = true;
            else
                _giveUpUntilTick = sim.tick_count + (int)(5f / Step);
        }
    }

    /// <summary>pathFound() 首次寻路平滑：当总长度不超过直线距离的 1.15 倍且
    /// 中点可通行时，折叠路径点区间。</summary>
    private void SmoothInitialPath(List<Vector2> pathArr)
    {
        if (pathArr.Count == 0 || sim.pathing_grid == null)
            return;
        Vector2 last = pos;
        Vector2 smoothingGroupStart = pos;
        Vector2 first = pathArr[0];
        float totalPathDistance = first.DistanceTo(last);
        last = first;
        int smoothingStartIndex = -1;
        int i = 0;
        while (i < pathArr.Count - 1)
        {
            Vector2 nextPossible = pathArr[i + 1];
            totalPathDistance += nextPossible.DistanceTo(last);
            Vector2 mid = (smoothingGroupStart + nextPossible) * 0.5f;
            if (totalPathDistance < 1.15f * nextPossible.DistanceTo(smoothingGroupStart)
                && W3Simulation.IsPathableFoot(sim.pathing_grid, mid.X, mid.Y, 0f))
            {
                if (smoothingStartIndex == -1)
                    smoothingStartIndex = i;
            }
            else
            {
                if (smoothingStartIndex != -1)
                {
                    for (int j = i - 1; j >= smoothingStartIndex; j--)
                        pathArr.RemoveAt(j);
                    i = smoothingStartIndex;
                }
                smoothingStartIndex = -1;
                Vector2 smoothGroupNext = pathArr[i];
                smoothingGroupStart = smoothGroupNext;
                totalPathDistance = nextPossible.DistanceTo(smoothGroupNext);
            }
            last = nextPossible;
            i++;
        }
        if (smoothingStartIndex != -1)
        {
            while (smoothingStartIndex < pathArr.Count - 1)
                pathArr.RemoveAt(smoothingStartIndex);
        }
    }
}
