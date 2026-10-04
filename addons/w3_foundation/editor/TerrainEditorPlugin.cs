#if TOOLS
using System;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Rendering;

namespace W3Foundation.Editor;

[Tool]
public partial class TerrainEditorPlugin : EditorPlugin
{
    private TerrainPaletteDock? _dock;
    private EditorDock? _editorDock;
    private TerrainWorld? _active;
    private TerrainDocument? _strokeBefore;
    private bool _painting;
    private bool _cursorHidden;
    private Control? _viewportControl;
    private Vector2 _lastAppliedCorner = new(float.PositiveInfinity, float.PositiveInfinity);

    public override void _EnterTree()
    {
        _dock = new TerrainPaletteDock();
        _dock.CreateTerrainRequested += CreateTerrain;
        _dock.BrushModeChanged += OnBrushModeChanged;
        _editorDock = new EditorDock
        {
            Name = "W3FoundationTerrainDock",
            Title = "地形",
            LayoutKey = "w3_foundation_terrain",
            DefaultSlot = EditorDock.DockSlot.RightUl,
            IconName = "Node3D",
            Transient = true,
        };
        _editorDock.AddChild(_dock);
        AddDock(_editorDock);
        SetInputEventForwardingAlwaysEnabled();
        Callable.From(HookViewportControl).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (_dock != null)
        {
            _dock.CreateTerrainRequested -= CreateTerrain;
            _dock.BrushModeChanged -= OnBrushModeChanged;
        }
        if (_editorDock != null)
        {
            RemoveDock(_editorDock);
            _editorDock.QueueFree();
        }
        _active?.HideBrushPreview();
        UnhookViewportControl();
        RestoreSystemCursor();
        _active = null;
    }

    public override bool _Handles(GodotObject @object) => @object is TerrainWorld;

    public override void _Edit(GodotObject @object)
    {
        SetActive(@object as TerrainWorld);
    }

    public override void _MakeVisible(bool visible)
    {
        if (!visible)
        {
            if (_painting)
                EndStroke();
            _active?.HideBrushPreview();
            RestoreSystemCursor();
        }
        if (_dock != null)
            _dock.Visible = visible;
    }

    // Godot 4.7.1-rc2's generated C# signature is int even though the API enum is
    // EditorPlugin.AfterGuiInput. Keep casts here until the binding is corrected.
    public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
    {
        if (_active == null || _dock == null || _active.Document == null)
            return (int)AfterGuiInput.Pass;

        if (@event is InputEventMouseButton wheel && wheel.Pressed &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            wheel.Factor = Math.Max(1.0f, wheel.Factor) * 5.0f;
            return (int)AfterGuiInput.Pass;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo &&
            key.Keycode == Key.Escape && _dock.BrushEnabled)
        {
            _dock.SetBrushEnabled(false);
            return ConsumeInput(viewportCamera);
        }

        if (!_dock.BrushEnabled)
        {
            if (_painting)
                EndStroke();
            _active.HideBrushPreview();
            RestoreSystemCursor();
            return (int)AfterGuiInput.Pass;
        }

        bool navigatingViewport =
            @event is InputEventMouseButton navigationButton &&
                (navigationButton.ButtonIndex == MouseButton.Right || navigationButton.ButtonIndex == MouseButton.Middle) ||
            @event is InputEventMouseMotion navigationMotion &&
                (navigationMotion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0;
        if (navigatingViewport)
        {
            if (_painting)
                EndStroke();
            _active.HideBrushPreview();
            RestoreSystemCursor();
            return (int)AfterGuiInput.Pass;
        }

        bool leftInteraction =
            @event is InputEventMouseButton leftButton && leftButton.ButtonIndex == MouseButton.Left ||
            @event is InputEventMouseMotion leftMotion &&
                (leftMotion.ButtonMask & MouseButtonMask.Left) != 0;

        Vector2 mousePosition;
        bool altPressed;
        if (@event is InputEventMouseMotion motion)
        {
            mousePosition = motion.Position;
            altPressed = motion.AltPressed;
        }
        else if (@event is InputEventMouseButton button)
        {
            mousePosition = button.Position;
            altPressed = button.AltPressed;
        }
        else
        {
            return (int)AfterGuiInput.Pass;
        }

        if (@event is InputEventMouseButton release &&
            release.ButtonIndex == MouseButton.Left && !release.Pressed)
        {
            if (_painting)
                EndStroke();
            return ConsumeInput(viewportCamera);
        }

        if (altPressed || !TryProjectToTerrain(viewportCamera, mousePosition, out Vector3 localPoint))
        {
            _active.HideBrushPreview();
            RestoreSystemCursor();
            return leftInteraction ? ConsumeInput(viewportCamera) : (int)AfterGuiInput.Pass;
        }

        _active.SetBrushPreview(localPoint, _dock.CurrentBrush.Radius, _dock.PreviewColor);
        HideSystemCursor();

        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
                BeginStroke(localPoint);
            else if (_painting)
                EndStroke();
            return ConsumeInput(viewportCamera);
        }

        if (@event is InputEventMouseMotion)
        {
            if (_painting)
                ApplyStroke(localPoint);
            if (leftInteraction || _painting)
                return ConsumeInput(viewportCamera);
        }

        return (int)AfterGuiInput.Pass;
    }

    private void SetActive(TerrainWorld? terrain)
    {
        if (ReferenceEquals(_active, terrain))
            return;
        if (_painting)
            EndStroke();
        _active?.HideBrushPreview();
        RestoreSystemCursor();
        _active = terrain;
        _dock?.SetActiveTerrain(_active);
        if (_active != null)
        {
            _editorDock?.MakeVisible();
            Callable.From(FrameEditorCamera).CallDeferred();
        }
    }

