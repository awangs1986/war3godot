using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace Diablo1Like.W3.Terrain;

/// <summary>
/// Godot presentation layer over the shared W3TerrainCore ground packing
/// (GroundLayout). All block-map math lives in the core library; this class
/// only uploads the produced arrays into Godot textures and a MultiMesh.
/// </summary>
[GlobalClass]
public partial class WarsmashGroundRenderer : Node3D
{
    private const int LayerStride = GroundLayout.LayerStride;
    private const int ArrayBankSize = 256;
    private const int ArrayBankCount = 3;
    private const int CellSize = GroundLayout.CellSize;

    private W3TerrainMap? _map;
    private WarsmashTerrainRules? _rules;
    private GroundLayout? _layout;
    private GodotObject? _textures;
    private ShaderMaterial? _material;
    private readonly List<bool> _extended = new();
    private readonly List<int> _cliffToGround = new();
    private readonly List<string> _layerIds = new();

    public W3TerrainMap? TerrainMap => _map;
    public WarsmashTerrainRules? Rules => _rules;

    public void Build(GodotObject mapResource, GodotObject textureProvider, byte[] shadowBytes)
    {
        ClearRenderer();
        _textures = textureProvider;
        _map = W3TerrainMapBridge.FromGodotResource(mapResource);
        _rules = new WarsmashTerrainRules(_map);
        int cliffCount = _rules.RebuildCliffs().Count;
        _layerIds.AddRange(_map.GroundTilesets);
        _layerIds.Add("__BLIGHT__");
        PrepareCliffGroundMapping();

        Texture2DArray[] arrays = BuildGroundArrays();
        _layout = new GroundLayout(_map, _rules, _extended, _cliffToGround, _layerIds.Count);

        ImageTexture heightTexture = BuildHeightTexture();
        ImageTexture normalHeightTexture = BuildNormalHeightTexture();
        ImageTexture existsTexture = BuildExistsTexture();
        ImageTexture tileTexture = BuildTileTexture();
        (Texture2D shadowTexture, bool shadowEnabled) = BuildShadowTexture(shadowBytes);

        var shader = GD.Load<Shader>("res://addons/w3kit/shaders/w3_ground_unified.gdshader");
        _material = new ShaderMaterial { Shader = shader };
        _material.SetShaderParameter("tile_data", tileTexture);
        _material.SetShaderParameter("height_tex", heightTexture);
        _material.SetShaderParameter("height_normal_tex", normalHeightTexture);
        _material.SetShaderParameter("height_normal_enabled", true);
        _material.SetShaderParameter("exists_tex", existsTexture);
        _material.SetShaderParameter("ground_array0", arrays[0]);
        _material.SetShaderParameter("ground_array1", arrays[1]);
        _material.SetShaderParameter("ground_array2", arrays[2]);
        _material.SetShaderParameter("shd_tex", shadowTexture);
        _material.SetShaderParameter("shd_enabled", shadowEnabled);
        _material.SetShaderParameter("map_tiles", new Vector2I(_map.TileWidth, _map.TileHeight));
        _material.SetShaderParameter("map_corners", new Vector2I(_map.Width, _map.Height));
        _material.SetShaderParameter("tile_size", W3TerrainMap.TileSize);
        _material.SetShaderParameter("map_origin", new Vector2(_map.CenterOffsetX, _map.CenterOffsetY));
        _material.SetShaderParameter("map_size", new Vector2(_map.TileWidth * W3TerrainMap.TileSize, _map.TileHeight * W3TerrainMap.TileSize));
        ApplyLighting(new Vector3(0.45f, 0.85f, 0.28f).Normalized(), 0.35f, 1.1f);

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = CreateUnitQuad(),
            InstanceCount = _map.TileWidth * _map.TileHeight,
        };
        for (int index = 0; index < multiMesh.InstanceCount; index++)
        {
            int x = index % _map.TileWidth;
            int y = index / _map.TileWidth;
            multiMesh.SetInstanceTransform(index, new Transform3D(Basis.Identity,
                new Vector3(_map.CenterOffsetX + x * W3TerrainMap.TileSize, 0.0f,
                    _map.CenterOffsetY + y * W3TerrainMap.TileSize)));
        }
        var instance = new MultiMeshInstance3D
        {
            Name = "WarsmashGround",
            Multimesh = multiMesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 256.0f,
        };
        AddChild(instance);
        GD.Print($"WarsmashGroundRenderer(C#): {_map.TileWidth}x{_map.TileHeight}, cliffs={cliffCount}, shd={shadowEnabled}");
    }

    // GDS-compatible surface while the surrounding map/object pipeline is
    // migrated separately.
    public void build(GodotObject mapResource, GodotObject textureProvider, byte[] shadowBytes) => Build(mapResource, textureProvider, shadowBytes);

    public void ApplyLighting(Vector3 direction, float ambient = 0.35f, float scale = 1.1f)
    {
        if (_material == null)
            return;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector3(0.45f, 0.85f, 0.28f);
        _material.SetShaderParameter("light_dir", direction.Normalized());
        _material.SetShaderParameter("light_ambient", ambient);
        _material.SetShaderParameter("light_scale", scale);
    }

    public void apply_lighting(Vector3 direction, float ambient = 0.35f, float scale = 1.1f) => ApplyLighting(direction, ambient, scale);

    public void SetFlatInspect(bool enabled) => _material?.SetShaderParameter("flat_lighting", enabled);
    public void SetSoftTint(bool enabled) => _material?.SetShaderParameter("soft_tint", enabled);
    public void set_flat_inspect(bool enabled) => SetFlatInspect(enabled);
    public void set_soft_tint(bool enabled) => SetSoftTint(enabled);

    public bool GroundExists(int x, int y) => _rules?.GroundExists(x, y) ?? false;
    public bool ground_exists(int x, int y) => GroundExists(x, y);

    private void ClearRenderer()
    {
        foreach (Node child in GetChildren())
            child.QueueFree();
        _map = null;
        _rules = null;
        _layout = null;
        _material = null;
        _extended.Clear();
        _cliffToGround.Clear();
        _layerIds.Clear();
    }

    private void PrepareCliffGroundMapping()
    {
        foreach (string cliffId in _map!.CliffTilesets)
        {
            string groundId = _textures!.Call("get_cliff_ground_tile", cliffId).AsString();
            int index = _map.GroundTilesets.IndexOf(groundId);
            _cliffToGround.Add(index >= 0 ? index : 0);
        }
    }

    private Texture2DArray[] BuildGroundArrays()
    {
        var layers = new List<Image>();
        for (int textureIndex = 0; textureIndex < _layerIds.Count; textureIndex++)
        {
            string id = _layerIds[textureIndex];
            Variant result = id == "__BLIGHT__"
                ? _textures!.Call("get_blight_image", _map!.Tileset.ToString())
                : _textures!.Call("get_ground_image", id);
            Image? source = result.AsGodotObject() as Image;
            Image image = source?.Duplicate() as Image ?? CreateFallbackAtlas();
            if (image.GetFormat() != Image.Format.Rgba8)
                image.Convert(Image.Format.Rgba8);
            bool extended = image.GetWidth() >= image.GetHeight() * 2;
            _extended.Add(extended);
            byte[] atlas = image.GetData();
            for (int sub = 0; sub < LayerStride; sub++)
            {
                byte[] cell = GroundLayout.SliceCell(atlas, image.GetWidth(), image.GetHeight(), sub, extended);
                Image layer = Image.CreateFromData(CellSize, CellSize, false, Image.Format.Rgba8, cell);
                // HiveWE GroundTexture: GL_LINEAR_MIPMAP_LINEAR with generated mips.
                layer.GenerateMipmaps();
                layers.Add(layer);
            }
        }

        var banks = new Texture2DArray[ArrayBankCount];
        for (int bank = 0; bank < ArrayBankCount; bank++)
        {
            var images = new Array<Image>();
            int first = bank * ArrayBankSize;
            int last = Math.Min(first + ArrayBankSize, layers.Count);
            for (int index = first; index < last; index++)
                images.Add(layers[index]);
            if (images.Count == 0)
            {
                var transparent = Image.CreateEmpty(CellSize, CellSize, false, Image.Format.Rgba8);
                transparent.Fill(Colors.Transparent);
                transparent.GenerateMipmaps(); // array layers must share the mip flag
                images.Add(transparent);
            }
            var array = new Texture2DArray();
            array.CreateFromImages(images);
            banks[bank] = array;
        }
        return banks;
    }

    private ImageTexture BuildHeightTexture()
    {
        float[] heights = _layout!.BuildHeightMap();
        byte[] data = new byte[heights.Length * 4];
        Buffer.BlockCopy(heights, 0, data, 0, data.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(_map!.Width, _map.Height, false, Image.Format.Rf, data));
    }

    /// <summary>
    /// Lighting-only heights (HiveWE ground_heights: no cliff layers / ramp
    /// lift) so cliff steps don't shade the surrounding ground tiles.
    /// </summary>
    private ImageTexture BuildNormalHeightTexture()
    {
        float[] heights = _layout!.BuildNormalHeightMap();
        byte[] data = new byte[heights.Length * 4];
        Buffer.BlockCopy(heights, 0, data, 0, data.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(_map!.Width, _map.Height, false, Image.Format.Rf, data));
    }

    private ImageTexture BuildExistsTexture()
    {
        byte[] exists = _layout!.BuildExistsMap();
        byte[] data = new byte[exists.Length];
        for (int i = 0; i < exists.Length; i++)
            data[i] = exists[i] != 0 ? (byte)255 : (byte)0;
        return ImageTexture.CreateFromImage(Image.CreateFromData(_map!.TileWidth, _map.TileHeight, false, Image.Format.R8, data));
    }

    private ImageTexture BuildTileTexture()
    {
        float[] tileData = _layout!.BuildTileData();
        byte[] data = new byte[tileData.Length * 4];
        Buffer.BlockCopy(tileData, 0, data, 0, data.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(_map!.TileWidth, _map.TileHeight, false, Image.Format.Rgbaf, data));
    }

    private (Texture2D Texture, bool Enabled) BuildShadowTexture(ReadOnlySpan<byte> bytes)
    {
        int width = _map!.TileWidth * 4;
        int height = _map.TileHeight * 4;
        int required = checked(width * height);
        if (bytes.Length >= required)
        {
            byte[] values = new byte[required];
            for (int i = 0; i < required; i++)
                values[i] = (byte)(bytes[i] >> 1);
            Image image = Image.CreateFromData(width, height, false, Image.Format.R8, values);
            return (ImageTexture.CreateFromImage(image), true);
        }
        var blank = Image.CreateEmpty(4, 4, false, Image.Format.R8);
        blank.Fill(Colors.Black);
        return (ImageTexture.CreateFromImage(blank), false);
    }

    private static ArrayMesh CreateUnitQuad()
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] vertices =
        {
            new(0,0,0), new(1,0,0), new(0,0,1),
            new(1,0,0), new(1,0,1), new(0,0,1),
        };
        foreach (Vector3 vertex in vertices)
        {
            surface.SetNormal(Vector3.Up);
            surface.SetUV(new Vector2(vertex.X, vertex.Z));
            surface.AddVertex(vertex);
        }
        return surface.Commit();
    }

    private static Image CreateFallbackAtlas()
    {
        var image = Image.CreateEmpty(256, 256, false, Image.Format.Rgba8);
        image.Fill(new Color(0.35f, 0.5f, 0.28f));
        return image;
    }
}
