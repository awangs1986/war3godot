using Godot;
using Diablo1Like.W3.World;

namespace Diablo1Like.W3.Lighting;

/// <summary>
/// 取材自 Jamsers/Bistro-Demo-Tweaked 的户外光照组合。
/// 不涉及 War3 / UE-Sky3D 路径；应用前会先卸载 Sky3D。
/// </summary>
[GlobalClass]
public partial class BistroLightingController : RefCounted
{
    public static void ApplyToScene(Node root, Resource? settings = null)
    {
        if (root == null)
            return;
        UeStyleLightingController.TeardownUeOnlyNodes(root);
        BistroLitSettings s = settings as BistroLitSettings
            ?? ResourceLoader.Load<BistroLitSettings>("res://addons/w3kit/bistro_lit_default.tres")
            ?? new BistroLitSettings();

        BistroLitSettings.ScenarioParams p = ResolveParams(s);
        EnsureEnvironment(root, s, p);
        EnsureKeyLight(root, p);
        EnsureFillLight(root, p);
        SyncTerrain(root, p);
        // 道具/单位 + 地面：保留 War3 烘焙观感，只取用方向光 + 天空。
        W3LightingController.SetModelsReceiveLights(root, true);
        W3LightingController.SetTerrainReceiveLights(root, true);
    }

    public static void apply_to_scene(Node root, Resource? settings = null) => ApplyToScene(root, settings);

    private static BistroLitSettings.ScenarioParams ResolveParams(BistroLitSettings s)
    {
        if (s.manual_override)
        {
            return new BistroLitSettings.ScenarioParams(
                s.sun_temperature_k, s.sun_energy, s.sun_pitch_deg, s.sun_yaw_deg,
                s.sun_energy * 0.85f, 0.9f, s.exposure);
        }
        BistroLitSettings.ScenarioParams p = BistroLitSettings.GetScenarioParams(s.scenario);
        // 场景曝光作为在场景表基础上的倍率（1.05 为场景表基准曝光）。
        float exposure = p.Exposure * s.exposure / 1.05f;
        return new BistroLitSettings.ScenarioParams(p.Temp, p.Energy, p.Pitch, p.Yaw, p.SkyEnergy, p.Ambient, exposure);
    }

