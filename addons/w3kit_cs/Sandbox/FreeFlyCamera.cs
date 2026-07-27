using Godot;

namespace Diablo1Like.W3.Sandbox;

/// <summary>
/// 自由飞行预览相机：WASD 移动，QE 升降，右键环顾，滚轮缩放。
/// 迁移自 scripts/w3/free_fly_camera.gd。
/// </summary>
[GlobalClass]
public partial class FreeFlyCamera : Camera3D
{
    [Export] public float MoveSpeed = 600.0f;
    [Export] public float LookSensitivity = 0.0025f;
    [Export] public float MinPitch = -1.4f;
    [Export] public float MaxPitch = 1.4f;

    // Warcraft III 默认俯角约为 56 度。
    private static readonly float DefaultW3Pitch = Mathf.DegToRad(-56.0f);
    private const float WheelZoomFactor = 1.12f;
    private const float WheelZoomSpeedMultiplier = 2.0f;

    private float _yaw;
    private float _pitch = DefaultW3Pitch;
    private bool _looking;
    private Vector2 _lookAnchor = Vector2.Zero;
    // 下列成员被 tools/*.gd 通过 GDScript 动态调用，必须保持 snake_case
    // （与 W3MapWorld.sampler / batcher / is_loaded() 同一套跨语言约定）。
    /// <summary>为 true 时冻结 WASD / 环顾（沙盒控制台打开中）。</summary>
    public bool input_blocked;
    /// <summary>内嵌子窗口模式（外部编辑器嫁接）：WS_CHILD 窗口永远不会获得 Win32
    /// 激活，MOUSE_MODE_CAPTURED 会静默失效，因此改用隐藏光标 + 回锚点的方式模拟捕获。</summary>
    public bool emulate_capture;
    /// <summary>顶视角模式状态（focus_tile_topdown 进入，focus_point 退出）。</summary>
    private bool _topdownActive;
    private Vector3 _topdownCenter = Vector3.Zero;

    public override void _Ready()
    {
        Projection = ProjectionType.Perspective;
        Fov = 55.0f;
        Near = 1.0f;
        Far = 11000.0f;
        Current = true;
        ApplyRot();
    }

    public void focus_point(Vector3 center, float extent = 2000.0f)
    {
        _topdownActive = false;
        Projection = ProjectionType.Perspective;
        Fov = 55.0f;
        Near = 1.0f;
        Far = 11000.0f;
        GlobalPosition = center + new Vector3(extent * 0.45f, extent * 0.55f, extent * 0.45f);
        AimAt(center);
    }

