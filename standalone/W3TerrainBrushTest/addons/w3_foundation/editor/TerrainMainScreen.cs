using System;
using Godot;
using W3Foundation.Preview;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Rendering;

namespace W3Foundation.Editor;

/// <summary>Top-level Godot editor workspace for Warcraft-style terrain editing.</summary>
[Tool]
public partial class TerrainMainScreen : Control
{
    private readonly TerrainPaletteDock _palette;
    private readonly TerrainViewportInputBridge _viewportContainer;
    private readonly SubViewport _viewport;
    private readonly TerrainWorld _previewTerrain;
    private readonly TerrainPreviewCamera _camera;
    private readonly TerrainMainScreenBrush _brush;
    private readonly Label _status;
    private readonly Label _emptyMessage;
    private TerrainWorld? _activeSceneTerrain;
    private TerrainDocument? _boundDocument;

    public event Action? CreateTerrainRequested;
    public event Action<TerrainDocument, TerrainDocument>? StrokeCommitted;
    public event Action? UndoRequested;

    public TerrainMainScreen()
    {
        Name = "W3TerrainMainScreen";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var split = new HSplitContainer
        {
            Name = "WorkspaceSplit",
            SplitOffsets = new[] { 330 },
        };
        split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(split);

        var tools = new VBoxContainer { CustomMinimumSize = new Vector2(320.0f, 0.0f) };
        split.AddChild(tools);
        tools.AddChild(new Label
        {
            Text = "W3 Terrain",
            ThemeTypeVariation = "HeaderLarge",
        });
        tools.AddChild(new Label
        {
            Text = "独立地形画布 · 不经过 Godot 3D 框选工具",
            Modulate = new Color(0.72f, 0.8f, 0.9f),
        });

        _palette = new TerrainPaletteDock
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(310.0f, 500.0f),
        };
        _palette.CreateTerrainRequested += () => CreateTerrainRequested?.Invoke();
        _palette.BrushModeChanged += OnBrushModeChanged;
        tools.AddChild(_palette);

        _status = new Label
        {
            Text = "请选择 TerrainWorld",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(300.0f, 42.0f),
        };
        tools.AddChild(_status);

        var viewportFrame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(viewportFrame);
        _viewportContainer = new TerrainViewportInputBridge
        {
            Name = "TerrainCanvas",
            Stretch = true,
            MouseTarget = true,
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        viewportFrame.AddChild(_viewportContainer);

        _viewport = new SubViewport
        {
            Name = "TerrainViewport",
            OwnWorld3D = true,
            HandleInputLocally = true,
            GuiDisableInput = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
            UseHdr2D = true,
        };
        _viewportContainer.AddChild(_viewport);
        _viewportContainer.TargetViewport = _viewport;

        var world = new Node3D { Name = "PreviewWorld" };
        _viewport.AddChild(world);
        _previewTerrain = new TerrainWorld
        {
            Name = "TerrainPreview",
            CreateDefaultDocument = false,
            ShowTileGrid = true,
            ChunkSize = 16,
        };
        world.AddChild(_previewTerrain);

        _camera = new TerrainPreviewCamera
        {
            Name = "Camera3D",
            Current = true,
            Far = 30000.0f,
            MoveSpeed = 1800.0f,
        };
        world.AddChild(_camera);

        world.AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-48.0f, 35.0f, 0.0f),
            LightColor = new Color(1.0f, 0.91f, 0.8f),
            LightEnergy = 1.2f,
            ShadowEnabled = true,
        });
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.20f, 0.27f, 0.34f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.68f, 0.76f),
            AmbientLightEnergy = 0.75f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.45f,
        };
        world.AddChild(new WorldEnvironment { Name = "Environment", Environment = environment });

        _brush = new TerrainMainScreenBrush
        {
            Name = "TerrainBrushInput",
            BrushProvider = () => _palette.CurrentBrush,
            BrushEnabledProvider = () => _palette.BrushEnabled,
            PreviewColorProvider = () => _palette.PreviewColor,
        };
        _brush.StrokeCommitted += (before, after) => StrokeCommitted?.Invoke(before, after);
        _brush.RadiusDeltaRequested += delta => _palette.SetBrushRadius(_palette.BrushRadius + delta);
        _brush.BrushEnabledRequested += _palette.SetBrushEnabled;
        _brush.UndoRequested += () => UndoRequested?.Invoke();
        _brush.StatusChanged += text => _status.Text = text;
        world.AddChild(_brush);
        _brush.Setup(_previewTerrain, _camera);
        _viewportContainer.MouseExited += () =>
        {
            _viewportContainer.NotifyTerrainMouseExited();
            _brush.PointerExited();
        };

        _emptyMessage = new Label
        {
            Text = "当前场景没有 TerrainWorld\n请在场景树中选择地形，或点击左侧创建 32×32 地形",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _emptyMessage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        viewportFrame.AddChild(_emptyMessage);

        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_activeSceneTerrain == null || !IsInstanceValid(_activeSceneTerrain))
            return;
        if (!ReferenceEquals(_boundDocument, _activeSceneTerrain.Document))
            BindDocument(_activeSceneTerrain.Document);
    }

    public void SetActiveTerrain(TerrainWorld? terrain)
    {
        _activeSceneTerrain = terrain;
        _palette.SetActiveTerrain(terrain);
        BindDocument(terrain?.Document);
    }

    public void FocusTerrain()
    {
        if (_boundDocument == null)
            return;
        float width = _boundDocument.TileWidth * _boundDocument.TileSize;
        float depth = _boundDocument.TileHeight * _boundDocument.TileSize;
        Vector3 center = new(width * 0.5f, 0.0f, depth * 0.5f);
        center.Y = _previewTerrain.SampleHeight(new Vector2(center.X, center.Z));
        float span = Math.Max(width, depth);
        _camera.Projection = Camera3D.ProjectionType.Perspective;
        _camera.Fov = 52.0f;
        _camera.GlobalPosition = center + new Vector3(0.0f, span * 0.57f, span * 0.50f);
        _camera.LookAt(center, Vector3.Up);
    }

    private void BindDocument(TerrainDocument? document)
    {
        _boundDocument = document;
        _previewTerrain.Document = document;
        _previewTerrain.Visible = document != null;
        _emptyMessage.Visible = document == null;
        if (document == null)
        {
            _status.Text = "请选择 TerrainWorld，或创建 32×32 地形";
            return;
        }
        document.EnsureInitialized();
        _previewTerrain.QueueFullRebuild();
        _status.Text = "左键绘制 · 右键旋转 · 滚轮缩放 · Shift+滚轮调笔刷 · Ctrl+Z 撤销";
        Callable.From(FocusTerrain).CallDeferred();
    }

    private void OnBrushModeChanged(bool enabled)
    {
        if (!enabled)
            _brush.PointerExited();
    }
}
