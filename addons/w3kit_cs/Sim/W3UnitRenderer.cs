using System.Collections.Generic;
using Godot;
using Diablo1Like.W3.Mdx;
using Diablo1Like.W3.Paths;
using Diablo1Like.W3.World;
using W3Compat.Coordinates;
using NVector3 = System.Numerics.Vector3;

namespace Diablo1Like.W3.Sim;

/// <summary>
/// 表现层绑定：每个存活 W3SimUnit 对应一个带动画的 MDX 实例。
/// 只读取模拟状态（位置/朝向/动画意图）并在模拟 tick 间插值，绝不回写模拟。
/// 迁移自 addons/w3kit/sim/w3_unit_renderer.gd；MDX 几何解析走 C# W3MdxModel。
/// </summary>
[GlobalClass]
public partial class W3UnitRenderer : Node3D
{
    private const int AnimIdle = 0;
    private const int AnimWalk = 1;
    private const int AnimAttack = 2;
    private const int AnimDeath = 3;

    // 与 W3MdxBatcher 相同的分级：只给附近的空闲单位蒙皮，但正在行动的单位
    // 一直动画到绘制距离，避免它们以定格姿势滑行。
    private const float AnimNearDist = 950f;
    private const float DrawDist = 4200f;
    private const float ProximityCheckSec = 0.25f;

    private static readonly W3PathResolverBridge Paths = new();

    private sealed class View
    {
        public W3SimUnit Unit = null!;
        public Node3D Node = null!;
        public W3MdxInstance? Mdx;
        public int Anim = AnimIdle;
    }

    private W3MapWorld? _world;
    private W3Simulation? _sim;
    private readonly Dictionary<int, View> _views = new();
    private readonly Dictionary<string, GodotObject> _modelCache = new();
    private float _proximityTimer;

    public void setup(W3MapWorld world, W3Simulation sim)
    {
        _world = world;
        _sim = sim;
        sim.UnitSpawned += OnUnitSpawned;
        sim.UnitRemoved += OnUnitRemoved;
        sim.UnitDied += OnUnitDied;
        foreach (W3SimUnit unit in sim.Units)
            OnUnitSpawned(unit);
    }

    public Node3D? view_node(W3SimUnit unit) =>
        _views.TryGetValue(unit.unit_id, out View? view) ? view.Node : null;

    /// <summary>无渲染模型的模拟单位数量；供审计类工具使用（避免直接访问私有 _views）。</summary>
    public int broken_view_count()
    {
        int count = 0;
        foreach (View view in _views.Values)
            if (view.Mdx == null)
                count++;
        return count;
    }

    private void OnUnitSpawned(W3SimUnit unit)
    {
        var node = new Node3D { Name = $"Unit_{unit.unit_id}_{unit.rawcode}" };
        AddChild(node);
        var view = new View { Unit = unit, Node = node };
        view.Mdx = BuildMdx(unit, node);
        unit.AttackSwing += OnAttackSwing;
        _views[unit.unit_id] = view;
        ApplyTransform(view, 1f);
    }

    private void OnAttackSwing(W3SimUnit unit, W3SimUnit target)
    {
        // 把攻击循环重新同步到真实的挥砍 tick。
        if (!_views.TryGetValue(unit.unit_id, out View? view) || view.Mdx == null)
            return;
        view.Anim = AnimAttack;
        view.Mdx.play_named("Attack", true);
    }

    private void OnUnitRemoved(W3SimUnit unit)
    {
        if (!_views.TryGetValue(unit.unit_id, out View? view))
            return;
        _views.Remove(unit.unit_id);
        if (IsInstanceValid(view.Node))
            view.Node.QueueFree();
    }

    private void OnUnitDied(W3SimUnit unit, W3SimUnit killer)
    {
        if (!_views.TryGetValue(unit.unit_id, out View? view))
            return;
        view.Anim = AnimDeath;
        W3MdxInstance? mdx = view.Mdx;
        if (mdx != null && !mdx.play_named_once("Death"))
        {
            // 模型没有 Death 序列：立即隐藏尸体。
            if (IsInstanceValid(view.Node))
                view.Node.Visible = false;
        }
    }

