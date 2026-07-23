extends Node3D
## DEBUG-ONLY unit pick: click → WC3-style selection circle (Warsmash/open-realm) +
## optional mesh grow outline + nametag with report id.
## Gated by sandbox /debug. Delete this file + sandbox hooks when QA is done.
##
## Refs: Warsmash UberSplat SelectionCircle; open-realm R_RenderSelectedCircle (green).


signal unit_selected(report_id: int, info: Dictionary)
signal unit_deselected

const LABEL_HEIGHT := 210.0
const PICK_MAX_DIST := 9000.0
const CIRCLE_Y_BIAS := 6.0
## open-realm hard-green; Warsmash uses alliance tints — debug sticks to green.
const SELECT_COLOR := Color(0.15, 1.0, 0.25, 0.92)
const OUTLINE_COLOR := Color(0.2, 1.0, 0.35, 1.0)


var _enabled: bool = false
var _batcher: Node = null
var _cam: Camera3D = null
var _selected_id: int = -1
var _selected_node: Node3D = null
var _circle_root: Node3D
var _circle_mi: MeshInstance3D
var _label: Label3D
var _outline_overlays: Array = [] ## Material applied as material_overlay


func setup(batcher: Node, camera: Camera3D) -> void:
	_batcher = batcher
	_cam = camera
	_ensure_visuals()
	_clear_selection()
	visible = _enabled
	set_process_unhandled_input(_enabled)


func set_debug_enabled(on: bool) -> void:
	_enabled = on
	visible = on
	set_process_unhandled_input(on)
	if not on:
		_clear_selection()
	else:
		# Always-on nametags off — only selected unit shows a label.
		if _batcher != null and _batcher.has_method("set_unit_labels_visible"):
			_batcher.set_unit_labels_visible(false)
		_rebuild_if_needed()
		print("[UnitDebugPick] ON — 左键点选单位 | Esc 取消 | /u <编号> 跳转")


func is_debug_enabled() -> bool:
	return _enabled


func get_selected_report_id() -> int:
	return _selected_id


func get_selected_info() -> Dictionary:
	if _selected_id < 0 or _batcher == null:
		return {}
	if _batcher.has_method("get_unit_info"):
		return _batcher.get_unit_info(_selected_id)
	return {}


func select_report_id(report_id: int) -> bool:
	_rebuild_if_needed()
	var target := _find_target(report_id)
	if target.is_empty():
		_clear_selection()
		return false
	_apply_selection(target)
	return true


func _unhandled_input(event: InputEvent) -> void:
	if not _enabled:
		return
	# Yield LMB to Blade Master place/move when that debug mode is on.
	if get_tree().get_first_node_in_group("w3_debug_pawn_control") != null:
		return
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_ESCAPE and _selected_id >= 0:
			_clear_selection()
			get_viewport().set_input_as_handled()
			return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if _try_pick(event.position):
			get_viewport().set_input_as_handled()


func _ensure_visuals() -> void:
	if _circle_root != null:
		return
	_circle_root = Node3D.new()
	_circle_root.name = "SelectCircle"
	add_child(_circle_root)
	_circle_mi = MeshInstance3D.new()
	_circle_mi.mesh = _make_ring_mesh(1.0, 0.12, 48)
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.albedo_color = SELECT_COLOR
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.depth_draw_mode = BaseMaterial3D.DEPTH_DRAW_DISABLED
	mat.no_depth_test = false
	mat.render_priority = 10
	_circle_mi.material_override = mat
	_circle_mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_circle_mi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	_circle_root.add_child(_circle_mi)
	_circle_root.visible = false

	_label = Label3D.new()
	_label.name = "SelectLabel"
	_label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	_label.font_size = 72
	_label.outline_size = 18
	_label.modulate = Color(1.0, 0.98, 0.75, 1.0)
	_label.outline_modulate = Color(0, 0, 0, 0.95)
	_label.pixel_size = 0.5
	_label.no_depth_test = true
	_label.fixed_size = true
	_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_label.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	add_child(_label)
	_label.visible = false


func _make_ring_mesh(outer_r: float, thickness: float, segments: int) -> ArrayMesh:
	var inner_r: float = maxf(outer_r - thickness, outer_r * 0.72)
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for i in segments:
		var a0: float = TAU * float(i) / float(segments)
		var a1: float = TAU * float(i + 1) / float(segments)
		var o0 := Vector3(cos(a0) * outer_r, 0.0, sin(a0) * outer_r)
		var o1 := Vector3(cos(a1) * outer_r, 0.0, sin(a1) * outer_r)
		var i0 := Vector3(cos(a0) * inner_r, 0.0, sin(a0) * inner_r)
		var i1 := Vector3(cos(a1) * inner_r, 0.0, sin(a1) * inner_r)
		st.set_normal(Vector3.UP)
		st.add_vertex(o0)
		st.add_vertex(o1)
		st.add_vertex(i0)
		st.add_vertex(i0)
		st.add_vertex(o1)
		st.add_vertex(i1)
	return st.commit()


