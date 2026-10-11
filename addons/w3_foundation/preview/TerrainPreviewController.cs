using System;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Rendering;

namespace W3Foundation.Preview;

/// <summary>
/// Foundation-level preview diagnostics. Commands and hotkeys remain available
/// after the diablolike test template is removed.
/// </summary>
[GlobalClass]
public partial class TerrainPreviewController : Node
{
    [Export] public NodePath TerrainPath { get; set; } = new();
    [Export] public NodePath CameraPath { get; set; } = new();
    [Export] public NodePath VisibilityBudgetPath { get; set; } = new();

    private TerrainWorld? _terrain;
    private Camera3D? _camera;
    private PreviewVisibilityBudget? _budget;
    private TerrainDebugOverlay? _overlay;
    private CanvasLayer? _canvas;
    private Label? _status;
    private Label? _help;
    private LineEdit? _console;
    private bool _debugEnabled = true;
    private bool _waterVisible = true;
    private bool _flatLighting;
    private bool _materialIds;

    public override void _Ready()
    {
        _terrain = TerrainPath.IsEmpty ? null : GetNodeOrNull<TerrainWorld>(TerrainPath);
        _camera = CameraPath.IsEmpty ? GetViewport().GetCamera3D() : GetNodeOrNull<Camera3D>(CameraPath);
        _budget = VisibilityBudgetPath.IsEmpty ? null : GetNodeOrNull<PreviewVisibilityBudget>(VisibilityBudgetPath);
        if (_terrain == null)
        {
            GD.PushWarning("TerrainPreviewController requires TerrainPath.");
            return;
        }

        _overlay = new TerrainDebugOverlay { Name = "_FoundationTerrainDiagnostics" };
        _terrain.AddChild(_overlay);
        _overlay.Setup(_terrain);
        BuildUi();
    }

    public override void _Process(double delta)
    {
        if (_status == null || _terrain?.Document == null || _camera == null || _overlay == null)
            return;
        Vector2I tile = _overlay.HoveredTile;
        string tileInfo = "outside map";
        if (tile.X >= 0 && tile.Y >= 0)
        {
            TerrainDocument document = _terrain.Document;
            int index = document.CornerIndex(tile.X, tile.Y);
            tileInfo = $"tile {tile.X},{tile.Y}  h={document.GetWorldHeight(tile.X, tile.Y):0.0}  mat={document.GroundMaterials[index]}  cliff={document.CliffLevels[index]}  flags=0x{document.CornerFlags[index]:X2}";
        }
        string budget = _budget == null
            ? "preview objects: not connected"
            : $"preview objects: anim {_budget.ActiveAnimationCount}/{_budget.VisibleAnimatedCount}  static {_budget.VisibleStaticCount}";
        _status.Text = $"W3 FOUNDATION PREVIEW\n{tileInfo}\nlabels {_overlay.ActiveLabelCount} (range < {_overlay.LabelNearDistance:0})  {budget}";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        if (key.Unicode == '`')
        {
            ToggleConsole();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_console?.Visible == true)
            return;

        switch (key.PhysicalKeycode)
        {
            case Key.G:
                if (_overlay != null) _overlay.SetGridEnabled(!_overlay.GridEnabled);
                break;
            case Key.B:
                if (_overlay != null) _overlay.SetChunkBoundsEnabled(!_overlay.ChunkBoundsEnabled);
                break;
            case Key.Tab:
                _flatLighting = !_flatLighting;
                _terrain?.SetDebugFlatLighting(_flatLighting);
                break;
            case Key.H:
                _materialIds = !_materialIds;
                _terrain?.SetDebugMaterialIds(_materialIds);
                break;
            case Key.F:
                _waterVisible = !_waterVisible;
                _terrain?.SetWaterVisible(_waterVisible);
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    public void ExecuteCommand(string command)
    {
        string value = command.Trim();
        if (value.Length == 0)
            return;
        switch (value.ToLowerInvariant())
        {
            case "/debug":
                SetDebugEnabled(!_debugEnabled);
                return;
            case "/debugon":
                SetDebugEnabled(true);
                return;
            case "/debugoff":
                SetDebugEnabled(false);
                return;
            case "/grid":
                if (_overlay != null) _overlay.SetGridEnabled(!_overlay.GridEnabled);
                return;
            case "/chunks":
                if (_overlay != null) _overlay.SetChunkBoundsEnabled(!_overlay.ChunkBoundsEnabled);
                return;
            case "/help":
                if (_help != null) _help.Visible = !_help.Visible;
                return;
        }

        string[] coordinates = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (coordinates.Length == 2 && int.TryParse(coordinates[0], out int x) && int.TryParse(coordinates[1], out int y))
            FocusTile(new Vector2I(x, y));
    }

    private void FocusTile(Vector2I tile)
    {
        if (_terrain?.Document == null || _camera == null)
            return;
        TerrainDocument document = _terrain.Document;
        tile.X = Math.Clamp(tile.X, 0, document.TileWidth - 1);
        tile.Y = Math.Clamp(tile.Y, 0, document.TileHeight - 1);
        Vector2 localXz = new((tile.X + 0.5f) * document.TileSize, (tile.Y + 0.5f) * document.TileSize);
        Vector3 world = _terrain.ToGlobal(new Vector3(localXz.X, _terrain.SampleHeight(localXz), localXz.Y));
        if (_camera is TerrainPreviewCamera previewCamera)
            previewCamera.FocusPoint(world);
        else
        {
            _camera.GlobalPosition = world + new Vector3(0.0f, 1400.0f, 1800.0f);
            _camera.LookAt(world, Vector3.Up);
        }
    }

    private void SetDebugEnabled(bool enabled)
    {
        _debugEnabled = enabled;
        if (_overlay != null) _overlay.Visible = enabled;
        if (_status != null) _status.Visible = enabled;
        if (_help != null) _help.Visible = enabled;
    }

    private void ToggleConsole()
    {
        if (_console == null)
            return;
        _console.Visible = !_console.Visible;
        if (_console.Visible)
            _console.GrabFocus();
        else
            _console.ReleaseFocus();
    }

    private void BuildUi()
    {
        _canvas = new CanvasLayer { Name = "FoundationPreviewUi", Layer = 100 };
        AddChild(_canvas);

        _status = new Label
        {
            Position = new Vector2(14.0f, 14.0f),
            Text = "W3 FOUNDATION PREVIEW",
        };
        _canvas.AddChild(_status);

        _help = new Label
        {
            Position = new Vector2(14.0f, 94.0f),
            Text = "` console   G grid   B chunks   Tab flat light   H material IDs   F water\nWASD move   Q/E down/up   RMB look   Shift accelerate   command: x,y /debug /grid /chunks",
            Modulate = new Color(0.78f, 0.84f, 0.9f),
        };
        _canvas.AddChild(_help);

        _console = new LineEdit
        {
            Position = new Vector2(14.0f, 154.0f),
            Size = new Vector2(520.0f, 34.0f),
            PlaceholderText = "/debug, /grid, /chunks, /help or tile x,y",
            Visible = false,
        };
        _console.TextSubmitted += text =>
        {
            ExecuteCommand(text);
            _console.Clear();
            ToggleConsole();
        };
        _canvas.AddChild(_console);
    }
}
