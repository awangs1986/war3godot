using W3Editor.Contracts;

namespace W3Editor.Modules.Objects;

internal sealed record BaseObjectInfo(string Id, string Name, string Group);
internal sealed record FieldMetadata(string Id, string Field, string DisplayName, string Type, string Category, string Repeat);

/// <summary>Thin module adapter over the shell's single shared SLK cache.</summary>
internal sealed class ObjectMetadataCatalog
{
    private readonly IWarcraftObjectDatabase? _database;
    public ObjectMetadataCatalog(IWarcraftObjectDatabase? database) => _database = database;

    public IReadOnlyList<BaseObjectInfo> Objects(ObjectKind kind) => _database?
        .Objects((WarcraftObjectKind)kind)
        .Select(item => new BaseObjectInfo(item.Id, item.Name, item.Group)).ToArray()
        ?? Array.Empty<BaseObjectInfo>();

    public IReadOnlyList<FieldMetadata> Fields(ObjectKind kind) => _database?
        .Fields((WarcraftObjectKind)kind)
        .Select(item => new FieldMetadata(item.Id, item.Field, item.DisplayName, item.Type, item.Category, item.Repeat)).ToArray()
        ?? Array.Empty<FieldMetadata>();

    public FieldMetadata? Field(ObjectKind kind, string id) => Fields(kind)
        .FirstOrDefault(field => string.Equals(field.Id, id.Replace("·", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase));
}
