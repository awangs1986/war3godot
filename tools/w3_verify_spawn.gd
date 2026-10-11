extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	print("VERIFY start")
	var world_script = load("res://addons/w3kit/w3_map_world.gd")
	if world_script == null:
		push_error("map_world failed to load")
		quit(1)
		return
	var world = world_script.new()
	root.add_child(world)
	world.terrain_only = false
	var map_path = ProjectSettings.globalize_path("res://war3/maps/Adrenaline")
	print("map ", map_path)
	var t0 = Time.get_ticks_msec()
	var ok = world.load_path(map_path)
	print("DONE ok=", ok, " ms=", Time.get_ticks_msec() - t0, " status=", world.get_status())
	print("doodads=", world.get_node("Doodads").get_child_count(), " units=", world.get_node("MapUnits").get_child_count())
	quit(0 if ok else 1)
