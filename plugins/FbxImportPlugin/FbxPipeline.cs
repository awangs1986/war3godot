using System.Globalization;
using System.Text.Json;
using Assimp;
using Assimp.Configs;
using W3Editor.PluginContracts;

namespace FbxImportPlugin;

public enum FbxOwnership
{
    External,
    MapPacked,
}

public sealed class FbxScaleDecision
{
    public float ScaleFactor = 1f;
    public bool AutoScaled;
}

public sealed class FbxImportResult
{
    public PluginMeshPreview Mesh = new();
    public PluginValidationReport Report = new();
    public string ModelId = "";
    public string OutputDir = "";
}

/// <summary>FBX/OBJ import: axis fix, fountain×10 scale, structure check, dual write.</summary>
public static class FbxPipeline
{
    public const string MakerGuidanceZh =
        "比例指引：道具以 Lost Temple 喷泉（City_Fountain）为 1× 视觉锚点；"
        + "大型建筑/地标最长边不超过喷泉的 10×。"
        + "坐标约定：+X 东、+Y 北、+Z 上（与编辑器 / d1map 一致）。"
        + "贴图建议提供 albedo/diffuse、normal、ORM（或 metal/rough），分辨率 512 或 1024。";

    /// <summary>
    /// If <paramref name="longestEdge"/> exceeds fountain×10, return uniform scale to clamp to MaxEdge.
    /// </summary>
    public static FbxScaleDecision ApplyScale(float longestEdge)
    {
        if (longestEdge > FountainReference.MaxEdgeWorldUnits && longestEdge > 1e-8f)
        {
            return new FbxScaleDecision
            {
                AutoScaled = true,
                ScaleFactor = FountainReference.MaxEdgeWorldUnits / longestEdge,
            };
        }

        return new FbxScaleDecision { AutoScaled = false, ScaleFactor = 1f };
    }

    public static FbxImportResult Import(string fbxPath, FbxOwnership ownership, string targetRoot)
    {
        if (string.IsNullOrWhiteSpace(fbxPath) || !File.Exists(fbxPath))
            throw new FileNotFoundException("模型文件不存在", fbxPath);
        if (string.IsNullOrWhiteSpace(targetRoot))
            throw new ArgumentException("targetRoot 不能为空", nameof(targetRoot));

        string ownershipLabel = ownership == FbxOwnership.External ? "external" : "map-packed";
        string modelId = SanitizeModelId(Path.GetFileNameWithoutExtension(fbxPath));
        string outputDir = ownership == FbxOwnership.External
            ? Path.Combine(targetRoot, modelId)
            : Path.Combine(targetRoot, "imported", modelId);
        outputDir = EnsureUniqueDir(outputDir, ref modelId, ownership, targetRoot);
        RequireUnderRoot(outputDir, targetRoot);

        using var context = new AssimpContext();
        context.SetConfig(new NormalSmoothingAngleConfig(66f));
        Scene? scene;
        try
        {
            scene = context.ImportFile(fbxPath,
                PostProcessSteps.Triangulate
                | PostProcessSteps.GenerateNormals
                | PostProcessSteps.JoinIdenticalVertices
                | PostProcessSteps.ImproveCacheLocality);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Assimp 无法打开模型：" + ex.Message, ex);
        }

        if (scene == null || !scene.HasMeshes)
            throw new InvalidDataException("模型无网格数据");

        var positions = new List<float>();
        var normals = new List<float>();
        var uvs = new List<float>();
        var indices = new List<int>();

        CollectMeshes(scene, scene.RootNode, Matrix4x4.Identity, positions, normals, uvs, indices);
        if (positions.Count < 9 || indices.Count < 3)
            throw new InvalidDataException("模型网格为空");

        // Assimp typically: +X right, +Y up, +Z toward camera.
        // Editor / d1map: +X east, +Y north, +Z up. Proper rotation about X:
        // (x, y_up, z) → (x, -z, y_up); a plain swap would mirror the mesh.
        SwapAssimpYUpToEditorZUp(positions, normals);

        float longest = LongestEdge(positions);
        FbxScaleDecision scale = ApplyScale(longest);
        if (scale.AutoScaled)
            ScalePositions(positions, scale.ScaleFactor);

        var issues = new List<PluginValidationIssue>();
        MaterialStructureChecker.Check(scene, Path.GetDirectoryName(fbxPath) ?? "", issues);

        issues.Insert(0, new PluginValidationIssue
        {
            Severity = PluginIssueSeverity.Info,
            Code = "maker-guidance",
            Message = MakerGuidanceZh,
        });
        issues.Insert(1, new PluginValidationIssue
        {
            Severity = PluginIssueSeverity.Info,
            Code = "size",
            Message = scale.AutoScaled
                ? $"原始最长边 {longest:0.###}，已自动缩放到喷泉×10 上限 {FountainReference.MaxEdgeWorldUnits:0.###}（系数 ×{scale.ScaleFactor:0.####}）。"
                : $"最长边 {longest:0.###}，未超过喷泉×10 上限 {FountainReference.MaxEdgeWorldUnits:0.###}，未缩放。",
        });

        string? albedoCopy = CopyTextures(scene, Path.GetDirectoryName(fbxPath) ?? "", outputDir, issues);

        Directory.CreateDirectory(outputDir);
        WriteMeshBin(Path.Combine(outputDir, "mesh.bin"), positions, normals, uvs, indices);
        WritePreviewObj(Path.Combine(outputDir, "preview.obj"), positions, normals, uvs, indices);

        var report = new PluginValidationReport
        {
            Ownership = ownershipLabel,
            TargetDir = outputDir,
            AutoScaled = scale.AutoScaled,
            ScaleFactor = scale.ScaleFactor,
            Issues = issues,
        };

        WriteMetaJson(Path.Combine(outputDir, "meta.json"), fbxPath, ownershipLabel, scale, issues);

        return new FbxImportResult
        {
            ModelId = modelId,
            OutputDir = outputDir,
            Report = report,
            Mesh = new PluginMeshPreview
            {
                Positions = positions.ToArray(),
                Normals = normals.ToArray(),
                Uv0 = uvs.ToArray(),
                Indices = indices.ToArray(),
                AlbedoPath = albedoCopy,
            },
        };
    }

