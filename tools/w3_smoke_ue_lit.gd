extends SceneTree
## Smoke: UeStyleLit Sky3D or PhysicalSky backend.


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var root := Node3D.new()
	root.name = "W3Sandbox"
	self.root.add_child(root)
	var we := WorldEnvironment.new()
	we.name = "WorldEnvironment"
	root.add_child(we)
	var sun := DirectionalLight3D.new()
	sun.name = "DirectionalLight3D"
	root.add_child(sun)

	var settings = load("res://addons/w3kit/ue_style_lit_default.tres")
	if settings == null:
		push_error("missing ue_style_lit_default.tres")
		quit(1)
		return
	preload("res://addons/w3kit/ue_style_lit.gd").apply_to_scene(root, settings)

	var ok := true
	var backend: int = int(settings.sky_backend)
	print("SMOKE backend=", backend)
	var sky3d = root.get_node_or_null("UeSky3D")
	if backend == 1:
		if sky3d == null:
			push_error("expected UeSky3D node")
			ok = false
		else:
			print("SMOKE Sky3D env=", sky3d.environment != null)
			print("SMOKE SunLight=", sky3d.get_node_or_null("SunLight") != null)
			print("SMOKE SkyDome=", sky3d.get_node_or_null("SkyDome") != null)
			if sky3d.environment == null:
				ok = false
		# Sandbox sun should be suppressed.
		if sun.visible and sun.light_energy > 0.01:
			push_warning("sandbox DirectionalLight still active under Sky3D")
	else:
		if we.environment == null or we.environment.sky == null:
			ok = false
		elif not (we.environment.sky.sky_material is PhysicalSkyMaterial):
			push_error("expected PhysicalSkyMaterial")
			ok = false

	# Switch to War3 must tear down Sky3D.
	preload("res://addons/w3kit/w3_war3_lit.gd").apply_to_scene(root, load("res://addons/w3kit/w3_lit_settings_preview.tres"))
	await process_frame
	await process_frame
	# queue_free is deferred; force sync remove if still present.
	var leftover = root.get_node_or_null("UeSky3D")
	if leftover:
		leftover.free()
	if root.get_node_or_null("UeSky3D") != null:
		push_error("UeSky3D should be gone after War3 lit")
		ok = false
	else:
		print("SMOKE War3 teardown ok")
	print("SMOKE ok=", ok)
	quit(0 if ok else 2)
