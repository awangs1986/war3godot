extends RefCounted
## One simulated ground unit: pure state + behavior, no scene nodes.
## Movement is the Warsmash CBehaviorMove port (turn rate, propulsion window,
## budgeted re-pathing); melee combat is a scoped-down CBehaviorAttack (chase,
## cooldown swings, WC3 dice damage). Rendering reads this state, never writes.

const STATE_IDLE := 0
const STATE_MOVE := 1
const STATE_ATTACK := 2
const STATE_DEAD := 3

const STEP := 1.0 / 20.0 # WarsmashConstants.SIMULATION_STEP_TIME
const ACQUIRE_SCAN_TICKS := 10 # 0.5s between idle target scans
const LEASH_RANGE := 1500.0
const CORPSE_TICKS := 8 * 20

signal damaged(unit, amount: float, attacker)
signal died(unit, killer)
signal attack_swing(unit, target)

var sim # W3Sim back-reference
var unit_id: int = -1
var rawcode: String = ""
var owner: int = 0
var auto_acquire: bool = false

# Stats (see W3ObjectCatalog.unit_combat_stats).
var max_hp: float = 100.0
var hp: float = 100.0
var speed: float = 270.0
var turn_rate: float = 0.6 # radians per simulation tick
var prop_window_deg: float = 60.0
var collision_size: float = 32.0
var acquire_range: float = 500.0
var attack_range: float = 90.0
var attack_cooldown: float = 1.5
var damage_dice: int = 1
var damage_sides: int = 4
var damage_plus: int = 1

# W3 ground-plane state. prev_* is the last tick's value for render interpolation.
var pos := Vector2.ZERO
var prev_pos := Vector2.ZERO
var facing: float = 270.0
var prev_facing: float = 270.0
var home := Vector2.ZERO

var state: int = STATE_IDLE
## True while the propulsion window allowed forward motion this tick (renderer
## maps it to the Walk sequence).
var is_moving: bool = false
## True while standing in attack range of a live target (renderer maps it to
## the Attack sequence; attack_swing re-syncs the loop to real swings).
var is_attacking: bool = false

# CBehaviorMove state.
var _move_goal := Vector2.ZERO
var _path = null # Array[Vector2] or null (null -> needs pathfind)
var _search_cycles := 0
var _first_pathfind_job := false
var _pathfinding_active := false
var _pathfinding_failed_give_up := false
var _give_up_until_tick := 0
var _returning_home := false

# Attack state.
var _attack_target = null # W3SimUnit or null
var _swing_ticks_left := 0
var _corpse_ticks_left := 0
var _acquire_scan_at := 0


func configure(stats: Dictionary) -> void:
	max_hp = maxf(1.0, float(stats.get("hp", 100.0)))
	hp = max_hp
	speed = maxf(1.0, float(stats.get("speed", 270.0)))
	turn_rate = clampf(float(stats.get("turn_rate", 0.6)), 0.01, TAU)
	prop_window_deg = clampf(float(stats.get("prop_window_deg", 60.0)), 1.0, 180.0)
	collision_size = maxf(0.0, float(stats.get("collision", 32.0)))
	acquire_range = maxf(0.0, float(stats.get("acquire_range", 500.0)))
	attack_range = maxf(8.0, float(stats.get("attack_range", 90.0)))
	attack_cooldown = maxf(0.1, float(stats.get("attack_cooldown", 1.5)))
	damage_dice = maxi(0, int(stats.get("damage_dice", 1)))
	damage_sides = maxi(1, int(stats.get("damage_sides", 4)))
	damage_plus = maxi(0, int(stats.get("damage_plus", 1)))


func is_dead() -> bool:
	return state == STATE_DEAD


func rect_min() -> Vector2:
	return Vector2(pos.x - collision_size, pos.y - collision_size)


func rect_max() -> Vector2:
	return Vector2(pos.x + collision_size, pos.y + collision_size)


## Distance between collision circles (>= 0); WC3 range checks are rect-based,
## the circle approximation is within one cell for our square footprints.
func range_to(other) -> float:
	return maxf(0.0, pos.distance_to(other.pos) - collision_size - other.collision_size)


# --- Orders -----------------------------------------------------------------


func order_move(point: Vector2) -> void:
	if is_dead():
		return
	_attack_target = null
	_returning_home = false
	state = STATE_MOVE
	_reset_move(point)


func order_attack(target) -> void:
	if is_dead() or target == null or target == self or target.is_dead():
		return
	_attack_target = target
	_returning_home = false
	state = STATE_ATTACK
	_reset_move(target.pos)


func stop() -> void:
	if is_dead():
		return
	_attack_target = null
	_returning_home = false
	state = STATE_IDLE
	_cancel_move()


## Debug/editor helper: instant reposition (also resets render interpolation).
func teleport(point: Vector2) -> void:
	if is_dead():
		return
	stop()
	var old_min := rect_min()
	var old_max := rect_max()
	pos = point
	prev_pos = point
	home = point
	sim.collision.translate_unit(self, old_min, old_max)


# --- Damage -----------------------------------------------------------------


