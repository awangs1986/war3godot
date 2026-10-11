using W3Editor.Contracts;

namespace W3Editor.Modules.WorldData;

public sealed class RegionsPartModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        "part.regions", "1.0.0", ContractVersion.Value,
        ModuleTier.Full, DisplayName: "区域数据 (war3map.w3r)");
    public void Register(IModuleContext ctx) => ctx.AddPartCodec(new RegionsCodec());
    public void Dispose() { }
}

public sealed class RegionToolModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        "tool.region", "1.0.0", ContractVersion.Value,
        ModuleTier.Full, DisplayName: "区域工具");
    public void Register(IModuleContext ctx) => ctx.AddTool(new RegionToolDescriptor());
    public void Dispose() { }
}

internal sealed class RegionToolDescriptor : IToolDescriptor
{
    public string Id => "tool.region";
    public string DisplayName => "区域";
    public string Group => "regions";
    public ToolTargetKind Target => ToolTargetKind.Region;
    public int Order => 10;
    public ITool Create(IModuleContext ctx) => new RegionTool();
}

internal sealed class RegionTool : ITool
{
    private RegionsPart? _part;
    private RegionsSnapshotDelta? _before;
    private float _startX;
    private float _startY;
    private float _endX;
    private float _endY;

    public IToolOptions? Options => null;
    public void OnBegin(ToolStrokeContext ctx)
    {
        _part = ctx.Document.GetPart<RegionsPart>();
        _before = _part == null ? null : new RegionsSnapshotDelta(_part);
        _startX = _endX = ctx.Fx;
        _startY = _endY = ctx.Fy;
    }
    public void OnApply(ToolStrokeContext ctx, double frameDelta)
    {
        _endX = ctx.Fx;
        _endY = ctx.Fy;
    }
    public void OnEnd(ToolStrokeContext ctx)
    {
        if (_part == null || _before == null)
            return;
        _endX = ctx.Fx;
        _endY = ctx.Fy;
        if (Math.Abs(_endX - _startX) < 0.001f || Math.Abs(_endY - _startY) < 0.001f)
            return;
        byte[][] colors =
        {
            new byte[] { 177, 84, 84 }, new byte[] { 185, 122, 86 },
            new byte[] { 181, 157, 84 }, new byte[] { 122, 158, 90 },
            new byte[] { 84, 158, 146 }, new byte[] { 84, 122, 168 },
            new byte[] { 122, 98, 168 }, new byte[] { 164, 92, 146 },
            new byte[] { 180, 100, 124 }, new byte[] { 110, 122, 138 },
        };
        byte[] color = colors[_part.Regions.Count % colors.Length];
        _part.Regions.Add(new RegionRecord
        {
            Left = Math.Min(_startX, _endX),
            Bottom = Math.Min(_startY, _endY),
            Right = Math.Max(_startX, _endX),
            Top = Math.Max(_startY, _endY),
            Name = _part.NextName(),
            CreationNumber = _part.NextCreationNumber(),
            R = color[0], G = color[1], B = color[2],
        });
        ctx.Transaction.RecordBefore(_part, _before);
        _part.MarkDirty();
        ctx.GrowTouched(
            (int)MathF.Floor(Math.Min(_startX, _endX) / 128f),
            (int)MathF.Floor(Math.Min(_startY, _endY) / 128f),
            (int)MathF.Ceiling(Math.Max(_startX, _endX) / 128f),
            (int)MathF.Ceiling(Math.Max(_startY, _endY) / 128f));
        _before = null;
    }
}
