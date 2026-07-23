extends SceneTree

const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var rel := str(args[0]) if not args.is_empty() else "units/creeps/GnollArcher/GnollArcher.mdx"
	var started := Time.get_ticks_msec()
	var abs_path := rel if rel.is_absolute_path() else W3Paths.resolve_path(rel)
	printerr("MDX_PROBE resolving_ms=%d path=%s" % [Time.get_ticks_msec() - started, abs_path])
	var model = W3MdxModel.load_file(abs_path)
	printerr("MDX_PROBE loaded_ms=%d model=%s" % [Time.get_ticks_msec() - started, model != null])
	if model == null:
		quit(1)
		return
	var idle_index: int = int(model.find_idle_sequence())
	var idle_time := int(model.sequences[idle_index].interval_start) if idle_index >= 0 else 0
	printerr("MDX_PROBE geosets=%d textures=%d materials=%d" % [model.geosets.size(), model.textures.size(), model.materials.size()])
	printerr("MDX_PROBE attachments=%d emitters2=%d events=%d lights=%d" % [
		model.attachments.size(), model.particle_emitters2.size(), model.event_objects.size(), model.lights.size()])
	for attachment in model.attachments:
		printerr(" ATCH name=%s id=%d path=%s" % [attachment.node.name, attachment.attachment_id, attachment.path])
	for event in model.event_objects:
		printerr(" EVTS name=%s times=%s" % [event.node.name, event.times])
	for emitter in model.particle_emitters2:
		printerr(" PRE2 name=%s texture=%d rate=%.2f life=%.2f speed=%.2f latitude=%.2f size=%.2fx%.2f atlas=%dx%d scale=%s vis=%s/%s emit=%s/%s" % [
			emitter.node.name, emitter.texture_id, emitter.emission_rate, emitter.life_span,
			emitter.speed, emitter.latitude, emitter.width, emitter.length,
			emitter.columns, emitter.rows, emitter.scaling,
			emitter.visibility_times, emitter.visibility_values,
			emitter.emission_rate_times, emitter.emission_rate_values])
	for light in model.lights:
		printerr(" LITE name=%s type=%d atten=%.2f..%.2f color=%s intensity=%.3f ambient=%s/%.3f intensity_track=%s/%s visibility=%s/%s" % [
			light.node.name, light.type, light.attenuation_start, light.attenuation_end,
			light.color, light.intensity, light.ambient_color, light.ambient_intensity,
			light.intensity_times, light.intensity_values,
			light.visibility_times, light.visibility_values])
	for i in model.textures.size():
		printerr(" TEX %d rid=%d path=%s" % [i, model.texture_replaceable_ids[i], model.textures[i]])
	for i in model.materials.size():
		var desc: Array[String] = []
		for layer in model.materials[i]:
			desc.append("tex=%d slots=%s shader=%d filter=%d flags=0x%X" % [
				layer.texture_id, layer.texture_ids, layer.shader, layer.filter_mode, layer.flags,
			])
		printerr(" MAT %d layers=%s" % [i, desc])
	for i in model.geosets.size():
		var geoset = model.geosets[i]
		var uv_min := Vector2(1e9, 1e9)
		var uv_max := Vector2(-1e9, -1e9)
		for uv in geoset.uvs:
			uv_min = uv_min.min(uv)
			uv_max = uv_max.max(uv)
		printerr(" GEO %d mat=%d lod=%d stand_alpha=%.2f verts=%d faces=%d uv=%s..%s" % [
			i, geoset.material_id, geoset.lod, model.geoset_alpha_at(i, idle_time),
			geoset.vertices.size(), geoset.faces.size(), uv_min, uv_max])
	quit(0)
