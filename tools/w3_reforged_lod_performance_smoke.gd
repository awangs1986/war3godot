extends SceneTree


const SAMPLE_FRAMES := 90
const WARMUP_FRAMES := 30


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := load("res://scenes/w3_lost_temple_preview.tscn") as PackedScene
	if packed == null:
		_fail("preview scene did not load")
		return
	var scene := packed.instantiate()
	root.add_child(scene)
	var world = scene.get_node("W3MapWorld")
	var camera := scene.get_node("FreeFlyCamera") as Camera3D
	if world == null or camera == null:
		_fail("preview world/camera missing")
		return

	var complete := false
	for _frame in 1200:
		await process_frame
		if world.batcher == null:
			continue
		var counts: Dictionary = world.batcher.get_counts()
		if int(counts.jobs) == 0 and int(counts.queued) == 0 and int(counts.anim_pending) == 0 \
				and int(counts.ok) > 5000 and scene.is_loading_complete():
			complete = true
			break
	if not complete:
		_fail("streaming did not complete: %s" % world.batcher.get_counts())
		return

	var center_tile := Vector2i(world.map.tile_count_x() / 2, world.map.tile_count_y() / 2)
	var target: Vector3 = world.sampler.tile_center_world(center_tile)
	var tiers := [
		{"name": "near", "extent": 950.0, "expected_lod": 0},
		{"name": "medium", "extent": 2200.0, "expected_lod": 1},
		{"name": "far", "extent": 3800.0, "expected_lod": 2},
		{"name": "overview", "extent": 6200.0, "expected_lod": 3},
	]
	var results: Array = []
	for tier in tiers:
		camera.focus_point(target, float(tier.extent))
		for _warmup in WARMUP_FRAMES:
			await process_frame
		var frame_times_ms: Array[float] = []
		var begin_usec := Time.get_ticks_usec()
		var previous_usec := begin_usec
		var draw_calls_total := 0.0
		var primitives_total := 0.0
		for _sample in SAMPLE_FRAMES:
			await process_frame
			var now_usec := Time.get_ticks_usec()
			frame_times_ms.append(float(now_usec - previous_usec) / 1000.0)
			previous_usec = now_usec
			draw_calls_total += Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)
			primitives_total += Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)
		var elapsed_sec := float(Time.get_ticks_usec() - begin_usec) / 1000000.0
		frame_times_ms.sort()
		var p95_index := mini(frame_times_ms.size() - 1, int(ceil(frame_times_ms.size() * 0.95)) - 1)
		var counts: Dictionary = world.batcher.get_counts()
		var result := {
			"name": str(tier.name),
			"extent": float(tier.extent),
			"fps": float(SAMPLE_FRAMES) / maxf(elapsed_sec, 0.001),
			"p95_ms": frame_times_ms[p95_index],
			"draw_calls": draw_calls_total / SAMPLE_FRAMES,
			"primitives": primitives_total / SAMPLE_FRAMES,
			"lod": int(counts.lod),
			"visible_static": int(counts.visible_static),
			"anim_active": int(counts.anim_active),
			"render_scale": root.scaling_3d_scale,
		}
		results.append(result)
		print("W3_LOD_PERF ", result)
		if int(counts.lod) != int(tier.expected_lod):
			_fail("wrong LOD for %s: expected %s got %s" % [tier.name, tier.expected_lod, counts.lod])
			return

	var image := root.get_viewport().get_texture().get_image()
	var output := ProjectSettings.globalize_path("res://.godot/w3_reforged_lod_overview.png")
	if image == null or image.is_empty() or image.save_png(output) != OK:
		_fail("overview screenshot failed")
		return
	if world.batcher._anim_instances.is_empty():
		_fail("no animated actor available for proximity test")
		return
	var probe_actor = world.batcher._anim_instances[0]
	camera.focus_point(probe_actor.global_position, 600.0)
	for _frame in 30:
		await process_frame
	if not probe_actor.is_anim_active():
		_fail("animation did not start inside 800-unit threshold")
		return
	var active_distance: float = float(probe_actor.global_position.distance_to(camera.global_position))
	camera.focus_point(probe_actor.global_position, 1400.0)
	for _frame in 30:
		await process_frame
	if probe_actor.is_anim_active():
		_fail("animation did not stop outside 950-unit hysteresis")
		return
	print("W3_ANIM_PROXIMITY_OK active_distance=", active_distance,
		" stopped_distance=", probe_actor.global_position.distance_to(camera.global_position))
	print("W3_LOD_PERFORMANCE_OK results=", results, " screenshot=", output)
	quit(0)


func _fail(message: String) -> void:
	printerr("W3_LOD_PERFORMANCE_FAIL ", message)
	quit(1)
