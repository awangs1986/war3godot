using System;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Rendering;

/// <summary>A visible pre-painted map that remains fully editable by the terrain dock.</summary>
[Tool]
public partial class TestTerrainWorld : TerrainWorld
{
    public override void _Ready()
    {
        base._Ready();
        if (Document == null)
            return;
        Document.EnsureInitialized();
        if (HasAuthoredContent(Document))
            return;
        PaintShowcase(Document);
    }

    private static bool HasAuthoredContent(TerrainDocument document)
    {
        foreach (byte value in document.GroundMaterials)
            if (value != 0) return true;
        foreach (byte value in document.CliffLevels)
            if (value != 0) return true;
        foreach (byte value in document.CornerFlags)
            if (value != 0) return true;
        foreach (float value in document.GroundHeights)
            if (Math.Abs(value) > 0.001f) return true;
        return false;
    }

    private static void PaintShowcase(TerrainDocument document)
    {
        for (int y = 0; y < document.CornerHeight; y++)
        {
            for (int x = 0; x < document.CornerWidth; x++)
            {
                int index = document.CornerIndex(x, y);
                document.GroundMaterials[index] = 4; // grass

                float pathCenter = 16.0f + MathF.Sin(x * 0.34f) * 3.0f;
                float pathDistance = MathF.Abs(y - pathCenter);
                if (pathDistance < 1.7f)
                    document.GroundMaterials[index] = 0; // dirt road
                else if (pathDistance < 2.8f)
                    document.GroundMaterials[index] = 2; // grassy dirt shoulder

                float hillDistance = new Vector2(x - 7.0f, y - 25.0f).Length();
                if (hillDistance < 5.5f)
                {
                    float hill = 1.0f - hillDistance / 5.5f;
                    document.GroundHeights[index] += hill * hill * 150.0f;
                    document.GroundMaterials[index] = 5; // dark grass hill
                }

                float pondDistance = new Vector2(x - 7.0f, y - 7.0f).Length();
                if (pondDistance < 4.2f)
                {
                    document.GroundHeights[index] = -42.0f;
                    document.CornerFlags[index] |= TerrainDocument.FlagWater;
                    document.WaterHeights[index] = 18.0f;
                    document.GroundMaterials[index] = 1; // rough dirt pond bed
                }

                if (x >= 21 && x <= 29 && y >= 20 && y <= 29)
                {
                    document.CliffLevels[index] = 1;
                    document.CliffMaterials[index] = 0;
                    document.GroundMaterials[index] = 4;
                }
                // A visible sloped entrance into the first plateau.
                if (x >= 18 && x <= 21 && y >= 23 && y <= 25)
                {
                    document.CliffLevels[index] = 0;
                    document.GroundHeights[index] = (x - 18) / 3.0f * document.CliffStep;
                    document.CornerFlags[index] |= TerrainDocument.FlagRamp;
                    document.GroundMaterials[index] = 2;
                }
            }
        }

        document.NotifyEdited(new Rect2I(0, 0, document.CornerWidth, document.CornerHeight), true);
    }
}
