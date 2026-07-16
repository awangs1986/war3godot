extends SceneTree
## Deterministic, asset-light regression tests for parsers, pathfinding and import.

var _failures: int = 0


class OpenGridSampler:
	var map = {}

	func is_tile_walkable(tile: Vector2i) -> bool:
		return tile.x >= 0 and tile.y >= 0 and tile.x < 16 and tile.y < 16

	func can_step(from_tile: Vector2i, to_tile: Vector2i) -> bool:
		return is_tile_walkable(from_tile) and is_tile_walkable(to_tile)


func _initialize() -> void:
	_run()


func _run() -> void:
	var W3EMap = preload("res://addons/w3kit/w3e_map.gd")
	var W3GridPathfinder = preload("res://addons/w3kit/w3_grid_pathfinder.gd")
	var W3LevelData = preload("res://addons/w3kit/w3_level_data.gd")
	var W3LevelImporter = preload("res://addons/w3kit/w3_level_importer.gd")
	var W3MapWorld = preload("res://addons/w3kit/w3_map_world.gd")
	var W3BlpDecoder = preload("res://addons/w3kit/w3_blp_decoder.gd")
	var W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")

	var reader = preload("res://addons/w3kit/w3_binary_reader.gd").new(PackedByteArray([1, 2]))
	_expect(reader.read_u32() == 0 and reader.failed, "binary reader rejects truncated scalar")
	_expect(not W3BlpDecoder._valid_dimensions(100000, 100000), "BLP rejects oversized image allocation")
	_expect(W3MdxModel._bounded_count(1000, 4, 12, 2000000) == 0, "MDX rejects impossible element count")
	var strict_world = W3MapWorld.new()
	_expect(not strict_world.allow_procedural_fallback, "runtime map loading is strict by default")
	strict_world.free()

	_expect(W3EMap.load_bytes(PackedByteArray()) == null, "W3E rejects empty input")
	var valid = W3EMap.make_test_map(8, 6)
	var encoded: PackedByteArray = W3EMap.save_bytes(valid)
	var decoded = W3EMap.load_bytes(encoded)
	_expect(decoded != null, "W3E roundtrip parses")
	_expect(decoded != null and decoded.width == 9 and decoded.height == 7, "W3E dimensions roundtrip")

	var truncated := encoded.slice(0, encoded.size() - 1)
	_expect(W3EMap.load_bytes(truncated) == null, "W3E rejects truncated corner grid")

	var pf = W3GridPathfinder.new(OpenGridSampler.new())
	_expect(pf._h(Vector2i.ZERO, Vector2i(1, 1)) == 14, "A* octile heuristic matches diagonal cost")
	var route: Array[Vector2i] = pf.find_path(Vector2i.ZERO, Vector2i(3, 2))
	_expect(route.size() == 4 and route[0] == Vector2i.ZERO and route[-1] == Vector2i(3, 2), "A* returns optimal open-grid route")

	var level = W3LevelData.from_w3e(decoded)
	_expect(level.is_valid(), "W3LevelData accepts converted map")
	_expect(level.get_corner(3, 2).ground_height == decoded.get_corner(3, 2).ground_height, "W3LevelData preserves corners")

	var sample_dir := ProjectSettings.globalize_path("res://testdata/hivewe_sample")
	var output := "user://w3_pipeline_regression.res"
	var err: int = W3LevelImporter.import_to_file(sample_dir, output)
	_expect(err == OK, "importer writes binary LevelData")
	var imported = ResourceLoader.load(output, "", ResourceLoader.CACHE_MODE_IGNORE)
	_expect(imported != null and imported.has_method("is_valid") and imported.is_valid(), "imported LevelData reloads")
	_expect(imported != null and imported.source_w3e_sha256.length() == 64, "import stores source fingerprint")
	_expect(imported != null and not imported.doodad_bytes.is_empty(), "import embeds doodad data")

	if _failures > 0:
		printerr("REGRESSION_FAILED: %d failure(s)" % _failures)
		quit(1)
		return
	print("REGRESSION_OK")
	quit(0)


func _expect(condition: bool, label: String) -> void:
	if condition:
		print("PASS: ", label)
		return
	_failures += 1
	printerr("FAIL: ", label)
