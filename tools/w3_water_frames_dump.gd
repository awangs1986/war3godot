extends SceneTree
## Evidence dump for the red/blue water flicker: decode every Water frame BLP
## and print its JPEG component count plus average RGB. Mixed red-vs-blue
## averages across frames means inconsistent channel handling in the decoder.

const W3Paths = preload("res://addons/w3kit/w3_paths.gd")
const W3BlpDecoder = preload("res://addons/w3kit/w3_blp_decoder.gd")


func _initialize() -> void:
	for frame in 45:
		var rel := "ReplaceableTextures/Water/Water%02d.blp" % frame
		var absolute := W3Paths.resolve_path(rel)
		if absolute.is_empty():
			print("frame %02d: MISSING" % frame)
			continue
		var f := FileAccess.open(absolute, FileAccess.READ)
		var bytes := f.get_buffer(f.get_length())
		var content := bytes.decode_u32(4)
		var components := 0
		if content == 0:
			var hdr_size := bytes.decode_u32(156)
			var header := bytes.slice(160, 160 + hdr_size)
			var body_off := bytes.decode_u32(28)
			var body_size := bytes.decode_u32(28 + 64)
			var jpg := PackedByteArray()
			jpg.append_array(header)
			jpg.append_array(bytes.slice(body_off, body_off + body_size))
			components = W3BlpDecoder._jpeg_component_count(jpg)
		var img := W3BlpDecoder.load_image(absolute)
		if img == null:
			print("frame %02d: content=%d comps=%d DECODE_FAIL" % [frame, content, components])
			continue
		if img.get_format() != Image.FORMAT_RGBA8:
			img.convert(Image.FORMAT_RGBA8)
		var sum := Vector3.ZERO
		var n := 0
		for y in range(0, img.get_height(), 8):
			for x in range(0, img.get_width(), 8):
				var c := img.get_pixel(x, y)
				sum += Vector3(c.r, c.g, c.b)
				n += 1
		sum /= float(n)
		print("frame %02d: content=%d comps=%d avg=(%.2f, %.2f, %.2f)" % [
			frame, content, components, sum.x, sum.y, sum.z])
	quit(0)
