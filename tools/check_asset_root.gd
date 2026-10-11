extends SceneTree
## godot --headless --path . -s res://tools/check_asset_root.gd


func _init() -> void:
	var cfg = preload("res://addons/w3kit_cs/Config/W3AssetConfiguration.cs").new()
	cfg.load_config()
	var report: Dictionary = cfg.check_report()
	print("=== W3 Asset Root Check ===")
	for m in report["messages"]:
		print(m)
	print("ok=", report["ok"])
	# Unset root is expected until user configures — exit 0 if config loads.
	quit(0)
