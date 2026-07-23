using System;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Editing;
using W3Foundation.Terrain.Rendering;
using W3Foundation.Validation.HiveWe;

namespace W3Foundation.Editor;

/// <summary>
/// Native Godot 3D viewport input/overlay for the temporary HiveWE validation port.
/// It owns no preview world: the scene TerrainWorld is the only rendered terrain.
/// </summary>
[Tool]
public sealed class TerrainNative3DBrush
{
    private readonly HiveWeTerrainBrushPort _hiveWe = new();
    private readonly Func<TerrainBrush> _brushProvider;
    private readonly Func<bool> _enabledProvider;
    private readonly Func<Color> _colorProvider;
    private readonly Action<TerrainDocument, TerrainDocument> _commit;
    private TerrainWorld? _terrain;
    private TerrainDocument? _before;
    private Camera3D? _camera;
    private Vector3 _localPoint;
    private bool _pointerOnTerrain;
    private bool _painting;
    private bool _cursorHidden;
    private Rect2I _dirty;
    private ulong _lastPaintMsec;
    private Vector2 _lastScreenPosition;
    private bool _hasScreenPosition;

    public TerrainNative3DBrush(
        Func<TerrainBrush> brushProvider,
        Func<bool> enabledProvider,
        Func<Color> colorProvider,
        Action<TerrainDocument, TerrainDocument> commit)
    {
        _brushProvider = brushProvider;
        _enabledProvider = enabledProvider;
        _colorProvider = colorProvider;
        _commit = commit;
    }

    public void SetTerrain(TerrainWorld? terrain)
    {
        if (!ReferenceEquals(_terrain, terrain))
            EndStroke();
        _terrain = terrain;
        _pointerOnTerrain = false;
    }

    public void Deactivate()
    {
        EndStroke();
        _pointerOnTerrain = false;
        RestoreCursor();
    }

    /// <summary>The pointer moved onto UI that consumes mouse events (e.g. the
    /// palette panel), so no further motion events will reach Handle(). Any
    /// stroke in progress is committed, exactly like a left-button release.</summary>
    public void NotifyPointerExited()
    {
        EndStroke();
        _pointerOnTerrain = false;
        RestoreCursor();
    }

    public bool Handle(Camera3D camera, InputEvent @event, Action updateOverlays, Action undo)
    {
        _camera = camera;
        if (_terrain?.Document == null || !_enabledProvider())
        {
            Deactivate();
            return false;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.CtrlPressed && key.Keycode == Key.Z)
        {
            if (_painting) EndStroke();
            undo();
            return true;
        }

        Vector2? position = @event switch
        {
            InputEventMouseMotion motion => motion.Position,
            InputEventMouseButton button => button.Position,
            _ => null,
        };
        if (position == null)
            return false;
        _lastScreenPosition = position.Value;
        _hasScreenPosition = true;

        // RMB/MMB stay native. Wheel is handled here so it has no editor-distance cap.
        if (@event is InputEventMouseButton wheel && wheel.Pressed &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            DollyCamera(wheel.ButtonIndex == MouseButton.WheelUp ? 1.0f : -1.0f);
            UpdatePointer(position.Value);
            updateOverlays();
            return true;
        }
        if (@event is InputEventMouseButton navigation &&
            navigation.ButtonIndex is MouseButton.Right or MouseButton.Middle)
            return false;
        if (@event is InputEventMouseMotion navigationMotion &&
            (navigationMotion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0)
            return false;

        if (@event is InputEventMouseButton release && release.ButtonIndex == MouseButton.Left && !release.Pressed)
        {
            EndStroke();
            updateOverlays();
            return true;
        }

        UpdatePointer(position.Value);
        updateOverlays();

        if (@event is InputEventMouseButton press && press.ButtonIndex == MouseButton.Left && press.Pressed)
        {
            // Consume even outside the terrain while brush mode is active: no Godot box-select.
            if (_pointerOnTerrain)
            {
                BeginStroke();
                Apply(0.5); // HiveWE Brush::mouse_press_event.
            }
            return true;
        }

        if (@event is InputEventMouseMotion drag && _painting && (drag.ButtonMask & MouseButtonMask.Left) != 0)
        {
            if (_pointerOnTerrain)
            {
                ulong now = Time.GetTicksMsec();
                double delta = Math.Clamp((now - _lastPaintMsec) / 1000.0, 1.0 / 240.0, 0.1);
                Apply(delta); // HiveWE Brush::mouse_move_event.
            }
            return true;
        }

        return false;
    }

    /// <summary>WASD/QE camera movement independent of native viewport keyboard focus.</summary>
    public void UpdateNavigation(double delta, Action updateOverlays)
    {
        if (_camera == null || !_hasScreenPosition || Input.IsMouseButtonPressed(MouseButton.Right))
            return;
        Vector3 input = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) input -= _camera.GlobalTransform.Basis.Z;
        if (Input.IsPhysicalKeyPressed(Key.S)) input += _camera.GlobalTransform.Basis.Z;
        if (Input.IsPhysicalKeyPressed(Key.A)) input -= _camera.GlobalTransform.Basis.X;
        if (Input.IsPhysicalKeyPressed(Key.D)) input += _camera.GlobalTransform.Basis.X;
        if (Input.IsPhysicalKeyPressed(Key.Q) || Input.IsPhysicalKeyPressed(Key.Ctrl)) input -= Vector3.Up;
        if (Input.IsPhysicalKeyPressed(Key.E) || Input.IsPhysicalKeyPressed(Key.Space)) input += Vector3.Up;
        if (input.LengthSquared() <= 0.0f)
            return;
        float speed = Input.IsPhysicalKeyPressed(Key.Shift) ? 5400.0f : 1800.0f;
        _camera.GlobalPosition += input.Normalized() * speed * (float)delta;
        _camera.Far = Math.Max(_camera.Far, 100000.0f);
        UpdatePointer(_lastScreenPosition);
        updateOverlays();
    }

