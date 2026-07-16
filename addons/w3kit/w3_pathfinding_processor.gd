extends RefCounted
## Warsmash CPathfindingProcessor A* (sync port for Diablo click-move).
## No unit-unit dynamic collision yet (isPathableDynamically ≡ true).
## Returns world XZ waypoints (Godot X/Z); caller sticks Y via HeightSampler.


const W3PathingGrid = preload("res://addons/w3kit/w3_pathing_grid.gd")

const _DIRS: Array[Vector2i] = [
	Vector2i(-1, 1), Vector2i(0, 1), Vector2i(1, 1), Vector2i(1, 0),
	Vector2i(1, -1), Vector2i(0, -1), Vector2i(-1, -1), Vector2i(-1, 0),
]

var pathing_grid
var _job_id: int = 0


func _init(grid = null) -> void:
	pathing_grid = grid


static func is_collision_size_better_suited_for_corners(collision_size: float) -> bool:
	return (((2 * int(collision_size)) / 32) % 2) == 1


func pathable_between(start_x: float, start_z: float, end_x: float, end_z: float, movement: int, collision_size: float) -> bool:
	if pathing_grid == null:
		return false
	return (
		pathing_grid.is_pathable(end_x, end_z, movement, collision_size)
		and pathing_grid.is_pathable(start_x, end_z, movement, collision_size)
		and pathing_grid.is_pathable(end_x, start_z, movement, collision_size)
	)


