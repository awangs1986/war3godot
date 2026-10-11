extends Node3D
## HiveWE-ish MDX batching: one MultiMesh per (model,geoset).
## Streams unique MDX loads, frustum-culls instances; no shadows.
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")


const MAX_STATIC := 4000
const BATCH_LOAD_PER_FRAME := 6

var textures
var catalog
var archive
var sampler

var _queue: Array = []
var _model_jobs: Array = []
var _model_cache: Dictionary = {}
var _mesh_cache: Dictionary = {}
var _batches: Dictionary = {}
var _ok: int = 0
var _miss: int = 0
var _fail: int = 0
var _flushed: bool = false


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
	_ok = 0
	_miss = 0
	_fail = 0
	_flushed = false
	for c in get_children():
		c.queue_free()
	set_process(false)


func get_counts() -> Dictionary:
	return {
		"ok": _ok,
		"miss": _miss,
		"fail": _fail,
		"queued": _queue.size(),
		"jobs": _model_jobs.size(),
		"batches": _batches.size(),
	}


func enqueue_placement(p) -> void:
	if p == null:
		return
	if p.id in ["sloc", "ncam", "Start"]:
		return
	if p.is_unit:
		return
	if _queue.size() >= MAX_STATIC:
		return
	var model_rel: String = ""
	if catalog != null:
		model_rel = catalog.resolve_model(p.id, false, p.variation)
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
	if catalog != null:
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
	_queue.append({
		"abs_path": abs_path,
		"replaceable": replaceable,
		"xform": xform,
		"center": pos,
		"radius": 200.0,
	})
	if not _model_cache.has(abs_path) and not _model_jobs.has(abs_path):
		_model_jobs.append(abs_path)


func begin_streaming() -> void:
	if _queue.is_empty() and _model_jobs.is_empty():
		return
	set_process(true)


func _process(_delta: float) -> void:
	var loaded := 0
	while loaded < BATCH_LOAD_PER_FRAME and not _model_jobs.is_empty():
		_ensure_model(str(_model_jobs.pop_front()))
		loaded += 1
	if not _flushed and _model_jobs.is_empty() and not _queue.is_empty():
		_flush_queue_into_batches()
		_flushed = true
		print("W3MdxBatcher: ok=%d miss=%d fail=%d batches=%d" % [_ok, _miss, _fail, _batches.size()])
	if _flushed:
		# Frustum pack can zero visible_instance_count with bad camera planes;
		# use Godot's built-in MMI custom_aabb culling instead for stability.
		pass


func _ensure_model(abs_path: String) -> void:
	if _model_cache.has(abs_path):
		return
	var model = W3MdxModel.load_file(abs_path)
	_model_cache[abs_path] = model
	if model == null or model.geosets.is_empty():
		_fail += 1


func _flush_queue_into_batches() -> void:
	var by_gkey: Dictionary = {}
	for item in _queue:
		var model = _model_cache.get(item["abs_path"], null)
		if model == null or model.geosets.is_empty():
			continue
		var bank_key: String = "%s|%s" % [item["abs_path"], item["replaceable"]]
		if not _mesh_cache.has(bank_key):
			_mesh_cache[bank_key] = _build_geoset_meshes(model, str(item["replaceable"]))
		var geos: Array = _mesh_cache[bank_key]
		_ok += 1
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
		mmi.custom_aabb = aabb.grow(256.0)
		mmi.extra_cull_margin = 512.0
		add_child(mmi)
		_batches[gkey] = {
			"mm": mm,
			"centers": centers,
			"radii": radii,
			"xforms": xforms,
		}


func _build_geoset_meshes(model, replaceable_tex: String) -> Array:
	var out: Array = []
	for gi in model.geosets.size():
		var g = model.geosets[gi]
		out.append({"mesh": _commit_geoset(g), "mat": _make_material(model, g, replaceable_tex)})
	return out


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


func _make_material(model, g, replaceable_tex: String) -> StandardMaterial3D:
	var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
	var mat := StandardMaterial3D.new()
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
	W3War3Lit.style_model_material(mat)
	var tex_path := _resolve_tex(model, g, replaceable_tex)
	var layer_fm := 0
	if not model.materials.is_empty():
		var mid = clampi(g.material_id, 0, model.materials.size() - 1)
		var layers: Array = model.materials[mid]
		if not layers.is_empty():
			layer_fm = int(layers[0].filter_mode)
	var path_l := tex_path.to_lower()
	var looks_cutout := (
		path_l.contains("tree") or path_l.contains("leaf") or path_l.contains("canopy") or path_l.contains("plant")
	)
	var looks_add := (
		path_l.contains("glow") or path_l.contains("fire") or path_l.contains("torch") or path_l.contains("lensflare")
	)
	if not tex_path.is_empty() and textures != null:
		mat.albedo_texture = textures.get_texture(tex_path)
		if layer_fm == 1 or looks_cutout:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
			mat.alpha_scissor_threshold = 0.28
		elif layer_fm == 2:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		elif layer_fm == 3 or layer_fm == 4 or looks_add:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
			mat.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
			mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	else:
		mat.albedo_color = Color(0.45, 0.55, 0.35)
	return mat


func _resolve_tex(model, g, replaceable_tex: String) -> String:
	if model.materials.is_empty():
		return ""
	var mid = clampi(g.material_id, 0, model.materials.size() - 1)
	var layers: Array = model.materials[mid]
	if layers.is_empty():
		return ""
	var tid: int = int(layers[0].texture_id)
	if tid < 0 or tid >= model.textures.size():
		return ""
	var path: String = model.textures[tid]
	if not path.is_empty():
		return path
	var rid := 0
	if tid < model.texture_replaceable_ids.size():
		rid = int(model.texture_replaceable_ids[tid])
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
	for gkey in _batches.keys():
		var b: Dictionary = _batches[gkey]
		var mm: MultiMesh = b["mm"]
		var centers: PackedVector3Array = b["centers"]
		var radii: PackedFloat32Array = b["radii"]
		var xforms: Array = b["xforms"]
		var vis := 0
		for i in centers.size():
			if _sphere_in_frustum(centers[i], radii[i], planes):
				mm.set_instance_transform(vis, xforms[i])
				vis += 1
		mm.visible_instance_count = vis


func _sphere_in_frustum(center: Vector3, radius: float, planes: Array) -> bool:
	for plane in planes:
		if plane.distance_to(center) < -radius:
			return false
	return true
