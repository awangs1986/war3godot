extends Node3D
## Loads an imported .res level, an extracted map folder, or a raw .w3e.
## HiveWE-inspired path: GPU-instanced terrain/water, batched MDX, frustum cull,
## terrain-first then streamed props.
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3GpuTerrain = preload("res://addons/w3kit/w3_gpu_terrain.gd")
const W3CliffBuilder = preload("res://addons/w3kit/w3_cliff_builder.gd")
const W3GpuWater = preload("res://addons/w3kit/w3_gpu_water.gd")
const W3GridPathfinder = preload("res://addons/w3kit/w3_grid_pathfinder.gd")
const W3PathingGrid = preload("res://addons/w3kit/w3_pathing_grid.gd")
const W3MapArchive = preload("res://addons/w3kit/w3_map_archive.gd")
const W3DooParser = preload("res://addons/w3kit/w3_doo_parser.gd")
const W3MdxBatcher = preload("res://addons/w3kit/w3_mdx_batcher.gd")
const W3ObjectCatalog = preload("res://addons/w3kit/w3_object_catalog.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3LevelData = preload("res://addons/w3kit/w3_level_data.gd")
const W3MapInfoVersion = preload("res://addons/w3kit/w3_map_info_version.gd")
const NativeMapAdapter = preload("res://addons/w3kit/native_map_adapter.gd")
const WarsmashGroundRenderer = preload("res://addons/w3kit_cs/Terrain/WarsmashGroundRenderer.cs")
const WarsmashCliffRenderer = preload("res://addons/w3kit_cs/Terrain/WarsmashCliffRenderer.cs")


signal load_finished(ok: bool, message: String)

@export var terrain_only: bool = false
## Preview: skip directional shadows (HiveWE editor doesn't pay game shadow cost).
@export var preview_no_shadows: bool = false
## Preview-only escape hatch. Tests and production should keep this disabled.
@export var allow_procedural_fallback: bool = false
## Authoritative terrain path: C# port of Warsmash terrain/cliff rules.
@export var use_csharp_terrain: bool = true
## When true, mobile map units are handed to the simulation layer via
## unit_placements instead of being baked into batches + the pathing overlay.
@export var simulate_units: bool = false

var archive
var map
var sampler
var pathfinder
## Warsmash-style static pathing grid from war3map.wpm (null when unavailable).
var pathing
var textures
var catalog
var terrain
var cliffs
var water
var batcher
## W3DooParser.Placement list for the game layer (filled when simulate_units).
var unit_placements: Array = []
var _status: String = ""
var _load_path_label: String = ""
var _game_version_major: int = 0
var _game_version_minor: int = 0


func _ready() -> void:
	textures = W3TextureProvider.new()
	textures.load_terrain_slk()
	catalog = W3ObjectCatalog.new()
	terrain = WarsmashGroundRenderer.new() if use_csharp_terrain else W3GpuTerrain.new()
	terrain.name = "Terrain"
	add_child(terrain)
	cliffs = WarsmashCliffRenderer.new() if use_csharp_terrain else W3CliffBuilder.new()
	cliffs.name = "Cliffs"
	add_child(cliffs)
	water = W3GpuWater.new()
	water.name = "Water"
	add_child(water)
	batcher = W3MdxBatcher.new()
	batcher.name = "MdxBatches"
	add_child(batcher)


func load_path(path: String) -> bool:
	_clear_visuals()
	_reset_loaded_state()
	W3Paths.clear_resolve_cache()
	_load_path_label = path
	if path.get_extension().to_lower() == "res" or path.get_extension().to_lower() == "tres":
		var resource_path := path
		if not resource_path.begins_with("res://") and not resource_path.begins_with("user://"):
			resource_path = ProjectSettings.localize_path(resource_path)
		var level := ResourceLoader.load(resource_path)
		return load_level_data(level)
	var native_dir := path
	if path.get_file().to_lower() == "map.json":
		native_dir = path.get_base_dir()
	if DirAccess.dir_exists_absolute(native_dir) and FileAccess.file_exists(native_dir.path_join("map.json")):
		return _load_native(native_dir)
	archive = W3MapArchive.new()
	if path.is_empty():
		return _load_failure("empty map path")
	if not archive.open(path):
		_status = "W3X/map open failed: %s (%s)" % [path, archive.extract_error]
		return _fallback_or_fail(_status)
	var map_info := W3MapInfoVersion.parse(archive.read_file("war3map.w3i"))
	_game_version_major = int(map_info.game_version_major)
	_game_version_minor = int(map_info.game_version_minor)
	textures.set_map_dir(archive.map_dir)
	textures.load_terrain_slk()
	catalog.load_from_roots(archive)
	var w3e_bytes = archive.read_file("war3map.w3e")
	if w3e_bytes.is_empty():
		map = W3EMap.load_file(archive.map_dir.path_join("war3map.w3e"))
	else:
		map = W3EMap.load_bytes(w3e_bytes)
	if map == null:
		return _fallback_or_fail("failed to parse war3map.w3e: %s" % W3EMap.last_error)
	sampler = W3HeightSampler.new(map)
	pathfinder = W3GridPathfinder.new(sampler)
	_load_pathing_grid(archive.read_file("war3map.wpm"), archive.map_dir)

	# 1–2: terrain + water instanced (fast path). Cliffs still shared MDX instances.
	var shd_bytes: PackedByteArray = archive.read_file("war3map.shd")
	if shd_bytes.is_empty():
		var shd_path: String = archive.map_dir.path_join("war3map.shd")
		if FileAccess.file_exists(shd_path):
			var f := FileAccess.open(shd_path, FileAccess.READ)
			if f:
				shd_bytes = f.get_buffer(f.get_length())
	terrain.build(map, textures, shd_bytes)
	if cliffs.has_method("set_path_resolver"):
		# C# cliff renderer resolves MDX/textures through the archive (map dir
		# first, then game asset roots); a bare W3EMap has no resolve_path.
		cliffs.set_path_resolver(archive)
	cliffs.build(map, textures, shd_bytes)
	water.build(map, textures)

	if preview_no_shadows:
		_disable_scene_shadows()

	# 3–4: queue doodads into MultiMesh batcher (streamed + frustum).
	if not terrain_only:
		batcher.setup(textures, catalog, archive, sampler)
		_queue_doodads()
		batcher.begin_streaming()
	else:
		print("W3MapWorld: terrain_only — skipped doodads")

	_refresh_status()
	print(_status)
	load_finished.emit(true, _status)
	return true


func _load_native(folder: String) -> bool:
	var loaded: Dictionary = NativeMapAdapter.load_folder(folder)
	if loaded.is_empty() or loaded.get("map") == null:
		return _load_failure("failed to load d1map: %s (%s)" % [
			folder,
			str(loaded.get("error", "missing map data")),
		])

	# Reuse the existing presentation and asset pipeline, but never parse
	# war3map.w3e/doo on this path.
	archive = W3MapArchive.new()
	archive.map_dir = folder.rstrip("/")
	archive.source_path = folder
	map = loaded["map"]
	_game_version_major = 0
	_game_version_minor = 0
	textures.set_map_dir(archive.map_dir)
	textures.load_terrain_slk()
	catalog.load_from_roots(archive)
	sampler = W3HeightSampler.new(map)
	pathfinder = W3GridPathfinder.new(sampler)
	pathing = null

	terrain.build(map, textures, PackedByteArray())
	if cliffs.has_method("set_path_resolver"):
		cliffs.set_path_resolver(archive)
	cliffs.build(map, textures, PackedByteArray())
	water.build(map, textures)
	if preview_no_shadows:
		_disable_scene_shadows()

	var placements: Array = loaded.get("placements", [])
	if not terrain_only:
		batcher.setup(textures, catalog, archive, sampler)
		_queue_native_placements(placements)
		batcher.begin_streaming()
	_refresh_status()
	_status += " | d1map placements=%d" % placements.size()
	print("[D1Map] loaded name=%s corners=%d placements=%d" % [
		str(loaded.get("name", "")),
		map.width * map.height,
		placements.size(),
	])
	print(_status)
	load_finished.emit(true, _status)
	return true


## Runtime path for data produced by W3LevelImporter. No W3E parsing occurs here.
func load_level_data(level: Resource) -> bool:
	_clear_visuals()
	_reset_loaded_state()
	W3Paths.clear_resolve_cache()
	if level == null or not level.has_method("is_valid") or not level.is_valid():
		return _load_failure("invalid imported W3LevelData")
	_load_path_label = str(level.source_path)
	archive = level
	map = level
	_game_version_major = int(level.game_version_major)
	_game_version_minor = int(level.game_version_minor)
	textures.set_map_dir("")
	textures.load_terrain_slk()
	catalog.load_from_roots(level)
	sampler = W3HeightSampler.new(map)
	pathfinder = W3GridPathfinder.new(sampler)
	_load_pathing_grid(level.read_file("war3map.wpm"), "")
	terrain.build(map, textures, level.shadow_bytes)
	if cliffs.has_method("set_path_resolver"):
		cliffs.set_path_resolver(level)
	cliffs.build(map, textures, level.shadow_bytes)
	water.build(map, textures)
	if preview_no_shadows:
		_disable_scene_shadows()
	if not terrain_only:
		batcher.setup(textures, catalog, level, sampler)
		_queue_doodads()
		batcher.begin_streaming()
	_refresh_status()
	print(_status)
	load_finished.emit(true, _status)
	return true


func _fallback_or_fail(message: String) -> bool:
	if allow_procedural_fallback:
		push_warning(message)
		return _load_procedural(message)
	return _load_failure(message)


func _load_failure(message: String) -> bool:
	_status = message
	push_error("W3MapWorld: %s" % message)
	load_finished.emit(false, _status)
	return false


func _load_pathing_grid(wpm_bytes: PackedByteArray, map_dir: String) -> void:
	pathing = null
	if wpm_bytes.is_empty() and not map_dir.is_empty():
		var wpm_path: String = map_dir.path_join("war3map.wpm")
		if FileAccess.file_exists(wpm_path):
			var f := FileAccess.open(wpm_path, FileAccess.READ)
			if f:
				wpm_bytes = f.get_buffer(f.get_length())
	if wpm_bytes.is_empty():
		print("W3MapWorld: no war3map.wpm — pathing grid unavailable")
		return
	pathing = W3PathingGrid.load_bytes(wpm_bytes, map.center_offset)
	if pathing == null:
		push_warning("W3MapWorld: wpm parse failed: %s" % W3PathingGrid.last_error)
	else:
		print("W3MapWorld: pathing grid %dx%d" % [pathing.width, pathing.height])


func _reset_loaded_state() -> void:
	archive = null
	map = null
	sampler = null
	pathfinder = null
	pathing = null
	unit_placements = []
	_status = ""
	_game_version_major = 0
	_game_version_minor = 0


func load_procedural(tiles_x: int = 24, tiles_y: int = 24) -> bool:
	return _load_procedural("procedural test map", tiles_x, tiles_y)


func _load_procedural(msg: String, tiles_x: int = 24, tiles_y: int = 24) -> bool:
	_clear_visuals()
	_reset_loaded_state()
	map = W3EMap.make_test_map(tiles_x, tiles_y)
	textures = W3TextureProvider.new()
	catalog = W3ObjectCatalog.new()
	catalog.load_from_roots(null)
	sampler = W3HeightSampler.new(map)
	pathfinder = W3GridPathfinder.new(sampler)
	terrain.build(map, textures, PackedByteArray())
	_status = msg
	print("W3MapWorld: ", _status)
	load_finished.emit(true, _status)
	return true


func get_status() -> String:
	return _status


# --- Terrain boundary API (gameplay code should not reach into internals) ----


func is_loaded() -> bool:
	return map != null and sampler != null


## FOOT walkability on the W3 ground plane (static grid + baked overlay).
## Without a pathing grid everything is considered walkable.
func is_walkable(w3_x: float, w3_y: float, collision_size: float = 0.0) -> bool:
	if pathing == null:
		return true
	return pathing.is_pathable_foot(w3_x, w3_y, collision_size)


## Godot-Y terrain height at a W3 ground-plane point (0 when not loaded).
func ground_height_at(w3_x: float, w3_y: float) -> float:
	if sampler == null:
		return 0.0
	return float(sampler.sample_y(w3_x, w3_y))


## Marches a camera ray against the sampled terrain. Returns the Godot-space
## ground point (Vector3) or null when the ray leaves the map.
func pick_ground(camera: Camera3D, screen_pos: Vector2) -> Variant:
	if sampler == null or camera == null:
		return null
	var from := camera.project_ray_origin(screen_pos)
	var dir := camera.project_ray_normal(screen_pos)
	var prev_t := 0.0
	for _i in 400:
		var t := prev_t + 25.0
		var p := from + dir * t
		var dy := p.y - float(sampler.sample_y(p.x, p.z))
		if dy <= 0.0:
			# Bisect between prev_t and t for a tighter hit.
			var lo := prev_t
			var hi := t
			for _j in 12:
				var mid := (lo + hi) * 0.5
				var mp := from + dir * mid
				if mp.y - float(sampler.sample_y(mp.x, mp.z)) <= 0.0:
					hi = mid
				else:
					lo = mid
			var hp := from + dir * hi
			return Vector3(hp.x, float(sampler.sample_y(hp.x, hp.z)), hp.z)
		prev_t = t
	return null


func _disable_scene_shadows() -> void:
	var root := get_tree().current_scene if get_tree() else null
	if root == null:
		root = get_parent()
	if root == null:
		return
	for n in root.find_children("*", "DirectionalLight3D", true, false):
		var light := n as DirectionalLight3D
		if light:
			light.shadow_enabled = false


func _refresh_status() -> void:
	var cliff_stats: Dictionary = cliffs.get_counts() if cliffs.has_method("get_counts") else {}
	var bcounts: Dictionary = batcher.get_counts() if batcher else {}
	_status = "GPU map %dx%d | props ok=%s miss=%s batches=%s | cliffs ok=%s batches=%s | water=%s%s" % [
		map.tile_count_x() if map else 0,
		map.tile_count_y() if map else 0,
		str(bcounts.get("ok", 0)),
		str(bcounts.get("miss", 0)),
		str(bcounts.get("batches", 0)),
		str(cliff_stats.get("ok", 0)),
		str(cliff_stats.get("batches", 0)),
		str(water.get_water_tile_count() if water.has_method("get_water_tile_count") else 0),
		" [terrain preview]" if terrain_only else "",
	]


func _clear_visuals() -> void:
	if batcher:
		batcher.clear()
	if terrain:
		for c in terrain.get_children():
			c.queue_free()
	if cliffs:
		for c in cliffs.get_children():
			c.queue_free()
	if water:
		for c in water.get_children():
			c.queue_free()


func _queue_doodads() -> void:
	if archive == null:
		return
	var data = archive.read_file("war3map.doo")
	var doodads: Array = W3DooParser.parse_doodads(data, _game_version_major, _game_version_minor) if not data.is_empty() else []
	var unit_data = archive.read_file("war3mapUnits.doo")
	var units: Array = W3DooParser.parse_units(unit_data, _game_version_major, _game_version_minor) if not unit_data.is_empty() else []
	print("W3MapWorld: doodads=%d units=%d" % [doodads.size(), units.size()])
	for p in doodads:
		batcher.enqueue_placement(p)
	var blocked := 0
	for p in units:
		if simulate_units and _is_simulated_unit(p):
			unit_placements.append(p)
			continue
		batcher.enqueue_placement(p)
		if _blit_unit_pathing(p):
			blocked += 1
	if blocked > 0:
		print("W3MapWorld: pathing overlay from %d ground units" % blocked)
	if not unit_placements.is_empty():
		print("W3MapWorld: %d units handed to simulation layer" % unit_placements.size())


func _queue_native_placements(placements: Array) -> void:
	var doodads := 0
	var units := 0
	var blocked := 0
	for p in placements:
		if p.is_unit:
			units += 1
			if simulate_units and _is_simulated_unit(p):
				unit_placements.append(p)
				continue
			batcher.enqueue_placement(p)
			if _blit_unit_pathing(p):
				blocked += 1
		else:
			doodads += 1
			batcher.enqueue_placement(p)
	print("W3MapWorld(d1map): doodads=%d units=%d" % [doodads, units])
	if blocked > 0:
		print("W3MapWorld: pathing overlay from %d ground units" % blocked)
	if not unit_placements.is_empty():
		print("W3MapWorld: %d units handed to simulation layer" % unit_placements.size())


## Mobile ground units become live W3SimUnits (dynamic collision) instead of
## static batched statues baked into the pathing overlay.
func _is_simulated_unit(p) -> bool:
	if p == null or not p.is_unit or catalog == null:
		return false
	if str(p.id) in ["sloc", "ncam", "Start"]:
		return false
	if catalog.unit_move_height(str(p.id)) > 0.0:
		return false
	return catalog.unit_move_type(str(p.id)) in ["foot", "horse", "amph", "hover"]


## Bakes a placed unit's CWorldCollision square into the pathing overlay so the
## simulated hero collides with map units (Warsmash keeps these in a quadtree;
## static preview units are equivalent as baked cells).
func _blit_unit_pathing(p) -> bool:
	if pathing == null or catalog == null or p == null:
		return false
	var id := str(p.id)
	if id in ["sloc", "ncam", "Start"]:
		return false
	# Flying units never block FOOT movement.
	if catalog.unit_move_type(id) == "fly" or catalog.unit_move_height(id) > 0.0:
		return false
	var collision: float = catalog.unit_collision(id)
	if collision <= 0.0:
		return false
	pathing.blit_unit_collision(p.position.x, p.position.z, collision)
	return true
