using System.Globalization;
using W3Editor.Rendering;
using W3Editor.Contracts;

namespace W3Editor.Services;

/// <summary>One placeable doodad/destructible type (Doodads.slk / DestructableData.slk row).</summary>
public sealed class DoodadTypeInfo
{
    public string Id = "";
    public char Category;
    /// <summary>Tileset letters this type belongs to ("*" = all).</summary>
    public string Tilesets = "*";
    /// <summary>Model path stem (may include .mdl extension in SLK; no variation suffix).</summary>
    public string ModelStem = "";
    public string Name = "";
    public int NumVar = 1;
    public float DefScale = 1f;
    public float MinScale = 1f;
    public float MaxScale = 1f;
    public bool RandomScale;
    /// <summary>-1 = free rotation allowed; >=0 = fixed editor rotation (degrees).</summary>
    public float FixedRot = -1f;
    public bool IsDestructible;
    /// <summary>DestructableData texFile — replaceable texture stem for trees.</summary>
    public string TexFile = "";

    public bool InTileset(char tileset) =>
        Tilesets.Contains('*') || Tilesets.Contains(char.ToUpperInvariant(tileset)) || Tilesets.Contains(char.ToLowerInvariant(tileset));
}

/// <summary>One placeable unit type (UnitData.slk + unitUI.slk row).</summary>
public sealed class UnitTypeInfo
{
    public string Id = "";
    public string ModelStem = "";
    public string Name = "";
    public float ModelScale = 1f;
    /// <summary>UnitData.slk race column (human/orc/nightelf/undead/creeps/naga/demon/other...).</summary>
    public string Race = "";
    /// <summary>Icon path (Art field from Units/*UnitFunc.txt), classic rel path.</summary>
    public string IconPath = "";
    /// <summary>unitUI.slk hiddenInEditor — filtered out of the palette.</summary>
    public bool HiddenInEditor;
}

/// <summary>
/// Editor-side port of addons/w3kit/w3_object_catalog.gd: fourcc → model stem
/// via the stock SLKs plus the Reforged skin INIs, with the same
/// stem+variation → .mdx candidate rules (_pick_variation_path).
/// </summary>
public sealed class ObjectCatalog : IObjectCatalog, IWarcraftObjectDatabase
{
    /// <summary>WE palette category letters (Doodads.slk category / DestructableData.slk category).</summary>
    public static readonly (char Letter, string Name)[] DoodadCategories =
    {
        ('D', "树木/可破坏物"),
        ('B', "桥/坡道"),
        ('P', "路径阻断器"),
        ('E', "环境"),
        ('O', "障碍物"),
        ('S', "建筑"),
        ('W', "水域"),
        ('C', "电影艺术"),
        ('Z', "悬崖/地形"),
    };

    public List<DoodadTypeInfo> Doodads { get; } = new();
    public List<UnitTypeInfo> Units { get; } = new();

    IReadOnlyList<DoodadTypeInfo> IObjectCatalog.Doodads => Doodads;
    IReadOnlyList<UnitTypeInfo> IObjectCatalog.Units => Units;

    private readonly Dictionary<string, DoodadTypeInfo> _doodadById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnitTypeInfo> _unitById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resolveCache = new(StringComparer.Ordinal);
    private readonly EditorAssets _assets;
    private readonly Dictionary<WarcraftObjectKind, IReadOnlyList<WarcraftObjectDefinition>> _editorObjects = new();
    private readonly Dictionary<WarcraftObjectKind, IReadOnlyList<WarcraftFieldDefinition>> _editorFields = new();

    public ObjectCatalog(EditorAssets assets)
    {
        _assets = assets;
        LoadDoodadSlk("Doodads/Doodads.slk", "doodID", isDestructible: false);
        LoadDoodadSlk("Units/DestructableData.slk", "DestructableID", isDestructible: true);
        LoadUnitSlk();
        // Reforged HD overrides (HiveWE merges these INI tables over the SLKs).
        ApplySkinIni("Doodads/DoodadSkins.txt", (id, fields) => Override(_doodadById, id, fields));
        ApplySkinIni("Units/DestructableSkin.txt", (id, fields) => Override(_doodadById, id, fields));
        ApplySkinIni("Units/UnitSkin.txt", (id, fields) =>
        {
            if (!_unitById.TryGetValue(id, out var unit)) return;
            if (fields.TryGetValue("file", out string? file)) unit.ModelStem = file;
            if (fields.TryGetValue("modelscale", out string? s) && ParseF(s) is { } v && v > 0.0001f) unit.ModelScale = v;
        });
        BuildEditorDatabase();
    }

