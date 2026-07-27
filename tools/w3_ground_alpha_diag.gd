extends SceneTree
func _init() -> void:
	preload("res://addons/w3kit_cs/IO/W3BinaryReaderBridge.cs")
	preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")
	var W3TextureProvider = preload("res://addons/w3kit_cs/Assets/W3TextureProvider.cs")
	var tp = W3TextureProvider.new()
	tp.load_terrain_slk("")
	for tid in ["Cdrt", "Cgrs", "Cdrd", "Arck", "Ndrd"]:
		var img: Image = tp.get_ground_image(tid)
		if img == null:
			print(tid, " NULL")
			continue
		print(tid, " ", img.get_width(), "x", img.get_height(), " fmt=", img.get_format())
		# cell1 at (64,0)
		var a0 := 0.0
		var a1 := 0.0
		var n := 0
		for y in range(0, 64, 8):
			for x in range(0, 64, 8):
				a0 += img.get_pixel(x, y).a
				a1 += img.get_pixel(64 + x, y).a
				n += 1
		print("  cell0 meanA=", a0/n, " cell1 meanA=", a1/n)
	quit(0)
