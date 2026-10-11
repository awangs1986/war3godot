using System;
using Diablo1Like.W3.Config;
using Diablo1Like.W3.Game;
using Diablo1Like.W3.Lighting;
using Diablo1Like.W3.World;
using Godot;

namespace Diablo1Like.W3.Sandbox;

/// <summary>
/// War3 0.1 沙盒：Diablo 点击移动 + MDX，或自由飞行地图预览。
/// 迁移自 scripts/w3/w3_sandbox.gd（原 ~960 行单文件）。按职责拆分：
/// 加载遮罩 → <see cref="W3SandboxLoadingPresenter"/>，HUD/热键条/Bistro 面板 →
/// <see cref="W3SandboxHud"/>，控制台命令解析 → <see cref="W3SandboxDebugConsole"/>；
/// 本类只保留场景装配、输入分发与光照/相机档位这些必须贴着节点树跑的逻辑。
/// </summary>
[GlobalClass]
public partial class W3SandboxController : Node3D
{
    private static readonly float[] ZoomRenderScales = { 0.77f, 0.58f, 0.40f, 0.28f };

    [Export] public string MapPath = "";
    [Export] public Resource? ImportedLevel;
    [Export] public bool TerrainOnly;
    /// <summary>0.1 验收路径经 DiabloPlay 开启；默认关闭，沙盒默认以地图预览方式启动。
    /// 剑圣：/debug → M（保留相机，落地）→ 出生后转 Warsmash。绝不自动启动。</summary>
    [Export] public bool DiabloPlay;
    /// <summary>户外光照，可随时换 Resource：War3 → w3_lit_settings_preview.tres |
    /// UE → ue_style_lit_default.tres | Bistro → bistro_lit_default.tres。</summary>
    [Export] public Resource? LitSettings = GD.Load<Resource>("res://addons/w3kit/ue_style_lit_default.tres");
    /// <summary>F 键运行时也会切换雾效。</summary>
    [Export] public bool PreviewFog = true;
    /// <summary>资产根下的相对 MDX 路径（0.1 默认步兵）。</summary>
    [Export] public string PlayerMdx = "Units/Human/Footman/Footman.mdx";

    public W3MapWorld? World { get; private set; }
    public FreeFlyCamera? Cam { get; private set; }
    public Resource? Settings { get; private set; }
    public string Weather = "default"; // /sun -> "sun"，/fog -> "overcast"
    public W3TileDebugOverlay? TileDebug { get; private set; }
    public W3SelectionController? UnitSelector { get; private set; }
    public W3GameRuntime? Game { get; private set; }
    public bool ConsoleOpen { get; private set; }
    public Vector2I FocusedTile = new(-1, -1);
    public bool InspectMode { get; private set; }
    public bool SoftTint { get; private set; }
    public bool DebugTools { get; private set; }
    public W3LightProbe? LightOrb { get; private set; }
    public float RenderScale { get; private set; } = 0.77f;
    public bool FogOn = true;

    private Label? _hud;
    private Control? _console;
    private LineEdit? _consoleEdit;
    private Control? _dirLegend;
    private Node? _player;
    private Node? _iso;

    private readonly System.Collections.Generic.Dictionary<string, float> _savedLightEnergies = new();
    private float _hudTimer;
    private int _lightingLod = -1;
    private bool _nativeSmoke;

    private W3SandboxLoadingPresenter _loading = null!;
    private W3SandboxHud _sandboxHud = null!;
    private W3SandboxDebugConsole _debugConsole = null!;

