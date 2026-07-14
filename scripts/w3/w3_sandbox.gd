extends Node3D
## War3 map preview: free-fly camera + Godot lighting / fog / modern water.


@export var map_path: String = "/path/to/Warcraft.III.R/Maps/(4)LostTemple.w3m"
@export var terrain_only: bool = false
## Outdoor lighting (sun / fill / fog). Preview tres enables fog for testing.
@export var lit_settings: Resource = preload("res://addons/w3kit/w3_lit_settings_preview.tres")
## Hotkey F also toggles fog at runtime.
@export var preview_fog: bool = true

@onready var _world = $W3MapWorld
@onready var _cam: Camera3D = $FreeFlyCamera
@onready var _hud: Label = $CanvasLayer/Status
@onready var _console: Control = $CanvasLayer/GotoConsole
@onready var _console_edit: LineEdit = $CanvasLayer/GotoConsole/VBox/LineEdit
@onready var _dir_legend: Control = $CanvasLayer/DirLegend
@onready var _player: Node = get_node_or_null("W3GridPlayer")
@onready var _iso: Node = get_node_or_null("IsoCamera")

var _settings: Resource
var _fog_on: bool = true
var _tile_debug: Node3D
var _console_open: bool = false
var _focused_tile: Vector2i = Vector2i(-1, -1)
var _inspect_mode: bool = false
var _soft_tint: bool = false
var _debug_tools: bool = false
var _saved_light_energies: Dictionary = {} # NodePath -> float


func _ready() -> void:
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

	_world.terrain_only = terrain_only
	_apply_lighting()

	var path := map_path
	if path.begins_with("res://") or path.begins_with("user://"):
		path = ProjectSettings.globalize_path(path)
	if path.is_empty():
		path = W3AssetConfig.default_map_path
	var report: Dictionary = W3AssetConfig.check_report()
	for m in report["messages"]:
		print("[W3Asset] ", m)
	print("[W3Sandbox] preview map: ", path)
	if path.is_empty():
		_world.load_procedural()
	else:
		_world.load_path(path)

	# Terrain exists now — push open-realm light dir into GPU terrain shader.
	_apply_lighting()

	if _world.map and _cam and _cam.has_method("focus_point"):
		var mid := Vector2i(_world.map.tile_count_x() / 2, _world.map.tile_count_y() / 2)
		var center: Vector3 = _world.sampler.tile_center_world(mid)
		var extent: float = float(maxi(_world.map.tile_count_x(), _world.map.tile_count_y())) * 128.0
		_cam.focus_point(center, extent * 0.65)
	elif _cam:
		_cam.current = true

	if _console_edit:
		_console_edit.text_submitted.connect(_on_console_submit)
		_console_edit.placeholder_text = "x,y 或 /debug"
	_set_console_open(false)
	if _dir_legend:
		_dir_legend.visible = false
	_update_hud()


func _process(_delta: float) -> void:
	if _console_open:
		return
	if not _debug_tools:
		return
	if _tile_debug != null and _tile_debug.has_method("is_debug_enabled") and _tile_debug.is_debug_enabled():
		_tile_debug.update_hover(_cam)
		_update_hud()


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
		elif not _debug_tools:
			return
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
		print("[W3Sandbox] debug ON — G=grid Tab=inspect(无光) H=soft染色 `=goto")
	else:
		_disable_all_debug_visuals()
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

	t = t.replace("，", ",").replace(" ", "")
	var parts: PackedStringArray = t.split(",")
	if parts.size() < 2:
		_console_edit.placeholder_text = "x,y 或 /debug"
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


func _apply_lighting() -> void:
	var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
	var settings = lit_settings
	if settings == null or not (settings is Resource) or settings.get_script() == null:
		settings = load("res://addons/w3kit/w3_lit_settings_preview.tres")
	if settings == null:
		settings = preload("res://addons/w3kit/w3_lit_settings.gd").new()
	_settings = settings.duplicate(true)
	_settings.fog_enabled = preview_fog and _fog_on
	W3War3Lit.apply_to_scene(self, _settings)


func _update_hud() -> void:
	if _hud == null:
		return
	var fog_state := "ON" if (_settings != null and bool(_settings.get("fog_enabled"))) else "OFF"
	var water_n := 0
	if _world and _world.get("water") != null and _world.water.has_method("get_water_tile_count"):
		water_n = int(_world.water.get_water_tile_count())
	if not _debug_tools:
		_hud.text = "%s\nF=fog(%s) `=console | wheel=zoom\nwater=%d" % [
			_world.get_status(),
			fog_state,
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
	_hud.text = "%s\n[debug] F=fog(%s) G=grid(%s) Tab=inspect(%s) H=soft(%s) `=goto\nsoft: BR红 BL绿 TR蓝 TL黄 | water=%d | look=%s | FOCUS=%s" % [
		_world.get_status(),
		fog_state,
		grid_state,
		("ON" if _inspect_mode else "OFF"),
		("ON" if _soft_tint else "OFF"),
		water_n,
		hover,
		("—" if _focused_tile.x < 0 else ("%d,%d" % [_focused_tile.x, _focused_tile.y])),
	]