    private W3MdxInstance? BuildMdx(W3SimUnit unit, Node3D parent)
    {
        if (_world == null || _world.catalog == null || _world.textures == null)
        {
            GD.PushError($"W3UnitRenderer: world catalog/textures unavailable for {unit.rawcode}");
            return null;
        }
        string modelRel = _world.catalog.Call("resolve_model", unit.rawcode, true).AsString();
        if (string.IsNullOrEmpty(modelRel))
        {
            GD.PushError($"W3UnitRenderer: no model for rawcode {unit.rawcode}");
            return null;
        }
        string absPath = Paths.ResolveGamePath(modelRel);
        if (string.IsNullOrEmpty(absPath))
        {
            GD.PushError($"W3UnitRenderer: cannot resolve model file {modelRel} ({unit.rawcode})");
            return null;
        }
        GodotObject? model = _modelCache.GetValueOrDefault(absPath);
        if (model == null)
        {
            model = LoadModelFile(absPath);
            if (model == null || model.Get("geosets").AsGodotArray().Count == 0)
            {
                GD.PushError($"W3UnitRenderer: MDX load failed: {absPath}");
                return null;
            }
            _modelCache[absPath] = model;
        }
        var mdx = new W3MdxInstance { Name = "Mdx" };
        parent.AddChild(mdx);
        mdx.setup(model, _world.textures, absPath, new Godot.Collections.Dictionary { ["animate"] = true });
        float scale = _world.catalog.Call("model_scale", unit.rawcode, true).AsSingle();
        mdx.Scale = Vector3.One * scale;
        return mdx;
    }

    private static GodotObject? LoadModelFile(string absPath) => W3MdxModel.LoadFile(absPath);

    public override void _Process(double delta)
    {
        if (_sim == null)
            return;
        float alpha = _sim.interpolation_alpha();
        foreach (View view in _views.Values)
        {
            ApplyTransform(view, alpha);
            ApplyAnimation(view);
        }
        _proximityTimer += (float)delta;
        if (_proximityTimer >= ProximityCheckSec)
        {
            _proximityTimer = 0f;
            UpdateAnimProximity();
        }
    }

    private void UpdateAnimProximity()
    {
        Camera3D? cam = IsInsideTree() ? GetViewport().GetCamera3D() : null;
        if (cam == null)
            return;
        Vector3 camPos = cam.GlobalPosition;
        foreach (View view in _views.Values)
        {
            W3MdxInstance? mdx = view.Mdx;
            Node3D node = view.Node;
            if (mdx == null || !IsInstanceValid(node))
                continue;
            W3SimUnit unit = view.Unit;
            float d = camPos.DistanceTo(node.GlobalPosition);
            node.Visible = d <= DrawDist;
            bool acting = unit.is_moving || unit.is_attacking;
            mdx.set_anim_active(d <= AnimNearDist || (acting && d <= DrawDist));
        }
    }

    private void ApplyTransform(View view, float alpha)
    {
        W3SimUnit unit = view.Unit;
        Node3D node = view.Node;
        if (!IsInstanceValid(node))
            return;
        Vector2 p = unit.prev_pos.Lerp(unit.pos, alpha);
        float height = 0f;
        if (_world != null && _world.sampler != null)
            height = _world.sampler.Call("sample_y", p.X, p.Y).AsSingle();
        NVector3 ground = W3Coordinates.GroundToGodotAxes(p.X, p.Y, height);
        node.Position = new Vector3(ground.X, ground.Y, ground.Z);
        float facing = LerpAngleRad(Mathf.DegToRad(unit.prev_facing), Mathf.DegToRad(unit.facing), alpha);
        node.Rotation = new Vector3(0f, W3Coordinates.FacingRadToGodotYaw(facing), 0f);
    }

    private static float LerpAngleRad(float from, float to, float weight)
    {
        float difference = (to - from) % Mathf.Tau;
        float distance = ((2f * difference) % Mathf.Tau) - difference;
        return from + distance * weight;
    }

    private void ApplyAnimation(View view)
    {
        W3SimUnit unit = view.Unit;
        W3MdxInstance? mdx = view.Mdx;
        if (mdx == null || unit.is_dead())
            return; // 死亡的一次性动画由 OnUnitDied 处理
        int wanted = AnimIdle;
        if (unit.is_moving)
            wanted = AnimWalk;
        else if (unit.is_attacking)
            wanted = AnimAttack;
        if (view.Anim == wanted)
            return;
        view.Anim = wanted;
        switch (wanted)
        {
            case AnimWalk:
                mdx.set_moving(true);
                break;
            case AnimAttack:
                mdx.play_named("Attack");
                break;
            default:
                mdx.set_moving(false);
                break;
        }
    }
}
