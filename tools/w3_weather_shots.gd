extends SceneTree
## Visual check for the /sun and /fog weather presets: terrain-only Lost
## Temple + player unit, one screenshot per weather into user://w3_shots/.
## Run WITHOUT --headless.

const W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
const W3LitSettings = preload("res://addons/w3kit/w3_lit_settings.gd")

var _world
var _game: Node
var _spawn := Vector3.ZERO
var _phase := 0
var _wait := 0
var _weathers := ["default", "sun", "overcast"]


func _initialize() -> void:
	root.mode = Window.MODE_WINDOWED
	root.size = Vector2i(1280, 720)
	var World = load("res://addons/w3kit/w3_map_world.gd")
	_world = World.new()
	_world.name = "W3MapWorld" # apply_to_scene syncs terrain lighting by name
	_world.terrain_only = true
	root.add_child(_world)
	call_deferred("_setup")


func _setup() -> void:
	var level = ResourceLoader.load("res://war3/maps/LostTemple/lost_temple.res", "", ResourceLoader.CACHE_MODE_IGNORE)
	if not _world.load_level_data(level):
		printerr("WEATHER_SHOT_FAILED: level load ", _world.get_status())
		quit(1)
		return
	var mid := Vector2i(_world.map.tile_count_x() / 2, _world.map.tile_count_y() / 2)
	_spawn = _world.sampler.tile_center_world(mid)
	var GameScript = load("res://scripts/game/w3_game.gd")
	_game = GameScript.new()
	root.add_child(_game)
	if not _game.setup(_world) or _game.spawn_player(_spawn) == null:
		printerr("WEATHER_SHOT_FAILED: player setup")
		quit(1)
		return
	var cam := Camera3D.new()
	cam.near = 1.0
	cam.far = 20000.0
	root.add_child(cam)
	cam.global_position = _spawn + Vector3(0.0, 1100.0, 800.0)
	cam.look_at(_spawn + Vector3(0, 60, 0))
	cam.make_current()
	_apply_weather(_weathers[0])
	process_frame.connect(_tick)


func _apply_weather(weather: String) -> void:
	var s := W3LitSettings.new()
	s.weather = weather
	s.fog_enabled = weather == "overcast"
	W3War3Lit.apply_to_scene(root, s)
	print("weather applied: ", weather)


func _tick() -> void:
	_wait += 1
	if _wait < 75:
		return
	_save_shot("weather_" + _weathers[_phase])
	if _weathers[_phase] == "overcast":
		# Horizon shot: near ground must stay clear while the far map hazes out.
		var cam := root.get_viewport().get_camera_3d()
		cam.global_position = _spawn + Vector3(0.0, 260.0, 420.0)
		cam.look_at(_spawn + Vector3(0.0, 120.0, -3500.0))
		_weathers.append("overcast_horizon")
	if _weathers[_phase] == "overcast_horizon":
		print("WEATHER_SHOT_DONE: ", ProjectSettings.globalize_path("user://w3_shots"))
		quit(0)
		return
	_phase += 1
	if _phase >= _weathers.size():
		print("WEATHER_SHOT_DONE: ", ProjectSettings.globalize_path("user://w3_shots"))
		quit(0)
		return
	if _weathers[_phase] != "overcast_horizon":
		_apply_weather(_weathers[_phase])
	_wait = 0


func _save_shot(name: String) -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("user://w3_shots"))
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(ProjectSettings.globalize_path("user://w3_shots/%s.png" % name))
	print("saved ", name)
