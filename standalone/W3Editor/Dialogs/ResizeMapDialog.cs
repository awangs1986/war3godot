using Avalonia.Controls;
using Avalonia.Layout;
using W3Editor.Formats;

namespace W3Editor.Dialogs;

public sealed class ResizeMapDialog : Window
{
    private readonly NumericUpDown _width;
    private readonly NumericUpDown _height;
    public bool Confirmed { get; private set; }
    public int WidthTiles => NormalizeSize(_width.Value);
    public int HeightTiles => NormalizeSize(_height.Value);

    public ResizeMapDialog(int widthTiles, int heightTiles)
    {
        Title = "调整地图大小";
        Width = 420;
        Height = 285;
        Ui.ApplyWindowChrome(this);
        _width = SizeField(widthTiles);
        _height = SizeField(heightTiles);
        var root = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 8 };
        root.Children.Add(Ui.Section("地图边界"));
        AddField(root, "宽度（地块）", _width);
        AddField(root, "高度（地块）", _height);
        root.Children.Add(Ui.FieldLabel("从四边对称扩展/裁剪；HiveWE/WE 范围 32–480，步长 4。操作后清空撤销历史。"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Avalonia.Thickness(0, 12, 0, 0) };
        var apply = Ui.PrimaryButton("调整");
        apply.Click += (_, _) =>
        {
            _width.Value = WidthTiles;
            _height.Value = HeightTiles;
            Confirmed = true;
            Close();
        };
        var cancel = Ui.SecondaryButton("取消");
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;
    }

    private static NumericUpDown SizeField(int value) => new()
    {
        Minimum = W3EMap.MinTiles,
        Maximum = W3EMap.MaxTiles,
        Increment = W3EMap.TileStep,
        Value = value,
        FormatString = "0",
    };

    internal static int NormalizeSize(decimal? value)
    {
        int raw = (int)Math.Round(value ?? W3EMap.MinTiles, MidpointRounding.AwayFromZero);
        int rounded = (int)Math.Round(
            raw / (double)W3EMap.TileStep,
            MidpointRounding.AwayFromZero) * W3EMap.TileStep;
        return Math.Clamp(rounded, W3EMap.MinTiles, W3EMap.MaxTiles);
    }

    private static void AddField(StackPanel root, string label, Control field)
    {
        root.Children.Add(Ui.FieldLabel(label));
        root.Children.Add(field);
    }
}
