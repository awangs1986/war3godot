using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Diablo1Like.W3.Terrain;
using W3Editor.Formats;
using W3Editor.Rendering;
using W3Editor.Services;

namespace W3Editor.Dialogs;

/// <summary>
/// New-map dialog (WE 新建地图流程第 1 步)。
/// 尺寸规则与 HiveWE / 官方 WE 完全一致：每边 32–480 格且为 4 的倍数。
/// 图块集从 TerrainArt/Terrain.slk + CliffTypes.slk 枚举（WE 调色板顺序），
/// 初始地表纹理可选（默认草地类）。
/// </summary>
public sealed class NewMapDialog : Window
{
    public bool Confirmed { get; private set; }
    public int WidthTiles { get; private set; } = 64;
    public int HeightTiles { get; private set; } = 64;
    public string MapName { get; private set; } = "新建地图";
    public NewMapSettings Settings { get; private set; } = NewMapSettings.LordaeronSummer;

    private readonly NumericUpDown _width;
    private readonly NumericUpDown _height;
    private readonly TextBox _name;
    private readonly TextBlock _hint;
    private readonly Button _ok;
    private readonly ComboBox _tileset;
    private readonly ComboBox _initialTile;
    private readonly ComboBox _cliffType;
    private readonly TextBlock _tilesetInfo;
    private readonly EditorAssets _assets;
    private List<GroundTileInfo> _tiles = new();
    private List<CliffTypeInfo> _cliffs = new();

    private static readonly int[] Presets = { 32, 64, 96, 128, 160, 192, 256, 320, 480 };

    public NewMapDialog(EditorAssets? assets = null)
    {
        Title = "新建地图";
        Width = 480;
        Ui.ApplyWindowChrome(this);
        _assets = assets ?? new EditorAssets();

        _name = new TextBox { Text = MapName, Watermark = "地图名（文件夹名）" };

        _width = MakeSizeBox();
        _height = MakeSizeBox();

        _tileset = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (char letter in MapSession.TilesetLetters)
            _tileset.Items.Add(new ComboBoxItem { Content = $"{MapSession.TilesetName(letter)} ({letter})", Tag = letter });
        _tileset.SelectedIndex = 0;
        _tileset.SelectionChanged += (_, _) => RefreshTilesetChoices();

        _initialTile = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _cliffType = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _tilesetInfo = Ui.FieldLabel("");
        _tilesetInfo.TextWrapping = TextWrapping.Wrap;

        _hint = Ui.FieldLabel("HiveWE/WE 规则：每边 32–480 格，且必须是 4 的倍数。");
        _hint.TextWrapping = TextWrapping.Wrap;

        var presetPanel = new WrapPanel();
        foreach (int size in Presets)
        {
            int s = size;
            var b = new Button
            {
                Content = $"{s}×{s}",
                FontSize = 12,
                Padding = new Avalonia.Thickness(10, 4),
                Margin = new Avalonia.Thickness(0, 0, 6, 6),
                CornerRadius = new Avalonia.CornerRadius(6),
            };
            b.Click += (_, _) =>
            {
                _width.Value = s;
                _height.Value = s;
            };
            presetPanel.Children.Add(b);
        }

        _ok = Ui.PrimaryButton("创建");
        _ok.Click += (_, _) => Confirm();
        var cancel = Ui.SecondaryButton("取消");
        cancel.Click += (_, _) => Close();

        var sizeGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };
        var wl = Ui.FieldLabel("宽度（格）");
        var hl = Ui.FieldLabel("高度（格）");
        Grid.SetColumn(wl, 0); Grid.SetRow(wl, 0);
        Grid.SetColumn(hl, 2); Grid.SetRow(hl, 0);
        Grid.SetColumn(_width, 0); Grid.SetRow(_width, 1);
        Grid.SetColumn(_height, 2); Grid.SetRow(_height, 1);
        sizeGrid.Children.Add(wl);
        sizeGrid.Children.Add(hl);
        sizeGrid.Children.Add(_width);
        sizeGrid.Children.Add(_height);

