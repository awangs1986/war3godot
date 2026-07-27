using Diablo1Like.W3.World;
using Godot;

namespace Diablo1Like.W3.Sandbox;

/// <summary>
/// 鼠标跟随的自发光球 + OmniLight3D，用于测试场景光照接收。
/// 地形（自定义无光照 shader）通过 W3GpuTerrain.set_probe_light 接收探针
/// （当前渲染器未实现该钩子时优雅跳过）。迁移自 scripts/w3/w3_light_orb.gd。
/// </summary>
[GlobalClass]
public partial class W3LightProbe : Node3D
{
    [Export] public bool ProbeEnabled;
    [Export] public float LightEnergy = 14.0f;
    [Export] public float LightRange = 950.0f;
    [Export] public float OrbRadius = 32.0f;
    [Export] public float HoverHeight = 56.0f;
    [Export] public Color LightColor = new(1.0f, 0.9f, 0.7f);

    private Camera3D? _cam;
    private GodotObject? _sampler;
    private W3MapWorld? _world;
    private MeshInstance3D? _mesh;
    private OmniLight3D? _light;

    public void Setup(Camera3D cam, W3MapWorld? world)
    {
        _cam = cam;
        _world = world;
        _sampler = world?.sampler;
        if (_mesh == null)
            Build();
        SetProbeEnabled(ProbeEnabled);
    }

    public void RefreshWorld(W3MapWorld? world)
    {
        _world = world;
        _sampler = world?.sampler;
    }

    private void Build()
    {
        var sphere = new SphereMesh { Radius = OrbRadius, Height = OrbRadius * 2.0f, RadialSegments = 24, Rings = 12 };
        _mesh = new MeshInstance3D { Mesh = sphere, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.95f, 0.8f, 0.85f),
            EmissionEnabled = true,
            Emission = LightColor,
            EmissionEnergyMultiplier = 4.5f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _mesh.MaterialOverride = mat;
        AddChild(_mesh);

        _light = new OmniLight3D
        {
            LightColor = LightColor,
            LightEnergy = LightEnergy,
            OmniRange = LightRange,
            OmniAttenuation = 1.2f,
            ShadowEnabled = false,
            LightSpecular = 0.9f,
            LightIndirectEnergy = 1.0f,
        };
        AddChild(_light);
    }

    public void SetProbeEnabled(bool on)
    {
        ProbeEnabled = on;
        Visible = on;
        SetProcess(on);
        if (_light != null)
        {
            _light.Visible = on;
            _light.LightEnergy = on ? LightEnergy : 0.0f;
        }
        if (on)
            Input.MouseMode = Input.MouseModeEnum.Hidden;
        else
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            ClearTerrainProbe();
        }
    }

    public void Toggle() => SetProbeEnabled(!ProbeEnabled);

    private void ClearTerrainProbe()
    {
        if (_world?.terrain != null && _world.terrain.HasMethod("set_probe_light"))
            _world.terrain.Call("set_probe_light", false);
    }

    public override void _Process(double delta)
    {
        if (!ProbeEnabled || _cam == null)
            return;
        if (_sampler == null && _world != null)
            _sampler = _world.sampler;
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // 右键环顾中——让球停在相机前方，而不是追逐被锁住的光标。
            GlobalPosition = _cam.GlobalPosition - _cam.GlobalTransform.Basis.Z * 420.0f;
            GlobalPosition = new Vector3(GlobalPosition.X, SnapGroundY(GlobalPosition.X, GlobalPosition.Z) + HoverHeight, GlobalPosition.Z);
        }
        else
        {
            Vector3? hit = RayGround(GetViewport().GetMousePosition());
            if (hit.HasValue)
                GlobalPosition = hit.Value + new Vector3(0.0f, HoverHeight, 0.0f);
        }
        if (_world?.terrain != null && _world.terrain.HasMethod("set_probe_light"))
            _world.terrain.Call("set_probe_light", true, GlobalPosition, LightEnergy, LightRange, LightColor);
    }

    private float SnapGroundY(float wx, float wz)
    {
        if (_sampler != null && _sampler.HasMethod("sample_y"))
            return _sampler.Call("sample_y", wx, wz).AsSingle();
        return 0.0f;
    }

    private Vector3? RayGround(Vector2 screenPos)
    {
        if (_cam == null)
            return null;
        Vector3 origin = _cam.ProjectRayOrigin(screenPos);
        Vector3 dir = _cam.ProjectRayNormal(screenPos);
        if (dir.LengthSquared() < 0.0001f)
            return null;
        dir = dir.Normalized();
        // 步长较大——避免大地图上每帧都卡顿。
        float t = 80.0f;
        const float tMax = 12000.0f;
        const float step = 128.0f;
        bool prevAbove = true;
        while (t < tMax)
        {
            Vector3 p = origin + dir * t;
            float gy = SnapGroundY(p.X, p.Z);
            bool above = p.Y > gy;
            if (prevAbove && !above)
            {
                float lo = t - step;
                float hi = t;
                for (int i = 0; i < 6; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Vector3 pm = origin + dir * mid;
                    if (pm.Y > SnapGroundY(pm.X, pm.Z))
                        lo = mid;
                    else
                        hi = mid;
                }
                Vector3 grounded = origin + dir * hi;
                grounded.Y = SnapGroundY(grounded.X, grounded.Z);
                return grounded;
            }
            prevAbove = above;
            t += step;
        }
        Vector3 far = origin + dir * 2500.0f;
        far.Y = SnapGroundY(far.X, far.Z);
        return far;
    }
}
