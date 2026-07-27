using Godot;

namespace Diablo1Like.Runtime.World;

/// <summary>
/// 走近 / 点击触发的交互物（传送门或商人）。迁移自 scripts/interactable.gd。
/// </summary>
[GlobalClass]
public partial class Interactable : StaticBody3D
{
    [Signal]
    public delegate void ActivatedEventHandler();

    [Signal]
    public delegate void OpenedEventHandler();

    /// <summary>portal_dungeon | portal_town | merchant</summary>
    [Export] public string Kind = "portal_dungeon";
    [Export] public string Prompt = "Enter";

    private Label3D _label = null!;
    private Area3D _area = null!;

    public override void _Ready()
    {
        _label = GetNode<Label3D>("Label3D");
        _area = GetNode<Area3D>("Area3D");

        AddToGroup("interact");
        AddToGroup(Kind);
        CollisionLayer = 16;
        CollisionMask = 0;
        _label.Text = Prompt;
        _area.BodyEntered += OnBody;
        _area.CollisionLayer = 0;
        _area.CollisionMask = 2;
    }

    public void Activate(Node? player)
    {
        if (Kind == "merchant")
            EmitSignal(SignalName.Opened);
        else
            EmitSignal(SignalName.Activated);
    }

    private void OnBody(Node body)
    {
        if (body.IsInGroup("player") && Input.IsActionPressed("interact"))
            Activate(body);
    }

    public override void _PhysicsProcess(double delta)
    {
        // 交互物附近按 E。
        var bodies = _area.GetOverlappingBodies();
        if (bodies.Count == 0 || !Input.IsActionJustPressed("interact"))
            return;
        foreach (Node b in bodies)
        {
            if (b.IsInGroup("player"))
            {
                Activate(b);
                return;
            }
        }
    }
}
