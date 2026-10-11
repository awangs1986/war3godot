extends Node3D
## Presentation binding: one animated MDX instance per live W3SimUnit.
## Reads sim state only (position/facing/anim intent) and interpolates between
## simulation ticks; it never mutates the simulation.

const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3Coords = preload("res://addons/w3kit/w3_coords.gd")

const ANIM_IDLE := 0
const ANIM_WALK := 1
const ANIM_ATTACK := 2
const ANIM_DEATH := 3

# Same tiers as W3MdxBatcher: skin nearby idlers only, but keep acting units
# animated out to the draw distance so they never glide in a frozen pose.
const ANIM_NEAR_DIST := 950.0
const DRAW_DIST := 4200.0
const PROXIMITY_CHECK_SEC := 0.25

var _world # w3_map_world (height sampling, textures, catalog)
var _sim # W3Sim
var _views: Dictionary = {} # unit_id -> {unit, node, mdx, anim}
var _model_cache: Dictionary = {} # abs path -> W3MdxModel
var _proximity_timer := 0.0


func setup(world, sim) -> void:
	_world = world
	_sim = sim
	sim.unit_spawned.connect(_on_unit_spawned)
	sim.unit_removed.connect(_on_unit_removed)
	sim.unit_died.connect(_on_unit_died)
	for unit in sim.get_units():
		_on_unit_spawned(unit)


func view_node(unit) -> Node3D:
	var view: Dictionary = _views.get(unit.unit_id, {})
	return view.get("node")


func _on_unit_spawned(unit) -> void:
	var node := Node3D.new()
	node.name = "Unit_%d_%s" % [unit.unit_id, unit.rawcode]
	add_child(node)
	var view := {"unit": unit, "node": node, "mdx": null, "anim": ANIM_IDLE}
	view["mdx"] = _build_mdx(unit, node)
	unit.attack_swing.connect(_on_attack_swing)
	_views[unit.unit_id] = view
	_apply_transform(view, 1.0)


func _on_attack_swing(unit, _target) -> void:
	# Re-sync the attack loop to the actual swing tick.
	var view: Dictionary = _views.get(unit.unit_id, {})
	if view.is_empty() or view["mdx"] == null:
		return
	view["anim"] = ANIM_ATTACK
	view["mdx"].play_named("Attack", true)


func _on_unit_removed(unit) -> void:
	var view: Dictionary = _views.get(unit.unit_id, {})
	if view.is_empty():
		return
	_views.erase(unit.unit_id)
	var node: Node3D = view["node"]
	if is_instance_valid(node):
		node.queue_free()


func _on_unit_died(unit, _killer) -> void:
	var view: Dictionary = _views.get(unit.unit_id, {})
	if view.is_empty():
		return
	view["anim"] = ANIM_DEATH
	var mdx = view["mdx"]
	if mdx != null and not mdx.play_named_once("Death"):
		# Model without a Death sequence: hide the corpse immediately.
		var node: Node3D = view["node"]
		if is_instance_valid(node):
			node.visible = false


func _build_mdx(unit, parent: Node3D) -> Node3D:
	if _world == null or _world.catalog == null or _world.textures == null:
		push_error("W3UnitRenderer: world catalog/textures unavailable for %s" % unit.rawcode)
		return null
	var model_rel: String = _world.catalog.resolve_model(unit.rawcode, true)
	if model_rel.is_empty():
		push_error("W3UnitRenderer: no model for rawcode %s" % unit.rawcode)
		return null
	var abs_path := W3Paths.resolve_reforged_path(model_rel)
	if abs_path.is_empty():
		abs_path = W3Paths.resolve_path(model_rel)
	if abs_path.is_empty():
		push_error("W3UnitRenderer: cannot resolve model file %s (%s)" % [model_rel, unit.rawcode])
		return null
	var model = _model_cache.get(abs_path)
	if model == null:
		model = W3MdxModel.load_file(abs_path)
		if model == null or model.geosets.is_empty():
			push_error("W3UnitRenderer: MDX load failed: %s" % abs_path)
			return null
		_model_cache[abs_path] = model
	var mdx := W3MdxInstance.new()
	mdx.name = "Mdx"
	parent.add_child(mdx)
	mdx.setup(model, _world.textures, abs_path, {"animate": true})
	mdx.scale = Vector3.ONE * float(_world.catalog.model_scale(unit.rawcode, true))
	return mdx


func _process(delta: float) -> void:
	if _sim == null:
		return
	var alpha: float = _sim.interpolation_alpha()
	for view in _views.values():
		_apply_transform(view, alpha)
		_apply_animation(view)
	_proximity_timer += delta
	if _proximity_timer >= PROXIMITY_CHECK_SEC:
		_proximity_timer = 0.0
		_update_anim_proximity()


func _update_anim_proximity() -> void:
	var cam := get_viewport().get_camera_3d() if is_inside_tree() else null
	if cam == null:
		return
	var cam_pos := cam.global_position
	for view in _views.values():
		var mdx = view["mdx"]
		var node: Node3D = view["node"]
		if mdx == null or not is_instance_valid(node):
			continue
		var unit = view["unit"]
		var d := cam_pos.distance_to(node.global_position)
		node.visible = d <= DRAW_DIST
		var acting: bool = unit.is_moving or unit.is_attacking
		mdx.set_anim_active(d <= ANIM_NEAR_DIST or (acting and d <= DRAW_DIST))


func _apply_transform(view: Dictionary, alpha: float) -> void:
	var unit = view["unit"]
	var node: Node3D = view["node"]
	if not is_instance_valid(node):
		return
	var p: Vector2 = unit.prev_pos.lerp(unit.pos, alpha)
	var height := 0.0
	if _world != null and _world.sampler != null:
		height = float(_world.sampler.sample_y(p.x, p.y))
	node.position = W3Coords.ground_to_godot(p.x, p.y, height)
	var facing := lerp_angle(deg_to_rad(unit.prev_facing), deg_to_rad(unit.facing), alpha)
	node.rotation = Vector3(0.0, W3Coords.facing_rad_to_godot_yaw(facing), 0.0)


func _apply_animation(view: Dictionary) -> void:
	var unit = view["unit"]
	var mdx = view["mdx"]
	if mdx == null or unit.is_dead():
		return # death one-shot is handled by _on_unit_died
	var wanted := ANIM_IDLE
	if unit.is_moving:
		wanted = ANIM_WALK
	elif unit.is_attacking:
		wanted = ANIM_ATTACK
	if int(view["anim"]) == wanted:
		return
	view["anim"] = wanted
	match wanted:
		ANIM_WALK:
			mdx.set_moving(true)
		ANIM_ATTACK:
			mdx.play_named("Attack")
		_:
			mdx.set_moving(false)
