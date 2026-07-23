extends Node3D
## War3 map preview: free-fly camera + Godot lighting / fog / modern water.


@export var map_path: String = ""
@export var imported_level: Resource
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
@onready var _loading_overlay: Control = $CanvasLayer/LoadingOverlay
@onready var _loading_title: Label = $CanvasLayer/LoadingOverlay/Title
@onready var _loading_progress: ProgressBar = $CanvasLayer/LoadingOverlay/Progress
@onready var _loading_detail: Label = $CanvasLayer/LoadingOverlay/Detail
@onready var _player: Node = get_node_or_null("W3GridPlayer")
@onready var _iso: Node = get_node_or_null("IsoCamera")

var _settings: Resource
var _fog_on: bool = true
var _weather: String = "default" # /sun -> "sun", /fog -> "overcast"
var _tile_debug: Node3D
var _unit_selector: Node3D
var _game: Node # W3Game (sim + unit renderer), created after world load
var _console_open: bool = false
var _focused_tile: Vector2i = Vector2i(-1, -1)
var _inspect_mode: bool = false
var _soft_tint: bool = false
var _debug_tools: bool = false
var _saved_light_energies: Dictionary = {} # NodePath -> float
var _hud_timer: float = 0.0
var _render_scale: float = 0.77
var _lighting_lod: int = -1
var _loading: bool = true
var _load_setup_complete: bool = false
var _loading_fade_started: bool = false
var _stable_loading_frames: int = 0
var _native_smoke: bool = false


## Whole-frame dynamic resolution. At overview distance the 3D scene is
## intentionally soft; CanvasLayer HUD stays native resolution.
const ZOOM_RENDER_SCALES := [0.77, 0.58, 0.40, 0.28]
const LOADING_STABLE_FRAMES := 45


func _ready() -> void:
	_native_smoke = OS.get_environment("D1MAP_SMOKE") == "1"
	_begin_loading_screen()
	# The overlay must reach the display before synchronous terrain/cliff setup.
	await get_tree().process_frame
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
	# Mobile map units run in the simulation layer (live monsters), not as
	# static batched statues.
	_world.simulate_units = not terrain_only
	_apply_lighting()
	# Keep dynamic GI and volumetrics off while runtime terrain and thousands of
	# HD instances are being constructed. They are enabled after camera focus.
	var W3War3LitBootstrap = preload("res://addons/w3kit/w3_war3_lit.gd")
	W3War3LitBootstrap.apply_quality_lod(self, 3, _settings)
	_lighting_lod = 3

	var path := map_path
	if path.begins_with("res://") or path.begins_with("user://"):
		path = ProjectSettings.globalize_path(path)
	# External editor (standalone/W3Editor) preview: `-- --map=<abs path>`.
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--map="):
			path = arg.substr(6)
			if path.begins_with("res://") or path.begins_with("user://"):
				path = ProjectSettings.globalize_path(path)
		elif arg == "--embedded":
			# Grafted into the external editor: fill the host area instead of
			# letterboxing to the project's 16:9 design resolution.
			get_window().content_scale_aspect = Window.CONTENT_SCALE_ASPECT_EXPAND
			# WS_CHILD windows never get Win32 activation, which silently kills
			# MOUSE_MODE_CAPTURED — use the camera's warp-based look emulation.
			if _cam and _cam.get("emulate_capture") != null:
				_cam.emulate_capture = true
		elif arg == "--native-smoke":
			_native_smoke = true
	if path.is_empty():
		path = W3AssetConfig.default_map_path
	var report: Dictionary = W3AssetConfig.check_report()
	for m in report["messages"]:
		print("[W3Asset] ", m)
	print("[W3Sandbox] preview map: ", path)
	_set_loading_visual(0.08, "正在读取地图和地形")
	await get_tree().process_frame
	var loaded_ok := false
	if imported_level != null:
		loaded_ok = _world.load_level_data(imported_level)
	elif path.is_empty():
		loaded_ok = _world.load_procedural()
	else:
		loaded_ok = _world.load_path(path)
	if not loaded_ok:
		_show_loading_error(_world.get_status())
		if _native_smoke:
			get_tree().quit(1)
		return
	if _native_smoke:
		print("[D1MapSmoke] PASS ", _world.get_status())
		get_tree().quit(0)
		return
	_set_loading_visual(0.22, "地形完成，正在载入高清模型")
	if not terrain_only:
		_start_game_layer()

	# Terrain exists now — push open-realm light dir into GPU terrain shader.
	_apply_lighting()

	if _world.map and _cam and _cam.has_method("focus_point"):
		var mid := Vector2i(_world.map.tile_count_x() / 2, _world.map.tile_count_y() / 2)
		var center: Vector3 = _world.sampler.tile_center_world(mid)
		var extent: float = float(maxi(_world.map.tile_count_x(), _world.map.tile_count_y())) * 128.0
		# Large maps should start close enough that the preview LOD shows nearby
		# props and units instead of presenting an apparently empty whole-map shot.
		_cam.focus_point(center, clampf(extent * 0.30, 1200.0, 4200.0))
	elif _cam:
		_cam.current = true
	# Match the eventual render resolution now, but defer shadows/GI until all
	# streamed model batches are ready. The stable-frame phase warms them once.
	_apply_zoom_render_scale(false)

	if _console_edit:
		_console_edit.text_submitted.connect(_on_console_submit)
		_console_edit.placeholder_text = "x,y 或 /debug /sun /fog"
	_ensure_unit_selector()
	_set_console_open(false)
	if _dir_legend:
		_dir_legend.visible = false
	_update_hud()
	_load_setup_complete = true