## Sync pathfind. Points are Vector2(world_x, world_z). Empty if failed.
func find_path(start_x: float, start_z: float, goal_x: float, goal_z: float, movement: int = W3PathingGrid.MovementType.FOOT, collision_size: float = 16.0, allow_smoothing: bool = true) -> Array[Vector2]:
	var empty: Array[Vector2] = []
	if pathing_grid == null:
		return empty
	if is_equal_approx(start_x, goal_x) and is_equal_approx(start_z, goal_z):
		return empty

	_job_id += 1
	var job_id := _job_id
	var weight_walls: float = 1e9
	if not pathing_grid.is_pathable(goal_x, goal_z, movement, collision_size):
		weight_walls = 5e2

	var use_corners := is_collision_size_better_suited_for_corners(collision_size)
	var grid_w: int = pathing_grid.width + (1 if use_corners else 0)
	var grid_h: int = pathing_grid.height + (1 if use_corners else 0)

	# Node state keyed by "x,y"
	var g_score: Dictionary = {}
	var f_score: Dictionary = {}
	var came_from: Dictionary = {}
	var came_dir: Dictionary = {}
	var touched: Dictionary = {}

	var start_gx := _map_x(start_x, use_corners)
	var start_gz := _map_z(start_z, use_corners)
	var goal_gx := _map_x(goal_x, use_corners)
	var goal_gz := _map_z(goal_z, use_corners)

	var goals: Array[Vector2i] = []
	var most := Vector2i(goal_gx, goal_gz)
	var best_goal_d: float = _node_pos(most, use_corners).distance_to(Vector2(goal_x, goal_z))
	for j in range(goal_gz - 1, goal_gz + 2):
		for i in range(goal_gx - 1, goal_gx + 2):
			if not _in_graph(i, j, use_corners):
				continue
			var p: Vector2 = _node_pos(Vector2i(i, j), use_corners)
			if p.distance_to(Vector2(goal_x, goal_z)) <= best_goal_d + 0.001:
				goals.append(Vector2i(i, j))
	if goals.is_empty():
		goals.append(most)

	var open: Array[Vector2i] = []
	var open_set: Dictionary = {}

	var start_min_x := start_gx
	var start_max_x := start_gx
	var start_min_z := start_gz
	var start_max_z := start_gz
	var start_pt := _node_pos(Vector2i(start_gx, start_gz), use_corners)
	if start_x > start_pt.x:
		start_max_x = start_gx + 1
	elif start_x < start_pt.x:
		start_min_x = start_gx - 1
	if start_z > start_pt.y:
		start_max_z = start_gz + 1
	elif start_z < start_pt.y:
		start_min_z = start_gz - 1

	for cell_x in range(start_min_x, start_max_x + 1):
		for cell_z in range(start_min_z, start_max_z + 1):
			if not _in_graph(cell_x, cell_z, use_corners):
				continue
			var key := Vector2i(cell_x, cell_z)
			_touch(key, job_id, touched, g_score, f_score, came_from, came_dir)
			var np: Vector2 = _node_pos(key, use_corners)
			var tent: float
			if pathable_between(start_x, start_z, np.x, np.y, movement, collision_size):
				tent = np.distance_to(Vector2(start_x, start_z))
			else:
				tent = weight_walls
			g_score[key] = tent
			f_score[key] = tent + _h(np, goals, use_corners)
			open.append(key)
			open_set[key] = true

	var iterations := 0
	var cell_count: int = pathing_grid.width * pathing_grid.height
	while not open.is_empty():
		iterations += 1
		if iterations > 20000:
			break
		var current := _pop_lowest(open, f_score)
		open_set.erase(current)
		_touch(current, job_id, touched, g_score, f_score, came_from, came_dir)

		if _is_goal(current, goals):
			return _reconstruct(
				current, came_from, came_dir, start_x, start_z, goal_x, goal_z,
				movement, collision_size, allow_smoothing, use_corners, weight_walls
			)

		var cur_pos: Vector2 = _node_pos(current, use_corners)
		var cur_g: float = float(g_score.get(current, INF))
		var cur_dir = came_dir.get(current, null)

		for di in _DIRS.size():
			var d: Vector2i = _DIRS[di]
			var nx: float = cur_pos.x + float(d.x) * W3PathingGrid.CELL
			var nz: float = cur_pos.y + float(d.y) * W3PathingGrid.CELL
			if not pathing_grid.contains(nx, nz) and not use_corners:
				continue
			# Corner graph may sit on edges; still require mapped indices in range.
			var nkey := Vector2i(_map_x(nx, use_corners), _map_z(nz, use_corners))
			if not _in_graph(nkey.x, nkey.y, use_corners):
				continue
			_touch(nkey, job_id, touched, g_score, f_score, came_from, came_dir)
			var turn_cost: float = 0.25 if cur_dir != null and int(cur_dir) != di else 0.0
			var dir_len: float = sqrt(float(d.x * d.x + d.y * d.y))
			var tentative: float = cur_g + (dir_len + turn_cost) * W3PathingGrid.CELL
			if not pathable_between(cur_pos.x, cur_pos.y, nx, nz, movement, collision_size):
				tentative += dir_len * weight_walls
			var old_g: float = float(g_score.get(nkey, INF))
			if tentative < old_g:
				came_from[nkey] = current
				came_dir[nkey] = di
				g_score[nkey] = tentative
				var npos: Vector2 = _node_pos(nkey, use_corners)
				f_score[nkey] = tentative + _h(npos, goals, use_corners)
				if not open_set.has(nkey):
					open.append(nkey)
					open_set[nkey] = true

	return empty


func _map_x(world_x: float, use_corners: bool) -> int:
	return pathing_grid.get_corner_x(world_x) if use_corners else pathing_grid.get_cell_x(world_x)


func _map_z(world_z: float, use_corners: bool) -> int:
	return pathing_grid.get_corner_y(world_z) if use_corners else pathing_grid.get_cell_y(world_z)


func _in_graph(x: int, z: int, use_corners: bool) -> bool:
	if use_corners:
		return x >= 0 and z >= 0 and x <= pathing_grid.width and z <= pathing_grid.height
	return x >= 0 and z >= 0 and x < pathing_grid.width and z < pathing_grid.height


