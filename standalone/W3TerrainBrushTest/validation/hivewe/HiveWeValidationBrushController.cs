using System;
using System.Collections.Generic;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Editing;
using W3Foundation.Terrain.Rendering;

namespace W3TerrainBrushTest.Validation.HiveWe;

/// <summary>
/// Temporary flow-validation controller translated from HiveWE's Brush and
/// TerrainBrush state machines. It deliberately lives outside the production
/// add-on: the validation canvas owns placement input, so Godot's 3D selection
/// tool can never create a selection rectangle during a brush stroke.
/// </summary>
public partial class HiveWeValidationBrushController : Node
{
    [Export] public NodePath TerrainPath { get; set; } = new();
    [Export] public NodePath CameraPath { get; set; } = new();

    private TerrainWorld? _terrain;
    private Camera3D? _camera;
    private TerrainDocument? _strokeBefore;
    private readonly Stack<TerrainDocument> _undo = new();
    private Rect2I _updatedArea;
    private bool _painting;
    private bool _brushEnabled = true;
    private bool _cursorHidden;
    private Vector2 _mouseCorner;
    private bool _mouseOnTerrain;
    private float _frameDelta = 1.0f / 60.0f;
    private int _brushSize = 5;
    private int _materialIndex = 4;
    private TerrainBrushOperation _operation = TerrainBrushOperation.PaintTexture;
    private bool _circle = true;

    private PanelContainer? _panel;
    private Button? _brushToggle;
    private OptionButton? _operationPicker;
    private OptionButton? _materialPicker;
    private SpinBox? _sizePicker;
    private OptionButton? _shapePicker;
    private Label? _status;

    public override void _Ready()
    {
        CallDeferred(nameof(FinishReady));
    }

    private void FinishReady()
    {
        _terrain = TerrainPath.IsEmpty ? GetParentOrNull<TerrainWorld>() : GetNodeOrNull<TerrainWorld>(TerrainPath);
        _camera = CameraPath.IsEmpty ? GetViewport().GetCamera3D() : GetNodeOrNull<Camera3D>(CameraPath);
        if (_terrain?.Document == null || _camera == null)
        {
            GD.PushError("HiveWE validation controller requires TerrainWorld and Camera3D.");
            return;
        }

        FrameTerrain();
        BuildUi();
        SetProcessInput(true);
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        RestoreCursor();
    }

    public override void _Process(double delta)
    {
        _frameDelta = Math.Clamp((float)delta, 1.0f / 240.0f, 0.1f);
        if (_status == null || _terrain?.Document == null)
            return;

        Vector2 snapped = GetSnappedPosition(_mouseCorner);
        _status.Text = _mouseOnTerrain
            ? $"corner {snapped.X:0.0}, {snapped.Y:0.0}   dirty {_updatedArea}"
            : "Move the brush onto the terrain";
    }

