extends SceneTree
## Headless smoke: Lost Temple + doodads + units, wait for MDX stream.


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var map_path := "/path/to/Warcraft.III.R/Maps/(4)LostTemple.w3m"
	if not FileAccess.file_exists(map_path):
		push_error("map missing: " + map_path)
		quit(1)
		return
	var world_script = load("res://addons/w3kit_cs/World/W3MapWorld.cs")
	var world = world_script.new()
	root.add_child(world)
	world.terrain_only = false
	print("SMOKE load_path_async ", map_path)
	var t0 := Time.get_ticks_msec()
	var ok: bool = await world.load_path_async(map_path)
	var ms := Time.get_ticks_msec() - t0
	var counts: Dictionary = world.batcher.get_counts() if world.batcher else {}
	print("SMOKE ok=", ok, " ms=", ms)
	print("SMOKE status=", world.get_status())
	print("SMOKE batcher=", counts)
	print("SMOKE map=", world.map.tile_count_x() if world.map else 0, "x", world.map.tile_count_y() if world.map else 0)
	var pass_ok: bool = ok and int(counts.get("ok", 0)) > 0 and world.batcher.is_streaming_complete()
	quit(0 if pass_ok else 2)
