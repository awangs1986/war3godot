using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Dialogs;

/// <summary>
/// 更换图块集 (TileSetter) — functional HiveWE-style tile palette editing.
/// Operates on a staged copy (palette + per-corner texture indices);
/// 确定 writes back into the session map and marks it dirty.
/// </summary>
public sealed class TileSetterDialog : Window
{
    /// <summary>True when 确定 applied staged changes to the session.</summary>
    public bool Applied { get; private set; }

    private readonly MapSession _session;
    private readonly List<string> _tiles;
    private readonly int[] _tex;

    private readonly List<TerrainTile> _catalog;
    private readonly List<char> _tilesetLetters = new();

    private int _selected = -1;
    private string? _selectedAdd;
    private char _addTileset;

    private readonly WrapPanel _currentPanel = new();
    private readonly WrapPanel _addPanel = new();
    private readonly TextBlock _error;
    private readonly ComboBox _tilesetCombo = new() { Width = 310 };

    public TileSetterDialog(MapSession session)
    {
        _session = session;
        var map = session.Map ?? throw new InvalidOperationException("未打开地图");
        _tiles = new List<string>(map.GroundTilesets);
        _tex = new int[map.Corners.Length];
        for (int i = 0; i < map.Corners.Length; i++)
            _tex[i] = map.Corners[i].GroundTexture;

        _catalog = LoadCatalog();
        foreach (var t in _catalog)
            if (!_tilesetLetters.Contains(t.Tileset))
                _tilesetLetters.Add(t.Tileset);
        _tilesetLetters.Sort();
        _addTileset = _tilesetLetters.Contains(map.Tileset) ? map.Tileset
            : _tilesetLetters.Count > 0 ? _tilesetLetters[0] : 'L';

        Title = "更换图块集";
        Width = 800;
        Ui.ApplyWindowChrome(this);

        _error = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Ui.Danger),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var root = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 8 };

        root.Children.Add(Ui.Section("当前使用的图块（点击选择）"));
        root.Children.Add(_currentPanel);

        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        ops.Children.Add(OpButton("← 左移", () => MoveSelected(-1)));
        ops.Children.Add(OpButton("右移 →", () => MoveSelected(1)));
        ops.Children.Add(OpButton("移除", RemoveSelected, danger: true));
        root.Children.Add(ops);

        var addHeader = new DockPanel { Margin = new Avalonia.Thickness(0, 14, 0, 0) };
        foreach (var letter in _tilesetLetters)
            _tilesetCombo.Items.Add(new ComboBoxItem { Content = $"{MapSession.TilesetName(letter)} ({letter})", Tag = letter });
        _tilesetCombo.SelectedIndex = Math.Max(0, _tilesetLetters.IndexOf(_addTileset));
        _tilesetCombo.SelectionChanged += (_, _) =>
        {
            if (_tilesetCombo.SelectedItem is ComboBoxItem { Tag: char letter })
            {
                _addTileset = letter;
                _selectedAdd = null;
                RebuildAddPanel();
            }
        };
        _tilesetCombo.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(_tilesetCombo, Dock.Right);
        addHeader.Children.Add(_tilesetCombo);
        addHeader.Children.Add(Ui.Section("可添加的图块（点击选择）"));
        root.Children.Add(addHeader);

        if (_catalog.Count == 0)
        {
            root.Children.Add(Ui.FieldLabel("未找到 war3/game/TerrainArt/Terrain.slk — 无法列出可添加图块。"));
        }
        root.Children.Add(_addPanel);

        var addBtn = OpButton("添加所选图块", AddSelected);
        root.Children.Add(addBtn);
        root.Children.Add(_error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
        };
        var okBtn = Ui.PrimaryButton("确定", "把修改写入内存中的地图（保存 .w3e 后才落盘）");
        okBtn.Click += (_, _) => Apply();
        var cancelBtn = Ui.SecondaryButton("取消");
        cancelBtn.Click += (_, _) => Close();
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        root.Children.Add(buttons);

        Content = root;
        RebuildCurrentPanel();
        RebuildAddPanel();
    }

    private static List<TerrainTile> LoadCatalog()
    {
        var root = GodotLauncher.FindProjectRoot();
        if (root == null) return new List<TerrainTile>();
        foreach (var sub in new[] { "game", "reforged" })
        {
            var tiles = TerrainSlk.TryLoad(Path.Combine(root, "war3", sub));
            if (tiles is { Count: > 0 })
                return tiles;
        }
        return new List<TerrainTile>();
    }

    // ---------- staged operations ----------

    private int UsageCount(int index)
    {
        int n = 0;
        foreach (var t in _tex)
            if (t == index)
                n++;
        return n;
    }

    private void MoveSelected(int dir)
    {
        ClearError();
        if (_selected < 0) { ShowError("请先在上方选择一个图块。"); return; }
        int j = _selected + dir;
        if (j < 0 || j >= _tiles.Count) return;
        (_tiles[_selected], _tiles[j]) = (_tiles[j], _tiles[_selected]);
        for (int i = 0; i < _tex.Length; i++)
        {
            if (_tex[i] == _selected) _tex[i] = j;
            else if (_tex[i] == j) _tex[i] = _selected;
        }
        _selected = j;
        RebuildCurrentPanel();
    }

    private void RemoveSelected()
    {
        ClearError();
        if (_selected < 0) { ShowError("请先在上方选择一个图块。"); return; }
        int used = UsageCount(_selected);
        if (used > 0)
        {
            ShowError($"图块 {_tiles[_selected]} 仍被 {used} 个角点使用，无法移除（重绘替换功能到 P1 笔刷再做）。");
            return;
        }
        if (_tiles.Count <= 1)
        {
            ShowError("至少要保留一个图块。");
            return;
        }
        _tiles.RemoveAt(_selected);
        for (int i = 0; i < _tex.Length; i++)
            if (_tex[i] > _selected)
                _tex[i]--;
        _selected = -1;
        RebuildCurrentPanel();
        RebuildAddPanel();
    }

    private void AddSelected()
    {
        ClearError();
        if (_selectedAdd == null) { ShowError("请先在下方选择一个要添加的图块。"); return; }
        if (_tiles.Contains(_selectedAdd)) { ShowError($"图块 {_selectedAdd} 已在使用中。"); return; }
        if (_tiles.Count >= _session.MaxGroundTextures)
        {
            ShowError($"图块集已满（上限 {_session.MaxGroundTextures} 个）。");
            return;
        }
        _tiles.Add(_selectedAdd);
        _selectedAdd = null;
        RebuildCurrentPanel();
        RebuildAddPanel();
    }

    private void Apply()
    {
        var map = _session.Map!;
        map.GroundTilesets.Clear();
        map.GroundTilesets.AddRange(_tiles);
        for (int i = 0; i < map.Corners.Length; i++)
            map.Corners[i].GroundTexture = _tex[i];
        _session.MarkDirty();
        Applied = true;
        Close();
    }

    // ---------- UI ----------

    private void RebuildCurrentPanel()
    {
        _currentPanel.Children.Clear();
        for (int i = 0; i < _tiles.Count; i++)
        {
            int index = i;
            var chip = Ui.TileChip(_tiles[i], 84, i == _selected);
            ToolTip.SetTip(chip, $"{_tiles[i]} · {UsageCount(i)} 个角点使用");
            chip.PointerPressed += (_, _) =>
            {
                _selected = index;
                ClearError();
                RebuildCurrentPanel();
            };
            _currentPanel.Children.Add(chip);
        }
    }

    private void RebuildAddPanel()
    {
        _addPanel.Children.Clear();
        foreach (var t in _catalog)
        {
            if (t.Tileset != _addTileset || _tiles.Contains(t.Id))
                continue;
            var chip = Ui.TileChip(t.Id, 72, t.Id == _selectedAdd);
            ToolTip.SetTip(chip, $"{t.Id} · {t.Comment}");
            string id = t.Id;
            chip.PointerPressed += (_, _) =>
            {
                _selectedAdd = id;
                ClearError();
                RebuildAddPanel();
            };
            _addPanel.Children.Add(chip);
        }
        if (_addPanel.Children.Count == 0 && _catalog.Count > 0)
            _addPanel.Children.Add(Ui.FieldLabel("该图块集下没有可添加的图块（均已在使用中）。"));
    }

    private Button OpButton(string text, Action action, bool danger = false)
    {
        var b = new Button
        {
            Content = text,
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        if (danger)
            b.Foreground = new SolidColorBrush(Ui.Danger);
        b.Click += (_, _) => action();
        return b;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    private void ClearError() => _error.IsVisible = false;
}
