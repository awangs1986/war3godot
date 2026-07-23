extends RefCounted
const W3BinaryReader = preload("res://addons/w3kit/w3_binary_reader.gd")
const W3Corner = preload("res://addons/w3kit/w3_corner.gd")
## Parsed war3map.w3e environment.

const TILE_SIZE := 128.0
const MAX_TILESET_COUNT := 64
const MAX_CORNER_DIMENSION := 4097
const MAX_CORNER_COUNT := 4097 * 4097
## Classic (v11) corners are 7 bytes. Since format version 12 (HiveWE and
## Reforged) texture+flags widen to 16 bits, making each corner 8 bytes.
const CORNER_BYTES := 7
const CORNER_BYTES_V12 := 8

static var last_error: String = ""

var version: int = 11
var tileset: String = "L"
var custom_tileset: bool = false
var ground_tilesets: PackedStringArray = PackedStringArray()
var cliff_tilesets: PackedStringArray = PackedStringArray()
## Corner grid size: (map_width_tiles + 1) x (map_height_tiles + 1)
var width: int = 0
var height: int = 0
var center_offset: Vector2 = Vector2.ZERO
## corners[y][x] — row = Y (south→north), col = X (west→east)
var corners: Array = []


func tile_count_x() -> int:
	return maxi(0, width - 1)


func tile_count_y() -> int:
	return maxi(0, height - 1)


func get_corner(x: int, y: int):
	if y < 0 or y >= corners.size():
		return null
	var row: Array = corners[y]
	if x < 0 or x >= row.size():
		return null
	return row[x] as W3Corner


func corner_world_pos(x: int, y: int) -> Vector3:
	var wx = center_offset.x + float(x) * TILE_SIZE
	var wz = center_offset.y + float(y) * TILE_SIZE
	return Vector3(wx, corner_world_y(x, y), wz)


## HiveWE/Warsmash final ground height. A low-level corner shared by a valid
## full-ramp entrance is lifted half a tile, preventing a gap between the
## entrance ground and its neighbouring CliffTrans model.
func corner_world_y(x: int, y: int) -> float:
	var c = get_corner(x, y)
	if c == null:
		return 0.0
	var y_world := c.world_y(TILE_SIZE)
	for tile_y in range(y - 1, y + 1):
		for tile_x in range(x - 1, x + 1):
			if not is_ramp_entrance(tile_x, tile_y):
				continue
			var c00 = get_corner(tile_x, tile_y)
			var c10 = get_corner(tile_x + 1, tile_y)
			var c01 = get_corner(tile_x, tile_y + 1)
			var c11 = get_corner(tile_x + 1, tile_y + 1)
			var base := mini(mini(c00.layer_height, c10.layer_height), mini(c01.layer_height, c11.layer_height))
			if c.layer_height == base:
				return y_world + TILE_SIZE * 0.5
	return y_world


func is_ramp_entrance(x: int, y: int) -> bool:
	if x < 0 or y < 0 or x >= tile_count_x() or y >= tile_count_y():
		return false
	var c00 = get_corner(x, y)
	var c10 = get_corner(x + 1, y)
	var c01 = get_corner(x, y + 1)
	var c11 = get_corner(x + 1, y + 1)
	if c00 == null or c10 == null or c01 == null or c11 == null:
		return false
	if not (c00.ramp and c10.ramp and c01.ramp and c11.ramp):
		return false
	# A four-ramp plateau is not an entrance. The diagonal layers must differ.
	return not (c00.layer_height == c11.layer_height and c01.layer_height == c10.layer_height)


