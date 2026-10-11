extends SceneTree
## Headless smoke: bake nav, set player goal, ensure velocity/path updates.

func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	change_scene_to_file("res://scenes/town.tscn")
	await process_frame
	await process_frame
	var nav := root.get_node_or_null("Town/NavigationRegion3D")
	var player := root.get_node_or_null("Town/Player")
	if nav == null or player == null:
		printerr("SMOKE FAIL: missing nodes")
		quit(1)
		return
	if not nav.bake_finished:
		await nav.navigation_ready
	await physics_frame
	await physics_frame
	print("SMOKE polygons=", nav.polygon_count, " bake=", nav.bake_finished)
	player.handle_world_click(Vector3(5, 0, -5), null)
	var moved := false
	for i in 30:
		await physics_frame
		if Vector2(player.velocity.x, player.velocity.z).length() > 0.1:
			moved = true
			break
		if player.global_position.distance_to(Vector3(5, player.global_position.y, -5)) < 1.0:
			moved = true
			break
	print("SMOKE moved=", moved, " pos=", player.global_position, " vel=", player.velocity, " goal=", player._has_move_goal)
	quit(0 if moved else 2)
