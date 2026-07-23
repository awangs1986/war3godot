using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using W3Editor.Formats;
using W3Editor.Rendering;

namespace W3Editor.Dialogs;

/// <summary>Shared dark-theme palette + widget factory for code-built dialogs.</summary>
internal static class Ui
{
    public static readonly Color BgWindow = Color.FromRgb(0x1E, 0x20, 0x25);
    public static readonly Color BgElevated = Color.FromRgb(0x26, 0x29, 0x30);
    public static readonly Color Stroke = Color.FromRgb(0x38, 0x3C, 0x45);
    public static readonly Color TextPrimary = Color.FromRgb(0xE9, 0xEA, 0xEE);
    public static readonly Color TextMuted = Color.FromRgb(0x9A, 0xA0, 0xAB);
    public static readonly Color Gold = Color.FromRgb(0xE8, 0xB8, 0x4B);
    public static readonly Color Danger = Color.FromRgb(0xE0, 0x6C, 0x5C);

    public static TextBlock Section(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold,
        LetterSpacing = 2,
        Foreground = new SolidColorBrush(Gold),
        Margin = new Avalonia.Thickness(0, 4, 0, 2),
    };

    public static TextBlock FieldLabel(string text) => new()
    {
        Text = text, FontSize = 12,
        Foreground = new SolidColorBrush(TextMuted),
    };

    /// <summary>Rounded tile swatch with bottom scrim + fourcc label.</summary>
    public static Border TileChip(string fourcc, double size, bool selected = false)
    {
        uint c = MinimapRenderer.GetTileColor(fourcc);
        var baseColor = Color.FromRgb((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));

        var scrim = new Border
        {
            Height = size * 0.42,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x00, 0x00, 0x00, 0x00), 0),
                    new GradientStop(Color.FromArgb(0xB0, 0x00, 0x00, 0x00), 1),
                },
            },
        };
        var label = new TextBlock
        {
            Text = fourcc, FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
        };
        var chip = new Border
        {
            Width = size, Height = size,
            Margin = new Avalonia.Thickness(0, 2, 8, 6),
            CornerRadius = new Avalonia.CornerRadius(8),
            ClipToBounds = true,
            BorderBrush = new SolidColorBrush(selected ? Gold : Stroke),
            BorderThickness = new Avalonia.Thickness(selected ? 2 : 1),
            Background = new SolidColorBrush(baseColor),
            Child = new Panel { Children = { scrim, label } },
        };
        ToolTip.SetTip(chip, fourcc);
        return chip;
    }

    public static Button PrimaryButton(string text, string? tooltip = null)
    {
        var b = new Button
        {
            Content = text, Width = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Gold),
            Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x18, 0x1C)),
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        if (tooltip != null) ToolTip.SetTip(b, tooltip);
        return b;
    }

    public static Button SecondaryButton(string text)
        => new()
        {
            Content = text, Width = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            CornerRadius = new Avalonia.CornerRadius(6),
        };

    public static TextBlock PlaceholderLink(string text, bool isDanger = false)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = 12,
            Foreground = new SolidColorBrush(isDanger ? Danger : TextMuted),
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };
        ToolTip.SetTip(t, "占位 · P4");
        return t;
    }

    public static Border Group(string title)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = title, FontSize = 12,
            Foreground = new SolidColorBrush(TextMuted),
            Margin = new Avalonia.Thickness(12, 8, 12, 0),
        });
        return new Border
        {
            Background = new SolidColorBrush(BgElevated),
            CornerRadius = new Avalonia.CornerRadius(8),
            BorderBrush = new SolidColorBrush(Stroke),
            BorderThickness = new Avalonia.Thickness(1),
            Child = inner,
        };
    }

    public static void ApplyWindowChrome(Window w)
    {
        w.Background = new SolidColorBrush(BgWindow);
        w.CanResize = false;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.SizeToContent = SizeToContent.Height;
    }
}
