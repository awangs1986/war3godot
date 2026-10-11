extends SceneTree

const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3GpuTerrain = preload("res://addons/w3kit/w3_gpu_terrain.gd")
const W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3DooParser = preload("res://addons/w3kit/w3_doo_parser.gd")
const W3ObjectCatalog = preload("res://addons/w3kit/w3_object_catalog.gd")
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
const FreeFlyCamera = preload("res://scripts/w3/free_fly_camera.gd")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var map = W3EMap.load_file(ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.w3e"))
	var provider := W3TextureProvider.new()
	provider.load_terrain_slk()
	var terrain := W3GpuTerrain.new()
	root.add_child(terrain)
	terrain.build(map, provider)
	var sampler := W3HeightSampler.new(map)
	var catalog := W3ObjectCatalog.new()
	catalog.load_from_roots()
	var file := FileAccess.open(ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3mapUnits.doo"), FileAccess.READ)
	var units: Array = W3DooParser.parse_units(file.get_buffer(file.get_length()))
	var center := Vector3(-64.0, sampler.sample_y(-64.0, -64.0), -64.0)
	var spawned := 0
	for unit in units:
		var delta := Vector2(unit.position.x - center.x, unit.position.z - center.z)
		if delta.length() > 420.0:
			continue
		var rel := catalog.resolve_model(unit.id, true, unit.variation)
		var abs_path := W3Paths.resolve_path(rel)
		var model = W3MdxModel.load_file(abs_path)
		if model == null or model.geosets.is_empty():
			continue
		var instance := W3MdxInstance.new()
		root.add_child(instance)
		var pos: Vector3 = unit.position
		var ground_y := sampler.sample_y(pos.x, pos.z)
		if pos.y < ground_y + 8.0:
			pos.y = ground_y
		instance.transform = Transform3D(Basis(Vector3.UP, -unit.angle).scaled(unit.scale), pos)
		instance.setup(model, provider, abs_path, {"animate": true, "team_color": int(unit.player_owner)})
		printerr("FOUNTAIN_RENDER id=%s model=%s pos=%s" % [unit.id, rel, pos])
		spawned += 1
	W3War3Lit.apply_to_scene(root)
	var camera: Camera3D = FreeFlyCamera.new()
	root.add_child(camera)
	camera.focus_point(center + Vector3(0.0, 80.0, 0.0), 650.0)
	for _frame in 10:
		await process_frame
	await RenderingServer.frame_post_draw
	var image := root.get_viewport().get_texture().get_image()
	var output := "res://.godot/w3_fountain_units.png"
	var err := image.save_png(output)
	printerr("W3_FOUNTAIN_RENDER spawned=%d output=%s err=%d" % [spawned, ProjectSettings.globalize_path(output), err])
	quit(0 if err == OK else 1)
