extends RefCounted
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
## Opens an already-extracted map folder or a raw war3map.w3e file.
## MPQ archives (.w3x/.w3m) are NOT supported at runtime: extract offline and
## import to a .res level (see war3/README.md). JASS is ignored — visual
## resources only.


var map_dir: String = ""
var source_path: String = ""
var extract_error: String = ""


func open(path: String) -> bool:
	source_path = path
	extract_error = ""
	if path.is_empty():
		extract_error = "empty path"
		return false
	if DirAccess.dir_exists_absolute(path):
		map_dir = path.rstrip("/")
		if FileAccess.file_exists(map_dir.path_join("war3map.w3e")):
			return true
		extract_error = "war3map.w3e not found in folder: %s" % map_dir
		return false
	if not FileAccess.file_exists(path):
		extract_error = "file not found: %s" % path
		return false
	var ext = path.get_extension().to_lower()
	if ext == "w3e":
		map_dir = path.get_base_dir()
		return true
	if ext in ["w3x", "w3m", "mpq"]:
		extract_error = (
			"MPQ archives are not supported at runtime. "
			+ "Import the map to a .res level first (war3/README.md)."
		)
		return false
	extract_error = "unsupported map type: %s" % ext
	return false


func read_file(rel: String) -> PackedByteArray:
	var norm = _safe_relative_path(rel)
	if norm.is_empty():
		return PackedByteArray()
	var full = map_dir.path_join(norm)
	if not FileAccess.file_exists(full):
		# Case-insensitive
		full = _find_ci(map_dir, norm)
	if full.is_empty() or not FileAccess.file_exists(full):
		return PackedByteArray()
	var f = FileAccess.open(full, FileAccess.READ)
	if f == null:
		return PackedByteArray()
	return f.get_buffer(f.get_length())


func has_file(rel: String) -> bool:
	var norm := _safe_relative_path(rel)
	return not norm.is_empty() and (not read_file(norm).is_empty() or FileAccess.file_exists(map_dir.path_join(norm)))


func resolve_path(rel: String) -> String:
	var norm = _safe_relative_path(rel)
	if norm.is_empty():
		return ""
	var full = map_dir.path_join(norm)
	if FileAccess.file_exists(full):
		return full
	var ci = _find_ci(map_dir, norm)
	if not ci.is_empty():
		return ci
	return W3Paths.resolve_game_path(norm)


func _safe_relative_path(rel: String) -> String:
	var norm := rel.replace("\\", "/").strip_edges()
	if norm.is_empty() or norm.begins_with("/") or norm.contains(":"):
		return ""
	var clean_parts: PackedStringArray = PackedStringArray()
	for part in norm.split("/", false):
		if part == "." or part.is_empty():
			continue
		if part == "..":
			return ""
		clean_parts.append(part)
	return "/".join(clean_parts)


func _find_ci(root: String, rel: String) -> String:
	var parts = rel.split("/")
	var cur = root
	for p in parts:
		if p.is_empty():
			continue
		var d = DirAccess.open(cur)
		if d == null:
			return ""
		var match_name = ""
		d.list_dir_begin()
		var fn = d.get_next()
		while fn != "":
			if fn != "." and fn != ".." and fn.to_lower() == p.to_lower():
				match_name = fn
				break
			fn = d.get_next()
		d.list_dir_end()
		if match_name.is_empty():
			return ""
		cur = cur.path_join(match_name)
	return cur if FileAccess.file_exists(cur) or DirAccess.dir_exists_absolute(cur) else ""
