extends RefCounted
## Applies W3LitSettings via Godot DirectionalLight3D + WorldEnvironment.
## Material helpers stay separate from tunable light knobs.


const W3LitSettingsScript = preload("res://addons/w3kit/w3_lit_settings.gd")


static func apply_to_scene(root: Node, settings = null) -> void:
	if root == null:
		return
	var s = settings
	if s == null or not (s is Resource) or s.get_script() == null:
		s = load("res://addons/w3kit/w3_lit_settings_default.tres")
	if s == null:
		s = W3LitSettingsScript.new()
	s = _apply_weather_preset(s)
	_ensure_environment(root, s)
	_ensure_key_light(root, s)
	_ensure_fill_light(root, s)
	_sync_terrain_open_realm_light(root, s)


static func _weather_of(s) -> String:
	var w = s.get("weather")
	return str(w) if w != null else "default"


## Weather presets override the base knobs; "default" keeps the legacy look.
## "sun": hard noon key light. "overcast": soft grey light, fog handled in
## _ensure_environment (far = dense, near stays fully clear).
static func _apply_weather_preset(s):
	match _weather_of(s):
		"sun":
			var d = s.duplicate()
			d.sun_intensity = 1.55
			d.sun_temperature_k = 5700.0
			d.sun_pitch_deg = -67.0
			d.ambient_intensity = 0.55
			d.fill_intensity = 0.18
			return d
		"overcast":
			var d = s.duplicate()
			d.sun_intensity = 0.75
			d.sun_temperature_k = 6600.0
			d.sun_pitch_deg = -55.0
			d.ambient_intensity = 0.95
			d.fill_intensity = 0.55
			return d
	return s


## Push open-realm Lambert params into GPU terrain (custom unshaded shader).
static func _sync_terrain_open_realm_light(root: Node, s) -> void:
	var world := root.get_node_or_null("W3MapWorld")
	if world == null:
		return
	var terrain = world.get("terrain")
	if terrain == null or not terrain.has_method("apply_lighting"):
		return
	var sun := root.get_node_or_null("DirectionalLight3D") as DirectionalLight3D
	if sun == null:
		sun = root.get_node_or_null("Sun") as DirectionalLight3D
	var light_dir := Vector3(0.45, 0.85, 0.28)
	if sun != null:
		# Godot directional light shines along -Z of its basis.
		light_dir = -sun.global_transform.basis.z
	# open-realm MDX defaults: ambient 0.35, directional scale baked into formula.
	var ambient: float = 0.35
	if s.get("ambient_intensity") != null:
		# Map our 0..2 ambient knob onto open-realm floor (~0.25–0.45).
		ambient = clampf(0.25 + float(s.get("ambient_intensity")) * 0.15, 0.2, 0.5)
	terrain.apply_lighting(light_dir, ambient, 1.1)
	var cliffs = world.get("cliffs")
	if cliffs != null and cliffs.has_method("apply_lighting"):
		cliffs.apply_lighting(light_dir, ambient, 1.1)


static func style_terrain_material(mat: StandardMaterial3D) -> void:
	if mat == null:
		return
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_PER_PIXEL
	mat.metallic = 0.0
	mat.roughness = 0.92
	mat.specular_mode = BaseMaterial3D.SPECULAR_SCHLICK_GGX
	mat.metallic_specular = 0.18


static func style_model_material(mat: StandardMaterial3D) -> void:
	if mat == null:
		return
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_PER_PIXEL
	mat.metallic = 0.0
	mat.roughness = 0.82
	mat.specular_mode = BaseMaterial3D.SPECULAR_SCHLICK_GGX
	mat.metallic_specular = 0.24


