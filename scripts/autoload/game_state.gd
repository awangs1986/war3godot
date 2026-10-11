extends Node
## Persistent run state across town / dungeon scenes.
## Inventory model inspired by hearthkeep list-bag (simplified, no grid).

signal gold_changed(amount: int)
signal inventory_changed
signal equipment_changed
signal player_stats_changed

const MAX_BAG := 24
const EQUIP_SLOTS := ["weapon", "armor", "ring"]

var gold: int = 100
var bag: Array[Dictionary] = []
var equipped: Dictionary = {} # slot -> item dict
var max_hp: int = 100
var hp: int = 100
var base_damage: int = 8
var dungeon_depth_reached: int = 0
var _started: bool = false

func _ready() -> void:
	if not _started:
		_started = true
		reset_new_game()


func reset_new_game() -> void:
	gold = 100
	bag.clear()
	equipped.clear()
	max_hp = 100
	hp = 100
	base_damage = 8
	dungeon_depth_reached = 0
	# Starter club
	var club := ItemDB.make_item("club")
	_equip_internal(club)
	gold_changed.emit(gold)
	inventory_changed.emit()
	equipment_changed.emit()
	player_stats_changed.emit()


func add_gold(amount: int) -> void:
	gold = max(0, gold + amount)
	gold_changed.emit(gold)


func try_add_item(item: Dictionary) -> bool:
	if bag.size() >= MAX_BAG:
		return false
	bag.append(item)
	inventory_changed.emit()
	# Auto-equip empty slot (hearthkeep-style convenience)
	var slot: String = str(item.get("slot", ""))
	if slot != "" and not equipped.has(slot):
		equip_from_bag(item)
	return true


func equip_from_bag(item: Dictionary) -> void:
	if not bag.has(item):
		return
	_equip_internal(item)
	bag.erase(item)
	inventory_changed.emit()
	equipment_changed.emit()
	player_stats_changed.emit()


func unequip(slot: String) -> void:
	if not equipped.has(slot):
		return
	if bag.size() >= MAX_BAG:
		return
	bag.append(equipped[slot])
	equipped.erase(slot)
	inventory_changed.emit()
	equipment_changed.emit()
	player_stats_changed.emit()


func sell_item(item: Dictionary) -> int:
	if not bag.has(item):
		return 0
	var value: int = int(item.get("sell", 5))
	bag.erase(item)
	add_gold(value)
	inventory_changed.emit()
	return value


func get_attack_damage() -> int:
	var dmg := base_damage
	if equipped.has("weapon"):
		dmg += int(equipped["weapon"].get("damage", 0))
	return dmg


func get_armor() -> int:
	if equipped.has("armor"):
		return int(equipped["armor"].get("armor", 0))
	return 0


func get_bonus_hp() -> int:
	var bonus := 0
	if equipped.has("armor"):
		bonus += int(equipped["armor"].get("hp", 0))
	if equipped.has("ring"):
		bonus += int(equipped["ring"].get("hp", 0))
	return bonus


func refresh_max_hp() -> void:
	var new_max := 100 + get_bonus_hp()
	var ratio := 1.0 if max_hp <= 0 else float(hp) / float(max_hp)
	max_hp = new_max
	hp = clampi(int(round(max_hp * ratio)), 1, max_hp)
	player_stats_changed.emit()


func apply_damage(amount: int) -> void:
	var reduced: int = maxi(1, amount - get_armor())
	hp = maxi(0, hp - reduced)
	player_stats_changed.emit()


func heal_full() -> void:
	refresh_max_hp()
	hp = max_hp
	player_stats_changed.emit()


func _equip_internal(item: Dictionary) -> void:
	var slot: String = str(item.get("slot", ""))
	if slot == "" or not EQUIP_SLOTS.has(slot):
		return
	if equipped.has(slot):
		bag.append(equipped[slot])
	equipped[slot] = item
	refresh_max_hp()
