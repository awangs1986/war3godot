extends SceneTree

const W3BlpDecoder = preload("res://addons/w3kit/w3_blp_decoder.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")
const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")


func _initialize() -> void:
	var path := ProjectSettings.globalize_path(
		"res://war3/game/Doodads/Cityscape/Props/WhaleStatue/killerwhalestone.blp"
	)
	var image := W3BlpDecoder.load_image(path)
	if image == null or image.get_size() != Vector2i(256, 128):
		printerr("WHALE_TEXTURE_FAIL image=", image)
		quit(1)
		return
	var provider := W3TextureProvider.new()
	var texture := provider.get_texture("Doodads/Cityscape/Props/WhaleStatue/killerwhalestone.blp")
	if texture == null or texture.get_size() != Vector2(256, 128):
		printerr("WHALE_TEXTURE_FAIL texture=", texture)
		quit(1)
		return
	var model_path := ProjectSettings.globalize_path(
		"res://war3/game/Doodads/Cityscape/Props/WhaleStatue/WhaleStatue.mdx"
	)
	var instance := W3MdxInstance.new()
	root.add_child(instance)
	instance.setup(W3MdxModel.load_file(model_path), provider, model_path)
	if instance._geoset_nodes.size() != 1:
		printerr("WHALE_TEXTURE_FAIL geosets=", instance._geoset_nodes.size())
		quit(1)
		return
	var material = instance._geoset_nodes[0].material_override
	if not (material is StandardMaterial3D) or material.albedo_texture == null \
			or material.albedo_texture.get_size() != Vector2(256, 128):
		printerr("WHALE_TEXTURE_FAIL material=", material)
		quit(1)
		return
	print("WHALE_TEXTURE_OK size=", image.get_size(), " sample=", image.get_pixel(128, 64))
	quit(0)
