extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var terrain = load("res://TerrainBrushTest.tscn").instantiate()
	root.add_child(terrain)
	for _frame in 8:
		await process_frame
	var ground := terrain.get_node("_TerrainChunks/Chunk_0_0/Ground") as MeshInstance3D
	var material := ground.material_override as ShaderMaterial
	var palette := material.get_shader_parameter("palette_tex") as Texture2D
	var indices := material.get_shader_parameter("ground_index") as Texture2D
	var environment := (terrain.get_node("WorldEnvironment") as WorldEnvironment).environment
	var sun := terrain.get_node("Sun") as DirectionalLight3D
	print("W3_TERRAIN_MATERIAL_PROBE shader=", material.shader != null,
		" palette=", palette.get_size() if palette else Vector2i.ZERO,
		" palette_px=", palette.get_image().get_pixel(4, 0) if palette else Color.BLACK,
		" index=", indices.get_size() if indices else Vector2i.ZERO,
		" index_px=", indices.get_image().get_pixel(0, 0) if indices else Color.BLACK,
		" env=", environment != null,
		" ambient=", environment.ambient_light_energy if environment else -1.0,
		" bg=", environment.background_mode if environment else -1,
		" sun=", sun.light_energy,
		" camera=", root.get_viewport().get_camera_3d().global_position)
	quit(0)