    private static void EnsureEnvironment(Node root, BistroLitSettings s, BistroLitSettings.ScenarioParams p)
    {
        var we = root.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (we == null)
        {
            we = new WorldEnvironment { Name = "WorldEnvironment" };
            root.AddChild(we);
        }

        // Bistro 用 ProceduralSkyMaterial（简单日轮）+ 重后处理堆栈。
        var skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.28f, 0.48f, 0.82f),
            SkyHorizonColor = new Color(0.72f, 0.78f, 0.88f),
            GroundBottomColor = new Color(0.12f, 0.11f, 0.1f),
            GroundHorizonColor = new Color(0.45f, 0.42f, 0.38f),
            SunAngleMax = 5.0f,
            SunCurve = 0.08f,
            SkyEnergyMultiplier = p.SkyEnergy,
            GroundEnergyMultiplier = 0.35f,
        };

        var sky = new Sky { SkyMaterial = skyMat, ProcessMode = Sky.ProcessModeEnum.Realtime, RadianceSize = Sky.RadianceSizeEnum.Size256 };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = p.Ambient,
            AmbientLightSkyContribution = 1.0f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            // Bistro WorldEnvironment.tres 中 tonemap_mode 3 == ACES。
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = p.Exposure,
            TonemapWhite = 6.0f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = s.saturation,
            GlowEnabled = s.glow_enabled,
            GlowIntensity = s.glow_intensity,
            GlowStrength = s.glow_strength,
            GlowBloom = 0.05f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = s.ssao_intensity,
            SsaoHorizon = 0.0f,
            SsaoSharpness = 0.85f,
            SsilEnabled = s.ssil_enabled,
            SsilRadius = 2.3f,
            SsilIntensity = 1.0f,
            SsilSharpness = 0.9f,
            SsrEnabled = s.ssr_enabled,
            SdfgiEnabled = s.sdfgi_enabled,
            // Bistro 体积雾密度为 0（等价关闭），室外地图保留深度雾。
            VolumetricFogEnabled = false,
        };
        foreach (int level in new[] { 1, 2, 4, 5, 6 })
            env.SetGlowLevel(level, 1.0f);
        if (env.SsrEnabled)
        {
            env.SsrMaxSteps = 64;
            env.SsrFadeIn = 0.05f;
            env.SsrFadeOut = 1.5f;
        }
        if (env.SdfgiEnabled)
        {
            env.SdfgiBounceFeedback = 1.0f;
            env.SdfgiUseOcclusion = true;
            env.SdfgiCascades = 4;
            env.SdfgiMinCellSize = 16.0f;
        }

        bool fogOn = s.fog_enabled;
        env.FogEnabled = fogOn;
        env.FogMode = Godot.Environment.FogModeEnum.Depth;
        env.FogLightColor = new Color(0.7f, 0.74f, 0.82f);
        float fogDist = s.fog_distance;
        float fogAmt = Mathf.Clamp(s.fog_intensity, 0.0f, 1.0f);
        env.FogDepthBegin = fogDist;
        env.FogDepthEnd = fogDist + Mathf.Lerp(16000.0f, 3500.0f, fogAmt);
        env.FogAerialPerspective = fogOn ? 0.4f * fogAmt : 0.0f;
        env.FogSunScatter = fogOn ? 0.15f : 0.0f;

        we.Environment = env;

        // Bistro 用 CameraAttributesPractical 曝光——搭配 ACES 获得自动曝光的观感。
        we.CameraAttributes = new CameraAttributesPractical
        {
            ExposureMultiplier = p.Exposure,
            // War3 地图平移时不希望自动曝光剧烈波动，保持关闭。
            AutoExposureEnabled = false,
        };
    }

    private static void EnsureKeyLight(Node root, BistroLitSettings.ScenarioParams p)
    {
        var sun = root.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") ?? root.GetNodeOrNull<DirectionalLight3D>("Sun");
        if (sun == null)
        {
            sun = new DirectionalLight3D { Name = "DirectionalLight3D" };
            root.AddChild(sun);
        }
        root.MoveChild(sun, 0);
        sun.RotationDegrees = new Vector3(p.Pitch, p.Yaw, 0.0f);
        sun.LightColor = W3LitSettings.TemperatureToColor(p.Temp);
        sun.LightEnergy = p.Energy;
        sun.LightIndirectEnergy = 1.1f;
        sun.LightSpecular = 0.75f;
        sun.ShadowEnabled = true;
        sun.ShadowBlur = 1.5f;
        sun.ShadowOpacity = 0.88f;
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        sun.DirectionalShadowMaxDistance = 6500.0f;
        sun.DirectionalShadowPancakeSize = 40.0f;
        sun.LightAngularDistance = 0.55f;
        sun.Visible = true;
    }

    private static void EnsureFillLight(Node root, BistroLitSettings.ScenarioParams p)
    {
        // 冷色柔和补光，模拟对面反弹光——Bistro 更依赖 GI，这里只做弱补光近似。
        var fill = root.GetNodeOrNull<DirectionalLight3D>("FillLight3D");
        if (fill == null)
        {
            fill = new DirectionalLight3D { Name = "FillLight3D" };
            root.AddChild(fill);
        }
        fill.RotationDegrees = new Vector3(-22.0f, p.Yaw + 160.0f, 0.0f);
        fill.LightColor = new Color(0.55f, 0.65f, 0.9f);
        fill.LightEnergy = Mathf.Clamp(p.Energy * 0.18f, 0.05f, 0.45f);
        fill.ShadowEnabled = false;
        fill.Visible = fill.LightEnergy > 0.02f;
    }

    private static void SyncTerrain(Node root, BistroLitSettings.ScenarioParams p)
    {
        var world = root.GetNodeOrNull<W3MapWorld>("W3MapWorld");
        if (world?.Terrain == null)
            return;
        var sun = root.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        Vector3 lightDir = sun != null ? -sun.GlobalTransform.Basis.Z : new Vector3(0.45f, 0.85f, 0.28f);
        float ambient = Mathf.Clamp(0.4f + p.Ambient * 0.25f, 0.4f, 0.8f);
        world.Terrain.ApplyLighting(lightDir, ambient, 1.3f);
        world.Cliffs?.ApplyLighting(lightDir, ambient, 1.3f);
    }
}
