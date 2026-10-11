extends RefCounted
const W3BinaryReader = preload("res://addons/w3kit/w3_binary_reader.gd")
const W3Corner = preload("res://addons/w3kit/w3_corner.gd")
## Parsed war3map.w3e environment.

const TILE_SIZE := 128.0
const MAX_TILESET_COUNT := 64
const MAX_CORNER_DIMENSION := 4097
const MAX_CORNER_COUNT := 4097 * 4097
const CORNER_BYTES := 7

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
	var c = get_corner(x, y)
	var wx = center_offset.x + float(x) * TILE_SIZE
	var wz = center_offset.y + float(y) * TILE_SIZE
	var wy = 0.0 if c == null else c.world_y(TILE_SIZE)
	return Vector3(wx, wy, wz)


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
	if corner_count > MAX_CORNER_COUNT or not r.can_read(corner_count * CORNER_BYTES):
		return _parse_error("truncated or oversized corner grid: %dx%d, remaining=%d" % [m.width, m.height, r.remaining()])
	m.corners.clear()
	for y in m.height:
		var row: Array = []
		for x in m.width:
			row.append(_read_corner(r))
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


static func _read_corner(r):
	var c = W3Corner.new()
	c.ground_height = float(r.read_i16() - 8192) / 512.0
	var water_and_edge = r.read_u16()
	c.water_height = float((water_and_edge & 0x3FFF) - 8192) / 512.0
	c.map_edge = (water_and_edge & 0x4000) != 0
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
