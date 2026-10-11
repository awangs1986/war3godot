extends SceneTree
func _init() -> void:
	var imgs: Array[Image] = []
	for i in 240:
		var im := Image.create(64, 64, false, Image.FORMAT_RGBA8)
		im.fill(Color(float(i % 16) / 16.0, 0.5, 0.2, 1.0))
		imgs.append(im)
	var arr := Texture2DArray.new()
	var err := arr.create_from_images(imgs)
	print("create_err=", err, " layers=", arr.get_layers(), " w=", arr.get_width(), " h=", arr.get_height())
	# Also test with get_region slices from real file
	var path := ProjectSettings.globalize_path("res://war3/game/TerrainArt/Felwood/Felwood_Dirt.png")
	var src := Image.new()
	src.load(path)
	src.convert(Image.FORMAT_RGBA8)
	var imgs2: Array[Image] = []
	var ts := src.get_height() / 4
	for y in 4:
		for x in 4:
			var r := src.get_region(Rect2i(x * ts, y * ts, ts, ts))
			if r.get_width() != 64:
				r.resize(64, 64, Image.INTERPOLATE_NEAREST)
			imgs2.append(r)
			var r2 := src.get_region(Rect2i((x + 4) * ts, y * ts, ts, ts))
			if r2.get_width() != 64:
				r2.resize(64, 64, Image.INTERPOLATE_NEAREST)
			imgs2.append(r2)
	# Wait that makes 32 but order wrong - just 32 slices
	imgs2.clear()
	for y in 4:
		for x in 4:
			var r := src.get_region(Rect2i(x * ts, y * ts, ts, ts))
			r.resize(64, 64, Image.INTERPOLATE_NEAREST)
			imgs2.append(r)
		for x in 4:
			var r2 := src.get_region(Rect2i((x + 4) * ts, y * ts, ts, ts))
			r2.resize(64, 64, Image.INTERPOLATE_NEAREST)
			imgs2.append(r2)
	var arr2 := Texture2DArray.new()
	var err2 := arr2.create_from_images(imgs2)
	print("slice_create_err=", err2, " layers=", arr2.get_layers(), " first mean=", imgs2[0].get_pixel(32,32))
	quit(0)
