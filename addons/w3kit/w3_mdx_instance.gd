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
var _global_time: float = 0.0 ## milliseconds for GLBS-driven material tracks
var _playing: bool = true
var _loop: bool = true ## false = one-shot sequence held at its final frame
var _playback: bool = false ## sequence/material/effect clock
var _animate: bool = false ## skeletal deformation
var _proximity_gate: bool = false
var _anim_active: bool = true
var _skin_frame: int = 0
const SKIN_EVERY_N_FRAMES := 2
var _cliff_remap: bool = false ## Warsmash (y,-x,z) on bind verts
## Per-tile ground_height corners (g00,g10,g01,g11). HiveWE cliff.vert samples these per vertex.
var _cliff_ground: Vector4 = Vector4.ZERO
## Corner heights beginning at the cliff placement. 3x3 covers CliffTrans
## models whose footprint extends across two tiles on either axis.
var _cliff_ground_grid: PackedFloat32Array = PackedFloat32Array()
var _cliff_ground_grid_size: Vector2i = Vector2i.ZERO
var _cliff_displace: bool = false
## DestructableData texFile override for replaceable tree/leaf slots.
var _replaceable_tex: String = ""
var _mesh_root: Node3D
var _placeholder: MeshInstance3D
var _cache_key: String = ""
var _geoset_nodes: Array = [] # MeshInstance3D
var _bind_geosets: Array = [] # Geoset refs for skinning
var _skinned_arrays: Array = [] # ArrayMesh per geoset (unique, not banked)
var _particle_nodes: Array = [] # {emitter, node: GPUParticles3D}
var _light_nodes: Array = [] # {light, node: Light3D}
var _skeleton: Skeleton3D
var _skin: Skin
var _bone_index_by_oid: Dictionary = {}
var _material_tracks: Array = [] # {layer, material, team_color, cliff_texture}


static func clear_mesh_bank() -> void:
	_mesh_bank.clear()


func setup(p_model, p_textures, cache_key: String = "", opts: Dictionary = {}) -> void:
	model = p_model
	textures = p_textures
	_cache_key = cache_key
	_playback = (
		bool(opts.get("animate", false))
		and model != null
		and (
			not model.sequences.is_empty()
			or model.has_layer_animation()
			or not model.particle_emitters2.is_empty()
			or not model.lights.is_empty()
		)
	)
	_animate = _playback and not model.bones.is_empty()
	_proximity_gate = bool(opts.get("proximity_gate", false)) and _playback
	_cliff_remap = bool(opts.get("cliff_remap", false))
	_cliff_displace = opts.has("cliff_ground") or opts.has("cliff_ground_grid")
	_cliff_ground = opts.get("cliff_ground", Vector4.ZERO) as Vector4
	_cliff_ground_grid = opts.get("cliff_ground_grid", PackedFloat32Array()) as PackedFloat32Array
	_cliff_ground_grid_size = opts.get("cliff_ground_grid_size", Vector2i.ZERO) as Vector2i
	_replaceable_tex = str(opts.get("replaceable_texture", ""))
	_geoset_nodes.clear()
	_bind_geosets.clear()
	_skinned_arrays.clear()
	_particle_nodes.clear()
	_light_nodes.clear()
	_skeleton = null
	_skin = null
	_bone_index_by_oid.clear()
	_material_tracks.clear()
	_anim_active = not _proximity_gate
	_skin_frame = 0
	_anim_time = 0.0
	_global_time = 0.0
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
			# Material flipbooks need an instance-local material. Sharing a bank
			# would make every copy overwrite the same KMTF/KMTA frame.
			if not _playback and not bank_key.is_empty() and _mesh_bank.has(bank_key):
				_attach_bank(_mesh_bank[bank_key])
			else:
				var bank: Array = _build_meshes(opts)
				if not _playback and not bank_key.is_empty():
					_mesh_bank[bank_key] = bank
	_bind_material_tracks(opts)
	_build_model_effects(opts)
	play_idle()
	if _proximity_gate:
		set_anim_active(false)


func set_anim_active(on: bool) -> void:
	if not _playback:
		return
	if on == _anim_active:
		set_process(on)
		return
	_anim_active = on
	set_process(on)
	if on:
		_update_material_tracks()
		if _animate:
			_skin_at_current_time()
	else:
		if _animate:
			_restore_bind_pose()
	_set_effects_active(on)