    public override void _Ready()
    {
        World = GetNode<W3MapWorld>("W3MapWorld");
        Cam = GetNode<FreeFlyCamera>("FreeFlyCamera");
        _hud = GetNode<Label>("CanvasLayer/Status");
        _console = GetNode<Control>("CanvasLayer/GotoConsole");
        _consoleEdit = GetNode<LineEdit>("CanvasLayer/GotoConsole/VBox/LineEdit");
        _dirLegend = GetNode<Control>("CanvasLayer/DirLegend");
        var loadingOverlay = GetNode<Control>("CanvasLayer/LoadingOverlay");
        var loadingTitle = GetNode<Label>("CanvasLayer/LoadingOverlay/Title");
        var loadingProgress = GetNode<ProgressBar>("CanvasLayer/LoadingOverlay/Progress");
        var loadingDetail = GetNode<Label>("CanvasLayer/LoadingOverlay/Detail");
        _player = GetNodeOrNull("W3GridPlayer");
        _iso = GetNodeOrNull("IsoCamera");

        _loading = new W3SandboxLoadingPresenter(this, loadingOverlay, loadingTitle, loadingProgress, loadingDetail, _hud, Cam);
        _loading.OnFinished += () => _sandboxHud.UpdateHud();
        _sandboxHud = new W3SandboxHud(this, GetNodeOrNull<CanvasLayer>("CanvasLayer"), _hud);
        _debugConsole = new W3SandboxDebugConsole(this, _consoleEdit);

        RunReadySequenceAsync();
    }

    private async void RunReadySequenceAsync()
    {
        _nativeSmoke = OS.GetEnvironment("D1MAP_SMOKE") == "1";
        _loading.Begin();
        // 遮罩必须先上屏，才能开始同步的地形/悬崖构建。
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (_player != null)
        {
            _player.Set("visible", false);
            _player.SetProcess(false);
            _player.SetPhysicsProcess(false);
            _player.SetProcessInput(false);
            _player.SetProcessUnhandledInput(false);
        }
        if (_iso != null)
        {
            _iso.Set("visible", false);
            _iso.SetProcess(false);
            if (_iso.GetNodeOrNull("Camera3D") is Camera3D isoCam)
                isoCam.Current = false;
        }
        if (Cam != null)
        {
            Cam.Visible = true;
            Cam.SetProcess(true);
            Cam.SetProcessUnhandledInput(true);
            if (World!.map != null)
            {
                var mid = new Vector2I(World.map.Call("tile_count_x").AsInt32() / 2, World.map.Call("tile_count_y").AsInt32() / 2);
                Vector3 center = World.sampler!.Call("tile_center_world", mid).AsVector3();
                float extent = Mathf.Max(World.map.Call("tile_count_x").AsInt32(), World.map.Call("tile_count_y").AsInt32()) * 128.0f;
                Cam.focus_point(center, extent * 0.65f);
            }
            else
            {
                Cam.Current = true;
            }
        }
        GD.Print("[W3Sandbox] preview fly ON");

        World!.terrain_only = TerrainOnly;
        // 地图移动单位交给模拟层（活体怪物），而不是静态批处理雕像。
        World.simulate_units = !TerrainOnly;
        ApplyLighting();
        // 运行时地形和数千个高清实例构建期间保持动态 GI/体积雾关闭，相机对焦后再开启。
        W3LightingRouter.ApplyQualityLod(this, 3, Settings);
        _lightingLod = 3;

        string path = MapPath;
        if (path.StartsWith("res://") || path.StartsWith("user://"))
            path = ProjectSettings.GlobalizePath(path);
        // 外部编辑器（standalone/W3Editor）预览：`-- --map=<abs path>`。
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--map="))
            {
                path = arg[6..];
                if (path.StartsWith("res://") || path.StartsWith("user://"))
                    path = ProjectSettings.GlobalizePath(path);
            }
            else if (arg == "--embedded")
            {
                // 嫁接进外部编辑器：铺满宿主区域而不是按项目 16:9 设计分辨率letterbox。
                GetWindow().ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
                // WS_CHILD 窗口永远不会获得 Win32 激活，会静默杀死 MOUSE_MODE_CAPTURED——
                // 改用相机基于 warp 的环顾模拟。
                if (Cam != null)
                    Cam.emulate_capture = true;
            }
            else if (arg == "--native-smoke")
            {
                _nativeSmoke = true;
            }
        }
        var assetConfig = GetNodeOrNull<W3AssetConfiguration>("/root/W3AssetConfig");
        if (path.Length == 0)
            path = assetConfig?.DefaultMapPath ?? "";
        if (assetConfig != null)
        {
            Godot.Collections.Dictionary report = assetConfig.CheckReport();
            foreach (Variant m in report["messages"].AsGodotArray())
                GD.Print("[W3Asset] ", m);
        }
        GD.Print("[W3Sandbox] preview map: ", path);
        _loading.SetLoadingVisual(0.08f, "正在读取地图和地形");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        bool loadedOk;
        if (ImportedLevel != null)
            loadedOk = World.load_level_data(ImportedLevel);
        else if (path.Length == 0)
            loadedOk = World.load_procedural();
        else
            loadedOk = World.load_path(path);
        if (!loadedOk)
        {
            _loading.ShowLoadingError(World.get_status());
            if (_nativeSmoke)
                GetTree().Quit(1);
            return;
        }
        if (_nativeSmoke)
        {
            GD.Print("[D1MapSmoke] PASS ", World.get_status());
            GetTree().Quit(0);
            return;
        }
        _loading.SetLoadingVisual(0.22f, "地形完成，正在载入高清模型");
        if (!TerrainOnly)
            StartGameLayer();

