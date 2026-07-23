extends RefCounted
const W3BinaryReader = preload("res://addons/w3kit/w3_binary_reader.gd")
const MAX_IMAGE_DIMENSION := 8192
const MAX_IMAGE_PIXELS := 64 * 1024 * 1024
## BLP1 (palette / JPEG) and basic BLP2 decode → Godot Image.
## PNG sidecars are used when they keep transparency; otherwise decode BLP
## (many tree/leaf sidecars were baked as opaque RGB and look like cubes).


static func load_image(path: String) -> Image:
	if path.is_empty():
		return null
	var direct_ext := path.get_extension().to_lower()
	if direct_ext in ["dds", "png", "tga", "jpg", "jpeg", "webp"] and FileAccess.file_exists(path):
		var direct := Image.new()
		if direct_ext == "dds":
			# Runtime-extracted Reforged assets live below a .gdignore directory,
			# so the ResourceLoader has no imported texture to use. Image.load()
			# also does not route raw DDS files through the buffer decoder in this
			# Godot build; invoke the supported DDS codec explicitly.
			var dds_file := FileAccess.open(path, FileAccess.READ)
			if dds_file == null:
				return null
			var dds_bytes := dds_file.get_buffer(dds_file.get_length())
			return direct if direct.load_dds_from_buffer(dds_bytes) == OK else null
		return direct if direct.load(path) == OK else null
	var base := path.get_basename()
	var png := base + ".png"
	var blp := path if path.get_extension().to_lower() == "blp" else base + ".blp"
	var img_png: Image = null
	if FileAccess.file_exists(png):
		img_png = Image.new()
		if img_png.load(png) != OK:
			img_png = null
	var img_blp: Image = null
	if FileAccess.file_exists(blp):
		var f = FileAccess.open(blp, FileAccess.READ)
		if f != null:
			var blp_data := f.get_buffer(f.get_length())
			# A pre-converted opaque sidecar is lossless for a BLP that declares
			# no alpha. Prefer it before asking Godot to decode a four-channel
			# War3 JPEG, which is unsupported on Windows without TurboJPEG.
			if img_png != null and not _blp_declares_alpha(blp_data):
				return img_png
			img_blp = decode_bytes(blp_data)
	# Prefer BLP when PNG lost alpha (leaves/torches), else prefer PNG (faster ground).
	if img_png != null and _image_has_transparency(img_png):
		return img_png
	if img_blp != null:
		return img_blp
	if img_png == null and FileAccess.file_exists(blp) and _is_cmyk_jpeg_blp(blp):
		push_error(
			"W3BlpDecoder: %s is a JPEG-CMYK BLP without a .png sidecar. "
			% blp
			+ "Run: python tools/mpqtool/convert_cmyk_sidecars.py <asset_root>"
		)
	return img_png


static func _is_cmyk_jpeg_blp(path: String) -> bool:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return false
	var data := f.get_buffer(f.get_length())
	if data.size() < 160 or data.slice(0, 4).get_string_from_ascii() != "BLP1":
		return false
	if data.decode_u32(4) != 0: # content: 0 = JPEG
		return false
	var header_size := data.decode_u32(156)
	if header_size <= 0 or 160 + header_size > data.size():
		return false
	var mip_off := data.decode_u32(28)
	var mip_size := data.decode_u32(28 + 64)
	if mip_off + mip_size > data.size():
		return false
	var jpg := data.slice(160, 160 + header_size)
	jpg.append_array(data.slice(mip_off, mip_off + mip_size))
	return _jpeg_component_count(jpg) == 4


static func _blp_declares_alpha(data: PackedByteArray) -> bool:
	if data.size() < 12:
		return true
	var magic := data.slice(0, 4).get_string_from_ascii()
	if magic == "BLP1":
		return data.decode_u32(8) != 0
	if magic == "BLP2":
		# BLP2: type u32, compression u8, alpha_depth u8.
		return data.size() > 9 and data[9] != 0
	return true


static func _image_has_transparency(img: Image) -> bool:
	if img == null:
		return false
	var fmt := img.get_format()
	# Opaque RGB sidecars (common for tree leaves) must not win over BLP.
	if fmt == Image.FORMAT_RGB8 or fmt == Image.FORMAT_RGB565:
		return false
	if fmt != Image.FORMAT_RGBA8 and fmt != Image.FORMAT_RGBA4444:
		img.convert(Image.FORMAT_RGBA8)
	# Sample a grid — full scan is too slow for 512 atlases on every load.
	var w := img.get_width()
	var h := img.get_height()
	if w <= 0 or h <= 0:
		return false
	var step_x := maxi(1, w / 32)
	var step_y := maxi(1, h / 32)
	for y in range(0, h, step_y):
		for x in range(0, w, step_x):
			if img.get_pixel(x, y).a < 0.98:
				return true
	return false


static func decode_bytes(data: PackedByteArray) -> Image:
	if data.size() < 8:
		return null
	var magic = data.slice(0, 4).get_string_from_ascii()
	if magic == "BLP1":
		return _decode_blp1(data)
	if magic == "BLP2":
		return _decode_blp2(data)
	return null


