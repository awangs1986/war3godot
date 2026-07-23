extends Node3D
## Preview tile debug: grid plus camera-bounded, frustum-tested tile labels.

const W3EMap = preload("res://addons/w3kit/w3e_map.gd")

const LABEL_NEAR := 650.0
@export var grid_y_bias: float = 4.0
@export var label_y_bias: float = 12.0

var _map = null
var _grid_mi: MeshInstance3D
var _labels_root: Node3D
var _highlight_root: Node3D
var _enabled: bool = false
var _hover_tile: Vector2i = Vector2i(-1, -1)
var _focus_tile: Vector2i = Vector2i(-1, -1)
var _label_pool: Dictionary = {}
var _active_keys: Dictionary = {}


func setup(p_map) -> void:
	_map = p_map
	_clear()
	if _map == null:
		return
	_labels_root = Node3D.new()
	_labels_root.name = "TileLabels"
	add_child(_labels_root)
	_highlight_root = Node3D.new()
	_highlight_root.name = "FocusHighlight"
	add_child(_highlight_root)
	visible = _enabled


func set_debug_enabled(on: bool) -> void:
	_enabled = on
	# G is the explicit full-grid toggle. Goto only needs its small focus marker;
	# building the entire map grid synchronously here made the first jump stall.
	if on and _grid_mi == null:
		_build_grid()
	visible = on
	if not on:
		_hide_all_labels()
	elif _focus_tile.x >= 0:
		highlight_tile(_focus_tile)


func is_debug_enabled() -> bool:
	return _enabled


func toggle() -> bool:
	set_debug_enabled(not _enabled)
	return _enabled


func get_hover_tile() -> Vector2i:
	return _hover_tile


func get_focus_tile() -> Vector2i:
	return _focus_tile


## Force-show one tile: thick red border, red coord label, corner tags TL/TR/BL/BR.
func highlight_tile(tile: Vector2i) -> void:
	_focus_tile = tile
	if _highlight_root == null or _map == null:
		return
	for c in _highlight_root.get_children():
		c.queue_free()
	if tile.x < 0 or tile.y < 0:
		return
	if tile.x >= _map.tile_count_x() or tile.y >= _map.tile_count_y():
		return

	var x := tile.x
	var y := tile.y
	var p00: Vector3 = _map.corner_world_pos(x, y)
	var p10: Vector3 = _map.corner_world_pos(x + 1, y)
	var p01: Vector3 = _map.corner_world_pos(x, y + 1)
	var p11: Vector3 = _map.corner_world_pos(x + 1, y + 1)
	var y_top: float = maxf(maxf(p00.y, p10.y), maxf(p01.y, p11.y)) + grid_y_bias + 2.0

	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_LINES)
	var red := Color(1.0, 0.08, 0.08, 1.0)
	var ring: Array = [
		Vector3(p00.x, y_top, p00.z), Vector3(p10.x, y_top, p10.z),
		Vector3(p10.x, y_top, p10.z), Vector3(p11.x, y_top, p11.z),
		Vector3(p11.x, y_top, p11.z), Vector3(p01.x, y_top, p01.z),
		Vector3(p01.x, y_top, p01.z), Vector3(p00.x, y_top, p00.z),
	]
	var mid: Vector3 = (p00 + p11) * 0.5
	for i in range(0, ring.size(), 2):
		var a: Vector3 = ring[i]
		var b: Vector3 = ring[i + 1]
		st.set_color(red)
		st.add_vertex(a)
		st.set_color(red)
		st.add_vertex(b)
		var a2: Vector3 = a.lerp(mid, 0.06)
		var b2: Vector3 = b.lerp(mid, 0.06)
		a2.y = y_top + 1.0
		b2.y = y_top + 1.0
		st.set_color(red)
		st.add_vertex(a2)
		st.set_color(red)
		st.add_vertex(b2)

	var mi := MeshInstance3D.new()
	mi.mesh = st.commit()
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.vertex_color_use_as_albedo = true
	mat.no_depth_test = true
	mat.render_priority = 20
	mi.material_override = mat
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_highlight_root.add_child(mi)

	var lab := Label3D.new()
	lab.text = "%d,%d" % [x, y]
	lab.font_size = 96
	lab.pixel_size = 0.55
	lab.modulate = Color(1.0, 0.05, 0.05, 1.0)
	lab.outline_modulate = Color(0, 0, 0, 1)
	lab.outline_size = 12
	lab.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	lab.no_depth_test = true
	lab.position = Vector3(mid.x, y_top + label_y_bias + 8.0, mid.z)
	_highlight_root.add_child(lab)

	_add_corner_tag("BL", Vector3(p00.x, y_top + 6.0, p00.z), Color(1, 0.35, 0.35))
	_add_corner_tag("BR", Vector3(p10.x, y_top + 6.0, p10.z), Color(1, 0.35, 0.35))
	_add_corner_tag("TL", Vector3(p01.x, y_top + 6.0, p01.z), Color(1, 0.35, 0.35))
	_add_corner_tag("TR", Vector3(p11.x, y_top + 6.0, p11.z), Color(1, 0.35, 0.35))

	visible = true
	_enabled = true


