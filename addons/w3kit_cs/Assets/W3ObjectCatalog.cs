using System;
using System.Collections.Generic;
using System.IO;
using Diablo1Like.W3.Paths;
using Diablo1Like.W3.Terrain;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Assets;

/// <summary>
/// unit/doodad/destructable fourcc → 模型路径解析（SLK + Reforged skin INI 覆盖）。
/// 迁移自 w3_object_catalog.gd；SLK 单元格解析复用 W3TerrainCore.Slk（与 Terrain/CliffTypes 同源）。
/// war3map.w3u/.w3d/.w3b 地图内覆盖表解析延后（与旧 GDS 一致，占位不做事）。
/// </summary>
[GlobalClass]
public partial class W3ObjectCatalog : RefCounted
{
    private readonly W3PathResolverBridge _paths = new();

    private readonly Dictionary<string, string> _unitModels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _doodadModels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _destructableModels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _destructableTex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitModelScale = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitMoveHeight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitCollision = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitMoveType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitHp = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitSpeed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitTurnRate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitPropWindow = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitAcquire = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitAttackRange = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitAttackCooldown = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitDamageDice = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitDamageSides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unitDamagePlus = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _doodadDefaultScale = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resolveCache = new(StringComparer.Ordinal);

    public void LoadFromRoots(GodotObject? mapArchive = null)
    {
        _unitModels.Clear();
        _unitNames.Clear();
        _doodadModels.Clear();
        _destructableModels.Clear();
        _destructableTex.Clear();
        _unitModelScale.Clear();
        _unitMoveHeight.Clear();
        _unitCollision.Clear();
        _unitMoveType.Clear();
        _unitHp.Clear();
        _unitSpeed.Clear();
        _unitTurnRate.Clear();
        _unitPropWindow.Clear();
        _unitAcquire.Clear();
        _unitAttackRange.Clear();
        _unitAttackCooldown.Clear();
        _unitDamageDice.Clear();
        _unitDamageSides.Clear();
        _unitDamagePlus.Clear();
        _doodadDefaultScale.Clear();
        _resolveCache.Clear();

        IngestSlk("Units/UnitData.slk", "unitID", "file", _unitModels);
        IngestSlk("Units/unitUI.slk", "unitUIID", "file", _unitModels);
        IngestSlk("Units/UnitUI.slk", "unitUIID", "file", _unitModels);
        IngestSlk("Units/unitUI.slk", "unitUIID", "modelScale", _unitModelScale);
        IngestSlk("Units/UnitUI.slk", "unitUIID", "modelScale", _unitModelScale);
        IngestSlk("Units/UnitData.slk", "unitID", "moveHeight", _unitMoveHeight);
        IngestSlk("Units/UnitData.slk", "unitID", "movetp", _unitMoveType);
        IngestSlk("Units/UnitData.slk", "unitID", "turnRate", _unitTurnRate);
        IngestSlk("Units/UnitData.slk", "unitID", "propWin", _unitPropWindow);
        IngestSlk("Units/UnitBalance.slk", "unitBalanceID", "collision", _unitCollision);
        IngestSlk("Units/UnitBalance.slk", "unitBalanceID", "HP", _unitHp);
        IngestSlk("Units/UnitBalance.slk", "unitBalanceID", "spd", _unitSpeed);
        IngestSlk("Units/UnitWeapons.slk", "unitWeapID", "acquire", _unitAcquire);
        IngestSlk("Units/UnitWeapons.slk", "unitWeapID", "rangeN1", _unitAttackRange);
        IngestSlk("Units/UnitWeapons.slk", "unitWeapID", "cool1", _unitAttackCooldown);
        IngestSlk("Units/UnitWeapons.slk", "unitWeapID", "dice1", _unitDamageDice);
        IngestSlk("Units/UnitWeapons.slk", "unitWeapID", "sides1", _unitDamageSides);
        IngestSlk("Units/UnitWeapons.slk", "unitWeapID", "dmgplus1", _unitDamagePlus);
        IngestSlk("Doodads/Doodads.slk", "doodID", "file", _doodadModels);
        IngestSlk("Doodads/Doodads.slk", "doodID", "defScale", _doodadDefaultScale);
        IngestSlk("Units/DestructableData.slk", "DestructableID", "file", _destructableModels);
        IngestSlk("Units/DestructableData.slk", "DestructableID", "texFile", _destructableTex);
        // HiveWE 会把这些 INI 表叠加在基础 SLK 之上：既含标准 HD 路径，也含 Reforged 换肤 rawcode。
        IngestSkinIni("Units/UnitSkin.txt", "file", _unitModels);
        IngestSkinIni("Units/UnitSkin.txt", "modelScale", _unitModelScale);
        IngestSkinIni("Units/UnitSkin.txt", "moveHeight", _unitMoveHeight);
        IngestSkinIni("Doodads/DoodadSkins.txt", "file", _doodadModels);
        IngestSkinIni("Doodads/DoodadSkins.txt", "defScale", _doodadDefaultScale);
        IngestSkinIni("Units/DestructableSkin.txt", "file", _destructableModels);
        IngestSkinIni("Units/DestructableSkin.txt", "texFile", _destructableTex);
        // war3map.w3u/.w3d/.w3b 地图内覆盖：完整解析延后，基础 SLK 已覆盖 Adrenaline 存量内容。
        GD.Print(
            $"W3ObjectCatalog: units={_unitModels.Count} names={_unitNames.Count} " +
            $"doodads={_doodadModels.Count} destructables={_destructableModels.Count}");
    }

