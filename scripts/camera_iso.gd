extends Node3D
## Diablo-like orthogonal isometric follow camera.
## Pivot follows the player; child Camera3D keeps a fixed local iso transform.

@export var follow_target: Node3D
@export var follow_speed: float = 12.0
@export var ortho_size: float = 22.0
## Distance from pivot along the iso view vector.
@export var distance: float = 32.0

@onready var _camera: Camera3D = $Camera3D


func _ready() -> void:
	_configure_camera()
	if follow_target != null and is_instance_valid(follow_target):
		global_position = follow_target.global_position


func snap_to_target() -> void:
	if follow_target != null and is_instance_valid(follow_target):
		global_position = follow_target.global_position
	_configure_camera()


func _configure_camera() -> void:
	# Classic D1 / WC3 style: yaw 45°, pitch ~35° (atan(1/sqrt(2))).
	var yaw := deg_to_rad(45.0)
	var pitch := deg_to_rad(-35.264)
	var offset := Vector3(
		distance * cos(pitch) * sin(yaw),
		distance * -sin(pitch),
		distance * cos(pitch) * cos(yaw)
	)
	_camera.position = offset
	# Aim at pivot (local origin). Set once — do not look_at every frame.
	_camera.basis = Basis.looking_at(-offset, Vector3.UP)
	_camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	_camera.size = ortho_size
	_camera.near = 0.05
	_camera.far = 1000.0
	_camera.make_current()


func _process(delta: float) -> void:
	if follow_target != null and is_instance_valid(follow_target):
		global_position = global_position.lerp(
			follow_target.global_position,
			clampf(follow_speed * delta, 0.0, 1.0)
		)
	if _camera and not _camera.is_current():
		_camera.make_current()


func get_camera() -> Camera3D:
	return _camera


## World-space axes matching what "screen up / screen right" mean for this iso cam.
func get_screen_forward() -> Vector3:
	# Away from camera on XZ (screen-up).
	return Vector3(-1.0, 0.0, -1.0).normalized()


func get_screen_right() -> Vector3:
	return Vector3(1.0, 0.0, -1.0).normalized()