func _process(delta: float) -> void:
	if _loading:
		_update_loading_screen()
		return
	_hud_timer += delta
	if _hud_timer >= 0.25:
		_hud_timer = 0.0
		_apply_zoom_render_scale()
		_update_hud()
	if _console_open:
		return
	if not _debug_tools:
		return
	if _tile_debug != null and _tile_debug.has_method("is_debug_enabled") and _tile_debug.is_debug_enabled():
		_tile_debug.update_hover(_cam)
		_update_hud()


func _input(event: InputEvent) -> void:
	if _loading:
		return
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
	if _loading:
		return
	if _console_open:
		return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		# The player unit owns LMB while it exists: attack enemies, move otherwise.
		if _game != null and _game.has_live_player():
			_game.issue_player_order(_cam, event.position)
			_update_hud()
			get_viewport().set_input_as_handled()
			return
		_ensure_unit_selector()
		if _unit_selector != null and _unit_selector.has_method("pick"):
			_unit_selector.pick(_cam, event.position)
			_update_hud()
			get_viewport().set_input_as_handled()
			return
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_F:
			_fog_on = not _fog_on
			_apply_lighting()
			if _inspect_mode and _world and _world.terrain and _world.terrain.has_method("set_flat_inspect"):
				_world.terrain.set_flat_inspect(true)
			if _inspect_mode and _world and _world.cliffs and _world.cliffs.has_method("set_flat_inspect"):
				_world.cliffs.set_flat_inspect(true)
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
		elif event.keycode == KEY_T:
			_spawn_player_at_mouse()
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
	if _world and _world.cliffs and _world.cliffs.has_method("set_flat_inspect"):
		_world.cliffs.set_flat_inspect(_inspect_mode)
	_update_hud()


func _set_debug_tools(on: bool) -> void:
	_debug_tools = on
	if on:
		_ensure_tile_debug()
		print("[W3Sandbox] debug ON — G=grid Tab=inspect(无光) H=soft染色 T=剑圣 LMB=移动 `=goto")
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
	if _game != null and _game.has_live_player():
		_game.sim.remove_unit(_game.player)
		_game.player = null
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
	if cmd == "/sun":
		# Noon sunshine with hard shadows; repeat returns to the default look.
		_weather = "default" if _weather == "sun" else "sun"
		_fog_on = false
		_apply_lighting()
		print("[W3Sandbox] weather=", _weather)
		_console_edit.clear()
		_set_console_open(false)
		_update_hud()
		return
	if cmd == "/fog":
		# Soft overcast + distance fog (near stays clear); repeat turns it off.
		var on := _weather != "overcast"
		_weather = "overcast" if on else "default"
		_fog_on = on
		_apply_lighting()
		print("[W3Sandbox] weather=", _weather)
		_console_edit.clear()
		_set_console_open(false)
		_update_hud()
		return

	t = t.replace("，", ",").replace(" ", "")
	var parts: PackedStringArray = t.split(",")
	if parts.size() < 2:
		_console_edit.placeholder_text = "x,y 或 /debug /sun /fog"
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


