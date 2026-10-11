using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using W3Editor.Contracts;

namespace W3Editor;

/// <summary>
/// Shell-side composition only. The actual Avalonia trees are created by the
/// panel modules through IPanelDescriptor.CreateView.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<string, Control> _modulePanelViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TabItem> _modulePanelTabs = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Panel, string Name, Type Type), Control> _detachedFallbacks = new();

    private void ComposePhysicalModulePanels()
    {
        _syncingModeUi = true;
        foreach (TabItem tab in _modulePanelTabs.Values)
            ToolTabs.Items.Remove(tab);
        _modulePanelTabs.Clear();
        _modulePanelViews.Clear();
        LeftPanelHost.Content = null;

        var context = new ShellModuleContext(_moduleHost);
        foreach (IPanelDescriptor descriptor in _moduleHost.Registry.Panels.OrderBy(panel => panel.Order))
        {
            object boxed = descriptor.CreateView(context);
            if (boxed is not Control view)
            {
                Console.Error.WriteLine($"Panel '{descriptor.Id}' returned {boxed?.GetType().Name ?? "null"}, expected Avalonia Control.");
                continue;
            }
            _modulePanelViews[descriptor.Id] = view;
            if (descriptor.Dock == PanelDock.Left)
            {
                LeftPanelHost.Content = view;
                continue;
            }
            if (descriptor.Dock != PanelDock.Right)
                continue;
            var tab = new TabItem { Header = descriptor.Title, Content = view, Tag = descriptor.Id };
            _modulePanelTabs[descriptor.Id] = tab;
            ToolTabs.Items.Add(tab);
        }
        _syncingModeUi = false;
        WirePhysicalPanelEvents();
    }

    private void WirePhysicalPanelEvents()
    {
        if (_modulePanelViews.ContainsKey("panel.terrain"))
        {
            foreach (ToggleButton button in BrushToggles())
                button.Click += OnBrushOpClicked;
            BtnTileSetter.Click += OnTileSetter;
            BtnTilePathing.Click += OnTilePathing;
        }
        if (_modulePanelViews.ContainsKey("panel.minimap"))
            PanelControl<Button>("panel.minimap", "BtnHideLeftSidebar").Click += OnHideLeftSidebar;
        if (_modulePanelViews.ContainsKey("panel.pathing"))
        {
            ChkNoWalk.Click += OnPathingOptionChanged;
            ChkNoFly.Click += OnPathingOptionChanged;
            ChkNoBuild.Click += OnPathingOptionChanged;
        }
        if (_modulePanelViews.ContainsKey("panel.regions"))
        {
            BtnRenameRegion.Click += OnRenameRegion;
            BtnDeleteRegion.Click += OnDeleteRegion;
            RegionList.SelectionChanged += OnRegionSelected;
        }
    }

    private T PanelControl<T>(string panelId, string name) where T : Control, new()
    {
        if (_modulePanelViews.TryGetValue(panelId, out Control? root))
        {
            T? found = EnumerateControls(root)
                .OfType<T>()
                .FirstOrDefault(control => control.Name == name);
            if (found != null)
                return found;
            throw new InvalidOperationException($"Panel '{panelId}' is missing required control '{name}'.");
        }
        var key = (panelId, name, typeof(T));
        if (!_detachedFallbacks.TryGetValue(key, out Control? fallback))
        {
            fallback = new T { Name = name, IsVisible = false };
            _detachedFallbacks[key] = fallback;
        }
        return (T)fallback;
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        var pending = new Stack<Control>();
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        pending.Push(root);
        while (pending.Count > 0)
        {
            Control current = pending.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            if (current is Panel panel)
                foreach (Control child in panel.Children)
                    pending.Push(child);
            if (current is ContentControl content && content.Content is Control childContent)
                pending.Push(childContent);
            if (current is Decorator decorator && decorator.Child is Control decoratedChild)
                pending.Push(decoratedChild);
        }
    }

    private TabItem PanelTab(string id) => _modulePanelTabs.TryGetValue(id, out TabItem? tab)
        ? tab
        : (TabItem)PanelControl<TabItem>(id, id + ".detached-tab");

    private TabItem TerrainTab => PanelTab("panel.terrain");
    private TabItem DoodadsTab => PanelTab("panel.doodads");
    private TabItem UnitsTab => PanelTab("panel.units");
    private TabItem PathingTab => PanelTab("panel.pathing");
    private TabItem RegionsTab => PanelTab("panel.regions");

    private ItemsControl TexturePalette => PanelControl<ItemsControl>("panel.terrain", "TexturePalette");
    private ToggleButton BtnRaise => PanelControl<ToggleButton>("panel.terrain", "BtnRaise");
    private ToggleButton BtnLower => PanelControl<ToggleButton>("panel.terrain", "BtnLower");
    private ToggleButton BtnPlateau => PanelControl<ToggleButton>("panel.terrain", "BtnPlateau");
    private ToggleButton BtnSmooth => PanelControl<ToggleButton>("panel.terrain", "BtnSmooth");
    private ToggleButton BtnCliffLower2 => PanelControl<ToggleButton>("panel.terrain", "BtnCliffLower2");
    private ToggleButton BtnCliffLower1 => PanelControl<ToggleButton>("panel.terrain", "BtnCliffLower1");
    private ToggleButton BtnCliffRaise1 => PanelControl<ToggleButton>("panel.terrain", "BtnCliffRaise1");
    private ToggleButton BtnCliffRaise2 => PanelControl<ToggleButton>("panel.terrain", "BtnCliffRaise2");
    private ToggleButton BtnCliffLevel => PanelControl<ToggleButton>("panel.terrain", "BtnCliffLevel");
    private ToggleButton BtnRamp => PanelControl<ToggleButton>("panel.terrain", "BtnRamp");
    private ToggleButton BtnWaterShallow => PanelControl<ToggleButton>("panel.terrain", "BtnWaterShallow");
    private ToggleButton BtnWaterDeep => PanelControl<ToggleButton>("panel.terrain", "BtnWaterDeep");
    private Slider SliderSize => PanelControl<Slider>("panel.terrain", "SliderSize");
    private Slider SliderStrength => PanelControl<Slider>("panel.terrain", "SliderStrength");
    private TextBlock LblSize => PanelControl<TextBlock>("panel.terrain", "LblSize");
    private TextBlock LblStrength => PanelControl<TextBlock>("panel.terrain", "LblStrength");
    private TextBlock TexturePaletteHint => PanelControl<TextBlock>("panel.terrain", "TexturePaletteHint");
    private Button BtnTileSetter => PanelControl<Button>("panel.terrain", "BtnTileSetter");
    private Button BtnTilePathing => PanelControl<Button>("panel.terrain", "BtnTilePathing");

    private ComboBox DoodadCategory => PanelControl<ComboBox>("panel.doodads", "DoodadCategory");
    private TextBox DoodadSearch => PanelControl<TextBox>("panel.doodads", "DoodadSearch");
    private ListBox DoodadList => PanelControl<ListBox>("panel.doodads", "DoodadList");
    private CheckBox ChkRandVariation => PanelControl<CheckBox>("panel.doodads", "ChkRandVariation");
    private CheckBox ChkRandRotation => PanelControl<CheckBox>("panel.doodads", "ChkRandRotation");
    private CheckBox ChkRandScale => PanelControl<CheckBox>("panel.doodads", "ChkRandScale");
    private Slider SliderScatterSpacing => PanelControl<Slider>("panel.doodads", "SliderScatterSpacing");
    private Slider SliderScatterRadius => PanelControl<Slider>("panel.doodads", "SliderScatterRadius");
    private TextBlock LblScatterSpacing => PanelControl<TextBlock>("panel.doodads", "LblScatterSpacing");
    private TextBlock LblScatterRadius => PanelControl<TextBlock>("panel.doodads", "LblScatterRadius");
    private TextBlock DoodadHint => PanelControl<TextBlock>("panel.doodads", "DoodadHint");

    private ComboBox UnitOwner => PanelControl<ComboBox>("panel.units", "UnitOwner");
    private ComboBox UnitRace => PanelControl<ComboBox>("panel.units", "UnitRace");
    private TextBox UnitSearch => PanelControl<TextBox>("panel.units", "UnitSearch");
    private ListBox UnitList => PanelControl<ListBox>("panel.units", "UnitList");
    private CheckBox ChkUnitRandRotation => PanelControl<CheckBox>("panel.units", "ChkUnitRandRotation");
    private TextBlock UnitHint => PanelControl<TextBlock>("panel.units", "UnitHint");

    private Image MinimapImage => PanelControl<Image>("panel.minimap", "MinimapImage");
    private TextBlock MinimapHint => PanelControl<TextBlock>("panel.minimap", "MinimapHint");
    private ListBox LayerList => PanelControl<ListBox>("panel.minimap", "LayerList");
    private TextBlock InfoSource => PanelControl<TextBlock>("panel.minimap", "InfoSource");
    private TextBlock InfoVersion => PanelControl<TextBlock>("panel.minimap", "InfoVersion");
    private TextBlock InfoW3i => PanelControl<TextBlock>("panel.minimap", "InfoW3i");
    private TextBlock InfoTileset => PanelControl<TextBlock>("panel.minimap", "InfoTileset");
    private TextBlock InfoSize => PanelControl<TextBlock>("panel.minimap", "InfoSize");
    private TextBlock InfoOffset => PanelControl<TextBlock>("panel.minimap", "InfoOffset");
    private TextBlock InfoGroundTex => PanelControl<TextBlock>("panel.minimap", "InfoGroundTex");
    private TextBlock InfoCliffTex => PanelControl<TextBlock>("panel.minimap", "InfoCliffTex");
    private TextBlock InfoPathing => PanelControl<TextBlock>("panel.minimap", "InfoPathing");

    private CheckBox ChkNoWalk => PanelControl<CheckBox>("panel.pathing", "ChkNoWalk");
    private CheckBox ChkNoFly => PanelControl<CheckBox>("panel.pathing", "ChkNoFly");
    private CheckBox ChkNoBuild => PanelControl<CheckBox>("panel.pathing", "ChkNoBuild");
    private TextBlock LblPathingRadius => PanelControl<TextBlock>("panel.pathing", "LblPathingRadius");
    private Slider SliderPathingRadius => PanelControl<Slider>("panel.pathing", "SliderPathingRadius");
    private TextBlock PathingHint => PanelControl<TextBlock>("panel.pathing", "PathingHint");

    private TextBox RegionName => PanelControl<TextBox>("panel.regions", "RegionName");
    private Button BtnRenameRegion => PanelControl<Button>("panel.regions", "BtnRenameRegion");
    private Button BtnDeleteRegion => PanelControl<Button>("panel.regions", "BtnDeleteRegion");
    private TextBlock RegionsHint => PanelControl<TextBlock>("panel.regions", "RegionsHint");
    private ListBox RegionList => PanelControl<ListBox>("panel.regions", "RegionList");
}
