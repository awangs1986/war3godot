extends Node3D
## HiveWE-ish MDX batching: static MultiMesh + animated W3MdxInstance for units/wind.
## Streams unique MDX loads; no shadows. Doodads + units from DOO files.
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")


signal streaming_finished

const MAX_STATIC := 20000
const BATCH_LOAD_PER_FRAME := 10
const ANIM_SPAWN_PER_FRAME := 4
## Proximity-gated instances (units + nearby-capable doodads). Only near camera actually skin.
const MAX_ANIMATED_DOODADS := 180
const ANIM_NEAR_DIST := 2200.0
const ANIM_FAR_DIST := 2800.0 ## hysteresis — stay active a bit farther
const ANIM_DRAW_DIST := 4200.0 ## hide bind-pose instances beyond this
const MAX_NEAR_ANIMS := 48
const PROXIMITY_CHECK_SEC := 0.2
## Static MultiMesh: frustum + distance pack (was written but never called).
const PROP_DRAW_DIST := 5200.0
const FRUSTUM_CHECK_SEC := 0.15
## Unit nametag draw distance (report ids stay useful near camera).
const LABEL_DRAW_DIST := 4800.0
const LABEL_HEIGHT := 200.0

var textures
var catalog
var archive
var sampler

var _queue: Array = []
var _model_jobs: Array = []
var _model_cache: Dictionary = {}
var _mesh_cache: Dictionary = {}
var _batches: Dictionary = {}
var _anim_pending: Array = []
var _anim_instances: Array = [] ## W3MdxInstance refs
var _ok: int = 0
var _miss: int = 0
var _fail: int = 0
var _animated: int = 0
var _anim_active_n: int = 0
var _flushed: bool = false
var _jobs_total: int = 0
var _anim_root: Node3D
var _prox_timer: float = 0.0
var _frustum_timer: float = 0.0
var _proximity_on: bool = false
var _cull_on: bool = false
var _next_unit_report_id: int = 1
var _unit_labels: Array = [] ## { "marker": Node3D, "label": Label3D, "center": Vector3, "info": Dictionary }
var _unit_label_root: Node3D
var _unit_labels_visible: bool = false ## default off — use /debug click pick for nametags
var _unit_registry: Dictionary = {} # report_id int -> info Dictionary


func setup(p_textures, p_catalog, p_archive, p_sampler) -> void:
	textures = p_textures
	catalog = p_catalog
	archive = p_archive
	sampler = p_sampler


func clear() -> void:
	_queue.clear()
	_model_jobs.clear()
	_batches.clear()
	_mesh_cache.clear()
	_anim_pending.clear()
	_anim_instances.clear()
	_ok = 0
	_miss = 0
	_fail = 0
	_animated = 0
	_anim_active_n = 0
	_flushed = false
	_jobs_total = 0
	_anim_root = null
	_prox_timer = 0.0
	_frustum_timer = 0.0
	_proximity_on = false
	_cull_on = false
	_next_unit_report_id = 1
	_unit_labels.clear()
	_unit_registry.clear()
	_unit_label_root = null
	for c in get_children():
		c.queue_free()
	set_process(false)


func get_counts() -> Dictionary:
	return {
		"ok": _ok,
		"miss": _miss,
		"fail": _fail,
		"animated": _animated,
		"anim_active": _anim_active_n,
		"anim_pending": _anim_pending.size(),
		"queued": _queue.size(),
		"jobs": _model_jobs.size(),
		"batches": _batches.size(),
	}


func is_streaming_complete() -> bool:
	## True once static Multimesh is up (scene can show). Anim units may still trickle in.
	return _flushed


func get_stream_progress() -> float:
	if _flushed:
		return 1.0
	if _jobs_total <= 0:
		return 0.0
	var done := _jobs_total - _model_jobs.size()
	return clampf(float(done) / float(_jobs_total) * 0.92, 0.0, 0.92)