        if (World.map != null && Cam != null)
        {
            var mid = new Vector2I(World.map.Call("tile_count_x").AsInt32() / 2, World.map.Call("tile_count_y").AsInt32() / 2);
            Vector3 center = World.sampler!.Call("tile_center_world", mid).AsVector3();
            float extent = Mathf.Max(World.map.Call("tile_count_x").AsInt32(), World.map.Call("tile_count_y").AsInt32()) * 128.0f;
            // 大地图应该从足够近的距离开始，让预览 LOD 显示附近的道具和单位，
            // 而不是呈现一张看起来空荡荡的全图俯瞰。
            Cam.focus_point(center, Mathf.Clamp(extent * 0.30f, 1200.0f, 4200.0f));
        }
        else if (Cam != null)
        {
            Cam.Current = true;
        }
        // 现在就匹配最终渲染分辨率，但阴影/GI 延后到所有流式模型批次就绪后再开启；
        // 稳定帧阶段会负责预热它们。
        ApplyZoomRenderScale(false);

        if (_consoleEdit != null)
        {
            _consoleEdit.TextSubmitted += _debugConsole.OnSubmit;
            _consoleEdit.PlaceholderText = "x,y 或 /debug /sun /fog";
        }
        EnsureUnitSelector();
        SetConsoleOpen(false);
        if (_dirLegend != null)
            _dirLegend.Visible = false;
        _sandboxHud.UpdateHud();
        _loading.LoadSetupComplete = true;
    }

    public override void _Process(double delta)
    {
        if (_loading.Loading)
        {
            _loading.UpdateLoadingScreen(ApplyZoomRenderScale, GetBatcherLoadingState, TerrainOnly);
            return;
        }
        _hudTimer += (float)delta;
        if (_hudTimer >= 0.25f)
        {
            _hudTimer = 0.0f;
            ApplyZoomRenderScale();
            _sandboxHud.UpdateHud();
        }
        if (ConsoleOpen)
            return;
        if (!DebugTools)
            return;
        if (TileDebug != null && TileDebug.is_debug_enabled())
        {
            Camera3D? cam = GetViewport().GetCamera3D() ?? Cam;
            TileDebug.update_hover(cam);
            _sandboxHud.UpdateHud();
        }
    }

    private Godot.Collections.Dictionary? GetBatcherLoadingState() =>
        World?.batcher != null ? World.batcher.get_loading_state() : null;

    public void EnsureLightOrb()
    {
        if (LightOrb != null)
        {
            LightOrb.RefreshWorld(World);
            return;
        }
        var orb = new W3LightProbe { Name = "LightOrbProbe", ProbeEnabled = false };
        AddChild(orb);
        LightOrb = orb;
        orb.Setup(Cam!, World);
        // Setup 可能重新应用导出默认值——强制关闭。
        orb.SetProbeEnabled(false);
        GD.Print("[W3Sandbox] light orb OFF (L to enable)");
    }

    public override void _Input(InputEvent @event)
    {
        if (_loading.Loading)
            return;
        // 即便 LineEdit 拿到焦点，也要能捕获 `。
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode is Key.Quoteleft or Key.Asciitilde)
            {
                SetConsoleOpen(!ConsoleOpen);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (ConsoleOpen && key.Keycode == Key.Escape)
            {
                SetConsoleOpen(false);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_loading.Loading || ConsoleOpen)
            return;
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            // 玩家单位存在时 LMB 归它所有：攻击敌人，否则移动。
            if (Game != null && Game.has_live_player())
            {
                Game.issue_player_order(Cam!, mb.Position);
                _sandboxHud.UpdateHud();
                GetViewport().SetInputAsHandled();
                return;
            }
            EnsureUnitSelector();
            if (UnitSelector != null)
            {
                UnitSelector.Pick(Cam, mb.Position);
                _sandboxHud.UpdateHud();
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.F)
            {
                FogOn = !FogOn;
                ApplyLighting();
                if (InspectMode)
                {
                    World?.terrain?.SetFlatInspect(true);
                    World?.cliffs?.SetFlatInspect(true);
                }
                _sandboxHud.UpdateHud();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.L)
            {
                EnsureLightOrb();
                LightOrb?.Toggle();
                GD.Print("[W3Sandbox] light orb=", LightOrb?.ProbeEnabled ?? false);
                _sandboxHud.UpdateHud();
                GetViewport().SetInputAsHandled();
            }
            else if (!DebugTools)
            {
                // 非调试模式下其余热键无效。
            }
            else if (key.Keycode == Key.N)
            {
                ToggleUnitLabels();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.G)
            {
                EnsureTileDebug();
                if (TileDebug != null)
                {
                    TileDebug.toggle();
                    _sandboxHud.UpdateHud();
                }
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Tab)
            {
                ToggleInspectMode();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.T)
            {
                SpawnPlayerAtMouse();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.H)
            {
                SoftTint = !SoftTint;
                World?.terrain?.SetSoftTint(SoftTint);
                GD.Print("[W3Sandbox] soft_tint=", SoftTint);
                _sandboxHud.UpdateHud();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.M)
            {
                // 剑圣调试指挥模式：底层挂钩尚未实现（EnsureSpawnPawn 空实现），
                // 与迁移前一致——按键被消费但无可见效果。
                EnsureSpawnPawn();
                _sandboxHud.UpdateHud();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public void ToggleInspectMode()
    {
        InspectMode = !InspectMode;
        if (World?.water != null)
            World.water.Visible = !InspectMode;
        foreach (Node n in FindChildren("*", "Light3D", true, false))
        {
            if (n is not Light3D light)
                continue;
            string path = light.GetPath().ToString();
            if (InspectMode)
            {
                _savedLightEnergies[path] = light.LightEnergy;
                light.LightEnergy = 0.0f;
            }
            else
            {
                light.LightEnergy = _savedLightEnergies.TryGetValue(path, out float saved) ? saved : 1.0f;
            }
        }
        if (!InspectMode)
            _savedLightEnergies.Clear();
        if (InspectMode)
            FogOn = false;
        ApplyLighting();
        // 灯光同步完成后再压平反照率（灯光应用会覆盖环境光/SHD）。
        World?.terrain?.SetFlatInspect(InspectMode);
        World?.cliffs?.SetFlatInspect(InspectMode);
        _sandboxHud.UpdateHud();
    }

    public void SetDebugTools(bool on)
    {
        DebugTools = on;
        if (on)
        {
            EnsureTileDebug();
            GD.Print("[W3Sandbox] debug ON — G=grid Tab=inspect(无光) H=soft染色 T=剑圣 LMB=移动 `=goto");
        }
        else
        {
            DisableAllDebugVisuals();
            GD.Print("[W3Sandbox] debug OFF");
        }
        _sandboxHud.UpdateHud();
    }

    private void DisableAllDebugVisuals()
    {
        if (InspectMode)
            ToggleInspectMode();
        if (SoftTint)
        {
            SoftTint = false;
            World?.terrain?.SetSoftTint(false);
        }
        TileDebug?.set_debug_enabled(false);
        if (Game != null && Game.has_live_player())
        {
            Game.sim!.remove_unit(Game.player);
            Game.player = null;
        }
        FocusedTile = new Vector2I(-1, -1);
        if (_dirLegend != null)
            _dirLegend.Visible = false;
    }

    public void SetConsoleOpen(bool open)
    {
        ConsoleOpen = open;
        if (_console != null)
            _console.Visible = open;
        if (Cam != null)
            Cam.input_blocked = open;
        if (open)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Cam?.release_look();
            if (_consoleEdit != null)
            {
                _consoleEdit.GrabFocus();
                _consoleEdit.SelectAll();
            }
        }
        else
        {
            _consoleEdit?.ReleaseFocus();
            // 若点光源探针启用中，恢复隐藏光标。
            if (LightOrb is { ProbeEnabled: true })
                Input.MouseMode = Input.MouseModeEnum.Hidden;
        }
    }

    public bool UnitLabelsOn()
    {
        if (World?.batcher == null)
            return false;
        return World.batcher.HasMethod("are_unit_labels_visible") && World.batcher.Call("are_unit_labels_visible").AsBool();
    }

    public void ToggleUnitLabels()
    {
        if (!DebugTools || World?.batcher == null)
            return;
        if (!World.batcher.HasMethod("set_unit_labels_visible"))
            return;
        bool on = !UnitLabelsOn();
        World.batcher.Call("set_unit_labels_visible", on);
        _sandboxHud.UpdateHud();
    }

    public void EnsureTileDebug()
    {
        if (TileDebug != null || World?.map == null)
            return;
        var overlay = new W3TileDebugOverlay { Name = "TileDebugOverlay" };
        AddChild(overlay);
        TileDebug = overlay;
        overlay.setup(World.map);
        overlay.set_debug_enabled(false);
    }

    /// <summary>剑圣（Blade Master）调试功能预留钩子：目前没有对应的操纵/拾取实现，
    /// 保持空——M 键在调试模式下暂无可见效果，与迁移前一致。</summary>
    private void EnsureSpawnPawn()
    {
    }

    /// <summary>地图怪物在加载时变成实时模拟单位；玩家单位按需生成（调试模式下按 T）。</summary>
    public void StartGameLayer()
    {
        if (Game != null)
            return;
        var game = new W3GameRuntime { Name = "W3Game" };
        AddChild(game);
        if (!game.setup(World!))
        {
            game.QueueFree();
            GD.PushError("[W3Sandbox] game layer setup failed");
            return;
        }
        Game = game;
    }

    public void SpawnPlayerAtMouse()
    {
        if (Game == null || World?.map == null || World.sampler == null)
            return;
        Variant hitVariant = World.pick_ground(Cam!, GetViewport().GetMousePosition());
        Vector3 hit;
        if (hitVariant.VariantType == Variant.Type.Nil)
        {
            // 鼠标下没有地形：退回地图中心。
            var mid = new Vector2I(World.map.Call("tile_count_x").AsInt32() / 2, World.map.Call("tile_count_y").AsInt32() / 2);
            hit = World.sampler.Call("tile_center_world", mid).AsVector3();
        }
        else
        {
            hit = hitVariant.AsVector3();
        }
        Game.spawn_player(hit);
        GD.Print($"[W3Sandbox] player at {hit} | LMB=移动/攻击");
        _sandboxHud.UpdateHud();
    }

    public void EnsureUnitSelector()
    {
        if (UnitSelector != null || World?.map == null || World.sampler == null)
            return;
        var selector = new W3SelectionController { Name = "W3UnitSelection" };
        AddChild(selector);
        UnitSelector = selector;
        selector.Setup(World);
        GD.Print("[W3Sandbox] LMB selects visible W3 units through physics colliders");
    }

    public void ApplyLighting()
    {
        Resource? settings = LitSettings;
        if (settings == null || settings.GetScript().AsGodotObject() == null)
            settings = GD.Load<Resource>("res://addons/w3kit/ue_style_lit_default.tres");
        settings ??= new UeStyleLitSettings();
        Settings = (Resource)settings.Duplicate(true);
        // weather 仅 W3LitSettings 有对应字段；Bistro/UE 预设上是无操作（与旧 GDS 动态属性语义一致）。
        Settings.Set("weather", Weather);
        Settings.Set("fog_enabled", PreviewFog && FogOn);
        W3LightingRouter.ApplyToScene(this, Settings);
    }

    public void ApplyZoomRenderScale(bool applyLightingLod = true)
    {
        if (World?.batcher == null)
            return;
        int lod = CameraRenderLod();
        float desired = ZoomRenderScales[lod];
        if (!Mathf.IsEqualApprox(desired, RenderScale))
        {
            RenderScale = desired;
            Viewport? viewport = GetViewport();
            if (viewport != null)
                viewport.Scaling3DScale = desired;
        }
        if (applyLightingLod && lod != _lightingLod)
        {
            _lightingLod = lod;
            W3LightingRouter.ApplyQualityLod(this, lod, Settings);
        }
    }

    private int CameraRenderLod()
    {
        if (Cam == null)
            return 3;
        if (Cam.Projection == Camera3D.ProjectionType.Orthogonal)
        {
            if (Cam.Size >= 2600.0f)
                return 3;
            if (Cam.Size >= 1500.0f)
                return 2;
            if (Cam.Size >= 800.0f)
                return 1;
            return 0;
        }
        // War3 地图不共享 Y=0 地形基准线，改用相机相对地面采样高度的高度，让光照档位在任何地方都随缩放一致变化。
        float groundY = World?.sampler != null
            ? World.sampler.Call("sample_y", Cam.GlobalPosition.X, Cam.GlobalPosition.Z).AsSingle()
            : 0.0f;
        float height = Mathf.Max(0.0f, Cam.GlobalPosition.Y - groundY);
        if (height >= 2800.0f)
            return 3;
        if (height >= 1600.0f)
            return 2;
        if (height >= 850.0f)
            return 1;
        return 0;
    }

    // ---- GDScript 门面 ----
    // tools/*.gd（w3_loading_screen_smoke、w3_tps_lighting_smoke、w3_reforged_scene_smoke、
    // w3_hero_screenshot_full 等）沿用旧 w3_sandbox.gd 的 snake_case 成员名动态访问本节点，
    // 必须原样保留，否则这些脚本会以 "Nonexistent function" 报错并卡死 SceneTree。
    public bool is_loading_complete() => !_loading.Loading;

    public float get_loading_progress() => _loading.GetLoadingProgress();

    /// <summary>旧 GDS 私有方法名，w3_tps_lighting_smoke.gd 直接调用。</summary>
    public void _apply_zoom_render_scale() => ApplyZoomRenderScale();

    /// <summary>旧 GDS 里 tools 靠 get("_world") / get("_game") / get("_loading") 摸私有字段；
    /// C# 非 [Export] 成员不进属性表，改为公开取值方法，调用方 tools 已同步更新。</summary>
    public W3MapWorld? get_world() => World;

    public W3GameRuntime? get_game() => Game;

    public bool is_loading() => _loading.Loading;

    public void UpdateHud() => _sandboxHud.UpdateHud();

    public void SetDirLegendVisible(bool visible)
    {
        if (_dirLegend != null)
            _dirLegend.Visible = visible;
    }
}