func apply_damage(amount: float, attacker) -> void:
	if is_dead():
		return
	hp -= amount
	damaged.emit(self, amount, attacker)
	if hp <= 0.0:
		hp = 0.0
		_die(attacker)
		return
	# WC3-style retaliation for AI units standing around. Leash-returning units
	# ignore it, otherwise they could be kited at the leash edge forever.
	if auto_acquire and not _returning_home and _attack_target == null \
			and attacker != null and not attacker.is_dead():
		order_attack(attacker)


func _die(killer) -> void:
	state = STATE_DEAD
	is_moving = false
	_attack_target = null
	_cancel_move()
	_corpse_ticks_left = CORPSE_TICKS
	sim.collision.remove_unit(self)
	died.emit(self, killer)


# --- Tick -------------------------------------------------------------------


func tick() -> void:
	prev_pos = pos
	prev_facing = facing
	is_moving = false
	is_attacking = false
	match state:
		STATE_DEAD:
			_corpse_ticks_left -= 1
			if _corpse_ticks_left <= 0:
				sim.remove_unit(self)
		STATE_IDLE:
			_tick_idle()
		STATE_MOVE:
			if _tick_move(_move_goal):
				state = STATE_IDLE
				_returning_home = false
		STATE_ATTACK:
			_tick_attack()
	if _swing_ticks_left > 0:
		_swing_ticks_left -= 1


func _tick_idle() -> void:
	if not auto_acquire or sim.tick_count < _acquire_scan_at:
		return
	_acquire_scan_at = sim.tick_count + ACQUIRE_SCAN_TICKS
	var enemy = sim.find_nearest_enemy(self, acquire_range)
	if enemy != null:
		order_attack(enemy)


func _tick_attack() -> void:
	var target = _attack_target
	if target == null or target.is_dead():
		_attack_target = null
		if auto_acquire:
			_return_home()
		else:
			state = STATE_IDLE
		return
	if auto_acquire and pos.distance_to(home) > LEASH_RANGE:
		_return_home()
		return
	if range_to(target) <= attack_range:
		_cancel_move_keep_state()
		is_attacking = true
		var goal_angle := _angle_to(target.pos)
		var abs_delta := _turn_toward(goal_angle)
		if abs_delta < prop_window_deg and _swing_ticks_left <= 0:
			_swing_ticks_left = int(round(attack_cooldown / STEP))
			attack_swing.emit(self, target)
			target.apply_damage(sim.roll_damage(damage_dice, damage_sides, damage_plus), self)
		return
	# Chase: re-path when the target strays from the requested goal.
	if target.pos.distance_to(_move_goal) > 128.0:
		_reset_move(target.pos)
	_tick_move(target.pos)


func _return_home() -> void:
	_attack_target = null
	_returning_home = true
	state = STATE_MOVE
	hp = max_hp # classic leash reset keeps camps from being chipped down
	_reset_move(home)


# --- Movement (CBehaviorMove port) ------------------------------------------


func _reset_move(goal: Vector2) -> void:
	_move_goal = goal
	_path = null
	_search_cycles = 0
	_first_pathfind_job = false
	_pathfinding_failed_give_up = false
	_give_up_until_tick = 0
	_cancel_pathfind_request()


func _cancel_move() -> void:
	_path = null
	_cancel_pathfind_request()


func _cancel_move_keep_state() -> void:
	# Reached attack range: drop waypoints but keep the ATTACK state.
	_path = null
	_cancel_pathfind_request()


func _cancel_pathfind_request() -> void:
	if _pathfinding_active:
		sim.cancel_path_request(self)
		_pathfinding_active = false


## One movement tick toward `goal`. Returns true when the move finished.
func _tick_move(goal: Vector2) -> bool:
	if _pathfinding_failed_give_up:
		return true
	if _path == null and not _pathfinding_active:
		if sim.pathing_grid == null:
			_path = [] # no grid: walk straight at the goal
		else:
			sim.request_path(self, pos, goal, true)
			_pathfinding_active = true
			_first_pathfind_job = true
	# While waiting for the shared pathfinding queue we may still turn in place.
	var current_target := _current_waypoint(goal)
	var delta_vec := current_target - pos
	if delta_vec.is_zero_approx():
		if _path != null and not _pathfinding_active:
			var path_arr: Array = _path
			if path_arr.is_empty():
				return true
			path_arr.pop_front()
		return false
	var goal_angle := _angle_to(current_target)
	var abs_delta := _turn_toward(goal_angle)

	var blocked_by_give_up_delay: bool = sim.tick_count < _give_up_until_tick
	if blocked_by_give_up_delay or _path == null or _pathfinding_active or abs_delta >= prop_window_deg:
		return false

	var continue_distance := speed * STEP
	var current := pos
	while continue_distance > 0.0:
		var done: bool
		var next: Vector2
		var travel_distance := (current_target - current).length()
		if travel_distance <= continue_distance:
			next = current_target
			continue_distance -= travel_distance
			done = true
		else:
			var radian_facing := deg_to_rad(facing)
			next = current + Vector2(cos(radian_facing), sin(radian_facing)) * continue_distance
			continue_distance = 0.0
			done = false
		if _point_pathable(next):
			_set_point(next)
			is_moving = true
			current = next
			if done:
				if travel_distance > 0.0:
					_search_cycles = 0
				var path_arr: Array = _path
				if path_arr.is_empty():
					return true
				path_arr.pop_front()
				current_target = _current_waypoint(goal)
				var next_delta := current_target - current
				if next_delta.is_zero_approx() and (path_arr as Array).is_empty():
					return true
				goal_angle = _angle_to(current_target)
				abs_delta = _turn_toward(goal_angle)
				if abs_delta >= prop_window_deg:
					return false
		else:
			# Blocked ahead (wall or another unit): re-path.
			if not _pathfinding_active and sim.pathing_grid != null:
				sim.request_path(self, pos, goal, _search_cycles < 4)
				_pathfinding_active = true
				_search_cycles += 1
			return false
	return false


