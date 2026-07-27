extends SceneTree
## Per-geoset alpha of HeroBladeMaster on the Stand and Walk sequences, plus
## what the animated instance actually applies. Reveals death/dissipate quads
## leaking into the standing pose.

const W3MdxModel = preload("res://addons/w3kit_cs/Mdx/W3MdxModel.cs")
const W3Paths = preload("res://addons/w3kit_cs/Paths/W3PathResolverBridge.cs")


func _initialize() -> void:
	var rel := "Units/Orc/HeroBladeMaster/HeroBladeMaster.mdx"
	var abs_path := W3Paths.new().resolve_reforged_path(rel)
	if abs_path.is_empty():
		abs_path = W3Paths.new().resolve_path(rel)
	var model = W3MdxModel.load_file(abs_path)
	for si in model.sequences.size():
		var s = model.sequences[si]
		print("seq %d '%s' [%d..%d]" % [si, s.name, s.interval_start, s.interval_end])
	for si in model.sequences.size():
		var s = model.sequences[si]
		var sname := str(s.name).to_lower()
		if not (sname.begins_with("stand") or sname.begins_with("walk") or sname.begins_with("dissipate")):
			continue
		var t: int = int(s.interval_start) + (int(s.interval_end) - int(s.interval_start)) / 2
		var alphas := ""
		for gi in model.geosets.size():
			alphas += "g%d=%.2f " % [gi, model.geoset_alpha_at(gi, t)]
		print("'%s' mid t=%d: %s" % [s.name, t, alphas])
	quit(0)
