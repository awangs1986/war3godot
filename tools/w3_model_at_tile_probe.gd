extends SceneTree

const W3DooParser = preload("res://addons/w3kit_cs/Formats/W3DooParserBridge.cs")
const W3ObjectCatalog = preload("res://addons/w3kit_cs/Assets/W3ObjectCatalog.cs")
const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")
const W3Paths = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")
const W3EMap = preload("res://addons/w3kit_cs/Formats/W3EMapBridge.cs")
const W3HeightSampler = preload("res://addons/w3kit_cs/Terrain/W3HeightSamplerBridge.cs")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var tx := int(args[0]) if args.size() > 0 else 98
	var ty := int(args[1]) if args.size() > 1 else 73
	var radius := float(args[2]) if args.size() > 2 else 512.0
	var level = ResourceLoader.load("res://war3/maps/LostTemple/lost_temple.res", "", ResourceLoader.CACHE_MODE_IGNORE)
	if level == null:
		printerr("MODEL_TILE_PROBE_FAIL level")
		quit(1)
		return
	var catalog := W3ObjectCatalog.new()
	catalog.load_from_roots(null)
	var terrain = W3EMap.load_file(ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.w3e"))
	var sampler := W3HeightSampler.new() # C# 带参构造在 GDScript 侧不可用
	sampler.bind_map(terrain)
	var placements: Array = W3DooParser.parse_doodads(level.doodad_bytes, 0, 0)
	placements.append_array(W3DooParser.parse_units(level.unit_bytes, 0, 0))
	var target := Vector2(level.center_offset.x + tx * 128.0, level.center_offset.y + ty * 128.0)
	var nearby: Array = []
	for p in placements:
		var delta := Vector2(p.position.x, p.position.z) - target
		if delta.length() <= radius:
			p.model_path = catalog.resolve_model(p.id, p.is_unit, p.variation)
			nearby.append({"p": p, "distance": delta.length()})
	nearby.sort_custom(func(a, b): return float(a.distance) < float(b.distance))
	print("MODEL_TILE_PROBE tile=%d,%d world=%s radius=%.1f count=%d" % [tx, ty, target, radius, nearby.size()])
	var inspected: Dictionary = {}
	for entry in nearby:
		var p = entry.p
		var ground_y: float = sampler.sample_y(p.position.x, p.position.z)
		print("PLACEMENT dist=%.1f id=%s skin=%s unit=%s var=%d pos=%s ground=%.3f delta=%.3f scale=%s data_scale=%.3f move_height=%.3f model=%s" % [
			entry.distance, p.id, p.skin_id, p.is_unit, p.variation, p.position,
			ground_y, p.position.y - ground_y, p.scale,
			catalog.model_scale(p.id, p.is_unit), catalog.unit_move_height(p.id) if p.is_unit else 0.0,
			p.model_path,
		])
		if p.model_path.is_empty() or inspected.has(p.model_path):
			continue
		inspected[p.model_path] = true
		var abs_path := W3Paths.new().resolve_game_path(p.model_path)
		var model = W3MdxModel.load_file(abs_path)
		if model == null:
			print("  MODEL_MISSING resolved=%s" % abs_path)
			continue
		print("  MODEL geosets=%d textures=%d materials=%d bones=%d sequences=%d stand_motion=%s emitters=%d resolved=%s" % [
			model.geosets.size(), model.textures.size(), model.materials.size(),
			model.bones.size(), model.sequences.size(), _has_stand_motion(model),
			model.particle_emitters2.size(), abs_path,
		])
		var bounds := _model_bounds(model)
		print("  BOUNDS min=%s max=%s size=%s bottom_world=%.3f" % [
			bounds.position, bounds.end, bounds.size, p.position.y + bounds.position.y * p.scale.y,
		])
		for i in model.textures.size():
			var texture_path: String = model.textures[i]
			print("  TEX %d rid=%d path=%s resolved=%s exists=%s" % [
				i, model.texture_replaceable_ids[i], texture_path,
				W3Paths.new().resolve_game_path(texture_path), FileAccess.file_exists(W3Paths.new().resolve_game_path(texture_path)),
			])
	quit(0)


func _model_bounds(model) -> AABB:
	var has_vertex := false
	var minimum := Vector3.ZERO
	var maximum := Vector3.ZERO
	for geoset in model.geosets:
		if geoset.lod != 0 and geoset.lod != -1:
			continue
		for vertex in geoset.vertices:
			var v: Vector3 = vertex
			if not has_vertex:
				minimum = v
				maximum = v
				has_vertex = true
			else:
				minimum = minimum.min(v)
				maximum = maximum.max(v)
	return AABB(minimum, maximum - minimum) if has_vertex else AABB()


func _has_stand_motion(model) -> bool:
	var sequence_index: int = int(model.find_idle_sequence())
	if sequence_index < 0 or sequence_index >= model.sequences.size():
		return false
	var sequence = model.sequences[sequence_index]
	for bone in model.bones:
		for times in [bone.translation_times, bone.rotation_times, bone.scale_times]:
			var keys_in_sequence := 0
			for time in times:
				if int(time) >= int(sequence.interval_start) and int(time) <= int(sequence.interval_end):
					keys_in_sequence += 1
					if keys_in_sequence >= 2:
						return true
	return false
