class_name W3LevelData
extends Resource
## Project-owned, serialization-friendly terrain data produced by W3LevelImporter.
## Runtime systems consume this interface directly and do not parse W3E again.

const TILE_SIZE := 128.0
const FORMAT_VERSION := 1
const W3Corner = preload("res://addons/w3kit/w3_corner.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")

const FLAG_MAP_EDGE := 1
const FLAG_RAMP := 2
const FLAG_BLIGHT := 4
const FLAG_WATER := 8
const FLAG_BOUNDARY := 16

@export var format_version: int = FORMAT_VERSION
@export var source_path: String = ""
@export var source_w3e_sha256: String = ""
@export var imported_at_utc: String = ""
@export var w3e_version: int = 11
@export var tileset: String = "L"
@export var custom_tileset: bool = false
@export var ground_tilesets: PackedStringArray = PackedStringArray()
@export var cliff_tilesets: PackedStringArray = PackedStringArray()
@export var width: int = 0
@export var height: int = 0
@export var center_offset: Vector2 = Vector2.ZERO

@export var ground_heights: PackedFloat32Array = PackedFloat32Array()
@export var water_heights: PackedFloat32Array = PackedFloat32Array()
@export var corner_flags: PackedByteArray = PackedByteArray()
@export var ground_textures: PackedByteArray = PackedByteArray()
@export var ground_variations: PackedByteArray = PackedByteArray()
@export var cliff_variations: PackedByteArray = PackedByteArray()
@export var cliff_textures: PackedByteArray = PackedByteArray()
@export var layer_heights: PackedByteArray = PackedByteArray()

@export var shadow_bytes: PackedByteArray = PackedByteArray()
@export var pathing_bytes: PackedByteArray = PackedByteArray()
@export var map_info_bytes: PackedByteArray = PackedByteArray()
@export var game_version_major: int = 0
@export var game_version_minor: int = 0
@export var game_version_patch: int = 0
@export var game_version_build: int = 0
@export var doodad_bytes: PackedByteArray = PackedByteArray()
@export var unit_bytes: PackedByteArray = PackedByteArray()
@export var unit_override_bytes: PackedByteArray = PackedByteArray()
@export var doodad_override_bytes: PackedByteArray = PackedByteArray()
@export var destructable_override_bytes: PackedByteArray = PackedByteArray()

var map_dir: String = ""
var _corner_cache: Array = []


static func from_w3e(map):
	var script = load("res://addons/w3kit/w3_level_data.gd")
	var out = script.new()
	out.w3e_version = int(map.version)
	out.tileset = str(map.tileset)
	out.custom_tileset = bool(map.custom_tileset)
	out.ground_tilesets = map.ground_tilesets.duplicate()
	out.cliff_tilesets = map.cliff_tilesets.duplicate()
	out.width = int(map.width)
	out.height = int(map.height)
	out.center_offset = map.center_offset
	var count: int = out.width * out.height
	out.ground_heights.resize(count)
	out.water_heights.resize(count)
	out.corner_flags.resize(count)
	out.ground_textures.resize(count)
	out.ground_variations.resize(count)
	out.cliff_variations.resize(count)
	out.cliff_textures.resize(count)
	out.layer_heights.resize(count)
	for y in out.height:
		for x in out.width:
			var i: int = y * out.width + x
			var c = map.get_corner(x, y)
			out.ground_heights[i] = c.ground_height
			out.water_heights[i] = c.water_height
			var flags := 0
			if c.map_edge: flags |= FLAG_MAP_EDGE
			if c.ramp: flags |= FLAG_RAMP
			if c.blight: flags |= FLAG_BLIGHT
			if c.water: flags |= FLAG_WATER
			if c.boundary: flags |= FLAG_BOUNDARY
			out.corner_flags[i] = flags
			out.ground_textures[i] = clampi(c.ground_texture, 0, 255)
			out.ground_variations[i] = clampi(c.ground_variation, 0, 255)
			out.cliff_variations[i] = clampi(c.cliff_variation, 0, 255)
			out.cliff_textures[i] = clampi(c.cliff_texture, 0, 255)
			out.layer_heights[i] = clampi(c.layer_height, 0, 255)
	return out


func is_valid() -> bool:
	if format_version != FORMAT_VERSION or width < 2 or height < 2:
		return false
	var count: int = width * height
	return (
		ground_heights.size() == count
		and water_heights.size() == count
		and corner_flags.size() == count
		and ground_textures.size() == count
		and ground_variations.size() == count
		and cliff_variations.size() == count
		and cliff_textures.size() == count
		and layer_heights.size() == count
	)


func tile_count_x() -> int:
	return maxi(0, width - 1)


func tile_count_y() -> int:
	return maxi(0, height - 1)


func get_corner(x: int, y: int):
	if x < 0 or y < 0 or x >= width or y >= height:
		return null
	_ensure_corner_cache()
	var i: int = y * width + x
	var cached = _corner_cache[i]
	if cached != null:
		return cached
	var c = W3Corner.new()
	c.ground_height = ground_heights[i]
	c.water_height = water_heights[i]
	var flags: int = corner_flags[i]
	c.map_edge = (flags & FLAG_MAP_EDGE) != 0
	c.ramp = (flags & FLAG_RAMP) != 0
	c.blight = (flags & FLAG_BLIGHT) != 0
	c.water = (flags & FLAG_WATER) != 0
	c.boundary = (flags & FLAG_BOUNDARY) != 0
	c.ground_texture = ground_textures[i]
	c.ground_variation = ground_variations[i]
	c.cliff_variation = cliff_variations[i]
	c.cliff_texture = cliff_textures[i]
	c.layer_height = layer_heights[i]
	_corner_cache[i] = c
	return c


func corner_world_pos(x: int, y: int) -> Vector3:
	var wx := center_offset.x + float(x) * TILE_SIZE
	var wz := center_offset.y + float(y) * TILE_SIZE
	return Vector3(wx, corner_world_y(x, y), wz)


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
	return not (c00.layer_height == c11.layer_height and c01.layer_height == c10.layer_height)


func read_file(rel: String) -> PackedByteArray:
	match rel.replace("\\", "/").to_lower():
		"war3map.shd": return shadow_bytes
		"war3map.wpm": return pathing_bytes
		"war3map.w3i": return map_info_bytes
		"war3map.doo": return doodad_bytes
		"war3mapunits.doo": return unit_bytes
		"war3map.w3u": return unit_override_bytes
		"war3map.w3d": return doodad_override_bytes
		"war3map.w3b": return destructable_override_bytes
		_: return PackedByteArray()


func resolve_path(rel: String) -> String:
	return W3Paths.resolve_game_path(rel)


func _ensure_corner_cache() -> void:
	var count: int = width * height
	if _corner_cache.size() != count:
		_corner_cache.resize(count)
		_corner_cache.fill(null)
