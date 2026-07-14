extends Node
## Autoload: path to unpacked War3 game files + optional default map.

const CONFIG_PATH := "user://w3_asset_config.cfg"
const LOCAL_PATHS := "res://war3/local_paths.json"
const SECTION := "w3"

## Absolute path to unpacked War3 asset root (Units/, TerrainArt/, ...).
var asset_root: String = ""
## Absolute path to .w3x / extracted map directory / war3map.w3e.
var default_map_path: String = ""

signal config_changed


func _ready() -> void:
	load_config()


func load_config() -> void:
	var cfg = ConfigFile.new()
	if cfg.load(CONFIG_PATH) == OK:
		asset_root = _abs(str(cfg.get_value(SECTION, "asset_root", "")))
		default_map_path = _abs(str(cfg.get_value(SECTION, "default_map_path", "")))
	# Project-local defaults from extract (Adrenaline + game dump).
	if asset_root.is_empty() or default_map_path.is_empty():
		_apply_local_paths_json()
	# Final hardcoded fallbacks if JSON missing.
	if asset_root.is_empty():
		var g := _abs("res://war3/game")
		if DirAccess.dir_exists_absolute(g):
			asset_root = g
	if default_map_path.is_empty():
		var m := _abs("res://war3/maps/Adrenaline")
		if DirAccess.dir_exists_absolute(m):
			default_map_path = m


func _apply_local_paths_json() -> void:
	if not FileAccess.file_exists(LOCAL_PATHS):
		return
	var f := FileAccess.open(LOCAL_PATHS, FileAccess.READ)
	if f == null:
		return
	var data = JSON.parse_string(f.get_as_text())
	if typeof(data) != TYPE_DICTIONARY:
		return
	if asset_root.is_empty() and data.has("asset_root"):
		asset_root = _abs(str(data["asset_root"]))
	if default_map_path.is_empty() and data.has("default_map_path"):
		default_map_path = _abs(str(data["default_map_path"]))


func _abs(path: String) -> String:
	if path.is_empty():
		return ""
	if path.begins_with("res://") or path.begins_with("user://"):
		return ProjectSettings.globalize_path(path).rstrip("/")
	return path.rstrip("/")


func save_config() -> void:
	var cfg = ConfigFile.new()
	cfg.set_value(SECTION, "asset_root", asset_root)
	cfg.set_value(SECTION, "default_map_path", default_map_path)
	cfg.save(CONFIG_PATH)
	config_changed.emit()


func set_asset_root(path: String) -> void:
	asset_root = _abs(path)
	save_config()


func set_default_map_path(path: String) -> void:
	default_map_path = _abs(path)
	save_config()


func has_asset_root() -> bool:
	return not asset_root.is_empty() and DirAccess.dir_exists_absolute(asset_root)


func resolve(rel_path: String) -> String:
	if rel_path.is_empty():
		return ""
	var norm = rel_path.replace("\\", "/")
	while norm.begins_with("/"):
		norm = norm.substr(1)
	if not has_asset_root():
		return ""
	var full = asset_root.path_join(norm)
	if FileAccess.file_exists(full):
		return full
	return _find_ci(asset_root, norm)


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
	if FileAccess.file_exists(cur):
		return cur
	return ""


func check_report() -> Dictionary:
	var msgs: PackedStringArray = []
	var ok = true
	if asset_root.is_empty():
		msgs.append("asset_root is empty — set via W3AssetConfig or tools/check_asset_root.")
		ok = false
	elif not DirAccess.dir_exists_absolute(asset_root):
		msgs.append("asset_root does not exist: %s" % asset_root)
		ok = false
	else:
		msgs.append("asset_root OK: %s" % asset_root)
		for sub in ["Units", "TerrainArt", "ReplaceableTextures", "Doodads"]:
			var p = asset_root.path_join(sub)
			if DirAccess.dir_exists_absolute(p):
				msgs.append("  + %s/" % sub)
			else:
				msgs.append("  - missing %s/" % sub)
	if not default_map_path.is_empty():
		if FileAccess.file_exists(default_map_path) or DirAccess.dir_exists_absolute(default_map_path):
			msgs.append("default_map_path OK: %s" % default_map_path)
		else:
			msgs.append("default_map_path missing: %s" % default_map_path)
			ok = false
	else:
		msgs.append("default_map_path unset")
		ok = false
	return {"ok": ok, "messages": msgs}
