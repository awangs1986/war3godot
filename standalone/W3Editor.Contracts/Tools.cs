namespace W3Editor.Contracts;

/// <summary>What a tool addresses. Mirrors HiveWE <c>Brush::Type</c>.</summary>
public enum ToolTargetKind
{
    /// <summary>Corner grid (heights, cliffs, textures).</summary>
    Corner,
    /// <summary>Tile cell centers (water, boundary, holes).</summary>
    Cell,
    /// <summary>Placed objects (doodads, units).</summary>
    Object,
    /// <summary>Rectangular areas (regions, cameras).</summary>
    Region,
}

public interface IToolDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    /// <summary>UI grouping key, e.g. "terrain" / "placement" / "pathing".</summary>
    string Group { get; }
    ToolTargetKind Target { get; }
    /// <summary>Sort key inside the group; ties break by Id so layout is stable across runs.</summary>
    int Order { get; }
    ITool Create(IModuleContext ctx);
}

/// <summary>
/// One brush/tool. All three callbacks run on the UI thread and must be cheap
/// (see <see cref="ThreadingContract"/>). The kernel opens the transaction:
/// OnBegin is called inside a fresh transaction, OnEnd is followed by Commit.
/// </summary>
public interface ITool
{
    /// <summary>Stroke start — HiveWE <c>apply_begin</c>.</summary>
    void OnBegin(ToolStrokeContext ctx);

    /// <summary>Stroke step — HiveWE <c>apply(frame_delta)</c>.</summary>
    void OnApply(ToolStrokeContext ctx, double frameDelta);

    /// <summary>
    /// Stroke end — HiveWE <c>apply_end</c>. This is where pathing must be
    /// re-blitted for the touched rect (HiveWE PathingRect semantics).
    /// </summary>
    void OnEnd(ToolStrokeContext ctx);

    IToolOptions? Options { get; }
}

/// <summary>Per-stroke state handed to a tool.</summary>
public sealed class ToolStrokeContext
{
    public required IMapDocument Document { get; init; }
    public required IEditTransaction Transaction { get; init; }

    /// <summary>Cursor position in the tool's target space (corner or cell indices).</summary>
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>Continuous position, for tools that want sub-cell precision.</summary>
    public float Fx { get; set; }
    public float Fy { get; set; }

    /// <summary>Secondary/right button — HiveWE's erase-modifier convention.</summary>
    public bool Erase { get; set; }

    public bool Shift { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }

    /// <summary>
    /// Region the tool actually touched, in corner coordinates. Tools MUST
    /// report it: the renderer syncs only this rect, and an under-reported
    /// rect leaves stale geometry (external review 4.33).
    /// </summary>
    public (int X0, int Y0, int X1, int Y1)? TouchedRect { get; set; }

    public void GrowTouched(int x0, int y0, int x1, int y1)
    {
        TouchedRect = TouchedRect is { } r
            ? (Math.Min(r.X0, x0), Math.Min(r.Y0, y0), Math.Max(r.X1, x1), Math.Max(r.Y1, y1))
            : (x0, y0, x1, y1);
    }
}

// ---------------------------------------------------------------------------
// Declarative options: tools describe their parameters, the shell renders the
// UI. Without this every tool would have to ship its own view, which would put
// an Avalonia dependency in every module and defeat the layering.
// ---------------------------------------------------------------------------

public enum ToolOptionKind { Int, Float, Bool, Choice, Color }

public sealed record ToolOption(
    string Key,
    string DisplayName,
    ToolOptionKind Kind,
    object? Default = null,
    double Min = 0,
    double Max = 0,
    double Step = 1,
    IReadOnlyList<ToolChoice>? Choices = null);

public sealed record ToolChoice(string Value, string DisplayName);

/// <summary>Declarative parameter set. The shell builds the panel from this.</summary>
public interface IToolOptions
{
    IReadOnlyList<ToolOption> Schema { get; }
    object? Get(string key);
    void Set(string key, object? value);
    event Action<string>? OptionChanged;
}
