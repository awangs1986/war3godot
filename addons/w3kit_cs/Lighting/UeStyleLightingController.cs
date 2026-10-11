using Godot;

namespace Diablo1Like.W3.Lighting;

/// <summary>
/// 仅应用 <see cref="UeStyleLitSettings"/>；War3 路径永不使用。
/// 天空后端二选一：PhysicalSky（内置 Nishita/Preetham）或 Tokisan Sky3D（addons/sky_3d）。
/// Sky3D 仍是 GDScript（MIT 第三方插件，未随本轮迁移改写），这里通过动态 Call/Set 调用它，
/// 与旧 GDS 的调用方式完全一致——详见 R3 迁移记录中的 Sky3D 决定。
/// </summary>
[GlobalClass]
public partial class UeStyleLightingController : RefCounted
{
    private const string Sky3DNodeName = "UeSky3D";

    public static void ApplyToScene(Node root, Resource? settings = null)
    {
        if (root == null)
            return;
        UeStyleLitSettings s = settings as UeStyleLitSettings
            ?? ResourceLoader.Load<UeStyleLitSettings>("res://addons/w3kit/ue_style_lit_default.tres")
            ?? new UeStyleLitSettings();

        var backend = s.sky_backend;
        if (backend == UeStyleLitSettings.SkyBackend.Sky3D && Sky3DAvailable())
        {
            ApplySky3D(root, s);
        }
        else
        {
            if (backend == UeStyleLitSettings.SkyBackend.Sky3D)
                GD.PushWarning("UeStyleLightingController: Sky3D unavailable — falling back to PhysicalSky (Nishita)");
            TeardownSky3D(root);
            EnsureEnvironmentPhysical(root, s);
            EnsureKeyLight(root, s);
            DisableFillLight(root);
        }
    }

    public static void apply_to_scene(Node root, Resource? settings = null) => ApplyToScene(root, settings);

    /// <summary>供 War3 lit 调用，避免切回 War3 后留下 Sky3D / UE 节点。</summary>
    public static void TeardownUeOnlyNodes(Node root)
    {
        if (root == null)
            return;
        TeardownSky3D(root);
        var sun = root.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (sun != null)
            sun.Visible = true;
    }

    public static void teardown_ue_only_nodes(Node root) => TeardownUeOnlyNodes(root);

    private static bool Sky3DAvailable() => ResourceLoader.Exists("res://addons/sky_3d/src/Sky3D.gd");

    /// <summary>class_name 链 + 贴图必须先解析，Sky3D.new() 才能成功。</summary>
    private static GDScript? LoadSky3DScript()
    {
        if (!Sky3DAvailable())
            return null;
        GD.Load("res://addons/sky_3d/src/TOD_Math.gd");
        GD.Load("res://addons/sky_3d/src/ScatterLib.gd");
        GD.Load("res://addons/sky_3d/src/OrbitalElements.gd");
        GD.Load("res://addons/sky_3d/src/SkyDome.gd");
        GD.Load("res://addons/sky_3d/src/TimeOfDay.gd");
        return GD.Load<GDScript>("res://addons/sky_3d/src/Sky3D.gd");
    }

    private static void TeardownSky3D(Node root)
    {
        Node? n = root.GetNodeOrNull(Sky3DNodeName);
        if (n == null)
            return;
        root.RemoveChild(n);
        n.Free();
    }

