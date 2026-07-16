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
			img_blp = decode_bytes(f.get_buffer(f.get_length()))
	# Prefer BLP when PNG lost alpha (leaves/torches), else prefer PNG (faster ground).
	if img_png != null and _image_has_transparency(img_png):
		return img_png
	if img_blp != null:
		return img_blp
	return img_png


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
		# Magos/HiveWE: jpeg header at byte 156, mip is JPEG body; War3 stores BGRA as fake CMYK.
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
		# Prefer turbojpeg BGRA path — Godot load_jpg strips alpha → black mats on units/leaves.
		var converted := _magick_cmyk_to_rgb(jpg)
		if converted != null:
			if converted.get_width() != int(width) or converted.get_height() != int(height):
				converted.resize(width, height)
			# Keep alpha even when alpha_bits==0: WC3 still uses the plane on model blend.
			return converted
		var img := Image.new()
		if img.load_jpg_from_buffer(jpg) == OK:
			if img.get_width() != int(width) or img.get_height() != int(height):
				img.resize(width, height)
			# RGB-only fallback: no real alpha; leave as-is (will look wrong on cutouts).
			return img
		return _checker(width, height)
	# Palette (direct)
	var pal_pos := 156
	if data.size() < pal_pos + 256 * 4 + width * height:
		return _checker(width, height)
	var colors: PackedColorArray = PackedColorArray()
	colors.resize(256)
	for i in 256:
		var b = data[pal_pos + i * 4]
		var g = data[pal_pos + i * 4 + 1]
		var r8 = data[pal_pos + i * 4 + 2]
		# Palette A is unused for WC3 direct; real alpha is a separate mip plane.
		colors[i] = Color(r8 / 255.0, g / 255.0, b / 255.0, 1.0)
	var pix_n: int = width * height
	if off + pix_n > data.size():
		return _checker(width, height)
	var indices = data.slice(off, off + pix_n)
	var img2 := Image.create(width, height, false, Image.FORMAT_RGBA8)
	for y in height:
		for x in width:
			var idx = indices[y * width + x]
			img2.set_pixel(x, y, colors[idx])
	# Alpha plane follows indices inside the same mip block (War3Net GetAlpha).
	if alpha_bits > 0:
		_apply_palette_alpha(img2, data, off + pix_n, int(alpha_bits), width, height)
	return img2


static func _apply_palette_alpha(img: Image, data: PackedByteArray, alpha_off: int, alpha_bits: int, width: int, height: int) -> void:
	var n: int = width * height
	if n <= 0 or img == null:
		return
	for i in n:
		var a: float = 1.0
		match alpha_bits:
			1:
				var bi: int = alpha_off + int(i / 8)
				if bi >= data.size():
					break
				var bit: int = (data[bi] >> (i % 8)) & 1
				a = 1.0 if bit != 0 else 0.0
			4:
				var ni: int = alpha_off + int(i / 2)
				if ni >= data.size():
					break
				var b4: int = data[ni]
				var nibble: int = (b4 & 0x0F) if (i % 2) == 0 else ((b4 >> 4) & 0x0F)
				a = float(nibble) / 15.0
			8:
				var ai: int = alpha_off + i
				if ai >= data.size():
					break
				a = float(data[ai]) / 255.0
			_:
				a = 1.0
		var x: int = i % width
		var y: int = int(i / width)
		var c := img.get_pixel(x, y)
		c.a = a
		img.set_pixel(x, y, c)


static func _magick_cmyk_to_rgb(jpg: PackedByteArray) -> Image:
	## HiveWE: War3 BLP JPEG is CMYK where channels are B,G,R,A (libturbojpeg).
	## ImageMagick color-managed CMYK is wrong — shell out to blp_cmyk_to_png.py.
	var tmp_dir := "user://w3_blp_cache"
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(tmp_dir))
	var abs_dir := ProjectSettings.globalize_path(tmp_dir)
	var key := str(jpg.size()) + "_" + str(hash(jpg.slice(0, mini(64, jpg.size()))))
	var jpg_path := abs_dir.path_join(key + ".jpg")
	var png_path := abs_dir.path_join(key + ".png")
	if not FileAccess.file_exists(png_path):
		var jf := FileAccess.open(jpg_path, FileAccess.WRITE)
		if jf == null:
			return null
		jf.store_buffer(jpg)
		jf.close()
		var helper := ProjectSettings.globalize_path("res://tools/mpqtool/blp_cmyk_to_png.py")
		var out: Array = []
		var err := OS.execute("python3", [helper, jpg_path, png_path], out, true)
		if err != 0 or not FileAccess.file_exists(png_path):
			return null
	var img := Image.new()
	if img.load(png_path) != OK:
		return null
	return img


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
