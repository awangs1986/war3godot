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


static func reforged_cache_root_path() -> String:
	if FileAccess.file_exists("res://war3/local_paths.json"):
		var f = FileAccess.open("res://war3/local_paths.json", FileAccess.READ)
		if f:
			var data = JSON.parse_string(f.get_as_text())
			if typeof(data) == TYPE_DICTIONARY and data.has("reforged_cache_root"):
				var configured := str(data["reforged_cache_root"])
				if configured.begins_with("res://") or configured.begins_with("user://"):
					return ProjectSettings.globalize_path(configured).rstrip("/")
				return configured.rstrip("/")
	return ProjectSettings.globalize_path("res://war3/reforged")


static func has_reforged_cache() -> bool:
	return DirAccess.dir_exists_absolute(reforged_cache_root_path())


static func resolve_reforged_path(rel_path: String) -> String:
	if rel_path.is_empty() or not has_reforged_cache():
		return ""
	var norm := rel_path.replace("\\", "/")
	while norm.begins_with("/"):
		norm = norm.substr(1)
	return _resolve_ci_cached(reforged_cache_root_path(), norm)


## Runtime game-data lookup: extracted Reforged assets override the classic
## export, while map-local files are still handled by W3MapArchive first.
static func resolve_game_path(rel_path: String) -> String:
	var hd := resolve_reforged_path(rel_path)
	return hd if not hd.is_empty() else resolve_path(rel_path)


static func resolve_path(rel_path: String) -> String:
	var root = asset_root_path()
	if root.is_empty() or rel_path.is_empty():
		return ""
	var norm = rel_path.replace("\\", "/")
	while norm.begins_with("/"):
		norm = norm.substr(1)
	# Windows reports a differently-cased path as existing. Returning that
	# spelling makes Godot warn and later breaks case-sensitive exports, so walk
	# the directory entries first and preserve their canonical on-disk names.
	if OS.get_name() == "Windows":
		return _resolve_ci_cached(root, norm)
	var full = root.path_join(norm)
	if FileAccess.file_exists(full):
		return full
	# Also try common casing variants without full CI walk.
	var alt = root.path_join(_title_case_segments(norm))
	if alt != full and FileAccess.file_exists(alt):
		return alt
	# Case-insensitive walk (cached)
	return _resolve_ci_cached(root, norm)


static var _ci_cache: Dictionary = {} # root + norm.lower() -> canonical abs path or ""
static var _dir_case_cache: Dictionary = {} # canonical dir.lower() -> { child.lower(): child }


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
	var key := root.replace("\\", "/").to_lower().path_join(norm.to_lower())
	if _ci_cache.has(key):
		return str(_ci_cache[key])
	var parts = norm.split("/")
	var cur = root
	for p in parts:
		if p.is_empty():
			continue
		var dir_key := cur.replace("\\", "/").to_lower()
		if not _dir_case_cache.has(dir_key):
			var entries: Dictionary = {}
			var d = DirAccess.open(cur)
			if d == null:
				_ci_cache[key] = ""
				return ""
			d.list_dir_begin()
			var fn = d.get_next()
			while fn != "":
				if fn != "." and fn != "..":
					var lower := fn.to_lower()
					var previous := str(entries.get(lower, ""))
					if previous.is_empty() or _case_specificity(fn) > _case_specificity(previous):
						entries[lower] = fn
				fn = d.get_next()
			d.list_dir_end()
			_dir_case_cache[dir_key] = entries
		var names: Dictionary = _dir_case_cache[dir_key]
		var match_name := str(names.get(p.to_lower(), ""))
		if match_name.is_empty():
			_ci_cache[key] = ""
			return ""
		cur = cur.path_join(match_name)
	var result = cur if FileAccess.file_exists(cur) else ""
	_ci_cache[key] = result
	return result


static func _case_specificity(value: String) -> int:
	var score := 0
	for i in range(value.length()):
		var ch := value[i]
		if ch >= "A" and ch <= "Z":
			score += 1
	return score


static func clear_resolve_cache() -> void:
	_ci_cache.clear()
	_dir_case_cache.clear()
