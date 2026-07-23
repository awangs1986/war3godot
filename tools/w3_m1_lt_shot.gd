extends SceneTree
## M1 acceptance: render the full sandbox preview (terrain + cliffs + water +
## doodads/units) of a map and save a PNG once loading settles.
## Usage: godot --script res://tools/w3_m1_lt_shot.gd -- --map=<abs> --out=<png> [--wait=45]


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var out_png := "D:/temp/cursor/w3editor/m1m2/m1_lt_godot.png"
	var wait_sec := 45.0
	var focus := Vector2.INF # W3 world x/y == Godot world x/z
	var dist := 900.0
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--out="):
			out_png = arg.substr(6)
		elif arg.begins_with("--wait="):
			wait_sec = float(arg.substr(7))
		elif arg.begins_with("--focus="):
			var parts := arg.substr(8).split(",")
			if parts.size() == 2:
				focus = Vector2(float(parts[0]), float(parts[1]))
		elif arg.begins_with("--dist="):
			dist = float(arg.substr(7))
	# --map= is consumed by w3_sandbox.gd itself.
	var packed = load("res://scenes/w3_sandbox.tscn")
	var scene = packed.instantiate()
	root.add_child(scene)

	var deadline := Time.get_ticks_msec() + int(wait_sec * 1000.0)
	while Time.get_ticks_msec() < deadline:
		await process_frame
		# The sandbox flips _loading off after its stable-frame warmup.
		if not scene._loading and Time.get_ticks_msec() > 8000:
			break
	if focus != Vector2.INF and scene._world and scene._world.sampler:
		var ground_y: float = scene._world.sampler.sample_y(focus.x, focus.y)
		var target := Vector3(focus.x, ground_y, focus.y)
		var cam: Camera3D = scene.get_node("FreeFlyCamera")
		if cam.has_method("focus_point"):
			cam.focus_point(target, dist)
		else:
			cam.global_position = target + Vector3(0.35, 0.8, 0.55).normalized() * dist
			cam.look_at(target, Vector3.UP)
	# A few extra frames so deferred batches/shadows settle.
	for i in 30:
		await process_frame
	var img: Image = root.get_viewport().get_texture().get_image()
	img.save_png(out_png)
	printerr("W3_M1_SHOT: saved %s loading=%s status=%s" % [
		out_png, str(scene._loading), scene._world.get_status()])
	quit(0)
