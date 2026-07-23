extends RefCounted
const W3BinaryReader = preload("res://addons/w3kit/w3_binary_reader.gd")
const MAX_ELEMENTS := 2_000_000
const MAX_TRACKS := 1_000_000
const MAX_LAYERS := 4096
## Minimal MDX800 loader: geosets + sequences + bones + simple keyframe anim.


class Sequence:
	var name: String = ""
	var interval_start: int = 0
	var interval_end: int = 0
	var move_speed: float = 0.0
	var flags: int = 0
	var rarity: float = 0.0
	var sync_point: int = 0
	var extent_min: Vector3 = Vector3.ZERO
	var extent_max: Vector3 = Vector3.ZERO
	var extent_radius: float = 0.0


class Geoset:
	var vertices: PackedVector3Array = PackedVector3Array() ## Godot Y-up
	var normals: PackedVector3Array = PackedVector3Array()
	var vertices_war3: PackedVector3Array = PackedVector3Array() ## Z-up bind pose for skinning
	var normals_war3: PackedVector3Array = PackedVector3Array()
	var uvs: PackedVector2Array = PackedVector2Array()
	var faces: PackedInt32Array = PackedInt32Array()
	var material_id: int = 0
	var lod: int = 0
	var lod_name: String = ""
	var vertex_groups: PackedByteArray = PackedByteArray() ## GNDX
	var matrix_group_sizes: PackedInt32Array = PackedInt32Array() ## MTGC
	var matrix_indices: PackedInt32Array = PackedInt32Array() ## MATS bone object ids
	var bone_indices: PackedInt32Array = PackedInt32Array() # 4 object_ids per vert
	var bone_weights: PackedFloat32Array = PackedFloat32Array()
	var tangents: PackedFloat32Array = PackedFloat32Array() # Godot xyz + handedness, 4 floats/vertex


class Bone:
	var name: String = ""
	var object_id: int = -1
	var parent_id: int = -1
	var pivot: Vector3 = Vector3.ZERO ## War3 Z-up
	var translation_times: PackedInt32Array = PackedInt32Array()
	var translation_values: PackedVector3Array = PackedVector3Array() ## War3 Z-up
	var rotation_times: PackedInt32Array = PackedInt32Array()
	var rotation_values: Array = [] # Quaternion (War3 space)
	var scale_times: PackedInt32Array = PackedInt32Array()
	var scale_values: PackedVector3Array = PackedVector3Array()


class LayerInfo:
	var texture_id: int = 0
	var texture_ids: PackedInt32Array = PackedInt32Array() # HD PBR slots
	var texture_slots: PackedInt32Array = PackedInt32Array()
	## One KMTF flipbook track per texture entry. Arrays are kept parallel to
	## texture_ids so v1000 HD PBR layers and v1100 explicit slots both work.
	var texture_track_times: Array = [] # PackedInt32Array[]
	var texture_track_values: Array = [] # PackedInt32Array[] (texture IDs)
	var texture_track_global_sequences: PackedInt32Array = PackedInt32Array()
	var filter_mode: int = 0
	var flags: int = 0
	var alpha: float = 1.0
	var alpha_times: PackedInt32Array = PackedInt32Array()
	var alpha_values: PackedFloat32Array = PackedFloat32Array()
	var alpha_global_sequence: int = -1
	var emissive_gain: float = 1.0
	var fresnel_color: Color = Color.WHITE
	var fresnel_opacity: float = 0.0
	var fresnel_team_color: float = 0.0
	var shader: int = 0 # 0=SD, 1=HD, 2=SDOnHD (v1100)


class GeosetAnim:
	var geoset_id: int = -1
	var static_alpha: float = 1.0
	var alpha_times: PackedInt32Array = PackedInt32Array()
	var alpha_values: PackedFloat32Array = PackedFloat32Array()


class Attachment:
	var node: Bone
	var path: String = ""
	var attachment_id: int = 0


class ParticleEmitter2:
	var node: Bone
	var speed: float = 0.0
	var speed_variation: float = 0.0
	var latitude: float = 0.0
	var gravity: float = 0.0
	var life_span: float = 0.0
	var emission_rate: float = 0.0
	var length: float = 0.0
	var width: float = 0.0
	var filter_mode: int = 0
	var rows: int = 1
	var columns: int = 1
	var head_or_tail: int = 0
	var tail_length: float = 0.0
	var time_middle: float = 0.5
	var colors: PackedColorArray = PackedColorArray()
	var scaling: PackedFloat32Array = PackedFloat32Array()
	var texture_id: int = -1
	var replaceable_id: int = 0
	var visibility_times: PackedInt32Array = PackedInt32Array()
	var visibility_values: PackedFloat32Array = PackedFloat32Array()
	var emission_rate_times: PackedInt32Array = PackedInt32Array()
	var emission_rate_values: PackedFloat32Array = PackedFloat32Array()


class EventObject:
	var node: Bone
	var global_sequence_id: int = -1
	var times: PackedInt32Array = PackedInt32Array()


class LightInfo:
	var node: Bone
	var type: int = 0
	var attenuation_start: float = 0.0
	var attenuation_end: float = 0.0
	var color: Color = Color.WHITE
	var intensity: float = 1.0
	var ambient_color: Color = Color.WHITE
	var ambient_intensity: float = 0.0
	var intensity_times: PackedInt32Array = PackedInt32Array()
	var intensity_values: PackedFloat32Array = PackedFloat32Array()
	var visibility_times: PackedInt32Array = PackedInt32Array()
	var visibility_values: PackedFloat32Array = PackedFloat32Array()
	var color_times: PackedInt32Array = PackedInt32Array()
	var color_values: PackedVector3Array = PackedVector3Array()
	var attenuation_end_times: PackedInt32Array = PackedInt32Array()
	var attenuation_end_values: PackedFloat32Array = PackedFloat32Array()


