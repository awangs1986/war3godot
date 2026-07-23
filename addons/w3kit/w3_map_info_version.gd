extends RefCounted
## Minimal war3map.w3i header parser used to gate Reforged-only placement fields.
## Field order mirrors Warsmash War3MapW3i and HiveWE MapInfo.

const W3BinaryReader = preload("res://addons/w3kit/w3_binary_reader.gd")


static func parse(data: PackedByteArray) -> Dictionary:
	var result := {
		"format_version": 0,
		"map_version": 0,
		"editor_version": 0,
		"game_version_major": 0,
		"game_version_minor": 0,
		"game_version_patch": 0,
		"game_version_build": 0,
		"valid": false,
	}
	if data.size() < 4:
		return result
	var r = W3BinaryReader.new(data)
	result.format_version = r.read_i32()
	if result.format_version >= 18:
		if r.remaining() < 8:
			return result
		result.map_version = r.read_i32()
		result.editor_version = r.read_i32()
	# W3I v28+ stores the game build. Warsmash uses version > 27;
	# HiveWE uses version >= 28. Both are equivalent.
	if result.format_version >= 28:
		if r.remaining() < 16:
			return result
		result.game_version_major = r.read_i32()
		result.game_version_minor = r.read_i32()
		result.game_version_patch = r.read_i32()
		result.game_version_build = r.read_i32()
	result.valid = not r.failed
	return result


static func has_reforged_skin_ids(info: Dictionary) -> bool:
	return int(info.get("game_version_major", 0)) * 100 + int(info.get("game_version_minor", 0)) >= 132
