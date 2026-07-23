extends Node3D
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3Shd = preload("res://addons/w3kit/w3_shd.gd")
## Warcraft III cliff MDX placement. HiveWE's renderer constructs a normal
## cliff filename in TL, TR, BR, BL order and clamps W3E cliffVariation to
## the available variants for that shape.


## Warsmash Variations.CLIFF_VARS (verified identical entry-by-entry, 2026-07-21).
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
var _nodes_by_tile: Dictionary = {} # Vector2i -> generated cliff/ramp Node3D
var _ramp_claimed_tiles: Dictionary = {} # tiles hidden/owned by a displaced CliffTrans
var _shd_tex: ImageTexture
var _shd_enabled: bool = false


func build(p_map, p_textures = null, p_shd_bytes: PackedByteArray = PackedByteArray()) -> void:
	map = p_map
	textures = p_textures if p_textures != null else W3TextureProvider.new()
	_shd_tex = null
	_shd_enabled = false
	if map != null:
		_shd_tex = W3Shd.load_texture(p_shd_bytes, map.tile_count_x(), map.tile_count_y())
		_shd_enabled = _shd_tex != null
	_cliff_slk.clear()
	_model_cache.clear()
	_spawned = 0
	_miss = 0
	_nodes_by_tile.clear()
	_ramp_claimed_tiles.clear()
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


func apply_lighting(light_direction: Vector3, ambient: float = 0.35, scale: float = 1.1) -> void:
	var direction := light_direction
	if direction.length_squared() < 0.0001:
		direction = Vector3(0.45, 0.85, 0.28)
	direction = direction.normalized()
	for mesh_node in find_children("*", "MeshInstance3D", true, false):
		var mat = (mesh_node as MeshInstance3D).material_override
		if mat is ShaderMaterial:
			(mat as ShaderMaterial).set_shader_parameter("light_dir", direction)
			(mat as ShaderMaterial).set_shader_parameter("light_ambient", ambient)
			(mat as ShaderMaterial).set_shader_parameter("light_scale", scale)


