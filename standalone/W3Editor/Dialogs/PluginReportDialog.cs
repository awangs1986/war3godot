using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using W3Editor.PluginContracts;

namespace W3Editor.Dialogs;

/// <summary>Read-only validation report window for editor plugins.</summary>
public sealed class PluginReportDialog : Window
{
    private PluginReportDialog(string title, PluginValidationReport report)
    {
        Title = title;
        Width = 560;
        Height = 420;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Ui.BgWindow);

        var body = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 10,
        };

        body.Children.Add(MetaRow("归属", string.IsNullOrWhiteSpace(report.Ownership) ? "-" : report.Ownership));
        body.Children.Add(MetaRow("目标目录", string.IsNullOrWhiteSpace(report.TargetDir) ? "-" : report.TargetDir));
        body.Children.Add(MetaRow("自动缩放", report.AutoScaled
            ? $"是（×{report.ScaleFactor:0.###}）"
            : $"否（×{report.ScaleFactor:0.###}）"));

        body.Children.Add(Ui.Section("问题列表"));

        var issues = new StackPanel { Spacing = 6 };
        if (report.Issues.Count == 0)
        {
            issues.Children.Add(new TextBlock
            {
                Text = "无问题。",
                Foreground = new SolidColorBrush(Ui.TextMuted),
                FontSize = 12,
            });
        }
        else
        {
            foreach (var issue in report.Issues)
            {
                string sev = issue.Severity switch
                {
                    PluginIssueSeverity.Error => "错误",
                    PluginIssueSeverity.Warn => "警告",
                    _ => "信息",
                };
                Color color = issue.Severity switch
                {
                    PluginIssueSeverity.Error => Ui.Danger,
                    PluginIssueSeverity.Warn => Ui.Gold,
                    _ => Ui.TextMuted,
                };
                string code = string.IsNullOrWhiteSpace(issue.Code) ? "" : $"[{issue.Code}] ";
                issues.Children.Add(new TextBlock
                {
                    Text = $"{sev} {code}{issue.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(color),
                    FontSize = 12,
                    LineHeight = 18,
                });
            }
        }

        body.Children.Add(new ScrollViewer
        {
            Height = 220,
            Content = issues,
        });

        var ok = Ui.PrimaryButton("确定");
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Click += (_, _) => Close();
        body.Children.Add(ok);

        Content = body;
    }

    private static DockPanel MetaRow(string label, string value)
    {
        var lbl = new TextBlock
        {
            Text = label,
            Width = 72,
            FontSize = 12,
            Foreground = new SolidColorBrush(Ui.TextMuted),
            VerticalAlignment = VerticalAlignment.Top,
        };
        DockPanel.SetDock(lbl, Dock.Left);
        return new DockPanel
        {
            Children =
            {
                lbl,
                new TextBlock
                {
                    Text = value,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Ui.TextPrimary),
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
    }

    public static Task Show(Window owner, string title, PluginValidationReport report)
        => new PluginReportDialog(title, report).ShowDialog(owner);
}
