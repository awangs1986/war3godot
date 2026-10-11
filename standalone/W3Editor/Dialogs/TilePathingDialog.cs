using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using W3Editor.Formats;

namespace W3Editor.Dialogs;

/// <summary>
/// 全局图块寻路 (Tile Pathing) — Figma 终稿对话框。
/// 图块行来自真实 w3e 数据；寻路类型 / 操作方式 / 应用范围为 P4 占位。
/// </summary>
public sealed class TilePathingDialog : Window
{
    public TilePathingDialog(W3EMap? map)
    {
        Title = "全局图块寻路";
        Width = 780;
        Ui.ApplyWindowChrome(this);

        var root = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 8 };
        root.Children.Add(Ui.Section("图块寻路设置"));

        string first = map != null && map.GroundTilesets.Count > 0 ? map.GroundTilesets[0] : "-";
        root.Children.Add(Ui.FieldLabel($"图块：{first}"));

        var tiles = new WrapPanel();
        if (map != null && map.GroundTilesets.Count > 0)
        {
            bool selected = true;
            foreach (var id in map.GroundTilesets)
            {
                tiles.Children.Add(Ui.TileChip(id, 86, selected));
                selected = false;
            }
        }
        else
        {
            tiles.Children.Add(Ui.FieldLabel("（未打开地图）"));
        }
        root.Children.Add(tiles);

        var row = new Grid
        {
            Margin = new Avalonia.Thickness(0, 14, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("230,14,300,14,*"),
        };

        var pathingGroup = Ui.Group("寻路类型");
        var pathingRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Margin = new Avalonia.Thickness(12, 8, 12, 10),
        };
        foreach (var (glyph, caption) in new[] { ("人", "行走"), ("鸟", "飞行"), ("房", "建造") })
            pathingRow.Children.Add(ForbiddenGlyph(glyph, caption));
        ((StackPanel)pathingGroup.Child!).Children.Add(pathingRow);
        Grid.SetColumn(pathingGroup, 0);
        row.Children.Add(pathingGroup);

        var opGroup = Ui.Group("操作方式");
        var opRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14,
            Margin = new Avalonia.Thickness(12, 12, 12, 14),
        };
        foreach (var (label, isChecked) in new[] { ("替换", true), ("添加", false), ("移除", false) })
        {
            var rb = new RadioButton { Content = label, IsChecked = isChecked, IsEnabled = false, GroupName = "op" };
            ToolTip.SetTip(rb, "占位 · P4");
            opRow.Children.Add(rb);
        }
        ((StackPanel)opGroup.Child!).Children.Add(opRow);
        Grid.SetColumn(opGroup, 2);
        row.Children.Add(opGroup);

        var applyGroup = Ui.Group("应用范围");
        var cb = new CheckBox
        {
            Content = "同时更新已有地形", IsEnabled = false,
            Margin = new Avalonia.Thickness(12, 12, 12, 14),
        };
        ToolTip.SetTip(cb, "占位 · P4：把新规则回溯应用到已绘制的地形");
        ((StackPanel)applyGroup.Child!).Children.Add(cb);
        Grid.SetColumn(applyGroup, 4);
        row.Children.Add(applyGroup);

        root.Children.Add(row);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
        };
        var okBtn = Ui.PrimaryButton("确定", "占位 · P4：写回 wpm/w3e 寻路标记");
        okBtn.Click += (_, _) => Close();
        var cancelBtn = Ui.SecondaryButton("取消");
        cancelBtn.Click += (_, _) => Close();
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        root.Children.Add(buttons);

        Content = root;
    }

    /// <summary>Glyph with a red forbidden slash overlay + caption.</summary>
    private static StackPanel ForbiddenGlyph(string glyph, string caption)
    {
        var g = new TextBlock
        {
            Text = glyph, FontSize = 26, FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Ui.TextPrimary),
        };
        var slash = new Line
        {
            StartPoint = new Avalonia.Point(4, 36),
            EndPoint = new Avalonia.Point(40, 4),
            Stroke = new SolidColorBrush(Ui.Danger),
            StrokeThickness = 3,
        };
        var stack = new StackPanel { Spacing = 3, Width = 58 };
        stack.Children.Add(new Panel
        {
            Width = 44, Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { g, slash },
        });
        stack.Children.Add(new TextBlock
        {
            Text = caption, FontSize = 10,
            Foreground = new SolidColorBrush(Ui.TextMuted),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        ToolTip.SetTip(stack, "占位 · P4：切换该图块的禁行标记");
        return stack;
    }
}
