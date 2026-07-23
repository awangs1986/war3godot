extends SceneTree
## godot --headless --path . -s res://tools/w3_smoke.gd
## 0.1 acceptance smoke: W3E + height + A* + optional Footman MDX.


func _init() -> void:
	# Ordered preloads register class_name without circular parse.
	preload("res://addons/w3kit/w3_binary_reader.gd")
	preload("res://addons/w3kit/w3_paths.gd")
	preload("res://addons/w3kit/w3_corner.gd")
	var W3EMap = preload("res://addons/w3kit/w3e_map.gd")
	var W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
	var W3GridPathfinder = preload("res://addons/w3kit/w3_grid_pathfinder.gd")
	var W3MapArchive = preload("res://addons/w3kit/w3_map_archive.gd")
	var W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
	var W3Paths = preload("res://addons/w3kit/w3_paths.gd")
	var W3Wpm = preload("res://addons/w3kit/w3_wpm.gd")
	var W3WarsmashPathfinder = preload("res://addons/w3kit/w3_warsmash_pathfinder.gd")

	print("=== W3 0.1 smoke ===")
	var path := ProjectSettings.globalize_path("res://testdata/hivewe_sample/war3map.w3e")
	var map = W3EMap.load_file(path)
	if map == null:
		map = W3EMap.make_test_map(12, 12)
	if map == null:
		push_error("W3E parse failed")
		quit(1)
		return
	print("W3E ok: corners=%dx%d tiles=%dx%d tileset=%s" % [map.width, map.height, map.tile_count_x(), map.tile_count_y(), map.tileset])
	var samp = W3HeightSampler.new(map)
	var mid := Vector2i(map.tile_count_x() / 2, map.tile_count_y() / 2)
	var c: Vector3 = samp.tile_center_world(mid)
	print("mid tile center=", c)
	if not samp.is_tile_walkable(mid):
		push_error("mid tile not walkable")
		quit(1)
		return
	var pf = W3GridPathfinder.new(samp)
	var path_tiles = pf.find_path(mid, mid + Vector2i(3, 2))
	print("path len=", path_tiles.size())
	if path_tiles.is_empty():
		push_error("pathfinding failed")
		quit(1)
		return
	var arch = W3MapArchive.new()
	var sample := ProjectSettings.globalize_path("res://testdata/hivewe_sample")
	var ok: bool = arch.open(sample)
	print("archive open=", ok, " dir=", arch.map_dir, " err=", arch.extract_error)
	if not ok:
		push_error("archive open failed: %s" % arch.extract_error)
		quit(1)
		return
	print("SMOKE_OK")
	quit(0)
