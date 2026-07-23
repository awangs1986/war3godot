using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace W3Editor.Dialogs;

/// <summary>Minimal modal message box in the editor's dark style.</summary>
public sealed class MessageDialog : Window
{
    private MessageDialog(string title, string message)
    {
        Title = title;
        Width = 480;
        Ui.ApplyWindowChrome(this);

        var ok = Ui.PrimaryButton("确定");
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Ui.TextPrimary),
                    LineHeight = 20,
                },
                ok,
            },
        };
    }

    public static Task Show(Window owner, string title, string message)
        => new MessageDialog(title, message).ShowDialog(owner);
}
