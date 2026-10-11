using System.Diagnostics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;

namespace W3Editor.Rendering;

/// <summary>
/// Avalonia-hosted 3D viewport: owns a <see cref="GlTerrainScene"/> and drives
/// the editor camera with the WE/HiveWE control scheme:
///  - right-drag          → pan the look-at point on the ground;
///  - middle-drag         → rotate (yaw/pitch);
///  - Ctrl + right-drag   → rotate (alias, official WE binding);
///  - wheel               → zoom;
///  - arrow keys          → pan (distance-scaled, per-frame);
///  - Home                → reset camera angles/distance;
///  - G                   → toggle the tile grid.
/// Left-button strokes are forwarded as brush events with picked map-corner
/// coordinates.
/// </summary>
public sealed class GlTerrainViewport : OpenGlControlBase
{
    public void RequestRender() => RequestNextFrameRendering();
    public GlTerrainScene Scene { get; } = new();

    public enum ViewportTool
    {
        TerrainBrush,
        Placement,
    }

    /// <summary>Left-button behavior: terrain brush strokes vs placement editing.</summary>
    public ViewportTool Tool { get; set; } = ViewportTool.TerrainBrush;

    /// <summary>Shift was held when the current brush stroke started (line brush).</summary>
    public bool StrokeShift { get; private set; }

    /// <summary>Left button pressed/dragged over terrain. erase = Ctrl modifier.</summary>
    public event Action<float, float, bool>? BrushStroke;
    public event Action? BrushStrokeEnded;
    public event Action<float, float>? HoverPicked;
    /// <summary>Raw pixel hover (any tool) — used for placement ghost tracking.</summary>
    public event Action<float, float>? PixelHover;
    /// <summary>Placement tool: left press / drag / release with pixel coords + modifiers.</summary>
    public event Action<float, float, KeyModifiers>? PlacePressed;
    public event Action<float, float, KeyModifiers>? PlaceDragged;
    public event Action<float, float, KeyModifiers>? PlaceReleased;
    public event Action<string>? GlError;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrameTime;
    private bool _initialized;
    private bool _failed;
    private readonly System.Collections.Concurrent.ConcurrentQueue<
        (string Path, int Width, int Height, TaskCompletionSource<string?> Tcs)> _captureQueue = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<
        (W3Compat.Formats.W3Placement Proto, int Size, TaskCompletionSource<byte[]?> Tcs)> _thumbQueue = new();
    private Point _lastPointer;
    private bool _rotating;
    private bool _panning;
    private bool _brushing;
    private bool _placing;
    private bool _brushErase;
    private readonly HashSet<Key> _keys = new();

    /// <summary>Diagnostics: counts pointer events that actually reached this control.</summary>
    public int PointerPressCount { get; private set; }
    public int PointerMoveCount { get; private set; }

    public GlTerrainViewport()
    {
        Focusable = true;
    }

