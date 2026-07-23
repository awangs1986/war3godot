extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := load("res://NativeTerrainEditorTest.tscn") as PackedScene
	if packed == null:
		_fail("main terrain scene could not be loaded")
		return
	var terrain := packed.instantiate()
	root.add_child(terrain)
	for _frame in 8:
		await process_frame
	var document = terrain.get("Document")
	if document == null:
		_fail("TerrainWorld has no TerrainDocument")
		return
	if int(document.get("TileWidth")) != 32 or int(document.get("TileHeight")) != 32:
		_fail("terrain is not 32x32")
		return
	var materials = document.get("GroundMaterials")
	var flags = document.get("CornerFlags")
	var has_grass := false
	var has_water := false
	for value in materials:
		if int(value) == 4:
			has_grass = true
			break
	for value in flags:
		if (int(value) & 2) != 0:
			has_water = true
			break
	if not has_grass or not has_water:
		_fail("showcase terrain was not pre-painted")
		return
	var chunks := terrain.get_node_or_null("_TerrainChunks")
	if chunks == null or chunks.get_child_count() != 4:
		_fail("expected four initialized 16x16 chunks")
		return
	var plugins: PackedStringArray = ProjectSettings.get_setting("editor_plugins/enabled", PackedStringArray())
	if not plugins.has("res://addons/w3_foundation/plugin.cfg"):
		_fail("terrain editor plugin is not enabled")
		return
	print("W3_TERRAIN_TEST_PROJECT_OK size=32x32 prepainted=true chunks=", chunks.get_child_count(),
		" plugin=enabled scene=res://NativeTerrainEditorTest.tscn")
	quit(0)


func _fail(message: String) -> void:
	printerr("W3_TERRAIN_TEST_PROJECT_FAIL ", message)
	quit(1)
