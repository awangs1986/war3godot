using Assimp;
using W3Editor.PluginContracts;

namespace FbxImportPlugin;

/// <summary>Structure-only material / texture checks (no pixel analysis).</summary>
public static class MaterialStructureChecker
{
    public static void Check(Scene scene, string modelDir, List<PluginValidationIssue> issues)
    {
        if (scene.MaterialCount == 0)
        {
            issues.Add(new PluginValidationIssue
            {
                Severity = PluginIssueSeverity.Warn,
                Code = "no-materials",
                Message = "模型未包含材质；预览将使用灰模。",
            });
            if (!SceneHasVertexColors(scene))
            {
                issues.Add(new PluginValidationIssue
                {
                    Severity = PluginIssueSeverity.Error,
                    Code = "no-color-source",
                    Message = "无可渲染色：无材质贴图且网格无顶点色。将以灰模预览。",
                });
            }
            return;
        }

        bool anyColor = false;
        for (int mi = 0; mi < scene.MaterialCount; mi++)
        {
            Material mat = scene.Materials[mi];
            string matName = string.IsNullOrWhiteSpace(mat.Name) ? $"材质[{mi}]" : mat.Name;

            bool hasAlbedo = HasTexture(mat, TextureType.Diffuse);
            bool hasNormal = HasTexture(mat, TextureType.Normals) || HasTexture(mat, TextureType.Height);
            bool hasOrm = HasTexture(mat, TextureType.Unknown)
                || HasTexture(mat, TextureType.Specular)
                || HasTexture(mat, TextureType.Shininess)
                || HasNamedTexture(mat, "_orm", "_mrao", "_metallic", "_roughness", "_metal", "_rough");

            if (hasAlbedo)
                anyColor = true;
            else if (mat.HasColorDiffuse && mat.ColorDiffuse.A > 0.01f
                     && (mat.ColorDiffuse.R + mat.ColorDiffuse.G + mat.ColorDiffuse.B) > 0.01f)
                anyColor = true;

            if (!hasAlbedo)
            {
                issues.Add(new PluginValidationIssue
                {
                    Severity = PluginIssueSeverity.Warn,
                    Code = "missing-albedo",
                    Message = $"{matName}：缺少 albedo/diffuse 贴图槽；若无顶点色将无法正确着色。",
                });
            }

            if (!hasNormal)
            {
                issues.Add(new PluginValidationIssue
                {
                    Severity = PluginIssueSeverity.Warn,
                    Code = "missing-normal",
                    Message = $"{matName}：建议提供 normal 贴图。",
                });
            }

            if (!hasOrm)
            {
                issues.Add(new PluginValidationIssue
                {
                    Severity = PluginIssueSeverity.Warn,
                    Code = "missing-orm",
                    Message = $"{matName}：建议提供 ORM / metallic-roughness 贴图。",
                });
            }

            InspectTextureSlots(mat, matName, modelDir, issues);
        }

        if (!anyColor && !SceneHasVertexColors(scene))
        {
            issues.Add(new PluginValidationIssue
            {
                Severity = PluginIssueSeverity.Error,
                Code = "no-color-source",
                Message = "无可渲染色：所有材质均无 albedo/diffuse，且网格无顶点色。将以灰模预览。",
            });
        }
    }

    private static bool SceneHasVertexColors(Scene scene)
    {
        foreach (Mesh mesh in scene.Meshes)
        {
            if (mesh.VertexColorChannelCount > 0 && mesh.VertexColorChannels[0].Count > 0)
                return true;
        }
        return false;
    }

    private static bool HasTexture(Material mat, TextureType type)
        => mat.GetMaterialTextureCount(type) > 0;

