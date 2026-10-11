extends SceneTree

const W3GpuTerrain = preload("res://addons/w3kit_cs/Terrain/WarsmashGroundRenderer.cs")
const W3TextureProvider = preload("res://addons/w3kit_cs/Assets/W3TextureProvider.cs")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var level = ResourceLoader.load("res://war3/maps/LostTemple/lost_temple.res", "", ResourceLoader.CACHE_MODE_IGNORE)
	var provider = W3TextureProvider.new()
	provider.load_terrain_slk("")
	var terrain = W3GpuTerrain.new()
	terrain.map = level
	terrain.textures = provider
	terrain._layer_ids = level.ground_tilesets.duplicate()
	terrain._prepare_blight()
	terrain._prepare_cliff_to_ground()
	terrain._prepare_romp_tiles()

	var atlas: Image = provider.get_ground_image("Lgrs")
	if atlas == null:
		printerr("W3_PROBLEM_TILE_PROBE_FAILED missing Lgrs")
		quit(1)
		return
	if atlas.get_format() != Image.FORMAT_RGBA8:
		atlas = atlas.duplicate()
		atlas.convert(Image.FORMAT_RGBA8)
	atlas.save_png("res://.godot/w3_lgrs_atlas.png")
	var cell := terrain._slice_cell(atlas, 28, true)
	var alpha_min := 1.0
	var alpha_max := 0.0
	for y in cell.get_height():
		for x in cell.get_width():
			var alpha := cell.get_pixel(x, y).a
			alpha_min = minf(alpha_min, alpha)
			alpha_max = maxf(alpha_max, alpha)
	cell.resize(512, 512, Image.INTERPOLATE_NEAREST)
	cell.save_png("res://.godot/w3_lgrs_cell_28.png")

	# Build arrays once so extended flags match the real runtime, then report the
	# exact packed layers and cliff ownership at the three regression coordinates.
	terrain._build_ground_arrays()
	var tile_data: Image = terrain._build_tile_data(level.tile_count_x(), level.tile_count_y())
	for p in [Vector2i(43, 99), Vector2i(43, 60), Vector2i(54, 73), Vector2i(54, 86), Vector2i(43, 77), Vector2i(43, 82)]:
		printerr("PROBE %d,%d packed=%s cull=%s romp=%s layer_cliff=%s ramp_entrance=%s" % [
			p.x, p.y, tile_data.get_pixel(p.x, p.y), terrain._should_cull_ground(p.x, p.y),
			terrain._tile_is_romp(p.x, p.y), terrain._tile_has_layer_cliff(p.x, p.y),
			terrain._is_ramp_entrance(p.x, p.y),
		])
	for y in range(98, 102):
		var row: Array[String] = []
		for x in range(42, 46):
			var c = level.get_corner(x, y)
			row.append("%d:v%d" % [int(c.ground_texture), int(c.ground_variation)])
		printerr("FLAT_NEIGHBOR y=%d %s" % [y, " ".join(row)])
	printerr("W3_PROBLEM_TILE_PROBE_OK atlas=%dx%d cell28_alpha=[%.3f,%.3f]" % [
		atlas.get_width(), atlas.get_height(), alpha_min, alpha_max,
	])
	quit(0)
