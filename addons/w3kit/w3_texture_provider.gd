extends RefCounted
const W3BlpDecoder = preload("res://addons/w3kit/w3_blp_decoder.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3TerrainSlk = preload("res://addons/w3kit/w3_terrain_slk.gd")
## Resolves BLP/PNG from map archive first, then game asset root.
## Ground tiles use Terrain.slk (tileID → dir/file).
## Cliff → ground uses CliffTypes.slk groundTile (Warsmash/HiveWE).


var _map_dir: String = ""
var _cache: Dictionary = {}
var _cliff_ground_by_id: Dictionary = {} # "CLgr" -> "Lgrs" from CliffTypes.slk
var terrain_slk = W3TerrainSlk.new()
## cliffID → ground tileID (e.g. CLgr → Lgrs). Empty if SLK missing.
var _cliff_ground: Dictionary = {}


func set_map_dir(path: String) -> void:
	_map_dir = path.rstrip("/")


func load_terrain_slk(asset_root: String = "") -> void:
	var root := asset_root
	if root.is_empty():
		root = W3Paths.asset_root_path()
	terrain_slk.load_from_asset_root(root)
	_load_cliff_ground_tiles(root)


func get_cliff_ground_tile(cliff_id: String) -> String:
	return str(_cliff_ground_by_id.get(cliff_id, ""))


func _load_cliff_ground_tiles(asset_root: String) -> void:
	_cliff_ground_by_id.clear()
	if asset_root.is_empty():
		return
	var path := asset_root.path_join("TerrainArt/CliffTypes.slk")
	if not FileAccess.file_exists(path):
		path = _find_ci(asset_root, "TerrainArt/CliffTypes.slk")
	if path.is_empty() or not FileAccess.file_exists(path):
		push_warning("W3TextureProvider: CliffTypes.slk missing under %s" % asset_root)
		return
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return
	_parse_cliff_ground_tiles(f.get_as_text())
	print("W3TextureProvider: loaded %d cliff ground mappings" % _cliff_ground_by_id.size())


func _parse_cliff_ground_tiles(text: String) -> void:
	var cur_y := -1
	var row: Dictionary = {}
	var id_x := -1
	var ground_x := -1
	for raw_line in text.split("\n"):
		var line := raw_line.strip_edges().trim_suffix("\r")
		if not line.begins_with("C;"):
			continue
		var y := _slk_int(line, "Y")
		var x := _slk_int(line, "X")
		var value := _slk_str(line, "K").trim_prefix("\"").trim_suffix("\"")
		if y > 0 and y != cur_y:
			_commit_cliff_ground_row(row, id_x, ground_x)
			cur_y = y
			row = {}
		if x <= 0:
			continue
		if cur_y == 1:
			if value == "cliffID":
				id_x = x
			elif value == "groundTile":
				ground_x = x
		elif cur_y > 1:
			row[x] = value
	_commit_cliff_ground_row(row, id_x, ground_x)


func _commit_cliff_ground_row(row: Dictionary, id_x: int, ground_x: int) -> void:
	if id_x < 0 or ground_x < 0 or not row.has(id_x):
		return
	var cliff_id := str(row[id_x])
	var ground_id := str(row.get(ground_x, ""))
	if not cliff_id.is_empty() and not ground_id.is_empty():
		_cliff_ground_by_id[cliff_id] = ground_id


func get_texture(rel_or_abs: String) -> Texture2D:
	var key = rel_or_abs.replace("\\", "/").to_lower()
	if _cache.has(key):
		return _cache[key]
	var img = _load_image(rel_or_abs)
	var tex: Texture2D
	if img != null:
		# Mipmaps needed for LINEAR_WITH_MIPMAPS terrain filtering.
		# Reforged DDS files already carry compressed mip chains; attempting to
		# regenerate them is invalid in Godot and discards the authored chain.
		if not img.is_compressed() and not img.has_mipmaps() and img.get_width() >= 64 and img.get_height() >= 64:
			img.generate_mipmaps()
		tex = ImageTexture.create_from_image(img)
	else:
		tex = _fallback_tex()
	_cache[key] = tex
	return tex


func get_ground_texture(tileset_id: String) -> Texture2D:
	var rel := terrain_slk.resolve_blp_rel(tileset_id)
	var candidates: PackedStringArray = _ground_candidates(rel)
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
	var candidates: PackedStringArray = _ground_candidates(rel)
	candidates.append(rel.get_basename() + ".png")
	var folder_guess := _tileset_folder(tileset_id.left(1) if tileset_id.length() > 0 else "L")
	candidates.append("TerrainArt/%s/%s.blp" % [folder_guess, tileset_id])
	for c in candidates:
		if _exists_any(c) or _exists_any(c.get_basename() + ".png"):
			var img: Image = _load_image(c)
			if img != null:
				_prepare_editable_image(img)
				return img
	var img2: Image = _load_image(rel)
	if img2 != null:
		_prepare_editable_image(img2)
	return img2


func get_blight_image(tileset_letter: String) -> Image:
	var rel := get_blight_rel(tileset_letter)
	for candidate in _ground_candidates(rel):
		var image := _load_image(candidate)
		if image != null:
			_prepare_editable_image(image)
			return image
	return null


func _prepare_editable_image(image: Image) -> void:
	# DDS decoders retain BC compression. Terrain atlases are sliced and resized
	# on CPU, which Godot only permits after explicit decompression.
	if image.is_compressed():
		image.decompress()
	if image.get_format() != Image.FORMAT_RGBA8:
		image.convert(Image.FORMAT_RGBA8)


## Classic BLP/PNG first, Reforged `name_diffuse.dds` as fallback — same order
## as the editor's EditorAssets and HiveWE's classic GroundTexture. The Reforged
## 0-15 transition cells carry different alpha masks, so mixing sources breaks
## pixel parity with the editor/HiveWE (2026-07-23, unified per user).
func _ground_candidates(classic_rel: String) -> PackedStringArray:
	var candidates := PackedStringArray()
	candidates.append(classic_rel)
	if W3Paths.has_reforged_cache():
		candidates.append(classic_rel.get_basename() + "_diffuse.dds")
	return candidates


func get_blight_rel(tileset_letter: String) -> String:
	var by_tileset := {
		"L": "TerrainArt/Blight/Lords_Blight.blp",
		"F": "TerrainArt/Blight/Lordf_Blight.blp",
		"W": "TerrainArt/Blight/Lordw_Blight.blp",
		"B": "TerrainArt/Blight/Barrens_Blight.blp",
		"A": "TerrainArt/Blight/Ashen_Blight.blp",
		"C": "TerrainArt/Blight/Felwood_Blight.blp",
		"N": "TerrainArt/Blight/North_Blight.blp",
		"Y": "TerrainArt/Blight/Village_Blight.blp",
		"X": "TerrainArt/Blight/Village_Blight.blp",
		"V": "TerrainArt/Blight/Village_Blight.blp",
		"Q": "TerrainArt/Blight/VillageFall_Blight.blp",
		"D": "TerrainArt/Blight/Dungeon_Blight.blp",
		"G": "TerrainArt/Blight/G_Blight.blp",
		"Z": "TerrainArt/Blight/Ruins_Blight.blp",
		"I": "TerrainArt/Blight/Ice_Blight.blp",
		"J": "TerrainArt/Blight/DRuins_Blight.blp",
		"O": "TerrainArt/Blight/Outland_Blight.blp",
		"K": "TerrainArt/Blight/Citadel_Blight.blp",
	}
	return str(by_tileset.get(tileset_letter.left(1), by_tileset["L"]))


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
			var resolved: String = W3Paths.resolve_game_path(norm)
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
		if not W3Paths.resolve_game_path(norm).is_empty():
			return true
		if not W3Paths.resolve_game_path(png).is_empty():
			return true
		var root := W3Paths.asset_root_path()
		if FileAccess.file_exists(root.path_join(norm)) or FileAccess.file_exists(root.path_join(png)):
			return true
	return false


func _slk_int(line: String, key: String) -> int:
	var start := 0
	while true:
		var i := line.find(key, start)
		if i < 0:
			return -1
		if i > 0 and line[i - 1] != ";":
			start = i + 1
			continue
		var j := i + key.length()
		if j >= line.length() or line[j] < "0" or line[j] > "9":
			start = i + 1
			continue
		var k := j
		while k < line.length() and line[k] >= "0" and line[k] <= "9":
			k += 1
		return int(line.substr(j, k - j))
	return -1


func _slk_str(line: String, key: String) -> String:
	var start := 0
	while true:
		var i := line.find(key, start)
		if i < 0:
			return ""
		var after := i + key.length()
		if after >= line.length():
			return ""
		var ch := line[after]
		if i > 0 and line[i - 1] == ";" and (ch == "\"" or ch == "-" or (ch >= "0" and ch <= "9")):
			return line.substr(after).strip_edges()
		start = i + 1
	return ""


func _find_ci(root: String, rel: String) -> String:
	var cur := root
	for part in rel.split("/"):
		if part.is_empty():
			continue
		var dir := DirAccess.open(cur)
		if dir == null:
			return ""
		var matched := ""
		dir.list_dir_begin()
		var file_name := dir.get_next()
		while file_name != "":
			if file_name != "." and file_name != ".." and file_name.to_lower() == part.to_lower():
				matched = file_name
				break
			file_name = dir.get_next()
		dir.list_dir_end()
		if matched.is_empty():
			return ""
		cur = cur.path_join(matched)
	return cur


func _fallback_tex() -> Texture2D:
	var img = Image.create(64, 64, false, Image.FORMAT_RGBA8)
	for y in 64:
		for x in 64:
			var on = ((x >> 3) + (y >> 3)) % 2 == 0
			img.set_pixel(x, y, Color(0.4, 0.55, 0.3) if on else Color(0.3, 0.42, 0.22))
	return ImageTexture.create_from_image(img)