## mdx-m3-viewer replaceableids.js — empty TEXS path + replaceableId → this stem + .blp
const REPLACEABLE_STEMS := {
	1: "ReplaceableTextures/TeamColor/TeamColor00",
	2: "ReplaceableTextures/TeamGlow/TeamGlow00",
	11: "ReplaceableTextures/Cliff/Cliff0",
	31: "ReplaceableTextures/LordaeronTree/LordaeronSummerTree",
	32: "ReplaceableTextures/AshenvaleTree/AshenTree",
	33: "ReplaceableTextures/BarrensTree/BarrensTree",
	34: "ReplaceableTextures/NorthrendTree/NorthTree",
	35: "ReplaceableTextures/Mushroom/MushroomTree",
	36: "ReplaceableTextures/RuinsTree/RuinsTree",
	37: "ReplaceableTextures/OutlandMushroomTree/MushroomTree",
}

var name: String = ""
## MDX version from VERS (800 classic, 900+ Reforged extras).
var version: int = 800
var sequences: Array = [] # Sequence
var global_sequences: PackedInt32Array = PackedInt32Array() # GLBS durations (ms)
var geosets: Array = [] # Geoset
var bones: Array = [] # Bone
var textures: PackedStringArray = PackedStringArray()
## Parallel to textures[] — 0 means use path; else replaceable table when path empty.
var texture_replaceable_ids: PackedInt32Array = PackedInt32Array()
var materials: Array = [] # Array of Array[LayerInfo]
var pivots: PackedVector3Array = PackedVector3Array()
## geoset_id → GeosetAnim (mdx-m3-viewer GEOA)
var geoset_anims: Dictionary = {}
var attachments: Array = [] # Attachment
var particle_emitters2: Array = [] # ParticleEmitter2
var event_objects: Array = [] # EventObject
var lights: Array = [] # LightInfo


func has_layer_animation() -> bool:
	for material_layers in materials:
		for layer in material_layers:
			if not layer.alpha_times.is_empty():
				return true
			for times in layer.texture_track_times:
				if times is PackedInt32Array and not times.is_empty():
					return true
	return false


func has_layer_animation_for_sequence(sequence_index: int) -> bool:
	if sequence_index < 0 or sequence_index >= sequences.size():
		return false
	var sequence = sequences[sequence_index]
	for material_layers in materials:
		for layer in material_layers:
			if _track_runs_in_sequence(layer.alpha_times, int(layer.alpha_global_sequence), sequence):
				return true
			for track_index in layer.texture_track_times.size():
				var global_sequence := -1
				if track_index < layer.texture_track_global_sequences.size():
					global_sequence = int(layer.texture_track_global_sequences[track_index])
				if _track_runs_in_sequence(layer.texture_track_times[track_index], global_sequence, sequence):
					return true
	return false


func has_idle_layer_animation() -> bool:
	return has_layer_animation_for_sequence(find_idle_sequence())


func _track_runs_in_sequence(times: PackedInt32Array, global_sequence_id: int, sequence) -> bool:
	if times.is_empty():
		return false
	if global_sequence_id >= 0 and global_sequence_id < global_sequences.size():
		return true
	for time in times:
		if int(time) >= int(sequence.interval_start) and int(time) <= int(sequence.interval_end):
			return true
	return false


func has_sequence_bone_motion(sequence_index: int) -> bool:
	if sequence_index < 0 or sequence_index >= sequences.size():
		return false
	var sequence = sequences[sequence_index]
	for bone in bones:
		for times in [bone.translation_times, bone.rotation_times, bone.scale_times]:
			for time in times:
				if int(time) >= int(sequence.interval_start) and int(time) <= int(sequence.interval_end):
					return true
	return false


func has_idle_bone_motion() -> bool:
	return has_sequence_bone_motion(find_idle_sequence())


## War3 MDX is Z-up; Godot is Y-up. Same remap as DOO positions: (x,y,z)→(x,z,y).
static func w3_to_godot(v: Vector3) -> Vector3:
	return Vector3(v.x, v.z, v.y)


static func replaceable_rel_path(replaceable_id: int) -> String:
	if REPLACEABLE_STEMS.has(replaceable_id):
		return str(REPLACEABLE_STEMS[replaceable_id]) + ".blp"
	return ""


func geoset_alpha_at(geoset_id: int, time_ms: int) -> float:
	if not geoset_anims.has(geoset_id):
		return 1.0
	var a: GeosetAnim = geoset_anims[geoset_id]
	if a.alpha_times.is_empty():
		return a.static_alpha
	var times := a.alpha_times
	var values := a.alpha_values
	if time_ms <= times[0]:
		return values[0]
	if time_ms >= times[times.size() - 1]:
		return values[values.size() - 1]
	for i in range(times.size() - 1):
		if time_ms >= times[i] and time_ms <= times[i + 1]:
			return values[i]
	return values[values.size() - 1]


static func load_file(path: String):
	if not FileAccess.file_exists(path):
		return null
	var f = FileAccess.open(path, FileAccess.READ)
	if f == null:
		return null
	return load_bytes(f.get_buffer(f.get_length()))


