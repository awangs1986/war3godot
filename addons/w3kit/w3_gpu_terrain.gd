extends Node3D
## HiveWE/Warsmash true-source GPU terrain: single MultiMesh, single pass.
## Per-tile uvec4 (base + up to 3 overlays) → Texture2DArray sub-images, composited
## in-shader. Soft stitches come from each ground BLP's own baked alpha sub-images.
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3Shd = preload("res://addons/w3kit/w3_shd.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")

var map
var textures
var _blight_index: int = -1
var _layer_ids: PackedStringArray = PackedStringArray()
var _shd_tex: ImageTexture
var _mats: Array = [] # ShaderMaterial per tileset layer
var _cliff_to_ground: PackedInt32Array = PackedInt32Array()
var _tileset_extended: PackedByteArray = PackedByteArray()
var _romp_tiles: PackedByteArray = PackedByteArray()
var _romp_tiles_x: int = 0
var _romp_tiles_y: int = 0
var _mmis: Array = []

## Per-tileset sub-image count: 0..15 = blend shapes (bitset), 16..31 = extended deco.
const LAYER_STRIDE := 32
const ARRAY_BANK_SIZE := 256
const ARRAY_BANK_COUNT := 3
## Sentinel written into tile_data channels for empty slots (matches shader EMPTY).
const EMPTY_LAYER := 60000.0


