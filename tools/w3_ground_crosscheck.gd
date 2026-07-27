extends SceneTree
## Cross-check Lost Temple's effective corner textures against HiveWE's
## Terrain::real_tile_texture rule. Keeps colored-boundary regressions visible.
##
## R6: sole authority is now the C# WarsmashTerrainRules/GroundLayout pipeline
## (via GroundLayoutCrosscheckBridge) —the GDS reference renderer
## (w3_gpu_terrain.gd) this tool used to instantiate and diff against is gone.
## The "hivewe_*" functions below remain independent from-scratch
## re-derivations (straight from raw corner flags), so this still catches
## regressions in the C# packer, not just self-comparisons.

const W3EMap = preload("res://addons/w3kit_cs/Formats/W3EMapBridge.cs")
const W3TextureProvider = preload("res://addons/w3kit_cs/Assets/W3TextureProvider.cs")
const W3PathResolver = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")
const GroundLayoutCrosscheckBridge = preload("res://addons/w3kit_cs/Terrain/GroundLayoutCrosscheckBridge.cs")

const _CELL_SIZE := 64


func _initialize() -> void:
	var asset_free := "--asset-free" in OS.get_cmdline_user_args()
	var source_res := "res://testdata/hivewe_sample/war3map.w3e" if asset_free else "res://war3/maps/LostTemple/source/war3map.w3e"
	var source := ProjectSettings.globalize_path(source_res)
	var level = W3EMap.load_file(source)
	if level == null:
		push_error("W3_GROUND_CROSSCHECK_FAIL map missing")
		quit(1)
		return
	var provider = null
	if not asset_free:
		provider = W3TextureProvider.new()
		provider.load_terrain_slk("")

	var layer_count: int = level.ground_tilesets.size() + 1
	var cliff_to_ground := _prepare_cliff_to_ground(level, provider, asset_free)
	var tileset_extended := _prepare_tileset_extended(level, provider, asset_free, layer_count)
	var romp := _prepare_romp_tiles(level)

	var bridge := GroundLayoutCrosscheckBridge.new()
	bridge.prepare(level, cliff_to_ground, tileset_extended, not asset_free)

	var hivewe_romp := _hivewe_romp_mask(level)
	var romp_mismatches: Array[String] = []
	for y in range(level.tile_count_y()):
		for x in range(level.tile_count_x()):
			var expected_romp := hivewe_romp[y * level.tile_count_x() + x] != 0
			var actual_romp := _tile_is_romp(romp, x, y)
			if expected_romp != actual_romp and romp_mismatches.size() < 24:
				romp_mismatches.append("%d,%d actual=%s hive=%s" % [x, y, actual_romp, expected_romp])
	print("W3_GROUND_CROSSCHECK romp_mismatch_count=", romp_mismatches.size())
	for mismatch in romp_mismatches:
		print("  ", mismatch)
	if not romp_mismatches.is_empty():
		quit(4)
		return

	var mismatches: Array[String] = []
	var mismatch_count := 0
	var warsmash_disagreements: Array[String] = []
	var warsmash_disagreement_count := 0
	for y in range(level.height):
		for x in range(level.width):
			var expected := _hivewe_corner_texture(level, cliff_to_ground, x, y)
			var warsmash_expected := _warsmash_corner_texture(level, cliff_to_ground, romp, x, y)
			if warsmash_expected != expected:
				warsmash_disagreement_count += 1
				if warsmash_disagreements.size() < 16:
					warsmash_disagreements.append("%d,%d hive=%d warsmash=%d" % [x, y, expected, warsmash_expected])
			var actual: int = bridge.corner_texture(x, y)
			if actual == expected:
				continue
			mismatch_count += 1
			if mismatches.size() < 24:
				var c = level.get_corner(x, y)
				mismatches.append("%d,%d actual=%d hive=%d ramp=%s ground=%d cliff=%d" % [
					x, y, actual, expected, bool(c.ramp), int(c.ground_texture), int(c.cliff_texture),
				])
	print("W3_GROUND_CROSSCHECK mismatch_count=", mismatch_count)
	for mismatch in mismatches:
		print("  ", mismatch)
	if mismatch_count != 0:
		quit(2)
		return
	print("W3_GROUND_CROSSCHECK hive_warsmash_disagreement_count=", warsmash_disagreement_count)
	for disagreement in warsmash_disagreements:
		print("  ", disagreement)
	var mask_mismatches: Array[String] = []
	if not asset_free:
		mask_mismatches = _mask_orientation_mismatches(level, provider)
	print("W3_GROUND_CROSSCHECK mask_mismatch_count=", mask_mismatches.size())
	for mismatch in mask_mismatches.slice(0, mini(24, mask_mismatches.size())):
		print("  ", mismatch)
	if not mask_mismatches.is_empty():
		quit(3)
		return
	print("W3_GROUND_CROSSCHECK_OK corners=", level.width * level.height)
	quit(0)


