extends Node3D
## HiveWE-ish MDX batching: static MultiMesh + animated W3MdxInstance for units/wind.
## Streams unique MDX loads; no shadows. Doodads + units from DOO files.
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")


const MAX_STATIC := 20000
const BATCH_LOAD_PER_FRAME := 10
const PROP_DRAW_DIST := 5200.0
const FRUSTUM_CHECK_SEC := 0.08
## Extra world-space slack on culling spheres: hides the FRUSTUM_CHECK_SEC
## latency while the camera pans/rotates.
const FRUSTUM_MARGIN := 192.0
const ANIM_SPAWN_PER_FRAME := 4
## Animation is a close-up detail. Start inside 800 world units and use a
## small stop-distance hysteresis so actors do not flicker at the boundary.
const ANIM_NEAR_DIST := 800.0
const ANIM_FAR_DIST := 950.0
const ANIM_DRAW_DIST := 4200.0
const MAX_NEAR_ANIMS := 48
const PROXIMITY_CHECK_SEC := 0.1

var textures
var catalog
var archive
var sampler

var _queue: Array = []
var _model_jobs: Array = []
var _model_cache: Dictionary = {}
var _model_radius_cache: Dictionary = {}
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
var _frustum_timer: float = 0.0
var _visible_static: int = 0
var _anim_pending: Array = []
var _anim_instances: Array = []
var _anim_root: Node3D
var _proximity_timer: float = 0.0
var _anim_active_n: int = 0
var _lod_level: int = 0
var _load_total_models: int = 0
var _load_total_placements: int = 0
var _load_total_animations: int = 0
## Physics bodies only for visible W3 units.  This deliberately follows the
## original Godot enemy-selection path instead of approximate model bounds.
var _selectable_units_root: Node3D


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
	_model_radius_cache.clear()
	_ok = 0
	_miss = 0
	_fail = 0
	_animated = 0
	_anim_active_n = 0
	_flushed = false
	_frustum_timer = 0.0
	_visible_static = 0
	_anim_pending.clear()
	_anim_instances.clear()
	_anim_root = null
	_proximity_timer = 0.0
	_anim_active_n = 0
	_lod_level = 0
	_load_total_models = 0
	_load_total_placements = 0
	_load_total_animations = 0
	_selectable_units_root = null
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
		"visible_static": _visible_static,
		"animated": _anim_instances.size(),
		"anim_active": _anim_active_n,
		"anim_pending": _anim_pending.size(),
		"lod": _lod_level,
	}