func enqueue_placement(p) -> void:
	if p == null:
		return
	if p.id in ["sloc", "ncam", "Start"]:
		return
	if _queue.size() >= MAX_STATIC:
		_miss += 1
		return
	var model_rel: String = ""
	if catalog != null:
		model_rel = catalog.resolve_model(p.id, p.is_unit, p.variation)
	if model_rel.is_empty():
		model_rel = str(p.model_path)
	var abs_path := ""
	if archive != null and not model_rel.is_empty():
		abs_path = archive.resolve_path(model_rel)
		if abs_path.is_empty():
			abs_path = W3Paths.resolve_path(model_rel)
	if abs_path.is_empty():
		_miss += 1
		return
	var replaceable := ""
	if catalog != null and not p.is_unit:
		replaceable = catalog.destructable_tex_file(p.id)
	var pos: Vector3 = p.position
	if sampler:
		var ground_y: float = sampler.sample_y(pos.x, pos.z)
		if pos.y < ground_y + 8.0:
			pos.y = ground_y
	var basis := Basis(Vector3.UP, -p.angle)
	var scl: Vector3 = p.scale if p.scale.length() > 0.01 else Vector3.ONE
	basis = basis.scaled(scl)
	var xform := Transform3D(basis, pos)
	var is_unit: bool = bool(p.is_unit)
	var report_id := 0
	var unit_name := ""
	var unit_id := str(p.id)
	var creation_number: int = int(p.creation_number)
	if is_unit:
		report_id = _next_unit_report_id
		_next_unit_report_id += 1
		if catalog != null and catalog.has_method("resolve_unit_name"):
			unit_name = str(catalog.resolve_unit_name(unit_id))
		else:
			unit_name = unit_id
	_queue.append({
		"abs_path": abs_path,
		"replaceable": replaceable,
		"xform": xform,
		"center": pos,
		"radius": 200.0,
		"is_unit": is_unit,
		"unit_id": unit_id,
		"unit_name": unit_name,
		"report_id": report_id,
		"creation_number": creation_number,
		"player_owner": int(p.player_owner),
	})
	if not _model_cache.has(abs_path) and not _model_jobs.has(abs_path):
		_model_jobs.append(abs_path)


func begin_streaming() -> void:
	_jobs_total = _model_jobs.size()
	if _queue.is_empty() and _model_jobs.is_empty():
		_flushed = true
		streaming_finished.emit()
		return
	_flushed = false
	set_process(true)


func _process(delta: float) -> void:
	# 1) Stream unique MDX files.
	if not _model_jobs.is_empty():
		var loaded := 0
		while loaded < BATCH_LOAD_PER_FRAME and not _model_jobs.is_empty():
			_ensure_model(str(_model_jobs.pop_front()))
			loaded += 1
		return

	# 2) One-shot: static Multimesh + queue animated (do NOT spawn all now).
	if not _queue.is_empty():
		_flush_static_and_queue_anims()
		_flushed = true
		print(
			"W3MdxBatcher: static ready ok=%d anim_queued=%d miss=%d batches=%d"
			% [_ok, _anim_pending.size(), _miss, _batches.size()]
		)
		streaming_finished.emit()
		_proximity_on = true
		_cull_on = not _batches.is_empty()
		_update_frustum_visibility()
		_print_unit_registry_summary()
		_update_unit_label_visibility()
		if _anim_pending.is_empty() and _anim_instances.is_empty() and not _cull_on and _unit_labels.is_empty():
			set_process(false)
			_proximity_on = false
		return

	# 3) Spawn a few proximity-gated instances per frame.
	if not _anim_pending.is_empty():
		var n := 0
		while n < ANIM_SPAWN_PER_FRAME and not _anim_pending.is_empty():
			var item: Dictionary = _anim_pending.pop_front()
			var model = _model_cache.get(item["abs_path"], null)
			if model != null and not model.geosets.is_empty():
				_spawn_animated(item, model)
			n += 1
		if _anim_pending.is_empty():
			print(
				"W3MdxBatcher: anim instances=%d (proximity LOD near≤%d draw≤%.0f)"
				% [_animated, MAX_NEAR_ANIMS, ANIM_DRAW_DIST]
			)
		# fall through to proximity / cull ticks

	# 4) Camera proximity: anim skin LOD + unit nametag distance.
	if _proximity_on or not _unit_labels.is_empty():
		_prox_timer += delta
		if _prox_timer >= PROXIMITY_CHECK_SEC:
			_prox_timer = 0.0
			if _proximity_on and not _anim_instances.is_empty():
				_update_anim_proximity()
			_update_unit_label_visibility()

	# 5) Static MultiMesh: pack visible+near instances (frustum + distance).
	if _cull_on and not _batches.is_empty():
		_frustum_timer += delta
		if _frustum_timer >= FRUSTUM_CHECK_SEC:
			_frustum_timer = 0.0
			_update_frustum_visibility()

	if (
		_anim_pending.is_empty()
		and _anim_instances.is_empty()
		and not _cull_on
		and _unit_labels.is_empty()
	):
		set_process(false)
		_proximity_on = false


