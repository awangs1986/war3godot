extends RefCounted
## Applies UeStyleLitSettings only. Never used by War3 lit path.
## Sky backends: PhysicalSky (Nishita/Preetham) or Tokisan Sky3D (addons/sky_3d).


const UeStyleLitSettingsScript = preload("res://addons/w3kit/ue_style_lit_settings.gd")
const W3LitSettingsScript = preload("res://addons/w3kit/w3_lit_settings.gd")

const UE_SKY3D_NAME := "UeSky3D"


static func apply_to_scene(root: Node, settings = null) -> void:
	if root == null:
		return
	var s = settings
	if s == null or not (s is Resource) or s.get_script() == null:
		s = load("res://addons/w3kit/ue_style_lit_default.tres")
	if s == null:
		s = UeStyleLitSettingsScript.new()

	var backend: int = int(s.get("sky_backend")) if s.get("sky_backend") != null else 0
	if backend == UeStyleLitSettingsScript.SkyBackend.SKY3D and _sky3d_available():
		_apply_sky3d(root, s)
	else:
		if backend == UeStyleLitSettingsScript.SkyBackend.SKY3D:
			push_warning("UeStyleLit: Sky3D unavailable — falling back to PhysicalSky (Nishita)")
		_teardown_sky3d(root)
		_ensure_environment_physical(root, s)
		_ensure_key_light(root, s)
		_disable_fill_light(root)

	_sync_terrain_open_realm_light(root, s)
	if root.is_inside_tree() and backend == UeStyleLitSettingsScript.SkyBackend.SKY3D:
		var tree := root.get_tree()
		if tree:
			tree.create_timer(0.05).timeout.connect(
				func() -> void:
					if is_instance_valid(root):
						_sync_terrain_open_realm_light(root, s)
						preload("res://addons/w3kit/w3_war3_lit.gd").set_models_receive_lights(root, true)
						preload("res://addons/w3kit/w3_war3_lit.gd")._set_terrain_receive_lights(root, true)
			)
	preload("res://addons/w3kit/w3_war3_lit.gd").set_models_receive_lights(root, true)
	preload("res://addons/w3kit/w3_war3_lit.gd")._set_terrain_receive_lights(root, true)


## Called by War3 lit so switching presets does not leave Sky3D / UE nodes around.
static func teardown_ue_only_nodes(root: Node) -> void:
	if root == null:
		return
	_teardown_sky3d(root)
	var sun := root.get_node_or_null("DirectionalLight3D") as DirectionalLight3D
	if sun:
		sun.visible = true


static func _sky3d_available() -> bool:
	return ResourceLoader.exists("res://addons/sky_3d/src/Sky3D.gd")


## class_name chain + textures must resolve before Sky3D.new().
static func _load_sky3d_script():
	if not _sky3d_available():
		return null
	preload("res://addons/sky_3d/src/TOD_Math.gd")
	preload("res://addons/sky_3d/src/ScatterLib.gd")
	preload("res://addons/sky_3d/src/OrbitalElements.gd")
	preload("res://addons/sky_3d/src/SkyDome.gd")
	preload("res://addons/sky_3d/src/TimeOfDay.gd")
	return load("res://addons/sky_3d/src/Sky3D.gd")


static func _teardown_sky3d(root: Node) -> void:
	var n := root.get_node_or_null(UE_SKY3D_NAME)
	if n:
		root.remove_child(n)
		n.free()


