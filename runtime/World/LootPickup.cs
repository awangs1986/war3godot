using Diablo1Like.Runtime.Autoload;
using Diablo1Like.Runtime.Items;
using Godot;

namespace Diablo1Like.Runtime.World;

/// <summary>
/// 地面掉落拾取：金币或装备，经 GameState / ItemDatabase 处理。
/// 迁移自 scripts/loot_pickup.gd。
/// </summary>
[GlobalClass]
public partial class LootPickup : Area3D
{
    public ItemInstance Item = new();

    private MeshInstance3D _mesh = null!;
    private Label3D _label = null!;
    private GameState _gameState = null!;
    private ItemDatabase _itemDb = null!;

    public override void _Ready()
    {
        _mesh = GetNode<MeshInstance3D>("Mesh");
        _label = GetNode<Label3D>("Label3D");
        _gameState = GetNode<GameState>("/root/GameState");
        _itemDb = GetNode<ItemDatabase>("/root/ItemDB");

        CollisionLayer = 8;
        CollisionMask = 2;
        BodyEntered += OnBodyEntered;
        Monitoring = true;
        Monitorable = true;
        RefreshVisual();
    }

    public void Setup(ItemInstance item)
    {
        Item = item;
        if (IsNodeReady())
            RefreshVisual();
    }

    private void RefreshVisual()
    {
        Color rarity = _itemDb.RarityColor(Item);
        var mat = new StandardMaterial3D
        {
            AlbedoColor = rarity,
            EmissionEnabled = true,
            Emission = rarity * 0.4f,
        };
        _mesh.MaterialOverride = mat;
        _label.Text = _itemDb.Describe(Item);
        _label.Modulate = rarity;
    }

    private void OnBodyEntered(Node body)
    {
        if (!body.IsInGroup("player"))
            return;
        if (Item.TemplateId == "gold_pile" || Item.Gold.HasValue)
        {
            _gameState.AddGold(Item.Gold ?? 10);
            QueueFree();
            return;
        }
        if (_gameState.TryAddItem(Item))
            QueueFree();
        // 背包已满——留在地上。
    }
}
