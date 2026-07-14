extends SceneTree
func _init() -> void:
	var img := Image.new()
	var err := img.load("res://war3/game/TerrainArt/Felwood/Felwood_Grass.png")
	print("load_err=", err, " fmt=", img.get_format(), " a_cell1=", img.get_pixel(70, 32).a, " a_cell0=", img.get_pixel(32, 32).a)
	var out := Image.create(512, 256, false, Image.FORMAT_RGBA8)
	out.blit_rect(img, Rect2i(0, 0, 512, 256), Vector2i(0, 0))
	print("blit a_cell1=", out.get_pixel(70, 32).a)
	var arr := Texture2DArray.new()
	print("create=", arr.create_from_images([out]), " layers=", arr.get_layers())
	var back: Image = arr.get_layer_data(0)
	print("back_null=", back == null)
	if back != null:
		print("back a_cell1=", back.get_pixel(70, 32).a)
	quit(0)
