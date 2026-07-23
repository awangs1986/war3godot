extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := load("res://scenes/w3_lost_temple_preview.tscn") as PackedScene
	if packed == null:
		_fail("preview scene did not load")
		return
	var scene := packed.instantiate()
	root.add_child(scene)
	await process_frame
	await RenderingServer.frame_post_draw
	var overlay := scene.get_node("CanvasLayer/LoadingOverlay") as Control
	var hud := scene.get_node("CanvasLayer/Status") as Label
	var camera = scene.get_node("FreeFlyCamera")
	if overlay == null or not overlay.visible or hud.visible or not bool(camera.input_blocked):
		_fail("loading screen did not own the initial frame")
		return
	var start_path := ProjectSettings.globalize_path("res://.godot/w3_loading_start.png")
	var start_image := root.get_viewport().get_texture().get_image()
	if start_image == null or start_image.is_empty() or start_image.save_png(start_path) != OK:
		_fail("initial loading screenshot failed")
		return

	var previous_progress: float = float(scene.get_loading_progress())
	var saw_streaming_progress := false
	var complete := false
	for _frame in 1200:
		await process_frame
		var progress: float = scene.get_loading_progress()
		if progress + 0.001 < previous_progress:
			_fail("loading progress went backwards: %.3f -> %.3f" % [previous_progress, progress])
			return
		previous_progress = progress
		if progress >= 0.22 and progress < 0.95:
			saw_streaming_progress = true
		if scene.is_loading_complete():
			complete = true
			break
	if not complete:
		_fail("loading screen did not finish")
		return
	if not saw_streaming_progress:
		_fail("model streaming progress was never reported")
		return
	if overlay.visible or not hud.visible or bool(camera.input_blocked):
		_fail("game input/overlay state was not handed over")
		return
	var counts: Dictionary = scene.get_node("W3MapWorld").batcher.get_counts()
	if int(counts.jobs) != 0 or int(counts.queued) != 0 or int(counts.anim_pending) != 0:
		_fail("game entered before streaming settled: %s" % counts)
		return
	print("W3_LOADING_SCREEN_OK progress=", previous_progress, " counts=", counts,
		" start_screenshot=", start_path)
	quit(0)


func _fail(message: String) -> void:
	printerr("W3_LOADING_SCREEN_FAIL ", message)
	quit(1)