func _rebuild_if_needed() -> void:
	if _batcher == null:
		return
	# Ensure registry exists even if labels never shown.
	if _batcher.has_method("ensure_unit_registry"):
		_batcher.ensure_unit_registry()


func _find_target(report_id: int) -> Dictionary:
	for t in _collect_targets():
		if int(t.get("report_id", -1)) == report_id:
			return t
	return {}


func _collect_targets() -> Array:
	var out: Array = []
	if _batcher != null and _batcher.has_method("get_unit_pick_targets"):
		out = _batcher.get_unit_pick_targets()
		if not out.is_empty():
			return out
	# Fallback: registry centers only.
	if _batcher != null and _batcher.has_method("get_unit_registry"):
		var reg: Dictionary = _batcher.get_unit_registry()
		for rid in reg.keys():
			var info: Dictionary = reg[rid]
			out.append({
				"report_id": int(rid),
				"center": info.get("center", Vector3.ZERO),
				"node": null,
				"radius": 140.0,
				"info": info,
			})
	return out


func _try_pick(screen_pos: Vector2) -> bool:
	if _cam == null:
		_cam = get_viewport().get_camera_3d() if get_viewport() else null
	if _cam == null:
		return false
	_rebuild_if_needed()
	var from: Vector3 = _cam.project_ray_origin(screen_pos)
	var dir: Vector3 = _cam.project_ray_normal(screen_pos)
	var best: Dictionary = {}
	var best_score := INF
	for t in _collect_targets():
		var center: Vector3 = t.get("center", Vector3.ZERO)
		var radius: float = float(t.get("radius", 140.0))
		# Ray–sphere (XZ cylinder-ish): closest approach in 3D with vertical bias.
		var to_c: Vector3 = center + Vector3(0, radius * 0.35, 0) - from
		var t_hit: float = to_c.dot(dir)
		if t_hit < 0.0 or t_hit > PICK_MAX_DIST:
			continue
		var closest: Vector3 = from + dir * t_hit
		var dist: float = closest.distance_to(center + Vector3(0, radius * 0.35, 0))
		var hit_r: float = radius * 1.15
		if dist > hit_r:
			continue
		# Prefer nearer camera hits.
		var score: float = t_hit + dist * 0.25
		if score < best_score:
			best_score = score
			best = t
	if best.is_empty():
		_clear_selection()
		return true
	_apply_selection(best)
	return true


func _apply_selection(target: Dictionary) -> void:
	_clear_outline_only()
	_selected_id = int(target.get("report_id", -1))
	_selected_node = target.get("node") as Node3D
	var center: Vector3 = target.get("center", Vector3.ZERO)
	var radius: float = float(target.get("radius", 140.0))
	var info: Dictionary = target.get("info", {})
	if info.is_empty() and _batcher != null and _batcher.has_method("get_unit_info"):
		info = _batcher.get_unit_info(_selected_id)

	# Footprint circle (open-realm / Warsmash style).
	_circle_root.visible = true
	_circle_root.global_position = Vector3(center.x, center.y + CIRCLE_Y_BIAS, center.z)
	var scale_r: float = maxf(radius, 80.0) * 0.55
	_circle_root.scale = Vector3(scale_r, 1.0, scale_r)

	# Mesh grow outline (debug extra — refs use circle only; grow helps pick readability).
	if _selected_node != null and is_instance_valid(_selected_node):
		_selected_node.visible = true
		_apply_outline(_selected_node)

	# Nametag only for selection.
	var unit_name := str(info.get("unit_name", info.get("unit_id", "?")))
	var unit_id := str(info.get("unit_id", "?"))
	var cn: int = int(info.get("creation_number", 0))
	_label.text = "#%d  %s\n%s · cn=%d" % [_selected_id, unit_name, unit_id, cn]
	_label.global_position = center + Vector3(0.0, LABEL_HEIGHT, 0.0)
	_label.visible = true

	print(
		"[UnitDebugPick] selected #%d  %s  [%s]  cn=%d  pos=(%.0f,%.0f,%.0f)"
		% [_selected_id, unit_name, unit_id, cn, center.x, center.y, center.z]
	)
	unit_selected.emit(_selected_id, info)


func _apply_outline(root: Node3D) -> void:
	var outline := StandardMaterial3D.new()
	outline.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	outline.albedo_color = OUTLINE_COLOR
	outline.cull_mode = BaseMaterial3D.CULL_FRONT
	outline.grow = true
	outline.grow_amount = 4.0
	outline.transparency = BaseMaterial3D.TRANSPARENCY_DISABLED
	for n in root.find_children("*", "GeometryInstance3D", true, false):
		var gi := n as GeometryInstance3D
		if gi == null:
			continue
		gi.material_overlay = outline
		_outline_overlays.append(gi)


func _clear_outline_only() -> void:
	for gi in _outline_overlays:
		if gi != null and is_instance_valid(gi):
			gi.material_overlay = null
	_outline_overlays.clear()


func _clear_selection() -> void:
	var had := _selected_id >= 0
	_clear_outline_only()
	_selected_id = -1
	_selected_node = null
	if _circle_root:
		_circle_root.visible = false
	if _label:
		_label.visible = false
	if had:
		unit_deselected.emit()
