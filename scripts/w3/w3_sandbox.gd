extends Node3D
## War3 0.1 sandbox: Diablo click-move + MDX, or free-fly map preview.


@export var map_path: String = ""
## Optional pre-imported W3LevelData (.res). When set, sandbox loads this instead of map_path.
@export var imported_level: Resource
@export var terrain_only: bool = false
## 0.1 acceptance path available via diablo_play; default OFF so sandbox starts as map preview.
## Blade Master: /debug → M (keep cam, place) → Warsmash after spawn. Never auto-start.
@export var diablo_play: bool = false
## Outdoor lighting. Swap Resource anytime:
## War3 → w3_lit_settings_preview.tres | UE → ue_style_lit_default.tres | Bistro → bistro_lit_default.tres
@export var lit_settings: Resource = preload("res://addons/w3kit/ue_style_lit_default.tres")
## Hotkey F also toggles fog at runtime.
@export var preview_fog: bool = true
## Relative MDX under asset root (0.1 default Footman).
@export var player_mdx: String = "Units/Human/Footman/Footman.mdx"

@onready var _world = $W3MapWorld
@onready var _cam: Camera3D = $FreeFlyCamera
@onready var _hud: Label = $CanvasLayer/Status
@onready var _loading: Control = $CanvasLayer/LoadingOverlay
@onready var _load_bar: ProgressBar = $CanvasLayer/LoadingOverlay/Center/VBox/Progress
@onready var _load_detail: Label = $CanvasLayer/LoadingOverlay/Center/VBox/Detail
@onready var _console: Control = $CanvasLayer/GotoConsole
@onready var _console_edit: LineEdit = $CanvasLayer/GotoConsole/VBox/LineEdit
@onready var _dir_legend: Control = $CanvasLayer/DirLegend
@onready var _player: Node = get_node_or_null("W3GridPlayer")
@onready var _iso: Node = get_node_or_null("IsoCamera")

var _settings: Resource
var _fog_on: bool = true
var _tile_debug: Node3D
var _unit_pick: Node3D
var _spawn_pawn: Node3D
var _console_open: bool = false
var _focused_tile: Vector2i = Vector2i(-1, -1)
var _inspect_mode: bool = false
var _soft_tint: bool = false
var _debug_tools: bool = false
var _scene_ready: bool = false
var _light_orb: Node3D
var _bistro_bar: Control
var _bistro_btns: Dictionary = {} # scenario int -> Button
var _hotkeys_bar: Label
var _saved_light_energies: Dictionary = {} # NodePath -> float


func _ready() -> void:
	_world.terrain_only = terrain_only
	# UE soft sun needs real shadows; War3 preview often skips them for speed.
	if _uses_ue_style_lit(lit_settings) and lit_settings.get("sun_shadows") == true:
		_world.preview_no_shadows = false
	_world.visible = false
	_set_loading_visible(true)
	_apply_lighting()

	var path := map_path
	if path.begins_with("res://") or path.begins_with("user://"):
		path = ProjectSettings.globalize_path(path)
	if path.is_empty():
		path = W3AssetConfig.default_map_path
	var report: Dictionary = W3AssetConfig.check_report()
	for m in report["messages"]:
		print("[W3Asset] ", m)
	print("[W3Sandbox] map: ", path if not path.is_empty() else "(procedural)", " diablo_play=", diablo_play)

	_world.load_progress.connect(_on_load_progress)
	if imported_level != null:
		_on_load_progress(0.05, "Loading imported level…")
		_world.load_level_data(imported_level)
	elif path.is_empty():
		_on_load_progress(1.0, "Procedural map")
		# Sandbox preview may use procedural when no map configured.
		_world.allow_procedural_fallback = true
		_world.load_procedural()
	else:
		await _world.load_path_async(path)

	# Terrain exists now — push open-realm light dir into GPU terrain shader.
	_apply_lighting()

	if diablo_play:
		_setup_diablo_play()
	else:
		_setup_preview_fly()

	_world.visible = true
	_set_loading_visible(false)
	_scene_ready = true
	if _hud:
		_hud.visible = true
	# Mouse light probe temporarily off (was blowing out the scene white).
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	if _world and _world.get("terrain") != null and _world.terrain.has_method("set_probe_light"):
		_world.terrain.set_probe_light(false)
	_ensure_bistro_bar()
	_ensure_hotkeys_bar()

	if _console_edit:
		_console_edit.text_submitted.connect(_on_console_submit)
		_console_edit.placeholder_text = "x,y 或 /u 编号 或 /debug"
	_set_console_open(false)
	if _dir_legend:
		_dir_legend.visible = false
	_update_hud()