static func load_bytes(data: PackedByteArray):
	if data.size() < 8:
		return null
	var magic = W3BinaryReader.fourcc_from_bytes(data.slice(0, 4))
	if magic != "MDLX":
		push_error("W3MdxModel: not MDX (%s)" % magic)
		return null
	var model = preload("res://addons/w3kit/w3_mdx_model.gd").new()
	var pos = 4
	while pos + 8 <= data.size():
		var tag = W3BinaryReader.fourcc_from_bytes(data.slice(pos, pos + 4))
		var size = data.decode_u32(pos + 4)
		pos += 8
		if pos + size > data.size():
			break
		var chunk = data.slice(pos, pos + size)
		pos += size
		match tag:
			"VERS":
				if chunk.size() >= 4:
					model.version = chunk.decode_u32(0)
			"MODL":
				model.name = W3BinaryReader.cstring_from_padded(chunk.slice(0, 80))
			"SEQS":
				_parse_seqs(model, chunk)
			"GLBS":
				_parse_global_sequences(model, chunk)
			"GEOS":
				_parse_geos(model, chunk)
			"GEOA":
				_parse_geoa(model, chunk)
			"TEXS":
				_parse_texs(model, chunk)
			"MTLS":
				_parse_mtls(model, chunk)
			"BONE":
				_parse_generic_nodes(model, chunk, true)
			"HELP":
				# Warsmash: helpers are skeletal nodes — Footman Stand keys live here, not in BONE.
				_parse_generic_nodes(model, chunk, false)
			"ATCH":
				_parse_attachments(model, chunk)
			"PRE2":
				_parse_particle_emitters2(model, chunk)
			"EVTS":
				_parse_event_objects(model, chunk)
			"LITE":
				_parse_lights(model, chunk)
			"PIVT":
				_parse_pivots(model, chunk)
			_:
				pass
	# Attach pivots (War3 space) to bones by object id.
	for i in model.bones.size():
		var b: Bone = model.bones[i]
		if b.object_id >= 0 and b.object_id < model.pivots.size():
			b.pivot = model.pivots[b.object_id]
		elif i < model.pivots.size():
			b.pivot = model.pivots[i]
	# Classic GNDX+MTGC+MATS → 4 bone indices / equal weights per vertex.
	for g in model.geosets:
		_finalize_geoset_skin(g)
	return model


static func _finalize_geoset_skin(g) -> void:
	var nvert: int = g.vertices_war3.size()
	if nvert == 0:
		nvert = g.vertices.size()
	# Reforged SKIN already contains four bone IDs followed by four byte weights.
	if g.bone_indices.size() >= nvert * 4 and g.bone_weights.size() >= nvert * 4:
		return
	g.bone_indices = PackedInt32Array()
	g.bone_weights = PackedFloat32Array()
	# Slice matrix groups from MATS via MTGC sizes.
	var groups: Array = []
	var off := 0
	for sz in g.matrix_group_sizes:
		var slice: PackedInt32Array = PackedInt32Array()
		for k in sz:
			if off < g.matrix_indices.size():
				slice.append(g.matrix_indices[off])
			off += 1
		groups.append(slice)
	for vi in nvert:
		var gid := 0
		if vi < g.vertex_groups.size():
			gid = int(g.vertex_groups[vi])
		var group: PackedInt32Array = PackedInt32Array()
		if gid >= 0 and gid < groups.size():
			group = groups[gid]
		var nb: int = mini(4, group.size())
		var wt: float = 1.0 / float(maxi(1, nb)) if nb > 0 else 0.0
		for j in 4:
			if j < nb:
				g.bone_indices.append(group[j])
				g.bone_weights.append(wt)
			else:
				g.bone_indices.append(-1)
				g.bone_weights.append(0.0)


static func _parse_seqs(model, chunk: PackedByteArray) -> void:
	var r = W3BinaryReader.new(chunk)
	while r.remaining() >= 132:
		var s = Sequence.new()
		s.name = r.read_fixed_cstring(80)
		s.interval_start = r.read_i32()
		s.interval_end = r.read_i32()
		s.move_speed = r.read_f32()
		s.flags = r.read_i32()
		s.rarity = r.read_f32()
		s.sync_point = r.read_i32()
		s.extent_min = Vector3(r.read_f32(), r.read_f32(), r.read_f32())
		s.extent_max = Vector3(r.read_f32(), r.read_f32(), r.read_f32())
		s.extent_radius = r.read_f32()
		model.sequences.append(s)


static func _parse_global_sequences(model, chunk: PackedByteArray) -> void:
	var r = W3BinaryReader.new(chunk)
	while r.remaining() >= 4:
		model.global_sequences.append(r.read_i32())


static func _parse_texs(model, chunk: PackedByteArray) -> void:
	var r = W3BinaryReader.new(chunk)
	while r.remaining() >= 268:
		var replaceable_id: int = r.read_i32()
		var path = r.read_fixed_cstring(260)
		r.read_i32() # flags
		model.textures.append(path.replace("\\", "/"))
		model.texture_replaceable_ids.append(replaceable_id)


## mdx-m3-viewer GeosetAnimation.readMdx: alpha, flags, color[3], geosetId, then KGAO/KGAC.
static func _parse_geoa(model, chunk: PackedByteArray) -> void:
	var r = W3BinaryReader.new(chunk)
	while r.remaining() >= 28:
		var inclusive: int = r.read_i32()
		var start: int = r.pos - 4
		var end: int = start + inclusive
		var anim = GeosetAnim.new()
		anim.static_alpha = r.read_f32()
		r.read_i32() # flags
		r.read_f32() # color r
		r.read_f32() # color g
		r.read_f32() # color b
		anim.geoset_id = r.read_i32()
		while r.pos + 4 <= end and r.remaining() >= 4:
			var tag := r.read_fourcc()
			if tag == "KGAO":
				var ntracks: int = r.read_i32()
				var interp: int = r.read_i32()
				r.read_i32() # global seq
				ntracks = _bounded_count(ntracks, r.remaining(), 8 + (8 if interp > 1 else 0), MAX_TRACKS)
				for _i in ntracks:
					if r.remaining() < 8:
						break
					anim.alpha_times.append(r.read_i32())
					anim.alpha_values.append(r.read_f32())
					if interp > 1:
						# hermite/bezier tangents
						for _k in 2:
							if r.remaining() >= 4:
								r.read_f32()
			elif tag == "KGAC":
				var n2: int = r.read_i32()
				var interp2: int = r.read_i32()
				r.read_i32()
				n2 = _bounded_count(n2, r.remaining(), 16 + (24 if interp2 > 1 else 0), MAX_TRACKS)
				for _j in n2:
					if r.remaining() < 16:
						break
					r.read_i32() # time
					r.read_f32()
					r.read_f32()
					r.read_f32()
					if interp2 > 1:
						for _k in 6:
							if r.remaining() >= 4:
								r.read_f32()
			else:
				r.pos -= 4
				break
		r.seek(end)
		if anim.geoset_id >= 0:
			model.geoset_anims[anim.geoset_id] = anim


