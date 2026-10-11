extends RefCounted
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
## Resolves unit/doodad fourcc → model path via SLK + war3map overrides.
## SLK cell format matches Terrain.slk: K"text" / K123 (no semicolon after K).


var _unit_models: Dictionary = {} # id -> path stem (no required .mdx)
var _doodad_models: Dictionary = {}
var _destructable_models: Dictionary = {}
## Optional replaceable tex stems from DestructableData texFile (id -> path).
var _destructable_tex: Dictionary = {}
var _resolve_cache: Dictionary = {} # "id|unit|var" -> path


func load_from_roots(map_archive = null) -> void:
	_unit_models.clear()
	_doodad_models.clear()
	_destructable_models.clear()
	_destructable_tex.clear()
	_resolve_cache.clear()
	_ingest_slk("Units/UnitData.slk", "unitID", "file", _unit_models)
	_ingest_slk("Units/unitUI.slk", "unitUIID", "file", _unit_models)
	_ingest_slk("Units/UnitUI.slk", "unitUIID", "file", _unit_models)
	_ingest_slk("Doodads/Doodads.slk", "doodID", "file", _doodad_models)
	_ingest_slk("Units/DestructableData.slk", "DestructableID", "file", _destructable_models)
	_ingest_slk("Units/DestructableData.slk", "DestructableID", "texFile", _destructable_tex)
	if map_archive != null:
		_ingest_w3u(map_archive.read_file("war3map.w3u"), true)
		_ingest_w3u(map_archive.read_file("war3map.w3d"), false)
		_ingest_w3u(map_archive.read_file("war3map.w3b"), false)
	print(
		"W3ObjectCatalog: units=%d doodads=%d destructables=%d"
		% [_unit_models.size(), _doodad_models.size(), _destructable_models.size()]
	)


## Returns relative model path (forward slashes). Tries variation suffix then bare file.
func resolve_model(rawcode: String, is_unit: bool, variation: int = 0) -> String:
	var cache_key := "%s|%d|%d" % [rawcode, 1 if is_unit else 0, variation]
	if _resolve_cache.has(cache_key):
		return str(_resolve_cache[cache_key])
	var id := rawcode
	var stem := ""
	if is_unit and _unit_models.has(id):
		stem = str(_unit_models[id])
	elif not is_unit:
		if _doodad_models.has(id):
			stem = str(_doodad_models[id])
		elif _destructable_models.has(id):
			stem = str(_destructable_models[id])
	if stem.is_empty() and _unit_models.has(id):
		stem = str(_unit_models[id])
	if stem.is_empty():
		stem = _heuristic(id)
	var result := ""
	if not stem.is_empty():
		result = _pick_variation_path(stem, variation)
	_resolve_cache[cache_key] = result
	return result


func destructable_tex_file(rawcode: String) -> String:
	if _destructable_tex.has(rawcode):
		return str(_destructable_tex[rawcode]).replace("\\", "/")
	return ""


func _heuristic(id: String) -> String:
	match id:
		"hfoo":
			return "Units/Human/Footman/Footman"
		"hkni":
			return "Units/Human/Knight/Knight"
		"hpea":
			return "Units/Human/Peasant/Peasant"
		"opeo":
			return "Units/Orc/Peon/Peon"
		_:
			return ""


func _pick_variation_path(stem: String, variation: int) -> String:
	var p := stem.replace("\\", "/")
	if p.to_lower().ends_with(".mdx") or p.to_lower().ends_with(".mdl"):
		p = p.get_basename()
	var candidates: PackedStringArray = PackedStringArray([
		"%s%d.mdx" % [p, variation],
		"%s.mdx" % p,
		"%s%d.mdl" % [p, variation],
		"%s.mdl" % p,
	])
	for c in candidates:
		if not W3Paths.resolve_path(c).is_empty():
			return c
	# Prefer numbered variation for trees/destructables even if not yet on disk.
	if variation >= 0:
		return "%s%d.mdx" % [p, variation]
	return "%s.mdx" % p


func _ingest_slk(rel: String, id_col: String, file_col: String, dest: Dictionary) -> void:
	var path := ""
	if W3Paths.has_root():
		path = W3Paths.resolve_path(rel)
	if path.is_empty() or not FileAccess.file_exists(path):
		return
	var f = FileAccess.open(path, FileAccess.READ)
	if f == null:
		return
	_parse_slk(f.get_as_text(), id_col, file_col, dest)


func _parse_slk(text: String, id_col: String, file_col: String, dest: Dictionary) -> void:
	var headers: Dictionary = {}
	var id_x := -1
	var file_x := -1
	var cur_y := -1
	var row_id := ""
	var row_file := ""
	for line in text.split("\n"):
		line = line.strip_edges().trim_suffix("\r")
		if line.is_empty() or line.begins_with("ID;") or line == "E":
			continue
		if not line.begins_with("C;"):
			continue
		var y := _slk_int(line, "Y")
		var x := _slk_int(line, "X")
		var k := _slk_str(line, "K")
		if y > 0:
			if y != cur_y and cur_y > 1 and not row_id.is_empty() and not row_file.is_empty():
				dest[row_id] = row_file
			if y != cur_y:
				cur_y = y
				row_id = ""
				row_file = ""
		if cur_y == 1 and x > 0 and not k.is_empty():
			var hname := k.trim_prefix("\"").trim_suffix("\"")
			headers[x] = hname
			if hname == id_col:
				id_x = x
			elif hname == file_col:
				file_x = x
		elif cur_y > 1 and x > 0:
			var val := k.trim_prefix("\"").trim_suffix("\"")
			if x == id_x:
				row_id = val
			elif x == file_x:
				row_file = val
	if cur_y > 1 and not row_id.is_empty() and not row_file.is_empty():
		dest[row_id] = row_file


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
	# SLK uses K"text" or K123 — no semicolon after the letter.
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


func _ingest_w3u(data: PackedByteArray, is_unit_table: bool) -> void:
	if data.is_empty():
		return
	var _u = is_unit_table
	# Full w3u/w3d/w3b parser deferred — base SLK covers stock Adrenaline content.
	var _n = data.size()
