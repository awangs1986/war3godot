using Godot;

namespace Diablo1Like.Runtime.Camera;

/// <summary>
/// Diablo 风格的正交等距跟随相机：Pivot 跟随玩家，子节点 Camera3D 保持固定局部等距变换。
/// 迁移自 scripts/camera_iso.gd。
/// </summary>
[GlobalClass]
public partial class IsometricCameraRig : Node3D
{
    [Export] public Node3D? FollowTarget;
    [Export] public float FollowSpeed = 12.0f;
    [Export] public float OrthoSize = 22.0f;
    /// <summary>沿等距视线方向、距 pivot 的距离。</summary>
    [Export] public float Distance = 32.0f;
    /// <summary>为 false 时不抢占 Camera3D.Current（例如剑圣 Warsmash 相机占用视角时）。</summary>
    [Export] public bool OwnCurrent = true;

    private Camera3D _camera = null!;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        ConfigureCamera();
        if (FollowTarget != null && IsInstanceValid(FollowTarget))
            GlobalPosition = FollowTarget.GlobalPosition;
    }

    public void SnapToTarget()
    {
        if (FollowTarget != null && IsInstanceValid(FollowTarget))
            GlobalPosition = FollowTarget.GlobalPosition;
        ConfigureCamera();
    }

    public void SetOwnCurrent(bool on)
    {
        OwnCurrent = on;
        if (!on && _camera != null)
            _camera.Current = false;
    }

    private void ConfigureCamera()
    {
        // 经典 D1 / WC3 风格：yaw 45°，pitch ≈35°（atan(1/sqrt(2))）。
        float yaw = Mathf.DegToRad(45.0f);
        float pitch = Mathf.DegToRad(-35.264f);
        var offset = new Vector3(
            Distance * Mathf.Cos(pitch) * Mathf.Sin(yaw),
            Distance * -Mathf.Sin(pitch),
            Distance * Mathf.Cos(pitch) * Mathf.Cos(yaw));
        _camera.Position = offset;
        // 瞄准 pivot（局部原点）。只设置一次——不要每帧 look_at。
        _camera.Basis = Basis.LookingAt(-offset, Vector3.Up);
        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.Size = OrthoSize;
        _camera.Near = Distance < 200.0f ? 0.05f : 1.0f;
        // War3 地图尺度大，需要更深的远裁剪面。
        _camera.Far = Mathf.Max(1000.0f, Distance * 8.0f + OrthoSize * 2.0f);
        if (OwnCurrent)
            _camera.MakeCurrent();
    }

    public override void _Process(double delta)
    {
        if (FollowTarget != null && IsInstanceValid(FollowTarget))
        {
            GlobalPosition = GlobalPosition.Lerp(
                FollowTarget.GlobalPosition,
                Mathf.Clamp(FollowSpeed * (float)delta, 0.0f, 1.0f));
        }
        if (OwnCurrent && _camera != null && !_camera.IsCurrent())
            _camera.MakeCurrent();
    }

    public Camera3D GetCamera() => _camera;

    /// <summary>该等距相机下"屏幕上/屏幕右"对应的世界空间轴。</summary>
    public Vector3 GetScreenForward() => new Vector3(-1.0f, 0.0f, -1.0f).Normalized();

    public Vector3 GetScreenRight() => new Vector3(1.0f, 0.0f, -1.0f).Normalized();
}
