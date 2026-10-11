extends SceneTree

const W3DooParser = preload("res://addons/w3kit_cs/Formats/W3DooParserBridge.cs")
const W3ObjectCatalog = preload("res://addons/w3kit_cs/Assets/W3ObjectCatalog.cs")
const W3Paths = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")
const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")


func _initialize() -> void:
	var level = ResourceLoader.load("res://war3/maps/LostTemple/lost_temple.res")
	if level == null:
		printerr("W3_REFORGED_MANIFEST_FAIL missing level")
		quit(1)
		return
	var catalog = W3ObjectCatalog.new()
	catalog.load_from_roots(level)
	var major := int(level.game_version_major)
	var minor := int(level.game_version_minor)
	var placements: Array = W3DooParser.parse_doodads(level.doodad_bytes, major, minor)
	placements.append_array(W3DooParser.parse_units(level.unit_bytes, major, minor))
	var counts: Dictionary = {}
	for placement in placements:
		var appearance_id := str(placement.skin_id)
		if appearance_id.length() != 4 or _contains_nul(appearance_id):
			appearance_id = str(placement.id)
		var model_path := catalog.resolve_model(appearance_id, placement.is_unit, placement.variation)
		if model_path.is_empty():
			continue
		model_path = model_path.replace("\\", "/")
		counts[model_path] = int(counts.get(model_path, 0)) + 1
	var paths: Array = counts.keys()
	paths.sort_custom(func(a, b): return str(a).to_lower() < str(b).to_lower())
	var hd_models := 0
	var classic_models := 0
	var missing_models := 0
	var parsed_hd_models := 0
	var required_textures: Dictionary = {}
	var attachment_dependencies := 0
	for path in paths:
		var hd_path := W3Paths.new().resolve_reforged_path(str(path))
		if not hd_path.is_empty():
			hd_models += 1
			var parsed_model = W3MdxModel.load_file(hd_path)
			if parsed_model != null and parsed_model.version > 800 and not parsed_model.geosets.is_empty():
				parsed_hd_models += 1
				_collect_model_textures(parsed_model, required_textures)
				for attachment in parsed_model.attachments:
					if not str(attachment.path).is_empty():
						attachment_dependencies += 1
		elif not W3Paths.new().resolve_path(str(path)).is_empty():
			classic_models += 1
		else:
			missing_models += 1
		print("CASC_MODEL\t", counts[path], "\t", path)
	for team_index in 24:
		required_textures["ReplaceableTextures/TeamColor/TeamColor%02d.dds" % team_index] = true
		required_textures["ReplaceableTextures/TeamGlow/TeamGlow%02d.dds" % team_index] = true
	var missing_textures := 0
	var invalid_textures := 0
	for rel in required_textures:
		var texture_path := W3Paths.new().resolve_reforged_path(str(rel))
		if texture_path.is_empty():
			missing_textures += 1
			printerr("CASC_TEXTURE_MISSING\t", rel)
		elif not _valid_dds(texture_path):
			invalid_textures += 1
			printerr("CASC_TEXTURE_INVALID\t", rel, "\t", texture_path)
	print("W3_REFORGED_MANIFEST_OK placements=", placements.size(), " unique_models=", paths.size(),
		" hd=", hd_models, " parsed_hd=", parsed_hd_models,
		" classic=", classic_models, " missing=", missing_models,
		" textures=", required_textures.size(), " texture_missing=", missing_textures,
		" texture_invalid=", invalid_textures, " attachment_dependencies=", attachment_dependencies)
	if hd_models != paths.size() or parsed_hd_models != paths.size() \
			or missing_textures != 0 or invalid_textures != 0:
		quit(1)
		return
	quit(0)


func _collect_model_textures(model, out: Dictionary) -> void:
	for material in model.materials:
		for layer in material:
			if int(layer.shader) == 1:
				for slot in layer.texture_ids.size():
					var rel := _hd_texture_rel(model, int(layer.texture_ids[slot]), slot)
					if not rel.is_empty() and not rel.contains("#ignore_"):
						out[rel] = true
			elif int(layer.texture_id) >= 0 and int(layer.texture_id) < model.textures.size():
				var authored := str(model.textures[int(layer.texture_id)])
				if not authored.is_empty():
					out[authored.get_basename() + ".dds"] = true
	for emitter in model.particle_emitters2:
		if int(emitter.texture_id) >= 0 and int(emitter.texture_id) < model.textures.size():
			var authored := str(model.textures[int(emitter.texture_id)])
			if not authored.is_empty():
				out[authored.get_basename() + ".dds"] = true


func _hd_texture_rel(model, texture_index: int, slot: int) -> String:
	if texture_index < 0 or texture_index >= model.textures.size():
		return ""
	var authored := str(model.textures[texture_index])
	if not authored.is_empty():
		return authored.get_basename() + ".dds"
	var rid := int(model.texture_replaceable_ids[texture_index])
	if rid == 1:
		return "ReplaceableTextures/TeamColor/TeamColor00.dds"
	if rid == 2:
		return "ReplaceableTextures/TeamGlow/TeamGlow00.dds"
	var stem := str(W3MdxModel.replaceable_stem(rid))
	if stem.is_empty():
		return "Textures/White.dds"
	var suffix := ""
	match slot:
		0: suffix = "_diffuse"
		1: suffix = "_normal"
		2: suffix = "_orm"
		3: suffix = "_emissive"
	return stem + suffix + ".dds"


func _valid_dds(path: String) -> bool:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null or file.get_length() < 128:
		return false
	var magic := file.get_buffer(4)
	return magic.size() == 4 and magic[0] == 68 and magic[1] == 68 and magic[2] == 83 and magic[3] == 32


func _contains_nul(value: String) -> bool:
	for i in value.length():
		if value.unicode_at(i) == 0:
			return true
	return false
