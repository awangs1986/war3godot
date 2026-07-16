extends Node3D
## Loads a .w3x / extracted map folder / .w3e / imported W3LevelData (.res).
## HiveWE-inspired path: GPU-instanced terrain/water, batched MDX (doodads + units).
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3GpuTerrain = preload("res://addons/w3kit/w3_gpu_terrain.gd")
const W3CliffBuilder = preload("res://addons/w3kit/w3_cliff_builder.gd")
const W3GpuWater = preload("res://addons/w3kit/w3_gpu_water.gd")
const W3WarsmashPathfinder = preload("res://addons/w3kit/w3_warsmash_pathfinder.gd")
const W3MapArchive = preload("res://addons/w3kit/w3_map_archive.gd")
const W3DooParser = preload("res://addons/w3kit/w3_doo_parser.gd")
const W3MdxBatcher = preload("res://addons/w3kit/w3_mdx_batcher.gd")
const W3ObjectCatalog = preload("res://addons/w3kit/w3_object_catalog.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")


signal load_finished(ok: bool, message: String)
signal load_progress(ratio: float, message: String)

@export var terrain_only: bool = false
## Preview: skip directional shadows (HiveWE editor doesn't pay game shadow cost).
@export var preview_no_shadows: bool = true
## Preview-only escape hatch. Tests and production should keep this disabled.
@export var allow_procedural_fallback: bool = false

var archive
var map
var sampler
var pathfinder
var textures
var catalog
var terrain
var cliffs
var water
var batcher
var _status: String = ""
var _load_path_label: String = ""


func _ready() -> void:
	textures = W3TextureProvider.new()
	textures.load_terrain_slk()
	catalog = W3ObjectCatalog.new()
	terrain = W3GpuTerrain.new()
	terrain.name = "Terrain"
	add_child(terrain)
	cliffs = W3CliffBuilder.new()
	cliffs.name = "Cliffs"
	add_child(cliffs)
	water = W3GpuWater.new()
	water.name = "Water"
	add_child(water)
	batcher = W3MdxBatcher.new()
	batcher.name = "MdxBatches"
	add_child(batcher)


func load_path(path: String) -> bool:
	## Sync entry for tools/smoke. Starts MDX stream but does not wait for it.
	return _load_path_sync(path)


func _emit_progress(ratio: float, message: String) -> void:
	load_progress.emit(clampf(ratio, 0.0, 1.0), message)


func _is_level_resource_path(path: String) -> bool:
	var ext := path.get_extension().to_lower()
	return ext == "res" or ext == "tres"


func _resolve_resource_path(path: String) -> String:
	if path.begins_with("res://") or path.begins_with("user://"):
		return path
	return ProjectSettings.localize_path(path)


## Runtime path for data produced by W3LevelImporter. No W3E parsing occurs here.
func load_level_data(level: Resource) -> bool:
	_clear_visuals()
	_reset_loaded_state()
	W3Paths.clear_resolve_cache()
	if level == null or not level.has_method("is_valid") or not level.is_valid():
		return _load_failure("invalid imported W3LevelData")
	_load_path_label = str(level.get("source_path"))
	archive = level
	map = level
	textures.set_map_dir("")
	textures.load_terrain_slk()
	catalog.load_from_roots(level)
	sampler = W3HeightSampler.new(map)
	pathfinder = W3WarsmashPathfinder.load_for_map(sampler, archive)
	print("W3MapWorld: pathfinder=", pathfinder.source_label if pathfinder else "null")
	var shd: PackedByteArray = PackedByteArray()
	if level.get("shadow_bytes") != null:
		shd = level.shadow_bytes
	terrain.build(map, textures, shd)
	cliffs.build(map, textures)
	water.build(map, textures)
	if preview_no_shadows:
		_disable_scene_shadows()
	if not terrain_only:
		batcher.setup(textures, catalog, level, sampler)
		_queue_doodads()
		_queue_units()
		batcher.begin_streaming()
	_refresh_status()
	print(_status)
	_emit_progress(1.0, "Ready")
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


func _reset_loaded_state() -> void:
	archive = null
	map = null
	sampler = null
	pathfinder = null
	_status = ""


