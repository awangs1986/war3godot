extends Node3D
## DEBUG Blade Master: /debug → M → ghost MDX follows mouse → click place → Warsmash follow.


const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3WarsmashCameraScript = preload("res://scripts/w3/w3_warsmash_camera.gd")

const BLADE_MDX := "Units/Orc/HeroBladeMaster/HeroBladeMaster.mdx"
## UnitBalance Obla: spd≈320, collision≈32
const MOVE_SPEED := 320.0
const ARRIVE := 20.0
const COLLISION_SIZE := 32.0
const CURSOR_COLOR := Color(0.25, 1.0, 0.45, 0.95)
const PATH_COLOR := Color(0.2, 0.45, 1.0, 0.92)


var _debug_on: bool = false
var _control_mode: bool = false
var _world: Node = null
var _fallback_cam: Camera3D = null

var _pawn: Node3D = null
var _mdx: Node3D = null
var _path: Array[Vector3] = []
var _path_i: int = 0

var _ws_cam: Camera3D = null
var _dest_cursor: Node3D
var _dest_ring: MeshInstance3D
var _dest_beam: MeshInstance3D
var _path_mi: MeshInstance3D
var _cursor_ttl: float = 0.0

## Place-phase: Blade Master model follows mouse (becomes the pawn on click).
var _ghost_root: Node3D = null
var _ghost_mdx: Node3D = null
var _ghost_ready: bool = false
var _ghost_hit: Variant = null ## last valid ground hit, or null


func setup(world: Node, camera: Camera3D) -> void:
	bind(world, camera)
	_ensure_cursor_visuals()
	_ensure_ws_camera()
	set_process(false)
	set_process_input(false)
	set_process_unhandled_input(false)
	visible = false


## Refresh world/cam without resetting debug/process state (called on every M).
func bind(world: Node, camera: Camera3D) -> void:
	_world = world
	_fallback_cam = camera


## Only meaningful while sandbox /debug is on.
func set_debug_enabled(on: bool) -> void:
	_debug_on = on
	if not on:
		_exit_control_mode(false)
		visible = false
		set_process(false)
		set_process_input(false)
		set_process_unhandled_input(false)
	else:
		# Debug on ≠ blade mode. Wait for M.
		_control_mode = false
		if is_in_group("w3_debug_pawn_control"):
			remove_from_group("w3_debug_pawn_control")
		_hide_cursors()
		visible = true
		set_process(true)
		set_process_input(true)
		set_process_unhandled_input(true)
		print("[DebugSpawnPawn] /debug 已开 — 再按 M：剑圣模型跟鼠标 → 左键放置")


func is_control_mode() -> bool:
	return _control_mode and _debug_on


## True only after place: Warsmash cam owns the view.
func is_warsmash_active() -> bool:
	return (
		_debug_on
		and _control_mode
		and _pawn != null
		and _ws_cam != null
		and _ws_cam.current
	)


func toggle_control_mode() -> void:
	if not _debug_on:
		print("[DebugSpawnPawn] 忽略 M — 需要先 /debug")
		return
	if _control_mode:
		_exit_control_mode(true)
	else:
		_enter_control_mode()


func _enter_control_mode() -> void:
	_control_mode = true
	add_to_group("w3_debug_pawn_control")
	set_process(true)
	set_process_input(true)
	set_process_unhandled_input(true)
	# M: keep current camera. Ghost MDX follows mouse until click.
	if _pawn != null:
		_hide_place_ghost()
		_activate_ws_camera()
		print("[DebugSpawnPawn] 剑圣控制 ON — Warsmash 相机追踪，左键寻路移动")
	else:
		_ensure_place_ghost_model()
		print("[DebugSpawnPawn] 放置模式 — 原相机不变，剑圣模型跟鼠标；左键吸附地面放置")
	var p := get_parent()
	if p != null and p.has_method("_sync_cameras_after_blade_mode"):
		p._sync_cameras_after_blade_mode()


