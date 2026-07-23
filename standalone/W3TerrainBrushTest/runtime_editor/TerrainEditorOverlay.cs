using Godot;
using W3Foundation.Editor;

namespace W3Foundation.RuntimeEditor;

/// <summary>
/// Full-window 2D overlay that renders the shared brush cursor (circle, crosshair
/// and hovered-tile outline) in the runtime editor. Mouse events pass through.
/// </summary>
public partial class TerrainEditorOverlay : Control
{
    private TerrainNative3DBrush? _brush;

    public TerrainEditorOverlay()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Configure(TerrainNative3DBrush brush)
    {
        _brush = brush;
    }

    public override void _Process(double delta)
    {
        // The camera can move every frame (WASD/RMB look), so the projected
        // brush shape is refreshed every frame as well.
        QueueRedraw();
    }

    public override void _Draw()
    {
        _brush?.Draw(this);
    }
}
