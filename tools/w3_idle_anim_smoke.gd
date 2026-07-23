extends SceneTree
## godot --headless --path . -s res://tools/w3_idle_anim_smoke.gd
## Uses a classic creep model to guard the Warsmash-style Stand selection path.

const W3MdxModel = preload("res://addons/w3kit/w3_mdx_model.gd")
const W3MdxInstance = preload("res://addons/w3kit/w3_mdx_instance.gd")


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	var source := ProjectSettings.globalize_path("res://war3/game/Units/Creeps/ForestTroll/ForestTroll.mdx")
	if not FileAccess.file_exists(source):
		_fail("ForestTroll model is unavailable: %s" % source)
		return
	var model = W3MdxModel.load_file(source)
	if model == null or model.geosets.is_empty() or model.bones.is_empty():
		_fail("ForestTroll MDX did not load its renderable skeleton")
		return
	var stands: Array = []
	for sequence in model.sequences:
		var primary := str(sequence.name).split("-")[0].strip_edges().to_lower()
		if primary == "stand":
			stands.append(sequence.name)
	if stands.is_empty():
		_fail("ForestTroll has no Warsmash-style Stand sequence")
		return

	var instance = W3MdxInstance.new()
	root.add_child(instance)
	instance.setup(model, null, source, {"animate": true})
	if not instance.is_anim_active() or not stands.has(instance.get_current_sequence_name()):
		_fail("animated instance did not enter a Stand sequence")
		return
	if instance._skeleton == null or instance._skeleton.get_bone_count() != model.bones.size():
		_fail("animated instance did not build its GPU skeleton")
		return
	if instance._geoset_nodes.is_empty():
		_fail("animated instance has no render geosets")
		return
	var mesh_format := int(instance._geoset_nodes[0].mesh.surface_get_format(0))
	if (mesh_format & int(Mesh.ARRAY_FORMAT_BONES)) == 0 or (mesh_format & int(Mesh.ARRAY_FORMAT_WEIGHTS)) == 0:
		_fail("animated mesh did not retain bone weights for GPU skinning")
		return
	await process_frame
	await process_frame
	print("W3_IDLE_ANIM_OK stand=%s candidates=%d bones=%d gpu_skinning=1" % [
		instance.get_current_sequence_name(), stands.size(), model.bones.size(),
	])
	quit(0)


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