static func _parse_mtls(model, chunk: PackedByteArray) -> void:
	var r = W3BinaryReader.new(chunk)
	while r.remaining() >= 8:
		var inclusive: int = r.read_i32()
		var start: int = r.pos - 4
		var end: int = mini(start + inclusive, chunk.size())
		if inclusive < 12 or end <= r.pos:
			break
		r.read_i32() # priority plane
		r.read_i32() # flags
		var shader_name := ""
		# Warsmash/HiveWE: v900/v1000 insert an 80-byte shader name here;
		# any non-empty shader marks the material as HD.
		if (model.version == 900 or model.version == 1000) and r.pos + 80 <= end:
			shader_name = r.read_fixed_cstring(80)
		# LAYS
		if r.pos + 8 > end:
			break
		var lays = r.read_fourcc()
		if lays != "LAYS":
			r.seek(end)
			continue
		var layer_count: int = r.read_i32()
		layer_count = _bounded_count(layer_count, r.remaining(), 4, MAX_LAYERS)
		var layers: Array = []
		var pre_v1100_hd: bool = model.version < 1100 and not shader_name.is_empty()
		var merged_hd = LayerInfo.new() if pre_v1100_hd else null
		if merged_hd != null:
			merged_hd.shader = 1
		for _i in layer_count:
			if r.pos + 4 > end:
				break
			var layer_inclusive: int = r.read_i32()
			var layer_start: int = r.pos - 4
			var layer_end: int = mini(layer_start + layer_inclusive, end)
			if layer_inclusive < 16 or layer_end <= r.pos:
				break
			var li = LayerInfo.new()
			li.filter_mode = r.read_i32()
			li.flags = r.read_i32() # Unshaded/TwoSided/Unfogged/NoDepthTest...
			li.texture_id = r.read_i32()
			r.read_i32() # texture anim id
			r.read_i32() # coord id
			li.alpha = r.read_f32()
			if model.version > 800 and r.pos + 4 <= layer_end:
				li.emissive_gain = r.read_f32()
			if model.version > 900 and r.pos + 20 <= layer_end:
				li.fresnel_color = Color(r.read_f32(), r.read_f32(), r.read_f32(), 1.0)
				li.fresnel_opacity = r.read_f32()
				li.fresnel_team_color = r.read_f32()
			if pre_v1100_hd:
				# HiveWE writes one complete pre-v1100 layer per HD PBR slot. Each
				# physical layer may carry its own KMTF track; only slot zero owns
				# the shared KMTA/layer properties.
				merged_hd.texture_ids.append(li.texture_id)
				merged_hd.texture_slots.append(_i)
				_parse_layer_tracks(r, layer_end, li, 0)
				_append_texture_track(merged_hd, li, 0)
				if _i == 0:
					merged_hd.filter_mode = li.filter_mode
					merged_hd.flags = li.flags
					merged_hd.texture_id = li.texture_id
					merged_hd.alpha = li.alpha
					merged_hd.alpha_times = li.alpha_times
					merged_hd.alpha_values = li.alpha_values
					merged_hd.alpha_global_sequence = li.alpha_global_sequence
					merged_hd.emissive_gain = li.emissive_gain
					merged_hd.fresnel_color = li.fresnel_color
					merged_hd.fresnel_opacity = li.fresnel_opacity
					merged_hd.fresnel_team_color = li.fresnel_team_color
			elif model.version >= 1100 and r.pos + 8 <= layer_end:
				li.shader = r.read_i32()
				var tex_count := _bounded_count(r.read_i32(), layer_end - r.pos, 8, 64)
				for _slot_index in tex_count:
					if r.pos + 8 > layer_end:
						break
					li.texture_ids.append(r.read_i32())
					li.texture_slots.append(r.read_i32())
					_parse_kmtf_if_present(r, layer_end, li)
				_parse_layer_tracks(r, layer_end, li, -1)
				if not li.texture_ids.is_empty():
					li.texture_id = li.texture_ids[0]
				layers.append(li)
			else:
				li.texture_ids.append(li.texture_id)
				li.texture_slots.append(0)
				_parse_layer_tracks(r, layer_end, li, 0)
				layers.append(li)
			# Skip remaining layer bytes (keyframes etc.)
			r.seek(layer_end)
		if merged_hd != null:
			layers.append(merged_hd)
		model.materials.append(layers)
		r.seek(end)


static func _append_texture_track(target, source, source_index: int) -> void:
	var times := PackedInt32Array()
	var values := PackedInt32Array()
	var global_sequence := -1
	if source_index >= 0 and source_index < source.texture_track_times.size():
		times = source.texture_track_times[source_index]
		values = source.texture_track_values[source_index]
		if source_index < source.texture_track_global_sequences.size():
			global_sequence = int(source.texture_track_global_sequences[source_index])
	target.texture_track_times.append(times)
	target.texture_track_values.append(values)
	target.texture_track_global_sequences.append(global_sequence)


