extends Node3D
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
## Warsmash water pass: quads where any corner has water flag.
## Y = (water_height + Water.slk height) * TILE_SIZE  (TerrainShaders.Water).
## Chunked so transparent sorting stays sane (one map-sized mesh often vanishes).


const CHUNK_TILES := 8

var map
var textures
var _water_tiles: int = 0
var _water_height_offset: float = -0.7 ## LSha default from Water.slk


func build(p_map, p_textures = null) -> void:
	map = p_map
	textures = p_textures if p_textures != null else W3TextureProvider.new()
	_water_tiles = 0
	_water_height_offset = _load_water_height_offset()
	for c in get_children():
		c.queue_free()
	var tx: int = map.tile_count_x()
	var ty: int = map.tile_count_y()
	var mat := _make_modern_water_material()
	var ox := 0
	while ox < tx:
		var oy := 0
		while oy < ty:
			_build_chunk(ox, oy, mini(CHUNK_TILES, tx - ox), mini(CHUNK_TILES, ty - oy), mat)
			oy += CHUNK_TILES
		ox += CHUNK_TILES
	if _water_tiles == 0:
		print("W3WaterBuilder: no water tiles")
		return
	print("W3WaterBuilder: tiles=%d height_offset=%.2f modern_water=1" % [_water_tiles, _water_height_offset])


func get_water_tile_count() -> int:
	return _water_tiles


func _build_chunk(ox: int, oy: int, w: int, h: int, mat: Material) -> void:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var n := 0
	for y in h:
		for x in w:
			if _add_water_tile(st, ox + x, oy + y):
				n += 1
	if n == 0:
		return
	_water_tiles += n
	st.generate_normals()
	var mesh := st.commit()
	var mi := MeshInstance3D.new()
	mi.name = "Water_%d_%d" % [ox, oy]
	mi.mesh = mesh
	mi.material_override = mat
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	add_child(mi)


func _make_modern_water_material() -> Material:
	var sh := load("res://addons/w3kit/shaders/w3_modern_water.gdshader") as Shader
	if sh != null:
		var sm := ShaderMaterial.new()
		sm.shader = sh
		sm.render_priority = 2
		sm.set_shader_parameter("edge_size", 16.0)
		sm.set_shader_parameter("absorption_mult", 0.014)
		sm.set_shader_parameter("reflection_strength", 0.5)
		return sm
	var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
	var mat := StandardMaterial3D.new()
	W3War3Lit.style_modern_water_material(mat)
	mat.render_priority = 2
	return mat


func _load_water_height_offset() -> float:
	## Warsmash Terrain.java: waterTable.get(tileset + "Sha").height
	var letter := "L"
	if map != null and str(map.tileset).length() > 0:
		letter = str(map.tileset)[0]
	var path := W3Paths.resolve_path("TerrainArt/Water.slk")
	if path.is_empty() or not FileAccess.file_exists(path):
		return -0.7
	var text := FileAccess.get_file_as_string(path)
	var key := "%sSha" % letter
	var cur_y := -1
	var row_id := ""
	var row_height := NAN
	for line in text.split("\n"):
		line = line.strip_edges().trim_suffix("\r")
		if not line.begins_with("C;"):
			continue
		var y := _slk_int(line, "Y")
		var x := _slk_int(line, "X")
		var k := _slk_str(line, "K").trim_prefix("\"").trim_suffix("\"")
		if y > 0 and y != cur_y:
			if row_id == key and not is_nan(row_height):
				return row_height
			cur_y = y
			row_id = ""
			row_height = NAN
		if x == 1 and cur_y > 1:
			row_id = k
		elif x == 2 and cur_y > 1:
			row_height = k.to_float()
	if row_id == key and not is_nan(row_height):
		return row_height
	return -0.7


func _add_water_tile(st: SurfaceTool, x: int, y: int) -> bool:
	var c00 = map.get_corner(x, y)
	var c10 = map.get_corner(x + 1, y)
	var c01 = map.get_corner(x, y + 1)
	var c11 = map.get_corner(x + 1, y + 1)
	if c00 == null or c10 == null or c01 == null or c11 == null:
		return false
	if not (c00.water or c10.water or c01.water or c11.water):
		return false
	var p00 := _water_corner_pos(x, y, c00)
	var p10 := _water_corner_pos(x + 1, y, c10)
	var p01 := _water_corner_pos(x, y + 1, c01)
	var p11 := _water_corner_pos(x + 1, y + 1, c11)
	_tri(st, p00, p10, p01, Vector2(0, 0), Vector2(1, 0), Vector2(0, 1))
	_tri(st, p10, p11, p01, Vector2(1, 0), Vector2(1, 1), Vector2(0, 1))
	return true


func _water_corner_pos(x: int, y: int, c) -> Vector3:
	var wx: float = map.center_offset.x + float(x) * W3EMap.TILE_SIZE
	var wz: float = map.center_offset.y + float(y) * W3EMap.TILE_SIZE
	# Warsmash: (corner.waterHeight + waterHeightOffset) * 128
	var wy: float = (float(c.water_height) + _water_height_offset) * W3EMap.TILE_SIZE
	return Vector3(wx, wy, wz)


func _tri(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, uva: Vector2, uvb: Vector2, uvc: Vector2) -> void:
	st.set_uv(uva)
	st.add_vertex(a)
	st.set_uv(uvb)
	st.add_vertex(b)
	st.set_uv(uvc)
	st.add_vertex(c)


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
