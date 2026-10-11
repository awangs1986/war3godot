using System;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Editing;
using W3Foundation.Terrain.Rendering;

namespace W3Foundation.Editor;

[Tool]
public partial class TerrainEditorPlugin : EditorPlugin
{
    private TerrainPaletteDock? _palette;
    private TerrainNative3DBrush? _nativeBrush;
    private TerrainWorld? _active;
    private TerrainDocument? _observedDocument;
    private bool _focusPending;

    public override void _EnterTree()
    {
        _palette = new TerrainPaletteDock { Name = "W3 Terrain" };
        _palette.CreateTerrainRequested += CreateTerrain;
        _palette.BrushModeChanged += OnBrushModeChanged;
        AddControlToContainer(CustomControlContainer.SpatialEditorSideLeft, _palette);
        _nativeBrush = new TerrainNative3DBrush(
            () => _palette.CurrentBrush,
            () => _palette.BrushEnabled,
            () => _palette.PreviewColor,
            CommitStroke);
        SetInputEventForwardingAlwaysEnabled();

        SceneChanged += OnSceneChanged;
        EditorInterface.Singleton.GetSelection().SelectionChanged += OnSelectionChanged;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        SceneChanged -= OnSceneChanged;
        EditorInterface.Singleton.GetSelection().SelectionChanged -= OnSelectionChanged;
        if (_palette != null)
        {
            _palette.CreateTerrainRequested -= CreateTerrain;
            _palette.BrushModeChanged -= OnBrushModeChanged;
            RemoveControlFromContainer(CustomControlContainer.SpatialEditorSideLeft, _palette);
            _palette.QueueFree();
        }
        _nativeBrush?.Deactivate();
        _nativeBrush = null;
        _palette = null;
        _active = null;
    }

    public override void _Process(double delta)
    {
        _nativeBrush?.UpdateNavigation(delta, () => { UpdateOverlays(); });
        if (_focusPending)
        {
            _focusPending = false;
            FocusNative3DViewport();
        }
        TerrainWorld? terrain = FindTerrainInEditedScene();
        if (terrain == null)
            return;
        if (!ReferenceEquals(terrain, _active) || !ReferenceEquals(terrain.Document, _observedDocument))
        {
            SetActive(terrain);
            _observedDocument = terrain.Document;
            _focusPending = terrain.Document != null;
        }
    }

    public override bool _Handles(GodotObject @object) => @object is TerrainWorld;

    public override void _Edit(GodotObject @object)
    {
        if (@object is TerrainWorld terrain)
            SetActive(terrain);
    }

    public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
    {
        if (_nativeBrush == null)
            return (int)EditorPlugin.AfterGuiInput.Pass;
        return (int)(_nativeBrush.Handle(viewportCamera, @event, () => { UpdateOverlays(); }, UndoLastAction)
            ? EditorPlugin.AfterGuiInput.Stop
            : EditorPlugin.AfterGuiInput.Pass);
    }

    public override void _Forward3DDrawOverViewport(Control viewportControl)
    {
        _nativeBrush?.Draw(viewportControl);
    }

    private void OnSceneChanged(Node sceneRoot)
    {
        TerrainWorld? terrain = sceneRoot as TerrainWorld ?? FindTerrainBelow(sceneRoot);
        SetActive(terrain);
        _focusPending = terrain?.Document != null;
    }

    private void OnSelectionChanged()
    {
        foreach (Node node in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (node is TerrainWorld terrain)
            {
                SetActive(terrain);
                return;
            }
        }
    }

    private void SetActive(TerrainWorld? terrain)
    {
        _active = terrain;
        _observedDocument = terrain?.Document;
        _palette?.SetActiveTerrain(terrain);
        _nativeBrush?.SetTerrain(terrain);
    }

    /// <summary>
    /// The Godot 3D editor starts with its own saved camera pose. Frame the terrain
    /// explicitly so users do not land on the editor's blue horizon background.
    /// </summary>
    private void FocusNative3DViewport()
    {
        if (_active?.Document == null || !IsInstanceValid(_active))
            return;
        SubViewport viewport = EditorInterface.Singleton.GetEditorViewport3D(0);
        Camera3D? camera = viewport.GetCamera3D();
        if (camera == null)
            return;

        TerrainDocument document = _active.Document;
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
        Vector3 localCenter = new(width * 0.5f, (lowest + highest) * 0.5f, depth * 0.5f);
        Vector3 center = _active.ToGlobal(localCenter);
        camera.Projection = Camera3D.ProjectionType.Perspective;
        // Map-camera framing: HiveWE/Warsmash keep a target and a controlled
        // distance/angle. A free editor camera placed too close turns a 4096-wide
        // map into a solid horizon. This high, distant isometric frame contains
        // the complete map on first load without changing world scale.
        camera.Fov = 62.0f;
        camera.Far = Math.Max(camera.Far, 100000.0f);
        camera.GlobalPosition = center + new Vector3(0.0f, span * 1.50f, span * 0.75f);
        camera.LookAt(center, Vector3.Up);
    }

    private TerrainWorld? FindTerrainInEditedScene()
    {
        Node? root = EditorInterface.Singleton.GetEditedSceneRoot();
        return root as TerrainWorld ?? FindTerrainBelow(root);
    }

    private static TerrainWorld? FindTerrainBelow(Node? root)
    {
        if (root == null)
            return null;
        foreach (Node node in root.FindChildren("*", "TerrainWorld", true, false))
            if (node is TerrainWorld terrain)
                return terrain;
        return null;
    }

    private void CommitStroke(TerrainDocument before, TerrainDocument after)
    {
        if (_active == null || !IsInstanceValid(_active))
            return;
        EditorUndoRedoManager undo = GetUndoRedo();
        undo.CreateAction("W3 Terrain Brush", UndoRedo.MergeMode.Disable, _active);
        undo.AddDoProperty(_active, TerrainWorld.PropertyName.Document, after);
        undo.AddUndoProperty(_active, TerrainWorld.PropertyName.Document, before);
        // Execute the after-snapshot immediately. The 3D viewport therefore always
        // renders the same immutable document stored by the undo action.
        undo.CommitAction();
        EditorInterface.Singleton.MarkSceneAsUnsaved();
    }

    private void UndoLastAction()
    {
        if (_active == null || !IsInstanceValid(_active))
            return;
        EditorUndoRedoManager manager = GetUndoRedo();
        int historyId = manager.GetObjectHistoryId(_active);
        UndoRedo history = manager.GetHistoryUndoRedo(historyId);
        if (history.HasUndo())
            history.Undo();
    }

    private void CreateTerrain()
    {
        Node? root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root == null)
        {
            GD.PushWarning("W3 Terrain: create or open a 3D scene first.");
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
        EditorInterface.Singleton.MarkSceneAsUnsaved();
        SetActive(terrain);
    }

    private void OnBrushModeChanged(bool enabled)
    {
        if (!enabled)
            _nativeBrush?.Deactivate();
        UpdateOverlays();
    }
}