## Modern water look — no War3 Water*.blp. Geometry still from W3E heights.
static func style_modern_water_material(mat: StandardMaterial3D) -> void:
	if mat == null:
		return
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_PER_PIXEL
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.albedo_color = Color(0.12, 0.40, 0.52, 0.78)
	mat.albedo_texture = null
	mat.metallic = 0.12
	mat.roughness = 0.08
	mat.specular_mode = BaseMaterial3D.SPECULAR_SCHLICK_GGX
	mat.emission_enabled = true
	mat.emission = Color(0.05, 0.14, 0.18)
	mat.emission_energy_multiplier = 0.35
	mat.refraction_enabled = false
	mat.proximity_fade_enabled = true
	mat.proximity_fade_distance = 12.0
	mat.rim_enabled = true
	mat.rim = 0.4
	mat.rim_tint = 0.55
	mat.depth_draw_mode = BaseMaterial3D.DEPTH_DRAW_ALWAYS
	mat.render_priority = 2


static func _ensure_environment(root: Node, s) -> void:
	var weather := _weather_of(s)
	var we := root.get_node_or_null("WorldEnvironment") as WorldEnvironment
	if we == null:
		we = WorldEnvironment.new()
		we.name = "WorldEnvironment"
		root.add_child(we)
	var env := Environment.new()
	var sky_material := ProceduralSkyMaterial.new()
	match weather:
		"sun":
			sky_material.sky_top_color = Color(0.22, 0.42, 0.72)
			sky_material.sky_horizon_color = Color(0.74, 0.80, 0.86)
			sky_material.ground_bottom_color = Color(0.12, 0.11, 0.10)
			sky_material.ground_horizon_color = Color(0.56, 0.52, 0.44)
		"overcast":
			sky_material.sky_top_color = Color(0.44, 0.47, 0.51)
			sky_material.sky_horizon_color = Color(0.63, 0.65, 0.67)
			sky_material.ground_bottom_color = Color(0.12, 0.12, 0.12)
			sky_material.ground_horizon_color = Color(0.50, 0.50, 0.49)
		_:
			sky_material.sky_top_color = Color(0.18, 0.28, 0.43)
			sky_material.sky_horizon_color = Color(0.62, 0.68, 0.72)
			sky_material.ground_bottom_color = Color(0.10, 0.09, 0.08)
			sky_material.ground_horizon_color = Color(0.48, 0.43, 0.36)
	sky_material.sun_angle_max = 8.0
	sky_material.sun_curve = 0.08
	var sky := Sky.new()
	sky.sky_material = sky_material
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.background_energy_multiplier = 0.4
	# TPS uses color ambient and background reflections. Keep that pairing, but
	# adapt its warm indoor color to Lost Temple's outdoor daylight.
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.58, 0.61, 0.68)
	env.ambient_light_energy = float(s.get("ambient_intensity"))
	env.reflected_light_source = Environment.REFLECTION_SOURCE_BG
	var modern := bool(s.get("modern_pipeline_enabled"))
	env.tonemap_mode = Environment.TONE_MAPPER_AGX if modern else Environment.TONE_MAPPER_FILMIC
	env.tonemap_exposure = float(s.get("tonemap_exposure")) if modern else 1.0
	env.tonemap_white = 6.0
	env.tonemap_agx_contrast = float(s.get("agx_contrast")) if modern else 1.0
	env.ssr_enabled = false
	env.ssao_enabled = modern and bool(s.get("ssao_enabled"))
	env.ssao_radius = 64.0
	env.ssao_intensity = float(s.get("ssao_intensity"))
	env.ssao_power = 1.25
	env.ssao_detail = 0.65
	env.ssao_horizon = 0.06
	env.ssao_sharpness = 0.92
	env.ssao_light_affect = 0.30
	env.ssao_ao_channel_affect = 0.8
	env.ssil_enabled = modern and bool(s.get("ssil_enabled"))
	env.ssil_radius = 128.0
	env.ssil_intensity = 0.75
	env.ssil_sharpness = 0.9
	env.ssil_normal_rejection = 1.0
	env.sdfgi_enabled = modern and bool(s.get("dynamic_gi_enabled"))
	env.sdfgi_cascades = 4
	env.sdfgi_min_cell_size = 64.0
	env.sdfgi_max_distance = 6144.0
	env.sdfgi_cascade0_distance = 768.0
	env.sdfgi_y_scale = Environment.SDFGI_Y_SCALE_50_PERCENT
	env.sdfgi_use_occlusion = true
	env.sdfgi_read_sky_light = true
	env.sdfgi_bounce_feedback = 0.33
	env.sdfgi_energy = 1.25
	env.glow_enabled = modern and bool(s.get("glow_enabled"))
	env.glow_intensity = float(s.get("glow_intensity"))
	env.glow_strength = 0.85
	env.glow_bloom = 0.08
	env.glow_hdr_threshold = 0.9
	env.glow_hdr_scale = 1.7
	env.glow_hdr_luminance_cap = 3.0
	env.set_glow_level(3, 0.0)
	env.set_glow_level(4, 1.0)
	# TPS serialized glow level 7 on an older layout; Godot 4.7 exposes 0..6.
	env.set_glow_level(6, 1.0)
	var fog_on: bool = bool(s.get("fog_enabled"))
	env.fog_enabled = fog_on
	env.fog_mode = Environment.FOG_MODE_DEPTH
	var fog_dist: float = float(s.get("fog_distance")) if s.get("fog_distance") != null else 4000.0
	var fog_amt: float = clampf(float(s.get("fog_intensity")) if s.get("fog_intensity") != null else 0.35, 0.0, 1.0)
	if weather == "overcast":
		# Overcast fog contract: everything near the camera stays at full
		# clarity/contrast (fog only starts past gameplay range), then ramps up
		# steeply so the far map drowns in haze. depth_curve > 1 delays the
		# ramp; density stays 0 so depth fog alone never washes near units.
		env.fog_light_color = Color(0.66, 0.68, 0.71)
		env.fog_light_energy = 0.9
		env.fog_depth_begin = 1400.0
		env.fog_depth_end = 4600.0
		env.fog_depth_curve = 1.6
		env.fog_aerial_perspective = 0.6
		# In FOG_MODE_DEPTH the density acts as the maximum fog opacity; the
		# near range stays clear because depth_begin/curve delay the ramp.
		env.fog_density = 0.92
	else:
		env.fog_light_color = Color(0.55, 0.57, 0.60)
		env.fog_light_energy = 0.65
		env.fog_depth_begin = fog_dist
		env.fog_depth_end = fog_dist + lerpf(14000.0, 2800.0, fog_amt)
		env.fog_depth_curve = lerpf(1.0, 0.55, fog_amt)
		env.fog_aerial_perspective = 0.25 * fog_amt if fog_on else 0.0
		env.fog_density = 0.00008
	env.volumetric_fog_enabled = modern and fog_on and bool(s.get("volumetric_fog_enabled"))
	if weather == "overcast":
		# Thin grey volumetric layer for the "rolling mist" feel; density is an
		# order of magnitude too low to blur nearby monsters.
		env.volumetric_fog_density = 0.00006
		env.volumetric_fog_albedo = Color(0.80, 0.82, 0.85)
		env.volumetric_fog_emission = Color(0.030, 0.032, 0.036)
		env.volumetric_fog_emission_energy = 0.30
		env.volumetric_fog_length = 6400.0
		env.volumetric_fog_detail_spread = 2.2
		env.volumetric_fog_gi_inject = 0.25
	else:
		env.volumetric_fog_density = 0.00010
		env.volumetric_fog_albedo = Color(0.82, 0.78, 0.69)
		env.volumetric_fog_emission = Color(0.025, 0.018, 0.010)
		env.volumetric_fog_emission_energy = 0.35
		env.volumetric_fog_length = 4600.0
		env.volumetric_fog_detail_spread = 1.5
		env.volumetric_fog_gi_inject = 0.35
	env.adjustment_enabled = modern
	env.adjustment_brightness = 1.0
	env.adjustment_contrast = 1.04
	env.adjustment_saturation = float(s.get("saturation"))
	we.environment = env
	we.set_meta("w3_tps_modern_lighting", modern)