static func _parse_kmtf_if_present(r, limit: int, layer) -> bool:
	if r.pos + 4 > limit:
		return false
	var saved: int = r.pos
	if r.read_fourcc() != "KMTF":
		r.seek(saved)
		return false
	var track := _read_scalar_track(r, limit, true)
	layer.texture_track_times.append(track["times"])
	layer.texture_track_values.append(track["values"])
	layer.texture_track_global_sequences.append(int(track["global_sequence"]))
	return true


static func _parse_layer_tracks(r, limit: int, layer, texture_index: int) -> void:
	while r.pos + 4 <= limit:
		var tag: String = str(r.read_fourcc())
		match tag:
			"KMTF":
				var texture_track := _read_scalar_track(r, limit, true)
				if texture_index >= 0:
					while layer.texture_track_times.size() <= texture_index:
						layer.texture_track_times.append(PackedInt32Array())
						layer.texture_track_values.append(PackedInt32Array())
						layer.texture_track_global_sequences.append(-1)
					layer.texture_track_times[texture_index] = texture_track["times"]
					layer.texture_track_values[texture_index] = texture_track["values"]
					layer.texture_track_global_sequences[texture_index] = int(texture_track["global_sequence"])
			"KMTA":
				var alpha_track := _read_scalar_track(r, limit, false)
				layer.alpha_times = alpha_track["times"]
				layer.alpha_values = alpha_track["values"]
				layer.alpha_global_sequence = int(alpha_track["global_sequence"])
			"KMTE", "KFCA", "KFTC":
				_read_scalar_track(r, limit, false)
			"KFC3":
				_skip_vector3_track(r, limit)
			_:
				# Inclusive layer sizes are authoritative. An unknown future tag
				# cannot be skipped safely without knowing its value width.
				r.seek(limit)


static func _read_scalar_track(r, limit: int, integer_values: bool) -> Dictionary:
	var times := PackedInt32Array()
	var int_values := PackedInt32Array()
	var float_values := PackedFloat32Array()
	if r.pos + 12 > limit:
		r.seek(limit)
		return {"times": times, "values": int_values if integer_values else float_values, "global_sequence": -1}
	var count: int = clampi(r.read_i32(), 0, MAX_TRACKS)
	var interpolation: int = r.read_i32()
	var global_sequence: int = r.read_i32()
	var bytes_per_track := 8 + (8 if interpolation > 1 else 0)
	count = mini(count, maxi(0, limit - r.pos) / bytes_per_track)
	for _i in count:
		times.append(r.read_i32())
		if integer_values:
			int_values.append(r.read_i32())
		else:
			float_values.append(r.read_f32())
		if interpolation > 1:
			if integer_values:
				r.read_i32()
				r.read_i32()
			else:
				r.read_f32()
				r.read_f32()
	return {
		"times": times,
		"values": int_values if integer_values else float_values,
		"global_sequence": global_sequence,
	}


static func _skip_vector3_track(r, limit: int) -> void:
	if r.pos + 12 > limit:
		r.seek(limit)
		return
	var count: int = clampi(r.read_i32(), 0, MAX_TRACKS)
	var interpolation: int = r.read_i32()
	r.read_i32() # global sequence
	var bytes_per_track := 16 + (24 if interpolation > 1 else 0)
	count = mini(count, maxi(0, limit - r.pos) / bytes_per_track)
	r.seek(r.pos + count * bytes_per_track)


static func _parse_pivots(model, chunk: PackedByteArray) -> void:
	var r = W3BinaryReader.new(chunk)
	while r.remaining() >= 12:
		# Keep War3 Z-up — bone matrices / skinning use this space.
		model.pivots.append(Vector3(r.read_f32(), r.read_f32(), r.read_f32()))


## Bones and Helpers share GenericObject layout. Bones append geosetId+geosetAnimId after inclusive size.
static func _parse_generic_nodes(model, chunk: PackedByteArray, is_bone: bool) -> void:
	var r = W3BinaryReader.new(chunk)
	while r.remaining() >= 4:
		var inclusive: int = r.read_i32()
		var start: int = r.pos - 4
		var end: int = start + inclusive
		if inclusive < 96 or end > chunk.size():
			break
		var b = Bone.new()
		var name_bytes = r.read_bytes(80)
		b.name = W3BinaryReader.cstring_from_padded(name_bytes)
		b.object_id = r.read_i32()
		b.parent_id = r.read_i32()
		r.read_i32() # flags
		# Anim tracks fill the inclusive generic-object size.
		while r.pos + 8 <= end:
			var tag = r.read_fourcc()
			if tag != "KGTR" and tag != "KGRT" and tag != "KGSC":
				r.pos -= 4
				break
			var track_bytes = _read_track_block(r, tag)
			_apply_track(b, tag, track_bytes)
		r.seek(end)
		if is_bone and r.remaining() >= 8:
			r.read_i32() # geosetId
			r.read_i32() # geosetAnimId
		model.bones.append(b)


static func _read_node(r, limit: int):
	if r.remaining() < 96:
		return null
	var inclusive: int = int(r.read_i32())
	var start: int = int(r.pos) - 4
	var end: int = mini(start + inclusive, limit)
	if inclusive < 96 or end > limit:
		return null
	var node := Bone.new()
	node.name = r.read_fixed_cstring(80)
	node.object_id = r.read_i32()
	node.parent_id = r.read_i32()
	r.read_i32() # flags
	while r.pos + 8 <= end:
		var tag: String = str(r.read_fourcc())
		if tag != "KGTR" and tag != "KGRT" and tag != "KGSC":
			r.pos -= 4
			break
		_apply_track(node, tag, _read_track_block(r, tag))
	r.seek(end)
	return node


