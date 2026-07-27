using System.Numerics;

namespace W3Compat.Mdx;

public sealed record MdxResourceReport(
    string ModelName,
    int Version,
    IReadOnlyList<string> Sequences,
    IReadOnlyList<string> StandSequences,
    IReadOnlyList<string> ReferencedTextures,
    IReadOnlyList<string> MissingTextures,
    IReadOnlyList<string> Warnings)
{
    public bool HasStand => StandSequences.Count > 0;
    public bool IsRenderable => Warnings.All(w => !w.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Engine-agnostic model/asset diagnostics used by editor and future Godot adapters.</summary>
public static class MdxResourceDiagnostics
{
    public static MdxResourceReport Analyze(MdxModel model, Func<string, bool>? textureExists = null)
    {
        var warnings = new List<string>();
        var stand = model.Sequences
            .Where(sequence => sequence.Name.Split('-')[0].Trim().Equals("stand", StringComparison.OrdinalIgnoreCase))
            .Select(sequence => sequence.Name).ToList();
        if (model.Sequences.Count == 0) warnings.Add("fatal:no animation sequences");
        if (stand.Count == 0) warnings.Add("warning:no Stand animation");
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MdxTexture texture in model.Textures)
        {
            string path = texture.Path.Length > 0 ? texture.Path : MdxModel.ReplaceableRelativePath(texture.ReplaceableId);
            if (path.Length == 0)
            {
                warnings.Add($"warning:unresolved replaceable texture id {texture.ReplaceableId}");
                continue;
            }
            referenced.Add(path);
        }
        var missing = textureExists == null
            ? new List<string>()
            : referenced.Where(path => !textureExists(path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string path in missing) warnings.Add("missing texture: " + path);
        foreach (MdxNode node in model.Bones)
            if (node.ParentId >= model.Bones.Count || node.ParentId < -1)
                warnings.Add($"warning:invalid parent for node {node.Name} ({node.ParentId})");
        foreach (List<MdxLayer> material in model.Materials)
        foreach (MdxLayer layer in material)
            if (layer.TextureId >= model.Textures.Count && layer.TextureId >= 0)
                warnings.Add($"warning:material texture index out of range {layer.TextureId}");
        foreach (MdxGeosetAnimation animation in model.GeosetAnimations.Values)
            if (animation.GeosetId >= model.Geosets.Count)
                warnings.Add($"warning:geoset animation index out of range {animation.GeosetId}");
        return new MdxResourceReport(model.Name, model.Version,
            model.Sequences.Select(s => s.Name).ToList(), stand, referenced.OrderBy(x => x).ToList(), missing, warnings);
    }
}
