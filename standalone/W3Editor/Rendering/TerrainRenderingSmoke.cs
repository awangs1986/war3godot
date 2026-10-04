using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Diablo1Like.W3.Terrain;

namespace W3Editor.Rendering;

/// <summary>Asset-free regressions through the editor's actual CPU cliff builder.</summary>
internal static class TerrainRenderingSmoke
{
    public static int Run()
    {
        var checks = new Dictionary<string, bool>();
        var map = CreateMap(12);
        map.GetCorner(4, 5).LayerHeight = 3;
        for (int x = 4; x <= 6; x++)
        {
            map.GetCorner(x, 5).Ramp = true;
            map.GetCorner(x, 5).CliffTexture = (byte)(x == 4 ? 1 : 0);
        }
        byte[] authored = map.Corners.Select(c => c.CliffTexture).ToArray();
        var rules = new WarsmashTerrainRules(map);
        var firstCliffs = rules.RebuildCliffs().ToArray();
        var first = Layout(map, rules).BuildTileData();
        checks["fixtureHasRamps"] = firstCliffs.Any(c => c.IsRamp);
        checks["renderLeavesAuthoredTexturesUnchanged"] = authored.SequenceEqual(map.Corners.Select(c => c.CliffTexture));
        var secondCliffs = rules.RebuildCliffs().ToArray();
        checks["repeatRebuildKeepsCliffMeshes"] = firstCliffs.SequenceEqual(secondCliffs);
        checks["repeatRebuildKeepsGroundPacking"] = first.SequenceEqual(Layout(map, rules).BuildTileData());

        // NotifyCornersChanged refreshes only the edited region, then creates
        // a new rules object for the entire persistent mirror. A distant edit
        // must not change ownership around this untouched ramp.
        map.GetCorner(10, 10).GroundHeight = 0.25f;
        var recreatedRules = new WarsmashTerrainRules(map);
        recreatedRules.RebuildCliffs();
        checks["distantHeightEditKeepsRampPacking"] = first.SequenceEqual(Layout(map, recreatedRules).BuildTileData());

        var slope = CreateMap(8);
        for (int y = 0; y < slope.Height; y++)
        for (int x = 0; x < slope.Width; x++)
            slope.GetCorner(x, y).GroundHeight = x * 0.25f + y * 0.125f;
        // HiveWE cliff.vert: central differences in raw corner-height units,
        // Y-up conversion of normalize(hL-hR, hD-hU, 2).
        Vector3 expected = Vector3.Normalize(new Vector3(-0.5f, 2f, -0.25f));
        var sloped = BuildCliff(slope, Vector3.UnitZ);
        checks["cliffRoofMatchesGroundNormalOnSlope"] = Enumerable.Range(0, 3)
            .All(i => Vector3.Distance(Normal(sloped, i), expected) < 0.00001f);
        Vector3 modelNormal = Vector3.Normalize(new Vector3(0.2f, -0.4f, 0.9f));
        var flat = BuildCliff(CreateMap(8), modelNormal, new[] { new Vector3(-79, 37, 53), new Vector3(-83, 43, 59), new Vector3(-89, 47, 61) });
        Vector3 rotated = new(modelNormal.Y, modelNormal.Z, -modelNormal.X);
        checks["offGridCliffNormalsUnchanged"] = Vector3.Distance(Normal(flat, 0), rotated) < 0.00001f;
        // Measured classic CliffsACAA0 boundary normal/position. Before the
        // seam correction this roof corner differed from flat ground by 31.5°.
        Vector3 authoredEdge = new(0.376173f, -0.361763f, 0.853007f); // MDX axes
        Vector3[] edgePositions = { new(-128, 128, 256), new(0, 128, 256), new(-128, 0, 0) };
        var seamFlat = BuildCliff(CreateMap(8), authoredEdge, edgePositions);
        checks["authoredCliffEdgeNormalsMeetFlatGround"] = Enumerable.Range(0, 3)
            .All(i => Vector3.Distance(Normal(seamFlat, i), Vector3.UnitY) < 0.00001f);
        var seamSlope = BuildCliff(slope, authoredEdge, edgePositions);
        checks["authoredCliffEdgeNormalsMeetSlopedGround"] = Enumerable.Range(0, 3)
            .All(i => Vector3.Distance(Normal(seamSlope, i), expected) < 0.00001f);

        var curved = CreateMap(8);
        for (int y = 0; y < curved.Height; y++)
        for (int x = 0; x < curved.Width; x++)
            curved.GetCorner(x, y).GroundHeight = x * x * 0.125f + y * y * 0.0625f;
        // Actual CliffsAAAC0 near-zero coordinates, plus near-128 rounding.
        Vector3[] nearGrid = { new(0, -0.00034f, 256), new(-128, -0.00024f, 0), new(-128, 127.99976f, 256) };
        var rounded = BuildCliff(curved, authoredEdge, nearGrid);
        Vector3[] groundNormals = { new(-1f, 2f, -0.5f), new(-1f, 2f, -0.75f), new(-1.5f, 2f, -0.75f) };
        checks["roundedCliffEdgesUseMatchingGroundStencil"] = Enumerable.Range(0, 3)
            .All(i => Vector3.Distance(Normal(rounded, i), Vector3.Normalize(groundNormals[i])) < 0.00001f);
        var partial = TerrainSurfaceGeometry.CliffNormal(curved, 2, 2, new Vector3(128, 73, 45), rotated);
        checks["cliffEdgeCorrectsOnlyGridComponents"] = Vector3.Distance(partial,
            Vector3.Normalize(new Vector3(-1.5f, rotated.Y, rotated.Z))) < 0.00001f;
        var offGrid = TerrainSurfaceGeometry.CliffNormal(curved, 2, 2, new Vector3(0.01f, 73, 45), rotated);
        checks["nearbyInteriorNormalIsNotSnapped"] = Vector3.Distance(offGrid, rotated) < 0.00001f;
        // First model vertex is (0,0,0), at corner (2,2): raw height=.75
        // tiles; base layer 2 contributes zero. No extra vertical scale.
        checks["cliffHeightUses128WorldUnitsPerTile"] = MathF.Abs(sloped[1] - 96f) < 0.00001f;
        var heights = new GroundLayout(slope, new WarsmashTerrainRules(slope), new[] { false, false }, new[] { 0, 1 }, 2).BuildHeightMap();
        checks["groundHeightUses128WorldUnitsPerTile"] = MathF.Abs(heights[2 * slope.Width + 2] - 96f) < 0.00001f;
        Vector2[] hiveTriangles = { new(1, 1), new(0, 1), new(0, 0), new(0, 0), new(1, 0), new(1, 1) };
        checks["groundTrianglesMatchHiveWe"] = TerrainSurfaceGeometry.GroundTriangleCorners.SequenceEqual(hiveTriangles);
        // On a saddle (BL=TR=0, BR=TL=128), HiveWE's diagonal has center
        // height 0; the former opposite diagonal put the same center at 128.
        checks["saddleCenterMatchesHiveWe"] = MathF.Abs(TriangleHeightAtCenter()) < 0.00001f;

        var saddle = CreateMap(2);
        saddle.GetCorner(1, 0).GroundHeight = 1;
        saddle.GetCorner(0, 1).GroundHeight = 1;
        var sampler = new W3HeightSampler(saddle);
        checks["heightQueryMatchesRenderedSaddle"] = MathF.Abs(sampler.SampleY(64, 64)) < 0.00001f;
        checks["heightQueryMatchesBothTriangles"] =
            MathF.Abs(sampler.SampleY(96, 32) - 64f) < 0.00001f &&
            MathF.Abs(sampler.SampleY(32, 96) - 64f) < 0.00001f;
        checks["heightQueryClampsMapEdges"] = sampler.SampleY(128, 0) == 128f
            && sampler.SampleY(-64, 0) == 0f;

        var authoredMap = CreateMap(2);
        authoredMap.GetCorner(1, 0).LayerHeight = 3;
        authoredMap.GetCorner(1, 1).LayerHeight = 3;
        authoredMap.GetCorner(0, 0).CliffVariation = 7;
        var built = TerrainBuild.Create(authoredMap, _ => true, _ => CliffTypeInfo.Fallback);
        checks["buildDoesNotWriteDerivedFlagsToAuthoredMap"] = authoredMap.Corners.All(c => !c.Cliff && !c.Romp && !c.HideCliff);
        checks["buildUsesHiveWeVariationPolicy"] = built.Cliffs.Count == 1 &&
            built.Cliffs[0].Variation == WarsmashTerrainRules.CliffVariationMaximum[built.Cliffs[0].Tag];
        var missing = TerrainBuild.Create(authoredMap, _ => false, _ => CliffTypeInfo.Fallback);
        checks["missingCliffKeepsGroundAndReportsAsset"] = missing.Cliffs.Count == 0 &&
            missing.MissingModels.Count == 1 &&
            missing.CreateGroundLayout(new[] { false }).BuildExistsMap()[0] == 1;
        authoredMap.GetCorner(1, 0).GroundHeight = 4;
        checks["buildSnapshotSurvivesLaterEdit"] = built.Map.GetCorner(1, 0).GroundHeight == 0;
        var deformed = TerrainSurfaceGeometry.DeformCliffVertex(slope, 2, 2, Vector3.Zero, Vector3.UnitY);
        checks["sharedCliffDeformationMatchesEditor"] = MathF.Abs(deformed.Position.Y - sloped[1]) < 0.00001f
            && Vector3.Distance(deformed.Normal, Normal(sloped, 0)) < 0.00001f;

        // At map edges the upstream SSBO shader can read past its buffer.
        // Our explicitly clamped stencil must remain finite on every edge.
        checks["cliffNormalsFiniteAtMapEdges"] = new[] { Vector3.Zero, new Vector3(7 * 128, 0, 7 * 128), new Vector3(-32, 0, -32) }
            .All(p => Finite(TerrainSurfaceGeometry.CliffNormal(slope, 0, 0, p, Vector3.UnitY)));

        bool pass = checks.Values.All(value => value);
        Console.WriteLine(JsonSerializer.Serialize(new { pass, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return pass ? 0 : 1;
    }

    private static GroundLayout Layout(W3TerrainMap map, WarsmashTerrainRules rules) =>
        new(map, rules, new[] { false, false, false }, new[] { 0, 1 }, 3);

    private static W3TerrainMap CreateMap(int size)
    {
        var map = new W3TerrainMap { Width = size, Height = size };
        map.Corners = Enumerable.Range(0, size * size)
            .Select(_ => new W3TerrainMap.Corner { LayerHeight = 2 }).ToArray();
        map.GroundTilesets.AddRange(new[] { "Ldrt", "Lgrs" });
        map.CliffTilesets.AddRange(new[] { "CLdi", "CLgr" });
        return map;
    }

    private static Vector3 Normal(List<float> vertices, int index) =>
        new(vertices[index * 8 + 3], vertices[index * 8 + 4], vertices[index * 8 + 5]);

    private static bool Finite(Vector3 n) => float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z);

    private static float TriangleHeightAtCenter()
    {
        ReadOnlySpan<Vector2> corners = TerrainSurfaceGeometry.GroundTriangleCorners;
        Vector2 a = corners[0], b = corners[1], c = corners[2], point = new(0.5f, 0.5f);
        float cross = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        float wa = ((b.Y - c.Y) * (point.X - c.X) + (c.X - b.X) * (point.Y - c.Y)) / cross;
        float wb = ((c.Y - a.Y) * (point.X - c.X) + (a.X - c.X) * (point.Y - c.Y)) / cross;
        static float Height(Vector2 p) => p.X == p.Y ? 0f : 128f;
        return wa * Height(a) + wb * Height(b) + (1f - wa - wb) * Height(c);
    }

    private static List<float> BuildCliff(W3TerrainMap map, Vector3 normal, Vector3[]? positions = null)
    {
        using var scene = new GlTerrainScene();
        // Exercise the production mesh emission without requiring a GL context
        // or proprietary assets. Only the input map is injected by reflection.
        typeof(GlTerrainScene).GetField("_map", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, map);
        var model = new MdxStaticModel();
        model.Geosets.Add(new MdxStaticModel.Geoset
        {
            Positions = positions == null ? new() { 0, 0, 0, 0, 128, 0, -128, 0, 0 }
                : positions.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToList(),
            Normals = Enumerable.Range(0, 3).SelectMany(_ => new[] { normal.X, normal.Y, normal.Z }).ToList(),
            Faces = new() { 0, 1, 2 },
        });
        var vertices = new List<float>();
        var placement = new WarsmashTerrainRules.CliffPlacement(2, 2, "AAAB", false, 0, 0, 2, "fixture");
        typeof(GlTerrainScene).GetMethod("AppendCliffInstance", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scene, new object[] { vertices, new List<uint>(), model, placement });
        return vertices;
    }
}
