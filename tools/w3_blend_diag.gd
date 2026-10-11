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
	var hist := {}
	var mixed := 0
	var samples: Array = []
	for y in mini(map.tile_count_y(), 128):
		for x in mini(map.tile_count_x(), 128):
			var layers: Array = t._tile_texture_layers(x, y)
			if layers.size() > 1:
				mixed += 1
				var v1: int = int(layers[1]["variation"])
				hist[v1] = int(hist.get(v1, 0)) + 1
				if samples.size() < 8:
					samples.append([x, y, layers])
	print("mixed_tiles=", mixed)
	print("overlay_variation_hist=", hist)
	for s in samples:
		print("sample ", s)
	# open-realm GetTile style for same samples
	for s in samples:
		var x: int = s[0]
		var y: int = s[1]
		var c00 = map.get_corner(x, y)
		var c10 = map.get_corner(x + 1, y)
		var c01 = map.get_corner(x, y + 1)
		var c11 = map.get_corner(x + 1, y + 1)
		var g = [
			int(c11.ground_texture), # TR open-realm mv[0]
			int(c01.ground_texture), # TL mv[1]
			int(c10.ground_texture), # BR mv[2]
			int(c00.ground_texture), # BL mv[3]
		]
		print("corners BL,BR,TL,TR=", [int(c00.ground_texture), int(c10.ground_texture), int(c01.ground_texture), int(c11.ground_texture)])
		for layer in 4:
			var bitset := 0
			if layer == 0:
				bitset = 15
			else:
				bitset = (4 if g[0] >= layer else 0) + (8 if g[1] >= layer else 0) + (1 if g[2] >= layer else 0) + (2 if g[3] >= layer else 0)
			print("  open-realm layer", layer, " tile=", bitset)
	quit(0)
