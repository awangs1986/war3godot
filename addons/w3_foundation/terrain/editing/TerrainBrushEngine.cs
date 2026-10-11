using System;
using Godot;
using W3Foundation.Terrain.Data;

namespace W3Foundation.Terrain.Editing;

/// <summary>Pure terrain mutation logic, independent from Godot editor UI.</summary>
public static class TerrainBrushEngine
{
    public static Rect2I Apply(
        TerrainDocument document,
        Vector2 cornerPosition,
        TerrainBrush brush,
        TerrainDocument? strokeBaseline = null)
    {
        document.EnsureInitialized();
        float radius = Math.Max(0.5f, brush.Radius);
        int minX = Math.Clamp((int)MathF.Floor(cornerPosition.X - radius), 0, document.CornerWidth - 1);
        int minY = Math.Clamp((int)MathF.Floor(cornerPosition.Y - radius), 0, document.CornerHeight - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(cornerPosition.X + radius), 0, document.CornerWidth - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(cornerPosition.Y + radius), 0, document.CornerHeight - 1);
        var dirty = new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);

        int centerX = Math.Clamp((int)MathF.Round(cornerPosition.X), 0, document.CornerWidth - 1);
        int centerY = Math.Clamp((int)MathF.Round(cornerPosition.Y), 0, document.CornerHeight - 1);
        int centerIndex = document.CornerIndex(centerX, centerY);
        float flattenHeight = document.GroundHeights[centerIndex];
        byte flattenCliff = (strokeBaseline ?? document).CliffLevels[centerIndex];
        float[] sourceHeights = brush.Operation == TerrainBrushOperation.HeightSmooth
            ? (float[])document.GroundHeights.Clone()
            : document.GroundHeights;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cornerPosition.X;
                float dy = y - cornerPosition.Y;
                float distance = brush.Square ? Math.Max(Math.Abs(dx), Math.Abs(dy)) : MathF.Sqrt(dx * dx + dy * dy);
                if (distance > radius)
                    continue;

                float falloff = SmoothFalloff(distance / radius);
                int index = document.CornerIndex(x, y);
                ApplyAt(document, strokeBaseline, sourceHeights, index, x, y, flattenHeight, flattenCliff, falloff, brush);
            }
        }

        bool geometryChanged = brush.Operation != TerrainBrushOperation.PaintTexture;
        document.NotifyEdited(dirty, geometryChanged);
        return dirty;
    }

    private static void ApplyAt(
        TerrainDocument document,
        TerrainDocument? strokeBaseline,
        float[] sourceHeights,
        int index,
        int x,
        int y,
        float flattenHeight,
        byte flattenCliff,
        float falloff,
        TerrainBrush brush)
    {
        switch (brush.Operation)
        {
            case TerrainBrushOperation.PaintTexture:
                if (falloff >= 0.18f)
                    document.GroundMaterials[index] = (byte)Math.Clamp(brush.MaterialIndex, 0, 255);
                break;

            case TerrainBrushOperation.CliffLowerTwo:
                AdjustCliff(document, strokeBaseline, index, -2, brush.CliffMaterialIndex);
                break;
            case TerrainBrushOperation.CliffLowerOne:
                AdjustCliff(document, strokeBaseline, index, -1, brush.CliffMaterialIndex);
                break;
            case TerrainBrushOperation.CliffFlatten:
                document.CliffLevels[index] = flattenCliff;
                document.CliffMaterials[index] = (byte)Math.Clamp(brush.CliffMaterialIndex, 0, 255);
                break;
            case TerrainBrushOperation.CliffRaiseOne:
                AdjustCliff(document, strokeBaseline, index, 1, brush.CliffMaterialIndex);
                break;
            case TerrainBrushOperation.CliffRaiseTwo:
                AdjustCliff(document, strokeBaseline, index, 2, brush.CliffMaterialIndex);
                break;
            case TerrainBrushOperation.DeepWater:
                SetWater(document, index, 128.0f);
                break;
            case TerrainBrushOperation.ShallowWater:
                SetWater(document, index, 48.0f);
                break;
            case TerrainBrushOperation.Ramp:
                document.CornerFlags[index] |= TerrainDocument.FlagRamp;
                break;

            case TerrainBrushOperation.HeightRaise:
                document.GroundHeights[index] += brush.Strength * falloff;
                break;
            case TerrainBrushOperation.HeightLower:
                document.GroundHeights[index] -= brush.Strength * falloff;
                break;
            case TerrainBrushOperation.HeightFlatten:
                document.GroundHeights[index] = Mathf.Lerp(
                    document.GroundHeights[index],
                    flattenHeight,
                    Math.Clamp(brush.Strength / 24.0f, 0.02f, 1.0f) * falloff);
                break;
            case TerrainBrushOperation.HeightSteep:
                document.GroundHeights[index] = flattenHeight +
                    (document.GroundHeights[index] - flattenHeight) * (1.0f + brush.Strength * 0.035f * falloff);
                break;
            case TerrainBrushOperation.HeightSmooth:
                float average = NeighborAverage(document, sourceHeights, x, y);
                document.GroundHeights[index] = Mathf.Lerp(
                    sourceHeights[index],
                    average,
                    Math.Clamp(brush.Strength / 18.0f, 0.05f, 1.0f) * falloff);
                break;
        }
    }

    private static void AdjustCliff(
        TerrainDocument document,
        TerrainDocument? strokeBaseline,
        int index,
        int delta,
        int cliffMaterial)
    {
        int sourceLevel = strokeBaseline?.CliffLevels[index] ?? document.CliffLevels[index];
        document.CliffLevels[index] = (byte)Math.Clamp(sourceLevel + delta, 0, 15);
        document.CliffMaterials[index] = (byte)Math.Clamp(cliffMaterial, 0, 255);
        document.CornerFlags[index] &= unchecked((byte)~TerrainDocument.FlagRamp);
    }

    private static void SetWater(TerrainDocument document, int index, float depth)
    {
        document.CornerFlags[index] |= TerrainDocument.FlagWater;
        document.WaterHeights[index] = document.GroundHeights[index] +
            document.CliffLevels[index] * document.CliffStep + depth;
    }

    private static float NeighborAverage(TerrainDocument document, float[] source, int x, int y)
    {
        float total = 0.0f;
        int count = 0;
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                int nx = Math.Clamp(x + ox, 0, document.CornerWidth - 1);
                int ny = Math.Clamp(y + oy, 0, document.CornerHeight - 1);
                total += source[document.CornerIndex(nx, ny)];
                count++;
            }
        }
        return total / count;
    }

    private static float SmoothFalloff(float value)
    {
        value = Math.Clamp(value, 0.0f, 1.0f);
        float inverse = 1.0f - value;
        return inverse * inverse * (3.0f - 2.0f * inverse);
    }
}
