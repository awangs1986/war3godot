extends RefCounted
## Little-endian binary reader over a PackedByteArray.


var _data: PackedByteArray
var pos: int = 0
var failed: bool = false
var error_message: String = ""


func _init(data: PackedByteArray = PackedByteArray()) -> void:
	_data = data
	pos = 0


func set_data(data: PackedByteArray) -> void:
	_data = data
	pos = 0
	failed = false
	error_message = ""


func remaining() -> int:
	return maxi(0, _data.size() - pos)


func can_read(byte_count: int) -> bool:
	return byte_count >= 0 and pos >= 0 and pos + byte_count <= _data.size()


func _require(byte_count: int) -> bool:
	if can_read(byte_count):
		return true
	failed = true
	if error_message.is_empty():
		error_message = "read past end at %d: need %d byte(s), size=%d" % [pos, byte_count, _data.size()]
	pos = _data.size()
	return false


func seek(p: int) -> void:
	pos = clampi(p, 0, _data.size())


func read_u8() -> int:
	if not _require(1):
		return 0
	var v = _data[pos]
	pos += 1
	return v


func read_i16() -> int:
	if not _require(2):
		return 0
	var v = _data.decode_s16(pos)
	pos += 2
	return v


func read_u16() -> int:
	if not _require(2):
		return 0
	var v = _data.decode_u16(pos)
	pos += 2
	return v


func read_i32() -> int:
	if not _require(4):
		return 0
	var v = _data.decode_s32(pos)
	pos += 4
	return v


func read_u32() -> int:
	if not _require(4):
		return 0
	var v = _data.decode_u32(pos)
	pos += 4
	return v


func read_f32() -> float:
	if not _require(4):
		return 0.0
	var v = _data.decode_float(pos)
	pos += 4
	return v


func read_bytes(n: int) -> PackedByteArray:
	if not _require(n):
		return PackedByteArray()
	var out = _data.slice(pos, pos + n)
	pos += n
	return out


func read_fourcc() -> String:
	return read_bytes(4).get_string_from_ascii()


func read_char() -> String:
	return String.chr(read_u8())


## Null-terminated UTF-8/ASCII until first 0 (does not scan padding NULs as UTF-8).
func read_cstring() -> String:
	var start = pos
	while pos < _data.size() and _data[pos] != 0:
		pos += 1
	var s = cstring_from_padded(_data.slice(start, pos))
	if pos < _data.size():
		pos += 1
	return s


## Fixed-width MDX field (e.g. name[80], path[260]) — stop at first NUL, ASCII-safe.
func read_fixed_cstring(n: int) -> String:
	return cstring_from_padded(read_bytes(n))


## Decode a padded C string without feeding trailing NULs to the UTF-8 parser
## (avoids Godot "Unexpected NUL character" spam on MDX/BLP metadata).
static func cstring_from_padded(data: PackedByteArray) -> String:
	if data.is_empty():
		return ""
	var end := data.size()
	for i in end:
		if data[i] == 0:
			end = i
			break
	if end <= 0:
		return ""
	return data.slice(0, end).get_string_from_ascii().strip_edges()
