class_name W3LevelImporter
extends RefCounted
## Converts War3 map input into a project-owned binary W3LevelData resource.

const W3MapArchive = preload("res://addons/w3kit/w3_map_archive.gd")
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3LevelData = preload("res://addons/w3kit/w3_level_data.gd")
const W3MapInfoVersion = preload("res://addons/w3kit/w3_map_info_version.gd")

static var last_error: String = ""


static func build(source_path: String):
	last_error = ""
	var archive = W3MapArchive.new()
	if not archive.open(source_path):
		return _fail("cannot open source: %s" % archive.extract_error)
	var w3e_bytes: PackedByteArray = archive.read_file("war3map.w3e")
	if w3e_bytes.is_empty() and source_path.get_extension().to_lower() == "w3e":
		var f := FileAccess.open(source_path, FileAccess.READ)
		if f != null:
			w3e_bytes = f.get_buffer(f.get_length())
	if w3e_bytes.is_empty():
		return _fail("war3map.w3e is missing")
	var map = W3EMap.load_bytes(w3e_bytes)
	if map == null:
		return _fail("W3E parse failed: %s" % W3EMap.last_error)
	var level = W3LevelData.from_w3e(map)
	level.source_path = source_path
	var hashing := HashingContext.new()
	hashing.start(HashingContext.HASH_SHA256)
	hashing.update(w3e_bytes)
	level.source_w3e_sha256 = hashing.finish().hex_encode()
	level.imported_at_utc = Time.get_datetime_string_from_system(true)
	level.map_info_bytes = archive.read_file("war3map.w3i")
	var map_info := W3MapInfoVersion.parse(level.map_info_bytes)
	level.game_version_major = int(map_info.game_version_major)
	level.game_version_minor = int(map_info.game_version_minor)
	level.game_version_patch = int(map_info.game_version_patch)
	level.game_version_build = int(map_info.game_version_build)
	level.shadow_bytes = archive.read_file("war3map.shd")
	level.pathing_bytes = archive.read_file("war3map.wpm")
	level.doodad_bytes = archive.read_file("war3map.doo")
	level.unit_bytes = archive.read_file("war3mapUnits.doo")
	level.unit_override_bytes = archive.read_file("war3map.w3u")
	level.doodad_override_bytes = archive.read_file("war3map.w3d")
	level.destructable_override_bytes = archive.read_file("war3map.w3b")
	return level


static func import_to_file(source_path: String, output_path: String) -> int:
	var level = build(source_path)
	if level == null:
		return ERR_FILE_CORRUPT
	if output_path.get_extension().to_lower() != "res":
		last_error = "output must use binary .res format"
		return ERR_INVALID_PARAMETER
	var abs_output := ProjectSettings.globalize_path(output_path)
	var err := DirAccess.make_dir_recursive_absolute(abs_output.get_base_dir())
	if err != OK and err != ERR_ALREADY_EXISTS:
		last_error = "cannot create output directory: %s" % abs_output.get_base_dir()
		return err
	err = ResourceSaver.save(level, output_path, ResourceSaver.FLAG_COMPRESS)
	if err != OK:
		last_error = "ResourceSaver failed with error %d" % err
	return err


static func _fail(message: String):
	last_error = message
	push_error("W3LevelImporter: %s" % message)
	return null
