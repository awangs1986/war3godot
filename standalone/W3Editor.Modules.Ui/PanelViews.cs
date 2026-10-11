using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace W3Editor.Modules.Ui;

/// <summary>Concrete Avalonia trees owned by panel modules, not by the Shell.</summary>
internal static class PanelViews
{
    public static Control Create(string id) => id switch
    {
        "panel.terrain" => Terrain(),
        "panel.doodads" => Doodads(),
        "panel.units" => Units(),
        "panel.minimap" => Minimap(),
        "panel.pathing" => Pathing(),
        "panel.regions" => Regions(),
        _ => Placeholder(id),
    };

    private static T Named<T>(T control, string name) where T : Control
    {
        control.Name = name;
        return control;
    }

    private static TextBlock Text(string value, string? style = null) => new()
    {
        Text = value,
        TextWrapping = TextWrapping.Wrap,
        Classes = { style ?? "fieldLabel" },
    };

    private static TextBlock Section(string value) => Text(value, "sectionTitle");

    private static ToggleButton BrushButton(string name, string text, string tag, string? tip = null)
    {
        var button = Named(new ToggleButton
        {
            Content = text, Tag = tag, IsEnabled = false,
            Margin = new Thickness(0, 0, 5, 5), HorizontalAlignment = HorizontalAlignment.Stretch,
        }, name);
        if (tip != null) ToolTip.SetTip(button, tip);
        return button;
    }

