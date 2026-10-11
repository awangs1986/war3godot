extends RefCounted
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
## Bilinear height sample + walkability helpers on a W3EMap.


var map
func _init(p_map = null) -> void:
	map = p_map


func sample_y(world_x: float, world_z: float) -> float:
	if map == null:
		return 0.0
	var lx = (world_x - map.center_offset.x) / W3EMap.TILE_SIZE
	var ly = (world_z - map.center_offset.y) / W3EMap.TILE_SIZE
	var x0 = int(floor(lx))
	var y0 = int(floor(ly))
	var tx = clampf(lx - float(x0), 0.0, 1.0)
	var ty = clampf(ly - float(y0), 0.0, 1.0)
	var h00 = _corner_y(x0, y0)
	var h10 = _corner_y(x0 + 1, y0)
	var h01 = _corner_y(x0, y0 + 1)
	var h11 = _corner_y(x0 + 1, y0 + 1)
	var h0 = lerpf(h00, h10, tx)
	var h1 = lerpf(h01, h11, tx)
	return lerpf(h0, h1, ty)


func _corner_y(x: int, y: int) -> float:
	var c = map.get_corner(clampi(x, 0, map.width - 1), clampi(y, 0, map.height - 1))
	return 0.0 if c == null else c.world_y()


func world_to_tile(world_x: float, world_z: float) -> Vector2i:
	var lx = (world_x - map.center_offset.x) / W3EMap.TILE_SIZE
	var ly = (world_z - map.center_offset.y) / W3EMap.TILE_SIZE
	return Vector2i(int(floor(lx)), int(floor(ly)))


func tile_center_world(tile: Vector2i) -> Vector3:
	var wx = map.center_offset.x + (float(tile.x) + 0.5) * W3EMap.TILE_SIZE
	var wz = map.center_offset.y + (float(tile.y) + 0.5) * W3EMap.TILE_SIZE
	var wy = sample_y(wx, wz)
	return Vector3(wx, wy, wz)


func is_tile_walkable(tile: Vector2i) -> bool:
	if map == null:
		return false
	if tile.x < 0 or tile.y < 0 or tile.x >= map.tile_count_x() or tile.y >= map.tile_count_y():
		return false
	# A tile is blocked if any corner is boundary/map edge, or water without walk intent.
	for oy in 2:
		for ox in 2:
			var c = map.get_corner(tile.x + ox, tile.y + oy)
			if c == null:
				return false
			if c.boundary or c.map_edge:
				return false
	return true


func can_step(from_tile: Vector2i, to_tile: Vector2i) -> bool:
	if not is_tile_walkable(to_tile):
		return false
	var from_c = map.get_corner(from_tile.x, from_tile.y)
	var to_c = map.get_corner(to_tile.x, to_tile.y)
	if from_c == null or to_c == null:
		return false
	var dh: int = absi(from_c.layer_height - to_c.layer_height)
	if dh <= 1:
		return true
	# Allow larger steps only across ramp corners.
	if from_c.ramp or to_c.ramp:
		return dh <= 2
	return false