## Game layer: map monsters become live simulated units at load time; the
## player unit is spawned on demand (T in debug mode).
func _start_game_layer() -> void:
	if _game != null:
		return
	var script: GDScript = load("res://scripts/game/w3_game.gd")
	_game = script.new()
	_game.name = "W3Game"
	add_child(_game)
	if not _game.setup(_world):
		_game.queue_free()
		_game = null
		push_error("[W3Sandbox] game layer setup failed")


func _spawn_player_at_mouse() -> void:
	if _game == null or _world == null or _world.map == null or _world.sampler == null:
		return
	var hit = _world.pick_ground(_cam, get_viewport().get_mouse_position())
	if hit == null:
		# No terrain under the cursor: fall back to the map center.
		var mid := Vector2i(_world.map.tile_count_x() / 2, _world.map.tile_count_y() / 2)
		hit = _world.sampler.tile_center_world(mid)
	_game.spawn_player(hit)
	print("[W3Sandbox] player at %s | LMB=移动/攻击" % str(hit))
	_update_hud()


func _ensure_unit_selector() -> void:
	if _unit_selector != null:
		return
	if _world == null or _world.map == null or _world.sampler == null:
		return
	var script: GDScript = load("res://scripts/w3/w3_unit_selection.gd")
	_unit_selector = script.new()
	_unit_selector.name = "W3UnitSelection"
	add_child(_unit_selector)
	_unit_selector.setup(_world)
	print("[W3Sandbox] LMB selects visible W3 units through physics colliders")


func _apply_lighting() -> void:
	var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
	var settings = lit_settings
	if settings == null or not (settings is Resource) or settings.get_script() == null:
		settings = load("res://addons/w3kit/w3_lit_settings_preview.tres")
	if settings == null:
		settings = preload("res://addons/w3kit/w3_lit_settings.gd").new()
	_settings = settings.duplicate(true)
	_settings.weather = _weather
	_settings.fog_enabled = preview_fog and _fog_on
	W3War3Lit.apply_to_scene(self, _settings)