    private void OnBrushModeChanged(bool enabled)
    {
        if (enabled)
        {
            if (_active != null)
            {
                EditorInterface.Singleton.GetSelection().Clear();
                EditorInterface.Singleton.GetSelection().AddNode(_active);
            }
            return;
        }

        if (_painting)
            EndStroke();
        _active?.HideBrushPreview();
        RestoreSystemCursor();
    }

    private void HideSystemCursor()
    {
        if (_cursorHidden)
            return;
        Input.MouseMode = Input.MouseModeEnum.Hidden;
        _cursorHidden = true;
    }

    private void HookViewportControl()
    {
        if (EditorInterface.Singleton.GetEditorViewport3D(0)?.GetParent() is not Control control ||
            ReferenceEquals(_viewportControl, control))
            return;
        UnhookViewportControl();
        _viewportControl = control;
        _viewportControl.MouseExited += RestoreSystemCursor;
    }

    private void UnhookViewportControl()
    {
        if (_viewportControl != null && IsInstanceValid(_viewportControl))
            _viewportControl.MouseExited -= RestoreSystemCursor;
        _viewportControl = null;
    }

    private void RestoreSystemCursor()
    {
        if (!_cursorHidden)
            return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _cursorHidden = false;
    }

    private static int ConsumeInput(Camera3D viewportCamera)
    {
        viewportCamera.GetViewport().SetInputAsHandled();
        return (int)AfterGuiInput.Stop;
    }

    private void FrameEditorCamera()
    {
        if (_active?.Document is not { IsInitialized: true } document || !IsInstanceValid(_active))
            return;

        Camera3D? camera = EditorInterface.Singleton.GetEditorViewport3D(0)?.GetCamera3D();
        if (camera == null)
            return;

        float width = document.TileWidth * document.TileSize;
        float depth = document.TileHeight * document.TileSize;
        Vector2 centerXZ = new(width * 0.5f, depth * 0.5f);
        Vector3 center = _active.ToGlobal(new Vector3(centerXZ.X, _active.SampleHeight(centerXZ), centerXZ.Y));
        float extent = Math.Max(width, depth);
        camera.GlobalPosition = center + new Vector3(extent * 0.2f, extent * 0.9f, extent * 0.35f);
        camera.LookAt(center, Vector3.Up);
    }

    private void BeginStroke(Vector3 localPoint)
    {
        if (_active?.Document == null || _dock == null)
            return;
        _strokeBefore = _active.Document.CloneDocument();
        _painting = true;
        _lastAppliedCorner = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        ApplyStroke(localPoint);
    }

    private void ApplyStroke(Vector3 localPoint)
    {
        if (_active == null || _dock == null)
            return;
        Vector2 corner = _active.LocalPointToCorner(localPoint);
        float minimumSpacing = Math.Max(0.3f, _dock.CurrentBrush.Radius * 0.18f);
        if (_lastAppliedCorner.DistanceTo(corner) < minimumSpacing)
            return;
        _active.ApplyBrush(corner, _dock.CurrentBrush, _strokeBefore);
        _lastAppliedCorner = corner;
    }

    private void EndStroke()
    {
        _painting = false;
        if (_active?.Document == null || _strokeBefore == null)
            return;

        TerrainDocument after = _active.Document.CloneDocument();
        EditorUndoRedoManager undo = GetUndoRedo();
        undo.CreateAction("地形笔刷", UndoRedo.MergeMode.Disable, _active);
        undo.AddDoProperty(_active, TerrainWorld.PropertyName.Document, after);
        undo.AddUndoProperty(_active, TerrainWorld.PropertyName.Document, _strokeBefore);
        undo.CommitAction(false);
        _strokeBefore = null;
    }

    private bool TryProjectToTerrain(Camera3D camera, Vector2 screenPosition, out Vector3 localPoint)
    {
        localPoint = Vector3.Zero;
        if (_active == null)
            return false;

        Vector3 worldOrigin = camera.ProjectRayOrigin(screenPosition);
        Vector3 worldDirection = camera.ProjectRayNormal(screenPosition);
        Vector3 localOrigin = _active.ToLocal(worldOrigin);
        Vector3 localDirection = (_active.ToLocal(worldOrigin + worldDirection) - localOrigin).Normalized();
        if (Math.Abs(localDirection.Y) < 0.00001f)
            return false;

        float distance = -localOrigin.Y / localDirection.Y;
        if (distance < 0.0f)
            return false;

        for (int i = 0; i < 5; i++)
        {
            Vector3 point = localOrigin + localDirection * distance;
            float height = _active.SampleHeight(new Vector2(point.X, point.Z));
            distance = (height - localOrigin.Y) / localDirection.Y;
        }

        if (distance < 0.0f)
            return false;
        localPoint = localOrigin + localDirection * distance;
        return _active.ContainsLocalPoint(new Vector2(localPoint.X, localPoint.Z));
    }

    private void CreateTerrain()
    {
        Node? root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root == null)
        {
            GD.PushWarning("W3 Foundation: open or create a 3D scene before creating terrain.");
            return;
        }

        var document = new TerrainDocument { TileWidth = 32, TileHeight = 32 };
        document.EnsureInitialized();
        var terrain = new TerrainWorld
        {
            Name = "TerrainWorld",
            Document = document,
        };
        root.AddChild(terrain, true);
        terrain.Owner = root;
        EditorInterface.Singleton.GetSelection().Clear();
        EditorInterface.Singleton.GetSelection().AddNode(terrain);
        SetActive(terrain);
    }
}
#endif
