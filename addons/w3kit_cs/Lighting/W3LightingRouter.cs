using Godot;

namespace Diablo1Like.W3.Lighting;

/// <summary>
/// 按 lit_settings 的实际类型分发到 War3 / UE / Bistro 三套光照控制器。
/// 供仍是 GDScript 的 w3_sandbox.gd 统一调用一个入口（避免在 GDScript 里做类型判断）。
/// </summary>
[GlobalClass]
public partial class W3LightingRouter : RefCounted
{
    public static void ApplyToScene(Node root, Resource? settings = null)
    {
        switch (settings)
        {
            case BistroLitSettings:
                BistroLightingController.ApplyToScene(root, settings);
                break;
            case UeStyleLitSettings:
                UeStyleLightingController.ApplyToScene(root, settings);
                break;
            default:
                // W3LitSettings 或空/未知类型都落到 War3 路径（沿用旧 GDS 的默认兜底行为）。
                W3LightingController.ApplyToScene(root, settings);
                break;
        }
    }

    public static void apply_to_scene(Node root, Resource? settings = null) => ApplyToScene(root, settings);

    public static void ApplyQualityLod(Node root, int lod, Resource? settings) => W3LightingController.ApplyQualityLod(root, lod, settings);

    public static void apply_quality_lod(Node root, int lod, Resource? settings) => ApplyQualityLod(root, lod, settings);

    public static bool UsesBistroLit(Resource? settings) => settings is BistroLitSettings;

    public static bool uses_bistro_lit(Resource? settings) => UsesBistroLit(settings);

    public static bool UsesUeLit(Resource? settings) => settings is UeStyleLitSettings;

    public static bool uses_ue_lit(Resource? settings) => UsesUeLit(settings);

    public static string LitLabel(Resource? settings) => settings switch
    {
        BistroLitSettings => "Bistro",
        UeStyleLitSettings => "UE",
        W3LitSettings => "War3",
        _ => "War3",
    };

    public static string lit_label(Resource? settings) => LitLabel(settings);
}
