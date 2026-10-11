extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := load("res://TerrainBrushTest.tscn") as PackedScene
	if packed == null:
		_fail("scene load")
		return
	var terrain := packed.instantiate()
	root.add_child(terrain)
	for _frame in 20:
		await process_frame

	var camera := terrain.get_node("Camera3D") as Camera3D
	var document = terrain.get("Document")
	if camera == null or document == null:
		_fail("camera/document missing")
		return

	var tile_size: float = document.get("TileSize")
	var target_corner := Vector2i(16, 16)
	var target_index: int = document.call("CornerIndex", target_corner.x, target_corner.y)
	var before_material: int = document.get("GroundMaterials")[target_index]
	var local := Vector3(target_corner.x * tile_size, 0.0, target_corner.y * tile_size)
	local.y = terrain.call("SampleHeight", Vector2(local.x, local.z))
	var screen := camera.unproject_position(terrain.to_global(local))

	var hover := InputEventMouseMotion.new()
	hover.position = screen
	hover.global_position = screen
	Input.parse_input_event(hover)
	await process_frame

	var press := InputEventMouseButton.new()
	press.button_index = MOUSE_BUTTON_LEFT
	press.pressed = true
	press.position = screen
	press.global_position = screen
	Input.parse_input_event(press)
	await process_frame

	var drag := InputEventMouseMotion.new()
	drag.position = screen + Vector2(8.0, 0.0)
	drag.global_position = drag.position
	drag.button_mask = MOUSE_BUTTON_MASK_LEFT
	Input.parse_input_event(drag)
	await process_frame

	var release := InputEventMouseButton.new()
	release.button_index = MOUSE_BUTTON_LEFT
	release.pressed = false
	release.position = drag.position
	release.global_position = drag.position
	Input.parse_input_event(release)
	for _frame in 4:
		await process_frame

	var after_material: int = document.get("GroundMaterials")[target_index]
	if before_material == 4 or after_material != 4:
		_fail("stroke did not paint target: before=%d after=%d" % [before_material, after_material])
		return

	var camera_before := camera.global_position
	var wheel := InputEventMouseButton.new()
	wheel.button_index = MOUSE_BUTTON_WHEEL_UP
	wheel.pressed = true
	wheel.position = Vector2(1200.0, 500.0)
	wheel.global_position = wheel.position
	Input.parse_input_event(wheel)
	await process_frame
	var zoom_distance := camera_before.distance_to(camera.global_position)
	if zoom_distance < 1000.0:
		_fail("wheel zoom is not 5x: displacement=%.1f" % zoom_distance)
		return

	await RenderingServer.frame_post_draw
	var image := root.get_viewport().get_texture().get_image()
	var output := ProjectSettings.globalize_path("res://hivewe_brush_flow.png")
	if image == null or image.is_empty() or image.save_png(output) != OK:
		_fail("screenshot")
		return
	print("HIVEWE_BRUSH_FLOW_OK painted=", before_material, "->", after_material,
		" zoom_displacement=", zoom_distance, " screenshot=", output)
	quit(0)


func _fail(message: String) -> void:
	printerr("HIVEWE_BRUSH_FLOW_FAIL ", message)
	quit(1)
