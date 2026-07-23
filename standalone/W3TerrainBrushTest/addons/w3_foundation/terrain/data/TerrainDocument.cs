using System;
using Godot;
using GodotPaletteArray = Godot.Collections.Array<W3Foundation.Terrain.Data.TerrainPaletteEntry>;

namespace W3Foundation.Terrain.Data;

/// <summary>
/// Project-owned terrain data. Warcraft/CASC importers map source data into this
/// resource; the renderer and editor never read source archives or gameplay state.
/// </summary>
[GlobalClass]
public partial class TerrainDocument : Resource
{
    public const int CurrentFormatVersion = 1;
    public const byte FlagRamp = 1 << 0;
    public const byte FlagWater = 1 << 1;
    public const byte FlagBoundary = 1 << 2;

    [Signal]
    public delegate void TerrainEditedEventHandler(Rect2I dirtyCorners, bool geometryChanged);

    [Export] public int FormatVersion { get; set; } = CurrentFormatVersion;
    [Export(PropertyHint.Range, "4,1024,1")] public int TileWidth { get; set; } = 32;
    [Export(PropertyHint.Range, "4,1024,1")] public int TileHeight { get; set; } = 32;
    [Export(PropertyHint.Range, "16,512,1")] public float TileSize { get; set; } = 128.0f;
    [Export(PropertyHint.Range, "16,512,1")] public float CliffStep { get; set; } = 128.0f;

    // Corner-grid fields. Their dimensions are (TileWidth + 1) x (TileHeight + 1).
    [Export] public float[] GroundHeights { get; set; } = System.Array.Empty<float>();
    [Export] public float[] WaterHeights { get; set; } = System.Array.Empty<float>();
    [Export] public byte[] CornerFlags { get; set; } = System.Array.Empty<byte>();
    [Export] public byte[] GroundMaterials { get; set; } = System.Array.Empty<byte>();
    [Export] public byte[] CliffLevels { get; set; } = System.Array.Empty<byte>();
    [Export] public byte[] CliffMaterials { get; set; } = System.Array.Empty<byte>();
    [Export] public GodotPaletteArray Palette { get; set; } = new();

    public int CornerWidth => TileWidth + 1;
    public int CornerHeight => TileHeight + 1;
    public int CornerCount => CornerWidth * CornerHeight;

    public bool IsInitialized =>
        TileWidth >= 1 && TileHeight >= 1 &&
        GroundHeights.Length == CornerCount &&
        WaterHeights.Length == CornerCount &&
        CornerFlags.Length == CornerCount &&
        GroundMaterials.Length == CornerCount &&
        CliffLevels.Length == CornerCount &&
        CliffMaterials.Length == CornerCount;

    public void EnsureInitialized()
    {
        TileWidth = Math.Clamp(TileWidth, 4, 1024);
        TileHeight = Math.Clamp(TileHeight, 4, 1024);
        int count = CornerCount;

        GroundHeights = ResizePreserving(GroundHeights, count);
        WaterHeights = ResizePreserving(WaterHeights, count);
        CornerFlags = ResizePreserving(CornerFlags, count);
        GroundMaterials = ResizePreserving(GroundMaterials, count);
        CliffLevels = ResizePreserving(CliffLevels, count);
        CliffMaterials = ResizePreserving(CliffMaterials, count);

        if (Palette.Count == 0)
            Palette = CreateDefaultPalette();

        ResourceLocalToScene = true;
    }

    public int CornerIndex(int x, int y)
    {
        x = Math.Clamp(x, 0, CornerWidth - 1);
        y = Math.Clamp(y, 0, CornerHeight - 1);
        return y * CornerWidth + x;
    }

    public float GetWorldHeight(int x, int y)
    {
        int index = CornerIndex(x, y);
        return GroundHeights[index] + CliffLevels[index] * CliffStep;
    }

    public void NotifyEdited(Rect2I dirtyCorners, bool geometryChanged)
    {
        EmitChanged();
        EmitSignal(SignalName.TerrainEdited, dirtyCorners, geometryChanged);
    }

    public TerrainDocument CloneDocument()
    {
        var clone = new TerrainDocument
        {
            FormatVersion = FormatVersion,
            TileWidth = TileWidth,
            TileHeight = TileHeight,
            TileSize = TileSize,
            CliffStep = CliffStep,
            GroundHeights = (float[])GroundHeights.Clone(),
            WaterHeights = (float[])WaterHeights.Clone(),
            CornerFlags = (byte[])CornerFlags.Clone(),
            GroundMaterials = (byte[])GroundMaterials.Clone(),
            CliffLevels = (byte[])CliffLevels.Clone(),
            CliffMaterials = (byte[])CliffMaterials.Clone(),
            ResourceLocalToScene = true,
        };

        foreach (TerrainPaletteEntry entry in Palette)
        {
            clone.Palette.Add(new TerrainPaletteEntry
            {
                Id = entry.Id,
                DisplayName = entry.DisplayName,
                PreviewColor = entry.PreviewColor,
                SourceAssetPath = entry.SourceAssetPath,
                ResourceLocalToScene = true,
            });
        }
        return clone;
    }

    public static GodotPaletteArray CreateDefaultPalette()
    {
        return new GodotPaletteArray
        {
            new("dirt", "泥土", new Color("765333")),
            new("rough_dirt", "坑洼的泥土", new Color("644329")),
            new("grassy_dirt", "草色泥土", new Color("66733d")),
            new("rock", "岩石（不可建造）", new Color("615f69")),
            new("grass", "草地", new Color("3f6d32")),
            new("dark_grass", "深色草地", new Color("254c2f")),
            new("barren", "荒芜地表", new Color("514b3e")),
            new("boundary", "边界", new Color("263d24")),
        };
    }

    private static T[] ResizePreserving<T>(T[] source, int length)
    {
        if (source.Length == length)
            return source;
        var resized = new T[length];
        System.Array.Copy(source, resized, Math.Min(source.Length, length));
        return resized;
    }
}
