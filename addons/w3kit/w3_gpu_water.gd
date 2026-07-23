extends Node3D
## HiveWE-style GPU water: one MultiMesh, discard dry tiles in VS.
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3BlpDecoder = preload("res://addons/w3kit/w3_blp_decoder.gd")


var map
var _water_tiles: int = 0
var _water_height_offset: float = -0.7
var _water_texture_count: int = 0


func build(p_map, _p_textures = null) -> void:
	map = p_map
	_water_tiles = 0
	for c in get_children():
		c.queue_free()
	if map == null:
		return
	var water_config := _load_water_config()
	_water_height_offset = float(water_config.get("height", -0.7))
	var corners_x: int = map.width
	var corners_y: int = map.height
	var tiles_x: int = map.tile_count_x()
	var tiles_y: int = map.tile_count_y()
	if tiles_x <= 0 or tiles_y <= 0:
		return

	var wh_img := Image.create(corners_x, corners_y, false, Image.FORMAT_RF)
	var gh_img := Image.create(corners_x, corners_y, false, Image.FORMAT_RF)
	var exists_img := Image.create(tiles_x, tiles_y, false, Image.FORMAT_R8)

	for y in corners_y:
		for x in corners_x:
			var c = map.get_corner(x, y)
			var wh: float = 0.0 if c == null else float(c.water_height)
			wh_img.set_pixel(x, y, Color(wh, 0, 0, 1))
			# Depth shading uses the final ground height INCLUDING the ramp
			# entrance +0.5 lift (Warsmash groundCornerHeights semantics).
			var gh: float = 0.0 if c == null else float(map.corner_world_y(x, y)) / W3EMap.TILE_SIZE
			gh_img.set_pixel(x, y, Color(gh, 0, 0, 1))

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
	mat.set_shader_parameter("ground_height_tex", ImageTexture.create_from_image(gh_img))
	mat.set_shader_parameter("water_exists_tex", ImageTexture.create_from_image(exists_img))
	mat.set_shader_parameter("map_tiles", Vector2i(tiles_x, tiles_y))
	mat.set_shader_parameter("map_corners", Vector2i(corners_x, corners_y))
	mat.set_shader_parameter("tile_size", W3EMap.TILE_SIZE)
	mat.set_shader_parameter("water_height_offset", _water_height_offset)
	var water_frames := _load_water_frames(water_config)
	if water_frames != null:
		mat.set_shader_parameter("water_textures", water_frames)
	mat.set_shader_parameter("water_texture_count", _water_texture_count)
	mat.set_shader_parameter("water_texture_rate", float(water_config.get("texrate", 0.0)))
	mat.set_shader_parameter("shallow_color_min", water_config.get("shallow_min", Color(0.0, 0.45, 0.55, 0.50)))
	mat.set_shader_parameter("shallow_color_max", water_config.get("shallow_max", Color(0.0, 0.35, 0.45, 0.65)))
	mat.set_shader_parameter("deep_color_min", water_config.get("deep_min", Color(0.0, 0.20, 0.35, 0.75)))
	mat.set_shader_parameter("deep_color_max", water_config.get("deep_max", Color(0.0, 0.10, 0.25, 0.85)))

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
	print("W3GpuWater: wet=%d / %d instanced=1 offset=%.2f frames=%d" % [
		_water_tiles, tiles_x * tiles_y, _water_height_offset, _water_texture_count])


func get_water_tile_count() -> int:
	return _water_tiles


func get_water_texture_count() -> int:
	return _water_texture_count


func _make_unit_quad() -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var verts := [
		Vector3(0, 0, 0), Vector3(1, 0, 0), Vector3(0, 0, 1),
		Vector3(1, 0, 0), Vector3(1, 0, 1), Vector3(0, 0, 1),
	]
	for v in verts:
		st.set_normal(Vector3.UP)
		# HiveWE water.vert derives UV from the tile-local position; without
		# this the shader sampled one texel for the whole surface.
		st.set_uv(Vector2(v.x, v.z))
		st.add_vertex(v)
	return st.commit()


