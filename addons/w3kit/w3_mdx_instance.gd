extends Node3D
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxAnim = preload("res://addons/w3kit/w3_mdx_anim.gd")
## Instantiates a W3MdxModel as mesh children; plays Stand/Walk by sequence time.
## Static props reuse a mesh bank. Animated units CPU-skin bones each frame (Warsmash-style).


static var _mesh_bank: Dictionary = {} # abs_path -> Array[{ "mesh": Mesh, "mat": Material, "geoset_id": int }]

var model
var textures
var _seq_index: int = -1
var _anim_time: float = 0.0 ## milliseconds within sequence
var _playing: bool = true
var _animate: bool = false ## skeletal deformation
var _cliff_remap: bool = false ## Warsmash (y,-x,z) on bind verts
## Per-tile ground_height corners (g00,g10,g01,g11). HiveWE cliff.vert samples these per vertex.
var _cliff_ground: Vector4 = Vector4.ZERO
var _cliff_displace: bool = false
## DestructableData texFile override for replaceable tree/leaf slots.
var _replaceable_tex: String = ""
var _mesh_root: Node3D
var _placeholder: MeshInstance3D
var _cache_key: String = ""
var _geoset_nodes: Array = [] # MeshInstance3D
var _bind_geosets: Array = [] # Geoset refs for skinning
var _skinned_arrays: Array = [] # ArrayMesh per geoset (unique, not banked)


static func clear_mesh_bank() -> void:
	_mesh_bank.clear()


func setup(p_model, p_textures, cache_key: String = "", opts: Dictionary = {}) -> void:
	model = p_model
	textures = p_textures
	_cache_key = cache_key
	_animate = bool(opts.get("animate", false)) and model != null and not model.bones.is_empty()
	_cliff_remap = bool(opts.get("cliff_remap", false))
	_cliff_displace = opts.has("cliff_ground")
	_cliff_ground = opts.get("cliff_ground", Vector4.ZERO) as Vector4
	_replaceable_tex = str(opts.get("replaceable_texture", ""))
	_geoset_nodes.clear()
	_bind_geosets.clear()
	_skinned_arrays.clear()
	for c in get_children():
		c.queue_free()
	_mesh_root = Node3D.new()
	_mesh_root.name = "MdxMeshes"
	add_child(_mesh_root)
	if model == null or model.geosets.is_empty():
		_add_placeholder()
		return
	if _animate:
		_build_animated_meshes(opts)
	else:
		# Per-tile ground displacement → unique meshes (cannot share bank).
		if _cliff_displace:
			_build_meshes(opts)
		else:
			var bank_key := cache_key
			if opts.has("cliff_texture"):
				bank_key = "%s|cliff:%s" % [cache_key, str(opts["cliff_texture"])]
			if opts.has("team_color"):
				bank_key = "%s|tc:%d" % [bank_key, int(opts["team_color"])]
			if not _replaceable_tex.is_empty():
				bank_key = "%s|rtex:%s" % [bank_key, _replaceable_tex]
			if _cliff_remap:
				bank_key = "%s|cremap" % bank_key
			if not bank_key.is_empty() and _mesh_bank.has(bank_key):
				_attach_bank(_mesh_bank[bank_key])
			else:
				var bank: Array = _build_meshes(opts)
				if not bank_key.is_empty():
					_mesh_bank[bank_key] = bank
	play_idle()


func _add_placeholder() -> void:
	_placeholder = MeshInstance3D.new()
	var box = BoxMesh.new()
	box.size = Vector3(32, 64, 32)
	_placeholder.mesh = box
	_placeholder.position.y = 32
	var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
	var mat = StandardMaterial3D.new()
	mat.albedo_color = Color(0.35, 0.55, 0.28)
	W3War3Lit.style_model_material(mat)
	_placeholder.material_override = mat
	add_child(_placeholder)


func _attach_bank(bank: Array) -> void:
	for entry in bank:
		var mi = MeshInstance3D.new()
		mi.mesh = entry["mesh"]
		mi.material_override = entry["mat"]
		mi.set_meta("geoset_id", int(entry.get("geoset_id", -1)))
		_mesh_root.add_child(mi)
		_geoset_nodes.append(mi)