func _ensure_model(abs_path: String) -> void:
	if _model_cache.has(abs_path):
		return
	var model = W3MdxModel.load_file(abs_path)
	_model_cache[abs_path] = model
	if model == null or model.geosets.is_empty():
		_fail += 1


func _ensure_anim_root() -> Node3D:
	if _anim_root != null and is_instance_valid(_anim_root):
		return _anim_root
	_anim_root = Node3D.new()
	_anim_root.name = "Animated"
	add_child(_anim_root)
	return _anim_root


func _model_can_animate(model) -> bool:
	return model != null and not model.bones.is_empty() and not model.sequences.is_empty()


func _spawn_animated(item: Dictionary, model) -> void:
	var root := _ensure_anim_root()
	var inst: Node3D = W3MdxInstance.new()
	root.add_child(inst)
	inst.transform = item["xform"]
	inst.setup(
		model,
		textures,
		str(item["abs_path"]),
		{
			"animate": true,
			"proximity_gate": true,
			"replaceable_texture": str(item.get("replaceable", "")),
			"team_color": int(item.get("player_owner", 0)),
		}
	)
	# Hidden until proximity pass brings it into draw range.
	inst.visible = false
	if int(item.get("report_id", 0)) > 0:
		inst.set_meta("report_id", int(item["report_id"]))
		inst.name = "Unit_%d_%s" % [int(item["report_id"]), str(item.get("unit_id", ""))]
		_bind_registry_node(int(item["report_id"]), inst)
	_anim_instances.append(inst)
	_animated += 1
	_ok += 1


func _ensure_unit_label_root() -> Node3D:
	if _unit_label_root != null and is_instance_valid(_unit_label_root):
		return _unit_label_root
	_unit_label_root = Node3D.new()
	_unit_label_root.name = "UnitLabels"
	add_child(_unit_label_root)
	return _unit_label_root


func _spawn_unit_label(item: Dictionary) -> void:
	_register_unit(item)
	if not _unit_labels_visible:
		return
	_spawn_unit_label_visual(item)


func _register_unit(item: Dictionary) -> void:
	var rid: int = int(item.get("report_id", 0))
	if rid <= 0:
		return
	var unit_id := str(item.get("unit_id", "?"))
	var unit_name := str(item.get("unit_name", unit_id))
	var cn: int = int(item.get("creation_number", 0))
	var center: Vector3 = item.get("center", Vector3.ZERO)
	var info := {
		"report_id": rid,
		"unit_id": unit_id,
		"unit_name": unit_name,
		"creation_number": cn,
		"player_owner": int(item.get("player_owner", 0)),
		"center": center,
		"abs_path": str(item.get("abs_path", "")),
		"radius": float(item.get("radius", 140.0)),
		"node": null,
	}
	if _unit_registry.has(rid):
		var prev: Dictionary = _unit_registry[rid]
		info["node"] = prev.get("node")
	_unit_registry[rid] = info


