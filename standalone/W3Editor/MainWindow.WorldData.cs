using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using W3Editor.Formats;
using W3Editor.Rendering;
using W3Editor.Services;

namespace W3Editor;

/// <summary>Pathing and region editing routed through the shared 2D/3D viewport.</summary>
public partial class MainWindow
{
    private bool _pathingStrokeActive;
    private bool _pathingStrokeChanged;
    private bool _regionStrokeActive;
    private W3RegionRecord[]? _regionBefore;
    private (float X, float Y) _regionStartWorld;
    private (float X, float Y) _regionEndWorld;
    private Avalonia.Point _regionScreenStart;
    private Avalonia.Point _regionScreenEnd;

    private sealed record RegionListEntry(W3RegionRecord Region)
    {
        public override string ToString() =>
            $"{Region.Name}  [{Region.Left:0},{Region.Bottom:0} – {Region.Right:0},{Region.Top:0}]";
    }

    private void ActivatePathingMode()
    {
        if (!_session.IsOpen)
            return;
        if (_session.IsNativeFormat)
        {
            StatusLeft.Text = "d1map 当前不承载 War3 WPM；请打开 War3 地图目录。";
            return;
        }
        MenuOverlayPathing.IsChecked = true;
        SliderPathingRadius.IsEnabled = true;
        ChkNoWalk.IsEnabled = ChkNoFly.IsEnabled = ChkNoBuild.IsEnabled = true;
        PathingHint.Text = _session.Pathing == null
            ? "首次落笔会创建与地形对齐的空 war3map.wpm。"
            : $"已加载 wpm：{_session.Pathing.Width}×{_session.Pathing.Height} 格。";
        RenderViewport();
        StatusLeft.Text = "寻路模式：左键添加标志，右键（3D 为 Ctrl+左键）清除。";
    }

    private void ActivateRegionsMode()
    {
        if (_session.FormatKind != Services.MapFormatKind.War3Folder)
        {
            StatusLeft.Text = "区域编辑需要打开已解压的 War3 地图目录。";
            return;
        }
        RefreshRegionsUi();
        RenderViewport();
        StatusLeft.Text = "区域模式：在 2D 或 3D 地面按住左键拖出矩形。";
    }

    private void OnPathingOptionChanged(object? sender, RoutedEventArgs e)
    {
        MenuOverlayPathing.IsChecked = true;
        RenderViewport();
    }

    private byte PathingMask()
    {
        int mask = 0;
        if (ChkNoWalk.IsChecked == true) mask |= W3PathingMap.Unwalkable;
        if (ChkNoFly.IsChecked == true) mask |= W3PathingMap.Unflyable;
        if (ChkNoBuild.IsChecked == true) mask |= W3PathingMap.Unbuildable;
        return (byte)mask;
    }

    private bool TryBeginWorldDataStroke(PointerPressedEventArgs e)
    {
        if (_mode is not (EditMode.Pathing or EditMode.Regions) || GlView.IsVisible)
            return false;
        var props = e.GetCurrentPoint(ViewportPanel).Properties;
        if (props.IsMiddleButtonPressed || (!props.IsLeftButtonPressed && !props.IsRightButtonPressed))
            return false;
        if (!TryPointToCorner(e.GetPosition(ViewportImage), out int cx, out int cy))
            return true;

        if (_mode == EditMode.Pathing)
        {
            BeginPathingStroke();
            ApplyPathingAtCorner(cx, cy, props.IsRightButtonPressed);
        }
        else
        {
            if (!props.IsLeftButtonPressed)
                return true;
            BeginRegionStroke(cx, cy);
            _regionScreenStart = _regionScreenEnd = e.GetPosition(ViewportPanel);
            UpdateRegionRectVisual();
        }
        e.Pointer.Capture(ViewportPanel);
        return true;
    }

    private bool TryMoveWorldDataStroke(PointerEventArgs e)
    {
        if (_pathingStrokeActive)
        {
            if (TryPointToCorner(e.GetPosition(ViewportImage), out int cx, out int cy))
            {
                var props = e.GetCurrentPoint(ViewportPanel).Properties;
                ApplyPathingAtCorner(cx, cy, props.IsRightButtonPressed);
            }
            return true;
        }
        if (_regionStrokeActive)
        {
            if (TryPointToCorner(e.GetPosition(ViewportImage), out int cx, out int cy))
                UpdateRegionEnd(cx, cy);
            _regionScreenEnd = e.GetPosition(ViewportPanel);
            UpdateRegionRectVisual();
            return true;
        }
        return false;
    }

