using Godot;

namespace W3Foundation.Terrain.Data;

[GlobalClass]
public partial class TerrainPaletteEntry : Resource
{
    [Export] public string Id { get; set; } = "terrain";
    [Export] public string DisplayName { get; set; } = "地表";
    [Export] public Color PreviewColor { get; set; } = new(0.35f, 0.5f, 0.25f);
    [Export] public string SourceAssetPath { get; set; } = string.Empty;

    public TerrainPaletteEntry()
    {
    }

    public TerrainPaletteEntry(string id, string displayName, Color previewColor)
    {
        Id = id;
        DisplayName = displayName;
        PreviewColor = previewColor;
    }
}
