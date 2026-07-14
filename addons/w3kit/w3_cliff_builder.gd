extends Node3D
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
## Warsmash-aligned cliff MDX placement (Terrain.updateCliffMeshes simplified).
## Filename order: TL TR BR BL relative to base layer (Warsmash Terrain.java).
## Variation table: Warsmash Variations.CLIFF_VARS / CITY_CLIFF_VARS.


## Max variation index per tag (value = max inclusive, matching Warsmash map).
const CLIFF_VARS := {
	"AAAB": 1, "AAAC": 1, "AABA": 1, "AABB": 2, "AABC": 0, "AACA": 1, "AACB": 0, "AACC": 1,
	"ABAA": 1, "ABAB": 1, "ABAC": 0, "ABBA": 2, "ABBB": 1, "ABBC": 0, "ABCA": 0, "ABCB": 0, "ABCC": 0,
	"ACAA": 1, "ACAB": 0, "ACAC": 1, "ACBA": 0, "ACBB": 0, "ACBC": 0, "ACCA": 1, "ACCB": 0, "ACCC": 1,
	"BAAA": 1, "BAAB": 1, "BAAC": 0, "BABA": 1, "BABB": 1, "BABC": 0, "BACA": 0, "BACB": 0, "BACC": 0,
	"BBAA": 1, "BBAB": 1, "BBAC": 0, "BBBA": 1, "BBCA": 0,
	"BCAA": 0, "BCAB": 0, "BCAC": 0, "BCBA": 0, "BCCA": 0,
	"CAAA": 1, "CAAB": 0, "CAAC": 1, "CABA": 0, "CABB": 0, "CABC": 0, "CACA": 1, "CACB": 0, "CACC": 1,
	"CBAA": 0, "CBAB": 0, "CBAC": 0, "CBBA": 0, "CBCA": 0,
	"CCAA": 1, "CCAB": 0, "CCAC": 1, "CCBA": 0, "CCCA": 1,
}

var map
var textures
var _cliff_slk: Dictionary = {} # cliffID -> {cliffModelDir, rampModelDir, texDir, texFile, groundTile}
var _model_cache: Dictionary = {}
var _spawned: int = 0
var _miss: int = 0


func build(p_map, p_textures = null) -> void:
	map = p_map
	textures = p_textures if p_textures != null else W3TextureProvider.new()
	_cliff_slk.clear()
	_model_cache.clear()
	_spawned = 0
	_miss = 0
	for c in get_children():
		c.queue_free()
	_load_cliff_types()
	var tx: int = map.tile_count_x()
	var ty: int = map.tile_count_y()
	for y in ty:
		for x in tx:
			_try_spawn_cliff(x, y)
	print("W3CliffBuilder: spawned=%d miss=%d" % [_spawned, _miss])


func get_counts() -> Dictionary:
	return {"ok": _spawned, "miss": _miss}


func _load_cliff_types() -> void:
	var path := W3Paths.resolve_path("TerrainArt/CliffTypes.slk")
	if path.is_empty() or not FileAccess.file_exists(path):
		push_warning("W3CliffBuilder: CliffTypes.slk missing")
		return
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return
	_parse_cliff_slk(f.get_as_text())


func _parse_cliff_slk(text: String) -> void:
	var headers: Dictionary = {}
	var cur_y := -1
	var row: Dictionary = {}
	var id_x := -1
	var model_x := -1
	var ramp_x := -1
	var tex_dir_x := -1
	var tex_file_x := -1
	var ground_x := -1
	for line in text.split("\n"):
		line = line.strip_edges().trim_suffix("\r")
		if not line.begins_with("C;"):
			continue
		var y := _slk_int(line, "Y")
		var x := _slk_int(line, "X")
		var k := _slk_str(line, "K").trim_prefix("\"").trim_suffix("\"")
		if y > 0 and y != cur_y:
			_commit_cliff_row(row, id_x, model_x, ramp_x, tex_dir_x, tex_file_x, ground_x)
			cur_y = y
			row = {}
		if x <= 0:
			continue
		if cur_y == 1:
			headers[x] = k
			match k:
				"cliffID":
					id_x = x
				"cliffModelDir":
					model_x = x
				"rampModelDir":
					ramp_x = x
				"texDir":
					tex_dir_x = x
				"texFile":
					tex_file_x = x
				"groundTile":
					ground_x = x
		elif cur_y > 1:
			row[x] = k
	_commit_cliff_row(row, id_x, model_x, ramp_x, tex_dir_x, tex_file_x, ground_x)
	print("W3CliffBuilder: cliff types=", _cliff_slk.size())