func get_loading_state() -> Dictionary:
	if not _flushed:
		var total_models := maxi(1, _load_total_models)
		var loaded_models := clampi(_load_total_models - _model_jobs.size(), 0, total_models)
		return {
			"progress": 0.22 + 0.48 * (float(loaded_models) / float(total_models)),
			"phase": "正在载入高清模型 %d/%d" % [loaded_models, _load_total_models],
			"complete": false,
		}
	if not _anim_pending.is_empty():
		var total_animations := maxi(1, _load_total_animations)
		var spawned := clampi(_load_total_animations - _anim_pending.size(), 0, total_animations)
		return {
			"progress": 0.76 + 0.18 * (float(spawned) / float(total_animations)),
			"phase": "正在建立场景对象 %d/%d" % [
				_load_total_placements - _anim_pending.size(), _load_total_placements,
			],
			"complete": false,
		}
	return {
		"progress": 0.95,
		"phase": "正在预热渲染器",
		"complete": _flushed and _model_jobs.is_empty() and _queue.is_empty() and _anim_pending.is_empty(),
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
	var appearance_id := str(p.skin_id)
	if appearance_id.length() != 4 or _contains_nul(appearance_id):
		appearance_id = str(p.id)
	var model_rel: String = ""
	if catalog != null:
		model_rel = catalog.resolve_model(appearance_id, p.is_unit, p.variation)
	if model_rel.is_empty():
		model_rel = str(p.model_path)
	var abs_path := ""
	if archive != null and not model_rel.is_empty():
		abs_path = archive.resolve_path(model_rel)
		if abs_path.is_empty():
			abs_path = W3Paths.resolve_game_path(model_rel)
	if abs_path.is_empty():
		_miss += 1
		return
	var replaceable := ""
	if catalog != null and not p.is_unit:
		replaceable = catalog.destructable_tex_file(appearance_id)
	var pos: Vector3 = p.position
	if sampler:
		var ground_y: float = sampler.sample_y(pos.x, pos.z)
		if pos.y < ground_y + 8.0:
			pos.y = ground_y
		if p.is_unit and catalog != null:
			# HiveWE Unit::update adds UnitData.moveHeight after the authored map
			# position; flying units otherwise sit on the terrain in previews.
			pos.y += catalog.unit_move_height(appearance_id)
	var basis := Basis(Vector3.UP, -p.angle)
	var scl: Vector3 = p.scale if p.scale.length() > 0.01 else Vector3.ONE
	if catalog != null:
		scl *= catalog.model_scale(appearance_id, p.is_unit)
	basis = basis.scaled(scl)
	var xform := Transform3D(basis, pos)
	var item := {
		"abs_path": abs_path,
		"model_rel": model_rel,
		"replaceable": replaceable,
		"xform": xform,
		"center": pos,
		"radius": 200.0,
		"rawcode": str(p.id),
		"skin_rawcode": appearance_id,
		"variation": int(p.variation),
		"is_unit": bool(p.is_unit),
		"player_owner": int(p.player_owner),
		"resolved": true,
	}
	_queue.append(item)
	if not _model_cache.has(abs_path) and not _model_jobs.has(abs_path):
		_model_jobs.append(abs_path)


func _contains_nul(value: String) -> bool:
	for i in value.length():
		if value.unicode_at(i) == 0:
			return true
	return false


func begin_streaming() -> void:
	_load_total_models = _model_jobs.size()
	_load_total_placements = _queue.size()
	_load_total_animations = 0
	if _queue.is_empty() and _model_jobs.is_empty():
		_flushed = true
		return
	_flushed = false
	set_process(true)


func _process(delta: float) -> void:
	var loaded := 0
	while loaded < BATCH_LOAD_PER_FRAME and not _model_jobs.is_empty():
		_ensure_model(str(_model_jobs.pop_front()))
		loaded += 1
	if not _flushed and _model_jobs.is_empty() and not _queue.is_empty():
		_flush_queue_into_batches()
		_flushed = true
		print("W3MdxBatcher: ok=%d miss=%d fail=%d batches=%d" % [_ok, _miss, _fail, _batches.size()])
		_update_frustum_visibility()
		var world := get_parent()
		if world != null and world.has_method("_refresh_status"):
			world._refresh_status()
	if _flushed:
		var had_pending := not _anim_pending.is_empty()
		var spawned := 0
		while spawned < ANIM_SPAWN_PER_FRAME and not _anim_pending.is_empty():
			var item: Dictionary = _anim_pending.pop_front()
			var model = _model_cache.get(item["abs_path"], null)
			if model != null and not model.geosets.is_empty():
				_spawn_animated(item, model)
			spawned += 1
		if had_pending and _anim_pending.is_empty():
			print("W3MdxBatcher: animated=%d active=%d near<=%.0f far<=%.0f draw<=%.0f" % [
				_anim_instances.size(), _anim_active_n, ANIM_NEAR_DIST, ANIM_FAR_DIST, ANIM_DRAW_DIST,
			])
			var world := get_parent()
			if world != null and world.has_method("_refresh_status"):
				world._refresh_status()
		_proximity_timer += delta
		if _proximity_timer >= PROXIMITY_CHECK_SEC:
			_proximity_timer = 0.0
			_update_anim_proximity()
		_frustum_timer += delta
		if _frustum_timer >= FRUSTUM_CHECK_SEC:
			_frustum_timer = 0.0
			_update_frustum_visibility()


func _ensure_model(abs_path: String) -> void:
	if _model_cache.has(abs_path):
		return
	var model = W3MdxModel.load_file(abs_path)
	_model_cache[abs_path] = model
	if model == null or model.geosets.is_empty():
		_fail += 1


func _spawn_animated(item: Dictionary, model) -> void:
	if _anim_root == null or not is_instance_valid(_anim_root):
		_anim_root = Node3D.new()
		_anim_root.name = "AnimatedPreviewUnits"
		add_child(_anim_root)
	# Keep the concrete instance type: this is the component that advances the
	# Warsmash-style Stand sequence and CPU-skins its bones every frame.
	var inst := W3MdxInstance.new()
	_anim_root.add_child(inst)
	inst.transform = item["xform"]
	inst.setup(model, textures, str(item["abs_path"]), {
		"animate": true,
		"proximity_gate": true,
		"team_color": int(item.get("player_owner", 0)),
	})
	# Newly streamed actors start frozen. The proximity pass explicitly enables
	# animation only when the camera is close enough to see the motion.
	inst.set_anim_active(false)
	inst.set_meta("w3_preview_item", item)
	if bool(item.get("is_unit", false)):
		inst.set_meta("w3_selection_body", _add_unit_selection_body(inst, Transform3D.IDENTITY, model, item))
	else:
		inst.set_meta("w3_selection_body", null)
	inst.visible = false
	_anim_instances.append(inst)
	_ok += 1


func _update_anim_proximity() -> void:
	var cam := get_viewport().get_camera_3d() if get_viewport() else null
	if cam == null:
		return
	var cam_pos: Vector3 = cam.global_position
	var planes: Array = cam.get_frustum()
	var near2 := ANIM_NEAR_DIST * ANIM_NEAR_DIST
	var far2 := ANIM_FAR_DIST * ANIM_FAR_DIST
	var draw2 := ANIM_DRAW_DIST * ANIM_DRAW_DIST
	var camera_lod := _camera_lod_level(cam)
	var scored: Array = []
	var alive: Array = []
	for inst in _anim_instances:
		if inst == null or not is_instance_valid(inst):
			continue
		alive.append(inst)
		scored.append({"inst": inst, "d2": inst.global_position.distance_squared_to(cam_pos)})
	_anim_instances = alive
	scored.sort_custom(func(a, b): return float(a["d2"]) < float(b["d2"]))

	_anim_active_n = 0
	var animation_slots := 0
	for entry in scored:
		var inst = entry["inst"]
		var d2: float = float(entry["d2"])
		# Keep the collision state identical to the visual state: off-screen
		# units must not become ray-pick targets just because they are nearby.
		# At map-overview zoom these models occupy only a few pixels while each
		# animated HD unit costs several skeletal draw calls. Hide them at LOD2+
		# and let terrain/prop batches carry the overview.
		var item_meta: Dictionary = inst.get_meta("w3_preview_item") if inst.has_meta("w3_preview_item") else {}
		var cull_radius := float(item_meta.get("radius", 200.0)) + FRUSTUM_MARGIN
		var draw := camera_lod < 2 and d2 <= draw2 and _sphere_in_frustum(inst.global_position, cull_radius, planes)
		inst.visible = draw
		var selection_body = inst.get_meta("w3_selection_body") if inst.has_meta("w3_selection_body") else null
		_set_unit_selection_enabled(selection_body, draw)
		var was_active: bool = inst.is_anim_active()
		var animate := false
		if draw and animation_slots < MAX_NEAR_ANIMS:
			animate = d2 <= (far2 if was_active else near2)
		if animate:
			animation_slots += 1
			_anim_active_n += 1
		inst.set_anim_active(animate)


func _flush_queue_into_batches() -> void:
	var by_gkey: Dictionary = {}
	var placement_counts: Dictionary = {}
	for queued_item in _queue:
		var queued_path := str(queued_item.get("abs_path", ""))
		placement_counts[queued_path] = int(placement_counts.get(queued_path, 0)) + 1
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
		_apply_model_radius(item, model)
		if _needs_live_instance(item, model, int(placement_counts.get(str(item["abs_path"]), 0))):
			_anim_pending.append(item)
			continue
		if bool(item.get("is_unit", false)):
			_add_unit_selection_body(_get_selectable_units_root(), item["xform"], model, item)
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
	_load_total_animations = _anim_pending.size()

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
		# Additive/modulate glow quads must not shadow-cast: Godot's shadow pass
		# renders them opaque, which painted dark rectangles on the ground.
		mmi.cast_shadow = (
			GeometryInstance3D.SHADOW_CASTING_SETTING_ON
			if bool(geo.get("casts_shadow", true))
			else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		)
		mmi.gi_mode = GeometryInstance3D.GI_MODE_STATIC
		var aabb := AABB(centers[0], Vector3.ZERO)
		var max_radius := 0.0
		for i in centers.size():
			aabb = aabb.expand(centers[i])
			max_radius = maxf(max_radius, radii[i])
		mmi.custom_aabb = aabb.grow(max_radius + 128.0)
		mmi.extra_cull_margin = 128.0
		mmi.visibility_range_end = PROP_DRAW_DIST + 800.0
		mmi.visibility_range_end_margin = 400.0
		add_child(mmi)
		_batches[gkey] = {
			"mm": mm,
			"lod_meshes": geo.get("lod_meshes", {}),
			"centers": centers,
			"radii": radii,
			"xforms": xforms,
			"items": items,
		}


## Replaces the placeholder culling radius with half the scaled AABB diagonal.
func _apply_model_radius(item: Dictionary, model) -> void:
	var abs_path := str(item["abs_path"])
	if not _model_radius_cache.has(abs_path):
		_model_radius_cache[abs_path] = _model_local_aabb(model).size.length() * 0.5
	var xf: Transform3D = item["xform"]
	var s := xf.basis.get_scale()
	item["radius"] = float(_model_radius_cache[abs_path]) * maxf(s.x, maxf(s.y, s.z))


func _needs_live_instance(item: Dictionary, model, placement_count: int) -> bool:
	if model.sequences.is_empty():
		return false
	if bool(item.get("is_unit", false)):
		return not model.bones.is_empty() or model.has_idle_layer_animation() or not model.particle_emitters2.is_empty() or not model.lights.is_empty()
	# Warcraft animates doodads too. Keep common forests and decorative clutter
	# batched, but give authored ambient actors their Stand motion. This preserves
	# the map's thousands of static instances while matching Warsmash for the
	# visibly animated scene models used by Lost Temple.
	var model_rel := str(item.get("model_rel", "")).replace("\\", "/").to_lower()
	var is_ambient_actor := (
		model_rel.contains("/bird/")
		or model_rel.contains("/fish/")
		or model_rel.contains("fountain")
		or model_rel.contains("dolphin")
		or model_rel.contains("whale")
		or model_rel.contains("lilypad")
		or model_rel.contains("lillypad")
	)
	if placement_count <= 64 and is_ambient_actor:
		return model.has_idle_bone_motion() or model.has_idle_layer_animation()
	# Rare authored light/particle props remain live; mass forest/destructable
	# animations stay in the static batch until gameplay activates that state.
	return placement_count <= 16 and (not model.particle_emitters2.is_empty() or not model.lights.is_empty())


func _build_geoset_meshes(model, replaceable_tex: String) -> Array:
	var out: Array = []
	var stand_time := 0
	var stand_index: int = int(model.find_idle_sequence())
	if stand_index >= 0 and stand_index < model.sequences.size():
		stand_time = int(model.sequences[stand_index].interval_start)
	var by_lod: Dictionary = {}
	for gi in model.geosets.size():
		var g = model.geosets[gi]
		var lod := maxi(0, int(g.lod))
		# Warsmash MdxComplexInstance samples each geoset animation for the
		# selected sequence before drawing. Static MultiMesh props still need
		# that Stand visibility decision or death/stump geosets render together.
		if model.geoset_alpha_at(gi, stand_time) <= 0.01:
			continue
		if not by_lod.has(lod):
			by_lod[lod] = []
		by_lod[lod].append({"g": g, "gi": gi})
	var base_geosets: Array = by_lod.get(0, [])
	if base_geosets.is_empty() and not by_lod.is_empty():
		var first_lod: int = int(by_lod.keys().min())
		base_geosets = by_lod[first_lod]
	for surface_index in base_geosets.size():
		var base_entry: Dictionary = base_geosets[surface_index]
		var base_g = base_entry["g"]
		var lod_meshes: Dictionary = {}
		for lod_key in by_lod.keys():
			var lod_geosets: Array = by_lod[lod_key]
			if surface_index >= lod_geosets.size():
				continue
			var lod_g = lod_geosets[surface_index]["g"]
			# Surface order is stable within each Reforged LOD bank. Guard the
			# pairing so a model that drops a material at low LOD falls back safely.
			if int(lod_g.material_id) != int(base_g.material_id):
				continue
			lod_meshes[int(lod_key)] = _commit_geoset(lod_g)
		var base_mesh = lod_meshes.get(0, null)
		if base_mesh == null:
			base_mesh = _commit_geoset(base_g)
		out.append({
			"mesh": base_mesh,
			"lod_meshes": lod_meshes,
			"mat": _make_material(model, base_g, replaceable_tex),
			"casts_shadow": _geoset_casts_shadow(model, base_g),
		})
	return out


## WC3 filter modes 0 (opaque) and 1 (cutout) are the depth-writing draws;
## blend/additive/modulate layers (2+) never write depth and must not cast
## shadow-map shadows either (blob shadows are a separate decal system).
static func _geoset_casts_shadow(model, g) -> bool:
	if model.materials.is_empty():
		return true
	var mid = clampi(g.material_id, 0, model.materials.size() - 1)
	var layers: Array = model.materials[mid]
	if layers.is_empty():
		return true
	return int(layers[0].filter_mode) <= 1


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
			if g.tangents.size() >= (idx + 1) * 4:
				var ti := idx * 4
				st.set_tangent(Plane(
					float(g.tangents[ti]), float(g.tangents[ti + 1]),
					float(g.tangents[ti + 2]), float(g.tangents[ti + 3])
				))
			if idx < uvs.size():
				st.set_uv(uvs[idx])
			st.add_vertex(verts[idx])
	return st.commit()


func _make_material(model, g, replaceable_tex: String):
	if not model.materials.is_empty():
		var hd_mid = clampi(g.material_id, 0, model.materials.size() - 1)
		var hd_layers: Array = model.materials[hd_mid]
		if not hd_layers.is_empty() and int(hd_layers[0].shader) == 1:
			return _make_hd_material(model, hd_layers[0], replaceable_tex)
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


func _make_hd_material(model, layer, replaceable_tex: String) -> ShaderMaterial:
	var material := ShaderMaterial.new()
	material.shader = load(_hd_shader_path(int(layer.filter_mode)))
	var ids: PackedInt32Array = layer.texture_ids
	for slot in mini(6, ids.size()):
		var path := _resolve_hd_tex(model, ids[slot], slot, replaceable_tex)
		if path.is_empty() or textures == null:
			continue
		var uniform_name: String = [
			"diffuse_map", "normal_map", "orm_map", "emissive_map", "team_color_map", "environment_map"
		][slot]
		material.set_shader_parameter(uniform_name, textures.get_texture(path))
	material.set_shader_parameter("layer_alpha", clampf(float(layer.alpha), 0.0, 1.0))
	material.set_shader_parameter("filter_mode", int(layer.filter_mode))
	material.set_shader_parameter("emissive_gain", maxf(0.0, float(layer.emissive_gain)))
	material.set_shader_parameter("unshaded_layer", (int(layer.flags) & 0x1) != 0)
	return material


func _hd_shader_path(filter_mode: int) -> String:
	# Warsmash SetupGroups and HiveWE SkinnedMesh agree that modes 0/1 are
	# opaque depth-writing draws; only modes 2+ enter the transparent pass.
	if filter_mode <= 1:
		return "res://addons/w3kit/shaders/w3_reforged_unit_opaque.gdshader"
	if filter_mode == 3 or filter_mode == 4:
		return "res://addons/w3kit/shaders/w3_reforged_unit_add.gdshader"
	if filter_mode == 5 or filter_mode == 6:
		return "res://addons/w3kit/shaders/w3_reforged_unit_modulate.gdshader"
	return "res://addons/w3kit/shaders/w3_reforged_unit.gdshader"


func _resolve_hd_tex(model, tex_index: int, pbr_slot: int, replaceable_tex: String) -> String:
	if tex_index < 0 or tex_index >= model.textures.size():
		return ""
	var authored: String = model.textures[tex_index]
	if not authored.is_empty():
		return authored.get_basename() + ".dds"
	var rid := int(model.texture_replaceable_ids[tex_index]) if tex_index < model.texture_replaceable_ids.size() else 0
	if rid == 1 or rid == 2:
		var team_stem := "ReplaceableTextures/TeamColor/TeamColor" if rid == 1 else "ReplaceableTextures/TeamGlow/TeamGlow"
		return team_stem + "00.dds"
	var stem := ""
	if not replaceable_tex.is_empty() and rid >= 31:
		stem = replaceable_tex.replace("\\", "/").get_basename()
	elif W3MdxModel.REPLACEABLE_STEMS.has(rid):
		stem = str(W3MdxModel.REPLACEABLE_STEMS[rid])
	if stem.is_empty():
		return "Textures/White.dds"
	return stem + _hd_slot_suffix(pbr_slot) + ".dds"


func _hd_slot_suffix(pbr_slot: int) -> String:
	match pbr_slot:
		0: return "_diffuse"
		1: return "_normal"
		2: return "_orm"
		3: return "_emissive"
		_: return ""


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
	var cam_pos: Vector3 = cam.global_position
	var planes: Array = cam.get_frustum()
	_update_batch_lod(cam)
	var draw2: float = PROP_DRAW_DIST * PROP_DRAW_DIST
	_visible_static = 0
	for gkey in _batches.keys():
		var b: Dictionary = _batches[gkey]
		var mm: MultiMesh = b["mm"]
		var centers: PackedVector3Array = b["centers"]
		var radii: PackedFloat32Array = b["radii"]
		var xforms: Array = b["xforms"]
		var items: Array = b["items"]
		var vis := 0
		for i in centers.size():
			# Sphere-vs-frustum with the model's real bounding radius: a point
			# test culled canopies/roofs whose origin was just off screen, so
			# rotating the camera made whole models pop in at the edges.
			var visible := (
				cam_pos.distance_squared_to(centers[i]) <= draw2
				and _sphere_in_frustum(centers[i], radii[i] + FRUSTUM_MARGIN, planes)
			)
			if visible:
				mm.set_instance_transform(vis, xforms[i])
				vis += 1
			if bool(items[i].get("is_unit", false)):
				_set_unit_selection_enabled(items[i].get("selection_body", null), visible)
		mm.visible_instance_count = vis
		_visible_static += vis


func _camera_lod_level(cam: Camera3D) -> int:
	if cam.projection == Camera3D.PROJECTION_ORTHOGONAL:
		if cam.size >= 2600.0:
			return 3
		if cam.size >= 1500.0:
			return 2
		if cam.size >= 800.0:
			return 1
		return 0
	# W3E heights are absolute Warcraft world coordinates and can sit far above
	# Y=0. LOD must use camera altitude over the local terrain, not world-space Y.
	var ground_y := 0.0
	if sampler != null:
		ground_y = float(sampler.sample_y(cam.global_position.x, cam.global_position.z))
	var height := maxf(0.0, cam.global_position.y - ground_y)
	if height >= 2800.0:
		return 3
	if height >= 1600.0:
		return 2
	if height >= 850.0:
		return 1
	return 0


func _update_batch_lod(cam: Camera3D) -> void:
	var next_lod := _camera_lod_level(cam)
	if next_lod == _lod_level:
		return
	_lod_level = next_lod
	for batch in _batches.values():
		var lod_meshes: Dictionary = batch.get("lod_meshes", {})
		if lod_meshes.is_empty():
			continue
		var selected_lod := _lod_level
		while selected_lod > 0 and not lod_meshes.has(selected_lod):
			selected_lod -= 1
		if not lod_meshes.has(selected_lod):
			selected_lod = int(lod_meshes.keys().min())
		var mm: MultiMesh = batch["mm"]
		mm.mesh = lod_meshes[selected_lod]


func _get_selectable_units_root() -> Node3D:
	if _selectable_units_root != null and is_instance_valid(_selectable_units_root):
		return _selectable_units_root
	_selectable_units_root = Node3D.new()
	_selectable_units_root.name = "SelectablePreviewUnits"
	add_child(_selectable_units_root)
	return _selectable_units_root


func _add_unit_selection_body(parent: Node3D, local_xform: Transform3D, model, item: Dictionary) -> StaticBody3D:
	if parent == null:
		return null
	var body := StaticBody3D.new()
	body.name = "SelectableUnit_%s" % str(item.get("rawcode", "unit"))
	body.transform = local_xform
	body.collision_layer = 4 # Same layer used by the native Enemy scene.
	body.collision_mask = 0
	body.add_to_group("enemy")
	body.add_to_group("w3_selectable_unit")
	body.set_meta("w3_preview_item", item)
	parent.add_child(body)

	var aabb := _model_local_aabb(model)
	var shape := BoxShape3D.new()
	shape.size = aabb.size
	var collision := CollisionShape3D.new()
	collision.name = "SelectionCollision"
	collision.shape = shape
	collision.position = aabb.get_center()
	collision.disabled = true
	body.add_child(collision)
	body.set_meta("w3_selection_collision", collision)
	body.set_meta("w3_selection_radius", maxf(aabb.size.x, aabb.size.z) * 0.55)
	item["selection_body"] = body
	return body


func _set_unit_selection_enabled(body: Variant, enabled: bool) -> void:
	if not (body is StaticBody3D) or not is_instance_valid(body):
		return
	var collision: CollisionShape3D = body.get_meta("w3_selection_collision", null) as CollisionShape3D
	if collision != null and is_instance_valid(collision):
		collision.set_deferred("disabled", not enabled)


func _model_local_aabb(model) -> AABB:
	var has_vertex := false
	var min_v := Vector3.ZERO
	var max_v := Vector3.ZERO
	for geoset in model.geosets:
		if geoset.lod != 0 and geoset.lod != -1:
			continue
		for vertex in geoset.vertices:
			var vertex3: Vector3 = vertex
			if not has_vertex:
				min_v = vertex3
				max_v = vertex3
				has_vertex = true
			else:
				min_v = min_v.min(vertex3)
				max_v = max_v.max(vertex3)
	if not has_vertex:
		return AABB(Vector3(-64.0, 0.0, -64.0), Vector3(128.0, 128.0, 128.0))
	var size := max_v - min_v
	size.x = maxf(size.x, 24.0)
	size.y = maxf(size.y, 24.0)
	size.z = maxf(size.z, 24.0)
	return AABB(min_v, size)


## Camera3D.get_frustum() planes have outward normals (is_point_over == outside),
## so the sphere is culled once it sits more than `radius` beyond any plane.
func _sphere_in_frustum(center: Vector3, radius: float, planes: Array) -> bool:
	for plane in planes:
		if plane.distance_to(center) > radius:
			return false
	return true
