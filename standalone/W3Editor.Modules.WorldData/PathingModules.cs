using W3Editor.Contracts;
using W3Editor.Formats;

namespace W3Editor.Modules.WorldData;

public sealed class PathingPartModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        "part.pathing", "1.0.0", ContractVersion.Value,
        ModuleTier.Full, DisplayName: "寻路数据 (war3map.wpm)");
    public void Register(IModuleContext ctx) => ctx.AddPartCodec(new PathingCodec());
    public void Dispose() { }
}

public sealed class PathingToolModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        "tool.pathing", "1.0.0", ContractVersion.Value,
        ModuleTier.Full, DisplayName: "寻路笔刷");
    public void Register(IModuleContext ctx)
    {
        ctx.AddTool(new PathingToolDescriptor());
        ctx.ProvideCapability<IPathingBlitService>(new PathingBlitService(ctx));
    }
    public void Dispose() { }
}

internal sealed class PathingBlitService(IModuleContext context) : IPathingBlitService
{
    private const byte TerrainBits = 0b0100_1110;

    public void RebuildTerrainPathing(
        IMapDocument document,
        IEditTransaction transaction,
        int terrainX0,
        int terrainY0,
        int terrainX1,
        int terrainY1)
    {
        PathingPart? part = document.GetPart<PathingPart>();
        ITerrainPathingSource? source = context.Capabilities.Get<ITerrainPathingSource>();
        if (part == null || source == null)
            return;

        W3PathingMap map = part.Map;
        int x0 = Math.Clamp(Math.Min(terrainX0, terrainX1) * 4 - 2, 0, map.Width - 1);
        int y0 = Math.Clamp(Math.Min(terrainY0, terrainY1) * 4 - 2, 0, map.Height - 1);
        int x1 = Math.Clamp(Math.Max(terrainX0, terrainX1) * 4 + 2, 0, map.Width - 1);
        int y1 = Math.Clamp(Math.Max(terrainY0, terrainY1) * 4 + 2, 0, map.Height - 1);
        var before = new Dictionary<int, byte>();
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            int index = y * map.Width + x;
            byte old = map.Cells[index];
            byte next = (byte)((old & ~TerrainBits) | source.GetTerrainPathing(document, x, y));
            if (next == old)
                continue;
            before[index] = old;
            map.Cells[index] = next;
        }
        if (before.Count == 0)
            return;
        transaction.RecordBefore(part, new PathingDelta(part.ShapeFingerprint, before));
        part.MarkDirty();
    }
}

internal sealed class PathingToolDescriptor : IToolDescriptor
{
    public string Id => "tool.pathing";
    public string DisplayName => "寻路";
    public string Group => "pathing";
    public ToolTargetKind Target => ToolTargetKind.Cell;
    public int Order => 10;
    public ITool Create(IModuleContext ctx) => new PathingTool();
}

internal sealed class PathingTool : ITool
{
    private readonly PathingOptions _options = new();
    private readonly Dictionary<int, byte> _before = new();
    private PathingPart? _part;

    public IToolOptions? Options => _options;

    public void OnBegin(ToolStrokeContext ctx)
    {
        _part = ctx.Document.GetPart<PathingPart>();
        _before.Clear();
    }

    public void OnApply(ToolStrokeContext ctx, double frameDelta)
    {
        if (_part == null)
            return;
        int radius = _options.GetInt("radius", 2);
        byte mask = (byte)_options.GetInt("mask", W3PathingMap.Unwalkable);
        W3PathingMap map = _part.Map;
        for (int y = Math.Max(0, ctx.Y - radius); y <= Math.Min(map.Height - 1, ctx.Y + radius); y++)
        for (int x = Math.Max(0, ctx.X - radius); x <= Math.Min(map.Width - 1, ctx.X + radius); x++)
        {
            int dx = x - ctx.X;
            int dy = y - ctx.Y;
            if (dx * dx + dy * dy > radius * radius)
                continue;
            int index = y * map.Width + x;
            byte old = map.Cells[index];
            byte next = ctx.Erase ? (byte)(old & ~mask) : (byte)(old | mask);
            if (next == old)
                continue;
            _before.TryAdd(index, old);
            map.Cells[index] = next;
        }
        if (_before.Count > 0)
        {
            _part.MarkDirty();
            ctx.GrowTouched(
                Math.Max(0, (ctx.X - radius) / 4),
                Math.Max(0, (ctx.Y - radius) / 4),
                Math.Min((map.Width - 1) / 4, (ctx.X + radius) / 4 + 1),
                Math.Min((map.Height - 1) / 4, (ctx.Y + radius) / 4 + 1));
        }
    }

    public void OnEnd(ToolStrokeContext ctx)
    {
        if (_part != null && _before.Count > 0)
            ctx.Transaction.RecordBefore(
                _part,
                new PathingDelta(_part.ShapeFingerprint, _before));
        _before.Clear();
    }
}

internal sealed class PathingOptions : IToolOptions
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal)
    {
        ["radius"] = 2,
        ["mask"] = W3PathingMap.Unwalkable,
    };

    public IReadOnlyList<ToolOption> Schema { get; } = new[]
    {
        new ToolOption("radius", "半径", ToolOptionKind.Int, 2, 0, 32, 1),
        new ToolOption("mask", "标志", ToolOptionKind.Choice, W3PathingMap.Unwalkable,
            Choices: new[]
            {
                new ToolChoice(W3PathingMap.Unwalkable.ToString(), "不可行走"),
                new ToolChoice(W3PathingMap.Unflyable.ToString(), "不可飞行"),
                new ToolChoice(W3PathingMap.Unbuildable.ToString(), "不可建造"),
            }),
    };

    public event Action<string>? OptionChanged;
    public object? Get(string key) => _values.TryGetValue(key, out object? value) ? value : null;
    public void Set(string key, object? value)
    {
        if (!_values.ContainsKey(key))
            throw new ArgumentException($"Unknown option '{key}'.", nameof(key));
        _values[key] = value;
        OptionChanged?.Invoke(key);
    }
    public int GetInt(string key, int fallback = 0) => Get(key) switch
    {
        int value => value,
        string text when int.TryParse(text, out int value) => value,
        _ => fallback,
    };
}