func _add_corner_tag(text: String, pos: Vector3, col: Color) -> void:
	var lab := Label3D.new()
	lab.text = text
	lab.font_size = 64
	lab.pixel_size = 0.35
	lab.modulate = col
	lab.outline_modulate = Color(0, 0, 0, 1)
	lab.outline_size = 8
	lab.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	lab.no_depth_test = true
	lab.position = pos
	_highlight_root.add_child(lab)


func _clear() -> void:
	for c in get_children():
		c.queue_free()
	_grid_mi = null
	_labels_root = null
	_highlight_root = null
	_label_pool.clear()
	_active_keys.clear()


func _corner(x: int, y: int) -> Vector3:
	var p: Vector3 = _map.corner_world_pos(x, y)
	p.y += grid_y_bias
	return p


func _build_grid() -> void:
	var tiles_x: int = _map.tile_count_x()
	var tiles_y: int = _map.tile_count_y()
	var corners_x: int = _map.width
	var corners_y: int = _map.height
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_LINES)
	var col := Color(0.15, 0.95, 0.35, 0.85)
	for y in corners_y:
		for x in tiles_x:
			st.set_color(col)
			st.add_vertex(_corner(x, y))
			st.set_color(col)
			st.add_vertex(_corner(x + 1, y))
	for x in corners_x:
		for y in tiles_y:
			st.set_color(col)
			st.add_vertex(_corner(x, y))
			st.set_color(col)
			st.add_vertex(_corner(x, y + 1))
	var mesh := st.commit()
	_grid_mi = MeshInstance3D.new()
	_grid_mi.name = "TileGridLines"
	_grid_mi.mesh = mesh
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.vertex_color_use_as_albedo = true
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.albedo_color = Color(1, 1, 1, 1)
	mat.no_depth_test = true
	mat.render_priority = 10
	_grid_mi.material_override = mat
	_grid_mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_grid_mi)


func update_hover(camera: Camera3D) -> void:
	_hover_tile = Vector2i(-1, -1)
	if camera == null or _map == null:
		_hide_all_labels()
		return
	_update_hover_tile(camera)
	_update_near_labels(camera)