func _setup_diablo_play() -> void:
	# Free-fly off — iso follow owns the view.
	if _cam:
		_cam.current = false
		_cam.set_process(false)
		_cam.set_process_unhandled_input(false)
		_cam.visible = false
	_ensure_iso_camera()
	if _player == null:
		push_warning("[W3Sandbox] W3GridPlayer missing")
		return
	_player.visible = true
	_player.set_process(true)
	_player.set_physics_process(true)
	_player.set_process_input(true)
	_player.set_process_unhandled_input(true)
	if _player.has_method("bind_world"):
		_player.bind_world(_world)
	var mdx_path := player_mdx
	if not mdx_path.is_empty() and _player.has_method("attach_mdx"):
		_player.attach_mdx(mdx_path)
		print("[W3Sandbox] player MDX: ", mdx_path)
	if _iso:
		_iso.set("follow_target", _player)
		if _iso.has_method("snap_to_target"):
			_iso.snap_to_target()
	print("[W3Sandbox] diablo_play ON — LMB grid move, Stand/Walk on MDX")


func _setup_preview_fly() -> void:
	if _player:
		_player.visible = false
		_player.set_process(false)
		_player.set_physics_process(false)
		_player.set_process_input(false)
		_player.set_process_unhandled_input(false)
	if _iso:
		_iso.visible = false
		if _iso.has_method("set_process"):
			_iso.set_process(false)
		var iso_cam := _iso.get_node_or_null("Camera3D") as Camera3D
		if iso_cam:
			iso_cam.current = false
	if _cam:
		_cam.visible = true
		_cam.set_process(true)
		_cam.set_process_unhandled_input(true)
		if _world.map and _cam.has_method("focus_point"):
			var mid := Vector2i(_world.map.tile_count_x() / 2, _world.map.tile_count_y() / 2)
			var center: Vector3 = _world.sampler.tile_center_world(mid)
			var extent: float = float(maxi(_world.map.tile_count_x(), _world.map.tile_count_y())) * 128.0
			_cam.focus_point(center, extent * 0.65)
		else:
			_cam.current = true
	print("[W3Sandbox] preview fly ON")


func _ensure_iso_camera() -> void:
	if _iso == null:
		var packed: PackedScene = load("res://scenes/iso_camera.tscn")
		if packed:
			_iso = packed.instantiate()
			_iso.name = "IsoCamera"
			add_child(_iso)
	if _iso == null:
		return
	_iso.visible = true
	if _iso.has_method("set_process"):
		_iso.set_process(true)
	# War3 tile = 128wu; town ortho (~22) is far too tight.
	_iso.set("ortho_size", 1100.0)
	_iso.set("distance", 1600.0)
	_iso.set("follow_speed", 10.0)
	if _iso.has_method("snap_to_target"):
		_iso.snap_to_target()
	var iso_cam := _iso.get_node_or_null("Camera3D") as Camera3D
	if iso_cam:
		iso_cam.far = 20000.0
		iso_cam.near = 1.0
		iso_cam.current = true


func _set_loading_visible(on: bool) -> void:
	if _loading:
		_loading.visible = on


func _on_load_progress(ratio: float, message: String) -> void:
	if _load_bar:
		_load_bar.value = ratio
	if _load_detail:
		_load_detail.text = message


