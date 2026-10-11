extends StaticBody3D
## Walk-up / click interactable (portal or merchant).

signal activated
signal opened

@export var kind: String = "portal_dungeon" # portal_dungeon | portal_town | merchant
@export var prompt: String = "Enter"

@onready var _label: Label3D = $Label3D
@onready var _area: Area3D = $Area3D


func _ready() -> void:
	add_to_group("interact")
	add_to_group(kind)
	collision_layer = 16
	collision_mask = 0
	_label.text = prompt
	_area.body_entered.connect(_on_body)
	_area.collision_layer = 0
	_area.collision_mask = 2


func activate(_player: Node) -> void:
	if kind == "merchant":
		opened.emit()
	else:
		activated.emit()


func _on_body(body: Node) -> void:
	if body.is_in_group("player") and Input.is_action_pressed("interact"):
		activate(body)


func _physics_process(_delta: float) -> void:
	# E near interactable
	if not _area.get_overlapping_bodies().is_empty() and Input.is_action_just_pressed("interact"):
		var bodies := _area.get_overlapping_bodies()
		for b in bodies:
			if b.is_in_group("player"):
				activate(b)
				return
