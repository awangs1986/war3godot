using W3Editor.Contracts;
using W3Editor.Services;

namespace W3Editor.Modules.Terrain;

/// <summary>
/// Adapts the HiveWE-sealed <see cref="TerrainBrush"/> operators to
/// <see cref="ITool"/>. The stroke model is snapshot-then-diff, matching how
/// the operators actually work: they mutate corners in place, and the delta is
/// derived at stroke end.
///
/// No terrain math lives here. Changing behavior means changing TerrainBrush
/// rather than this adapter.
/// </summary>
public abstract class TerrainToolBase : ITool
{
    private readonly IModuleContext _moduleContext;
    private byte[]? _before;
    private BrushStroke _stroke = new();
    private TerrainPart? _part;

    protected TerrainToolBase(IModuleContext moduleContext, ToolOptions options)
    {
        _moduleContext = moduleContext;
        TerrainOptions = options;
    }

    protected ToolOptions TerrainOptions { get; }
    public IToolOptions? Options => TerrainOptions;

    /// <summary>Operator for this stroke; <paramref name="erase"/> is the right-button modifier.</summary>
    protected abstract BrushOp ResolveOp(bool erase);

    /// <summary>Hook for ops that need extra brush settings (texture index, cliff id).</summary>
    protected virtual void ConfigureSettings(BrushSettings settings) { }
    protected TerrainPart? CurrentPart => _part;

    public void OnBegin(ToolStrokeContext ctx)
    {
        _part = ctx.Document.GetPart<TerrainPart>();
        if (_part == null)
            return;
        _stroke = new BrushStroke();
        _before = _part.PackAll();
    }

    public void OnApply(ToolStrokeContext ctx, double frameDelta)
    {
        if (_part == null || _before == null)
            return;
        BrushOp op = ResolveOp(ctx.Erase);
        if (op == BrushOp.None)
            return;

        var settings = new BrushSettings
        {
            Op = op,
            Radius = Math.Clamp(TerrainOptions.GetInt("radius", 4), 1, 32),
            Strength = TerrainOptions.GetFloat("strength", 0.6f),
            Shape = ParseShape(TerrainOptions.GetString("shape", "Circle")),
        };
        ConfigureSettings(settings);

        // The operators take erase as their own reverse flag; ResolveOp has
        // already consumed it for tools that map erase to a different op.
        TerrainBrush.Apply(_part.Map, ctx.X, ctx.Y, settings, PassEraseThrough && ctx.Erase,
            _stroke, (float)frameDelta);

        int reach = settings.Radius + CliffPropagationSlack;
        ctx.GrowTouched(ctx.X - reach, ctx.Y - reach, ctx.X + reach, ctx.Y + reach);
    }

    public void OnEnd(ToolStrokeContext ctx)
    {
        if (_part == null || _before == null)
            return;
        TerrainDelta? delta = _part.DiffFrom(_before);
        _before = null;
        if (delta == null)
            return; // no-op stroke: nothing to undo

        ctx.Transaction.RecordBefore(_part, delta);
        _part.MarkDirty();
        // Pathing must follow terrain (HiveWE re-blits the PathingRect at
        // apply_end). Degrades cleanly when the pathing module is absent.
        if (ctx.TouchedRect is { } touched)
            _moduleContext.Capabilities.Get<IPathingBlitService>()?.RebuildTerrainPathing(
                ctx.Document, ctx.Transaction, touched.X0, touched.Y0, touched.X1, touched.Y1);
    }

    /// <summary>True when the operator itself understands the erase flag.</summary>
    protected virtual bool PassEraseThrough => false;

    /// <summary>
    /// Cliff propagation runs an unbounded BFS, so the touched rect is padded.
    /// (External review 4.33 flagged the fixed +6 as able to undershoot; tools
    /// that can report an exact rect should override GrowTouched usage.)
    /// </summary>
    protected virtual int CliffPropagationSlack => 6;

    private static BrushShape ParseShape(string value) =>
        Enum.TryParse(value, ignoreCase: true, out BrushShape shape) ? shape : BrushShape.Circle;

    protected static ToolOption[] CommonSchema(params ToolOption[] extra) =>
        new[]
        {
            new ToolOption("radius", "笔刷大小", ToolOptionKind.Int, 4, 1, 32, 1),
            new ToolOption("strength", "强度", ToolOptionKind.Float, 0.6f, 0.0, 1.0, 0.05),
            new ToolOption("shape", "形状", ToolOptionKind.Choice, "Circle", Choices: new[]
            {
                new ToolChoice("Circle", "圆形"),
                new ToolChoice("Square", "方形"),
                new ToolChoice("Diamond", "菱形"),
            }),
        }.Concat(extra).ToArray();
}

