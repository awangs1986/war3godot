extends RefCounted
const W3BlpDecoder = preload("res://addons/w3kit/w3_blp_decoder.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3TerrainSlk = preload("res://addons/w3kit/w3_terrain_slk.gd")
## Resolves BLP/PNG from map archive first, then game asset root.
## Ground tiles use Terrain.slk (tileID → dir/file).


var _map_dir: String = ""
var _cache: Dictionary = {}
var terrain_slk = W3TerrainSlk.new()


func set_map_dir(path: String) -> void:
	_map_dir = path.rstrip("/")


func load_terrain_slk(asset_root: String = "") -> void:
	var root := asset_root
	if root.is_empty():
		root = W3Paths.asset_root_path()
	terrain_slk.load_from_asset_root(root)


func get_texture(rel_or_abs: String) -> Texture2D:
	var key = rel_or_abs.replace("\\", "/").to_lower()
	if _cache.has(key):
		return _cache[key]
	var img = _load_image(rel_or_abs)
	var tex: Texture2D
	if img != null:
		# Mipmaps needed for LINEAR_WITH_MIPMAPS terrain filtering.
		if img.get_width() >= 64 and img.get_height() >= 64:
			img.generate_mipmaps()
		tex = ImageTexture.create_from_image(img)
	else:
		tex = _fallback_tex()
	_cache[key] = tex
	return tex


func get_ground_texture(tileset_id: String) -> Texture2D:
	var rel := terrain_slk.resolve_blp_rel(tileset_id)
	var candidates: PackedStringArray = PackedStringArray([rel])
	candidates.append(rel.get_basename() + ".png")
	var folder_guess := _tileset_folder(tileset_id.left(1) if tileset_id.length() > 0 else "L")
	candidates.append("TerrainArt/%s/%s.blp" % [folder_guess, tileset_id])
	for c in candidates:
		if _exists_any(c) or _exists_any(c.get_basename() + ".png"):
			return get_texture(c)
	return get_texture(rel)


## Raw RGBA Image for classic Warsmash ground slicing — no mipmaps, no ImageTexture roundtrip.
func get_ground_image(tileset_id: String) -> Image:
	var rel := terrain_slk.resolve_blp_rel(tileset_id)
	var candidates: PackedStringArray = PackedStringArray([rel])
	candidates.append(rel.get_basename() + ".png")
	var folder_guess := _tileset_folder(tileset_id.left(1) if tileset_id.length() > 0 else "L")
	candidates.append("TerrainArt/%s/%s.blp" % [folder_guess, tileset_id])
	for c in candidates:
		if _exists_any(c) or _exists_any(c.get_basename() + ".png"):
			var img: Image = _load_image(c)
			if img != null:
				if img.get_format() != Image.FORMAT_RGBA8:
					img.convert(Image.FORMAT_RGBA8)
				return img
	var img2: Image = _load_image(rel)
	if img2 != null and img2.get_format() != Image.FORMAT_RGBA8:
		img2.convert(Image.FORMAT_RGBA8)
	return img2


func _tileset_folder(letter: String) -> String:
	match letter:
		"L":
			return "LordaeronSummer"
		"F":
			return "LordaeronFall"
		"W":
			return "LordaeronWinter"
		"B":
			return "Barrens"
		"A":
			return "Ashenvale"
		"C":
			return "Felwood"
		"N":
			return "Northrend"
		"Y":
			return "Cityscape"
		"X":
			return "Dalaran"
		"V":
			return "Village"
		"Q":
			return "VillageFall"
		"D":
			return "Dungeon"
		"G":
			return "Underground"
		"Z":
			return "Ruins"
		"I":
			return "Icecrown"
		"J":
			return "DalaranRuins"
		"O":
			return "Outland"
		"K":
			return "BlackCitadel"
		_:
			return "LordaeronSummer"


func _load_image(rel_or_abs: String) -> Image:
	var norm = rel_or_abs.replace("\\", "/")
	var paths: PackedStringArray = PackedStringArray()
	if norm.begins_with("/") or (norm.length() > 2 and norm[1] == ":"):
		paths.append(norm)
	else:
		if not _map_dir.is_empty():
			paths.append(_map_dir.path_join(norm))
			paths.append(_map_dir.path_join(norm.get_basename() + ".png"))
		if W3Paths.has_root():
			var resolved: String = W3Paths.resolve_path(norm)
			if not resolved.is_empty():
				paths.append(resolved)
				paths.append(resolved.get_basename() + ".png")
			paths.append(W3Paths.asset_root_path().path_join(norm))
			paths.append(W3Paths.asset_root_path().path_join(norm.get_basename() + ".png"))
	for p in paths:
		var img = W3BlpDecoder.load_image(p)
		if img != null:
			return img
	return null


func _exists_any(rel: String) -> bool:
	var norm = rel.replace("\\", "/")
	var png = norm.get_basename() + ".png"
	if not _map_dir.is_empty():
		if FileAccess.file_exists(_map_dir.path_join(norm)) or FileAccess.file_exists(_map_dir.path_join(png)):
			return true
	if W3Paths.has_root():
		if not W3Paths.resolve_path(norm).is_empty():
			return true
		if not W3Paths.resolve_path(png).is_empty():
			return true
		var root := W3Paths.asset_root_path()
		if FileAccess.file_exists(root.path_join(norm)) or FileAccess.file_exists(root.path_join(png)):
			return true
	return false


func _fallback_tex() -> Texture2D:
	var img = Image.create(64, 64, false, Image.FORMAT_RGBA8)
	for y in 64:
		for x in 64:
			var on = ((x >> 3) + (y >> 3)) % 2 == 0
			img.set_pixel(x, y, Color(0.4, 0.55, 0.3) if on else Color(0.3, 0.42, 0.22))
	return ImageTexture.create_from_image(img)