func _bind_registry_node(report_id: int, node: Node3D) -> void:
	if not _unit_registry.has(report_id):
		return
	var info: Dictionary = _unit_registry[report_id]
	info["node"] = node
	if node != null and is_instance_valid(node):
		info["center"] = node.global_position
	_unit_registry[report_id] = info


func _spawn_unit_label_visual(item: Dictionary) -> void:
	var rid: int = int(item.get("report_id", 0))
	if rid <= 0:
		return
	var info: Dictionary = _unit_registry.get(rid, {})
	var unit_id := str(info.get("unit_id", item.get("unit_id", "?")))
	var unit_name := str(info.get("unit_name", unit_id))
	var cn: int = int(info.get("creation_number", 0))
	var center: Vector3 = info.get("center", item.get("center", Vector3.ZERO))

	var root := _ensure_unit_label_root()
	var marker := Node3D.new()
	marker.name = "Label_%d" % rid
	marker.position = center + Vector3(0.0, LABEL_HEIGHT, 0.0)
	root.add_child(marker)

	var label := Label3D.new()
	label.name = "Text"
	label.text = "#%d  %s\n%s · cn=%d" % [rid, unit_name, unit_id, cn]
	label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	label.font_size = 64
	label.outline_size = 16
	label.modulate = Color(1.0, 0.96, 0.78, 1.0)
	label.outline_modulate = Color(0.0, 0.0, 0.0, 0.92)
	label.pixel_size = 0.55
	label.no_depth_test = true
	label.fixed_size = true
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	marker.add_child(label)

	_unit_labels.append({
		"marker": marker,
		"label": label,
		"center": center,
		"info": info,
	})


func ensure_unit_registry() -> void:
	## No-op if already filled during flush; kept for debug pick API.
	pass


func get_unit_pick_targets() -> Array:
	var out: Array = []
	for rid in _unit_registry.keys():
		var info: Dictionary = _unit_registry[rid]
		var center: Vector3 = info.get("center", Vector3.ZERO)
		var node = info.get("node")
		if node != null and is_instance_valid(node):
			center = (node as Node3D).global_position
			info["center"] = center
			_unit_registry[rid] = info
		out.append({
			"report_id": int(rid),
			"center": center,
			"node": node if (node != null and is_instance_valid(node)) else null,
			"radius": float(info.get("radius", 140.0)),
			"info": info.duplicate(),
		})
	out.sort_custom(func(a, b): return int(a["report_id"]) < int(b["report_id"]))
	return out


func _update_unit_label_visibility() -> void:
	if _unit_labels.is_empty():
		return
	var cam := get_viewport().get_camera_3d() if get_viewport() else null
	if cam == null:
		return
	var cam_pos: Vector3 = cam.global_position
	var draw2: float = LABEL_DRAW_DIST * LABEL_DRAW_DIST
	for entry in _unit_labels:
		var marker: Node3D = entry.get("marker")
		if marker == null or not is_instance_valid(marker):
			continue
		var center: Vector3 = entry.get("center", marker.global_position)
		var near_enough: bool = cam_pos.distance_squared_to(center) <= draw2
		marker.visible = _unit_labels_visible and near_enough


func set_unit_labels_visible(on: bool) -> void:
	_unit_labels_visible = on
	if on and _unit_labels.is_empty() and not _unit_registry.is_empty():
		for rid in _unit_registry.keys():
			var info: Dictionary = _unit_registry[rid]
			_spawn_unit_label_visual({
				"report_id": rid,
				"unit_id": info.get("unit_id", ""),
				"unit_name": info.get("unit_name", ""),
				"creation_number": info.get("creation_number", 0),
				"center": info.get("center", Vector3.ZERO),
			})
	if not on:
		for entry in _unit_labels:
			var marker = entry.get("marker")
			if marker != null and is_instance_valid(marker):
				marker.queue_free()
		_unit_labels.clear()
	else:
		_update_unit_label_visibility()
	print("W3MdxBatcher: unit labels=", "ON" if on else "OFF", " registry=", _unit_registry.size())


func are_unit_labels_visible() -> bool:
	return _unit_labels_visible


