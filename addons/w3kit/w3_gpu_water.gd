extends Node3D
## HiveWE-style GPU water: one MultiMesh, discard dry tiles in VS.
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")


var map
var _water_tiles: int = 0
var _water_height_offset: float = -0.7


func build(p_map, _p_textures = null) -> void:
	map = p_map
	_water_tiles = 0
	for c in get_children():
		c.queue_free()
	if map == null:
		return
	_water_height_offset = _load_water_height_offset()
	var corners_x: int = map.width
	var corners_y: int = map.height
	var tiles_x: int = map.tile_count_x()
	var tiles_y: int = map.tile_count_y()
	if tiles_x <= 0 or tiles_y <= 0:
		return

	var wh_img := Image.create(corners_x, corners_y, false, Image.FORMAT_RF)
	var exists_img := Image.create(tiles_x, tiles_y, false, Image.FORMAT_R8)

	for y in corners_y:
		for x in corners_x:
			var c = map.get_corner(x, y)
			var wh: float = 0.0 if c == null else float(c.water_height)
			wh_img.set_pixel(x, y, Color(wh, 0, 0, 1))

	for y in tiles_y:
		for x in tiles_x:
			var c00 = map.get_corner(x, y)
			var c10 = map.get_corner(x + 1, y)
			var c01 = map.get_corner(x, y + 1)
			var c11 = map.get_corner(x + 1, y + 1)
			var wet := false
			if c00 and c00.water:
				wet = true
			elif c10 and c10.water:
				wet = true
			elif c01 and c01.water:
				wet = true
			elif c11 and c11.water:
				wet = true
			exists_img.set_pixel(x, y, Color(1, 0, 0, 1) if wet else Color(0, 0, 0, 1))
			if wet:
				_water_tiles += 1

	if _water_tiles == 0:
		print("W3GpuWater: no water tiles")
		return

	var sh: Shader = load("res://addons/w3kit/shaders/w3_gpu_water.gdshader")
	var mat := ShaderMaterial.new()
	mat.shader = sh
	mat.render_priority = 2
	mat.set_shader_parameter("water_height_tex", ImageTexture.create_from_image(wh_img))
	mat.set_shader_parameter("water_exists_tex", ImageTexture.create_from_image(exists_img))
	mat.set_shader_parameter("map_tiles", Vector2i(tiles_x, tiles_y))
	mat.set_shader_parameter("map_corners", Vector2i(corners_x, corners_y))
	mat.set_shader_parameter("tile_size", W3EMap.TILE_SIZE)
	mat.set_shader_parameter("water_height_offset", _water_height_offset)

	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = _make_unit_quad()
	mm.instance_count = tiles_x * tiles_y
	var y_min := 1.0e9
	var y_max := -1.0e9
	for i in mm.instance_count:
		var tile_x: int = i % tiles_x
		var tile_y: int = int(i / tiles_x)
		var wx: float = map.center_offset.x + float(tile_x) * W3EMap.TILE_SIZE
		var wz: float = map.center_offset.y + float(tile_y) * W3EMap.TILE_SIZE
		mm.set_instance_transform(i, Transform3D(Basis.IDENTITY, Vector3(wx, 0.0, wz)))
	for y in corners_y:
		for x in corners_x:
			var c = map.get_corner(x, y)
			if c == null or not c.water:
				continue
			var wy: float = (float(c.water_height) + _water_height_offset) * W3EMap.TILE_SIZE
			y_min = minf(y_min, wy)
			y_max = maxf(y_max, wy)
	if y_max < y_min:
		y_min = 0.0
		y_max = 128.0

	var mmi := MultiMeshInstance3D.new()
	mmi.name = "GpuWater"
	mmi.multimesh = mm
	mmi.material_override = mat
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mmi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	var x0: float = map.center_offset.x
	var z0: float = map.center_offset.y
	var x1: float = x0 + float(tiles_x) * W3EMap.TILE_SIZE
	var z1: float = z0 + float(tiles_y) * W3EMap.TILE_SIZE
	mmi.custom_aabb = AABB(
		Vector3(minf(x0, x1), y_min - 64.0, minf(z0, z1)),
		Vector3(absf(x1 - x0), (y_max - y_min) + 128.0, absf(z1 - z0))
	)
	mmi.extra_cull_margin = 256.0
	add_child(mmi)
	print("W3GpuWater: wet=%d / %d instanced=1 offset=%.2f" % [_water_tiles, tiles_x * tiles_y, _water_height_offset])


func get_water_tile_count() -> int:
	return _water_tiles


func _make_unit_quad() -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var verts := [
		Vector3(0, 0, 0), Vector3(1, 0, 0), Vector3(0, 0, 1),
		Vector3(1, 0, 0), Vector3(1, 0, 1), Vector3(0, 0, 1),
	]
	for v in verts:
		st.set_normal(Vector3.UP)
		st.add_vertex(v)
	return st.commit()


func _load_water_height_offset() -> float:
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