func _update_hud() -> void:
	if _hud == null:
		return
	var fog_state := "ON" if (_settings != null and bool(_settings.get("fog_enabled"))) else "OFF"
	var water_n := 0
	if _world and _world.get("water") != null and _world.water.has_method("get_water_tile_count"):
		water_n = int(_world.water.get_water_tile_count())
	var preview_perf := "3D %d%% | static 0/0 | anim 0/0" % int(round(_render_scale * 100.0))
	if _world and _world.get("batcher") != null and _world.batcher.has_method("get_counts"):
		var counts: Dictionary = _world.batcher.get_counts()
		preview_perf = "LOD%s | 3D %d%% | static %s/%s | anim %s/%s" % [
			counts.get("lod", 0),
			int(round(_render_scale * 100.0)),
			counts.get("visible_static", 0), counts.get("ok", 0),
			counts.get("anim_active", 0), counts.get("animated", 0),
		]
	var selection_status := "LMB=select visible unit"
	if _unit_selector != null and _unit_selector.has_method("get_last_summary"):
		selection_status = "LMB=select unit | %s" % _unit_selector.get_last_summary()
	if not _debug_tools:
		_hud.text = "%s\n天气=%s F=fog(%s) `=/sun /fog | wheel=zoom | %s\nwater=%d | %s" % [
			_world.get_status(),
			_weather,
			fog_state,
			selection_status,
			water_n,
			preview_perf,
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
	var hero_state := "T=出剑圣"
	if _game != null and _game.has_live_player():
		var p = _game.player
		hero_state = "剑圣HP %d/%d LMB=移动/攻击 T=传送" % [int(p.hp), int(p.max_hp)]
	_hud.text = "%s\n[debug] F=fog(%s) G=grid(%s) Tab=inspect(%s) H=soft(%s) %s `=goto | %s\n%s | water=%d | look=%s | FOCUS=%s" % [
		_world.get_status(),
		fog_state,
		grid_state,
		("ON" if _inspect_mode else "OFF"),
		("ON" if _soft_tint else "OFF"),
		hero_state,
		selection_status,
		preview_perf,
		water_n,
		hover,
		("—" if _focused_tile.x < 0 else ("%d,%d" % [_focused_tile.x, _focused_tile.y])),
	]


func _apply_zoom_render_scale(apply_lighting: bool = true) -> void:
	if _world == null or _world.get("batcher") == null:
		return
	var lod := _camera_render_lod()
	var desired: float = float(ZOOM_RENDER_SCALES[lod])
	if not is_equal_approx(desired, _render_scale):
		_render_scale = desired
		var viewport := get_viewport()
		if viewport != null:
			viewport.scaling_3d_scale = desired
	if apply_lighting and lod != _lighting_lod:
		_lighting_lod = lod
		var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
		W3War3Lit.apply_quality_lod(self, lod, _settings)


func _camera_render_lod() -> int:
	if _cam == null:
		return 3
	if _cam.projection == Camera3D.PROJECTION_ORTHOGONAL:
		if _cam.size >= 2600.0:
			return 3
		if _cam.size >= 1500.0:
			return 2
		if _cam.size >= 800.0:
			return 1
		return 0
	# Warcraft maps do not share a Y=0 terrain baseline. Use altitude above the
	# sampled ground so lighting quality follows zoom consistently everywhere.
	var ground_y := 0.0
	if _world != null and _world.sampler != null:
		ground_y = float(_world.sampler.sample_y(_cam.global_position.x, _cam.global_position.z))
	var height := maxf(0.0, _cam.global_position.y - ground_y)
	if height >= 2800.0:
		return 3
	if height >= 1600.0:
		return 2
	if height >= 850.0:
		return 1
	return 0


func is_loading_complete() -> bool:
	return not _loading


func get_loading_progress() -> float:
	return float(_loading_progress.value) / 100.0 if _loading_progress != null else 0.0


func _begin_loading_screen() -> void:
	_loading = true
	_load_setup_complete = false
	_loading_fade_started = false
	_stable_loading_frames = 0
	if _hud:
		_hud.visible = false
	if _loading_overlay:
		_loading_overlay.visible = true
		_loading_overlay.modulate = Color.WHITE
	if _cam:
		_cam.input_blocked = true
	_set_loading_visual(0.02, "正在初始化渲染器")


func _update_loading_screen() -> void:
	if not _load_setup_complete or _loading_fade_started:
		return
	# Stream geometry at the eventual dynamic resolution, but keep the bootstrap
	# lighting tier (no shadows/GI/volumetrics) until the queues are empty.
	_apply_zoom_render_scale(false)
	if _world == null or _world.batcher == null or terrain_only:
		_apply_zoom_render_scale(true)
		_stable_loading_frames += 1
		_update_warmup_progress()
		return
	var state: Dictionary = _world.batcher.get_loading_state()
	if not bool(state.get("complete", false)):
		_stable_loading_frames = 0
		_set_loading_visual(float(state.get("progress", 0.22)), str(state.get("phase", "正在加载")))
		return
	# Enable and warm the final camera-dependent lighting only after streaming.
	_apply_zoom_render_scale(true)
	_stable_loading_frames += 1
	_update_warmup_progress()


func _update_warmup_progress() -> void:
	var warmup := clampf(float(_stable_loading_frames) / float(LOADING_STABLE_FRAMES), 0.0, 1.0)
	_set_loading_visual(0.95 + warmup * 0.05,
		"正在稳定画面 %d/%d" % [mini(_stable_loading_frames, LOADING_STABLE_FRAMES), LOADING_STABLE_FRAMES])
	if _stable_loading_frames >= LOADING_STABLE_FRAMES:
		_finish_loading_screen()


func _finish_loading_screen() -> void:
	if _loading_fade_started:
		return
	_loading_fade_started = true
	_set_loading_visual(1.0, "加载完成")
	await get_tree().process_frame
	var tween := create_tween()
	tween.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN_OUT)
	tween.tween_property(_loading_overlay, "modulate:a", 0.0, 0.35)
	await tween.finished
	_loading_overlay.visible = false
	_loading_overlay.modulate = Color.WHITE
	_loading = false
	if _hud:
		_hud.visible = true
	if _cam:
		_cam.input_blocked = false
	_update_hud()
	print("[W3Sandbox] loading complete; stable_frames=", _stable_loading_frames)


func _set_loading_visual(progress: float, detail: String) -> void:
	if _loading_progress:
		_loading_progress.value = clampf(progress, 0.0, 1.0) * 100.0
	if _loading_detail:
		_loading_detail.text = "%s  ·  %d%%" % [detail, int(round(clampf(progress, 0.0, 1.0) * 100.0))]
	if _loading_title:
		_loading_title.text = "正在进入失落神庙"


func _show_loading_error(message: String) -> void:
	_load_setup_complete = false
	if _loading_title:
		_loading_title.text = "地图加载失败"
	if _loading_detail:
		_loading_detail.text = message
	if _loading_progress:
		_loading_progress.value = 0.0