func _process(_delta: float) -> void:
	if _console_open:
		return
	if not _debug_tools:
		return
	if _tile_debug != null and _tile_debug.has_method("is_debug_enabled") and _tile_debug.is_debug_enabled():
		var cam := get_viewport().get_camera_3d()
		if cam == null:
			cam = _cam
		_tile_debug.update_hover(cam)
		_update_hud()


func _ensure_light_orb() -> void:
	if _light_orb != null:
		if _light_orb.has_method("refresh_world"):
			_light_orb.refresh_world(_world)
		return
	var script: GDScript = load("res://scripts/w3/w3_light_orb.gd")
	_light_orb = script.new()
	_light_orb.name = "LightOrbProbe"
	_light_orb.set("probe_enabled", false)
	add_child(_light_orb)
	_light_orb.setup(_cam, _world)
	# setup may re-apply export default — force off.
	if _light_orb.has_method("set_probe_enabled"):
		_light_orb.set_probe_enabled(false)
	print("[W3Sandbox] light orb OFF (L to enable)")


func _ensure_hotkeys_bar() -> void:
	if _hotkeys_bar != null:
		_refresh_hotkeys_bar()
		return
	var layer := get_node_or_null("CanvasLayer") as CanvasLayer
	if layer == null:
		return
	var label := Label.new()
	label.name = "HotkeysBar"
	label.set_anchors_preset(Control.PRESET_TOP_WIDE)
	label.offset_left = 12.0
	label.offset_top = 4.0
	label.offset_right = -12.0
	label.offset_bottom = 40.0
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.add_theme_font_size_override("font_size", 14)
	label.add_theme_color_override("font_color", Color(0.95, 0.93, 0.86, 0.95))
	label.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.85))
	label.add_theme_constant_override("outline_size", 4)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	layer.add_child(label)
	_hotkeys_bar = label
	# Keep status below the hotkey strip.
	if _hud:
		_hud.offset_top = 44.0
		_hud.offset_bottom = 200.0
	_refresh_hotkeys_bar()


func _refresh_hotkeys_bar() -> void:
	if _hotkeys_bar == null:
		return
	var fog_on := _settings != null and bool(_settings.get("fog_enabled"))
	var orb_on := _light_orb != null and bool(_light_orb.get("probe_enabled"))
	var parts: PackedStringArray = [
		"F 雾(%s)" % ("开" if fog_on else "关"),
		"L 点光源(%s)" % ("开" if orb_on else "关"),
		"` 控制台",
	]
	if diablo_play:
		parts.append("左键点地移动")
		parts.append("play=ON")
	else:
		parts.append_array(PackedStringArray([
			"WASD 移动",
			"QE 升降",
			"右键 视角",
			"滚轮 缩放",
		]))
	if _debug_tools:
		var sel := -1
		if _unit_pick != null and _unit_pick.has_method("get_selected_report_id"):
			sel = int(_unit_pick.get_selected_report_id())
		parts.append("左键点选单位")
		parts.append("选中#%s" % (str(sel) if sel >= 0 else "—"))
		parts.append("N 全部名牌(%s)" % ("开" if _unit_labels_on() else "关"))
		var grid_on := false
		if _tile_debug != null and _tile_debug.has_method("is_debug_enabled"):
			grid_on = bool(_tile_debug.is_debug_enabled())
		parts.append("G 格子(%s)" % ("开" if grid_on else "关"))
		parts.append("Tab 检视(%s)" % ("开" if _inspect_mode else "关"))
		parts.append("H 软染色(%s)" % ("开" if _soft_tint else "关"))
		var m_on := false
		if _spawn_pawn != null and _spawn_pawn.has_method("is_control_mode"):
			m_on = bool(_spawn_pawn.is_control_mode())
		parts.append("M 剑圣(%s)" % ("指挥" if m_on else "关"))
	_hotkeys_bar.text = "  ·  ".join(parts)