    private bool TryEndWorldDataStroke(PointerReleasedEventArgs e)
    {
        if (_pathingStrokeActive)
        {
            EndPathingStroke();
            e.Pointer.Capture(null);
            return true;
        }
        if (_regionStrokeActive)
        {
            if (TryPointToCorner(e.GetPosition(ViewportImage), out int cx, out int cy))
                UpdateRegionEnd(cx, cy);
            EndRegionStroke();
            e.Pointer.Capture(null);
            return true;
        }
        return false;
    }

    private void BeginPathingStroke()
    {
        if (_pathingStrokeActive)
            return;
        W3PathingMap map = _session.EnsurePathing();
        _history.BeginPathingStroke(map);
        _pathingStrokeActive = true;
        _pathingStrokeChanged = false;
        MenuOverlayPathing.IsEnabled = true;
        MenuOverlayPathing.IsChecked = true;
        ChkNoWalk.IsEnabled = ChkNoFly.IsEnabled = ChkNoBuild.IsEnabled = true;
    }

    private void ApplyPathingAtCorner(float cornerX, float cornerY, bool erase)
    {
        if (!_pathingStrokeActive)
        {
            try { BeginPathingStroke(); }
            catch (InvalidOperationException ex) { StatusLeft.Text = ex.Message; return; }
        }
        W3PathingMap map = _session.Pathing!;
        byte mask = PathingMask();
        if (mask == 0)
        {
            StatusLeft.Text = "请至少选择一个寻路标志。";
            return;
        }
        int centerX = Math.Clamp((int)MathF.Round(cornerX * 4f), 0, map.Width - 1);
        int centerY = Math.Clamp((int)MathF.Round(cornerY * 4f), 0, map.Height - 1);
        int radius = (int)SliderPathingRadius.Value;
        bool changed = false;
        for (int y = Math.Max(0, centerY - radius); y <= Math.Min(map.Height - 1, centerY + radius); y++)
        for (int x = Math.Max(0, centerX - radius); x <= Math.Min(map.Width - 1, centerX + radius); x++)
        {
            int dx = x - centerX, dy = y - centerY;
            if (dx * dx + dy * dy > radius * radius)
                continue;
            int index = y * map.Width + x;
            byte old = map.Cells[index];
            byte next = erase ? (byte)(old & ~mask) : (byte)(old | mask);
            if (old == next) continue;
            map.Cells[index] = next;
            changed = true;
        }
        _pathingStrokeChanged |= changed;
        if (changed)
            RenderViewport();
    }

    private bool EndPathingStroke()
    {
        if (!_pathingStrokeActive)
            return false;
        W3PathingMap map = _session.Pathing!;
        _history.CommitPathingStroke(map);
        _pathingStrokeActive = false;
        if (_pathingStrokeChanged)
        {
            _session.MarkPathingDirty();
            RenderViewport();
            UpdateTitle();
            UpdateLayerCounts();
            PathingHint.Text = $"已编辑 wpm：{map.Width}×{map.Height} 格（未保存）。";
            StatusLeft.Text = "寻路笔画完成（Ctrl+Z 撤销，Ctrl+S 保存）。";
        }
        return true;
    }

    private void BeginRegionStroke(float cornerX, float cornerY)
    {
        try
        {
            W3RegionsFile regions = _session.EnsureRegions();
            _regionBefore = regions.CloneRegions();
        }
        catch (InvalidOperationException ex)
        {
            StatusLeft.Text = ex.Message;
            return;
        }
        _regionStrokeActive = true;
        _regionStartWorld = _regionEndWorld = CornerToWorld(cornerX, cornerY);
    }

    private void ApplyRegionAtCorner(float cornerX, float cornerY)
    {
        if (!_regionStrokeActive)
            BeginRegionStroke(cornerX, cornerY);
        if (_regionStrokeActive)
            UpdateRegionEnd(cornerX, cornerY);
    }

    private void UpdateRegionEnd(float cornerX, float cornerY) =>
        _regionEndWorld = CornerToWorld(cornerX, cornerY);

    private (float X, float Y) CornerToWorld(float cornerX, float cornerY)
    {
        W3EMap map = _session.Map!;
        return (map.CenterOffsetX + cornerX * W3EMap.TileSize,
            map.CenterOffsetY + cornerY * W3EMap.TileSize);
    }