    private static void ApplySky3D(Node root, UeStyleLitSettings s)
    {
        GDScript? sky3DScript = LoadSky3DScript();
        if (sky3DScript == null)
        {
            GD.PushWarning("UeStyleLightingController: Sky3D script load failed");
            TeardownSky3D(root);
            EnsureEnvironmentPhysical(root, s);
            EnsureKeyLight(root, s);
            DisableFillLight(root);
            return;
        }

        // 隐藏沙盒自带的关键光——Sky3D 拥有 SunLight / MoonLight。
        var oldSun = root.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (oldSun != null)
        {
            oldSun.Visible = false;
            oldSun.LightEnergy = 0.0f;
            oldSun.ShadowEnabled = false;
        }
        DisableFillLight(root);

        // 卸下普通 WorldEnvironment，让 Sky3D（同样是 WorldEnvironment）成为生效实例。
        var we = root.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (we != null && we.Name != Sky3DNodeName)
        {
            we.Environment = null;
            we.CameraAttributes = null;
        }

        Node? sky3d = root.GetNodeOrNull(Sky3DNodeName);
        if (sky3d == null)
        {
            sky3d = sky3DScript.New().AsGodotObject() as Node;
            if (sky3d == null)
                return;
            sky3d.Name = Sky3DNodeName;
            root.AddChild(sky3d);
            // NOTIFICATION_ENTER_TREE 会跑 Sky3D._initialize（SunLight / SkyDome / TimeOfDay）。
        }

        sky3d.Set("sky3d_enabled", true);
        sky3d.Set("sky_enabled", true);
        sky3d.Set("clouds_enabled", true);
        sky3d.Set("lights_enabled", true);
        sky3d.Set("fog_enabled", s.fog_enabled);
        sky3d.Set("game_time_enabled", s.sky3d_time_progress);
        sky3d.Set("editor_time_enabled", false);
        sky3d.Set("current_time", s.sky3d_hour);
        sky3d.Set("sun_energy", s.sun_intensity * 1.15f);
        sky3d.Set("skydome_energy", s.sky_energy);
        sky3d.Set("cloud_intensity", 0.55f);
        sky3d.Set("sky_contribution", Mathf.Clamp(s.ambient_from_sky * 0.85f, 0.55f, 1.0f));
        sky3d.Set("ambient_energy", Mathf.Clamp(s.ambient_from_sky, 0.8f, 1.6f));
        sky3d.Set("tonemap_exposure", s.exposure);
        sky3d.Set("camera_exposure", s.exposure);
        sky3d.Set("sun_shadow_opacity", s.sun_shadows ? 0.9f : 0.0f);

        if (sky3d.Get("environment").AsGodotObject() is Godot.Environment env)
        {
            env.TonemapMode = Godot.Environment.ToneMapper.Aces;
            env.TonemapWhite = 6.0f;
            env.GlowEnabled = s.glow_enabled;
            env.GlowIntensity = s.glow_intensity;
            env.SsilEnabled = s.ssil_enabled;
            env.SdfgiEnabled = s.sdfgi_enabled;
            env.SsaoEnabled = true;
            env.SsaoIntensity = 0.65f;
            env.SsaoRadius = 1.0f;
            // 在 Sky3D 自带雾 shader 之上叠加深度雾（与 UE tres 保持一致的观感延续）。
            bool fogOn = s.fog_enabled;
            if (fogOn)
            {
                env.FogEnabled = true;
                env.FogMode = Godot.Environment.FogModeEnum.Depth;
                env.FogLightColor = new Color(0.62f, 0.72f, 0.88f);
                float fogDist = s.fog_distance;
                float fogAmt = Mathf.Clamp(s.fog_intensity, 0.0f, 1.0f);
                env.FogDepthBegin = fogDist;
                env.FogDepthEnd = fogDist + Mathf.Lerp(16000.0f, 3200.0f, fogAmt);
                env.FogAerialPerspective = 0.45f * fogAmt;
            }
            else
            {
                env.FogEnabled = false;
            }
        }

        var sunLight = sky3d.GetNodeOrNull<DirectionalLight3D>("SunLight");
        if (sunLight != null)
        {
            sunLight.ShadowEnabled = s.sun_shadows;
            sunLight.ShadowBlur = 1.35f;
            sunLight.LightAngularDistance = s.sun_angular_distance;
            sunLight.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
            sunLight.DirectionalShadowMaxDistance = 6500.0f;
        }
    }

