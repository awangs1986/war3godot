using Godot;

namespace W3Foundation.Terrain.Editing;

public enum TerrainBrushOperation
{
    PaintTexture,
    CliffLowerTwo,
    CliffLowerOne,
    CliffFlatten,
    CliffRaiseOne,
    CliffRaiseTwo,
    DeepWater,
    ShallowWater,
    Ramp,
    HeightRaise,
    HeightLower,
    HeightFlatten,
    HeightSteep,
    HeightSmooth,
}

public readonly struct TerrainBrush
{
    public TerrainBrushOperation Operation { get; init; }
    public int MaterialIndex { get; init; }
    public int CliffMaterialIndex { get; init; }
    public float Radius { get; init; }
    public float Strength { get; init; }
    public bool Square { get; init; }

    public TerrainBrush(
        TerrainBrushOperation operation,
        float radius = 2.5f,
        float strength = 12.0f,
        int materialIndex = 0,
        int cliffMaterialIndex = 0,
        bool square = false)
    {
        Operation = operation;
        Radius = radius;
        Strength = strength;
        MaterialIndex = materialIndex;
        CliffMaterialIndex = cliffMaterialIndex;
        Square = square;
    }
}