func _ensure_bistro_bar() -> void:
	if _bistro_bar != null:
		_refresh_bistro_bar()
		return
	var layer := get_node_or_null("CanvasLayer") as CanvasLayer
	if layer == null:
		return
	var panel := PanelContainer.new()
	panel.name = "BistroLitBar"
	panel.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	panel.offset_left = -420.0
	panel.offset_top = 48.0
	panel.offset_right = -12.0
	panel.offset_bottom = 92.0
	layer.add_child(panel)
	_bistro_bar = panel

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	panel.add_child(vbox)

	var title := Label.new()
	title.text = "Bistro lit"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	vbox.add_child(title)

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	row.alignment = BoxContainer.ALIGNMENT_END
	vbox.add_child(row)

	var labels := [
		[0, "Noon"],
		[1, "Afternoon"],
		[2, "Dusk"],
		[3, "Night"],
	]
	for entry in labels:
		var sc: int = int(entry[0])
		var btn := Button.new()
		btn.text = str(entry[1])
		btn.focus_mode = Control.FOCUS_NONE
		btn.pressed.connect(_on_bistro_scenario_pressed.bind(sc))
		row.add_child(btn)
		_bistro_btns[sc] = btn

	_refresh_bistro_bar()


func _on_bistro_scenario_pressed(scenario: int) -> void:
	# Switching scenario always activates Bistro lit (independent of War3 / UE).
	if not _uses_bistro_lit(lit_settings):
		lit_settings = load("res://addons/w3kit/bistro_lit_default.tres")
		if lit_settings:
			lit_settings = lit_settings.duplicate(true)
	if lit_settings == null:
		lit_settings = preload("res://addons/w3kit/bistro_lit_settings.gd").new()
	lit_settings.set("scenario", scenario)
	lit_settings.set("manual_override", false)
	# Soft sun shadows for outdoor readability.
	if _world and lit_settings.get("sun_shadows") == true:
		_world.preview_no_shadows = false
	_apply_lighting()
	_refresh_bistro_bar()
	_update_hud()
	print("[W3Sandbox] Bistro scenario=", scenario, " → ", _lit_label(_settings))


func _refresh_bistro_bar() -> void:
	if _bistro_bar == null:
		return
	var active := -1
	if _uses_bistro_lit(_settings if _settings else lit_settings):
		var s = _settings if _settings else lit_settings
		active = int(s.get("scenario")) if s.get("scenario") != null else 1
	for sc in _bistro_btns.keys():
		var btn: Button = _bistro_btns[sc]
		if btn == null:
			continue
		btn.disabled = false
		btn.modulate = Color(1.2, 1.15, 0.9) if int(sc) == active else Color(1, 1, 1)
		btn.button_pressed = int(sc) == active


func _input(event: InputEvent) -> void:
	# Catch ` even while LineEdit has focus.
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_QUOTELEFT or event.keycode == KEY_ASCIITILDE:
			_set_console_open(not _console_open)
			get_viewport().set_input_as_handled()
			return
		if _console_open and event.keycode == KEY_ESCAPE:
			_set_console_open(false)
			get_viewport().set_input_as_handled()


func _unhandled_input(event: InputEvent) -> void:
	if _console_open:
		return
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_F:
			_fog_on = not _fog_on
			_apply_lighting()
			if _inspect_mode and _world and _world.terrain and _world.terrain.has_method("set_flat_inspect"):
				_world.terrain.set_flat_inspect(true)
			_update_hud()
			get_viewport().set_input_as_handled()
		elif event.keycode == KEY_L:
			_ensure_light_orb()
			if _light_orb and _light_orb.has_method("toggle"):
				_light_orb.toggle()
				var on: bool = bool(_light_orb.get("probe_enabled"))
				print("[W3Sandbox] light orb=", on)
			_update_hud()
			get_viewport().set_input_as_handled()
		elif not _debug_tools:
			return
		elif event.keycode == KEY_N:
			_toggle_unit_labels()
			get_viewport().set_input_as_handled()
		elif event.keycode == KEY_G:
			_ensure_tile_debug()
			if _tile_debug != null and _tile_debug.has_method("toggle"):
				_tile_debug.toggle()
				_update_hud()
			get_viewport().set_input_as_handled()
		elif event.keycode == KEY_TAB:
			_toggle_inspect_mode()
			get_viewport().set_input_as_handled()
		elif event.keycode == KEY_H:
			_soft_tint = not _soft_tint
			if _world and _world.terrain and _world.terrain.has_method("set_soft_tint"):
				_world.terrain.set_soft_tint(_soft_tint)
			print("[W3Sandbox] soft_tint=", _soft_tint)
			_update_hud()
			get_viewport().set_input_as_handled()
		elif event.keycode == KEY_M:
			_ensure_spawn_pawn()
			if _spawn_pawn != null and _spawn_pawn.has_method("toggle_control_mode"):
				_spawn_pawn.toggle_control_mode()
				_sync_cameras_after_blade_mode()
			_update_hud()
			get_viewport().set_input_as_handled()


