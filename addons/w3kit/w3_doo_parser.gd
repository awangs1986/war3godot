extends RefCounted
const W3BinaryReader = preload("res://addons/w3kit/w3_binary_reader.gd")
## war3map.doo / war3mapUnits.doo — layout aligned with HiveWE
## (src/base/doodads.ixx, src/base/units.ixx).


class Placement:
	var id: String = ""
	var variation: int = 0
	var position: Vector3 = Vector3.ZERO
	var angle: float = 0.0
	var scale: Vector3 = Vector3.ONE
	var skin_id: String = ""
	var flags: int = 0
	var player_owner: int = 0
	var life: int = 100
	var is_unit: bool = false
	var model_path: String = ""
	var creation_number: int = 0


static func parse_doodads(data: PackedByteArray) -> Array:
	## Mirrors HiveWE Doodads::load (classic maps: version 7/8).
	var out: Array = []
	if data.size() < 12:
		return out
	var r = W3BinaryReader.new(data)
	var magic = r.read_fourcc()
	if magic != "W3do":
		push_warning("W3DooParser: doodad magic %s" % magic)
		return out
	var version = r.read_i32()
	var _subversion = r.read_i32()
	var count = r.read_i32()
	count = clampi(count, 0, 100000)
	for _i in count:
		# id(4)+var(4)+pos(12)+angle(4)+scale(12)+flags(1)+life(1)+editor(4) ≈ 42 min
		if r.remaining() < 42:
			break
		var p = Placement.new()
		p.id = r.read_fourcc()
		p.is_unit = false
		p.variation = r.read_i32()
		var px = r.read_f32()
		var pz = r.read_f32() # War3 Y → Godot Z
		var py = r.read_f32() # War3 Z → Godot Y
		p.position = Vector3(px, py, pz)
		p.angle = r.read_f32()
		var sx = r.read_f32()
		var sy = r.read_f32()
		var sz = r.read_f32()
		p.scale = Vector3(sx, sz, sy)
		p.skin_id = p.id
		p.flags = r.read_u8() # state
		p.life = r.read_u8()
		if version >= 8:
			if r.remaining() < 8:
				break
			r.read_i32() # item_table_pointer
			var item_sets = clampi(r.read_i32(), 0, 256)
			var sets_ok := true
			for _s in item_sets:
				if r.remaining() < 4:
					sets_ok = false
					break
				var n = clampi(r.read_i32(), 0, 256)
				for _it in n:
					if r.remaining() < 8:
						sets_ok = false
						break
					r.read_fourcc()
					r.read_i32()
				if not sets_ok:
					break
			if not sets_ok:
				break
		if r.remaining() < 4:
			break
		p.creation_number = r.read_i32()
		out.append(p)
	# Special/terrain doodads (HiveWE / Warsmash second section).
	var special: Array = _parse_special_doodads(r)
	if not special.is_empty():
		print("W3DooParser: special/terrain doodads=", special.size())
		out.append_array(special)
	print("W3DooParser: doodads=", out.size(), "/", count, " ver=", version)
	return out


static func _parse_special_doodads(r) -> Array:
	## After main doodads: int special_count; each: id(4) + z(4?)/pos depending on version.
	## HiveWE doodads.ixx: after doodads, reads special doodads with id + position (no angle).
	var out: Array = []
	if r.remaining() < 4:
		return out
	var special_count = r.read_i32()
	special_count = clampi(special_count, 0, 50000)
	for _i in special_count:
		if r.remaining() < 16:
			break
		var p = Placement.new()
		p.id = r.read_fourcc()
		p.is_unit = false
		p.variation = 0
		var px = r.read_f32()
		var pz = r.read_f32()
		var py = r.read_f32()
		p.position = Vector3(px, py, pz)
		p.angle = 0.0
		p.scale = Vector3.ONE
		p.skin_id = p.id
		out.append(p)
	return out