func _current_waypoint(goal: Vector2) -> Vector2:
	if _path == null:
		return goal
	var path_arr: Array = _path
	if path_arr.is_empty():
		return goal
	return path_arr[0]


## Static grid + dynamic unit collision, matching Warsmash's movement-step
## check: isPathable(...) && !intersectsAnythingOtherThan(tempRect, unit).
func _point_pathable(p: Vector2) -> bool:
	if sim.pathing_grid != null and not sim.pathing_grid.is_pathable_foot(p.x, p.y, collision_size):
		return false
	var rmin := Vector2(p.x - collision_size, p.y - collision_size)
	var rmax := Vector2(p.x + collision_size, p.y + collision_size)
	return not sim.collision.intersects_anything_other_than(rmin, rmax, self)


func _set_point(next: Vector2) -> void:
	var old_min := rect_min()
	var old_max := rect_max()
	pos = next
	sim.collision.translate_unit(self, old_min, old_max)


func _angle_to(point: Vector2) -> float:
	var d := point - pos
	var a := rad_to_deg(atan2(d.y, d.x))
	return a + 360.0 if a < 0.0 else a


## Turns at most turn_rate toward goal_angle; returns |delta| BEFORE the turn
## (Warsmash gates propulsion on the pre-turn delta).
func _turn_toward(goal_angle: float) -> float:
	var delta := _angle_delta(goal_angle, facing)
	var abs_delta: float = absf(delta)
	if abs_delta <= 1.0 and abs_delta != 0.0:
		facing = goal_angle
	elif abs_delta > 0.0:
		var angle_to_add := signf(delta) * rad_to_deg(turn_rate)
		if abs_delta < absf(angle_to_add):
			angle_to_add = delta
		facing = wrapf(facing + angle_to_add, 0.0, 360.0)
	return abs_delta


static func _angle_delta(goal_angle: float, from: float) -> float:
	var delta := goal_angle - from
	if delta < -180.0:
		delta = 360.0 + delta
	if delta > 180.0:
		delta = -360.0 + delta
	return delta


## CBehaviorMove.pathFound.
func on_path_found(waypoints: PackedVector2Array) -> void:
	_pathfinding_active = false
	var path_arr: Array = []
	for p in waypoints:
		path_arr.append(p)
	_path = path_arr
	if _first_pathfind_job:
		_first_pathfind_job = false
		_smooth_initial_path(path_arr)
	elif path_arr.is_empty() or _search_cycles > 6:
		if _search_cycles > 9:
			_pathfinding_failed_give_up = true
		else:
			_give_up_until_tick = sim.tick_count + int(5.0 / STEP)


## pathFound() first-job smoothing: collapse waypoint runs whose total length
## stays within 1.15x of the straight line, when the midpoint is pathable.
func _smooth_initial_path(path_arr: Array) -> void:
	if path_arr.is_empty() or sim.pathing_grid == null:
		return
	var last := pos
	var smoothing_group_start := pos
	var first: Vector2 = path_arr[0]
	var total_path_distance := first.distance_to(last)
	last = first
	var smoothing_start_index := -1
	var i := 0
	while i < path_arr.size() - 1:
		var next_possible: Vector2 = path_arr[i + 1]
		total_path_distance += next_possible.distance_to(last)
		var mid := (smoothing_group_start + next_possible) * 0.5
		if (
			total_path_distance < 1.15 * next_possible.distance_to(smoothing_group_start)
			and sim.pathing_grid.is_pathable_foot(mid.x, mid.y, 0.0)
		):
			if smoothing_start_index == -1:
				smoothing_start_index = i
		else:
			if smoothing_start_index != -1:
				for j in range(i - 1, smoothing_start_index - 1, -1):
					path_arr.remove_at(j)
				i = smoothing_start_index
			smoothing_start_index = -1
			var smooth_group_next: Vector2 = path_arr[i]
			smoothing_group_start = smooth_group_next
			total_path_distance = next_possible.distance_to(smooth_group_next)
		last = next_possible
		i += 1
	if smoothing_start_index != -1:
		while smoothing_start_index < path_arr.size() - 1:
			path_arr.remove_at(smoothing_start_index)
