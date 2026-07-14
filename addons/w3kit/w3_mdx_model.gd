extends RefCounted
const W3BinaryReader = preload("res://addons/w3kit/w3_binary_reader.gd")
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
	var vertex_groups: PackedByteArray = PackedByteArray() ## GNDX
	var matrix_group_sizes: PackedInt32Array = PackedInt32Array() ## MTGC
	var matrix_indices: PackedInt32Array = PackedInt32Array() ## MATS bone object ids
	var bone_indices: PackedInt32Array = PackedInt32Array() # 4 object_ids per vert
	var bone_weights: PackedFloat32Array = PackedFloat32Array()


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
	var filter_mode: int = 0
	var alpha: float = 1.0


class GeosetAnim:
	var geoset_id: int = -1
	var static_alpha: float = 1.0
	var alpha_times: PackedInt32Array = PackedInt32Array()
	var alpha_values: PackedFloat32Array = PackedFloat32Array()


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
var geosets: Array = [] # Geoset
var bones: Array = [] # Bone
var textures: PackedStringArray = PackedStringArray()
## Parallel to textures[] — 0 means use path; else replaceable table when path empty.
var texture_replaceable_ids: PackedInt32Array = PackedInt32Array()
var materials: Array = [] # Array of Array[LayerInfo]
var pivots: PackedVector3Array = PackedVector3Array()
## geoset_id → GeosetAnim (mdx-m3-viewer GEOA)
var geoset_anims: Dictionary = {}


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
	var magic = data.slice(0, 4).get_string_from_ascii()
	if magic != "MDLX":
		push_error("W3MdxModel: not MDX (%s)" % magic)
		return null
	var model = preload("res://addons/w3kit/w3_mdx_model.gd").new()
	var pos = 4
	while pos + 8 <= data.size():
		var tag = data.slice(pos, pos + 4).get_string_from_ascii()
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
	g.bone_indices = PackedInt32Array()
	g.bone_weights = PackedFloat32Array()
	var nvert: int = g.vertices_war3.size()
	if nvert == 0:
		nvert = g.vertices.size()
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
		var inclusive = r.read_i32()
		var start = r.pos - 4
		r.read_i32() # priority plane
		r.read_i32() # flags
		# LAYS
		if r.remaining() < 8:
			break
		var lays = r.read_fourcc()
		if lays != "LAYS":
			# rewind-ish: skip rest of material by inclusive size
			r.seek(start + inclusive)
			continue
		var layer_count = r.read_i32()
		var layers: Array = []
		for _i in layer_count:
			if r.remaining() < 4:
				break
			var layer_inclusive = r.read_i32()
			var layer_start = r.pos - 4
			var li = LayerInfo.new()
			li.filter_mode = r.read_i32()
			r.read_i32() # shading flags
			li.texture_id = r.read_i32()
			r.read_i32() # texture anim id
			r.read_i32() # coord id
			li.alpha = r.read_f32()
			# Skip remaining layer bytes (keyframes etc.)
			r.seek(layer_start + layer_inclusive)
			layers.append(li)
		model.materials.append(layers)
		r.seek(start + inclusive)


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
			var tag = r.read_bytes(4).get_string_from_ascii()
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


static func _parse_bones(model, chunk: PackedByteArray) -> void:
	_parse_generic_nodes(model, chunk, true)


static func _read_track_block(r, _tag: String) -> Dictionary:
	var tracks = r.read_i32()
	var interpolation = r.read_i32()
	r.read_i32() # global seq
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
					var n = r.read_i32()
					for _i in n:
						var vw := Vector3(r.read_f32(), r.read_f32(), r.read_f32())
						g.vertices_war3.append(vw)
						g.vertices.append(w3_to_godot(vw))
				"NRMS":
					var n2 = r.read_i32()
					for _i in n2:
						var nw := Vector3(r.read_f32(), r.read_f32(), r.read_f32())
						g.normals_war3.append(nw.normalized())
						g.normals.append(w3_to_godot(nw).normalized())
				"PTYP":
					var cnt = r.read_i32()
					for _i in cnt:
						r.read_i32()
				"PCNT":
					var cnt2 = r.read_i32()
					for _i in cnt2:
						r.read_i32()
				"PVTX":
					var cnt3 = r.read_i32()
					for _i in cnt3:
						g.faces.append(r.read_u16())
				"GNDX":
					var cnt4 = r.read_i32()
					g.vertex_groups.resize(cnt4)
					for i in cnt4:
						g.vertex_groups[i] = r.read_u8()
				"MTGC":
					var cnt5 = r.read_i32()
					for _i in cnt5:
						g.matrix_group_sizes.append(r.read_i32())
				"MATS":
					# mdx-m3-viewer Geoset.readMdx — matrix indices, then header, extents, UVAS.
					var cnt6 = r.read_i32()
					for _i in cnt6:
						g.matrix_indices.append(r.read_i32())
					g.material_id = r.read_i32()
					r.read_i32() # selection group
					r.read_i32() # selection flags
					if model.version > 800:
						r.read_i32() # lod
						if r.remaining() >= 80 and r.pos + 80 <= end:
							r.read_bytes(80) # lod name
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
								var tn: int = r.read_i32()
								for _i in tn * 4:
									r.read_f32()
							else:
								r.pos -= 4
						if r.pos + 4 <= end:
							var peek_skin := r.read_fourcc()
							if peek_skin == "SKIN":
								var sn: int = r.read_i32()
								r.read_bytes(clampi(sn, 0, end - r.pos))
							else:
								r.pos -= 4
				"UVAS":
					var n_uv_sets = r.read_i32()
					for u_i in n_uv_sets:
						if r.pos + 4 > end:
							break
						var uvs_tag = r.read_fourcc()
						if uvs_tag != "UVBS":
							r.pos -= 4
							break
						var n3 = r.read_i32()
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


## War3 has no "Idle" — Warsmash SequenceUtils.randomStandSequence / selectSequence("stand"):
## name.split("-")[0].trim().toLowerCase() == "stand"  (excludes Stand Ready / Victory / Defend).
func find_idle_sequence() -> int:
	var candidates: PackedInt32Array = PackedInt32Array()
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
		return find_sequence("Stand")
	# Prefer rarity 0 (Warsmash breaks on first rarity==0 after sort); else lowest rarity.
	var best: int = candidates[0]
	var best_r: float = sequences[best].rarity
	for j in range(1, candidates.size()):
		var idx: int = candidates[j]
		var r: float = sequences[idx].rarity
		if r < best_r or (is_equal_approx(r, best_r) and sequences[idx].name.strip_edges().to_lower() == "stand"):
			best = idx
			best_r = r
	for i in candidates:
		if is_equal_approx(sequences[i].rarity, 0.0):
			return i
	return best


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
