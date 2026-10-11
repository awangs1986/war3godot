extends SceneTree
## Headless visual probe for a small Lost Temple tile area.
## Usage: --script res://tools/w3_render_tile_debug.gd -- <tile_x> <tile_y>

const W3EMap = preload("res://addons/w3kit_cs/Formats/W3EMapBridge.cs")
const W3GpuTerrain = preload("res://addons/w3kit_cs/Terrain/WarsmashGroundRenderer.cs")
const W3CliffBuilder = preload("res://addons/w3kit_cs/Terrain/WarsmashCliffRenderer.cs")
const W3HeightSampler = preload("res://addons/w3kit_cs/Terrain/W3HeightSamplerBridge.cs")
const W3TextureProvider = preload("res://addons/w3kit_cs/Assets/W3TextureProvider.cs")
const FreeFlyCamera = preload("res://addons/w3kit_cs/Sandbox/FreeFlyCamera.cs")
const W3LightingController = preload("res://addons/w3kit_cs/Lighting/W3LightingController.cs")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var tile := Vector2i(
		int(args[0]) if args.size() > 0 else 71,
		int(args[1]) if args.size() > 1 else 98
	)
	var source := ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.w3e")
	var level = W3EMap.load_file(source)
	if level == null:
		printerr("W3_RENDER_TILE: W3E missing")
		quit(1)
		return
	var provider := W3TextureProvider.new()
	provider.load_terrain_slk("")
	var terrain := W3GpuTerrain.new()
	root.add_child(terrain)
	var shd_bytes := PackedByteArray()
	var shd_file := FileAccess.open(ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.shd"), FileAccess.READ)
	if shd_file != null:
		shd_bytes = shd_file.get_buffer(shd_file.get_length())
	terrain.build(level, provider, shd_bytes)
	if args.size() > 2 and args[2] == "coords":
		terrain._mats[0].set_shader_parameter("debug_tile_coords", true)
	var render_mode := str(args[2]) if args.size() > 2 else ""
	if render_mode == "cliffs" or render_mode == "flat" or render_mode == "perspective":
		var cliffs := W3CliffBuilder.new()
		root.add_child(cliffs)
		cliffs.map = level
		cliffs.textures = provider
		cliffs._shd_tex = terrain._shd_tex
		cliffs._shd_enabled = not shd_bytes.is_empty()
		cliffs._load_cliff_types()
		for y in range(maxi(0, tile.y - 6), mini(level.tile_count_y(), tile.y + 7)):
			for x in range(maxi(0, tile.x - 6), mini(level.tile_count_x(), tile.x + 7)):
				cliffs._try_spawn_cliff(x, y)
		printerr("W3_RENDER_LOCAL_CLIFFS: %s" % cliffs.get_counts())
		for placement in cliffs._nodes_by_tile:
			if (placement as Vector2i).distance_squared_to(tile) > 2:
				continue
			var cliff_node = cliffs._nodes_by_tile[placement]
			for mesh_node in cliff_node.find_children("*", "MeshInstance3D", true, false):
				printerr("W3_RENDER_CLIFF tile=%s model=%s aabb=%s" % [
					placement, cliff_node.get_meta("w3_model_rel", ""), mesh_node.mesh.get_aabb(),
				])
		if render_mode == "flat":
			terrain.set_flat_inspect(true)
			cliffs.set_flat_inspect(true)
	var sampler := W3HeightSampler.new() # C# 带参构造在 GDScript 侧不可用
	sampler.bind_map(level)
	W3LightingController.apply_to_scene(root, null)
	var cam: Camera3D = FreeFlyCamera.new()
	root.add_child(cam)
	if render_mode == "perspective":
		cam.focus_point(sampler.tile_center_world(tile), 700.0)
	else:
		cam.focus_tile_topdown(sampler.tile_center_world(tile), 512.0)
	for _frame in 4:
		await process_frame
	await RenderingServer.frame_post_draw
	var image := root.get_viewport().get_texture().get_image()
	printerr("W3_RENDER_TILE_CENTER: %s" % image.get_pixel(image.get_width() / 2, image.get_height() / 2))
	var suffix := "_" + render_mode if not render_mode.is_empty() else ""
	var output := "res://.godot/w3_tile_%d_%d%s.png" % [tile.x, tile.y, suffix]
	var err := image.save_png(output)
	if err != OK:
		printerr("W3_RENDER_TILE: save failed %d" % err)
		quit(1)
		return
	print("W3_RENDER_TILE: ", ProjectSettings.globalize_path(output))
	quit(0)
