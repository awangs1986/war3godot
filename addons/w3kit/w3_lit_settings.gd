extends Resource
## User-facing outdoor lighting knobs (Godot lights, War3-ish mood).
## Keep this small — only what map authors commonly tweak.


@export_group("Sun")
@export_range(0.0, 3.0, 0.01) var sun_intensity: float = 0.95
## Blackbody-ish color for the key light (K). 5500 ≈ noon, 4000 warmer dusk.
@export_range(2500.0, 10000.0, 50.0) var sun_temperature_k: float = 5500.0
@export_range(-80.0, -10.0, 0.5) var sun_pitch_deg: float = -52.0
@export_range(-180.0, 180.0, 0.5) var sun_yaw_deg: float = 40.0
@export var sun_shadows: bool = true

@export_group("Fill")
@export_range(0.0, 2.0, 0.01) var ambient_intensity: float = 0.62
@export_range(0.0, 1.5, 0.01) var fill_intensity: float = 0.28

@export_group("Fog")
## Off while editing so the map stays clear. On for play / mood preview.
@export var fog_enabled: bool = false
## World units: fog starts after this distance from camera (depth fog begin).
@export_range(0.0, 20000.0, 50.0) var fog_distance: float = 4000.0
## 0 = soft/far haze, 1 = thicker sooner.
@export_range(0.0, 1.0, 0.01) var fog_intensity: float = 0.35



static func default_settings():
	return load("res://addons/w3kit/w3_lit_settings.gd").new()


## Approximate CIE daylight / blackbody to RGB for DirectionalLight color.
static func temperature_to_color(kelvin: float) -> Color:
	var k := clampf(kelvin, 1000.0, 40000.0) / 100.0
	var r := 1.0
	var g := 1.0
	var b := 1.0
	if k <= 66.0:
		r = 1.0
		g = clampf(0.3900815788 * log(k) - 0.6318414438, 0.0, 1.0)
	else:
		r = clampf(1.292936186 * pow(k - 60.0, -0.1332047592), 0.0, 1.0)
		g = clampf(1.1298908609 * pow(k - 60.0, -0.0755148492), 0.0, 1.0)
	if k >= 66.0:
		b = 1.0
	elif k <= 19.0:
		b = 0.0
	else:
		b = clampf(0.5432067891 * log(k - 10.0) - 1.1962540795, 0.0, 1.0)
	return Color(r, g, b)