func is_anim_active() -> bool:
	return _anim_active


func get_current_sequence_name() -> String:
	if model == null or _seq_index < 0 or _seq_index >= model.sequences.size():
		return ""
	return str(model.sequences[_seq_index].name)


func _restore_bind_pose() -> void:
	if _skeleton != null and is_instance_valid(_skeleton):
		for bone_index in _skeleton.get_bone_count():
			_skeleton.set_bone_pose_position(bone_index, Vector3.ZERO)
			_skeleton.set_bone_pose_rotation(bone_index, Quaternion.IDENTITY)
			_skeleton.set_bone_pose_scale(bone_index, Vector3.ONE)
	for mi in _geoset_nodes:
		if mi == null or not is_instance_valid(mi):
			continue
		var gid := int(mi.get_meta("geoset_id", -1))
		if gid >= 0 and gid < _skinned_arrays.size() and _skinned_arrays[gid] != null:
			mi.mesh = _skinned_arrays[gid]
	_apply_geoset_visibility()


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
		if not bool(entry.get("casts_shadow", true)):
			mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_mesh_root.add_child(mi)
		_geoset_nodes.append(mi)


## WC3 filter modes 0 (opaque) and 1 (cutout) write depth and cast shadows.
## Blend/additive/modulate layers (2+) render into Godot's shadow map as solid
## quads otherwise — team glows became dark rectangles under unit feet.
func _geoset_casts_shadow(g) -> bool:
	if model == null or model.materials.is_empty():
		return true
	var mid = clampi(g.material_id, 0, model.materials.size() - 1)
	var layers: Array = model.materials[mid]
	if layers.is_empty():
		return true
	return int(layers[0].filter_mode) <= 1


func _make_material(g, team_color: int, cliff_tex_override: String) -> Material:
	var tex_path = _texture_for_geoset(g, team_color, cliff_tex_override)
	if _cliff_remap:
		# Ground and cliff surfaces must use the same HiveWE-style Lambert curve.
		# A Godot StandardMaterial lights the MDX normals differently and exposes
		# every 128x128 cliff top as a dark square against the GPU ground.
		var cliff_mat := ShaderMaterial.new()
		cliff_mat.shader = load("res://addons/w3kit/shaders/w3_cliff_lit.gdshader")
		if not tex_path.is_empty() and textures != null:
			cliff_mat.set_shader_parameter("albedo_tex", textures.get_texture(tex_path))
		cliff_mat.set_shader_parameter("light_dir", Vector3(0.45, 0.85, 0.28).normalized())
		cliff_mat.set_shader_parameter("light_ambient", 0.35)
		cliff_mat.set_shader_parameter("light_scale", 1.1)
		return cliff_mat
	var layers: Array = []
	if not model.materials.is_empty():
		var mid = clampi(g.material_id, 0, model.materials.size() - 1)
		layers = model.materials[mid]
	if layers.is_empty():
		return _make_layer_material(null, tex_path, 0)
	if int(layers[0].shader) == 1:
		return _make_hd_material(layers[0], team_color, cliff_tex_override)
	var first: StandardMaterial3D = null
	var previous: StandardMaterial3D = null
	for layer in layers:
		var layer_path := ""
		if int(layer.texture_id) >= 0 and int(layer.texture_id) < model.textures.size():
			layer_path = _resolve_texture_slot(int(layer.texture_id), team_color, cliff_tex_override)
		var layer_mat := _make_layer_material(layer, layer_path, int(layer.filter_mode))
		if first == null:
			first = layer_mat
		if previous != null:
			previous.next_pass = layer_mat
		previous = layer_mat
	return first


func _make_hd_material(layer, team_color: int, cliff_tex_override: String) -> ShaderMaterial:
	# Warsmash Shader_HD_DefaultUnit and HiveWE use six fixed slots:
	# diffuse, normal, ORM, emissive, team color, environment.
	var material := ShaderMaterial.new()
	material.shader = load(_hd_shader_path(int(layer.filter_mode)))
	var ids: PackedInt32Array = layer.texture_ids
	for slot in mini(6, ids.size()):
		var path := _resolve_hd_texture_slot(ids[slot], slot, team_color, cliff_tex_override)
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