func _toggle_inspect_mode() -> void:
	_inspect_mode = not _inspect_mode
	# Water off.
	if _world and _world.water:
		_world.water.visible = not _inspect_mode
	# Scene lights off / restore.
	for n in find_children("*", "Light3D", true, false):
		var light := n as Light3D
		if light == null:
			continue
		var path := light.get_path()
		if _inspect_mode:
			_saved_light_energies[path] = light.light_energy
			light.light_energy = 0.0
		else:
			if _saved_light_energies.has(path):
				light.light_energy = float(_saved_light_energies[path])
			else:
				light.light_energy = 1.0
	if not _inspect_mode:
		_saved_light_energies.clear()
	# Fog off while inspecting textures.
	if _inspect_mode:
		_fog_on = false
	_apply_lighting()
	# Flat albedo after lit sync (lit would overwrite ambient / SHD).
	if _world and _world.terrain and _world.terrain.has_method("set_flat_inspect"):
		_world.terrain.set_flat_inspect(_inspect_mode)
	_update_hud()


func _set_debug_tools(on: bool) -> void:
	_debug_tools = on
	if on:
		_ensure_tile_debug()
		_ensure_unit_pick()
		_ensure_spawn_pawn()
		if _unit_pick != null and _unit_pick.has_method("set_debug_enabled"):
			_unit_pick.set_debug_enabled(true)
		if _spawn_pawn != null and _spawn_pawn.has_method("set_debug_enabled"):
			_spawn_pawn.set_debug_enabled(true)
		print("[W3Sandbox] debug ON — M=剑圣模式(需再按M) | 左键点选 | N名牌 G格子 Tab检视")
	else:
		_disable_all_debug_visuals()
		_sync_cameras_after_blade_mode()
		print("[W3Sandbox] debug OFF")
	_update_hud()


func _disable_all_debug_visuals() -> void:
	if _inspect_mode:
		_toggle_inspect_mode()
	if _soft_tint:
		_soft_tint = false
		if _world and _world.terrain and _world.terrain.has_method("set_soft_tint"):
			_world.terrain.set_soft_tint(false)
	if _tile_debug != null and _tile_debug.has_method("set_debug_enabled"):
		_tile_debug.set_debug_enabled(false)
	if _unit_pick != null and _unit_pick.has_method("set_debug_enabled"):
		_unit_pick.set_debug_enabled(false)
	if _spawn_pawn != null and _spawn_pawn.has_method("set_debug_enabled"):
		_spawn_pawn.set_debug_enabled(false)
	_focused_tile = Vector2i(-1, -1)
	if _dir_legend:
		_dir_legend.visible = false


func _set_console_open(open: bool) -> void:
	_console_open = open
	if _console:
		_console.visible = open
	if _cam and _cam.get("input_blocked") != null:
		_cam.input_blocked = open
	if open:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		if _cam and _cam.has_method("release_look"):
			_cam.release_look()
		if _console_edit:
			_console_edit.grab_focus()
			_console_edit.select_all()
	else:
		if _console_edit:
			_console_edit.release_focus()
		# Restore hidden cursor if light orb is active.
		if _light_orb != null and bool(_light_orb.get("probe_enabled")):
			Input.mouse_mode = Input.MOUSE_MODE_HIDDEN


func _unit_labels_on() -> bool:
	if _world == null or _world.batcher == null:
		return false
	if _world.batcher.has_method("are_unit_labels_visible"):
		return bool(_world.batcher.are_unit_labels_visible())
	return false