func set_flat_inspect(on: bool) -> void:
	for mesh_node in find_children("*", "MeshInstance3D", true, false):
		var mat = (mesh_node as MeshInstance3D).material_override
		if mat is ShaderMaterial:
			(mat as ShaderMaterial).set_shader_parameter("flat_lighting", on)


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
	var source_tile := Vector2i(x, y)
	if _ramp_claimed_tiles.has(source_tile):
		return
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
	# Full-ramp entrances retain their ground quad. Partial ramps use a
	# CliffTrans model whose placement can be one tile left/down of its W3E data.
	if map.is_ramp_entrance(x, y):
		return
	var transition: Dictionary = map.get_ramp_transition(x, y) if map.has_method("get_ramp_transition") else {}
	var is_ramp := not transition.is_empty()
	var placement: Vector2i = transition.get("placement", source_tile) if is_ramp else source_tile
	# HiveWE / the classic model names use TL, TR, BR, BL. For a valid partial
	# ramp the L/H/X tag and placement come directly from Warsmash.
	var base: int = mini(mini(bl, br), mini(tl, tr))
	var tag := ""
	for corner in [c01, c11, c10, c00]:
		var diff: int = int(corner.layer_height) - base
		var ramp_letter := is_ramp and bool(corner.ramp)
		if diff <= 0:
			tag += "L" if ramp_letter else "A"
		elif diff == 1:
			tag += "H" if ramp_letter else "B"
		else:
			tag += "X" if ramp_letter else "C"
	if is_ramp:
		tag = str(transition["tag"])
	if tag == "AAAA":
		return
	var cliff_tex := _cliff_texture_index(c00, c10, c01, c11)
	var cliff_id := "CLdi"
	if cliff_tex >= 0 and cliff_tex < map.cliff_tilesets.size():
		cliff_id = map.cliff_tilesets[cliff_tex]
	var info: Dictionary = _cliff_slk.get(cliff_id, {
		"cliffModelDir": "Cliffs",
		"rampModelDir": "CliffTrans",
		"texDir": "ReplaceableTextures/Cliff",
		"texFile": "Cliff0",
	})
	# Normal cliffs use CliffTypes.cliffModelDir and a W3E-selected, clamped
	# variation. Ramp transitions use CliffTypes.rampModelDir and are always 0.
	# HiveWE/Warsmash supply the former rule; OpenRealm independently confirms
	# the latter directory/zero-variation rule.
	var dir: String = str(info.get("rampModelDir", "CliffTrans")) if is_ramp else str(info.get("cliffModelDir", "Cliffs"))
	var var_i := 0 if is_ramp else _cliff_variation(tag, int(c00.cliff_variation))
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
	if is_ramp:
		_remove_node_at(placement)
		_ramp_claimed_tiles[source_tile] = true
		_ramp_claimed_tiles[transition["hide_neighbor"]] = true
	var p00 = map.get_corner(placement.x, placement.y)
	var p10 = map.get_corner(placement.x + 1, placement.y)
	var p01 = map.get_corner(placement.x, placement.y + 1)
	var p11 = map.get_corner(placement.x + 1, placement.y + 1)
	if p00 == null or p10 == null or p01 == null or p11 == null:
		return
	var placement_base: int = mini(mini(int(p00.layer_height), int(p10.layer_height)), mini(int(p01.layer_height), int(p11.layer_height)))
	var node := Node3D.new()
	node.name = "Cliff_%d_%d_%s" % [placement.x, placement.y, tag]
	node.set_meta("w3_model_rel", rel)
	node.set_meta("w3_source_tile", source_tile)
	node.set_meta("w3_placement_tile", placement)
	# HiveWE offset: (i, j, min(layer)-2) * 128 + centerOffset.
	# ground_height is applied per-vertex (cliff_ground), not as a flat average.
	var wx: float = map.center_offset.x + float(placement.x) * W3EMap.TILE_SIZE
	var wz: float = map.center_offset.y + float(placement.y) * W3EMap.TILE_SIZE
	var wy: float = (float(placement_base) - 2.0) * W3EMap.TILE_SIZE
	node.position = Vector3(wx, wy, wz)
	node.rotation = Vector3.ZERO
	add_child(node)
	var inst := W3MdxInstance.new()
	node.add_child(inst)
	var cliff_tex_dir := str(info.get("texDir", "ReplaceableTextures/Cliff"))
	var cliff_tex_file := str(info.get("texFile", "Cliff0"))
	# Prefer the map-tileset variant (for example L_Cliff1), then the shared
	# fallback. This mirrors the classic renderer's CliffTypes lookup.
	var cliff_tex_rel := "%s/%s_%s.blp" % [cliff_tex_dir, str(map.tileset), cliff_tex_file]
	if W3Paths.resolve_path(cliff_tex_rel).is_empty():
		cliff_tex_rel = "%s/%s.blp" % [cliff_tex_dir, cliff_tex_file]
	var gcorn := Vector4(p00.ground_height, p10.ground_height, p01.ground_height, p11.ground_height)
	# CliffTrans models can cover two tiles (128x256 or 256x128). A single
	# Vector4 only describes the first tile and used to clamp the far edge to
	# the middle row/column, leaving a seam against the terrain. Keep a 3x3
	# corner window so every vertex can sample the corner it actually spans.
	var ground_grid := PackedFloat32Array()
	for gy in 3:
		for gx in 3:
			var corner = map.get_corner(
				clampi(placement.x + gx, 0, map.width - 1),
				clampi(placement.y + gy, 0, map.height - 1)
			)
			ground_grid.append(float(corner.ground_height) if corner != null else 0.0)
	inst.setup(model, textures, abs_path, {
		"cliff_texture": cliff_tex_rel,
		"cliff_remap": true,
		"cliff_ground": gcorn,
		"cliff_ground_grid": ground_grid,
		"cliff_ground_grid_size": Vector2i(3, 3),
	})
	# Warsmash applies the static SHD texture to cliffs as well as ground. Keep
	# Godot real-time shadows disabled so the baked map is not shadowed twice.
	for mesh_node in inst.find_children("*", "MeshInstance3D", true, false):
		var mi := mesh_node as MeshInstance3D
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		var material = mi.material_override
		if material is ShaderMaterial:
			var cliff_material := material as ShaderMaterial
			cliff_material.set_shader_parameter("shd_enabled", _shd_enabled)
			if _shd_tex != null:
				cliff_material.set_shader_parameter("shd_tex", _shd_tex)
			cliff_material.set_shader_parameter("map_origin", map.center_offset)
			cliff_material.set_shader_parameter("map_size", Vector2(
				float(map.tile_count_x()) * W3EMap.TILE_SIZE,
				float(map.tile_count_y()) * W3EMap.TILE_SIZE
			))
	_spawned += 1
	_nodes_by_tile[placement] = node


func _remove_node_at(tile: Vector2i) -> void:
	var old = _nodes_by_tile.get(tile, null)
	if old != null and is_instance_valid(old):
		old.queue_free()
	_nodes_by_tile.erase(tile)


func _cliff_variation(tag: String, variation: int) -> int:
	if not CLIFF_VARS.has(tag):
		return 0
	# Warsmash Variations.getCliffVariation: wrap with modulo instead of
	# clamping (HiveWE clamps; Warsmash is authoritative for this project).
	var max_variation: int = int(CLIFF_VARS[tag])
	if variation < max_variation:
		return variation
	return variation % (max_variation + 1)


func _cliff_texture_index(c00, c10, c01, c11) -> int:
	# Warsmash uses the BL cliff texture directly; HiveWE only remaps 15 -> 1
	# on the ground path. The neighbour scan below is a LOCAL extension kept
	# because BL==15 would otherwise index past cliff_tilesets (crosscheck
	# 2026-07-21: no reference source implements this scan).
	var cliff_tex := int(c00.cliff_texture) # BL
	if cliff_tex != 15:
		return cliff_tex
	for corner in [c01, c11, c10]: # TL, TR, BR
		var candidate := int(corner.cliff_texture)
		if candidate != 15:
			return candidate
	return 1


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
