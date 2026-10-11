using W3Editor.Contracts;

namespace W3Editor.Modules.Terrain;

/// <summary>Owns the terrain part and its codec. Everything terrain depends on this.</summary>
public sealed class TerrainPartModule : IEditorModule
{
    public ModuleManifest Manifest { get; } = new(
        Id: "part.terrain",
        Version: "1.0.0",
        ContractsVersion: ContractVersion.Value,
        Tier: ModuleTier.Full,
        DisplayName: "地形数据 (war3map.w3e)");

    public void Register(IModuleContext ctx)
    {
        ctx.AddPartCodec(new TerrainCodec());
        ctx.ProvideCapability<ITerrainPathingSource>(new TerrainPathingSource());
    }

    public void Dispose() { }
}

/// <summary>Shared base: terrain tools are useless without the terrain part.</summary>
public abstract class TerrainToolModuleBase : IEditorModule
{
    public abstract ModuleManifest Manifest { get; }
    public abstract void Register(IModuleContext ctx);
    public void Dispose() { }

    protected static IReadOnlyList<string> RequiresTerrain { get; } = Array.Empty<string>();
}

public sealed class HeightToolModule : TerrainToolModuleBase
{
    public override ModuleManifest Manifest { get; } = new(
        Id: "tool.terrain.height",
        Version: "1.0.0",
        ContractsVersion: ContractVersion.Value,
        Tier: ModuleTier.Full,
        DisplayName: "高度笔刷");

    public override void Register(IModuleContext ctx) =>
        ctx.AddTool(new TerrainToolDescriptor(
            "tool.terrain.height", "高度", 10, context => new HeightTool(context)));
}

public sealed class TextureToolModule : TerrainToolModuleBase
{
    public override ModuleManifest Manifest { get; } = new(
        Id: "tool.terrain.texture",
        Version: "1.0.0",
        ContractsVersion: ContractVersion.Value,
        Tier: ModuleTier.Full,
        DisplayName: "地表笔刷");

    public override void Register(IModuleContext ctx) =>
        ctx.AddTool(new TerrainToolDescriptor(
            "tool.terrain.texture", "地表", 20, context => new TextureTool(context)));
}

public sealed class CliffToolModule : TerrainToolModuleBase
{
    public override ModuleManifest Manifest { get; } = new(
        Id: "tool.terrain.cliff",
        Version: "1.0.0",
        ContractsVersion: ContractVersion.Value,
        Tier: ModuleTier.Full,
        DisplayName: "悬崖笔刷");

    public override void Register(IModuleContext ctx) =>
        ctx.AddTool(new TerrainToolDescriptor(
            "tool.terrain.cliff", "悬崖", 30, context => new CliffTool(context)));
}

public sealed class CellToolModule : TerrainToolModuleBase
{
    public override ModuleManifest Manifest { get; } = new(
        Id: "tool.terrain.cell",
        Version: "1.0.0",
        ContractsVersion: ContractVersion.Value,
        Tier: ModuleTier.Full,
        DisplayName: "水面/单元笔刷");

    public override void Register(IModuleContext ctx) =>
        ctx.AddTool(new TerrainToolDescriptor(
            "tool.terrain.cell", "水面", 40, context => new CellTool(context), ToolTargetKind.Cell));
}

internal sealed class TerrainToolDescriptor : IToolDescriptor
{
    private readonly Func<IModuleContext, ITool> _factory;

    public TerrainToolDescriptor(
        string id, string displayName, int order, Func<IModuleContext, ITool> factory,
        ToolTargetKind target = ToolTargetKind.Corner)
    {
        Id = id;
        DisplayName = displayName;
        Order = order;
        _factory = factory;
        Target = target;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Group => "terrain";
    public ToolTargetKind Target { get; }
    public int Order { get; }
    public ITool Create(IModuleContext ctx) => _factory(ctx);
}

internal sealed class TerrainPathingSource : ITerrainPathingSource
{
    private const float WaterOffset = -0.7f;

    public byte GetTerrainPathing(IMapDocument document, int pathingX, int pathingY)
    {
        TerrainPart? part = document.GetPart<TerrainPart>();
        if (part == null)
            return 0;
        W3Editor.Formats.W3EMap map = part.Map;
        int cx = Math.Clamp(pathingX / 4, 0, map.Width - 1);
        int cy = Math.Clamp(pathingY / 4, 0, map.Height - 1);
        int closestX = Math.Clamp((int)Math.Floor(pathingX / 4.0 + 0.5), 0, map.Width - 1);
        int closestY = Math.Clamp((int)Math.Floor(pathingY / 4.0 + 0.5), 0, map.Height - 1);
        W3Editor.Formats.W3Corner baseCorner = map.Corners[cy * map.Width + cx];
        W3Editor.Formats.W3Corner closest = map.Corners[closestY * map.Width + closestX];
        byte mask = 0;

        if (cx + 1 < map.Width && cy + 1 < map.Height)
        {
            W3Editor.Formats.W3Corner br = map.Corners[cy * map.Width + cx + 1];
            W3Editor.Formats.W3Corner tl = map.Corners[(cy + 1) * map.Width + cx];
            W3Editor.Formats.W3Corner tr = map.Corners[(cy + 1) * map.Width + cx + 1];
            bool cliff = baseCorner.LayerHeight != br.LayerHeight
                || baseCorner.LayerHeight != tl.LayerHeight
                || baseCorner.LayerHeight != tr.LayerHeight;
            bool fullRamp = baseCorner.Ramp && br.Ramp && tl.Ramp && tr.Ramp;
            if (cliff && !fullRamp)
                mask |= W3Compat.Formats.W3PathingMap.Unbuildable | W3Compat.Formats.W3PathingMap.Unwalkable;
        }
        if (closest.Blight)
            mask |= W3Compat.Formats.W3PathingMap.Blight;
        if (closest.Water)
        {
            mask |= W3Compat.Formats.W3PathingMap.Water;
            float water = closest.WaterHeight + WaterOffset;
            float ground = closest.GroundHeight + closest.LayerHeight - 2f;
            if (water > ground + 0.40f)
                mask |= W3Compat.Formats.W3PathingMap.Unbuildable | W3Compat.Formats.W3PathingMap.Unwalkable;
            else if (water > ground)
                mask |= W3Compat.Formats.W3PathingMap.Unbuildable;
        }
        if (baseCorner.MapEdge || baseCorner.Boundary)
            mask |= W3Compat.Formats.W3PathingMap.Unbuildable
                | W3Compat.Formats.W3PathingMap.Unflyable
                | W3Compat.Formats.W3PathingMap.Unwalkable;
        return mask;
    }
}
