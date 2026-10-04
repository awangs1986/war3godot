using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Diablo1Like.MapRuntime;
using Diablo1Like.W3.Archives;
using Diablo1Like.W3.Assets;
using Diablo1Like.W3.Terrain;
using Diablo1Like.W3.Mdx;
using Diablo1Like.W3.Lighting;
using Godot;
using File = System.IO.File;

namespace Diablo1Like.Tests;

/// <summary>Asset-free integration through the real Godot cliff build and material batches.</summary>
public partial class W3TerrainBuildSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "w3-terrain-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Doodads/Terrain/FixtureCliffs"));
        Directory.CreateDirectory(Path.Combine(folder, "TerrainArt"));
        string slk = "ID;PWXL;N;E\nC;X1;Y1;K\"cliffID\"\nC;X2;K\"cliffModelDir\"\nC;X3;K\"rampModelDir\"\n" +
            "C;X1;Y2;K\"TEST\"\nC;X2;K\"FixtureCliffs\"\nC;X3;K\"FixtureRamps\"\nE\n";
        File.WriteAllText(Path.Combine(folder, "TerrainArt/CliffTypes.slk"), slk);
        File.WriteAllBytes(Path.Combine(folder, "Doodads/Terrain/FixtureCliffs/FixtureCliffsABBA0.mdx"), ModelBytes());
        var map = new W3TerrainMap { Width = 3, Height = 2 };
        map.GroundTilesets.Add("Ldrt");
        map.CliffTilesets.Add("TEST");
        map.Corners = Enumerable.Range(0, 6).Select(i => new W3TerrainMap.Corner
        {
            LayerHeight = (byte)(i % 3 == 0 ? 2 : 3), GroundHeight = (i % 3) * 0.25f,
        }).ToArray();
        var resource = new W3PackedTerrainMap();
        resource.PopulateFromCore(map);
        var archive = new W3MapArchiveBridge { MapDir = folder, SourcePath = folder };
        var textures = new W3TextureProvider();
        textures.SetMapDir(folder);
        var cliffs = new WarsmashCliffRenderer();
        AddChild(cliffs);
        cliffs.SetPathResolver(archive);
        cliffs.Build(resource, textures, Array.Empty<byte>());
        var ground = new WarsmashGroundRenderer();
        AddChild(ground);
        ground.BuildFrom(cliffs.BuildResult!, textures, Array.Empty<byte>());
        MeshInstance3D? batch = cliffs.GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
        ShaderMaterial? material = batch?.GetActiveMaterial(0) as ShaderMaterial;
        var checks = new Dictionary<string, bool>
        {
            ["customCliffDirectoryUsed"] = cliffs.SpawnedCount == 1 && cliffs.MissingCount == 0,
            ["groundAndCliffsShareBuild"] = ReferenceEquals(ground.TerrainMap, cliffs.BuildResult!.Map),
            ["groundCoverageMatchesCliffs"] = !ground.GroundExists(0, 0) && ground.GroundExists(1, 0),
            ["batchRetainsCliffMaterial"] = material != null,
            ["batchRetainsBothGeosets"] = batch?.Mesh?.GetFaces().Length == 6,
            ["sourceMapHasNoRenderFlags"] = map.Corners.All(c => !c.Cliff && !c.Romp),
        };
        cliffs.SetFlatInspect(true);
        checks["flatInspectUpdatesBatchedMaterial"] = material != null && material.GetShaderParameter("flat_lighting").AsBool();
        cliffs.SetFlatInspect(false);
        checks["flatInspectRestoresSceneLighting"] = material != null && !material.GetShaderParameter("flat_lighting").AsBool();
        // Exercise the public import paths: static batches and instances must
        // honor the same authored response even with a missing texture.
        foreach (var spec in new[] { (Flags: 0, Mode: 0), (Flags: 1, Mode: 2), (Flags: 0, Mode: 3), (Flags: 1, Mode: 5) })
        {
            var model = W3MdxModel.LoadBytes(ModelBytes())!;
            model.materials.Add(new Godot.Collections.Array
            {
                new W3MdxLayerData { flags = spec.Flags, filter_mode = spec.Mode, alpha = 0.6f },
            });
            var instance = new W3MdxInstance();
            AddChild(instance);
            instance.setup(model, textures);
            var instanceMat = ((MeshInstance3D)instance._geoset_nodes[0].AsGodotObject()).MaterialOverride as StandardMaterial3D;
            var batcher = new W3MdxBatcher { textures = textures };
            AddChild(batcher);
            var bank = batcher._build_geoset_meshes(model, "");
            var batchMat = bank[0].AsGodotDictionary()["mat"].AsGodotObject() as StandardMaterial3D;
            var expected = spec.Flags == 1 || spec.Mode == 3
                ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel;
            checks[$"classicInstanceBatchResponse_{spec.Flags}_{spec.Mode}"] = instanceMat != null && batchMat != null &&
                instanceMat.ShadingMode == expected && batchMat.ShadingMode == expected &&
                instanceMat.BlendMode == batchMat.BlendMode && instanceMat.Transparency == batchMat.Transparency &&
                Mathf.IsEqualApprox(instanceMat.AlbedoColor.A, 0.6f) && Mathf.IsEqualApprox(batchMat.AlbedoColor.A, 0.6f);
        }
        // A lighting preset must not carry exposure settings into another preset.
        var lightingRoot = new Node3D();
        AddChild(lightingRoot);
        W3LightingRouter.ApplyToScene(lightingRoot, new BistroLitSettings());
        W3LightingRouter.ApplyToScene(lightingRoot, new W3LitSettings());
        checks["war3ClearsBistroCameraExposure"] = lightingRoot.GetNode<WorldEnvironment>("WorldEnvironment").CameraAttributes == null;
        W3LightingRouter.ApplyToScene(lightingRoot, new BistroLitSettings());
        W3LightingRouter.ApplyToScene(lightingRoot, new UeStyleLitSettings { sky_backend = UeStyleLitSettings.SkyBackend.PhysicalNishita });
        checks["physicalSkyClearsBistroCameraExposure"] = lightingRoot.GetNode<WorldEnvironment>("WorldEnvironment").CameraAttributes == null;
        lightingRoot.QueueFree();
        string missingFolder = Path.Combine(folder, "missing");
        Directory.CreateDirectory(Path.Combine(missingFolder, "TerrainArt"));
        File.WriteAllText(Path.Combine(missingFolder, "TerrainArt/CliffTypes.slk"), slk);
        var missing = new WarsmashCliffRenderer();
        AddChild(missing);
        missing.SetPathResolver(new W3MapArchiveBridge { MapDir = missingFolder, SourcePath = missingFolder });
        missing.Build(resource, textures, Array.Empty<byte>());
        checks["missingModelHasGroundFallback"] = missing.SpawnedCount == 0 && missing.MissingCount == 1 &&
            missing.BuildResult!.GroundExists(0, 0);

        // Cross the old 20,000-tile threshold: collision must not silently
        // switch to a different topology or skip interior tile coverage.
        var largeMap = new W3TerrainMap { Width = 145, Height = 145 };
        largeMap.GroundTilesets.Add("Ldrt");
        largeMap.Corners = Enumerable.Range(0, 145 * 145)
            .Select(_ => new W3TerrainMap.Corner { LayerHeight = 2 }).ToArray();
        var largeGround = new WarsmashGroundRenderer();
        AddChild(largeGround);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        largeGround.BuildFrom(TerrainBuild.Create(largeMap, _ => false, _ => CliffTypeInfo.Fallback), textures, Array.Empty<byte>());
        timer.Stop();
        var body = largeGround.GetChildren().OfType<StaticBody3D>().Single();
        var shape = body.GetChildren().OfType<CollisionShape3D>().Single().Shape as ConcavePolygonShape3D;
        checks["largeCollisionUsesEveryTile"] = shape?.GetFaces().Length == 144 * 144 * 6;
        bool pass = checks.Values.All(value => value);
        string report = JsonSerializer.Serialize(new { pass, checks, largeBuildMs = timer.ElapsedMilliseconds, fixture = folder }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(folder, "result.json"), report);
        GD.Print(report);
        GD.Print(pass ? "TERRAIN_BUILD_SMOKE_OK" : "TERRAIN_BUILD_SMOKE_FAIL");
        GetTree().Quit(pass ? 0 : 1);
    }

    private static byte[] ModelBytes()
    {
        using var geoStream = new MemoryStream();
        using (var w = new BinaryWriter(geoStream, Encoding.ASCII, true))
        {
            w.Write(0); // Inclusive geoset size filled below.
            void Tag(string tag, int count) { w.Write(Encoding.ASCII.GetBytes(tag)); w.Write(count); }
            Tag("VRTX", 3);
            foreach (float v in new float[] { 0, 0, 0, 0, 128, 128, -128, 0, 0 }) w.Write(v);
            Tag("NRMS", 3);
            for (int i = 0; i < 3; i++) { w.Write(0f); w.Write(0f); w.Write(1f); }
            Tag("PTYP", 1); w.Write(4);
            Tag("PCNT", 1); w.Write(3);
            Tag("PVTX", 3); w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2);
            Tag("GNDX", 3); w.Write(new byte[3]);
            Tag("MTGC", 0);
            Tag("MATS", 0); w.Write(new byte[12 + 28]); w.Write(0);
            Tag("UVAS", 1); Tag("UVBS", 3);
            foreach (float v in new float[] { 0, 0, 1, 0, 0, 1 }) w.Write(v);
            w.Flush();
            geoStream.Position = 0; w.Write((int)geoStream.Length);
        }
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write(Encoding.ASCII.GetBytes("MDLX"));
        writer.Write(Encoding.ASCII.GetBytes("VERS")); writer.Write(4); writer.Write(800);
        writer.Write(Encoding.ASCII.GetBytes("GEOS")); writer.Write((int)geoStream.Length * 2);
        writer.Write(geoStream.ToArray()); writer.Write(geoStream.ToArray());
        return file.ToArray();
    }
}