    public void load_from_roots(GodotObject? mapArchive = null) => LoadFromRoots(mapArchive);

    /// <summary>unit fourcc 的可读名；当前无名称来源，回退为 rawcode 本身。</summary>
    public string ResolveUnitName(string rawcode) =>
        _unitNames.TryGetValue(rawcode, out string? name) && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : rawcode;

    public string resolve_unit_name(string rawcode) => ResolveUnitName(rawcode);

    /// <summary>相对模型路径（正斜杠）：先带变体后缀，再退回不带变体的裸文件。</summary>
    public string ResolveModel(string rawcode, bool isUnit, int variation = 0)
    {
        string cacheKey = $"{rawcode}|{(isUnit ? 1 : 0)}|{variation}";
        if (_resolveCache.TryGetValue(cacheKey, out string? cached))
            return cached;

        string stem = "";
        if (isUnit && _unitModels.TryGetValue(rawcode, out string? unitStem))
            stem = unitStem;
        else if (!isUnit)
        {
            if (_doodadModels.TryGetValue(rawcode, out string? doodadStem))
                stem = doodadStem;
            else if (_destructableModels.TryGetValue(rawcode, out string? destructableStem))
                stem = destructableStem;
        }
        if (string.IsNullOrEmpty(stem) && _unitModels.TryGetValue(rawcode, out string? fallbackUnitStem))
            stem = fallbackUnitStem;
        if (string.IsNullOrEmpty(stem))
            stem = Heuristic(rawcode);

        string result = string.IsNullOrEmpty(stem) ? "" : PickVariationPath(stem, variation);
        _resolveCache[cacheKey] = result;
        return result;
    }

    public string resolve_model(string rawcode, bool isUnit, int variation = 0) =>
        ResolveModel(rawcode, isUnit, variation);

    public string DestructableTexFile(string rawcode) =>
        _destructableTex.TryGetValue(rawcode, out string? tex) ? tex.Replace('\\', '/') : "";

    public string destructable_tex_file(string rawcode) => DestructableTexFile(rawcode);

    public float ModelScale(string rawcode, bool isUnit)
    {
        Dictionary<string, string> source = isUnit ? _unitModelScale : _doodadDefaultScale;
        if (!source.TryGetValue(rawcode, out string? raw))
            return 1.0f;
        float value = ParseFloat(raw, 0f);
        return value > 0.0001f ? value : 1.0f;
    }

    public float model_scale(string rawcode, bool isUnit) => ModelScale(rawcode, isUnit);

    public float UnitMoveHeight(string rawcode) =>
        _unitMoveHeight.TryGetValue(rawcode, out string? raw) ? ParseFloat(raw, 0f) : 0f;

    public float unit_move_height(string rawcode) => UnitMoveHeight(rawcode);

    /// <summary>UnitBalance.slk "collision"（CUnitType.getCollisionSize）；未知时 0。</summary>
    public float UnitCollision(string rawcode) =>
        _unitCollision.TryGetValue(rawcode, out string? raw) ? ParseFloat(raw, 0f) : 0f;

    public float unit_collision(string rawcode) => UnitCollision(rawcode);

    /// <summary>UnitData.slk "movetp"（foot/horse/fly/hover/float/amph）；未知时空串。</summary>
    public string UnitMoveType(string rawcode) =>
        _unitMoveType.TryGetValue(rawcode, out string? raw) ? raw.Trim('"') : "";

    public string unit_move_type(string rawcode) => UnitMoveType(rawcode);

    /// <summary>模拟层使用的战斗/移动数值；缺行时取文档化默认值，伤害为 WC3 骰子模型。</summary>
    public GDictionary UnitCombatStats(string rawcode) => new()
    {
        ["hp"] = StatFloat(_unitHp, rawcode, 100.0f),
        ["speed"] = StatFloat(_unitSpeed, rawcode, 270.0f),
        ["collision"] = StatFloat(_unitCollision, rawcode, 32.0f),
        ["turn_rate"] = StatFloat(_unitTurnRate, rawcode, 0.6f),
        ["prop_window_deg"] = StatFloat(_unitPropWindow, rawcode, 60.0f),
        ["acquire_range"] = StatFloat(_unitAcquire, rawcode, 500.0f),
        ["attack_range"] = StatFloat(_unitAttackRange, rawcode, 90.0f),
        ["attack_cooldown"] = StatFloat(_unitAttackCooldown, rawcode, 1.5f),
        ["damage_dice"] = (int)StatFloat(_unitDamageDice, rawcode, 1.0f),
        ["damage_sides"] = (int)StatFloat(_unitDamageSides, rawcode, 4.0f),
        ["damage_plus"] = (int)StatFloat(_unitDamagePlus, rawcode, 1.0f),
        ["move_type"] = UnitMoveType(rawcode),
    };