func _exit_control_mode(restore_cam: bool) -> void:
	_control_mode = false
	if is_in_group("w3_debug_pawn_control"):
		remove_from_group("w3_debug_pawn_control")
	_hide_place_ghost()
	_clear_path_draw()
	if restore_cam:
		_restore_fallback_camera()
		var p := get_parent()
		if p != null and p.has_method("_sync_cameras_after_blade_mode"):
			p._sync_cameras_after_blade_mode()
	print("[DebugSpawnPawn] 剑圣模式 OFF")


func _input(event: InputEvent) -> void:
	# Use _input (not only unhandled) so unit-pick / UI cannot steal place/move clicks.
	if not _debug_on or not _control_mode:
		return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var hit = _ghost_hit if _pawn == null and _ghost_hit != null else _ray_terrain(event.position)
		if hit == null:
			print("[DebugSpawnPawn] 点击未命中地形")
			return
		if _pawn == null:
			_confirm_place(hit)
		else:
			_order_move(hit)
		get_viewport().set_input_as_handled()


func _unhandled_input(event: InputEvent) -> void:
	if not _debug_on or not _control_mode:
		return
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_ESCAPE:
			_exit_control_mode(true)
			get_viewport().set_input_as_handled()


func _process(delta: float) -> void:
	if not _debug_on:
		return
	if _control_mode and _pawn == null:
		_update_place_ghost()
	else:
		if _ghost_root != null and _ghost_root != _pawn:
			_ghost_root.visible = false
	_tick_move(delta)
	if _cursor_ttl > 0.0:
		_cursor_ttl = maxf(0.0, _cursor_ttl - delta)
		if _dest_cursor:
			var a: float = 1.0 if _path_i < _path.size() else clampf(_cursor_ttl / 0.35, 0.0, 1.0)
			_dest_cursor.visible = a > 0.02
			if _dest_ring and _dest_ring.material_override:
				var c: Color = CURSOR_COLOR
				c.a = CURSOR_COLOR.a * a
				_dest_ring.material_override.albedo_color = c


func _ensure_place_ghost_model() -> void:
	if _ghost_ready and _ghost_root != null and is_instance_valid(_ghost_root):
		return
	_ghost_root = Node3D.new()
	_ghost_root.name = "BladePlaceGhost"
	add_child(_ghost_root)
	_ghost_mdx = W3MdxInstance.new()
	_ghost_root.add_child(_ghost_mdx)
	var abs_path: String = W3Paths.resolve_path(BLADE_MDX)
	var model = null
	if not abs_path.is_empty() and FileAccess.file_exists(abs_path):
		model = W3MdxModel.load_file(abs_path)
	else:
		push_warning("[DebugSpawnPawn] Blade MDX missing: %s" % BLADE_MDX)
	var tex = W3TextureProvider.new()
	if _world != null and _world.get("archive") != null and _world.archive:
		tex.set_map_dir(_world.archive.map_dir)
	_ghost_mdx.setup(model, tex, abs_path, {"animate": true, "team_color": 1})
	if _ghost_mdx.has_method("set_anim_active"):
		_ghost_mdx.set_anim_active(true)
	if _ghost_mdx.has_method("play_idle"):
		_ghost_mdx.play_idle()
	_ghost_ready = true
	_ghost_root.visible = false
	print("[DebugSpawnPawn] 放置幽灵已加载: ", abs_path)


## Promote ghost → controlled pawn at snapped ground pos; then Warsmash cam.
func _confirm_place(pos: Vector3) -> void:
	if _world == null:
		return
	_ensure_place_ghost_model()
	if _world.sampler != null:
		pos.y = float(_world.sampler.sample_y(pos.x, pos.z))
	_ghost_root.global_position = pos
	_pawn = _ghost_root
	_mdx = _ghost_mdx
	_ghost_root = null
	_ghost_mdx = null
	_ghost_ready = false
	_ghost_hit = null
	_pawn.name = "DebugBladeMaster"

	_ensure_ws_camera()
	_activate_ws_camera()
	_ws_cam.set_follow(_pawn)
	_show_dest_cursor(pos, 1.2)
	var p := get_parent()
	if p != null and p.has_method("_sync_cameras_after_blade_mode"):
		p._sync_cameras_after_blade_mode()
	print(
		"[DebugSpawnPawn] 已放置 Blade Master @ ",
		pos,
		" — Warsmash 相机已启用；左键点地寻路移动"
	)