static func _apply_sky3d(root: Node, s) -> void:
	# Hide sandbox key lights — Sky3D owns SunLight / MoonLight.
	var old_sun := root.get_node_or_null("DirectionalLight3D") as DirectionalLight3D
	if old_sun:
		old_sun.visible = false
		old_sun.light_energy = 0.0
		old_sun.shadow_enabled = false
	_disable_fill_light(root)

	# Detach plain WorldEnvironment so Sky3D (also a WorldEnvironment) is the active one.
	var we := root.get_node_or_null("WorldEnvironment") as WorldEnvironment
	if we != null and we.name != UE_SKY3D_NAME:
		we.environment = null

	var Sky3DScript = _load_sky3d_script()
	if Sky3DScript == null:
		push_warning("UeStyleLit: Sky3D script load failed")
		_ensure_environment_physical(root, s)
		_ensure_key_light(root, s)
		return

	var sky3d: Node = root.get_node_or_null(UE_SKY3D_NAME)
	if sky3d == null:
		sky3d = Sky3DScript.new()
		sky3d.name = UE_SKY3D_NAME
		root.add_child(sky3d)
		# NOTIFICATION_ENTER_TREE runs Sky3D._initialize (SunLight / SkyDome / TimeOfDay).

	sky3d.set("sky3d_enabled", true)
	sky3d.set("sky_enabled", true)
	sky3d.set("clouds_enabled", true)
	sky3d.set("lights_enabled", true)
	sky3d.set("fog_enabled", bool(s.get("fog_enabled")))
	sky3d.set("game_time_enabled", bool(s.get("sky3d_time_progress")))
	sky3d.set("editor_time_enabled", false)
	sky3d.set("current_time", float(s.get("sky3d_hour")))
	sky3d.set("sun_energy", float(s.get("sun_intensity")) * 1.15)
	sky3d.set("skydome_energy", float(s.get("sky_energy")))
	sky3d.set("cloud_intensity", 0.55)
	sky3d.set("sky_contribution", clampf(float(s.get("ambient_from_sky")) * 0.85, 0.55, 1.0))
	sky3d.set("ambient_energy", clampf(float(s.get("ambient_from_sky")), 0.8, 1.6))
	sky3d.set("tonemap_exposure", float(s.get("exposure")))
	sky3d.set("camera_exposure", float(s.get("exposure")))
	sky3d.set("sun_shadow_opacity", 0.9 if bool(s.get("sun_shadows")) else 0.0)

	var env: Environment = sky3d.get("environment") as Environment
	if env:
		env.tonemap_mode = Environment.TONE_MAPPER_ACES
		env.tonemap_white = 6.0
		env.glow_enabled = bool(s.get("glow_enabled"))
		env.glow_intensity = float(s.get("glow_intensity"))
		env.ssil_enabled = bool(s.get("ssil_enabled"))
		env.sdfgi_enabled = bool(s.get("sdfgi_enabled"))
		env.ssao_enabled = true
		env.ssao_intensity = 0.65
		env.ssao_radius = 1.0
		# Depth fog on top of Sky3D's own fog shader (optional continuity with UE tres).
		var fog_on: bool = bool(s.get("fog_enabled"))
		if fog_on:
			env.fog_enabled = true
			env.fog_mode = Environment.FOG_MODE_DEPTH
			env.fog_light_color = Color(0.62, 0.72, 0.88)
			var fog_dist: float = float(s.get("fog_distance")) if s.get("fog_distance") != null else 4200.0
			var fog_amt: float = clampf(float(s.get("fog_intensity")) if s.get("fog_intensity") != null else 0.22, 0.0, 1.0)
			env.fog_depth_begin = fog_dist
			env.fog_depth_end = fog_dist + lerpf(16000.0, 3200.0, fog_amt)
			env.fog_aerial_perspective = 0.45 * fog_amt
		else:
			env.fog_enabled = false

	# Soft shadows on Sky3D sun if present.
	var sun_light := sky3d.get_node_or_null("SunLight") as DirectionalLight3D
	if sun_light:
		sun_light.shadow_enabled = bool(s.get("sun_shadows"))
		sun_light.shadow_blur = 1.35
		sun_light.light_angular_distance = float(s.get("sun_angular_distance"))
		sun_light.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS
		sun_light.directional_shadow_max_distance = 6500.0


static func _sync_terrain_open_realm_light(root: Node, s) -> void:
	var world := root.get_node_or_null("W3MapWorld")
	if world == null:
		return
	var terrain = world.get("terrain")
	if terrain == null or not terrain.has_method("apply_lighting"):
		return
	var sun := _find_active_sun(root)
	var light_dir := Vector3(0.45, 0.85, 0.28)
	if sun != null:
		light_dir = -sun.global_transform.basis.z
	var ambient: float = clampf(0.48 + float(s.get("ambient_from_sky")) * 0.22, 0.45, 0.85)
	var col := Color.WHITE
	var scale := 1.4
	if sun != null:
		col = sun.light_color
		# Fold energy into color so unshaded ground tracks the scene sun.
		var e: float = clampf(sun.light_energy, 0.35, 2.5)
		col = Color(col.r * e, col.g * e, col.b * e)
		scale = 1.25
	terrain.apply_lighting(light_dir, ambient, scale, col)


static func _find_active_sun(root: Node) -> DirectionalLight3D:
	var sky3d := root.get_node_or_null(UE_SKY3D_NAME)
	if sky3d:
		var sl := sky3d.get_node_or_null("SunLight") as DirectionalLight3D
		if sl and sl.visible:
			return sl
	var sun := root.get_node_or_null("DirectionalLight3D") as DirectionalLight3D
	if sun and sun.visible:
		return sun
	return root.get_node_or_null("Sun") as DirectionalLight3D


