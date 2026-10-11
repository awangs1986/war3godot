extends SceneTree
## godot --headless --path . -s res://tools/w3_shd_smoke.gd


func _init() -> void:
	preload("res://addons/w3kit/w3_binary_reader.gd")
	preload("res://addons/w3kit/w3_paths.gd")
	preload("res://addons/w3kit/w3_corner.gd")
	var W3EMap = preload("res://addons/w3kit/w3e_map.gd")
	var W3Shd = preload("res://addons/w3kit/w3_shd.gd")
	var W3MapArchive = preload("res://addons/w3kit/w3_map_archive.gd")
	var W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
	var W3GpuTerrain = preload("res://addons/w3kit/w3_gpu_terrain.gd")

	var map_dir := ProjectSettings.globalize_path("res://war3/maps/DotA_v6.80c")
	var arch = W3MapArchive.new()
	if not arch.open(map_dir):
		push_error("map open fail")
		quit(1)
		return
	var map = W3EMap.load_file(arch.map_dir.path_join("war3map.w3e"))
	var shd: PackedByteArray = arch.read_file("war3map.shd")
	print("shd_bytes=", shd.size(), " expect=", W3Shd.expected_size(map.tile_count_x(), map.tile_count_y()))
	var tex = W3Shd.load_texture(shd, map.tile_count_x(), map.tile_count_y())
	print("shd_tex=", tex != null, " size=", tex.get_width() if tex else 0, "x", tex.get_height() if tex else 0)
	var tp = W3TextureProvider.new()
	tp.set_map_dir(arch.map_dir)
	tp.load_terrain_slk()
	var terrain = W3GpuTerrain.new()
	terrain.build(map, tp, shd)
	print("SHD_SMOKE_OK")
	quit(0)
