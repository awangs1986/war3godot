using Diablo1Like.Runtime.Autoload;
using Diablo1Like.Runtime.Items;
using Godot;

namespace Diablo1Like.Runtime.UI;

/// <summary>
/// HUD + 列表式背包 + 商人出售面板（方案 B：列表而非格子）。
/// 迁移自 scripts/ui/hud.gd。
/// </summary>
[GlobalClass]
public partial class HudController : CanvasLayer
{
    private ProgressBar _hp = null!;
    private Label _hpLabel = null!;
    private Label _gold = null!;
    private Label _hint = null!;
    private PanelContainer _invPanel = null!;
    private VBoxContainer _invList = null!;
    private Label _eqLabel = null!;
    private PanelContainer _merchantPanel = null!;
    private VBoxContainer _merchantList = null!;
    private Button _closeInv = null!;

    private GameState _gameState = null!;
    private ItemDatabase _itemDb = null!;

    public override void _Ready()
    {
        _hp = GetNode<ProgressBar>("Root/TopLeft/HPBar");
        _hpLabel = GetNode<Label>("Root/TopLeft/HPLabel");
        _gold = GetNode<Label>("Root/TopLeft/GoldLabel");
        _hint = GetNode<Label>("Root/Bottom/Hint");
        _invPanel = GetNode<PanelContainer>("Root/InventoryPanel");
        _invList = GetNode<VBoxContainer>("Root/InventoryPanel/Margin/VBox/Scroll/ItemList");
        _eqLabel = GetNode<Label>("Root/InventoryPanel/Margin/VBox/EquipLabel");
        _merchantPanel = GetNode<PanelContainer>("Root/MerchantPanel");
        _merchantList = GetNode<VBoxContainer>("Root/MerchantPanel/Margin/VBox/Scroll/SellList");
        _closeInv = GetNode<Button>("Root/InventoryPanel/Margin/VBox/CloseInv");
        _gameState = GetNode<GameState>("/root/GameState");
        _itemDb = GetNode<ItemDatabase>("/root/ItemDB");

        _invPanel.Visible = false;
        _merchantPanel.Visible = false;
        _closeInv.Pressed += OnCloseInvPressed;
        _gameState.GoldChanged += OnGold;
        _gameState.PlayerStatsChanged += RefreshStats;
        _gameState.InventoryChanged += RebuildInv;
        _gameState.EquipmentChanged += RebuildInv;
        OnGold(_gameState.Gold);
        RefreshStats();
        RebuildInv();
        _hint.Text = "LMB click-move · WASD (iso) · E interact · I inventory";
        _hpLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _gold.AddThemeColorOverride("font_color", new Color(1, 0.9f, 0.4f));
        _hint.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        // 大横幅方便确认场景已加载（即便 3D 渲染有问题）。
        if (!HasNode("Root/Banner"))
        {
            var banner = new Label
            {
                Name = "Banner",
                Text = "  SCENE LOADED  ",
                Position = new Vector2(16, 130),
            };
            banner.AddThemeFontSizeOverride("font_size", 28);
            banner.AddThemeColorOverride("font_color", new Color(1, 0.85f, 0.2f));
            GetNode("Root").AddChild(banner);
        }
        Visible = true;
        Layer = 100;
    }

    public bool IsBlockingClicks() => _invPanel.Visible || _merchantPanel.Visible;

    public void ToggleInventory()
    {
        _merchantPanel.Visible = false;
        _invPanel.Visible = !_invPanel.Visible;
        if (_invPanel.Visible)
            RebuildInv();
    }

    public void ShowMerchant()
    {
        _invPanel.Visible = false;
        _merchantPanel.Visible = true;
        RebuildMerchant();
    }

    private void OnGold(int amount) => _gold.Text = $"Gold: {amount}";

    private void RefreshStats()
    {
        _hp.MaxValue = _gameState.MaxHp;
        _hp.Value = _gameState.Hp;
        _hpLabel.Text = $"HP {_gameState.Hp} / {_gameState.MaxHp}  ·  ATK {_gameState.GetAttackDamage()}  ·  Arm {_gameState.GetArmor()}";
    }

    private void RebuildInv()
    {
        foreach (Node c in _invList.GetChildren())
            c.QueueFree();
        var eqParts = new System.Collections.Generic.List<string>();
        foreach (string slot in GameState.EquipSlots)
        {
            eqParts.Add(_gameState.Equipped.TryGetValue(slot, out ItemInstance? item)
                ? $"{slot}: {item.Name}"
                : $"{slot}: —");
        }
        _eqLabel.Text = string.Join("  |  ", eqParts);

        foreach (ItemInstance item in _gameState.Bag)
            _invList.AddChild(MakeItemRow(item, true));
    }

    private void RebuildMerchant()
    {
        foreach (Node c in _merchantList.GetChildren())
            c.QueueFree();
        var title = new Label { Text = "Griswold-ish: sell bag items (click Sell)" };
        _merchantList.AddChild(title);
        foreach (ItemInstance item in _gameState.Bag)
        {
            var row = new HBoxContainer();
            var lab = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Text = $"{_itemDb.Describe(item)}  ({item.Sell}g)",
                Modulate = _itemDb.RarityColor(item),
            };
            var btn = new Button { Text = "Sell" };
            btn.Pressed += () => Sell(item);
            row.AddChild(lab);
            row.AddChild(btn);
            _merchantList.AddChild(row);
        }
        var close = new Button { Text = "Close" };
        close.Pressed += () => _merchantPanel.Visible = false;
        _merchantList.AddChild(close);
    }

    private Control MakeItemRow(ItemInstance item, bool allowEquip)
    {
        var row = new HBoxContainer();
        var lab = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Text = _itemDb.Describe(item),
            Modulate = _itemDb.RarityColor(item),
        };
        row.AddChild(lab);
        if (allowEquip && item.Slot != "")
        {
            var eq = new Button { Text = "Equip" };
            eq.Pressed += () =>
            {
                _gameState.EquipFromBag(item);
                RebuildInv();
            };
            row.AddChild(eq);
        }
        return row;
    }

    private void Sell(ItemInstance item)
    {
        _gameState.SellItem(item);
        RebuildMerchant();
        RebuildInv();
    }

    private void OnCloseInvPressed() => _invPanel.Visible = false;
}
