extends SceneTree
## Smoke: Bistro lit applies + switches cleanly to War3.


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

	var settings = load("res://addons/w3kit/bistro_lit_default.tres")
	preload("res://addons/w3kit_cs/Lighting/BistroLightingController.cs").apply_to_scene(root, settings)
	var ok := true
	if we.environment == null:
		ok = false
		push_error("no env")
	else:
		print("SMOKE tonemap=", we.environment.tonemap_mode, " ssao=", we.environment.ssao_intensity)
		print("SMOKE sky=", we.environment.sky.sky_material.get_class() if we.environment.sky else "null")
		print("SMOKE sat=", we.environment.adjustment_saturation)
		if we.environment.tonemap_mode != Environment.TONE_MAPPER_ACES:
			ok = false
		if not (we.environment.sky.sky_material is ProceduralSkyMaterial):
			ok = false
	print("SMOKE sun energy=", sun.light_energy, " color=", sun.light_color)
	preload("res://addons/w3kit_cs/Lighting/W3LightingController.cs").apply_to_scene(root, load("res://addons/w3kit/w3_lit_settings_preview.tres"))
	print("SMOKE after War3 energy=", sun.light_energy)
	print("SMOKE ok=", ok)
	quit(0 if ok else 2)