    /// <summary>俯视某个 tile 的正交视角：屏幕上=地图 +Y（北），屏幕右=地图 +X（东）。
    /// 手写 basis——look_at(center, +Z) 会镜像 X 轴（东西颠倒），导致 TL 落到右上角。</summary>
    public void focus_tile_topdown(Vector3 center, float orthoSize = 380.0f)
    {
        _topdownActive = true;
        _topdownCenter = center;
        Projection = ProjectionType.Orthogonal;
        Size = orthoSize;
        Near = 1.0f;
        Far = 20000.0f;
        GlobalPosition = center + new Vector3(0.0f, 2500.0f, 0.0f);
        // 相机坐标轴：+X 东，+Y 北（+Z 世界），-Z 朝下看（-Y 世界）。
        // 不要从这个 basis 同步 _yaw/_pitch——天顶欧拉角会发生万向节死锁，
        // 污染下一次切回透视时的视角（白天空/视角损坏）。
        GlobalTransform = new Transform3D(
            new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0)),
            GlobalTransform.Origin);
    }

    public void release_look()
    {
        _looking = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (input_blocked)
            return;
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _looking = mb.Pressed;
                if (_looking)
                {
                    if (emulate_capture)
                    {
                        _lookAnchor = GetViewport().GetMousePosition();
                        Input.MouseMode = Input.MouseModeEnum.Hidden;
                    }
                    else
                    {
                        Input.MouseMode = Input.MouseModeEnum.Captured;
                    }
                }
                else
                {
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                }
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                Zoom(1.0f);
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                Zoom(-1.0f);
            }
        }
        else if (@event is InputEventMouseMotion mm && _looking)
        {
            Vector2 delta = mm.Relative;
            if (emulate_capture)
            {
                // warp_mouse 会产生一个相对锚点的合成移动事件，若继续用 relative 会
                // 反向抵消旋转；改为对比锚点位置。
                delta = mm.Position - _lookAnchor;
                if (delta == Vector2.Zero)
                    return;
                GetViewport().WarpMouse(_lookAnchor);
            }
            EnsurePerspective();
            _yaw -= delta.X * LookSensitivity;
            _pitch -= delta.Y * LookSensitivity;
            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
            ApplyRot();
        }
    }

    /// <summary>正向缩放朝视点靠近。透视/正交两种投影下滚轮手感保持一致。</summary>
    private void Zoom(float sign)
    {
        if (Projection == ProjectionType.Orthogonal)
        {
            float wheelFactor = Mathf.Pow(WheelZoomFactor, WheelZoomSpeedMultiplier);
            Size = Mathf.Clamp(Size * (sign > 0.0f ? 1.0f / wheelFactor : wheelFactor), 80.0f, 4000.0f);
        }
        else
        {
            Vector3 dir = -GlobalTransform.Basis.Z;
            GlobalPosition += dir * sign * MoveSpeed * 0.08f * WheelZoomSpeedMultiplier;
        }
    }

    public override void _Process(double delta)
    {
        if (!Current || input_blocked)
            return;
        Vector3 wish = Vector3.Zero;
        var basisYaw = new Basis(Vector3.Up, _yaw);
        Vector3 forward = -basisYaw.Z;
        Vector3 right = basisYaw.X;
        if (Input.IsKeyPressed(Key.W))
            wish += forward;
        if (Input.IsKeyPressed(Key.S))
            wish -= forward;
        if (Input.IsKeyPressed(Key.D))
            wish += right;
        if (Input.IsKeyPressed(Key.A))
            wish -= right;
        if (Input.IsKeyPressed(Key.E) || Input.IsKeyPressed(Key.Space))
            wish += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q) || Input.IsKeyPressed(Key.Ctrl))
            wish -= Vector3.Up;
        if (wish.Length() > 0.001f)
        {
            EnsurePerspective();
            GlobalPosition += wish.Normalized() * MoveSpeed * (float)delta;
        }
    }

    private void EnsurePerspective()
    {
        if (Projection != ProjectionType.Orthogonal && !_topdownActive)
            return;
        Projection = ProjectionType.Perspective;
        Fov = 55.0f;
        Near = 1.0f;
        Far = 11000.0f;
        if (_topdownActive)
        {
            _topdownActive = false;
            // 稳定的退出视角：不用 look_at+get_euler（丢弃 roll 会导致地面朝天）。
            const float extent = 900.0f;
            GlobalPosition = _topdownCenter + new Vector3(extent * 0.45f, extent * 0.65f, extent * 0.45f);
            AimAt(_topdownCenter);
        }
        else
        {
            ApplyRot();
        }
    }

    /// <summary>把自由飞行的 yaw/pitch 同步到看向目标。匹配 rotation = (pitch, yaw, 0)。</summary>
    private void AimAt(Vector3 target)
    {
        Vector3 dir = target - GlobalPosition;
        if (dir.LengthSquared() < 0.0001f)
        {
            ApplyRot();
            return;
        }
        dir = dir.Normalized();
        // 默认朝向是 -Z；负 pitch 表示向下看（默认 _pitch=-0.55）。
        _yaw = Mathf.Atan2(-dir.X, -dir.Z);
        _pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(dir.Y, -1.0f, 1.0f)), MinPitch, MaxPitch);
        ApplyRot();
    }

    private void ApplyRot()
    {
        Rotation = new Vector3(_pitch, _yaw, 0.0f);
    }
}
