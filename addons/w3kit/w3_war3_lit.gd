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
	_ensure_environment(root, s)
	_ensure_key_light(root, s)
	_ensure_fill_light(root, s)
	_sync_terrain_open_realm_light(root, s)


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


static func style_terrain_material(mat: StandardMaterial3D) -> void:
	if mat == null:
		return
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_PER_PIXEL
	mat.metallic = 0.0
	mat.roughness = 0.92
	mat.specular_mode = BaseMaterial3D.SPECULAR_DISABLED


static func style_model_material(mat: StandardMaterial3D) -> void:
	if mat == null:
		return
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_PER_PIXEL
	mat.metallic = 0.0
	mat.roughness = 0.88
	mat.specular_mode = BaseMaterial3D.SPECULAR_DISABLED


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
	var we := root.get_node_or_null("WorldEnvironment") as WorldEnvironment
	if we == null:
		we = WorldEnvironment.new()
		we.name = "WorldEnvironment"
		root.add_child(we)
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	# open-realm clear / sky-ish cool grey-blue
	env.background_color = Color(0.40, 0.48, 0.58)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	# Models use Godot lights; match open-realm ambient≈0.35 energy scale.
	env.ambient_light_color = Color(0.78, 0.80, 0.86)
	env.ambient_light_energy = float(s.get("ambient_intensity"))
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.tonemap_exposure = 1.0
	env.ssr_enabled = false
	env.ssao_enabled = false
	env.ssil_enabled = false
	env.glow_enabled = false
	var fog_on: bool = bool(s.get("fog_enabled"))
	env.fog_enabled = fog_on
	env.fog_light_color = Color(0.55, 0.62, 0.72)
	env.fog_mode = Environment.FOG_MODE_DEPTH
	var fog_dist: float = float(s.get("fog_distance")) if s.get("fog_distance") != null else 4000.0
	var fog_amt: float = clampf(float(s.get("fog_intensity")) if s.get("fog_intensity") != null else 0.35, 0.0, 1.0)
	env.fog_depth_begin = fog_dist
	env.fog_depth_end = fog_dist + lerpf(14000.0, 2800.0, fog_amt)
	env.fog_depth_curve = lerpf(1.0, 0.55, fog_amt)
	env.fog_aerial_perspective = 0.25 * fog_amt if fog_on else 0.0
	env.fog_density = 0.00008
	we.environment = env


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
	sun.shadow_opacity = 0.55
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS
	sun.light_angular_distance = 0.8


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
