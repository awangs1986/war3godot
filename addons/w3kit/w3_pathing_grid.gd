extends RefCounted
## Warsmash PathingGrid port (environment/PathingGrid.java).
## Static war3map.wpm pathing cells, 32 world units per cell, W3 (x,y) plane.
## Only the static grid is ported; the dynamic building/tree overlay is not
## needed yet because the debug hero is the only simulated unit.

const CELL_SIZE := 32.0

## PathingGrid.PathingFlags
const UNWALKABLE := 0x2
const UNFLYABLE := 0x4
const UNBUILDABLE := 0x8

static var last_error: String = ""

var width: int = 0
var height: int = 0
var center_offset: Vector2 = Vector2.ZERO
var cells: PackedByteArray = PackedByteArray()
## PathingGrid.dynamicPathingOverlay: unit/building collision blitted on load.
var dynamic_overlay: PackedByteArray = PackedByteArray()
## Cached "walkable with 32 collision" cells (see is_pathable_foot fast path).
var _foot32: PackedByteArray = PackedByteArray()


## bytes = war3map.wpm content. center_offset = W3E center offset (same value
## Warsmash passes from terrain into `new PathingGrid(terrainPathing, centerOffset)`).
static func load_bytes(bytes: PackedByteArray, p_center_offset: Vector2):
	last_error = ""
	if bytes.size() < 16:
		last_error = "wpm too small"
		return null
	if bytes.slice(0, 4).get_string_from_ascii() != "MP3W":
		last_error = "bad wpm magic"
		return null
	var w := bytes.decode_u32(8)
	var h := bytes.decode_u32(12)
	if w <= 0 or h <= 0 or bytes.size() < 16 + w * h:
		last_error = "wpm truncated: %dx%d" % [w, h]
		return null
	var grid = load("res://addons/w3kit/w3_pathing_grid.gd").new()
	grid.width = w
	grid.height = h
	grid.center_offset = p_center_offset
	grid.cells = bytes.slice(16, 16 + w * h)
	return grid


func get_cell_x(x: float) -> int:
	return int((x - center_offset.x) / CELL_SIZE)


func get_cell_y(y: float) -> int:
	return int((y - center_offset.y) / CELL_SIZE)


## Cell centers (PathingGrid.getWorldX/getWorldY).
func get_world_x(cell_x: int) -> float:
	return cell_x * CELL_SIZE + center_offset.x + 16.0


func get_world_y(cell_y: int) -> float:
	return cell_y * CELL_SIZE + center_offset.y + 16.0


## Cell corners (PathingGrid.getWorldXFromCorner / getCornerX).
func get_world_x_from_corner(corner_x: int) -> float:
	return corner_x * CELL_SIZE + center_offset.x


func get_world_y_from_corner(corner_y: int) -> float:
	return corner_y * CELL_SIZE + center_offset.y


func get_corner_x(x: float) -> int:
	return int((x + 16.0 - center_offset.x) / CELL_SIZE)


func get_corner_y(y: float) -> int:
	return int((y + 16.0 - center_offset.y) / CELL_SIZE)


func contains(x: float, y: float) -> bool:
	var cx := get_cell_x(x)
	var cy := get_cell_y(y)
	return cx >= 0 and cy >= 0 and cx < width and cy < height


func get_cell_pathing(cell_x: int, cell_y: int) -> int:
	var index := cell_y * width + cell_x
	if index < 0 or index >= cells.size():
		return 0
	var value := int(cells[index])
	if not dynamic_overlay.is_empty():
		value |= dynamic_overlay[index]
	return value


## Warsmash registers unit collision as CWorldCollision rectangles
## (side 2*collisionSize, centered on the unit). Static map units are baked
## into the overlay instead, which makes pathfinding and the movement step see
## the identical blocked set.
func blit_unit_collision(unit_x: float, unit_y: float, collision_size: float) -> void:
	if collision_size <= 0.0:
		return
	if dynamic_overlay.is_empty():
		dynamic_overlay.resize(width * height)
	var min_cx := clampi(get_cell_x(unit_x - collision_size), 0, width - 1)
	var max_cx := clampi(get_cell_x(unit_x + collision_size - 0.01), 0, width - 1)
	var min_cy := clampi(get_cell_y(unit_y - collision_size), 0, height - 1)
	var max_cy := clampi(get_cell_y(unit_y + collision_size - 0.01), 0, height - 1)
	for cy in range(min_cy, max_cy + 1):
		var row := cy * width
		for cx in range(min_cx, max_cx + 1):
			dynamic_overlay[row + cx] |= UNWALKABLE
	_foot32 = PackedByteArray() # dilated cache is stale now


func get_pathing(x: float, y: float) -> int:
	return get_cell_pathing(get_cell_x(x), get_cell_y(y))


## MovementType.FOOT.isPathable: walkable when UNWALKABLE flag is clear.
func is_pathable_foot(x: float, y: float, collision_size: float) -> bool:
	if collision_size == 0.0:
		if not contains(x, y):
			return false
		return (get_pathing(x, y) & UNWALKABLE) == 0
	# Fast path: with collisionSize == 32 the ±collisionSize offsets land exactly
	# one pathing cell away, so Warsmash's 3x3 sample pattern is equivalent to a
	# one-cell dilation of the unwalkable mask. That dilation is precomputed once.
	if collision_size == 32.0:
		var cx := get_cell_x(x)
		var cy := get_cell_y(y)
		if cx < 0 or cy < 0 or cx >= width or cy >= height:
			return false
		_ensure_foot32()
		return _foot32[cy * width + cx] != 0
	# Warsmash samples a 3x3 pattern at ±collisionSize around the unit center.
	for i in range(-1, 2):
		for j in range(-1, 2):
			var px := x + i * collision_size
			var py := y + j * collision_size
			if not contains(px, py):
				return false
			if (get_pathing(px, py) & UNWALKABLE) != 0:
				return false
	return true


func _ensure_foot32() -> void:
	if not _foot32.is_empty():
		return
	var count := width * height
	# Separable min filter: horizontal pass then vertical pass. Border cells are
	# unwalkable because Warsmash's out-of-grid samples fail contains().
	var walkable := PackedByteArray()
	walkable.resize(count)
	var has_overlay := not dynamic_overlay.is_empty()
	for i in count:
		var v := int(cells[i])
		if has_overlay:
			v |= dynamic_overlay[i]
		walkable[i] = 1 if (v & UNWALKABLE) == 0 else 0
	var horizontal := PackedByteArray()
	horizontal.resize(count)
	for y in height:
		var row := y * width
		horizontal[row] = 0
		horizontal[row + width - 1] = 0
		for x in range(1, width - 1):
			var i := row + x
			horizontal[i] = walkable[i - 1] & walkable[i] & walkable[i + 1]
	_foot32.resize(count)
	for x in width:
		_foot32[x] = 0
		_foot32[(height - 1) * width + x] = 0
	for y in range(1, height - 1):
		var row := y * width
		for x in width:
			var i := row + x
			_foot32[i] = horizontal[i - width] & horizontal[i] & horizontal[i + width]
