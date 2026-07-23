extends RefCounted
## Dynamic unit-vs-unit collision — port of Warsmash CWorldCollision, scoped to
## ground units. Warsmash uses a quadtree; a uniform spatial hash is equivalent
## for our unit counts and simpler to keep correct. Units occupy an
## axis-aligned square of side 2 * collision_size on the W3 ground plane.

const CELL := 256.0

## cell key (Vector2i) -> Array of units whose rect overlaps that cell.
var _cells: Dictionary = {}


func add_unit(unit) -> void:
	for key in _keys_for_rect(unit.rect_min(), unit.rect_max()):
		_bucket(key).append(unit)


func remove_unit(unit) -> void:
	for key in _keys_for_rect(unit.rect_min(), unit.rect_max()):
		var bucket: Array = _cells.get(key, [])
		bucket.erase(unit)
		if bucket.is_empty():
			_cells.erase(key)


## Warsmash CWorldCollision.translate: cheap incremental move.
func translate_unit(unit, old_min: Vector2, old_max: Vector2) -> void:
	var new_keys := _keys_for_rect(unit.rect_min(), unit.rect_max())
	var old_keys := _keys_for_rect(old_min, old_max)
	if new_keys == old_keys:
		return
	for key in old_keys:
		if key not in new_keys:
			var bucket: Array = _cells.get(key, [])
			bucket.erase(unit)
			if bucket.is_empty():
				_cells.erase(key)
	for key in new_keys:
		if key not in old_keys:
			var bucket := _bucket(key)
			if unit not in bucket:
				bucket.append(unit)


## Warsmash CWorldCollision.intersectsAnythingOtherThan (ground layer).
func intersects_anything_other_than(rect_min: Vector2, rect_max: Vector2, ignore) -> bool:
	for key in _keys_for_rect(rect_min, rect_max):
		for other in _cells.get(key, []):
			if other == ignore or other.is_dead():
				continue
			var omin: Vector2 = other.rect_min()
			var omax: Vector2 = other.rect_max()
			if rect_min.x < omax.x and rect_max.x > omin.x and rect_min.y < omax.y and rect_max.y > omin.y:
				return true
	return false


## Live units whose center is within `radius` of (x, y), excluding `ignore`.
func enum_units_in_range(x: float, y: float, radius: float, ignore = null) -> Array:
	var out: Array = []
	var seen: Dictionary = {}
	var rmin := Vector2(x - radius, y - radius)
	var rmax := Vector2(x + radius, y + radius)
	var r2 := radius * radius
	for key in _keys_for_rect(rmin, rmax):
		for other in _cells.get(key, []):
			if other == ignore or other.is_dead() or seen.has(other.unit_id):
				continue
			seen[other.unit_id] = true
			if other.pos.distance_squared_to(Vector2(x, y)) <= r2:
				out.append(other)
	return out


func _bucket(key: Vector2i) -> Array:
	if not _cells.has(key):
		_cells[key] = []
	return _cells[key]


func _keys_for_rect(rect_min: Vector2, rect_max: Vector2) -> Array[Vector2i]:
	var keys: Array[Vector2i] = []
	var x0 := floori(rect_min.x / CELL)
	var x1 := floori(rect_max.x / CELL)
	var y0 := floori(rect_min.y / CELL)
	var y1 := floori(rect_max.y / CELL)
	for cy in range(y0, y1 + 1):
		for cx in range(x0, x1 + 1):
			keys.append(Vector2i(cx, cy))
	return keys
