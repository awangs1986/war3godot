extends Node
## Game layer: connects the loaded W3 world (terrain + assets) to the
## simulation (W3Sim) and its presentation (W3UnitRenderer). This is the seam
## the ARPG grows from: player unit, map monsters, click orders.

const W3Sim = preload("res://addons/w3kit/sim/w3_sim.gd")
const W3UnitRenderer = preload("res://addons/w3kit/sim/w3_unit_renderer.gd")
const W3Coords = preload("res://addons/w3kit/w3_coords.gd")

const PLAYER_OWNER := 0
const NEUTRAL_HOSTILE := 24 # WC3 PLAYER_NEUTRAL_AGGRESSIVE
const PLAYER_RAWCODE := "Obla" # Blademaster stand-in until ARPG classes exist

signal player_spawned(unit)
signal player_died(unit)

var world # w3_map_world
var sim: W3Sim
var renderer: W3UnitRenderer
var player = null # W3SimUnit or null


func setup(p_world) -> bool:
	world = p_world
	if world == null or not world.is_loaded():
		push_error("W3Game: world is not loaded")
		return false
	sim = W3Sim.new()
	sim.name = "W3Sim"
	add_child(sim)
	sim.setup(world.pathing)
	renderer = W3UnitRenderer.new()
	renderer.name = "W3UnitRenderer"
	add_child(renderer)
	renderer.setup(world, sim)
	sim.unit_died.connect(_on_unit_died)
	_spawn_map_units()
	return true


func _spawn_map_units() -> void:
	if world.catalog == null:
		push_error("W3Game: no object catalog — map units skipped")
		return
	var spawned := 0
	for p in world.unit_placements:
		var rawcode := str(p.id)
		var stats: Dictionary = world.catalog.unit_combat_stats(rawcode)
		var w3_pos: Vector2 = W3Coords.godot_to_ground(p.position)
		var unit = sim.create_unit(rawcode, stats, w3_pos, rad_to_deg(float(p.angle)), int(p.player_owner))
		unit.auto_acquire = int(p.player_owner) == NEUTRAL_HOSTILE
		spawned += 1
	if spawned > 0:
		print("W3Game: %d map units simulated" % spawned)


## Spawns (or teleports) the player unit at a Godot-space ground point.
func spawn_player(godot_point: Vector3):
	var w3_point := W3Coords.godot_to_ground(godot_point)
	w3_point = sim.find_pathable_spawn(w3_point, 32.0)
	if player != null and not player.is_dead():
		player.teleport(w3_point)
		return player
	var stats: Dictionary = world.catalog.unit_combat_stats(PLAYER_RAWCODE)
	player = sim.create_unit(PLAYER_RAWCODE, stats, w3_point, 270.0, PLAYER_OWNER)
	player_spawned.emit(player)
	print("W3Game: player %s spawned at %s" % [PLAYER_RAWCODE, str(w3_point)])
	return player


func has_live_player() -> bool:
	return player != null and not player.is_dead()


## LMB order: attack a picked enemy, otherwise move to the ground point.
func issue_player_order(camera: Camera3D, screen_pos: Vector2) -> bool:
	if not has_live_player():
		return false
	var target = pick_unit(camera, screen_pos, player)
	if target != null and target.owner != player.owner:
		player.order_attack(target)
		return true
	var hit = world.pick_ground(camera, screen_pos)
	if hit == null:
		return false
	player.order_move(W3Coords.godot_to_ground(hit))
	return true


## Screen-space unit pick: nearest live unit whose projected center is within
## its projected collision circle (plus a small grace margin).
func pick_unit(camera: Camera3D, screen_pos: Vector2, ignore = null):
	var best = null
	var best_px := INF
	for unit in sim.get_units():
		if unit == ignore or unit.is_dead():
			continue
		var height: float = world.ground_height_at(unit.pos.x, unit.pos.y)
		var center := W3Coords.ground_to_godot(unit.pos.x, unit.pos.y, height + 40.0)
		if camera.is_position_behind(center):
			continue
		var projected := camera.unproject_position(center)
		var edge := camera.unproject_position(center + Vector3(unit.collision_size * 1.6, 0.0, 0.0))
		var pick_radius := maxf(16.0, projected.distance_to(edge))
		var d := projected.distance_to(screen_pos)
		if d <= pick_radius and d < best_px:
			best_px = d
			best = unit
	return best


func _on_unit_died(unit, _killer) -> void:
	if unit == player:
		player_died.emit(unit)
		print("W3Game: player died")
