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
	print("tile A* path len=", path_tiles.size())
	if path_tiles.is_empty():
		push_error("tile A* path empty")
		quit(1)
		return

	# Warsmash WPM pathing (Adrenaline if present, else synthesized).
	var wpf = W3WarsmashPathfinder.new(samp, null)
	print("warsmash pathfinder source=", wpf.source_label, " grid=", wpf.pathing_grid.width, "x", wpf.pathing_grid.height)
	var adr := ProjectSettings.globalize_path("res://war3/maps/Adrenaline/war3map.wpm")
	if FileAccess.file_exists(adr):
		var wpm = W3Wpm.load_file(adr)
		if wpm:
			var adr_map = W3EMap.load_file(ProjectSettings.globalize_path("res://war3/maps/Adrenaline/war3map.w3e"))
			if adr_map:
				var adr_samp = W3HeightSampler.new(adr_map)
				wpf = W3WarsmashPathfinder.new(adr_samp, wpm)
				print("loaded Adrenaline wpm ", wpm.width, "x", wpm.height)
				var a: Vector3 = adr_samp.tile_center_world(Vector2i(adr_map.tile_count_x() / 2, adr_map.tile_count_y() / 2))
				var b: Vector3 = adr_samp.tile_center_world(Vector2i(adr_map.tile_count_x() / 2 + 4, adr_map.tile_count_y() / 2 + 3))
				var wp: Array = wpf.find_world_path(a, b, 16.0)
				print("warsmash world path len=", wp.size())
				if wp.is_empty():
					push_error("warsmash path empty on Adrenaline")
					quit(1)
					return
	else:
		var a2: Vector3 = samp.tile_center_world(mid)
		var b2: Vector3 = samp.tile_center_world(mid + Vector2i(3, 2))
		var wp2: Array = wpf.find_world_path(a2, b2, 16.0)
		print("synthesized warsmash path len=", wp2.size())

	var arch = W3MapArchive.new()
	var sample := ProjectSettings.globalize_path("res://testdata/hivewe_sample")
	var ok: bool = arch.open(sample)
	print("archive open=", ok, " dir=", arch.map_dir, " err=", arch.extract_error)

	# MDX Stand/Walk — requires unpacked asset root (optional if missing).
	var mdx_rel := "Units/Human/Footman/Footman.mdx"
	var mdx_abs: String = W3Paths.resolve_path(mdx_rel)
	if mdx_abs.is_empty() or not FileAccess.file_exists(mdx_abs):
		print("MDX skip (no Footman at asset root) — configure W3AssetConfig")
	else:
		var model = W3MdxModel.load_file(mdx_abs)
		if model == null:
			push_error("Footman MDX failed to load")
			quit(1)
			return
		var stand_i: int = model.find_sequence("Stand")
		var walk_i: int = model.find_sequence("Walk")
		print("MDX ok: geosets=", model.geosets.size(), " Stand=", stand_i, " Walk=", walk_i)
		if stand_i < 0 and walk_i < 0:
			push_error("Footman missing Stand/Walk sequences")
			quit(1)
			return

	print("SMOKE_OK")
	quit(0)