    // NOTE: OpenGlControlBase presents through a composition surface that is
    // not hit-testable in Avalonia's input pipeline — pointer events fall
    // through to the panel underneath. The host (MainWindow) therefore
    // forwards panel-level pointer/keyboard events into the Forward* methods
    // below; the On* overrides remain as a fallback should hit-testing work.

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            Scene.Initialize(name => gl.GetProcAddress(name));
            _initialized = true;
        }
        catch (Exception ex)
        {
            _failed = true;
            Dispatcher.UIThread.Post(() => GlError?.Invoke(ex.Message));
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        Scene.Dispose();
        _initialized = false;
        // Settle every pending await — no frames will come after teardown.
        while (_captureQueue.TryDequeue(out var cap))
            cap.Tcs.TrySetResult("GL context torn down before capture");
        while (_thumbQueue.TryDequeue(out var thumb))
            thumb.Tcs.TrySetResult(null);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!_initialized || _failed)
            return;
        double now = _clock.Elapsed.TotalSeconds;
        float delta = (float)Math.Clamp(now - _lastFrameTime, 0.0, 0.1);
        _lastFrameTime = now;
        ApplyKeyPan(delta);

        // Palette thumbnails render into their own FBO before the main pass so
        // the frame Avalonia presents is left in a clean state. Budget a few
        // per frame to keep interaction smooth.
        for (int i = 0; i < 4 && _thumbQueue.TryDequeue(out var thumb); i++)
        {
            byte[]? pixels = null;
            try
            {
                pixels = Scene.RenderPlacementThumbnail(thumb.Proto, thumb.Size);
            }
            catch
            {
                // Missing model / GL hiccup — caller keeps the category chip.
            }
            thumb.Tcs.TrySetResult(pixels);
        }

        double scaling = VisualRoot?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)(Bounds.Width * scaling));
        int height = Math.Max(1, (int)(Bounds.Height * scaling));
        try
        {
            Scene.Render(fb, width, height, now);
        }
        catch (Exception ex)
        {
            _failed = true;
            Dispatcher.UIThread.Post(() => GlError?.Invoke(ex.Message));
            return;
        }
        while (_captureQueue.TryDequeue(out var cap))
        {
            string? error;
            try
            {
                int cw = cap.Width > 0 ? cap.Width : width;
                int ch = cap.Height > 0 ? cap.Height : height;
                error = Scene.CaptureToPng(cap.Path, cw, ch, now);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            cap.Tcs.TrySetResult(error);
        }
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Queues an offscreen screenshot for the next GL frame (independent of
    /// window visibility). width/height 0 = current viewport size.
    /// Resolves to null on success, otherwise an error message.
    /// </summary>
    public async Task<string?> CaptureToPngAsync(string path, int width = 0, int height = 0, int timeoutMs = 15000)
    {
        if (!_initialized || _failed)
            return "GL viewport not initialized";
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _captureQueue.Enqueue((path, width, height, tcs));
        RequestNextFrameRendering();
        // The GL loop can stall (window minimized, compositor pause) — don't
        // leave IPC callers hanging on a frame that never comes.
        Task done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        return done == tcs.Task ? tcs.Task.Result : $"capture timed out after {timeoutMs} ms (no GL frame rendered)";
    }

    /// <summary>Queues an offscreen palette thumbnail (bottom-up RGBA); null when unavailable.</summary>
    public Task<byte[]?> RenderThumbnailAsync(W3Compat.Formats.W3Placement proto, int size)
    {
        if (!_initialized || _failed)
            return Task.FromResult<byte[]?>(null);
        var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thumbQueue.Enqueue((proto, size, tcs));
        RequestNextFrameRendering();
        return tcs.Task;
    }

    /// <summary>HiveWE arrow-key panning (camera.ixx Camera::update).</summary>
    private void ApplyKeyPan(float delta)
    {
        float right = 0f, forward = 0f;
        if (_keys.Contains(Key.Left) || _keys.Contains(Key.A)) right -= 1f;
        if (_keys.Contains(Key.Right) || _keys.Contains(Key.D)) right += 1f;
        if (_keys.Contains(Key.Up) || _keys.Contains(Key.W)) forward -= 1f;
        if (_keys.Contains(Key.Down) || _keys.Contains(Key.S)) forward += 1f;
        Scene.Camera.KeyPan(right, forward, delta);
    }

    // ------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        ForwardKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        ForwardKeyUp(e);
    }

    public void ForwardKeyDown(KeyEventArgs e)
    {
        _keys.Add(e.Key);
        if (e.Key == Key.Home)
        {
            Scene.Camera.Reset();
            e.Handled = true;
        }
        else if (e.Key == Key.G)
        {
            Scene.ShowGrid = !Scene.ShowGrid;
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.W or Key.A or Key.S or Key.D)
        {
            e.Handled = true;
        }
    }

    public void ForwardKeyUp(KeyEventArgs e) => _keys.Remove(e.Key);

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _keys.Clear();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ForwardPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ForwardPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ForwardPointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ForwardPointerWheel(e);
    }

    public void ForwardPointerPressed(PointerPressedEventArgs e)
    {
        if (e.Handled)
            return;
        PointerPressCount++;
        Focus();
        PointerPoint point = e.GetCurrentPoint(this);
        _lastPointer = point.Position;
        if (point.Properties.IsRightButtonPressed)
        {
            // WE/HiveWE: RightButton+Ctrl rotates, plain RightButton pans.
            _rotating = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            _panning = !_rotating;
            e.Pointer.Capture(this);
        }
        else if (point.Properties.IsMiddleButtonPressed)
        {
            _rotating = true;
            e.Pointer.Capture(this);
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            if (Tool == ViewportTool.Placement)
            {
                _placing = true;
                e.Pointer.Capture(this);
                PlacePressed?.Invoke((float)point.Position.X, (float)point.Position.Y, e.KeyModifiers);
            }
            else
            {
                _brushing = true;
                _brushErase = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                StrokeShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                e.Pointer.Capture(this);
                EmitBrush(point.Position);
            }
        }
        e.Handled = true;
    }

    public void ForwardPointerMoved(PointerEventArgs e)
    {
        if (e.Handled)
            return;
        PointerMoveCount++;
        Point position = e.GetCurrentPoint(this).Position;
        var delta = position - _lastPointer;
        _lastPointer = position;
        if (_rotating)
        {
            Scene.Camera.Rotate((float)delta.X, (float)delta.Y);
        }
        else if (_panning)
        {
            Scene.Camera.Pan((float)delta.X, (float)delta.Y);
        }
        else if (_brushing)
        {
            EmitBrush(position);
        }
        else if (_placing)
        {
            PlaceDragged?.Invoke((float)position.X, (float)position.Y, e.KeyModifiers);
        }
        else
        {
            PixelHover?.Invoke((float)position.X, (float)position.Y);
            var pick = Pick(position);
            if (pick != null)
                HoverPicked?.Invoke(pick.Value.CornerX, pick.Value.CornerY);
        }
        e.Handled = true;
    }

    public void ForwardPointerReleased(PointerReleasedEventArgs e)
    {
        if (_brushing)
            BrushStrokeEnded?.Invoke();
        if (_placing)
        {
            var position = e.GetCurrentPoint(this).Position;
            PlaceReleased?.Invoke((float)position.X, (float)position.Y, e.KeyModifiers);
        }
        _rotating = _panning = _brushing = _placing = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    public void ForwardPointerWheel(PointerWheelEventArgs e)
    {
        if (e.Handled)
            return;
        Scene.Camera.Zoom((float)e.Delta.Y);
        e.Handled = true;
    }

    private (float CornerX, float CornerY)? Pick(Point position)
    {
        if (!Scene.HasMap || Bounds.Width < 1 || Bounds.Height < 1)
            return null;
        return Scene.Pick((float)position.X, (float)position.Y, (float)Bounds.Width, (float)Bounds.Height);
    }

    private void EmitBrush(Point position)
    {
        var pick = Pick(position);
        if (pick != null)
            BrushStroke?.Invoke(pick.Value.CornerX, pick.Value.CornerY, _brushErase);
    }
}
