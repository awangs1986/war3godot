extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var path := ProjectSettings.globalize_path("res://war3/reforged/Units/Creeps/GnollWarden/GnollWarden.mdx")
	print("path=", path, " exists=", FileAccess.file_exists(path))
	var model = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs").load_file(path)
	if model == null:
		print("MODEL_NULL")
		quit(1)
		return
	print("geosets=", model.geosets.size(), " bones=", model.bones.size(), " pre2=", model.particle_emitters2.size())
	var f := FileAccess.open(path, FileAccess.READ)
	var buf := f.get_buffer(f.get_length())
	var idx := -1
	for i in buf.size() - 4:
		if buf[i] == 80 and buf[i + 1] == 82 and buf[i + 2] == 69 and buf[i + 3] == 50:
			idx = i
			break
	print("PRE2 tag byte offset=", idx, " file_size=", buf.size())
	if idx >= 0:
		var chunk_size := buf.decode_u32(idx + 4)
		print("PRE2 chunk_size(payload after tag+size)=", chunk_size, " end=", idx + 8 + chunk_size)
		var node_size := buf.decode_u32(idx + 8)
		print("first item inclusive_size=", node_size)
	quit(0)
