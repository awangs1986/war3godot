extends RefCounted
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
## Opens a .w3x/.w3m (via 7z extract) or an already-extracted map folder.
## JASS (war3map.j / war3map.lua) is ignored — visual resources only.


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
		return FileAccess.file_exists(map_dir.path_join("war3map.w3e"))
	if not FileAccess.file_exists(path):
		extract_error = "file not found: %s" % path
		return false
	var ext = path.get_extension().to_lower()
	if ext == "w3e":
		map_dir = path.get_base_dir()
		return true
	if ext == "w3x" or ext == "w3m" or ext == "mpq":
		return _extract_archive(path)
	extract_error = "unsupported map type: %s" % ext
	return false


func _extract_archive(path: String) -> bool:
	var cache_root = "user://w3x_cache"
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(cache_root))
	var abs_cache = ProjectSettings.globalize_path(cache_root)
	var key = path.get_file().get_basename() + "_" + str(path.hash())
	var out_dir = abs_cache.path_join(key)
	if FileAccess.file_exists(out_dir.path_join("war3map.w3e")):
		map_dir = out_dir
		return true
	DirAccess.make_dir_recursive_absolute(out_dir)
	var abs_map = path if path.begins_with("/") else ProjectSettings.globalize_path(path)
	# Prefer project MPQ extractor — .w3x is HM3W+MPQ, not a 7z archive.
	var tool := ProjectSettings.globalize_path("res://tools/mpqtool/extract_map.cjs")
	var output: Array = []
	var err: int = -1
	if FileAccess.file_exists(tool):
		err = OS.execute("node", [tool, abs_map, out_dir], output, true)
		if err == 0 and FileAccess.file_exists(out_dir.path_join("war3map.w3e")):
			map_dir = out_dir
			return true
	# Fallback: 7z (works only for rare zip-wrapped maps).
	output.clear()
	err = OS.execute("7z", ["x", "-y", "-o%s" % out_dir, abs_map], output, true)
	if err != 0:
		output.clear()
		err = OS.execute("7za", ["x", "-y", "-o%s" % out_dir, abs_map], output, true)
	if err != 0:
		extract_error = "map extract failed (%d): %s" % [err, "\n".join(PackedStringArray(output))]
		return false
	if not FileAccess.file_exists(out_dir.path_join("war3map.w3e")):
		var nested = _find_w3e(out_dir)
		if not nested.is_empty():
			map_dir = nested.get_base_dir()
			return true
		extract_error = "war3map.w3e not found after extract"
		return false
	map_dir = out_dir
	return true


func _find_w3e(root: String) -> String:
	var stack: Array[String] = [root]
	while not stack.is_empty():
		var d: String = stack.pop_back()
		var da = DirAccess.open(d)
		if da == null:
			continue
		da.list_dir_begin()
		var fn = da.get_next()
		while fn != "":
			if fn != "." and fn != "..":
				var full = d.path_join(fn)
				if da.current_is_dir():
					stack.append(full)
				elif fn.to_lower() == "war3map.w3e":
					return full
			fn = da.get_next()
		da.list_dir_end()
	return ""


func read_file(rel: String) -> PackedByteArray:
	var norm = rel.replace("\\", "/")
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
	return not read_file(rel).is_empty() or FileAccess.file_exists(map_dir.path_join(rel.replace("\\", "/")))


func resolve_path(rel: String) -> String:
	var norm = rel.replace("\\", "/")
	var full = map_dir.path_join(norm)
	if FileAccess.file_exists(full):
		return full
	var ci = _find_ci(map_dir, norm)
	if not ci.is_empty():
		return ci
	return W3Paths.resolve_path(norm)


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