static func _ensure_environment_physical(root: Node, s) -> void:
	var we := root.get_node_or_null("WorldEnvironment") as WorldEnvironment
	if we == null:
		we = WorldEnvironment.new()
		we.name = "WorldEnvironment"
		root.add_child(we)

	# PhysicalSky = Preetham analytic daylight (Godot docs); sun dir/color from first DirectionalLight3D.
	var sky_mat := PhysicalSkyMaterial.new()
	sky_mat.energy_multiplier = float(s.get("sky_energy"))
	sky_mat.turbidity = float(s.get("sky_turbidity"))
	sky_mat.rayleigh_coefficient = 2.2
	sky_mat.rayleigh_color = Color(0.28, 0.42, 0.72)
	sky_mat.mie_coefficient = 0.005
	sky_mat.mie_eccentricity = 0.85
	sky_mat.mie_color = Color(0.95, 0.92, 0.85)
	sky_mat.ground_color = Color(0.14, 0.13, 0.11)
	sky_mat.sun_disk_scale = 1.15
	sky_mat.use_debanding = true

	var sky := Sky.new()
	sky.sky_material = sky_mat
	sky.process_mode = Sky.PROCESS_MODE_REALTIME
	sky.radiance_size = Sky.RADIANCE_SIZE_256

	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_energy = float(s.get("ambient_from_sky"))
	env.ambient_light_sky_contribution = 1.0
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY

	env.tonemap_mode = Environment.TONE_MAPPER_ACES
	env.tonemap_exposure = float(s.get("exposure"))
	env.tonemap_white = 6.0

	env.glow_enabled = bool(s.get("glow_enabled"))
	env.glow_intensity = float(s.get("glow_intensity"))
	env.glow_bloom = 0.05
	env.glow_hdr_threshold = 1.0

	env.ssil_enabled = bool(s.get("ssil_enabled"))
	env.ssil_intensity = 0.85
	env.ssil_radius = 4.0

	env.sdfgi_enabled = bool(s.get("sdfgi_enabled"))
	if env.sdfgi_enabled:
		env.sdfgi_use_occlusion = true
		env.sdfgi_cascades = 4
		env.sdfgi_min_cell_size = 8.0
		env.sdfgi_energy = 0.9

	env.ssao_enabled = true
	env.ssao_radius = 1.0
	env.ssao_intensity = 0.65

	var fog_on: bool = bool(s.get("fog_enabled"))
	env.fog_enabled = fog_on
	env.fog_light_color = Color(0.62, 0.72, 0.88)
	env.fog_mode = Environment.FOG_MODE_DEPTH
	var fog_dist: float = float(s.get("fog_distance")) if s.get("fog_distance") != null else 4200.0
	var fog_amt: float = clampf(float(s.get("fog_intensity")) if s.get("fog_intensity") != null else 0.22, 0.0, 1.0)
	env.fog_depth_begin = fog_dist
	env.fog_depth_end = fog_dist + lerpf(16000.0, 3200.0, fog_amt)
	env.fog_depth_curve = lerpf(1.0, 0.5, fog_amt)
	env.fog_aerial_perspective = 0.55 * fog_amt if fog_on else 0.0
	env.fog_sun_scatter = 0.18 if fog_on else 0.0
	env.fog_density = 0.00005

	we.environment = env


static func _ensure_key_light(root: Node, s) -> void:
	var sun := root.get_node_or_null("DirectionalLight3D") as DirectionalLight3D
	if sun == null:
		sun = root.get_node_or_null("Sun") as DirectionalLight3D
	if sun == null:
		sun = DirectionalLight3D.new()
		sun.name = "DirectionalLight3D"
		root.add_child(sun)
	# PhysicalSky samples the first DirectionalLight3D — keep this node first among lights.
	root.move_child(sun, 0)
	sun.rotation_degrees = Vector3(float(s.get("sun_pitch_deg")), float(s.get("sun_yaw_deg")), 0.0)
	sun.light_color = W3LitSettingsScript.temperature_to_color(float(s.get("sun_temperature_k")))
	sun.light_energy = float(s.get("sun_intensity"))
	sun.light_indirect_energy = 1.15
	sun.light_specular = 0.7
	sun.shadow_enabled = bool(s.get("sun_shadows"))
	sun.shadow_blur = 1.4
	sun.shadow_opacity = 0.85
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS
	sun.directional_shadow_max_distance = 6500.0
	sun.light_angular_distance = float(s.get("sun_angular_distance"))
	sun.visible = true


static func _disable_fill_light(root: Node) -> void:
	var fill := root.get_node_or_null("FillLight3D") as DirectionalLight3D
	if fill != null:
		fill.visible = false
		fill.light_energy = 0.0
