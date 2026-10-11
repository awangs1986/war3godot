class_name W3Coords
extends RefCounted
## Single source of truth for W3 <-> Godot coordinate conventions.
##
## Warcraft III world space:      X east, Y north, Z up (right-handed).
## Godot world space:             X east, Y up,    Z south (right-handed).
## Mapping used across w3kit:     godot.x = w3.x, godot.y = w3.z, godot.z = w3.y
##
## Facing: W3 stores counter-clockwise degrees where 0 points at +X and 90 at
## +Y (north). After the axis mapping, +Y(north) becomes Godot -Z... except the
## project mirrors W3 Y onto Godot +Z (no sign flip), so a W3 rotation of `a`
## becomes a Godot yaw of `-a` (see W3DooParser placements: Basis(UP, -angle)).


## W3 (x, y, z) -> Godot (x, z, y).
static func w3_to_godot(w3: Vector3) -> Vector3:
	return Vector3(w3.x, w3.z, w3.y)


## Godot (x, y, z) -> W3 (x, z, y).
static func godot_to_w3(g: Vector3) -> Vector3:
	return Vector3(g.x, g.z, g.y)


## W3 ground-plane point -> Godot XZ plane at the given height.
static func ground_to_godot(w3_x: float, w3_y: float, godot_height: float = 0.0) -> Vector3:
	return Vector3(w3_x, godot_height, w3_y)


## Godot world position -> W3 ground-plane point (drops height).
static func godot_to_ground(g: Vector3) -> Vector2:
	return Vector2(g.x, g.z)


## W3 facing in degrees -> Godot Y-axis rotation in radians.
static func facing_deg_to_godot_yaw(facing_deg: float) -> float:
	return -deg_to_rad(facing_deg)


## W3 facing in radians -> Godot Y-axis rotation in radians.
static func facing_rad_to_godot_yaw(facing_rad: float) -> float:
	return -facing_rad


## Direction on the W3 ground plane for a facing in degrees.
static func facing_deg_to_dir(facing_deg: float) -> Vector2:
	var r := deg_to_rad(facing_deg)
	return Vector2(cos(r), sin(r))
