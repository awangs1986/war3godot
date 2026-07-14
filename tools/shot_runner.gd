extends Node
func _ready() -> void:
	await get_tree().process_frame
	get_tree().change_scene_to_file("res://scenes/dungeon.tscn")
	# This node dies on scene change — use autoload style differently