func build(p_map, p_textures = null, p_shd_bytes: PackedByteArray = PackedByteArray()) -> void:
	map = p_map
	textures = p_textures if p_textures != null else W3TextureProvider.new()
	for c in get_children():
		c.queue_free()
	_mmis.clear()
	_mats.clear()
	_shd_tex = null
	if map == null:
		return
	_layer_ids.clear()
	for ground_id in map.ground_tilesets:
		_layer_ids.append(str(ground_id))
	_prepare_blight()
	_prepare_cliff_to_ground()
	_prepare_romp_tiles()
	var corners_x: int = map.width
	var corners_y: int = map.height
	var tiles_x: int = map.tile_count_x()
	var tiles_y: int = map.tile_count_y()
	if tiles_x <= 0 or tiles_y <= 0:
		return

	_shd_tex = W3Shd.load_texture(p_shd_bytes, tiles_x, tiles_y)
	var shd_ok: bool = _shd_tex != null
	if not shd_ok:
		_shd_tex = W3Shd.blank_texture()

	var height_img := Image.create(corners_x, corners_y, false, Image.FORMAT_RF)
	# HiveWE terrain.vert: normals read ground_heights (raw corner ground_height,
	# no cliff layers / ramp lift) while positions read clif_levels. Splitting the
	# two keeps cliff steps from stamping lit/dark squares onto skirt tiles.
	var normal_height_img := Image.create(corners_x, corners_y, false, Image.FORMAT_RF)
	var exists_img := Image.create(tiles_x, tiles_y, false, Image.FORMAT_R8)
	for y in corners_y:
		for x in corners_x:
			var hy: float = map.corner_world_pos(x, y).y
			height_img.set_pixel(x, y, Color(hy, 0, 0, 1))
			var c = map.get_corner(x, y)
			var raw_h: float = 0.0 if c == null else float(c.ground_height) * W3EMap.TILE_SIZE
			normal_height_img.set_pixel(x, y, Color(raw_h, 0, 0, 1))

	# Slice every tileset atlas into LAYER_STRIDE sub-image layers of one Texture2DArray.
	var ground_arrays := _build_ground_arrays()

	# Cliff / hole existence mask.
	for y in tiles_y:
		for x in tiles_x:
			var cull := _should_cull_ground(x, y)
			exists_img.set_pixel(x, y, Color(0, 0, 0, 1) if cull else Color(1, 0, 0, 1))

	# Per-tile uvec4 (HiveWE update_ground_textures) → RGBAF tile_data.
	var tile_data_img := _build_tile_data(tiles_x, tiles_y)

	var height_tex := ImageTexture.create_from_image(height_img)
	var normal_height_tex := ImageTexture.create_from_image(normal_height_img)
	var exists_tex := ImageTexture.create_from_image(exists_img)
	var tile_data_tex := ImageTexture.create_from_image(tile_data_img)

	var y_min := 1.0e9
	var y_max := -1.0e9
	for y in corners_y:
		for x in corners_x:
			var hy: float = map.corner_world_pos(x, y).y
			y_min = minf(y_min, hy)
			y_max = maxf(y_max, hy)
	var x0: float = map.center_offset.x
	var z0: float = map.center_offset.y
	var x1: float = x0 + float(tiles_x) * W3EMap.TILE_SIZE
	var z1: float = z0 + float(tiles_y) * W3EMap.TILE_SIZE
	var custom_aabb := AABB(
		Vector3(minf(x0, x1), y_min - 64.0, minf(z0, z1)),
		Vector3(absf(x1 - x0), (y_max - y_min) + 128.0, absf(z1 - z0))
	)

	var sh: Shader = load("res://addons/w3kit/shaders/w3_ground_unified.gdshader")
	var unit_quad := _make_unit_quad()

	# Single MultiMesh: one unit-quad instance per tile. Tile coords come from
	# INSTANCE_ID in the shader; per-tile layers come from tile_data. No per-layer pass.
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = unit_quad
	mm.instance_count = tiles_x * tiles_y
	for i in mm.instance_count:
		var tile_x: int = i % tiles_x
		var tile_y: int = int(i / tiles_x)
		var wx: float = map.center_offset.x + float(tile_x) * W3EMap.TILE_SIZE
		var wz: float = map.center_offset.y + float(tile_y) * W3EMap.TILE_SIZE
		mm.set_instance_transform(i, Transform3D(Basis.IDENTITY, Vector3(wx, 0.0, wz)))

	var mat := ShaderMaterial.new()
	mat.shader = sh
	mat.set_shader_parameter("tile_data", tile_data_tex)
	mat.set_shader_parameter("height_tex", height_tex)
	mat.set_shader_parameter("height_normal_tex", normal_height_tex)
	mat.set_shader_parameter("height_normal_enabled", true)
	mat.set_shader_parameter("exists_tex", exists_tex)
	mat.set_shader_parameter("ground_array0", ground_arrays[0])
	mat.set_shader_parameter("ground_array1", ground_arrays[1])
	mat.set_shader_parameter("ground_array2", ground_arrays[2])
	mat.set_shader_parameter("shd_tex", _shd_tex)
	mat.set_shader_parameter("shd_enabled", shd_ok)
	mat.set_shader_parameter("map_tiles", Vector2i(tiles_x, tiles_y))
	mat.set_shader_parameter("map_corners", Vector2i(corners_x, corners_y))
	mat.set_shader_parameter("tile_size", W3EMap.TILE_SIZE)
	mat.set_shader_parameter("map_origin", map.center_offset)
	mat.set_shader_parameter("map_size", Vector2(float(tiles_x) * W3EMap.TILE_SIZE, float(tiles_y) * W3EMap.TILE_SIZE))
	mat.set_shader_parameter("light_dir", Vector3(0.45, 0.85, 0.28).normalized())
	mat.set_shader_parameter("light_ambient", 0.35)
	mat.set_shader_parameter("light_scale", 1.1)
	mat.set_shader_parameter("soft_tint", false)
	_mats.append(mat)

	var mmi := MultiMeshInstance3D.new()
	mmi.name = "GpuTerrain"
	mmi.multimesh = mm
	mmi.material_override = mat
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mmi.custom_aabb = custom_aabb
	mmi.extra_cull_margin = 256.0
	add_child(mmi)
	_mmis.append(mmi)

	_build_height_collision(height_img, tiles_x, tiles_y)
	print("W3GpuTerrain: unified tilesets=%d layers=%d tiles=%dx%d shd=%s aabb_y=[%.0f,%.0f]" % [
		_layer_ids.size(), _layer_ids.size() * LAYER_STRIDE, tiles_x, tiles_y,
		"yes" if shd_ok else "no", y_min, y_max
	])