    public IReadOnlyList<WarcraftObjectDefinition> Objects(WarcraftObjectKind kind) =>
        _editorObjects.GetValueOrDefault(kind) ?? Array.Empty<WarcraftObjectDefinition>();

    public IReadOnlyList<WarcraftFieldDefinition> Fields(WarcraftObjectKind kind) =>
        _editorFields.GetValueOrDefault(kind) ?? Array.Empty<WarcraftFieldDefinition>();

    private void BuildEditorDatabase()
    {
        _editorObjects[WarcraftObjectKind.Units] = Units.Select(unit =>
            new WarcraftObjectDefinition(unit.Id, unit.Name, unit.Race)).ToArray();
        _editorObjects[WarcraftObjectKind.Doodads] = Doodads.Where(item => !item.IsDestructible).Select(item =>
            new WarcraftObjectDefinition(item.Id, item.Name, item.Category.ToString())).ToArray();
        _editorObjects[WarcraftObjectKind.Destructibles] = Doodads.Where(item => item.IsDestructible).Select(item =>
            new WarcraftObjectDefinition(item.Id, item.Name, item.Category.ToString())).ToArray();

        LoadEditorObjects(WarcraftObjectKind.Items, "Units/ItemData.slk", "itemID", "comment", "class");
        LoadEditorObjects(WarcraftObjectKind.Abilities, "Units/AbilityData.slk", "alias", "comments", "race");
        LoadEditorObjects(WarcraftObjectKind.Upgrades, "Units/UpgradeData.slk", "upgradeid", "comments", "race");
        LoadEditorObjects(WarcraftObjectKind.Buffs, "Units/AbilityBuffData.slk", "alias", "comments", "race");

        LoadEditorFields(WarcraftObjectKind.Units, "Units/UnitMetaData.slk");
        LoadEditorFields(WarcraftObjectKind.Items, "Units/ItemMetaData.slk");
        LoadEditorFields(WarcraftObjectKind.Abilities, "Units/AbilityMetaData.slk");
        LoadEditorFields(WarcraftObjectKind.Destructibles, "Units/DestructableMetaData.slk");
        LoadEditorFields(WarcraftObjectKind.Doodads, "Doodads/DoodadMetaData.slk");
        LoadEditorFields(WarcraftObjectKind.Upgrades, "Units/UpgradeMetaData.slk");
        LoadEditorFields(WarcraftObjectKind.Buffs, "Units/AbilityBuffMetaData.slk");
    }

    private void LoadEditorObjects(WarcraftObjectKind kind, string path, string idColumn, string nameColumn, string groupColumn)
    {
        string? resolved = _assets.ResolveDataFile(path);
        if (resolved == null) return;
        _editorObjects[kind] = SlkRows(File.ReadAllText(resolved), idColumn)
            .Select(row => new WarcraftObjectDefinition(
                row.GetValueOrDefault(idColumn, ""),
                row.GetValueOrDefault(nameColumn, row.GetValueOrDefault(idColumn, "")),
                row.GetValueOrDefault(groupColumn, "未分类")))
            .Where(item => item.Id.Length == 4).ToArray();
    }

    private void LoadEditorFields(WarcraftObjectKind kind, string path)
    {
        string? resolved = _assets.ResolveDataFile(path);
        if (resolved == null) return;
        _editorFields[kind] = SlkRows(File.ReadAllText(resolved), "ID")
            .Select(row => new WarcraftFieldDefinition(
                row.GetValueOrDefault("ID", ""), row.GetValueOrDefault("field", ""),
                row.GetValueOrDefault("displayName", row.GetValueOrDefault("field", "")),
                row.GetValueOrDefault("type", "string"), row.GetValueOrDefault("category", ""),
                row.GetValueOrDefault("repeat", "0")))
            .Where(field => field.Id.Length is >= 1 and <= 4)
            .OrderBy(field => field.Category).ThenBy(field => field.DisplayName).ToArray();
    }

    public DoodadTypeInfo? FindDoodad(string id) => _doodadById.GetValueOrDefault(id);
    public UnitTypeInfo? FindUnit(string id) => _unitById.GetValueOrDefault(id);

    /// <summary>Model relative path for a placement (skin id preferred), or "" when unknown.</summary>
    public string ResolveModelRel(string rawcode, bool isUnit, int variation)
    {
        string key = $"{rawcode}|{(isUnit ? 1 : 0)}|{variation}";
        if (_resolveCache.TryGetValue(key, out string? cached))
            return cached;
        string stem = "";
        if (isUnit && _unitById.TryGetValue(rawcode, out var unit))
            stem = unit.ModelStem;
        else if (!isUnit && _doodadById.TryGetValue(rawcode, out var doodad))
            stem = doodad.ModelStem;
        if (stem.Length == 0 && _unitById.TryGetValue(rawcode, out var fallback))
            stem = fallback.ModelStem;
        string result = stem.Length == 0 ? "" : PickVariationPath(stem, variation);
        _resolveCache[key] = result;
        return result;
    }