func _load_water_config() -> Dictionary:
	var result := {
		"height": -0.7,
		"texfile": "ReplaceableTextures/Water/Water",
		"numtex": 0,
		"texrate": 0.0,
		"shallow_min": Color(0.0, 0.45, 0.55, 0.50),
		"shallow_max": Color(0.0, 0.35, 0.45, 0.65),
		"deep_min": Color(0.0, 0.20, 0.35, 0.75),
		"deep_max": Color(0.0, 0.10, 0.25, 0.85),
	}
	var letter := "L"
	if map != null and str(map.tileset).length() > 0:
		letter = str(map.tileset)[0]
	# Water.slk is shared metadata. Both Warsmash and HiveWE intentionally use
	# the SD animated water frames in Reforged 1.32; there is no HD frame set.
	var path := W3Paths.resolve_game_path("TerrainArt/Water.slk")
	if path.is_empty() or not FileAccess.file_exists(path):
		return result
	var text := FileAccess.get_file_as_string(path)
	var key := "%sSha" % letter
	var cur_y := -1
	var headers: Dictionary = {}
	var row: Dictionary = {}
	for line in text.split("\n"):
		line = line.strip_edges().trim_suffix("\r")
		if not line.begins_with("C;"):
			continue
		var y := _slk_int(line, "Y")
		var x := _slk_int(line, "X")
		var k := _slk_str(line, "K").trim_prefix("\"").trim_suffix("\"")
		if y > 0 and y != cur_y:
			if str(row.get("_id", "")) == key:
				_apply_water_row(result, row)
				return result
			cur_y = y
			row = {}
		if cur_y == 1 and x > 0:
			headers[x] = k.to_lower()
		elif cur_y > 1 and x > 0:
			if x == 1:
				row["_id"] = k
			row[str(headers.get(x, x))] = k
	if str(row.get("_id", "")) == key:
		_apply_water_row(result, row)
	return result


func _apply_water_row(result: Dictionary, row: Dictionary) -> void:
	result.height = str(row.get("height", result.height)).to_float()
	result.texfile = str(row.get("texfile", result.texfile)).replace("\\", "/")
	result.numtex = str(row.get("numtex", result.numtex)).to_int()
	result.texrate = str(row.get("texrate", result.texrate)).to_float()
	result.shallow_min = _water_color(row, "smin", result.shallow_min)
	result.shallow_max = _water_color(row, "smax", result.shallow_max)
	result.deep_min = _water_color(row, "dmin", result.deep_min)
	result.deep_max = _water_color(row, "dmax", result.deep_max)


func _water_color(row: Dictionary, prefix: String, fallback: Color) -> Color:
	return Color(
		str(row.get(prefix + "_r", fallback.r * 255.0)).to_float() / 255.0,
		str(row.get(prefix + "_g", fallback.g * 255.0)).to_float() / 255.0,
		str(row.get(prefix + "_b", fallback.b * 255.0)).to_float() / 255.0,
		str(row.get(prefix + "_a", fallback.a * 255.0)).to_float() / 255.0)


func _load_water_frames(config: Dictionary) -> Texture2DArray:
	_water_texture_count = 0
	var count := maxi(0, int(config.get("numtex", 0)))
	var base := str(config.get("texfile", ""))
	if count == 0 or base.is_empty():
		return null
	var images: Array[Image] = []
	for frame in count:
		var rel := "%s%02d.blp" % [base, frame]
		# Deliberately force the shared classic tier: see _load_water_config.
		var absolute := W3Paths.resolve_path(rel)
		var image := W3BlpDecoder.load_image(absolute)
		if image == null:
			push_error("W3GpuWater: missing water frame %s" % rel)
			return null
		if image.get_format() != Image.FORMAT_RGBA8:
			image.convert(Image.FORMAT_RGBA8)
		if image.get_width() != 128 or image.get_height() != 128:
			image.resize(128, 128, Image.INTERPOLATE_BILINEAR)
		images.append(image)
	var array := Texture2DArray.new()
	if array.create_from_images(images) != OK:
		push_error("W3GpuWater: failed to create %d-frame texture array" % images.size())
		return null
	_water_texture_count = images.size()
	return array


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
