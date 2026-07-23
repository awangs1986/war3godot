extends RefCounted
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
## Resolves unit/doodad fourcc → model path via SLK + war3map overrides.
## SLK cell format matches Terrain.slk: K"text" / K123 (no semicolon after K).


var _unit_models: Dictionary = {} # id -> path stem (no required .mdx)
var _doodad_models: Dictionary = {}
var _destructable_models: Dictionary = {}
## Optional replaceable tex stems from DestructableData texFile (id -> path).
var _destructable_tex: Dictionary = {}
var _unit_model_scale: Dictionary = {}
var _unit_move_height: Dictionary = {}
var _unit_collision: Dictionary = {}
var _unit_move_type: Dictionary = {}
## Simulation stats (see unit_combat_stats): SLK column -> id -> raw value.
var _unit_hp: Dictionary = {}
var _unit_speed: Dictionary = {}
var _unit_turn_rate: Dictionary = {}
var _unit_prop_window: Dictionary = {}
var _unit_acquire: Dictionary = {}
var _unit_attack_range: Dictionary = {}
var _unit_attack_cooldown: Dictionary = {}
var _unit_damage_dice: Dictionary = {}
var _unit_damage_sides: Dictionary = {}
var _unit_damage_plus: Dictionary = {}
var _doodad_default_scale: Dictionary = {}
var _resolve_cache: Dictionary = {} # "id|unit|var" -> path


func load_from_roots(map_archive = null) -> void:
	_unit_models.clear()
	_doodad_models.clear()
	_destructable_models.clear()
	_destructable_tex.clear()
	_unit_model_scale.clear()
	_unit_move_height.clear()
	_unit_collision.clear()
	_unit_move_type.clear()
	_unit_hp.clear()
	_unit_speed.clear()
	_unit_turn_rate.clear()
	_unit_prop_window.clear()
	_unit_acquire.clear()
	_unit_attack_range.clear()
	_unit_attack_cooldown.clear()
	_unit_damage_dice.clear()
	_unit_damage_sides.clear()
	_unit_damage_plus.clear()
	_doodad_default_scale.clear()
	_resolve_cache.clear()
	_ingest_slk("Units/UnitData.slk", "unitID", "file", _unit_models)
	_ingest_slk("Units/unitUI.slk", "unitUIID", "file", _unit_models)
	_ingest_slk("Units/UnitUI.slk", "unitUIID", "file", _unit_models)
	_ingest_slk("Units/unitUI.slk", "unitUIID", "modelScale", _unit_model_scale)
	_ingest_slk("Units/UnitUI.slk", "unitUIID", "modelScale", _unit_model_scale)
	_ingest_slk("Units/UnitData.slk", "unitID", "moveHeight", _unit_move_height)
	_ingest_slk("Units/UnitData.slk", "unitID", "movetp", _unit_move_type)
	_ingest_slk("Units/UnitData.slk", "unitID", "turnRate", _unit_turn_rate)
	_ingest_slk("Units/UnitData.slk", "unitID", "propWin", _unit_prop_window)
	_ingest_slk("Units/UnitBalance.slk", "unitBalanceID", "collision", _unit_collision)
	_ingest_slk("Units/UnitBalance.slk", "unitBalanceID", "HP", _unit_hp)
	_ingest_slk("Units/UnitBalance.slk", "unitBalanceID", "spd", _unit_speed)
	_ingest_slk("Units/UnitWeapons.slk", "unitWeapID", "acquire", _unit_acquire)
	_ingest_slk("Units/UnitWeapons.slk", "unitWeapID", "rangeN1", _unit_attack_range)
	_ingest_slk("Units/UnitWeapons.slk", "unitWeapID", "cool1", _unit_attack_cooldown)
	_ingest_slk("Units/UnitWeapons.slk", "unitWeapID", "dice1", _unit_damage_dice)
	_ingest_slk("Units/UnitWeapons.slk", "unitWeapID", "sides1", _unit_damage_sides)
	_ingest_slk("Units/UnitWeapons.slk", "unitWeapID", "dmgplus1", _unit_damage_plus)
	_ingest_slk("Doodads/Doodads.slk", "doodID", "file", _doodad_models)
	_ingest_slk("Doodads/Doodads.slk", "doodID", "defScale", _doodad_default_scale)
	_ingest_slk("Units/DestructableData.slk", "DestructableID", "file", _destructable_models)
	_ingest_slk("Units/DestructableData.slk", "DestructableID", "texFile", _destructable_tex)
	# HiveWE merges these INI tables over the base SLKs. They contain both the
	# stock HD paths and alternate Reforged skin rawcodes.
	_ingest_skin_ini("Units/UnitSkin.txt", "file", _unit_models)
	_ingest_skin_ini("Units/UnitSkin.txt", "modelScale", _unit_model_scale)
	_ingest_skin_ini("Units/UnitSkin.txt", "moveHeight", _unit_move_height)
	_ingest_skin_ini("Doodads/DoodadSkins.txt", "file", _doodad_models)
	_ingest_skin_ini("Doodads/DoodadSkins.txt", "defScale", _doodad_default_scale)
	_ingest_skin_ini("Units/DestructableSkin.txt", "file", _destructable_models)
	_ingest_skin_ini("Units/DestructableSkin.txt", "texFile", _destructable_tex)
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