    public GDictionary unit_combat_stats(string rawcode) => UnitCombatStats(rawcode);

    private static float StatFloat(Dictionary<string, string> table, string rawcode, float fallback)
    {
        if (!table.TryGetValue(rawcode, out string? raw))
            return fallback;
        string text = raw.Trim('"');
        if (text.Length == 0 || text == "-")
            return fallback;
        return ParseFloat(text, fallback);
    }

    private static float ParseFloat(string text, float fallback) =>
        float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
            ? value
            : fallback;

    private static string Heuristic(string id) => id switch
    {
        "hfoo" => "Units/Human/Footman/Footman",
        "hkni" => "Units/Human/Knight/Knight",
        "hpea" => "Units/Human/Peasant/Peasant",
        "opeo" => "Units/Orc/Peon/Peon",
        _ => "",
    };

    private string PickVariationPath(string stem, int variation)
    {
        string p = stem.Replace('\\', '/');
        string lower = p.ToLowerInvariant();
        if (lower.EndsWith(".mdx", StringComparison.Ordinal) || lower.EndsWith(".mdl", StringComparison.Ordinal))
            p = GetBasename(p);
        string[] candidates =
        {
            $"{p}{variation}.mdx",
            $"{p}.mdx",
            $"{p}{variation}.mdl",
            $"{p}.mdl",
        };
        // 每个 Reforged 候选都要先于任何经典候选检查，否则经典编号变体
        // （Model0.mdx）会盖过 Reforged 的无编号替换（Model.mdx）。
        foreach (string c in candidates)
        {
            if (!string.IsNullOrEmpty(_paths.ResolveReforgedPath(c)))
                return c;
        }
        foreach (string c in candidates)
        {
            if (!string.IsNullOrEmpty(_paths.ResolvePath(c)))
                return c;
        }
        // 树木/可破坏物优先带编号变体，即使磁盘上还不存在。
        return variation >= 0 ? $"{p}{variation}.mdx" : $"{p}.mdx";
    }

    private void IngestSlk(string rel, string idCol, string fileCol, Dictionary<string, string> dest)
    {
        string path = _paths.HasRoot() ? _paths.ResolvePath(rel) : "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        ParseSlk(File.ReadAllText(path), idCol, fileCol, dest);
    }

    private void IngestSkinIni(string rel, string fieldName, Dictionary<string, string> dest)
    {
        string path = _paths.ResolveReforgedPath(rel);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        string section = "";
        string baseValue = "";
        string hdValue = "";
        string wanted = fieldName.ToLowerInvariant();
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                CommitSkinIni(section, !string.IsNullOrEmpty(hdValue) ? hdValue : baseValue, dest);
                section = line[1..^1];
                baseValue = "";
                hdValue = "";
                continue;
            }
            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;
            string key = line[..equals].Trim().ToLowerInvariant();
            string value = line[(equals + 1)..].Trim();
            if (key == wanted)
                baseValue = value;
            else if (key == wanted + ":hd")
                hdValue = value;
        }
        CommitSkinIni(section, !string.IsNullOrEmpty(hdValue) ? hdValue : baseValue, dest);
    }

    private static void CommitSkinIni(string section, string value, Dictionary<string, string> dest)
    {
        if (section.Length == 4 && !string.IsNullOrEmpty(value) && value != "-")
            dest[section] = value;
    }

    private static void ParseSlk(string text, string idCol, string fileCol, Dictionary<string, string> dest)
    {
        int idX = -1, fileX = -1, curY = -1;
        string rowId = "";
        string rowFile = "";
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("ID;", StringComparison.Ordinal) || line == "E")
                continue;
            if (!line.StartsWith("C;", StringComparison.Ordinal))
                continue;
            int y = Slk.Int(line, "Y");
            int x = Slk.Int(line, "X");
            string k = Slk.Str(line, "K");
            if (y > 0)
            {
                if (y != curY && curY > 1 && rowId.Length > 0 && rowFile.Length > 0)
                    dest[rowId] = rowFile;
                if (y != curY)
                {
                    curY = y;
                    rowId = "";
                    rowFile = "";
                }
            }
            if (curY == 1 && x > 0 && k.Length > 0)
            {
                string hname = k.Trim('"');
                if (hname == idCol) idX = x;
                else if (hname == fileCol) fileX = x;
            }
            else if (curY > 1 && x > 0)
            {
                string val = k.Trim('"');
                if (x == idX) rowId = val;
                else if (x == fileX) rowFile = val;
            }
        }
        if (curY > 1 && rowId.Length > 0 && rowFile.Length > 0)
            dest[rowId] = rowFile;
    }

    /// <summary>Godot get_basename() 等价：去掉最后一个扩展名，保留目录部分。</summary>
    private static string GetBasename(string path)
    {
        int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        int dot = path.LastIndexOf('.');
        return dot > slash ? path[..dot] : path;
    }
}
