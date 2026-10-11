extends Node3D
## Shared level logic: click raycast, HUD hooks, portals, enemy loot.

@export var is_dungeon: bool = false
@export var dungeon_depth: int = 1

@onready var _player: CharacterBody3D = $Player
@onready var _camera_rig: Node3D = $IsoCamera
@onready var _hud: CanvasLayer = $HUD


func _ready() -> void:
	RenderingServer.set_default_clear_color(Color(0.18, 0.16, 0.22))
	_camera_rig.follow_target = _player
	if _camera_rig.has_method("snap_to_target"):
		_camera_rig.snap_to_target()
	# Ensure active camera after scene swap.
	var cam: Camera3D = _camera_rig.get_camera() if _camera_rig.has_method("get_camera") else null
	if cam:
		cam.make_current()

	if _player.has_signal("died"):
		_player.died.connect(_on_player_died)
	for e in get_tree().get_nodes_in_group("enemy"):
		if e.has_signal("died"):
			e.died.connect(_on_enemy_died)
	if is_dungeon:
		GameState.dungeon_depth_reached = maxi(GameState.dungeon_depth_reached, dungeon_depth)
	for n in get_tree().get_nodes_in_group("portal_dungeon"):
		if n.has_signal("activated"):
			n.activated.connect(_go_dungeon)
	for n in get_tree().get_nodes_in_group("portal_town"):
		if n.has_signal("activated"):
			n.activated.connect(_go_town)
	for n in get_tree().get_nodes_in_group("merchant"):
		if n.has_signal("opened"):
			n.opened.connect(_hud.show_merchant)

	print("Level ready: ", name, " dungeon=", is_dungeon, " player=", _player != null, " cam_current=", cam.current if cam else false)


func _input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_inventory"):
		_hud.toggle_inventory()
		get_viewport().set_input_as_handled()
		return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if _hud.is_blocking_clicks():
			return
		_handle_click(get_viewport().get_mouse_position())
		get_viewport().set_input_as_handled()


func _handle_click(screen_pos: Vector2) -> void:
	var cam: Camera3D = _camera_rig.get_camera()
	if cam == null or _player == null:
		return

	var from: Vector3 = cam.project_ray_origin(screen_pos)
	var dir: Vector3 = cam.project_ray_normal(screen_pos)
	var plane_point: Variant = _ray_to_ground(from, dir)

	var space := get_world_3d().direct_space_state
	var q := PhysicsRayQueryParameters3D.create(from, from + dir * 800.0)
	q.collide_with_areas = false
	q.collide_with_bodies = true
	q.collision_mask = 1 | 4 | 16
	var hit := space.intersect_ray(q)

	if not hit.is_empty():
		var cur: Node = hit["collider"] as Node
		while cur:
			if cur.is_in_group("enemy"):
				_player.handle_world_click(hit["position"], cur)
				return
			if cur.is_in_group("interact") and cur.has_method("activate"):
				cur.activate(_player)
				return
			cur = cur.get_parent()

	if plane_point != null:
		_player.handle_world_click(plane_point as Vector3, null)
	elif not hit.is_empty():
		var p: Vector3 = hit["position"]
		p.y = _player.global_position.y
		_player.handle_world_click(p, null)


func _ray_to_ground(from: Vector3, dir: Vector3) -> Variant:
	if absf(dir.y) < 0.0001:
		return null
	var t: float = -from.y / dir.y
	if t < 0.0:
		return null
	return from + dir * t


func _on_enemy_died(enemy: Node3D) -> void:
	var loot := ItemDB.random_loot_for_depth(dungeon_depth if is_dungeon else 0)
	var drop := preload("res://scenes/loot_pickup.tscn").instantiate()
	add_child(drop)
	drop.global_position = enemy.global_position + Vector3(0, 0.4, 0)
	drop.setup(loot)


func _on_player_died() -> void:
	var loss := mini(GameState.gold, 25)
	GameState.add_gold(-loss)
	GameState.heal_full()
	await get_tree().create_timer(0.8).timeout
	_go_town()


func _go_dungeon() -> void:
	# Deferred — changing scene mid-input/physics is unsafe and can blank the viewport.
	get_tree().call_deferred("change_scene_to_file", "res://scenes/dungeon.tscn")


func _go_town() -> void:
	GameState.heal_full()
	get_tree().call_deferred("change_scene_to_file", "res://scenes/town.tscn")