func _make_material(g, team_color: int, cliff_tex_override: String) -> StandardMaterial3D:
	var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
	var mat = StandardMaterial3D.new()
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
	W3War3Lit.style_model_material(mat)
	var tex_path = _texture_for_geoset(g, team_color, cliff_tex_override)
	var layer_fm := 0
	if not model.materials.is_empty():
		var mid = clampi(g.material_id, 0, model.materials.size() - 1)
		var layers: Array = model.materials[mid]
		if not layers.is_empty():
			layer_fm = int(layers[0].filter_mode)
	var path_l := tex_path.to_lower()
	var looks_cutout := (
		path_l.contains("tree")
		or path_l.contains("plant")
		or path_l.contains("leaf")
		or path_l.contains("canopy")
		or path_l.contains("grass")
		or path_l.contains("vine")
	)
	var looks_add := (
		path_l.contains("glow")
		or path_l.contains("fire")
		or path_l.contains("flame")
		or path_l.contains("torch")
		or path_l.contains("lensflare")
		or path_l.contains("lightning")
	)
	if not tex_path.is_empty() and textures != null:
		mat.albedo_texture = textures.get_texture(tex_path)
		# WC3 filter modes: 0 opaque, 1 transparent(cutout), 2 blend, 3 additive, 4 addAlpha, 5/6 modulate.
		if layer_fm == 1 or looks_cutout:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
			mat.alpha_scissor_threshold = 0.28
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


func _build_meshes(opts: Dictionary = {}) -> Array:
	var bank: Array = []
	var team_color: int = int(opts.get("team_color", 0))
	var cliff_tex_override: String = str(opts.get("cliff_texture", ""))
	for gi in model.geosets.size():
		var g = model.geosets[gi]
		var mesh := _commit_geoset_mesh(g, g.vertices, g.normals)
		var mi = MeshInstance3D.new()
		mi.mesh = mesh
		mi.material_override = _make_material(g, team_color, cliff_tex_override)
		mi.set_meta("geoset_id", gi)
		_mesh_root.add_child(mi)
		_geoset_nodes.append(mi)
		bank.append({"mesh": mesh, "mat": mi.material_override, "geoset_id": gi})
	return bank


func _build_animated_meshes(opts: Dictionary = {}) -> void:
	var team_color: int = int(opts.get("team_color", 0))
	var cliff_tex_override: String = str(opts.get("cliff_texture", ""))
	for gi in model.geosets.size():
		var g = model.geosets[gi]
		_bind_geosets.append(g)
		var mesh := _commit_geoset_mesh(g, g.vertices, g.normals)
		var mi = MeshInstance3D.new()
		mi.mesh = mesh
		mi.material_override = _make_material(g, team_color, cliff_tex_override)
		mi.set_meta("geoset_id", gi)
		_mesh_root.add_child(mi)
		_geoset_nodes.append(mi)
		_skinned_arrays.append(mesh)


func _commit_geoset_mesh(g, verts: PackedVector3Array, norms: PackedVector3Array) -> ArrayMesh:
	var st = SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var nvert = verts.size()
	for fi in range(0, g.faces.size() - 2, 3):
		for k in 3:
			var idx: int = g.faces[fi + k]
			if idx < 0 or idx >= nvert:
				continue
			var nrm: Vector3 = norms[idx] if idx < norms.size() else Vector3.UP
			var vtx: Vector3 = verts[idx]
			if _cliff_remap:
				# Godot vert is w3_to_godot(x,y,z)=(x,z,y). Warsmash wants (y,-x,z)→Godot (y,z,-x).
				vtx = Vector3(vtx.z, vtx.y, -vtx.x)
				nrm = Vector3(nrm.z, nrm.y, -nrm.x).normalized()
			if _cliff_displace:
				# HiveWE: height = ground_height[floor(tile_uv)]; seal corners to terrain.
				var u := clampf(vtx.x / 128.0, 0.0, 1.0)
				var v := clampf(vtx.z / 128.0, 0.0, 1.0)
				var g0 := lerpf(_cliff_ground.x, _cliff_ground.y, u)
				var g1 := lerpf(_cliff_ground.z, _cliff_ground.w, u)
				vtx.y += lerpf(g0, g1, v) * 128.0
			st.set_normal(nrm)
			if idx < g.uvs.size():
				st.set_uv(g.uvs[idx])
			st.add_vertex(vtx)
	return st.commit()


func _texture_for_geoset(g, team_color: int = 0, cliff_tex_override: String = "") -> String:
	if model.materials.is_empty():
		return _resolve_texture_slot(0, team_color, cliff_tex_override)
	var mid = clampi(g.material_id, 0, model.materials.size() - 1)
	var layers: Array = model.materials[mid]
	if layers.is_empty():
		return ""
	var layer = layers[0]
	if layer.texture_id >= 0 and layer.texture_id < model.textures.size():
		return _resolve_texture_slot(layer.texture_id, team_color, cliff_tex_override)
	return ""


