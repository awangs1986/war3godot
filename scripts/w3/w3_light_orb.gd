extends Node3D
## Mouse-follow emissive orb + OmniLight3D for testing scene light reception.
## Terrain (custom unshaded) gets probe via W3GpuTerrain.set_probe_light.


@export var probe_enabled: bool = false
@export var light_energy: float = 14.0
@export var light_range: float = 950.0
@export var orb_radius: float = 32.0
@export var hover_height: float = 56.0
@export var light_color: Color = Color(1.0, 0.9, 0.7)

var _cam: Camera3D
var _sampler
var _world: Node
var _mesh: MeshInstance3D
var _light: OmniLight3D


func setup(cam: Camera3D, world: Node) -> void:
	_cam = cam
	_world = world
	_sampler = world.get("sampler") if world else null
	if _mesh == null:
		_build()
	set_probe_enabled(probe_enabled)


func refresh_world(world: Node) -> void:
	_world = world
	_sampler = world.get("sampler") if world else null


func _build() -> void:
	var sphere := SphereMesh.new()
	sphere.radius = orb_radius
	sphere.height = orb_radius * 2.0
	sphere.radial_segments = 24
	sphere.rings = 12
	_mesh = MeshInstance3D.new()
	_mesh.mesh = sphere
	_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = Color(1.0, 0.95, 0.8, 0.85)
	mat.emission_enabled = true
	mat.emission = light_color
	mat.emission_energy_multiplier = 4.5
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	_mesh.material_override = mat
	add_child(_mesh)

	_light = OmniLight3D.new()
	_light.light_color = light_color
	_light.light_energy = light_energy
	_light.omni_range = light_range
	_light.omni_attenuation = 1.2
	_light.shadow_enabled = false
	_light.light_specular = 0.9
	_light.light_indirect_energy = 1.0
	add_child(_light)


func set_probe_enabled(on: bool) -> void:
	probe_enabled = on
	visible = on
	set_process(on)
	if _light:
		_light.visible = on
		_light.light_energy = light_energy if on else 0.0
	if on:
		Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
	else:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		_clear_terrain_probe()


func toggle() -> void:
	set_probe_enabled(not probe_enabled)


func _clear_terrain_probe() -> void:
	if _world and _world.get("terrain") != null and _world.terrain.has_method("set_probe_light"):
		_world.terrain.set_probe_light(false)


func _process(_delta: float) -> void:
	if not probe_enabled or _cam == null:
		return
	if _sampler == null and _world != null:
		_sampler = _world.get("sampler")
	if Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		# RMB look — keep orb in front of camera instead of chasing locked cursor.
		global_position = _cam.global_position - _cam.global_transform.basis.z * 420.0
		global_position.y = _snap_ground_y(global_position.x, global_position.z) + hover_height
	else:
		var hit: Variant = _ray_ground(get_viewport().get_mouse_position())
		if hit is Vector3:
			global_position = (hit as Vector3) + Vector3(0.0, hover_height, 0.0)
	if _world and _world.get("terrain") != null and _world.terrain.has_method("set_probe_light"):
		_world.terrain.set_probe_light(true, global_position, light_energy, light_range, light_color)


func _snap_ground_y(wx: float, wz: float) -> float:
	if _sampler != null and _sampler.has_method("sample_y"):
		return float(_sampler.sample_y(wx, wz))
	return 0.0


## Returns Vector3 on hit, or null.
func _ray_ground(screen_pos: Vector2) -> Variant:
	if _cam == null:
		return null
	var origin: Vector3 = _cam.project_ray_origin(screen_pos)
	var dir: Vector3 = _cam.project_ray_normal(screen_pos)
	if dir.length_squared() < 0.0001:
		return null
	dir = dir.normalized()
	# Coarse march — keep step large so we do not hitch every frame on big maps.
	var t := 80.0
	var t_max := 12000.0
	var step := 128.0
	var prev_above := true
	while t < t_max:
		var p: Vector3 = origin + dir * t
		var gy: float = _snap_ground_y(p.x, p.z)
		var above: bool = p.y > gy
		if prev_above and not above:
			var lo := t - step
			var hi := t
			for _i in 6:
				var mid := (lo + hi) * 0.5
				var pm: Vector3 = origin + dir * mid
				if pm.y > _snap_ground_y(pm.x, pm.z):
					lo = mid
				else:
					hi = mid
			var grounded: Vector3 = origin + dir * hi
			grounded.y = _snap_ground_y(grounded.x, grounded.z)
			return grounded
		prev_above = above
		t += step
	var far: Vector3 = origin + dir * 2500.0
	far.y = _snap_ground_y(far.x, far.z)
	return far