    private static Control Terrain()
    {
        var stack = new StackPanel { Margin = new Thickness(10), Spacing = 10 };
        stack.Children.Add(Section("高度（左键绘制 · 右键反向）"));
        var heights = new UniformGrid { Columns = 2 };
        heights.Children.Add(BrushButton("BtnRaise", "抬升", "Raise"));
        heights.Children.Add(BrushButton("BtnLower", "降低", "Lower"));
        heights.Children.Add(BrushButton("BtnPlateau", "整平", "Plateau", "把范围内地面/水面高度拉平到起笔点"));
        heights.Children.Add(BrushButton("BtnSmooth", "平滑", "Smooth"));
        stack.Children.Add(heights);

        stack.Children.Add(Section("悬崖（层级 · 以起笔点为基准）"));
        var cliffs = new UniformGrid { Columns = 2 };
        cliffs.Children.Add(BrushButton("BtnCliffLower2", "降低两层", "CliffLower2"));
        cliffs.Children.Add(BrushButton("BtnCliffLower1", "降低一层", "CliffLower1"));
        cliffs.Children.Add(BrushButton("BtnCliffRaise1", "升高一层", "CliffRaise1"));
        cliffs.Children.Add(BrushButton("BtnCliffRaise2", "升高两层", "CliffRaise2"));
        cliffs.Children.Add(BrushButton("BtnCliffLevel", "同层整平", "CliffLevel"));
        cliffs.Children.Add(BrushButton("BtnRamp", "坡道", "Ramp", "在相差一层的悬崖边沿刷出坡道；右键清除"));
        stack.Children.Add(cliffs);

        stack.Children.Add(Section("水（右键移除水）"));
        var water = new UniformGrid { Columns = 2 };
        water.Children.Add(BrushButton("BtnWaterShallow", "浅水", "WaterShallow"));
        water.Children.Add(BrushButton("BtnWaterDeep", "深水", "WaterDeep"));
        stack.Children.Add(water);
        stack.Children.Add(LabelRow("笔刷大小（角点）", Named(Text("4", "fieldValue"), "LblSize")));
        stack.Children.Add(Named(new Slider { Minimum = 1, Maximum = 16, Value = 4, IsEnabled = false }, "SliderSize"));
        stack.Children.Add(LabelRow("笔刷强度（仅 MCP；交互抬升用帧时间）", Named(Text("60%", "fieldValue"), "LblStrength")));
        stack.Children.Add(Named(new Slider { Minimum = 0, Maximum = 100, Value = 60, IsEnabled = false }, "SliderStrength"));
        stack.Children.Add(Section("地面纹理（点击选择后刷绘）"));
        stack.Children.Add(Named(new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()),
        }, "TexturePalette"));
        stack.Children.Add(Named(Text("打开地图后显示该图块集的纹理"), "TexturePaletteHint"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Named(new Button { Content = "更换图块集…" }, "BtnTileSetter"));
        buttons.Children.Add(Named(new Button { Content = "图块寻路…" }, "BtnTilePathing"));
        stack.Children.Add(buttons);
        return new ScrollViewer { Content = stack };
    }

    private static Control Doodads()
    {
        var root = new DockPanel { Margin = new Thickness(10) };
        var title = Section("装饰物调色板（点击选中 → 3D 视口左键放置）");
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var category = Named(new ComboBox { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch }, "DoodadCategory");
        DockPanel.SetDock(category, Dock.Top); root.Children.Add(category);
        var search = Named(new TextBox { Watermark = "按名称 / ID 过滤…", Margin = new Thickness(0, 8, 0, 8) }, "DoodadSearch");
        DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        var random = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        random.Children.Add(Named(new CheckBox { Content = "随机变体", IsChecked = true, FontSize = 12 }, "ChkRandVariation"));
        random.Children.Add(Named(new CheckBox { Content = "随机旋转", IsChecked = true, FontSize = 12 }, "ChkRandRotation"));
        random.Children.Add(Named(new CheckBox { Content = "随机缩放", IsChecked = true, FontSize = 12 }, "ChkRandScale"));
        bottom.Children.Add(random);
        bottom.Children.Add(SliderRow("散布间距", "LblScatterSpacing", "1.0 格", "SliderScatterSpacing", .5, 4, 1, .5));
        bottom.Children.Add(SliderRow("散布半径", "LblScatterRadius", "3 格", "SliderScatterRadius", 0, 8, 3, 1));
        bottom.Children.Add(Named(Text("打开地图后可用。左键放置 · 拖动散布 · 空格/Esc 取消。"), "DoodadHint"));
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(Named(new ListBox(), "DoodadList"));
        return root;
    }

    private static Control Units()
    {
        var root = new DockPanel { Margin = new Thickness(10) };
        var title = Section("单位调色板（点击选中 → 3D 视口左键放置）");
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        foreach (ComboBox combo in new[]
        {
            Named(new ComboBox { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch }, "UnitOwner"),
            Named(new ComboBox { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch }, "UnitRace"),
        }) { DockPanel.SetDock(combo, Dock.Top); root.Children.Add(combo); }
        var search = Named(new TextBox { Watermark = "按名称 / ID 过滤…", Margin = new Thickness(0, 8, 0, 8) }, "UnitSearch");
        DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        bottom.Children.Add(Named(new CheckBox { Content = "随机朝向（默认 WE 270°）", FontSize = 12 }, "ChkUnitRandRotation"));
        bottom.Children.Add(Named(Text("打开地图后可用。左键放置 · 空格/Esc 取消。"), "UnitHint"));
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(Named(new ListBox(), "UnitList"));
        return root;
    }

    private static Control Minimap()
    {
        var stack = new StackPanel { Margin = new Thickness(14, 12), Spacing = 10 };
        var header = new DockPanel();
        var hide = Named(new Button { Content = "×", Width = 28, Height = 28, Padding = new Thickness(0) }, "BtnHideLeftSidebar");
        DockPanel.SetDock(hide, Dock.Right); header.Children.Add(hide); header.Children.Add(Section("小地图"));
        stack.Children.Add(header);
        var imagePanel = new Panel { Height = 222 };
        imagePanel.Children.Add(Named(new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8) }, "MinimapImage"));
        imagePanel.Children.Add(Named(new TextBlock
        {
            Text = "未打开地图", Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        }, "MinimapHint"));
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = imagePanel });
        stack.Children.Add(Section("地图信息"));
        var info = new StackPanel { Spacing = 7 };
        info.Children.Add(InfoRow("来源", "InfoSource"));
        info.Children.Add(InfoRow("w3e 版本", "InfoVersion"));
        info.Children.Add(InfoRow("地图版本", "InfoW3i"));
        info.Children.Add(InfoRow("图块集", "InfoTileset"));
        info.Children.Add(InfoRow("尺寸(格)", "InfoSize"));
        info.Children.Add(InfoRow("中心偏移", "InfoOffset"));
        info.Children.Add(InfoRow("地面纹理", "InfoGroundTex"));
        info.Children.Add(InfoRow("悬崖纹理", "InfoCliffTex"));
        info.Children.Add(InfoRow("寻路网格", "InfoPathing"));
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = info });
        stack.Children.Add(Section("图层"));
        var layers = Named(new ListBox { IsHitTestVisible = false }, "LayerList");
        foreach (string value in new[] { "地形", "装饰物", "单位", "寻路" })
            layers.Items.Add(new ListBoxItem { Content = value });
        stack.Children.Add(layers);
        return new ScrollViewer { Content = stack };
    }

    private static Control Pathing()
    {
        var stack = new StackPanel { Margin = new Thickness(10), Spacing = 10 };
        stack.Children.Add(Section("寻路笔刷（war3map.wpm）"));
        stack.Children.Add(Text("左键添加、右键清除；可组合多个标志。每格 32 世界单位，2D 与 3D 使用同一数据和撤销栈。"));
        stack.Children.Add(Named(new CheckBox { Content = "禁行走（红）", IsChecked = true, IsEnabled = false }, "ChkNoWalk"));
        stack.Children.Add(Named(new CheckBox { Content = "禁飞行（蓝）", IsEnabled = false }, "ChkNoFly"));
        stack.Children.Add(Named(new CheckBox { Content = "禁建造（金）", IsEnabled = false }, "ChkNoBuild"));
        stack.Children.Add(LabelRow("笔刷半径", Named(Text("2 格", "fieldValue"), "LblPathingRadius")));
        stack.Children.Add(Named(new Slider
        {
            Minimum = 0, Maximum = 32, Value = 2,
            TickFrequency = 1, IsSnapToTickEnabled = true, IsEnabled = false,
        }, "SliderPathingRadius"));
        stack.Children.Add(Named(Text("未加载 wpm。"), "PathingHint"));
        return stack;
    }

    private static Control Regions()
    {
        var root = new DockPanel { Margin = new Thickness(10) };
        var top = new StackPanel { Spacing = 8 };
        top.Children.Add(Section("矩形区域（war3map.w3r）"));
        top.Children.Add(Text("切换到区域模式后，在 2D 或 3D 地面按住左键拖出矩形。区域可供出生点、传送和触发器引用。"));
        top.Children.Add(Named(new TextBox { Watermark = "区域名称", IsEnabled = false }, "RegionName"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(Named(new Button { Content = "重命名", IsEnabled = false }, "BtnRenameRegion"));
        buttons.Children.Add(Named(new Button { Content = "删除", IsEnabled = false }, "BtnDeleteRegion"));
        top.Children.Add(buttons);
        top.Children.Add(Named(Text("未加载区域数据。"), "RegionsHint"));
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        root.Children.Add(Named(new ListBox { Margin = new Thickness(0, 10, 0, 0) }, "RegionList"));
        return root;
    }

    private static DockPanel LabelRow(string label, Control value)
    {
        var row = new DockPanel();
        DockPanel.SetDock(value, Dock.Right); row.Children.Add(value); row.Children.Add(Text(label));
        return row;
    }

    private static DockPanel SliderRow(string label, string labelName, string labelValue,
        string sliderName, double min, double max, double value, double tick)
    {
        var row = new DockPanel();
        var valueText = Named(Text(labelValue), labelName);
        DockPanel.SetDock(valueText, Dock.Right); row.Children.Add(valueText); row.Children.Add(Text(label));
        var stack = new StackPanel(); stack.Children.Add(row);
        stack.Children.Add(Named(new Slider
        {
            Minimum = min, Maximum = max, Value = value,
            TickFrequency = tick, IsSnapToTickEnabled = true,
        }, sliderName));
        return new DockPanel { Children = { stack } };
    }

    private static DockPanel InfoRow(string label, string name)
    {
        var row = new DockPanel();
        row.Children.Add(new TextBlock { Text = label, Width = 78, Classes = { "fieldLabel" } });
        row.Children.Add(Named(new TextBlock { Text = "-", TextWrapping = TextWrapping.Wrap, Classes = { "fieldValue" } }, name));
        return row;
    }

    private static Control Placeholder(string id) => new Border
    {
        Padding = new Thickness(12),
        Child = Text($"{id} 由 Shell 的新面板实现承载。"),
    };
}