func _order_move(goal: Vector3) -> void:
	if _pawn == null or _world == null or _world.pathfinder == null:
		return
	if _world.sampler != null:
		goal.y = float(_world.sampler.sample_y(goal.x, goal.z))
	_show_dest_cursor(goal, 12.0)
	var pts: Array[Vector3] = []
	if _world.pathfinder.has_method("find_world_path"):
		pts = _world.pathfinder.find_world_path(_pawn.global_position, goal, COLLISION_SIZE)
	_path = pts
	_path_i = 0
	_draw_path(_path)
	if _path.is_empty():
		print("[DebugSpawnPawn] 无路径（障碍/不可走）")
		if _mdx and _mdx.has_method("set_moving"):
			_mdx.set_moving(false)
	else:
		# Face first leg immediately so we never slide sideways.
		var first: Vector3 = _path[0] - _pawn.global_position
		_face(first)
		print("[DebugSpawnPawn] 路径点=", _path.size(), " source=", _world.pathfinder.get("source_label"))


func _tick_move(delta: float) -> void:
	if _pawn == null or _world == null or _world.sampler == null:
		return
	var moving := false
	if _path_i < _path.size():
		var target: Vector3 = _path[_path_i]
		var to: Vector3 = target - _pawn.global_position
		to.y = 0.0
		if to.length() <= ARRIVE:
			_path_i += 1
			if _path_i >= _path.size():
				_cursor_ttl = minf(_cursor_ttl, 0.45)
				_clear_path_draw()
			elif _path_i < _path.size():
				# Face next waypoint as soon as we advance.
				var nxt: Vector3 = _path[_path_i] - _pawn.global_position
				_face(nxt)
		else:
			var step: Vector3 = to.normalized() * MOVE_SPEED * delta
			if step.length() > to.length():
				step = to
			_pawn.global_position.x += step.x
			_pawn.global_position.z += step.z
			_face(to)
			moving = true
	_pawn.global_position.y = float(_world.sampler.sample_y(_pawn.global_position.x, _pawn.global_position.z))
	if _mdx and _mdx.has_method("set_moving"):
		_mdx.set_moving(moving)
	if moving and _dest_cursor and _path.size() > 0:
		var goal: Vector3 = _path[_path.size() - 1]
		_dest_cursor.global_position = Vector3(
			goal.x, float(_world.sampler.sample_y(goal.x, goal.z)) + 4.0, goal.z
		)


func _face(dir: Vector3) -> void:
	if _pawn == null:
		return
	var flat := Vector3(dir.x, 0.0, dir.z)
	if flat.length() < 0.001:
		return
	flat = flat.normalized()
	# WC3 MDX faces +X; doodads use Basis(UP, -angle) with angle=atan2(z,x).
	# Matches map units so the hero always looks along the move vector.
	_pawn.rotation.y = -atan2(flat.z, flat.x)


