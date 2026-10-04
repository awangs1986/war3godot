extends SceneTree
## Real render check. Run with a display/software GL, NOT --headless (dummy renderer).
## No War3 assets: actual production shaders, constant textures, fixed exposure.

var viewport: SubViewport
var sun: DirectionalLight3D
var checks := {}
var samples := {}
var capture_index := 0
var evidence_dir := ""

func _initialize() -> void:
	_run.call_deferred()

func solid(color: Color, format := Image.FORMAT_RGBA8) -> ImageTexture:
	var image := Image.create(2, 2, false, format)
	image.fill(color)
	return ImageTexture.create_from_image(image)

func capture() -> Color:
	for i in 3:
		await process_frame
	await RenderingServer.frame_post_draw
	var image := viewport.get_texture().get_image()
	if not evidence_dir.is_empty():
		image.save_png(evidence_dir.path_join("sample_%02d.png" % capture_index))
	capture_index += 1
	return image.get_pixel(32, 32)

func brightness(c: Color) -> float:
	return (c.r + c.g + c.b) / 3.0

func probe(label: String, geometry: GeometryInstance3D, material: ShaderMaterial, flat_param: String) -> void:
	viewport.add_child(geometry)
	geometry.material_override = material
	material.set_shader_parameter(flat_param, false)
	sun.light_color = Color.WHITE
	sun.light_energy = 0.0
	var dark := await capture()
	sun.light_energy = 1.0
	var lit := await capture()
	sun.light_color = Color(1.0, 0.0, 0.0)
	var red := await capture()
	checks[label + "_receives_energy"] = brightness(lit) > brightness(dark) + 0.05
	checks[label + "_receives_color"] = red.r > red.g + 0.05
	material.set_shader_parameter(flat_param, true)
	sun.light_energy = 0.0
	var flat_dark := await capture()
	sun.light_color = Color.WHITE
	sun.light_energy = 3.0
	var flat_lit := await capture()
	checks[label + "_unlit_stable"] = absf(brightness(flat_lit) - brightness(flat_dark)) < 0.015 and brightness(flat_dark) > 0.05
	samples[label] = {"dark": brightness(dark), "lit": brightness(lit), "red": str(red), "flat_dark": brightness(flat_dark), "flat_lit": brightness(flat_lit)}
	viewport.remove_child(geometry)
	geometry.queue_free()

func plane() -> MeshInstance3D:
	var mesh := PlaneMesh.new()
	mesh.size = Vector2.ONE
	var node := MeshInstance3D.new()
	node.mesh = mesh
	node.position = Vector3(0.5, 0.0, 0.5)
	return node

