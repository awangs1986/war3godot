using System;
using Godot;
using W3Foundation.Preview;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Editing;
using W3Foundation.Terrain.Rendering;
using W3Foundation.Validation.HiveWe;

namespace W3Foundation.Editor;

/// <summary>
/// Project-owned editor-canvas input controller. It runs inside the W3 Terrain
/// SubViewport, so no input is forwarded through Godot's native 3D selection tool.
/// </summary>
[Tool]
public partial class TerrainMainScreenBrush : Node
{
    private TerrainWorld? _terrain;
    private TerrainPreviewCamera? _camera;
    private TerrainDocument? _strokeBefore;
    private readonly HiveWeTerrainBrushPort _hiveWeBrush = new();
    private bool _painting;
    private bool _mouseOnTerrain;
    private bool _cursorHidden;
    private Vector2 _mouseCorner;
    private Rect2I _dirtyArea;

    public Func<TerrainBrush>? BrushProvider { get; set; }
    public Func<bool>? BrushEnabledProvider { get; set; }
    public Func<Color>? PreviewColorProvider { get; set; }

    public event Action<TerrainDocument, TerrainDocument>? StrokeCommitted;
    public event Action<float>? RadiusDeltaRequested;
    public event Action<bool>? BrushEnabledRequested;
    public event Action? UndoRequested;
    public event Action<string>? StatusChanged;

    public void Setup(TerrainWorld terrain, TerrainPreviewCamera camera)
    {
        _terrain = terrain;
        _camera = camera;
        SetProcessInput(true);
    }

    public void PointerExited()
    {
        if (_painting)
            EndStroke();
        _mouseOnTerrain = false;
        _terrain?.HideBrushPreview();
        RestoreCursor();
        StatusChanged?.Invoke("鼠标移到地形上即可开始绘制");
    }

    public override void _ExitTree()
    {
        RestoreCursor();
    }

    public override void _Input(InputEvent @event)
    {
        if (_terrain?.Document == null || _camera == null || BrushProvider == null)
            return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Escape)
            {
                BrushEnabledRequested?.Invoke(false);
                PointerExited();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (key.CtrlPressed && key.Keycode == Key.Z)
            {
                if (_painting)
                    EndStroke();
                UndoRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (key.Keycode is Key.Equal or Key.Plus)
            {
                RadiusDeltaRequested?.Invoke(0.5f);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (key.Keycode == Key.Minus)
            {
                RadiusDeltaRequested?.Invoke(-0.5f);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        Vector2? pointer = null;
        if (@event is InputEventMouseMotion motion)
            pointer = motion.Position;
        else if (@event is InputEventMouseButton button)
            pointer = button.Position;
        if (pointer == null)
            return;

        if (@event is InputEventMouseButton release && release.ButtonIndex == MouseButton.Left &&
            !release.Pressed && _painting)
        {
            EndStroke();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton wheel && wheel.Pressed && wheel.ShiftPressed &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            RadiusDeltaRequested?.Invoke(wheel.ButtonIndex == MouseButton.WheelUp ? 0.5f : -0.5f);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton navigation &&
            navigation.ButtonIndex is MouseButton.Right or MouseButton.Middle or MouseButton.WheelUp or MouseButton.WheelDown)
            return;
        if (@event is InputEventMouseMotion navigationMotion &&
            (navigationMotion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0)
            return;

        UpdatePointer(pointer.Value);
        bool enabled = BrushEnabledProvider?.Invoke() ?? false;

        if (@event is InputEventMouseButton left && left.ButtonIndex == MouseButton.Left && left.Pressed)
        {
            if (enabled && _mouseOnTerrain)
            {
                BeginStroke();
                ApplyStroke();
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion drag && _painting &&
            (drag.ButtonMask & MouseButtonMask.Left) != 0)
        {
            ApplyStroke();
            GetViewport().SetInputAsHandled();
        }
    }

    private void UpdatePointer(Vector2 viewportPosition)
    {
        bool enabled = BrushEnabledProvider?.Invoke() ?? false;
        _mouseOnTerrain = TryProjectToTerrain(viewportPosition, out Vector3 localPoint);
        if (!enabled || !_mouseOnTerrain)
        {
            if (!_painting)
            {
                _terrain!.HideBrushPreview();
                RestoreCursor();
            }
            StatusChanged?.Invoke(enabled ? "鼠标位于地图外" : "笔刷已关闭；点击左侧按钮重新开启");
            return;
        }

        TerrainBrush brush = BrushProvider!.Invoke();
        _mouseCorner = _terrain!.LocalPointToCorner(localPoint);
        Vector2 snapped = _hiveWeBrush.GetPosition(_mouseCorner, brush);
        float tileSize = _terrain.Document!.TileSize;
        Vector2 localXz = snapped * tileSize;
        _terrain.SetBrushPreview(
            new Vector3(localXz.X, _terrain.SampleHeight(localXz), localXz.Y),
            brush.Radius,
            PreviewColorProvider?.Invoke() ?? Colors.Lime);
        HideCursor();
        StatusChanged?.Invoke($"角点 {snapped.X:0.0}, {snapped.Y:0.0}  ·  左键绘制  ·  Shift+滚轮调笔刷");
    }

    private void BeginStroke()
    {
        if (_painting || _terrain?.Document == null)
            return;
        TerrainBrush brush = BrushProvider!.Invoke();
        _hiveWeBrush.ApplyBegin(_terrain.Document, _mouseCorner, brush);
        _strokeBefore = _hiveWeBrush.Before;
        _dirtyArea = new Rect2I();
        _painting = true;
    }

    private void ApplyStroke()
    {
        if (!_painting || _terrain?.Document == null || _strokeBefore == null || BrushProvider == null)
            return;
        TerrainBrush brush = BrushProvider.Invoke();
        // HiveWE calls apply(0.5) on press and apply(frame_delta) on every drag.
        double delta = _dirtyArea.Size == Vector2I.Zero ? 0.5 : GetProcessDeltaTime();
        Rect2I dirty = _hiveWeBrush.Apply(_terrain.Document, _mouseCorner, brush, delta);
        _dirtyArea = _dirtyArea.Area == 0 ? dirty : _dirtyArea.Merge(dirty);
    }

    private void EndStroke()
    {
        if (!_painting)
            return;
        _painting = false;
        _hiveWeBrush.ApplyEnd();
        if (_terrain?.Document != null && _strokeBefore != null && _dirtyArea.Area > 0)
            StrokeCommitted?.Invoke(_strokeBefore, _terrain.Document.CloneDocument());
        _strokeBefore = null;
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

    private void HideCursor()
    {
        if (_cursorHidden)
            return;
        Input.MouseMode = Input.MouseModeEnum.Hidden;
        _cursorHidden = true;
    }

    private void RestoreCursor()
    {
        if (!_cursorHidden)
            return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _cursorHidden = false;
    }
}
