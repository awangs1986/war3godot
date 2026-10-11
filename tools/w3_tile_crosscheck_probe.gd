extends SceneTree
## Data probe for terrain/cliff/shadow cross-checks against HiveWE/Warsmash.
## Usage: --script res://tools/w3_tile_crosscheck_probe.gd -- <tile_x> <tile_y>

const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3GpuTerrain = preload("res://addons/w3kit/w3_gpu_terrain.gd")
const W3CliffBuilder = preload("res://addons/w3kit/w3_cliff_builder.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
const W3DooParser = preload("res://addons/w3kit/w3_doo_parser.gd")


func _initialize() -> void:
	call_deferred("_run")


func _read_bytes(path: String) -> PackedByteArray:
	var file := FileAccess.open(ProjectSettings.globalize_path(path), FileAccess.READ)
	return file.get_buffer(file.get_length()) if file != null else PackedByteArray()


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var target := Vector2i(
		int(args[0]) if args.size() > 0 else 44,
		int(args[1]) if args.size() > 1 else 65
	)
	var level = W3EMap.load_file(ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.w3e"))
	if level == null:
		printerr("CROSSCHECK_FAILED W3E")
		quit(1)
		return
	var provider := W3TextureProvider.new()
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
	for y in range(maxi(0, target.y - 4), mini(level.tile_count_y(), target.y + 5)):
		for x in range(maxi(0, target.x - 4), mini(level.tile_count_x(), target.x + 5)):
			cliffs._try_spawn_cliff(x, y)

	var shd := _read_bytes("res://war3/maps/LostTemple/source/war3map.shd")
	var shd_w: int = int(level.tile_count_x()) * 4
	for y in range(target.y - 2, target.y + 3):
		for x in range(target.x - 2, target.x + 3):
			if x < 0 or y < 0 or x >= level.tile_count_x() or y >= level.tile_count_y():
				continue
			var p := Vector2i(x, y)
			var corners: Array[String] = []
			for cp in [p, p + Vector2i(1, 0), p + Vector2i(0, 1), p + Vector2i(1, 1)]:
				var c = level.get_corner(cp.x, cp.y)
				corners.append("h%d/r%d/g%d/c%d/z%.3f" % [
					int(c.layer_height), int(c.ramp), int(c.ground_texture),
					int(c.cliff_texture), float(c.ground_height),
				])
			var shd_min := 255
			var shd_max := 0
			var shd_nonzero := 0
			for sy in 4:
				for sx in 4:
					var index: int = (y * 4 + sy) * shd_w + x * 4 + sx
					var value := int(shd[index]) if index >= 0 and index < shd.size() else -1
					if value >= 0:
						shd_min = mini(shd_min, value)
						shd_max = maxi(shd_max, value)
						if value != 0: shd_nonzero += 1
			var transition: Dictionary = level.get_ramp_transition(x, y)
			var node = cliffs._nodes_by_tile.get(p, null)
			printerr("CROSS tile=%s packed=%s cull=%s romp=%s cliff=%s entrance=%s transition=%s node=%s shd=%d..%d nz=%d corners=%s" % [
				p, tile_data.get_pixel(x, y), terrain._should_cull_ground(x, y),
				terrain._tile_is_romp(x, y), level.tile_has_layer_cliff(x, y),
				level.is_ramp_entrance(x, y), transition,
				str(node.get_meta("w3_model_rel", "")) if node != null else "",
				shd_min, shd_max, shd_nonzero, corners,
			])

	for placement in cliffs._nodes_by_tile:
		if (placement as Vector2i).distance_squared_to(target) > 16:
			continue
		var node = cliffs._nodes_by_tile[placement]
		var aabbs: Array[String] = []
		for mesh_node in node.find_children("*", "MeshInstance3D", true, false):
			aabbs.append(str((mesh_node as MeshInstance3D).mesh.get_aabb()))
		printerr("CROSS model placement=%s source=%s rel=%s pos=%s aabb=%s" % [
			placement, node.get_meta("w3_source_tile", Vector2i(-1, -1)),
			node.get_meta("w3_model_rel", ""), node.position, aabbs,
		])
	var sampler := W3HeightSampler.new(level)
	var center: Vector3 = sampler.tile_center_world(target)
	var placements: Array = W3DooParser.parse_doodads(_read_bytes("res://war3/maps/LostTemple/source/war3map.doo"))
	placements.append_array(W3DooParser.parse_units(_read_bytes("res://war3/maps/LostTemple/source/war3mapUnits.doo")))
	for item in placements:
		var distance := Vector2(item.position.x - center.x, item.position.z - center.z).length()
		if distance <= 640.0:
			printerr("CROSS object id=%s unit=%s distance=%.1f pos=%s" % [
				item.id, item.is_unit, distance, item.position,
			])
	quit(0)
