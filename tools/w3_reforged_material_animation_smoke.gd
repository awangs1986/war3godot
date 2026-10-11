extends SceneTree

const W3MdxAnim = preload("res://addons/w3kit/w3_mdx_anim.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var reforged_root := ProjectSettings.globalize_path("res://war3/reforged")
	var relative := "Doodads/Cityscape/Props/City_Fountain/City_Fountain.mdx"
	var model_path := reforged_root.path_join(relative)
	var model = W3MdxModel.load_file(model_path)
	if model == null or not model.has_layer_animation():
		_fail("fountain KMTF/KMTA tracks were not parsed")
		return
	var animated_layers := 0
	var animated_slots := 0
	var sampled_ids := PackedInt32Array()
	for layers in model.materials:
		for layer in layers:
			if not layer.alpha_times.is_empty() or _has_texture_track(layer):
				animated_layers += 1
			for slot_index in layer.texture_track_times.size():
				var times: PackedInt32Array = layer.texture_track_times[slot_index]
				if times.is_empty():
					continue
				animated_slots += 1
				var values: PackedInt32Array = layer.texture_track_values[slot_index]
				if values.size() < 2:
					_fail("fountain flipbook track has fewer than two frames")
					return
				for sampled in [values[0], values[values.size() - 1]]:
					var texture_id := int(sampled)
					if texture_id < 0 or texture_id >= model.textures.size():
						_fail("KMTF sampled an invalid texture id")
						return
					sampled_ids.append(texture_id)
	if animated_slots < 3:
		_fail("fountain did not expose diffuse/normal flipbook tracks")
		return

	var provider := W3TextureProvider.new()
	provider.set_map_dir(reforged_root)
	var instance := W3MdxInstance.new()
	root.add_child(instance)
	instance.setup(model, provider, model_path, {"animate": true})
	if not instance._playback or instance._material_tracks.is_empty():
		_fail("runtime did not bind fountain material tracks")
		return
	var first_entry: Dictionary = instance._material_tracks[0]
	var first_layer = first_entry["layer"]
	var track_index := _first_track_index(first_layer)
	if track_index < 0:
		_fail("runtime material binding has no KMTF")
		return
	var track_times: PackedInt32Array = first_layer.texture_track_times[track_index]
	var track_values: PackedInt32Array = first_layer.texture_track_values[track_index]
	var seq_index := int(model.find_idle_sequence())
	var seq_start := int(model.sequences[seq_index].interval_start) if seq_index >= 0 else -1
	var seq_end := int(model.sequences[seq_index].interval_end) if seq_index >= 0 else -1
	var global_sequence_id := int(first_layer.texture_track_global_sequences[track_index])
	var sample_start := seq_start
	var sample_end := seq_end
	var filter_start := seq_start
	var filter_end := seq_end
	if global_sequence_id >= 0 and global_sequence_id < model.global_sequences.size():
		sample_start = 0
		sample_end = maxi(1, int(model.global_sequences[global_sequence_id])) - 1
		filter_start = -1
		filter_end = -1
	var first_id := W3MdxAnim.sample_int_step(track_times, track_values, sample_start, -1, filter_start, filter_end)
	var last_id := W3MdxAnim.sample_int_step(track_times, track_values, sample_end, -1, filter_start, filter_end)
	if first_id < 0 or last_id < 0 or first_id == last_id:
		_fail("KMTF runtime step sampling did not advance")
		return
	instance._anim_time = float(maxi(0, seq_end - seq_start - 1))
	instance._global_time = float(sample_end)
	instance._update_material_tracks()
	first_entry = instance._material_tracks[0]
	var cached_ids: PackedInt32Array = first_entry["last_texture_ids"]
	if cached_ids.is_empty() or cached_ids[track_index] != last_id:
		_fail("runtime did not apply the sampled texture frame cached=%s expected=%d global=%d times=%s" % [
			cached_ids, last_id,
			global_sequence_id, track_times,
		])
		return
	print("W3_REFORGED_MATERIAL_ANIM_OK layers=", animated_layers,
		" slots=", animated_slots, " sampled_ids=", sampled_ids,
		" runtime_bindings=", instance._material_tracks.size())
	instance.queue_free()
	await process_frame
	quit(0)


func _has_texture_track(layer) -> bool:
	return _first_track_index(layer) >= 0


func _first_track_index(layer) -> int:
	for i in layer.texture_track_times.size():
		var times: PackedInt32Array = layer.texture_track_times[i]
		if not times.is_empty():
			return i
	return -1


func _fail(message: String) -> void:
	printerr("W3_REFORGED_MATERIAL_ANIM_FAIL ", message)
	quit(1)
