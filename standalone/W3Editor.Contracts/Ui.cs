namespace W3Editor.Contracts;

public enum PanelDock { Left, Right, Bottom, Floating }

/// <summary>
/// A dockable panel (HiveWE <c>menus/*_palette</c>). CreateView returns an
/// Avalonia Control boxed as object so this assembly stays UI-free; the shell
/// casts it. A module that returns a non-Control is rejected with a clear log
/// line rather than crashing startup.
/// </summary>
public interface IPanelDescriptor
{
    string Id { get; }
    string Title { get; }
    PanelDock Dock { get; }
    int Order { get; }
    object CreateView(IModuleContext ctx);
}

/// <summary>
/// A standalone top-level window (object editor, trigger editor, model editor).
/// These are too large for a dock panel — HiveWE ships them as separate windows
/// and so do we.
/// </summary>
public interface IWindowDescriptor
{
    string Id { get; }
    string Title { get; }
    int DefaultWidth { get; }
    int DefaultHeight { get; }
    /// <summary>Returns an Avalonia Window boxed as object.</summary>
    object CreateWindow(IModuleContext ctx);
}

/// <summary>Shell-owned host for module-contributed top-level windows.</summary>
public interface IWindowService
{
    bool CanOpen(string windowId);
    Task OpenAsync(string windowId, CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------
// Overlays: deliberately a retained debug-draw list, NOT a GL context. Handing
// modules the GL device would make every module depend on the renderer's
// Silk.NET version and kill the layering. Custom-shaded rendering needs a
// separate, higher contract — do not widen this one.
// ---------------------------------------------------------------------------

public readonly record struct OverlayColor(byte R, byte G, byte B, byte A = 255)
{
    public static OverlayColor Rgb(byte r, byte g, byte b) => new(r, g, b);
}

/// <summary>World-space primitive sink. Coordinates are map world units.</summary>
public interface IOverlayDrawList
{
    void Line(float x0, float y0, float z0, float x1, float y1, float z1, OverlayColor color);
    void Rect(float x0, float y0, float x1, float y1, float z, OverlayColor color, bool filled = false);
    void Billboard(float x, float y, float z, float size, OverlayColor color);
    void Text(float x, float y, float z, string text, OverlayColor color);
}

public interface IOverlayRenderContext
{
    IMapDocument? Document { get; }
    IOverlayDrawList Draw { get; }
    /// <summary>Camera position in world units, for LOD/culling decisions.</summary>
    (float X, float Y, float Z) CameraPosition { get; }
}

/// <summary>
/// A toggleable viewport overlay (pathing / regions / wireframe / brush /
/// click helpers / debug — HiveWE's 10 visibility switches).
/// </summary>
public interface IViewportOverlay
{
    string Id { get; }
    string DisplayName { get; }
    bool DefaultVisible { get; }
    int Order { get; }
    void Render(IOverlayRenderContext ctx);
}

/// <summary>
/// A menu / toolbar / shortcut entry. The handler is free-threaded and gets a
/// real CancellationToken — this is where long work belongs.
/// </summary>
public sealed record CommandDescriptor(
    string Id,
    string DisplayName,
    Func<IModuleContext, CancellationToken, Task> Handler,
    string MenuPath = "",
    string? Shortcut = null,
    int Order = 0,
    Func<IModuleContext, bool>? CanExecute = null);

/// <summary>
/// Narrow host bridge used while legacy Avalonia workflows are extracted into
/// modules. Modules identify an action but never reference the Shell window or
/// its concrete services. A headless host may omit this capability, in which
/// case the corresponding UI modules are skipped by normal dependency rules.
/// </summary>
public interface IEditorShellActions
{
    bool CanExecute(string actionId);
    Task ExecuteAsync(string actionId, CancellationToken cancellationToken = default);
}
