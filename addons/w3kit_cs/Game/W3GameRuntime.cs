using Godot;
using Diablo1Like.MapRuntime;
using Diablo1Like.W3.Sim;
using Diablo1Like.W3.World;
using W3Compat.Coordinates;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Diablo1Like.W3.Game;

/// <summary>
/// 游戏层：把已加载的 W3 世界（地形+资产）与模拟层（W3Simulation）及其
/// 表现层（W3UnitRenderer）连接起来；ARPG 从这里生长出玩家单位、地图怪物、
/// 点击指令。迁移自 scripts/game/w3_game.gd。公共成员保持 snake_case 以兼容
/// scripts/w3/w3_sandbox.gd、tools/*.gd 等仍在使用的鸭子类型调用点。
/// </summary>
[GlobalClass]
public partial class W3GameRuntime : Node
{
    [Signal]
    public delegate void PlayerSpawnedEventHandler(W3SimUnit unit);

    [Signal]
    public delegate void PlayerDiedEventHandler(W3SimUnit unit);

    public const int PlayerOwner = 0;
    public const int NeutralHostile = 24; // WC3 PLAYER_NEUTRAL_AGGRESSIVE
    public const string PlayerRawcode = "Obla"; // ARPG 职业就绪前的剑圣替身

    public W3MapWorld? world;
    public W3Simulation? sim;
    public W3UnitRenderer? renderer;
    public W3SimUnit? player;

    public bool setup(W3MapWorld pWorld)
    {
        world = pWorld;
        if (world == null || !world.is_loaded())
        {
            GD.PushError("W3GameRuntime: world is not loaded");
            return false;
        }
        sim = new W3Simulation { Name = "W3Sim" };
        AddChild(sim);
        sim.setup(world.pathing);
        renderer = new W3UnitRenderer { Name = "W3UnitRenderer" };
        AddChild(renderer);
        renderer.setup(world, sim);
        sim.UnitDied += OnUnitDied;
        SpawnMapUnits();
        return true;
    }

    private void SpawnMapUnits()
    {
        if (world!.catalog == null)
        {
            GD.PushError("W3GameRuntime: no object catalog — map units skipped");
            return;
        }
        int spawned = 0;
        foreach (Variant pv in world.unit_placements)
        {
            if (pv.AsGodotObject() is not W3PlacementData p)
                continue;
            string rawcode = p.Id;
            Godot.Collections.Dictionary stats = world.catalog.Call("unit_combat_stats", rawcode).AsGodotDictionary();
            Vector2 w3Pos = GodotAxesToGround(p.Position);
            int ownerId = p.PlayerOwner;
            W3SimUnit unit = sim!.create_unit(rawcode, stats, w3Pos, Mathf.RadToDeg(p.Angle), ownerId);
            unit.auto_acquire = ownerId == NeutralHostile;
            spawned++;
        }
        if (spawned > 0)
            GD.Print($"W3GameRuntime: {spawned} map units simulated");
    }

    /// <summary>在 Godot 空间地面点生成（或传送）玩家单位。</summary>
    public W3SimUnit spawn_player(Vector3 godotPoint)
    {
        Vector2 w3Point = GodotAxesToGround(godotPoint);
        w3Point = sim!.find_pathable_spawn(w3Point, 32f);
        if (player != null && !player.is_dead())
        {
            player.teleport(w3Point);
            return player;
        }
        Godot.Collections.Dictionary stats = world!.catalog!.Call("unit_combat_stats", PlayerRawcode).AsGodotDictionary();
        player = sim.create_unit(PlayerRawcode, stats, w3Point, 270f, PlayerOwner);
        EmitSignal(SignalName.PlayerSpawned, player);
        GD.Print($"W3GameRuntime: player {PlayerRawcode} spawned at {w3Point}");
        return player;
    }

    public bool has_live_player() => player != null && !player.is_dead();

    /// <summary>左键指令：攻击选中的敌人，否则移动到地面落点。</summary>
    public bool issue_player_order(Camera3D camera, Vector2 screenPos)
    {
        if (!has_live_player())
            return false;
        W3SimUnit? target = pick_unit(camera, screenPos, player);
        if (target != null && target.owner != player!.owner)
        {
            player.order_attack(target);
            return true;
        }
        Variant hitVariant = world!.pick_ground(camera, screenPos);
        if (hitVariant.VariantType == Variant.Type.Nil)
            return false;
        player!.order_move(GodotAxesToGround(hitVariant.AsVector3()));
        return true;
    }

    /// <summary>屏幕空间选取：投影中心落在其投影碰撞圆（附加少量容差）内的最近存活单位。</summary>
    public W3SimUnit? pick_unit(Camera3D camera, Vector2 screenPos, W3SimUnit? ignore = null)
    {
        W3SimUnit? best = null;
        float bestPx = float.PositiveInfinity;
        foreach (W3SimUnit unit in sim!.Units)
        {
            if (unit == ignore || unit.is_dead())
                continue;
            float height = world!.ground_height_at(unit.pos.X, unit.pos.Y);
            Vector3 center = GroundToGodot(unit.pos.X, unit.pos.Y, height + 40f);
            if (camera.IsPositionBehind(center))
                continue;
            Vector2 projected = camera.UnprojectPosition(center);
            Vector2 edge = camera.UnprojectPosition(center + new Vector3(unit.collision_size * 1.6f, 0f, 0f));
            float pickRadius = Mathf.Max(16f, projected.DistanceTo(edge));
            float d = projected.DistanceTo(screenPos);
            if (d <= pickRadius && d < bestPx)
            {
                bestPx = d;
                best = unit;
            }
        }
        return best;
    }

    private void OnUnitDied(W3SimUnit unit, W3SimUnit killer)
    {
        if (unit == player)
        {
            EmitSignal(SignalName.PlayerDied, unit);
            GD.Print("W3GameRuntime: player died");
        }
    }

    private static Vector2 GodotAxesToGround(Vector3 godot)
    {
        NVector2 ground = W3Coordinates.GodotAxesToGround(new NVector3(godot.X, godot.Y, godot.Z));
        return new Vector2(ground.X, ground.Y);
    }

    private static Vector3 GroundToGodot(float w3X, float w3Y, float godotHeight)
    {
        NVector3 v = W3Coordinates.GroundToGodotAxes(w3X, w3Y, godotHeight);
        return new Vector3(v.X, v.Y, v.Z);
    }
}