func _ray_terrain(screen_pos: Vector2) -> Variant:
	var cam := _active_cam()
	if cam == null or _world == null or _world.sampler == null:
		return null
	var from := cam.project_ray_origin(screen_pos)
	var dir := cam.project_ray_normal(screen_pos)
	if dir.length_squared() < 0.000001:
		return null
	dir = dir.normalized()
	# Dense walk along ray; hit when crossing terrain height.
	var prev_above := true
	var prev_p: Vector3 = from
	for i in 200:
		var t: float = 10.0 + float(i) * 25.0
		var p: Vector3 = from + dir * t
		var gy: float = float(_world.sampler.sample_y(p.x, p.z))
		var above: bool = p.y > gy + 2.0
		if prev_above and not above:
			# Refine between prev and p.
			var a: Vector3 = prev_p
			var b: Vector3 = p
			for _k in 8:
				var mid: Vector3 = (a + b) * 0.5
				var mgy: float = float(_world.sampler.sample_y(mid.x, mid.z))
				if mid.y > mgy + 2.0:
					a = mid
				else:
					b = mid
			var hit: Vector3 = b
			hit.y = float(_world.sampler.sample_y(hit.x, hit.z))
			return hit
		if p.y <= gy + 8.0 and absf(dir.y) > 0.02:
			return Vector3(p.x, gy, p.z)
		prev_above = above
		prev_p = p
	# Flat-plane fallback at map mid height under ray.
	if absf(dir.y) > 0.0001:
		var y0: float = float(_world.sampler.sample_y(from.x, from.z))
		var t2: float = (y0 - from.y) / dir.y
		if t2 > 0.0:
			var p2: Vector3 = from + dir * t2
			p2.y = float(_world.sampler.sample_y(p2.x, p2.z))
			return p2
	return null


func _active_cam() -> Camera3D:
	if is_warsmash_active():
		return _ws_cam
	var vp := get_viewport()
	if vp != null:
		var cur := vp.get_camera_3d()
		if cur != null:
			return cur
	return _fallback_cam


func _ensure_ws_camera() -> void:
	if _ws_cam != null:
		return
	_ws_cam = Camera3D.new()
	_ws_cam.set_script(W3WarsmashCameraScript)
	_ws_cam.name = "WarsmashFollowCam"
	add_child(_ws_cam)
	_ws_cam.current = false


func _activate_ws_camera() -> void:
	if _pawn == null:
		return
	_ensure_ws_camera()
	var sandbox := get_parent()
	if sandbox != null and sandbox.get("_iso") != null:
		var iso = sandbox._iso
		if iso != null and iso.has_method("set_own_current"):
			iso.set_own_current(false)
		elif iso != null:
			iso.set("own_current", false)
			if iso.has_method("set_process"):
				iso.set_process(false)
			var iso_cam := iso.get_node_or_null("Camera3D") as Camera3D
			if iso_cam:
				iso_cam.current = false
	if _fallback_cam:
		_fallback_cam.current = false
		if _fallback_cam.has_method("set_process"):
			_fallback_cam.set_process(false)
	_ws_cam.set_follow(_pawn)
	_ws_cam.snap_to(_pawn.global_position)
	_ws_cam.projection = Camera3D.PROJECTION_PERSPECTIVE
	_ws_cam.current = true
	print("[DebugSpawnPawn] Warsmash cam ON follow=", _pawn.name)


func _restore_fallback_camera() -> void:
	if _ws_cam:
		_ws_cam.clear_follow()
		_ws_cam.current = false
	if _fallback_cam:
		_fallback_cam.current = true
		if _fallback_cam.has_method("set_process"):
			_fallback_cam.set_process(true)


func _ensure_cursor_visuals() -> void:
	if _dest_cursor != null:
		return
	_dest_cursor = Node3D.new()
	_dest_cursor.name = "MoveCursor"
	add_child(_dest_cursor)
	_dest_ring = MeshInstance3D.new()
	_dest_ring.mesh = _make_ring(48.0, 8.0, 40)
	_dest_ring.material_override = _unshaded(CURSOR_COLOR)
	_dest_ring.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_dest_cursor.add_child(_dest_ring)
	_dest_beam = MeshInstance3D.new()
	var beam := CylinderMesh.new()
	beam.top_radius = 2.0
	beam.bottom_radius = 2.0
	beam.height = 90.0
	_dest_beam.mesh = beam
	_dest_beam.position.y = 45.0
	_dest_beam.material_override = _unshaded(Color(CURSOR_COLOR.r, CURSOR_COLOR.g, CURSOR_COLOR.b, 0.35))
	_dest_beam.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_dest_cursor.add_child(_dest_beam)
	_dest_cursor.visible = false

	_path_mi = MeshInstance3D.new()
	_path_mi.name = "PathRibbon"
	_path_mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_path_mi.material_override = _unshaded(PATH_COLOR)
	add_child(_path_mi)
	_path_mi.visible = false


