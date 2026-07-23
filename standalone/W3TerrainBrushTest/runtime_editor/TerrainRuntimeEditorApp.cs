using System;
using Godot;
using W3Foundation.Editor;
using W3Foundation.Preview;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Rendering;

namespace W3Foundation.RuntimeEditor;

/// <summary>
/// Runtime terrain editor shell (F5-run scene, no EditorPlugin). It reuses the
/// proven pieces unchanged: TerrainNative3DBrush + HiveWE port for painting,
/// TerrainPaletteDock for tool selection, TerrainWorld/TestTerrainWorld for
/// rendering and TerrainPreviewCamera for navigation. This class only wires
/// them together and owns file/undo state.
/// </summary>
public partial class TerrainRuntimeEditorApp : Node
{
    private readonly TerrainEditHistory _history = new();
    private TerrainWorld _terrain = null!;
    private TerrainPreviewCamera _camera = null!;
    private TerrainPaletteDock _palette = null!;
    private TerrainNative3DBrush _brush = null!;
    private TerrainEditorOverlay _overlay = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Label _fileLabel = null!;
    private FileDialog _saveDialog = null!;
    private FileDialog _openDialog = null!;
    private string? _currentPath;
    private bool _unsaved;

    public override void _Ready()
    {
        GetWindow().Title = "W3 地形编辑器";
        BuildWorld();
        BuildBrush();
        BuildUi();
        BuildInputRouter();
        FrameCamera();
        _brush.SetTerrain(_terrain);
        _palette.SetActiveTerrain(_terrain);
        _history.Changed += () => UpdateStatus();
        UpdateStatus();
        HandleScreenshotMode();
    }