func _bind_material_tracks(opts: Dictionary) -> void:
	if not _playback or model == null:
		return
	if model.materials.is_empty():
		return
	var team_color := int(opts.get("team_color", 0))
	var cliff_texture := str(opts.get("cliff_texture", ""))
	for mi in _geoset_nodes:
		if mi == null or not is_instance_valid(mi):
			continue
		var geoset_id := int(mi.get_meta("geoset_id", -1))
		if geoset_id < 0 or geoset_id >= model.geosets.size():
			continue
		var material_id := clampi(int(model.geosets[geoset_id].material_id), 0, model.materials.size() - 1)
		var layers: Array = model.materials[material_id]
		var material_pass: Material = mi.material_override
		for layer in layers:
			if material_pass == null:
				break
			if not layer.alpha_times.is_empty() or _layer_has_texture_track(layer):
				_material_tracks.append({
					"layer": layer,
					"material": material_pass,
					"team_color": team_color,
					"cliff_texture": cliff_texture,
					"last_texture_ids": PackedInt32Array(),
				})
			material_pass = material_pass.next_pass


func _layer_has_texture_track(layer) -> bool:
	for times in layer.texture_track_times:
		if times is PackedInt32Array and not times.is_empty():
			return true
	return false


func _material_track_clock(global_sequence_id: int) -> Dictionary:
	if global_sequence_id >= 0 and global_sequence_id < model.global_sequences.size():
		var duration := maxi(1, int(model.global_sequences[global_sequence_id]))
		return {"time": int(_global_time) % duration, "start": -1, "end": -1}
	var start := -1
	var end := -1
	if _seq_index >= 0 and _seq_index < model.sequences.size():
		start = int(model.sequences[_seq_index].interval_start)
		end = int(model.sequences[_seq_index].interval_end)
	return {"time": _current_time_ms(), "start": start, "end": end}


func _update_material_tracks() -> void:
	for entry in _material_tracks:
		var layer = entry["layer"]
		var material: Material = entry["material"]
		if material == null or not is_instance_valid(material):
			continue
		var alpha_clock := _material_track_clock(int(layer.alpha_global_sequence))
		var alpha := W3MdxAnim.sample_float(
			layer.alpha_times, layer.alpha_values, int(alpha_clock["time"]), float(layer.alpha),
			int(alpha_clock["start"]), int(alpha_clock["end"])
		)
		if material is ShaderMaterial:
			(material as ShaderMaterial).set_shader_parameter("layer_alpha", clampf(alpha, 0.0, 1.0))
		elif material is StandardMaterial3D:
			var color := (material as StandardMaterial3D).albedo_color
			color.a = clampf(alpha, 0.0, 1.0)
			(material as StandardMaterial3D).albedo_color = color

		var ids: PackedInt32Array = layer.texture_ids
		var slots: PackedInt32Array = layer.texture_slots
		var last_ids: PackedInt32Array = entry["last_texture_ids"]
		if last_ids.size() != ids.size():
			last_ids.resize(ids.size())
			last_ids.fill(-2147483648)
		for texture_index in mini(ids.size(), layer.texture_track_times.size()):
			var times: PackedInt32Array = layer.texture_track_times[texture_index]
			if times.is_empty() or texture_index >= layer.texture_track_values.size():
				continue
			var values: PackedInt32Array = layer.texture_track_values[texture_index]
			var global_sequence_id := -1
			if texture_index < layer.texture_track_global_sequences.size():
				global_sequence_id = int(layer.texture_track_global_sequences[texture_index])
			var clock := _material_track_clock(global_sequence_id)
			var texture_id := W3MdxAnim.sample_int_step(
				times, values, int(clock["time"]), int(ids[texture_index]),
				int(clock["start"]), int(clock["end"])
			)
			if last_ids[texture_index] == texture_id:
				continue
			last_ids[texture_index] = texture_id
			var pbr_slot := int(slots[texture_index]) if texture_index < slots.size() else texture_index
			if material is ShaderMaterial:
				if pbr_slot < 0 or pbr_slot >= 6:
					continue
				var path := _resolve_hd_texture_slot(
					texture_id, pbr_slot, int(entry["team_color"]), str(entry["cliff_texture"]))
				if not path.is_empty() and textures != null:
					var uniform_name: String = [
						"diffuse_map", "normal_map", "orm_map", "emissive_map", "team_color_map", "environment_map"
					][pbr_slot]
					(material as ShaderMaterial).set_shader_parameter(uniform_name, textures.get_texture(path))
			elif material is StandardMaterial3D:
				var sd_path := _resolve_texture_slot(texture_id, int(entry["team_color"]), str(entry["cliff_texture"]))
				if not sd_path.is_empty() and textures != null:
					(material as StandardMaterial3D).albedo_texture = textures.get_texture(sd_path)
		entry["last_texture_ids"] = last_ids


