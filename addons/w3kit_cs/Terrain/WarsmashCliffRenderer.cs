using System;
using System.Collections.Generic;
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

    private sealed class CliffBatchBuilder
    {
        public readonly SurfaceTool Surface = new();
        public int InstanceCount;
        public Material Material { get; }

        public CliffBatchBuilder(Material material)
        {
            Material = material;
            Surface.Begin(Mesh.PrimitiveType.Triangles);
        }
    }

    private GodotTerrainAssets? _assets;
    private readonly HashSet<ShaderMaterial> _materials = new();
    internal TerrainBuild? BuildResult { get; private set; }
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
        _assets = new GodotTerrainAssets(_map, ResolvePath);
        BuildResult = _assets.Build();
        _map = BuildResult.Map;
        _missing = BuildResult.MissingModels.Count;
        BuildShadowTexture(shadowBytes);
        foreach (WarsmashTerrainRules.CliffPlacement placement in BuildResult.Cliffs)
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

    public void SetFlatInspect(bool enabled)
    {
        foreach (ShaderMaterial material in _materials)
            material.SetShaderParameter("flat_lighting", enabled);
    }

    public void set_flat_inspect(bool enabled) => SetFlatInspect(enabled);

    private void Spawn(WarsmashTerrainRules.CliffPlacement placement)
    {
        string absolutePath = ResolvePath(placement.ModelPath);
        // Geometry is already decoded and retained by the build's asset adapter.
        if (string.IsNullOrEmpty(absolutePath)) absolutePath = placement.ModelPath;
        W3MdxModel? model = _assets!.LoadModel(placement.ModelPath);
        if (model == null)
            throw new InvalidOperationException($"Terrain build lost its decoded model: {placement.ModelPath}");

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
        Node3D instance = new W3MdxInstance
        {
            StaticVertexTransform = (position, normal) =>
            {
                var result = TerrainSurfaceGeometry.DeformCliffVertex(_map, placement.X, placement.Y,
                    new System.Numerics.Vector3(position.X, position.Y, position.Z),
                    new System.Numerics.Vector3(normal.X, normal.Y, normal.Z));
                return (new Vector3(result.Position.X, result.Position.Y, result.Position.Z),
                    new Vector3(result.Normal.X, result.Normal.Y, result.Normal.Z));
            },
        };
        node.AddChild(instance);
        CliffTypeInfo type = _assets.TypeFor(placement.CliffTexture);
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
        var options = new GDictionary
        {
            ["cliff_texture"] = texturePath,
            ["cliff_remap"] = true,
        };
        instance.Call("setup", Variant.From(model), _textures!, absolutePath, options);
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
        Transform3D rendererInverse = GlobalTransform.AffineInverse();
        foreach (Node child in instance.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (child is not MeshInstance3D { Mesh: not null } source)
                continue;
            Transform3D toRenderer = rendererInverse * source.GlobalTransform;
            for (int surface = 0; surface < source.Mesh.GetSurfaceCount(); surface++)
            {
                // MakeMaterial uses one cliff shader/albedo per texturePath.
                // AppendFrom copies geometry, not the node's MaterialOverride.
                Material material = source.GetActiveMaterial(surface);
                if (!_batchBuilders.TryGetValue(key, out CliffBatchBuilder? builder))
                {
                    builder = new CliffBatchBuilder(material);
                    _batchBuilders[key] = builder;
                    if (material is ShaderMaterial shader) _materials.Add(shader);
                }
                builder.Surface.AppendFrom(source.Mesh, surface, toRenderer);
            }
        }
        if (_batchBuilders.TryGetValue(key, out CliffBatchBuilder? completed))
            completed.InstanceCount++;
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
            mesh.SurfaceSetMaterial(0, pair.Value.Material);
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
        _assets = null;
        BuildResult = null;
        _materials.Clear();
        _batchBuilders.Clear();
        _map = null;
        _mapResource = null;
        _textures = null;
        _shadowTexture = null;
        _shadowEnabled = false;
        _spawned = 0;
        _missing = 0;
        _batchCount = 0;
    }

}