static func _parse_attachments(model, chunk: PackedByteArray) -> void:
	var r := W3BinaryReader.new(chunk)
	while r.remaining() >= 4:
		var inclusive := r.read_i32()
		var start := r.pos - 4
		var end := mini(start + inclusive, chunk.size())
		if inclusive < 4 or end <= r.pos:
			break
		var attachment := Attachment.new()
		attachment.node = _read_node(r, end)
		if attachment.node == null or r.pos + 264 > end:
			r.seek(end)
			continue
		attachment.path = r.read_fixed_cstring(256).replace("\\", "/")
		r.read_i32() # reserved
		attachment.attachment_id = r.read_i32()
		model.attachments.append(attachment)
		model.bones.append(attachment.node)
		r.seek(end)


static func _parse_particle_emitters2(model, chunk: PackedByteArray) -> void:
	var r := W3BinaryReader.new(chunk)
	while r.remaining() >= 4:
		var inclusive := r.read_i32()
		var start := r.pos - 4
		var end := mini(start + inclusive, chunk.size())
		if inclusive < 4 or end <= r.pos:
			break
		var emitter := ParticleEmitter2.new()
		emitter.node = _read_node(r, end)
		if emitter.node == null or r.pos + 171 > end:
			r.seek(end)
			continue
		emitter.speed = r.read_f32()
		emitter.speed_variation = r.read_f32()
		emitter.latitude = r.read_f32()
		emitter.gravity = r.read_f32()
		emitter.life_span = r.read_f32()
		emitter.emission_rate = r.read_f32()
		emitter.length = r.read_f32()
		emitter.width = r.read_f32()
		emitter.filter_mode = r.read_i32()
		emitter.rows = maxi(1, r.read_i32())
		emitter.columns = maxi(1, r.read_i32())
		emitter.head_or_tail = r.read_i32()
		emitter.tail_length = r.read_f32()
		emitter.time_middle = r.read_f32()
		var color_values: Array[Vector3] = []
		for _color_index in 3:
			color_values.append(Vector3(r.read_f32(), r.read_f32(), r.read_f32()))
		var alphas := PackedByteArray([r.read_u8(), r.read_u8(), r.read_u8()])
		for color_index in 3:
			var c: Vector3 = color_values[color_index]
			emitter.colors.append(Color(c.x, c.y, c.z, float(alphas[color_index]) / 255.0))
		for _scale_index in 3:
			emitter.scaling.append(r.read_f32())
		# Four atlas interval triplets (head/head decay/tail/tail decay).
		for _interval_value in 12:
			r.read_i32()
		emitter.texture_id = r.read_i32()
		r.read_i32() # squirt
		r.read_i32() # priority plane
		emitter.replaceable_id = r.read_i32()
		# MDX ParticleEmitter2 animation tracks. Visibility and emission rate
		# decide whether an effect belongs to the active sequence; ignoring them
		# makes attack/death particles leak into Stand.
		while r.pos + 16 <= end:
			var tag := str(r.read_fourcc())
			if tag != "KP2S" and tag != "KP2R" and tag != "KP2L" and tag != "KP2G" \
					and tag != "KP2V" and tag != "KP2E" and tag != "KP2W" and tag != "KP2N":
				r.pos -= 4
				break
			var track := _read_float_track_block(r)
			if tag == "KP2V":
				emitter.visibility_times = track["times"]
				emitter.visibility_values = track["values"]
			elif tag == "KP2E":
				emitter.emission_rate_times = track["times"]
				emitter.emission_rate_values = track["values"]
		model.particle_emitters2.append(emitter)
		model.bones.append(emitter.node)
		r.seek(end)


static func _parse_event_objects(model, chunk: PackedByteArray) -> void:
	var r := W3BinaryReader.new(chunk)
	while r.remaining() >= 96:
		var event := EventObject.new()
		event.node = _read_node(r, chunk.size())
		if event.node == null or r.remaining() < 12:
			break
		if r.read_fourcc() != "KEVT":
			break
		var count := _bounded_count(r.read_i32(), r.remaining() - 4, 4, MAX_TRACKS)
		event.global_sequence_id = r.read_i32()
		for _time_index in count:
			event.times.append(r.read_i32())
		model.event_objects.append(event)
		model.bones.append(event.node)


static func _parse_lights(model, chunk: PackedByteArray) -> void:
	var r := W3BinaryReader.new(chunk)
	while r.remaining() >= 4:
		var inclusive := r.read_i32()
		var start := r.pos - 4
		var end := mini(start + inclusive, chunk.size())
		if inclusive < 4 or end <= r.pos:
			break
		var light := LightInfo.new()
		light.node = _read_node(r, end)
		if light.node == null or r.pos + 44 > end:
			r.seek(end)
			continue
		light.type = r.read_i32()
		light.attenuation_start = r.read_f32()
		light.attenuation_end = r.read_f32()
		light.color = Color(r.read_f32(), r.read_f32(), r.read_f32(), 1.0)
		light.intensity = r.read_f32()
		light.ambient_color = Color(r.read_f32(), r.read_f32(), r.read_f32(), 1.0)
		light.ambient_intensity = r.read_f32()
		if model.version >= 1200 and r.pos + 4 <= end:
			r.read_f32() # shadow intensity
		while r.pos + 16 <= end:
			var tag := str(r.read_fourcc())
			if tag == "KLAC" or tag == "KLBC":
				var vector_track := _read_track_block(r, tag)
				if tag == "KLAC":
					light.color_times = vector_track["times"]
					light.color_values = vector_track["values"]
			elif tag == "KLAS" or tag == "KLAE" or tag == "KLAI" \
					or tag == "KLBI" or tag == "KLAV":
				var float_track := _read_float_track_block(r)
				if tag == "KLAI":
					light.intensity_times = float_track["times"]
					light.intensity_values = float_track["values"]
				elif tag == "KLAV":
					light.visibility_times = float_track["times"]
					light.visibility_values = float_track["values"]
				elif tag == "KLAE":
					light.attenuation_end_times = float_track["times"]
					light.attenuation_end_values = float_track["values"]
			else:
				r.pos -= 4
				break
		model.lights.append(light)
		model.bones.append(light.node)
		r.seek(end)


