extends CanvasLayer
## HUD + list inventory + merchant sell panel (inventory option B).

@onready var _hp: ProgressBar = $Root/TopLeft/HPBar
@onready var _hp_label: Label = $Root/TopLeft/HPLabel
@onready var _gold: Label = $Root/TopLeft/GoldLabel
@onready var _hint: Label = $Root/Bottom/Hint
@onready var _inv_panel: PanelContainer = $Root/InventoryPanel
@onready var _inv_list: VBoxContainer = $Root/InventoryPanel/Margin/VBox/Scroll/ItemList
@onready var _eq_label: Label = $Root/InventoryPanel/Margin/VBox/EquipLabel
@onready var _merchant_panel: PanelContainer = $Root/MerchantPanel
@onready var _merchant_list: VBoxContainer = $Root/MerchantPanel/Margin/VBox/Scroll/SellList
@onready var _close_inv: Button = $Root/InventoryPanel/Margin/VBox/CloseInv


func _ready() -> void:
	_inv_panel.visible = false
	_merchant_panel.visible = false
	_close_inv.pressed.connect(_on_close_inv_pressed)
	GameState.gold_changed.connect(_on_gold)
	GameState.player_stats_changed.connect(_refresh_stats)
	GameState.inventory_changed.connect(_rebuild_inv)
	GameState.equipment_changed.connect(_rebuild_inv)
	_on_gold(GameState.gold)
	_refresh_stats()
	_rebuild_inv()
	_hint.text = "LMB click-move · WASD (iso) · E interact · I inventory"
	_hp_label.add_theme_color_override("font_color", Color(1, 1, 1))
	_gold.add_theme_color_override("font_color", Color(1, 0.9, 0.4))
	_hint.add_theme_color_override("font_color", Color(1, 1, 1))
	# Big banner so we can tell the scene loaded even if 3D is wrong.
	if not has_node("Root/Banner"):
		var banner := Label.new()
		banner.name = "Banner"
		banner.text = "  SCENE LOADED  "
		banner.add_theme_font_size_override("font_size", 28)
		banner.add_theme_color_override("font_color", Color(1, 0.85, 0.2))
		banner.position = Vector2(16, 130)
		$Root.add_child(banner)
	visible = true
	layer = 100


func is_blocking_clicks() -> bool:
	return _inv_panel.visible or _merchant_panel.visible


func toggle_inventory() -> void:
	_merchant_panel.visible = false
	_inv_panel.visible = not _inv_panel.visible
	if _inv_panel.visible:
		_rebuild_inv()


func show_merchant() -> void:
	_inv_panel.visible = false
	_merchant_panel.visible = true
	_rebuild_merchant()


func _on_gold(amount: int) -> void:
	_gold.text = "Gold: %d" % amount


func _refresh_stats() -> void:
	_hp.max_value = GameState.max_hp
	_hp.value = GameState.hp
	_hp_label.text = "HP %d / %d  ·  ATK %d  ·  Arm %d" % [
		GameState.hp, GameState.max_hp, GameState.get_attack_damage(), GameState.get_armor()
	]


func _rebuild_inv() -> void:
	for c in _inv_list.get_children():
		c.queue_free()
	var eq_parts: PackedStringArray = []
	for slot in GameState.EQUIP_SLOTS:
		if GameState.equipped.has(slot):
			eq_parts.append("%s: %s" % [slot, GameState.equipped[slot].get("name", "?")])
		else:
			eq_parts.append("%s: —" % slot)
	_eq_label.text = "  |  ".join(eq_parts)

	for item in GameState.bag:
		var row := _make_item_row(item, true)
		_inv_list.add_child(row)


func _rebuild_merchant() -> void:
	for c in _merchant_list.get_children():
		c.queue_free()
	var title := Label.new()
	title.text = "Griswold-ish: sell bag items (click Sell)"
	_merchant_list.add_child(title)
	for item in GameState.bag:
		var row := HBoxContainer.new()
		var lab := Label.new()
		lab.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		lab.text = "%s  (%dg)" % [ItemDB.describe(item), int(item.get("sell", 5))]
		lab.modulate = ItemDB.rarity_color(item)
		var btn := Button.new()
		btn.text = "Sell"
		btn.pressed.connect(_sell.bind(item))
		row.add_child(lab)
		row.add_child(btn)
		_merchant_list.add_child(row)
	var close := Button.new()
	close.text = "Close"
	close.pressed.connect(func(): _merchant_panel.visible = false)
	_merchant_list.add_child(close)


func _make_item_row(item: Dictionary, allow_equip: bool) -> Control:
	var row := HBoxContainer.new()
	var lab := Label.new()
	lab.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	lab.text = ItemDB.describe(item)
	lab.modulate = ItemDB.rarity_color(item)
	row.add_child(lab)
	if allow_equip and str(item.get("slot", "")) != "":
		var eq := Button.new()
		eq.text = "Equip"
		eq.pressed.connect(func(): GameState.equip_from_bag(item); _rebuild_inv())
		row.add_child(eq)
	return row


func _sell(item: Dictionary) -> void:
	GameState.sell_item(item)
	_rebuild_merchant()
	_rebuild_inv()


func _on_close_inv_pressed() -> void:
	_inv_panel.visible = false
