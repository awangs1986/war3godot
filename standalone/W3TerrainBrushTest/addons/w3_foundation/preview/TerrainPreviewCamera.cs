using Godot;

namespace W3Foundation.Preview;

/// <summary>
/// Reusable editor-like fly camera for imported-map previews. It intentionally
/// contains no gameplay input or Diablo-specific behavior.
/// </summary>
[GlobalClass]
public partial class TerrainPreviewCamera : Camera3D
{
    [Export(PropertyHint.Range, "100,10000,10")]
    public float MoveSpeed { get; set; } = 1800.0f;

    [Export(PropertyHint.Range, "1,10,0.1")]
    public float FastMultiplier { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0.01,1,0.01")]
    public float MouseSensitivity { get; set; } = 0.14f;

    private bool _looking;

    public override void _Process(double delta)
    {
        Vector3 input = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) input -= Transform.Basis.Z;
        if (Input.IsPhysicalKeyPressed(Key.S)) input += Transform.Basis.Z;
        if (Input.IsPhysicalKeyPressed(Key.A)) input -= Transform.Basis.X;
        if (Input.IsPhysicalKeyPressed(Key.D)) input += Transform.Basis.X;
        if (Input.IsPhysicalKeyPressed(Key.Q) || Input.IsPhysicalKeyPressed(Key.Ctrl)) input -= Vector3.Up;
        if (Input.IsPhysicalKeyPressed(Key.E) || Input.IsPhysicalKeyPressed(Key.Space)) input += Vector3.Up;

        if (input.LengthSquared() > 0.0f)
        {
            float multiplier = Input.IsPhysicalKeyPressed(Key.Shift) ? FastMultiplier : 1.0f;
            GlobalPosition += input.Normalized() * MoveSpeed * multiplier * (float)delta;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Right)
            {
                _looking = button.Pressed;
                Input.MouseMode = _looking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                GetViewport().SetInputAsHandled();
            }
            else if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float direction = button.ButtonIndex == MouseButton.WheelUp ? -1.0f : 1.0f;
                GlobalPosition += -GlobalTransform.Basis.Z * direction * MoveSpeed * 0.60f;
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseMotion motion && _looking)
        {
            RotationDegrees = new Vector3(
                Mathf.Clamp(RotationDegrees.X - motion.Relative.Y * MouseSensitivity, -89.0f, 89.0f),
                RotationDegrees.Y - motion.Relative.X * MouseSensitivity,
                0.0f);
            GetViewport().SetInputAsHandled();
        }
    }

    public void FocusPoint(Vector3 worldPoint, float distance = 1800.0f)
    {
        GlobalPosition = worldPoint + new Vector3(0.0f, distance * 0.75f, distance);
        LookAt(worldPoint, Vector3.Up);
    }

    public void SetTopDown(Vector3 worldPoint, float height = 2200.0f)
    {
        Projection = ProjectionType.Orthogonal;
        Size = height;
        GlobalPosition = worldPoint + Vector3.Up * height;
        RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f);
    }
}