static func _parse_bones(model, chunk: PackedByteArray) -> void:
	_parse_generic_nodes(model, chunk, true)


static func _read_track_block(r, _tag: String) -> Dictionary:
	var tracks = r.read_i32()
	var interpolation = r.read_i32()
	r.read_i32() # global seq
	var value_bytes := 20 if _tag == "KGRT" else 16
	if interpolation > 1:
		value_bytes += 32 if _tag == "KGRT" else 24
	tracks = _bounded_count(tracks, r.remaining(), value_bytes, MAX_TRACKS)
	var times: PackedInt32Array = PackedInt32Array()
	var values: Array = []
	for _i in tracks:
		times.append(r.read_i32())
		if _tag == "KGRT":
			values.append(Quaternion(r.read_f32(), r.read_f32(), r.read_f32(), r.read_f32()))
			if interpolation > 1:
				# skip tangents in/out (8 floats)
				for _k in 8:
					r.read_f32()
		elif _tag == "KGTR" or _tag == "KGSC":
			values.append(Vector3(r.read_f32(), r.read_f32(), r.read_f32()))
			if interpolation > 1:
				for _k in 6:
					r.read_f32()
	return {"times": times, "values": values}


static func _read_float_track_block(r) -> Dictionary:
	var tracks: int = int(r.read_i32())
	var interpolation: int = int(r.read_i32())
	r.read_i32() # global sequence
	var value_bytes: int = 8 + (8 if interpolation > 1 else 0)
	tracks = _bounded_count(tracks, r.remaining(), value_bytes, MAX_TRACKS)
	var times := PackedInt32Array()
	var values := PackedFloat32Array()
	for _i in tracks:
		times.append(r.read_i32())
		values.append(r.read_f32())
		if interpolation > 1:
			r.read_f32() # in tangent
			r.read_f32() # out tangent
	return {"times": times, "values": values}


static func _apply_track(b: Bone, tag: String, data: Dictionary) -> void:
	var times: PackedInt32Array = data["times"]
	var values: Array = data["values"]
	match tag:
		"KGTR":
			b.translation_times = times
			b.translation_values = PackedVector3Array()
			for v in values:
				b.translation_values.append(v)
		"KGRT":
			b.rotation_times = times
			b.rotation_values = []
			for v in values:
				b.rotation_values.append(v)
		"KGSC":
			b.scale_times = times
			b.scale_values = PackedVector3Array()
			for v in values:
				b.scale_values.append(v)


static func _parse_geos(model, chunk: PackedByteArray) -> void:
	var r = W3BinaryReader.new(chunk)
	while r.remaining() >= 4:
		var inclusive = r.read_i32()
		var start = r.pos - 4
		var end = start + inclusive
		var g = Geoset.new()
		while r.pos + 4 <= end and r.remaining() >= 4:
			var tag = r.read_fourcc()
			match tag:
				"VRTX":
					var n = _bounded_count(r.read_i32(), r.remaining(), 12, MAX_ELEMENTS)
					for _i in n:
						var vw := Vector3(r.read_f32(), r.read_f32(), r.read_f32())
						g.vertices_war3.append(vw)
						g.vertices.append(w3_to_godot(vw))
				"NRMS":
					var n2 = _bounded_count(r.read_i32(), r.remaining(), 12, MAX_ELEMENTS)
					for _i in n2:
						var nw := Vector3(r.read_f32(), r.read_f32(), r.read_f32())
						g.normals_war3.append(nw.normalized())
						g.normals.append(w3_to_godot(nw).normalized())
				"PTYP":
					var cnt = _bounded_count(r.read_i32(), r.remaining(), 4, MAX_ELEMENTS)
					for _i in cnt:
						r.read_i32()
				"PCNT":
					var cnt2 = _bounded_count(r.read_i32(), r.remaining(), 4, MAX_ELEMENTS)
					for _i in cnt2:
						r.read_i32()
				"PVTX":
					var cnt3 = _bounded_count(r.read_i32(), r.remaining(), 2, MAX_ELEMENTS * 3)
					for _i in cnt3:
						g.faces.append(r.read_u16())
				"GNDX":
					var cnt4 = _bounded_count(r.read_i32(), r.remaining(), 1, MAX_ELEMENTS)
					g.vertex_groups.resize(cnt4)
					for i in cnt4:
						g.vertex_groups[i] = r.read_u8()
				"MTGC":
					var cnt5 = _bounded_count(r.read_i32(), r.remaining(), 4, MAX_ELEMENTS)
					for _i in cnt5:
						g.matrix_group_sizes.append(r.read_i32())
				"MATS":
					# mdx-m3-viewer Geoset.readMdx — matrix indices, then header, extents, UVAS.
					var cnt6 = _bounded_count(r.read_i32(), r.remaining(), 4, MAX_ELEMENTS * 4)
					for _i in cnt6:
						g.matrix_indices.append(r.read_i32())
					g.material_id = r.read_i32()
					r.read_i32() # selection group
					r.read_i32() # selection flags
					if model.version > 800:
						g.lod = r.read_i32()
						if r.remaining() >= 80 and r.pos + 80 <= end:
							g.lod_name = r.read_fixed_cstring(80)
					# Extent: min(3) max(3) radius
					for _i in 7:
						r.read_f32()
					# Sequence extents
					if r.pos + 4 <= end:
						var seq_ext_count: int = r.read_i32()
						seq_ext_count = clampi(seq_ext_count, 0, 4096)
						for _s in seq_ext_count:
							if r.pos + 28 > end:
								break
							for _i in 7:
								r.read_f32()
					if model.version > 800:
						if r.pos + 4 <= end:
							var peek_tang := r.read_fourcc()
							if peek_tang == "TANG":
								var tn: int = _bounded_count(r.read_i32(), r.remaining(), 16, MAX_ELEMENTS)
								var tangent_base: int = g.tangents.size()
								g.tangents.resize(tangent_base + tn * 4)
								for tangent_index in tn:
									var tw := Vector3(r.read_f32(), r.read_f32(), r.read_f32())
									var handedness := r.read_f32()
									var tg := w3_to_godot(tw).normalized()
									var tangent_out: int = tangent_base + tangent_index * 4
									g.tangents[tangent_out] = tg.x
									g.tangents[tangent_out + 1] = tg.y
									g.tangents[tangent_out + 2] = tg.z
									g.tangents[tangent_out + 3] = handedness
							else:
								r.pos -= 4
						if r.pos + 4 <= end:
							var peek_skin := r.read_fourcc()
							if peek_skin == "SKIN":
								var sn: int = clampi(r.read_i32(), 0, end - r.pos)
								var skin_bytes := r.read_bytes(sn)
								var skin_vertices: int = skin_bytes.size() / 8
								var skin_base: int = g.bone_indices.size()
								g.bone_indices.resize(skin_base + skin_vertices * 4)
								g.bone_weights.resize(skin_base + skin_vertices * 4)
								for vi in skin_vertices:
									for j in 4:
										g.bone_indices[skin_base + vi * 4 + j] = int(skin_bytes[vi * 8 + j])
									for j in 4:
										g.bone_weights[skin_base + vi * 4 + j] = float(skin_bytes[vi * 8 + 4 + j]) / 255.0
							else:
								r.pos -= 4
				"UVAS":
					var n_uv_sets = _bounded_count(r.read_i32(), r.remaining(), 8, 16)
					for u_i in n_uv_sets:
						if r.pos + 4 > end:
							break
						var uvs_tag = r.read_fourcc()
						if uvs_tag != "UVBS":
							r.pos -= 4
							break
						var n3 = _bounded_count(r.read_i32(), r.remaining(), 8, MAX_ELEMENTS)
						for _i in n3:
							var u = r.read_f32()
							var v = r.read_f32()
							if u_i == 0:
								g.uvs.append(Vector2(u, v))
				_:
					# Unknown tag — stop subparse; seek to geoset end
					r.seek(end)
					break
		r.seek(end)
		if g.vertices.size() > 0 and g.faces.size() > 0:
			model.geosets.append(g)


