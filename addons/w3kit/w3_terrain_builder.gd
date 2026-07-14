extends Node3D
const W3EMap = preload("res://addons/w3kit/w3e_map.gd")
const W3HeightSampler = preload("res://addons/w3kit/w3_height_sampler.gd")
const W3TextureProvider = preload("res://addons/w3kit/w3_texture_provider.gd")
## Builds chunked ground + cliff-face meshes from W3EMap.
## Ground BLP/PNG is a variation atlas (HiveWE / mdx-m3-viewer / Warsmash):
##   square -> 4x4 cells; width>height -> 8x4.
## Soft borders: secondary textures use atlas cells 0-15 as alpha masks
## (bitset of which corners have that texture) - NOT hard square tiles.


const CHUNK_TILES := 8

var map
var textures
var sampler
## tileset_id -> { "extended": bool, "tex": Texture2D }
var _atlas_info: Dictionary = {}
## Warsmash appends blight as an extra ground texture index.
var _blight_index: int = -1


func build(p_map, p_textures = null) -> void:
	map = p_map
	textures = p_textures if p_textures != null else W3TextureProvider.new()
	sampler = W3HeightSampler.new(map)
	_atlas_info.clear()
	_blight_index = -1
	_ensure_blight_tileset()
	for c in get_children():
		c.queue_free()
	var tx = map.tile_count_x()
	var ty = map.tile_count_y()
	var cx0 = 0
	while cx0 < tx:
		var cy0 = 0
		while cy0 < ty:
			_build_chunk(cx0, cy0, mini(CHUNK_TILES, tx - cx0), mini(CHUNK_TILES, ty - cy0))
			cy0 += CHUNK_TILES
		cx0 += CHUNK_TILES


func _ensure_blight_tileset() -> void:
	var letter := "L"
	if map != null and str(map.tileset).length() > 0:
		letter = str(map.tileset).left(1)
	var blight_map := {
		"L": "TerrainArt/Blight/Lords_Blight.blp",
		"F": "TerrainArt/Blight/Lordf_Blight.blp",
		"W": "TerrainArt/Blight/Lordw_Blight.blp",
		"B": "TerrainArt/Blight/Barrens_Blight.blp",
		"A": "TerrainArt/Blight/Ashen_Blight.blp",
		"C": "TerrainArt/Blight/Felwood_Blight.blp",
		"N": "TerrainArt/Blight/North_Blight.blp",
		"Y": "TerrainArt/Blight/Village_Blight.blp",
		"X": "TerrainArt/Blight/Dalaran_Blight.blp",
		"V": "TerrainArt/Blight/Village_Blight.blp",
		"Q": "TerrainArt/Blight/VillageFall_Blight.blp",
		"D": "TerrainArt/Blight/Cave_Blight.blp",
		"G": "TerrainArt/Blight/Cave_Blight.blp",
		"Z": "TerrainArt/Blight/Ruins_Blight.blp",
		"I": "TerrainArt/Blight/Ice_Blight.blp",
		"O": "TerrainArt/Blight/Outland_Blight.blp",
		"K": "TerrainArt/Blight/Citadel_Blight.blp",
	}
	var rel: String = blight_map.get(letter, "TerrainArt/Blight/Lords_Blight.blp")
	var blight_id := "__BLIGHT__"
	if map.ground_tilesets.has(blight_id):
		_blight_index = map.ground_tilesets.find(blight_id)
	else:
		map.ground_tilesets.append(blight_id)
		_blight_index = map.ground_tilesets.size() - 1
	var tex: Texture2D = textures.get_texture(rel)
	var extended := false
	if tex != null and tex.get_height() > 0:
		extended = tex.get_width() > tex.get_height()
	_atlas_info[blight_id] = {"tex": tex, "extended": extended}


