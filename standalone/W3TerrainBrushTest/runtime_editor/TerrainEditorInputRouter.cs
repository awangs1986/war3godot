using System;
using Godot;
using W3Foundation.Editor;

namespace W3Foundation.RuntimeEditor;

/// <summary>
/// Runtime replacement for EditorPlugin._Forward3DGuiInput: feeds unhandled
/// viewport input to the existing TerrainNative3DBrush. This node must be the
/// LAST child of the editor root so it receives unhandled input before the
/// navigation camera (Godot delivers unhandled input in reverse tree order).
/// </summary>
public partial class TerrainEditorInputRouter : Node
{
    private TerrainRuntimeEditorApp _app = null!;
    private TerrainNative3DBrush _brush = null!;
    private Camera3D _camera = null!;
    private Action _refreshOverlay = null!;

    public void Configure(
        TerrainRuntimeEditorApp app,
        TerrainNative3DBrush brush,
        Camera3D camera,
        Action refreshOverlay)
    {
        _app = app;
        _brush = brush;
        _camera = camera;
        _refreshOverlay = refreshOverlay;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_brush == null)
            return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // Redo must be matched before the brush: its own Ctrl+Z check does
            // not look at Shift, so Ctrl+Shift+Z would otherwise undo.
            if (key.CtrlPressed && (key.Keycode == Key.Y || (key.ShiftPressed && key.Keycode == Key.Z)))
            {
                _app.Redo();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (key.CtrlPressed && key.Keycode == Key.S)
            {
                _app.SaveCurrent();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (key.Keycode == Key.Escape)
            {
                _app.SetBrushMode(false);
                GetViewport().SetInputAsHandled();
                return;
            }
            // Mid-stroke Ctrl+Z is owned by the brush (commit stroke, then undo).
            // When the brush is disabled it ignores keys, so undo here instead.
            if (key.CtrlPressed && key.Keycode == Key.Z)
            {
                if (!_brush.Handle(_camera, @event, _refreshOverlay, _app.Undo))
                    _app.Undo();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // Unlike _Forward3DGuiInput, unhandled input can also fire for mouse
        // events over 2D panels. Treat those as leaving the map: no painting
        // below the UI, and a stroke in progress is committed.
        if (@event is InputEventMouse && GetViewport().GuiGetHoveredControl() != null)
        {
            _brush.NotifyPointerExited();
            _refreshOverlay();
            return;
        }

        // The brush consumes LMB/wheel while active and passes RMB/MMB through,
        // so the navigation camera (an earlier sibling) still receives those.
        if (_brush.Handle(_camera, @event, _refreshOverlay, _app.Undo))
            GetViewport().SetInputAsHandled();
    }
}
