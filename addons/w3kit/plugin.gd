@tool
extends EditorPlugin


func _enter_tree() -> void:
	pass


func _exit_tree() -> void:
	pass


## Future dock will edit this shared resource (sun / fog / fill).
func get_default_lit_settings_path() -> String:
	return "res://addons/w3kit/w3_lit_settings_default.tres"