## Mirrors the old w3_gpu_terrain.gd _prepare_cliff_to_ground (minus the
## SLK-missing heuristic fallback, which never triggers once CliffTypes.slk
## is present —asset_free skips cliff mapping entirely, matching the old
## GDS asset-free branch which also filled zeros).
func _prepare_cliff_to_ground(level, provider, asset_free: bool) -> PackedInt32Array:
	var cliff_to_ground := PackedInt32Array()
	if asset_free or provider == null:
		cliff_to_ground.resize(level.cliff_tilesets.size())
		cliff_to_ground.fill(0)
		return cliff_to_ground
	for i in level.cliff_tilesets.size():
		var cid := str(level.cliff_tilesets[i])
		var gid := str(provider.get_cliff_ground_tile(cid))
		var mapped := 0
		for gi in level.ground_tilesets.size():
			if str(level.ground_tilesets[gi]) == gid:
				mapped = gi
				break
		cliff_to_ground.append(mapped)
	return cliff_to_ground


## Mirrors the ground/blight "extended atlas" flags the old w3_gpu_terrain.gd
## _build_ground_arrays computed while loading each layer's image.
func _prepare_tileset_extended(level, provider, asset_free: bool, layer_count: int) -> PackedByteArray:
	var extended := PackedByteArray()
	extended.resize(layer_count)
	extended.fill(0)
	if asset_free or provider == null:
		return extended
	for i in level.ground_tilesets.size():
		var img: Image = provider.get_ground_image(str(level.ground_tilesets[i]))
		if img != null and img.get_width() >= img.get_height() * 2:
			extended[i] = 1
	var blight_img: Image = provider.get_blight_image(str(level.tileset))
	if blight_img != null and blight_img.get_width() >= blight_img.get_height() * 2:
		extended[level.ground_tilesets.size()] = 1
	return extended


## Mirrors w3_gpu_terrain.gd _prepare_romp_tiles/_mark_romp_tile, driven
## entirely by level.get_ramp_transition (already C#, W3EMapBridge).
func _prepare_romp_tiles(level) -> Dictionary:
	var tiles_x: int = level.tile_count_x()
	var tiles_y: int = level.tile_count_y()
	var mask := PackedByteArray()
	mask.resize(tiles_x * tiles_y)
	mask.fill(0)
	for y in range(tiles_y):
		for x in range(tiles_x):
			var transition: Dictionary = level.get_ramp_transition(x, y)
			if transition.is_empty():
				continue
			_mark_romp_tile(mask, tiles_x, tiles_y, transition.get("source", Vector2i(x, y)))
			_mark_romp_tile(mask, tiles_x, tiles_y, transition.get("hide_neighbor", Vector2i(-1, -1)))
	return {"mask": mask, "tiles_x": tiles_x, "tiles_y": tiles_y}


func _mark_romp_tile(mask: PackedByteArray, tiles_x: int, tiles_y: int, tile: Vector2i) -> void:
	if tile.x < 0 or tile.y < 0 or tile.x >= tiles_x or tile.y >= tiles_y:
		return
	mask[tile.y * tiles_x + tile.x] = 1


func _tile_is_romp(romp: Dictionary, x: int, y: int) -> bool:
	var tiles_x: int = romp["tiles_x"]
	var tiles_y: int = romp["tiles_y"]
	if x < 0 or y < 0 or x >= tiles_x or y >= tiles_y:
		return false
	var mask: PackedByteArray = romp["mask"]
	return mask[y * tiles_x + x] != 0


