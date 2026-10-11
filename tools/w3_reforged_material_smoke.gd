extends SceneTree

const W3MdxInstance = preload("res://addons/w3kit_cs/Mdx/W3MdxInstance.cs")
const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")
const W3MdxBatcher = preload("res://addons/w3kit_cs/Mdx/W3MdxBatcher.cs")
const W3TextureProvider = preload("res://addons/w3kit_cs/Assets/W3TextureProvider.cs")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var root_path := ProjectSettings.globalize_path("res://war3/reforged")
	var model_path := root_path.path_join("Units/Human/Footman/Footman.mdx")
	var model = W3MdxModel.load_file(model_path)
	if model == null or model.version != 1000 or model.materials.size() != 9:
		_fail("v1000 model/material parse failed")
		return
	var first_layer = model.materials[0][0]
	if first_layer.shader != 1 or first_layer.texture_ids.size() != 6:
		_fail("Shader_HD_DefaultUnit did not merge six PBR slots")
		return
	var first_geoset = model.geosets[0]
	if first_geoset.tangents.size() != first_geoset.vertices.size() * 4:
		_fail("TANG count mismatch")
		return
	if first_geoset.bone_indices.size() != first_geoset.vertices.size() * 4:
		_fail("SKIN count mismatch")
		return
	var parsed_geoset_count: int = model.geosets.size()
	var render_geoset_count := 0
	var static_render_geoset_count := 0
	var stand_index: int = int(model.find_idle_sequence())
	var stand_time := int(model.sequences[stand_index].interval_start) if stand_index >= 0 else 0
	for geoset in model.geosets:
		if geoset.lod == 0 or geoset.lod == -1:
			render_geoset_count += 1
	for geoset_id in model.geosets.size():
		var geoset = model.geosets[geoset_id]
		if (geoset.lod == 0 or geoset.lod == -1) and model.geoset_alpha_at(geoset_id, stand_time) > 0.01:
			static_render_geoset_count += 1
	if render_geoset_count <= 0 or render_geoset_count >= parsed_geoset_count:
		_fail("Reforged LOD fields were not parsed")
		return
	var provider := W3TextureProvider.new()
	provider.set_map_dir(root_path)
	var instance := W3MdxInstance.new()
	root.add_child(instance)
	instance.setup(model, provider, model_path, {"team_color": 0})
	if instance._geoset_nodes.size() != render_geoset_count:
		_fail("HD base LOD selection mismatch")
		return
	var material = instance._geoset_nodes[0].material_override
	if not (material is ShaderMaterial):
		_fail("HD geoset did not use the Reforged shader")
		return
	for parameter in ["diffuse_map", "normal_map", "orm_map", "emissive_map", "team_color_map", "environment_map"]:
		var texture = material.get_shader_parameter(parameter)
		if not (texture is Texture2D) or texture.get_width() <= 2:
			_fail("HD texture slot missing: " + parameter)
			return
	var batcher := W3MdxBatcher.new()
	root.add_child(batcher)
	batcher.textures = provider
	var static_bank: Array = batcher._build_geoset_meshes(model, "")
	if static_bank.size() != static_render_geoset_count or not (static_bank[0].mat is ShaderMaterial):
		_fail("static HD batching did not retain the Reforged material")
		return
	if not str((static_bank[0].mat as ShaderMaterial).shader.resource_path).ends_with("w3_reforged_unit_opaque.gdshader"):
		_fail("filter mode 0/1 did not use the depth-writing Reforged shader")
		return
	if (int(static_bank[0].mesh.surface_get_format(0)) & int(Mesh.ARRAY_FORMAT_TANGENT)) == 0:
		_fail("static HD batching dropped TANG data")
		return
	print("W3_REFORGED_MATERIAL_OK version=", model.version,
		" geosets=", parsed_geoset_count, " rendered_lod0=", render_geoset_count,
		" materials=", model.materials.size(),
		" slots=", first_layer.texture_ids)
	instance.queue_free()
	batcher.queue_free()
	await process_frame
	quit(0)


func _fail(message: String) -> void:
	printerr("W3_REFORGED_MATERIAL_FAIL ", message)
	quit(1)
