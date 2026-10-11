using Godot;

namespace Diablo1Like.W3.Lighting;

/// <summary>
/// 户外光照可调参数（War3 风格，烘焙 BLP 贴图配合无光照地形）。
/// 属性名刻意保持与旧 GDS/.tres 一致的小写下划线命名，
/// 以兼容仍未迁移的 GDScript 调用方（w3_sandbox.gd 用 "." / get()/set() 动态访问）。
/// </summary>
[GlobalClass]
public partial class W3LitSettings : Resource
{
    [ExportGroup("Weather")]
    // "default" 保留旧观感；"sun" = 正午硬光；"overcast" = 阴天柔光 + 近处保持清晰的远雾。
    [Export] public string weather { get; set; } = "default";

    [ExportGroup("Sun")]
    [Export(PropertyHint.Range, "0.0,3.0,0.01")] public float sun_intensity { get; set; } = 1.25f;
    // 类黑体色温（K）。5500 约等于正午,4000 偏暖黄昏。
    [Export(PropertyHint.Range, "2500.0,10000.0,50.0")] public float sun_temperature_k { get; set; } = 5000.0f;
    [Export(PropertyHint.Range, "-80.0,-10.0,0.5")] public float sun_pitch_deg { get; set; } = -42.0f;
    [Export(PropertyHint.Range, "-180.0,180.0,0.5")] public float sun_yaw_deg { get; set; } = 40.0f;
    [Export] public bool sun_shadows { get; set; } = true;

    [ExportGroup("TPS Demo Modern Lighting")]
    // 等价移植自 godotengine/tps-demo 的 Godot 4 环境配置。
    [Export] public bool modern_pipeline_enabled { get; set; } = true;
    [Export] public bool ssao_enabled { get; set; } = true;
    [Export] public bool ssil_enabled { get; set; } = true;
    [Export] public bool dynamic_gi_enabled { get; set; } = true;
    [Export] public bool glow_enabled { get; set; } = true;
    [Export] public bool volumetric_fog_enabled { get; set; } = true;
    [Export(PropertyHint.Range, "0.1,4.0,0.05")] public float tonemap_exposure { get; set; } = 2.05f;
    [Export(PropertyHint.Range, "0.5,2.0,0.01")] public float agx_contrast { get; set; } = 1.08f;
    [Export(PropertyHint.Range, "0.5,2.0,0.01")] public float saturation { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0.0,4.0,0.05")] public float ssao_intensity { get; set; } = 1.25f;
    [Export(PropertyHint.Range, "0.0,1.0,0.01")] public float glow_intensity { get; set; } = 0.10f;

    [ExportGroup("Fill")]
    [Export(PropertyHint.Range, "0.0,2.0,0.01")] public float ambient_intensity { get; set; } = 0.72f;
    [Export(PropertyHint.Range, "0.0,1.5,0.01")] public float fill_intensity { get; set; } = 0.42f;

    [ExportGroup("Fog")]
    // 编辑时默认关闭以保持清晰；游玩/氛围预览时开启。
    [Export] public bool fog_enabled { get; set; }
    [Export(PropertyHint.Range, "0.0,20000.0,50.0")] public float fog_distance { get; set; } = 4000.0f;
    [Export(PropertyHint.Range, "0.0,1.0,0.01")] public float fog_intensity { get; set; } = 0.35f;

    public static W3LitSettings DefaultSettings() => new();
    public static W3LitSettings default_settings() => DefaultSettings();

    /// <summary>近似 CIE 日光/黑体色温 → RGB，用于 DirectionalLight 颜色。</summary>
    public static Color TemperatureToColor(float kelvin)
    {
        float k = Mathf.Clamp(kelvin, 1000.0f, 40000.0f) / 100.0f;
        float r;
        float g;
        float b;
        if (k <= 66.0f)
        {
            r = 1.0f;
            g = Mathf.Clamp(0.3900815788f * Mathf.Log(k) - 0.6318414438f, 0.0f, 1.0f);
        }
        else
        {
            r = Mathf.Clamp(1.292936186f * Mathf.Pow(k - 60.0f, -0.1332047592f), 0.0f, 1.0f);
            g = Mathf.Clamp(1.1298908609f * Mathf.Pow(k - 60.0f, -0.0755148492f), 0.0f, 1.0f);
        }
        if (k >= 66.0f)
            b = 1.0f;
        else if (k <= 19.0f)
            b = 0.0f;
        else
            b = Mathf.Clamp(0.5432067891f * Mathf.Log(k - 10.0f) - 1.1962540795f, 0.0f, 1.0f);
        return new Color(r, g, b);
    }

    public static Color temperature_to_color(float kelvin) => TemperatureToColor(kelvin);
}
