using W3Editor.Contracts;
using W3Editor.Formats;

namespace W3Editor.Modules.Placement;

public sealed class DoodadsPartModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        Id: "part.doodads",
        Version: "1.0.0",
        ContractsVersion: ContractVersion.Value,
        Tier: ModuleTier.Full,
        DisplayName: "装饰物数据 (war3map.doo)");

    public void Register(IModuleContext ctx) => ctx.AddPartCodec(new DoodadsCodec());
    public void Dispose() { }
}

public sealed class UnitsPartModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        Id: "part.units",
        Version: "1.0.0",
        ContractsVersion: ContractVersion.Value,
        Tier: ModuleTier.Full,
        DisplayName: "单位数据 (war3mapUnits.doo)");

    public void Register(IModuleContext ctx) => ctx.AddPartCodec(new UnitsCodec());
    public void Dispose() { }
}

public sealed class DoodadToolModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        Id: "tool.doodad",
        Version: "1.0.0",
        ContractsVersion: ContractVersion.Value,
        Tier: ModuleTier.Full,
        DisplayName: "装饰物笔刷");

    public void Register(IModuleContext ctx) =>
        ctx.AddTool(new PlacementToolDescriptor(
            "tool.doodad", "装饰物", 10, isUnit: false));

    public void Dispose() { }
}

public sealed class UnitToolModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        Id: "tool.unit",
        Version: "1.0.0",
        ContractsVersion: ContractVersion.Value,
        Tier: ModuleTier.Full,
        DisplayName: "单位笔刷");

    public void Register(IModuleContext ctx) =>
        ctx.AddTool(new PlacementToolDescriptor(
            "tool.unit", "单位", 20, isUnit: true));

    public void Dispose() { }
}

internal sealed class PlacementToolDescriptor : IToolDescriptor
{
    private readonly bool _isUnit;

    public PlacementToolDescriptor(string id, string displayName, int order, bool isUnit)
    {
        Id = id;
        DisplayName = displayName;
        Order = order;
        _isUnit = isUnit;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Group => "placement";
    public ToolTargetKind Target => ToolTargetKind.Object;
    public int Order { get; }
    public ITool Create(IModuleContext ctx) => new PlacementTool(_isUnit, ctx);
}

/// <summary>
/// Places one object per stroke. Ground height comes from IPickService when a
/// renderer provides it; without one the placement keeps the caller's Z, so the
/// tool still works headlessly (that degradation is the point of ICapabilities).
/// </summary>
public sealed class PlacementTool : ITool
{
    private readonly bool _isUnit;
    private readonly IModuleContext _ctx;
    private readonly ToolOptions _options;
    private PlacementPartBase? _part;
    private PlacementDelta? _before;

    public PlacementTool(bool isUnit, IModuleContext ctx)
    {
        _isUnit = isUnit;
        _ctx = ctx;
        _options = new ToolOptions(
            new ToolOption("id", "对象 ID", ToolOptionKind.Choice, isUnit ? "hfoo" : "LTlt"),
            new ToolOption("variation", "变体", ToolOptionKind.Int, 0, 0, 32, 1),
            new ToolOption("scale", "缩放", ToolOptionKind.Float, 1.0f, 0.1, 10.0, 0.1),
            new ToolOption("angleDeg", "朝向", ToolOptionKind.Float, isUnit ? 270f : 0f, 0, 360, 1),
            new ToolOption("owner", "所属玩家", ToolOptionKind.Int, 0, 0, 15, 1));
    }

    public IToolOptions? Options => _options;

    public void OnBegin(ToolStrokeContext ctx)
    {
        _part = _isUnit
            ? ctx.Document.GetPart<UnitsPart>()
            : (PlacementPartBase?)ctx.Document.GetPart<DoodadsPart>();
        _before = _part?.Snapshot();
    }

    public void OnApply(ToolStrokeContext ctx, double frameDelta) { }

    public void OnEnd(ToolStrokeContext ctx)
    {
        if (_part == null || _before == null)
            return;

        float z = _ctx.Capabilities.Get<IPickService>()?.GroundHeightAt(ctx.Fx, ctx.Fy) ?? 0f;
        float scale = _options.GetFloat("scale", 1f);
        var placement = new W3Placement
        {
            Id = _options.GetString("id", _isUnit ? "hfoo" : "LTlt"),
            Variation = _options.GetInt("variation"),
            X = ctx.Fx,
            Y = ctx.Fy,
            Z = z,
            Angle = _options.GetFloat("angleDeg") * MathF.PI / 180f,
            ScaleX = scale,
            ScaleY = scale,
            ScaleZ = scale,
            PlayerOwner = _options.GetInt("owner"),
            IsUnit = _isUnit,
            CreationNumber = _part.Document.NextCreationNumber(),
        };

        ctx.Transaction.RecordBefore(_part, _before);
        _part.Placements.Add(placement);
        _part.MarkDirty();
        _before = null;
    }
}

/// <summary>Local copy of the declarative option bag (modules do not share code).</summary>
internal sealed class ToolOptions : IToolOptions
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public ToolOptions(params ToolOption[] schema)
    {
        Schema = schema;
        foreach (ToolOption option in schema)
            _values[option.Key] = option.Default;
    }

    public IReadOnlyList<ToolOption> Schema { get; }
    public event Action<string>? OptionChanged;

    public object? Get(string key) => _values.TryGetValue(key, out object? v) ? v : null;

    public void Set(string key, object? value)
    {
        if (!_values.ContainsKey(key))
            throw new ArgumentException($"Unknown option '{key}'.", nameof(key));
        if (Equals(_values[key], value))
            return;
        _values[key] = value;
        OptionChanged?.Invoke(key);
    }

    public int GetInt(string key, int fallback = 0) => Get(key) switch
    {
        int i => i,
        double d => (int)d,
        string s when int.TryParse(s, out int p) => p,
        _ => fallback,
    };

    public float GetFloat(string key, float fallback = 0f) => Get(key) switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        string s when float.TryParse(s, out float p) => p,
        _ => fallback,
    };

    public string GetString(string key, string fallback = "") => Get(key)?.ToString() ?? fallback;
}