## Exact partial-ramp placement rule used by Warsmash Terrain.updateCliffMeshes.
## A partial ramp is authored in one W3E tile but its CliffTrans model may
## occupy the preceding tile. `source` and `hide_neighbor` are both ground
## holes; `placement` is where the model's height/texture sampling happens.
func get_ramp_transition(x: int, y: int) -> Dictionary:
	if x < 0 or y < 0 or x >= tile_count_x() or y >= tile_count_y():
		return {}
	var c00 = get_corner(x, y) # BL
	var c10 = get_corner(x + 1, y) # BR
	var c01 = get_corner(x, y + 1) # TL
	var c11 = get_corner(x + 1, y + 1) # TR
	if c00 == null or c10 == null or c01 == null or c11 == null:
		return {}
	if not tile_has_layer_cliff(x, y) or is_ramp_entrance(x, y):
		return {}
	var vertical := bool(c00.ramp) != bool(c10.ramp) and bool(c01.ramp) != bool(c11.ramp)
	var horizontal := bool(c00.ramp) != bool(c01.ramp) and bool(c10.ramp) != bool(c11.ramp)
	if not vertical and not horizontal:
		return {}
	var base := mini(mini(int(c00.layer_height), int(c10.layer_height)), mini(int(c01.layer_height), int(c11.layer_height)))
	var heights := [
		int(c01.layer_height) - base, int(c11.layer_height) - base,
		int(c10.layer_height) - base, int(c00.layer_height) - base,
	]
	# Warsmash ships with DISALLOW_HEIGHT_3_RAMPS enabled: classic map ramps
	# only bridge one layer (L/H). Higher transitions fall back to a cliff.
	for height in heights:
		if int(height) > 1:
			return {}
	var facing_down := int(c01.layer_height) >= int(c00.layer_height) and int(c11.layer_height) >= int(c10.layer_height)
	var facing_left := int(c10.layer_height) >= int(c00.layer_height) and int(c11.layer_height) >= int(c01.layer_height)
	var blocked_x := x + (-1 if facing_left else 1)
	var blocked_y := y + (-1 if facing_down else 1)
	if vertical and tile_has_layer_cliff(x, blocked_y):
		return {}
	if horizontal and tile_has_layer_cliff(blocked_x, y):
		return {}
	var placement := Vector2i(
		x + (-1 if horizontal and facing_left else 0),
		y + (-1 if vertical and facing_down else 0),
	)
	var hide_neighbor := Vector2i(
		x + ((-1 if facing_left else 1) if horizontal else 0),
		y + ((-1 if facing_down else 1) if vertical else 0),
	)
	if placement.x < 0 or placement.y < 0 or placement.x >= tile_count_x() or placement.y >= tile_count_y():
		return {}
	if hide_neighbor.x < 0 or hide_neighbor.y < 0 or hide_neighbor.x >= tile_count_x() or hide_neighbor.y >= tile_count_y():
		return {}
	var tag := ""
	var ordered_corners := [c01, c11, c10, c00]
	for i in range(heights.size()): # TL, TR, BR, BL
		var height: int = int(heights[i])
		var corner = ordered_corners[i]
		var is_ramp_corner := bool(corner.ramp)
		if is_ramp_corner:
			tag += "L" if height == 0 else ("H" if height == 1 else "X")
		else:
			tag += String.chr(65 + height)
	return {
		"tag": tag,
		"base": base,
		"vertical": vertical,
		"horizontal": horizontal,
		"source": Vector2i(x, y),
		"placement": placement,
		"hide_neighbor": hide_neighbor,
	}


## Warsmash's `cliff || romp` ground-hole condition for partial ramps.
func ramp_transition_covers_tile(x: int, y: int) -> bool:
	for sy in range(y - 1, y + 2):
		for sx in range(x - 1, x + 2):
			var transition := get_ramp_transition(sx, sy)
			if transition.is_empty():
				continue
			if transition["source"] == Vector2i(x, y) or transition["hide_neighbor"] == Vector2i(x, y):
				return true
	return false


func tile_has_layer_cliff(x: int, y: int) -> bool:
	if x < 0 or y < 0 or x >= tile_count_x() or y >= tile_count_y():
		return false
	var c00 = get_corner(x, y)
	var c10 = get_corner(x + 1, y)
	var c01 = get_corner(x, y + 1)
	var c11 = get_corner(x + 1, y + 1)
	if c00 == null or c10 == null or c01 == null or c11 == null:
		return false
	var height: int = int(c00.layer_height)
	return int(c10.layer_height) != height or int(c01.layer_height) != height or int(c11.layer_height) != height


