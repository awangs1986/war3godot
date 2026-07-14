extends Area3D
## World loot pickup. Gold or equipment via GameState / ItemDB.

@export var item: Dictionary = {}

@onready var _mesh: MeshInstance3D = $Mesh
@onready var _label: Label3D = $Label3D


func _ready() -> void:
	collision_layer = 8
	collision_mask = 2
	body_entered.connect(_on_body_entered)
	monitoring = true
	monitorable = true
	_refresh_visual()


func setup(p_item: Dictionary) -> void:
	item = p_item
	if is_node_ready():
		_refresh_visual()


func _refresh_visual() -> void:
	var mat := StandardMaterial3D.new()
	mat.albedo_color = ItemDB.rarity_color(item)
	mat.emission_enabled = true
	mat.emission = mat.albedo_color * 0.4
	_mesh.material_override = mat
	_label.text = ItemDB.describe(item)
	_label.modulate = mat.albedo_color


func _on_body_entered(body: Node) -> void:
	if not body.is_in_group("player"):
		return
	if item.get("id", "") == "gold_pile" or item.has("gold"):
		GameState.add_gold(int(item.get("gold", 10)))
		queue_free()
		return
	if GameState.try_add_item(item):
		queue_free()
	# else bag full — leave on ground
