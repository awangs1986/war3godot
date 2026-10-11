extends SceneTree
## Compares a generated cliff model's footprint/edge heights with terrain.

const W3EMap = preload("res://addons/w3kit_cs/Formats/W3EMapBridge.cs")
const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")
const W3Paths = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")


func _initialize() -> void:
	call_deferred("_run")


func _sample_ground(level, placement: Vector2i, local_x: float, local_z: float) -> float:
	var gx := clampf(local_x / 128.0, 0.0, 2.0)
	var gz := clampf(local_z / 128.0, 0.0, 2.0)
	var x0 := floori(gx)
	var z0 := floori(gz)
	var x1 := mini(x0 + 1, 2)
	var z1 := mini(z0 + 1, 2)
	var tx := gx - float(x0)
	var tz := gz - float(z0)
	var c00 = level.get_corner(placement.x + x0, placement.y + z0)
	var c10 = level.get_corner(placement.x + x1, placement.y + z0)
	var c01 = level.get_corner(placement.x + x0, placement.y + z1)
	var c11 = level.get_corner(placement.x + x1, placement.y + z1)
	return lerpf(
		lerpf(float(c00.ground_height), float(c10.ground_height), tx),
		lerpf(float(c01.ground_height), float(c11.ground_height), tx), tz
	)


func _point_in_triangle(p: Vector2, a: Vector2, b: Vector2, c: Vector2) -> bool:
	var d1 := (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y)
	var d2 := (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y)
	var d3 := (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y)
	var has_neg := d1 < -0.001 or d2 < -0.001 or d3 < -0.001
	var has_pos := d1 > 0.001 or d2 > 0.001 or d3 > 0.001
	return not (has_neg and has_pos)


func _run() -> void:
	var level = W3EMap.load_file(ProjectSettings.globalize_path("res://war3/maps/LostTemple/source/war3map.w3e"))
	var source := Vector2i(44, 65)
	var transition: Dictionary = level.get_ramp_transition(source.x, source.y)
	var placement: Vector2i = transition.get("placement", source)
	var tag := str(transition.get("tag", ""))
	var rel := "Doodads/Terrain/CliffTrans/CliffTrans%s0.mdx" % tag
	var model = W3MdxModel.load_file(W3Paths.new().resolve_path(rel))
	if model == null or model.geosets.is_empty():
		printerr("CLIFF_GEOMETRY_FAILED model=%s" % rel)
		quit(1)
		return
	var base := 99
	for gy in 2:
		for gx in 2:
			base = mini(base, int(level.get_corner(placement.x + gx, placement.y + gy).layer_height))
	var transformed: Array[PackedVector3Array] = []
	for geoset in model.geosets:
		var vertices := PackedVector3Array()
		for original in geoset.vertices:
			var v := Vector3(original.z, original.y, -original.x)
			v.y += (float(base) - 2.0 + _sample_ground(level, placement, v.x, v.z)) * 128.0
			vertices.append(v)
		transformed.append(vertices)

	for z in [0.0, 64.0, 128.0]:
		for x in [0.0, 128.0, 256.0]:
			var ys: Array[float] = []
			for vertices in transformed:
				for v in vertices:
					if absf(v.x - x) < 0.01 and absf(v.z - z) < 0.01:
						ys.append(snappedf(v.y, 0.01))
			ys.sort()
			var cx := placement.x + int(round(x / 128.0))
			var cz := placement.y + int(round(z / 128.0))
			printerr("CLIFF_EDGE local=(%d,%d) vertex_y=%s terrain_corner_y=%.2f" % [
				int(x), int(z), ys, level.corner_world_y(cx, cz),
			])

	for row in 8:
		var cells := ""
		for col in 16:
			var p := Vector2((float(col) + 0.5) * 16.0, (float(row) + 0.5) * 16.0)
			var covered := false
			for gi in model.geosets.size():
				var geoset = model.geosets[gi]
				var vertices: PackedVector3Array = transformed[gi]
				for fi in range(0, geoset.faces.size() - 2, 3):
					var a := vertices[int(geoset.faces[fi])]
					var b := vertices[int(geoset.faces[fi + 1])]
					var c := vertices[int(geoset.faces[fi + 2])]
					if _point_in_triangle(p, Vector2(a.x, a.z), Vector2(b.x, b.z), Vector2(c.x, c.z)):
						covered = true
						break
				if covered: break
			cells += "#" if covered else "."
		printerr("CLIFF_COVER z=%03d %s" % [row * 16, cells])
	printerr("CLIFF_GEOMETRY_OK source=%s placement=%s tag=%s base=%d" % [source, placement, tag, base])
	quit(0)
