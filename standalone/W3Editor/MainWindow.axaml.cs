using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using W3Editor.Dialogs;
using W3Editor.Contracts;
using W3Editor.Formats;
using W3Editor.Kernel;
using W3Editor.PluginContracts;
using W3Editor.Plugins;
using W3Editor.Rendering;
using W3Editor.Services;

namespace W3Editor;

public partial class MainWindow : Window
{
    private readonly MapSession _session = new();
    private readonly ModuleHost _moduleHost = new();
    private readonly Dictionary<string, (MenuItem Item, CommandDescriptor Command)> _moduleCommandItems = new(StringComparer.Ordinal);
    private ModuleLoadReport? _moduleLoadReport;
    /// <summary>Current map session — exposed for <see cref="EditorPluginContext"/>.</summary>
    internal MapSession Session => _session;
    private readonly BrushSettings _brush = new();
    private readonly EditHistory _history = new();
    private EditorAssets? _assets;
    private int _viewScale = 4;

    // 笔画状态（HiveWE：press apply(0.5)，move apply(frame_delta)）
    private BrushStroke? _stroke;
    private bool _strokeErase;
    private bool _strokeApplied;
    private (int X, int Y) _strokeAnchor = (-1, -1); // Shift 直线笔刷锚点
    private (float X, float Y) _rampStrokeAnchor = (-1, -1);
    private readonly Stopwatch _strokeClock = new();
    private long _lastApplyTimestamp;
    private bool _leftSidebarVisible = true;
    private readonly VisibilityOptions _visibility = VisibilityOptionsStore.Load();

    /// <summary>Shift line brush: constrain the stamp to the dominant axis from the stroke anchor.</summary>
    private (int X, int Y) ConstrainToLine(int cx, int cy, bool shift)
    {
        if (!shift || _strokeAnchor.X < 0)
            return (cx, cy);
        return Math.Abs(cx - _strokeAnchor.X) >= Math.Abs(cy - _strokeAnchor.Y)
            ? (cx, _strokeAnchor.Y)
            : (_strokeAnchor.X, cy);
    }

    private (float X, float Y) ConstrainRampToLine(float cx, float cy, bool shift)
    {
        if (!shift || _rampStrokeAnchor.X < 0) return (cx, cy);
        return MathF.Abs(cx - _rampStrokeAnchor.X) >= MathF.Abs(cy - _rampStrokeAnchor.Y)
            ? (cx, _rampStrokeAnchor.Y) : (_rampStrokeAnchor.X, cy);
    }

