extends CharacterBody3D
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
## Diablo-style click-to-move on War3 grid terrain with height sticking.


const SPEED := 220.0
const ARRIVE := 16.0

var world
var _path: Array[Vector2i] = []
var _path_i: int = 0
var _mdx
var _mesh: MeshInstance3D


func _ready() -> void:
	add_to_group("player")
	collision_layer = 2
	collision_mask = 1
	floor_snap_length = 8.0
	_ensure_visual()


func bind_world(w) -> void:
	world = w
	if world and world.sampler:
		var t: Vector2i = world.sampler.world_to_tile(global_position.x, global_position.z)
		if not world.sampler.is_tile_walkable(t):
			t = Vector2i(world.map.tile_count_x() / 2, world.map.tile_count_y() / 2)
		global_position = world.sampler.tile_center_world(t)


func attach_mdx(model_path: String) -> void:
	if _mesh:
		_mesh.visible = false
	if _mdx:
		_mdx.queue_free()
	_mdx = W3MdxInstance.new()
	add_child(_mdx)
	var model = null
	var abs_path: String = model_path
	if not FileAccess.file_exists(abs_path):
		abs_path = W3Paths.resolve_path(model_path)
	if not abs_path.is_empty() and FileAccess.file_exists(abs_path):
		model = W3MdxModel.load_file(abs_path)
	var tex = W3TextureProvider.new()
	if world and world.archive:
		tex.set_map_dir(world.archive.map_dir)
	_mdx.setup(model, tex)
	_mdx.scale = Vector3.ONE


func _ensure_visual() -> void:
	if has_node("Mesh"):
		_mesh = $Mesh
		return
	_mesh = MeshInstance3D.new()
	_mesh.name = "Mesh"
	var box := BoxMesh.new()
	box.size = Vector3(28, 56, 28)
	_mesh.mesh = box
	_mesh.position.y = 28
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(0.25, 0.4, 0.9)
	_mesh.material_override = mat
	add_child(_mesh)
	var col := CollisionShape3D.new()
	var shape := CapsuleShape3D.new()
	shape.radius = 14.0
	shape.height = 48.0
	col.shape = shape
	col.position.y = 28
	add_child(col)


func _input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_click_move()


func _click_move() -> void:
	if world == null or world.sampler == null:
		return
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return
	var mouse := get_viewport().get_mouse_position()
	var from := cam.project_ray_origin(mouse)
	var dir := cam.project_ray_normal(mouse)
	var hit = _ray_terrain(from, dir)
	if hit == null:
		return
	var goal_tile: Vector2i = world.sampler.world_to_tile(hit.x, hit.z)
	var start_tile: Vector2i = world.sampler.world_to_tile(global_position.x, global_position.z)
	_path = world.pathfinder.find_path(start_tile, goal_tile)
	_path_i = 0


func _ray_terrain(from: Vector3, dir: Vector3) -> Variant:
	if absf(dir.y) < 0.0001:
		return null
	for i in 80:
		var t: float = 20.0 + float(i) * 40.0
		var p: Vector3 = from + dir * t
		var ground_y: float = float(world.sampler.sample_y(p.x, p.z))
		if p.y <= ground_y + 4.0:
			return Vector3(p.x, ground_y, p.z)
	var y0: float = float(world.sampler.sample_y(global_position.x, global_position.z))
	var t2: float = (y0 - from.y) / dir.y
	if t2 < 0.0:
		return null
	var p2: Vector3 = from + dir * t2
	p2.y = float(world.sampler.sample_y(p2.x, p2.z))
	return p2


func _physics_process(delta: float) -> void:
	var moving := false
	if _path_i < _path.size() and world and world.sampler:
		var target: Vector3 = world.sampler.tile_center_world(_path[_path_i])
		var to: Vector3 = target - global_position
		to.y = 0.0
		if to.length() <= ARRIVE:
			_path_i += 1
		else:
			var step: Vector3 = to.normalized() * SPEED * delta
			if step.length() > to.length():
				step = to
			global_position.x += step.x
			global_position.z += step.z
			_face(to)
			moving = true
	if world and world.sampler:
		global_position.y = float(world.sampler.sample_y(global_position.x, global_position.z))
	if _mdx:
		_mdx.set_moving(moving)
	velocity = Vector3.ZERO


func _face(dir: Vector3) -> void:
	var flat := Vector3(dir.x, 0.0, dir.z)
	if flat.length() < 0.001:
		return
	flat = flat.normalized()
	rotation.y = atan2(flat.x, flat.z)