func _resolve_texture_slot(tex_index: int, team_color: int = 0, cliff_tex_override: String = "") -> String:
	if tex_index < 0 or tex_index >= model.textures.size():
		return ""
	var path: String = model.textures[tex_index]
	if not path.is_empty():
		return path
	var rid := 0
	if tex_index < model.texture_replaceable_ids.size():
		rid = int(model.texture_replaceable_ids[tex_index])
	if rid == 11 and not cliff_tex_override.is_empty():
		return cliff_tex_override
	if rid == 1 or rid == 2:
		var stem := "ReplaceableTextures/TeamColor/TeamColor" if rid == 1 else "ReplaceableTextures/TeamGlow/TeamGlow"
		return "%s%02d.blp" % [stem, clampi(team_color, 0, 15)]
	# DestructableData.texFile wins for tree/canopy replaceables (same rid, different file).
	if not _replaceable_tex.is_empty() and rid >= 31:
		var rt := _replaceable_tex.replace("\\", "/")
		if not rt.to_lower().ends_with(".blp"):
			rt += ".blp"
		return rt
	return W3MdxModel.replaceable_rel_path(rid)


func play_named(seq_name: String) -> void:
	if model == null:
		return
	var idx = model.find_sequence(seq_name)
	if idx < 0 and seq_name.to_lower() != "stand":
		idx = model.find_idle_sequence()
	_apply_sequence_index(idx)


func play_idle() -> void:
	if model == null:
		return
	_apply_sequence_index(model.find_idle_sequence())


func _apply_sequence_index(idx: int) -> void:
	if idx == _seq_index:
		_apply_geoset_visibility()
		if _animate:
			_skin_at_current_time()
		return
	_seq_index = idx
	_anim_time = 0.0
	_apply_geoset_visibility()
	if _animate:
		_skin_at_current_time()


func set_moving(moving: bool) -> void:
	if moving:
		play_named("Walk")
	else:
		play_idle()


func _apply_geoset_visibility() -> void:
	if model == null or _mesh_root == null:
		return
	var time_ms := 0
	if _seq_index >= 0 and _seq_index < model.sequences.size():
		var s = model.sequences[_seq_index]
		time_ms = int(s.interval_start + _anim_time)
	for mi in _geoset_nodes:
		if mi == null or not is_instance_valid(mi):
			continue
		var gid := int(mi.get_meta("geoset_id", -1))
		if gid < 0:
			mi.visible = true
			continue
		mi.visible = model.geoset_alpha_at(gid, time_ms) > 0.01


func _current_time_ms() -> int:
	if _seq_index < 0 or _seq_index >= model.sequences.size():
		return 0
	var s = model.sequences[_seq_index]
	return int(s.interval_start + _anim_time)


func _skin_at_current_time() -> void:
	if not _animate or model == null or model.bones.is_empty():
		return
	var time_ms := _current_time_ms()
	var seq_start := -1
	var seq_end := -1
	if _seq_index >= 0 and _seq_index < model.sequences.size():
		var s = model.sequences[_seq_index]
		seq_start = int(s.interval_start)
		seq_end = int(s.interval_end)
	var worlds: Dictionary = W3MdxAnim.compute_world_matrices(model.bones, time_ms, seq_start, seq_end)
	for gi in _geoset_nodes.size():
		var mi: MeshInstance3D = _geoset_nodes[gi]
		if mi == null or not is_instance_valid(mi):
			continue
		if gi >= _bind_geosets.size():
			continue
		var g = _bind_geosets[gi]
		var nvert: int = g.vertices_war3.size()
		if nvert == 0:
			continue
		var skinned_v := PackedVector3Array()
		var skinned_n := PackedVector3Array()
		skinned_v.resize(nvert)
		skinned_n.resize(nvert)
		for vi in nvert:
			var vw: Vector3 = g.vertices_war3[vi]
			var nw: Vector3 = g.normals_war3[vi] if vi < g.normals_war3.size() else Vector3.UP
			var sw: Vector3 = W3MdxAnim.skin_vertex(vw, g.bone_indices, g.bone_weights, vi, worlds)
			var sn: Vector3 = W3MdxAnim.skin_normal(nw, g.bone_indices, g.bone_weights, vi, worlds)
			skinned_v[vi] = W3MdxModel.w3_to_godot(sw)
			skinned_n[vi] = W3MdxModel.w3_to_godot(sn).normalized()
		mi.mesh = _commit_geoset_mesh(g, skinned_v, skinned_n)


func _process(delta: float) -> void:
	if not _playing or model == null or _seq_index < 0:
		return
	var s = model.sequences[_seq_index]
	var span = float(maxi(1, s.interval_end - s.interval_start))
	_anim_time = fmod(_anim_time + delta * 1000.0, span)
	_apply_geoset_visibility()
	if _animate:
		_skin_at_current_time()