    public MainWindow()
    {
        InitializeComponent();
        // Panel descriptors must materialize their controls before the Shell
        // wires input events. This is composition, not business ownership.
        LoadEditorModules();
        Opened += OnWindowOpened;
        DoodadSearch.TextChanged += (_, _) => RefreshDoodadList();
        UnitSearch.TextChanged += (_, _) => RefreshUnitList();
        DoodadCategory.SelectionChanged += (_, _) => RefreshDoodadList();
        UnitRace.SelectionChanged += (_, _) => RefreshUnitList();
        DoodadList.SelectionChanged += OnDoodadPaletteSelected;
        UnitList.SelectionChanged += OnUnitPaletteSelected;
        foreach (CheckBox chk in new[] { ChkRandVariation, ChkRandRotation, ChkRandScale, ChkUnitRandRotation })
            chk.IsCheckedChanged += (_, _) => RerollGhost();
        SliderScatterSpacing.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                LblScatterSpacing.Text = $"{SliderScatterSpacing.Value:0.0} 格";
        };
        SliderScatterRadius.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                LblScatterRadius.Text = SliderScatterRadius.Value < 0.5 ? "关（沿轨迹）" : $"{SliderScatterRadius.Value:0} 格";
        };
        SliderPathingRadius.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                LblPathingRadius.Text = $"{(int)SliderPathingRadius.Value} 格";
        };

        SliderSize.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                _brush.Radius = (int)SliderSize.Value;
                LblSize.Text = _brush.Radius.ToString();
            }
        };
        SliderStrength.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                _brush.Strength = (float)(SliderStrength.Value / 100.0);
                LblStrength.Text = $"{(int)SliderStrength.Value}%";
            }
        };

        ViewportPanel.PointerPressed += OnViewportPressed;
        ViewportPanel.PointerMoved += OnViewportMoved;
        ViewportPanel.PointerReleased += OnViewportReleased;
        ViewportPanel.PointerWheelChanged += OnViewportWheel;
        ViewportPanel.PointerExited += (_, _) => BrushCursor.IsVisible = false;

        _history.Changed += UpdateUndoButtons;

        GlView.BrushStroke += OnGlBrushStroke;
        GlView.BrushStrokeEnded += OnGlBrushStrokeEnded;
        GlView.HoverPicked += OnGlHoverPicked;
        GlView.PixelHover += OnGlPixelHover;
        GlView.PlacePressed += OnPlacePressed;
        GlView.PlaceDragged += OnPlaceDragged;
        GlView.PlaceReleased += OnPlaceReleased;
        GlView.GlError += message =>
        {
            Stop3D();
            BtnView3D.IsChecked = false;
            StatusLeft.Text = "GL 渲染器初始化失败：" + message;
        };
        ApplyVisibilityOptions();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.Z: OnUndo(null, null!); e.Handled = true; return;
                case Key.Y: OnRedo(null, null!); e.Handled = true; return;
                case Key.S when HasModuleCommand("op.save"): OnSave(null, null!); e.Handled = true; return;
                case Key.O when HasModuleCommand("op.open"): OnOpenMap(null, null!); e.Handled = true; return;
                case Key.N when HasModuleCommand("op.newmap"): OnNewMap(null, null!); e.Handled = true; return;
            }
        }
        // Placement-mode keys (Space/Esc/Delete/Ctrl+C/V/A/+/-/V) come first.
        if (!e.Handled && HandlePlacementKey(e))
        {
            e.Handled = true;
            return;
        }
        // 3D camera keys (arrows / G / Home) work regardless of which control has focus.
        if (!e.Handled && GlView.IsVisible)
            GlView.ForwardKeyDown(e);
        // The 2D terrain canvas has no frame loop, so key repeat supplies
        // continuous WASD/arrow panning while a brush remains active.
        if (!e.Handled && !GlView.IsVisible && e.Source is not TextBox
            && e.Key is Key.W or Key.A or Key.S or Key.D or Key.Left or Key.Right or Key.Up or Key.Down)
        {
            const double step = 48.0;
            double dx = e.Key is Key.A or Key.Left ? step : e.Key is Key.D or Key.Right ? -step : 0.0;
            double dy = e.Key is Key.W or Key.Up ? step : e.Key is Key.S or Key.Down ? -step : 0.0;
            _pan2d = new Avalonia.Point(_pan2d.X + dx, _pan2d.Y + dy);
            RenderViewport();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (GlView.IsVisible)
            GlView.ForwardKeyUp(e);
        base.OnKeyUp(e);
    }

    // ---------- 新建 / 打开 ----------

    private async void OnNewMap(object? sender, RoutedEventArgs e)
    {
        _assets ??= new EditorAssets();
        var dlg = new NewMapDialog(_assets);
        await dlg.ShowDialog(this);
        if (!dlg.Confirmed)
            return;
        try
        {
            var folder = _session.CreateNew(dlg.MapName, dlg.WidthTiles, dlg.HeightTiles, dlg.Settings);
            OpenModuleDocumentFromSession();
            _history.Clear();
            GlView.Scene.ClearPluginPreview();
            RefreshFromSession();
            StatusLeft.Text = $"已新建 {dlg.WidthTiles}×{dlg.HeightTiles} 地图：{folder}";
            Refresh3DIfActive();
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, "新建地图失败", ex.Message);
        }
    }

    private async void OnOpenMap(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开地图 — d1map 的 map.json、.w3x/.w3m 或 .w3e",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("d1map 清单 (map.json)") { Patterns = new[] { "map.json" } },
                new FilePickerFileType("所有地图 (*.json;*.w3x;*.w3m;*.w3e)") { Patterns = new[] { "*.json", "*.w3x", "*.w3m", "*.w3e" } },
                new FilePickerFileType("W3 地图归档 (*.w3x;*.w3m)") { Patterns = new[] { "*.w3x", "*.w3m" } },
                new FilePickerFileType("W3 环境文件 (*.w3e)") { Patterns = new[] { "*.w3e" } },
            },
            SuggestedStartLocation = await TryGetMapsFolderAsync(),
        });
        if (files.Count == 0)
            return;
        var path = files[0].TryGetLocalPath();
        if (path == null)
            return;
        await OpenPath(path);
    }

    private async Task OpenPath(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".w3x" or ".w3m")
            {
                // Offline unpack (editor-side only); the runtime keeps eating folders.
                StatusLeft.Text = $"正在解包 {Path.GetFileName(path)} …";
                var (folder, files) = await Task.Run(() => W3xImporter.Extract(path));
                _session.Open(folder);
                StatusLeft.Text = $"已解包 {files} 个文件 → {folder}";
            }
            else if (Path.GetDirectoryName(path) is { } dir
                && Path.GetFileName(path).Equals("war3map.w3e", StringComparison.OrdinalIgnoreCase))
            {
                // Opening war3map.w3e inside an extracted folder == opening that folder.
                _session.Open(dir);
            }
            else
            {
                _session.Open(path);
            }
        }
        catch (Exception ex)
        {
            StatusLeft.Text = "打开失败：" + ex.Message;
            await MessageDialog.Show(this, "打开地图失败", ex.Message);
            return;
        }
        _history.Clear();
        OpenModuleDocumentFromSession();
        GlView.Scene.ClearPluginPreview();
        RefreshFromSession();
        Refresh3DIfActive();
    }

    private async Task<IStorageFolder?> TryGetMapsFolderAsync()
    {
        var root = GodotLauncher.FindProjectRoot();
        if (root == null) return null;
        var maps = Path.Combine(root, "war3", "maps");
        if (!Directory.Exists(maps)) return null;
        return await StorageProvider.TryGetFolderFromPathAsync(maps);
    }

    // ---------- 刷新 ----------

    private void RefreshFromSession()
    {
        var map = _session.Map;
        if (map == null) return;

        UpdateTitle();
        StatusLeft.Text = $"已打开：{(_session.IsNativeFormat ? _session.SourcePath : _session.W3ePath)}";
        MenuSave.Header = _session.IsNativeFormat ? "保存 d1map" : "保存 War3 地图";
        ToolTip.SetTip(BtnSave, _session.IsNativeFormat
            ? "保存 d1map"
            : "保存 w3e 与摆放文件（每次保存刷新 .bak 备份）");

        InfoSource.Text = _session.SourcePath;
        InfoVersion.Text = map.Version.ToString() + (map.Version >= 12 ? "（HiveWE/重制 8 字节角点）" : "（经典 7 字节角点）");
        InfoW3i.Text = _session.Info is { Valid: true } info
            ? (info.GameVersionMajor > 0
                ? $"w3i v{info.FormatVersion} · 游戏 {info.GameVersionMajor}.{info.GameVersionMinor}.{info.GameVersionPatch}"
                : $"w3i v{info.FormatVersion} · 经典版")
            : "无 w3i";
        InfoTileset.Text = _session.TilesetDisplayName;
        InfoSize.Text = $"{map.TileCountX} × {map.TileCountY}（角点 {map.Width} × {map.Height}）";
        InfoOffset.Text = $"{map.CenterOffsetX:0}, {map.CenterOffsetY:0}";
        InfoGroundTex.Text = string.Join(" ", map.GroundTilesets);
        InfoCliffTex.Text = string.Join(" ", map.CliffTilesets);
        InfoPathing.Text = _session.Pathing != null
            ? $"{_session.Pathing.Width} × {_session.Pathing.Height}（32 单位/格）"
            : "无 wpm";

        // 小地图（无覆盖层）
        MinimapImage.Source = MinimapRenderer.Render(map, 2);
        MinimapHint.IsVisible = false;

        _viewScale = Math.Clamp(1024 / Math.Max(map.Width, map.Height), 2, 8);
        Reset2DView();
        RenderViewport();
        ViewportHint.IsVisible = false;
        ViewportBadge.IsVisible = true;

        // 保存/另存为 d1map/3D 可用性
        BtnSave.IsEnabled = true;
        MenuSave.IsEnabled = true;
        BtnExport.IsEnabled = true;
        MenuExport.IsEnabled = true;
        BtnView3D.IsEnabled = true;

        // 笔刷控件可用性
        foreach (var ctl in BrushToggles())
            ctl.IsEnabled = true;
        SliderSize.IsEnabled = true;
        SliderStrength.IsEnabled = true;
        UpdateUndoButtons();

        // 覆盖层开关可用性（3D 摆放渲染默认开启）
        MenuOverlayPathing.IsEnabled = _session.Pathing != null;
        MenuOverlayDoodads.IsEnabled = true;
        MenuOverlayUnits.IsEnabled = true;
        MenuOverlayDoodads.IsChecked = true;
        MenuOverlayUnits.IsChecked = true;
        GlView.Scene.Placements.ShowDoodads = true;
        GlView.Scene.Placements.ShowUnits = true;
        ChkNoWalk.IsEnabled = _session.Pathing != null;
        ChkNoFly.IsEnabled = _session.Pathing != null;
        ChkNoBuild.IsEnabled = _session.Pathing != null;
        SliderPathingRadius.IsEnabled = !_session.IsNativeFormat;
        BtnModePathing.IsEnabled = !_session.IsNativeFormat;
        BtnModeRegions.IsEnabled = _session.FormatKind == Services.MapFormatKind.War3Folder;
        PathingHint.Text = _session.Pathing != null
            ? $"已加载 wpm：{_session.Pathing.Width}×{_session.Pathing.Height} 格。"
            : _session.IsNativeFormat
                ? "d1map 当前不承载 War3 WPM 数据。"
                : "该地图没有 war3map.wpm；首次绘制时自动创建。";
        RefreshRegionsUi();
        GlView.Scene.Regions = _session.Regions?.Regions is { } loadedRegions
            ? loadedRegions
            : Array.Empty<W3RegionRecord>();

        RebuildTexturePalette();

        // 图层计数 + 摆放调色板 + 选区复位
        UpdateLayerCounts();
        _selection.Clear();
        RefreshSelectionVisual();
        DisarmPlacement();
        PopulatePlacementPalettes();
    }

    private void UpdateTitle()
    {
        if (!_session.IsOpen)
        {
            Title = "war3godot · W3 编辑器 — 未打开地图";
            return;
        }
        Title = "war3godot · W3 编辑器 — " + _session.SourcePath + (_session.IsDirty ? " ●" : "");
    }

    private void RenderViewport()
    {
        var map = _session.Map;
        if (map == null) return;
        var overlays = new OverlayOptions
        {
            Pathing = _session.Pathing,
            ShowNoWalk = MenuOverlayPathing.IsChecked && ChkNoWalk.IsChecked == true,
            ShowNoFly = MenuOverlayPathing.IsChecked && ChkNoFly.IsChecked == true,
            ShowNoBuild = MenuOverlayPathing.IsChecked && ChkNoBuild.IsChecked == true,
            Doodads = _session.Doodads,
            Units = _session.Units,
            ShowDoodads = MenuOverlayDoodads.IsChecked,
            ShowUnits = MenuOverlayUnits.IsChecked,
            Regions = _session.Regions?.Regions,
            ShowRegions = _mode == EditMode.Regions && _visibility.Regions,
        };
        ViewportImage.Source = MinimapRenderer.Render(map, _viewScale, overlays);
        GlView.Scene.ShowRegions = _mode == EditMode.Regions && _visibility.Regions;
        GlView.Scene.Regions = _session.Regions?.Regions is { } regions
            ? regions
            : Array.Empty<W3RegionRecord>();

        var parts = new List<string>
        {
            $"俯视预览 · {map.TileCountX}×{map.TileCountY} · {_session.TilesetDisplayName}",
        };
        if (overlays.ShowDoodads) parts.Add($"装饰物 {_session.Doodads.Count}");
        if (overlays.ShowUnits) parts.Add($"单位 {_session.Units.Count}");
        if (overlays.ShowNoWalk || overlays.ShowNoFly || overlays.ShowNoBuild) parts.Add("寻路覆盖");
        if (overlays.ShowRegions) parts.Add($"区域 {_session.Regions?.Regions.Count ?? 0}");
        if (_brush.Op != BrushOp.None) parts.Add(BrushOpName(_brush.Op));
        ViewportBadgeText.Text = string.Join(" · ", parts);
    }

    private void OnOverlayToggled(object? sender, RoutedEventArgs e)
    {
        // 2D minimap overlay + 3D pass visibility share the same menu toggles.
        GlView.Scene.Placements.ShowDoodads = MenuOverlayDoodads.IsChecked;
        GlView.Scene.Placements.ShowUnits = MenuOverlayUnits.IsChecked;
        GlView.Scene.NotifyPlacementsChanged();
        RenderViewport();
    }

    // ---------- 笔刷 ----------

    private static string BrushOpName(BrushOp op) => op switch
    {
        BrushOp.Texture => "纹理刷",
        BrushOp.Raise => "高度抬升",
        BrushOp.Lower => "高度降低",
        BrushOp.Plateau => "高度整平",
        BrushOp.Smooth => "平滑",
        BrushOp.CliffLower2 => "悬崖降两层",
        BrushOp.CliffLower1 => "悬崖降一层",
        BrushOp.CliffLevel => "悬崖同层整平",
        BrushOp.CliffRaise1 => "悬崖升一层",
        BrushOp.CliffRaise2 => "悬崖升两层",
        BrushOp.WaterShallow => "浅水",
        BrushOp.WaterDeep => "深水",
        BrushOp.Ramp => "坡道",
        _ => "无笔刷",
    };

    private IEnumerable<ToggleButton> BrushToggles()
    {
        yield return BtnRaise;
        yield return BtnLower;
        yield return BtnPlateau;
        yield return BtnSmooth;
        yield return BtnCliffLower2;
        yield return BtnCliffLower1;
        yield return BtnCliffRaise1;
        yield return BtnCliffRaise2;
        yield return BtnCliffLevel;
        yield return BtnRamp;
        yield return BtnWaterShallow;
        yield return BtnWaterDeep;
    }

    private void OnBrushOpClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn) return;
        if (btn.IsChecked == true)
        {
            _brush.Op = Enum.Parse<BrushOp>((string)btn.Tag!);
            foreach (var other in BrushToggles())
                if (!ReferenceEquals(other, btn))
                    other.IsChecked = false;
        }
        else
        {
            _brush.Op = BrushOp.None;
        }
        RebuildTexturePalette();
        RenderViewport();
    }

    private void SelectTexture(int index)
    {
        _brush.Op = BrushOp.Texture;
        _brush.TextureIndex = index;
        foreach (var other in BrushToggles())
            other.IsChecked = false;
        RebuildTexturePalette();
        RenderViewport();
    }

    private void RebuildTexturePalette()
    {
        var map = _session.Map;
        TexturePalette.Items.Clear();
        if (map == null) return;
        for (int i = 0; i < map.GroundTilesets.Count; i++)
        {
            int index = i;
            var id = map.GroundTilesets[i];
            bool selected = _brush.Op == BrushOp.Texture && _brush.TextureIndex == i;
            var chip = Ui.TileChip(id, 58, selected);
            ToolTip.SetTip(chip, $"{id} — 点击选为纹理笔刷");
            chip.PointerPressed += (_, _) => SelectTexture(index);
            TexturePalette.Items.Add(chip);
        }
        TexturePaletteHint.Text = _brush.Op == BrushOp.Texture
            ? $"纹理笔刷：{map.GroundTilesets[Math.Clamp(_brush.TextureIndex, 0, map.GroundTilesets.Count - 1)]}（左键刷绘）"
            : $"共 {map.GroundTilesets.Count} 种地面纹理（点击选择）";
    }

    // ---------- 视口交互 ----------

    // P0: 2D 俯视缩放/平移（滚轮缩放到光标 · 中键拖拽平移 · 中键双击复位）。
    private double _zoom2d = 1.0;
    private Avalonia.Point _pan2d;
    private bool _panning2d;
    private Avalonia.Point _panLast;

    private void Apply2DTransform()
    {
        var group = new Avalonia.Media.TransformGroup();
        group.Children.Add(new Avalonia.Media.ScaleTransform(_zoom2d, _zoom2d));
        group.Children.Add(new Avalonia.Media.TranslateTransform(_pan2d.X, _pan2d.Y));
        ViewportImage.RenderTransform = group;
    }

    private void Reset2DView()
    {
        _zoom2d = 1.0;
        _pan2d = default;
        Apply2DTransform();
    }

    private void OnViewportWheel(object? sender, PointerWheelEventArgs e)
    {
        if (GlView.IsVisible)
        {
            // OpenGlControlBase is not hit-testable; route panel events into it.
            GlView.ForwardPointerWheel(e);
            return;
        }
        if (_session.Map == null)
            return;
        double factor = Math.Pow(1.18, e.Delta.Y);
        double newZoom = Math.Clamp(_zoom2d * factor, 0.25, 24.0);
        if (Math.Abs(newZoom - _zoom2d) < 0.0001)
            return;
        // Keep the map point under the cursor stationary (transform origin = center).
        var s = e.GetPosition(ViewportPanel);
        var c = new Avalonia.Point(ViewportPanel.Bounds.Width / 2, ViewportPanel.Bounds.Height / 2);
        var p = new Avalonia.Point((s.X - c.X - _pan2d.X) / _zoom2d, (s.Y - c.Y - _pan2d.Y) / _zoom2d);
        _zoom2d = newZoom;
        _pan2d = new Avalonia.Point(s.X - c.X - p.X * _zoom2d, s.Y - c.Y - p.Y * _zoom2d);
        Apply2DTransform();
        UpdateBrushCursor(e);
        e.Handled = true;
    }

    private bool TryPointToCorner(Avalonia.Point position, out int cx, out int cy)
    {
        cx = cy = -1;
        if (!TryPointToMapPixel(position, out double px, out double py)) return false;
        // Preserve the original double-precision cell selection for other tools.
        cx = (int)(px / _viewScale);
        cy = _session.Map!.Height - 1 - (int)(py / _viewScale);
        return cx >= 0 && cy >= 0 && cx < _session.Map.Width && cy < _session.Map.Height;
    }

    private bool TryPointToBrushPosition(Avalonia.Point position, out float cx, out float cy)
    {
        if (_brush.Op == BrushOp.Ramp) return TryPointToCorner(position, out cx, out cy);
        bool found = TryPointToCorner(position, out int x, out int y);
        cx = x; cy = y;
        return found;
    }

    private bool TryPointToCorner(Avalonia.Point position, out float cx, out float cy)
    {
        cx = cy = -1;
        if (!TryPointToMapPixel(position, out double px, out double py)) return false;
        cx = (float)(px / _viewScale);
        cy = (float)(_session.Map!.Height - 1 - py / _viewScale);
        return true;
    }

    private bool TryPointToMapPixel(Avalonia.Point posImage, out double px, out double py)
    {
        px = py = -1;
        var map = _session.Map;
        if (map == null || ViewportImage.Source is not WriteableBitmap bmp) return false;
        double bw = ViewportImage.Bounds.Width, bh = ViewportImage.Bounds.Height;
        double pw = bmp.PixelSize.Width, ph = bmp.PixelSize.Height;
        if (bw <= 0 || bh <= 0 || pw <= 0 || ph <= 0) return false;
        double fit = Math.Min(bw / pw, bh / ph);
        double offX = (bw - pw * fit) / 2, offY = (bh - ph * fit) / 2;
        px = (posImage.X - offX) / fit;
        py = (posImage.Y - offY) / fit;
        return px >= 0 && py >= 0 && px < pw && py < ph;
    }

    private double ViewFitScale()
    {
        if (ViewportImage.Source is not WriteableBitmap bmp) return 1;
        double bw = ViewportImage.Bounds.Width, bh = ViewportImage.Bounds.Height;
        if (bw <= 0 || bh <= 0) return 1;
        return Math.Min(bw / bmp.PixelSize.Width, bh / bmp.PixelSize.Height);
    }

    private void OnViewportPressed(object? sender, PointerPressedEventArgs e)
    {
        if (GlView.IsVisible)
        {
            GlView.ForwardPointerPressed(e);
            return;
        }
        var map = _session.Map;
        if (map == null) return;
        if (TryBeginWorldDataStroke(e))
            return;
        var props = e.GetCurrentPoint(ViewportPanel).Properties;
        if (props.IsMiddleButtonPressed)
        {
            if (e.ClickCount >= 2) { Reset2DView(); return; }
            _panning2d = true;
            _panLast = e.GetPosition(ViewportPanel);
            e.Pointer.Capture(ViewportPanel);
            return;
        }
        if (_brush.Op == BrushOp.None)
        {
            StatusLeft.Text = "未选择笔刷 — 先在右侧“地形”页选择高度/悬崖/水/坡道工具，或点击一个地面纹理";
            return;
        }
        if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed) return;
        if (!TryPointToBrushPosition(e.GetPosition(ViewportImage), out float cx, out float cy)) return;

        _strokeErase = props.IsRightButtonPressed;
        // Shift 语义（任务 6）：坡道笔刷下 Shift = **移除**（官方实测 R23：同组 ramp 旗 1→0）。
        // 因此坡道时 Shift 不再兼作"直线约束"（冲突已在此消解）；其它笔刷保持原有直线约束语义。
        {
            bool shiftDown = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _brush.Shift = shiftDown && _brush.Op == BrushOp.Ramp;
        }
        _stroke = new BrushStroke();
        _strokeApplied = false;
        _strokeAnchor = ((int)cx, (int)cy);
        _rampStrokeAnchor = (cx, cy);
        _history.BeginStroke(map);
        _strokeClock.Restart();
        _lastApplyTimestamp = 0;
        // HiveWE Brush::mouse_press_event → apply(0.5)
        ApplyBrushAt(cx, cy, frameDelta: 0.5f);
        e.Pointer.Capture(ViewportPanel);
    }

    private void OnViewportMoved(object? sender, PointerEventArgs e)
    {
        if (GlView.IsVisible)
        {
            GlView.ForwardPointerMoved(e);
            return;
        }
        if (_panning2d)
        {
            var pos = e.GetPosition(ViewportPanel);
            _pan2d = new Avalonia.Point(_pan2d.X + pos.X - _panLast.X, _pan2d.Y + pos.Y - _panLast.Y);
            _panLast = pos;
            Apply2DTransform();
            return;
        }
        UpdateBrushCursor(e);
        if (TryMoveWorldDataStroke(e))
            return;
        if (_stroke == null) return;
        if (!TryPointToBrushPosition(e.GetPosition(ViewportImage), out float cx, out float cy)) return;
        (cx, cy) = _brush.Op == BrushOp.Ramp
            ? (cx, cy)   // 坡道：Shift = 移除（官方 R23），此处不作直线约束
            : ConstrainToLine((int)cx, (int)cy, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        // HiveWE：每次 move 都 apply(frame_delta)，不按格插值
        ApplyBrushAt(cx, cy, ConsumeFrameDelta());
    }

    private void OnViewportReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (GlView.IsVisible)
        {
            GlView.ForwardPointerReleased(e);
            return;
        }
        if (_panning2d)
        {
            _panning2d = false;
            e.Pointer.Capture(null);
            return;
        }
        if (TryEndWorldDataStroke(e))
            return;
        if (_stroke == null) return;
        var map = _session.Map!;
        _history.CommitStroke(map);
        _brush.Shift = false;   // 笔画结束复位修饰键状态（避免"移除模式"粘住）
        _stroke = null;
        if (_strokeApplied)
        {
            _session.MarkDirty();
            MinimapImage.Source = MinimapRenderer.Render(map, 2);
            UpdateTitle();
            StatusLeft.Text = $"{BrushOpName(_brush.Op)} 完成（未保存 · Ctrl+S 保存）";
        }
        e.Pointer.Capture(null);
    }

    private float ConsumeFrameDelta()
    {
        long now = _strokeClock.ElapsedMilliseconds;
        float dt = Math.Clamp((now - _lastApplyTimestamp) / 1000f, 1f / 240f, 0.1f);
        _lastApplyTimestamp = now;
        return dt;
    }

    private void ApplyBrushAt(float cx, float cy, float frameDelta)
    {
        var map = _session.Map;
        if (map == null || _stroke == null) return;
        TerrainBrush.Apply(map, cx, cy, _brush, _strokeErase, _stroke, frameDelta);
        _strokeApplied = true;
        // 任务 7：刷新范围取**实际改动范围**（含传播/坡度影响的角点），而不是固定的 Radius+6 矩形。
        // 优先用撤销记录里"笔画进行中的实际改动范围"；没有改动时退回笔刷足迹（保持预览一致）。
        var actual = _history.PendingAffectedRegion(map);
        int reach = _brush.Radius + 6;
        int rx0 = (int)MathF.Floor(cx) - reach, ry0 = (int)MathF.Floor(cy) - reach;
        int rx1 = (int)MathF.Ceiling(cx) + reach + 1, ry1 = (int)MathF.Ceiling(cy) + reach + 1;
        if (actual is { } a)
        {
            rx0 = Math.Min(rx0, a.X0); ry0 = Math.Min(ry0, a.Y0);
            rx1 = Math.Max(rx1, a.X1); ry1 = Math.Max(ry1, a.Y1);
        }
        GlView.Scene.NotifyCornersChanged(rx0, ry0, rx1, ry1);
        if (!GlView.IsVisible)
            RenderViewport();
    }

    private void UpdateBrushCursor(PointerEventArgs e)
    {
        var map = _session.Map;
        bool active = map != null
            && (_mode is EditMode.Pathing or EditMode.Regions || _brush.Op != BrushOp.None)
            && TryPointToCorner(e.GetPosition(ViewportImage), out int cx, out int cy);
        BrushCursor.IsVisible = active && _visibility.Brush;
        if (!active)
        {
            if (map != null && !GlView.IsVisible)
                StatusRight.Text = "视口：2D 俯视";
            return;
        }
        TryPointToCorner(e.GetPosition(ViewportImage), out int hx, out int hy);
        var c = map!.GetCorner(hx, hy);
        if (c != null)
            StatusRight.Text = $"角点 {hx},{hy} · 高度 {c.GroundHeight:0.00} · 层 {c.LayerHeight}"
                + (c.Water ? " · 水" : "") + (c.Ramp ? " · 坡道" : "");

        double fit = ViewFitScale() * _zoom2d;
        // Terrain radius is in corners; pathing radius is in 32-unit cells.
        double d = _mode switch
        {
            EditMode.Pathing => Math.Max(SliderPathingRadius.Value * 2 + 1, 1) * _viewScale * fit / 4.0,
            EditMode.Regions => Math.Max(_viewScale * fit / 2.0, 6),
            _ => Math.Max(_brush.Radius, 1) * _viewScale * fit,
        };
        var pos = e.GetPosition(ViewportPanel);
        BrushCursor.Width = d;
        BrushCursor.Height = d;
        Canvas.SetLeft(BrushCursor, pos.X - d / 2);
        Canvas.SetTop(BrushCursor, pos.Y - d / 2);
    }

    // ---------- 撤销 / 重做 ----------

    private void UpdateUndoButtons()
    {
        bool open = _session.IsOpen;
        BtnUndo.IsEnabled = open && _history.CanUndo;
        MenuUndo.IsEnabled = open && _history.CanUndo;
        BtnRedo.IsEnabled = open && _history.CanRedo;
        MenuRedo.IsEnabled = open && _history.CanRedo;
    }

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        var map = _session.Map;
        if (map == null || !_history.Undo(_session)) return;
        _session.MarkDirty();
        AfterMapMutation("已撤销", _history.LastAffectedRegion(map));
    }

    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        var map = _session.Map;
        if (map == null || !_history.Redo(_session)) return;
        _session.MarkDirty();
        AfterMapMutation("已重做", _history.LastAffectedRegion(map));
    }

    private void AfterMapMutation(string what, (int X0, int Y0, int X1, int Y1)? region = null)
    {
        var map = _session.Map!;
        RenderViewport();
        MinimapImage.Source = MinimapRenderer.Render(map, 2);
        RebuildTexturePalette();
        UpdateTitle();
        if (_history.LastTouchedPlacements)
        {
            GlView.Scene.NotifyPlacementsChanged();
            PruneSelection();
            UpdateLayerCounts();
        }
        if (_history.LastTouchedPathing)
            _session.MarkPathingDirty();
        if (_history.LastTouchedRegions)
        {
            _session.MarkRegionsDirty();
            RefreshRegionsUi();
        }
        if (region is { } r)
            GlView.Scene.NotifyCornersChanged(r.X0, r.Y0, r.X1, r.Y1);
        else if (!_history.LastTouchedPlacements && !_history.LastTouchedPathing && !_history.LastTouchedRegions)
            GlView.Scene.NotifyMapReplaced();
        StatusLeft.Text = what;
    }

    // ---------- 保存 / 导出 ----------

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!_session.IsOpen) return;
        try
        {
            SaveCurrentDocument();
            StatusLeft.Text = _session.IsNativeFormat
                ? $"已保存 d1map：{_session.SourcePath}"
                : _session.IsFolder
                    ? $"已保存 w3e + doo：{_session.SourcePath}（原文件备份为 .bak）"
                    : $"已保存：{_session.W3ePath}（原文件备份为 .bak）";
            UpdateTitle();
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, "保存失败", ex.Message);
        }
    }

    private async void OnSaveAsNative(object? sender, RoutedEventArgs e)
    {
        if (!_session.IsOpen) return;
        var mapName = Path.GetFileName(_session.SourcePath.TrimEnd('\\', '/'));
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 d1map 输出文件夹",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        string? folder = folders[0].TryGetLocalPath();
        if (folder == null) return;

        StatusLeft.Text = "正在写入 d1map …";
        BtnExport.IsEnabled = false;
        try
        {
            _session.SaveAsNative(folder, mapName);
            OpenModuleDocumentFromSession();
            GlView.Scene.ClearPluginPreview();
            RefreshFromSession();
            Refresh3DIfActive();
            StatusLeft.Text = $"已切换到 d1map：{folder}";
            UpdateTitle();
            await MessageDialog.Show(this, "另存完成",
                $"当前会话已切换为 d1map：\n{folder}");
        }
        catch (Exception ex)
        {
            StatusLeft.Text = "另存失败";
            await MessageDialog.Show(this, "另存 d1map 失败", ex.Message);
        }
        finally
        {
            BtnExport.IsEnabled = _session.IsOpen;
        }
    }

    // ---------- 3D 视口（内置 Silk.NET GL 渲染器） ----------

    private void OnView3DToggled(object? sender, RoutedEventArgs e)
    {
        if (BtnView3D.IsChecked == true)
            Start3D();
        else
            Stop3D();
    }

    private void Start3D()
    {
        var map = _session.Map;
        if (map == null)
        {
            BtnView3D.IsChecked = false;
            return;
        }
        _assets ??= new EditorAssets();
        _assets.MapDir = _session.IsFolder ? _session.SourcePath : "";
        GlView.Scene.SetMap(map, _assets);
        EnsureCatalog();
        GlView.Scene.SetPlacements(_session.Doodads, _session.Units, _catalog!, _assets);
        GlView.Scene.Placements.ShowDoodads = MenuOverlayDoodads.IsChecked;
        GlView.Scene.Placements.ShowUnits = MenuOverlayUnits.IsChecked;
        GlView.IsVisible = true;
        ViewportImage.IsVisible = false;
        BrushCursor.IsVisible = false;
        StatusRight.Text = "视口：3D（内置 GL）";
        StatusLeft.Text = _assets.HasGameData
            ? "3D 视口就绪 — 右键平移 · 中键/Ctrl+右键旋转 · 滚轮缩放 · 方向键平移 · G 网格 · Home 复位 · 左键刷地形（Ctrl+左键反向）"
            : "3D 视口就绪（未找到 war3 资产目录，纹理用占位棋盘格）";
    }

    private void Stop3D()
    {
        GlView.IsVisible = false;
        GlView.Scene.BrushRing = null;
        ViewportImage.IsVisible = true;
        StatusRight.Text = "视口：2D 俯视";
        if (_session.IsOpen)
            RenderViewport();
    }

    /// <summary>Map identity changed (new / open): hand the new map to the GL scene.</summary>
    private void Refresh3DIfActive()
    {
        if (BtnView3D.IsChecked == true && _session.Map != null)
            Start3D();
    }

    // 3D 内直接刷地形：GlView 拾取到角点坐标后走与 2D 相同的笔画管线。
    private void OnGlBrushStroke(float cornerX, float cornerY, bool erase)
    {
        var map = _session.Map;
        if (map == null) return;
        if (_mode == EditMode.Pathing)
        {
            ApplyPathingAtCorner(cornerX, cornerY, erase);
            return;
        }
        if (_mode == EditMode.Regions)
        {
            ApplyRegionAtCorner(cornerX, cornerY);
            return;
        }
        if (_brush.Op == BrushOp.None)
        {
            StatusLeft.Text = "未选择笔刷 — 先在右侧“地形”页选择工具，或点击一个地面纹理";
            return;
        }
        float cx = Math.Clamp(_brush.Op == BrushOp.Ramp ? cornerX : MathF.Round(cornerX), 0, map.Width - 1);
        float cy = Math.Clamp(_brush.Op == BrushOp.Ramp ? cornerY : MathF.Round(cornerY), 0, map.Height - 1);
        if (_stroke == null)
        {
            _strokeErase = erase;
            _stroke = new BrushStroke();
            _strokeApplied = false;
            _strokeAnchor = ((int)cx, (int)cy);
            _rampStrokeAnchor = (cx, cy);
            _history.BeginStroke(map);
            _strokeClock.Restart();
            _lastApplyTimestamp = 0;
            ApplyBrushAt(cx, cy, frameDelta: 0.5f);
            return;
        }
        (cx, cy) = _brush.Op == BrushOp.Ramp
            ? (cx, cy)   // 坡道：Shift = 移除，不作直线约束
            : ConstrainToLine((int)cx, (int)cy, GlView.StrokeShift);
        ApplyBrushAt(cx, cy, ConsumeFrameDelta());
    }

    private void OnGlBrushStrokeEnded()
    {
        if (_mode == EditMode.Pathing && EndPathingStroke())
            return;
        if (_mode == EditMode.Regions && EndRegionStroke())
            return;
        if (_stroke == null) return;
        var map = _session.Map!;
        _history.CommitStroke(map);
        _brush.Shift = false;   // 笔画结束复位修饰键状态（避免"移除模式"粘住）
        _stroke = null;
        if (_strokeApplied)
        {
            _session.MarkDirty();
            MinimapImage.Source = MinimapRenderer.Render(map, 2);
            UpdateTitle();
            StatusLeft.Text = $"{BrushOpName(_brush.Op)} 完成（未保存 · Ctrl+S 保存）";
        }
    }

    private void OnGlHoverPicked(float cornerX, float cornerY)
    {
        var map = _session.Map;
        if (map == null) return;
        int hx = Math.Clamp((int)MathF.Round(cornerX), 0, map.Width - 1);
        int hy = Math.Clamp((int)MathF.Round(cornerY), 0, map.Height - 1);
        var c = map.GetCorner(hx, hy);
        if (c != null)
            StatusRight.Text = $"3D · 角点 {hx},{hy} · 高度 {c.GroundHeight:0.00} · 层 {c.LayerHeight}"
                + (c.Water ? " · 水" : "") + (c.Ramp ? " · 坡道" : "");
        GlView.Scene.BrushRing = _mode switch
        {
            EditMode.Pathing => (cornerX, cornerY, Math.Max((float)SliderPathingRadius.Value / 4f, 0.25f)),
            EditMode.Regions => (cornerX, cornerY, 0.25f),
            _ when _brush.Op != BrushOp.None => (cornerX, cornerY, Math.Max(_brush.Radius * 0.5f, 0.5f)),
            _ => null,
        };
    }

    // ---------- 预览 / 对话框 ----------

    private async void OnGodotPreview(object? sender, RoutedEventArgs e)
    {
        if (!_session.IsOpen)
        {
            await MessageDialog.Show(this, "在 Godot 中预览", "请先打开一张地图。");
            return;
        }
        try
        {
            string previewPath;
            if (_session.IsNativeFormat)
            {
                if (_session.IsDirty)
                    SaveCurrentDocument();
                previewPath = _session.SourcePath;
            }
            else
            {
                previewPath = Path.Combine(
                    EditorPaths.OutDir,
                    "preview",
                    $"editor-{Environment.ProcessId}");
                _session.SaveNativeCopy(previewPath, "Editor Preview");
            }
            GodotLauncher.LaunchPreview(previewPath);
            StatusLeft.Text = $"已用 d1map 启动 Godot 预览：{previewPath}";
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, "启动 Godot 失败", ex.Message);
        }
    }

    private async void OnTileSetter(object? sender, RoutedEventArgs e)
    {
        if (!_session.IsOpen)
        {
            await MessageDialog.Show(this, "更换图块集", "请先打开一张地图。");
            return;
        }
        var dlg = new TileSetterDialog(_session);
        await dlg.ShowDialog(this);
        if (dlg.Applied)
        {
            _history.Clear();
            _brush.TextureIndex = 0;
            RefreshFromSession();
            StatusLeft.Text = "图块集已修改（未保存）— 记得保存 .w3e";
        }
    }

    private async void OnTilePathing(object? sender, RoutedEventArgs e)
    {
        await new TilePathingDialog(_session.Map).ShowDialog(this);
    }

    private async void OnMapInfo(object? sender, RoutedEventArgs e)
    {
        if (!_session.IsFolder || _session.MapIdentity == null)
        {
            await MessageDialog.Show(this, "地图信息", "当前地图没有可编辑的 war3map.w3i。");
            return;
        }
        var dialog = new MapInfoDialog(_session.MapIdentity);
        await dialog.ShowDialog(this);
        if (!dialog.Applied)
            return;
        _session.UpdateMapIdentity(_session.MapIdentity);
        StatusLeft.Text = "地图信息已修改（未保存）";
        UpdateTitle();
    }

    private async void OnImportHeightmap(object? sender, RoutedEventArgs e)
    {
        W3EMap? map = _session.Map;
        if (map == null)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入高度图（红通道，HiveWE -16..+16）",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("高度图") { Patterns = new[] { "*.png", "*.bmp", "*.jpg", "*.jpeg", "*.tga" } },
            },
        });
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path == null)
            return;
        try
        {
            using SkiaSharp.SKBitmap bitmap = SkiaSharp.SKBitmap.Decode(path)
                ?? throw new InvalidDataException("无法解码图像。");
            HiveWeHeightmapImport.Apply(
                map,
                bitmap.Width,
                bitmap.Height,
                (x, y) => bitmap.GetPixel(x, y).Red);
            _history.Clear();
            _session.MarkDirty();
            RefreshFromSession();
            GlView.Scene.NotifyMapReplaced();
            StatusLeft.Text = $"已按 HiveWE 规则导入高度图：{Path.GetFileName(path)}（未保存）";
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, "导入高度图失败", ex.Message);
        }
    }

    private async void OnResizeMap(object? sender, RoutedEventArgs e)
    {
        W3EMap? map = _session.Map;
        if (map == null) return;
        var dialog = new ResizeMapDialog(map.TileCountX, map.TileCountY);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;
        try
        {
            _session.Resize(dialog.WidthTiles, dialog.HeightTiles);
            _history.Clear();
            _selection.Clear();
            OpenModuleDocumentFromSession();
            RefreshFromSession();
            GlView.Scene.NotifyMapReplaced();
            StatusLeft.Text = $"已按 HiveWE 四边规则调整为 {dialog.WidthTiles}×{dialog.HeightTiles}（未保存，撤销历史已清空）";
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, "调整地图失败", ex.Message);
        }
    }

    private async void ShowModuleInformation(string title, string message) =>
        await MessageDialog.Show(this, title, message);

    private void RunCopyPasteModule()
    {
        if (_selection.Count > 0)
        {
            _clipboard = _selection.Select(item => item.Clone()).ToList();
            StatusLeft.Text = $"已复制 {_clipboard.Count} 个摆放；再执行一次可粘贴到当前指针。";
        }
        else if (_clipboard.Count > 0)
        {
            PasteClipboard();
        }
        else
        {
            StatusLeft.Text = "请先在 3D 视口选中单位或装饰物。";
        }
    }

    private void ShowUnusedFilesReport()
    {
        IReadOnlyList<UnknownPart> files = _moduleHost.Document?.UnknownParts ?? Array.Empty<UnknownPart>();
        string detail = files.Count == 0
            ? "当前文档没有未被模块认领的文件。"
            : $"发现 {files.Count} 个未认领文件（保存时仍会逐字节保留）：\n" +
              string.Join("\n", files.Select(file => $"{file.FileName}  ({file.Bytes.Length} bytes)"));
        ShowModuleInformation("未使用/未认领文件", detail);
    }

    private async void ShowAssetStatus()
    {
        _assets ??= new EditorAssets();
        await new AssetManagerDialog(_assets).ShowDialog(this);
    }

    private async void ShowVisibilityDialog()
    {
        ApplyVisibilityOptions();
        await new VisibilityDialog(_visibility, ApplyVisibilityOptions).ShowDialog(this);
    }

    private async void ShowSettingsDialog() =>
        await new SettingsDialog(_visibility, ApplyVisibilityOptions).ShowDialog(this);

    private void ApplyVisibilityOptions()
    {
        MenuOverlayUnits.IsChecked = _visibility.Units;
        MenuOverlayDoodads.IsChecked = _visibility.Doodads;
        MenuOverlayPathing.IsChecked = _visibility.Pathing;
        GlView.Scene.Placements.ShowUnits = _visibility.Units;
        GlView.Scene.Placements.ShowDoodads = _visibility.Doodads;
        GlView.Scene.ShowBrush = _visibility.Brush;
        GlView.Scene.LightingEnabled = _visibility.Lighting;
        GlView.Scene.ShowWater = _visibility.Water;
        GlView.Scene.ShowClickHelpers = _visibility.ClickHelpers;
        GlView.Scene.ShowGrid = _visibility.Wireframe;
        ViewportBadge.IsVisible = _visibility.Debug;
        GlView.Scene.NotifyPlacementsChanged();
        if (_session.IsOpen) RenderViewport();
        GlView.RequestRender();
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        await MessageDialog.Show(this, "关于 war3godot · W3 编辑器",
            "war3godot · W3 编辑器 — Godot 上的 Warcraft III 地形与地图制作工具（README.md）\n\n" +
            "已实现：直接打开/保存 d1map；导入 .w3x/.w3m（离线解包）/ 地图目录 / .w3e、地图信息（w3e/w3i/wpm）、\n" +
            "俯视小地图、2D 缩放平移、地形笔刷（HiveWE 全工具集：纹理 / 高度抬升·降低·整平·平滑 /\n" +
            "悬崖升降 1–2 层·同层整平·坡道 / 浅水·深水，2D/3D 视口均可刷）、\n" +
            "角点级撤销重做、装饰物与单位统计（doo）、寻路覆盖层（wpm）、\n" +
            "更换图块集（v12 最多 64 图块）、保存 .w3e（自动备份）、另存为 d1map、\n" +
            "内置 GL 3D 视口（Silk.NET，与 Godot 共享 W3TerrainCore 算法核心）、独立预览。\n\n" +
            "项目方向：地形编辑与运行时一致性、模型动画、材质光照与 24 小时日夜变化；具体玩法待定。\n" +
            "资源基线：Warcraft III: Reforged 1.32.10.18820（war3/README.md）。\n" +
            "项目仓库：https://github.com/awangs1986/war3godot");
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    // ---------- 自检 ----------

    /// <summary>
    /// Dev self-check: W3Editor --open &lt;path&gt; [--brushtest] [--shot &lt;png&gt;] [--shot3d &lt;png&gt;]
    /// opens a map (archives auto-unpack), optionally runs the brush/undo regression,
    /// saves screenshots (2D and/or embedded-3D), exits.
    /// </summary>
    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        StartControlServer();
        if (HasModuleCommand("host.plugin"))
            LoadPlugins();
        var args = Environment.GetCommandLineArgs();
        string? openPath = null, shotPath = null, shot3dPath = null, newSpec = null, brushOp = null;
        bool brushTest = args.Contains("--brushtest");
        bool paint = args.Contains("--paint");
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--open") openPath = args[i + 1];
            if (args[i] == "--shot") shotPath = args[i + 1];
            if (args[i] == "--shot3d") shot3dPath = args[i + 1];
            if (args[i] == "--new") newSpec = args[i + 1];
            if (args[i] == "--brushop") brushOp = args[i + 1];
        }
        if (openPath == null && newSpec == null)
            return;
        try
        {
            if (newSpec != null)
            {
                // --new WxH[xT]: T = tileset letter (e.g. A = Ashenvale). Uses the
                // same SLK-driven settings the NewMapDialog builds, so the palette
                // acceptance test exercises the production data path.
                var dims = newSpec.Split('x', 'X');
                NewMapSettings? settings = null;
                if (dims.Length >= 3 && dims[2].Length == 1)
                {
                    char tsLetter = char.ToUpperInvariant(dims[2][0]);
                    _assets ??= new EditorAssets();
                    var tiles = _assets.GroundTilesFor(tsLetter);
                    var cliffs = _assets.CliffTypesFor(tsLetter);
                    if (tiles.Count > 0 && cliffs.Count > 0)
                    {
                        int grassIdx = tiles.FindIndex(t => t.Comment.Contains("Grass", StringComparison.OrdinalIgnoreCase));
                        settings = new NewMapSettings
                        {
                            Tileset = tsLetter,
                            GroundTilesets = tiles.Select(t => t.Id).ToList(),
                            CliffTilesets = cliffs.Select(c => c.Id).ToList(),
                            InitialTextureIndex = Math.Max(0, grassIdx),
                        };
                    }
                }
                var folder = _session.CreateNew("自检新图", int.Parse(dims[0]), int.Parse(dims[1]), settings);
                StatusLeft.Text = $"已新建：{folder}";
            }
            else
            {
                var ext = Path.GetExtension(openPath!).ToLowerInvariant();
                if (ext is ".w3x" or ".w3m")
                {
                    var (folder, files) = await Task.Run(() => W3xImporter.Extract(openPath!));
                    StatusLeft.Text = $"已解包 {files} 个文件 → {folder}";
                    _session.Open(folder);
                }
                else
                {
                    _session.Open(openPath!);
                }
            }
            OpenModuleDocumentFromSession();
            _history.Clear();
            GlView.Scene.ClearPluginPreview();
            RefreshFromSession();
            string? objectShot = null;
            string? moduleWindowId = null;
            string? moduleWindowShot = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--object-shot") objectShot = args[i + 1];
                if (args[i] == "--window-shot" && i + 2 < args.Length)
                {
                    moduleWindowId = args[i + 1];
                    moduleWindowShot = args[i + 2];
                }
            }
            if (objectShot != null)
            {
                await _moduleHost.Capabilities.Get<IWindowService>()!.OpenAsync("window.object-editor");
                await Task.Delay(1200);
                Window? objectWindow = (Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?
                    .Windows.FirstOrDefault(window => window != this && window.Title?.Contains("对象编辑器") == true);
                bool captured = objectWindow != null && OperatingSystem.IsWindows()
                    && NativeCapture.CaptureWindow(objectWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, objectShot);
                File.WriteAllText(Path.ChangeExtension(objectShot, ".txt"), captured ? "OK: object editor captured" : "FAIL: object editor capture");
                objectWindow?.Close();
                Close();
                return;
            }
            if (moduleWindowId != null && moduleWindowShot != null)
            {
                IWindowService? windowService = _moduleHost.Capabilities.Get<IWindowService>();
                bool opened = windowService?.CanOpen(moduleWindowId) == true;
                if (opened)
                    await windowService!.OpenAsync(moduleWindowId);
                await Task.Delay(1200);
                Window? moduleWindow = (Application.Current?.ApplicationLifetime as
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?
                    .Windows.LastOrDefault(window => window != this);
                bool captured = opened && moduleWindow != null && OperatingSystem.IsWindows()
                    && NativeCapture.CaptureWindow(moduleWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, moduleWindowShot);
                File.WriteAllText(Path.ChangeExtension(moduleWindowShot, ".txt"), captured
                    ? $"OK: {moduleWindowId} captured"
                    : $"FAIL: {moduleWindowId} open={opened} window={moduleWindow != null}");
                moduleWindow?.Close();
                Close();
                return;
            }
            if (brushOp != null && Enum.TryParse<BrushOp>(brushOp, out var preOp))
            {
                // Pointer-path self test: preselect a tool exactly like a toggle click.
                _brush.Op = preOp;
                foreach (var t in BrushToggles())
                    t.IsChecked = (string)t.Tag! == brushOp;
                RenderViewport();
            }
            if (paint && _session.Map != null)
            {
                RunNewMapPaint();
                SaveCurrentDocument();
            }
        }
        catch (Exception ex)
        {
            StatusLeft.Text = "打开失败：" + ex.Message;
        }
        // --view3d [--camlog <txt>]: stay open in 3D and periodically dump camera
        // state — lets an external SendInput script verify real mouse input.
        if (args.Contains("--view3d") && _session.IsOpen)
        {
            BtnView3D.IsChecked = true;
            Start3D();
            string? camLog = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--camlog") camLog = args[i + 1];
            if (camLog != null)
            {
                var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                timer.Tick += (_, _) =>
                {
                    var cam = GlView.Scene.Camera;
                    try
                    {
                        var winPos = this.Position;
                        var glTopLeft = Avalonia.VisualExtensions.PointToScreen(GlView, new Avalonia.Point(0, 0));
                        File.WriteAllText(camLog,
                            $"yaw={cam.Yaw:F4} pitch={cam.Pitch:F4} dist={cam.Distance:F1} " +
                            $"target={cam.Target.X:F1},{cam.Target.Z:F1} grid={GlView.Scene.ShowGrid} " +
                            $"press={GlView.PointerPressCount} move={GlView.PointerMoveCount} " +
                            $"glrect={glTopLeft.X},{glTopLeft.Y},{GlView.Bounds.Width:F0},{GlView.Bounds.Height:F0} win={winPos.X},{winPos.Y}");
                    }
                    catch { /* transient share violation with the reader */ }
                };
                timer.Start();
            }
        }
        // --shotwin <png> [--tab terrain|doodads|units|pathing|regions]: full-window PrintWindow
        // capture (palette/UI evidence; the GL viewport is included).
        string? shotWinPath = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--shotwin") shotWinPath = args[i + 1];
        if (shotWinPath != null && _session.IsOpen)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "--tab") continue;
                SetMode(ParseMode(args[i + 1] switch
                {
                    "doodads" => "Doodads",
                    "units" => "Units",
                    "pathing" => "Pathing",
                    "regions" => "Regions",
                    _ => "Terrain",
                }));
            }
            await Task.Delay(4500); // decode atlases + palette icons + first frames
            var hwndWin = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            bool okWin = OperatingSystem.IsWindows() && NativeCapture.CaptureWindow(hwndWin, shotWinPath);
            File.WriteAllText(Path.ChangeExtension(shotWinPath, ".txt"), okWin ? "OK: window captured" : "FAIL: PrintWindow");
            if (shotPath == null && shot3dPath == null)
            {
                Close();
                return;
            }
        }

        if (shot3dPath != null && _session.IsOpen)
        {
            BtnView3D.IsChecked = true;
            Start3D();
            await Task.Delay(4000); // decode atlases + first GL frames
            var log = new List<string> { GlView.Scene.StatusLine, StatusLeft.Text ?? "" };
            if (args.Contains("--topdown"))
            {
                // Straight-down view of the map center for pixel comparison with
                // the CPU tile composite (--probe-tile-composite).
                var cam = GlView.Scene.Camera;
                cam.Target = new System.Numerics.Vector3(0f, 0f, 0f);
                cam.Yaw = MathF.PI;
                cam.Pitch = MathF.PI / 2f - 0.001f;
                cam.Distance = 2600f;
                await Task.Delay(400);
            }
            if (args.Contains("--camtest"))
            {
                // Exercise the camera ops: zoom in hard, rotate far below the
                // horizon (must be clamped back above ground), pan.
                var cam = GlView.Scene.Camera;
                cam.Zoom(28f);                 // 28 notches in ≈ distance * 0.999^3360
                cam.Rotate(220f, -900f);       // pitch would go to ≈ -1.27 rad without the ground clamp
                cam.Pan(-120f, 60f);           // right-drag equivalent
                await Task.Delay(400);         // let a frame run the clamp
                log.Add($"cam: dist={cam.Distance:F0} yaw={cam.Yaw:F3} pitch={cam.Pitch:F3} eyeY={cam.Position.Y:F0} target=({cam.Target.X:F0},{cam.Target.Z:F0})");
            }
            if (args.Contains("--paint3d"))
                log.AddRange(RunBrush3DSelfTest());
            if (args.Contains("--placetest"))
                log.AddRange(RunPlacementSelfTest());
            if (args.Contains("--foresttest"))
                log.AddRange(RunForestSelfTest());
            await Task.Delay(800); // let the incremental re-upload render
            // Offscreen FBO capture (window-independent); PrintWindow only as fallback.
            string? captureError = await GlView.CaptureToPngAsync(shot3dPath, 1600, 900);
            bool ok = captureError == null;
            if (!ok && OperatingSystem.IsWindows())
            {
                var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                ok = NativeCapture.CaptureWindow(hwnd, shot3dPath);
            }
            log.Insert(0, ok
                ? "OK: GL viewport captured" + (captureError == null ? " (offscreen FBO)" : " (PrintWindow fallback)")
                : $"FAIL: offscreen={captureError}, PrintWindow=false");
            File.WriteAllText(Path.ChangeExtension(shot3dPath, ".txt"), string.Join('\n', log));
            BtnView3D.IsChecked = false;
            Stop3D();
            if (shotPath == null)
            {
                Close();
                return;
            }
        }
        if (shotPath == null)
            return;

        var dir = Path.GetDirectoryName(shotPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(shotPath);
        if (brushTest && _session.Map != null)
        {
            var log = RunBrushSelfTest();
            File.WriteAllLines(Path.Combine(dir, $"{stem}_brushtest.txt"), log);
        }

        MenuOverlayUnits.IsChecked = true;
        RenderViewport();
        await Task.Delay(900);
        SaveShot(this, shotPath);

        foreach (Window dlg in new Window[] { new TileSetterDialog(_session), new TilePathingDialog(_session.Map) })
        {
            dlg.Show(this);
            await Task.Delay(600);
            SaveShot(dlg, Path.Combine(dir, $"{stem}_{dlg.GetType().Name}.png"));
            dlg.Close();
        }
        Close();
    }

    /// <summary>
    /// A1 bridge: module discovery is manifest-driven and does not reference
    /// any module namespace. Legacy controls remain active during extraction;
    /// their replacement panels/commands will consume this registry one by one.
    /// In development, module assemblies may live beside MCP rather than the
    /// Avalonia binary, so the loader accepts those explicit assembly paths.
    /// </summary>
    private void LoadEditorModules()
    {
        try
        {
            string? root = FindProjectRoot();
            string[] args = Environment.GetCommandLineArgs();
            string? manifestOverride = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--modules-manifest")
                    manifestOverride = Path.GetFullPath(args[i + 1]);
            }

            string? manifestPath = manifestOverride ?? new[]
            {
                Path.Combine(AppContext.BaseDirectory, "modules.json"),
                root == null ? "" : Path.Combine(root, "standalone", "modules.json"),
            }.FirstOrDefault(File.Exists);
            if (manifestPath == null)
                return;
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("Module manifest not found.", manifestPath);

            ModulesFile manifest = ModuleHost.ReadManifest(manifestPath);
            if (!_moduleHost.Capabilities.Has<IEditorShellActions>())
                _moduleHost.CapabilityRegistry.Provide<IEditorShellActions>(new EditorShellActions(this), "shell");
            if (!_moduleHost.Capabilities.Has<IWindowService>())
                _moduleHost.CapabilityRegistry.Provide<IWindowService>(new EditorWindowService(this), "shell");
            if (!_moduleHost.Capabilities.Has<IWarcraftDataSource>())
                _moduleHost.CapabilityRegistry.Provide<IWarcraftDataSource>(new EditorWarcraftDataSource(this), "shell");
            _assets ??= new EditorAssets();
            _catalog ??= new ObjectCatalog(_assets);
            if (!_moduleHost.Capabilities.Has<IWarcraftObjectDatabase>() && _catalog is IWarcraftObjectDatabase objectDatabase)
                _moduleHost.CapabilityRegistry.Provide<IWarcraftObjectDatabase>(objectDatabase, "shell");
            var runtimeDisabled = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "--disable-module")
                    continue;
                string id = args[i + 1];
                runtimeDisabled.Add(id);
                foreach (ModuleEntry entry in manifest.Modules.Where(module => module.Id == id))
                    entry.Enabled = false;
            }
            var assemblies = new List<Assembly>();
            foreach (string name in manifest.Assemblies)
            {
                string? path = FindModuleAssembly(name, root);
                if (path == null)
                    continue;
                try { assemblies.Add(Assembly.LoadFrom(path)); }
                catch (Exception ex) { Console.Error.WriteLine($"Module assembly '{name}': {ex.Message}"); }
            }
            _moduleLoadReport = _moduleHost.Load(manifest, assemblies);
            PopulateModuleCommands();
            ApplyCommandRegistry();
            ApplyPanelRegistry();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "--module-report")
                    continue;
                string reportPath = Path.GetFullPath(args[i + 1]);
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
                {
                    loaded = _moduleLoadReport.Loaded,
                    disabled = _moduleLoadReport.Disabled,
                    skipped = _moduleLoadReport.Skipped.Select(item => new { id = item.Id, reason = item.Reason }),
                    commands = _moduleHost.Registry.Commands.Select(command => command.Id),
                    visibleCommands = GetVisibleCommandIds(),
                    tools = _moduleHost.Registry.Tools.Select(tool => tool.Id),
                    panels = _moduleHost.Registry.Panels.Select(panel => panel.Id),
                    visiblePanels = GetVisiblePanelIds(),
                    windows = _moduleHost.Registry.Windows.Select(window => window.Id),
                    runtimeDisabled,
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            string warning = _moduleLoadReport.Skipped.Count == 0
                ? ""
                : $"，跳过 {_moduleLoadReport.Skipped.Count}";
            StatusLeft.Text = $"模块 {_moduleLoadReport.Loaded.Count}/{manifest.Modules.Count} 已加载{warning}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"LoadEditorModules: {ex}");
            StatusLeft.Text = "模块加载失败：" + ex.Message;
        }
    }

    private void PopulateModuleCommands()
    {
        if (_moduleHost.Registry.Commands.Count == 0)
            return;
        var context = new ShellModuleContext(_moduleHost);
        MenuPlugins.Items.Add(new Separator());
        var commandsWithDedicatedShellControls = new HashSet<string>(StringComparer.Ordinal)
        {
            "op.newmap", "op.open", "op.save", "op.export",
            "op.changetileset", "op.changetilepathing", "view.camera",
        };
        foreach (CommandDescriptor command in _moduleHost.Registry.Commands)
        {
            if (commandsWithDedicatedShellControls.Contains(command.Id))
                continue;
            var item = new MenuItem
            {
                Header = command.DisplayName,
                Tag = command.Id,
                IsEnabled = command.CanExecute?.Invoke(context) ?? true,
            };
            item.Click += async (_, _) =>
            {
                try
                {
                    await command.Handler(context, CancellationToken.None);
                    StatusLeft.Text = $"模块命令完成：{command.DisplayName}";
                }
                catch (Exception ex)
                {
                    StatusLeft.Text = $"模块命令失败：{command.DisplayName} — {ex.Message}";
                }
            };
            _moduleCommandItems[command.Id] = (item, command);
            MenuPlugins.Items.Add(item);
        }
    }

    private void RefreshModuleCommandStates()
    {
        var context = new ShellModuleContext(_moduleHost);
        foreach ((MenuItem item, CommandDescriptor command) in _moduleCommandItems.Values)
            item.IsEnabled = command.CanExecute?.Invoke(context) ?? true;
    }

    private void ApplyPanelRegistry()
    {
        ComposePhysicalModulePanels();
        var active = _moduleHost.Registry.Panels
            .Select(panel => panel.Id)
            .ToHashSet(StringComparer.Ordinal);

        TerrainTab.IsVisible = active.Contains("panel.terrain");
        DoodadsTab.IsVisible = active.Contains("panel.doodads");
        UnitsTab.IsVisible = active.Contains("panel.units");
        PathingTab.IsVisible = active.Contains("panel.pathing");
        RegionsTab.IsVisible = active.Contains("panel.regions");

        BtnModeTerrain.IsVisible = TerrainTab.IsVisible;
        BtnModeDoodads.IsVisible = DoodadsTab.IsVisible;
        BtnModeUnits.IsVisible = UnitsTab.IsVisible;
        BtnModePathing.IsVisible = PathingTab.IsVisible;
        BtnModeRegions.IsVisible = RegionsTab.IsVisible;

        bool anyRight = ToolTabs.Items.OfType<TabItem>().Any(tab => tab.IsVisible);
        RightSidebar.IsVisible = anyRight;
        MainLayoutGrid.ColumnDefinitions[3].Width = anyRight
            ? new Avalonia.Controls.GridLength(6)
            : new Avalonia.Controls.GridLength(0);
        MainLayoutGrid.ColumnDefinitions[4].Width = anyRight
            ? new Avalonia.Controls.GridLength(316)
            : new Avalonia.Controls.GridLength(0);

        bool minimap = active.Contains("panel.minimap");
        BtnToggleLeftSidebar.IsVisible = minimap;
        SetLeftSidebarVisible(minimap);

        if (ToolTabs.SelectedItem is not TabItem selected || !selected.IsVisible)
            ToolTabs.SelectedItem = ToolTabs.Items.OfType<TabItem>().FirstOrDefault(tab => tab.IsVisible);
    }

    private IReadOnlyList<string> GetVisiblePanelIds()
    {
        var visible = new List<string>();
        if (TerrainTab.IsVisible) visible.Add("panel.terrain");
        if (DoodadsTab.IsVisible) visible.Add("panel.doodads");
        if (UnitsTab.IsVisible) visible.Add("panel.units");
        if (PathingTab.IsVisible) visible.Add("panel.pathing");
        if (RegionsTab.IsVisible) visible.Add("panel.regions");
        if (LeftSidebar.IsVisible) visible.Add("panel.minimap");
        return visible;
    }

    private sealed class ShellModuleContext(ModuleHost host) : IModuleContext
    {
        public IMapDocument? Document => host.Document;
        public ICapabilities Capabilities => host.Capabilities;
        public IEditorLog Log => host.Log;
        public string ModuleId => "shell";
        public event Action<IMapDocument?>? DocumentChanged
        {
            add => host.DocumentChanged += value;
            remove => host.DocumentChanged -= value;
        }
        public void AddCommand(CommandDescriptor command) => throw ReadOnly();
        public void AddTool(IToolDescriptor tool) => throw ReadOnly();
        public void AddPanel(IPanelDescriptor panel) => throw ReadOnly();
        public void AddWindow(IWindowDescriptor window) => throw ReadOnly();
        public void AddOverlay(IViewportOverlay overlay) => throw ReadOnly();
        public void AddPartCodec(IPartCodec codec) => throw ReadOnly();
        public void AddAssetSource(IAssetSource source) => throw ReadOnly();
        public void ProvideCapability<T>(T instance) where T : class => throw ReadOnly();
        private static InvalidOperationException ReadOnly() =>
            new("The shell context consumes contributions; it cannot register them.");
    }

    private sealed class EditorShellActions(MainWindow owner) : IEditorShellActions
    {
        public bool CanExecute(string actionId) => actionId switch
        {
            "op.newmap" or "op.open" or "dialog.settings" or "host.plugin" or
            "assets.pack" or "format.d1map" => true,
            "op.save" or "op.saveas" or "op.export" or
            "op.changetileset" or "op.changetilepathing" or
            "dialog.tilesetter" or "dialog.tilepicker" or "dialog.tilepather" or "dialog.mapinfo" or
            "op.importheightmap" or "op.resize" or "op.unusedfiles" or "op.copypaste" or
            "op.viewhistory" or "op.testmap" or "part.tilesets" or "manager.asset" or
            "bridge.godot" or "import.fbx" or "view.visibility" or
            "view.camera" or "render.terrain" or "render.water" or "render.cliff" or "render.pbr"
                => owner._session.IsOpen,
            _ => false,
        };

        public Task ExecuteAsync(string actionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (actionId)
            {
                case "op.newmap": owner.OnNewMap(null, null!); break;
                case "op.open": owner.OnOpenMap(null, null!); break;
                case "op.save": owner.OnSave(null, null!); break;
                case "op.saveas":
                case "op.export": owner.OnSaveAsNative(null, null!); break;
                case "op.changetileset":
                case "dialog.tilesetter":
                case "dialog.tilepicker": owner.OnTileSetter(null, null!); break;
                case "op.changetilepathing":
                case "dialog.tilepather": owner.OnTilePathing(null, null!); break;
                case "view.camera": owner.Toggle3DFromModule(); break;
                case "dialog.mapinfo": owner.OnMapInfo(null, null!); break;
                case "op.importheightmap": owner.OnImportHeightmap(null, null!); break;
                case "op.resize": owner.OnResizeMap(null, null!); break;
                case "op.unusedfiles": owner.ShowUnusedFilesReport(); break;
                case "op.copypaste": owner.RunCopyPasteModule(); break;
                case "op.viewhistory": owner.ShowModuleInformation("撤销历史",
                    $"可撤销：{owner._history.CanUndo}\n可重做：{owner._history.CanRedo}\n地图 resize 会按 HiveWE/WE 规则清空全部历史。"); break;
                case "op.testmap":
                case "bridge.godot": owner.OnGodotPreview(null, null!); break;
                case "part.tilesets": owner.OnTileSetter(null, null!); break;
                case "manager.asset": owner.ShowAssetStatus(); break;
                case "view.visibility": owner.ShowVisibilityDialog(); break;
                case "dialog.settings": owner.ShowSettingsDialog(); break;
                case "format.d1map": owner.ShowModuleInformation("d1map",
                    "原生地图由 map.json + terrain.bin + placements.bin 组成，通过原子代提交保存。"); break;
                case "import.fbx": owner.ShowModuleInformation("FBX 导入",
                    "FBX 导入能力由 v1 兼容插件注册；请从工具 → 插件选择导入命令。"); break;
                case "host.plugin": owner.ShowModuleInformation("插件宿主", "v1 IW3EditorPlugin 兼容宿主已启用。"); break;
                case "assets.pack": owner.ShowModuleInformation("资产包", "资产包用于组织地形、模型与材质资源。"); break;
                case "render.pbr": owner.Ensure3DFromModule(actionId); break;
                case "render.terrain":
                case "render.water":
                case "render.cliff": owner.Ensure3DFromModule(actionId); break;
                default: throw new InvalidOperationException($"Unknown Shell action '{actionId}'.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class EditorWindowService(MainWindow owner) : IWindowService
    {
        private readonly Dictionary<string, Window> _open = new(StringComparer.Ordinal);

        public bool CanOpen(string windowId) =>
            owner._moduleHost.Registry.Windows.Any(window => window.Id == windowId);

        public Task OpenAsync(string windowId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_open.TryGetValue(windowId, out Window? existing))
            {
                existing.Activate();
                return Task.CompletedTask;
            }

            IWindowDescriptor descriptor = owner._moduleHost.Registry.Windows
                .FirstOrDefault(window => window.Id == windowId)
                ?? throw new InvalidOperationException($"Window '{windowId}' is not registered.");
            if (descriptor.CreateWindow(new ShellModuleContext(owner._moduleHost)) is not Window window)
                throw new InvalidOperationException($"Window '{windowId}' did not create an Avalonia Window.");

            window.Title = string.IsNullOrWhiteSpace(window.Title) ? descriptor.Title : window.Title;
            if (double.IsNaN(window.Width) || window.Width <= 0) window.Width = descriptor.DefaultWidth;
            if (double.IsNaN(window.Height) || window.Height <= 0) window.Height = descriptor.DefaultHeight;
            _open[windowId] = window;
            window.Closed += (_, _) => _open.Remove(windowId);
            window.Show(owner);
            return Task.CompletedTask;
        }
    }

    private sealed class EditorWarcraftDataSource(MainWindow owner) : IWarcraftDataSource
    {
        public bool TryRead(string relativePath, out byte[] bytes)
        {
            owner._assets ??= new EditorAssets();
            string? path = owner._assets.ResolveDataFile(relativePath);
            if (path == null)
            {
                bytes = Array.Empty<byte>();
                return false;
            }
            bytes = File.ReadAllBytes(path);
            return true;
        }
    }

    private void OpenModuleDocumentFromSession()
    {
        if (!_session.IsOpen || string.IsNullOrWhiteSpace(_session.SourcePath))
            return;
        string folder = _session.FormatKind == Services.MapFormatKind.War3EnvironmentFile
            ? Path.GetDirectoryName(_session.W3ePath) ?? _session.SourcePath
            : _session.SourcePath;
        var format = _session.FormatKind switch
        {
            Services.MapFormatKind.War3Folder => W3Editor.Contracts.MapFormatKind.War3Folder,
            Services.MapFormatKind.War3EnvironmentFile => W3Editor.Contracts.MapFormatKind.War3EnvironmentFile,
            Services.MapFormatKind.D1MapFolder => W3Editor.Contracts.MapFormatKind.D1MapFolder,
            _ => W3Editor.Contracts.MapFormatKind.None,
        };
        _moduleHost.OpenDocument(folder, format, _session.CaptureSavePayloads());
        RefreshModuleCommandStates();
    }

    private void SaveCurrentDocument()
    {
        if (_moduleHost.Document == null)
            throw new InvalidOperationException("Module document is not open.");
        _session.Save(_moduleHost.Document);
    }

    private bool HasModuleCommand(string id) =>
        _moduleHost.Registry.Commands.Any(command => command.Id == id);

    private void ApplyCommandRegistry()
    {
        MenuNewMap.IsVisible = HasModuleCommand("op.newmap");
        MenuOpenMap.IsVisible = HasModuleCommand("op.open");
        BtnOpen.IsVisible = MenuOpenMap.IsVisible;
        MenuSave.IsVisible = HasModuleCommand("op.save");
        BtnSave.IsVisible = MenuSave.IsVisible;
        MenuExport.IsVisible = HasModuleCommand("op.export");
        BtnExport.IsVisible = MenuExport.IsVisible;
        MenuTileSetter.IsVisible = HasModuleCommand("op.changetileset");
        BtnTileSetter.IsVisible = MenuTileSetter.IsVisible;
        MenuTilePathing.IsVisible = HasModuleCommand("op.changetilepathing");
        BtnTilePathing.IsVisible = MenuTilePathing.IsVisible;
        BtnView3D.IsVisible = HasModuleCommand("view.camera");
    }

    private IReadOnlyList<string> GetVisibleCommandIds()
    {
        var visible = new List<string>();
        if (MenuNewMap.IsVisible) visible.Add("op.newmap");
        if (MenuOpenMap.IsVisible) visible.Add("op.open");
        if (MenuSave.IsVisible) visible.Add("op.save");
        if (MenuExport.IsVisible) visible.Add("op.export");
        if (MenuTileSetter.IsVisible) visible.Add("op.changetileset");
        if (MenuTilePathing.IsVisible) visible.Add("op.changetilepathing");
        if (BtnView3D.IsVisible) visible.Add("view.camera");
        visible.AddRange(MenuPlugins.Items.OfType<MenuItem>()
            .Select(item => item.Tag as string)
            .Where(id => !string.IsNullOrEmpty(id))!);
        return visible;
    }

    private void Toggle3DFromModule()
    {
        BtnView3D.IsChecked = BtnView3D.IsChecked != true;
        OnView3DToggled(null, null!);
    }

    private void Ensure3DFromModule(string actionId)
    {
        if (BtnView3D.IsChecked != true)
        {
            BtnView3D.IsChecked = true;
            Start3D();
        }
        StatusLeft.Text = $"模块已启用：{actionId}";
    }

    private static string? FindModuleAssembly(string name, string? root)
    {
        var directories = new List<string> { AppContext.BaseDirectory };
        if (root != null)
        {
            directories.Add(Path.Combine(root, "standalone", name, "bin", "Release", "net8.0"));
            directories.Add(Path.Combine(root, "standalone", name, "bin", "Debug", "net8.0"));
            // MCP also copies module DLLs, but those may be stale after a
            // panel-only rebuild. Prefer the module's own output directory.
            directories.Add(Path.Combine(root, "standalone", "W3Editor.Mcp", "bin", "Release", "net8.0"));
            directories.Add(Path.Combine(root, "standalone", "W3Editor.Mcp", "bin", "Debug", "net8.0"));
        }
        return directories
            .Select(directory => Path.Combine(directory, name + ".dll"))
            .FirstOrDefault(File.Exists);
    }

    private static string? FindProjectRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
                return dir.FullName;
        }
        string current = Directory.GetCurrentDirectory();
        for (DirectoryInfo? dir = new(current); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
                return dir.FullName;
        }
        return null;
    }

    private void OnHideLeftSidebar(object? sender, RoutedEventArgs e) =>
        SetLeftSidebarVisible(false);

    private void OnToggleLeftSidebar(object? sender, RoutedEventArgs e)
    {
        // ToggleButton flips IsChecked before Click; sync layout to the new value.
        bool show = BtnToggleLeftSidebar.IsChecked == true;
        SetLeftSidebarVisible(show);
    }

    private void SetLeftSidebarVisible(bool visible)
    {
        _leftSidebarVisible = visible;
        LeftSidebar.IsVisible = visible;
        MainLayoutGrid.ColumnDefinitions[0].Width = visible
            ? new Avalonia.Controls.GridLength(264)
            : new Avalonia.Controls.GridLength(0);
        MainLayoutGrid.ColumnDefinitions[1].Width = visible
            ? new Avalonia.Controls.GridLength(6)
            : new Avalonia.Controls.GridLength(0);
        BtnToggleLeftSidebar.IsChecked = visible;
    }

    /// <summary>
    /// Scan <see cref="EditorPaths.PluginsDir"/>, register each plugin against
    /// an <see cref="EditorPluginContext"/>, and populate 工具 → 插件.
    /// </summary>
    private void LoadPlugins()
    {
        try
        {
            var plugins = PluginLoader.LoadAll(EditorPaths.PluginsDir, out var loadFailures);
            var ctx = new EditorPluginContext(this);
            int ok = 0;
            var failures = new List<string>(loadFailures);
            foreach (var plugin in plugins)
            {
                try
                {
                    plugin.Register(ctx);
                    ok++;
                }
                catch (Exception ex)
                {
                    string detail = $"{plugin.Id}：{ex.Message}";
                    failures.Add(detail);
                    Console.Error.WriteLine($"LoadPlugins: Register failed {detail}");
                }
            }

            if (ok == 0 && failures.Count == 0)
                return;

            string status = $"已加载 {ok} 个插件";
            if (failures.Count > 0)
                status += $"，失败 {failures.Count}：{string.Join("；", failures)}";
            StatusLeft.Text = status;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"LoadPlugins: {ex.Message}");
            StatusLeft.Text = $"加载插件失败：{ex.Message}";
        }
    }

    /// <summary>
    /// Adds a menu item under 工具 → 插件. Paths look like
    /// <c>工具(_T)/插件/导入 FBX（外部模型）…</c>; the leaf name becomes the header.
    /// </summary>
    /// <summary>
    /// Places (or replaces) the single plugin preview mesh in the 3D viewport.
    /// Auto-enters 3D when a map is open; throws with a Chinese message on failure.
    /// </summary>
    internal void PlacePluginPreview(PluginMeshPreview mesh, string label)
    {
        if (!_session.IsOpen || _session.Map == null)
        {
            StatusLeft.Text = "预览失败：请先打开地图";
            throw new InvalidOperationException("预览失败：请先打开地图后再放置模型。");
        }

        if (BtnView3D.IsChecked != true)
        {
            BtnView3D.IsChecked = true;
            Start3D();
        }
        else if (!GlView.IsVisible || !GlView.Scene.HasMap)
        {
            Start3D();
        }

        if (!GlView.IsVisible || !GlView.Scene.HasMap)
        {
            StatusLeft.Text = "预览失败：无法进入 3D 视口";
            throw new InvalidOperationException("预览失败：无法启动 3D 视口，模型未放置。");
        }

        GlView.Scene.SetPluginPreview(mesh ?? new PluginMeshPreview(), label ?? "");
        string name = string.IsNullOrWhiteSpace(label) ? "（未命名）" : label;
        StatusLeft.Text = $"已放置插件预览：{name}";
    }

    internal void AddPluginMenuCommand(string menuPath, string id, Func<CancellationToken, Task> handler)
    {
        const string prefix = "工具(_T)/插件/";
        string header = menuPath.StartsWith(prefix, StringComparison.Ordinal)
            ? menuPath[prefix.Length..]
            : menuPath;
        int slash = header.LastIndexOf('/');
        if (slash >= 0 && slash < header.Length - 1)
            header = header[(slash + 1)..];
        if (string.IsNullOrWhiteSpace(header))
            header = id;

        var item = new MenuItem { Header = header, Tag = id };
        item.Click += async (_, _) =>
        {
            try
            {
                await handler(CancellationToken.None);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = $"插件命令失败：{ex.Message}";
                await MessageDialog.Show(this, "插件错误", ex.Message);
            }
        };
        MenuPlugins.Items.Add(item);
    }

    /// <summary>
    /// New-map paint demo (--paint): texture bands across the Lordaeron Summer
    /// palette plus hill / cliff / water, all through the normal brush path.
    /// </summary>
    private void RunNewMapPaint()
    {
        var map = _session.Map!;
        void Stroke(BrushOp op, int cx, int cy, int radius, int steps, int dx, int dy, int tex = 0)
        {
            _brush.Op = op;
            _brush.Radius = radius;
            _brush.Strength = 1f;
            _brush.TextureIndex = tex;
            _stroke = new BrushStroke();
            _history.BeginStroke(map);
            for (int i = 0; i < steps; i++)
                TerrainBrush.Apply(map, cx + i * dx, cy + i * dy, _brush, erase: false, _stroke, frameDelta: 0.5f);
            _history.CommitStroke(map);
            _brush.Shift = false;   // 笔画结束复位修饰键状态（避免"移除模式"粘住）
            _stroke = null;
        }

        int mx = map.Width / 2, my = map.Height / 2;
        Stroke(BrushOp.Texture, mx - 40, my - 32, 7, 20, 4, 0, tex: 0); // Ldrt 泥土
        Stroke(BrushOp.Texture, mx - 40, my - 16, 7, 20, 4, 0, tex: 1); // Ldro 粗泥
        Stroke(BrushOp.Texture, mx - 40, my, 7, 20, 4, 0, tex: 3);      // Lrok 岩石
        Stroke(BrushOp.Texture, mx - 40, my + 16, 7, 20, 4, 0, tex: 5); // Lgrd 深草
        Stroke(BrushOp.Raise, mx + 26, my + 26, 10, 6, 2, 2);
        Stroke(BrushOp.CliffRaise2, mx - 28, my + 28, 5, 1, 0, 0);
        Stroke(BrushOp.WaterShallow, mx + 28, my - 28, 8, 4, 3, 0);
        // 单层台地 + 西侧坡道（坡道只在相差一层的悬崖沿上成立）。
        Stroke(BrushOp.CliffRaise1, mx - 6, my + 28, 5, 1, 0, 0);
        Stroke(BrushOp.Ramp, mx - 12, my + 28, 2, 1, 0, 0);
        _session.MarkDirty();
        AfterMapMutation("新图绘制自检完成");
    }

    /// <summary>
    /// 3D-path self test (--paint3d): ray-picks screen pixels through the GL
    /// camera, drives the same stroke pipeline the mouse uses, verifies the
    /// map changed and undo restores it.
    /// </summary>
    private List<string> RunBrush3DSelfTest()
    {
        var map = _session.Map!;
        var log = new List<string>();
        float w = (float)GlView.Bounds.Width, h = (float)GlView.Bounds.Height;
        var center = GlView.Scene.Pick(w * 0.5f, h * 0.5f, w, h);
        log.Add(center != null
            ? $"OK: pick center -> corner {center.Value.CornerX:0.0},{center.Value.CornerY:0.0}"
            : "FAIL: center ray missed terrain");
        if (center == null)
            return log;

        var before = map.SaveBytes();
        _brush.Op = BrushOp.Raise;
        _brush.Radius = 6;
        _brush.Strength = 1f;
        for (int i = 0; i <= 10; i++)
        {
            var pick = GlView.Scene.Pick(w * (0.35f + 0.03f * i), h * 0.5f, w, h);
            if (pick != null)
                OnGlBrushStroke(pick.Value.CornerX, pick.Value.CornerY, erase: false);
        }
        OnGlBrushStrokeEnded();
        bool changed = !map.SaveBytes().AsSpan().SequenceEqual(before);
        log.Add(changed ? "OK: 3D raise stroke modified map" : "FAIL: 3D stroke was a no-op");
        if (changed)
        {
            _history.Undo(_session);
            log.Add(map.SaveBytes().AsSpan().SequenceEqual(before)
                ? "OK: undo restored pre-stroke bytes"
                : "FAIL: undo mismatch after 3D stroke");
            _history.Redo(_session);
            GlView.Scene.NotifyMapReplaced();
        }
        _brush.Op = BrushOp.None;
        return log;
    }

    /// <summary>Applies scripted strokes through the normal brush path and verifies undo/redo byte-roundtrips.</summary>
    private List<string> RunBrushSelfTest()
    {
        var map = _session.Map!;
        var log = new List<string>();
        var before = map.SaveBytes();

        void Stroke(BrushOp op, int cx, int cy, int radius, float strength, int steps, int dx, int texIndex = 0)
        {
            _brush.Op = op;
            _brush.Radius = radius;
            _brush.Strength = strength;
            _brush.TextureIndex = texIndex;
            _stroke = new BrushStroke();
            _history.BeginStroke(map);
            for (int i = 0; i < steps; i++)
                TerrainBrush.Apply(map, cx + i * dx, cy, _brush, erase: false, _stroke,
                    frameDelta: i == 0 ? 0.5f : Math.Clamp(strength, 0.01f, 1f));
            _history.CommitStroke(map);
            _brush.Shift = false;   // 笔画结束复位修饰键状态（避免"移除模式"粘住）
            _stroke = null;
        }

        int mx = map.Width / 2, my = map.Height / 2;
        Stroke(BrushOp.Raise, mx - 30, my - 30, 8, 1.0f, 8, 2);
        Stroke(BrushOp.Texture, mx + 25, my - 30, 10, 1.0f, 4, 3,
            texIndex: Math.Min(5, map.GroundTilesets.Count - 1));
        Stroke(BrushOp.CliffRaise2, mx + 30, my + 28, 6, 1.0f, 1, 0);
        Stroke(BrushOp.WaterShallow, mx - 30, my + 30, 9, 1.0f, 3, 3);
        Stroke(BrushOp.Plateau, mx, my, 6, 1.0f, 2, 2);
        Stroke(BrushOp.CliffLevel, mx + 10, my - 10, 4, 1.0f, 1, 0);
        const int strokes = 6;
        var after = map.SaveBytes();
        log.Add(after.AsSpan().SequenceEqual(before) ? "FAIL: strokes did not modify map" : $"OK: {strokes} strokes modified map");

        // 双层悬崖语义抽查：CliffRaise2 后目标区中心应比原层高 2 层。
        var cliffCenter = map.GetCorner(mx + 30, my + 28);
        log.Add(cliffCenter != null && cliffCenter.LayerHeight == 4
            ? "OK: CliffRaise2 center layer 2 -> 4"
            : $"FAIL: CliffRaise2 center layer = {cliffCenter?.LayerHeight}");

        for (int i = 0; i < strokes; i++) _history.Undo(_session);
        log.Add(map.SaveBytes().AsSpan().SequenceEqual(before)
            ? $"OK: undo x{strokes} restored the original bytes"
            : "FAIL: undo did not restore original bytes");

        for (int i = 0; i < strokes; i++) _history.Redo(_session);
        log.Add(map.SaveBytes().AsSpan().SequenceEqual(after)
            ? $"OK: redo x{strokes} restored the edited bytes"
            : "FAIL: redo did not restore edited bytes");

        _session.MarkDirty();
        AfterMapMutation("笔刷自检完成");
        return log;
    }

    private static void SaveShot(Window w, string path)
    {
        var pixelSize = new Avalonia.PixelSize(
            Math.Max(1, (int)w.Bounds.Width), Math.Max(1, (int)w.Bounds.Height));
        using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(pixelSize, new Avalonia.Vector(96, 96));
        rtb.Render(w);
        rtb.Save(path);
    }
}
