extends Resource
class_name BistroLitSettings
## Distilled from Jamsers/Bistro-Demo-Tweaked lighting ideas (physical sun, ACES stack).
## Independent of War3 / UE-Sky3D presets — assign to W3Sandbox.lit_settings to switch.
## Does NOT enable project-wide physical light units (would break War3).


enum Scenario {
	NOON = 0,
	AFTERNOON = 1,
	DUSK = 2,
	NIGHT = 3,
}

@export_group("Scenario")
## Matches Bistro Light Change Utility presets (filmic sun angles for readability).
@export var scenario: Scenario = Scenario.AFTERNOON

@export_group("Overrides (optional)")
## If true, ignore scenario table and use the knobs below.
@export var manual_override: bool = false
@export_range(0.0, 4.0, 0.01) var sun_energy: float = 1.55
@export_range(2500.0, 15000.0, 50.0) var sun_temperature_k: float = 4250.0
@export_range(-80.0, -5.0, 0.5) var sun_pitch_deg: float = -48.0
@export_range(-180.0, 180.0, 0.5) var sun_yaw_deg: float = 35.0
@export var sun_shadows: bool = true

@export_group("Look (Bistro stack)")
@export_range(0.2, 2.5, 0.01) var exposure: float = 1.05
@export_range(0.5, 1.5, 0.01) var saturation: float = 1.17
@export var glow_enabled: bool = true
@export_range(0.0, 2.0, 0.01) var glow_intensity: float = 0.85
@export_range(0.0, 2.0, 0.01) var glow_strength: float = 0.7
## Costly on 160² War3 maps — default off (Bistro indoor street leaves it on).
@export var sdfgi_enabled: bool = false
@export var ssil_enabled: bool = false
@export var ssr_enabled: bool = false
@export_range(0.0, 4.0, 0.05) var ssao_intensity: float = 1.6

@export_group("Fog")
@export var fog_enabled: bool = true
@export_range(0.0, 20000.0, 50.0) var fog_distance: float = 4800.0
@export_range(0.0, 1.0, 0.01) var fog_intensity: float = 0.2


## Bistro LightingScenarios.tres distilled → non-physical energy + pitch/yaw.
static func scenario_params(s: Scenario) -> Dictionary:
	match s:
		Scenario.NOON:
			return {
				"temp": 5500.0, "energy": 1.85, "pitch": -62.0, "yaw": 25.0,
				"sky_energy": 1.55, "ambient": 1.05, "exposure": 1.0,
			}
		Scenario.AFTERNOON:
			# MainScene default sun ≈ afternoon (4250K / 75k lux).
			return {
				"temp": 4250.0, "energy": 1.55, "pitch": -48.0, "yaw": 35.0,
				"sky_energy": 1.25, "ambient": 0.95, "exposure": 1.05,
			}
		Scenario.DUSK:
			return {
				"temp": 2000.0, "energy": 0.95, "pitch": -28.0, "yaw": 40.0,
				"sky_energy": 0.65, "ambient": 0.7, "exposure": 1.15,
			}
		Scenario.NIGHT:
			return {
				"temp": 15000.0, "energy": 0.12, "pitch": -55.0, "yaw": -20.0,
				"sky_energy": 0.12, "ambient": 0.35, "exposure": 1.35,
			}
	return scenario_params(Scenario.AFTERNOON)
