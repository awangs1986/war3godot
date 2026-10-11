extends RefCounted
## Warsmash CPathfindingProcessor port (simulation/pathing/CPathfindingProcessor.java).
## Single-job variant of the budgeted queue: start_job() then call update() once
## per simulation tick; each update runs at most WORK_BUDGET A* iterations, so a
## worst-case search is spread over frames exactly like Warsmash's move queue.
## Faithfully kept: cells-vs-corners graph choice, multi-goal set, start
## multi-seeding, 8-direction expansion with 0.25 turn cost, the inadmissible
## max-distance heuristic ("always overestimate"), wall weights for unreachable
## goals, 20000 iteration cap, backtrace waypoint reduction and smoothing.

const W3PathingGrid = preload("res://addons/w3kit/w3_pathing_grid.gd")

const MAX_ITERATIONS := 20000
const WORK_BUDGET := 1500 # CPathfindingProcessor.update() workIterations cap

## Direction table (Direction enum): NW N NE E SE S SW W.
const DIR_X: Array[int] = [-1, 0, 1, 1, 1, 0, -1, -1]
const DIR_Y: Array[int] = [1, 1, 1, 0, -1, -1, -1, 0]
var _dir_len: PackedFloat64Array = PackedFloat64Array()

var grid # W3PathingGrid

# Per-job node state, flat-indexed; touch() semantics via _job_id stamps.
var _g: PackedFloat64Array = PackedFloat64Array()
var _came_from: PackedInt32Array = PackedInt32Array()
var _came_dir: PackedInt32Array = PackedInt32Array()
var _stamp: PackedInt32Array = PackedInt32Array()
var _job_id: int = 0
var _use_corners: bool = false
var _graph_w: int = 0
var _graph_h: int = 0

# Active job.
var _active: bool = false
var _start := Vector2.ZERO
var _goal := Vector2.ZERO
var _collision_size := 0.0
var _allow_smoothing := true
var _open: _Heap
var _iterations := 0
var _goal_indices: PackedInt32Array = PackedInt32Array()
var _goal_points: PackedVector2Array = PackedVector2Array()
var _weight_for_hitting_walls: float = 1e9


func _init(p_grid = null) -> void:
	grid = p_grid
	_dir_len.resize(8)
	for d in 8:
		_dir_len[d] = sqrt(float(DIR_X[d] * DIR_X[d] + DIR_Y[d] * DIR_Y[d]))


## CPathfindingProcessor.isCollisionSizeBetterSuitedForCorners
static func is_collision_size_better_suited_for_corners(collision_size: float) -> bool:
	return ((2 * int(collision_size)) / 32) % 2 == 1


func is_active() -> bool:
	return _active


func cancel() -> void:
	_active = false
	_open = null


## findNaiveSlowPath job setup. Overwrites any previous job.
func start_job(start: Vector2, goal: Vector2, collision_size: float, allow_smoothing: bool) -> void:
	_active = true
	_start = start
	_goal = goal
	_collision_size = collision_size
	_allow_smoothing = allow_smoothing
	_iterations = 0
	_open = _Heap.new()
	if grid == null or start.is_equal_approx(goal):
		# update() will immediately deliver the empty result.
		_open = null
		return

	_job_id += 1
	_use_corners = is_collision_size_better_suited_for_corners(collision_size)
	_graph_w = grid.width + (1 if _use_corners else 0)
	_graph_h = grid.height + (1 if _use_corners else 0)
	var node_count := _graph_w * _graph_h
	if _g.size() != node_count:
		_g.resize(node_count)
		_came_from.resize(node_count)
		_came_dir.resize(node_count)
		_stamp.resize(node_count)
		_stamp.fill(0)

	_weight_for_hitting_walls = 1e9
	if not grid.is_pathable_foot(goal.x, goal.y, collision_size):
		_weight_for_hitting_walls = 5e2

	# Goal set: the mapped goal cell plus 3x3 neighbors that are at least as
	# close to the exact goal point.
	var goal_cx := _map_x(goal.x)
	var goal_cy := _map_y(goal.y)
	_goal_indices.clear()
	_goal_points.clear()
	var best_goal_distance := _node_point(goal_cx, goal_cy).distance_to(goal)
	for i in range(goal_cx - 1, goal_cx + 2):
		for j in range(goal_cy - 1, goal_cy + 2):
			if j < 0 or j >= _graph_h or i < 0 or i >= _graph_w:
				continue
			var p := _node_point(i, j)
			if p.distance_to(goal) <= best_goal_distance:
				_goal_indices.append(j * _graph_w + i)
				_goal_points.append(p)

	# Start seeding: the up-to-4 grid nodes straddling the floating start point.
	var start_cx := _map_x(start.x)
	var start_cy := _map_y(start.y)
	var start_node_point := _node_point(start_cx, start_cy)
	var min_x := start_cx
	var max_x := start_cx
	if start.x > start_node_point.x:
		max_x = start_cx + 1
	elif start.x < start_node_point.x:
		min_x = start_cx - 1
	var min_y := start_cy
	var max_y := start_cy
	if start.y > start_node_point.y:
		max_y = start_cy + 1
	elif start.y < start_node_point.y:
		min_y = start_cy - 1
	for cell_x in range(min_x, max_x + 1):
		for cell_y in range(min_y, max_y + 1):
			if cell_x < 0 or cell_x >= grid.width or cell_y < 0 or cell_y >= grid.height:
				continue
			var index := cell_y * _graph_w + cell_x
			_touch(index)
			var p := _node_point(cell_x, cell_y)
			var tentative: float
			if _pathable_between(start.x, start.y, p.x, p.y, collision_size):
				tentative = p.distance_to(start)
			else:
				tentative = _weight_for_hitting_walls
			_g[index] = tentative
			_open.push(index, tentative + _h(p))


