extends RefCounted
## Thin Godot-object adapter over the C# d1map loader. It deliberately contains
## no binary-format parsing.

const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3Corner = preload("res://addons/w3kit/w3_corner.gd")
const W3DooParser = preload("res://addons/w3kit/w3_doo_parser.gd")
const NativeMapLoader = preload("res://addons/w3kit_cs/Native/NativeMapLoader.cs")


static func load_folder(path: String) -> Dictionary:
	var data: Dictionary = NativeMapLoader.new().try_load_folder(path)
	if data.has("error"):
		push_error("d1map load failed: %s" % str(data["error"]))
		return {"error": str(data["error"])}
	var map = W3EMap.new()
	map.version = int(data["version"])
	map.tileset = str(data["tileset"])
	map.custom_tileset = bool(data["custom_tileset"])
	map.width = int(data["width"])
	map.height = int(data["height"])
	map.center_offset = data["center_offset"] as Vector2
	map.ground_tilesets = PackedStringArray(data["ground_tilesets"])
	map.cliff_tilesets = PackedStringArray(data["cliff_tilesets"])

	var ground: PackedFloat32Array = data["ground_heights"]
	var water_heights: PackedFloat32Array = data["water_heights"]
	var flags: PackedByteArray = data["corner_flags"]
	var ground_textures: PackedByteArray = data["ground_textures"]
	var ground_variations: PackedByteArray = data["ground_variations"]
	var cliff_variations: PackedByteArray = data["cliff_variations"]
	var cliff_textures: PackedByteArray = data["cliff_textures"]
	var layers: PackedByteArray = data["layer_heights"]
	map.corners.resize(map.height)
	for y in map.height:
		var row: Array = []
		row.resize(map.width)
		for x in map.width:
			var i: int = y * int(map.width) + x
			var c = W3Corner.new()
			c.ground_height = ground[i]
			c.water_height = water_heights[i]
			c.map_edge = (flags[i] & 1) != 0
			c.ramp = (flags[i] & 2) != 0
			c.blight = (flags[i] & 4) != 0
			c.water = (flags[i] & 8) != 0
			c.boundary = (flags[i] & 16) != 0
			c.ground_texture = ground_textures[i]
			c.ground_variation = ground_variations[i]
			c.cliff_variation = cliff_variations[i]
			c.cliff_texture = cliff_textures[i]
			c.layer_height = layers[i]
			row[x] = c
		map.corners[y] = row

	var placements: Array = []
	for item in data["placements"]:
		var p = W3DooParser.Placement.new()
		p.id = str(item["id"])
		p.is_unit = bool(item["is_unit"])
		p.variation = int(item["variation"])
		p.position = item["position"] as Vector3
		p.angle = float(item["angle"])
		p.scale = item["scale"] as Vector3
		p.skin_id = p.id
		p.player_owner = int(item["player_owner"])
		p.flags = int(item["flags"])
		p.creation_number = int(item["creation_number"])
		placements.append(p)

	return {
		"name": str(data["name"]),
		"map": map,
		"placements": placements,
	}
