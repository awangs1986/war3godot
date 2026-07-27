using Diablo1Like.Runtime.Autoload;
using Diablo1Like.Runtime.Camera;
using Diablo1Like.Runtime.Enemy;
using Diablo1Like.Runtime.Items;
using Diablo1Like.Runtime.Player;
using Diablo1Like.Runtime.UI;
using Godot;

namespace Diablo1Like.Runtime.World;

/// <summary>
/// 关卡共享逻辑：点击射线、HUD 挂接、传送门、敌人掉落。
/// 迁移自 scripts/level_root.gd。
/// </summary>
[GlobalClass]
public partial class LevelController : Node3D
{
    [Export] public bool IsDungeon;
    [Export] public int DungeonDepth = 1;

    private PlayerController _player = null!;
    private IsometricCameraRig _cameraRig = null!;
    private HudController _hud = null!;
    private GameState _gameState = null!;
    private ItemDatabase _itemDb = null!;

    private static readonly PackedScene LootPickupScene = GD.Load<PackedScene>("res://scenes/loot_pickup.tscn");

    public override void _Ready()
    {
        _player = GetNode<PlayerController>("Player");
        _cameraRig = GetNode<IsometricCameraRig>("IsoCamera");
        _hud = GetNode<HudController>("HUD");
        _gameState = GetNode<GameState>("/root/GameState");
        _itemDb = GetNode<ItemDatabase>("/root/ItemDB");

        RenderingServer.SetDefaultClearColor(new Color(0.18f, 0.16f, 0.22f));
        _cameraRig.FollowTarget = _player;
        _cameraRig.SnapToTarget();
        Camera3D cam = _cameraRig.GetCamera();
        cam?.MakeCurrent();

        _player.Died += OnPlayerDied;
        foreach (Node e in GetTree().GetNodesInGroup("enemy"))
        {
            if (e is EnemyController enemy)
                enemy.Died += OnEnemyDied;
        }
        if (IsDungeon)
            _gameState.DungeonDepthReached = Mathf.Max(_gameState.DungeonDepthReached, DungeonDepth);
        foreach (Node n in GetTree().GetNodesInGroup("portal_dungeon"))
        {
            if (n is Interactable portal)
                portal.Activated += GoDungeon;
        }
        foreach (Node n in GetTree().GetNodesInGroup("portal_town"))
        {
            if (n is Interactable portal)
                portal.Activated += GoTown;
        }
        foreach (Node n in GetTree().GetNodesInGroup("merchant"))
        {
            if (n is Interactable merchant)
                merchant.Opened += _hud.ShowMerchant;
        }

        GD.Print($"Level ready: {Name} dungeon={IsDungeon} player={_player != null} cam_current={(cam != null && cam.Current)}");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_inventory"))
        {
            _hud.ToggleInventory();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            if (_hud.IsBlockingClicks())
                return;
            HandleClick(GetViewport().GetMousePosition());
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandleClick(Vector2 screenPos)
    {
        Camera3D? cam = _cameraRig.GetCamera();
        if (cam == null || _player == null)
            return;

        Vector3 from = cam.ProjectRayOrigin(screenPos);
        Vector3 dir = cam.ProjectRayNormal(screenPos);
        Vector3? planePoint = RayToGround(from, dir);

        var space = GetWorld3D().DirectSpaceState;
        var q = PhysicsRayQueryParameters3D.Create(from, from + dir * 800.0f);
        q.CollideWithAreas = false;
        q.CollideWithBodies = true;
        q.CollisionMask = 1 | 4 | 16;
        Godot.Collections.Dictionary hit = space.IntersectRay(q);

        if (hit.Count > 0)
        {
            Node? cur = hit["collider"].AsGodotObject() as Node;
            while (cur != null)
            {
                if (cur.IsInGroup("enemy"))
                {
                    _player.HandleWorldClick((Vector3)hit["position"], cur);
                    return;
                }
                if (cur is Interactable interactable)
                {
                    interactable.Activate(_player);
                    return;
                }
                cur = cur.GetParent();
            }
        }

        if (planePoint.HasValue)
        {
            _player.HandleWorldClick(planePoint.Value, null);
        }
        else if (hit.Count > 0)
        {
            var p = (Vector3)hit["position"];
            p.Y = _player.GlobalPosition.Y;
            _player.HandleWorldClick(p, null);
        }
    }

    private static Vector3? RayToGround(Vector3 from, Vector3 dir)
    {
        if (Mathf.Abs(dir.Y) < 0.0001f)
            return null;
        float t = -from.Y / dir.Y;
        if (t < 0.0f)
            return null;
        return from + dir * t;
    }

    private void OnEnemyDied(Node3D enemy)
    {
        ItemInstance loot = _itemDb.RandomLootForDepth(IsDungeon ? DungeonDepth : 0);
        var drop = (LootPickup)LootPickupScene.Instantiate();
        AddChild(drop);
        drop.GlobalPosition = enemy.GlobalPosition + new Vector3(0, 0.4f, 0);
        drop.Setup(loot);
    }

    private async void OnPlayerDied()
    {
        int loss = Mathf.Min(_gameState.Gold, 25);
        _gameState.AddGold(-loss);
        _gameState.HealFull();
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        GoTown();
    }

    private void GoDungeon()
    {
        // 延迟切换——在输入/物理回调中间切场景不安全，会导致视口黑屏。
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/dungeon.tscn");
    }

    private void GoTown()
    {
        _gameState.HealFull();
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/town.tscn");
    }
}
