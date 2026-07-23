extends SceneTree
## Headless render of the blocky-repro raise2 map (editor/HiveWE parity check).
## Usage: --script res://tools/w3_raise2_shot.gd -- [w3ePath] [outPng] [mode]
## mode: "topdown" (default) or "perspective".

const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3GpuTerrain = preload("res://addons/w3kit/w3_gpu_terrain.gd")
const W3CliffBuilder = preload("res://addons/w3kit/w3_cliff_builder.gd")
const W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const FreeFlyCamera = preload("res://scripts/w3/free_fly_camera.gd")
const W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var w3e_path: String = args[0] if args.size() > 0 else "D:/temp/cursor/w3editor/blocky_repro/raise2_map/war3map.w3e"
	var out_png: String = args[1] if args.size() > 1 else "D:/temp/cursor/w3editor/blocky_repro/godot_raise2.png"
	var mode: String = args[2] if args.size() > 2 else "topdown"
	var level = W3EMap.load_file(w3e_path)
	if level == null:
		printerr("W3_RAISE2_SHOT: cannot load ", w3e_path)
		quit(1)
		return
	var provider := W3TextureProvider.new()
	provider.load_terrain_slk()
	var terrain := W3GpuTerrain.new()
	root.add_child(terrain)
	terrain.build(level, provider, PackedByteArray())
	var cliffs := W3CliffBuilder.new()
	root.add_child(cliffs)
	cliffs.build(level, provider)
	printerr("W3_RAISE2_CLIFFS: %s" % [cliffs.get_counts()])
	if mode == "flat":
		terrain.set_flat_inspect(true)
		cliffs.set_flat_inspect(true)
	else:
		# Deterministic bright lighting (no tonemap surprises) so cliff-skirt
		# lighting squares are visible if the normal fix regresses.
		var sun := DirectionalLight3D.new()
		sun.rotation_degrees = Vector3(-62.0, 35.0, 0.0)
		sun.light_energy = 1.3
		sun.shadow_enabled = false
		root.add_child(sun)
		var we := WorldEnvironment.new()
		var env := Environment.new()
		env.background_mode = Environment.BG_COLOR
		env.background_color = Color(0.1, 0.1, 0.12)
		env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
		env.ambient_light_color = Color(0.7, 0.7, 0.72)
		env.ambient_light_energy = 0.8
		we.environment = env
		root.add_child(we)
	var sampler := W3HeightSampler.new(level)
	var center := Vector2i(level.tile_count_x() / 2, level.tile_count_y() / 2)
	var cam: Camera3D = FreeFlyCamera.new()
	root.add_child(cam)
	if mode == "perspective":
		cam.focus_point(sampler.tile_center_world(center), 1600.0)
	else:
		cam.focus_tile_topdown(sampler.tile_center_world(center), 1600.0)
	for _frame in 6:
		await process_frame
	await RenderingServer.frame_post_draw
	var image := root.get_viewport().get_texture().get_image()
	var err := image.save_png(out_png)
	if err != OK:
		printerr("W3_RAISE2_SHOT: save failed %d" % err)
		quit(1)
		return
	print("W3_RAISE2_SHOT: ", out_png)
	quit(0)
