extends RefCounted
## A* on W3E tiles with cliff/ramp step rules.


var sampler
func _init(p_sampler = null) -> void:
	sampler = p_sampler


func find_path(start: Vector2i, goal: Vector2i) -> Array[Vector2i]:
	var empty: Array[Vector2i] = []
	if sampler == null or sampler.map == null:
		return empty
	if not sampler.is_tile_walkable(start):
		return empty
	if not sampler.is_tile_walkable(goal):
		return empty
	if start == goal:
		return [start]

	var open: Array[Vector2i] = [start]
	var came: Dictionary = {}
	var gscore: Dictionary = {start: 0}
	var fscore: Dictionary = {start: _h(start, goal)}
	var closed: Dictionary = {}

	var dirs: Array[Vector2i] = [
		Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1),
		Vector2i(1, 1), Vector2i(1, -1), Vector2i(-1, 1), Vector2i(-1, -1),
	]

	while not open.is_empty():
		var current = _pop_lowest(open, fscore)
		if current == goal:
			return _reconstruct(came, current)
		closed[current] = true
		for d in dirs:
			var nxt: Vector2i = current + d
			if closed.has(nxt):
				continue
			if not sampler.can_step(current, nxt):
				continue
			# Diagonal: both orthogonals must be walkable.
			if d.x != 0 and d.y != 0:
				if not sampler.can_step(current, Vector2i(current.x + d.x, current.y)):
					continue
				if not sampler.can_step(current, Vector2i(current.x, current.y + d.y)):
					continue
			var tentative: int = int(gscore[current]) + (14 if d.x != 0 and d.y != 0 else 10)
			if not gscore.has(nxt) or tentative < int(gscore[nxt]):
				came[nxt] = current
				gscore[nxt] = tentative
				fscore[nxt] = tentative + _h(nxt, goal)
				if not open.has(nxt):
					open.append(nxt)
	return empty


func _h(a: Vector2i, b: Vector2i) -> int:
	var dx = absi(a.x - b.x)
	var dy = absi(a.y - b.y)
	# Octile distance for cardinal cost 10 and diagonal cost 14.
	return 10 * (dx + dy) - 6 * mini(dx, dy)


func _pop_lowest(open: Array[Vector2i], fscore: Dictionary) -> Vector2i:
	var best_i = 0
	var best_f: int = int(fscore.get(open[0], 1 << 30))
	for i in range(1, open.size()):
		var f: int = int(fscore.get(open[i], 1 << 30))
		if f < best_f:
			best_f = f
			best_i = i
	var v: Vector2i = open[best_i]
	open.remove_at(best_i)
	return v


func _reconstruct(came: Dictionary, current: Vector2i) -> Array[Vector2i]:
	var path: Array[Vector2i] = [current]
	while came.has(current):
		current = came[current]
		path.push_front(current)
	return path