static func _ensure_key_light(root: Node, s) -> void:
	var sun := root.get_node_or_null("DirectionalLight3D") as DirectionalLight3D
	if sun == null:
		sun = root.get_node_or_null("Sun") as DirectionalLight3D
	if sun == null:
		sun = DirectionalLight3D.new()
		sun.name = "DirectionalLight3D"
		root.add_child(sun)
	sun.rotation_degrees = Vector3(float(s.get("sun_pitch_deg")), float(s.get("sun_yaw_deg")), 0.0)
	sun.light_color = W3LitSettingsScript.temperature_to_color(float(s.get("sun_temperature_k")))
	# open-realm directional ≈ 0.75 on models; sun_intensity scales that.
	sun.light_energy = float(s.get("sun_intensity"))
	sun.light_indirect_energy = 0.0
	sun.shadow_enabled = bool(s.get("sun_shadows"))
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS
	sun.directional_shadow_max_distance = 5600.0
	sun.directional_shadow_fade_start = 0.82
	sun.directional_shadow_split_1 = 0.08
	sun.directional_shadow_split_2 = 0.22
	sun.directional_shadow_split_3 = 0.48
	sun.shadow_bias = 0.08
	sun.shadow_normal_bias = 1.2
	match _weather_of(s):
		"sun":
			# Noon sun: small solar disc + minimal penumbra = hard-edged, dark
			# shadows.
			sun.shadow_opacity = 0.85
			sun.shadow_blur = 0.35
			sun.light_angular_distance = 0.2
		"overcast":
			# Sky-sized light source: faint, wide, blurry contact shadows.
			sun.shadow_opacity = 0.30
			sun.shadow_blur = 3.0
			sun.light_angular_distance = 3.0
		_:
			sun.shadow_opacity = 0.55
			sun.shadow_blur = 1.25
			sun.light_angular_distance = 0.65


