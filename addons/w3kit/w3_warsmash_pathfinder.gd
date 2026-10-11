extends RefCounted
## Facade: Warsmash WPM pathing + A* with HeightSampler Y stick.
## Drop-in for map world `pathfinder` (Diablo click-move).


const W3Wpm = preload("res://addons/w3kit/w3_wpm.gd")
const W3PathingGrid = preload("res://addons/w3kit/w3_pathing_grid.gd")
const W3PathfindingProcessor = preload("res://addons/w3kit/w3_pathfinding_processor.gd")
const W3GridPathfinder = preload("res://addons/w3kit/w3_grid_pathfinder.gd")

var sampler
var pathing_grid
var processor
var _tile_fallback
var source_label: String = ""


func _init(p_sampler = null, wpm = null) -> void:
	sampler = p_sampler
	var center := Vector2.ZERO
	if sampler != null and sampler.map != null:
		center = sampler.map.center_offset
	if wpm != null:
		pathing_grid = W3PathingGrid.from_wpm(wpm, center)
		source_label = "wpm"
	else:
		pathing_grid = W3PathingGrid.from_sampler(sampler, center)
		source_label = "synthesized"
	processor = W3PathfindingProcessor.new(pathing_grid)
	_tile_fallback = W3GridPathfinder.new(sampler)


## Preferred API: world-space waypoints with Y from sampler.
func find_world_path(from: Vector3, to: Vector3, collision_size: float = 16.0) -> Array[Vector3]:
	var out: Array[Vector3] = []
	if processor == null or pathing_grid == null:
		return _fallback_world(from, to)
	var pts: Array[Vector2] = processor.find_path(
		from.x, from.z, to.x, to.z, W3PathingGrid.MovementType.FOOT, collision_size, true
	)
	if pts.is_empty():
		return _fallback_world(from, to)
	for p in pts:
		var y: float = 0.0
		if sampler:
			y = float(sampler.sample_y(p.x, p.y))
		out.append(Vector3(p.x, y, p.y))
	return out


## Legacy tile API used by older callers.
func find_path(start: Vector2i, goal: Vector2i) -> Array[Vector2i]:
	if sampler == null:
		return []
	var a: Vector3 = sampler.tile_center_world(start)
	var b: Vector3 = sampler.tile_center_world(goal)
	var world_path := find_world_path(a, b)
	var tiles: Array[Vector2i] = []
	var last := Vector2i(-999999, -999999)
	for p in world_path:
		var t: Vector2i = sampler.world_to_tile(p.x, p.z)
		if t != last:
			tiles.append(t)
			last = t
	if tiles.is_empty() and _tile_fallback:
		return _tile_fallback.find_path(start, goal)
	return tiles


func _fallback_world(from: Vector3, to: Vector3) -> Array[Vector3]:
	var out: Array[Vector3] = []
	if sampler == null or _tile_fallback == null:
		return out
	var st: Vector2i = sampler.world_to_tile(from.x, from.z)
	var gt: Vector2i = sampler.world_to_tile(to.x, to.z)
	var tiles: Array[Vector2i] = _tile_fallback.find_path(st, gt)
	for t in tiles:
		out.append(sampler.tile_center_world(t))
	return out


static func load_for_map(sampler, archive) -> RefCounted:
	var wpm = null
	if archive != null:
		var bytes: PackedByteArray = archive.read_file("war3map.wpm")
		if bytes.is_empty() and not str(archive.map_dir).is_empty():
			var p: String = str(archive.map_dir).path_join("war3map.wpm")
			if FileAccess.file_exists(p):
				wpm = W3Wpm.load_file(p)
		elif not bytes.is_empty():
			wpm = W3Wpm.load_bytes(bytes)
	return new(sampler, wpm)
