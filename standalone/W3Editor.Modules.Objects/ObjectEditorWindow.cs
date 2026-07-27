using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using W3Editor.Contracts;

namespace W3Editor.Modules.Objects;

internal sealed class ObjectEditorWindow : Window
{
    private readonly IModuleContext _context;
    private readonly ObjectMetadataCatalog _metadata;
    private readonly ComboBox _kind = new();
    private readonly ComboBox _group = new() { Width = 150 };
    private readonly CheckBox _skin = new() { Content = "Skin 文件" };
    private readonly CheckBox _modifiedOnly = new() { Content = "仅修改/自定义" };
    private readonly TextBox _search = new() { Watermark = "搜索对象 ID / 字段 / 值", Width = 250 };
    private readonly TextBox _baseId = new() { Width = 68, MaxLength = 4, Text = "hfoo" };
    private readonly ListBox _objects = new();
    private readonly ListBox _mods = new();
    private readonly TextBox _field = new() { MaxLength = 4 };
    private readonly ComboBox _fieldPicker = new();
    private readonly ComboBox _type = new() { ItemsSource = new[] { "0 · Integer", "1 · Real", "2 · Unreal", "3 · String" } };
    private readonly NumericUpDown _level = new() { Minimum = 0, Maximum = int.MaxValue };
    private readonly NumericUpDown _pointer = new() { Minimum = 0, Maximum = int.MaxValue };
    private readonly TextBox _value = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 4), Foreground = Brushes.Gray };
    private bool _refreshing;

    public ObjectEditorWindow(IModuleContext context)
    {
        _context = context;
        _metadata = new ObjectMetadataCatalog(context.Capabilities.Get<IWarcraftObjectDatabase>());
        Title = "W3 对象编辑器";
        Width = 1180;
        Height = 760;
        MinWidth = 900;
        MinHeight = 560;
        Content = BuildUi();

        _kind.ItemsSource = ObjectKinds.All.Select(x => x.Title).ToArray();
        _kind.SelectedIndex = 0;
        _type.SelectedIndex = 3;
        _kind.SelectionChanged += (_, _) => { UpdateDefaultBase(); RefreshGroups(); RefreshObjects(); };
        _group.SelectionChanged += (_, _) => RefreshObjects();
        _skin.IsCheckedChanged += (_, _) => RefreshObjects();
        _modifiedOnly.IsCheckedChanged += (_, _) => RefreshObjects();
        _search.TextChanged += (_, _) => RefreshObjects();
        _objects.SelectionChanged += (_, _) => RefreshModifications();
        _mods.SelectionChanged += (_, _) => RefreshProperty();
        _fieldPicker.SelectionChanged += (_, _) => SelectMetadataField();
        _context.DocumentChanged += OnDocumentChanged;
        Closed += (_, _) => _context.DocumentChanged -= OnDocumentChanged;
        RefreshObjects();
    }

    private Control BuildUi()
    {
        var root = new DockPanel();
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbar.Children.Add(new TextBlock { Text = "类别", VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(_kind);
        toolbar.Children.Add(new TextBlock { Text = "分类", VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(_group);
        toolbar.Children.Add(_skin);
        toolbar.Children.Add(_modifiedOnly);
        toolbar.Children.Add(_search);
        toolbar.Children.Add(new Separator { Width = 8 });
        toolbar.Children.Add(new TextBlock { Text = "基对象", VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(_baseId);
        toolbar.Children.Add(Button("新建自定义", AddCustom));
        toolbar.Children.Add(Button("复制", CloneCustom));
        toolbar.Children.Add(Button("删除", DeleteCustom));
        toolbar.Children.Add(Button("撤销", Undo));
        toolbar.Children.Add(Button("重做", Redo));
        toolbar.Children.Add(Button("保存地图", SaveMap));
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        var grid = new Grid { Margin = new Thickness(8, 0, 8, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(280)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(360)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.Children.Add(Group("对象（地图修改 + 自定义）", _objects, 0));
        grid.Children.Add(Group("修改字段", _mods, 1));
        grid.Children.Add(BuildPropertyPane());
        root.Children.Add(grid);
        return root;
    }

    private Control BuildPropertyPane()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = "属性", FontSize = 16, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(Labelled("从 MetaData 选择字段", _fieldPicker));
        panel.Children.Add(Labelled("字段 4CC", _field));
        panel.Children.Add(Labelled("类型", _type));
        panel.Children.Add(Labelled("等级 / Variation", _level));
        panel.Children.Add(Labelled("Data pointer", _pointer));
        panel.Children.Add(Labelled("值", _value));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Button("应用修改", ApplyProperty));
        row.Children.Add(Button("新增字段", AddModification));
        row.Children.Add(Button("删除字段", DeleteModification));
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Text = "格式规则与 HiveWE modification_tables 一致：Ability、Doodad、Upgrade 使用额外的等级/Variation 与 Data pointer。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray,
        });
        var border = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = panel, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(border, 2);
        return border;
    }

    private static Control Group(string title, Control content, int column)
    {
        var panel = new DockPanel();
        var heading = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Margin = new Thickness(6) };
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);
        panel.Children.Add(content);
        var border = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = panel, Margin = new Thickness(0, 0, 6, 0) };
        Grid.SetColumn(border, column);
        return border;
    }

    private static Control Labelled(string label, Control control)
    {
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.Gray });
        panel.Children.Add(control);
        return panel;
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }

    private ObjectKind CurrentKind => (ObjectKind)Math.Max(0, _kind.SelectedIndex);
    private bool IsSkin => _skin.IsChecked == true;
    private ObjectDataPart? Part => _context.Document?.GetPart<ObjectDataPart>();
    private ObjectDataFile? CurrentFile(bool create = false) => create
        ? Part?.EnsureFile(CurrentKind, IsSkin)
        : Part?.GetFile(CurrentKind, IsSkin);

    private void OnDocumentChanged(IMapDocument? _) => Dispatcher.UIThread.Post(RefreshObjects);

    private void RefreshObjects()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            ObjectDataFile? file = CurrentFile();
            string query = _search.Text?.Trim() ?? "";
            var rowsList = new List<RecordRow>();
            var originalById = file?.OriginalTable.ToDictionary(record => record.OriginalText, StringComparer.OrdinalIgnoreCase)
                               ?? new Dictionary<string, ObjectRecord>(StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BaseObjectInfo info in _metadata.Objects(CurrentKind))
            {
                if (_group.SelectedIndex > 0 && !string.Equals(info.Group, _group.SelectedItem as string, StringComparison.OrdinalIgnoreCase))
                    continue;
                known.Add(info.Id);
                bool persisted = originalById.TryGetValue(info.Id, out ObjectRecord? record);
                record ??= new ObjectRecord { OriginalId = FourCC.Parse(info.Id), IsCustom = false };
                rowsList.Add(new RecordRow(record, false, rowsList.Count, info.Name, persisted));
            }
            if (file != null)
            {
                foreach (ObjectRecord record in file.OriginalTable.Where(record => !known.Contains(record.OriginalText)))
                    rowsList.Add(new RecordRow(record, false, rowsList.Count, record.OriginalText, true));
                foreach (ObjectRecord record in file.CustomTable)
                    rowsList.Add(new RecordRow(record, true, rowsList.Count, record.ModifiedText, true));
            }
            IEnumerable<RecordRow> rows = rowsList;
            if (_modifiedOnly.IsChecked == true)
                rows = rows.Where(row => row.IsCustom || row.Persisted);
            if (query.Length > 0)
                rows = rows.Where(row => row.Record.DisplayId.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.Record.OriginalText.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.Record.Modifications.Any(mod => mod.FieldText.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || mod.ValueText.Contains(query, StringComparison.OrdinalIgnoreCase)));
            _objects.ItemsSource = rows.ToArray();
            _status.Text = file == null
                ? $"当前地图没有 {ObjectKinds.FileName(CurrentKind, IsSkin)}；新建自定义对象时会创建。"
                : $"{file.FileName} · v{file.Version} · 基础对象 {_metadata.Objects(CurrentKind).Count} · 原始对象修改 {file.OriginalTable.Count} · 自定义对象 {file.CustomTable.Count}"
                    + (file.IsDirty ? " · 未保存" : "");
        }
        catch (Exception ex) { _status.Text = "读取失败：" + ex.Message; }
        finally { _refreshing = false; }
        RefreshModifications();
    }

    private void RefreshGroups()
    {
        string[] groups = new[] { "全部" }.Concat(_metadata.Objects(CurrentKind)
            .Select(info => string.IsNullOrWhiteSpace(info.Group) ? "未分类" : info.Group)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)).ToArray();
        _group.ItemsSource = groups;
        _group.SelectedIndex = 0;
    }

    private void RefreshModifications()
    {
        RecordRow? row = _objects.SelectedItem as RecordRow;
        _mods.ItemsSource = row?.Record.Modifications.Select((mod, index) => new ModificationRow(
            mod, index, _metadata.Field(CurrentKind, mod.FieldText)?.DisplayName ?? mod.FieldText)).ToArray()
                            ?? Array.Empty<ModificationRow>();
        RefreshProperty();
    }

    private void RefreshProperty()
    {
        _refreshing = true;
        try
        {
            _fieldPicker.ItemsSource = _metadata.Fields(CurrentKind).Select(field => new FieldChoice(field)).ToArray();
            ObjectModification? mod = (_mods.SelectedItem as ModificationRow)?.Modification;
            _field.Text = mod?.FieldText ?? "";
            _type.SelectedIndex = mod == null ? 3 : (int)Math.Min(mod.ValueType, 3);
            _level.Value = mod?.LevelOrVariation ?? 0;
            _pointer.Value = mod?.DataPointer ?? 0;
            _value.Text = mod?.ValueText ?? "";
            bool optional = ObjectKinds.Get(CurrentKind).OptionalInts;
            _level.IsEnabled = optional;
            _pointer.IsEnabled = optional;
        }
        finally { _refreshing = false; }
    }

    private void SelectMetadataField()
    {
        if (_refreshing || _fieldPicker.SelectedItem is not FieldChoice choice) return;
        _field.Text = choice.Metadata.Id;
        _type.SelectedIndex = (int)TypeCode(choice.Metadata.Type);
    }

    private void Edit(string label, Action<ObjectDataFile> mutation)
    {
        ObjectDataPart? part = Part;
        IMapDocument? document = _context.Document;
        if (part == null || document == null) { _status.Text = "请先打开地图。"; return; }
        using IEditTransaction edit = document.BeginEdit(label);
        edit.RecordBefore(part, new ObjectDataSnapshotDelta(part.Capture()));
        try
        {
            ObjectDataFile file = part.EnsureFile(CurrentKind, IsSkin);
            mutation(file);
            part.Touch(file);
            edit.Commit();
            RefreshObjects();
        }
        catch (Exception ex)
        {
            edit.Rollback();
            _status.Text = label + "失败：" + ex.Message;
        }
    }

    private void AddCustom() => Edit("新建自定义对象", file =>
    {
        var record = new ObjectRecord
        {
            OriginalId = FourCC.Parse(_baseId.Text ?? ""),
            ModifiedId = FourCC.Parse(NextCustomId(file)), IsCustom = true,
        };
        file.CustomTable.Add(record);
    });

    private void CloneCustom()
    {
        if (_objects.SelectedItem is not RecordRow selected) { _status.Text = "请先选择对象。"; return; }
        Edit("复制自定义对象", file =>
        {
            ObjectRecord clone = selected.Record.Clone();
            clone.OriginalId = selected.Record.IsCustom ? selected.Record.OriginalId.ToArray() : selected.Record.OriginalId.ToArray();
            clone.ModifiedId = FourCC.Parse(NextCustomId(file));
            clone.IsCustom = true;
            file.CustomTable.Add(clone);
        });
    }

    private void DeleteCustom()
    {
        if (_objects.SelectedItem is not RecordRow { IsCustom: true } selected) { _status.Text = "只能删除自定义对象。"; return; }
        Edit("删除自定义对象", file => file.CustomTable.Remove(selected.Record));
    }

    private void AddModification()
    {
        if (_objects.SelectedItem is not RecordRow selected) { _status.Text = "请先选择对象。"; return; }
        Edit("新增对象字段", file =>
        {
            EnsurePersisted(file, selected);
            FieldMetadata? metadata = _metadata.Fields(CurrentKind).FirstOrDefault();
            selected.Record.Modifications.Add(new ObjectModification
            {
                FieldId = FourCC.Parse(metadata?.Id ?? "unam"),
                ValueType = TypeCode(metadata?.Type), StringValue = "", ValueChanged = true,
            });
        });
    }

    private void DeleteModification()
    {
        if (_objects.SelectedItem is not RecordRow selected || _mods.SelectedItem is not ModificationRow mod) return;
        Edit("删除对象字段", _ => selected.Record.Modifications.Remove(mod.Modification));
    }

    private void ApplyProperty()
    {
        if (_mods.SelectedItem is not ModificationRow selected) { _status.Text = "请先选择修改字段。"; return; }
        Edit("修改对象属性", _ =>
        {
            ObjectModification mod = selected.Modification;
            mod.FieldText = _field.Text ?? "";
            mod.ValueType = (uint)Math.Max(0, _type.SelectedIndex);
            mod.LevelOrVariation = (uint)(_level.Value ?? 0);
            mod.DataPointer = (uint)(_pointer.Value ?? 0);
            mod.ValueText = _value.Text ?? "";
        });
    }

    private async void SaveMap()
    {
        try
        {
            IEditorShellActions? shell = _context.Capabilities.Get<IEditorShellActions>();
            if (shell == null) throw new InvalidOperationException("Shell save service is unavailable.");
            await shell.ExecuteAsync("op.save");
            RefreshObjects();
        }
        catch (Exception ex) { _status.Text = "保存失败：" + ex.Message; }
    }

    private void Undo()
    {
        if (_context.Capabilities.Get<IEditHistory>()?.Undo() == true) RefreshObjects();
    }

    private void Redo()
    {
        if (_context.Capabilities.Get<IEditHistory>()?.Redo() == true) RefreshObjects();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z) { Undo(); e.Handled = true; return; }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y) { Redo(); e.Handled = true; return; }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S) { SaveMap(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    private void UpdateDefaultBase()
    {
        string[] ids = { "hfoo", "rat9", "AHhb", "LTlt", "LTlt", "Rhme", "BHbz" };
        _baseId.Text = ids[Math.Clamp(_kind.SelectedIndex, 0, ids.Length - 1)];
    }

    private static void EnsurePersisted(ObjectDataFile file, RecordRow row)
    {
        if (row.IsCustom || row.Persisted) return;
        file.OriginalTable.Add(row.Record);
        row.Persisted = true;
    }

    private static uint TypeCode(string? type) => type?.ToLowerInvariant() switch
    {
        "int" or "bool" or "attackbits" or "channeltype" or "deathtype" or "defensetypeint"
            or "detectiontype" or "spelldetail" or "teamcolor" or "techavail" => 0,
        "real" => 1,
        "unreal" => 2,
        _ when type?.EndsWith("Flags", StringComparison.OrdinalIgnoreCase) == true => 0,
        _ => 3,
    };

    private static string NextCustomId(ObjectDataFile file)
    {
        var used = file.OriginalTable.Concat(file.CustomTable).Select(x => x.DisplayId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        for (int value = 0; value < alphabet.Length * alphabet.Length * alphabet.Length; value++)
        {
            int n = value;
            string id = "X" + alphabet[(n / (36 * 36)) % 36] + alphabet[(n / 36) % 36] + alphabet[n % 36];
            if (!used.Contains(id)) return id;
        }
        throw new InvalidOperationException("没有可用的自定义 4CC。" );
    }

    private sealed record RecordRow(ObjectRecord Record, bool IsCustom, int Index, string Name, bool InitiallyPersisted)
    {
        public bool Persisted { get; set; } = InitiallyPersisted;
        public override string ToString() => IsCustom
            ? $"★ {Record.ModifiedText} ← {Record.OriginalText} · {Name}  ({Record.Modifications.Count})"
            : $"  {Record.OriginalText} · {Name}  ({Record.Modifications.Count})";
    }

    private sealed record ModificationRow(ObjectModification Modification, int Index, string Name)
    {
        public override string ToString()
        {
            string suffix = Modification.LevelOrVariation != 0 || Modification.DataPointer != 0
                ? $"  L{Modification.LevelOrVariation}/D{Modification.DataPointer}" : "";
            string value = Modification.ValueText.Replace('\r', ' ').Replace('\n', ' ');
            if (value.Length > 45) value = value[..45] + "…";
            return $"{Name} [{Modification.FieldText}] · T{Modification.ValueType}{suffix} = {value}";
        }
    }

    private sealed record FieldChoice(FieldMetadata Metadata)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Metadata.Category)
            ? $"{Metadata.DisplayName} [{Metadata.Id}]"
            : $"{Metadata.Category} · {Metadata.DisplayName} [{Metadata.Id}]";
    }
}
