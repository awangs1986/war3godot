using System.Text.Json;

namespace W3Editor.Services;

/// <summary>
/// Native object catalog: a single catalog.json instead of War3 SLK tables.
///
/// Schema (v1):
/// {
///   "format": "d1catalog", "version": 1,
///   "doodads": [{ "id": "tree_01", "name": "树", "category": "D",
///                 "models": ["models/tree_01a.glb", "models/tree_01b.glb"],
///                 "defScale": 1.0, "minScale": 0.8, "maxScale": 1.2,
///                 "randomScale": true, "fixedRot": -1, "texFile": "" }],
///   "units":   [{ "id": "grunt", "name": "兵", "model": "models/grunt.glb",
///                 "scale": 1.0, "race": "monster", "icon": "icons/grunt.png" }]
/// }
///
/// "models" length doubles as the variation count; variation indexes into it.
/// </summary>
public sealed class JsonObjectCatalog : IObjectCatalog
{
    private readonly List<DoodadTypeInfo> _doodads = new();
    private readonly List<UnitTypeInfo> _units = new();
    private readonly Dictionary<string, DoodadTypeInfo> _doodadById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnitTypeInfo> _unitById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _doodadModels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitModels = new(StringComparer.Ordinal);

    public IReadOnlyList<DoodadTypeInfo> Doodads => _doodads;
    public IReadOnlyList<UnitTypeInfo> Units => _units;

    public JsonObjectCatalog(string jsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
        var root = doc.RootElement;
        if (root.GetProperty("format").GetString() != "d1catalog")
            throw new InvalidDataException($"{jsonPath} is not a d1catalog file");

        if (root.TryGetProperty("doodads", out var doodads))
        {
            foreach (var e in doodads.EnumerateArray())
            {
                string id = e.GetProperty("id").GetString() ?? "";
                if (id.Length == 0) continue;
                string[] models = e.TryGetProperty("models", out var m)
                    ? m.EnumerateArray().Select(v => v.GetString() ?? "").ToArray()
                    : Array.Empty<string>();
                var info = new DoodadTypeInfo
                {
                    Id = id,
                    Name = GetString(e, "name", id),
                    Category = GetString(e, "category", "E") is { Length: > 0 } c ? char.ToUpperInvariant(c[0]) : 'E',
                    ModelStem = models.Length > 0 ? models[0] : "",
                    NumVar = Math.Max(1, models.Length),
                    DefScale = GetFloat(e, "defScale", 1f),
                    MinScale = GetFloat(e, "minScale", 1f),
                    MaxScale = GetFloat(e, "maxScale", 1f),
                    RandomScale = e.TryGetProperty("randomScale", out var rs) && rs.GetBoolean(),
                    FixedRot = GetFloat(e, "fixedRot", -1f),
                    TexFile = GetString(e, "texFile", ""),
                };
                _doodads.Add(info);
                _doodadById[id] = info;
                _doodadModels[id] = models;
            }
        }

        if (root.TryGetProperty("units", out var units))
        {
            foreach (var e in units.EnumerateArray())
            {
                string id = e.GetProperty("id").GetString() ?? "";
                if (id.Length == 0) continue;
                string model = GetString(e, "model", "");
                var info = new UnitTypeInfo
                {
                    Id = id,
                    Name = GetString(e, "name", id),
                    ModelStem = model,
                    ModelScale = GetFloat(e, "scale", 1f),
                    Race = GetString(e, "race", ""),
                    IconPath = GetString(e, "icon", ""),
                };
                _units.Add(info);
                _unitById[id] = info;
                _unitModels[id] = model;
            }
        }
    }

    public DoodadTypeInfo? FindDoodad(string id) => _doodadById.GetValueOrDefault(id);
    public UnitTypeInfo? FindUnit(string id) => _unitById.GetValueOrDefault(id);

    public string ResolveModelRel(string id, bool isUnit, int variation)
    {
        if (isUnit)
            return _unitModels.GetValueOrDefault(id, "");
        string[] models = _doodadModels.GetValueOrDefault(id) ?? Array.Empty<string>();
        if (models.Length == 0)
            return "";
        return models[Math.Clamp(variation, 0, models.Length - 1)];
    }

    public float ModelScale(string id, bool isUnit)
    {
        if (isUnit)
            return _unitById.TryGetValue(id, out var u) && u.ModelScale > 0.0001f ? u.ModelScale : 1f;
        return _doodadById.TryGetValue(id, out var d) && d.DefScale > 0.0001f ? d.DefScale : 1f;
    }

    public string DestructibleTexFile(string id) =>
        _doodadById.TryGetValue(id, out var d) ? d.TexFile : "";

    private static string GetString(JsonElement e, string name, string fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static float GetFloat(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
}