func get_unit_info(report_id: int) -> Dictionary:
	return _unit_registry.get(report_id, {})


func get_unit_registry() -> Dictionary:
	return _unit_registry.duplicate(true)


func find_unit_center(report_id: int) -> Vector3:
	var info: Dictionary = _unit_registry.get(report_id, {})
	if info.is_empty():
		return Vector3.INF
	return info.get("center", Vector3.INF)


func _print_unit_registry_summary() -> void:
	if _unit_registry.is_empty():
		return
	var ids: Array = _unit_registry.keys()
	ids.sort()
	print("W3MdxBatcher: unit report ids 1..%d (N toggles labels)" % ids.size())
	var shown := 0
	for rid in ids:
		if shown >= 12:
			print("  … +%d more (console: /u <编号>)" % (ids.size() - shown))
			break
		var info: Dictionary = _unit_registry[rid]
		print(
			"  #%d  %s  [%s]  cn=%d"
			% [rid, info.get("unit_name", ""), info.get("unit_id", ""), info.get("creation_number", 0)]
		)
		shown += 1


func _update_anim_proximity() -> void:
	var cam := get_viewport().get_camera_3d() if get_viewport() else null
	if cam == null:
		return
	var cam_pos: Vector3 = cam.global_position
	var near2: float = ANIM_NEAR_DIST * ANIM_NEAR_DIST
	var far2: float = ANIM_FAR_DIST * ANIM_FAR_DIST
	var draw2: float = ANIM_DRAW_DIST * ANIM_DRAW_DIST
	var scored: Array = []
	var alive: Array = []
	for inst in _anim_instances:
		if inst == null or not is_instance_valid(inst):
			continue
		alive.append(inst)
		var d2: float = inst.global_position.distance_squared_to(cam_pos)
		scored.append({"d2": d2, "inst": inst})
	_anim_instances = alive
	scored.sort_custom(func(a, b): return float(a["d2"]) < float(b["d2"]))

	_anim_active_n = 0
	var activated := 0
	for entry in scored:
		var inst = entry["inst"]
		var d2: float = float(entry["d2"])
		var want_draw: bool = d2 <= draw2
		inst.visible = want_draw
		if not want_draw:
			if inst.has_method("set_anim_active"):
				inst.set_anim_active(false)
			continue
		var was_on: bool = inst.is_anim_active() if inst.has_method("is_anim_active") else false
		var want_on := false
		if activated < MAX_NEAR_ANIMS:
			if was_on:
				want_on = d2 <= far2
			else:
				want_on = d2 <= near2
		if want_on:
			activated += 1
		if inst.has_method("set_anim_active"):
			inst.set_anim_active(want_on)
		if want_on:
			_anim_active_n += 1