    private static bool HasNamedTexture(Material mat, params string[] tokens)
    {
        foreach (TextureType type in Enum.GetValues<TextureType>())
        {
            int count = mat.GetMaterialTextureCount(type);
            for (int i = 0; i < count; i++)
            {
                if (!mat.GetMaterialTexture(type, i, out TextureSlot slot))
                    continue;
                string path = slot.FilePath ?? "";
                foreach (string token in tokens)
                {
                    if (path.Contains(token, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        return false;
    }

    private static void InspectTextureSlots(
        Material mat,
        string matName,
        string modelDir,
        List<PluginValidationIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TextureType type in Enum.GetValues<TextureType>())
        {
            int count = mat.GetMaterialTextureCount(type);
            for (int i = 0; i < count; i++)
            {
                if (!mat.GetMaterialTexture(type, i, out TextureSlot slot))
                    continue;
                string rel = slot.FilePath?.Trim() ?? "";
                if (rel.Length == 0 || !seen.Add(rel))
                    continue;

                string resolved = ResolvePath(modelDir, rel);
                if (!File.Exists(resolved))
                {
                    issues.Add(new PluginValidationIssue
                    {
                        Severity = PluginIssueSeverity.Error,
                        Code = "missing-texture-path",
                        Message = $"{matName}：贴图路径不存在 — {rel}",
                    });
                    continue;
                }

                CheckNaming(matName, Path.GetFileName(resolved), issues);
                CheckResolution(matName, resolved, issues);
            }
        }
    }

    private static void CheckNaming(string matName, string fileName, List<PluginValidationIssue> issues)
    {
        bool ok = ContainsAny(fileName, "_albedo", "_diffuse", "_normal", "_orm", "_mrao",
            "_metallic", "_roughness", "_metal", "_rough");
        issues.Add(new PluginValidationIssue
        {
            Severity = PluginIssueSeverity.Info,
            Code = "texture-naming",
            Message = ok
                ? $"{matName}：贴图命名符合约定 — {fileName}"
                : $"{matName}：建议贴图命名含 _albedo/_diffuse、_normal、_orm/_mrao — 当前为 {fileName}",
        });
    }

    private static bool ContainsAny(string name, params string[] tokens)
    {
        foreach (string t in tokens)
        {
            if (name.Contains(t, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void CheckResolution(string matName, string path, List<PluginValidationIssue> issues)
    {
        if (!TryReadImageSize(path, out int w, out int h))
            return;

        bool sizeOk = (w == 512 || w == 1024) && (h == 512 || h == 1024);
        if (!sizeOk)
        {
            issues.Add(new PluginValidationIssue
            {
                Severity = PluginIssueSeverity.Warn,
                Code = "texture-resolution",
                Message = $"{matName}：贴图 {Path.GetFileName(path)} 为 {w}×{h}，建议 512 或 1024。",
            });
        }

        if (!IsPowerOfTwo(w) || !IsPowerOfTwo(h))
        {
            issues.Add(new PluginValidationIssue
            {
                Severity = PluginIssueSeverity.Warn,
                Code = "texture-pow2",
                Message = $"{matName}：贴图 {Path.GetFileName(path)} 宽高不是 2 的幂（{w}×{h}）。",
            });
        }
    }

    private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>Minimal PNG / JPEG / TGA header peek (no Skia dependency in plugin).</summary>
    private static bool TryReadImageSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[32];
            int read = fs.Read(header);
            if (read < 16)
                return false;

            // PNG: 8-byte signature + IHDR width/height big-endian
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                if (read < 24)
                    return false;
                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }

            // JPEG: scan for SOF0/SOF2
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                fs.Position = 2;
                while (fs.Position < fs.Length - 8)
                {
                    int b0 = fs.ReadByte();
                    if (b0 != 0xFF)
                        continue;
                    int marker;
                    do
                    {
                        marker = fs.ReadByte();
                    } while (marker == 0xFF);
                    if (marker < 0)
                        break;
                    int lenHi = fs.ReadByte();
                    int lenLo = fs.ReadByte();
                    if (lenHi < 0 || lenLo < 0)
                        break;
                    int segLen = (lenHi << 8) | lenLo;
                    if (marker is 0xC0 or 0xC1 or 0xC2)
                    {
                        fs.ReadByte(); // precision
                        int hHi = fs.ReadByte(), hLo = fs.ReadByte();
                        int wHi = fs.ReadByte(), wLo = fs.ReadByte();
                        height = (hHi << 8) | hLo;
                        width = (wHi << 8) | wLo;
                        return width > 0 && height > 0;
                    }
                    if (segLen < 2)
                        break;
                    fs.Position += segLen - 2;
                }
            }

            // TGA (uncompressed/true-color): width/height at offset 12 little-endian
            string ext = Path.GetExtension(path);
            if (ext.Equals(".tga", StringComparison.OrdinalIgnoreCase) && read >= 18)
            {
                width = header[12] | (header[13] << 8);
                height = header[14] | (header[15] << 8);
                return width > 0 && height > 0;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string ResolvePath(string modelDir, string filePath)
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
}
