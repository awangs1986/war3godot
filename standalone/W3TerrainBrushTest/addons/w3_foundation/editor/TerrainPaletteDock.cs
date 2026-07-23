using System;
using System.Collections.Generic;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Editing;
using W3Foundation.Terrain.Rendering;

namespace W3Foundation.Editor;

[Tool]
public partial class TerrainPaletteDock : VBoxContainer
{
    private sealed class Entry
    {
        public TerrainBrushOperation Operation { get; init; }
        public int Material { get; init; }
        public int CliffMaterial { get; init; }
    }

    private readonly Dictionary<TreeItem, Entry> _entries = new();
    private readonly List<Texture2D> _swatches = new();
    private Tree _tree = null!;
    private Label _status = null!;
    private HSlider _radius = null!;
    private HSlider _strength = null!;
    private Label _radiusValue = null!;
    private Label _strengthValue = null!;
    private CheckButton _enabled = null!;
    private CheckButton _square = null!;
    private Entry _selected = new() { Operation = TerrainBrushOperation.PaintTexture };

    public event Action? CreateTerrainRequested;
    public event Action<bool>? BrushModeChanged;

    public bool BrushEnabled => _enabled.ButtonPressed;

    public float BrushRadius => (float)_radius.Value;

    public void SetBrushEnabled(bool enabled)
    {
        if (_enabled.ButtonPressed != enabled)
            _enabled.ButtonPressed = enabled;
    }

    public void SetBrushRadius(float radius)
    {
        _radius.Value = Math.Clamp(radius, (float)_radius.MinValue, (float)_radius.MaxValue);
    }

    public TerrainPaletteDock()
    {
        Name = "地形编辑器";
        CustomMinimumSize = new Vector2(310, 560);
        BuildUi();
    }

    public TerrainBrush CurrentBrush => new(
        _selected.Operation,
        (float)_radius.Value,
        (float)_strength.Value,
        _selected.Material,
        _selected.CliffMaterial,
        _square.ButtonPressed);

    public Color PreviewColor => _selected.Operation switch
    {
        TerrainBrushOperation.PaintTexture => new Color(0.4f, 1.0f, 0.35f),
        TerrainBrushOperation.HeightLower or TerrainBrushOperation.CliffLowerOne or TerrainBrushOperation.CliffLowerTwo
            => new Color(0.35f, 0.65f, 1.0f),
        TerrainBrushOperation.DeepWater or TerrainBrushOperation.ShallowWater
            => new Color(0.2f, 0.75f, 1.0f),
        _ => new Color(1.0f, 0.7f, 0.2f),
    };

    public void SetActiveTerrain(TerrainWorld? terrain)
    {
        _status.Text = terrain == null
            ? "请选择 TerrainWorld 节点"
            : $"正在编辑：{terrain.Name}  ·  {terrain.Document?.TileWidth ?? 0}×{terrain.Document?.TileHeight ?? 0}";
        _enabled.Disabled = terrain == null;
    }