func apply_lighting(light_direction: Vector3, ambient: float = 0.35, scale: float = 1.1) -> void:
	var d := light_direction
	if d.length_squared() < 0.0001:
		d = Vector3(0.45, 0.85, 0.28)
	d = d.normalized()
	for mat in _mats:
		if mat == null:
			continue
		mat.set_shader_parameter("light_dir", d)
		mat.set_shader_parameter("light_ambient", ambient)
		mat.set_shader_parameter("light_scale", scale)


## Flat albedo for blend QA: no Lambert darkening, no SHD.
func set_flat_inspect(on: bool) -> void:
	for mat in _mats:
		if mat == null:
			continue
		mat.set_shader_parameter("flat_lighting", on)


func set_soft_tint(on: bool) -> void:
	for mat in _mats:
		if mat == null:
			continue
		mat.set_shader_parameter("soft_tint", on)


func _make_unit_quad() -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var verts := [
		Vector3(0, 0, 0), Vector3(1, 0, 0), Vector3(0, 0, 1),
		Vector3(1, 0, 0), Vector3(1, 0, 1), Vector3(0, 0, 1),
	]
	for v in verts:
		st.set_normal(Vector3.UP)
		st.set_uv(Vector2(v.x, v.z))
		st.add_vertex(v)
	return st.commit()


func _build_height_collision(height_img: Image, tiles_x: int, tiles_y: int) -> void:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var step := 1
	if tiles_x * tiles_y > 20000:
		step = 2
	for y in range(0, tiles_y, step):
		for x in range(0, tiles_x, step):
			if _should_cull_ground(x, y):
				continue
			var x1: int = mini(x + step, tiles_x)
			var y1: int = mini(y + step, tiles_y)
			var p00 := _corner_pos(x, y)
			var p10 := _corner_pos(x1, y)
			var p01 := _corner_pos(x, y1)
			var p11 := _corner_pos(x1, y1)
			st.add_vertex(p00)
			st.add_vertex(p10)
			st.add_vertex(p01)
			st.add_vertex(p10)
			st.add_vertex(p11)
			st.add_vertex(p01)
	var mesh := st.commit()
	var body := StaticBody3D.new()
	body.name = "TerrainCollision"
	var col := CollisionShape3D.new()
	var shape := ConcavePolygonShape3D.new()
	shape.set_faces(mesh.get_faces())
	col.shape = shape
	body.add_child(col)
	add_child(body)


func _corner_pos(x: int, y: int) -> Vector3:
	return map.corner_world_pos(x, y)


func _ground_tex(tileset_id: String) -> Texture2D:
	return textures.get_ground_texture(tileset_id)


func _tex_to_rgba_image(tex: Texture2D) -> Image:
	if tex == null:
		var blank := Image.create(512, 256, false, Image.FORMAT_RGBA8)
		blank.fill(Color(0.35, 0.5, 0.28))
		return blank
	var img: Image = tex.get_image()
	if img == null:
		var blank2 := Image.create(512, 256, false, Image.FORMAT_RGBA8)
		blank2.fill(Color(0.35, 0.5, 0.28))
		return blank2
	if img.get_format() != Image.FORMAT_RGBA8:
		img.convert(Image.FORMAT_RGBA8)
	return img.duplicate()


## Slice each tileset atlas into LAYER_STRIDE 64x64 layers of one Texture2DArray.
## Sub 0..15 = blend shapes (bitset = layer), 16..31 = extended deco variants.
## Non-extended tilesets leave 16..31 transparent (overlays never index them).
const CELL := 64


