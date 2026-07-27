using Godot;

namespace Diablo1Like.W3.Lighting;

/// <summary>
/// 仿 UE5 空白关卡的户外光照（Godot 4 Forward+）。
/// 属性名沿用旧 GDS/.tres 小写下划线命名以兼容动态 GDScript 调用方。
/// </summary>
[GlobalClass]
public partial class UeStyleLitSettings : Resource
{
    public enum SkyBackend
    {
        // 引擎内置 PhysicalSkyMaterial（Preetham/Nishita 风格日光），无需额外插件。
        PhysicalNishita = 0,
        // Tokisan Sky3D 大气天空 + 云 + 太阳/月亮管理（addons/sky_3d，仅 UE 路径使用）。
        Sky3D = 1,
    }

    [ExportGroup("Sky backend")]
    [Export] public SkyBackend sky_backend { get; set; } = SkyBackend.Sky3D;
    // Sky3D 时钟（小时）。PHYSICAL_NISHITA 忽略（改用太阳 pitch/yaw）。
    [Export(PropertyHint.Range, "0.0,23.99,0.01")] public float sky3d_hour { get; set; } = 10.5f;
    [Export] public bool sky3d_time_progress { get; set; }

    [ExportGroup("Sun")]
    [Export(PropertyHint.Range, "0.0,4.0,0.01")] public float sun_intensity { get; set; } = 1.35f;
    [Export(PropertyHint.Range, "2500.0,10000.0,50.0")] public float sun_temperature_k { get; set; } = 6500.0f;
    [Export(PropertyHint.Range, "-80.0,-10.0,0.5")] public float sun_pitch_deg { get; set; } = -52.0f;
    [Export(PropertyHint.Range, "-180.0,180.0,0.5")] public float sun_yaw_deg { get; set; } = 35.0f;
    [Export] public bool sun_shadows { get; set; } = true;
    [Export(PropertyHint.Range, "0.0,5.0,0.05")] public float sun_angular_distance { get; set; } = 0.53f;

    [ExportGroup("Sky")]
    [Export(PropertyHint.Range, "0.2,4.0,0.01")] public float sky_energy { get; set; } = 1.35f;
    [Export(PropertyHint.Range, "1.0,20.0,0.1")] public float sky_turbidity { get; set; } = 4.0f;
    [Export(PropertyHint.Range, "0.0,2.0,0.01")] public float ambient_from_sky { get; set; } = 1.15f;

    [ExportGroup("Look")]
    [Export(PropertyHint.Range, "0.2,2.5,0.01")] public float exposure { get; set; } = 1.05f;
    [Export] public bool glow_enabled { get; set; } = true;
    [Export(PropertyHint.Range, "0.0,2.0,0.01")] public float glow_intensity { get; set; } = 0.4f;
    // 大地图上开销较高——默认关闭,除非需要接触反弹。
    [Export] public bool sdfgi_enabled { get; set; }
    [Export] public bool ssil_enabled { get; set; }

    [ExportGroup("Fog")]
    [Export] public bool fog_enabled { get; set; } = true;
    [Export(PropertyHint.Range, "0.0,20000.0,50.0")] public float fog_distance { get; set; } = 4200.0f;
    [Export(PropertyHint.Range, "0.0,1.0,0.01")] public float fog_intensity { get; set; } = 0.22f;
}