static func _ensure_fill_light(root: Node, s) -> void:
	var fill := root.get_node_or_null("FillLight3D") as DirectionalLight3D
	if fill == null:
		fill = DirectionalLight3D.new()
		fill.name = "FillLight3D"
		root.add_child(fill)
	var yaw: float = float(s.get("sun_yaw_deg"))
	var fill_e: float = float(s.get("fill_intensity"))
	fill.rotation_degrees = Vector3(-25.0, yaw - 180.0, 0.0)
	fill.light_color = Color(0.72, 0.78, 0.92)
	fill.light_energy = fill_e
	fill.shadow_enabled = false
	fill.visible = fill_e > 0.001


static func apply_quality_lod(root: Node, lod: int, settings = null) -> void:
	var we := root.get_node_or_null("WorldEnvironment") as WorldEnvironment
	var sun := root.get_node_or_null("DirectionalLight3D") as DirectionalLight3D
	if we == null or we.environment == null:
		return
	var env := we.environment
	var s = settings
	if s == null:
		s = load("res://addons/w3kit/w3_lit_settings_preview.tres")
	var modern := s != null and bool(s.get("modern_pipeline_enabled"))
	var high := lod <= 0
	var medium := lod <= 1
	var far := lod <= 2
	env.ssao_enabled = modern and far and bool(s.get("ssao_enabled"))
	env.ssil_enabled = modern and medium and bool(s.get("ssil_enabled"))
	env.sdfgi_enabled = modern and medium and bool(s.get("dynamic_gi_enabled"))
	env.glow_enabled = modern and far and bool(s.get("glow_enabled"))
	env.volumetric_fog_enabled = modern and medium and bool(s.get("volumetric_fog_enabled")) and bool(s.get("fog_enabled"))
	if sun != null:
		sun.shadow_enabled = far and bool(s.get("sun_shadows"))
		sun.directional_shadow_max_distance = 5600.0 if high else 4200.0
