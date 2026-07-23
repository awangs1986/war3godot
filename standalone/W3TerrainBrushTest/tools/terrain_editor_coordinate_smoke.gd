extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var packed := load("res://NativeTerrainEditorTest.tscn") as PackedScene
	var terrain := packed.instantiate()
	root.add_child(terrain)
	for _frame in 3:
		await process_frame
	var document = terrain.get("Document")
	if document == null:
		printerr("TERRAIN_EDITOR_COORDINATE_FAIL missing document")
		quit(1)
		return
	var minimum := INF
	var maximum := -INF
	var ground = document.get("GroundHeights")
	var cliffs = document.get("CliffLevels")
	var step: float = document.get("CliffStep")
	for i in ground.size():
		var height: float = float(ground[i]) + int(cliffs[i]) * step
		minimum = minf(minimum, height)
		maximum = maxf(maximum, height)
	var world_min: float = terrain.global_position.y + minimum
	var world_max: float = terrain.global_position.y + maximum
	print("TERRAIN_EDITOR_COORDINATE world_size=", document.get("TileWidth") * document.get("TileSize"),
		"x", document.get("TileHeight") * document.get("TileSize"),
		" local_y=[", minimum, ",", maximum, "] world_y=[", world_min, ",", world_max, "]")
	if world_min <= 0.0 and world_max >= 0.0:
		printerr("TERRAIN_EDITOR_COORDINATE_GRID_INTERSECTION Y=0")
		quit(2)
		return
	quit(0)
