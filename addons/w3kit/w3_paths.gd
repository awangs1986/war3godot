extends RefCounted
## Path helpers that work without W3AssetConfig being parsed as a dependency.


static func asset_root_path() -> String:
	var tree = Engine.get_main_loop() as SceneTree
	if tree and tree.root:
		var n = tree.root.get_node_or_null("W3AssetConfig")
		if n != null and not str(n.asset_root).is_empty():
			return str(n.asset_root)
	var cfg = ConfigFile.new()
	if cfg.load("user://w3_asset_config.cfg") == OK:
		var v = str(cfg.get_value("w3", "asset_root", ""))
		if not v.is_empty():
			return v
	# Project extract defaults
	if FileAccess.file_exists("res://war3/local_paths.json"):
		var f = FileAccess.open("res://war3/local_paths.json", FileAccess.READ)
		if f:
			var data = JSON.parse_string(f.get_as_text())
			if typeof(data) == TYPE_DICTIONARY and data.has("asset_root"):
				var p = str(data["asset_root"])
				if p.begins_with("res://") or p.begins_with("user://"):
					return ProjectSettings.globalize_path(p).rstrip("/")
				return p.rstrip("/")
	var fallback = ProjectSettings.globalize_path("res://war3/game")
	if DirAccess.dir_exists_absolute(fallback):
		return fallback
	return ""


static func has_root() -> bool:
	var r = asset_root_path()
	return not r.is_empty() and DirAccess.dir_exists_absolute(r)


static func resolve_path(rel_path: String) -> String:
	var root = asset_root_path()
	if root.is_empty() or rel_path.is_empty():
		return ""
	var norm = rel_path.replace("\\", "/")
	while norm.begins_with("/"):
		norm = norm.substr(1)
	var full = root.path_join(norm)
	if FileAccess.file_exists(full):
		return full
	# Also try common casing variants without full CI walk.
	var alt = root.path_join(_title_case_segments(norm))
	if alt != full and FileAccess.file_exists(alt):
		return alt
	# Case-insensitive walk (cached)
	return _resolve_ci_cached(root, norm)


static var _ci_cache: Dictionary = {} # norm.lower() -> abs path or ""


static func _title_case_segments(norm: String) -> String:
	# units/human/Footman → Units/Human/Footman (best-effort)
	var parts = norm.split("/")
	var out: PackedStringArray = PackedStringArray()
	for i in parts.size():
		var p: String = parts[i]
		if p.is_empty():
			continue
		if i < parts.size() - 1 and p.length() > 0:
			out.append(p.substr(0, 1).to_upper() + p.substr(1))
		else:
			out.append(p)
	return "/".join(out)


static func _resolve_ci_cached(root: String, norm: String) -> String:
	var key = norm.to_lower()
	if _ci_cache.has(key):
		return str(_ci_cache[key])
	var parts = norm.split("/")
	var cur = root
	for p in parts:
		if p.is_empty():
			continue
		var d = DirAccess.open(cur)
		if d == null:
			_ci_cache[key] = ""
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
			_ci_cache[key] = ""
			return ""
		cur = cur.path_join(match_name)
	var result = cur if FileAccess.file_exists(cur) else ""
	_ci_cache[key] = result
	return result


static func clear_resolve_cache() -> void:
	_ci_cache.clear()
