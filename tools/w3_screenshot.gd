extends SceneTree
func _initialize():
	call_deferred("r")
func r():
	var packed = load("res://scenes/w3_sandbox.tscn")
	var scene = packed.instantiate()
	root.add_child(scene)
	await process_frame
	await process_frame
	await process_frame
	var world = scene.get_node("W3MapWorld")
	var units = world.get_node("MapUnits")
	var target := Vector3.ZERO
	if units.get_child_count() > 0:
		target = units.get_child(0).global_position
		# average a few
		var n = mini(5, units.get_child_count())
		var acc := Vector3.ZERO
		for i in n:
			acc += units.get_child(i).global_position
		target = acc / float(n)
	var cam = scene.get_node("FreeFlyCamera") as Camera3D
	cam.current = true
	cam.global_position = target + Vector3(120, 100, 160)
	cam.look_at(target + Vector3(0, 30, 0), Vector3.UP)
	cam.fov = 45
	await process_frame
	await process_frame
	root.get_viewport().get_texture().get_image().save_png(
		ProjectSettings.globalize_path("res://testdata/w3_preview.png"))
	print("units at ", target, " count=", units.get_child_count())
	quit(0)
