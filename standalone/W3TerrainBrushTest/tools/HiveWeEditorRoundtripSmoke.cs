using System;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Editing;
using W3Foundation.Terrain.Rendering;
using W3Foundation.Validation.HiveWe;

namespace W3TerrainBrushTest.Tools;

/// <summary>Regression test for W3 panel -> normal 3D terrain renderer hand-off.</summary>
public partial class HiveWeEditorRoundtripSmoke : Node
{
    private TerrainDocument _document = null!;
    private TerrainWorld _panelWorld = null!;
    private TerrainWorld _sceneWorld = null!;

    public override void _Ready() => Run();

    private async void Run()
    {
        _document = new TerrainDocument { TileWidth = 32, TileHeight = 32, TileSize = 128.0f, CliffStep = 128.0f };
        _document.EnsureInitialized();
        _panelWorld = CreateWorld("PanelWorld", Vector3.Zero, _document);
        _sceneWorld = CreateWorld("Scene3DWorld", new Vector3(-2048.0f, 0.0f, -2048.0f), _document);
        AddChild(_panelWorld);
        AddChild(_sceneWorld);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var brush = new TerrainBrush(TerrainBrushOperation.HeightRaise, radius: 2.5f, strength: 12.0f, square: true);
        var hiveWe = new HiveWeTerrainBrushPort();
        hiveWe.ApplyBegin(_document, new Vector2(10.0f, 10.0f), brush);
        hiveWe.Apply(_document, new Vector2(10.0f, 10.0f), brush, 0.5);
        hiveWe.ApplyEnd();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        AssertWorldMatchesDocument(_panelWorld, "panel mutable document");
        AssertWorldMatchesDocument(_sceneWorld, "3D mutable document");

        // This is the same stable after-snapshot that the editor undo action stores.
        TerrainDocument after = _document.CloneDocument();
        _sceneWorld.Document = after;
        _panelWorld.Document = after;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        AssertWorldMatchesDocument(_panelWorld, "panel committed snapshot");
        AssertWorldMatchesDocument(_sceneWorld, "3D committed snapshot");

        GD.Print("HIVEWE_EDITOR_ROUNDTRIP_OK height=", after.GetWorldHeight(10, 10), " chunks=", _sceneWorld.GetNode<Node3D>("_TerrainChunks").GetChildCount());
        GetTree().Quit(0);
    }

    private static TerrainWorld CreateWorld(string name, Vector3 position, TerrainDocument document) => new()
    {
        Name = name,
        Position = position,
        Document = document,
        ChunkSize = 16,
        CreateDefaultDocument = false,
    };

    private static void AssertWorldMatchesDocument(TerrainWorld world, string label)
    {
        const int x = 10;
        const int y = 10;
        Node3D root = world.GetNode<Node3D>("_TerrainChunks");
        MeshInstance3D ground = root.GetNode<Node3D>("Chunk_0_0").GetNode<MeshInstance3D>("Ground");
        var mesh = (ArrayMesh)ground.Mesh;
        var arrays = mesh.SurfaceGetArrays(0);
        var vertices = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
        const int stride = 17;
        float meshHeight = vertices[y * stride + x].Y;
        float documentHeight = world.Document!.GetWorldHeight(x, y);
        if (!Mathf.IsEqualApprox(meshHeight, documentHeight) || world.GetNode<Node3D>("_TerrainChunks").GetChildCount() != 4)
            throw new InvalidOperationException($"{label}: mesh={meshHeight}, document={documentHeight}");
    }
}
