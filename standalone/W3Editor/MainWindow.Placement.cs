using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using W3Editor.Formats;
using W3Editor.Rendering;
using W3Editor.Services;

namespace W3Editor;

/// <summary>
/// Doodad / unit placement & selection ("story step 5"): palette arming, ghost
/// preview, click place + drag scatter, click/box select, drag move,
/// Ctrl+drag rotate, +/- scale, V variation, Delete, Ctrl+C/V, Ctrl+A.
/// All edits go through the shared EditHistory command stack.
/// </summary>
public partial class MainWindow
{
    private enum EditMode { Terrain, Doodads, Units, Pathing, Regions }

    private IObjectCatalog? _catalog;
    private EditMode _mode = EditMode.Terrain;
    private readonly Random _rng = new();

    // Armed palette entry + ghost preview
    private DoodadTypeInfo? _armedDoodad;
    private UnitTypeInfo? _armedUnit;
    private W3Placement? _ghost;
    private bool _placeSuspended; // Space toggle (WE behavior)

    // Selection / drag state
    private readonly List<W3Placement> _selection = new();
    private List<W3Placement> _clipboard = new();
    private enum PlaceDrag { None, Scatter, Move, Rotate, Box }
    private PlaceDrag _placeDrag = PlaceDrag.None;
    private List<W3Placement> _scatterPlaced = new();
    private (float X, float Y) _scatterLast;
    private List<(W3Placement P, TransformPlacementsCommand.State Before)> _dragStates = new();
    private (float X, float Y) _dragGroundStart;
    private float _rotateStartPx;
    private Avalonia.Point _boxStart, _boxEnd;
    private (float X, float Y) _lastHoverGround;

    private bool _syncingModeUi;

    // ------------------------------------------------------------------
    // Mode switching (toolbar segment / 工具 menu / right tab strip)
    // ------------------------------------------------------------------

