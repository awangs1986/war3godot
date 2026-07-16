extends RefCounted
const W3BinaryReader = preload("res://addons/w3kit/w3_binary_reader.gd")
## war3map.wpm (MP3W) — Warsmash War3MapWpm layout.


var version: int = 0
var width: int = 0
var height: int = 0
## PackedByteArray of pathing flags per cell (uint8), row-major y*width+x.
var pathing: PackedByteArray = PackedByteArray()


static func load_bytes(data: PackedByteArray):
	if data.is_empty() or data.size() < 12:
		return null
	var r = W3BinaryReader.new(data)
	var magic := r.read_fourcc()
	if magic != "MP3W":
		push_error("W3Wpm: bad magic '%s'" % magic)
		return null
	var wpm = new()
	wpm.version = r.read_i32()
	wpm.width = r.read_i32()
	wpm.height = r.read_i32()
	var n: int = wpm.width * wpm.height
	if n <= 0 or r.remaining() < n:
		push_error("W3Wpm: size %dx%d vs remaining %d" % [wpm.width, wpm.height, r.remaining()])
		return null
	wpm.pathing = r.read_bytes(n)
	return wpm


static func load_file(path: String):
	if not FileAccess.file_exists(path):
		return null
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return null
	return load_bytes(f.get_buffer(f.get_length()))
