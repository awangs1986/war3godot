extends SceneTree
## Verifies that imported LevelData can drive runtime terrain without W3E parsing.


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var Importer = preload("res://addons/w3kit_cs/Level/W3LevelImporterBridge.cs")
	var World = preload("res://addons/w3kit_cs/World/W3MapWorld.cs")
	var args := OS.get_cmdline_user_args()
	var sample_dir := ProjectSettings.globalize_path("res://testdata/hivewe_sample")
	if not args.is_empty():
		sample_dir = args[0]
	var output := "user://w3_runtime_smoke.res"
	var import_error: int = int(Importer.import_to_file(sample_dir, output))
	if import_error != OK:
		printerr("RUNTIME_IMPORT_FAILED: ", Importer.GetLastError())
		quit(1)
		return
	var level = ResourceLoader.load(output, "", ResourceLoader.CACHE_MODE_IGNORE)
	var world = World.new()
	world.terrain_only = true
	root.add_child(world)
	var ok: bool = world.load_path(output)
	if not ok or world.map == null or world.sampler == null or world.map.source_w3e_sha256 != level.source_w3e_sha256:
		printerr("RUNTIME_LEVEL_FAILED: ", world.get_status())
		quit(1)
		return
	print("RUNTIME_LEVEL_OK: %dx%d sha256=%s" % [level.tile_count_x(), level.tile_count_y(), level.source_w3e_sha256])
	quit(0)
