extends Resource
class_name UeStyleLitSettings
## UE5-empty-level inspired outdoor lighting for Godot 4 Forward+.
## Assign to W3Sandbox.lit_settings. War3 uses w3_lit_settings_*.tres instead — never mixed.


enum SkyBackend {
	## Built-in PhysicalSkyMaterial (Preetham / Nishita-style daylight). No extra addon.
	PHYSICAL_NISHITA = 0,
	## Tokisan Sky3D atmospheric sky + clouds + managed sun/moon (addons/sky_3d).
	SKY3D = 1,
}

@export_group("Sky backend")
## PHYSICAL_NISHITA = engine PhysicalSky; SKY3D = Tokisan addon (UE path only).
@export var sky_backend: SkyBackend = SkyBackend.SKY3D
## Sky3D clock (hours). Ignored for PHYSICAL_NISHITA (uses sun pitch/yaw).
@export_range(0.0, 23.99, 0.01) var sky3d_hour: float = 10.5
## Let Sky3D time advance in-game (usually off for map preview).
@export var sky3d_time_progress: bool = false

@export_group("Sun")
@export_range(0.0, 4.0, 0.01) var sun_intensity: float = 1.35
## ~6500K ≈ UE default daylight temperature (PHYSICAL_NISHITA).
@export_range(2500.0, 10000.0, 50.0) var sun_temperature_k: float = 6500.0
@export_range(-80.0, -10.0, 0.5) var sun_pitch_deg: float = -52.0
@export_range(-180.0, 180.0, 0.5) var sun_yaw_deg: float = 35.0
@export var sun_shadows: bool = true
## Softens sun disk / shadow penumbra (degrees).
@export_range(0.0, 5.0, 0.05) var sun_angular_distance: float = 0.53

@export_group("Sky")
@export_range(0.2, 4.0, 0.01) var sky_energy: float = 1.35
@export_range(1.0, 20.0, 0.1) var sky_turbidity: float = 4.0
@export_range(0.0, 2.0, 0.01) var ambient_from_sky: float = 1.15

@export_group("Look")
@export_range(0.2, 2.5, 0.01) var exposure: float = 1.05
@export var glow_enabled: bool = true
@export_range(0.0, 2.0, 0.01) var glow_intensity: float = 0.4
## Expensive on huge War3 maps — leave off unless you want contact bounce.
@export var sdfgi_enabled: bool = false
@export var ssil_enabled: bool = false

@export_group("Fog")
@export var fog_enabled: bool = true
@export_range(0.0, 20000.0, 50.0) var fog_distance: float = 4200.0
@export_range(0.0, 1.0, 0.01) var fog_intensity: float = 0.22
