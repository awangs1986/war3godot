using Godot;

namespace Diablo1Like.W3.Mdx;

/// <summary>
/// Classic MDX material response shared by animated instances and static batches.
/// Lighting presets never rewrite materials. HD materials retain authored ORM.
/// </summary>
internal static class W3ModelMaterials
{
    internal static void ApplyClassicDefaults(StandardMaterial3D material)
    {
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        material.Metallic = 0f;
        material.Roughness = 0.82f;
        material.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
        material.MetallicSpecular = 0.24f;
    }

    internal static StandardMaterial3D CreateClassic(Texture2D? texture, string path,
        int filterMode, int flags, float alpha)
    {
        var material = new StandardMaterial3D
        {
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlbedoTexture = texture,
        };
        ApplyClassicDefaults(material);
        Color color = texture != null ? Colors.White : new Color(0.45f, 0.55f, 0.35f);
        color.A = Mathf.Clamp(alpha, 0f, 1f);
        material.AlbedoColor = color;

        // Retain the classic importer filename fallback for mode 0 assets.
        // Explicit layer modes and unshaded flags always take precedence.
        string lower = path.ToLowerInvariant();
        bool cutout = lower.Contains("tree") || lower.Contains("plant") || lower.Contains("leaf")
            || lower.Contains("canopy") || lower.Contains("grass") || lower.Contains("vine");
        bool additive = lower.Contains("glow") || lower.Contains("fire") || lower.Contains("flame")
            || lower.Contains("torch") || lower.Contains("lensflare") || lower.Contains("lightning");
        if (filterMode == 0)
            filterMode = cutout ? 1 : additive ? 3 : 0;
        switch (filterMode)
        {
            case 1:
                material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                material.AlphaScissorThreshold = 0.28f;
                break;
            case 2:
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                break;
            case 3:
            case 4:
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                material.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                break;
            case 5:
            case 6:
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                material.BlendMode = BaseMaterial3D.BlendModeEnum.Mul;
                break;
        }
        if ((flags & 0x1) != 0 || filterMode is 3 or 4)
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        return material;
    }
}
