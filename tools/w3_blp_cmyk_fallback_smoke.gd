extends SceneTree
## Regression check: opaque PNG sidecars must win when a CMYK BLP JPEG cannot
## be converted on the current platform. Cliff1 is a real Lost Temple asset.

const W3BlpDecoder = preload("res://addons/w3kit_cs/Assets/BlpDecoderBridge.cs")


func _init() -> void:
	var blp := ProjectSettings.globalize_path("res://war3/game/ReplaceableTextures/Cliff/Cliff1.blp")
	var image := W3BlpDecoder.load_image(blp)
	if image == null:
		push_error("W3_BLP_CMYK_FALLBACK_FAIL image missing")
		quit(1)
		return
	var sample := image.get_pixel(0, 0)
	var expected := Color8(148, 119, 92, 255)
	var diff: float = (
		absf(sample.r - expected.r) + absf(sample.g - expected.g)
		+ absf(sample.b - expected.b) + absf(sample.a - expected.a)
	)
	if diff > 0.04:
		push_error("W3_BLP_CMYK_FALLBACK_FAIL got=%s expected=%s" % [sample, expected])
		quit(1)
		return
	print("W3_BLP_CMYK_FALLBACK_OK")
	quit(0)
