extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := load("res://TerrainBrushTest.tscn") as PackedScene
	if packed == null:
		printerr("W3_TERRAIN_VISUAL_FAIL scene load")
		quit(1)
		return
	var terrain := packed.instantiate()
	root.add_child(terrain)
	for _frame in 30:
		await process_frame
	await RenderingServer.frame_post_draw
	var image := root.get_viewport().get_texture().get_image()
	var output := ProjectSettings.globalize_path("res://terrain_brush_test.png")
	if image == null or image.is_empty() or image.save_png(output) != OK:
		printerr("W3_TERRAIN_VISUAL_FAIL screenshot")
		quit(1)
		return
	print("W3_TERRAIN_VISUAL_OK screenshot=", output)
	quit(0)
