extends RefCounted
## Warsmash PathingGrid (algorithm port). Map plane: world X/Z in Godot = Warsmash X/Y.
## Cell size = 32wu. Flags / MovementType match Warsmash PathingFlags + MovementType.


const CELL := 32.0

const UNWALKABLE := 0x2
const UNFLYABLE := 0x4
const UNBUILDABLE := 0x8
const BLOCKVISION := 0x10
const BLIGHTED := 0x20
const UNSWIMABLE := 0x40

enum MovementType { FOOT, FLY, FLOAT, AMPHIBIOUS, HOVER, DISABLED }

var width: int = 0
var height: int = 0
## Godot: center_offset.x → world X, center_offset.y → world Z (Warsmash Y).
var center_offset: Vector2 = Vector2.ZERO
var _pathing: PackedInt32Array = PackedInt32Array()
var _dynamic: PackedInt32Array = PackedInt32Array()


static func from_wpm(wpm, center: Vector2):
	if wpm == null:
		return null
	var g = new()
	g.width = int(wpm.width)
	g.height = int(wpm.height)
	g.center_offset = center
	var n: int = g.width * g.height
	g._pathing.resize(n)
	g._dynamic.resize(n)
	for i in n:
		g._pathing[i] = int(wpm.pathing[i]) & 0xFF
		g._dynamic[i] = 0
	return g


## Fallback when war3map.wpm missing: 4×4 pathing cells per W3E tile from HeightSampler walkability.
static func from_sampler(sampler, center: Vector2):
	if sampler == null or sampler.map == null:
		return null
	var map = sampler.map
	var tiles_x: int = map.tile_count_x()
	var tiles_y: int = map.tile_count_y()
	var g = new()
	g.width = tiles_x * 4
	g.height = tiles_y * 4
	g.center_offset = center
	var n: int = g.width * g.height
	g._pathing.resize(n)
	g._dynamic.resize(n)
	for cy in g.height:
		for cx in g.width:
			var tile := Vector2i(cx / 4, cy / 4)
			var flags := 0
			if not sampler.is_tile_walkable(tile):
				flags = UNWALKABLE | UNSWIMABLE | UNBUILDABLE
			var idx: int = cy * g.width + cx
			g._pathing[idx] = flags
			g._dynamic[idx] = 0
	return g


func contains(world_x: float, world_z: float) -> bool:
	var cx := get_cell_x(world_x)
	var cy := get_cell_y(world_z)
	return cx >= 0 and cy >= 0 and cx < width and cy < height


func get_cell_x(world_x: float) -> int:
	return int((world_x - center_offset.x) / CELL)


func get_cell_y(world_z: float) -> int:
	return int((world_z - center_offset.y) / CELL)


func get_world_x(cell_x: int) -> float:
	return float(cell_x) * CELL + center_offset.x + CELL * 0.5


func get_world_z(cell_y: int) -> float:
	return float(cell_y) * CELL + center_offset.y + CELL * 0.5


func get_corner_x(world_x: float) -> int:
	return int(((world_x + CELL * 0.5) - center_offset.x) / CELL)


func get_corner_y(world_z: float) -> int:
	return int(((world_z + CELL * 0.5) - center_offset.y) / CELL)


func get_world_x_from_corner(corner_x: int) -> float:
	return float(corner_x) * CELL + center_offset.x


func get_world_z_from_corner(corner_y: int) -> float:
	return float(corner_y) * CELL + center_offset.y


func get_cell_pathing(cell_x: int, cell_y: int) -> int:
	if cell_x < 0 or cell_y < 0 or cell_x >= width or cell_y >= height:
		return 0
	var idx: int = cell_y * width + cell_x
	return int(_pathing[idx]) | int(_dynamic[idx])


func get_pathing(world_x: float, world_z: float) -> int:
	return get_cell_pathing(get_cell_x(world_x), get_cell_y(world_z))


static func _flag_set(pathing_value: int, flag: int) -> bool:
	return (pathing_value & flag) != 0


func is_pathable_movement(world_x: float, world_z: float, movement: int) -> bool:
	return _movement_ok(get_pathing(world_x, world_z), movement)


func is_pathable(world_x: float, world_z: float, movement: int, collision_size: float) -> bool:
	if collision_size <= 0.0:
		if not contains(world_x, world_z):
			return false
		return is_pathable_movement(world_x, world_z, movement)
	for i in range(-1, 2):
		for j in range(-1, 2):
			var px: float = world_x + float(i) * collision_size
			var pz: float = world_z + float(j) * collision_size
			if not contains(px, pz) or not is_pathable_movement(px, pz, movement):
				return false
	return true


func is_cell_pathable(cell_x: int, cell_y: int, movement: int, collision_size: float = 0.0) -> bool:
	return is_pathable(get_world_x(cell_x), get_world_z(cell_y), movement, collision_size)


static func _movement_ok(pathing_value: int, movement: int) -> bool:
	match movement:
		MovementType.FLY:
			return not _flag_set(pathing_value, UNFLYABLE)
		MovementType.FLOAT:
			return not _flag_set(pathing_value, UNSWIMABLE)
		MovementType.AMPHIBIOUS:
			return (not _flag_set(pathing_value, UNWALKABLE)) or (not _flag_set(pathing_value, UNSWIMABLE))
		MovementType.DISABLED:
			return true
		_:
			# FOOT / HOVER / default
			return not _flag_set(pathing_value, UNWALKABLE)