func _flush_static_and_queue_anims() -> void:
	var by_gkey: Dictionary = {}
	var doodad_anim_left: int = MAX_ANIMATED_DOODADS
	var ordered: Array = []
	var doodads: Array = []
	for item in _queue:
		if bool(item.get("is_unit", false)):
			ordered.append(item)
		else:
			doodads.append(item)
	ordered.append_array(doodads)

	for item in ordered:
		var model = _model_cache.get(item["abs_path"], null)
		if model == null or model.geosets.is_empty():
			continue
		var is_unit: bool = bool(item.get("is_unit", false))
		var want_anim := false
		if is_unit and _model_can_animate(model):
			want_anim = true
		elif (not is_unit) and doodad_anim_left > 0 and _model_can_animate(model):
			want_anim = true
			doodad_anim_left -= 1
		if want_anim:
			_anim_pending.append(item)
			if is_unit:
				_spawn_unit_label(item)
			continue
		var bank_key: String = "%s|%s" % [item["abs_path"], item["replaceable"]]
		if not _mesh_cache.has(bank_key):
			_mesh_cache[bank_key] = _build_geoset_meshes(model, str(item["replaceable"]))
		var geos: Array = _mesh_cache[bank_key]
		_ok += 1
		if is_unit:
			_spawn_unit_label(item)
		for gi in geos.size():
			var gkey: String = "%s#%d" % [bank_key, gi]
			if not by_gkey.has(gkey):
				by_gkey[gkey] = {"geo": geos[gi], "items": []}
			by_gkey[gkey]["items"].append(item)
	_queue.clear()

	for gkey in by_gkey.keys():
		var pack: Dictionary = by_gkey[gkey]
		var geo: Dictionary = pack["geo"]
		var items: Array = pack["items"]
		if items.is_empty():
			continue
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.mesh = geo["mesh"]
		mm.instance_count = items.size()
		var centers := PackedVector3Array()
		var radii := PackedFloat32Array()
		var xforms: Array = []
		centers.resize(items.size())
		radii.resize(items.size())
		for i in items.size():
			var it: Dictionary = items[i]
			var xf: Transform3D = it["xform"]
			mm.set_instance_transform(i, xf)
			centers[i] = it["center"]
			radii[i] = float(it["radius"])
			xforms.append(xf)
		var mmi := MultiMeshInstance3D.new()
		mmi.multimesh = mm
		mmi.material_override = geo["mat"]
		mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		mmi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
		var aabb := AABB(centers[0], Vector3.ZERO)
		for i in centers.size():
			aabb = aabb.expand(centers[i])
		# Tight AABB + small margin so Godot can cull whole batch when off-screen.
		mmi.custom_aabb = aabb.grow(220.0)
		mmi.extra_cull_margin = 128.0
		mmi.visibility_range_end = PROP_DRAW_DIST + 800.0
		mmi.visibility_range_end_margin = 400.0
		add_child(mmi)
		_batches[gkey] = {
			"mm": mm,
			"mmi": mmi,
			"centers": centers,
			"radii": radii,
			"xforms": xforms,
		}


func _build_geoset_meshes(model, replaceable_tex: String) -> Array:
	var out: Array = []
	for gi in model.geosets.size():
		var g = model.geosets[gi]
		var mesh := _commit_geoset(g)
		var layers: Array = _layers_for_geoset(model, g)
		for li in layers.size():
			var layer = layers[li]
			var mat := _make_material(model, g, replaceable_tex, layer)
			mat.render_priority = li
			out.append({"mesh": mesh, "mat": mat, "geoset_id": gi, "layer_index": li})
	return out


func _layers_for_geoset(model, g) -> Array:
	if model == null or model.materials.is_empty():
		return [null]
	var mid = clampi(g.material_id, 0, model.materials.size() - 1)
	var layers: Array = model.materials[mid]
	if layers.is_empty():
		return [null]
	return layers


func _commit_geoset(g) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var verts: PackedVector3Array = g.vertices
	var norms: PackedVector3Array = g.normals
	var uvs: PackedVector2Array = g.uvs
	var nvert := verts.size()
	for fi in range(0, g.faces.size() - 2, 3):
		for k in 3:
			var idx: int = g.faces[fi + k]
			if idx < 0 or idx >= nvert:
				continue
			st.set_normal(norms[idx] if idx < norms.size() else Vector3.UP)
			if idx < uvs.size():
				st.set_uv(uvs[idx])
			st.add_vertex(verts[idx])
	return st.commit()