    private bool EndRegionStroke()
    {
        if (!_regionStrokeActive)
            return false;
        _regionStrokeActive = false;
        SelectRect.IsVisible = false;
        W3RegionsFile regions = _session.Regions!;
        float left = Math.Min(_regionStartWorld.X, _regionEndWorld.X);
        float right = Math.Max(_regionStartWorld.X, _regionEndWorld.X);
        float bottom = Math.Min(_regionStartWorld.Y, _regionEndWorld.Y);
        float top = Math.Max(_regionStartWorld.Y, _regionEndWorld.Y);
        if (right - left < 1f || top - bottom < 1f)
        {
            _regionBefore = null;
            StatusLeft.Text = "区域过小，已取消。";
            return true;
        }

        byte[][] colors =
        {
            new byte[] { 177, 84, 84 }, new byte[] { 185, 122, 86 },
            new byte[] { 181, 157, 84 }, new byte[] { 122, 158, 90 },
            new byte[] { 84, 158, 146 }, new byte[] { 84, 122, 168 },
            new byte[] { 122, 98, 168 }, new byte[] { 164, 92, 146 },
        };
        byte[] color = colors[regions.Regions.Count % colors.Length];
        regions.Regions.Add(new W3RegionRecord
        {
            Left = left, Bottom = bottom, Right = right, Top = top,
            Name = regions.NextName(), CreationNumber = regions.NextCreationNumber(),
            R = color[0], G = color[1], B = color[2], A = 255,
        });
        _history.Push(new RegionsSnapshotCommand(_regionBefore ?? Array.Empty<W3RegionRecord>(), regions.Regions));
        _regionBefore = null;
        _session.MarkRegionsDirty();
        RefreshRegionsUi(regions.Regions[^1].CreationNumber);
        RenderViewport();
        UpdateTitle();
        StatusLeft.Text = $"已创建区域 {regions.Regions[^1].Name}（Ctrl+Z 撤销）。";
        return true;
    }

    private void UpdateRegionRectVisual()
    {
        SelectRect.IsVisible = _regionStrokeActive;
        double x = Math.Min(_regionScreenStart.X, _regionScreenEnd.X);
        double y = Math.Min(_regionScreenStart.Y, _regionScreenEnd.Y);
        Canvas.SetLeft(SelectRect, x);
        Canvas.SetTop(SelectRect, y);
        SelectRect.Width = Math.Abs(_regionScreenEnd.X - _regionScreenStart.X);
        SelectRect.Height = Math.Abs(_regionScreenEnd.Y - _regionScreenStart.Y);
    }

    private void RefreshRegionsUi(int? selectCreation = null)
    {
        int? previous = selectCreation
            ?? (RegionList.SelectedItem as RegionListEntry)?.Region.CreationNumber;
        RegionList.Items.Clear();
        if (_session.Regions == null)
        {
            RegionsHint.Text = _session.FormatKind == Services.MapFormatKind.War3Folder
                ? "该地图没有 war3map.w3r；首次绘制时自动创建。"
                : "区域编辑仅适用于 War3 地图目录。";
            RegionName.IsEnabled = BtnRenameRegion.IsEnabled = BtnDeleteRegion.IsEnabled = false;
            return;
        }
        foreach (W3RegionRecord region in _session.Regions.Regions)
        {
            var entry = new RegionListEntry(region);
            RegionList.Items.Add(entry);
            if (region.CreationNumber == previous)
                RegionList.SelectedItem = entry;
        }
        RegionsHint.Text = $"区域 {_session.Regions.Regions.Count} 个。";
    }

    private void OnRegionSelected(object? sender, SelectionChangedEventArgs e)
    {
        W3RegionRecord? region = (RegionList.SelectedItem as RegionListEntry)?.Region;
        bool selected = region != null;
        RegionName.IsEnabled = BtnRenameRegion.IsEnabled = BtnDeleteRegion.IsEnabled = selected;
        RegionName.Text = region?.Name ?? "";
    }

    private void OnRenameRegion(object? sender, RoutedEventArgs e)
    {
        if (_session.Regions == null || RegionList.SelectedItem is not RegionListEntry entry)
            return;
        string name = RegionName.Text?.Trim() ?? "";
        if (name.Length == 0 || name == entry.Region.Name)
            return;
        W3RegionRecord[] before = _session.Regions.CloneRegions();
        entry.Region.Name = name;
        _history.Push(new RegionsSnapshotCommand(before, _session.Regions.Regions));
        _session.MarkRegionsDirty();
        RefreshRegionsUi(entry.Region.CreationNumber);
        UpdateTitle();
        StatusLeft.Text = $"区域已重命名为 {name}。";
    }

    private void OnDeleteRegion(object? sender, RoutedEventArgs e)
    {
        if (_session.Regions == null || RegionList.SelectedItem is not RegionListEntry entry)
            return;
        W3RegionRecord[] before = _session.Regions.CloneRegions();
        _session.Regions.Regions.Remove(entry.Region);
        _history.Push(new RegionsSnapshotCommand(before, _session.Regions.Regions));
        _session.MarkRegionsDirty();
        RefreshRegionsUi();
        RenderViewport();
        UpdateTitle();
        StatusLeft.Text = $"已删除区域 {entry.Region.Name}（Ctrl+Z 撤销）。";
    }
}