func _build_chunk(ox: int, oy: int, w: int, h: int) -> void:
	# Primary layer: grouped by lowest unique corner texture.
	# Blend layers: (tex_idx -> tiles with that secondary + bitset as atlas cell).
	var by_primary: Dictionary = {}
	var by_blend: Dictionary = {} # tex_idx -> Array[{ "pos": Vector2i, "bitset": int }]
	var cliff_tiles: Array = []
	for y in h:
		for x in w:
			var gx := ox + x
			var gy := oy + y
			if _is_cliff_tile(gx, gy):
				cliff_tiles.append(Vector2i(gx, gy))
				continue
			var layers: Array = _tile_texture_layers(gx, gy)
			if layers.is_empty():
				continue
			var primary: Dictionary = layers[0]
			var pk: int = int(primary["tex"])
			if not by_primary.has(pk):
				by_primary[pk] = []
			by_primary[pk].append({"pos": Vector2i(gx, gy), "cell": int(primary["variation"])})
			for i in range(1, layers.size()):
				var bl: Dictionary = layers[i]
				var bk: int = int(bl["tex"])
				if not by_blend.has(bk):
					by_blend[bk] = []
				by_blend[bk].append({"pos": Vector2i(gx, gy), "cell": int(bl["variation"])})
	var chunk = Node3D.new()
	chunk.name = "Chunk_%d_%d" % [ox, oy]
	add_child(chunk)
	# Opaque primary ground.
	var first_collision := true
	for tex_idx in by_primary.keys():
		var tiles: Array = by_primary[tex_idx]
		var st = SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		for t in tiles:
			_add_tile_uv(st, t["pos"].x, t["pos"].y, int(tex_idx), int(t["cell"]), 0.0)
		st.generate_normals()
		var mesh = st.commit()
		var mi = MeshInstance3D.new()
		mi.mesh = mesh
		mi.material_override = _ground_material(int(tex_idx), false)
		chunk.add_child(mi)
		if first_collision:
			_add_collision(chunk, mesh)
			first_collision = false
	# Alpha-blended transition overlays (Warsmash / viewer ground.frag mix by texel.a).
	for tex_idx in by_blend.keys():
		var tiles2: Array = by_blend[tex_idx]
		var st2 = SurfaceTool.new()
		st2.begin(Mesh.PRIMITIVE_TRIANGLES)
		for t in tiles2:
			# Slight Y bias so overlays don't z-fight.
			_add_tile_uv(st2, t["pos"].x, t["pos"].y, int(tex_idx), int(t["cell"]), 0.55)
		st2.generate_normals()
		var mesh2 = st2.commit()
		var mi2 = MeshInstance3D.new()
		mi2.mesh = mesh2
		mi2.material_override = _ground_material(int(tex_idx), true)
		chunk.add_child(mi2)
	# Cliff tiles: no visible ground (Warsmash culls them). Collision-only caps so units can stand.
	var st_cliff = SurfaceTool.new()
	st_cliff.begin(Mesh.PRIMITIVE_TRIANGLES)
	var any_cliff := false
	for t in cliff_tiles:
		_add_tile_flat(st_cliff, t.x, t.y)
		any_cliff = true
	if any_cliff:
		st_cliff.generate_normals()
		var cmesh = st_cliff.commit()
		# Always add cliff-tile collision (separate from textured ground).
		_add_collision(chunk, cmesh)


func _ground_material(tex_idx: int, blend_layer: bool) -> StandardMaterial3D:
	var W3War3Lit = preload("res://addons/w3kit/w3_war3_lit.gd")
	var mat = StandardMaterial3D.new()
	W3War3Lit.style_terrain_material(mat)
	mat.texture_filter = BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
	var tileset_id := _tileset_id(tex_idx)
	var info := _ground_atlas(tileset_id)
	mat.albedo_texture = info["tex"]
	if blend_layer:
		# Soft atlas masks (cells 0-15). Must be ALPHA blend — depth-prepass
		# turns soft gradients into hard square cutouts.
		mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		mat.blend_mode = BaseMaterial3D.BLEND_MODE_MIX
		mat.depth_draw_mode = BaseMaterial3D.DEPTH_DRAW_DISABLED
		mat.render_priority = 1
	return mat


