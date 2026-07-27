using Godot;
using Diablo1Like.W3.Terrain;
using Diablo1Like.W3.World;

namespace Diablo1Like.W3.Lighting;

/// <summary>
/// 通过 Godot DirectionalLight3D + WorldEnvironment 应用 <see cref="W3LitSettings"/>（War3 风格）。
/// 迁移自 addons/w3kit/w3_war3_lit.gd；静态工具类，供 GDScript 通过脚本引用直接调用静态方法。
/// </summary>
[GlobalClass]
public partial class W3LightingController : RefCounted
{
    // true 时（UE/Bistro）MDX 用 PER_PIXEL 并接收方向光/点光；War3 时保持 UNSHADED（BLP 贴图已预烘焙光照）。
    public static bool ModelsReceiveLights;

    public static void ApplyToScene(Node root, Resource? settings = null)
    {
        if (root == null)
            return;
        // 切换回 War3 前先卸载 UE 独占的 Sky3D 节点，恢复 WorldEnvironment + DirectionalLight3D。
        UeStyleLightingController.TeardownUeOnlyNodes(root);
        W3LitSettings s = settings as W3LitSettings ?? ResourceLoader.Load<W3LitSettings>("res://addons/w3kit/w3_lit_settings_default.tres") ?? new W3LitSettings();
        s = ApplyWeatherPreset(s);
        EnsureEnvironment(root, s);
        EnsureKeyLight(root, s);
        EnsureFillLight(root, s);
        SyncTerrainOpenRealmLight(root, s);
        SetModelsReceiveLights(root, false);
        SetTerrainReceiveLights(root, false);
    }

    public static void apply_to_scene(Node root, Resource? settings = null) => ApplyToScene(root, settings);

    /// <summary>切换 MDX 材质是否接收场景灯光（UE/Bistro=on，War3 烘焙=off）。</summary>
    public static void SetModelsReceiveLights(Node root, bool on)
    {
        ModelsReceiveLights = on;
        if (root == null)
            return;
        var world = root.GetNodeOrNull<W3MapWorld>("W3MapWorld");
        if (world == null)
            return;
        var seen = new Godot.Collections.Dictionary<StandardMaterial3D, bool>();
        int count = 0;
        foreach (Node node in world.FindChildren("*", "GeometryInstance3D", true, false))
        {
            if (node is not GeometryInstance3D geo || geo.MaterialOverride is not StandardMaterial3D mat)
                continue;
            if (seen.ContainsKey(mat))
                continue;
            seen[mat] = true;
            // 加色发光/火焰保持 unshaded。
            if (mat.BlendMode == BaseMaterial3D.BlendModeEnum.Add)
                continue;
            StyleModelMaterial(mat);
            count++;
        }
        GD.Print(on
            ? $"W3LightingController: models receive scene lights (unique mats={count})"
            : $"W3LightingController: models unshaded / baked albedo (unique mats={count})");
    }

    public static void set_models_receive_lights(Node root, bool on) => SetModelsReceiveLights(root, on);

    /// <summary>
    /// 地形材质是否接收场景灯光（与 GPU/C# 地形 renderer 约定）。
    /// 旧 GDS 回退 (w3_gpu_terrain.gd) 的同名方法本就是空实现；
    /// WarsmashGroundRenderer.cs（现行 C# 渲染器）没有对应开关，此处保留调用点但暂为空操作。
    /// </summary>
    public static void SetTerrainReceiveLights(Node root, bool on)
    {
    }

    public static void _set_terrain_receive_lights(Node root, bool on) => SetTerrainReceiveLights(root, on);

    private static string WeatherOf(W3LitSettings s) => string.IsNullOrEmpty(s.weather) ? "default" : s.weather;

    /// <summary>天气预设覆盖基础参数；"default" 保留旧观感。</summary>
    private static W3LitSettings ApplyWeatherPreset(W3LitSettings s)
    {
        switch (WeatherOf(s))
        {
            case "sun":
            {
                var d = (W3LitSettings)s.Duplicate();
                d.sun_intensity = 1.55f;
                d.sun_temperature_k = 5700.0f;
                d.sun_pitch_deg = -67.0f;
                d.ambient_intensity = 0.55f;
                d.fill_intensity = 0.18f;
                return d;
            }
            case "overcast":
            {
                var d = (W3LitSettings)s.Duplicate();
                d.sun_intensity = 0.75f;
                d.sun_temperature_k = 6600.0f;
                d.sun_pitch_deg = -55.0f;
                d.ambient_intensity = 0.95f;
                d.fill_intensity = 0.55f;
                return d;
            }
            default:
                return s;
        }
    }

