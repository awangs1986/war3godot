using System.Collections.Generic;
using Diablo1Like.Runtime.Items;
using Godot;

namespace Diablo1Like.Runtime.Autoload;

/// <summary>
/// MVP 掉落/商店用的迷你物品目录。迁移自 scripts/autoload/item_db.gd，
/// Autoload 名称沿用 "ItemDB"（见 project.godot）。
/// </summary>
[GlobalClass]
public partial class ItemDatabase : Node
{
    private static readonly Color[] RarityColors =
    {
        new(0.85f, 0.85f, 0.85f),
        new(0.35f, 0.75f, 1.0f),
        new(0.75f, 0.35f, 1.0f),
    };

    // 静态字典而非在 _Ready 里构建：Autoload 加载顺序不保证 GameState 之后才轮到
    // ItemDatabase._Ready，模板数据必须在节点树 _ready 回调之前就可用（旧 GDS 用
    // const TEMPLATES 也是同样的"始终可用"语义）。
    private readonly Dictionary<string, ItemInstance> _templates = BuildTemplates();

    private static Dictionary<string, ItemInstance> BuildTemplates()
    {
        return new Dictionary<string, ItemInstance>
        {
            ["club"] = new ItemInstance { TemplateId = "club", Name = "Club", Slot = "weapon", Damage = 4, Sell = 8, Rarity = 0 },
            ["short_sword"] = new ItemInstance { TemplateId = "short_sword", Name = "Short Sword", Slot = "weapon", Damage = 8, Sell = 25, Rarity = 1 },
            ["war_axe"] = new ItemInstance { TemplateId = "war_axe", Name = "War Axe", Slot = "weapon", Damage = 14, Sell = 60, Rarity = 2 },
            ["ragged_cape"] = new ItemInstance { TemplateId = "ragged_cape", Name = "Ragged Cape", Slot = "armor", Armor = 1, Hp = 10, Sell = 15, Rarity = 0 },
            ["chain_vest"] = new ItemInstance { TemplateId = "chain_vest", Name = "Chain Vest", Slot = "armor", Armor = 3, Hp = 25, Sell = 45, Rarity = 1 },
            ["ring_of_vitality"] = new ItemInstance { TemplateId = "ring_of_vitality", Name = "Ring of Vitality", Slot = "ring", Hp = 20, Sell = 40, Rarity = 1 },
            ["gold_pile"] = new ItemInstance { TemplateId = "gold_pile", Name = "Gold", Slot = "", Gold = 15, Sell = 0, Rarity = 0 },
        };
    }

    public ItemInstance MakeItem(string templateId)
    {
        if (!_templates.TryGetValue(templateId, out ItemInstance? template))
            template = _templates["club"];
        ItemInstance item = template.Clone();
        item.Uid = $"{templateId}_{Time.GetTicksMsec() + (uint)GD.RandRange(0, 999)}";
        return item;
    }

    public ItemInstance RandomLootForDepth(int depth)
    {
        float roll = GD.Randf();
        if (roll < 0.35f)
        {
            ItemInstance gold = MakeItem("gold_pile");
            gold.Gold = 8 + depth * 6 + GD.RandRange(0, 11);
            return gold;
        }
        var table = new List<string> { "ragged_cape", "short_sword" };
        if (depth >= 1)
            table.AddRange(new[] { "chain_vest", "ring_of_vitality" });
        if (depth >= 2)
            table.Add("war_axe");
        return MakeItem(table[GD.RandRange(0, table.Count - 1)]);
    }

    public Color RarityColor(ItemInstance item)
    {
        int r = Mathf.Clamp(item.Rarity, 0, RarityColors.Length - 1);
        return RarityColors[r];
    }

    public string Describe(ItemInstance item)
    {
        var parts = new List<string> { item.Name.Length > 0 ? item.Name : "?" };
        if (item.Damage.HasValue)
            parts.Add($"+{item.Damage.Value} dmg");
        if (item.Armor.HasValue)
            parts.Add($"+{item.Armor.Value} arm");
        if (item.Hp.HasValue)
            parts.Add($"+{item.Hp.Value} hp");
        if (item.Gold.HasValue)
            parts.Add($"{item.Gold.Value} gold");
        return string.Join(" · ", parts);
    }
}