    public override void _Input(InputEvent @event)
    {
        if (_terrain?.Document == null || _camera == null)
            return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Escape)
            {
                SetBrushEnabled(false);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (key.CtrlPressed && key.Keycode == Key.Z)
            {
                UndoLastStroke();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (key.Keycode is Key.Equal or Key.Plus)
            {
                SetBrushSize(_brushSize + 2);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (key.Keycode == Key.Minus)
            {
                SetBrushSize(_brushSize - 2);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        Vector2? pointer = null;
        if (@event is InputEventMouseMotion pointerMotion)
            pointer = pointerMotion.Position;
        else if (@event is InputEventMouseButton pointerButton)
            pointer = pointerButton.Position;
        if (pointer == null)
            return;

        bool overUi = _panel != null && _panel.GetGlobalRect().HasPoint(pointer.Value);
        if (@event is InputEventMouseButton release && release.ButtonIndex == MouseButton.Left &&
            !release.Pressed && _painting)
        {
            ApplyEnd();
            if (overUi)
            {
                _mouseOnTerrain = false;
                _terrain.HideBrushPreview();
                RestoreCursor();
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (overUi)
        {
            if (!_painting)
            {
                _mouseOnTerrain = false;
                _terrain.HideBrushPreview();
                RestoreCursor();
            }
            return;
        }

        if (@event is InputEventMouseButton wheel && wheel.Pressed && wheel.ShiftPressed &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            SetBrushSize(_brushSize + (wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1));
            GetViewport().SetInputAsHandled();
            return;
        }

        // RMB/MMB and ordinary wheel events are intentionally left to the camera.
        if (@event is InputEventMouseButton navigation &&
            navigation.ButtonIndex is MouseButton.Right or MouseButton.Middle or MouseButton.WheelUp or MouseButton.WheelDown)
            return;
        if (@event is InputEventMouseMotion navigationMotion &&
            (navigationMotion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0)
            return;

        UpdatePointer(pointer.Value);

        if (@event is InputEventMouseButton left && left.ButtonIndex == MouseButton.Left)
        {
            if (left.Pressed && _brushEnabled && _mouseOnTerrain)
            {
                ApplyBegin();
                Apply(0.5f); // HiveWE applies once immediately with frame_delta=0.5.
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion motion && _painting &&
            (motion.ButtonMask & MouseButtonMask.Left) != 0)
        {
            Apply(_frameDelta); // HiveWE applies on every LMB mouse-move event.
            GetViewport().SetInputAsHandled();
        }
    }

    private void FrameTerrain()
    {
        TerrainDocument document = _terrain!.Document!;
        Vector3 localCenter = new(
            document.TileWidth * document.TileSize * 0.5f,
            0.0f,
            document.TileHeight * document.TileSize * 0.5f);
        localCenter.Y = _terrain.SampleHeight(new Vector2(localCenter.X, localCenter.Z));
        Vector3 center = _terrain.ToGlobal(localCenter);

        _camera!.Projection = Camera3D.ProjectionType.Perspective;
        _camera.Fov = 52.0f;
        _camera.GlobalPosition = center + new Vector3(0.0f, 2350.0f, 2050.0f);
        _camera.LookAt(center, Vector3.Up);
    }

    private void UpdatePointer(Vector2 screenPosition)
    {
        _mouseOnTerrain = TryProjectToTerrain(screenPosition, out Vector3 localPoint);
        if (!_brushEnabled || !_mouseOnTerrain)
        {
            if (!_painting)
            {
                _terrain!.HideBrushPreview();
                RestoreCursor();
            }
            return;
        }

        _mouseCorner = _terrain!.LocalPointToCorner(localPoint);
        Vector2 snapped = GetSnappedPosition(_mouseCorner);
        Vector2 localXz = snapped * _terrain.Document!.TileSize;
        var previewPoint = new Vector3(localXz.X, _terrain.SampleHeight(localXz), localXz.Y);
        _terrain.SetBrushPreview(previewPoint, _brushSize * 0.5f, PreviewColor());
        HideCursor();
    }

    // Direct translation of HiveWE Brush::get_position() for a corner brush.
    private Vector2 GetSnappedPosition(Vector2 mouseWorld)
    {
        bool even = _brushSize % 2 == 0;
        Vector2 cornerEven = mouseWorld.Floor() + new Vector2(0.5f, 0.5f);
        Vector2 cornerOdd = (mouseWorld + new Vector2(0.5f, 0.5f)).Floor();
        return even ? cornerEven : cornerOdd;
    }

    // Direct translation of HiveWE TerrainBrush::get_unclipped_pos().
    private Vector2I GetUnclippedPosition()
    {
        Vector2 topLeft = _mouseCorner - new Vector2(_brushSize * 0.5f, _brushSize * 0.5f) + Vector2.One;
        return new Vector2I(Mathf.FloorToInt(topLeft.X), Mathf.FloorToInt(topLeft.Y));
    }

    private void ApplyBegin()
    {
        if (_painting || _terrain?.Document == null)
            return;
        _strokeBefore = _terrain.Document.CloneDocument(); // HiveWE snapshots all corners/pathing here.
        _updatedArea = new Rect2I();
        _painting = true;
    }

    private void Apply(float frameDelta)
    {
        if (!_painting || _terrain?.Document == null || _strokeBefore == null)
            return;

        TerrainDocument document = _terrain.Document;
        Vector2I raw = GetUnclippedPosition();
        int minX = Math.Clamp(raw.X, 0, document.CornerWidth - 1);
        int minY = Math.Clamp(raw.Y, 0, document.CornerHeight - 1);
        int maxX = Math.Clamp(raw.X + _brushSize - 1, 0, document.CornerWidth - 1);
        int maxY = Math.Clamp(raw.Y + _brushSize - 1, 0, document.CornerHeight - 1);
        if (maxX < minX || maxY < minY)
            return;

        Rect2I area = new(minX, minY, maxX - minX + 1, maxY - minY + 1);
        float[] sourceHeights = _operation == TerrainBrushOperation.HeightSmooth
            ? (float[])document.GroundHeights.Clone()
            : document.GroundHeights;
        int half = _brushSize / 2;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int bx = x - raw.X;
                int by = y - raw.Y;
                if (_circle)
                {
                    int dx = bx - half;
                    int dy = by - half;
                    if (dx * dx + dy * dy > half * half)
                        continue;
                }
                ApplyCorner(document, sourceHeights, x, y, frameDelta);
            }
        }

        _updatedArea = _updatedArea.Area == 0 ? area : _updatedArea.Merge(area);
        bool geometryChanged = _operation != TerrainBrushOperation.PaintTexture;
        document.NotifyEdited(area, geometryChanged);
    }

    private void ApplyCorner(TerrainDocument document, float[] sourceHeights, int x, int y, float frameDelta)
    {
        int index = document.CornerIndex(x, y);
        int baseline = _strokeBefore!.CornerIndex(x, y);
        switch (_operation)
        {
            case TerrainBrushOperation.PaintTexture:
                document.GroundMaterials[index] = (byte)_materialIndex;
                break;
            case TerrainBrushOperation.HeightRaise:
                document.GroundHeights[index] += 96.0f * frameDelta;
                break;
            case TerrainBrushOperation.HeightLower:
                document.GroundHeights[index] -= 96.0f * frameDelta;
                break;
            case TerrainBrushOperation.HeightSmooth:
                document.GroundHeights[index] = Mathf.Lerp(sourceHeights[index], NeighborAverage(document, sourceHeights, x, y), 0.22f);
                break;
            case TerrainBrushOperation.CliffRaiseOne:
                document.CliffLevels[index] = (byte)Math.Clamp(_strokeBefore.CliffLevels[baseline] + 1, 0, 15);
                document.CornerFlags[index] &= unchecked((byte)~TerrainDocument.FlagRamp);
                break;
            case TerrainBrushOperation.CliffLowerOne:
                document.CliffLevels[index] = (byte)Math.Clamp(_strokeBefore.CliffLevels[baseline] - 1, 0, 15);
                document.CornerFlags[index] &= unchecked((byte)~TerrainDocument.FlagRamp);
                break;
        }
    }

    private static float NeighborAverage(TerrainDocument document, float[] source, int x, int y)
    {
        float total = 0.0f;
        int count = 0;
        for (int oy = -1; oy <= 1; oy++)
        for (int ox = -1; ox <= 1; ox++)
        {
            total += source[document.CornerIndex(x + ox, y + oy)];
            count++;
        }
        return total / count;
    }

    private void ApplyEnd()
    {
        if (!_painting)
            return;
        _painting = false;
        if (_strokeBefore != null && _updatedArea.Area > 0)
            _undo.Push(_strokeBefore);
        _strokeBefore = null;
    }

    private void UndoLastStroke()
    {
        if (_painting)
            ApplyEnd();
        if (_terrain == null || _undo.Count == 0)
            return;
        _terrain.Document = _undo.Pop();
        _terrain.QueueFullRebuild();
    }

    private bool TryProjectToTerrain(Vector2 screenPosition, out Vector3 localPoint)
    {
        localPoint = Vector3.Zero;
        Vector3 worldOrigin = _camera!.ProjectRayOrigin(screenPosition);
        Vector3 worldDirection = _camera.ProjectRayNormal(screenPosition);
        Vector3 localOrigin = _terrain!.ToLocal(worldOrigin);
        Vector3 localDirection = (_terrain.ToLocal(worldOrigin + worldDirection) - localOrigin).Normalized();
        if (Math.Abs(localDirection.Y) < 0.00001f)
            return false;

        float distance = -localOrigin.Y / localDirection.Y;
        if (distance < 0.0f)
            return false;
        for (int i = 0; i < 6; i++)
        {
            Vector3 point = localOrigin + localDirection * distance;
            float height = _terrain.SampleHeight(new Vector2(point.X, point.Z));
            distance = (height - localOrigin.Y) / localDirection.Y;
        }
        if (distance < 0.0f)
            return false;
        localPoint = localOrigin + localDirection * distance;
        return _terrain.ContainsLocalPoint(new Vector2(localPoint.X, localPoint.Z));
    }

    private void SetBrushEnabled(bool enabled)
    {
        _brushEnabled = enabled;
        if (_brushToggle != null)
            _brushToggle.Text = enabled ? "Brush ON  (Esc off)" : "Brush OFF  (click to enable)";
        if (!enabled)
        {
            if (_painting) ApplyEnd();
            _terrain?.HideBrushPreview();
            RestoreCursor();
        }
    }

    private void SetBrushSize(int value)
    {
        _brushSize = Math.Clamp(value, 1, 24);
        if (_sizePicker != null && Math.Abs(_sizePicker.Value - _brushSize) > 0.01)
            _sizePicker.Value = _brushSize;
        if (_mouseOnTerrain && _camera != null)
            UpdatePointer(GetViewport().GetMousePosition());
    }

    private Color PreviewColor() => _operation switch
    {
        TerrainBrushOperation.PaintTexture => new Color(0.25f, 1.0f, 0.3f),
        TerrainBrushOperation.HeightLower or TerrainBrushOperation.CliffLowerOne => new Color(0.25f, 0.65f, 1.0f),
        _ => new Color(1.0f, 0.65f, 0.15f),
    };

    private void HideCursor()
    {
        if (_cursorHidden) return;
        Input.MouseMode = Input.MouseModeEnum.Hidden;
        _cursorHidden = true;
    }

    private void RestoreCursor()
    {
        if (!_cursorHidden) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _cursorHidden = false;
    }

    private void BuildUi()
    {
        var canvas = new CanvasLayer { Name = "HiveWeValidationUi", Layer = 200 };
        AddChild(canvas);
        _panel = new PanelContainer
        {
            Position = new Vector2(18.0f, 180.0f),
            CustomMinimumSize = new Vector2(320.0f, 360.0f),
        };
        canvas.AddChild(_panel);
        var column = new VBoxContainer();
        _panel.AddChild(column);
        column.AddChild(new Label { Text = "HiveWE brush flow validation", ThemeTypeVariation = "HeaderMedium" });
        column.AddChild(new Label
        {
            Text = "Independent placement canvas (no Godot box-select)\nLMB paint  ·  RMB look  ·  wheel zoom  ·  Shift+wheel size\nCtrl+Z undo  ·  +/- size",
        });

        _brushToggle = new Button { Text = "Brush ON  (Esc off)" };
        _brushToggle.Pressed += () => SetBrushEnabled(!_brushEnabled);
        column.AddChild(_brushToggle);

        _operationPicker = new OptionButton();
        AddOperation("Paint texture", TerrainBrushOperation.PaintTexture);
        AddOperation("Raise height", TerrainBrushOperation.HeightRaise);
        AddOperation("Lower height", TerrainBrushOperation.HeightLower);
        AddOperation("Smooth height", TerrainBrushOperation.HeightSmooth);
        AddOperation("Raise cliff 1", TerrainBrushOperation.CliffRaiseOne);
        AddOperation("Lower cliff 1", TerrainBrushOperation.CliffLowerOne);
        _operationPicker.ItemSelected += index => _operation = (TerrainBrushOperation)(int)_operationPicker.GetItemMetadata((int)index);
        column.AddChild(_operationPicker);

        _materialPicker = new OptionButton();
        for (int i = 0; i < _terrain!.Document!.Palette.Count; i++)
        {
            TerrainPaletteEntry entry = _terrain.Document.Palette[i];
            _materialPicker.AddItem($"Material {i}: {entry.Id}");
        }
        _materialPicker.Selected = Math.Clamp(_materialIndex, 0, _materialPicker.ItemCount - 1);
        _materialPicker.ItemSelected += index => _materialIndex = (int)index;
        column.AddChild(_materialPicker);

        var sizeRow = new HBoxContainer();
        sizeRow.AddChild(new Label { Text = "Brush size" });
        _sizePicker = new SpinBox { MinValue = 1, MaxValue = 24, Step = 1, Value = _brushSize, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _sizePicker.ValueChanged += value => SetBrushSize((int)value);
        sizeRow.AddChild(_sizePicker);
        column.AddChild(sizeRow);

        _shapePicker = new OptionButton();
        _shapePicker.AddItem("Circle");
        _shapePicker.AddItem("Square");
        _shapePicker.ItemSelected += index => _circle = index == 0;
        column.AddChild(_shapePicker);

        _status = new Label { Text = "Move the brush onto the terrain" };
        column.AddChild(_status);
    }

    private void AddOperation(string label, TerrainBrushOperation operation)
    {
        _operationPicker!.AddItem(label);
        _operationPicker.SetItemMetadata(_operationPicker.ItemCount - 1, (int)operation);
    }
}
