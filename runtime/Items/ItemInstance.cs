using Godot;

namespace Diablo1Like.Runtime.Items;

/// <summary>
/// 强类型物品实例，替代旧 GDScript 的 Dictionary 物品模型
/// （scripts/autoload/item_db.gd / scripts/autoload/game_state.gd）。
/// 可选数值字段用 nullable：未设置时对应旧版 Dictionary 里"没有这个 key"。
/// </summary>
public sealed class ItemInstance
{
    public string TemplateId = "";
    public string Uid = "";
    public string Name = "";
    /// <summary>装备槽位："weapon" | "armor" | "ring" | ""（无槽位，例如金币堆）。</summary>
    public string Slot = "";
    public int? Damage;
    public int? Armor;
    public int? Hp;
    public int? Gold;
    public int Sell;
    public int Rarity;

    public ItemInstance Clone()
    {
        return new ItemInstance
        {
            TemplateId = TemplateId,
            Uid = Uid,
            Name = Name,
            Slot = Slot,
            Damage = Damage,
            Armor = Armor,
            Hp = Hp,
            Gold = Gold,
            Sell = Sell,
            Rarity = Rarity,
        };
    }
}
