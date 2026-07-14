extends RefCounted
## war3map.shd — open-realm / WC3 baked terrain shadow map.
## Raw bytes, size = (tiles_x*4) * (tiles_y*4). Byte 0x00=none, 0xFF=full.
## open-realm uploads alpha = shadow/2 and splats black over terrain.


static func expected_size(tiles_x: int, tiles_y: int) -> int:
	return tiles_x * 4 * tiles_y * 4


## Returns ImageTexture (R8), or null if missing/wrong size.
## Sample .r in shader; multiply albedo by (1.0 - r) to match open-realm splat.
static func load_texture(bytes: PackedByteArray, tiles_x: int, tiles_y: int) -> ImageTexture:
	if bytes.is_empty() or tiles_x <= 0 or tiles_y <= 0:
		return null
	var w: int = tiles_x * 4
	var h: int = tiles_y * 4
	var need: int = w * h
	if bytes.size() < need:
		push_warning("W3Shd: size %d < expected %d (%dx%d)" % [bytes.size(), need, w, h])
		return null
	var out := PackedByteArray()
	out.resize(need)
	for i in need:
		# open-realm: pixels[i].a = shadow / 2  (0..127)
		out[i] = int(bytes[i]) >> 1
	var img := Image.create_from_data(w, h, false, Image.FORMAT_R8, out)
	var tex := ImageTexture.create_from_image(img)
	return tex


static func blank_texture() -> ImageTexture:
	var img := Image.create(4, 4, false, Image.FORMAT_R8)
	img.fill(Color(0, 0, 0, 1))
	return ImageTexture.create_from_image(img)