func _hivewe_corner_texture(level, cliff_to_ground: PackedInt32Array, x: int, y: int) -> int:
	var c = level.get_corner(x, y)
	if c == null:
		return 0
	var near_cliff := false
	var near_romp := false
	for ty in range(y - 1, y + 1):
		for tx in range(x - 1, x + 1):
			if tx < 0 or ty < 0 or tx >= level.tile_count_x() or ty >= level.tile_count_y():
				continue
			near_cliff = near_cliff or level.tile_has_layer_cliff(tx, ty)
			near_romp = near_romp or level.ramp_transition_covers_tile(tx, ty)
	if near_romp or (near_cliff and not bool(c.ramp)):
		var cliff_texture := int(c.cliff_texture)
		if cliff_texture == 15:
			cliff_texture = 1
		if cliff_texture >= 0 and cliff_texture < cliff_to_ground.size():
			return int(cliff_to_ground[cliff_texture])
	if bool(c.blight):
		return level.ground_tilesets.size()
	return int(c.ground_texture)


func _warsmash_corner_texture(level, cliff_to_ground: PackedInt32Array, romp: Dictionary, x: int, y: int) -> int:
	var current = level.get_corner(x, y)
	if current == null:
		return 0
	var suppress_cliff := false
	for ox in range(-1, 1):
		for oy in range(-1, 1):
			var tx := x + ox
			var ty := y + oy
			if tx < 0 or ty < 0 or tx >= level.tile_count_x() or ty >= level.tile_count_y():
				continue
			var is_cliff: bool = level.tile_has_layer_cliff(tx, ty)
			if is_cliff and _warsmash_full_ramp_without_romp(level, romp, tx, ty):
				suppress_cliff = true
				break
			if _tile_is_romp(romp, tx, ty) or is_cliff:
				var owner = level.get_corner(tx, ty)
				var cliff_texture := int(owner.cliff_texture)
				if cliff_texture == 15:
					cliff_texture = 1
				if cliff_texture >= 0 and cliff_texture < cliff_to_ground.size():
					return int(cliff_to_ground[cliff_texture])
		if suppress_cliff:
			break
	if bool(current.blight):
		return level.ground_tilesets.size()
	return int(current.ground_texture)


func _warsmash_full_ramp_without_romp(level, romp: Dictionary, x: int, y: int) -> bool:
	if not level.is_ramp_entrance(x, y):
		return false
	for oy in range(2):
		for ox in range(2):
			if _tile_is_romp(romp, x + ox, y + oy):
				return false
	return true


func _mask_orientation_mismatches(level, provider) -> Array[String]:
	var mismatches: Array[String] = []
	for tile_id_value in level.ground_tilesets:
		var tile_id := str(tile_id_value)
		var image: Image = provider.get_ground_image(tile_id)
		if image == null:
			mismatches.append("%s image missing" % tile_id)
			continue
		var extended := image.get_width() >= image.get_height() * 2
		# Composite masks may intentionally spill into nominally absent corners;
		# the four single-corner masks are the unambiguous orientation anchors.
		for sub in [1, 2, 4, 8]:
			var cell: Image = _slice_cell(image, sub, extended)
			var observed := 0
			# Shader UV is (local.x, 1-local.y); Image coordinates are top-down.
			if _patch_alpha(cell, 55, 55) > 0.9: observed |= 1 # BR
			if _patch_alpha(cell, 8, 55) > 0.9: observed |= 2 # BL
			if _patch_alpha(cell, 55, 8) > 0.9: observed |= 4 # TR
			if _patch_alpha(cell, 8, 8) > 0.9: observed |= 8 # TL
			if observed != sub:
				mismatches.append("%s sub=%d observed=%d alpha=[%.2f,%.2f,%.2f,%.2f]" % [
					tile_id, sub, observed,
					_patch_alpha(cell, 55, 55), _patch_alpha(cell, 8, 55),
					_patch_alpha(cell, 55, 8), _patch_alpha(cell, 8, 8),
				])
	return mismatches


## Ported straight from the retired w3_gpu_terrain.gd _slice_cell (pure Image
## math, never depended on the terrain object's own state).
func _slice_cell(img: Image, sub: int, extended: bool) -> Image:
	var out := Image.create(_CELL_SIZE, _CELL_SIZE, false, Image.FORMAT_RGBA8)
	out.fill(Color(0, 0, 0, 0))
	if img == null or (sub >= 16 and not extended):
		return out
	var block_x := 0
	var local := sub
	if sub >= 16:
		block_x = 4
		local = sub - 16
	var csz: int = int(img.get_height() / 4)
	if csz <= 0:
		return out
	var sx: int = (block_x + (local % 4)) * csz
	var sy: int = int(local / 4) * csz
	if sx + csz > img.get_width() or sy + csz > img.get_height():
		return out
	var cell := img.get_region(Rect2i(sx, sy, csz, csz))
	if csz != _CELL_SIZE:
		cell.resize(_CELL_SIZE, _CELL_SIZE, Image.INTERPOLATE_BILINEAR)
	out.blit_rect(cell, Rect2i(0, 0, _CELL_SIZE, _CELL_SIZE), Vector2i(0, 0))
	return out


