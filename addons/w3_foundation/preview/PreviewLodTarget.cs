using Godot;

namespace W3Foundation.Preview;

/// <summary>
/// Adapter used by the foundation preview scheduler. Imported units, doodads,
/// and future model players can subclass it without depending on gameplay code.
/// </summary>
[GlobalClass]
public partial class PreviewLodTarget : Node3D
{
    public const string PreviewGroup = "w3_foundation_preview_lod_target";

    [Signal]
    public delegate void AnimationActivityChangedEventHandler(bool active);

    [Export] public NodePath VisualRootPath { get; set; } = new();
    [Export] public bool SupportsAnimation { get; set; } = true;
    [Export] public float BoundsRadius { get; set; } = 64.0f;

    public bool AnimationActive { get; private set; }
    public bool PreviewVisible { get; private set; } = true;

    public override void _EnterTree()
    {
        AddToGroup(PreviewGroup);
    }

    public void SetPreviewVisible(bool visible)
    {
        PreviewVisible = visible;
        ResolveVisualRoot().Visible = visible;
    }

    public virtual void SetAnimationActive(bool active)
    {
        if (!SupportsAnimation || AnimationActive == active)
            return;
        AnimationActive = active;
        EmitSignal(SignalName.AnimationActivityChanged, active);

        Node3D visual = ResolveVisualRoot();
        if (visual.HasMethod("set_preview_animation_active"))
            visual.Call("set_preview_animation_active", active);
    }

    private Node3D ResolveVisualRoot()
    {
        if (!VisualRootPath.IsEmpty && GetNodeOrNull<Node3D>(VisualRootPath) is { } visual)
            return visual;
        return this;
    }
}
