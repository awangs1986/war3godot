using System.Text.Json;
using System.Text.Json.Nodes;
using FbxImportPlugin;
using W3Editor.PluginContracts;
using W3Editor.Services;

namespace W3Editor.Mcp;

/// <summary>
/// FBX import plugin acceptance (Task 4): scale gate + dual ownership write.
/// Usage: --fbx-import-accept [outDir]
/// </summary>
internal static class FbxImportAccept
{
    public static int Run(string[] args)
    {
        string outDir = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(EditorPaths.OutDir, "fbx_import_accept");
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(outDir);

        float maxEdge = FountainReference.MaxEdgeWorldUnits;
        float halfMax = 0.5f * maxEdge;
        float doubleMax = 2f * maxEdge;

        // --- ApplyScale pure function ---
        var small = FbxPipeline.ApplyScale(halfMax);
        var large = FbxPipeline.ApplyScale(doubleMax);

        bool smallNoScale = !small.AutoScaled
            && Math.Abs(small.ScaleFactor - 1f) < 1e-5f;
        bool largeScaled = large.AutoScaled
            && Math.Abs(large.ScaleFactor * doubleMax - maxEdge) < 0.01f;

        // --- Dual ownership write via Import on synthetic OBJ cubes ---
        string sharedRoot = Path.Combine(outDir, "shared_models");
        string mapRoot = Path.Combine(outDir, "map");
        Directory.CreateDirectory(sharedRoot);
        Directory.CreateDirectory(mapRoot);

        string smallObj = WriteCubeObj(Path.Combine(outDir, "cube_small.obj"), halfMax);
        string largeObj = WriteCubeObj(Path.Combine(outDir, "cube_large.obj"), doubleMax);

        FbxImportResult external = FbxPipeline.Import(smallObj, FbxOwnership.External, sharedRoot);
        FbxImportResult packed = FbxPipeline.Import(largeObj, FbxOwnership.MapPacked, mapRoot);
        // Third import of the same source must not overwrite the first External dir.
        FbxImportResult external2 = FbxPipeline.Import(smallObj, FbxOwnership.External, sharedRoot);
        FbxImportResult external3 = FbxPipeline.Import(smallObj, FbxOwnership.External, sharedRoot);

        string externalMeta = Path.Combine(sharedRoot, external.ModelId, "meta.json");
        string packedMeta = Path.Combine(mapRoot, "imported", packed.ModelId, "meta.json");

        bool externalWrote = File.Exists(externalMeta)
            && Directory.Exists(Path.Combine(sharedRoot, external.ModelId));
        bool packedWrote = File.Exists(packedMeta)
            && Directory.Exists(Path.Combine(mapRoot, "imported", packed.ModelId));

        bool externalNoAuto = !external.Report.AutoScaled
            && Math.Abs(external.Report.ScaleFactor - 1f) < 1e-5f;
        bool packedAuto = packed.Report.AutoScaled
            && ApproxLongestEdge(packed.Mesh, maxEdge);

        bool uniqueDirs = external.OutputDir != external2.OutputDir
            && external2.OutputDir != external3.OutputDir
            && external.OutputDir != external3.OutputDir
            && Directory.Exists(external.OutputDir)
            && Directory.Exists(external2.OutputDir)
            && Directory.Exists(external3.OutputDir);

        JsonObject? extMetaJson = externalWrote
            ? JsonNode.Parse(File.ReadAllText(externalMeta)) as JsonObject
            : null;
        JsonObject? packMetaJson = packedWrote
            ? JsonNode.Parse(File.ReadAllText(packedMeta)) as JsonObject
            : null;

        bool metaFieldsOk = MetaHasRequired(extMetaJson, "external")
            && MetaHasRequired(packMetaJson, "map-packed");

        bool makerGuidanceOk = HasMakerGuidance(external.Report)
            && HasMakerGuidance(packed.Report)
            && MetaHasMakerGuidance(extMetaJson)
            && MetaHasMakerGuidance(packMetaJson);

        bool pass = smallNoScale && largeScaled
            && externalWrote && packedWrote
            && externalNoAuto && packedAuto
            && uniqueDirs
            && metaFieldsOk
            && makerGuidanceOk
            && FountainReference.EdgeWorldUnits > 0f
            && Math.Abs(FountainReference.MaxEdgeWorldUnits
                - FountainReference.EdgeWorldUnits * 10f) < 1e-3f;

        var report = new
        {
            outDir,
            fountainEdge = FountainReference.EdgeWorldUnits,
            fountainMaxEdge = FountainReference.MaxEdgeWorldUnits,
            smallNoScale,
            largeScaled,
            largeScaleFactor = large.ScaleFactor,
            externalWrote,
            packedWrote,
            externalMeta,
            packedMeta,
            externalNoAuto,
            packedAuto,
            packedScaleFactor = packed.Report.ScaleFactor,
            uniqueDirs,
            externalIds = new[] { external.ModelId, external2.ModelId, external3.ModelId },
            metaFieldsOk,
            makerGuidanceOk,
            pass,
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outDir, "fbx_import_accept.json"), json);
        Console.WriteLine(json);
        return pass ? 0 : 1;
    }

    private static bool MetaHasRequired(JsonObject? meta, string ownership)
    {
        if (meta == null)
            return false;
        return meta["source"] != null
            && meta["ownership"]?.GetValue<string>() == ownership
            && meta["scaleFactor"] != null
            && meta["autoScaled"] != null
            && meta["issues"] is JsonArray
            && meta["fountainMaxEdge"] != null;
    }

    private static bool HasMakerGuidance(PluginValidationReport report) =>
        report.Issues.Any(i =>
            i.Code == "maker-guidance"
            && i.Message.Contains("喷泉", StringComparison.Ordinal)
            && i.Message.Contains("10", StringComparison.Ordinal));

    private static bool MetaHasMakerGuidance(JsonObject? meta)
    {
        if (meta?["issues"] is not JsonArray issues)
            return false;
        foreach (JsonNode? node in issues)
        {
            if (node is not JsonObject obj)
                continue;
            string code = obj["code"]?.GetValue<string>() ?? "";
            string message = obj["message"]?.GetValue<string>() ?? "";
            if (code == "maker-guidance"
                && message.Contains("喷泉", StringComparison.Ordinal)
                && message.Contains("10", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool ApproxLongestEdge(W3Editor.PluginContracts.PluginMeshPreview mesh, float expected)
    {
        if (mesh.Positions.Length < 3)
            return false;
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i + 2 < mesh.Positions.Length; i += 3)
        {
            float x = mesh.Positions[i], y = mesh.Positions[i + 1], z = mesh.Positions[i + 2];
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        float edge = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        return Math.Abs(edge - expected) < 0.5f;
    }

    /// <summary>Axis-aligned cube centered at origin with the given edge length (OBJ).</summary>
    private static string WriteCubeObj(string path, float edge)
    {
        float h = edge * 0.5f;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# fbx-import-accept synthetic cube");
        sb.AppendLine($"v {-h} {-h} {-h}");
        sb.AppendLine($"v {h} {-h} {-h}");
        sb.AppendLine($"v {h} {h} {-h}");
        sb.AppendLine($"v {-h} {h} {-h}");
        sb.AppendLine($"v {-h} {-h} {h}");
        sb.AppendLine($"v {h} {-h} {h}");
        sb.AppendLine($"v {h} {h} {h}");
        sb.AppendLine($"v {-h} {h} {h}");
        sb.AppendLine("f 1 2 3 4");
        sb.AppendLine("f 5 8 7 6");
        sb.AppendLine("f 1 5 6 2");
        sb.AppendLine("f 2 6 7 3");
        sb.AppendLine("f 3 7 8 4");
        sb.AppendLine("f 4 8 5 1");
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