func _make_material(model, g, replaceable_tex: String, layer = null) -> StandardMaterial3D:
	var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
	var mat := StandardMaterial3D.new()
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
	W3War3Lit.style_model_material(mat)
	var layer_fm := 0
	var shading_flags := 0
	var tex_path := ""
	if layer != null:
		layer_fm = int(layer.filter_mode)
		shading_flags = int(layer.shading_flags)
		tex_path = _resolve_tex_slot(model, int(layer.texture_id), replaceable_tex)
		mat.albedo_color.a = clampf(float(layer.alpha), 0.0, 1.0)
	else:
		tex_path = _resolve_tex(model, g, replaceable_tex)
		if not model.materials.is_empty():
			var mid = clampi(g.material_id, 0, model.materials.size() - 1)
			var layers: Array = model.materials[mid]
			if not layers.is_empty():
				layer_fm = int(layers[0].filter_mode)
				shading_flags = int(layers[0].shading_flags)
	if (shading_flags & 0x1) != 0:
		mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	var path_l := tex_path.to_lower()
	var looks_cutout := (
		path_l.contains("tree") or path_l.contains("leaf") or path_l.contains("canopy") or path_l.contains("plant")
	)
	var looks_add := (
		path_l.contains("glow") or path_l.contains("fire") or path_l.contains("torch") or path_l.contains("lensflare")
	)
	if not tex_path.is_empty() and textures != null:
		mat.albedo_texture = textures.get_texture(tex_path)
		var tex_has_alpha := _texture_has_alpha(mat.albedo_texture)
		# filter_mode 0 + BLP alpha still needs cutout (unit skins), else black mats.
		if layer_fm == 1 or looks_cutout or (layer_fm == 0 and tex_has_alpha):
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
			mat.alpha_scissor_threshold = 0.75 if layer_fm == 1 else 0.05
		elif layer_fm == 2:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		elif layer_fm == 3 or layer_fm == 4 or looks_add:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
			mat.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
			mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		elif layer_fm == 5 or layer_fm == 6:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
			mat.blend_mode = BaseMaterial3D.BLEND_MODE_MUL
	else:
		mat.albedo_color = Color(0.45, 0.55, 0.35)
	return mat


func _texture_has_alpha(tex: Texture2D) -> bool:
	if tex == null:
		return false
	var img: Image = tex.get_image()
	if img == null:
		return false
	return preload("res://addons/w3kit/w3_blp_decoder.gd")._image_has_transparency(img)


func _resolve_tex(model, g, replaceable_tex: String) -> String:
	if model.materials.is_empty():
		return ""
	var mid = clampi(g.material_id, 0, model.materials.size() - 1)
	var layers: Array = model.materials[mid]
	if layers.is_empty():
		return ""
	return _resolve_tex_slot(model, int(layers[0].texture_id), replaceable_tex)


func _resolve_tex_slot(model, tid: int, replaceable_tex: String) -> String:
	if tid < 0 or tid >= model.textures.size():
		return ""
	var path: String = model.textures[tid]
	if not path.is_empty():
		return path
	var rid := 0
	if tid < model.texture_replaceable_ids.size():
		rid = int(model.texture_replaceable_ids[tid])
	if rid == 1 or rid == 2:
		var stem := "ReplaceableTextures/TeamColor/TeamColor" if rid == 1 else "ReplaceableTextures/TeamGlow/TeamGlow"
		return "%s00.blp" % stem
	if not replaceable_tex.is_empty() and rid >= 31:
		var rt := replaceable_tex.replace("\\", "/")
		if not rt.to_lower().ends_with(".blp"):
			rt += ".blp"
		return rt
	return W3MdxModel.replaceable_rel_path(rid)


func _update_frustum_visibility() -> void:
	var cam := get_viewport().get_camera_3d() if get_viewport() else null
	if cam == null or _batches.is_empty():
		return
	var planes: Array = cam.get_frustum()
	var cam_pos: Vector3 = cam.global_position
	var draw2: float = PROP_DRAW_DIST * PROP_DRAW_DIST
	for gkey in _batches.keys():
		var b: Dictionary = _batches[gkey]
		var mm: MultiMesh = b["mm"]
		var centers: PackedVector3Array = b["centers"]
		var radii: PackedFloat32Array = b["radii"]
		var xforms: Array = b["xforms"]
		var vis := 0
		for i in centers.size():
			var c: Vector3 = centers[i]
			if cam_pos.distance_squared_to(c) > draw2:
				continue
			if _sphere_in_frustum(c, radii[i], planes):
				mm.set_instance_transform(vis, xforms[i])
				vis += 1
		mm.visible_instance_count = vis


func _sphere_in_frustum(center: Vector3, radius: float, planes: Array) -> bool:
	for plane in planes:
		if plane.distance_to(center) < -radius:
			return false
	return true
