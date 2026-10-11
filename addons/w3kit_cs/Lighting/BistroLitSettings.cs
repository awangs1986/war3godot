using Godot;

namespace Diablo1Like.W3.Lighting;

/// <summary>
/// 取材自 Jamsers/Bistro-Demo-Tweaked 的物理太阳 + ACES 后处理组合。
/// 独立于 War3 / UE-Sky3D 预设，赋值给 W3Sandbox.lit_settings 即可切换。
/// 属性名沿用旧 GDS/.tres 小写下划线命名以兼容动态 GDScript 调用方。
/// </summary>
[GlobalClass]
public partial class BistroLitSettings : Resource
{
    public enum Scenario
    {
        Noon = 0,
        Afternoon = 1,
        Dusk = 2,
        Night = 3,
    }

    [ExportGroup("Scenario")]
    // 对应 Bistro Light Change Utility 预设（可读性优先的电影化太阳角度）。
    [Export] public Scenario scenario { get; set; } = Scenario.Afternoon;

    [ExportGroup("Overrides (optional)")]
    [Export] public bool manual_override { get; set; }
    [Export(PropertyHint.Range, "0.0,4.0,0.01")] public float sun_energy { get; set; } = 1.55f;
    [Export(PropertyHint.Range, "2500.0,15000.0,50.0")] public float sun_temperature_k { get; set; } = 4250.0f;
    [Export(PropertyHint.Range, "-80.0,-5.0,0.5")] public float sun_pitch_deg { get; set; } = -48.0f;
    [Export(PropertyHint.Range, "-180.0,180.0,0.5")] public float sun_yaw_deg { get; set; } = 35.0f;
    [Export] public bool sun_shadows { get; set; } = true;

    [ExportGroup("Look (Bistro stack)")]
    [Export(PropertyHint.Range, "0.2,2.5,0.01")] public float exposure { get; set; } = 1.05f;
    [Export(PropertyHint.Range, "0.5,1.5,0.01")] public float saturation { get; set; } = 1.17f;
    [Export] public bool glow_enabled { get; set; } = true;
    [Export(PropertyHint.Range, "0.0,2.0,0.01")] public float glow_intensity { get; set; } = 0.85f;
    [Export(PropertyHint.Range, "0.0,2.0,0.01")] public float glow_strength { get; set; } = 0.7f;
    // 160² War3 地图上开销较高——默认关闭（Bistro 室内场景保留开启）。
    [Export] public bool sdfgi_enabled { get; set; }
    [Export] public bool ssil_enabled { get; set; }
    [Export] public bool ssr_enabled { get; set; }
    [Export(PropertyHint.Range, "0.0,4.0,0.05")] public float ssao_intensity { get; set; } = 1.6f;

    [ExportGroup("Fog")]
    [Export] public bool fog_enabled { get; set; } = true;
    [Export(PropertyHint.Range, "0.0,20000.0,50.0")] public float fog_distance { get; set; } = 4800.0f;
    [Export(PropertyHint.Range, "0.0,1.0,0.01")] public float fog_intensity { get; set; } = 0.2f;

    private readonly record struct ScenarioLook(
        float Temp, float Energy, float Pitch, float Yaw, float SkyEnergy, float Ambient, float Exposure);

    /// <summary>Bistro LightingScenarios.tres 提炼版 → 非物理 energy + pitch/yaw。</summary>
    public static ScenarioParams GetScenarioParams(Scenario scenario)
    {
        ScenarioLook look = scenario switch
        {
            Scenario.Noon => new ScenarioLook(5500.0f, 1.85f, -62.0f, 25.0f, 1.55f, 1.05f, 1.0f),
            // MainScene 默认太阳 ≈ 下午（4250K / 约 75k lux）。
            Scenario.Afternoon => new ScenarioLook(4250.0f, 1.55f, -48.0f, 35.0f, 1.25f, 0.95f, 1.05f),
            Scenario.Dusk => new ScenarioLook(2000.0f, 0.95f, -28.0f, 40.0f, 0.65f, 0.7f, 1.15f),
            Scenario.Night => new ScenarioLook(15000.0f, 0.12f, -55.0f, -20.0f, 0.12f, 0.35f, 1.35f),
            _ => new ScenarioLook(4250.0f, 1.55f, -48.0f, 35.0f, 1.25f, 0.95f, 1.05f),
        };
        return new ScenarioParams(look.Temp, look.Energy, look.Pitch, look.Yaw, look.SkyEnergy, look.Ambient, look.Exposure);
    }

    /// <summary>场景光照参数（沿用 GDS Dictionary 语义的强类型版本）。</summary>
    public readonly struct ScenarioParams
    {
        public readonly float Temp;
        public readonly float Energy;
        public readonly float Pitch;
        public readonly float Yaw;
        public readonly float SkyEnergy;
        public readonly float Ambient;
        public readonly float Exposure;

        public ScenarioParams(float temp, float energy, float pitch, float yaw, float skyEnergy, float ambient, float exposure)
        {
            Temp = temp;
            Energy = energy;
            Pitch = pitch;
            Yaw = yaw;
            SkyEnergy = skyEnergy;
            Ambient = ambient;
            Exposure = exposure;
        }
    }
}
