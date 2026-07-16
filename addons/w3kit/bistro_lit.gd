extends RefCounted
## Bistro-Demo-Tweaked inspired outdoor stack for diablo1like.
## Source of ideas: https://github.com/Jamsers/Bistro-Demo-Tweaked (CC-BY demo).
## Does not touch War3 / UE-Sky3D paths; teardown Sky3D when applied.


const BistroLitSettingsScript = preload("res://addons/w3kit/bistro_lit_settings.gd")
const W3LitSettingsScript = preload("res://addons/w3kit/w3_lit_settings.gd")
const UeStyleLit = preload("res://addons/w3kit/ue_style_lit.gd")


static func apply_to_scene(root: Node, settings = null) -> void:
	if root == null:
		return
	UeStyleLit.teardown_ue_only_nodes(root)
	var s = settings
	if s == null or not (s is Resource) or s.get_script() == null:
		s = load("res://addons/w3kit/bistro_lit_default.tres")
	if s == null:
		s = BistroLitSettingsScript.new()

	var p: Dictionary = _resolve_params(s)
	_ensure_environment(root, s, p)
	_ensure_key_light(root, s, p)
	_ensure_fill_light(root, p)
	_sync_terrain(root, p)
	# Props/units + ground: leave War3 baked look; take DirectionalLight + sky.
	preload("res://addons/w3kit/w3_war3_lit.gd").set_models_receive_lights(root, true)
	preload("res://addons/w3kit/w3_war3_lit.gd")._set_terrain_receive_lights(root, true)


static func _resolve_params(s) -> Dictionary:
	if bool(s.get("manual_override")):
		return {
			"temp": float(s.get("sun_temperature_k")),
			"energy": float(s.get("sun_energy")),
			"pitch": float(s.get("sun_pitch_deg")),
			"yaw": float(s.get("sun_yaw_deg")),
			"sky_energy": float(s.get("sun_energy")) * 0.85,
			"ambient": 0.9,
			"exposure": float(s.get("exposure")),
		}
	var sc = int(s.get("scenario"))
	var p: Dictionary = BistroLitSettingsScript.scenario_params(sc)
	# Fog / exposure still honor resource fog_enabled via caller duplicate.
	if s.get("exposure") != null and not bool(s.get("manual_override")):
		# Keep scenario exposure unless user moved the Look.exposure away from default lightly —
		# always apply Look.exposure as multiplier on scenario.
		p["exposure"] = float(s.get("exposure")) * float(p.get("exposure", 1.0)) / 1.05
	return p


static func _ensure_environment(root: Node, s, p: Dictionary) -> void:
	var we := root.get_node_or_null("WorldEnvironment") as WorldEnvironment
	if we == null:
		we = WorldEnvironment.new()
		we.name = "WorldEnvironment"
		root.add_child(we)

	# Bistro uses ProceduralSkyMaterial (simple sun disk) + heavy post stack.
	var sky_mat := ProceduralSkyMaterial.new()
	sky_mat.sky_top_color = Color(0.28, 0.48, 0.82)
	sky_mat.sky_horizon_color = Color(0.72, 0.78, 0.88)
	sky_mat.ground_bottom_color = Color(0.12, 0.11, 0.1)
	sky_mat.ground_horizon_color = Color(0.45, 0.42, 0.38)
	sky_mat.sun_angle_max = 5.0
	sky_mat.sun_curve = 0.08
	sky_mat.sky_energy_multiplier = float(p.get("sky_energy", 1.2))
	sky_mat.ground_energy_multiplier = 0.35

	var sky := Sky.new()
	sky.sky_material = sky_mat
	sky.process_mode = Sky.PROCESS_MODE_REALTIME
	sky.radiance_size = Sky.RADIANCE_SIZE_256

	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_energy = float(p.get("ambient", 0.95))
	env.ambient_light_sky_contribution = 1.0
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY

	# tonemap_mode 3 == ACES in Bistro WorldEnvironment.tres
	env.tonemap_mode = Environment.TONE_MAPPER_ACES
	env.tonemap_exposure = float(p.get("exposure", 1.05))
	env.tonemap_white = 6.0

	env.adjustment_enabled = true
	env.adjustment_saturation = float(s.get("saturation")) if s.get("saturation") != null else 1.17

	env.glow_enabled = bool(s.get("glow_enabled"))
	env.glow_intensity = float(s.get("glow_intensity")) if s.get("glow_intensity") != null else 0.85
	env.glow_strength = float(s.get("glow_strength")) if s.get("glow_strength") != null else 0.7
	env.glow_bloom = 0.05
	env.glow_blend_mode = Environment.GLOW_BLEND_MODE_SOFTLIGHT
	for lvl in [1, 2, 4, 5, 6]:
		env.set_glow_level(lvl, 1.0)

	env.ssao_enabled = true
	env.ssao_radius = 1.2
	env.ssao_intensity = float(s.get("ssao_intensity")) if s.get("ssao_intensity") != null else 1.6
	env.ssao_horizon = 0.0
	env.ssao_sharpness = 0.85

	env.ssil_enabled = bool(s.get("ssil_enabled"))
	env.ssil_radius = 2.3
	env.ssil_intensity = 1.0
	env.ssil_sharpness = 0.9

	env.ssr_enabled = bool(s.get("ssr_enabled"))
	if env.ssr_enabled:
		env.ssr_max_steps = 64
		env.ssr_fade_in = 0.05
		env.ssr_fade_out = 1.5

	env.sdfgi_enabled = bool(s.get("sdfgi_enabled"))
	if env.sdfgi_enabled:
		env.sdfgi_bounce_feedback = 1.0
		env.sdfgi_use_occlusion = true
		env.sdfgi_cascades = 4
		env.sdfgi_min_cell_size = 16.0

	# Volumetric fog density 0 in Bistro = disabled contribution; keep depth fog for outdoor maps.
	env.volumetric_fog_enabled = false
	var fog_on: bool = bool(s.get("fog_enabled"))
	env.fog_enabled = fog_on
	env.fog_mode = Environment.FOG_MODE_DEPTH
	env.fog_light_color = Color(0.7, 0.74, 0.82)
	var fog_dist: float = float(s.get("fog_distance")) if s.get("fog_distance") != null else 4800.0
	var fog_amt: float = clampf(float(s.get("fog_intensity")) if s.get("fog_intensity") != null else 0.2, 0.0, 1.0)
	env.fog_depth_begin = fog_dist
	env.fog_depth_end = fog_dist + lerpf(16000.0, 3500.0, fog_amt)
	env.fog_aerial_perspective = 0.4 * fog_amt if fog_on else 0.0
	env.fog_sun_scatter = 0.15 if fog_on else 0.0

	we.environment = env

	# Bistro uses CameraAttributesPractical exposure — attach for ACES + auto feel.
	var attrs := CameraAttributesPractical.new()
	attrs.exposure_multiplier = float(p.get("exposure", 1.05))
	# Keep auto-exposure mild so War3 map pan doesn't pump wildly.
	attrs.auto_exposure_enabled = false
	we.camera_attributes = attrs