    private static string EnsureUniqueDir(
        string desired,
        ref string modelId,
        FbxOwnership ownership,
        string targetRoot)
    {
        string baseId = modelId;
        string candidate = desired;
        int attempt = 0;
        while (Directory.Exists(candidate))
        {
            attempt++;
            string suffix = attempt == 1
                ? Math.Abs(HashCode.Combine(baseId, targetRoot, desired))
                    .ToString("x", CultureInfo.InvariantCulture)
                : attempt.ToString(CultureInfo.InvariantCulture);
            if (suffix.Length > 8)
                suffix = suffix[..8];
            modelId = baseId + "_" + suffix;
            candidate = ownership == FbxOwnership.External
                ? Path.Combine(targetRoot, modelId)
                : Path.Combine(targetRoot, "imported", modelId);
        }

        return candidate;
    }

    /// <summary>Windows reserved device names that are invalid directory names.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static string SanitizeModelId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "model";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        // '.' is a valid filename char, so "...fbx" would otherwise yield the
        // id ".." and escape the designated root via Path.Combine.
        name = name.Trim().Trim('.').Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(name) || ReservedNames.Contains(name))
            name = "model";
        return name;
    }

    /// <summary>
    /// Rejects an output directory that resolved outside the designated root —
    /// the last line of defense against sanitizer gaps and hostile file names.
    /// </summary>
    private static void RequireUnderRoot(string outputDir, string root)
    {
        string full = Path.GetFullPath(outputDir);
        string rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"输出目录越界：{outputDir} 不在 {root} 内");
    }

    private static void CollectMeshes(
        Scene scene,
        Node node,
        Matrix4x4 parent,
        List<float> positions,
        List<float> normals,
        List<float> uvs,
        List<int> indices)
    {
        // AssimpNet's operator* is row-vector (transposed) while TransformPoint
        // applies matrices column-vector style (translation in A4/B4/C4), so the
        // child-then-parent composition must be written child * parent here.
        Matrix4x4 transform = node.Transform * parent;
        Matrix4x4 normalMatrix = transform;
        normalMatrix.Inverse();
        normalMatrix.Transpose();

        foreach (int meshIndex in node.MeshIndices)
        {
            if (meshIndex < 0 || meshIndex >= scene.MeshCount)
                continue;
            Mesh mesh = scene.Meshes[meshIndex];
            int baseVertex = positions.Count / 3;

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Vector3D p = mesh.Vertices[i];
                TransformPoint(transform, p.X, p.Y, p.Z, out float px, out float py, out float pz);
                positions.Add(px);
                positions.Add(py);
                positions.Add(pz);

                if (mesh.HasNormals && i < mesh.Normals.Count)
                {
                    Vector3D n = mesh.Normals[i];
                    TransformVector(normalMatrix, n.X, n.Y, n.Z, out float nx, out float ny, out float nz);
                    float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len > 1e-8f)
                    {
                        nx /= len;
                        ny /= len;
                        nz /= len;
                    }
                    normals.Add(nx);
                    normals.Add(ny);
                    normals.Add(nz);
                }
                else
                {
                    normals.Add(0f);
                    normals.Add(1f);
                    normals.Add(0f);
                }

                if (mesh.HasTextureCoords(0) && i < mesh.TextureCoordinateChannels[0].Count)
                {
                    Vector3D uv = mesh.TextureCoordinateChannels[0][i];
                    uvs.Add(uv.X);
                    uvs.Add(uv.Y);
                }
                else
                {
                    uvs.Add(0f);
                    uvs.Add(0f);
                }
            }

            foreach (Face face in mesh.Faces)
            {
                if (face.IndexCount < 3)
                    continue;
                for (int t = 0; t < face.IndexCount - 2; t++)
                {
                    indices.Add(baseVertex + face.Indices[0]);
                    indices.Add(baseVertex + face.Indices[t + 1]);
                    indices.Add(baseVertex + face.Indices[t + 2]);
                }
            }
        }

        foreach (Node child in node.Children)
            CollectMeshes(scene, child, transform, positions, normals, uvs, indices);
    }

    private static void TransformPoint(
        Matrix4x4 m, float x, float y, float z,
        out float ox, out float oy, out float oz)
    {
        ox = m.A1 * x + m.A2 * y + m.A3 * z + m.A4;
        oy = m.B1 * x + m.B2 * y + m.B3 * z + m.B4;
        oz = m.C1 * x + m.C2 * y + m.C3 * z + m.C4;
    }

    private static void TransformVector(
        Matrix4x4 m, float x, float y, float z,
        out float ox, out float oy, out float oz)
    {
        ox = m.A1 * x + m.A2 * y + m.A3 * z;
        oy = m.B1 * x + m.B2 * y + m.B3 * z;
        oz = m.C1 * x + m.C2 * y + m.C3 * z;
    }

    private static void SwapAssimpYUpToEditorZUp(List<float> positions, List<float> normals)
    {
        // Proper +90° rotation about X: (x, y, z) -> (x, -z, y). A plain axis
        // swap (x, z, y) has determinant -1 — it mirrors the model and flips
        // triangle winding, which persisted wrong geometry to mesh.bin.
        for (int i = 0; i + 2 < positions.Count; i += 3)
        {
            float y = positions[i + 1], z = positions[i + 2];
            positions[i + 1] = -z;
            positions[i + 2] = y;
        }

        for (int i = 0; i + 2 < normals.Count; i += 3)
        {
            float y = normals[i + 1], z = normals[i + 2];
            normals[i + 1] = -z;
            normals[i + 2] = y;
        }
    }

    private static float LongestEdge(List<float> positions)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i + 2 < positions.Count; i += 3)
        {
            float x = positions[i], y = positions[i + 1], z = positions[i + 2];
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        return Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
    }

    private static void ScalePositions(List<float> positions, float factor)
    {
        for (int i = 0; i < positions.Count; i++)
            positions[i] *= factor;
    }

    private static string? CopyTextures(
        Scene scene,
        string modelDir,
        string outputDir,
        List<PluginValidationIssue> issues)
    {
        Directory.CreateDirectory(outputDir);
        string? firstAlbedo = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Material mat in scene.Materials)
        {
            foreach (var slot in new[]
                     {
                         TextureType.Diffuse,
                         TextureType.Normals,
                         TextureType.Unknown,
                         TextureType.Specular,
                         TextureType.Ambient,
                         TextureType.Emissive,
                         TextureType.Lightmap,
                     })
            {
                int count = mat.GetMaterialTextureCount(slot);
                for (int i = 0; i < count; i++)
                {
                    if (!mat.GetMaterialTexture(slot, i, out TextureSlot tex))
                        continue;
                    string rel = tex.FilePath?.Trim() ?? "";
                    if (rel.Length == 0 || !seen.Add(slot + ":" + rel))
                        continue;

                    string resolved = ResolveTexturePath(modelDir, rel);
                    if (!File.Exists(resolved))
                        continue;

                    string destName = Path.GetFileName(resolved);
                    if (string.IsNullOrWhiteSpace(destName))
                        continue;
                    string dest = Path.Combine(outputDir, destName);
                    try
                    {
                        File.Copy(resolved, dest, overwrite: true);
                        if (firstAlbedo == null
                            && (slot == TextureType.Diffuse
                                || destName.Contains("_albedo", StringComparison.OrdinalIgnoreCase)
                                || destName.Contains("_diffuse", StringComparison.OrdinalIgnoreCase)))
                            firstAlbedo = dest;
                    }
                    catch (Exception ex)
                    {
                        issues.Add(new PluginValidationIssue
                        {
                            Severity = PluginIssueSeverity.Warn,
                            Code = "texture-copy",
                            Message = $"无法复制贴图 {destName}：{ex.Message}",
                        });
                    }
                }
            }
        }

        return firstAlbedo;
    }

    private static string ResolveTexturePath(string modelDir, string filePath)
    {
        string cleaned = filePath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(cleaned) && File.Exists(cleaned))
            return cleaned;
        string joined = Path.GetFullPath(Path.Combine(modelDir, cleaned));
        if (File.Exists(joined))
            return joined;
        string byName = Path.Combine(modelDir, Path.GetFileName(cleaned));
        return File.Exists(byName) ? byName : joined;
    }

    private static void WriteMeshBin(
        string path,
        List<float> positions,
        List<float> normals,
        List<float> uvs,
        List<int> indices)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(0x3148534Du); // 'MSH1'
        int vertexCount = positions.Count / 3;
        bw.Write(vertexCount);
        bw.Write(indices.Count);
        for (int i = 0; i < positions.Count; i++)
            bw.Write(positions[i]);
        for (int i = 0; i < normals.Count; i++)
            bw.Write(normals[i]);
        for (int i = 0; i < uvs.Count; i++)
            bw.Write(uvs[i]);
        for (int i = 0; i < indices.Count; i++)
            bw.Write(indices[i]);
    }

    private static void WritePreviewObj(
        string path,
        List<float> positions,
        List<float> normals,
        List<float> uvs,
        List<int> indices)
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine("# FbxImportPlugin preview");
        int verts = positions.Count / 3;
        for (int i = 0; i < verts; i++)
        {
            sw.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"v {positions[i * 3]} {positions[i * 3 + 1]} {positions[i * 3 + 2]}"));
        }
        for (int i = 0; i < verts; i++)
        {
            sw.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"vn {normals[i * 3]} {normals[i * 3 + 1]} {normals[i * 3 + 2]}"));
        }
        for (int i = 0; i < verts; i++)
        {
            sw.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"vt {uvs[i * 2]} {uvs[i * 2 + 1]}"));
        }
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int a = indices[i] + 1, b = indices[i + 1] + 1, c = indices[i + 2] + 1;
            sw.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
        }
    }

    private static void WriteMetaJson(
        string path,
        string source,
        string ownership,
        FbxScaleDecision scale,
        List<PluginValidationIssue> issues)
    {
        var payload = new
        {
            source,
            ownership,
            scaleFactor = scale.ScaleFactor,
            autoScaled = scale.AutoScaled,
            fountainMaxEdge = FountainReference.MaxEdgeWorldUnits,
            fountainEdge = FountainReference.EdgeWorldUnits,
            issues = issues.Select(i => new
            {
                severity = i.Severity.ToString(),
                code = i.Code,
                message = i.Message,
            }).ToArray(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }
}