static func _decode_blp1(data: PackedByteArray) -> Image:
	var r = W3BinaryReader.new(data)
	r.read_fourcc() # BLP1
	var content = r.read_u32() # 0=JPEG, 1=palette
	var alpha_bits = r.read_u32()
	var width = r.read_u32()
	var height = r.read_u32()
	r.read_u32() # extra
	r.read_u32() # has_mipmaps
	var mip_offsets: Array[int] = []
	var mip_sizes: Array[int] = []
	for _i in 16:
		mip_offsets.append(r.read_u32())
	for _i in 16:
		mip_sizes.append(r.read_u32())
	if not _valid_dimensions(width, height) or mip_sizes[0] <= 0:
		return null
	var off: int = mip_offsets[0]
	var sz: int = mip_sizes[0]
	if off + sz > data.size():
		return null
	if content == 0:
		# Magos/HiveWE: jpeg header at byte 156, mip is JPEG body; often CMYK.
		var hdr_off := 156
		if hdr_off + 4 > data.size():
			return _checker(width, height)
		var header_size: int = data.decode_u32(hdr_off)
		if header_size <= 0 or hdr_off + 4 + header_size > data.size():
			return _checker(width, height)
		var header := data.slice(hdr_off + 4, hdr_off + 4 + header_size)
		var body := data.slice(off, off + sz)
		var jpg := PackedByteArray()
		jpg.append_array(header)
		jpg.append_array(body)
		# War3 JPEG-BLPs are 4-channel "fake CMYK" (BGRA); Godot cannot decode
		# them. That conversion is an offline import step
		# (tools/mpqtool/convert_cmyk_sidecars.py) — the runtime never shells
		# out to external converters. load_image() falls back to the sidecar.
		if _jpeg_component_count(jpg) == 4:
			return null
		var img := Image.new()
		if img.load_jpg_from_buffer(jpg) == OK:
			if img.get_width() != int(width) or img.get_height() != int(height):
				img.resize(width, height)
			return img
		return null
	# Palette
	var pal_pos := 156
	if data.size() < pal_pos + 256 * 4 + width * height:
		return _checker(width, height)
	var colors: PackedColorArray = PackedColorArray()
	colors.resize(256)
	for i in 256:
		var b = data[pal_pos + i * 4]
		var g = data[pal_pos + i * 4 + 1]
		var r8 = data[pal_pos + i * 4 + 2]
		var a = data[pal_pos + i * 4 + 3]
		if alpha_bits == 0:
			a = 255
		colors[i] = Color(r8 / 255.0, g / 255.0, b / 255.0, a / 255.0)
	var indices = data.slice(off, off + width * height)
	var img2 := Image.create(width, height, false, Image.FORMAT_RGBA8)
	for y in height:
		for x in width:
			var idx = indices[y * width + x]
			img2.set_pixel(x, y, colors[idx])
	return img2


static func _jpeg_component_count(jpg: PackedByteArray) -> int:
	if jpg.size() < 12 or jpg[0] != 0xFF or jpg[1] != 0xD8:
		return 0
	var pos := 2
	while pos + 3 < jpg.size():
		while pos < jpg.size() and jpg[pos] == 0xFF:
			pos += 1
		if pos >= jpg.size():
			break
		var marker: int = jpg[pos]
		pos += 1
		if marker == 0xD9 or marker == 0xDA:
			break
		if marker == 0x01 or (marker >= 0xD0 and marker <= 0xD7):
			continue
		if pos + 1 >= jpg.size():
			break
		var segment_size: int = (int(jpg[pos]) << 8) | int(jpg[pos + 1])
		if segment_size < 2 or pos + segment_size > jpg.size():
			break
		var is_sof := (
			(marker >= 0xC0 and marker <= 0xC3)
			or (marker >= 0xC5 and marker <= 0xC7)
			or (marker >= 0xC9 and marker <= 0xCB)
			or (marker >= 0xCD and marker <= 0xCF)
		)
		if is_sof and segment_size >= 8:
			return int(jpg[pos + 7])
		pos += segment_size
	return 0


static func _decode_blp2(data: PackedByteArray) -> Image:
	var r = W3BinaryReader.new(data)
	r.read_fourcc()
	r.read_u32() # type
	var compression = r.read_u8()
	var alpha_depth = r.read_u8()
	var alpha_type = r.read_u8()
	r.read_u8() # has_mips
	var width = r.read_u32()
	var height = r.read_u32()
	if not _valid_dimensions(width, height):
		return null
	var offsets: Array[int] = []
	var sizes: Array[int] = []
	for _i in 16:
		offsets.append(r.read_u32())
	for _i in 16:
		sizes.append(r.read_u32())
	if sizes[0] <= 0 or offsets[0] + sizes[0] > data.size():
		return _checker(maxi(1, width), maxi(1, height))
	var block = data.slice(offsets[0], offsets[0] + sizes[0])
	# compression 1 = uncompressed BGRA; 2 = DXT
	if compression == 1:
		var img = Image.create(width, height, false, Image.FORMAT_RGBA8)
		for y in height:
			for x in width:
				var i = (y * width + x) * 4
				if i + 3 >= block.size():
					break
				img.set_pixel(x, y, Color(block[i + 2] / 255.0, block[i + 1] / 255.0, block[i] / 255.0, block[i + 3] / 255.0))
		return img
	# DXT1/3/5 via Image.create_from_data if we decompress — use checker fallback for 0.1 if unknown.
	var _ad = alpha_depth
	var _at = alpha_type
	return _checker(width, height)


static func _checker(w: int, h: int) -> Image:
	if not _valid_dimensions(w, h):
		return null
	var img = Image.create(maxi(1, w), maxi(1, h), false, Image.FORMAT_RGBA8)
	for y in h:
		for x in w:
			var on = ((x >> 3) + (y >> 3)) % 2 == 0
			img.set_pixel(x, y, Color(0.35, 0.55, 0.28) if on else Color(0.25, 0.4, 0.2))
	return img


static func _valid_dimensions(width: int, height: int) -> bool:
	return (
		width > 0
		and height > 0
		and width <= MAX_IMAGE_DIMENSION
		and height <= MAX_IMAGE_DIMENSION
		and width * height <= MAX_IMAGE_PIXELS
	)