func _add_collision(chunk: Node3D, mesh: Mesh) -> void:
	if mesh == null or mesh.get_surface_count() == 0:
		return
	var body = StaticBody3D.new()
	body.collision_layer = 1
	body.collision_mask = 0
	var col = CollisionShape3D.new()
	col.shape = mesh.create_trimesh_shape()
	body.add_child(col)
	chunk.add_child(body)


func _tileset_id(tex_idx: int) -> String:
	if tex_idx >= 0 and tex_idx < map.ground_tilesets.size():
		return map.ground_tilesets[tex_idx]
	return "Ldrt"


func _ground_atlas(tileset_id: String) -> Dictionary:
	if _atlas_info.has(tileset_id):
		return _atlas_info[tileset_id]
	var tex: Texture2D = textures.get_ground_texture(tileset_id)
	var extended := false
	if tex != null and tex.get_height() > 0:
		extended = tex.get_width() > tex.get_height()
	var info := {"tex": tex, "extended": extended}
	_atlas_info[tileset_id] = info
	return info


## Matches mdx-m3-viewer / Warsmash getVariation.
func _remap_variation(raw_variation: int, extended: bool) -> int:
	if extended:
		if raw_variation < 16:
			return 16 + raw_variation
		elif raw_variation == 16:
			return 15
		return 0
	if raw_variation == 0:
		return 0
	return 15


## Atlas cell UV - matches ground.vert getCell + getUV.
func _atlas_uv(local: Vector2, variation: int, extended: bool) -> Vector2:
	var cell_x: float
	var cell_y: float
	if variation < 16:
		cell_x = float(variation % 4)
		cell_y = float(int(variation / 4))
	else:
		var v2 := variation - 16
		cell_x = 4.0 + float(v2 % 4)
		cell_y = float(int(v2 / 4))
	var cell_w := 0.125 if extended else 0.25
	var cell_h := 0.25
	var u := local.x
	var v := 1.0 - local.y
	var px := 1.0 / 512.0
	var py := 1.0 / 256.0
	var out := Vector2((cell_x + u) * cell_w, (cell_y + v) * cell_h)
	var min_uv := Vector2(cell_x * cell_w + px, cell_y * cell_h + py)
	var max_uv := Vector2((cell_x + 1.0) * cell_w - px, (cell_y + 1.0) * cell_h - py)
	out.x = clampf(out.x, min_uv.x, max_uv.x)
	out.y = clampf(out.y, min_uv.y, max_uv.y)
	return out


func _is_cliff_tile(x: int, y: int) -> bool:
	var c00 = map.get_corner(x, y)
	var c10 = map.get_corner(x + 1, y)
	var c01 = map.get_corner(x, y + 1)
	var c11 = map.get_corner(x + 1, y + 1)
	if c00 == null or c10 == null or c01 == null or c11 == null:
		return false
	var a: int = c00.layer_height
	return a != c10.layer_height or a != c01.layer_height or a != c11.layer_height


## Returns [{tex, variation}, ...] - primary first (Warsmash getTextureVariations).
## Secondary variation is the corner bitset used as atlas cell index for alpha masks.
func _tile_texture_layers(x: int, y: int) -> Array:
	var t00 := _corner_tex(x, y)
	var t10 := _corner_tex(x + 1, y)
	var t01 := _corner_tex(x, y + 1)
	var t11 := _corner_tex(x + 1, y + 1)
	var unique: Array = []
	for c in [t00, t10, t01, t11]:
		if not unique.has(c):
			unique.append(c)
	unique.sort()
	var out: Array = []
	var primary: int = int(unique[0])
	var c00 = map.get_corner(x, y)
	var raw_var := 0 if c00 == null else int(c00.ground_variation)
	var info := _ground_atlas(_tileset_id(primary))
	out.append({"tex": primary, "variation": _remap_variation(raw_var, info["extended"])})
	for i in range(1, unique.size()):
		var tex: int = int(unique[i])
		var bitset := 0
		# Viewer bit order: bottomRight=1, bottomLeft=2, topRight=4, topLeft=8
		if t10 == tex:
			bitset |= 0b0001
		if t00 == tex:
			bitset |= 0b0010
		if t11 == tex:
			bitset |= 0b0100
		if t01 == tex:
			bitset |= 0b1000
		out.append({"tex": tex, "variation": bitset})
	return out


