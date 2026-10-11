extends SceneTree
## godot --headless --path . -s res://tools/w3_shd_smoke.gd


func _init() -> void:
	preload("res://addons/w3kit_cs/IO/W3BinaryReaderBridge.cs")
	preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")
	preload("res://addons/w3kit_cs/Native/W3CornerData.cs")
	var W3EMap = preload("res://addons/w3kit_cs/Formats/W3EMapBridge.cs")
	var W3Shd = preload("res://addons/w3kit_cs/Formats/W3ShadowMapBridge.cs")
	var W3MapArchive = preload("res://addons/w3kit_cs/Archives/W3MapArchiveBridge.cs")
	var W3TextureProvider = preload("res://addons/w3kit_cs/Assets/W3TextureProvider.cs")
	var W3GpuTerrain = preload("res://addons/w3kit_cs/Terrain/WarsmashGroundRenderer.cs")

	var map_dir := ProjectSettings.globalize_path("res://war3/maps/DotA_v6.80c")
	var arch = W3MapArchive.new()
	if not arch.open(map_dir):
		push_error("map open fail")
		quit(1)
		return
	var map = W3EMap.load_file(arch.map_dir.path_join("war3map.w3e"))
	var shd: PackedByteArray = arch.read_file("war3map.shd")
	var shd_bridge = W3Shd.new() # C# 桥的 expected_size/load_texture 是实例方法，不能静态调用
	print("shd_bytes=", shd.size(), " expect=", shd_bridge.expected_size(map.tile_count_x(), map.tile_count_y()))
	var tex = shd_bridge.load_texture(shd, map.tile_count_x(), map.tile_count_y())
	print("shd_tex=", tex != null, " size=", tex.get_width() if tex else 0, "x", tex.get_height() if tex else 0)
	var tp = W3TextureProvider.new()
	tp.set_map_dir(arch.map_dir)
	tp.load_terrain_slk("") # C# 默认参数在 GDScript 侧不生效，必须显式传参
	var terrain = W3GpuTerrain.new()
	terrain.build(map, tp, shd)
	print("SHD_SMOKE_OK")
	quit(0)
