extends Node
## The simulation layer: owns all W3SimUnits, the dynamic collision index and
## one shared budgeted pathfinding queue (Warsmash CSimulation, scoped down).
## Runs at a fixed 20 ticks per second inside _physics_process; rendering
## interpolates between prev/current tick state via interpolation_alpha().

const W3SimUnit = preload("res://addons/w3kit/sim/w3_sim_unit.gd")
const W3WorldCollision = preload("res://addons/w3kit/sim/w3_world_collision.gd")
const W3Pathfinding = preload("res://addons/w3kit/w3_pathfinding.gd")

const STEP := 1.0 / 20.0

signal unit_spawned(unit)
signal unit_died(unit, killer)
signal unit_removed(unit)

var pathing_grid # W3PathingGrid or null (null -> straight-line movement)
var collision := W3WorldCollision.new()
var tick_count: int = 0

var _units: Array = []
var _units_by_id: Dictionary = {}
var _next_unit_id: int = 1
var _tick_accum: float = 0.0

var _pathfinder # W3Pathfinding or null
var _path_queue: Array = [] # of {unit, start, goal, smoothing}
var _active_path_unit = null

## Deterministic combat rolls (fixed seed; reseed per save/load later).
var _rng := RandomNumberGenerator.new()


func _init() -> void:
	_rng.seed = 0x1337


func setup(p_pathing_grid) -> void:
	pathing_grid = p_pathing_grid
	if pathing_grid != null:
		_pathfinder = W3Pathfinding.new(pathing_grid)
	else:
		push_warning("W3Sim: no pathing grid — units move in straight lines only")


# --- Units ------------------------------------------------------------------


func create_unit(rawcode: String, stats: Dictionary, w3_pos: Vector2, facing_deg: float, owner_id: int) -> W3SimUnit:
	var unit := W3SimUnit.new()
	unit.sim = self
	unit.unit_id = _next_unit_id
	_next_unit_id += 1
	unit.rawcode = rawcode
	unit.owner = owner_id
	unit.configure(stats)
	unit.pos = w3_pos
	unit.prev_pos = w3_pos
	unit.home = w3_pos
	unit.facing = wrapf(facing_deg, 0.0, 360.0)
	unit.prev_facing = unit.facing
	_units.append(unit)
	_units_by_id[unit.unit_id] = unit
	collision.add_unit(unit)
	unit.died.connect(_on_unit_died)
	unit_spawned.emit(unit)
	return unit


func remove_unit(unit) -> void:
	if unit == null or not _units_by_id.has(unit.unit_id):
		return
	if not unit.is_dead():
		collision.remove_unit(unit)
	cancel_path_request(unit)
	_units.erase(unit)
	_units_by_id.erase(unit.unit_id)
	unit_removed.emit(unit)


func get_units() -> Array:
	return _units


func get_unit(unit_id: int) -> W3SimUnit:
	return _units_by_id.get(unit_id)


func find_nearest_enemy(unit, radius: float) -> W3SimUnit:
	var best = null
	var best_d2 := INF
	for other in collision.enum_units_in_range(unit.pos.x, unit.pos.y, radius, unit):
		if other.owner == unit.owner:
			continue
		var d2: float = unit.pos.distance_squared_to(other.pos)
		if d2 < best_d2:
			best_d2 = d2
			best = other
	return best


func roll_damage(dice: int, sides: int, plus: int) -> float:
	var total := plus
	for _i in dice:
		total += _rng.randi_range(1, maxi(1, sides))
	return float(total)


## Nudges a spawn point onto pathable ground (spiral search over cells).
func find_pathable_spawn(w3_pos: Vector2, collision_size: float, max_cells: int = 20) -> Vector2:
	if pathing_grid == null:
		return w3_pos
	if _spawn_ok(w3_pos, collision_size):
		return w3_pos
	for radius in range(1, max_cells):
		for dy in range(-radius, radius + 1):
			for dx in range(-radius, radius + 1):
				if maxi(absi(dx), absi(dy)) != radius:
					continue
				var candidate := w3_pos + Vector2(dx * 32.0, dy * 32.0)
				if _spawn_ok(candidate, collision_size):
					return candidate
	return w3_pos


func _spawn_ok(p: Vector2, collision_size: float) -> bool:
	if not pathing_grid.is_pathable_foot(p.x, p.y, collision_size):
		return false
	var rmin := Vector2(p.x - collision_size, p.y - collision_size)
	var rmax := Vector2(p.x + collision_size, p.y + collision_size)
	return not collision.intersects_anything_other_than(rmin, rmax, null)


# --- Pathfinding queue (CPathfindingProcessor.update) ------------------------


func request_path(unit, start: Vector2, goal: Vector2, allow_smoothing: bool) -> void:
	cancel_path_request(unit)
	_path_queue.append({"unit": unit, "start": start, "goal": goal, "smoothing": allow_smoothing})


func cancel_path_request(unit) -> void:
	if _active_path_unit == unit:
		_active_path_unit = null
		if _pathfinder != null:
			_pathfinder.cancel()
	for i in range(_path_queue.size() - 1, -1, -1):
		if _path_queue[i]["unit"] == unit:
			_path_queue.remove_at(i)


func _tick_pathfinding() -> void:
	if _pathfinder == null:
		return
	if _active_path_unit == null and not _path_queue.is_empty():
		var job: Dictionary = _path_queue.pop_front()
		_active_path_unit = job["unit"]
		_pathfinder.start_job(job["start"], job["goal"], _active_path_unit.collision_size, job["smoothing"])
	if _active_path_unit == null:
		return
	var result = _pathfinder.update() # budgeted; null while still searching
	if result != null:
		var unit = _active_path_unit
		_active_path_unit = null
		if _units_by_id.has(unit.unit_id) and not unit.is_dead():
			unit.on_path_found(result)


# --- Tick loop ---------------------------------------------------------------


func _physics_process(delta: float) -> void:
	_tick_accum += delta
	# Cap catch-up work so a long stall cannot spiral the frame time.
	var steps := 0
	while _tick_accum >= STEP and steps < 8:
		_tick_accum -= STEP
		steps += 1
		step_once()
	if steps == 8:
		_tick_accum = 0.0


## One 50ms simulation tick. Public so headless tests can drive the sim
## deterministically without a running physics loop.
func step_once() -> void:
	tick_count += 1
	_tick_pathfinding()
	# Iterate over a copy: corpse decay removes units mid-tick.
	for unit in _units.duplicate():
		unit.tick()


## 0..1 progress between the last and the current simulation tick.
func interpolation_alpha() -> float:
	return clampf(_tick_accum / STEP, 0.0, 1.0)


func _on_unit_died(unit, killer) -> void:
	unit_died.emit(unit, killer)
