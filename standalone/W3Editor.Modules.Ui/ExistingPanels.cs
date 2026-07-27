using Avalonia.Controls;
using W3Editor.Contracts;

namespace W3Editor.Modules.Ui;

/// <summary>
/// Panel descriptor whose CreateView materializes the module-owned Avalonia
/// tree. The Shell only docks the returned Control and routes shared services.
/// </summary>
internal sealed class ExistingPanelDescriptor(
    string id,
    string title,
    PanelDock dock,
    int order) : IPanelDescriptor
{
    public string Id => id;
    public string Title => title;
    public PanelDock Dock => dock;
    public int Order => order;

    public object CreateView(IModuleContext ctx) => PanelViews.Create(id);
}

public abstract class ExistingPanelModule : IEditorModule
{
    private readonly IPanelDescriptor _descriptor;

    protected ExistingPanelModule(string id, string title, PanelDock dock, int order)
    {
        Manifest = new ModuleManifest(
            id, "1.0.0", ContractVersion.Value, ModuleTier.Full,
            DisplayName: title);
        _descriptor = new ExistingPanelDescriptor(id, title, dock, order);
    }

    public ModuleManifest Manifest { get; }
    public void Register(IModuleContext ctx) => ctx.AddPanel(_descriptor);
    public void Dispose() { }
}

public sealed class TerrainPanelModule : ExistingPanelModule
{
    public TerrainPanelModule() : base("panel.terrain", "地形", PanelDock.Right, 10) { }
}

public sealed class DoodadsPanelModule : ExistingPanelModule
{
    public DoodadsPanelModule() : base("panel.doodads", "装饰物", PanelDock.Right, 20) { }
}

public sealed class UnitsPanelModule : ExistingPanelModule
{
    public UnitsPanelModule() : base("panel.units", "单位", PanelDock.Right, 30) { }
}

public sealed class MinimapPanelModule : ExistingPanelModule
{
    public MinimapPanelModule() : base("panel.minimap", "小地图", PanelDock.Left, 10) { }
}

public sealed class PathingPanelModule : ExistingPanelModule
{
    public PathingPanelModule() : base("panel.pathing", "寻路", PanelDock.Right, 40) { }
}

public sealed class RegionsPanelModule : ExistingPanelModule
{
    public RegionsPanelModule() : base("panel.regions", "区域", PanelDock.Right, 50) { }
}
