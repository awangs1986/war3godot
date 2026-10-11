extends SceneTree
## Headless simulation-layer smoke on the imported Lost Temple level:
## 1. pathing grid + budgeted pathfinding still work,
## 2. W3Sim moves a unit to a clicked point (CBehaviorMove port),
## 3. dynamic unit collision blocks walking through another unit,
## 4. melee combat: attacker chases, swings on cooldown, target dies and decays.


func _initialize() -> void:
	call_deferred("_run")


func _fail(msg: String) -> void:
	printerr("SIM_SMOKE_FAILED: ", msg)
	quit(1)


func _run() -> void:
	var World = preload("res://addons/w3kit_cs/World/W3MapWorld.cs")
	var W3Sim = preload("res://addons/w3kit_cs/Sim/W3Simulation.cs")
	var world = World.new()
	world.terrain_only = true
	root.add_child(world)
	var level = ResourceLoader.load("res://war3/maps/LostTemple/lost_temple.res", "", ResourceLoader.CACHE_MODE_IGNORE)
	if not world.load_level_data(level):
		_fail("level load: " + world.get_status())
		return
	if world.pathing == null:
		_fail("pathing grid missing (wpm not imported?)")
		return

	var start = _find_pathable_near(world, Vector2i(world.map.tile_count_x() / 2, world.map.tile_count_y() / 2))
	var goal = _find_pathable_near(world, Vector2i(world.map.tile_count_x() / 2 + 20, world.map.tile_count_y() / 2))
	if start == null or goal == null:
		_fail("no pathable start/goal found")
		return

	var Pathfinding = preload("res://addons/w3kit_cs/Pathing/W3PathfindingBridge.cs")
	var finder = Pathfinding.new() # C# 带参构造无法从 GDScript 调用，改 bind_grid
	finder.bind_grid(world.pathing)
	var t0 := Time.get_ticks_msec()
	var waypoints: PackedVector2Array = finder.find_naive_slow_path(
		Vector2(start.x, start.z), Vector2(goal.x, goal.z), 32.0, true)
	if waypoints.is_empty():
		_fail("pathfinder returned empty path")
		return
	print("SIM_SMOKE: path waypoints=%d in %dms" % [waypoints.size(), Time.get_ticks_msec() - t0])

	# --- Movement through the sim layer -------------------------------------
	var sim = W3Sim.new()
	root.add_child(sim)
	sim.set_physics_process(false) # ticks are driven manually below
	sim.setup(world.pathing)
	var stats := {
		"hp": 100.0, "speed": 320.0, "collision": 32.0, "turn_rate": 0.6,
		"prop_window_deg": 60.0, "attack_range": 128.0, "attack_cooldown": 1.0,
		"damage_dice": 1, "damage_sides": 1, "damage_plus": 9,
		"acquire_range": 500.0,
	}
	var mover = sim.create_unit("Obla", stats, Vector2(start.x, start.z), 270.0, 0)
	mover.order_move(Vector2(goal.x, goal.z))
	for _i in 1200:
		sim.step_once()
		if mover.state == mover.STATE_IDLE:
			break
	var dist: float = mover.pos.distance_to(Vector2(goal.x, goal.z))
	if mover.state != mover.STATE_IDLE or dist > 64.0:
		_fail("mover at %s, %.0f from goal (state=%d)" % [str(mover.pos), dist, mover.state])
		return
	print("SIM_SMOKE: movement arrived (%.1f from goal)" % dist)

	# --- Dynamic collision ----------------------------------------------------
	var blocker = sim.create_unit("ogru", stats, mover.pos + Vector2(96.0, 0.0), 270.0, 12)
	var bmin: Vector2 = blocker.rect_min()
	var bmax: Vector2 = blocker.rect_max()
	if not sim.collision.intersects_anything_other_than(bmin, bmax, mover):
		_fail("dynamic collision did not report the blocking unit")
		return
	if sim.collision.intersects_anything_other_than(bmin, bmax, blocker):
		_fail("dynamic collision reported a hit when only self overlaps")
		return
	print("SIM_SMOKE: dynamic unit collision responds correctly")

	# --- Melee combat ---------------------------------------------------------
	blocker.hp = 30.0
	blocker.max_hp = 30.0
	mover.order_attack(blocker)
	var died := false
	for _i in 600:
		sim.step_once()
		if blocker.is_dead():
			died = true
			break
	if not died:
		_fail("attack behavior never killed the 30hp target (target hp=%.0f)" % blocker.hp)
		return
	print("SIM_SMOKE: combat kill confirmed")
	# Corpse decay removes the unit from the sim.
	for _i in blocker.CORPSE_TICKS + 2:
		sim.step_once()
	if sim.get_unit(blocker.unit_id) != null:
		_fail("corpse did not decay")
		return
	if sim.get_unit(mover.unit_id) == null:
		_fail("attacker was removed unexpectedly")
		return
	print("SIM_SMOKE_OK: move + collide + fight + decay all pass")
	quit(0)


func _find_pathable_near(world, tile: Vector2i) -> Variant:
	for radius in 30:
		for dy in range(-radius, radius + 1):
			for dx in range(-radius, radius + 1):
				var t := tile + Vector2i(dx, dy)
				if t.x < 0 or t.y < 0 or t.x >= world.map.tile_count_x() or t.y >= world.map.tile_count_y():
					continue
				var p: Vector3 = world.sampler.tile_center_world(t)
				if world.pathing.is_pathable_foot(p.x, p.z, 32.0):
					return p
	return null
