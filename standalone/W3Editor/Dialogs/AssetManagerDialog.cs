using Avalonia.Controls;
using W3Editor.Rendering;

namespace W3Editor.Dialogs;

/// <summary>Read-only asset inventory paired with the assets.pack boundary.</summary>
public sealed class AssetManagerDialog : Window
{
    private readonly EditorAssets _assets;
    private readonly TextBox _search = new() { Watermark = "筛选四字符 ID、名称或路径…" };
    private readonly ListBox _terrain = new();
    private readonly ListBox _cliffs = new();

    public AssetManagerDialog(EditorAssets assets)
    {
        _assets = assets;
        Title = "资源管理器";
        Width = 760;
        Height = 620;
        Ui.ApplyWindowChrome(this);

        var root = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 9 };
        root.Children.Add(Ui.Section("Warcraft 资源目录"));
        root.Children.Add(Ui.FieldLabel($"经典：{assets.GameRoot ?? "未找到"}"));
        root.Children.Add(Ui.FieldLabel($"Reforged：{assets.ReforgedRoot ?? "未找到"}"));
        root.Children.Add(_search);
        root.Children.Add(new TabControl
        {
            Height = 430,
            ItemsSource = new[]
            {
                new TabItem { Header = $"地面图块 ({assets.TerrainTiles.Count})", Content = _terrain },
                new TabItem { Header = $"悬崖类型 ({assets.CliffTypes.Count})", Content = _cliffs },
            },
        });
        var close = Ui.PrimaryButton("完成");
        close.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        close.Click += (_, _) => Close();
        root.Children.Add(close);
        Content = root;
        _search.TextChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        string query = (_search.Text ?? "").Trim();
        bool Match(params string[] values) => query.Length == 0
            || values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

        _terrain.ItemsSource = _assets.TerrainTiles.Values
            .Where(item => Match(item.Id, item.Comment, item.Dir, item.File))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => $"{item.Id}  {item.Comment}  —  {item.Dir}/{item.File}")
            .ToArray();
        _cliffs.ItemsSource = _assets.CliffTypes.Values
            .Where(item => Match(item.Id, item.CliffModelDir, item.RampModelDir,
                item.TextureDir, item.TextureFile, item.GroundTile))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => $"{item.Id}  ground={item.GroundTile}  model={item.CliffModelDir}  texture={item.TextureDir}/{item.TextureFile}")
            .ToArray();
    }
}