    /// <summary>把 open-realm Lambert 参数推给 GPU 地形/悬崖（自定义无光照 shader）。</summary>
    private static void SyncTerrainOpenRealmLight(Node root, W3LitSettings s)
    {
        var world = root.GetNodeOrNull<W3MapWorld>("W3MapWorld");
        if (world?.Terrain == null)
            return;
        DirectionalLight3D? sun = root.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") ?? root.GetNodeOrNull<DirectionalLight3D>("Sun");
        Vector3 lightDir = sun != null ? -sun.GlobalTransform.Basis.Z : new Vector3(0.45f, 0.85f, 0.28f);
        // open-realm MDX 默认: ambient 0.35, 方向光缩放已烘进公式。
        float ambient = Mathf.Clamp(0.25f + s.ambient_intensity * 0.15f, 0.2f, 0.5f);
        world.Terrain.ApplyLighting(lightDir, ambient, 1.1f);
        world.Cliffs?.ApplyLighting(lightDir, ambient, 1.1f);
    }

    public static void StyleTerrainMaterial(StandardMaterial3D? mat)
    {
        if (mat == null)
            return;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        mat.Metallic = 0.0f;
        mat.Roughness = 0.92f;
        mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
        mat.MetallicSpecular = 0.18f;
    }

    public static void style_terrain_material(StandardMaterial3D? mat) => StyleTerrainMaterial(mat);

    public static void StyleModelMaterial(StandardMaterial3D? mat)
    {
        if (mat == null)
            return;
        mat.Metallic = 0.0f;
        mat.Roughness = 0.82f;
        mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
        mat.MetallicSpecular = 0.24f;
    }

    public static void style_model_material(StandardMaterial3D? mat) => StyleModelMaterial(mat);

    /// <summary>现代水面观感——不使用 War3 Water*.blp。几何体仍来自 W3E 高度。</summary>
    public static void StyleModernWaterMaterial(StandardMaterial3D? mat)
    {
        if (mat == null)
            return;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.AlbedoColor = new Color(0.12f, 0.40f, 0.52f, 0.78f);
        mat.AlbedoTexture = null;
        mat.Metallic = 0.12f;
        mat.Roughness = 0.08f;
        mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
        mat.EmissionEnabled = true;
        mat.Emission = new Color(0.05f, 0.14f, 0.18f);
        mat.EmissionEnergyMultiplier = 0.35f;
        mat.RefractionEnabled = false;
        mat.ProximityFadeEnabled = true;
        mat.ProximityFadeDistance = 12.0f;
        mat.RimEnabled = true;
        mat.Rim = 0.4f;
        mat.RimTint = 0.55f;
        mat.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Always;
        mat.RenderPriority = 2;
    }

    public static void style_modern_water_material(StandardMaterial3D? mat) => StyleModernWaterMaterial(mat);