static func _bounded_count(raw: int, bytes_remaining: int, bytes_per_item: int, maximum: int) -> int:
	if raw < 0 or raw > maximum or bytes_per_item <= 0:
		return 0
	if raw > bytes_remaining / bytes_per_item:
		return 0
	return raw


func find_sequence(query: String) -> int:
	var q = query.to_lower()
	for i in sequences.size():
		var s: Sequence = sequences[i]
		var n = s.name.to_lower()
		if n == q or n.begins_with(q):
			return i
	# fuzzy: "stand" matches "Stand - 1"
	for i in sequences.size():
		var s2: Sequence = sequences[i]
		if s2.name.to_lower().contains(q):
			return i
	return -1


## Warcraft III calls idle "Stand". This mirrors Warsmash
## SequenceUtils.randomStandSequence / selectSequence("stand"): only the
## text before the first '-' may be "stand", then rare variants are offered
## before a normal Stand is chosen at random.
func find_idle_sequence() -> int:
	var candidates: Array = []
	for i in sequences.size():
		var s: Sequence = sequences[i]
		# Match Warsmash filterSequences(String): part before first '-' must equal "stand".
		var primary := s.name
		var dash := primary.find("-")
		if dash >= 0:
			primary = primary.substr(0, dash)
		primary = primary.strip_edges().to_lower()
		if primary != "stand":
			continue
		candidates.append(i)
	if candidates.is_empty():
		var fallback := find_sequence("Stand")
		return fallback if fallback >= 0 else (0 if not sequences.is_empty() else -1)
	# Warsmash sorts higher Rarity first. A non-zero rarity is a low-probability
	# fidget/stand variant; the remainder are normal choices.
	candidates.sort_custom(func(a, b): return sequences[int(a)].rarity > sequences[int(b)].rarity)
	var first_normal := 0
	var roll := randf() * 100.0
	for i in candidates.size():
		var idx: int = int(candidates[i])
		var rarity: float = sequences[idx].rarity
		if is_zero_approx(rarity):
			first_normal = i
			break
		if roll < (10.0 - rarity):
			return idx
		first_normal = i + 1
	# A model may contain only rare Stand sequences. Warsmash falls back to a
	# random available sequence in that case instead of freezing the model.
	if first_normal >= candidates.size():
		first_normal = 0
	return int(candidates[randi_range(first_normal, candidates.size() - 1)])


static func _sequence_tokens(name: String) -> PackedStringArray:
	## "Stand - 1" / "Stand Ready" / "Stand, Ready" → ["stand"] or ["stand","ready"]
	var cleaned := name.to_lower()
	var dash := cleaned.find(" -")
	if dash >= 0:
		cleaned = cleaned.substr(0, dash)
	cleaned = cleaned.replace(",", " ")
	var parts := cleaned.split(" ", false)
	var out: PackedStringArray = PackedStringArray()
	for p in parts:
		var t := p.strip_edges()
		if t.is_empty() or t.is_valid_int():
			continue
		out.append(t)
	return out
