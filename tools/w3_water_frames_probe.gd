extends SceneTree
## Which decode path do the red frames take? Print resolved path, sidecar PNG
## existence, jpg cache key, and cache-file color for frames 02..05.

const W3Paths = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")


func _initialize() -> void:
	var cache_dir := ProjectSettings.globalize_path("user://w3_blp_cache")
	print("cache dir: ", cache_dir)
	for frame in [2, 3, 4, 5, 27, 30]:
		var rel := "ReplaceableTextures/Water/Water%02d.blp" % frame
		var absolute := W3Paths.new().resolve_path(rel)
		var png := absolute.get_basename() + ".png"
		var f := FileAccess.open(absolute, FileAccess.READ)
		var bytes := f.get_buffer(f.get_length())
		var hdr_size := bytes.decode_u32(156)
		var body_off := bytes.decode_u32(28)
		var body_size := bytes.decode_u32(28 + 64)
		var jpg := PackedByteArray()
		jpg.append_array(bytes.slice(160, 160 + hdr_size))
		jpg.append_array(bytes.slice(body_off, body_off + body_size))
		var key := str(jpg.size()) + "_" + str(hash(jpg.slice(0, mini(64, jpg.size()))))
		print("frame %02d: blp=%s sidecar_png=%s jpg_size=%d key=%s cached=%s" % [
			frame, absolute, str(FileAccess.file_exists(png)), jpg.size(), key,
			str(FileAccess.file_exists(cache_dir.path_join(key + ".png"))),
		])
	quit(0)
