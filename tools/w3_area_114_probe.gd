extends SceneTree

const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3GpuTerrain = preload("res://addons/w3kit/w3_gpu_terrain.gd")
const W3CliffBuilder = preload("res://addons/w3kit/w3_cliff_builder.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
const W3DooParser = preload("res://addons/w3kit/w3_doo_parser.gd")
const W3ObjectCatalog = preload("res://addons/w3kit/w3_object_catalog.gd")


func _initialize() -> void:
	call_deferred("_run")


func _read_bytes(path: String) -> PackedByteArray:
	var file := FileAccess.open(ProjectSettings.globalize_path(path), FileAccess.READ)
	return file.get_buffer(file.get_length()) if file != null else PackedByteArray()


func _run() -> void:
	var level = W3EMap.load_file(ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.w3e"))
	var provider := W3TextureProvider.new()
	var sampler := W3HeightSampler.new(level)
	provider.load_terrain_slk()
	var terrain := W3GpuTerrain.new()
	terrain.map = level
	terrain.textures = provider
	terrain._layer_ids = level.ground_tilesets.duplicate()
	terrain._prepare_blight()
	terrain._prepare_cliff_to_ground()
	terrain._prepare_romp_tiles()
	terrain._build_ground_arrays()
	var tile_data: Image = terrain._build_tile_data(level.tile_count_x(), level.tile_count_y())

	var cliffs := W3CliffBuilder.new()
	root.add_child(cliffs)
	cliffs.map = level
	cliffs.textures = provider
	cliffs._load_cliff_types()
	for y in range(108, 121):
		for x in range(64, 84):
			cliffs._try_spawn_cliff(x, y)

	var shd := _read_bytes("res://war3/maps/LostTemple/source/war3map.shd")
	var shd_w: int = int(level.tile_count_x()) * 4
	var doodads := W3DooParser.parse_doodads(_read_bytes("res://war3/maps/LostTemple/source/war3map.doo"))
	var catalog := W3ObjectCatalog.new()
	catalog.load_from_roots()
	for p in [Vector2i(70, 114), Vector2i(73, 114), Vector2i(74, 114), Vector2i(77, 114)]:
		var corners: Array[String] = []
		for cp in [p, p + Vector2i(1, 0), p + Vector2i(0, 1), p + Vector2i(1, 1)]:
			var c = level.get_corner(cp.x, cp.y)
			corners.append("h%d/r%d/t%d/c%d" % [int(c.layer_height), int(c.ramp), int(c.ground_texture), int(c.cliff_texture)])
		var transition: Dictionary = level.get_ramp_transition(p.x, p.y)
		var node = cliffs._nodes_by_tile.get(p, null)
		var model := str(node.get_meta("w3_model_rel", "")) if node != null else ""
		var source_tile = node.get_meta("w3_source_tile", Vector2i(-1, -1)) if node != null else Vector2i(-1, -1)
		var shd_values: Array[int] = []
		for sy in 4:
			for sx in 4:
				var index: int = (p.y * 4 + sy) * shd_w + p.x * 4 + sx
				shd_values.append(int(shd[index]) if index >= 0 and index < shd.size() else -1)
		printerr("AREA114 tile=%s packed=%s cull=%s romp=%s layer_cliff=%s entrance=%s claimed=%s corners=%s transition=%s model=%s source=%s shd=%s" % [
			p, tile_data.get_pixel(p.x, p.y), terrain._should_cull_ground(p.x, p.y), terrain._tile_is_romp(p.x, p.y),
			terrain._tile_has_layer_cliff(p.x, p.y), terrain._is_ramp_entrance(p.x, p.y), cliffs._ramp_claimed_tiles.has(p),
			corners, transition, model, source_tile, shd_values,
		])
		var center := sampler.tile_center_world(p)
		var nearby: Array = []
		for doodad in doodads:
			var delta := Vector2(doodad.position.x - center.x, doodad.position.z - center.z)
			if delta.length() <= 320.0:
				nearby.append("%s@%.0f:%s" % [doodad.id, delta.length(), catalog.resolve_model(doodad.id, false, doodad.variation)])
		printerr("  DOODADS %s" % [nearby])

	for placement in [Vector2i(70, 113), Vector2i(77, 113)]:
		var node = cliffs._nodes_by_tile.get(placement, null)
		if node == null:
			printerr("AREA114_MODEL placement=%s MISSING" % placement)
			continue
		printerr("AREA114_MODEL placement=%s source=%s model=%s position=%s" % [
			placement, node.get_meta("w3_source_tile", Vector2i(-1, -1)),
			node.get_meta("w3_model_rel", ""), node.position,
		])
		for mesh_node in node.find_children("*", "MeshInstance3D", true, false):
			printerr("  AABB %s" % [(mesh_node as MeshInstance3D).mesh.get_aabb()])

	for y in range(112, 117):
		var row: Array[String] = []
		for x in range(68, 80):
			var code := "."
			if terrain._should_cull_ground(x, y): code = "C"
			if terrain._tile_is_romp(x, y): code = "R"
			if cliffs._nodes_by_tile.has(Vector2i(x, y)): code += "N"
			if cliffs._ramp_claimed_tiles.has(Vector2i(x, y)): code += "K"
			row.append("%d:%s" % [x, code])
		printerr("AREA114_ROW y=%d %s" % [y, " ".join(row)])
	quit(0)
