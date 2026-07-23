extends Node3D
## Native Godot-style W3 unit selection: physics ray -> enemy collider -> UI.
## Terrain, cliffs and hidden MultiMesh props deliberately have no target body.

var _world: Node3D
var _sampler
var _selection_root: Node3D
var _selected: StaticBody3D
var _last_summary := "LMB=select visible unit"


func setup(world: Node3D) -> void:
	_world = world
	_sampler = world.sampler if world != null else null
	_selection_root = Node3D.new()
	_selection_root.name = "UnitSelection"
	add_child(_selection_root)


func pick(camera: Camera3D, screen_position: Vector2) -> Dictionary:
	if camera == null or get_world_3d() == null:
		return {}
	var from := camera.project_ray_origin(screen_position)
	var direction := camera.project_ray_normal(screen_position)
	var query := PhysicsRayQueryParameters3D.create(from, from + direction * 30000.0)
	query.collide_with_areas = false
	query.collide_with_bodies = true
	# This is the same native Enemy collision layer used by LevelRoot.
	query.collision_mask = 1 | 4 | 16
	var hit := get_world_3d().direct_space_state.intersect_ray(query)
	var target := _find_enemy_target(hit.get("collider", null))
	if target == null:
		clear_selection()
		_last_summary = "no visible unit selected"
		return {}
	_select(target)
	return {"target": target, "position": hit.get("position", target.global_position)}


func clear_selection() -> void:
	_selected = null
	if _selection_root == null:
		return
	for child in _selection_root.get_children():
		child.queue_free()


func get_last_summary() -> String:
	return _last_summary


func _find_enemy_target(collider: Variant) -> StaticBody3D:
	var current := collider as Node
	while current != null:
		if current.is_in_group("enemy") and current.is_in_group("w3_selectable_unit") and current is StaticBody3D:
			return current as StaticBody3D
		current = current.get_parent()
	return null


func _select(target: StaticBody3D) -> void:
	clear_selection()
	_selected = target
	var item: Dictionary = target.get_meta("w3_preview_item", {})
	var radius := float(target.get_meta("w3_selection_radius", 80.0))
	var scale := target.global_transform.basis.get_scale()
	radius *= maxf(absf(scale.x), absf(scale.z))
	radius = clampf(radius, 28.0, 360.0)
	var ring := MeshInstance3D.new()
	ring.name = "SelectedUnitRing"
	var torus := TorusMesh.new()
	torus.inner_radius = radius * 0.76
	torus.outer_radius = radius
	torus.rings = 12
	torus.ring_segments = 48
	ring.mesh = torus
	ring.material_override = _selection_material()
	ring.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_selection_root.add_child(ring)
	var p := target.global_position
	if _sampler != null:
		p.y = _sampler.sample_y(p.x, p.z) + 4.0
	ring.global_position = p
	var animation := ""
	var host := target.get_parent()
	if host != null and host.has_method("get_current_sequence_name"):
		animation = str(host.get_current_sequence_name())
	_last_summary = "selected %s%s" % [
		str(item.get("rawcode", "unit")),
		(" | %s" % animation) if not animation.is_empty() else "",
	]


func _selection_material() -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.albedo_color = Color(1.0, 0.78, 0.08, 0.95)
	material.emission_enabled = true
	material.emission = Color(1.0, 0.5, 0.02)
	material.no_depth_test = false
	return material
