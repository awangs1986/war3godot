extends SceneTree

const W3MapInfoVersion = preload("res://addons/w3kit/w3_map_info_version.gd")
const W3DooParser = preload("res://addons/w3kit/w3_doo_parser.gd")
const W3ObjectCatalog = preload("res://addons/w3kit/w3_object_catalog.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")


func _initialize() -> void:
	var info := W3MapInfoVersion.parse(_make_w3i_header(28, 1, 32, 10, 18820))
	_assert(bool(info.valid), "synthetic W3I header rejected")
	_assert(W3MapInfoVersion.has_reforged_skin_ids(info), "1.32 skin gate not enabled")

	var classic_file := FileAccess.open("res://war3/maps/LostTemple/source/war3map.w3i", FileAccess.READ)
	_assert(classic_file != null, "Lost Temple W3I missing")
	var classic := W3MapInfoVersion.parse(classic_file.get_buffer(classic_file.get_length()))
	_assert(int(classic.format_version) == 18, "Lost Temple W3I version changed")
	_assert(not W3MapInfoVersion.has_reforged_skin_ids(classic), "classic map incorrectly enables skin IDs")

	var placements := W3DooParser.parse_doodads(_make_reforged_doodad(), 1, 32)
	_assert(placements.size() == 1, "Reforged doodad record desynchronized")
	_assert(str(placements[0].id) == "hfoo", "base rawcode changed")
	_assert(str(placements[0].skin_id) == "Hjas", "skin rawcode not read after scale")
	_assert(int(placements[0].creation_number) == 1234, "post-skin fields are misaligned")

	var catalog = W3ObjectCatalog.new()
	catalog.load_from_roots(null)
	var footman_rel := catalog.resolve_model("hfoo", true, 0)
	var footman_abs := W3Paths.resolve_game_path(footman_rel).replace("\\", "/")
	_assert(footman_abs.to_lower().contains("/war3/reforged/"), "HD model did not override classic model")
	_assert(footman_abs.to_lower().ends_with("/units/human/footman/footman.mdx"), "wrong Footman HD path")
	var jaina_skin_rel := catalog.resolve_model("Hjas", true, 0).replace("\\", "/")
	_assert(jaina_skin_rel.to_lower().contains("units/human/jainasea/jainasea"), "UnitSkin.txt alternate skin was not merged")

	print("W3_REFORGED_SELECTION_OK w3i=%d game=%d.%d skin=%s model=%s" % [
		int(info.format_version), int(info.game_version_major), int(info.game_version_minor),
		str(placements[0].skin_id), footman_abs,
	])
	quit(0)


func _make_w3i_header(format_version: int, major: int, minor: int, patch: int, build: int) -> PackedByteArray:
	var b := StreamPeerBuffer.new()
	b.big_endian = false
	b.put_32(format_version)
	b.put_32(1) # map saves/version
	b.put_32(6105) # editor version
	b.put_32(major)
	b.put_32(minor)
	b.put_32(patch)
	b.put_32(build)
	return b.data_array


func _make_reforged_doodad() -> PackedByteArray:
	var b := StreamPeerBuffer.new()
	b.big_endian = false
	b.put_data("W3do".to_ascii_buffer())
	b.put_32(7)
	b.put_32(0)
	b.put_32(1)
	b.put_data("hfoo".to_ascii_buffer())
	b.put_32(0)
	for value in [100.0, 200.0, 300.0, 0.5, 1.0, 1.0, 1.0]:
		b.put_float(value)
	b.put_data("Hjas".to_ascii_buffer())
	b.put_u8(2)
	b.put_u8(87)
	b.put_32(1234)
	b.put_32(0) # special doodads
	return b.data_array


func _assert(condition: bool, message: String) -> void:
	if condition:
		return
	push_error("W3_REFORGED_SELECTION_FAIL: %s" % message)
	quit(1)