func _build_ground_arrays() -> Array:
	_tileset_extended = PackedByteArray()
	var layers: Array[Image] = []
	for i in _layer_ids.size():
		var tid: String = _layer_ids[i]
		var img: Image = null
		if tid == "__BLIGHT__" and textures.has_method("get_blight_image"):
			img = textures.get_blight_image(str(map.tileset))
		elif textures.has_method("get_ground_image"):
			img = textures.get_ground_image(tid)
		if img == null:
			img = _tex_to_rgba_image(_ground_tex(tid))
		if img != null and img.get_format() != Image.FORMAT_RGBA8:
			img = img.duplicate()
			img.convert(Image.FORMAT_RGBA8)
		var extended: bool = img != null and img.get_width() >= img.get_height() * 2
		_tileset_extended.append(1 if extended else 0)
		for sub in LAYER_STRIDE:
			var cell := _slice_cell(img, sub, extended)
			# HiveWE GroundTexture: GL_LINEAR_MIPMAP_LINEAR with generated mips.
			cell.generate_mipmaps()
			layers.append(cell)
	if layers.size() > ARRAY_BANK_SIZE * ARRAY_BANK_COUNT:
		push_error("W3GpuTerrain: %d layers exceed supported %d" % [layers.size(), ARRAY_BANK_SIZE * ARRAY_BANK_COUNT])
	var arrays: Array = []
	for bank in range(ARRAY_BANK_COUNT):
		var bank_layers: Array[Image] = []
		var first := bank * ARRAY_BANK_SIZE
		var last := mini(first + ARRAY_BANK_SIZE, layers.size())
		for layer_index in range(first, last):
			bank_layers.append(layers[layer_index])
		if bank_layers.is_empty():
			var transparent := Image.create(CELL, CELL, false, Image.FORMAT_RGBA8)
			transparent.fill(Color(0, 0, 0, 0))
			transparent.generate_mipmaps() # array layers must share the mip flag
			bank_layers.append(transparent)
		var array := Texture2DArray.new()
		array.create_from_images(bank_layers)
		arrays.append(array)
	return arrays


## Extract sub-image `sub` (0..31) as a 64x64 RGBA8 cell. sub<16 → left 4x4 block;
## sub>=16 → right 4x4 block (extended deco). Missing → transparent.
func _slice_cell(img: Image, sub: int, extended: bool) -> Image:
	var out := Image.create(CELL, CELL, false, Image.FORMAT_RGBA8)
	out.fill(Color(0, 0, 0, 0))
	if img == null or (sub >= 16 and not extended):
		return out
	var block_x := 0
	var local := sub
	if sub >= 16:
		block_x = 4
		local = sub - 16
	# Cell size scales with source: atlas is 4 rows tall.
	var csz: int = int(img.get_height() / 4)
	if csz <= 0:
		return out
	var sx: int = (block_x + (local % 4)) * csz
	var sy: int = int(local / 4) * csz
	if sx + csz > img.get_width() or sy + csz > img.get_height():
		return out
	var cell := img.get_region(Rect2i(sx, sy, csz, csz))
	if csz != CELL:
		cell.resize(CELL, CELL, Image.INTERPOLATE_BILINEAR)
	out.blit_rect(cell, Rect2i(0, 0, CELL, CELL), Vector2i(0, 0))
	return out


func _prepare_blight() -> void:
	# HiveWE/Warsmash append the current tileset's blight atlas after all W3E
	# ground tiles, giving it the highest texture priority.
	var existing := _layer_ids.find("__BLIGHT__")
	if existing >= 0:
		_blight_index = existing
		return
	_blight_index = _layer_ids.size()
	_layer_ids.append("__BLIGHT__")