func _update_hover_tile(camera: Camera3D) -> void:
	var from: Vector3 = camera.global_position
	var to: Vector3 = from + (-camera.global_transform.basis.z) * 20000.0
	var space := get_world_3d().direct_space_state
	var q := PhysicsRayQueryParameters3D.create(from, to)
	q.collide_with_areas = false
	q.collide_with_bodies = true
	var hit := space.intersect_ray(q)
	var world_xz: Vector2
	if hit.has("position"):
		world_xz = Vector2(hit.position.x, hit.position.z)
	else:
		var dir: Vector3 = -camera.global_transform.basis.z
		if absf(dir.y) < 0.001:
			return
		var y_plane: float = camera.global_position.y * 0.5
		var t: float = (y_plane - from.y) / dir.y
		if t < 0.0:
			return
		var p: Vector3 = from + dir * t
		world_xz = Vector2(p.x, p.z)
	var lx: float = (world_xz.x - _map.center_offset.x) / W3EMap.TILE_SIZE
	var ly: float = (world_xz.y - _map.center_offset.y) / W3EMap.TILE_SIZE
	var tx := int(floor(lx))
	var ty := int(floor(ly))
	if tx < 0 or ty < 0 or tx >= _map.tile_count_x() or ty >= _map.tile_count_y():
		return
	_hover_tile = Vector2i(tx, ty)


func _tile_center(tx: int, ty: int) -> Vector3:
	var c00: Vector3 = _map.corner_world_pos(tx, ty)
	var c11: Vector3 = _map.corner_world_pos(tx + 1, ty + 1)
	var mid: Vector3 = (c00 + c11) * 0.5
	mid.y = maxf(c00.y, c11.y) + label_y_bias
	return mid


func _update_near_labels(camera: Camera3D) -> void:
	if _labels_root == null:
		return
	_active_keys.clear()
	var cam_pos: Vector3 = camera.global_position
	var tiles_x: int = _map.tile_count_x()
	var tiles_y: int = _map.tile_count_y()
	var near2: float = LABEL_NEAR * LABEL_NEAR
	var min_tx := int(floor((cam_pos.x - LABEL_NEAR - _map.center_offset.x) / W3EMap.TILE_SIZE)) - 1
	var max_tx := int(floor((cam_pos.x + LABEL_NEAR - _map.center_offset.x) / W3EMap.TILE_SIZE)) + 1
	var min_ty := int(floor((cam_pos.z - LABEL_NEAR - _map.center_offset.y) / W3EMap.TILE_SIZE)) - 1
	var max_ty := int(floor((cam_pos.z + LABEL_NEAR - _map.center_offset.y) / W3EMap.TILE_SIZE)) + 1
	min_tx = clampi(min_tx, 0, tiles_x - 1)
	max_tx = clampi(max_tx, 0, tiles_x - 1)
	min_ty = clampi(min_ty, 0, tiles_y - 1)
	max_ty = clampi(max_ty, 0, tiles_y - 1)

	for ty in range(min_ty, max_ty + 1):
		for tx in range(min_tx, max_tx + 1):
			var mid: Vector3 = _tile_center(tx, ty)
			if cam_pos.distance_squared_to(mid) > near2:
				continue
			if not camera.is_position_in_frustum(mid):
				continue
			var key := "%d,%d" % [tx, ty]
			_active_keys[key] = true
			var lab: Label3D = _label_pool.get(key) as Label3D
			if lab == null or not is_instance_valid(lab):
				lab = _make_tile_label(key)
				_label_pool[key] = lab
			lab.visible = true
			lab.position = mid

	for key in _label_pool.keys():
		if not _active_keys.has(key):
			var lab: Label3D = _label_pool[key] as Label3D
			if lab != null and is_instance_valid(lab):
				lab.visible = false


func _make_tile_label(text: String) -> Label3D:
	var lab := Label3D.new()
	lab.text = text
	lab.font_size = 48
	lab.pixel_size = 0.45
	lab.modulate = Color(1.0, 0.95, 0.2, 1.0)
	lab.outline_modulate = Color(0, 0, 0, 1)
	lab.outline_size = 8
	lab.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	lab.no_depth_test = true
	lab.visible = false
	_labels_root.add_child(lab)
	return lab


func _hide_all_labels() -> void:
	for key in _label_pool.keys():
		var lab: Label3D = _label_pool[key] as Label3D
		if lab != null and is_instance_valid(lab):
			lab.visible = false
	_active_keys.clear()