    private void OnModeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn) return;
        SetMode(ParseMode((string)btn.Tag!));
    }

    private void OnModeMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        SetMode(ParseMode((string)item.Tag!));
    }

    private void OnToolTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingModeUi || ToolTabs == null) return;
        switch ((ToolTabs.SelectedItem as TabItem)?.Tag as string)
        {
            case "panel.terrain": SetMode(EditMode.Terrain); break;
            case "panel.doodads": SetMode(EditMode.Doodads); break;
            case "panel.units": SetMode(EditMode.Units); break;
            case "panel.pathing": SetMode(EditMode.Pathing); break;
            case "panel.regions": SetMode(EditMode.Regions); break;
        }
    }

    private static EditMode ParseMode(string tag) => tag switch
    {
        "Doodads" => EditMode.Doodads,
        "Units" => EditMode.Units,
        "Pathing" => EditMode.Pathing,
        "Regions" => EditMode.Regions,
        _ => EditMode.Terrain,
    };

    private void SetMode(EditMode mode)
    {
        if (BtnModeTerrain == null || GlView == null || DoodadList == null)
            return; // XAML still loading
        _mode = mode;
        _syncingModeUi = true;
        BtnModeTerrain.IsChecked = mode == EditMode.Terrain;
        BtnModeDoodads.IsChecked = mode == EditMode.Doodads;
        BtnModeUnits.IsChecked = mode == EditMode.Units;
        BtnModePathing.IsChecked = mode == EditMode.Pathing;
        BtnModeRegions.IsChecked = mode == EditMode.Regions;
        ToolTabs.SelectedItem = mode switch
        {
            EditMode.Doodads => DoodadsTab,
            EditMode.Units => UnitsTab,
            EditMode.Pathing => PathingTab,
            EditMode.Regions => RegionsTab,
            _ => TerrainTab,
        };
        _syncingModeUi = false;

        GlView.Tool = mode is EditMode.Terrain or EditMode.Pathing or EditMode.Regions
            ? Rendering.GlTerrainViewport.ViewportTool.TerrainBrush
            : Rendering.GlTerrainViewport.ViewportTool.Placement;

        if (mode is EditMode.Terrain or EditMode.Pathing or EditMode.Regions)
        {
            DisarmPlacement();
            SelectRect.IsVisible = false;
            if (mode == EditMode.Pathing)
                ActivatePathingMode();
            else if (mode == EditMode.Regions)
                ActivateRegionsMode();
        }
        else
        {
            GlView.Scene.BrushRing = null;
            // Placement lives in the 3D viewport — bring it up automatically.
            if (_session.IsOpen && BtnView3D.IsChecked != true)
            {
                BtnView3D.IsChecked = true;
                Start3D();
                // Thumbnails could not render while GL was down — rebuild the
                // visible list so pending entries queue against the live scene.
                if (mode == EditMode.Doodads)
                    RefreshDoodadList();
            }
            UpdatePlacementHint();
        }
    }

    // ------------------------------------------------------------------
    // Palette population
    // ------------------------------------------------------------------

    private void EnsureCatalog()
    {
        if (_catalog != null) return;
        _assets ??= new EditorAssets();
        _catalog = new ObjectCatalog(_assets);
    }

    /// <summary>WE unit palette races (UnitData.slk race column values).</summary>
    private static readonly (string Key, string Name)[] UnitRaces =
    {
        ("", "全部种族"),
        ("human", "人类"),
        ("orc", "兽族"),
        ("nightelf", "暗夜精灵"),
        ("undead", "不死族"),
        ("creeps", "中立敌对"),
        ("naga", "娜迦"),
        ("demon", "恶魔"),
        ("critters", "野生动物"),
        ("other", "其他"),
    };

    private void PopulatePlacementPalettes()
    {
        EnsureCatalog();
        // Category dropdown: 全部 + categories that actually exist in the catalog.
        if (DoodadCategory.Items.Count == 0)
        {
            DoodadCategory.Items.Add(new ComboBoxItem { Content = "全部分类", Tag = '\0' });
            var present = new HashSet<char>(_catalog!.Doodads.Select(d => d.Category));
            foreach ((char letter, string name) in ObjectCatalog.DoodadCategories)
                if (present.Contains(letter))
                    DoodadCategory.Items.Add(new ComboBoxItem { Content = name, Tag = letter });
            DoodadCategory.SelectedIndex = 0;
        }
        if (UnitOwner.Items.Count == 0)
        {
            for (int i = 0; i < 12; i++)
                UnitOwner.Items.Add(new ComboBoxItem { Content = $"玩家 {i + 1}", Tag = i });
            UnitOwner.Items.Add(new ComboBoxItem { Content = "中立敌对", Tag = 12 });
            UnitOwner.Items.Add(new ComboBoxItem { Content = "中立被动", Tag = 15 });
            UnitOwner.SelectedIndex = 0;
        }
        if (UnitRace.Items.Count == 0)
        {
            var present = new HashSet<string>(_catalog!.Units.Select(u => u.Race), StringComparer.Ordinal);
            foreach ((string key, string name) in UnitRaces)
                if (key.Length == 0 || present.Contains(key))
                    UnitRace.Items.Add(new ComboBoxItem { Content = name, Tag = key });
            UnitRace.SelectedIndex = 0;
        }
        RefreshDoodadList();
        RefreshUnitList();
    }

    // ------------------------------------------------------------------
    // Palette thumbnails
    // ------------------------------------------------------------------

    private readonly Dictionary<string, Avalonia.Media.Imaging.WriteableBitmap?> _paletteIcons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Command-button icon (BLP → PNG sidecar) as a 24px palette thumbnail.</summary>
    private Avalonia.Media.Imaging.WriteableBitmap? LoadPaletteIcon(string classicRel)
    {
        if (classicRel.Length == 0) return null;
        if (_paletteIcons.TryGetValue(classicRel, out var cached)) return cached;
        Avalonia.Media.Imaging.WriteableBitmap? bitmap = null;
        if (_assets?.LoadTexture(classicRel) is { } image)
        {
            bitmap = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(image.Width, image.Height), new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
            using var fb = bitmap.Lock();
            unsafe
            {
                var dst = (byte*)fb.Address;
                byte[] src = image.Pixels;
                for (int row = 0; row < image.Height; row++)
                {
                    int s = row * image.Width * 4;
                    byte* d = dst + row * fb.RowBytes;
                    for (int i = 0; i < image.Width * 4; i += 4)
                    {
                        d[i + 0] = src[s + i + 2]; // RGBA → BGRA
                        d[i + 1] = src[s + i + 1];
                        d[i + 2] = src[s + i + 0];
                        d[i + 3] = src[s + i + 3];
                    }
                }
            }
        }
        _paletteIcons[classicRel] = bitmap;
        return bitmap;
    }

    private static readonly Dictionary<char, string> CategoryChipColor = new()
    {
        ['D'] = "#5E8C4A", // trees
        ['B'] = "#8C6B4A", // bridges
        ['P'] = "#8C4A4A", // pathing blockers
        ['E'] = "#4A8C7E", // environment
        ['O'] = "#7E7E4A", // obstacles
        ['S'] = "#4A6B8C", // structures
        ['W'] = "#4A5E8C", // water
        ['C'] = "#7E4A8C", // cinematic
        ['Z'] = "#6B6B6B", // cliffs
    };

    private static Control DoodadChip(DoodadTypeInfo info) => new Border
    {
        Width = 24, Height = 24,
        CornerRadius = new Avalonia.CornerRadius(4),
        Background = Avalonia.Media.Brush.Parse(CategoryChipColor.GetValueOrDefault(info.Category, "#6B6B6B")),
        Child = new TextBlock
        {
            Text = info.Category.ToString(),
            FontSize = 12,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        },
    };

    // ------------------------------------------------------------------
    // Doodad model thumbnails (offscreen renders, cached per type id)
    // ------------------------------------------------------------------

    private const int DoodadThumbSize = 48;
    private readonly Dictionary<string, Avalonia.Media.Imaging.WriteableBitmap> _doodadThumbs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _doodadThumbPending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _doodadThumbFailed = new(StringComparer.Ordinal);

    /// <summary>Thumbnail container: shows the cached model render, or the
    /// category chip while one is being rendered offscreen.</summary>
    private Control DoodadIcon(DoodadTypeInfo info)
    {
        var host = new Border { Width = 24, Height = 24 };
        if (_doodadThumbs.TryGetValue(info.Id, out var cached))
        {
            host.Child = ThumbImage(cached);
            return host;
        }
        host.Child = DoodadChip(info);
        // Only queue while the GL scene is live; otherwise keep the chip and
        // retry on the next list refresh (SetMode refreshes after Start3D).
        if (GlView.IsVisible && !_doodadThumbFailed.Contains(info.Id) && _doodadThumbPending.Add(info.Id))
            _ = LoadDoodadThumbAsync(info, host);
        return host;
    }

    private static Avalonia.Controls.Image ThumbImage(Avalonia.Media.Imaging.WriteableBitmap bmp) => new()
    {
        Source = bmp, Width = 24, Height = 24,
        Stretch = Avalonia.Media.Stretch.Uniform,
    };

    private async Task LoadDoodadThumbAsync(DoodadTypeInfo info, Border host)
    {
        try
        {
            var proto = new W3Placement
            {
                Id = info.Id,
                IsUnit = false,
                Variation = 0,
                ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f,
            };
            byte[]? pixels = await GlView.RenderThumbnailAsync(proto, DoodadThumbSize);
            if (pixels == null)
            {
                _doodadThumbFailed.Add(info.Id);
                return;
            }
            var bmp = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(DoodadThumbSize, DoodadThumbSize), new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
            using (var fb = bmp.Lock())
            {
                unsafe
                {
                    var dst = (byte*)fb.Address;
                    int stride = DoodadThumbSize * 4;
                    for (int row = 0; row < DoodadThumbSize; row++)
                    {
                        int s = (DoodadThumbSize - 1 - row) * stride; // GL rows are bottom-up
                        byte* d = dst + row * fb.RowBytes;
                        for (int i = 0; i < stride; i += 4)
                        {
                            d[i + 0] = pixels[s + i + 2]; // RGBA → BGRA
                            d[i + 1] = pixels[s + i + 1];
                            d[i + 2] = pixels[s + i + 0];
                            d[i + 3] = pixels[s + i + 3];
                        }
                    }
                }
            }
            _doodadThumbs[info.Id] = bmp;
            host.Child = ThumbImage(bmp); // swap in place if the row is still alive
        }
        catch (Exception ex)
        {
            // Fire-and-forget task — an unobserved exception would vanish (or
            // crash on unobserved-task policies). Keep the placeholder chip.
            _doodadThumbFailed.Add(info.Id);
            System.Diagnostics.Debug.WriteLine($"doodad thumb {info.Id}: {ex.Message}");
        }
        finally
        {
            _doodadThumbPending.Remove(info.Id);
        }
    }

    private void RefreshDoodadList()
    {
        if (_catalog == null) return;
        DoodadList.Items.Clear();
        var map = _session.Map;
        char category = (DoodadCategory.SelectedItem as ComboBoxItem)?.Tag is char c ? c : '\0';
        string filter = DoodadSearch.Text?.Trim() ?? "";
        char tileset = map?.Tileset ?? 'L';
        int shown = 0;
        foreach (DoodadTypeInfo info in _catalog.Doodads)
        {
            if (category != '\0' && info.Category != category) continue;
            if (!info.InTileset(tileset)) continue;
            if (filter.Length > 0
                && !info.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !info.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (shown >= 400) break; // keep the list snappy
            string kind = info.IsDestructible ? "破" : "饰";
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(DoodadIcon(info));
            row.Children.Add(new TextBlock
            {
                Text = $"[{kind}] {info.Name}（{info.Id}）" + (info.NumVar > 1 ? $" ×{info.NumVar}变体" : ""),
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            DoodadList.Items.Add(new ListBoxItem { Content = row, Tag = info, Padding = new Avalonia.Thickness(6, 3) });
            shown++;
        }
        DoodadHint.Text = _catalog.Doodads.Count == 0
            ? "未找到 war3 资产目录（SLK），调色板为空。"
            : $"{shown} 项（图块集 {MapSession.TilesetName(tileset)}）。左键放置 · 拖动散布 · 空格/Esc 取消 · Delete 删除。";
    }

    private void RefreshUnitList()
    {
        if (_catalog == null) return;
        UnitList.Items.Clear();
        string filter = UnitSearch.Text?.Trim() ?? "";
        string race = (UnitRace.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        int shown = 0;
        foreach (UnitTypeInfo info in _catalog.Units)
        {
            if (race.Length > 0 && !info.Race.Equals(race, StringComparison.Ordinal))
                continue;
            if (filter.Length > 0
                && !info.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !info.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (shown >= 400) break;
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            if (LoadPaletteIcon(info.IconPath) is { } icon)
            {
                row.Children.Add(new Avalonia.Controls.Image
                {
                    Source = icon, Width = 24, Height = 24,
                    Stretch = Avalonia.Media.Stretch.UniformToFill,
                });
            }
            else
            {
                row.Children.Add(new Border
                {
                    Width = 24, Height = 24,
                    CornerRadius = new Avalonia.CornerRadius(4),
                    Background = Avalonia.Media.Brush.Parse("#4A6B8C"),
                });
            }
            row.Children.Add(new TextBlock
            {
                Text = $"{info.Name}（{info.Id}）",
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            UnitList.Items.Add(new ListBoxItem { Content = row, Tag = info, Padding = new Avalonia.Thickness(6, 3) });
            shown++;
        }
        UnitHint.Text = _catalog.Units.Count == 0
            ? "未找到 war3 资产目录（SLK），调色板为空。"
            : $"{shown} 项。左键放置 · 拖动散布（间距同装饰物页） · 空格/Esc 取消 · Ctrl+左键拖动调朝向。";
    }

    private void OnDoodadPaletteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if ((DoodadList.SelectedItem as ListBoxItem)?.Tag is not DoodadTypeInfo info) return;
        _armedUnit = null;
        _armedDoodad = info;
        _placeSuspended = false;
        if (_mode != EditMode.Doodads) SetMode(EditMode.Doodads);
        RerollGhost();
        UpdatePlacementHint();
    }

    private void OnUnitPaletteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if ((UnitList.SelectedItem as ListBoxItem)?.Tag is not UnitTypeInfo info) return;
        _armedDoodad = null;
        _armedUnit = info;
        _placeSuspended = false;
        if (_mode != EditMode.Units) SetMode(EditMode.Units);
        RerollGhost();
        UpdatePlacementHint();
    }

    // ------------------------------------------------------------------
    // Ghost (armed preview under the cursor)
    // ------------------------------------------------------------------

    /// <summary>Builds a fresh ghost from the armed entry with new random rolls.</summary>
    private void RerollGhost()
    {
        if (_armedDoodad is { } d)
        {
            float scale = 1f;
            if (ChkRandScale.IsChecked == true && d.RandomScale && d.MaxScale > d.MinScale)
            {
                float world = d.MinScale + (float)_rng.NextDouble() * (d.MaxScale - d.MinScale);
                scale = world / MathF.Max(d.DefScale, 0.0001f);
            }
            float angle;
            if (d.FixedRot >= 0f)
                angle = d.FixedRot * MathF.PI / 180f;
            else if (ChkRandRotation.IsChecked == true)
                angle = (float)(_rng.NextDouble() * Math.Tau);
            else
                angle = 0f;
            _ghost = new W3Placement
            {
                Id = d.Id,
                IsUnit = false,
                Variation = ChkRandVariation.IsChecked == true ? _rng.Next(Math.Max(d.NumVar, 1)) : 0,
                Angle = angle,
                ScaleX = scale, ScaleY = scale, ScaleZ = scale,
                X = _lastHoverGround.X, Y = _lastHoverGround.Y,
                Z = -1e9f, // below any terrain -> render ground-snapped
                Life = 100,
            };
        }
        else if (_armedUnit is { } u)
        {
            _ghost = new W3Placement
            {
                Id = u.Id,
                IsUnit = true,
                Angle = ChkUnitRandRotation.IsChecked == true
                    ? (float)(_rng.NextDouble() * Math.Tau)
                    : 270f * MathF.PI / 180f, // WE default facing
                PlayerOwner = SelectedOwner(),
                X = _lastHoverGround.X, Y = _lastHoverGround.Y,
                Z = -1e9f,
                Life = -1,
            };
        }
        else
        {
            _ghost = null;
        }
        GlView.Scene.Placements.Ghost = _placeSuspended ? null : _ghost;
    }

    private int SelectedOwner() =>
        (UnitOwner.SelectedItem as ComboBoxItem)?.Tag is int owner ? owner : 0;

    /// <summary>Scatter spacing slider value (tiles) in world units.</summary>
    private float ScatterSpacingWorld() =>
        (float)Math.Max(SliderScatterSpacing?.Value ?? 1.0, 0.25) * 128f;

    /// <summary>Scatter radius slider value (tiles) in world units. 0 = place on the cursor path.</summary>
    private float ScatterRadiusWorld() =>
        (float)Math.Max(SliderScatterRadius?.Value ?? 0.0, 0.0) * 128f;

    /// <summary>
    /// Scatter brush: jitters the position within the scatter radius and places
    /// only when the spot keeps the minimum spacing from everything placed in
    /// this stroke — random offsets break up rows, spacing keeps trees apart.
    /// </summary>
    private void TryScatterAt((float X, float Y) ground)
    {
        float radius = ScatterRadiusWorld();
        float spacing = ScatterSpacingWorld();
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float ang = (float)(_rng.NextDouble() * Math.Tau);
            float r = radius * MathF.Sqrt((float)_rng.NextDouble());
            float x = ground.X + MathF.Cos(ang) * r;
            float y = ground.Y + MathF.Sin(ang) * r;
            bool tooClose = false;
            foreach (W3Placement q in _scatterPlaced)
            {
                float dx = q.X - x, dy = q.Y - y;
                if (dx * dx + dy * dy < spacing * spacing)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose)
                continue;
            PlaceGhostAt(x, y);
            return;
        }
    }

    private void DisarmPlacement()
    {
        _armedDoodad = null;
        _armedUnit = null;
        _ghost = null;
        _placeSuspended = false;
        GlView.Scene.Placements.Ghost = null;
        DoodadList.SelectedItem = null;
        UnitList.SelectedItem = null;
    }

    private void UpdatePlacementHint()
    {
        if (_mode == EditMode.Terrain) return;
        string what = _armedDoodad?.Name ?? _armedUnit?.Name ?? "";
        StatusLeft.Text = what.Length > 0
            ? (_placeSuspended
                ? $"已暂停放置 {what}（再按空格恢复 · Esc 取消）"
                : $"放置中：{what} — 左键放置{(_armedDoodad != null ? " · 按住拖动散布" : "")} · 空格暂停 · Esc 取消")
            : "从右侧调色板选择一项后在 3D 视口放置；或左键点选 / 框选已有摆放。";
    }

    // ------------------------------------------------------------------
    // Ground picking helpers
    // ------------------------------------------------------------------

    private (float X, float Y)? PickGroundW3(float px, float py)
    {
        var map = _session.Map;
        if (map == null) return null;
        var pick = GlView.Scene.Pick(px, py, (float)GlView.Bounds.Width, (float)GlView.Bounds.Height);
        if (pick == null) return null;
        return (map.CenterOffsetX + pick.Value.CornerX * 128f,
                map.CenterOffsetY + pick.Value.CornerY * 128f);
    }

    private void OnGlPixelHover(float px, float py)
    {
        if (_mode == EditMode.Terrain) return;
        if (PickGroundW3(px, py) is not { } ground) return;
        _lastHoverGround = ground;
        if (_ghost != null && !_placeSuspended)
        {
            _ghost.X = ground.X;
            _ghost.Y = ground.Y;
        }
    }

    // ------------------------------------------------------------------
    // Viewport placement input (left button, Tool == Placement)
    // ------------------------------------------------------------------

    private void OnPlacePressed(float px, float py, KeyModifiers modifiers)
    {
        if (_session.Map == null) return;

        // Armed & visible ghost -> place / begin scatter.
        if (_ghost != null && !_placeSuspended)
        {
            if (PickGroundW3(px, py) is not { } ground) return;
            _scatterPlaced = new List<W3Placement>();
            if (_armedDoodad != null)
                TryScatterAt(ground); // jittered, spacing-checked
            else
                PlaceGhostAt(ground.X, ground.Y); // units: first one exactly on the click
            _scatterLast = ground;
            // Both kinds support drag-scatter; units share the doodad spacing slider.
            _placeDrag = PlaceDrag.Scatter;
            return;
        }

        // Selection editing path.
        W3Placement? hit = GlView.Scene.PickPlacement(px, py, (float)GlView.Bounds.Width, (float)GlView.Bounds.Height);
        bool additive = modifiers.HasFlag(KeyModifiers.Shift);
        if (hit != null)
        {
            if (!_selection.Contains(hit))
            {
                if (!additive) _selection.Clear();
                _selection.Add(hit);
                RefreshSelectionVisual();
            }
            else if (additive)
            {
                _selection.Remove(hit);
                RefreshSelectionVisual();
                return;
            }
            // Begin move (plain) or rotate (Ctrl) drag over the selection.
            _dragStates = _selection
                .Select(p => (p, TransformPlacementsCommand.Capture(p)))
                .ToList();
            if (modifiers.HasFlag(KeyModifiers.Control))
            {
                _placeDrag = PlaceDrag.Rotate;
                _rotateStartPx = px;
            }
            else
            {
                _placeDrag = PlaceDrag.Move;
                _dragGroundStart = PickGroundW3(px, py) ?? (hit.X, hit.Y);
            }
            return;
        }

        // Empty ground: start box selection.
        if (!additive)
        {
            _selection.Clear();
            RefreshSelectionVisual();
        }
        _placeDrag = PlaceDrag.Box;
        _boxStart = _boxEnd = new Avalonia.Point(px, py);
        UpdateSelectRectVisual(visible: true);
    }

    private void OnPlaceDragged(float px, float py, KeyModifiers modifiers)
    {
        switch (_placeDrag)
        {
            case PlaceDrag.Scatter:
            {
                if (_ghost != null && PickGroundW3(px, py) is { } ground)
                {
                    _ghost.X = ground.X;
                    _ghost.Y = ground.Y;
                    float dx = ground.X - _scatterLast.X, dy = ground.Y - _scatterLast.Y;
                    // Sample new scatter positions roughly every half spacing of travel.
                    float step = ScatterSpacingWorld() * 0.5f;
                    if (dx * dx + dy * dy >= step * step)
                    {
                        TryScatterAt(ground);
                        _scatterLast = ground;
                    }
                }
                break;
            }
            case PlaceDrag.Move:
            {
                if (PickGroundW3(px, py) is not { } ground) break;
                float dx = ground.X - _dragGroundStart.X, dy = ground.Y - _dragGroundStart.Y;
                foreach ((W3Placement p, TransformPlacementsCommand.State before) in _dragStates)
                {
                    p.X = before.X + dx;
                    p.Y = before.Y + dy;
                    p.Z = GlView.Scene.SampleHeightWorld(p.X, p.Y);
                }
                GlView.Scene.NotifyPlacementsChanged();
                break;
            }
            case PlaceDrag.Rotate:
            {
                float delta = (px - _rotateStartPx) * 0.02f;
                foreach ((W3Placement p, TransformPlacementsCommand.State before) in _dragStates)
                    p.Angle = before.Angle - delta;
                GlView.Scene.NotifyPlacementsChanged();
                break;
            }
            case PlaceDrag.Box:
            {
                _boxEnd = new Avalonia.Point(px, py);
                UpdateSelectRectVisual(visible: true);
                break;
            }
        }
    }

    private void OnPlaceReleased(float px, float py, KeyModifiers modifiers)
    {
        switch (_placeDrag)
        {
            case PlaceDrag.Scatter:
                CommitScatter();
                break;
            case PlaceDrag.Move:
            case PlaceDrag.Rotate:
            {
                var entries = _dragStates
                    .Select(s => (s.P, s.Before, TransformPlacementsCommand.Capture(s.P)))
                    .ToList();
                var command = new TransformPlacementsCommand(entries);
                if (!command.IsNoOp)
                {
                    _history.Push(command);
                    AfterPlacementEdit(_placeDrag == PlaceDrag.Move ? "已移动所选摆放" : "已调整所选朝向");
                }
                _dragStates = new List<(W3Placement, TransformPlacementsCommand.State)>();
                break;
            }
            case PlaceDrag.Box:
            {
                UpdateSelectRectVisual(visible: false);
                SelectInBox(modifiers.HasFlag(KeyModifiers.Shift));
                break;
            }
        }
        _placeDrag = PlaceDrag.None;
    }

    private void PlaceGhostAt(float wx, float wy)
    {
        if (_ghost == null || _session.Map == null) return;
        W3Placement p = _ghost.Clone();
        p.RawRecord = null;
        p.X = wx;
        p.Y = wy;
        p.Z = GlView.Scene.SampleHeightWorld(wx, wy);
        p.CreationNumber = _session.NextCreationNumber();
        if (p.IsUnit)
            p.PlayerOwner = SelectedOwner();
        _session.ListFor(p).Add(p);
        _scatterPlaced.Add(p);
        RerollGhost(); // next placement gets fresh random rolls
        if (_ghost != null)
        {
            _ghost.X = wx;
            _ghost.Y = wy;
        }
        GlView.Scene.NotifyPlacementsChanged();
    }

    private void CommitScatter()
    {
        if (_scatterPlaced.Count == 0) return;
        _history.Push(new AddPlacementsCommand(_scatterPlaced));
        AfterPlacementEdit($"已放置 {_scatterPlaced.Count} 个（Ctrl+Z 撤销）");
        _scatterPlaced = new List<W3Placement>();
    }

    // ------------------------------------------------------------------
    // Box selection
    // ------------------------------------------------------------------

    private void UpdateSelectRectVisual(bool visible)
    {
        SelectRect.IsVisible = visible;
        if (!visible) return;
        double x = Math.Min(_boxStart.X, _boxEnd.X), y = Math.Min(_boxStart.Y, _boxEnd.Y);
        Canvas.SetLeft(SelectRect, x);
        Canvas.SetTop(SelectRect, y);
        SelectRect.Width = Math.Abs(_boxEnd.X - _boxStart.X);
        SelectRect.Height = Math.Abs(_boxEnd.Y - _boxStart.Y);
    }

    private void SelectInBox(bool additive)
    {
        float x0 = (float)Math.Min(_boxStart.X, _boxEnd.X), x1 = (float)Math.Max(_boxStart.X, _boxEnd.X);
        float y0 = (float)Math.Min(_boxStart.Y, _boxEnd.Y), y1 = (float)Math.Max(_boxStart.Y, _boxEnd.Y);
        if (x1 - x0 < 4 && y1 - y0 < 4) return; // just a click on empty ground
        float w = (float)GlView.Bounds.Width, h = (float)GlView.Bounds.Height;
        if (!additive) _selection.Clear();
        foreach (W3Placement p in _session.Doodads.Concat(_session.Units))
        {
            if (GlView.Scene.PlacementCenter(p) is not { } center) continue;
            if (GlView.Scene.ProjectWorld(center, w, h) is not { } screen) continue;
            if (screen.X < x0 || screen.X > x1 || screen.Y < y0 || screen.Y > y1) continue;
            if (!_selection.Contains(p))
                _selection.Add(p);
        }
        RefreshSelectionVisual();
        StatusLeft.Text = $"已选中 {_selection.Count} 个摆放";
    }

    // ------------------------------------------------------------------
    // Selection commands (keys)
    // ------------------------------------------------------------------

    /// <summary>Placement-mode keys; returns true when the key was consumed.</summary>
    private bool HandlePlacementKey(KeyEventArgs e)
    {
        if (_mode == EditMode.Terrain || !GlView.IsVisible) return false;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Space when _ghost != null || _placeSuspended:
                _placeSuspended = !_placeSuspended;
                GlView.Scene.Placements.Ghost = _placeSuspended ? null : _ghost;
                UpdatePlacementHint();
                return true;
            case Key.Escape:
                if (_armedDoodad != null || _armedUnit != null)
                {
                    DisarmPlacement();
                    UpdatePlacementHint();
                }
                else if (_selection.Count > 0)
                {
                    _selection.Clear();
                    RefreshSelectionVisual();
                }
                return true;
            case Key.Delete when _selection.Count > 0:
                DeleteSelection();
                return true;
            case Key.C when ctrl && _selection.Count > 0:
                _clipboard = _selection.Select(p => p.Clone()).ToList();
                StatusLeft.Text = $"已复制 {_clipboard.Count} 个摆放（Ctrl+V 粘贴到光标处）";
                return true;
            case Key.V when ctrl && _clipboard.Count > 0:
                PasteClipboard();
                return true;
            case Key.V when !ctrl && _selection.Count > 0:
                CycleVariation();
                return true;
            case Key.A when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selection.Count > 0:
                SelectAllSameCategory();
                return true;
            case Key.A when ctrl && _selection.Count > 0:
                SelectAllSameType();
                return true;
            case Key.A when ctrl:
                SelectAllPlacements();
                return true;
            case Key.OemPlus or Key.Add when _selection.Count > 0:
                ScaleSelection(1.1f);
                return true;
            case Key.OemMinus or Key.Subtract when _selection.Count > 0:
                ScaleSelection(1f / 1.1f);
                return true;
        }
        return false;
    }

    private void DeleteSelection()
    {
        var removed = _selection.ToList();
        _history.Push(RemovePlacementsCommand.Execute(_session, removed));
        _selection.Clear();
        RefreshSelectionVisual();
        AfterPlacementEdit($"已删除 {removed.Count} 个摆放（Ctrl+Z 撤销）");
    }

    private void PasteClipboard()
    {
        if (_session.Map == null || _clipboard.Count == 0) return;
        // Paste centered on the last hovered ground point, keeping relative layout.
        float cx = _clipboard.Average(p => p.X), cy = _clipboard.Average(p => p.Y);
        float tx = _lastHoverGround.X, ty = _lastHoverGround.Y;
        if (MathF.Abs(tx) < 0.001f && MathF.Abs(ty) < 0.001f)
        {
            tx = cx + 128f;
            ty = cy + 128f;
        }
        var pasted = new List<W3Placement>();
        foreach (W3Placement src in _clipboard)
        {
            W3Placement p = src.Clone();
            p.RawRecord = src.RawRecord; // keep hero stats/items payload on copies
            p.X = src.X - cx + tx;
            p.Y = src.Y - cy + ty;
            p.Z = GlView.Scene.SampleHeightWorld(p.X, p.Y);
            p.CreationNumber = _session.NextCreationNumber();
            _session.ListFor(p).Add(p);
            pasted.Add(p);
        }
        _history.Push(new AddPlacementsCommand(pasted));
        _selection.Clear();
        _selection.AddRange(pasted);
        RefreshSelectionVisual();
        AfterPlacementEdit($"已粘贴 {pasted.Count} 个摆放");
    }

    private void SelectAllSameType()
    {
        var ids = new HashSet<string>(_selection.Select(p => p.Id), StringComparer.Ordinal);
        _selection.Clear();
        foreach (W3Placement p in _session.Doodads.Concat(_session.Units))
            if (ids.Contains(p.Id))
                _selection.Add(p);
        RefreshSelectionVisual();
        StatusLeft.Text = $"已全选同类型：{_selection.Count} 个（{string.Join(",", ids)}）";
    }

    /// <summary>Ctrl+Shift+A: extend the selection to every placement in the same palette category.</summary>
    private void SelectAllSameCategory()
    {
        EnsureCatalog();
        var doodadCats = new HashSet<char>();
        var unitRaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (W3Placement p in _selection)
        {
            if (p.IsUnit)
                unitRaces.Add(_catalog!.FindUnit(p.Id)?.Race ?? "");
            else
                doodadCats.Add(_catalog!.FindDoodad(p.Id)?.Category ?? '?');
        }
        _selection.Clear();
        foreach (W3Placement p in _session.Doodads)
            if (doodadCats.Contains(_catalog!.FindDoodad(p.Id)?.Category ?? '?'))
                _selection.Add(p);
        foreach (W3Placement p in _session.Units)
            if (unitRaces.Contains(_catalog!.FindUnit(p.Id)?.Race ?? ""))
                _selection.Add(p);
        RefreshSelectionVisual();
        string cats = string.Join(",", doodadCats.Select(c => c.ToString()).Concat(unitRaces));
        StatusLeft.Text = $"已全选同分类：{_selection.Count} 个（{cats}）";
    }

    /// <summary>Ctrl+A with empty selection: select every doodad and unit on the map.</summary>
    private void SelectAllPlacements()
    {
        _selection.Clear();
        _selection.AddRange(_session.Doodads);
        _selection.AddRange(_session.Units);
        RefreshSelectionVisual();
        StatusLeft.Text = $"已全选地图全部摆放：{_selection.Count} 个";
    }

    private void ScaleSelection(float factor)
    {
        var entries = new List<(W3Placement, TransformPlacementsCommand.State, TransformPlacementsCommand.State)>();
        foreach (W3Placement p in _selection)
        {
            var before = TransformPlacementsCommand.Capture(p);
            p.ScaleX *= factor;
            p.ScaleY *= factor;
            p.ScaleZ *= factor;
            entries.Add((p, before, TransformPlacementsCommand.Capture(p)));
        }
        _history.Push(new TransformPlacementsCommand(entries));
        AfterPlacementEdit($"缩放 ×{factor:0.00}");
    }

    private void CycleVariation()
    {
        EnsureCatalog();
        var entries = new List<(W3Placement, TransformPlacementsCommand.State, TransformPlacementsCommand.State)>();
        foreach (W3Placement p in _selection.Where(p => !p.IsUnit))
        {
            int numVar = _catalog!.FindDoodad(p.Id)?.NumVar ?? 1;
            if (numVar <= 1) continue;
            var before = TransformPlacementsCommand.Capture(p);
            p.Variation = (p.Variation + 1) % numVar;
            entries.Add((p, before, TransformPlacementsCommand.Capture(p)));
        }
        if (entries.Count == 0) return;
        _history.Push(new TransformPlacementsCommand(entries));
        AfterPlacementEdit($"已切换 {entries.Count} 个装饰物的变体");
    }

    // ------------------------------------------------------------------
    // Shared plumbing
    // ------------------------------------------------------------------

    private void RefreshSelectionVisual()
    {
        GlView.Scene.Placements.Selection = _selection.ToArray();
    }

    /// <summary>Placement lists changed: dirty flag, 3D rebuild, side-panel counts.</summary>
    private void AfterPlacementEdit(string status)
    {
        _session.MarkDirty();
        GlView.Scene.NotifyPlacementsChanged();
        UpdateTitle();
        UpdateLayerCounts();
        StatusLeft.Text = status;
    }

    /// <summary>Prunes the selection after undo/redo removed placements.</summary>
    private void PruneSelection()
    {
        _selection.RemoveAll(p => !_session.Doodads.Contains(p) && !_session.Units.Contains(p));
        RefreshSelectionVisual();
    }

    // ------------------------------------------------------------------
    // Self test (--placetest): drives the real input pipeline end-to-end.
    // ------------------------------------------------------------------

    internal List<string> RunPlacementSelfTest()
    {
        var log = new List<string>();
        EnsureCatalog();
        if (_session.Map == null || !_session.IsFolder)
        {
            log.Add("FAIL: placetest 需要打开地图目录");
            return log;
        }
        log.Add($"catalog: {_catalog!.Doodads.Count} doodads, {_catalog.Units.Count} units");
        log.Add($"3D pass: rendered {GlView.Scene.Placements.RenderedCount}, missing-model {GlView.Scene.Placements.MissingModelCount}");

        SetMode(EditMode.Doodads);
        DoodadTypeInfo? info = _catalog.FindDoodad("LTlt")
            ?? _catalog.Doodads.FirstOrDefault(d => d.InTileset(_session.Map.Tileset) && d.ModelStem.Length > 0);
        if (info == null)
        {
            log.Add("FAIL: 调色板为空（未找到 war3 资产），跳过摆放测试");
            return log;
        }
        _armedUnit = null;
        _armedDoodad = info;
        RerollGhost();
        float w = (float)GlView.Bounds.Width, h = (float)GlView.Bounds.Height;
        int before = _session.Doodads.Count;

        // Two click-places through the real press/release path.
        OnPlacePressed(w * 0.42f, h * 0.5f, KeyModifiers.None);
        OnPlaceReleased(w * 0.42f, h * 0.5f, KeyModifiers.None);
        OnPlacePressed(w * 0.58f, h * 0.5f, KeyModifiers.None);
        OnPlaceReleased(w * 0.58f, h * 0.5f, KeyModifiers.None);
        int placed = _session.Doodads.Count - before;
        log.Add(placed == 2 ? $"OK: placed 2 × {info.Id} ({info.Name})" : $"FAIL: expected 2 placements, got {placed}");

        // Undo/redo through the shared command stack.
        _history.Undo(_session);
        _history.Undo(_session);
        log.Add(_session.Doodads.Count == before ? "OK: undo ×2 removed placements" : $"FAIL: undo left {_session.Doodads.Count - before}");
        _history.Redo(_session);
        _history.Redo(_session);
        log.Add(_session.Doodads.Count == before + 2 ? "OK: redo ×2 restored placements" : "FAIL: redo mismatch");

        // Ray-pick the second placement, move it, delete it, undo the delete.
        GlView.Scene.NotifyPlacementsChanged();
        W3Placement? hit = GlView.Scene.PickPlacement(w * 0.58f, h * 0.5f, w, h);
        log.Add(hit != null ? $"OK: pick hit {hit.Id} @({hit.X:0},{hit.Y:0})" : "WARN: pick missed (bounds/occlusion)");
        W3Placement target = _session.Doodads[^1];
        _selection.Clear();
        _selection.Add(target);
        RefreshSelectionVisual();
        float oldX = target.X;
        var beforeState = TransformPlacementsCommand.Capture(target);
        target.X += 256f;
        _history.Push(new TransformPlacementsCommand(
            new List<(W3Placement, TransformPlacementsCommand.State, TransformPlacementsCommand.State)>
                { (target, beforeState, TransformPlacementsCommand.Capture(target)) }));
        _history.Undo(_session);
        log.Add(MathF.Abs(target.X - oldX) < 0.01f ? "OK: move undo restored X" : "FAIL: move undo");
        _history.Redo(_session);
        DeleteSelection();
        log.Add(_session.Doodads.Count == before + 1 ? "OK: delete removed 1" : "FAIL: delete");
        _history.Undo(_session);
        log.Add(_session.Doodads.Count == before + 2 ? "OK: undo restored deleted" : "FAIL: undo delete");

        // M3 batch select: same type → same category → select all.
        _selection.Clear();
        _selection.Add(_session.Doodads.First(p => p.Id == "LTlt"));
        SelectAllSameType();
        int sameType = _selection.Count;
        log.Add(sameType > 1 ? $"OK: Ctrl+A 同类型 {sameType} 个" : "FAIL: 同类型全选");
        SelectAllSameCategory();
        int sameCat = _selection.Count;
        log.Add(sameCat >= sameType ? $"OK: Ctrl+Shift+A 同分类 {sameCat} 个" : "FAIL: 同分类全选");
        _selection.Clear();
        SelectAllPlacements();
        log.Add(_selection.Count == _session.Doodads.Count + _session.Units.Count
            ? $"OK: 空选 Ctrl+A 全选 {_selection.Count} 个"
            : "FAIL: 全选");
        _selection.Clear();
        RefreshSelectionVisual();

        // Save → reparse roundtrip (byte-fidelity for untouched records).
        byte[] serializedDoodads = _session.DoodadsDoc.Write();
        var reparsed = W3DooFile.ParseDoodadsDocument(
            serializedDoodads, _session.DoodadsDoc.HasSkinIds);
        log.Add(reparsed.Placements.Count == _session.Doodads.Count
            ? $"OK: doo save/parse roundtrip ({reparsed.Placements.Count} records)"
            : $"FAIL: roundtrip {reparsed.Placements.Count} != {_session.Doodads.Count}");
        DisarmPlacement();
        SetMode(EditMode.Terrain);
        return log;
    }

    /// <summary>
    /// M3 acceptance (--foresttest): scatter a serpentine forest through the real
    /// press/drag/release pipeline, then verify the result is random (variations,
    /// angles, scales) and organic (not collinear), with spacing respected.
    /// Leaves the forest placed so --shot3d captures it.
    /// </summary>
    internal List<string> RunForestSelfTest()
    {
        var log = new List<string>();
        EnsureCatalog();
        if (_session.Map == null)
        {
            log.Add("FAIL: foresttest 需要打开地图");
            return log;
        }
        DoodadTypeInfo? tree = _catalog!.FindDoodad("LTlt")
            ?? _catalog.Doodads.FirstOrDefault(d =>
                d.Category == 'D' && d.InTileset(_session.Map.Tileset) && d.NumVar > 1 && d.ModelStem.Length > 0);
        if (tree == null)
        {
            log.Add("FAIL: 找不到可散布的树类型");
            return log;
        }

        SetMode(EditMode.Doodads);
        _armedUnit = null;
        _armedDoodad = tree;
        ChkRandVariation.IsChecked = true;
        ChkRandRotation.IsChecked = true;
        ChkRandScale.IsChecked = true;
        RerollGhost();

        float w = (float)GlView.Bounds.Width, h = (float)GlView.Bounds.Height;
        int before = _session.Doodads.Count;

        // Serpentine drag: left → right with three vertical S-waves.
        const int steps = 220;
        float StartX(float t) => w * (0.12f + 0.76f * t);
        float StartY(float t) => h * (0.50f + 0.27f * MathF.Sin(t * 3f * MathF.PI));
        OnPlacePressed(StartX(0), StartY(0), KeyModifiers.None);
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            OnPlaceDragged(StartX(t), StartY(t), KeyModifiers.None);
        }
        OnPlaceReleased(StartX(1), StartY(1), KeyModifiers.None);

        var placed = _session.Doodads.Skip(before).ToList();
        log.Add($"scatter: {placed.Count} × {tree.Id} ({tree.Name}), spacing={ScatterSpacingWorld():0} wu");
        log.Add(placed.Count >= 15 ? "OK: 数量 ≥ 15" : $"FAIL: 数量不足（{placed.Count}）");

        // Randomness: variations, angles, scales must not repeat into uniformity.
        int variations = placed.Select(p => p.Variation).Distinct().Count();
        int angles = placed.Select(p => (int)(p.Angle * 180f / MathF.PI) / 10).Distinct().Count();
        int scales = placed.Select(p => (int)(p.ScaleX * 100f)).Distinct().Count();
        log.Add(tree.NumVar > 1
            ? (variations >= 2 ? $"OK: 变体随机（{variations}/{tree.NumVar} 种）" : "FAIL: 变体未随机")
            : $"SKIP: 该类型仅 1 变体");
        log.Add(tree.FixedRot >= 0f
            ? $"SKIP: 该类型固定朝向 {tree.FixedRot:0}°（WE fixedRot 语义）"
            : angles >= Math.Min(6, placed.Count / 3)
                ? $"OK: 旋转随机（{angles} 个 10° 桶）"
                : $"FAIL: 旋转过于一致（{angles} 桶）");
        log.Add(tree.RandomScale
            ? (scales >= 3 ? $"OK: 缩放随机（{scales} 档）" : $"FAIL: 缩放未随机（{scales} 档）")
            : "SKIP: 该类型无随机缩放");

        // Spacing: no pair closer than 90% of the configured minimum.
        float minSpacing = float.MaxValue;
        for (int i = 0; i < placed.Count; i++)
        for (int j = i + 1; j < placed.Count; j++)
        {
            float dx = placed[i].X - placed[j].X, dy = placed[i].Y - placed[j].Y;
            minSpacing = MathF.Min(minSpacing, MathF.Sqrt(dx * dx + dy * dy));
        }
        log.Add(minSpacing >= ScatterSpacingWorld() * 0.9f
            ? $"OK: 最小间距 {minSpacing:0} wu"
            : $"FAIL: 间距过近（{minSpacing:0} wu）");

        // Organic layout: max perpendicular deviation from the best-fit line
        // must be well over a tile (a straight row would be ~0).
        if (placed.Count >= 3)
        {
            float mx = placed.Average(p => p.X), my = placed.Average(p => p.Y);
            double sxx = placed.Sum(p => (p.X - mx) * (double)(p.X - mx));
            double sxy = placed.Sum(p => (p.X - mx) * (double)(p.Y - my));
            double angle = Math.Atan2(sxy, sxx);
            float maxDev = placed.Max(p =>
                MathF.Abs((float)(-(p.X - mx) * Math.Sin(angle) + (p.Y - my) * Math.Cos(angle))));
            log.Add(maxDev > 256f
                ? $"OK: 非直线分布（最大偏离 {maxDev:0} wu）"
                : $"FAIL: 近似成排（偏离仅 {maxDev:0} wu）");
        }
        return log;
    }

    private void UpdateLayerCounts()
    {
        var map = _session.Map;
        if (map == null) return;
        LayerList.Items.Clear();
        LayerList.Items.Add(new ListBoxItem { Content = $"地形（{map.TileCountX}×{map.TileCountY}）" });
        LayerList.Items.Add(new ListBoxItem { Content = $"装饰物（{_session.Doodads.Count}）" });
        LayerList.Items.Add(new ListBoxItem { Content = $"单位（{_session.Units.Count}）" });
        LayerList.Items.Add(new ListBoxItem
        {
            Content = _session.Pathing != null ? $"寻路（{_session.Pathing.Width}×{_session.Pathing.Height}）" : "寻路（无）",
        });
    }
}