func _patch_alpha(image: Image, center_x: int, center_y: int) -> float:
	var total := 0.0
	var count := 0
	for y in range(center_y - 3, center_y + 4):
		for x in range(center_x - 3, center_x + 4):
			total += image.get_pixel(x, y).a
			count += 1
	return total / float(count)


func _hivewe_romp_mask(level) -> PackedByteArray:
	var tiles_x: int = level.tile_count_x()
	var tiles_y: int = level.tile_count_y()
	var mask := PackedByteArray()
	mask.resize(tiles_x * tiles_y)
	mask.fill(0)
	for i in range(tiles_x):
		for j in range(tiles_y):
			if j < tiles_y - 1 and _hivewe_vertical_ramp(level, i, j):
				mask[j * tiles_x + i] = 1
				mask[(j + 1) * tiles_x + i] = 1
				continue
			if i < tiles_x - 1 and _hivewe_horizontal_ramp(level, i, j):
				mask[j * tiles_x + i] = 1
				mask[j * tiles_x + i + 1] = 1
	return mask


func _hivewe_vertical_ramp(level, x: int, y: int) -> bool:
	var bl = level.get_corner(x, y)
	var br = level.get_corner(x + 1, y)
	var tl = level.get_corner(x, y + 1)
	var tr = level.get_corner(x + 1, y + 1)
	var ttl = level.get_corner(x, y + 2)
	var ttr = level.get_corner(x + 1, y + 2)
	if bl == null or br == null or tl == null or tr == null or ttl == null or ttr == null:
		return false
	var ae := mini(int(bl.layer_height), int(ttl.layer_height))
	var cf := mini(int(br.layer_height), int(ttr.layer_height))
	if int(tl.layer_height) != ae or int(tr.layer_height) != cf:
		return false
	if not (bool(bl.ramp) == bool(tl.ramp) and bool(bl.ramp) == bool(ttl.ramp)):
		return false
	if not (bool(br.ramp) == bool(tr.ramp) and bool(br.ramp) == bool(ttr.ramp)):
		return false
	if bool(bl.ramp) == bool(br.ramp):
		return false
	return _cliff_trans_exists([ttl, ttr, br, bl], mini(ae, cf))


func _hivewe_horizontal_ramp(level, x: int, y: int) -> bool:
	var bl = level.get_corner(x, y)
	var br = level.get_corner(x + 1, y)
	var tl = level.get_corner(x, y + 1)
	var tr = level.get_corner(x + 1, y + 1)
	var brr = level.get_corner(x + 2, y)
	var trr = level.get_corner(x + 2, y + 1)
	if bl == null or br == null or tl == null or tr == null or brr == null or trr == null:
		return false
	var ae := mini(int(bl.layer_height), int(brr.layer_height))
	var bf := mini(int(tl.layer_height), int(trr.layer_height))
	if int(br.layer_height) != ae or int(tr.layer_height) != bf:
		return false
	if not (bool(bl.ramp) == bool(br.ramp) and bool(bl.ramp) == bool(brr.ramp)):
		return false
	if not (bool(tl.ramp) == bool(tr.ramp) and bool(tl.ramp) == bool(trr.ramp)):
		return false
	if bool(bl.ramp) == bool(tl.ramp):
		return false
	return _cliff_trans_exists([tl, trr, brr, bl], mini(ae, bf))


func _cliff_trans_exists(corners: Array, base: int) -> bool:
	var tag := ""
	for corner in corners:
		var diff := int(corner.layer_height) - base
		var code := 76 if bool(corner.ramp) else 65 # L / A
		code += diff * (-4 if bool(corner.ramp) else 1)
		tag += String.chr(code)
	return not W3PathResolver.new().resolve_path("Doodads/Terrain/CliffTrans/CliffTrans%s0.mdx" % tag).is_empty()
