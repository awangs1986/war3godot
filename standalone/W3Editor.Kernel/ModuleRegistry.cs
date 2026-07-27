using W3Editor.Contracts;

namespace W3Editor.Kernel;

/// <summary>Everything the loaded modules contributed, grouped for the shell.</summary>
public sealed class ModuleRegistry
{
    private readonly List<(string ModuleId, CommandDescriptor Item)> _commands = new();
    private readonly List<(string ModuleId, IToolDescriptor Item)> _tools = new();
    private readonly List<(string ModuleId, IPanelDescriptor Item)> _panels = new();
    private readonly List<(string ModuleId, IWindowDescriptor Item)> _windows = new();
    private readonly List<(string ModuleId, IViewportOverlay Item)> _overlays = new();
    private readonly List<(string ModuleId, IPartCodec Item)> _codecs = new();
    private readonly List<(string ModuleId, IAssetSource Item)> _assetSources = new();

    // Contributions are sorted by (Order, Id) so UI layout does not depend on
    // module load order — otherwise menus reshuffle between runs.
    public IReadOnlyList<CommandDescriptor> Commands =>
        _commands.Select(x => x.Item).OrderBy(c => c.Order).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();

    public IReadOnlyList<IToolDescriptor> Tools =>
        _tools.Select(x => x.Item).OrderBy(t => t.Group, StringComparer.Ordinal)
            .ThenBy(t => t.Order).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();

    public IReadOnlyList<IPanelDescriptor> Panels =>
        _panels.Select(x => x.Item).OrderBy(p => p.Order).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();

    public IReadOnlyList<IWindowDescriptor> Windows =>
        _windows.Select(x => x.Item).OrderBy(w => w.Id, StringComparer.Ordinal).ToList();

    public IReadOnlyList<IViewportOverlay> Overlays =>
        _overlays.Select(x => x.Item).OrderBy(o => o.Order).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();

    public IReadOnlyList<IPartCodec> Codecs =>
        _codecs.Select(x => x.Item).OrderBy(c => c.Order).ThenBy(c => c.PartId, StringComparer.Ordinal).ToList();

    public IReadOnlyList<IAssetSource> AssetSources =>
        _assetSources.Select(x => x.Item).OrderBy(a => a.Priority).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();

    internal void AddCommand(string moduleId, CommandDescriptor item) => _commands.Add((moduleId, item));
    internal void AddTool(string moduleId, IToolDescriptor item) => _tools.Add((moduleId, item));
    internal void AddPanel(string moduleId, IPanelDescriptor item) => _panels.Add((moduleId, item));
    internal void AddWindow(string moduleId, IWindowDescriptor item) => _windows.Add((moduleId, item));
    internal void AddOverlay(string moduleId, IViewportOverlay item) => _overlays.Add((moduleId, item));
    internal void AddCodec(string moduleId, IPartCodec item) => _codecs.Add((moduleId, item));
    internal void AddAssetSource(string moduleId, IAssetSource item) => _assetSources.Add((moduleId, item));

    /// <summary>Drops every contribution from one module (removal / hot-unload).</summary>
    public void RemoveModule(string moduleId)
    {
        _commands.RemoveAll(x => x.ModuleId == moduleId);
        _tools.RemoveAll(x => x.ModuleId == moduleId);
        _panels.RemoveAll(x => x.ModuleId == moduleId);
        _windows.RemoveAll(x => x.ModuleId == moduleId);
        _overlays.RemoveAll(x => x.ModuleId == moduleId);
        _codecs.RemoveAll(x => x.ModuleId == moduleId);
        _assetSources.RemoveAll(x => x.ModuleId == moduleId);
    }

    public IReadOnlyList<string> ContributionsOf(string moduleId) =>
        _commands.Where(x => x.ModuleId == moduleId).Select(x => "command:" + x.Item.Id)
            .Concat(_tools.Where(x => x.ModuleId == moduleId).Select(x => "tool:" + x.Item.Id))
            .Concat(_panels.Where(x => x.ModuleId == moduleId).Select(x => "panel:" + x.Item.Id))
            .Concat(_windows.Where(x => x.ModuleId == moduleId).Select(x => "window:" + x.Item.Id))
            .Concat(_overlays.Where(x => x.ModuleId == moduleId).Select(x => "overlay:" + x.Item.Id))
            .Concat(_codecs.Where(x => x.ModuleId == moduleId).Select(x => "codec:" + x.Item.PartId))
            .Concat(_assetSources.Where(x => x.ModuleId == moduleId).Select(x => "assets:" + x.Item.Id))
            .ToList();
}

/// <summary>Per-module view of the host. One instance per loaded module.</summary>
internal sealed class ModuleContext : IModuleContext
{
    private readonly ModuleHost _host;

    public ModuleContext(ModuleHost host, string moduleId)
    {
        _host = host;
        ModuleId = moduleId;
    }

    public string ModuleId { get; }
    public IMapDocument? Document => _host.Document;
    public ICapabilities Capabilities => _host.Capabilities;
    public IEditorLog Log => _host.Log;

    public event Action<IMapDocument?>? DocumentChanged
    {
        add => _host.DocumentChanged += value;
        remove => _host.DocumentChanged -= value;
    }

    public void AddCommand(CommandDescriptor command) => _host.Registry.AddCommand(ModuleId, command);
    public void AddTool(IToolDescriptor tool) => _host.Registry.AddTool(ModuleId, tool);
    public void AddPanel(IPanelDescriptor panel) => _host.Registry.AddPanel(ModuleId, panel);
    public void AddWindow(IWindowDescriptor window) => _host.Registry.AddWindow(ModuleId, window);
    public void AddOverlay(IViewportOverlay overlay) => _host.Registry.AddOverlay(ModuleId, overlay);
    public void AddPartCodec(IPartCodec codec) => _host.Registry.AddCodec(ModuleId, codec);
    public void AddAssetSource(IAssetSource source) => _host.Registry.AddAssetSource(ModuleId, source);

    public void ProvideCapability<T>(T instance) where T : class =>
        _host.CapabilityRegistry.Provide(instance, ModuleId);
}
