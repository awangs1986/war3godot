extends SceneTree
## Full-sandbox visual evidence: load the real Lost Temple preview scene,
## wait for loading to finish, spawn the debug hero via the sandbox path and
## save RTS-angle screenshots. Run WITHOUT --headless.

var _sandbox: Node
var _phase := 0
var _wait := 0


func _initialize() -> void:
	root.mode = Window.MODE_WINDOWED
	root.size = Vector2i(1280, 720)
	var scene: PackedScene = load("res://scenes/w3_lost_temple_preview.tscn")
	_sandbox = scene.instantiate()
	root.add_child(_sandbox)
	process_frame.connect(_tick)


func _tick() -> void:
	_wait += 1
	if _wait > 60 * 300:
		printerr("SHOT_FAILED: timeout in phase ", _phase)
		quit(1)
		return
	match _phase:
		0:
			if bool(_sandbox.is_loading()):
				return
			_phase = 1
			_wait = 0
		1:
			if _wait < 120: # settle: batches + animated spawns
				return
			var world = _sandbox.get_world()
			var mid := Vector2i(world.map.tile_count_x() / 2, world.map.tile_count_y() / 2)
			var spawn: Vector3 = world.sampler.tile_center_world(mid)
			var game: Node = _sandbox.get_game()
			if game == null:
				printerr("SHOT_FAILED: sandbox game layer missing")
				quit(1)
				return
			if game.spawn_player(spawn) == null:
				printerr("SHOT_FAILED: player spawn")
				quit(1)
				return
			var cam: Camera3D = _sandbox.get_node("FreeFlyCamera")
			cam.global_position = spawn + Vector3(0.0, 900.0, 620.0)
			cam.look_at(spawn + Vector3(0, 60, 0))
			_phase = 2
			_wait = 0
		2:
			if _wait < 60:
				return
			_save_shot("full_default")
			var light: DirectionalLight3D = _sandbox.get_node_or_null("DirectionalLight3D")
			if light != null:
				light.shadow_enabled = false
			_phase = 3
			_wait = 0
		3:
			if _wait < 10:
				return
			_save_shot("full_noshadow")
			var game: Node = _sandbox.get_game()
			if game != null and game.has_live_player():
				var view: Node3D = game.renderer.view_node(game.player)
				if view != null:
					view.visible = false
			_phase = 4
			_wait = 0
		4:
			if _wait < 10:
				return
			_save_shot("full_nohero")
			print("SHOT_DONE: ", ProjectSettings.globalize_path("user://w3_shots"))
			quit(0)


func _save_shot(name: String) -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("user://w3_shots"))
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(ProjectSettings.globalize_path("user://w3_shots/%s.png" % name))
	print("saved ", name)
