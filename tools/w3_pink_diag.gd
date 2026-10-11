extends SceneTree
func _init() -> void:
	preload("res://addons/w3kit/w3_binary_reader.gd")
	preload("res://addons/w3kit/w3_paths.gd")
	preload("res://addons/w3kit/w3_corner.gd")
	var W3EMap = preload("res://addons/w3kit/w3e_map.gd")
	var W3MapArchive = preload("res://addons/w3kit/w3_map_archive.gd")
	var W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
	var W3GpuTerrain = preload("res://addons/w3kit/w3_gpu_terrain.gd")
	var arch = W3MapArchive.new()
	arch.open(ProjectSettings.globalize_path("res://war3/maps/DotA_v6.80c"))
	var map = W3EMap.load_file(arch.map_dir.path_join("war3map.w3e"))
	var tp = W3TextureProvider.new()
	tp.set_map_dir(arch.map_dir)
	tp.load_terrain_slk()
	var t = W3GpuTerrain.new()
	t.build(map, tp, arch.read_file("war3map.shd"))
	print("layers=", t._layer_ids.size(), " mats=", t._mats.size(), " mmis=", t._mmis.size())
	print("tilesets=", t._layer_ids)
	# Spot-check per-tile blend coverage (overlays present = soft stitches).
	var soft := 0
	var td := t._build_tile_data(min(32, map.tile_count_x()), min(32, map.tile_count_y()))
	for y in td.get_height():
		for x in td.get_width():
			var px := td.get_pixel(x, y)
			# any overlay channel (g/b/a) below EMPTY sentinel = a real blend layer
			if px.g < t.EMPTY_LAYER - 1.0 or px.b < t.EMPTY_LAYER - 1.0 or px.a < t.EMPTY_LAYER - 1.0:
				soft += 1
	print("tiles_with_overlay_in_32x32=", soft)
	quit(0)
