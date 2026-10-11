using System.Collections.Generic;
using Diablo1Like.Runtime.Items;
using Godot;

namespace Diablo1Like.Runtime.Autoload;

/// <summary>
/// 城镇/地牢间持续存在的存档状态。迁移自 scripts/autoload/game_state.gd。
/// 背包/装备模型改用强类型 <see cref="ItemInstance"/>（原版是裸 Dictionary）。
/// </summary>
[GlobalClass]
public partial class GameState : Node
{
    [Signal]
    public delegate void GoldChangedEventHandler(int amount);

    [Signal]
    public delegate void InventoryChangedEventHandler();

    [Signal]
    public delegate void EquipmentChangedEventHandler();

    [Signal]
    public delegate void PlayerStatsChangedEventHandler();

    public const int MaxBag = 24;
    public static readonly string[] EquipSlots = { "weapon", "armor", "ring" };

    public int Gold = 100;
    public readonly List<ItemInstance> Bag = new();
    public readonly Dictionary<string, ItemInstance> Equipped = new();
    public int MaxHp = 100;
    public int Hp = 100;
    public int BaseDamage = 8;
    public int DungeonDepthReached;

    private bool _started;
    private ItemDatabase? _itemDb;

    private ItemDatabase ItemDb => _itemDb ??= GetNode<ItemDatabase>("/root/ItemDB");

    public override void _Ready()
    {
        if (_started)
            return;
        _started = true;
        ResetNewGame();
    }

    public void ResetNewGame()
    {
        Gold = 100;
        Bag.Clear();
        Equipped.Clear();
        MaxHp = 100;
        Hp = 100;
        BaseDamage = 8;
        DungeonDepthReached = 0;
        ItemInstance club = ItemDb.MakeItem("club");
        EquipInternal(club);
        EmitSignal(SignalName.GoldChanged, Gold);
        EmitSignal(SignalName.InventoryChanged);
        EmitSignal(SignalName.EquipmentChanged);
        EmitSignal(SignalName.PlayerStatsChanged);
    }

    public void AddGold(int amount)
    {
        Gold = Mathf.Max(0, Gold + amount);
        EmitSignal(SignalName.GoldChanged, Gold);
    }

    public bool TryAddItem(ItemInstance item)
    {
        if (Bag.Count >= MaxBag)
            return false;
        Bag.Add(item);
        EmitSignal(SignalName.InventoryChanged);
        // 空槽位自动装备（hearthkeep 风格的便利设计）。
        if (item.Slot != "" && !Equipped.ContainsKey(item.Slot))
            EquipFromBag(item);
        return true;
    }

    public void EquipFromBag(ItemInstance item)
    {
        if (!Bag.Contains(item))
            return;
        EquipInternal(item);
        Bag.Remove(item);
        EmitSignal(SignalName.InventoryChanged);
        EmitSignal(SignalName.EquipmentChanged);
        EmitSignal(SignalName.PlayerStatsChanged);
    }

    public void Unequip(string slot)
    {
        if (!Equipped.TryGetValue(slot, out ItemInstance? item))
            return;
        if (Bag.Count >= MaxBag)
            return;
        Bag.Add(item);
        Equipped.Remove(slot);
        EmitSignal(SignalName.InventoryChanged);
        EmitSignal(SignalName.EquipmentChanged);
        EmitSignal(SignalName.PlayerStatsChanged);
    }

    public int SellItem(ItemInstance item)
    {
        if (!Bag.Contains(item))
            return 0;
        int value = item.Sell;
        Bag.Remove(item);
        AddGold(value);
        EmitSignal(SignalName.InventoryChanged);
        return value;
    }

    public int GetAttackDamage()
    {
        int dmg = BaseDamage;
        if (Equipped.TryGetValue("weapon", out ItemInstance? weapon))
            dmg += weapon.Damage ?? 0;
        return dmg;
    }

    public int GetArmor() => Equipped.TryGetValue("armor", out ItemInstance? armor) ? armor.Armor ?? 0 : 0;

    public int GetBonusHp()
    {
        int bonus = 0;
        if (Equipped.TryGetValue("armor", out ItemInstance? armor))
            bonus += armor.Hp ?? 0;
        if (Equipped.TryGetValue("ring", out ItemInstance? ring))
            bonus += ring.Hp ?? 0;
        return bonus;
    }

    public void RefreshMaxHp()
    {
        int newMax = 100 + GetBonusHp();
        float ratio = MaxHp <= 0 ? 1.0f : (float)Hp / MaxHp;
        MaxHp = newMax;
        Hp = Mathf.Clamp(Mathf.RoundToInt(MaxHp * ratio), 1, MaxHp);
        EmitSignal(SignalName.PlayerStatsChanged);
    }

    public void ApplyDamage(int amount)
    {
        int reduced = Mathf.Max(1, amount - GetArmor());
        Hp = Mathf.Max(0, Hp - reduced);
        EmitSignal(SignalName.PlayerStatsChanged);
    }

    public void HealFull()
    {
        RefreshMaxHp();
        Hp = MaxHp;
        EmitSignal(SignalName.PlayerStatsChanged);
    }

    private void EquipInternal(ItemInstance item)
    {
        string slot = item.Slot;
        if (slot == "" || System.Array.IndexOf(EquipSlots, slot) < 0)
            return;
        if (Equipped.TryGetValue(slot, out ItemInstance? previous))
            Bag.Add(previous);
        Equipped[slot] = item;
        RefreshMaxHp();
    }
}
