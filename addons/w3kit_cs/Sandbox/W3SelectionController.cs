using Godot;

namespace Diablo1Like.W3.Sandbox;

/// <summary>
/// 原生 Godot 风格的 W3 单位选择：物理射线 -&gt; 敌方碰撞体 -&gt; UI。
/// 地形、悬崖和隐藏的 MultiMesh 道具故意没有可选目标碰撞体。
/// 迁移自 scripts/w3/w3_unit_selection.gd。
/// </summary>
[GlobalClass]
public partial class W3SelectionController : Node3D
{
    private Node3D? _world;
    private GodotObject? _sampler;
    private Node3D _selectionRoot = null!;
    private StaticBody3D? _selected;
    private string _lastSummary = "LMB=select visible unit";

    public void Setup(Node3D? world)
    {
        _world = world;
        _sampler = world?.Get("sampler").AsGodotObject();
        _selectionRoot = new Node3D { Name = "UnitSelection" };
        AddChild(_selectionRoot);
    }

    public Godot.Collections.Dictionary Pick(Camera3D? camera, Vector2 screenPosition)
    {
        if (camera == null || GetWorld3D() == null)
            return new Godot.Collections.Dictionary();
        Vector3 from = camera.ProjectRayOrigin(screenPosition);
        Vector3 direction = camera.ProjectRayNormal(screenPosition);
        var query = PhysicsRayQueryParameters3D.Create(from, from + direction * 30000.0f);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        // 与 LevelController 使用的同一套原生敌人碰撞层。
        query.CollisionMask = 1 | 4 | 16;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        StaticBody3D? target = FindEnemyTarget(hit.TryGetValue("collider", out Variant colliderV) ? colliderV.AsGodotObject() : null);
        if (target == null)
        {
            ClearSelection();
            _lastSummary = "no visible unit selected";
            return new Godot.Collections.Dictionary();
        }
        Select(target);
        return new Godot.Collections.Dictionary
        {
            ["target"] = target,
            ["position"] = hit.TryGetValue("position", out Variant posV) ? posV : target.GlobalPosition,
        };
    }

    public void ClearSelection()
    {
        _selected = null;
        if (_selectionRoot == null)
            return;
        foreach (Node child in _selectionRoot.GetChildren())
            child.QueueFree();
    }

    public string GetLastSummary() => _lastSummary;

    private static StaticBody3D? FindEnemyTarget(GodotObject? collider)
    {
        Node? current = collider as Node;
        while (current != null)
        {
            if (current.IsInGroup("enemy") && current.IsInGroup("w3_selectable_unit") && current is StaticBody3D body)
                return body;
            current = current.GetParent();
        }
        return null;
    }

    private void Select(StaticBody3D target)
    {
        ClearSelection();
        _selected = target;
        var item = target.GetMeta("w3_preview_item", new Godot.Collections.Dictionary()).AsGodotDictionary();
        float radius = (float)target.GetMeta("w3_selection_radius", 80.0);
        Vector3 scale = target.GlobalTransform.Basis.Scale;
        radius *= Mathf.Max(Mathf.Abs(scale.X), Mathf.Abs(scale.Z));
        radius = Mathf.Clamp(radius, 28.0f, 360.0f);
        var ring = new MeshInstance3D { Name = "SelectedUnitRing" };
        var torus = new TorusMesh
        {
            InnerRadius = radius * 0.76f,
            OuterRadius = radius,
            Rings = 12,
            RingSegments = 48,
        };
        ring.Mesh = torus;
        ring.MaterialOverride = SelectionMaterial();
        ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _selectionRoot.AddChild(ring);
        Vector3 p = target.GlobalPosition;
        if (_sampler != null)
            p.Y = _sampler.Call("sample_y", p.X, p.Z).AsSingle() + 4.0f;
        ring.GlobalPosition = p;
        string animation = "";
        Node? host = target.GetParent();
        if (host != null && host.HasMethod("get_current_sequence_name"))
            animation = host.Call("get_current_sequence_name").AsString();
        string rawcode = item.ContainsKey("rawcode") ? item["rawcode"].AsString() : "unit";
        _lastSummary = $"selected {rawcode}{(animation.Length > 0 ? $" | {animation}" : "")}";
    }

    private static StandardMaterial3D SelectionMaterial()
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.78f, 0.08f, 0.95f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.5f, 0.02f),
            NoDepthTest = false,
        };
    }
}