func _hd_shader_path(filter_mode: int) -> String:
	if filter_mode <= 1:
		return "res://addons/w3kit/shaders/w3_reforged_unit_opaque.gdshader"
	if filter_mode == 3 or filter_mode == 4:
		return "res://addons/w3kit/shaders/w3_reforged_unit_add.gdshader"
	if filter_mode == 5 or filter_mode == 6:
		return "res://addons/w3kit/shaders/w3_reforged_unit_modulate.gdshader"
	return "res://addons/w3kit/shaders/w3_reforged_unit.gdshader"


func _make_layer_material(layer, tex_path: String, layer_fm: int) -> StandardMaterial3D:
	var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
	var mat = StandardMaterial3D.new()
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
	W3War3Lit.style_model_material(mat)
	if layer != null:
		mat.albedo_color.a = clampf(float(layer.alpha), 0.0, 1.0)
		if (int(layer.flags) & 0x1) != 0: # Unshaded
			mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
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
		# Warsmash SetupGeosets: render only base LOD (0 or legacy -1).
		if g.lod != 0 and g.lod != -1:
			continue
		var mesh := _commit_geoset_mesh(g, g.vertices, g.normals)
		var mi = MeshInstance3D.new()
		mi.mesh = mesh
		mi.material_override = _make_material(g, team_color, cliff_tex_override)
		mi.set_meta("geoset_id", gi)
		var casts := _geoset_casts_shadow(g)
		if not casts:
			mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_mesh_root.add_child(mi)
		_geoset_nodes.append(mi)
		bank.append({"mesh": mesh, "mat": mi.material_override, "geoset_id": gi, "casts_shadow": casts})
	return bank


func _build_animated_meshes(opts: Dictionary = {}) -> void:
	var team_color: int = int(opts.get("team_color", 0))
	var cliff_tex_override: String = str(opts.get("cliff_texture", ""))
	_build_gpu_skeleton()
	for gi in model.geosets.size():
		var g = model.geosets[gi]
		if g.lod != 0 and g.lod != -1:
			continue
		_bind_geosets.append(g)
		var mesh := _commit_gpu_skinned_mesh(g)
		var mi = MeshInstance3D.new()
		mi.mesh = mesh
		mi.skin = _skin
		mi.material_override = _make_material(g, team_color, cliff_tex_override)
		mi.set_meta("geoset_id", gi)
		if not _geoset_casts_shadow(g):
			mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_mesh_root.add_child(mi)
		mi.skeleton = mi.get_path_to(_skeleton)
		_geoset_nodes.append(mi)
		_skinned_arrays.append(mesh)


func _build_gpu_skeleton() -> void:
	_skeleton = Skeleton3D.new()
	_skeleton.name = "MdxSkeleton"
	add_child(_skeleton)
	for bone in model.bones:
		var index := _skeleton.get_bone_count()
		var safe_name := str(bone.name).strip_edges()
		for invalid in [":", "/", "\\", "%"]:
			safe_name = safe_name.replace(invalid, "_")
		if safe_name.is_empty():
			safe_name = "Bone"
		# Object IDs are unique in MDX and make duplicate authored helper names
		# legal in Godot's Skeleton3D name table.
		safe_name = "%s_%d" % [safe_name, int(bone.object_id)]
		_skeleton.add_bone(safe_name)
		_skeleton.set_bone_rest(index, Transform3D.IDENTITY)
		_bone_index_by_oid[int(bone.object_id)] = index
	for bone in model.bones:
		var index := int(_bone_index_by_oid.get(int(bone.object_id), -1))
		var parent_index := int(_bone_index_by_oid.get(int(bone.parent_id), -1))
		if index >= 0 and parent_index >= 0 and parent_index != index:
			_skeleton.set_bone_parent(index, parent_index)
	_skin = Skin.new()
	for bone_index in _skeleton.get_bone_count():
		_skin.add_bind(bone_index, Transform3D.IDENTITY)