func model_scale(rawcode: String, is_unit: bool) -> float:
	var source := _unit_model_scale if is_unit else _doodad_default_scale
	if not source.has(rawcode):
		return 1.0
	var value := str(source[rawcode]).to_float()
	return value if value > 0.0001 else 1.0


func unit_move_height(rawcode: String) -> float:
	if not _unit_move_height.has(rawcode):
		return 0.0
	return str(_unit_move_height[rawcode]).to_float()


## UnitBalance.slk "collision" (CUnitType.getCollisionSize); 0 when unknown.
func unit_collision(rawcode: String) -> float:
	if not _unit_collision.has(rawcode):
		return 0.0
	return str(_unit_collision[rawcode]).to_float()


## UnitData.slk "movetp" (foot/horse/fly/hover/float/amph); "" when unknown.
func unit_move_type(rawcode: String) -> String:
	if not _unit_move_type.has(rawcode):
		return ""
	return str(_unit_move_type[rawcode]).trim_prefix("\"").trim_suffix("\"")


## Combat/movement stats used by the simulation layer. Missing rows yield the
## documented defaults; damage is the WC3 dice model (dice, sides, plus).
func unit_combat_stats(rawcode: String) -> Dictionary:
	return {
		"hp": _stat_float(_unit_hp, rawcode, 100.0),
		"speed": _stat_float(_unit_speed, rawcode, 270.0),
		"collision": _stat_float(_unit_collision, rawcode, 32.0),
		"turn_rate": _stat_float(_unit_turn_rate, rawcode, 0.6),
		"prop_window_deg": _stat_float(_unit_prop_window, rawcode, 60.0),
		"acquire_range": _stat_float(_unit_acquire, rawcode, 500.0),
		"attack_range": _stat_float(_unit_attack_range, rawcode, 90.0),
		"attack_cooldown": _stat_float(_unit_attack_cooldown, rawcode, 1.5),
		"damage_dice": int(_stat_float(_unit_damage_dice, rawcode, 1.0)),
		"damage_sides": int(_stat_float(_unit_damage_sides, rawcode, 4.0)),
		"damage_plus": int(_stat_float(_unit_damage_plus, rawcode, 1.0)),
		"move_type": unit_move_type(rawcode),
	}


static func _stat_float(table: Dictionary, rawcode: String, fallback: float) -> float:
	if not table.has(rawcode):
		return fallback
	var text := str(table[rawcode]).trim_prefix("\"").trim_suffix("\"")
	if text.is_empty() or text == "-":
		return fallback
	return text.to_float()


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
	# Check every Reforged candidate before any classic candidate. Otherwise a
	# classic numbered variation (Model0.mdx) can mask Reforged's unnumbered
	# replacement (Model.mdx).
	for c in candidates:
		if not W3Paths.resolve_reforged_path(c).is_empty():
			return c
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


func _ingest_skin_ini(rel: String, field_name: String, dest: Dictionary) -> void:
	var path := W3Paths.resolve_reforged_path(rel)
	if path.is_empty():
		return
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return
	var section := ""
	var base_value := ""
	var hd_value := ""
	var wanted := field_name.to_lower()
	for raw_line in f.get_as_text().split("\n"):
		var line := raw_line.strip_edges().trim_suffix("\r")
		if line.is_empty() or line.begins_with("//") or line.begins_with(";"):
			continue
		if line.begins_with("[") and line.ends_with("]"):
			_commit_skin_ini(section, hd_value if not hd_value.is_empty() else base_value, dest)
			section = line.substr(1, line.length() - 2)
			base_value = ""
			hd_value = ""
			continue
		var equals := line.find("=")
		if equals <= 0:
			continue
		var key := line.left(equals).strip_edges().to_lower()
		var value := line.substr(equals + 1).strip_edges()
		if key == wanted:
			base_value = value
		elif key == wanted + ":hd":
			hd_value = value
	_commit_skin_ini(section, hd_value if not hd_value.is_empty() else base_value, dest)


func _commit_skin_ini(section: String, value: String, dest: Dictionary) -> void:
	if section.length() == 4 and not value.is_empty() and value != "-":
		dest[section] = value


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
