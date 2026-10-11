extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var tx := int(args[0]) if args.size() > 0 else 78
	var ty := int(args[1]) if args.size() > 1 else 115
	var level = ResourceLoader.load("res://war3/maps/LostTemple/lost_temple.res", "", ResourceLoader.CACHE_MODE_IGNORE)
	if level == null:
		printerr("TILE_INSPECT_FAILED: resource missing")
		quit(1)
		return
	print("MAP size=%dx%d tilesets=%s cliffs=%s tileset=%s custom=%s" % [
		level.tile_count_x(), level.tile_count_y(), level.ground_tilesets, level.cliff_tilesets,
		level.tileset, level.custom_tileset,
	])
	print("TILE %d,%d" % [tx, ty])
	for p in [Vector2i(tx, ty), Vector2i(tx + 1, ty), Vector2i(tx, ty + 1), Vector2i(tx + 1, ty + 1)]:
		var c = level.get_corner(p.x, p.y)
		print(" corner %d,%d ground_tex=%d variation=%d cliff_tex=%d layer=%d flags=0x%02X ground=%.3f water=%.3f" % [
			p.x, p.y, c.ground_texture, c.ground_variation, c.cliff_texture, c.layer_height,
			level.corner_flags[level.width * p.y + p.x], c.ground_height, c.water_height,
		])
	var provider = preload("res://addons/w3kit_cs/Assets/W3TextureProvider.cs").new()
	provider.load_terrain_slk("")
	var used: Dictionary = {}
	for p in [Vector2i(tx, ty), Vector2i(tx + 1, ty), Vector2i(tx, ty + 1), Vector2i(tx + 1, ty + 1)]:
		var c = level.get_corner(p.x, p.y)
		var id := str(level.ground_tilesets[clampi(c.ground_texture, 0, level.ground_tilesets.size() - 1)])
		used[id] = provider.terrain_slk.resolve_blp_rel(id)
	var resolver = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs").new() # C# 桥的 resolve_path 是实例方法
	for id in used.keys():
		var rel: String = used[id]
		print(" texture id=%s rel=%s resolved=%s" % [id, rel, resolver.resolve_path(rel)])
	var terrain = preload("res://addons/w3kit_cs/Terrain/WarsmashGroundRenderer.cs").new()
	terrain.map = level
	terrain.textures = provider
	terrain._prepare_cliff_to_ground()
	terrain._tileset_extended = PackedByteArray([0, 0, 0, 0, 0, 1, 0])
	var tile_data: Image = terrain._build_tile_data(level.tile_count_x(), level.tile_count_y())
	print(" GPU tile_data=%s corner_layers=%s" % [tile_data.get_pixel(tx, ty), [
		terrain._corner_tex(tx, ty), terrain._corner_tex(tx + 1, ty),
		terrain._corner_tex(tx, ty + 1), terrain._corner_tex(tx + 1, ty + 1),
	]])
	quit(0)
