extends SceneTree
## Visual evidence tool: terrain-only Lost Temple + simulated Blademaster.
## Saves user://w3_shots/{hero_default,hero_noshadow,no_hero}.png.
## Run WITHOUT --headless.

var _world
var _game: Node
var _light: DirectionalLight3D
var _phase := 0
var _wait := 0


func _initialize() -> void:
	root.mode = Window.MODE_WINDOWED
	root.size = Vector2i(1280, 720)
	var World = load("res://addons/w3kit_cs/World/W3MapWorld.cs")
	_world = World.new()
	_world.terrain_only = true
	root.add_child(_world)
	call_deferred("_setup")


func _setup() -> void:
	var level = ResourceLoader.load("res://war3/maps/LostTemple/lost_temple.res", "", ResourceLoader.CACHE_MODE_IGNORE)
	if not _world.load_level_data(level):
		printerr("SHOT_FAILED: level load ", _world.get_status())
		quit(1)
		return

	_light = DirectionalLight3D.new()
	_light.transform = Transform3D(Basis(Vector3(0.866, 0, -0.5), Vector3(-0.353, 0.707, -0.612), Vector3(0.353, 0.707, 0.612)), Vector3(0, 2000, 0))
	_light.shadow_enabled = true
	_light.light_energy = 1.05
	root.add_child(_light)

	var mid := Vector2i(_world.map.tile_count_x() / 2, _world.map.tile_count_y() / 2)
	var spawn: Vector3 = _world.sampler.tile_center_world(mid)
	var GameScript = load("res://addons/w3kit_cs/Game/W3GameRuntime.cs")
	_game = GameScript.new()
	root.add_child(_game)
	if not _game.setup(_world) or _game.spawn_player(spawn) == null:
		printerr("SHOT_FAILED: game/player setup")
		quit(1)
		return

	var cam := Camera3D.new()
	cam.near = 1.0
	cam.far = 20000.0
	root.add_child(cam)
	# WC3-like RTS angle: high above, tilted ~55 degrees.
	cam.global_position = spawn + Vector3(0.0, 900.0, 620.0)
	cam.look_at(spawn + Vector3(0, 60, 0))
	cam.make_current()
	process_frame.connect(_tick)


func _tick() -> void:
	_wait += 1
	match _phase:
		0:
			if _wait < 60:
				return
			_save_shot("hero_default")
			_light.shadow_enabled = false
			_phase = 1
			_wait = 0
		1:
			if _wait < 10:
				return
			_save_shot("hero_noshadow")
			_light.shadow_enabled = true
			var view: Node3D = _game.renderer.view_node(_game.player)
			if view != null:
				view.visible = false
			_phase = 2
			_wait = 0
		2:
			if _wait < 10:
				return
			_save_shot("no_hero")
			print("SHOT_DONE: ", ProjectSettings.globalize_path("user://w3_shots"))
			quit(0)


func _save_shot(name: String) -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("user://w3_shots"))
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(ProjectSettings.globalize_path("user://w3_shots/%s.png" % name))
	print("saved ", name)