    private static void EnsureEnvironment(Node root, W3LitSettings s)
    {
        string weather = WeatherOf(s);
        var we = root.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (we == null)
        {
            we = new WorldEnvironment { Name = "WorldEnvironment" };
            root.AddChild(we);
        }
        var env = new Godot.Environment();
        var skyMaterial = new ProceduralSkyMaterial();
        switch (weather)
        {
            case "sun":
                skyMaterial.SkyTopColor = new Color(0.22f, 0.42f, 0.72f);
                skyMaterial.SkyHorizonColor = new Color(0.74f, 0.80f, 0.86f);
                skyMaterial.GroundBottomColor = new Color(0.12f, 0.11f, 0.10f);
                skyMaterial.GroundHorizonColor = new Color(0.56f, 0.52f, 0.44f);
                break;
            case "overcast":
                skyMaterial.SkyTopColor = new Color(0.44f, 0.47f, 0.51f);
                skyMaterial.SkyHorizonColor = new Color(0.63f, 0.65f, 0.67f);
                skyMaterial.GroundBottomColor = new Color(0.12f, 0.12f, 0.12f);
                skyMaterial.GroundHorizonColor = new Color(0.50f, 0.50f, 0.49f);
                break;
            default:
                skyMaterial.SkyTopColor = new Color(0.18f, 0.28f, 0.43f);
                skyMaterial.SkyHorizonColor = new Color(0.62f, 0.68f, 0.72f);
                skyMaterial.GroundBottomColor = new Color(0.10f, 0.09f, 0.08f);
                skyMaterial.GroundHorizonColor = new Color(0.48f, 0.43f, 0.36f);
                break;
        }
        skyMaterial.SunAngleMax = 8.0f;
        skyMaterial.SunCurve = 0.08f;
        var sky = new Sky { SkyMaterial = skyMaterial };
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
        env.Sky = sky;
        env.BackgroundEnergyMultiplier = 0.4f;
        // TPS 用彩色环境光 + 背景反射；套用到 Lost Temple 户外日光配色。
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.58f, 0.61f, 0.68f);
        env.AmbientLightEnergy = s.ambient_intensity;
        env.ReflectedLightSource = Godot.Environment.ReflectionSource.Bg;
        bool modern = s.modern_pipeline_enabled;
        env.TonemapMode = modern ? Godot.Environment.ToneMapper.Agx : Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure = modern ? s.tonemap_exposure : 1.0f;
        env.TonemapWhite = 6.0f;
        env.TonemapAgxContrast = modern ? s.agx_contrast : 1.0f;
        env.SsrEnabled = false;
        env.SsaoEnabled = modern && s.ssao_enabled;
        env.SsaoRadius = 64.0f;
        env.SsaoIntensity = s.ssao_intensity;
        env.SsaoPower = 1.25f;
        env.SsaoDetail = 0.65f;
        env.SsaoHorizon = 0.06f;
        env.SsaoSharpness = 0.92f;
        env.SsaoLightAffect = 0.30f;
        env.SsaoAOChannelAffect = 0.8f;
        env.SsilEnabled = modern && s.ssil_enabled;
        env.SsilRadius = 128.0f;
        env.SsilIntensity = 0.75f;
        env.SsilSharpness = 0.9f;
        env.SsilNormalRejection = 1.0f;
        env.SdfgiEnabled = modern && s.dynamic_gi_enabled;
        env.SdfgiCascades = 4;
        env.SdfgiMinCellSize = 64.0f;
        env.SdfgiMaxDistance = 6144.0f;
        env.SdfgiCascade0Distance = 768.0f;
        env.SdfgiYScale = Godot.Environment.SdfgiyScale.Scale50Percent;
        env.SdfgiUseOcclusion = true;
        env.SdfgiReadSkyLight = true;
        env.SdfgiBounceFeedback = 0.33f;
        env.SdfgiEnergy = 1.25f;
        env.GlowEnabled = modern && s.glow_enabled;
        env.GlowIntensity = s.glow_intensity;
        env.GlowStrength = 0.85f;
        env.GlowBloom = 0.08f;
        env.GlowHdrThreshold = 0.9f;
        env.GlowHdrScale = 1.7f;
        env.GlowHdrLuminanceCap = 3.0f;
        env.SetGlowLevel(3, 0.0f);
        env.SetGlowLevel(4, 1.0f);
        env.SetGlowLevel(6, 1.0f);
        bool fogOn = s.fog_enabled;
        env.FogEnabled = fogOn;
        env.FogMode = Godot.Environment.FogModeEnum.Depth;
        float fogDist = s.fog_distance;
        float fogAmt = Mathf.Clamp(s.fog_intensity, 0.0f, 1.0f);
        if (weather == "overcast")
        {
            // 阴天雾契约：相机附近始终保持清晰(超出游玩范围雾才起效)，随后陡增，让远景完全被雾吞没。
            env.FogLightColor = new Color(0.66f, 0.68f, 0.71f);
            env.FogLightEnergy = 0.9f;
            env.FogDepthBegin = 1400.0f;
            env.FogDepthEnd = 4600.0f;
            env.FogDepthCurve = 1.6f;
            env.FogAerialPerspective = 0.6f;
            env.FogDensity = 0.92f;
        }
        else
        {
            env.FogLightColor = new Color(0.55f, 0.57f, 0.60f);
            env.FogLightEnergy = 0.65f;
            env.FogDepthBegin = fogDist;
            env.FogDepthEnd = fogDist + Mathf.Lerp(14000.0f, 2800.0f, fogAmt);
            env.FogDepthCurve = Mathf.Lerp(1.0f, 0.55f, fogAmt);
            env.FogAerialPerspective = fogOn ? 0.25f * fogAmt : 0.0f;
            env.FogDensity = 0.00008f;
        }
        env.VolumetricFogEnabled = modern && fogOn && s.volumetric_fog_enabled;
        if (weather == "overcast")
        {
            env.VolumetricFogDensity = 0.00006f;
            env.VolumetricFogAlbedo = new Color(0.80f, 0.82f, 0.85f);
            env.VolumetricFogEmission = new Color(0.030f, 0.032f, 0.036f);
            env.VolumetricFogEmissionEnergy = 0.30f;
            env.VolumetricFogLength = 6400.0f;
            env.VolumetricFogDetailSpread = 2.2f;
            env.VolumetricFogAmbientInject = 0.25f;
        }
        else
        {
            env.VolumetricFogDensity = 0.00010f;
            env.VolumetricFogAlbedo = new Color(0.82f, 0.78f, 0.69f);
            env.VolumetricFogEmission = new Color(0.025f, 0.018f, 0.010f);
            env.VolumetricFogEmissionEnergy = 0.35f;
            env.VolumetricFogLength = 4600.0f;
            env.VolumetricFogDetailSpread = 1.5f;
            env.VolumetricFogAmbientInject = 0.35f;
        }
        env.AdjustmentEnabled = modern;
        env.AdjustmentBrightness = 1.0f;
        env.AdjustmentContrast = 1.04f;
        env.AdjustmentSaturation = s.saturation;
        we.Environment = env;
        we.SetMeta("w3_tps_modern_lighting", modern);
    }

    private static void EnsureKeyLight(Node root, W3LitSettings s)
    {
        var sun = root.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") ?? root.GetNodeOrNull<DirectionalLight3D>("Sun");
        if (sun == null)
        {
            sun = new DirectionalLight3D { Name = "DirectionalLight3D" };
            root.AddChild(sun);
        }
        sun.RotationDegrees = new Vector3(s.sun_pitch_deg, s.sun_yaw_deg, 0.0f);
        sun.LightColor = W3LitSettings.TemperatureToColor(s.sun_temperature_k);
        // open-realm 方向光 ≈ 0.75（对模型而言）；sun_intensity 在此基础上缩放。
        sun.LightEnergy = s.sun_intensity;
        sun.LightIndirectEnergy = 0.0f;
        sun.ShadowEnabled = s.sun_shadows;
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        sun.DirectionalShadowMaxDistance = 5600.0f;
        sun.DirectionalShadowFadeStart = 0.82f;
        sun.DirectionalShadowSplit1 = 0.08f;
        sun.DirectionalShadowSplit2 = 0.22f;
        sun.DirectionalShadowSplit3 = 0.48f;
        sun.ShadowBias = 0.08f;
        sun.ShadowNormalBias = 1.2f;
        switch (WeatherOf(s))
        {
            case "sun":
                // 正午太阳：小日轮 + 极小半影 = 硬边深色阴影。
                sun.ShadowOpacity = 0.85f;
                sun.ShadowBlur = 0.35f;
                sun.LightAngularDistance = 0.2f;
                break;
            case "overcast":
                // 天空大小的光源：微弱、宽泛、模糊的接触阴影。
                sun.ShadowOpacity = 0.30f;
                sun.ShadowBlur = 3.0f;
                sun.LightAngularDistance = 3.0f;
                break;
            default:
                sun.ShadowOpacity = 0.55f;
                sun.ShadowBlur = 1.25f;
                sun.LightAngularDistance = 0.65f;
                break;
        }
    }

    private static void EnsureFillLight(Node root, W3LitSettings s)
    {
        var fill = root.GetNodeOrNull<DirectionalLight3D>("FillLight3D");
        if (fill == null)
        {
            fill = new DirectionalLight3D { Name = "FillLight3D" };
            root.AddChild(fill);
        }
        float yaw = s.sun_yaw_deg;
        float fillEnergy = s.fill_intensity;
        fill.RotationDegrees = new Vector3(-25.0f, yaw - 180.0f, 0.0f);
        fill.LightColor = new Color(0.72f, 0.78f, 0.92f);
        fill.LightEnergy = fillEnergy;
        fill.ShadowEnabled = false;
        fill.Visible = fillEnergy > 0.001f;
    }

    /// <summary>相机质量档：远景关闭 SSAO/GI/雾等高开销效果（近景全开）。</summary>
    public static void ApplyQualityLod(Node root, int lod, Resource? settings)
    {
        var we = root.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        var sun = root.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (we?.Environment == null)
            return;
        Godot.Environment env = we.Environment;
        // 只有 W3LitSettings 携带 modern_pipeline_enabled；UE/Bistro 沿用旧 GDS 行为（视为非 modern）。
        W3LitSettings? w = settings as W3LitSettings;
        bool modern = w != null && w.modern_pipeline_enabled;
        bool high = lod <= 0;
        bool medium = lod <= 1;
        bool far = lod <= 2;
        env.SsaoEnabled = modern && far && w!.ssao_enabled;
        env.SsilEnabled = modern && medium && w.ssil_enabled;
        env.SdfgiEnabled = modern && medium && w.dynamic_gi_enabled;
        env.GlowEnabled = modern && far && w.glow_enabled;
        env.VolumetricFogEnabled = modern && medium && w.volumetric_fog_enabled && w.fog_enabled;
        if (sun != null && w != null)
        {
            sun.ShadowEnabled = far && w.sun_shadows;
            sun.DirectionalShadowMaxDistance = high ? 5600.0f : 4200.0f;
        }
    }

    public static void apply_quality_lod(Node root, int lod, Resource? settings) => ApplyQualityLod(root, lod, settings);
}