        var tileGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
        };
        var tsl = Ui.FieldLabel("图块集");
        var itl = Ui.FieldLabel("初始地表纹理");
        var ctl = Ui.FieldLabel("悬崖类型（笔刷默认）");
        Grid.SetColumn(tsl, 0); Grid.SetRow(tsl, 0);
        Grid.SetColumn(itl, 2); Grid.SetRow(itl, 0);
        Grid.SetColumn(_tileset, 0); Grid.SetRow(_tileset, 1);
        Grid.SetColumn(_initialTile, 2); Grid.SetRow(_initialTile, 1);
        Grid.SetColumn(ctl, 0); Grid.SetRow(ctl, 2);
        Grid.SetColumn(_cliffType, 0); Grid.SetRow(_cliffType, 3);
        tileGrid.Children.Add(tsl);
        tileGrid.Children.Add(itl);
        tileGrid.Children.Add(ctl);
        tileGrid.Children.Add(_tileset);
        tileGrid.Children.Add(_initialTile);
        tileGrid.Children.Add(_cliffType);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { _ok, cancel },
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18, 14),
            Spacing = 10,
            Children =
            {
                Ui.Section("地图名"),
                _name,
                Ui.Section("图块集（主题决定可用纹理与悬崖）"),
                tileGrid,
                _tilesetInfo,
                Ui.Section("地形尺寸"),
                sizeGrid,
                Ui.FieldLabel("常用尺寸："),
                presetPanel,
                _hint,
                buttons,
            },
        };

        _width.ValueChanged += (_, _) => Validate();
        _height.ValueChanged += (_, _) => Validate();
        RefreshTilesetChoices();
        Validate();
    }

    private char SelectedTileset =>
        _tileset.SelectedItem is ComboBoxItem { Tag: char letter } ? letter : 'L';

    private void RefreshTilesetChoices()
    {
        char tileset = SelectedTileset;
        _initialTile.Items.Clear();
        _cliffType.Items.Clear();
        if (!_assets.HasGameData)
        {
            // No game data on disk: fall back to the fixed Lordaeron Summer set.
            _tiles = new List<GroundTileInfo>();
            _cliffs = new List<CliffTypeInfo>();
            _initialTile.Items.Add(new ComboBoxItem { Content = "Lgrs — Grass（默认）" });
            _initialTile.SelectedIndex = 0;
            _initialTile.IsEnabled = false;
            _cliffType.Items.Add(new ComboBoxItem { Content = "CLdi — Dirt（默认）" });
            _cliffType.SelectedIndex = 0;
            _cliffType.IsEnabled = false;
            _tileset.IsEnabled = false;
            _tilesetInfo.Text = "未找到 war3 资产目录，使用固定的洛丹伦（夏）图块集。";
            return;
        }
        _tiles = _assets.GroundTilesFor(tileset);
        _cliffs = _assets.CliffTypesFor(tileset);
        foreach (GroundTileInfo tile in _tiles)
            _initialTile.Items.Add(new ComboBoxItem { Content = $"{tile.Id} — {tile.Comment}" });
        int defaultIndex = _tiles.FindIndex(t => t.Comment.Contains("Grass", StringComparison.OrdinalIgnoreCase));
        _initialTile.SelectedIndex = _tiles.Count > 0 ? Math.Max(0, defaultIndex) : -1;
        _initialTile.IsEnabled = _tiles.Count > 0;
        foreach (CliffTypeInfo cliff in _cliffs)
            _cliffType.Items.Add(new ComboBoxItem { Content = $"{cliff.Id} — {cliff.TextureFile}（地面 {cliff.GroundTile}）" });
        _cliffType.SelectedIndex = _cliffs.Count > 0 ? 0 : -1;
        _cliffType.IsEnabled = _cliffs.Count > 0;
        _tilesetInfo.Text = _tiles.Count > 0
            ? $"地面纹理 {_tiles.Count} 种：{string.Join(" ", _tiles.Select(t => t.Id))}\n悬崖类型 {_cliffs.Count} 种：{string.Join(" ", _cliffs.Select(c => c.Id))}"
            : "该图块集在 Terrain.slk 中没有条目。";
    }

    private static NumericUpDown MakeSizeBox() => new()
    {
        Minimum = W3EMap.MinTiles,
        Maximum = W3EMap.MaxTiles,
        Increment = W3EMap.TileStep,
        Value = 64,
        FormatString = "0",
    };

    private (int W, int H, bool Ok) Current()
    {
        int w = (int)(_width.Value ?? 0);
        int h = (int)(_height.Value ?? 0);
        return (w, h, W3EMap.IsValidNewSize(w) && W3EMap.IsValidNewSize(h));
    }

    private void Validate()
    {
        var (w, h, ok) = Current();
        _ok.IsEnabled = ok;
        if (ok)
        {
            _hint.Foreground = new SolidColorBrush(Ui.TextMuted);
            _hint.Text = $"地形 {w}×{h} 格（角点 {w + 1}×{h + 1}），中心偏移 {-w * 64}, {-h * 64}。";
        }
        else
        {
            _hint.Foreground = new SolidColorBrush(Ui.Danger);
            _hint.Text = $"非法尺寸 {w}×{h}：每边必须在 {W3EMap.MinTiles}–{W3EMap.MaxTiles} 之间且为 {W3EMap.TileStep} 的倍数（HiveWE/WE 规则）。";
        }
    }

    private void Confirm()
    {
        var (w, h, ok) = Current();
        if (!ok) return;
        WidthTiles = w;
        HeightTiles = h;
        MapName = string.IsNullOrWhiteSpace(_name.Text) ? "新建地图" : _name.Text.Trim();
        if (_assets.HasGameData && _tiles.Count > 0 && _cliffs.Count > 0)
        {
            // Selected cliff type goes first: cliff brushes write CliffTexture=0,
            // so list order decides which cliff skin a new map's cliffs use.
            var cliffIds = _cliffs.Select(c => c.Id).ToList();
            int cliffPick = Math.Clamp(_cliffType.SelectedIndex, 0, cliffIds.Count - 1);
            if (cliffPick > 0)
            {
                string picked = cliffIds[cliffPick];
                cliffIds.RemoveAt(cliffPick);
                cliffIds.Insert(0, picked);
            }
            Settings = new NewMapSettings
            {
                Tileset = SelectedTileset,
                GroundTilesets = _tiles.Select(t => t.Id).ToList(),
                CliffTilesets = cliffIds,
                InitialTextureIndex = Math.Max(0, _initialTile.SelectedIndex),
            };
        }
        else
        {
            Settings = NewMapSettings.LordaeronSummer;
        }
        Confirmed = true;
        Close();
    }
}