## Runs up to WORK_BUDGET A* iterations. Returns null while the job is still in
## progress, otherwise the finished PackedVector2Array (empty = no path).
func update() -> Variant:
	if not _active:
		return null
	if _open == null:
		return _finish(PackedVector2Array())
	var work := 0
	while not _open.is_empty():
		var current := _open.pop()
		_touch(current)
		if _is_goal(current):
			return _finish(_backtrace(current))
		var current_point := _node_point(current % _graph_w, current / _graph_w)
		for d in 8:
			var x := current_point.x + DIR_X[d] * 32.0
			var y := current_point.y + DIR_Y[d] * 32.0
			if not grid.contains(x, y):
				continue
			var turn_cost := 0.0
			if _came_dir[current] >= 0 and d != _came_dir[current]:
				turn_cost = 0.25
			var tentative := _g[current] + (_dir_len[d] + turn_cost) * 32.0
			if not _pathable_between(current_point.x, current_point.y, x, y, _collision_size):
				tentative += _dir_len[d] * _weight_for_hitting_walls
			var neighbor := _map_y(y) * _graph_w + _map_x(x)
			_touch(neighbor)
			if tentative < _g[neighbor]:
				_came_from[neighbor] = current
				_came_dir[neighbor] = d
				_g[neighbor] = tentative
				_open.push(neighbor, tentative + _h(_node_point(neighbor % _graph_w, neighbor / _graph_w)))
		work += 1
		_iterations += 1
		if _iterations > MAX_ITERATIONS:
			return _finish(PackedVector2Array())
		if work >= WORK_BUDGET:
			return null
	return _finish(PackedVector2Array())


## Convenience blocking search (regression tools). Gameplay should prefer
## start_job()/update() to keep worst-case searches off a single frame.
func find_naive_slow_path(start: Vector2, goal: Vector2, collision_size: float, allow_smoothing: bool) -> PackedVector2Array:
	start_job(start, goal, collision_size, allow_smoothing)
	while true:
		var result = update()
		if result != null:
			return result
	return PackedVector2Array()


func _finish(result: PackedVector2Array) -> PackedVector2Array:
	_active = false
	_open = null
	return result


func _map_x(x: float) -> int:
	return grid.get_corner_x(x) if _use_corners else grid.get_cell_x(x)


func _map_y(y: float) -> int:
	return grid.get_corner_y(y) if _use_corners else grid.get_cell_y(y)


func _node_point(cell_x: int, cell_y: int) -> Vector2:
	if _use_corners:
		return Vector2(grid.get_world_x_from_corner(cell_x), grid.get_world_y_from_corner(cell_y))
	return Vector2(grid.get_world_x(cell_x), grid.get_world_y(cell_y))


func _touch(index: int) -> void:
	if _stamp[index] != _job_id:
		_g[index] = INF
		_came_from[index] = -1
		_came_dir[index] = -1
		_stamp[index] = _job_id