func _corner_tex(x: int, y: int) -> int:
	## Warsmash realTileTexture (simplified): blight uses appended blight atlas.
	var c = map.get_corner(x, y)
	if c == null:
		return 0
	if c.blight and _blight_index >= 0:
		return _blight_index
	return int(c.ground_texture)


func _add_tile_uv(st: SurfaceTool, x: int, y: int, tex_idx: int, atlas_cell: int, y_bias: float = 0.0) -> void:
	var tileset_id := _tileset_id(tex_idx)
	var info := _ground_atlas(tileset_id)
	var extended: bool = info["extended"]
	var p00 = map.corner_world_pos(x, y)
	var p10 = map.corner_world_pos(x + 1, y)
	var p01 = map.corner_world_pos(x, y + 1)
	var p11 = map.corner_world_pos(x + 1, y + 1)
	if y_bias != 0.0:
		p00.y += y_bias
		p10.y += y_bias
		p01.y += y_bias
		p11.y += y_bias
	var uv00 := _atlas_uv(Vector2(0, 0), atlas_cell, extended)
	var uv10 := _atlas_uv(Vector2(1, 0), atlas_cell, extended)
	var uv01 := _atlas_uv(Vector2(0, 1), atlas_cell, extended)
	var uv11 := _atlas_uv(Vector2(1, 1), atlas_cell, extended)
	_tri(st, p00, p10, p01, uv00, uv10, uv01)
	_tri(st, p10, p11, p01, uv10, uv11, uv01)


func _add_tile_flat(st: SurfaceTool, x: int, y: int) -> void:
	var p00 = map.corner_world_pos(x, y)
	var p10 = map.corner_world_pos(x + 1, y)
	var p01 = map.corner_world_pos(x, y + 1)
	var p11 = map.corner_world_pos(x + 1, y + 1)
	_tri(st, p00, p10, p01, Vector2(0, 0), Vector2(1, 0), Vector2(0, 1))
	_tri(st, p10, p11, p01, Vector2(1, 0), Vector2(1, 1), Vector2(0, 1))


func _add_cliff_faces(st: SurfaceTool, x: int, y: int) -> bool:
	var any := false
	any = _cliff_edge(st, x, y, x + 1, y) or any
	any = _cliff_edge(st, x, y, x, y + 1) or any
	any = _cliff_edge(st, x + 1, y, x + 1, y + 1) or any
	any = _cliff_edge(st, x, y + 1, x + 1, y + 1) or any
	return any


func _cliff_edge(st: SurfaceTool, x0: int, y0: int, x1: int, y1: int) -> bool:
	var a = map.get_corner(x0, y0)
	var b = map.get_corner(x1, y1)
	if a == null or b == null:
		return false
	if a.layer_height == b.layer_height and absf(a.world_y() - b.world_y()) < 1.0:
		return false
	if absi(a.layer_height - b.layer_height) < 1 and not (a.ramp or b.ramp):
		if absf(a.world_y() - b.world_y()) < W3EMap.TILE_SIZE * 0.25:
			return false
	var pa = map.corner_world_pos(x0, y0)
	var pb = map.corner_world_pos(x1, y1)
	var base_y = minf(pa.y, pb.y) - float(maxi(1, absi(a.layer_height - b.layer_height))) * W3EMap.TILE_SIZE
	var pa_b = Vector3(pa.x, base_y, pa.z)
	var pb_b = Vector3(pb.x, base_y, pb.z)
	_tri(st, pa_b, pb_b, pb, Vector2(0, 1), Vector2(1, 1), Vector2(1, 0))
	_tri(st, pa_b, pb, pa, Vector2(0, 1), Vector2(1, 0), Vector2(0, 0))
	return true


func _tri(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, uva: Vector2, uvb: Vector2, uvc: Vector2) -> void:
	st.set_uv(uva)
	st.add_vertex(a)
	st.set_uv(uvb)
	st.add_vertex(b)
	st.set_uv(uvc)
	st.add_vertex(c)
