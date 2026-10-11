extends SceneTree

const W3Paths = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")
const W3TerrainSlk = preload("res://addons/w3kit_cs/Formats/W3TerrainSlkBridge.cs")
const W3MapWorld = preload("res://addons/w3kit_cs/World/W3MapWorld.cs")
const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var level = ResourceLoader.load("res://war3/maps/LostTemple/lost_temple.res")
	if level == null:
		printerr("W3_REFORGED_TERRAIN_FAIL missing level")
		quit(1)
		return
	var terrain_slk = W3TerrainSlk.new()
	terrain_slk.load_from_asset_root(W3Paths.new().asset_root_path())
	print("TERRAIN_TILESET\t", level.tileset)
	var errors := 0
	for tile_id in level.ground_tilesets:
		var classic_rel := terrain_slk.resolve_blp_rel(str(tile_id)).replace("\\", "/")
		var hd_rel := classic_rel.get_basename() + "_diffuse.dds"
		print("TERRAIN_GROUND\t", tile_id, "\t", classic_rel, "\t", hd_rel,
			"\t", W3Paths.new().resolve_reforged_path(hd_rel))
		if not _valid_dds(W3Paths.new().resolve_reforged_path(hd_rel)):
			errors += 1
	for cliff_id in level.cliff_tilesets:
		print("TERRAIN_CLIFF_ID\t", cliff_id)
	var world = W3MapWorld.new()
	world.terrain_only = true
	root.add_child(world)
	if not world.load_path(ProjectSettings.globalize_path("res://war3/maps/LostTemple/lost_temple.res")):
		printerr("W3_REFORGED_TERRAIN_FAIL runtime load: ", world.get_status())
		quit(1)
		return
	var cliff_models: Dictionary = {}
	var cliff_texture_fallbacks := 0
	var cliff_batches := 0
	for child in world.cliffs.get_children():
		if bool(child.get_meta("w3_cliff_batch", false)):
			cliff_batches += 1
			var batch_texture_rel := str(child.get_meta("w3_texture_rel", ""))
			var found_batch_texture := false
			if child is MeshInstance3D and child.material_override is ShaderMaterial:
				var batch_texture = (child.material_override as ShaderMaterial).get_shader_parameter("albedo_tex")
				found_batch_texture = (
					batch_texture is Texture2D
					and batch_texture.get_width() > 64
					and batch_texture.get_height() > 64
				)
			if batch_texture_rel.is_empty() or W3Paths.new().resolve_game_path(batch_texture_rel).is_empty() or not found_batch_texture:
				cliff_texture_fallbacks += 1
			continue
		var rel := str(child.get_meta("w3_model_rel", ""))
		if not rel.is_empty():
			cliff_models[rel] = int(cliff_models.get(rel, 0)) + 1
		var texture_rel := str(child.get_meta("w3_texture_rel", ""))
		if texture_rel.is_empty() or W3Paths.new().resolve_game_path(texture_rel).is_empty():
			cliff_texture_fallbacks += 1
	if cliff_batches <= 0:
		printerr("TERRAIN_CLIFF_BATCHES_MISSING")
		errors += 1
	if cliff_texture_fallbacks != 0:
		printerr("TERRAIN_CLIFF_TEXTURE_FALLBACKS\t", cliff_texture_fallbacks)
		errors += cliff_texture_fallbacks
	var model_paths: Array = cliff_models.keys()
	model_paths.sort_custom(func(a, b): return str(a).to_lower() < str(b).to_lower())
	for rel in model_paths:
		var hd_model_path := W3Paths.new().resolve_reforged_path(str(rel))
		print("TERRAIN_CLIFF_MODEL\t", cliff_models[rel], "\t", rel,
			"\t", hd_model_path)
		var cliff_model = W3MdxModel.load_file(hd_model_path) if not hd_model_path.is_empty() else null
		if cliff_model == null or cliff_model.version <= 800 or cliff_model.geosets.is_empty():
			errors += 1
	for shared_cliff in ["ReplaceableTextures/Cliff/Cliff0.dds", "ReplaceableTextures/Cliff/Cliff1.dds"]:
		if not _valid_dds(W3Paths.new().resolve_reforged_path(shared_cliff)):
			errors += 1
	var blight := "TerrainArt/Blight/Lords_Blight_diffuse.dds"
	if not _valid_dds(W3Paths.new().resolve_reforged_path(blight)):
		errors += 1
	var water_missing := 0
	for frame in 45:
		var water_rel := "ReplaceableTextures/Water/Water%02d.blp" % frame
		if W3Paths.new().resolve_path(water_rel).is_empty():
			water_missing += 1
			errors += 1
	print("W3_REFORGED_TERRAIN_OK ground=", level.ground_tilesets.size(),
		" cliffs=", level.cliff_tilesets.size(), " cliff_models=", model_paths.size(),
		" cliff_batches=", cliff_batches, " cliff_texture_fallbacks=", cliff_texture_fallbacks,
		" shared_water_frames=", 45 - water_missing, " errors=", errors)
	quit(0 if errors == 0 else 1)


func _valid_dds(path: String) -> bool:
	if path.is_empty():
		return false
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null or file.get_length() < 128:
		return false
	var magic := file.get_buffer(4)
	return magic.size() == 4 and magic[0] == 68 and magic[1] == 68 and magic[2] == 83 and magic[3] == 32