func _run() -> void:
	if DisplayServer.get_name() == "headless":
		printerr("This probe requires a real renderer; use a display or xvfb-run.")
		quit(2)
		return
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--evidence="):
			evidence_dir = arg.trim_prefix("--evidence=")
			DirAccess.make_dir_recursive_absolute(evidence_dir)
	# The project defaults to FSR2; these tiny fixed-resolution probes must
	# measure the material itself and must not allocate FSR2 mip chains.
	root.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR
	root.scaling_3d_scale = 1.0
	viewport = SubViewport.new()
	viewport.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR
	viewport.scaling_3d_scale = 1.0
	viewport.size = Vector2i(64, 64)
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color.BLACK
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_DISABLED
	environment.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	var world_environment := WorldEnvironment.new()
	world_environment.environment = environment
	viewport.add_child(world_environment)
	var camera := Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 1.0
	camera.position = Vector3(0.5, 2.0, 0.5)
	camera.rotation_degrees.x = -90.0
	viewport.add_child(camera)
	camera.make_current()
	sun = DirectionalLight3D.new()
	sun.rotation_degrees.x = -90.0
	viewport.add_child(sun)

	var cliff := ShaderMaterial.new()
	cliff.shader = load("res://addons/w3kit/shaders/w3_cliff_lit.gdshader")
	cliff.set_shader_parameter("albedo_tex", solid(Color(0.4, 0.4, 0.4)))
	await probe("cliff", plane(), cliff, "flat_lighting")

	var ground := ShaderMaterial.new()
	ground.shader = load("res://addons/w3kit/shaders/w3_ground_unified.gdshader")
	ground.set_shader_parameter("map_tiles", Vector2i.ONE)
	ground.set_shader_parameter("map_corners", Vector2i(2, 2))
	ground.set_shader_parameter("tile_size", 1.0)
	ground.set_shader_parameter("height_tex", solid(Color.BLACK, Image.FORMAT_RF))
	ground.set_shader_parameter("exists_tex", solid(Color.WHITE))
	ground.set_shader_parameter("tile_data", solid(Color(0, 60000, 60000, 60000), Image.FORMAT_RGBAF))
	ground.set_shader_parameter("shd_enabled", false)
	var atlas := Texture2DArray.new()
	var tex := Image.create(4, 4, false, Image.FORMAT_RGBA8)
	tex.fill(Color(0.4, 0.4, 0.4))
	tex.generate_mipmaps()
	atlas.create_from_images([tex])
	for bank in 3:
		ground.set_shader_parameter("ground_array" + str(bank), atlas)
	var quad := PlaneMesh.new()
	quad.size = Vector2.ONE
	quad.center_offset = Vector3(0.5, 0, 0.5)
	var multimesh := MultiMesh.new()
	multimesh.transform_format = MultiMesh.TRANSFORM_3D
	multimesh.mesh = quad
	multimesh.instance_count = 1
	multimesh.set_instance_transform(0, Transform3D.IDENTITY)
	var ground_mesh := MultiMeshInstance3D.new()
	ground_mesh.multimesh = multimesh
	await probe("ground", ground_mesh, ground, "flat_lighting")

	var water := ShaderMaterial.new()
	water.shader = load("res://addons/w3kit/shaders/w3_gpu_water.gdshader")
	water.set_shader_parameter("map_tiles", Vector2i.ONE)
	water.set_shader_parameter("map_corners", Vector2i(2, 2))
	water.set_shader_parameter("tile_size", 1.0)
	water.set_shader_parameter("water_height_offset", 0.0)
	water.set_shader_parameter("water_height_tex", solid(Color(0.2, 0, 0), Image.FORMAT_RF))
	water.set_shader_parameter("ground_height_tex", solid(Color.BLACK, Image.FORMAT_RF))
	water.set_shader_parameter("water_exists_tex", solid(Color.WHITE))
	var water_mesh := MultiMeshInstance3D.new()
	water_mesh.multimesh = multimesh
	water_mesh.material_override = water
	viewport.add_child(water_mesh)
	sun.light_energy = 0.0
	var water_dark := await capture()
	sun.light_energy = 1.0
	var water_lit := await capture()
	checks["water_no_night_emission"] = brightness(water_dark) < 0.005
	checks["water_receives_energy"] = brightness(water_lit) > brightness(water_dark) + 0.02
	samples["water"] = {"dark": brightness(water_dark), "lit": brightness(water_lit)}
	viewport.remove_child(water_mesh)
	water_mesh.queue_free()

	for suffix in ["_opaque", "", "_modulate", "_add"]:
		var hd := ShaderMaterial.new()
		hd.shader = load("res://addons/w3kit/shaders/w3_reforged_unit" + suffix + ".gdshader")
		hd.set_shader_parameter("diffuse_map", solid(Color(0.4, 0.4, 0.4)))
		hd.set_shader_parameter("normal_map", solid(Color(0.5, 0.5, 1.0)))
		hd.set_shader_parameter("orm_map", solid(Color(1.0, 0.5, 0.0, 0.0)))
		hd.set_shader_parameter("emissive_map", solid(Color.BLACK))
		if suffix == "_modulate":
			environment.background_color = Color.WHITE
		if suffix == "_add":
			# Additive effects are intrinsically emissive in the material policy.
			var mesh := plane()
			mesh.material_override = hd
			viewport.add_child(mesh)
			sun.light_energy = 0.0
			var dark := await capture()
			sun.light_energy = 3.0
			var lit := await capture()
			checks["hd_add_unlit_stable"] = absf(brightness(lit) - brightness(dark)) < 0.015 and brightness(dark) > 0.05
			samples["hd_add"] = {"dark": brightness(dark), "lit": brightness(lit)}
			viewport.remove_child(mesh)
			mesh.queue_free()
		else:
			await probe("hd" + suffix, plane(), hd, "unshaded_layer")
		environment.background_color = Color.BLACK

	var passed := true
	for value in checks.values():
		passed = passed and value
	var report := {"pass": passed, "checks": checks, "samples": samples}
	if not evidence_dir.is_empty():
		var output := FileAccess.open(evidence_dir.path_join("result.json"), FileAccess.WRITE)
		output.store_string(JSON.stringify(report, "  "))
	print(JSON.stringify(report, "  "))
	print("MATERIAL_LIGHTING_SMOKE_OK" if passed else "MATERIAL_LIGHTING_SMOKE_FAIL")
	quit(0 if passed else 1)