    public float ModelScale(string rawcode, bool isUnit)
    {
        if (isUnit)
            return _unitById.TryGetValue(rawcode, out var unit) && unit.ModelScale > 0.0001f ? unit.ModelScale : 1f;
        return _doodadById.TryGetValue(rawcode, out var doodad) && doodad.DefScale > 0.0001f ? doodad.DefScale : 1f;
    }

    public string DestructibleTexFile(string rawcode) =>
        _doodadById.TryGetValue(rawcode, out var d) ? d.TexFile : "";

    /// <summary>w3_object_catalog._pick_variation_path: {stem}{var}.mdx → {stem}.mdx → .mdl variants.</summary>
    private string PickVariationPath(string stem, int variation)
    {
        string p = stem.Replace('\\', '/');
        if (p.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            p = p[..^4];
        string[] candidates =
        {
            $"{p}{variation}.mdx",
            $"{p}.mdx",
            $"{p}{variation}.mdl",
            $"{p}.mdl",
        };
        foreach (string candidate in candidates)
            if (_assets.ResolveModel(candidate) != null)
                return candidate;
        return variation >= 0 ? $"{p}{variation}.mdx" : $"{p}.mdx";
    }

    // ------------------------------------------------------------------
    // SLK / INI parsing
    // ------------------------------------------------------------------

    private void LoadDoodadSlk(string rel, string idColumn, bool isDestructible)
    {
        string? path = _assets.ResolveDataFile(rel);
        if (path == null) return;
        foreach (var row in SlkRows(File.ReadAllText(path), idColumn))
        {
            string id = row.GetValueOrDefault(idColumn, "");
            if (id.Length != 4) continue;
            var info = new DoodadTypeInfo
            {
                Id = id,
                Category = row.GetValueOrDefault("category", "E") is { Length: > 0 } cat ? char.ToUpperInvariant(cat[0]) : 'E',
                Tilesets = row.GetValueOrDefault("tilesets", "*"),
                ModelStem = row.GetValueOrDefault("file", ""),
                Name = row.GetValueOrDefault("comment", id),
                NumVar = (int)(ParseF(row.GetValueOrDefault("numVar", "1")) ?? 1f),
                DefScale = ParseF(row.GetValueOrDefault("defScale", "1")) ?? 1f,
                MinScale = ParseF(row.GetValueOrDefault("minScale", "1")) ?? 1f,
                MaxScale = ParseF(row.GetValueOrDefault("maxScale", "1")) ?? 1f,
                RandomScale = row.GetValueOrDefault("canPlaceRandScale", "0") == "1",
                FixedRot = ParseF(row.GetValueOrDefault("fixedRot", "-1")) ?? -1f,
                IsDestructible = isDestructible,
                TexFile = row.GetValueOrDefault("texFile", ""),
            };
            if (info.ModelStem.Length == 0) continue;
            if (info.NumVar < 1) info.NumVar = 1;
            if (isDestructible && info.Category is not ('D' or 'B' or 'P'))
                info.Category = 'D';
            Doodads.Add(info);
            _doodadById[id] = info;
        }
    }

    private void LoadUnitSlk()
    {
        string? dataPath = _assets.ResolveDataFile("Units/UnitData.slk");
        if (dataPath == null) return;
        foreach (var row in SlkRows(File.ReadAllText(dataPath), "unitID"))
        {
            string id = row.GetValueOrDefault("unitID", "");
            if (id.Length != 4) continue;
            var info = new UnitTypeInfo
            {
                Id = id,
                Name = id,
                Race = row.GetValueOrDefault("race", "").Trim().Trim('"').ToLowerInvariant(),
            };
            Units.Add(info);
            _unitById[id] = info;
        }
        string? uiPath = _assets.ResolveDataFile("Units/unitUI.slk") ?? _assets.ResolveDataFile("Units/UnitUI.slk");
        if (uiPath != null)
        {
            foreach (var row in SlkRows(File.ReadAllText(uiPath), "unitUIID"))
            {
                string id = row.GetValueOrDefault("unitUIID", "");
                if (!_unitById.TryGetValue(id, out var info)) continue;
                if (row.GetValueOrDefault("file", "") is { Length: > 0 } file) info.ModelStem = file;
                if (row.GetValueOrDefault("name", "") is { Length: > 0 } name) info.Name = name;
                if (ParseF(row.GetValueOrDefault("modelScale", "")) is { } scale && scale > 0.0001f) info.ModelScale = scale;
                if (row.GetValueOrDefault("hiddenInEditor", "0") == "1") info.HiddenInEditor = true;
            }
        }
        // Icon paths (WE palette thumbnails): Art field in the race Func INIs.
        foreach (string rel in new[]
        {
            "Units/HumanUnitFunc.txt", "Units/OrcUnitFunc.txt", "Units/NightElfUnitFunc.txt",
            "Units/UndeadUnitFunc.txt", "Units/NeutralUnitFunc.txt", "Units/CampaignUnitFunc.txt",
        })
        {
            ApplySkinIni(rel, (id, fields) =>
            {
                if (_unitById.TryGetValue(id, out var info)
                    && fields.TryGetValue("Art", out string? art) && art.Length > 0)
                    info.IconPath = art.Split(',')[0].Trim();
            });
        }
        // Display names (WE palette labels): Name field in the race Strings INIs.
        foreach (string rel in new[]
        {
            "Units/HumanUnitStrings.txt", "Units/OrcUnitStrings.txt", "Units/NightElfUnitStrings.txt",
            "Units/UndeadUnitStrings.txt", "Units/NeutralUnitStrings.txt", "Units/CampaignUnitStrings.txt",
        })
        {
            ApplySkinIni(rel, (id, fields) =>
            {
                if (_unitById.TryGetValue(id, out var info)
                    && fields.TryGetValue("Name", out string? name) && name.Length > 0)
                    info.Name = name;
            });
        }
        Units.RemoveAll(u => u.ModelStem.Length == 0 || u.HiddenInEditor);
    }

    private void ApplySkinIni(string rel, Action<string, Dictionary<string, string>> apply)
    {
        // Reforged skin INIs live only in the reforged cache.
        string? path = _assets.ResolveDataFile(rel);
        if (path == null) return;
        string section = "";
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Commit()
        {
            if (section.Length == 4 && fields.Count > 0)
                apply(section, NormalizeHd(fields));
            fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith(";")) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Commit();
                section = line[1..^1];
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            fields[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        Commit();
    }

    /// <summary>Prefers `key:hd` over `key`, drops "-" placeholders (w3_object_catalog._ingest_skin_ini).</summary>
    private static Dictionary<string, string> NormalizeHd(Dictionary<string, string> raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in raw)
        {
            if (key.EndsWith(":hd", StringComparison.OrdinalIgnoreCase))
                result[key[..^3]] = value;
            else if (!result.ContainsKey(key))
                result[key] = value;
        }
        foreach (string key in result.Keys.ToList())
            if (result[key] == "-" || result[key].Length == 0)
                result.Remove(key);
        return result;
    }

    private static void Override(Dictionary<string, DoodadTypeInfo> table, string id, Dictionary<string, string> fields)
    {
        if (!table.TryGetValue(id, out var info)) return;
        if (fields.TryGetValue("file", out string? file)) info.ModelStem = file;
        if (fields.TryGetValue("defScale", out string? s) && ParseF(s) is { } v && v > 0.0001f) info.DefScale = v;
        if (fields.TryGetValue("texFile", out string? tex)) info.TexFile = tex;
        if (fields.TryGetValue("numVar", out string? nv) && ParseF(nv) is { } n && n >= 1f) info.NumVar = (int)n;
    }

    private static float? ParseF(string text)
    {
        text = text.Trim().Trim('"');
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : null;
    }

    /// <summary>Streams SLK rows as header-name → cell-value dictionaries (row 1 = header).</summary>
    private static IEnumerable<Dictionary<string, string>> SlkRows(string slkText, string idColumn)
    {
        var headers = new Dictionary<int, string>();
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int currentY = -1;
        foreach (string rawLine in slkText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("C;", StringComparison.Ordinal)) continue;
            int y = Diablo1Like.W3.Terrain.Slk.Int(line, "Y");
            int x = Diablo1Like.W3.Terrain.Slk.Int(line, "X");
            string value = Diablo1Like.W3.Terrain.Slk.Str(line, "K").Trim().Trim('"');
            if (y > 0 && y != currentY)
            {
                if (currentY > 1 && row.ContainsKey(idColumn))
                    yield return row;
                currentY = y;
                row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            if (x <= 0) continue;
            if (currentY == 1)
                headers[x] = value;
            else if (currentY > 1 && headers.TryGetValue(x, out string? name))
                row[name] = value;
        }
        if (currentY > 1 && row.ContainsKey(idColumn))
            yield return row;
    }
}