func _prepare_cliff_to_ground() -> void:
	_cliff_to_ground = PackedInt32Array()
	if textures == null or not textures.has_method("get_cliff_ground_tile"):
		# Fallback: common Felwood CCgr→Cgrs, CCdi→Cdrt style via name match.
		for i in map.cliff_tilesets.size():
			var cid := str(map.cliff_tilesets[i])
			var mapped := 0
			var want := cid
			if cid.begins_with("CC"):
				want = "C" + cid.substr(2)
			elif cid.length() >= 4:
				want = cid
			for gi in map.ground_tilesets.size():
				if str(map.ground_tilesets[gi]) == want or str(map.ground_tilesets[gi]).begins_with(want.left(3)):
					mapped = gi
					break
			_cliff_to_ground.append(mapped)
		print("W3GpuTerrain: cliff_to_ground=%s" % [_cliff_to_ground])
		return
	for i in map.cliff_tilesets.size():
		var gid: String = textures.get_cliff_ground_tile(str(map.cliff_tilesets[i]))
		var mapped := 0
		for gi in map.ground_tilesets.size():
			if str(map.ground_tilesets[gi]) == gid:
				mapped = gi
				break
		_cliff_to_ground.append(mapped)
	print("W3GpuTerrain: cliff_to_ground=%s" % [_cliff_to_ground])


## HiveWE stores CliffTrans-owned ground holes separately as `corner_romp`.
## Precompute that tile mask once; querying ramp transitions per corner while
## packing every ground tile is needlessly expensive.
func _prepare_romp_tiles() -> void:
	_romp_tiles = PackedByteArray()
	_romp_tiles_x = 0
	_romp_tiles_y = 0
	if map == null:
		return
	_romp_tiles_x = map.tile_count_x()
	_romp_tiles_y = map.tile_count_y()
	_romp_tiles.resize(_romp_tiles_x * _romp_tiles_y)
	_romp_tiles.fill(0)
	if not map.has_method("get_ramp_transition"):
		return
	for y in range(_romp_tiles_y):
		for x in range(_romp_tiles_x):
			var transition: Dictionary = map.get_ramp_transition(x, y)
			if transition.is_empty():
				continue
			_mark_romp_tile(transition.get("source", Vector2i(x, y)))
			_mark_romp_tile(transition.get("hide_neighbor", Vector2i(-1, -1)))


func _mark_romp_tile(tile: Vector2i) -> void:
	if tile.x < 0 or tile.y < 0 or tile.x >= _romp_tiles_x or tile.y >= _romp_tiles_y:
		return
	_romp_tiles[tile.y * _romp_tiles_x + tile.x] = 1


func _tile_is_romp(x: int, y: int) -> bool:
	if x < 0 or y < 0 or x >= _romp_tiles_x or y >= _romp_tiles_y:
		return false
	return _romp_tiles[y * _romp_tiles_x + x] != 0


func _should_cull_ground(x: int, y: int) -> bool:
	# HiveWE/Warsmash: cull cliff tiles except full-ramp entrances.
	# Partial ramps additionally claim their adjacent placement tile (`romp` in
	# Warsmash), so a displaced CliffTrans model never exposes a square hole.
	if _tile_is_romp(x, y):
		return true
	if not _tile_has_layer_cliff(x, y):
		return false
	return not _is_ramp_entrance(x, y)


func _is_ramp_entrance(x: int, y: int) -> bool:
	return map.is_ramp_entrance(x, y)


func _remap_variation(raw_variation: int, extended: bool) -> int:
	# open-realm SetTileUV when tile==15 and extended.
	if extended:
		if raw_variation < 16:
			return 16 + raw_variation
		elif raw_variation == 16:
			return 15
		return 0
	if raw_variation == 0:
		return 0
	return 15


