extends SceneTree

const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")
const W3MdxInstance = preload("res://addons/w3kit_cs/Mdx/W3MdxInstance.cs")
const W3TextureProvider = preload("res://addons/w3kit_cs/Assets/W3TextureProvider.cs")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var path := ProjectSettings.globalize_path("res://war3/game/Units/Creeps/GnollArcher/GnollArcher.mdx")
	var model = W3MdxModel.load_file(path)
	var provider := W3TextureProvider.new()
	var instance := W3MdxInstance.new()
	root.add_child(instance)
	instance.setup(model, provider, path, {"team_color": 0})
	if instance._geoset_nodes.size() < 2:
		_fail("GnollArcher geosets were not instantiated")
		return
	var base = instance._geoset_nodes[1].material_override
	if not (base is StandardMaterial3D) or base.next_pass == null:
		_fail("GnollArcher team-color/body material did not form a two-pass chain")
		return
	var overlay = base.next_pass
	if not (overlay is StandardMaterial3D):
		_fail("GnollArcher body overlay is not a StandardMaterial3D")
		return
	if base.albedo_texture == null or overlay.albedo_texture == null:
		_fail("GnollArcher material chain is missing a texture")
		return
	if overlay.transparency != BaseMaterial3D.TRANSPARENCY_ALPHA:
		_fail("GnollArcher body overlay did not preserve MDX Blend filter mode")
		return
	print("W3_GNOLL_MATERIAL_SMOKE_OK passes=2 base=%s overlay=%s" % [
		base.albedo_texture.get_size(), overlay.albedo_texture.get_size(),
	])
	quit(0)


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
