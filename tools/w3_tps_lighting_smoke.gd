extends SceneTree


const WARMUP_FRAMES := 45
const SAMPLE_FRAMES := 60


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
	var environment_node := scene.get_node("WorldEnvironment") as WorldEnvironment
	var sun := scene.get_node("DirectionalLight3D") as DirectionalLight3D
	if world == null or camera == null or environment_node == null or sun == null:
		_fail("preview lighting nodes are missing")
		return

	var complete := false
	for _frame in 1400:
		await process_frame
		if scene.is_loading_complete():
			complete = true
			break
	if not complete:
		_fail("loading did not complete")
		return

	var env := environment_node.environment
	if env == null or not bool(environment_node.get_meta("w3_tps_modern_lighting", false)):
		_fail("TPS modern environment was not installed")
		return
	if env.tonemap_mode != Environment.TONE_MAPPER_AGX:
		_fail("AgX tonemapping is not active")
		return
	if sun.directional_shadow_mode != DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS:
		_fail("four-split directional shadows are not active")
		return

	var center_tile := Vector2i(world.map.tile_count_x() / 2, world.map.tile_count_y() / 2)
	var target: Vector3 = world.sampler.tile_center_world(center_tile)
	camera.focus_point(target, 950.0)
	scene._apply_zoom_render_scale()
	for _frame in WARMUP_FRAMES:
		await process_frame
	if not env.ssao_enabled or not env.ssil_enabled or not env.sdfgi_enabled \
			or not env.glow_enabled or not env.volumetric_fog_enabled or not sun.shadow_enabled:
		_fail("near lighting tier is incomplete: %s" % _environment_state(env, sun))
		return
	var near_result := await _sample_performance("near", world, camera, env, sun)
	var near_path := ProjectSettings.globalize_path("res://.godot/w3_tps_modern_lighting_near.png")
	if not await _capture(near_path):
		return

	camera.focus_point(target, 6200.0)
	scene._apply_zoom_render_scale()
	for _frame in WARMUP_FRAMES:
		await process_frame
	if env.ssao_enabled or env.ssil_enabled or env.sdfgi_enabled \
			or env.glow_enabled or env.volumetric_fog_enabled or sun.shadow_enabled:
		_fail("overview lighting tier did not shed expensive effects: %s" % _environment_state(env, sun))
		return
	var overview_result := await _sample_performance("overview", world, camera, env, sun)
	var overview_path := ProjectSettings.globalize_path("res://.godot/w3_tps_modern_lighting_overview.png")
	if not await _capture(overview_path):
		return

	print("W3_TPS_LIGHTING_OK near=", near_result, " overview=", overview_result,
		" screenshots=", [near_path, overview_path])
	quit(0)


func _sample_performance(label: String, world, camera: Camera3D, env: Environment, sun: DirectionalLight3D) -> Dictionary:
	var draw_calls := 0.0
	var primitives := 0.0
	var started := Time.get_ticks_usec()
	for _frame in SAMPLE_FRAMES:
		await process_frame
		draw_calls += Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)
		primitives += Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)
	var elapsed := maxf(float(Time.get_ticks_usec() - started) / 1000000.0, 0.001)
	var result := {
		"fps": SAMPLE_FRAMES / elapsed,
		"draw_calls": draw_calls / SAMPLE_FRAMES,
		"primitives": primitives / SAMPLE_FRAMES,
		"render_scale": root.scaling_3d_scale,
		"lod": int(world.batcher.get_counts().lod),
		"camera_y": camera.global_position.y,
		"ground_y": float(world.sampler.sample_y(camera.global_position.x, camera.global_position.z)),
		"lighting": _environment_state(env, sun),
	}
	print("W3_TPS_LIGHTING_PERF ", label, " ", result)
	return result


func _environment_state(env: Environment, sun: DirectionalLight3D) -> Dictionary:
	return {
		"ssao": env.ssao_enabled,
		"ssil": env.ssil_enabled,
		"sdfgi": env.sdfgi_enabled,
		"glow": env.glow_enabled,
		"volumetric_fog": env.volumetric_fog_enabled,
		"sun_shadows": sun.shadow_enabled,
	}


func _capture(path: String) -> bool:
	await RenderingServer.frame_post_draw
	var image := root.get_viewport().get_texture().get_image()
	if image == null or image.is_empty() or image.save_png(path) != OK:
		_fail("screenshot failed: " + path)
		return false
	return true


func _fail(message: String) -> void:
	printerr("W3_TPS_LIGHTING_FAIL ", message)
	quit(1)