    /// <summary>
    /// Automated visual acceptance: `-- --screenshot <path.png>` renders a few
    /// frames, saves the viewport to the given file and quits. This replaces
    /// manual "open the editor and look" round-trips for framing regressions.
    /// </summary>
    private async void HandleScreenshotMode()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int flag = System.Array.IndexOf(args, "--screenshot");
        if (flag < 0 || flag + 1 >= args.Length)
            return;
        string path = args[flag + 1];
        for (int i = 0; i < 8; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(path);
        GD.Print($"[screenshot] {(error == Error.Ok ? "saved" : error.ToString())} {path}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    // ---------------------------------------------------------------- world

    private void BuildWorld()
    {
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.30f, 0.46f, 0.68f),
                SkyHorizonColor = new Color(0.64f, 0.70f, 0.76f),
                GroundBottomColor = new Color(0.16f, 0.15f, 0.14f),
                GroundHorizonColor = new Color(0.64f, 0.70f, 0.76f),
            },
        };
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightSkyContribution = 0.6f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });

        var light = new DirectionalLight3D
        {
            Name = "SunLight",
            ShadowEnabled = true,
            LightEnergy = 1.15f,
        };
        light.RotationDegrees = new Vector3(-52.0f, -32.0f, 0.0f);
        AddChild(light);

        _terrain = new TestTerrainWorld { Name = "TerrainWorld" };
        AddChild(_terrain);

        _camera = new TerrainPreviewCamera
        {
            Name = "EditorCamera",
            Fov = 62.0f,
            Far = 100000.0f,
            Current = true,
        };
        AddChild(_camera);
    }

    private void BuildBrush()
    {
        _brush = new TerrainNative3DBrush(
            () => _palette.CurrentBrush,
            () => _palette.BrushEnabled,
            () => _palette.PreviewColor,
            CommitStroke);
    }

    /// <summary>Same terrain framing as the former editor-plugin focus logic:
    /// map center target, high distant view containing the whole map.</summary>
    private void FrameCamera()
    {
        TerrainDocument? document = _terrain.Document;
        if (document == null)
            return;
        float highest = float.MinValue;
        float lowest = float.MaxValue;
        for (int y = 0; y < document.CornerHeight; y++)
        for (int x = 0; x < document.CornerWidth; x++)
        {
            float height = document.GetWorldHeight(x, y);
            highest = Math.Max(highest, height);
            lowest = Math.Min(lowest, height);
        }
        float width = document.TileWidth * document.TileSize;
        float depth = document.TileHeight * document.TileSize;
        float span = Math.Max(width, depth);
        Vector3 center = _terrain.ToGlobal(new Vector3(width * 0.5f, (lowest + highest) * 0.5f, depth * 0.5f));
        _camera.GlobalPosition = center + new Vector3(0.0f, span * 1.1f, span * 0.72f);
        _camera.LookAt(center, Vector3.Up);
    }

    // ------------------------------------------------------------------- ui

    private void BuildUi()
    {
        // Brush palette must be created before any status update references it.
        _palette = new TerrainPaletteDock
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _palette.CreateTerrainRequested += NewDocument;
        _palette.BrushModeChanged += OnBrushModeChanged;

        var layer = new CanvasLayer { Name = "Ui" };
        AddChild(layer);

        _overlay = new TerrainEditorOverlay { Name = "BrushOverlay" };
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.Configure(_brush);
        layer.AddChild(_overlay);

        var panel = new PanelContainer { Name = "LeftPanel" };
        panel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        panel.MouseEntered += () => _brush.NotifyPointerExited();
        layer.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        var fileRow = new HBoxContainer();
        fileRow.AddChild(MakeButton("新建", NewDocument));
        fileRow.AddChild(MakeButton("打开", () => ShowDialog(_openDialog)));
        fileRow.AddChild(MakeButton("保存", SaveCurrent));
        fileRow.AddChild(MakeButton("另存为", () => ShowDialog(_saveDialog)));
        column.AddChild(fileRow);

        var editRow = new HBoxContainer();
        _undoButton = MakeButton("撤销 (Ctrl+Z)", Undo);
        _redoButton = MakeButton("重做 (Ctrl+Y)", Redo);
        editRow.AddChild(_undoButton);
        editRow.AddChild(_redoButton);
        column.AddChild(editRow);

        _fileLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.82f, 0.86f, 0.9f),
        };
        column.AddChild(_fileLabel);
        column.AddChild(new HSeparator());
        column.AddChild(_palette);

        _saveDialog = MakeFileDialog(FileDialog.FileModeEnum.SaveFile, "保存地形文档");
        _saveDialog.FileSelected += SaveTo;
        layer.AddChild(_saveDialog);

        _openDialog = MakeFileDialog(FileDialog.FileModeEnum.OpenFile, "打开地形文档");
        _openDialog.FileSelected += LoadFrom;
        layer.AddChild(_openDialog);
    }

    private static Button MakeButton(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += () => pressed();
        return button;
    }

    private FileDialog MakeFileDialog(FileDialog.FileModeEnum mode, string title)
    {
        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = mode,
            Title = title,
            Filters = new[] { "*.tres ; 地形文档" },
            CurrentFile = "terrain.tres",
            Size = new Vector2I(900, 600),
        };
        // WASD flying must not trigger while the user types a file name.
        dialog.VisibilityChanged += () => _camera.SetProcess(!dialog.Visible);
        return dialog;
    }

    private void ShowDialog(FileDialog dialog)
    {
        _brush.Deactivate();
        dialog.PopupCentered();
    }

    // ------------------------------------------------------------ edit flow

    private void CommitStroke(TerrainDocument before, TerrainDocument after)
    {
        _history.Push(before, after);
        _unsaved = true;
        UpdateStatus();
    }

    public void Undo() => ApplySnapshot(_history.Undo());

    public void Redo() => ApplySnapshot(_history.Redo());

    private void ApplySnapshot(TerrainDocument? snapshot)
    {
        if (snapshot == null)
            return;
        // History entries stay immutable: the live document is always a clone.
        _terrain.Document = snapshot.CloneDocument();
        _brush.SetTerrain(_terrain);
        _palette.SetActiveTerrain(_terrain);
        _unsaved = true;
        UpdateStatus();
    }

    public void SetBrushMode(bool enabled)
    {
        _palette.SetBrushEnabled(enabled);
    }

    private void OnBrushModeChanged(bool enabled)
    {
        if (!enabled)
            _brush.Deactivate();
    }

    private void NewDocument()
    {
        var document = new TerrainDocument { TileWidth = 32, TileHeight = 32 };
        document.EnsureInitialized();
        _terrain.Document = document;
        _brush.SetTerrain(_terrain);
        _palette.SetActiveTerrain(_terrain);
        _history.Clear();
        _currentPath = null;
        _unsaved = true;
        FrameCamera();
        UpdateStatus();
    }

    // ------------------------------------------------------------ file flow

    public void SaveCurrent()
    {
        if (_currentPath == null)
            ShowDialog(_saveDialog);
        else
            SaveTo(_currentPath);
    }

    private void SaveTo(string path)
    {
        if (_terrain.Document == null)
            return;
        Error error = ResourceSaver.Save(_terrain.Document, path);
        if (error != Error.Ok)
        {
            UpdateStatus($"保存失败：{error}");
            return;
        }
        _currentPath = path;
        _unsaved = false;
        UpdateStatus();
    }

    private void LoadFrom(string path)
    {
        var document = ResourceLoader.Load(path, cacheMode: ResourceLoader.CacheMode.Ignore) as TerrainDocument;
        if (document == null)
        {
            UpdateStatus($"打开失败：不是有效的地形文档 {path}");
            return;
        }
        document.EnsureInitialized();
        _terrain.Document = document;
        _brush.SetTerrain(_terrain);
        _palette.SetActiveTerrain(_terrain);
        _history.Clear();
        _currentPath = path;
        _unsaved = false;
        FrameCamera();
        UpdateStatus();
    }

    // --------------------------------------------------------------- status

    private void UpdateStatus(string? message = null)
    {
        _undoButton.Disabled = !_history.CanUndo;
        _redoButton.Disabled = !_history.CanRedo;
        string file = _currentPath ?? "未保存的新地形";
        string dirty = _unsaved ? " ●" : "";
        _fileLabel.Text = message == null ? $"{file}{dirty}" : $"{file}{dirty}\n{message}";
        GetWindow().Title = $"W3 地形编辑器 - {file}{dirty}";
    }

    private void BuildInputRouter()
    {
        var router = new TerrainEditorInputRouter { Name = "InputRouter" };
        router.Configure(this, _brush, _camera, () => _overlay.QueueRedraw());
        // Must stay the last child: unhandled input visits later siblings first,
        // so the brush sees events (and consumes LMB/wheel) before the camera.
        AddChild(router);
    }
}