    private static void EnsureEnvironmentPhysical(Node root, UeStyleLitSettings s)
    {
        var we = root.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (we == null)
        {
            we = new WorldEnvironment { Name = "WorldEnvironment" };
            root.AddChild(we);
        }

        // PhysicalSky = Preetham 解析日光；太阳方向/颜色取自第一个 DirectionalLight3D。
        var skyMat = new PhysicalSkyMaterial
        {
            EnergyMultiplier = s.sky_energy,
            Turbidity = s.sky_turbidity,
            RayleighCoefficient = 2.2f,
            RayleighColor = new Color(0.28f, 0.42f, 0.72f),
            MieCoefficient = 0.005f,
            MieEccentricity = 0.85f,
            MieColor = new Color(0.95f, 0.92f, 0.85f),
            GroundColor = new Color(0.14f, 0.13f, 0.11f),
            SunDiskScale = 1.15f,
            UseDebanding = true,
        };

        var sky = new Sky { SkyMaterial = skyMat, ProcessMode = Sky.ProcessModeEnum.Realtime, RadianceSize = Sky.RadianceSizeEnum.Size256 };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = s.ambient_from_sky,
            AmbientLightSkyContribution = 1.0f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = s.exposure,
            TonemapWhite = 6.0f,
            GlowEnabled = s.glow_enabled,
            GlowIntensity = s.glow_intensity,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.0f,
            SsilEnabled = s.ssil_enabled,
            SsilIntensity = 0.85f,
            SsilRadius = 4.0f,
            SdfgiEnabled = s.sdfgi_enabled,
            SsaoEnabled = true,
            SsaoRadius = 1.0f,
            SsaoIntensity = 0.65f,
        };
        if (env.SdfgiEnabled)
        {
            env.SdfgiUseOcclusion = true;
            env.SdfgiCascades = 4;
            env.SdfgiMinCellSize = 8.0f;
            env.SdfgiEnergy = 0.9f;
        }

        bool fogOn = s.fog_enabled;
        env.FogEnabled = fogOn;
        env.FogLightColor = new Color(0.62f, 0.72f, 0.88f);
        env.FogMode = Godot.Environment.FogModeEnum.Depth;
        float fogDist = s.fog_distance;
        float fogAmt = Mathf.Clamp(s.fog_intensity, 0.0f, 1.0f);
        env.FogDepthBegin = fogDist;
        env.FogDepthEnd = fogDist + Mathf.Lerp(16000.0f, 3200.0f, fogAmt);
        env.FogDepthCurve = Mathf.Lerp(1.0f, 0.5f, fogAmt);
        env.FogAerialPerspective = fogOn ? 0.55f * fogAmt : 0.0f;
        env.FogSunScatter = fogOn ? 0.18f : 0.0f;
        env.FogDensity = 0.00005f;

        we.CameraAttributes = null;
        we.Environment = env;
    }

    private static void EnsureKeyLight(Node root, UeStyleLitSettings s)
    {
        var sun = root.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") ?? root.GetNodeOrNull<DirectionalLight3D>("Sun");
        if (sun == null)
        {
            sun = new DirectionalLight3D { Name = "DirectionalLight3D" };
            root.AddChild(sun);
        }
        // PhysicalSky 采样第一个 DirectionalLight3D——保持该节点排第一。
        root.MoveChild(sun, 0);
        sun.RotationDegrees = new Vector3(s.sun_pitch_deg, s.sun_yaw_deg, 0.0f);
        sun.LightColor = W3LitSettings.TemperatureToColor(s.sun_temperature_k);
        sun.LightEnergy = s.sun_intensity;
        sun.LightIndirectEnergy = 1.15f;
        sun.LightSpecular = 0.7f;
        sun.ShadowEnabled = s.sun_shadows;
        sun.ShadowBlur = 1.4f;
        sun.ShadowOpacity = 0.85f;
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        sun.DirectionalShadowMaxDistance = 6500.0f;
        sun.LightAngularDistance = s.sun_angular_distance;
        sun.Visible = true;
    }

    private static void DisableFillLight(Node root)
    {
        var fill = root.GetNodeOrNull<DirectionalLight3D>("FillLight3D");
        if (fill != null)
        {
            fill.Visible = false;
            fill.LightEnergy = 0.0f;
        }
    }
}