func load_path_async(path: String) -> bool:
	## Sandbox: yield between stages + wait until MDX stream completes.
	_clear_visuals()
	_reset_loaded_state()
	W3Paths.clear_resolve_cache()
	_load_path_label = path
	_emit_progress(0.02, "Opening map…")
	await get_tree().process_frame

	if _is_level_resource_path(path):
		var resource_path := _resolve_resource_path(path)
		var level := ResourceLoader.load(resource_path)
		return load_level_data(level)

	archive = W3MapArchive.new()
	if path.is_empty():
		return _fallback_or_fail("empty path")
	if not archive.open(path):
		_status = "W3X/map open failed: %s (%s)" % [path, archive.extract_error]
		return _fallback_or_fail(_status)

	_emit_progress(0.10, "Catalog / SLK…")
	await get_tree().process_frame
	textures.set_map_dir(archive.map_dir)
	textures.load_terrain_slk()
	catalog.load_from_roots(archive)

	_emit_progress(0.15, "Parsing W3E…")
	await get_tree().process_frame
	var w3e_bytes = archive.read_file("war3map.w3e")
	if w3e_bytes.is_empty():
		map = W3EMap.load_file(archive.map_dir.path_join("war3map.w3e"))
	else:
		map = W3EMap.load_bytes(w3e_bytes)
	if map == null:
		return _fallback_or_fail("failed to parse war3map.w3e: %s" % W3EMap.last_error)
	sampler = W3HeightSampler.new(map)
	pathfinder = W3WarsmashPathfinder.load_for_map(sampler, archive)
	print("W3MapWorld: pathfinder=", pathfinder.source_label if pathfinder else "null")

	var shd_bytes: PackedByteArray = archive.read_file("war3map.shd")
	if shd_bytes.is_empty():
		var shd_path: String = archive.map_dir.path_join("war3map.shd")
		if FileAccess.file_exists(shd_path):
			var f := FileAccess.open(shd_path, FileAccess.READ)
			if f:
				shd_bytes = f.get_buffer(f.get_length())

	_emit_progress(0.25, "Building ground…")
	await get_tree().process_frame
	terrain.build(map, textures, shd_bytes)

	_emit_progress(0.45, "Building cliffs…")
	await get_tree().process_frame
	cliffs.build(map, textures)

	_emit_progress(0.55, "Building water…")
	await get_tree().process_frame
	water.build(map, textures)

	if preview_no_shadows:
		_disable_scene_shadows()

	if not terrain_only:
		_emit_progress(0.60, "Queueing doodads / units…")
		await get_tree().process_frame
		batcher.setup(textures, catalog, archive, sampler)
		_queue_doodads()
		_queue_units()
		batcher.begin_streaming()
		while not batcher.is_streaming_complete():
			var sp: float = batcher.get_stream_progress()
			_emit_progress(0.62 + sp * 0.36, "Loading models…")
			await get_tree().process_frame
	else:
		print("W3MapWorld: terrain_only — skipped doodads/units")

	_refresh_status()
	print(_status)
	_emit_progress(1.0, "Ready")
	load_finished.emit(true, _status)
	return true


func _load_path_sync(path: String) -> bool:
	_clear_visuals()
	_reset_loaded_state()
	W3Paths.clear_resolve_cache()
	_load_path_label = path

	if _is_level_resource_path(path):
		var resource_path := _resolve_resource_path(path)
		var level := ResourceLoader.load(resource_path)
		return load_level_data(level)

	archive = W3MapArchive.new()
	if path.is_empty():
		return _fallback_or_fail("empty path")
	if not archive.open(path):
		_status = "W3X/map open failed: %s (%s)" % [path, archive.extract_error]
		return _fallback_or_fail(_status)

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
	pathfinder = W3WarsmashPathfinder.load_for_map(sampler, archive)
	print("W3MapWorld: pathfinder=", pathfinder.source_label if pathfinder else "null")

	var shd_bytes: PackedByteArray = archive.read_file("war3map.shd")
	if shd_bytes.is_empty():
		var shd_path: String = archive.map_dir.path_join("war3map.shd")
		if FileAccess.file_exists(shd_path):
			var f := FileAccess.open(shd_path, FileAccess.READ)
			if f:
				shd_bytes = f.get_buffer(f.get_length())

	terrain.build(map, textures, shd_bytes)
	cliffs.build(map, textures)
	water.build(map, textures)

	if preview_no_shadows:
		_disable_scene_shadows()

	if not terrain_only:
		batcher.setup(textures, catalog, archive, sampler)
		_queue_doodads()
		_queue_units()
		batcher.begin_streaming()
	else:
		print("W3MapWorld: terrain_only — skipped doodads/units")

	_refresh_status()
	print(_status)
	load_finished.emit(true, _status)
	return true


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
	pathfinder = W3WarsmashPathfinder.load_for_map(sampler, archive)
	print("W3MapWorld: pathfinder=", pathfinder.source_label if pathfinder else "null")
	terrain.build(map, textures)
	_status = msg
	print("W3MapWorld: ", _status)
	_emit_progress(1.0, msg)
	load_finished.emit(true, _status)
	return true


func get_status() -> String:
	return _status


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
	_status = "GPU map %dx%d | props ok=%s anim=%s/%s miss=%s batches=%s | cliffs ok=%s | water=%s%s" % [
		map.tile_count_x() if map else 0,
		map.tile_count_y() if map else 0,
		str(bcounts.get("ok", 0)),
		str(bcounts.get("anim_active", 0)),
		str(bcounts.get("animated", 0)),
		str(bcounts.get("miss", 0)),
		str(bcounts.get("batches", 0)),
		str(cliff_stats.get("ok", 0)),
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
	if data.is_empty():
		return
	var placements: Array = W3DooParser.parse_doodads(data)
	print("W3MapWorld: doodads=", placements.size())
	for p in placements:
		batcher.enqueue_placement(p)


func _queue_units() -> void:
	if archive == null:
		return
	var data = archive.read_file("war3mapUnits.doo")
	if data.is_empty():
		data = archive.read_file("war3mapunits.doo")
	if data.is_empty():
		print("W3MapWorld: no war3mapUnits.doo")
		return
	var placements: Array = W3DooParser.parse_units(data)
	print("W3MapWorld: units=", placements.size())
	for p in placements:
		batcher.enqueue_placement(p)
