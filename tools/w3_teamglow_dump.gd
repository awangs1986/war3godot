extends SceneTree
## What do the TeamGlow/TeamColor replaceables decode to? A failed decode
## renders the Blademaster's 322x322 additive ground quad as a solid square.

const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3BlpDecoder = preload("res://addons/w3kit/w3_blp_decoder.gd")


func _initialize() -> void:
	for rel in [
		"ReplaceableTextures/TeamGlow/TeamGlow00.blp",
		"ReplaceableTextures/TeamColor/TeamColor00.blp",
		"Textures/GenericGlow2c.blp",
		"Textures/Red_Glow3.blp",
	]:
		var absolute := W3Paths.resolve_path(rel)
		if absolute.is_empty():
			print(rel, " -> UNRESOLVED")
			continue
		var png := absolute.get_basename() + ".png"
		var img := W3BlpDecoder.load_image(absolute)
		if img == null:
			print(rel, " -> DECODE NULL (sidecar png exists=", FileAccess.file_exists(png), ")")
			continue
		if img.get_format() != Image.FORMAT_RGBA8:
			img.convert(Image.FORMAT_RGBA8)
		var corner := img.get_pixel(2, 2)
		var center := img.get_pixel(img.get_width() / 2, img.get_height() / 2)
		print("%s -> %dx%d corner=%s center=%s sidecar=%s" % [
			rel, img.get_width(), img.get_height(), str(corner), str(center),
			str(FileAccess.file_exists(png))])
	quit(0)
