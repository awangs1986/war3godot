extends SceneTree
## Evidence dump for the rectangle under the debug Blademaster: list every
## geoset (vertex count, bbox, flatness) with its material layers.

const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")
const W3Paths = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")


func _initialize() -> void:
	var rel := "Units/Orc/HeroBladeMaster/HeroBladeMaster.mdx"
	var abs_path := W3Paths.new().resolve_reforged_path(rel)
	if abs_path.is_empty():
		abs_path = W3Paths.new().resolve_path(rel)
	print("model: ", abs_path)
	var model = W3MdxModel.load_file(abs_path)
	if model == null:
		printerr("load failed")
		quit(1)
		return
	print("geosets=%d materials=%d textures=%d" % [
		model.geosets.size(), model.materials.size(), model.textures.size()])
	for i in model.textures.size():
		var rid := 0
		if i < model.texture_replaceable_ids.size():
			rid = int(model.texture_replaceable_ids[i])
		print("  tex[%d] path='%s' replaceable=%d" % [i, model.textures[i], rid])
	for gi in model.geosets.size():
		var g = model.geosets[gi]
		var min_v := Vector3(1e9, 1e9, 1e9)
		var max_v := Vector3(-1e9, -1e9, -1e9)
		for v in g.vertices:
			min_v = min_v.min(v)
			max_v = max_v.max(v)
		var size := max_v - min_v
		var mid = clampi(g.material_id, 0, model.materials.size() - 1)
		var layer_desc := ""
		if not model.materials.is_empty():
			for layer in model.materials[mid]:
				layer_desc += "[fm=%d shader=%d tex=%d flags=%d] " % [
					int(layer.filter_mode), int(layer.shader),
					int(layer.texture_id), int(layer.flags)]
		print("geo %d lod=%d verts=%d faces=%d mat=%d size=(%.0f, %.0f, %.0f) ymin=%.0f %s" % [
			gi, int(g.lod), g.vertices.size(), g.faces.size() / 3, int(g.material_id),
			size.x, size.y, size.z, min_v.y, layer_desc])
	quit(0)
