using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Diablo1Like.W3.Mdx;
using GDictionary = Godot.Collections.Dictionary;

namespace Diablo1Like.W3.Terrain;

/// <summary>
/// C# cliff scheduler/renderer. Placement and ground ownership are produced by
/// the Warsmash port; GDScript is used only as the existing MDX/BLP asset codec.
/// </summary>
[GlobalClass]
public partial class WarsmashCliffRenderer : Node3D
{
    private const int BatchTileSize = 32;

    private sealed record CliffType(string CliffModelDir, string RampModelDir, string TextureDir, string TextureFile);

    private sealed class CliffBatchBuilder
    {
        public readonly SurfaceTool Surface = new();
        public int InstanceCount;

        public CliffBatchBuilder() => Surface.Begin(Mesh.PrimitiveType.Triangles);
    }

    private readonly Dictionary<string, CliffType> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Variant> _modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CliffBatchBuilder> _batchBuilders = new(StringComparer.OrdinalIgnoreCase);
    private W3TerrainMap? _map;
    private GodotObject? _mapResource;
    /// <summary>
    /// Asset path resolver (W3MapArchive / W3LevelData). Folder/w3x loads pass a
    /// bare W3EMap as mapResource, which has no resolve_path — the archive must
    /// be injected separately. Deliberately survives ClearRenderer (set once per load).
    /// </summary>
    private GodotObject? _pathResolver;
    private GodotObject? _textures;
    private Texture2D? _shadowTexture;
    private bool _shadowEnabled;
    private int _spawned;
    private int _missing;
    private int _batchCount;

    public int SpawnedCount => _spawned;
    public int MissingCount => _missing;

    public void Build(GodotObject mapResource, GodotObject textureProvider, byte[] shadowBytes)
    {
        ClearRenderer();
        _mapResource = mapResource;
        _textures = textureProvider;
        _map = W3TerrainMapBridge.FromGodotResource(mapResource);
        LoadCliffTypes();
        BuildShadowTexture(shadowBytes);
        var rules = new WarsmashTerrainRules(
            _map,
            rel => !string.IsNullOrEmpty(ResolvePath(rel)),
            texture => TypeFor(texture).CliffModelDir,
            texture => TypeFor(texture).RampModelDir,
            // 与 GDS w3_cliff_builder._cliff_variation 一致：modulo，而非 clamp
            (tag, authored) =>
            {
                int maxVariation = WarsmashTerrainRules.CliffVariationMaximum.GetValueOrDefault(tag, 0);
                if (maxVariation <= 0)
                    return 0;
                int v = authored % (maxVariation + 1);
                return v < 0 ? v + maxVariation + 1 : v;
            });
        IReadOnlyList<WarsmashTerrainRules.CliffPlacement> placements = rules.RebuildCliffs();
        foreach (WarsmashTerrainRules.CliffPlacement placement in placements)
            Spawn(placement);
        CommitBatches();
        GD.Print($"WarsmashCliffRenderer(C#): spawned={_spawned}, missing={_missing}, batches={_batchCount}");
    }

    public void build(GodotObject mapResource, GodotObject textureProvider, byte[] shadowBytes) => Build(mapResource, textureProvider, shadowBytes);

    public GDictionary GetCounts() => new()
    {
        ["ok"] = _spawned,
        ["miss"] = _missing,
        ["batches"] = _batchCount,
    };

    public GDictionary get_counts() => GetCounts();