func _commit_cliff_row(row: Dictionary, id_x: int, model_x: int, ramp_x: int, tex_dir_x: int, tex_file_x: int, ground_x: int) -> void:
	if id_x < 0 or not row.has(id_x):
		return
	var cid := str(row[id_x])
	if cid.is_empty():
		return
	_cliff_slk[cid] = {
		"cliffModelDir": str(row.get(model_x, "Cliffs")),
		"rampModelDir": str(row.get(ramp_x, "CliffTrans")),
		"texDir": str(row.get(tex_dir_x, "ReplaceableTextures\\Cliff")).replace("\\", "/"),
		"texFile": str(row.get(tex_file_x, "Cliff0")),
		"groundTile": str(row.get(ground_x, "")),
	}


func _try_spawn_cliff(x: int, y: int) -> void:
	var c00 = map.get_corner(x, y)
	var c10 = map.get_corner(x + 1, y)
	var c01 = map.get_corner(x, y + 1)
	var c11 = map.get_corner(x + 1, y + 1)
	if c00 == null or c10 == null or c01 == null or c11 == null:
		return
	var bl: int = c00.layer_height
	var br: int = c10.layer_height
	var tl: int = c01.layer_height
	var tr: int = c11.layer_height
	if bl == br and bl == tl and bl == tr:
		return
	# Warsmash isCornerRampEntrance — skip cliff MDX (ramp mesh owns the tile).
	if c00.ramp and c10.ramp and c01.ramp and c11.ramp:
		if not (bl == tr and tl == br):
			return
	var base: int = mini(mini(bl, br), mini(tl, tr))
	# Warsmash order: TL TR BR BL
	var tag := "%s%s%s%s" % [
		String.chr(65 + tl - base),
		String.chr(65 + tr - base),
		String.chr(65 + br - base),
		String.chr(65 + bl - base),
	]
	if tag == "AAAA":
		return
	var cliff_tex: int = int(c00.cliff_texture)
	if cliff_tex == 15:
		cliff_tex = 1
	var cliff_id := "CLdi"
	if cliff_tex >= 0 and cliff_tex < map.cliff_tilesets.size():
		cliff_id = map.cliff_tilesets[cliff_tex]
	var info: Dictionary = _cliff_slk.get(cliff_id, {"cliffModelDir": "Cliffs", "texDir": "ReplaceableTextures/Cliff", "texFile": "Cliff0"})
	var dir: String = str(info.get("cliffModelDir", "Cliffs"))
	var var_i := _cliff_variation(dir, tag, int(c00.cliff_variation))
	var rel := "Doodads/Terrain/%s/%s%s%d.mdx" % [dir, dir, tag, var_i]
	var abs_path := W3Paths.resolve_path(rel)
	if abs_path.is_empty():
		_miss += 1
		if _miss <= 8:
			push_warning("W3CliffBuilder: missing %s" % rel)
		return
	var model = null
	if _model_cache.has(abs_path):
		model = _model_cache[abs_path]
	else:
		model = W3MdxModel.load_file(abs_path)
		_model_cache[abs_path] = model
	if model == null or model.geosets.is_empty():
		_miss += 1
		return
	var node := Node3D.new()
	node.name = "Cliff_%d_%d_%s" % [x, y, tag]
	# Warsmash/HiveWE offset: (i, j, min(layer)-2) * 128 + centerOffset.
	# ground_height is applied per-vertex (cliff_ground), not as a flat average.
	var wx: float = map.center_offset.x + float(x) * W3EMap.TILE_SIZE
	var wz: float = map.center_offset.y + float(y) * W3EMap.TILE_SIZE
	var wy: float = (float(base) - 2.0) * W3EMap.TILE_SIZE
	node.position = Vector3(wx, wy, wz)
	node.rotation = Vector3.ZERO
	add_child(node)
	var inst := W3MdxInstance.new()
	node.add_child(inst)
	var cliff_tex_rel := "%s/%s.blp" % [str(info.get("texDir", "ReplaceableTextures/Cliff")), str(info.get("texFile", "Cliff0"))]
	var gcorn := Vector4(c00.ground_height, c10.ground_height, c01.ground_height, c11.ground_height)
	inst.setup(model, textures, abs_path, {
		"cliff_texture": cliff_tex_rel,
		"cliff_remap": true,
		"cliff_ground": gcorn,
	})
	# Preview: cliffs never cast shadows (HiveWE editor cost avoidance).
	for ch in inst.get_children():
		if ch is MeshInstance3D:
			(ch as MeshInstance3D).cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		elif ch is Node:
			for mi in ch.find_children("*", "MeshInstance3D", true, false):
				(mi as MeshInstance3D).cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_spawned += 1


func _cliff_variation(dir: String, tag: String, variation: int) -> int:
	var table: Dictionary = CLIFF_VARS
	if not table.has(tag):
		return 0
	var vmax: int = int(table[tag])
	if variation < vmax:
		return variation
	return variation % (vmax + 1)


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
