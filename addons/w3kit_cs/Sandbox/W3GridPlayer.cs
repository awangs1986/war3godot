using Diablo1Like.W3.Assets;
using Diablo1Like.W3.Mdx;
using Diablo1Like.W3.Paths;
using Diablo1Like.W3.World;
using Godot;

namespace Diablo1Like.W3.Sandbox;

/// <summary>
/// 用 Warsmash 风格 WPM 寻路 + A* 实现的 Diablo 式点击移动。
/// 迁移自 scripts/w3/w3_grid_player.gd。
/// MDX 模型解析走 C# <see cref="W3MdxModel"/>；贴图查询用 C# 桥 <see cref="W3TextureProvider"/>。
/// </summary>
[GlobalClass]
public partial class W3GridPlayer : CharacterBody3D
{
    private const float Speed = 220.0f;
    private const float Arrive = 16.0f;
    /// <summary>近似民兵体型碰撞；16 的奇数倍 → Warsmash 角点图。</summary>
    private const float CollisionSize = 16.0f;

    public W3MapWorld? World;
    private readonly Godot.Collections.Array<Vector3> _path = new();
    private int _pathI;
    private W3MdxInstance? _mdx;
    private MeshInstance3D _mesh = null!;

    public override void _Ready()
    {
        AddToGroup("player");
        CollisionLayer = 2;
        CollisionMask = 1;
        FloorSnapLength = 8.0f;
        EnsureVisual();
    }

    public void BindWorld(W3MapWorld world)
    {
        World = world;
        if (World?.sampler != null)
        {
            Vector2I t = World.sampler.Call("world_to_tile", GlobalPosition.X, GlobalPosition.Z).AsVector2I();
            if (!World.sampler.Call("is_tile_walkable", t).AsBool())
            {
                int tx = World.map!.Call("tile_count_x").AsInt32() / 2;
                int ty = World.map.Call("tile_count_y").AsInt32() / 2;
                t = new Vector2I(tx, ty);
            }
            GlobalPosition = World.sampler.Call("tile_center_world", t).AsVector3();
        }
    }

    public void AttachMdx(string modelPath)
    {
        if (_mesh != null)
            _mesh.Visible = false;
        _mdx?.QueueFree();
        _mdx = new W3MdxInstance();
        AddChild(_mdx);
        GodotObject? model = null;
        string absPath = modelPath;
        if (!FileAccess.FileExists(absPath))
            absPath = new W3PathResolverBridge().ResolvePath(modelPath);
        if (!string.IsNullOrEmpty(absPath) && FileAccess.FileExists(absPath))
        {
            model = W3MdxModel.LoadFile(absPath);
        }
        var tex = new W3TextureProvider();
        if (World?.archive != null)
            tex.SetMapDir(World.archive.Get("map_dir").AsString());
        _mdx.setup(model!, tex);
        _mdx.Scale = Vector3.One;
    }

    private void EnsureVisual()
    {
        if (HasNode("Mesh"))
        {
            _mesh = GetNode<MeshInstance3D>("Mesh");
            return;
        }
        _mesh = new MeshInstance3D { Name = "Mesh" };
        var box = new BoxMesh { Size = new Vector3(28, 56, 28) };
        _mesh.Mesh = box;
        _mesh.Position = new Vector3(0, 28, 0);
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.4f, 0.9f) };
        _mesh.MaterialOverride = mat;
        AddChild(_mesh);
        var col = new CollisionShape3D();
        var shape = new CapsuleShape3D { Radius = 14.0f, Height = 48.0f };
        col.Shape = shape;
        col.Position = new Vector3(0, 28, 0);
        AddChild(col);
    }

    public override void _Input(InputEvent @event)
    {
        if (GetTree().GetFirstNodeInGroup("w3_debug_pawn_control") != null)
            return;
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            ClickMove();
    }

    private void ClickMove()
    {
        if (World?.pathfinder == null || World.sampler == null)
            return;
        Camera3D? cam = GetViewport().GetCamera3D();
        if (cam == null)
            return;
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector3 from = cam.ProjectRayOrigin(mouse);
        Vector3 dir = cam.ProjectRayNormal(mouse);
        Vector3? hit = RayTerrain(from, dir);
        if (!hit.HasValue)
            return;
        _path.Clear();
        if (World.pathfinder.HasMethod("find_world_path"))
        {
            foreach (Variant v in World.pathfinder.Call("find_world_path", GlobalPosition, hit.Value, CollisionSize).AsGodotArray())
                _path.Add(v.AsVector3());
        }
        else
        {
            Vector2I goalTile = World.sampler.Call("world_to_tile", hit.Value.X, hit.Value.Z).AsVector2I();
            Vector2I startTile = World.sampler.Call("world_to_tile", GlobalPosition.X, GlobalPosition.Z).AsVector2I();
            foreach (Variant tv in World.pathfinder.Call("find_path", startTile, goalTile).AsGodotArray())
                _path.Add(World.sampler.Call("tile_center_world", tv.AsVector2I()).AsVector3());
        }
        _pathI = 0;
    }

    private Vector3? RayTerrain(Vector3 from, Vector3 dir)
    {
        if (Mathf.Abs(dir.Y) < 0.0001f)
            return null;
        for (int i = 0; i < 80; i++)
        {
            float t = 20.0f + i * 40.0f;
            Vector3 p = from + dir * t;
            float groundY = World!.sampler!.Call("sample_y", p.X, p.Z).AsSingle();
            if (p.Y <= groundY + 4.0f)
                return new Vector3(p.X, groundY, p.Z);
        }
        float y0 = World!.sampler!.Call("sample_y", GlobalPosition.X, GlobalPosition.Z).AsSingle();
        float t2 = (y0 - from.Y) / dir.Y;
        if (t2 < 0.0f)
            return null;
        Vector3 p2 = from + dir * t2;
        p2.Y = World.sampler.Call("sample_y", p2.X, p2.Z).AsSingle();
        return p2;
    }

    public override void _PhysicsProcess(double delta)
    {
        bool moving = false;
        if (_pathI < _path.Count && World?.sampler != null)
        {
            Vector3 target = _path[_pathI];
            Vector3 to = target - GlobalPosition;
            to.Y = 0.0f;
            if (to.Length() <= Arrive)
            {
                _pathI++;
            }
            else
            {
                Vector3 step = to.Normalized() * Speed * (float)delta;
                if (step.Length() > to.Length())
                    step = to;
                GlobalPosition = new Vector3(GlobalPosition.X + step.X, GlobalPosition.Y, GlobalPosition.Z + step.Z);
                Face(to);
                moving = true;
            }
        }
        if (World?.sampler != null)
        {
            GlobalPosition = new Vector3(
                GlobalPosition.X,
                World.sampler.Call("sample_y", GlobalPosition.X, GlobalPosition.Z).AsSingle(),
                GlobalPosition.Z);
        }
        _mdx?.set_moving(moving);
        Velocity = Vector3.Zero;
    }

    private void Face(Vector3 dir)
    {
        var flat = new Vector3(dir.X, 0.0f, dir.Z);
        if (flat.Length() < 0.001f)
            return;
        flat = flat.Normalized();
        Rotation = new Vector3(Rotation.X, Mathf.Atan2(flat.X, flat.Z), Rotation.Z);
    }
}