## HiveWE update_ground_textures port. Per tile: 4 corner textures → sort+unique →
## base (lowest idx) fills with deco variation, overlays carry 4-bit corner bitset.
## Packs into RGBAF: R=base(opaque), G/B/A = overlays in ascending priority.
## Shader composites A→R, so highest-index texture (topmost) lands in .a.
func _build_tile_data(tiles_x: int, tiles_y: int) -> Image:
	var img := Image.create(tiles_x, tiles_y, false, Image.FORMAT_RGBAF)
	var empty := Color(EMPTY_LAYER, EMPTY_LAYER, EMPTY_LAYER, EMPTY_LAYER)
	for ty in tiles_y:
		for tx in tiles_x:
			if _should_cull_ground(tx, ty):
				img.set_pixel(tx, ty, empty)
				continue
			# HiveWE bit order: br | bl<<1 | tr<<2 | tl<<3.
			var bl := _corner_tex(tx, ty)
			var br := _corner_tex(tx + 1, ty)
			var tl := _corner_tex(tx, ty + 1)
			var tr := _corner_tex(tx + 1, ty + 1)
			var uniq: Array = [bl, br, tl, tr]
			uniq.sort()
			var u: Array = []
			for v in uniq:
				if u.is_empty() or u[u.size() - 1] != v:
					u.append(v)
			var layers := [EMPTY_LAYER, EMPTY_LAYER, EMPTY_LAYER, EMPTY_LAYER]
			# Base: lowest tileset index, deco variation from bottom-left corner.
			var base_idx: int = int(u[0])
			var c00 = map.get_corner(tx, ty)
			var raw_var := 0 if c00 == null else int(c00.ground_variation)
			var ext: bool = base_idx < _tileset_extended.size() and _tileset_extended[base_idx] != 0
			layers[0] = float(base_idx * LAYER_STRIDE + _remap_variation(raw_var, ext))
			# Overlays: bitset of which corners equal this texture → blend sub-image.
			for k in range(1, u.size()):
				var tex: int = int(u[k])
				var bits := 0
				if br == tex: bits |= 1
				if bl == tex: bits |= 2
				if tr == tex: bits |= 4
				if tl == tex: bits |= 8
				layers[k] = float(tex * LAYER_STRIDE + bits)
			img.set_pixel(tx, ty, Color(layers[0], layers[1], layers[2], layers[3]))
	return img


func _corner_tex(x: int, y: int) -> int:
	var c = map.get_corner(x, y)
	if c == null:
		return 0
	# HiveWE real_tile_texture: nearby romp always wins; a normal cliff only
	# replaces a non-ramp corner. Keeping authored ramp textures prevents the
	# solid-color squares seen around mixed ground transitions.
	if _corner_near_romp(x, y) or (_corner_near_cliff(x, y) and not bool(c.ramp)):
		var ct: int = int(c.cliff_texture)
		if ct == 15:
			ct = 1
		if ct >= 0 and ct < _cliff_to_ground.size():
			return int(_cliff_to_ground[ct])
	if c.blight and _blight_index >= 0:
		return _blight_index
	return int(c.ground_texture)


func _corner_near_romp(x: int, y: int) -> bool:
	# HiveWE: OR romp on the four tiles meeting at this corner (tile BLs).
	for pair in [[x, y], [x - 1, y], [x, y - 1], [x - 1, y - 1]]:
		if _tile_is_romp(int(pair[0]), int(pair[1])):
			return true
	return false


func _corner_near_cliff(x: int, y: int) -> bool:
	# HiveWE: OR corner_cliff on the four tiles meeting at this corner.
	for pair in [[x, y], [x - 1, y], [x, y - 1], [x - 1, y - 1]]:
		var tx: int = int(pair[0])
		var ty: int = int(pair[1])
		if tx < 0 or ty < 0 or tx >= map.tile_count_x() or ty >= map.tile_count_y():
			continue
		if _tile_has_layer_cliff(tx, ty):
			return true
	return false


func _tile_has_layer_cliff(tx: int, ty: int) -> bool:
	if map != null and map.has_method("tile_has_layer_cliff"):
		return map.tile_has_layer_cliff(tx, ty)
	var c00 = map.get_corner(tx, ty)
	var c10 = map.get_corner(tx + 1, ty)
	var c01 = map.get_corner(tx, ty + 1)
	var c11 = map.get_corner(tx + 1, ty + 1)
	if c00 == null or c10 == null or c01 == null or c11 == null:
		return false
	var h0: int = int(c00.layer_height)
	return int(c10.layer_height) != h0 or int(c01.layer_height) != h0 or int(c11.layer_height) != h0
