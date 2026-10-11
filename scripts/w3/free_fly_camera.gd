extends Camera3D
## Free-fly preview camera: WASD move, QE up/down, RMB look, wheel zoom.


@export var move_speed: float = 600.0
@export var look_sensitivity: float = 0.0025
@export var min_pitch: float = -1.4
@export var max_pitch: float = 1.4

var _yaw: float = 0.0
var _pitch: float = -0.55
var _looking: bool = false
## When true, freeze WASD / look (sandbox console open).
var input_blocked: bool = false


func _ready() -> void:
	projection = PROJECTION_PERSPECTIVE
	fov = 55.0
	near = 1.0
	far = 20000.0
	current = true
	_apply_rot()


func focus_point(center: Vector3, extent: float = 2000.0) -> void:
	global_position = center + Vector3(extent * 0.45, extent * 0.55, extent * 0.45)
	look_at(center, Vector3.UP)
	var e := global_transform.basis.get_euler()
	_yaw = e.y
	_pitch = e.x
	_apply_rot()
	projection = PROJECTION_PERSPECTIVE


## Top-down ortho over a tile: screen-up = map +Y (north), screen-right = map +X (east).
## Manual basis — look_at(center, +Z) mirrors X (east↔west), so TL would land top-right.
func focus_tile_topdown(center: Vector3, ortho_size: float = 380.0) -> void:
	projection = PROJECTION_ORTHOGONAL
	size = ortho_size
	near = 1.0
	far = 20000.0
	global_position = center + Vector3(0.0, 2500.0, 0.0)
	# Camera axes: +X east, +Y north (+Z world), -Z look down (−Y world).
	global_transform.basis = Basis(Vector3(1, 0, 0), Vector3(0, 0, 1), Vector3(0, 1, 0))
	var e := global_transform.basis.get_euler()
	_yaw = e.y
	_pitch = e.x


func release_look() -> void:
	_looking = false
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE


func _unhandled_input(event: InputEvent) -> void:
	if input_blocked:
		return
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_RIGHT:
			_looking = event.pressed
			if _looking:
				Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
			else:
				Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		elif event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_UP:
			_zoom(-1.0)
		elif event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_zoom(1.0)
	elif event is InputEventMouseMotion and _looking:
		_ensure_perspective()
		_yaw -= event.relative.x * look_sensitivity
		_pitch -= event.relative.y * look_sensitivity
		_pitch = clampf(_pitch, min_pitch, max_pitch)
		_apply_rot()


## Wheel zoom: ortho → size; perspective → dolly along view axis.
func _zoom(sign: float) -> void:
	if projection == PROJECTION_ORTHOGONAL:
		size = clampf(size * (1.12 if sign > 0.0 else 1.0 / 1.12), 80.0, 4000.0)
	else:
		var dir: Vector3 = -global_transform.basis.z
		global_position += dir * sign * move_speed * 0.08


func _process(delta: float) -> void:
	if not current or input_blocked:
		return
	var wish := Vector3.ZERO
	var basis_yaw := Basis(Vector3.UP, _yaw)
	var forward := -basis_yaw.z
	var right := basis_yaw.x
	if Input.is_key_pressed(KEY_W):
		wish += forward
	if Input.is_key_pressed(KEY_S):
		wish -= forward
	if Input.is_key_pressed(KEY_D):
		wish += right
	if Input.is_key_pressed(KEY_A):
		wish -= right
	if Input.is_key_pressed(KEY_E) or Input.is_key_pressed(KEY_SPACE):
		wish += Vector3.UP
	if Input.is_key_pressed(KEY_Q) or Input.is_key_pressed(KEY_CTRL):
		wish -= Vector3.UP
	if wish.length() > 0.001:
		_ensure_perspective()
		global_position += wish.normalized() * move_speed * delta


func _ensure_perspective() -> void:
	if projection == PROJECTION_ORTHOGONAL:
		projection = PROJECTION_PERSPECTIVE
		fov = 55.0
		_apply_rot()


func _apply_rot() -> void:
	rotation = Vector3(_pitch, _yaw, 0.0)
