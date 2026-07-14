extends Node

func _ready() -> void:
	await get_tree().process_frame
	get_tree().change_scene_to_file("res://scenes/town.tscn")
	await get_tree().process_frame
	await get_tree().process_frame
	await get_tree().create_timer(0.5).timeout
	var nav = get_tree().get_first_node_in_group("nav_setup")
	var player = get_tree().get_first_node_in_group("player")
	print("SMOKE nav=", nav, " player=", player)
	if nav and not nav.bake_finished:
		await nav.navigation_ready
	print("SMOKE polygons=", nav.polygon_count if nav else -1)
	if player == null:
		printerr("no player")
		get_tree().quit(1)
		return
	player.handle_world_click(Vector3(6, 0, -6), null)
	var start: Vector3 = player.global_position
	for i in 45:
		await get_tree().physics_frame
	var end: Vector3 = player.global_position
	var dist := Vector2(end.x - start.x, end.z - start.z).length()
	print("SMOKE dist=", dist, " start=", start, " end=", end, " vel=", player.velocity, " has_goal=", player._has_move_goal)
	get_tree().quit(0 if dist > 0.5 else 2)
