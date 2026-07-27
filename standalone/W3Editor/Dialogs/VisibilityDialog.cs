using Avalonia.Controls;
using System.Text.Json;

namespace W3Editor.Dialogs;

public sealed class VisibilityOptions
{
    public bool Units = true;
    public bool Doodads = true;
    public bool Pathing;
    public bool Regions = true;
    public bool Brush = true;
    public bool Lighting = true;
    public bool Water = true;
    public bool ClickHelpers = true;
    public bool Wireframe = true;
    public bool Debug = true;
}

public static class VisibilityOptionsStore
{
    public static string SettingsPath =>
        Environment.GetEnvironmentVariable("W3EDITOR_SETTINGS_PATH") is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Diablo1Like", "W3Editor", "settings.json");

    public static VisibilityOptions Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new VisibilityOptions();
            Snapshot? value = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(SettingsPath));
            return value?.ToOptions() ?? new VisibilityOptions();
        }
        catch
        {
            // A malformed per-user preference file must never prevent startup.
            return new VisibilityOptions();
        }
    }

    public static void Save(VisibilityOptions value)
    {
        string path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary,
            JsonSerializer.Serialize(Snapshot.From(value), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record Snapshot(
        bool Units, bool Doodads, bool Pathing, bool Regions, bool Brush,
        bool Lighting, bool Water, bool ClickHelpers, bool Wireframe, bool Debug)
    {
        public static Snapshot From(VisibilityOptions value) => new(
            value.Units, value.Doodads, value.Pathing, value.Regions, value.Brush,
            value.Lighting, value.Water, value.ClickHelpers, value.Wireframe, value.Debug);

        public VisibilityOptions ToOptions() => new()
        {
            Units = Units, Doodads = Doodads, Pathing = Pathing, Regions = Regions,
            Brush = Brush, Lighting = Lighting, Water = Water, ClickHelpers = ClickHelpers,
            Wireframe = Wireframe, Debug = Debug,
        };
    }
}

public sealed class VisibilityDialog : Window
{
    public VisibilityDialog(VisibilityOptions options, Action apply)
    {
        Title = "可见性设置";
        Width = 400;
        Height = 500;
        Ui.ApplyWindowChrome(this);
        var root = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 7 };
        root.Children.Add(Ui.Section("HiveWE 可见性开关"));
        Add(root, "单位", () => options.Units, value => options.Units = value, apply);
        Add(root, "装饰物", () => options.Doodads, value => options.Doodads = value, apply);
        Add(root, "寻路覆盖", () => options.Pathing, value => options.Pathing = value, apply);
        Add(root, "区域", () => options.Regions, value => options.Regions = value, apply);
        Add(root, "笔刷指示", () => options.Brush, value => options.Brush = value, apply);
        Add(root, "光照", () => options.Lighting, value => options.Lighting = value, apply);
        Add(root, "水面", () => options.Water, value => options.Water = value, apply);
        Add(root, "点击/选择辅助", () => options.ClickHelpers, value => options.ClickHelpers = value, apply);
        Add(root, "线框网格", () => options.Wireframe, value => options.Wireframe = value, apply);
        Add(root, "调试信息", () => options.Debug, value => options.Debug = value, apply);
        root.Children.Add(Ui.FieldLabel("设置会同时应用到 2D 俯视图和 3D 视口。"));
        var close = Ui.PrimaryButton("完成");
        close.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        close.Click += (_, _) => Close();
        root.Children.Add(close);
        Content = new ScrollViewer { Content = root };
    }

    private static void Add(StackPanel root, string label, Func<bool> get, Action<bool> set, Action apply)
    {
        var box = new CheckBox { Content = label, IsChecked = get() };
        box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); apply(); };
        root.Children.Add(box);
    }
}

/// <summary>Persistent editor preferences; visibility dialog remains the quick, session-level entry.</summary>
public sealed class SettingsDialog : Window
{
    public SettingsDialog(VisibilityOptions options, Action apply)
    {
        Title = "编辑器设置";
        Width = 440;
        Height = 560;
        Ui.ApplyWindowChrome(this);
        var draft = new VisibilityOptions();
        Copy(options, draft);
        var root = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 7 };
        root.Children.Add(Ui.Section("默认可见性（保存到本机用户设置）"));
        Add(root, "单位", () => draft.Units, value => draft.Units = value);
        Add(root, "装饰物", () => draft.Doodads, value => draft.Doodads = value);
        Add(root, "寻路覆盖", () => draft.Pathing, value => draft.Pathing = value);
        Add(root, "区域", () => draft.Regions, value => draft.Regions = value);
        Add(root, "笔刷指示", () => draft.Brush, value => draft.Brush = value);
        Add(root, "光照", () => draft.Lighting, value => draft.Lighting = value);
        Add(root, "水面", () => draft.Water, value => draft.Water = value);
        Add(root, "点击/选择辅助", () => draft.ClickHelpers, value => draft.ClickHelpers = value);
        Add(root, "线框网格", () => draft.Wireframe, value => draft.Wireframe = value);
        Add(root, "调试信息", () => draft.Debug, value => draft.Debug = value);
        root.Children.Add(Ui.FieldLabel("设置文件：" + VisibilityOptionsStore.SettingsPath));

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
        };
        var defaults = Ui.SecondaryButton("恢复默认");
        defaults.Click += (_, _) =>
        {
            Copy(new VisibilityOptions(), draft);
            Copy(draft, options);
            VisibilityOptionsStore.Save(options);
            apply();
            Close();
        };
        var save = Ui.PrimaryButton("保存");
        save.Click += (_, _) =>
        {
            Copy(draft, options);
            VisibilityOptionsStore.Save(options);
            apply();
            Close();
        };
        buttons.Children.Add(defaults);
        buttons.Children.Add(save);
        root.Children.Add(buttons);
        Content = new ScrollViewer { Content = root };
    }

    private static void Add(StackPanel root, string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = get() };
        box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
        root.Children.Add(box);
    }

    private static void Copy(VisibilityOptions source, VisibilityOptions target)
    {
        target.Units = source.Units; target.Doodads = source.Doodads;
        target.Pathing = source.Pathing; target.Regions = source.Regions;
        target.Brush = source.Brush; target.Lighting = source.Lighting;
        target.Water = source.Water; target.ClickHelpers = source.ClickHelpers;
        target.Wireframe = source.Wireframe; target.Debug = source.Debug;
    }
}