func _toggle_unit_labels() -> void:
	if not _debug_tools:
		return
	if _world == null or _world.batcher == null:
		return
	if not _world.batcher.has_method("set_unit_labels_visible"):
		return
	var on := not _unit_labels_on()
	_world.batcher.set_unit_labels_visible(on)
	_update_hud()


func _focus_unit_report(report_id: int) -> void:
	if _world == null or _world.batcher == null:
		return
	if _debug_tools:
		_ensure_unit_pick()
		if _unit_pick != null and _unit_pick.has_method("select_report_id"):
			if _unit_pick.select_report_id(report_id):
				var info2: Dictionary = _unit_pick.get_selected_info() if _unit_pick.has_method("get_selected_info") else {}
				var c2: Vector3 = info2.get("center", Vector3.ZERO)
				if _cam and _cam.has_method("focus_point") and c2 != Vector3.ZERO:
					_cam.focus_point(c2, 900.0)
				_update_hud()
				return
	var info: Dictionary = {}
	if _world.batcher.has_method("get_unit_info"):
		info = _world.batcher.get_unit_info(report_id)
	if info.is_empty():
		print("[W3Sandbox] 无单位 #%d" % report_id)
		return
	var center: Vector3 = info.get("center", Vector3.ZERO)
	print(
		"[W3Sandbox] #%d  %s  [%s]  cn=%d  owner=%d  pos=(%.0f,%.0f,%.0f)"
		% [
			report_id,
			info.get("unit_name", ""),
			info.get("unit_id", ""),
			info.get("creation_number", 0),
			info.get("player_owner", 0),
			center.x,
			center.y,
			center.z,
		]
	)
	if _cam and _cam.has_method("focus_point"):
		_cam.focus_point(center, 900.0)
	_update_hud()


func _on_console_submit(text: String) -> void:
	var t := text.strip_edges()
	if t.is_empty():
		return
	var cmd := t.to_lower().replace(" ", "")
	if cmd == "/debug" or cmd == "debug":
		_set_debug_tools(not _debug_tools)
		_console_edit.clear()
		_set_console_open(false)
		return
	if cmd == "/debugon" or cmd == "/debug=1":
		_set_debug_tools(true)
		_console_edit.clear()
		_set_console_open(false)
		return
	if cmd == "/debugoff" or cmd == "/debug=0":
		_set_debug_tools(false)
		_console_edit.clear()
		_set_console_open(false)
		return
	# /u 42 — jump to unit report id (avoid bare "u" matching unrelated text)
	if cmd.begins_with("/u") or (cmd.length() >= 2 and cmd[0] == "u" and cmd.substr(1).is_valid_int()):
		var num_s := cmd.trim_prefix("/").trim_prefix("u").trim_prefix("=")
		if num_s.is_valid_int():
			_focus_unit_report(int(num_s))
			_console_edit.clear()
			_set_console_open(false)
			return

	t = t.replace("，", ",").replace(" ", "")
	var parts: PackedStringArray = t.split(",")
	if parts.size() < 2:
		_console_edit.placeholder_text = "x,y 或 /u 编号 或 /debug"
		_console_edit.clear()
		return
	if not parts[0].is_valid_int() or not parts[1].is_valid_int():
		_console_edit.placeholder_text = "坐标必须是整数"
		_console_edit.clear()
		return
	if not _debug_tools:
		_console_edit.placeholder_text = "先输入 /debug"
		_console_edit.clear()
		return
	var tx := int(parts[0])
	var ty := int(parts[1])
	if _world == null or _world.map == null or _world.sampler == null:
		return
	var max_x: int = _world.map.tile_count_x() - 1
	var max_y: int = _world.map.tile_count_y() - 1
	tx = clampi(tx, 0, max_x)
	ty = clampi(ty, 0, max_y)
	var center: Vector3 = _world.sampler.tile_center_world(Vector2i(tx, ty))
	_focused_tile = Vector2i(tx, ty)
	_ensure_tile_debug()
	if _tile_debug != null and _tile_debug.has_method("highlight_tile"):
		_tile_debug.highlight_tile(_focused_tile)
	if _cam and _cam.has_method("focus_tile_topdown"):
		_cam.focus_tile_topdown(center, W3EMap_TILE_ORTHO)
	elif _cam and _cam.has_method("focus_point"):
		_cam.focus_point(center, 900.0)
	if _dir_legend:
		_dir_legend.visible = true
	print("[W3Sandbox] goto tile %d,%d (topdown) -> %s" % [tx, ty, center])
	_set_console_open(false)
	_update_hud()


