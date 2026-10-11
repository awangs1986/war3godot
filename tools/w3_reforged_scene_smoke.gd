extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var started := Time.get_ticks_msec()
	var packed := load("res://scenes/w3_lost_temple_preview.tscn") as PackedScene
	if packed == null:
		_fail("preview scene did not load")
		return
	var scene := packed.instantiate()
	root.add_child(scene)
	var world = scene.get_node("W3MapWorld")
	var complete := false
	for _frame in 900:
		await process_frame
		if world == null or world.batcher == null:
			continue
		var counts: Dictionary = world.batcher.get_counts()
		if int(counts.jobs) == 0 and int(counts.queued) == 0 and int(counts.anim_pending) == 0 \
				and int(counts.ok) > 5000 and scene.is_loading_complete():
			complete = true
			break
	if not complete:
		_fail("streaming did not complete: %s" % world.batcher.get_counts())
		return
	print("W3_REFORGED_SCENE_STAGE streaming_ms=", Time.get_ticks_msec() - started)
	var counts: Dictionary = world.batcher.get_counts()
	if int(counts.miss) != 0 or int(counts.fail) != 0:
		_fail("runtime model fallback/failure: %s" % counts)
		return
	# Mobile map units live in the simulation layer now; only buildings and
	# flying units stay in the batcher's animated pool.
	var game = scene.get_game()
	var sim_units: int = game.sim.get_units().size() if game != null else 0
	if int(counts.animated) + sim_units < 100:
		_fail("animated units were not spawned: batcher=%s sim=%d" % [counts, sim_units])
		return
	if game == null or sim_units < 50:
		_fail("game layer did not simulate the map units (sim=%d)" % sim_units)
		return
	for model in world.batcher._model_cache.values():
		if model == null or int(model.version) <= 800:
			_fail("classic model reached the runtime model cache")
			return
	for instance in world.batcher._anim_instances:
		if instance._animate and (instance._skeleton == null or instance._skeleton.get_bone_count() == 0):
			_fail("animated unit missing GPU skeleton")
			return
	var broken_views: int = game.renderer.broken_view_count() if game != null else 0
	if broken_views > 0:
		_fail("%d simulated units have no renderable model" % broken_views)
		return
	print("W3_REFORGED_SCENE_STAGE audit_ms=", Time.get_ticks_msec() - started)
	var args := OS.get_cmdline_user_args()
	var target_tile := Vector2i(
		int(args[0]) if args.size() > 0 else 84,
		int(args[1]) if args.size() > 1 else 65
	)
	var camera := scene.get_node("FreeFlyCamera") as Camera3D
	var target: Vector3 = world.sampler.tile_center_world(target_tile)
	camera.focus_point(target, 950.0)
	for frame in 2:
		await process_frame
		print("W3_REFORGED_SCENE_STAGE render_frame=", frame, " ms=", Time.get_ticks_msec() - started)
	var image := root.get_viewport().get_texture().get_image()
	var output_name := "w3_reforged_full_scene.png" if args.is_empty() else "w3_reforged_%d_%d.png" % [target_tile.x, target_tile.y]
	var output := ProjectSettings.globalize_path("res://.godot/" + output_name)
	if image == null or image.is_empty() or image.save_png(output) != OK:
		_fail("full-scene screenshot failed")
		return
	print("W3_REFORGED_SCENE_OK counts=", counts, " cached_models=", world.batcher._model_cache.size(),
		" screenshot=", output)
	quit(0)


func _fail(message: String) -> void:
	printerr("W3_REFORGED_SCENE_FAIL ", message)
	quit(1)
