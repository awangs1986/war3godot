extends SceneTree

const W3EMap = preload("res://addons/w3kit_cs/Formats/W3EMapBridge.cs")
const W3HeightSampler = preload("res://addons/w3kit_cs/Terrain/W3HeightSamplerBridge.cs")
const W3TileDebugOverlay = preload("res://addons/w3kit_cs/Sandbox/W3TileDebugOverlay.cs")


func _initialize() -> void:
	var source := ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.w3e")
	var level = W3EMap.load_file(source)
	if level == null:
		push_error("W3_GOTO_SMOKE_FAIL map missing")
		quit(1)
		return
	var sampler := W3HeightSampler.new() # C# 带参构造无法从 GDScript 调用，改 bind_map
	sampler.bind_map(level)
	var center: Vector3 = sampler.tile_center_world(Vector2i(117, 56))
	var overlay := W3TileDebugOverlay.new()
	root.add_child(overlay)
	overlay.setup(level)
	if overlay.has_grid():
		push_error("W3_GOTO_SMOKE_FAIL setup eagerly built the full grid")
		quit(1)
		return
	overlay.highlight_tile(Vector2i(117, 56))
	if overlay.get_focus_tile() != Vector2i(117, 56) or overlay.highlight_child_count() == 0:
		push_error("W3_GOTO_SMOKE_FAIL target highlight missing")
		quit(1)
		return
	if overlay.has_grid():
		push_error("W3_GOTO_SMOKE_FAIL goto built the full grid")
		quit(1)
		return
	print("W3_GOTO_SMOKE_OK 117,56 -> ", center)
	quit(0)