const W3EMap_TILE_ORTHO := 420.0  # ~3 tiles wide in ortho view


func _ensure_tile_debug() -> void:
	if _tile_debug != null:
		return
	if _world == null or _world.map == null:
		return
	var script: GDScript = load("res://scripts/w3/w3_tile_debug_overlay.gd")
	_tile_debug = script.new()
	_tile_debug.name = "TileDebugOverlay"
	add_child(_tile_debug)
	_tile_debug.setup(_world.map)
	_tile_debug.set_debug_enabled(false)


func _ensure_unit_pick() -> void:
	if _unit_pick != null:
		if _unit_pick.has_method("setup"):
			_unit_pick.setup(_world.batcher if _world else null, _cam)
		return
	var script: GDScript = load("res://scripts/w3/w3_unit_debug_pick.gd")
	_unit_pick = script.new()
	_unit_pick.name = "UnitDebugPick"
	add_child(_unit_pick)
	_unit_pick.setup(_world.batcher if _world else null, _cam)
	if _unit_pick.has_signal("unit_selected"):
		_unit_pick.unit_selected.connect(func(_id, _info): _update_hud())
	if _unit_pick.has_signal("unit_deselected"):
		_unit_pick.unit_deselected.connect(func(): _update_hud())


func _ensure_spawn_pawn() -> void:
	if _spawn_pawn != null:
		# Never re-call setup() — it used to wipe process/input and broke placing.
		if _spawn_pawn.has_method("bind"):
			_spawn_pawn.bind(_world, _cam)
		return
	var script: GDScript = load("res://scripts/w3/w3_debug_spawn_pawn.gd")
	_spawn_pawn = script.new()
	_spawn_pawn.name = "DebugSpawnPawn"
	add_child(_spawn_pawn)
	_spawn_pawn.setup(_world, _cam)


## After leaving Blade Master mode, put the sandbox camera back (iso or freefly).
func _sync_cameras_after_blade_mode() -> void:
	# Place phase (M, no pawn yet): keep free-fly / iso. Only steal after Warsmash is on.
	var warsmash_on := false
	if _spawn_pawn != null and _spawn_pawn.has_method("is_warsmash_active"):
		warsmash_on = bool(_spawn_pawn.is_warsmash_active())
	if warsmash_on:
		# Blade follow owns view — keep iso from reclaiming.
		if _iso and _iso.has_method("set_own_current"):
			_iso.set_own_current(false)
		elif _iso:
			_iso.set("own_current", false)
			if _iso.has_method("set_process"):
				_iso.set_process(false)
			var iso_cam := _iso.get_node_or_null("Camera3D") as Camera3D
			if iso_cam:
				iso_cam.current = false
		if _cam:
			_cam.current = false
		return
	if diablo_play:
		_ensure_iso_camera()
		if _iso:
			_iso.visible = true
			if _iso.has_method("set_own_current"):
				_iso.set_own_current(true)
			else:
				_iso.set("own_current", true)
			if _iso.has_method("set_process"):
				_iso.set_process(true)
			if _player:
				_iso.set("follow_target", _player)
				if _iso.has_method("snap_to_target"):
					_iso.snap_to_target()
			var iso_cam := _iso.get_node_or_null("Camera3D") as Camera3D
			if iso_cam:
				iso_cam.current = true
		if _cam:
			_cam.current = false
	else:
		if _iso and _iso.has_method("set_own_current"):
			_iso.set_own_current(false)
		if _iso:
			var iso_cam := _iso.get_node_or_null("Camera3D") as Camera3D
			if iso_cam:
				iso_cam.current = false
			if _iso.has_method("set_process"):
				_iso.set_process(false)
		if _cam:
			_cam.visible = true
			_cam.current = true
			_cam.set_process(true)
			_cam.set_process_unhandled_input(true)


