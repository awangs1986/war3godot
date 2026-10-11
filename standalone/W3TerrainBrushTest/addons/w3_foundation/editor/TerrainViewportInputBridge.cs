using Godot;

namespace W3Foundation.Editor;

/// <summary>
/// Deterministic input bridge from Godot's editor UI into the isolated terrain
/// SubViewport. Editor main-screen controls do not share the game's root viewport
/// input path, so relying on implicit propagation is not sufficient here.
/// </summary>
[Tool]
public partial class TerrainViewportInputBridge : SubViewportContainer
{
    private bool _mouseInside;

    public SubViewport? TargetViewport { get; set; }

    public override bool _PropagateInputEvent(InputEvent @event)
    {
        // _GuiInput forwards exactly once with viewport-local coordinates.
        return false;
    }

    public override void _GuiInput(InputEvent @event)
    {
        ForwardToTerrainViewport(@event);
    }

    public void ForwardToTerrainViewport(InputEvent @event)
    {
        if (TargetViewport == null || !IsInstanceValid(TargetViewport))
            return;

        if (@event is InputEventMouseButton button && button.Pressed)
            GrabFocus();
        if (!_mouseInside)
        {
            TargetViewport.NotifyMouseEntered();
            _mouseInside = true;
        }
        TargetViewport.PushInput(@event, true);
        AcceptEvent();
    }

    public void NotifyTerrainMouseExited()
    {
        if (!_mouseInside || TargetViewport == null || !IsInstanceValid(TargetViewport))
            return;
        TargetViewport.NotifyMouseExited();
        _mouseInside = false;
    }
}