    public void ApplyLighting(Vector3 direction, float ambient = 0.35f, float scale = 1.1f)
    {
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector3(0.45f, 0.85f, 0.28f);
        foreach (Node node in FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D { MaterialOverride: ShaderMaterial material })
            {
                material.SetShaderParameter("light_dir", direction.Normalized());
                material.SetShaderParameter("light_ambient", ambient);
                material.SetShaderParameter("light_scale", scale);
            }
        }
    }

    public void apply_lighting(Vector3 direction, float ambient = 0.35f, float scale = 1.1f) => ApplyLighting(direction, ambient, scale);

    public void SetFlatInspect(bool enabled)
    {
        foreach (Node node in FindChildren("*", "MeshInstance3D", true, false))
            if (node is MeshInstance3D { MaterialOverride: ShaderMaterial material })
                material.SetShaderParameter("flat_lighting", enabled);
    }

    public void set_flat_inspect(bool enabled) => SetFlatInspect(enabled);

    private void Spawn(WarsmashTerrainRules.CliffPlacement placement)
    {
        string absolutePath = ResolvePath(placement.ModelPath);
        if (string.IsNullOrEmpty(absolutePath))
        {
            _missing++;
            return;
        }
        if (!_modelCache.TryGetValue(absolutePath, out Variant model))
        {
            W3MdxModel? loaded = W3MdxModel.LoadFile(absolutePath);
            model = loaded is null ? new Variant() : Variant.From(loaded);
            _modelCache[absolutePath] = model;
        }
        if (model.VariantType == Variant.Type.Nil)
        {
            _missing++;
            return;
        }

        var node = new Node3D
        {
            Name = $"Cliff_{placement.X}_{placement.Y}_{placement.Tag}",
            Position = new Vector3(
                _map!.CenterOffsetX + placement.X * W3TerrainMap.TileSize,
                (placement.BaseLayer - 2.0f) * W3TerrainMap.TileSize,
                _map.CenterOffsetY + placement.Y * W3TerrainMap.TileSize),
        };
        node.SetMeta("w3_model_rel", placement.ModelPath);
        node.SetMeta("w3_source_tile", new Vector2I(placement.SourceX ?? placement.X, placement.SourceY ?? placement.Y));
        node.SetMeta("w3_placement_tile", new Vector2I(placement.X, placement.Y));
        AddChild(node);

        // 悬崖实例直接构造 C# W3MdxInstance（GDS→C# 迁移后，w3_mdx_instance.gd 已删除）。
        Node3D instance = new W3MdxInstance();
        node.AddChild(instance);
        CliffType type = TypeFor(placement.CliffTexture);
        // HiveWE/Warsmash HD terrain resolves cliff replaceables as DDS. The
        // tileset-prefixed BLP spelling is retained only for classic exports.
        string texturePath = $"{type.TextureDir}/{type.TextureFile}.dds";
        if (string.IsNullOrEmpty(ResolvePath(texturePath)))
        {
            texturePath = $"{type.TextureDir}/{_map.Tileset}_{type.TextureFile}.blp";
            if (string.IsNullOrEmpty(ResolvePath(texturePath)))
                texturePath = $"{type.TextureDir}/{type.TextureFile}.blp";
        }
        node.SetMeta("w3_texture_rel", texturePath);
        float[] groundGrid = new float[9];
        for (int gy = 0; gy < 3; gy++)
        for (int gx = 0; gx < 3; gx++)
        {
            int cx = Math.Clamp(placement.X + gx, 0, _map.Width - 1);
            int cy = Math.Clamp(placement.Y + gy, 0, _map.Height - 1);
            groundGrid[gy * 3 + gx] = _map.GetCorner(cx, cy).GroundHeight;
        }
        W3TerrainMap.Corner p00 = _map.GetCorner(placement.X, placement.Y);
        W3TerrainMap.Corner p10 = _map.GetCorner(placement.X + 1, placement.Y);
        W3TerrainMap.Corner p01 = _map.GetCorner(placement.X, placement.Y + 1);
        W3TerrainMap.Corner p11 = _map.GetCorner(placement.X + 1, placement.Y + 1);
        var options = new GDictionary
        {
            ["cliff_texture"] = texturePath,
            ["cliff_remap"] = true,
            ["cliff_ground"] = new Vector4(p00.GroundHeight, p10.GroundHeight, p01.GroundHeight, p11.GroundHeight),
            ["cliff_ground_grid"] = groundGrid,
            ["cliff_ground_grid_size"] = new Vector2I(3, 3),
        };
        instance.Call("setup", model, _textures!, absolutePath, options);
        ConfigureMaterials(instance);
        AppendToBatch(placement, texturePath, node, instance);
        _spawned++;
    }

    private void AppendToBatch(
        WarsmashTerrainRules.CliffPlacement placement,
        string texturePath,
        Node3D placementNode,
        Node3D instance)
    {
        int chunkX = placement.X / BatchTileSize;
        int chunkY = placement.Y / BatchTileSize;
        string key = $"{chunkX}:{chunkY}:{texturePath}";
        if (!_batchBuilders.TryGetValue(key, out CliffBatchBuilder? builder))
        {
            builder = new CliffBatchBuilder();
            _batchBuilders[key] = builder;
        }

        Transform3D rendererInverse = GlobalTransform.AffineInverse();
        foreach (Node child in instance.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (child is not MeshInstance3D { Mesh: not null } source)
                continue;
            Transform3D toRenderer = rendererInverse * source.GlobalTransform;
            for (int surface = 0; surface < source.Mesh.GetSurfaceCount(); surface++)
                builder.Surface.AppendFrom(source.Mesh, surface, toRenderer);
            // 不把多材质压成单一 MaterialOverride；由 ArrayMesh surface 材质保留（对齐 GDS）
        }
        builder.InstanceCount++;
        placementNode.RemoveChild(instance);
        instance.QueueFree();
    }

    private void CommitBatches()
    {
        int index = 0;
        foreach (KeyValuePair<string, CliffBatchBuilder> pair in _batchBuilders)
        {
            ArrayMesh mesh = pair.Value.Surface.Commit();
            if (mesh == null || mesh.GetSurfaceCount() == 0)
                continue;
            var batch = new MeshInstance3D
            {
                Name = $"CliffBatch_{index++}",
                Mesh = mesh,
                // 保留各 surface 材质，避免多 geoset 悬崖被单一材质覆盖
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            batch.SetMeta("w3_cliff_batch", true);
            batch.SetMeta("w3_batch_key", pair.Key);
            batch.SetMeta("w3_batch_instances", pair.Value.InstanceCount);
            string[] keyParts = pair.Key.Split(':', 3);
            batch.SetMeta("w3_texture_rel", keyParts.Length == 3 ? keyParts[2] : string.Empty);
            AddChild(batch);
            _batchCount++;
        }
        _batchBuilders.Clear();
    }

    private void ConfigureMaterials(Node root)
    {
        foreach (Node child in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (child is not MeshInstance3D mesh)
                continue;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (mesh.MaterialOverride is not ShaderMaterial material)
                continue;
            material.SetShaderParameter("shd_enabled", _shadowEnabled);
            if (_shadowTexture != null)
                material.SetShaderParameter("shd_tex", _shadowTexture);
            material.SetShaderParameter("map_origin", new Vector2(_map!.CenterOffsetX, _map.CenterOffsetY));
            material.SetShaderParameter("map_size", new Vector2(_map.TileWidth * W3TerrainMap.TileSize, _map.TileHeight * W3TerrainMap.TileSize));
        }
    }

    private void BuildShadowTexture(ReadOnlySpan<byte> source)
    {
        int width = _map!.TileWidth * 4;
        int height = _map.TileHeight * 4;
        int required = checked(width * height);
        _shadowEnabled = source.Length >= required;
        if (_shadowEnabled)
        {
            byte[] values = new byte[required];
            for (int i = 0; i < required; i++) values[i] = (byte)(source[i] >> 1);
            _shadowTexture = ImageTexture.CreateFromImage(Image.CreateFromData(width, height, false, Image.Format.R8, values));
        }
        else
        {
            Image blank = Image.CreateEmpty(4, 4, false, Image.Format.R8);
            blank.Fill(Colors.Black);
            _shadowTexture = ImageTexture.CreateFromImage(blank);
        }
    }

    private void LoadCliffTypes()
    {
        string path = ResolvePath("TerrainArt/CliffTypes.slk");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        var headers = new Dictionary<int, string>();
        var row = new Dictionary<int, string>();
        int currentY = -1;
        int idX = -1, modelX = -1, rampX = -1, textureDirX = -1, textureFileX = -1;
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("C;", StringComparison.Ordinal)) continue;
            int y = SlkInt(line, "Y");
            int x = SlkInt(line, "X");
            string value = SlkString(line, "K").Trim('"');
            if (y > 0 && y != currentY)
            {
                CommitCliffRow(row, idX, modelX, rampX, textureDirX, textureFileX);
                row.Clear();
                currentY = y;
            }
            if (x <= 0) continue;
            if (currentY == 1)
            {
                headers[x] = value;
                switch (value)
                {
                    case "cliffID": idX = x; break;
                    case "cliffModelDir": modelX = x; break;
                    case "rampModelDir": rampX = x; break;
                    case "texDir": textureDirX = x; break;
                    case "texFile": textureFileX = x; break;
                }
            }
            else if (currentY > 1) row[x] = value;
        }
        CommitCliffRow(row, idX, modelX, rampX, textureDirX, textureFileX);
    }

    private void CommitCliffRow(Dictionary<int, string> row, int idX, int modelX, int rampX, int textureDirX, int textureFileX)
    {
        if (idX < 0 || !row.TryGetValue(idX, out string? id) || string.IsNullOrEmpty(id)) return;
        _types[id] = new CliffType(
            row.GetValueOrDefault(modelX, "Cliffs"), row.GetValueOrDefault(rampX, "CliffTrans"),
            row.GetValueOrDefault(textureDirX, "ReplaceableTextures/Cliff").Replace('\\', '/'),
            row.GetValueOrDefault(textureFileX, "Cliff0"));
    }

    private CliffType TypeFor(int texture)
    {
        string id = texture >= 0 && texture < _map!.CliffTilesets.Count ? _map.CliffTilesets[texture] : "CLdi";
        return _types.GetValueOrDefault(id, new CliffType("Cliffs", "CliffTrans", "ReplaceableTextures/Cliff", "Cliff0"));
    }

    /// <summary>GDScript side injects the archive/level before build().</summary>
    public void SetPathResolver(GodotObject? resolver) => _pathResolver = resolver;

    public void set_path_resolver(GodotObject? resolver) => SetPathResolver(resolver);

    private string ResolvePath(string relative)
    {
        GodotObject? resolver = _pathResolver ?? _mapResource;
        if (resolver != null && resolver.HasMethod("resolve_path"))
            return resolver.Call("resolve_path", relative).AsString();
        // Bare W3EMap resource (no archive attached): fall back to the shared
        // game-asset roots (classic → Reforged), matching the archive's own resolve_path.
        return new Diablo1Like.W3.Paths.W3PathResolverBridge().ResolveGamePath(relative);
    }

    private void ClearRenderer()
    {
        foreach (Node child in GetChildren()) child.QueueFree();
        foreach (Variant model in _modelCache.Values) model.Dispose();
        _modelCache.Clear();
        _batchBuilders.Clear();
        _types.Clear();
        _map = null;
        _mapResource = null;
        _textures = null;
        _shadowTexture = null;
        _shadowEnabled = false;
        _spawned = 0;
        _missing = 0;
        _batchCount = 0;
    }

    private static int SlkInt(string line, string key)
    {
        string token = ";" + key;
        int start = line.IndexOf(token, StringComparison.Ordinal);
        if (start < 0) return -1;
        start += token.Length;
        int end = line.IndexOf(';', start);
        string value = end < 0 ? line[start..] : line[start..end];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : -1;
    }

    private static string SlkString(string line, string key)
    {
        string token = ";" + key;
        int start = line.IndexOf(token, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += token.Length;
        if (start < line.Length && line[start] == '"')
        {
            int endQuote = line.IndexOf('"', start + 1);
            return endQuote > start ? line[start..(endQuote + 1)] : line[start..];
        }
        int end = line.IndexOf(';', start);
        return end < 0 ? line[start..] : line[start..end];
    }
}
