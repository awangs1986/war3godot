extends SceneTree

const W3MdxInstance = preload("res://addons/w3kit_cs/Mdx/W3MdxInstance.cs")
const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")
const W3TextureProvider = preload("res://addons/w3kit_cs/Assets/W3TextureProvider.cs")


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var hd_root := ProjectSettings.globalize_path("res://war3/reforged")
	var provider := W3TextureProvider.new()
	provider.set_map_dir(hd_root)
	var gnoll_path := hd_root.path_join("Units/Creeps/GnollWarden/GnollWarden.mdx")
	var gnoll = W3MdxModel.load_file(gnoll_path)
	if gnoll == null or gnoll.particle_emitters2.size() != 1:
		_fail("GnollWarden HD PRE2 parse failed")
		return
	var gnoll_instance := W3MdxInstance.new()
	root.add_child(gnoll_instance)
	gnoll_instance.setup(gnoll, provider, gnoll_path, {"animate": true, "proximity_gate": true})
	if gnoll_instance._particle_nodes.size() != 1:
		_fail("PRE2 did not create a particle node")
		return
	var particle: GPUParticles3D = gnoll_instance._particle_nodes[0]["node"]
	var quad := particle.draw_pass_1 as QuadMesh
	var particle_texture = quad.material.albedo_texture if quad and quad.material else null
	if not (particle_texture is Texture2D) or particle_texture.get_width() <= 2:
		_fail("PRE2 did not resolve its Reforged DDS texture")
		return

	var mine_path := hd_root.path_join("Buildings/Other/GoldMine/GoldMine.mdx")
	var mine = W3MdxModel.load_file(mine_path)
	if mine == null or mine.lights.size() != 2:
		_fail("GoldMine HD LITE parse failed")
		return
	var mine_instance := W3MdxInstance.new()
	root.add_child(mine_instance)
	mine_instance.setup(mine, provider, mine_path, {"animate": true, "proximity_gate": true})
	if mine_instance._light_nodes.size() != 2:
		_fail("GoldMine LITE nodes were not instantiated")
		return
	mine_instance._anim_time = 1000.0
	mine_instance._anim_active = true
	var sequence = mine.sequences[mine_instance._seq_index]
	var sample_time := int(sequence.interval_start + mine_instance._anim_time)
	var worlds := preload("res://addons/w3kit_cs/Mdx/W3MdxAnim.cs").compute_world_matrices(
		mine.bones, sample_time, sequence.interval_start, sequence.interval_end)
	mine_instance._update_model_effects(
		worlds, sample_time, sequence.interval_start, sequence.interval_end)
	var lit := 0
	for entry in mine_instance._light_nodes:
		if float(entry["node"].light_energy) > 0.001:
			lit += 1
	if lit == 0:
		_fail("GoldMine animated light intensity was not sampled")
		return
	print("W3_REFORGED_EFFECT_OK particles=1 particle_texture=", particle_texture.get_width(),
		"x", particle_texture.get_height(), " lights=", mine_instance._light_nodes.size(), " lit=", lit)
	gnoll_instance.queue_free()
	mine_instance.queue_free()
	await process_frame
	quit(0)


func _fail(message: String) -> void:
	printerr("W3_REFORGED_EFFECT_FAIL ", message)
	quit(1)
