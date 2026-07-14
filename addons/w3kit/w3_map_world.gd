extends Node3D
## Loads a .w3x / extracted map folder / .w3e.
## HiveWE-inspired path: GPU-instanced terrain/water, batched MDX, frustum cull,
## terrain-first then streamed props.
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3GpuTerrain = preload("res://addons/w3kit/w3_gpu_terrain.gd")
const W3CliffBuilder = preload("res://addons/w3kit/w3_cliff_builder.gd")
const W3GpuWater = preload("res://addons/w3kit/w3_gpu_water.gd")
const W3GridPathfinder = preload("res://addons/w3kit/w3_grid_pathfinder.gd")
const W3MapArchive = preload("res://addons/w3kit/w3_map_archive.gd")
const W3DooParser = preload("res://addons/w3kit/w3_doo_parser.gd")
const W3MdxBatcher = preload("res://addons/w3kit/w3_mdx_batcher.gd")
const W3ObjectCatalog = preload("res://addons/w3kit/w3_object_catalog.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")


signal load_finished(ok: bool, message: String)

@export var terrain_only: bool = false
## Preview: skip directional shadows (HiveWE editor doesn't pay game shadow cost).
@export var preview_no_shadows: bool = true

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
	_clear_visuals()
	W3Paths.clear_resolve_cache()
	_load_path_label = path
	archive = W3MapArchive.new()
	if path.is_empty():
		return _load_procedural("empty path — using procedural test map")
	if not archive.open(path):
		_status = "W3X/map open failed: %s — fallback procedural. (%s)" % [path, archive.extract_error]
		push_warning(_status)
		return _load_procedural(_status)
	textures.set_map_dir(archive.map_dir)
	textures.load_terrain_slk()
	catalog.load_from_roots(archive)
	var w3e_bytes = archive.read_file("war3map.w3e")
	if w3e_bytes.is_empty():
		map = W3EMap.load_file(archive.map_dir.path_join("war3map.w3e"))
	else:
		map = W3EMap.load_bytes(w3e_bytes)
	if map == null:
		return _load_procedural("failed to parse war3map.w3e")
	sampler = W3HeightSampler.new(map)
	pathfinder = W3GridPathfinder.new(sampler)

	# 1–2: terrain + water instanced (fast path). Cliffs still shared MDX instances.
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


func load_procedural(tiles_x: int = 24, tiles_y: int = 24) -> bool:
	return _load_procedural("procedural test map", tiles_x, tiles_y)


func _load_procedural(msg: String, tiles_x: int = 24, tiles_y: int = 24) -> bool:
	_clear_visuals()
	map = W3EMap.make_test_map(tiles_x, tiles_y)
	textures = W3TextureProvider.new()
	catalog = W3ObjectCatalog.new()
	catalog.load_from_roots(null)
	sampler = W3HeightSampler.new(map)
	pathfinder = W3GridPathfinder.new(sampler)
	terrain.build(map, textures)
	_status = msg
	print("W3MapWorld: ", _status)
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
	_status = "GPU map %dx%d | props ok=%s miss=%s batches=%s | cliffs ok=%s | water=%s%s" % [
		map.tile_count_x() if map else 0,
		map.tile_count_y() if map else 0,
		str(bcounts.get("ok", 0)),
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
