extends CharacterBody3D
## Aggro-on-range chase with navmesh pathing + direct fallback.
## Avoidance callback was unreliable — use physics separation instead.

signal died(enemy: Node3D)

@export var max_hp: int = 30
@export var damage: int = 6
@export var aggro_range: float = 11.0
@export var leash_range: float = 22.0
@export var attack_range: float = 1.8
@export var move_speed: float = 4.6
@export var attack_cooldown: float = 0.9
@export var path_refresh: float = 0.25

var hp: int
var _cd: float = 0.0
var _alive: bool = true
var _nav_ready: bool = false
var _refresh_left: float = 0.0
var _aggro: bool = false

@onready var _mesh: MeshInstance3D = $Mesh
@onready var _agent: NavigationAgent3D = $NavigationAgent3D


func _ready() -> void:
	add_to_group("enemy")
	collision_layer = 4
	collision_mask = 1 | 4 # world + other enemies
	hp = max_hp
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(0.75, 0.18, 0.15)
	_mesh.material_override = mat

	_agent.path_desired_distance = 0.5
	_agent.target_desired_distance = 0.7
	_agent.radius = 0.5
	_agent.height = 1.4
	# RVO avoidance was leaving enemies frozen when callback never fired.
	_agent.avoidance_enabled = false
	_agent.max_speed = move_speed
	call_deferred("_wait_for_nav")


func _wait_for_nav() -> void:
	var nav := get_tree().get_first_node_in_group("nav_setup")
	if nav != null:
		if nav.get("nav_bake_done") != true and nav.has_signal("navigation_ready"):
			await nav.navigation_ready
		_agent.set_navigation_map(nav.get_navigation_map())
	await get_tree().physics_frame
	await get_tree().physics_frame
	_nav_ready = true


func _physics_process(delta: float) -> void:
	if not _alive:
		return
	_cd = maxf(0.0, _cd - delta)
	_refresh_left -= delta

	if not is_on_floor():
		velocity.y -= 20.0 * delta
	else:
		velocity.y = 0.0

	var player: Node3D = _find_player()
	if player == null or not player.has_method("is_alive") or not bool(player.call("is_alive")):
		_stop_horizontal()
		move_and_slide()
		return

	var to: Vector3 = player.global_position - global_position
	to.y = 0.0
	var dist: float = to.length()

	# --- Aggro / leash ---
	if _aggro:
		if dist > leash_range:
			_aggro = false
	elif dist <= aggro_range:
		_aggro = true

	if not _aggro:
		_stop_horizontal()
		move_and_slide()
		return

	# --- Attack ---
	if dist <= attack_range:
		if _nav_ready:
			_agent.target_position = global_position
		_stop_horizontal()
		if to.length() > 0.01:
			_face_dir(to)
		if _cd <= 0.0:
			_cd = attack_cooldown
			player.call("take_damage", damage)
		move_and_slide()
		return

	# --- Chase ---
	if _nav_ready and _refresh_left <= 0.0:
		_refresh_left = path_refresh
		_agent.target_position = player.global_position

	var dir: Vector3 = to
	if _nav_ready and not _agent.is_navigation_finished():
		var next: Vector3 = _agent.get_next_path_position()
		var nav_dir: Vector3 = global_position.direction_to(next)
		nav_dir.y = 0.0
		if nav_dir.length() > 0.05:
			dir = nav_dir

	if dir.length() < 0.001:
		_stop_horizontal()
		move_and_slide()
		return

	dir = dir.normalized()
	# Soft separation so enemies don't fully stack.
	dir = (_separate(dir)).normalized()
	velocity.x = dir.x * move_speed
	velocity.z = dir.z * move_speed
	_face_dir(dir)
	move_and_slide()


func _separate(desired: Vector3) -> Vector3:
	var push := Vector3.ZERO
	for n in get_tree().get_nodes_in_group("enemy"):
		if n == self or not (n is Node3D):
			continue
		var other := n as Node3D
		var d: Vector3 = global_position - other.global_position
		d.y = 0.0
		var dist: float = d.length()
		if dist > 0.01 and dist < 1.4:
			push += d.normalized() * (1.4 - dist)
	if push.length() < 0.001:
		return desired
	return desired + push * 1.5


func _face_dir(dir: Vector3) -> void:
	var flat := Vector3(dir.x, 0.0, dir.z)
	if flat.length() < 0.001:
		return
	flat = flat.normalized()
	rotation.y = atan2(flat.x, flat.z)


func _stop_horizontal() -> void:
	velocity.x = 0.0
	velocity.z = 0.0


func take_damage(amount: int) -> void:
	if not _alive:
		return
	_aggro = true # getting hit always pulls aggro
	hp -= amount
	var tw := create_tween()
	tw.tween_property(_mesh, "scale", Vector3(1.2, 0.8, 1.2), 0.05)
	tw.tween_property(_mesh, "scale", Vector3.ONE, 0.08)
	if hp <= 0:
		_die()


func _die() -> void:
	_alive = false
	died.emit(self)
	queue_free()


func _find_player() -> Node3D:
	var nodes := get_tree().get_nodes_in_group("player")
	if nodes.is_empty():
		return null
	return nodes[0] as Node3D