func _is_goal(index: int) -> bool:
	return index in _goal_indices


## "always overestimate": max distance over the goal set.
func _h(p: Vector2) -> float:
	var best := 0.0
	for gp in _goal_points:
		var d := p.distance_to(gp)
		if d > best:
			best = d
	return best


## pathableBetween: destination plus the two axis-aligned staircase corners.
func _pathable_between(start_x: float, start_y: float, x: float, y: float, collision_size: float) -> bool:
	return (
		grid.is_pathable_foot(x, y, collision_size)
		and grid.is_pathable_foot(start_x, y, collision_size)
		and grid.is_pathable_foot(x, start_y, collision_size)
	)


## Goal-reached backtrace with waypoint reduction (direction changes only) and
## optional line-of-sight smoothing against the floating start/goal points.
func _backtrace(goal_index: int) -> PackedVector2Array:
	var total_path := PackedVector2Array()
	var current := goal_index
	var current_point := _node_point(current % _graph_w, current / _graph_w)
	var came := _came_from[current]
	if (
		came >= 0
		and _allow_smoothing
		and _pathable_between(current_point.x, current_point.y, _goal.x, _goal.y, _collision_size)
		and _pathable_between(_node_x(came), _node_y(came), current_point.x, current_point.y, _collision_size)
		and _pathable_between(_node_x(came), _node_y(came), _goal.x, _goal.y, _collision_size)
	):
		# Walk straight to the goal, skipping the last grid location.
		total_path.append(_goal)
		current = came
	else:
		total_path.append(_goal)
		total_path.insert(0, current_point)
	var last_came_from_direction := _came_dir[current]
	var last_node := -1
	var steps_backward := 0
	while _came_from[current] >= 0:
		last_node = current
		current = _came_from[current]
		var current_dir := _came_dir[current]
		if last_came_from_direction < 0 or current_dir != last_came_from_direction or current_dir < 0:
			if (
				current_dir >= 0
				or last_node < 0
				or not _pathable_between(_start.x, _start.y, _node_x(current), _node_y(current), _collision_size)
				or not _pathable_between(_node_x(current), _node_y(current), _node_x(last_node), _node_y(last_node), _collision_size)
				or not _pathable_between(_start.x, _start.y, _node_x(last_node), _node_y(last_node), _collision_size)
				or not _allow_smoothing
			):
				total_path.insert(0, Vector2(_node_x(current), _node_y(current)))
				last_came_from_direction = current_dir
		steps_backward += 1
		if steps_backward > grid.width * grid.height:
			push_error("W3Pathfinding: infinite backtrace cycle near %s" % str(current_point))
			total_path.clear()
			break
	return total_path


func _node_x(index: int) -> float:
	return _node_point(index % _graph_w, index / _graph_w).x


func _node_y(index: int) -> float:
	return _node_point(index % _graph_w, index / _graph_w).y


## Binary min-heap with lazy duplicates (Warsmash uses PriorityQueue plus a
## linear contains(); duplicates with stale keys are skipped on pop).
class _Heap:
	var _indices: PackedInt32Array = PackedInt32Array()
	var _keys: PackedFloat64Array = PackedFloat64Array()
	var _size: int = 0

	func is_empty() -> bool:
		return _size == 0

	func push(index: int, key: float) -> void:
		if _size == _indices.size():
			_indices.resize(maxi(16, _size * 2))
			_keys.resize(maxi(16, _size * 2))
		var i := _size
		_size += 1
		_indices[i] = index
		_keys[i] = key
		while i > 0:
			var parent := (i - 1) / 2
			if _keys[parent] <= _keys[i]:
				break
			_swap(i, parent)
			i = parent

	func pop() -> int:
		var result := _indices[0]
		_size -= 1
		if _size > 0:
			_indices[0] = _indices[_size]
			_keys[0] = _keys[_size]
			var i := 0
			while true:
				var left := i * 2 + 1
				var right := left + 1
				var smallest := i
				if left < _size and _keys[left] < _keys[smallest]:
					smallest = left
				if right < _size and _keys[right] < _keys[smallest]:
					smallest = right
				if smallest == i:
					break
				_swap(i, smallest)
				i = smallest
		return result

	func _swap(a: int, b: int) -> void:
		var ti := _indices[a]
		_indices[a] = _indices[b]
		_indices[b] = ti
		var tk := _keys[a]
		_keys[a] = _keys[b]
		_keys[b] = tk
