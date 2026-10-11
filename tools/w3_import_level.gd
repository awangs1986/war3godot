extends SceneTree
## Usage: godot --headless --path . --script res://tools/w3_import_level.gd -- INPUT OUTPUT.res


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2:
		printerr("Usage: w3_import_level.gd INPUT OUTPUT.res")
		quit(2)
		return
	var source := _globalize_if_resource_path(args[0])
	var output := args[1]
	var Importer = preload("res://addons/w3kit_cs/Level/W3LevelImporterBridge.cs")
	var err: int = Importer.import_to_file(source, output)
	if err != OK:
		printerr("IMPORT_FAILED: ", Importer.GetLastError())
		quit(1)
		return
	print("IMPORT_OK: ", output)
	quit(0)


func _globalize_if_resource_path(path: String) -> String:
	if path.begins_with("res://") or path.begins_with("user://"):
		return ProjectSettings.globalize_path(path)
	return path