func _uses_ue_style_lit(settings: Resource) -> bool:
	if settings == null or settings.get_script() == null:
		return false
	if settings is UeStyleLitSettings:
		return true
	return str(settings.get_script().resource_path).ends_with("ue_style_lit_settings.gd")


func _uses_bistro_lit(settings: Resource) -> bool:
	if settings == null or settings.get_script() == null:
		return false
	if settings is BistroLitSettings:
		return true
	return str(settings.get_script().resource_path).ends_with("bistro_lit_settings.gd")


func _lit_label(settings: Resource) -> String:
	if _uses_bistro_lit(settings):
		var sc = int(settings.get("scenario")) if settings.get("scenario") != null else 1
		match sc:
			0:
				return "Bistro/noon"
			2:
				return "Bistro/dusk"
			3:
				return "Bistro/night"
			_:
				return "Bistro/afternoon"
	if _uses_ue_style_lit(settings):
		var be = settings.get("sky_backend")
		return "UE/Sky3D" if be == 1 else "UE/Nishita"
	return "War3"


func _apply_lighting() -> void:
	var settings = lit_settings
	if settings == null or not (settings is Resource) or settings.get_script() == null:
		settings = load("res://addons/w3kit/ue_style_lit_default.tres")
	if settings == null:
		settings = preload("res://addons/w3kit/ue_style_lit_settings.gd").new()
	_settings = settings.duplicate(true)
	if _settings.get("fog_enabled") != null:
		_settings.fog_enabled = preview_fog and _fog_on
	if _uses_bistro_lit(_settings):
		preload("res://addons/w3kit/bistro_lit.gd").apply_to_scene(self, _settings)
	elif _uses_ue_style_lit(_settings):
		preload("res://addons/w3kit/ue_style_lit.gd").apply_to_scene(self, _settings)
	else:
		preload("res://addons/w3kit/w3_war3_lit.gd").apply_to_scene(self, _settings)
	_refresh_bistro_bar()


func _update_hud() -> void:
	_refresh_hotkeys_bar()
	if _hud == null:
		return
	var fog_state := "ON" if (_settings != null and bool(_settings.get("fog_enabled"))) else "OFF"
	var water_n := 0
	if _world and _world.get("water") != null and _world.water.has_method("get_water_tile_count"):
		water_n = int(_world.water.get_water_tile_count())
	if not _debug_tools:
		var lit_name := _lit_label(_settings)
		var orb := "ON" if (_light_orb != null and bool(_light_orb.get("probe_enabled"))) else "OFF"
		_hud.text = "%s\nlit=%s fog=%s orb=%s water=%d" % [
			_world.get_status(),
			lit_name,
			fog_state,
			orb,
			water_n,
		]
		return
	var grid_state := "OFF"
	var hover := "—"
	if _tile_debug != null and _tile_debug.has_method("is_debug_enabled"):
		grid_state = "ON" if _tile_debug.is_debug_enabled() else "OFF"
		if _tile_debug.is_debug_enabled():
			var ht: Vector2i = _tile_debug.get_hover_tile()
			if ht.x >= 0:
				hover = "%d,%d" % [ht.x, ht.y]
	_hud.text = "%s\n[debug] fog=%s grid=%s inspect=%s soft=%s\nsoft: BR红 BL绿 TR蓝 TL黄 | water=%d | look=%s | FOCUS=%s" % [
		_world.get_status(),
		fog_state,
		grid_state,
		("ON" if _inspect_mode else "OFF"),
		("ON" if _soft_tint else "OFF"),
		water_n,
		hover,
		("—" if _focused_tile.x < 0 else ("%d,%d" % [_focused_tile.x, _focused_tile.y])),
	]