func _draw_path(pts: Array[Vector3]) -> void:
	_ensure_cursor_visuals()
	if pts.size() < 2:
		_clear_path_draw()
		return
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var half_w := 10.0
	for i in range(pts.size() - 1):
		var a: Vector3 = pts[i]
		var b: Vector3 = pts[i + 1]
		a.y = float(_world.sampler.sample_y(a.x, a.z)) + 6.0
		b.y = float(_world.sampler.sample_y(b.x, b.z)) + 6.0
		var along: Vector3 = b - a
		along.y = 0.0
		if along.length() < 0.01:
			continue
		along = along.normalized()
		var side := Vector3(-along.z, 0.0, along.x) * half_w
		var a0 := a - side
		var a1 := a + side
		var b0 := b - side
		var b1 := b + side
		st.add_vertex(a0)
		st.add_vertex(b0)
		st.add_vertex(b1)
		st.add_vertex(a0)
		st.add_vertex(b1)
		st.add_vertex(a1)
	_path_mi.mesh = st.commit()
	_path_mi.visible = true


func _clear_path_draw() -> void:
	if _path_mi:
		_path_mi.visible = false
		_path_mi.mesh = null


func _show_dest_cursor(pos: Vector3, ttl: float) -> void:
	_ensure_cursor_visuals()
	var y: float = pos.y
	if _world and _world.sampler:
		y = float(_world.sampler.sample_y(pos.x, pos.z))
	_dest_cursor.global_position = Vector3(pos.x, y + 4.0, pos.z)
	_dest_cursor.visible = true
	_cursor_ttl = ttl
	if _dest_ring and _dest_ring.material_override:
		_dest_ring.material_override.albedo_color = CURSOR_COLOR


func _update_place_ghost() -> void:
	_ensure_place_ghost_model()
	var hit = _ray_terrain(get_viewport().get_mouse_position())
	_ghost_hit = hit
	if hit == null:
		return
	_ghost_root.visible = true
	_ghost_root.global_position = hit


func _hide_place_ghost() -> void:
	_ghost_hit = null
	if _ghost_root != null and is_instance_valid(_ghost_root) and _ghost_root != _pawn:
		_ghost_root.visible = false


func _hide_cursors() -> void:
	_hide_place_ghost()
	_clear_path_draw()
	if _dest_cursor:
		_dest_cursor.visible = false
	_cursor_ttl = 0.0


func _unshaded(color: Color) -> StandardMaterial3D:
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.albedo_color = color
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.depth_draw_mode = BaseMaterial3D.DEPTH_DRAW_DISABLED
	mat.render_priority = 12
	return mat


func _make_ring(radius: float, thickness: float, segments: int) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var r0: float = maxf(4.0, radius - thickness)
	var r1: float = radius
	for i in segments:
		var a0: float = TAU * float(i) / float(segments)
		var a1: float = TAU * float(i + 1) / float(segments)
		var c0 := Vector3(cos(a0) * r0, 0.0, sin(a0) * r0)
		var c1 := Vector3(cos(a1) * r0, 0.0, sin(a1) * r0)
		var o0 := Vector3(cos(a0) * r1, 0.0, sin(a0) * r1)
		var o1 := Vector3(cos(a1) * r1, 0.0, sin(a1) * r1)
		st.add_vertex(c0)
		st.add_vertex(o0)
		st.add_vertex(o1)
		st.add_vertex(c0)
		st.add_vertex(o1)
		st.add_vertex(c1)
	return st.commit()
