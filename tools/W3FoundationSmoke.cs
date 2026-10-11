using System;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Editing;
using W3Foundation.Terrain.Rendering;
using W3Foundation.Preview;

namespace W3Foundation.Tests;

public partial class W3FoundationSmoke : Node
{
    public override async void _Ready()
    {
        int failures = 0;
        var document = new TerrainDocument { TileWidth = 32, TileHeight = 32 };
        document.EnsureInitialized();
        failures += Expect(document.CornerCount == 1089, "corner grid dimensions");
        failures += Expect(document.Palette.Count == 8, "default palette");

        var terrain = new TerrainWorld
        {
            Name = "TerrainUnderTest",
            Document = document,
            ChunkSize = 16,
        };
        AddChild(terrain);

        Vector2 center = new(16.0f, 16.0f);
        terrain.ApplyBrush(center, new TerrainBrush(
            TerrainBrushOperation.PaintTexture, radius: 2.5f, materialIndex: 4));
        terrain.ApplyBrush(center, new TerrainBrush(
            TerrainBrushOperation.HeightRaise, radius: 2.5f, strength: 16.0f));
        terrain.ApplyBrush(new Vector2(9.0f, 9.0f), new TerrainBrush(
            TerrainBrushOperation.CliffRaiseOne, radius: 1.5f));
        terrain.ApplyBrush(new Vector2(24.0f, 24.0f), new TerrainBrush(
            TerrainBrushOperation.ShallowWater, radius: 2.5f));

        int centerIndex = document.CornerIndex(16, 16);
        failures += Expect(document.GroundMaterials[centerIndex] == 4, "texture brush");
        failures += Expect(document.GroundHeights[centerIndex] > 15.0f, "height brush");
        failures += Expect(document.CliffLevels[document.CornerIndex(9, 9)] == 1, "cliff brush");
        failures += Expect((document.CornerFlags[document.CornerIndex(24, 24)] & TerrainDocument.FlagWater) != 0, "water brush");
        failures += Expect(document.CloneDocument().GroundHeights[centerIndex] == document.GroundHeights[centerIndex], "document snapshot");

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Node? chunks = terrain.GetNodeOrNull("_TerrainChunks");
        failures += Expect(chunks != null && chunks.GetChildCount() == 4, "16x16 chunk renderer");

        var camera = new Camera3D
        {
            Name = "PreviewCameraUnderTest",
            Current = true,
            Position = new Vector3(2048.0f, 320.0f, 2048.0f),
        };
        AddChild(camera);
        camera.LookAt(new Vector3(2048.0f, 0.0f, 2048.0f), Vector3.Forward);

        var overlay = new TerrainDebugOverlay { Name = "OverlayUnderTest" };
        terrain.AddChild(overlay);
        overlay.Setup(terrain);
        overlay.UpdateForCamera(camera);
        failures += Expect(TerrainDebugOverlay.DefaultLabelNearDistance == 650.0f, "tile label distance contract");
        failures += Expect(overlay.ActiveLabelCount > 0, "near in-frustum tile labels");
        failures += Expect(overlay.ActiveLabelCount < document.TileWidth * document.TileHeight, "tile labels are camera bounded");
        camera.Position = new Vector3(2048.0f, 900.0f, 2048.0f);
        camera.LookAt(new Vector3(2048.0f, 0.0f, 2048.0f), Vector3.Forward);
        overlay.UpdateForCamera(camera);
        failures += Expect(overlay.ActiveLabelCount == 0, "far tile labels are hidden");

        camera.Position = Vector3.Zero;
        camera.Rotation = Vector3.Zero;
        var nearAnimated = new PreviewLodTarget { Name = "NearAnimated", Position = new Vector3(0.0f, 0.0f, -1000.0f) };
        var secondAnimated = new PreviewLodTarget { Name = "SecondAnimated", Position = new Vector3(100.0f, 0.0f, -1100.0f) };
        var farAnimated = new PreviewLodTarget { Name = "FarAnimated", Position = new Vector3(0.0f, 0.0f, -4500.0f) };
        AddChild(nearAnimated);
        AddChild(secondAnimated);
        AddChild(farAnimated);
        var budget = new PreviewVisibilityBudget { Name = "BudgetUnderTest", MaxNearAnimations = 1 };
        AddChild(budget);
        budget.EvaluateNow(camera);
        failures += Expect(budget.VisibleAnimatedCount == 2, "animated preview draw distance");
        failures += Expect(budget.ActiveAnimationCount == 1, "nearest animation budget cap");
        failures += Expect(nearAnimated.AnimationActive && !secondAnimated.AnimationActive, "nearest target receives animation slot");
        failures += Expect(!farAnimated.PreviewVisible, "far animated preview is hidden");

        budget.MaxNearAnimations = 2;
        nearAnimated.Position = new Vector3(0.0f, 0.0f, -2500.0f);
        budget.EvaluateNow(camera);
        failures += Expect(nearAnimated.AnimationActive, "animation hysteresis keeps active target alive");
        nearAnimated.Position = new Vector3(0.0f, 0.0f, -2900.0f);
        budget.EvaluateNow(camera);
        failures += Expect(!nearAnimated.AnimationActive, "animation stops beyond hysteresis distance");

        if (failures == 0)
            GD.Print("W3_FOUNDATION_SMOKE_OK");
        else
            GD.PushError($"W3_FOUNDATION_SMOKE_FAILED count={failures}");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private static int Expect(bool condition, string label)
    {
        if (condition)
            return 0;
        GD.PushError($"W3 Foundation smoke assertion failed: {label}");
        return 1;
    }
}
