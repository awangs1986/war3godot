extends SceneTree

const W3Paths = preload("res://addons/w3kit/w3_paths.gd")


func _initialize() -> void:
	var requested := "Units/Human/Gyrocopter/CopterBladeBlur.png"
	var resolved := W3Paths.resolve_path(requested).replace("\\", "/")
	if resolved.is_empty() or not resolved.contains("/Units/Human/GyroCopter/CopterBladeBlur.png"):
		push_error("W3_PATH_CASE_SMOKE_FAIL %s -> %s" % [requested, resolved])
		quit(1)
		return
	var file := FileAccess.open(resolved, FileAccess.READ)
	if file == null:
		push_error("W3_PATH_CASE_SMOKE_FAIL cannot open %s" % resolved)
		quit(1)
		return
	print("W3_PATH_CASE_SMOKE_OK ", resolved)
	quit(0)
