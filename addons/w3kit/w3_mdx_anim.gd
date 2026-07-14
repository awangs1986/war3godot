extends RefCounted
## Warsmash / mdx-m3-viewer bone sampling + hierarchy (War3 Z-up space).
## Skinning: skinned = (Σ world[bone_i]) / N * bind_vertex  (classic equal weights).


static func sample_vec3(
	times: PackedInt32Array,
	values: PackedVector3Array,
	time_ms: int,
	default_v: Vector3,
	seq_start: int = -1,
	seq_end: int = -1
) -> Vector3:
	var ft: PackedInt32Array = PackedInt32Array()
	var fv: PackedVector3Array = PackedVector3Array()
	_filter_vec3(times, values, seq_start, seq_end, ft, fv)
	if ft.is_empty() or fv.is_empty():
		return default_v
	if ft.size() == 1 or time_ms <= ft[0]:
		return fv[0]
	if time_ms >= ft[ft.size() - 1]:
		return fv[fv.size() - 1]
	for i in range(ft.size() - 1):
		if time_ms >= ft[i] and time_ms <= ft[i + 1]:
			var span: int = maxi(1, ft[i + 1] - ft[i])
			var a: float = float(time_ms - ft[i]) / float(span)
			return fv[i].lerp(fv[i + 1], a)
	return fv[fv.size() - 1]


static func sample_quat(
	times: PackedInt32Array,
	values: Array,
	time_ms: int,
	seq_start: int = -1,
	seq_end: int = -1
) -> Quaternion:
	var ft: PackedInt32Array = PackedInt32Array()
	var fv: Array = []
	_filter_quat(times, values, seq_start, seq_end, ft, fv)
	if ft.is_empty() or fv.is_empty():
		return Quaternion.IDENTITY
	if ft.size() == 1 or time_ms <= ft[0]:
		return fv[0] as Quaternion
	if time_ms >= ft[ft.size() - 1]:
		return fv[fv.size() - 1] as Quaternion
	for i in range(ft.size() - 1):
		if time_ms >= ft[i] and time_ms <= ft[i + 1]:
			var span: int = maxi(1, ft[i + 1] - ft[i])
			var a: float = float(time_ms - ft[i]) / float(span)
			return (fv[i] as Quaternion).slerp(fv[i + 1] as Quaternion, a)
	return fv[fv.size() - 1] as Quaternion


static func _filter_vec3(
	times: PackedInt32Array,
	values: PackedVector3Array,
	seq_start: int,
	seq_end: int,
	out_t: PackedInt32Array,
	out_v: PackedVector3Array
) -> void:
	for i in times.size():
		if i >= values.size():
			break
		var t: int = times[i]
		if seq_start >= 0 and seq_end >= 0 and (t < seq_start or t > seq_end):
			continue
		out_t.append(t)
		out_v.append(values[i])


static func _filter_quat(
	times: PackedInt32Array,
	values: Array,
	seq_start: int,
	seq_end: int,
	out_t: PackedInt32Array,
	out_v: Array
) -> void:
	for i in times.size():
		if i >= values.size():
			break
		var t: int = times[i]
		if seq_start >= 0 and seq_end >= 0 and (t < seq_start or t > seq_end):
			continue
		out_t.append(t)
		out_v.append(values[i])


## gl-matrix mat4.fromRotationTranslationScaleOrigin — rotate/scale about pivot, then translate.
static func local_matrix(rot: Quaternion, loc: Vector3, scale: Vector3, pivot: Vector3) -> Transform3D:
	var basis := Basis(rot).scaled(scale)
	var origin: Vector3 = loc + pivot - basis * pivot
	return Transform3D(basis, origin)


## Returns Dictionary object_id (int) -> Transform3D in War3 Z-up space.
static func compute_world_matrices(bones: Array, time_ms: int, seq_start: int = -1, seq_end: int = -1) -> Dictionary:
	var worlds: Dictionary = {}
	if bones.is_empty():
		return worlds
	var pending: Array = bones.duplicate()
	var guard := 0
	while not pending.is_empty() and guard < bones.size() + 2:
		guard += 1
		var next: Array = []
		for b in pending:
			var parent_id: int = int(b.parent_id)
			var object_id: int = int(b.object_id)
			var parent_ok: bool = parent_id < 0 or worlds.has(parent_id)
			if not parent_ok:
				next.append(b)
				continue
			var loc: Vector3 = sample_vec3(b.translation_times, b.translation_values, time_ms, Vector3.ZERO, seq_start, seq_end)
			var rot: Quaternion = sample_quat(b.rotation_times, b.rotation_values, time_ms, seq_start, seq_end)
			var scl: Vector3 = sample_vec3(b.scale_times, b.scale_values, time_ms, Vector3.ONE, seq_start, seq_end)
			var local: Transform3D = local_matrix(rot, loc, scl, b.pivot)
			if parent_id < 0:
				worlds[object_id] = local
			else:
				worlds[object_id] = worlds[parent_id] * local
		if next.size() == pending.size():
			for b2 in next:
				var loc2: Vector3 = sample_vec3(b2.translation_times, b2.translation_values, time_ms, Vector3.ZERO, seq_start, seq_end)
				var rot2: Quaternion = sample_quat(b2.rotation_times, b2.rotation_values, time_ms, seq_start, seq_end)
				var scl2: Vector3 = sample_vec3(b2.scale_times, b2.scale_values, time_ms, Vector3.ONE, seq_start, seq_end)
				worlds[int(b2.object_id)] = local_matrix(rot2, loc2, scl2, b2.pivot)
			break
		pending = next
	return worlds


static func skin_vertex(bind_war3: Vector3, bone_ids: PackedInt32Array, bone_wts: PackedFloat32Array, vert_i: int, worlds: Dictionary) -> Vector3:
	var base: int = vert_i * 4
	var acc := Vector3.ZERO
	var wsum := 0.0
	for j in 4:
		var wt: float = bone_wts[base + j] if base + j < bone_wts.size() else 0.0
		if wt <= 0.0001:
			continue
		var oid: int = bone_ids[base + j] if base + j < bone_ids.size() else -1
		if not worlds.has(oid):
			continue
		var xf: Transform3D = worlds[oid]
		acc += xf * bind_war3 * wt
		wsum += wt
	if wsum <= 0.0001:
		return bind_war3
	if absf(wsum - 1.0) > 0.01:
		acc /= wsum
	return acc


static func skin_normal(bind_war3_n: Vector3, bone_ids: PackedInt32Array, bone_wts: PackedFloat32Array, vert_i: int, worlds: Dictionary) -> Vector3:
	var base: int = vert_i * 4
	var acc := Vector3.ZERO
	var wsum := 0.0
	for j in 4:
		var wt: float = bone_wts[base + j] if base + j < bone_wts.size() else 0.0
		if wt <= 0.0001:
			continue
		var oid: int = bone_ids[base + j] if base + j < bone_ids.size() else -1
		if not worlds.has(oid):
			continue
		var xf: Transform3D = worlds[oid]
		acc += xf.basis * bind_war3_n * wt
		wsum += wt
	if wsum <= 0.0001:
		return bind_war3_n
	return (acc / wsum).normalized()