func _commit_gpu_skinned_mesh(g) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var nvert: int = g.vertices.size()
	for fi in range(0, g.faces.size() - 2, 3):
		for k in 3:
			var vertex_index: int = g.faces[fi + k]
			if vertex_index < 0 or vertex_index >= nvert:
				continue
			var bones := PackedInt32Array([0, 0, 0, 0])
			var weights := PackedFloat32Array([0.0, 0.0, 0.0, 0.0])
			var weight_sum := 0.0
			for influence in 4:
				var source_index := vertex_index * 4 + influence
				if source_index >= g.bone_indices.size() or source_index >= g.bone_weights.size():
					continue
				var object_id := int(g.bone_indices[source_index])
				var weight := float(g.bone_weights[source_index])
				if weight <= 0.0001 or not _bone_index_by_oid.has(object_id):
					continue
				bones[influence] = int(_bone_index_by_oid[object_id])
				weights[influence] = weight
				weight_sum += weight
			if weight_sum <= 0.0001:
				weights[0] = 1.0
			elif absf(weight_sum - 1.0) > 0.001:
				for influence in 4:
					weights[influence] /= weight_sum
			st.set_bones(bones)
			st.set_weights(weights)
			st.set_normal(g.normals[vertex_index] if vertex_index < g.normals.size() else Vector3.UP)
			if g.tangents.size() >= (vertex_index + 1) * 4:
				var tangent_index := vertex_index * 4
				st.set_tangent(Plane(
					float(g.tangents[tangent_index]), float(g.tangents[tangent_index + 1]),
					float(g.tangents[tangent_index + 2]), float(g.tangents[tangent_index + 3])))
			if vertex_index < g.uvs.size():
				st.set_uv(g.uvs[vertex_index])
			st.add_vertex(g.vertices[vertex_index])
	return st.commit()


func _build_model_effects(opts: Dictionary = {}) -> void:
	if model == null or not _animate:
		return
	var team_color := int(opts.get("team_color", 0))
	for emitter in model.particle_emitters2:
		var particles := GPUParticles3D.new()
		particles.name = str(emitter.node.name)
		particles.local_coords = true
		particles.amount = clampi(ceili(maxf(1.0, emitter.emission_rate) * maxf(0.05, emitter.life_span)), 1, 512)
		particles.lifetime = maxf(0.05, emitter.life_span)
		particles.visibility_aabb = AABB(Vector3(-256, -256, -256), Vector3(512, 512, 512))
		var process_mat := ParticleProcessMaterial.new()
		process_mat.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
		process_mat.emission_box_extents = Vector3(maxf(0.01, emitter.width * 0.5), 0.01, maxf(0.01, emitter.length * 0.5))
		process_mat.direction = Vector3.UP
		process_mat.spread = clampf(rad_to_deg(emitter.latitude), 0.0, 180.0) if absf(emitter.latitude) <= TAU else clampf(emitter.latitude, 0.0, 180.0)
		process_mat.gravity = Vector3(0.0, -emitter.gravity, 0.0)
		process_mat.initial_velocity_min = maxf(0.0, emitter.speed - absf(emitter.speed_variation))
		process_mat.initial_velocity_max = maxf(process_mat.initial_velocity_min, emitter.speed + absf(emitter.speed_variation))
		if emitter.colors.size() >= 3:
			var gradient := Gradient.new()
			gradient.set_color(0, emitter.colors[0])
			gradient.add_point(clampf(emitter.time_middle, 0.001, 0.999), emitter.colors[1])
			gradient.set_color(gradient.get_point_count() - 1, emitter.colors[2])
			var gradient_tex := GradientTexture1D.new()
			gradient_tex.gradient = gradient
			process_mat.color_ramp = gradient_tex
		if emitter.scaling.size() >= 3:
			var scale_curve := Curve.new()
			scale_curve.min_value = 0.0
			scale_curve.max_value = maxf(1.0, maxf(emitter.scaling[0], maxf(emitter.scaling[1], emitter.scaling[2])))
			scale_curve.add_point(Vector2(0.0, emitter.scaling[0]))
			scale_curve.add_point(Vector2(clampf(emitter.time_middle, 0.001, 0.999), emitter.scaling[1]))
			scale_curve.add_point(Vector2(1.0, emitter.scaling[2]))
			var scale_tex := CurveTexture.new()
			scale_tex.curve = scale_curve
			process_mat.scale_curve = scale_tex
		particles.process_material = process_mat
		var quad := QuadMesh.new()
		quad.size = Vector2.ONE
		var draw_mat := StandardMaterial3D.new()
		draw_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		draw_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		draw_mat.billboard_mode = BaseMaterial3D.BILLBOARD_PARTICLES
		draw_mat.vertex_color_use_as_albedo = true
		draw_mat.particles_anim_h_frames = maxi(1, emitter.columns)
		draw_mat.particles_anim_v_frames = maxi(1, emitter.rows)
		draw_mat.particles_anim_loop = false
		if emitter.filter_mode == 1 or emitter.filter_mode == 3 or emitter.filter_mode == 4:
			draw_mat.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
		var particle_path := _particle_texture_path(emitter.texture_id, team_color)
		if not particle_path.is_empty() and textures != null:
			draw_mat.albedo_texture = textures.get_texture(particle_path)
		quad.material = draw_mat
		particles.draw_pass_1 = quad
		particles.emitting = false
		add_child(particles)
		_particle_nodes.append({"emitter": emitter, "node": particles})
	for light in model.lights:
		# Lost Temple's HD model lights are omni lights. Keep the parser data for
		# other types, but avoid turning model-local directional/ambient entries
		# into global scene lights.
		if int(light.type) != 0:
			continue
		var omni := OmniLight3D.new()
		omni.name = str(light.node.name)
		omni.shadow_enabled = false
		omni.omni_range = maxf(1.0, light.attenuation_end)
		omni.light_color = light.color
		omni.light_energy = 0.0
		add_child(omni)
		_light_nodes.append({"light": light, "node": omni})


