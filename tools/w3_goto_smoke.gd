extends SceneTree

const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
const W3TileDebugOverlay = preload("res://scripts/w3/w3_tile_debug_overlay.gd")


func _initialize() -> void:
	var source := ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.w3e")
	var level = W3EMap.load_file(source)
	if level == null:
		push_error("W3_GOTO_SMOKE_FAIL map missing")
		quit(1)
		return
	var sampler := W3HeightSampler.new(level)
	var center: Vector3 = sampler.tile_center_world(Vector2i(117, 56))
	var overlay := W3TileDebugOverlay.new()
	root.add_child(overlay)
	overlay.setup(level)
	if overlay._grid_mi != null:
		push_error("W3_GOTO_SMOKE_FAIL setup eagerly built the full grid")
		quit(1)
		return
	overlay.highlight_tile(Vector2i(117, 56))
	if overlay.get_focus_tile() != Vector2i(117, 56) or overlay._highlight_root.get_child_count() == 0:
		push_error("W3_GOTO_SMOKE_FAIL target highlight missing")
		quit(1)
		return
	if overlay._grid_mi != null:
		push_error("W3_GOTO_SMOKE_FAIL goto built the full grid")
		quit(1)
		return
	print("W3_GOTO_SMOKE_OK 117,56 -> ", center)
	quit(0)
