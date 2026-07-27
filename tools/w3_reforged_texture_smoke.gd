extends SceneTree

const W3BlpDecoder = preload("res://addons/w3kit_cs/Assets/BlpDecoderBridge.cs")


func _initialize() -> void:
	var probe := Image.new()
	print("DDS_METHODS load_dds_from_buffer=", probe.has_method("load_dds_from_buffer"),
		" load_ktx_from_buffer=", probe.has_method("load_ktx_from_buffer"))
	var args := OS.get_cmdline_user_args()
	var failed := 0
	for path in args:
		var absolute := ProjectSettings.globalize_path(path) if path.begins_with("res://") else path
		var image := W3BlpDecoder.load_image(absolute)
		if image == null:
			printerr("W3_REFORGED_TEXTURE_FAIL ", path)
			failed += 1
		else:
			print("W3_REFORGED_TEXTURE_OK\t", path, "\t", image.get_width(), "x", image.get_height(),
				"\tformat=", image.get_format())
	quit(1 if failed else 0)
