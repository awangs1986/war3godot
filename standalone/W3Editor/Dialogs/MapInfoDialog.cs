using Avalonia.Controls;
using Avalonia.Layout;
using W3Editor.Formats;

namespace W3Editor.Dialogs;

/// <summary>Edits the safe identity prefix of war3map.w3i.</summary>
public sealed class MapInfoDialog : Window
{
    private readonly TextBox _name;
    private readonly TextBox _author;
    private readonly TextBox _description;
    private readonly TextBox _players;
    private readonly W3MapInfoIdentity _identity;

    public bool Applied { get; private set; }

    public MapInfoDialog(W3MapInfoIdentity identity)
    {
        _identity = identity;
        Title = "地图信息";
        Width = 620;
        Height = 520;
        Ui.ApplyWindowChrome(this);

        _name = Field(identity.Name);
        _author = Field(identity.Author);
        _description = Field(identity.Description, acceptsReturn: true, height: 110);
        _players = Field(identity.SuggestedPlayers, acceptsReturn: true, height: 72);

        var root = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 8 };
        root.Children.Add(Ui.Section("war3map.w3i 地图信息"));
        AddField(root, "名称", _name);
        AddField(root, "作者", _author);
        AddField(root, "说明", _description);
        AddField(root, "建议玩家", _players);
        root.Children.Add(Ui.FieldLabel(
            $"格式 v{identity.FormatVersion}；玩家、势力、科技等未解析区段会逐字节保留。"));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
        };
        var apply = Ui.PrimaryButton("应用");
        apply.Click += (_, _) =>
        {
            _identity.Name = _name.Text ?? "";
            _identity.Author = _author.Text ?? "";
            _identity.Description = _description.Text ?? "";
            _identity.SuggestedPlayers = _players.Text ?? "";
            Applied = true;
            Close();
        };
        var cancel = Ui.SecondaryButton("取消");
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = new ScrollViewer { Content = root };
    }

    private static TextBox Field(string text, bool acceptsReturn = false, double height = double.NaN) => new()
    {
        Text = text,
        AcceptsReturn = acceptsReturn,
        TextWrapping = acceptsReturn ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap,
        Height = height,
    };

    private static void AddField(StackPanel root, string label, Control field)
    {
        root.Children.Add(Ui.FieldLabel(label));
        root.Children.Add(field);
    }
}
