extends SceneTree
## Regression for Lost Temple's two-tile ramp transition at 70,118.
## Warsmash moves the authored transition one cell down, where it owns both
## the source and neighbour ground holes, then uses CliffTransBHLA0.
## the neighbouring 71,118 tile is the full-ramp ground entrance.

const W3CliffBuilder = preload("res://addons/w3kit/w3_cliff_builder.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var source := ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.w3e")
	var level = W3EMap.load_file(source)
	if level == null:
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL map resource missing")
		quit(1)
		return
	var builder := W3CliffBuilder.new()
	root.add_child(builder)
	builder.map = level
	var provider := W3TextureProvider.new()
	provider.load_terrain_slk()
	if provider.get_cliff_ground_tile("CLgr") != "Lgrs":
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL CLgr ground mapping is %s" % provider.get_cliff_ground_tile("CLgr"))
		quit(1)
		return
	builder.textures = provider
	builder._load_cliff_types()
	var transition: Dictionary = level.get_ramp_transition(70, 118)
	if str(transition.get("tag", "")) != "BHLA" or transition.get("placement", Vector2i(-1, -1)) != Vector2i(70, 117):
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL bad BHLA placement: %s" % transition)
		quit(1)
		return
	if not level.ramp_transition_covers_tile(70, 118) or not level.ramp_transition_covers_tile(70, 117):
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL ramp ground footprint missing")
		quit(1)
		return
	builder._try_spawn_cliff(70, 118)
	var node: Node3D = builder._nodes_by_tile.get(Vector2i(70, 117), null)
	if node == null:
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL no transition at 70,118")
		quit(1)
		return
	var expected := "Doodads/Terrain/CliffTrans/CliffTransBHLA0.mdx"
	if str(node.get_meta("w3_model_rel", "")) != expected:
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL got %s" % str(node.get_meta("w3_model_rel", "")))
		quit(1)
		return
	var hbal: Dictionary = level.get_ramp_transition(83, 65)
	if str(hbal.get("tag", "")) != "HBAL" or hbal.get("placement", Vector2i(-1, -1)) != Vector2i(83, 64):
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL bad HBAL placement: %s" % hbal)
		quit(1)
		return
	if not level.ramp_transition_covers_tile(83, 65) or not level.ramp_transition_covers_tile(83, 64):
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL HBAL ground footprint missing")
		quit(1)
		return
	# 44,65 is the horizontal counterpart: BALH covers the authored tile and
	# its right neighbour with one opaque 256x128 CliffTrans model.
	var balh: Dictionary = level.get_ramp_transition(44, 65)
	if str(balh.get("tag", "")) != "BALH" or balh.get("placement", Vector2i(-1, -1)) != Vector2i(44, 65):
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL bad BALH placement: %s" % balh)
		quit(1)
		return
	if not level.ramp_transition_covers_tile(44, 65) or not level.ramp_transition_covers_tile(45, 65):
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL BALH ground footprint missing")
		quit(1)
		return
	builder._try_spawn_cliff(44, 65)
	var balh_node: Node3D = builder._nodes_by_tile.get(Vector2i(44, 65), null)
	if balh_node == null or str(balh_node.get_meta("w3_model_rel", "")) != "Doodads/Terrain/CliffTrans/CliffTransBALH0.mdx":
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL no BALH model at 44,65")
		quit(1)
		return
	if not level.is_ramp_entrance(71, 118):
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL 71,118 is not a ramp entrance")
		quit(1)
		return
	# 117,56 is a flat CLgr-adjacent tile. HiveWE replaces its four effective
	# corner textures with CliffTypes.groundTile=Lgrs, not tileset index 0 dirt.
	var terrain := preload("res://addons/w3kit/w3_gpu_terrain.gd").new()
	terrain.map = level
	terrain.textures = provider
	terrain._layer_ids = level.ground_tilesets.duplicate()
	terrain._prepare_blight()
	terrain._prepare_cliff_to_ground()
	terrain._prepare_romp_tiles()
	for corner_pos in [Vector2i(117, 56), Vector2i(118, 56), Vector2i(117, 57), Vector2i(118, 57)]:
		if terrain._corner_tex(corner_pos.x, corner_pos.y) != 5:
			push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL 117,56 cliff-ground corner %s mapped to %d" % [corner_pos, terrain._corner_tex(corner_pos.x, corner_pos.y)])
			quit(1)
			return
	var low_corner = level.get_corner(71, 118)
	var lift: float = float(level.corner_world_y(71, 118) - low_corner.world_y())
	if not is_equal_approx(lift, 64.0):
		push_error("W3_CLIFF_TRANSITION_SMOKE_FAIL expected 64 lift, got %.3f" % lift)
		quit(1)
		return
	print("W3_CLIFF_TRANSITION_SMOKE_OK ", expected)
	quit(0)
