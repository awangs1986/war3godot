extends CharacterBody3D
## Click-to-move (nav + direct fallback) and camera-relative WASD backup.

signal died

const SPEED := 7.5
const ATTACK_RANGE := 2.2
const ATTACK_COOLDOWN := 0.45
const ARRIVE_DIST := 0.5

var attack_target: Node3D = null
var _attack_cd: float = 0.0
var _alive: bool = true
var _nav_ready: bool = false
var _move_goal: Vector3 = Vector3.ZERO
var _has_move_goal: bool = false

@onready var _mesh: MeshInstance3D = $Mesh
@onready var _agent: NavigationAgent3D = $NavigationAgent3D


func _ready() -> void:
	add_to_group("player")
	collision_layer = 2
	collision_mask = 1
	floor_snap_length = 0.3
	_agent.path_desired_distance = 0.5
	_agent.target_desired_distance = 0.6
	_agent.avoidance_enabled = false
	_agent.radius = 0.45
	_agent.height = 1.6
	GameState.heal_full()
	GameState.player_stats_changed.connect(_on_stats)
	_on_stats()
	call_deferred("_wait_for_nav")


func _wait_for_nav() -> void:
	var nav := get_tree().get_first_node_in_group("nav_setup")
	if nav != null:
		if nav.get("nav_bake_done") != true and nav.has_signal("navigation_ready"):
			await nav.navigation_ready
		_agent.set_navigation_map(nav.get_navigation_map())
	await get_tree().physics_frame
	_nav_ready = true
	if _has_move_goal:
		_agent.target_position = _move_goal


func _on_stats() -> void:
	if _mesh.material_override == null:
		_mesh.material_override = StandardMaterial3D.new()
	var mat := _mesh.material_override as StandardMaterial3D
	var t := 1.0 if GameState.max_hp <= 0 else float(GameState.hp) / float(GameState.max_hp)
	mat.albedo_color = Color(0.25 + 0.55 * t, 0.35 * t, 0.85)


func _face_dir(dir: Vector3) -> void:
	var flat := Vector3(dir.x, 0.0, dir.z)
	if flat.length() < 0.001:
		return
	flat = flat.normalized()
	# Yaw only — full look_at can tilt the capsule and soft-lock movement.
	rotation.y = atan2(flat.x, flat.z)


func _physics_process(delta: float) -> void:
	if not _alive:
		return
	_attack_cd = maxf(0.0, _attack_cd - delta)

	if not is_on_floor():
		velocity.y -= 20.0 * delta
	else:
		velocity.y = 0.0

	# WASD camera-relative (always works — backup if click path fails).
	var wasd := _wasd_dir()
	if wasd.length() > 0.01:
		_clear_move_goal()
		attack_target = null
		velocity.x = wasd.x * SPEED
		velocity.z = wasd.z * SPEED
		_face_dir(wasd)
		move_and_slide()
		return

	if attack_target != null and is_instance_valid(attack_target):
		var to_enemy: Vector3 = attack_target.global_position - global_position
		to_enemy.y = 0.0
		if to_enemy.length() <= ATTACK_RANGE:
			_clear_move_goal()
			velocity.x = 0.0
			velocity.z = 0.0
			if to_enemy.length() > 0.01:
				_face_dir(to_enemy)
			_try_attack()
			move_and_slide()
			return
		_set_move(attack_target.global_position)

	if _has_move_goal:
		_steer_toward_goal()
	else:
		velocity.x = 0.0
		velocity.z = 0.0

	move_and_slide()


func _wasd_dir() -> Vector3:
	var input := Vector2(
		Input.get_action_strength("ui_right") - Input.get_action_strength("ui_left"),
		Input.get_action_strength("ui_down") - Input.get_action_strength("ui_up")
	)
	if Input.is_key_pressed(KEY_D):
		input.x += 1.0
	if Input.is_key_pressed(KEY_A):
		input.x -= 1.0
	if Input.is_key_pressed(KEY_S):
		input.y += 1.0
	if Input.is_key_pressed(KEY_W):
		input.y -= 1.0
	if input.length() < 0.01:
		return Vector3.ZERO
	input = input.normalized()
	# Match 45° iso camera: screen-up / screen-right on XZ.
	var forward := Vector3(-1.0, 0.0, -1.0).normalized()
	var right := Vector3(1.0, 0.0, -1.0).normalized()
	# input.y: W=-1 (forward), S=+1 (back)
	return (right * input.x + forward * (-input.y)).normalized()


func _steer_toward_goal() -> void:
	var flat_goal := Vector3(_move_goal.x, global_position.y, _move_goal.z)
	var to_goal: Vector3 = flat_goal - global_position
	to_goal.y = 0.0
	if to_goal.length() <= ARRIVE_DIST:
		_clear_move_goal()
		velocity.x = 0.0
		velocity.z = 0.0
		return

	var dir: Vector3 = to_goal
	if _nav_ready:
		_agent.target_position = flat_goal
		# Prefer nav waypoint when a real path exists.
		if not _agent.is_navigation_finished():
			var next: Vector3 = _agent.get_next_path_position()
			var nav_dir: Vector3 = global_position.direction_to(next)
			nav_dir.y = 0.0
			if nav_dir.length() > 0.05:
				dir = nav_dir

	if dir.length() < 0.001:
		velocity.x = 0.0
		velocity.z = 0.0
		return
	dir = dir.normalized()
	velocity.x = dir.x * SPEED
	velocity.z = dir.z * SPEED
	_face_dir(dir)


func handle_world_click(world_pos: Vector3, clicked: Node) -> void:
	if not _alive:
		return
	if clicked != null and clicked.is_in_group("enemy"):
		attack_target = clicked
		_set_move(clicked.global_position)
		return
	attack_target = null
	_set_move(world_pos)


func _set_move(pos: Vector3) -> void:
	_move_goal = Vector3(pos.x, global_position.y, pos.z)
	_has_move_goal = true
	if _nav_ready:
		_agent.target_position = _move_goal


func _clear_move_goal() -> void:
	_has_move_goal = false
	if _nav_ready:
		_agent.target_position = global_position


func _try_attack() -> void:
	if _attack_cd > 0.0 or attack_target == null:
		return
	_attack_cd = ATTACK_COOLDOWN
	if attack_target.has_method("take_damage"):
		attack_target.take_damage(GameState.get_attack_damage())
	var tw := create_tween()
	tw.tween_property(_mesh, "scale", Vector3(1.15, 0.9, 1.15), 0.06)
	tw.tween_property(_mesh, "scale", Vector3.ONE, 0.08)


func take_damage(amount: int) -> void:
	if not _alive:
		return
	GameState.apply_damage(amount)
	if GameState.hp <= 0:
		_alive = false
		died.emit()


func is_alive() -> bool:
	return _alive