    private void BuildUi()
    {
        AddChild(new Label
        {
            Text = "W3 Foundation · 地形",
            ThemeTypeVariation = "HeaderSmall",
        });

        _status = new Label
        {
            Text = "请选择 TerrainWorld 节点",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        AddChild(_status);

        var create = new Button { Text = "创建 32×32 测试地形" };
        create.Pressed += () => CreateTerrainRequested?.Invoke();
        AddChild(create);

        _enabled = new CheckButton
        {
            Text = "开启地形刷模式（Esc 退出）",
            ButtonPressed = true,
            Disabled = true,
        };
        _enabled.Toggled += enabled => BrushModeChanged?.Invoke(enabled);
        AddChild(_enabled);

        _tree = new Tree
        {
            HideRoot = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(280, 350),
        };
        _tree.ItemSelected += OnTreeItemSelected;
        AddChild(_tree);
        BuildTree();

        var radiusRow = new HBoxContainer();
        radiusRow.AddChild(new Label { Text = "半径" });
        _radius = new HSlider
        {
            MinValue = 0.5,
            MaxValue = 12.0,
            Step = 0.5,
            Value = 2.5,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _radius.ValueChanged += value => _radiusValue.Text = value.ToString("0.0");
        radiusRow.AddChild(_radius);
        _radiusValue = new Label { Text = "2.5", CustomMinimumSize = new Vector2(32, 0) };
        radiusRow.AddChild(_radiusValue);
        AddChild(radiusRow);

        var strengthRow = new HBoxContainer();
        strengthRow.AddChild(new Label { Text = "强度" });
        _strength = new HSlider
        {
            MinValue = 1.0,
            MaxValue = 48.0,
            Step = 1.0,
            Value = 12.0,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _strength.ValueChanged += value => _strengthValue.Text = value.ToString("0");
        strengthRow.AddChild(_strength);
        _strengthValue = new Label { Text = "12", CustomMinimumSize = new Vector2(32, 0) };
        strengthRow.AddChild(_strengthValue);
        AddChild(strengthRow);

        _square = new CheckButton { Text = "方形笔刷" };
        AddChild(_square);

        AddChild(new Label
        {
            Text = "左键绘制 · 拖动连续绘制 · Ctrl+Z 撤销\n纹理只更新 GPU 数据；高度只重建受影响区块。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.75f, 0.78f, 0.82f),
        });
    }

    private void BuildTree()
    {
        TreeItem root = _tree.CreateItem();
        TreeItem terrain = Category(root, "地形");
        TreeItem textures = Category(terrain, "纹理");
        var palette = TerrainDocument.CreateDefaultPalette();
        for (int i = 0; i < palette.Count; i++)
        {
            TerrainPaletteEntry paletteEntry = palette[i];
            AddEntry(textures, paletteEntry.DisplayName,
                new Entry { Operation = TerrainBrushOperation.PaintTexture, Material = i },
                paletteEntry.PreviewColor);
        }

        AddCliffGroup(terrain, "草地悬崖", 0);
        AddCliffGroup(terrain, "泥土悬崖", 1);

        TreeItem height = Category(terrain, "高度");
        AddEntry(height, "隆起地表", new Entry { Operation = TerrainBrushOperation.HeightRaise }, new Color("7398c7"));
        AddEntry(height, "降低地表", new Entry { Operation = TerrainBrushOperation.HeightLower }, new Color("587daf"));
        AddEntry(height, "推平地表", new Entry { Operation = TerrainBrushOperation.HeightFlatten }, new Color("6f91bc"));
        AddEntry(height, "陡峭地表", new Entry { Operation = TerrainBrushOperation.HeightSteep }, new Color("4774aa"));
        AddEntry(height, "平滑化地表", new Entry { Operation = TerrainBrushOperation.HeightSmooth }, new Color("3b6899"));

        terrain.Collapsed = false;
        textures.Collapsed = false;
        height.Collapsed = false;
    }

    private void AddCliffGroup(TreeItem parent, string name, int cliffMaterial)
    {
        TreeItem cliff = Category(parent, name);
        AddEntry(cliff, "降低两层", new Entry { Operation = TerrainBrushOperation.CliffLowerTwo, CliffMaterial = cliffMaterial }, new Color("1f5a2d"));
        AddEntry(cliff, "降低一层", new Entry { Operation = TerrainBrushOperation.CliffLowerOne, CliffMaterial = cliffMaterial }, new Color("286c35"));
        AddEntry(cliff, "整平地面", new Entry { Operation = TerrainBrushOperation.CliffFlatten, CliffMaterial = cliffMaterial }, new Color("347a42"));
        AddEntry(cliff, "升高一层", new Entry { Operation = TerrainBrushOperation.CliffRaiseOne, CliffMaterial = cliffMaterial }, new Color("3d8a4c"));
        AddEntry(cliff, "升高两层", new Entry { Operation = TerrainBrushOperation.CliffRaiseTwo, CliffMaterial = cliffMaterial }, new Color("459b55"));
        AddEntry(cliff, "深水", new Entry { Operation = TerrainBrushOperation.DeepWater, CliffMaterial = cliffMaterial }, new Color("267f8e"));
        AddEntry(cliff, "浅水", new Entry { Operation = TerrainBrushOperation.ShallowWater, CliffMaterial = cliffMaterial }, new Color("3a9ba5"));
        AddEntry(cliff, "斜坡", new Entry { Operation = TerrainBrushOperation.Ramp, CliffMaterial = cliffMaterial }, new Color("315f39"));
    }

    private TreeItem Category(TreeItem parent, string text)
    {
        TreeItem item = _tree.CreateItem(parent);
        item.SetText(0, text);
        item.SetSelectable(0, false);
        return item;
    }

    private void AddEntry(TreeItem parent, string text, Entry entry, Color color)
    {
        TreeItem item = _tree.CreateItem(parent);
        item.SetText(0, text);
        Texture2D swatch = MakeSwatch(color);
        item.SetIcon(0, swatch);
        _entries[item] = entry;
    }

    private Texture2D MakeSwatch(Color color)
    {
        var image = Image.CreateEmpty(18, 18, false, Image.Format.Rgba8);
        image.Fill(color);
        Texture2D texture = ImageTexture.CreateFromImage(image);
        _swatches.Add(texture);
        return texture;
    }

    private void OnTreeItemSelected()
    {
        TreeItem? selected = _tree.GetSelected();
        if (selected != null && _entries.TryGetValue(selected, out Entry? entry))
            _selected = entry;
    }
}