/// <summary>Raise / lower / plateau / smooth — HiveWE <c>HeightOperator</c>.</summary>
public sealed class HeightTool : TerrainToolBase
{
    public HeightTool(IModuleContext context) : base(context, new ToolOptions(CommonSchema(
        new ToolOption("mode", "模式", ToolOptionKind.Choice, "Raise", Choices: new[]
        {
            new ToolChoice("Raise", "抬升"),
            new ToolChoice("Lower", "降低"),
            new ToolChoice("Plateau", "台地"),
            new ToolChoice("Smooth", "平滑"),
            new ToolChoice("Ripple", "波纹（HiveWE 当前为空操作）"),
        }))))
    { }

    protected override BrushOp ResolveOp(bool erase)
    {
        BrushOp op = TerrainOptions.GetString("mode", "Raise") switch
        {
            "Lower" => BrushOp.Lower,
            "Plateau" => BrushOp.Plateau,
            "Smooth" => BrushOp.Smooth,
            "Ripple" => BrushOp.Ripple,
            _ => BrushOp.Raise,
        };
        // Right button inverts raise/lower, as in HiveWE.
        if (erase)
            op = op switch
            {
                BrushOp.Raise => BrushOp.Lower,
                BrushOp.Lower => BrushOp.Raise,
                _ => op,
            };
        return op;
    }
}

/// <summary>Ground texture painting — HiveWE <c>TextureOperator</c>.</summary>
public sealed class TextureTool : TerrainToolBase
{
    public TextureTool(IModuleContext context) : base(context, new ToolOptions(CommonSchema(
        new ToolOption("textureIndex", "地表索引", ToolOptionKind.Int, 0, 0, 63, 1),
        new ToolOption("tileId", "地表 4CC", ToolOptionKind.Choice, ""))))
    { }

    protected override BrushOp ResolveOp(bool erase) => BrushOp.Texture;

    protected override void ConfigureSettings(BrushSettings settings)
    {
        int index = TerrainOptions.GetInt("textureIndex");
        string tileId = TerrainOptions.GetString("tileId");
        if (CurrentPart != null && !string.IsNullOrWhiteSpace(tileId))
        {
            int resolved = CurrentPart.Map.GroundTilesets.FindIndex(
                id => id.Equals(tileId, StringComparison.OrdinalIgnoreCase));
            if (resolved >= 0)
                index = resolved;
        }
        settings.TextureIndex = index;
    }
}

/// <summary>Cliff level operators + ramp — HiveWE <c>CliffOperator</c>.</summary>
public sealed class CliffTool : TerrainToolBase
{
    public CliffTool(IModuleContext context) : base(context, new ToolOptions(CommonSchema(
        new ToolOption("mode", "模式", ToolOptionKind.Choice, "CliffRaise1", Choices: new[]
        {
            new ToolChoice("CliffLower2", "降两级"),
            new ToolChoice("CliffLower1", "降一级"),
            new ToolChoice("CliffLevel", "拉平"),
            new ToolChoice("CliffRaise1", "升一级"),
            new ToolChoice("CliffRaise2", "升两级"),
            new ToolChoice("Ramp", "坡道"),
        }),
        new ToolOption("cliffTextureId", "悬崖材质", ToolOptionKind.Int, 0, 0, 15, 1))))
    { }

    protected override BrushOp ResolveOp(bool erase) =>
        TerrainOptions.GetString("mode", "CliffRaise1") switch
        {
            "CliffLower2" => BrushOp.CliffLower2,
            "CliffLower1" => BrushOp.CliffLower1,
            "CliffLevel" => BrushOp.CliffLevel,
            "CliffRaise2" => BrushOp.CliffRaise2,
            "Ramp" => BrushOp.Ramp,
            _ => BrushOp.CliffRaise1,
        };

    protected override void ConfigureSettings(BrushSettings settings) =>
        settings.CliffTextureId = TerrainOptions.GetInt("cliffTextureId");

    protected override bool PassEraseThrough => true;
}

/// <summary>Water/boundary cells — HiveWE <c>CellOperator</c>. Holes remain future.</summary>
public sealed class CellTool : TerrainToolBase
{
    public CellTool(IModuleContext context) : base(context, new ToolOptions(CommonSchema(
        new ToolOption("mode", "模式", ToolOptionKind.Choice, "WaterShallow", Choices: new[]
        {
            new ToolChoice("WaterShallow", "浅水"),
            new ToolChoice("WaterDeep", "深水"),
            new ToolChoice("BoundaryAdd", "增加边界"),
            new ToolChoice("BoundaryRemove", "移除边界"),
        }))))
    { }

    protected override BrushOp ResolveOp(bool erase)
    {
        BrushOp op = TerrainOptions.GetString("mode", "WaterShallow") switch
        {
            "WaterDeep" => BrushOp.WaterDeep,
            "BoundaryAdd" => BrushOp.BoundaryAdd,
            "BoundaryRemove" => BrushOp.BoundaryRemove,
            _ => BrushOp.WaterShallow,
        };
        return op;
    }

    protected override bool PassEraseThrough => true;
}