func _particle_texture_path(tex_index: int, team_color: int) -> String:
	if tex_index < 0 or tex_index >= model.textures.size():
		return ""
	if model.version > 800 and not str(model.textures[tex_index]).is_empty():
		return str(model.textures[tex_index]).get_basename() + ".dds"
	return _resolve_texture_slot(tex_index, team_color)


func _set_effects_active(on: bool) -> void:
	for entry in _particle_nodes:
		var particles: GPUParticles3D = entry["node"]
		if is_instance_valid(particles):
			particles.emitting = on and particles.emitting
	for entry in _light_nodes:
		var light: Light3D = entry["node"]
		if is_instance_valid(light) and not on:
			light.light_energy = 0.0


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
				vtx.y += _sample_cliff_ground(vtx.x, vtx.z) * 128.0
			st.set_normal(nrm)
			if g.tangents.size() >= (idx + 1) * 4:
				var tangent := Vector3(g.tangents[idx * 4], g.tangents[idx * 4 + 1], g.tangents[idx * 4 + 2])
				if _cliff_remap:
					tangent = Vector3(tangent.z, tangent.y, -tangent.x).normalized()
				st.set_tangent(Plane(tangent, g.tangents[idx * 4 + 3]))
			if idx < g.uvs.size():
				st.set_uv(g.uvs[idx])
			st.add_vertex(vtx)
	return st.commit()


func _sample_cliff_ground(local_x: float, local_z: float) -> float:
	var grid_w := _cliff_ground_grid_size.x
	var grid_h := _cliff_ground_grid_size.y
	if grid_w >= 2 and grid_h >= 2 and _cliff_ground_grid.size() >= grid_w * grid_h:
		# Bilinear sampling matches terrain interpolation while allowing a
		# two-tile transition model to reach the third corner row/column.
		var gx := clampf(local_x / 128.0, 0.0, float(grid_w - 1))
		var gz := clampf(local_z / 128.0, 0.0, float(grid_h - 1))
		var x0 := floori(gx)
		var z0 := floori(gz)
		var x1 := mini(x0 + 1, grid_w - 1)
		var z1 := mini(z0 + 1, grid_h - 1)
		var tx := gx - float(x0)
		var tz := gz - float(z0)
		var g0 := lerpf(_cliff_ground_grid[z0 * grid_w + x0], _cliff_ground_grid[z0 * grid_w + x1], tx)
		var g1 := lerpf(_cliff_ground_grid[z1 * grid_w + x0], _cliff_ground_grid[z1 * grid_w + x1], tx)
		return lerpf(g0, g1, tz)
	# Backward-compatible one-tile path for callers that only provide Vector4.
	var u := clampf(local_x / 128.0, 0.0, 1.0)
	var v := clampf(local_z / 128.0, 0.0, 1.0)
	var fallback0 := lerpf(_cliff_ground.x, _cliff_ground.y, u)
	var fallback1 := lerpf(_cliff_ground.z, _cliff_ground.w, u)
	return lerpf(fallback0, fallback1, v)