static func parse_units(data: PackedByteArray) -> Array:
	## Mirrors HiveWE Units::load — critical: two unknown bytes after player;
	## item_table_pointer / hero stats gated on subversion >= 11.
	var out: Array = []
	if data.size() < 12:
		return out
	var r = W3BinaryReader.new(data)
	var magic = r.read_fourcc()
	if magic != "W3do":
		push_warning("W3DooParser: units magic %s" % magic)
		return out
	var version = r.read_i32()
	var subversion = r.read_i32()
	var count = r.read_i32()
	count = clampi(count, 0, 100000)
	for _i in count:
		if r.remaining() < 50:
			break
		var p = Placement.new()
		p.id = r.read_fourcc()
		p.is_unit = true
		p.variation = r.read_i32()
		var px = r.read_f32()
		var pz = r.read_f32()
		var py = r.read_f32()
		p.position = Vector3(px, py, pz)
		p.angle = r.read_f32()
		var sx = r.read_f32()
		var sy = r.read_f32()
		var sz = r.read_f32()
		p.scale = Vector3(sx, sz, sy)
		# skin_id only for game >= 1.32 — classic maps omit it (HiveWE MapInfo check).
		p.skin_id = p.id
		p.flags = r.read_u8()
		p.player_owner = r.read_i32()
		# HiveWE: unknown1 + unknown2 (TWO bytes) — was the main parse desync.
		if r.remaining() < 2:
			break
		r.read_u8()
		r.read_u8()
		if r.remaining() < 8:
			break
		r.read_i32() # health
		r.read_i32() # mana
		if subversion >= 11:
			if r.remaining() < 4:
				break
			r.read_i32() # item_table_pointer
		# item_sets always present
		if r.remaining() < 4:
			break
		var item_sets = clampi(r.read_i32(), 0, 256)
		var sets_ok := true
		for _s in item_sets:
			if r.remaining() < 4:
				sets_ok = false
				break
			var n = clampi(r.read_i32(), 0, 256)
			for _it in n:
				if r.remaining() < 8:
					sets_ok = false
					break
				r.read_fourcc()
				r.read_i32()
			if not sets_ok:
				break
		if not sets_ok:
			break
		if r.remaining() < 12:
			break
		r.read_i32() # gold
		r.read_f32() # target_acquisition
		r.read_i32() # level
		if subversion >= 11:
			if r.remaining() < 12:
				break
			r.read_i32() # strength
			r.read_i32() # agility
			r.read_i32() # intelligence
		# inventory
		if r.remaining() < 4:
			break
		var inv = clampi(r.read_i32(), 0, 256)
		var inv_ok := true
		for _k in inv:
			if r.remaining() < 8:
				inv_ok = false
				break
			r.read_i32() # slot
			r.read_fourcc()
		if not inv_ok:
			break
		# abilities
		if r.remaining() < 4:
			break
		var abil = clampi(r.read_i32(), 0, 256)
		var abil_ok := true
		for _a in abil:
			if r.remaining() < 12:
				abil_ok = false
				break
			r.read_fourcc()
			r.read_i32() # autocast
			r.read_i32() # level
		if not abil_ok:
			break
		# random blob (HiveWE switch on random_type — unknown types read nothing)
		if r.remaining() < 4:
			break
		var random_type = r.read_i32()
		match random_type:
			0:
				if r.remaining() < 4:
					break
				r.read_bytes(4)
			1:
				if r.remaining() < 8:
					break
				r.read_bytes(8)
			2:
				if r.remaining() < 4:
					break
				var nrand = clampi(r.read_i32(), 0, 256)
				if r.remaining() < nrand * 8:
					break
				r.read_bytes(nrand * 8)
			_:
				# HiveWE: no extra bytes for other values (e.g. -1 / 0xFFFFFFFF)
				pass
		if r.remaining() < 12:
			break
		r.read_i32() # custom_color
		r.read_i32() # waygate
		p.creation_number = r.read_i32()
		out.append(p)
	print("W3DooParser: units=", out.size(), "/", count, " ver=", version, " sub=", subversion)
	return out