func _node_pos(key: Vector2i, use_corners: bool) -> Vector2:
	if use_corners:
		return Vector2(pathing_grid.get_world_x_from_corner(key.x), pathing_grid.get_world_z_from_corner(key.y))
	return Vector2(pathing_grid.get_world_x(key.x), pathing_grid.get_world_z(key.y))


func _touch(key: Vector2i, job_id: int, touched: Dictionary, g_score: Dictionary, f_score: Dictionary, came_from: Dictionary, came_dir: Dictionary) -> void:
	if int(touched.get(key, -1)) == job_id:
		return
	touched[key] = job_id
	g_score[key] = INF
	f_score[key] = INF
	came_from.erase(key)
	came_dir.erase(key)


func _h(pos: Vector2, goals: Array[Vector2i], use_corners: bool) -> float:
	# Warsmash: always overestimate — max distance to any goal node.
	var best := 0.0
	for g in goals:
		best = maxf(best, pos.distance_to(_node_pos(g, use_corners)))
	return best


func _is_goal(key: Vector2i, goals: Array[Vector2i]) -> bool:
	for g in goals:
		if g == key:
			return true
	return false


func _pop_lowest(open: Array[Vector2i], f_score: Dictionary) -> Vector2i:
	var bi := 0
	var best: float = float(f_score.get(open[0], INF))
	for i in range(1, open.size()):
		var f: float = float(f_score.get(open[i], INF))
		if f < best:
			best = f
			bi = i
	var node: Vector2i = open[bi]
	open.remove_at(bi)
	return node


func _reconstruct(
	current: Vector2i,
	came_from: Dictionary,
	came_dir: Dictionary,
	start_x: float,
	start_z: float,
	goal_x: float,
	goal_z: float,
	movement: int,
	collision_size: float,
	allow_smoothing: bool,
	use_corners: bool,
	_weight_walls: float
) -> Array[Vector2]:
	var total: Array[Vector2] = []
	var cur := current
	var last_dir = null

	if (
		came_from.has(cur)
		and pathable_between(_node_pos(cur, use_corners).x, _node_pos(cur, use_corners).y, goal_x, goal_z, movement, collision_size)
		and pathable_between(
			_node_pos(came_from[cur], use_corners).x, _node_pos(came_from[cur], use_corners).y,
			_node_pos(cur, use_corners).x, _node_pos(cur, use_corners).y, movement, collision_size
		)
		and pathable_between(
			_node_pos(came_from[cur], use_corners).x, _node_pos(came_from[cur], use_corners).y,
			goal_x, goal_z, movement, collision_size
		)
		and allow_smoothing
	):
		total.insert(0, Vector2(goal_x, goal_z))
		cur = came_from[cur]
	else:
		total.insert(0, Vector2(goal_x, goal_z))
		total.insert(0, _node_pos(cur, use_corners))

	last_dir = came_dir.get(cur, null)
	var last_node = null
	var steps := 0
	var limit: int = pathing_grid.width * pathing_grid.height + 8
	while came_from.has(cur):
		last_node = cur
		cur = came_from[cur]
		var cur_dir = came_dir.get(cur, null)
		if last_dir == null or cur_dir != last_dir or cur_dir == null:
			var cur_p: Vector2 = _node_pos(cur, use_corners)
			var last_p: Vector2 = _node_pos(last_node, use_corners) if last_node != null else cur_p
			if (
				cur_dir != null
				or last_node == null
				or not pathable_between(start_x, start_z, cur_p.x, cur_p.y, movement, collision_size)
				or not pathable_between(cur_p.x, cur_p.y, last_p.x, last_p.y, movement, collision_size)
				or not pathable_between(start_x, start_z, last_p.x, last_p.y, movement, collision_size)
				or not allow_smoothing
			):
				total.insert(0, cur_p)
				last_dir = cur_dir
		steps += 1
		if steps > limit:
			push_warning("W3Pathfinding: cycle guard tripped")
			return []
	return total
