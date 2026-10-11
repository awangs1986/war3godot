extends Camera3D
## Warsmash GameCameraManager orbit (MiscData AOA/FOV/Near/Far), Godot Y-up.
## Distance locked to calibrated blade-mode value (675 from /mark session).
## MiscData ladder was 1650…1100 — too far for single-hero follow.


## MiscData Camera preset 0 angles; distance locked (not MiscData 1650).
const AOA := 304.0
const FOV_DEG := 70.0
const ROTATION_DEG := 90.0
const DISTANCE := 675.0
const NEAR_Z := 40.0 ## Scaled for DISTANCE 675 (MiscData NearZ=100 @ 1650)
const FAR_Z := 5000.0
const HEIGHT_BIAS := 0.0

var follow_target: Node3D = null
var target_pos: Vector3 = Vector3.ZERO
var follow_lerp: float = 8.0


func _ready() -> void:
	fov = FOV_DEG
	near = NEAR_Z
	far = FAR_Z
	_apply_orbit(target_pos)


func set_follow(node: Node3D) -> void:
	follow_target = node
	if node != null and is_instance_valid(node):
		target_pos = node.global_position
		target_pos.y += HEIGHT_BIAS
		_apply_orbit(target_pos)


func clear_follow() -> void:
	follow_target = null


func snap_to(world: Vector3) -> void:
	target_pos = world
	target_pos.y += HEIGHT_BIAS
	_apply_orbit(target_pos)


func get_distance() -> float:
	return DISTANCE


func _process(delta: float) -> void:
	if follow_target != null and is_instance_valid(follow_target):
		var want: Vector3 = follow_target.global_position
		want.y += HEIGHT_BIAS
		target_pos = target_pos.lerp(want, clampf(follow_lerp * delta, 0.0, 1.0))
		_apply_orbit(target_pos)


func _apply_orbit(target: Vector3) -> void:
	# Warsmash: horizontalAngle = rad(rotation - 90); verticalAngle = rad(aoa - 270)
	var yaw := deg_to_rad(ROTATION_DEG - 90.0)
	var pitch := deg_to_rad(AOA - 270.0) # ≈ 34°
	var offset := Vector3.UP
	offset = offset.rotated(Vector3.RIGHT, pitch)
	offset = offset.rotated(Vector3.UP, yaw)
	offset = offset.normalized() * DISTANCE
	projection = Camera3D.PROJECTION_PERSPECTIVE
	global_position = target + offset
	look_at(target, Vector3.UP)
	fov = FOV_DEG
	near = NEAR_Z
	far = FAR_Z
