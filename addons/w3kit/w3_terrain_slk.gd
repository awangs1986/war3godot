extends RefCounted
## Parses TerrainArt/Terrain.slk → tileID → { dir, file } (HiveWE / WE mapping).


var _by_id: Dictionary = {} # "Ldrt" -> { "dir": "TerrainArt/LordaeronSummer", "file": "Lords_Dirt" }


func load_from_asset_root(asset_root: String) -> void:
	_by_id.clear()
	if asset_root.is_empty():
		return
	var path := asset_root.path_join("TerrainArt/Terrain.slk")
	if not FileAccess.file_exists(path):
		# case-insensitive
		path = _find_ci(asset_root, "TerrainArt/Terrain.slk")
	if path.is_empty() or not FileAccess.file_exists(path):
		push_warning("W3TerrainSlk: Terrain.slk missing under %s" % asset_root)
		return
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return
	_parse(f.get_as_text())
	print("W3TerrainSlk: loaded ", _by_id.size(), " tiles from ", path)


func resolve_blp_rel(tile_id: String) -> String:
	if _by_id.has(tile_id):
		var e: Dictionary = _by_id[tile_id]
		return "%s/%s.blp" % [e["dir"], e["file"]]
	# Heuristic fallback
	return "TerrainArt/%s.blp" % tile_id


func _parse(text: String) -> void:
	var headers: Dictionary = {} # x -> name
	var cur_y := -1
	var row: Dictionary = {}
	var id_x := -1
	var dir_x := -1
	var file_x := -1
	for line in text.split("\n"):
		line = line.strip_edges()
		if not line.begins_with("C;"):
			continue
		var y := _slk_int(line, "Y")
		var x := _slk_int(line, "X")
		var k := _slk_str(line, "K").trim_prefix("\"").trim_suffix("\"")
		if y > 0 and y != cur_y:
			_commit_row(row, id_x, dir_x, file_x)
			cur_y = y
			row = {}
		if x <= 0:
			continue
		if cur_y == 1:
			headers[x] = k
			if k == "tileID":
				id_x = x
			elif k == "dir":
				dir_x = x
			elif k == "file":
				file_x = x
		elif cur_y > 1:
			row[x] = k
	_commit_row(row, id_x, dir_x, file_x)


func _commit_row(row: Dictionary, id_x: int, dir_x: int, file_x: int) -> void:
	if id_x < 0 or not row.has(id_x):
		return
	var tid := str(row[id_x])
	var dir := str(row.get(dir_x, "")).replace("\\", "/")
	var file := str(row.get(file_x, ""))
	if tid.is_empty() or dir.is_empty() or file.is_empty():
		return
	_by_id[tid] = {"dir": dir, "file": file}


func _slk_int(line: String, key: String) -> int:
	# SLK fields look like ;X14; or ;Y2; — letter immediately followed by digits.
	var start := 0
	while true:
		var i := line.find(key, start)
		if i < 0:
			return -1
		if i > 0 and line[i - 1] != ";":
			start = i + 1
			continue
		var j := i + key.length()
		if j >= line.length() or line[j] < "0" or line[j] > "9":
			start = i + 1
			continue
		var k := j
		while k < line.length() and line[k] >= "0" and line[k] <= "9":
			k += 1
		return int(line.substr(j, k - j))
	return -1


func _slk_str(line: String, key: String) -> String:
	# SLK uses K"text" or K123 — no semicolon after the letter.
	var start := 0
	while true:
		var i := line.find(key, start)
		if i < 0:
			return ""
		var after := i + key.length()
		if after >= line.length():
			return ""
		var ch := line[after]
		if i > 0 and line[i - 1] == ";" and (ch == "\"" or ch == "-" or (ch >= "0" and ch <= "9")):
			return line.substr(after).strip_edges()
		start = i + 1
	return ""


func _find_ci(root: String, rel: String) -> String:
	var parts := rel.split("/")
	var cur := root
	for p in parts:
		if p.is_empty():
			continue
		var d := DirAccess.open(cur)
		if d == null:
			return ""
		var match_name := ""
		d.list_dir_begin()
		var fn := d.get_next()
		while fn != "":
			if fn != "." and fn != ".." and fn.to_lower() == p.to_lower():
				match_name = fn
				break
			fn = d.get_next()
		d.list_dir_end()
		if match_name.is_empty():
			return ""
		cur = cur.path_join(match_name)
	return cur
