extends SceneTree
## Full-map regression for the default C# Warsmash terrain path.

const W3MapWorld = preload("res://addons/w3kit/w3_map_world.gd")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var world = W3MapWorld.new()
	world.terrain_only = true
	world.use_csharp_terrain = true
	root.add_child(world)
	var ok: bool = world.load_path(ProjectSettings.globalize_path("res://war3/maps/LostTemple/lost_temple.res"))
	if not ok:
		printerr("CS_TERRAIN_FAIL load: ", world.get_status())
		quit(1)
		return
	var terrain_script_path := str(world.terrain.get_script().resource_path)
	var cliff_script_path := str(world.cliffs.get_script().resource_path)
	if not terrain_script_path.ends_with("WarsmashGroundRenderer.cs") or not cliff_script_path.ends_with("WarsmashCliffRenderer.cs"):
		printerr("CS_TERRAIN_FAIL wrong runtime scripts: ", terrain_script_path, " / ", cliff_script_path)
		quit(1)
		return
	if world.terrain.ground_exists(44, 65) or world.terrain.ground_exists(45, 65):
		printerr("CS_TERRAIN_FAIL BALH ground holes are not owned")
		quit(1)
		return
	var transition = world.cliffs.get_node_or_null("Cliff_44_65_BALH")
	if transition == null or str(transition.get_meta("w3_model_rel", "")) != "Doodads/Terrain/CliffTrans/CliffTransBALH0.mdx":
		printerr("CS_TERRAIN_FAIL BALH model missing")
		quit(1)
		return
	var counts: Dictionary = world.cliffs.get_counts()
	if int(counts.get("miss", -1)) != 0:
		printerr("CS_TERRAIN_FAIL missing cliff assets: ", counts)
		quit(1)
		return
	print("CS_TERRAIN_OK counts=", counts, " BALH=", transition.position)
	quit(0)