func _texture_for_geoset(g, team_color: int = 0, cliff_tex_override: String = "") -> String:
	# Reforged cliff MDX deliberately authors #ignore_Cliffs texture names.
	# HiveWE/Warsmash bind the CliffTypes texture supplied by the terrain, so it
	# must win even though the model's texture slot is non-empty and rid is zero.
	if _cliff_remap and not cliff_tex_override.is_empty():
		return cliff_tex_override
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


func _resolve_hd_texture_slot(tex_index: int, pbr_slot: int, team_color: int = 0, cliff_tex_override: String = "") -> String:
	if tex_index < 0 or tex_index >= model.textures.size():
		return ""
	var authored: String = model.textures[tex_index]
	if not authored.is_empty():
		return authored.get_basename() + ".dds"
	var rid := int(model.texture_replaceable_ids[tex_index]) if tex_index < model.texture_replaceable_ids.size() else 0
	if rid == 11 and not cliff_tex_override.is_empty():
		return cliff_tex_override.get_basename() + _hd_slot_suffix(pbr_slot) + ".dds"
	if rid == 1 or rid == 2:
		var team_stem := "ReplaceableTextures/TeamColor/TeamColor" if rid == 1 else "ReplaceableTextures/TeamGlow/TeamGlow"
		return "%s%02d.dds" % [team_stem, clampi(team_color, 0, 23)]
	var stem := ""
	if not _replaceable_tex.is_empty() and rid >= 31:
		stem = _replaceable_tex.replace("\\", "/").get_basename()
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


func play_named(seq_name: String, restart: bool = false) -> void:
	if model == null:
		return
	var idx = model.find_sequence(seq_name)
	if idx < 0:
		idx = model.find_idle_sequence()
	_loop = true
	_apply_sequence_index(idx, restart)


## One-shot playback (Death/Decay): holds the final frame instead of looping.
## Returns false when the model has no such sequence.
func play_named_once(seq_name: String) -> bool:
	if model == null:
		return false
	var idx = model.find_sequence(seq_name)
	if idx < 0:
		return false
	_loop = false
	_apply_sequence_index(idx, true)
	return true


func play_idle() -> void:
	if model == null:
		return
	_loop = true
	_apply_sequence_index(model.find_idle_sequence())


func _apply_sequence_index(idx: int, restart: bool = false) -> void:
	if idx < 0:
		_seq_index = -1
		return
	if idx == _seq_index and not restart:
		_apply_geoset_visibility()
		_update_material_tracks()
		if _animate and _anim_active:
			_skin_at_current_time()
		return
	_seq_index = idx
	_anim_time = 0.0
	_apply_geoset_visibility()
	_update_material_tracks()
	if _animate and _anim_active:
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
	if _skeleton != null and is_instance_valid(_skeleton):
		_update_gpu_skeleton(worlds)
		_update_model_effects(worlds, time_ms, seq_start, seq_end)
		return
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
	_update_model_effects(worlds, time_ms, seq_start, seq_end)


func _update_gpu_skeleton(worlds: Dictionary) -> void:
	for bone in model.bones:
		var object_id := int(bone.object_id)
		var bone_index := int(_bone_index_by_oid.get(object_id, -1))
		if bone_index < 0 or not worlds.has(object_id):
			continue
		var local_war3: Transform3D = worlds[object_id]
		var parent_id := int(bone.parent_id)
		if parent_id >= 0 and worlds.has(parent_id):
			local_war3 = (worlds[parent_id] as Transform3D).affine_inverse() * local_war3
		var local_godot := _war3_transform_to_godot(local_war3)
		var pose_scale := local_godot.basis.get_scale()
		var pose_rotation := local_godot.basis.orthonormalized().get_rotation_quaternion()
		_skeleton.set_bone_pose_position(bone_index, local_godot.origin)
		_skeleton.set_bone_pose_rotation(bone_index, pose_rotation)
		_skeleton.set_bone_pose_scale(bone_index, pose_scale)