static func _ensure_key_light(root: Node, s, p: Dictionary) -> void:
	var sun := root.get_node_or_null("DirectionalLight3D") as DirectionalLight3D
	if sun == null:
		sun = root.get_node_or_null("Sun") as DirectionalLight3D
	if sun == null:
		sun = DirectionalLight3D.new()
		sun.name = "DirectionalLight3D"
		root.add_child(sun)
	root.move_child(sun, 0)
	sun.rotation_degrees = Vector3(float(p.get("pitch", -48.0)), float(p.get("yaw", 35.0)), 0.0)
	sun.light_color = W3LitSettingsScript.temperature_to_color(float(p.get("temp", 4250.0)))
	sun.light_energy = float(p.get("energy", 1.55))
	sun.light_indirect_energy = 1.1
	sun.light_specular = 0.75
	sun.shadow_enabled = bool(s.get("sun_shadows")) if s.get("sun_shadows") != null else true
	sun.shadow_blur = 1.5
	sun.shadow_opacity = 0.88
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS
	sun.directional_shadow_max_distance = 6500.0
	sun.directional_shadow_pancake_size = 40.0
	sun.light_angular_distance = 0.55
	sun.visible = true


static func _ensure_fill_light(root: Node, p: Dictionary) -> void:
	# Soft cool bounce opposite the key — Bistro relies more on GI; we fake a weak fill.
	var fill := root.get_node_or_null("FillLight3D") as DirectionalLight3D
	if fill == null:
		fill = DirectionalLight3D.new()
		fill.name = "FillLight3D"
		root.add_child(fill)
	var yaw: float = float(p.get("yaw", 35.0))
	fill.rotation_degrees = Vector3(-22.0, yaw + 160.0, 0.0)
	fill.light_color = Color(0.55, 0.65, 0.9)
	var e: float = float(p.get("energy", 1.5))
	fill.light_energy = clampf(e * 0.18, 0.05, 0.45)
	fill.shadow_enabled = false
	fill.visible = fill.light_energy > 0.02


static func _sync_terrain(root: Node, p: Dictionary) -> void:
	var world := root.get_node_or_null("W3MapWorld")
	if world == null:
		return
	var terrain = world.get("terrain")
	if terrain == null or not terrain.has_method("apply_lighting"):
		return
	var sun := root.get_node_or_null("DirectionalLight3D") as DirectionalLight3D
	var light_dir := Vector3(0.45, 0.85, 0.28)
	if sun:
		light_dir = -sun.global_transform.basis.z
	var ambient: float = clampf(0.4 + float(p.get("ambient", 0.95)) * 0.25, 0.4, 0.8)
	var col := Color.WHITE
	var scale := 1.45
	if sun:
		col = sun.light_color
		var e: float = clampf(sun.light_energy, 0.35, 2.5)
		col = Color(col.r * e, col.g * e, col.b * e)
		scale = 1.3
	terrain.apply_lighting(light_dir, ambient, scale, col)
