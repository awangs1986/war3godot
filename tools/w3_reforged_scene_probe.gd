extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := load("res://scenes/w3_lost_temple_preview.tscn") as PackedScene
	var scene := packed.instantiate()
	root.add_child(scene)
	var world = scene.get_node("W3MapWorld")
	for _frame in 900:
		await process_frame
		var counts: Dictionary = world.batcher.get_counts()
		if int(counts.jobs) == 0 and int(counts.queued) == 0 and int(counts.anim_pending) == 0 and int(counts.ok) > 5000:
			break
	var args := OS.get_cmdline_user_args()
	var target_tile := Vector2i(
		int(args[0]) if args.size() > 0 else 84,
		int(args[1]) if args.size() > 1 else 65
	)
	var camera := scene.get_node("FreeFlyCamera") as Camera3D
	var target: Vector3 = world.sampler.tile_center_world(target_tile)
	camera.focus_point(target, 950.0)
	await process_frame
	await process_frame
	var viewport_size := root.get_viewport().get_visible_rect().size
	print("SCENE_PROBE viewport=", viewport_size, " camera=", camera.global_position, " target=", target)

	var seen_static: Dictionary = {}
	for batch in world.batcher._batches.values():
		var material = batch.mm.mesh.surface_get_material(0) if batch.mm.mesh != null else null
		for item in batch.items:
			var center: Vector3 = item.center
			var key := "%s|%.2f|%.2f|%.2f" % [item.rawcode, center.x, center.y, center.z]
			if seen_static.has(key) or camera.is_position_behind(center):
				continue
			seen_static[key] = true
			var screen := camera.unproject_position(center)
			var normalized := screen / viewport_size
			if normalized.x >= 0.35 and normalized.x <= 1.05 and normalized.y >= 0.35 and normalized.y <= 1.15:
				print(" STATIC raw=", item.rawcode, " pos=", center, " screen=", normalized,
					" model=", item.model_rel, " shader=", _shader_path(material))

	for cliff in world.cliffs.get_children():
		if not (cliff is Node3D) or camera.is_position_behind(cliff.global_position):
			continue
		var screen := camera.unproject_position(cliff.global_position)
		var normalized := screen / viewport_size
		if normalized.x < 0.35 or normalized.x > 1.05 or normalized.y < 0.35 or normalized.y > 1.15:
			continue
		var texture_size := Vector2i.ZERO
		for mesh_node in cliff.find_children("*", "MeshInstance3D", true, false):
			var mesh := mesh_node as MeshInstance3D
			if mesh.material_override is ShaderMaterial:
				var texture = (mesh.material_override as ShaderMaterial).get_shader_parameter("albedo_tex")
				if texture is Texture2D:
					texture_size = Vector2i(texture.get_width(), texture.get_height())
				break
		print(" CLIFF tile=", cliff.get_meta("w3_placement_tile", Vector2i(-1, -1)),
			" source=", cliff.get_meta("w3_source_tile", Vector2i(-1, -1)),
			" pos=", cliff.global_position, " screen=", normalized,
			" model=", cliff.get_meta("w3_model_rel", ""), " texture=", texture_size)
	for instance in world.batcher._anim_instances:
		if instance == null or not is_instance_valid(instance) or camera.is_position_behind(instance.global_position):
			continue
		var screen := camera.unproject_position(instance.global_position)
		var normalized := screen / viewport_size
		if normalized.x < 0.0 or normalized.x > 1.0 or normalized.y < 0.0 or normalized.y > 1.0:
			continue
		var item: Dictionary = instance.get_meta("w3_preview_item", {})
		print(" ANIM raw=", item.get("rawcode", ""), " pos=", instance.global_position,
			" screen=", normalized, " model=", item.get("model_rel", ""),
			" material_tracks=", instance._material_tracks.size())
	quit(0)


func _shader_path(material) -> String:
	if material is ShaderMaterial and (material as ShaderMaterial).shader != null:
		return str((material as ShaderMaterial).shader.resource_path)
	return str(material)