static func load_bytes(data: PackedByteArray):
	last_error = ""
	if data.size() < 21:
		return _parse_error("file is too short (%d bytes)" % data.size())
	var r = W3BinaryReader.new(data)
	var magic = r.read_fourcc()
	if magic != "W3E!":
		return _parse_error("bad magic '%s'" % magic)
	var m = preload("res://addons/w3kit/w3e_map.gd").new()
	m.version = r.read_i32()
	m.tileset = r.read_char()
	m.custom_tileset = r.read_i32() != 0
	var ng = r.read_i32()
	if ng < 0 or ng > MAX_TILESET_COUNT or not r.can_read(ng * 4 + 4):
		return _parse_error("invalid ground tileset count: %d" % ng)
	for _i in ng:
		m.ground_tilesets.append(r.read_fourcc())
	var nc = r.read_i32()
	if nc < 0 or nc > MAX_TILESET_COUNT or not r.can_read(nc * 4 + 16):
		return _parse_error("invalid cliff tileset count: %d" % nc)
	for _i in nc:
		m.cliff_tilesets.append(r.read_fourcc())
	m.width = r.read_i32()
	m.height = r.read_i32()
	m.center_offset = Vector2(r.read_f32(), r.read_f32())
	if r.failed:
		return _parse_error(r.error_message)
	if m.width < 2 or m.height < 2 or m.width > MAX_CORNER_DIMENSION or m.height > MAX_CORNER_DIMENSION:
		return _parse_error("invalid corner dimensions: %dx%d" % [m.width, m.height])
	var corner_count: int = m.width * m.height
	var corner_bytes: int = CORNER_BYTES_V12 if m.version >= 12 else CORNER_BYTES
	if corner_count > MAX_CORNER_COUNT or not r.can_read(corner_count * corner_bytes):
		return _parse_error("truncated or oversized corner grid: %dx%d, remaining=%d" % [m.width, m.height, r.remaining()])
	m.corners.clear()
	for y in m.height:
		var row: Array = []
		for x in m.width:
			row.append(_read_corner(r, m.version))
		m.corners.append(row)
	if r.failed:
		return _parse_error(r.error_message)
	return m


static func _parse_error(message: String):
	last_error = message
	return null


static func load_file(path: String):
	if not FileAccess.file_exists(path):
		return _parse_error("file missing %s" % path)
	var f = FileAccess.open(path, FileAccess.READ)
	if f == null:
		return _parse_error("cannot open %s" % path)
	return load_bytes(f.get_buffer(f.get_length()))


static func _read_corner(r, version: int = 11):
	var c = W3Corner.new()
	c.ground_height = float(r.read_i16() - 8192) / 512.0
	var water_and_edge = r.read_u16()
	c.water_height = float((water_and_edge & 0x3FFF) - 8192) / 512.0
	c.map_edge = (water_and_edge & 0x4000) != 0
	if version >= 12:
		# HiveWE terrain.ixx v12 branch: 16-bit field, 6-bit texture, flags at bits 6-9.
		var tex_flags16 = r.read_u16()
		c.ground_texture = tex_flags16 & 0x3F
		c.ramp = (tex_flags16 & 0x40) != 0
		c.blight = (tex_flags16 & 0x80) != 0
		c.water = (tex_flags16 & 0x100) != 0
		c.boundary = (tex_flags16 & 0x200) != 0
	else:
		# Classic 8-bit field (Warsmash Corner.java): 4-bit texture, flags at bits 4-7.
		var tex_flags = r.read_u8()
		c.ground_texture = tex_flags & 0x0F
		c.ramp = (tex_flags & 0x10) != 0
		c.blight = (tex_flags & 0x20) != 0
		c.water = (tex_flags & 0x40) != 0
		c.boundary = (tex_flags & 0x80) != 0
	var variation = r.read_u8()
	c.cliff_variation = (variation & 0xE0) >> 5
	c.ground_variation = variation & 0x1F
	var cliff_layer = r.read_u8()
	c.cliff_texture = (cliff_layer & 0xF0) >> 4
	c.layer_height = cliff_layer & 0x0F
	return c


