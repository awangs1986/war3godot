extends SceneTree
## Find tiles good for UV-orientation QA: exactly one corner differs (single-corner overlay).
func _init() -> void:
	preload("res://addons/w3kit_cs/IO/W3BinaryReaderBridge.cs")
	preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")
	preload("res://addons/w3kit_cs/Native/W3CornerData.cs")
	var W3EMap = preload("res://addons/w3kit_cs/Formats/W3EMapBridge.cs")
	var W3MapArchive = preload("res://addons/w3kit_cs/Archives/W3MapArchiveBridge.cs")
	var arch = W3MapArchive.new()
	arch.open(ProjectSettings.globalize_path("res://war3/maps/DotA_v6.80c"))
	var map = W3EMap.load_file(arch.map_dir.path_join("war3map.w3e"))
	print("tilesets=", map.ground_tilesets)
	print("size=", map.tile_count_x(), "x", map.tile_count_y())
	# Collect examples: only one corner differs from the other three.
	var by_corner := {"BL": [], "BR": [], "TL": [], "TR": []}
	var mid := Vector2i(map.tile_count_x() / 2, map.tile_count_y() / 2)
	for y in map.tile_count_y():
		for x in map.tile_count_x():
			var bl = map.get_corner(x, y).ground_texture
			var br = map.get_corner(x + 1, y).ground_texture
			var tl = map.get_corner(x, y + 1).ground_texture
			var tr = map.get_corner(x + 1, y + 1).ground_texture
			var vals = [bl, br, tl, tr]
			var uniq: Array = []
			for v in vals:
				if not uniq.has(v):
					uniq.append(v)
			if uniq.size() != 2:
				continue
			# Exactly one corner is the minority texture.
			var a: int = uniq[0]
			var b: int = uniq[1]
			var ca := 0
			var cb := 0
			for v in vals:
				if v == a: ca += 1
				else: cb += 1
			if ca != 1 and cb != 1:
				continue
			var minority: int = a if ca == 1 else b
			var which := ""
			if bl == minority: which = "BL"
			elif br == minority: which = "BR"
			elif tl == minority: which = "TL"
			elif tr == minority: which = "TR"
			var dist: int = absi(x - mid.x) + absi(y - mid.y)
			by_corner[which].append({"x": x, "y": y, "d": dist, "maj": a if ca == 3 else b, "min": minority, "bl": bl, "br": br, "tl": tl, "tr": tr})
	for k in ["BL", "BR", "TL", "TR"]:
		var arr: Array = by_corner[k]
		arr.sort_custom(func(p, q): return int(p["d"]) < int(q["d"]))
		print("--- single-corner ", k, " count=", arr.size())
		for i in mini(5, arr.size()):
			var e: Dictionary = arr[i]
			print("  tile %d,%d  corners BL,BR,TL,TR=%d,%d,%d,%d  minority=%d at %s" % [
				e["x"], e["y"], e["bl"], e["br"], e["tl"], e["tr"], e["min"], k
			])
	quit(0)
