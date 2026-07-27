extends SceneTree

const W3DooParser = preload("res://addons/w3kit_cs/Formats/W3DooParserBridge.cs")
const W3ObjectCatalog = preload("res://addons/w3kit_cs/Assets/W3ObjectCatalog.cs")
const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")
const W3Paths = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var path := ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3mapUnits.doo")
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		printerr("W3_FOUNTAIN_PROBE_FAILED units file")
		quit(1)
		return
	var units: Array = W3DooParser.parse_units(file.get_buffer(file.get_length()), 0, 0)
	var catalog := W3ObjectCatalog.new()
	catalog.load_from_roots(null)
	var fountains: Array = []
	for unit in units:
		unit.model_path = catalog.resolve_model(unit.id, true, unit.variation)
		if str(unit.model_path).to_lower().contains("fountain"):
			fountains.append(unit)
	printerr("W3_FOUNTAIN_PROBE fountains=%d units=%d" % [fountains.size(), units.size()])
	for fountain in fountains:
		printerr("FOUNTAIN id=%s pos=%s model=%s" % [fountain.id, fountain.position, fountain.model_path])
		var nearby: Array = []
		for unit in units:
			if unit == fountain:
				continue
			var delta: Vector3 = unit.position - fountain.position
			var dist2 := Vector2(delta.x, delta.z).length_squared()
			if dist2 <= 1600.0 * 1600.0:
				nearby.append({"unit": unit, "dist2": dist2})
		nearby.sort_custom(func(a, b): return float(a.dist2) < float(b.dist2))
		for entry in nearby.slice(0, mini(12, nearby.size())):
			var unit = entry.unit
			printerr("  NEAR dist=%.1f id=%s var=%d pos=%s model=%s" % [
				sqrt(float(entry.dist2)), unit.id, unit.variation, unit.position, unit.model_path,
			])
	var inspected: Dictionary = {}
	for unit in units:
		if not str(unit.model_path).to_lower().contains("gnoll") or inspected.has(unit.model_path):
			continue
		inspected[unit.model_path] = true
		var abs_path := W3Paths.new().resolve_path(unit.model_path)
		var model = W3MdxModel.load_file(abs_path)
		printerr("MODEL %s geosets=%d textures=%d materials=%d" % [
			unit.model_path, model.geosets.size(), model.textures.size(), model.materials.size(),
		])
		for i in model.textures.size():
			printerr("  TEX %d rid=%d path=%s" % [i, model.texture_replaceable_ids[i], model.textures[i]])
		for i in model.materials.size():
			var desc: Array[String] = []
			for layer in model.materials[i]:
				desc.append("tex=%d filter=%d flags=0x%X" % [layer.texture_id, layer.filter_mode, layer.flags])
			printerr("  MAT %d layers=%s" % [i, desc])
		for i in model.geosets.size():
			var geoset = model.geosets[i]
			var uv_min := Vector2(1e9, 1e9)
			var uv_max := Vector2(-1e9, -1e9)
			for uv in geoset.uvs:
				uv_min = uv_min.min(uv)
				uv_max = uv_max.max(uv)
			printerr("  GEO %d mat=%d verts=%d faces=%d uv=%s..%s" % [
				i, geoset.material_id, geoset.vertices.size(), geoset.faces.size(), uv_min, uv_max,
			])
	quit(0)