## Build a small procedural test map (no War3 copyrighted bytes).
static func make_test_map(tiles_x: int = 24, tiles_y: int = 24):
	var m = preload("res://addons/w3kit/w3e_map.gd").new()
	m.version = 11
	m.tileset = "L"
	m.ground_tilesets = PackedStringArray(["Ldrt", "Ldro", "Ldrg", "Lrok"])
	m.cliff_tilesets = PackedStringArray(["CLdi"])
	m.width = tiles_x + 1
	m.height = tiles_y + 1
	m.center_offset = Vector2(
		-0.5 * float(tiles_x) * TILE_SIZE,
		-0.5 * float(tiles_y) * TILE_SIZE
	)
	m.corners.clear()
	for y in m.height:
		var row: Array = []
		for x in m.width:
			var c = W3Corner.new()
			c.ground_texture = (x + y) % 4
			c.layer_height = 2
			c.ground_height = 0.0
			# Plateau + ramp-friendly cliff shelf in the north.
			if y > tiles_y * 0.55 and x > 4 and x < tiles_x - 4:
				c.layer_height = 4
			# Soft slope band (ramp flags) at plateau edge.
			if y == int(tiles_y * 0.55) and x > 4 and x < tiles_x - 4:
				c.ramp = true
				c.layer_height = 3
				c.ground_height = 0.5
			# Gentle hills.
			var hx = sin(float(x) * 0.35) * cos(float(y) * 0.28) * 0.35
			c.ground_height += hx
			row.append(c)
		m.corners.append(row)
	return m


static func save_bytes(m) -> PackedByteArray:
	var buf = PackedByteArray()
	buf.append_array("W3E!".to_ascii_buffer())
	_append_i32(buf, m.version)
	buf.append(m.tileset.unicode_at(0))
	_append_i32(buf, 1 if m.custom_tileset else 0)
	_append_i32(buf, m.ground_tilesets.size())
	for id in m.ground_tilesets:
		buf.append_array(id.to_ascii_buffer())
	_append_i32(buf, m.cliff_tilesets.size())
	for id in m.cliff_tilesets:
		buf.append_array(id.to_ascii_buffer())
	_append_i32(buf, m.width)
	_append_i32(buf, m.height)
	_append_f32(buf, m.center_offset.x)
	_append_f32(buf, m.center_offset.y)
	for y in m.height:
		for x in m.width:
			var c = m.corners[y][x]
			_append_i16(buf, int(c.ground_height * 512.0) + 8192)
			var we = int(c.water_height * 512.0) + 8192
			if c.map_edge:
				we |= 0x4000
			_append_u16(buf, we)
			if m.version >= 12:
				var tf16 = c.ground_texture & 0x3F
				if c.ramp:
					tf16 |= 0x40
				if c.blight:
					tf16 |= 0x80
				if c.water:
					tf16 |= 0x100
				if c.boundary:
					tf16 |= 0x200
				_append_u16(buf, tf16)
			else:
				var tf = c.ground_texture & 0x0F
				if c.ramp:
					tf |= 0x10
				if c.blight:
					tf |= 0x20
				if c.water:
					tf |= 0x40
				if c.boundary:
					tf |= 0x80
				buf.append(tf)
			buf.append(((c.cliff_variation & 7) << 5) | (c.ground_variation & 0x1F))
			buf.append(((c.cliff_texture & 0x0F) << 4) | (c.layer_height & 0x0F))
	return buf


static func _append_i16(buf: PackedByteArray, v: int) -> void:
	var t = PackedByteArray()
	t.resize(2)
	t.encode_s16(0, v)
	buf.append_array(t)


static func _append_u16(buf: PackedByteArray, v: int) -> void:
	var t = PackedByteArray()
	t.resize(2)
	t.encode_u16(0, v)
	buf.append_array(t)


static func _append_i32(buf: PackedByteArray, v: int) -> void:
	var t = PackedByteArray()
	t.resize(4)
	t.encode_s32(0, v)
	buf.append_array(t)


static func _append_f32(buf: PackedByteArray, v: float) -> void:
	var t = PackedByteArray()
	t.resize(4)
	t.encode_float(0, v)
	buf.append_array(t)