    public void Draw(Control viewport)
    {
        if (_terrain == null || _camera == null || !_pointerOnTerrain || !_enabledProvider())
            return;
        TerrainDocument document = _terrain.Document!;
        TerrainBrush brush = _brushProvider();
        Vector3 world = _terrain.ToGlobal(_localPoint);
        Vector3 edgeWorld = _terrain.ToGlobal(_localPoint + Vector3.Right * document.TileSize * Math.Max(0.5f, brush.Radius));
        if (_camera.IsPositionBehind(world) || _camera.IsPositionBehind(edgeWorld))
            return;
        Vector2 center = _camera.UnprojectPosition(world);
        float radius = Math.Max(6.0f, center.DistanceTo(_camera.UnprojectPosition(edgeWorld)));
        Color color = _colorProvider();
        DrawHoveredCell(viewport, color.Lightened(0.35f));
        viewport.DrawArc(center, radius, 0.0f, Mathf.Tau, 64, color, 2.0f, true);
        viewport.DrawLine(center + Vector2.Left * 8.0f, center + Vector2.Right * 8.0f, color, 2.0f, true);
        viewport.DrawLine(center + Vector2.Up * 8.0f, center + Vector2.Down * 8.0f, color, 2.0f, true);
    }

    private void DrawHoveredCell(Control viewport, Color color)
    {
        TerrainDocument document = _terrain!.Document!;
        int x = Math.Clamp(Mathf.FloorToInt(_localPoint.X / document.TileSize), 0, document.TileWidth - 1);
        int y = Math.Clamp(Mathf.FloorToInt(_localPoint.Z / document.TileSize), 0, document.TileHeight - 1);
        Vector3[] localCorners =
        {
            new(x * document.TileSize, document.GetWorldHeight(x, y), y * document.TileSize),
            new((x + 1) * document.TileSize, document.GetWorldHeight(x + 1, y), y * document.TileSize),
            new((x + 1) * document.TileSize, document.GetWorldHeight(x + 1, y + 1), (y + 1) * document.TileSize),
            new(x * document.TileSize, document.GetWorldHeight(x, y + 1), (y + 1) * document.TileSize),
        };
        for (int i = 0; i < localCorners.Length; i++)
        {
            Vector3 from = _terrain.ToGlobal(localCorners[i]);
            Vector3 to = _terrain.ToGlobal(localCorners[(i + 1) % localCorners.Length]);
            if (_camera!.IsPositionBehind(from) || _camera.IsPositionBehind(to))
                continue;
            viewport.DrawLine(_camera.UnprojectPosition(from), _camera.UnprojectPosition(to), color, 3.0f, true);
        }
    }

    private void UpdatePointer(Vector2 screenPosition)
    {
        _pointerOnTerrain = TryProjectToTerrain(screenPosition, out _localPoint);
        if (_pointerOnTerrain) HideCursor(); else RestoreCursor();
    }

    private void BeginStroke()
    {
        if (_painting || _terrain?.Document == null)
            return;
        _hiveWe.ApplyBegin(_terrain.Document, _terrain.LocalPointToCorner(_localPoint), _brushProvider());
        _before = _hiveWe.Before;
        _dirty = new Rect2I();
        _painting = _before != null;
    }

    private void Apply(double delta)
    {
        if (!_painting || _terrain?.Document == null)
            return;
        Rect2I dirty = _hiveWe.Apply(_terrain.Document, _terrain.LocalPointToCorner(_localPoint), _brushProvider(), delta);
        _dirty = _dirty.Area == 0 ? dirty : _dirty.Merge(dirty);
        _lastPaintMsec = Time.GetTicksMsec();
    }

    private void EndStroke()
    {
        if (!_painting)
            return;
        _painting = false;
        _hiveWe.ApplyEnd();
        if (_terrain?.Document != null && _before != null && _dirty.Area > 0)
            _commit(_before, _terrain.Document.CloneDocument());
        _before = null;
    }

    private bool TryProjectToTerrain(Vector2 screenPosition, out Vector3 localPoint)
    {
        localPoint = Vector3.Zero;
        if (_terrain == null || _camera == null)
            return false;
        Vector3 worldOrigin = _camera.ProjectRayOrigin(screenPosition);
        Vector3 worldDirection = _camera.ProjectRayNormal(screenPosition);
        Vector3 localOrigin = _terrain.ToLocal(worldOrigin);
        Vector3 localDirection = (_terrain.ToLocal(worldOrigin + worldDirection) - localOrigin).Normalized();
        if (Math.Abs(localDirection.Y) < 0.00001f)
            return false;
        float distance = -localOrigin.Y / localDirection.Y;
        if (distance < 0.0f)
            return false;
        for (int i = 0; i < 6; i++)
        {
            Vector3 point = localOrigin + localDirection * distance;
            distance = (_terrain.SampleHeight(new Vector2(point.X, point.Z)) - localOrigin.Y) / localDirection.Y;
        }
        if (distance < 0.0f)
            return false;
        localPoint = localOrigin + localDirection * distance;
        return _terrain.ContainsLocalPoint(new Vector2(localPoint.X, localPoint.Z));
    }

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

    private void DollyCamera(float direction)
    {
        if (_camera == null || _terrain?.Document == null)
            return;
        // Historical terrain-editor wheel direction: wheel up moves back, wheel down forward.
        float step = Math.Max(5400.0f, _terrain.Document.TileSize * 32.0f);
        _camera.GlobalPosition += _camera.GlobalTransform.Basis.Z * direction * step;
        _camera.Far = Math.Max(_camera.Far, 100000.0f);
    }
}