func _update_model_effects(worlds: Dictionary, time_ms: int, seq_start: int, seq_end: int) -> void:
	for entry in _particle_nodes:
		var emitter = entry["emitter"]
		var particles: GPUParticles3D = entry["node"]
		if not is_instance_valid(particles):
			continue
		if worlds.has(int(emitter.node.object_id)):
			particles.transform = _war3_transform_to_godot(worlds[int(emitter.node.object_id)])
		var visibility := W3MdxAnim.sample_float(
			emitter.visibility_times, emitter.visibility_values, time_ms, 1.0, seq_start, seq_end)
		var rate := W3MdxAnim.sample_float(
			emitter.emission_rate_times, emitter.emission_rate_values, time_ms,
			emitter.emission_rate, seq_start, seq_end)
		particles.emitting = _anim_active and visibility > 0.01 and rate > 0.01
	for entry in _light_nodes:
		var info = entry["light"]
		var light: OmniLight3D = entry["node"]
		if not is_instance_valid(light):
			continue
		if worlds.has(int(info.node.object_id)):
			light.transform = _war3_transform_to_godot(worlds[int(info.node.object_id)])
		var visibility := W3MdxAnim.sample_float(
			info.visibility_times, info.visibility_values, time_ms, 1.0, seq_start, seq_end)
		var intensity := W3MdxAnim.sample_float(
			info.intensity_times, info.intensity_values, time_ms, info.intensity, seq_start, seq_end)
		var attenuation_end := W3MdxAnim.sample_float(
			info.attenuation_end_times, info.attenuation_end_values, time_ms,
			info.attenuation_end, seq_start, seq_end)
		var color_vec := W3MdxAnim.sample_vec3(
			info.color_times, info.color_values, time_ms,
			Vector3(info.color.r, info.color.g, info.color.b), seq_start, seq_end)
		light.light_color = Color(color_vec.x, color_vec.y, color_vec.z)
		light.omni_range = maxf(1.0, attenuation_end)
		light.light_energy = maxf(0.0, intensity * visibility) if _anim_active else 0.0


func _war3_transform_to_godot(xf: Transform3D) -> Transform3D:
	# P * M * P^-1 for the War3 (X,Y,Z-up) -> Godot (X,Y-up,Z) axis swap.
	var gx := W3MdxModel.w3_to_godot(xf.basis * Vector3(1, 0, 0))
	var gy := W3MdxModel.w3_to_godot(xf.basis * Vector3(0, 0, 1))
	var gz := W3MdxModel.w3_to_godot(xf.basis * Vector3(0, 1, 0))
	return Transform3D(Basis(gx, gy, gz), W3MdxModel.w3_to_godot(xf.origin))


func _process(delta: float) -> void:
	if not _playing or not _anim_active or model == null or not _playback:
		return
	_global_time += delta * 1000.0
	if _seq_index < 0 or _seq_index >= model.sequences.size():
		_update_material_tracks()
		return
	var s = model.sequences[_seq_index]
	var span = float(maxi(1, s.interval_end - s.interval_start))
	if _loop:
		# Warsmash War3MapViewer.standOnRepeat() chooses one Stand at spawn, then
		# keeps that selected sequence in ALWAYS_LOOP mode. The modulo is that
		# equivalent: different monsters may have different Stand variants, while
		# a given monster's chosen idle loops smoothly.
		_anim_time = fmod(_anim_time + delta * 1000.0, span)
	else:
		# One-shot (Death): advance to the last frame and hold it.
		_anim_time = minf(_anim_time + delta * 1000.0, span - 1.0)
	_apply_geoset_visibility()
	_update_material_tracks()
	if not _animate:
		return
	_skin_frame += 1
	if (_skin_frame % SKIN_EVERY_N_FRAMES) == 0:
		_skin_at_current_time()
