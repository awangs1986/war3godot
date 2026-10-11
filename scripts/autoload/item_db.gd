extends Node
## Tiny item catalog for MVP loot / shop.

const TEMPLATES := {
	"club": {
		"id": "club",
		"name": "Club",
		"slot": "weapon",
		"damage": 4,
		"sell": 8,
		"rarity": 0,
	},
	"short_sword": {
		"id": "short_sword",
		"name": "Short Sword",
		"slot": "weapon",
		"damage": 8,
		"sell": 25,
		"rarity": 1,
	},
	"war_axe": {
		"id": "war_axe",
		"name": "War Axe",
		"slot": "weapon",
		"damage": 14,
		"sell": 60,
		"rarity": 2,
	},
	"ragged_cape": {
		"id": "ragged_cape",
		"name": "Ragged Cape",
		"slot": "armor",
		"armor": 1,
		"hp": 10,
		"sell": 15,
		"rarity": 0,
	},
	"chain_vest": {
		"id": "chain_vest",
		"name": "Chain Vest",
		"slot": "armor",
		"armor": 3,
		"hp": 25,
		"sell": 45,
		"rarity": 1,
	},
	"ring_of_vitality": {
		"id": "ring_of_vitality",
		"name": "Ring of Vitality",
		"slot": "ring",
		"hp": 20,
		"sell": 40,
		"rarity": 1,
	},
	"gold_pile": {
		"id": "gold_pile",
		"name": "Gold",
		"slot": "",
		"gold": 15,
		"sell": 0,
		"rarity": 0,
	},
}

const RARITY_COLOR := [
	Color(0.85, 0.85, 0.85),
	Color(0.35, 0.75, 1.0),
	Color(0.75, 0.35, 1.0),
]


func make_item(template_id: String) -> Dictionary:
	var t: Dictionary = TEMPLATES.get(template_id, TEMPLATES["club"]).duplicate(true)
	t["uid"] = "%s_%d" % [template_id, Time.get_ticks_msec() + randi() % 1000]
	return t


func random_loot_for_depth(depth: int) -> Dictionary:
	var roll := randf()
	if roll < 0.35:
		var g := make_item("gold_pile")
		g["gold"] = 8 + depth * 6 + randi() % 12
		return g
	var table: Array[String] = ["ragged_cape", "short_sword"]
	if depth >= 1:
		table.append_array(["chain_vest", "ring_of_vitality"])
	if depth >= 2:
		table.append("war_axe")
	return make_item(table[randi() % table.size()])


func rarity_color(item: Dictionary) -> Color:
	var r: int = clampi(int(item.get("rarity", 0)), 0, RARITY_COLOR.size() - 1)
	return RARITY_COLOR[r]


func describe(item: Dictionary) -> String:
	var parts: PackedStringArray = [str(item.get("name", "?"))]
	if item.has("damage"):
		parts.append("+%d dmg" % int(item["damage"]))
	if item.has("armor"):
		parts.append("+%d arm" % int(item["armor"]))
	if item.has("hp"):
		parts.append("+%d hp" % int(item["hp"]))
	if item.has("gold"):
		parts.append("%d gold" % int(item["gold"]))
	return " · ".join(parts)
