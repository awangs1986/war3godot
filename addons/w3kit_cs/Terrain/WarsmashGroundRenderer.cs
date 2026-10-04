using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using W3Compat.Formats;

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
    private TerrainBuild? _build;
    private GroundLayout? _layout;
    private GodotObject? _textures;
    private ShaderMaterial? _material;
    private readonly List<bool> _extended = new();
    private readonly List<string> _layerIds = new();

    public W3TerrainMap? TerrainMap => _map;

    public void Build(GodotObject mapResource, GodotObject textureProvider, byte[] shadowBytes)
    {
        _textures = textureProvider;
        W3TerrainMap map = W3TerrainMapBridge.FromGodotResource(mapResource);
        var assets = new GodotTerrainAssets(map, ResolveModelPath);
        BuildFrom(assets.Build(), textureProvider, shadowBytes);
    }

    /// <summary>Consumes the exact build already used to draw cliffs.</summary>
    internal void BuildFrom(TerrainBuild build, GodotObject textureProvider, byte[] shadowBytes)
    {
        ClearRenderer();
        _textures = textureProvider;
        _map = build.Map;
        _build = build;
        int cliffCount = build.Cliffs.Count;
        _layerIds.AddRange(_map.GroundTilesets);
        _layerIds.Add("__BLIGHT__");

        Texture2DArray[] arrays = BuildGroundArrays();
        _layout = build.CreateGroundLayout(_extended);

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
        // 与 GDS 一致：按角点高度算 CustomAabb，避免高地形被误剔除
        float yMin = float.PositiveInfinity;
        float yMax = float.NegativeInfinity;
        for (int cy = 0; cy < _map.Height; cy++)
        {
            for (int cx = 0; cx < _map.Width; cx++)
            {
                float hy = _map.FinalGroundHeightWithRamp(cx, cy) * W3TerrainMap.TileSize;
                if (hy < yMin) yMin = hy;
                if (hy > yMax) yMax = hy;
            }
        }
        if (!float.IsFinite(yMin))
        {
            yMin = 0f;
            yMax = 0f;
        }
        float x0 = _map.CenterOffsetX;
        float z0 = _map.CenterOffsetY;
        float x1 = x0 + _map.TileWidth * W3TerrainMap.TileSize;
        float z1 = z0 + _map.TileHeight * W3TerrainMap.TileSize;
        var customAabb = new Aabb(
            new Vector3(Mathf.Min(x0, x1), yMin - 64.0f, Mathf.Min(z0, z1)),
            new Vector3(Mathf.Abs(x1 - x0), (yMax - yMin) + 128.0f, Mathf.Abs(z1 - z0)));

        var instance = new MultiMeshInstance3D
        {
            Name = "WarsmashGround",
            Multimesh = multiMesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = customAabb,
            ExtraCullMargin = 256.0f,
        };
        AddChild(instance);
        BuildHeightCollision();
        GD.Print(
            $"WarsmashGroundRenderer(C#): {_map.TileWidth}x{_map.TileHeight}, cliffs={cliffCount}, shd={shadowEnabled}, aabb_y=[{yMin:F0},{yMax:F0}]");
    }

    // GDS-compatible surface while the surrounding map/object pipeline is
    // migrated separately.
    public void build(GodotObject mapResource, GodotObject textureProvider, byte[] shadowBytes) => Build(mapResource, textureProvider, shadowBytes);

    public void SetFlatInspect(bool enabled) => _material?.SetShaderParameter("flat_lighting", enabled);
    public void SetSoftTint(bool enabled) => _material?.SetShaderParameter("soft_tint", enabled);
    public void set_flat_inspect(bool enabled) => SetFlatInspect(enabled);
    public void set_soft_tint(bool enabled) => SetSoftTint(enabled);

    public bool GroundExists(int x, int y) => _build?.GroundExists(x, y) ?? false;
    public bool ground_exists(int x, int y) => GroundExists(x, y);

    private void ClearRenderer()
    {
        foreach (Node child in GetChildren())
            child.QueueFree();
        _map = null;
        _build = null;
        _layout = null;
        _material = null;
        _extended.Clear();
        _layerIds.Clear();
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
        // 与 W3Shd / open-realm 一致：R8 = shadow/2
        byte[]? values = W3ShadowMap.PackR8Half(bytes, _map!.TileWidth, _map.TileHeight);
        if (values != null)
        {
            int width = W3ShadowMap.PixelWidth(_map.TileWidth);
            int height = W3ShadowMap.PixelHeight(_map.TileHeight);
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
        foreach (System.Numerics.Vector2 corner in TerrainSurfaceGeometry.GroundTriangleCorners)
        {
            var vertex = new Vector3(corner.X, 0, corner.Y);
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

    /// <summary>解析悬崖/坡道模型路径；无资产根时返回空，避免地面虚挖洞。</summary>
    private string ResolveModelPath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return "";
        // 与 WarsmashCliffRenderer.ResolvePath 对齐
        if (_textures != null && _textures.HasMethod("resolve_path"))
        {
            string viaTextures = _textures.Call("resolve_path", relativePath).AsString();
            if (!string.IsNullOrEmpty(viaTextures))
                return viaTextures;
        }
        return new Diablo1Like.W3.Paths.W3PathResolverBridge().ResolvePath(relativePath);
    }

    /// <summary>移植 GDS _build_height_collision：供射线/角色查询落地。</summary>
    private void BuildHeightCollision()
    {
        if (_map == null || _build == null)
            return;
        int tilesX = _map.TileWidth;
        int tilesY = _map.TileHeight;
        // Collision uses the same tile topology and coverage as rendering.
        const int step = 1;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int y = 0; y < tilesY; y += step)
        {
            for (int x = 0; x < tilesX; x += step)
            {
                if (!_build.GroundExists(x, y))
                    continue;
                int x1 = Math.Min(x + step, tilesX);
                int y1 = Math.Min(y + step, tilesY);
                Vector3 p00 = CornerPos(x, y);
                Vector3 p10 = CornerPos(x1, y);
                Vector3 p01 = CornerPos(x, y1);
                Vector3 p11 = CornerPos(x1, y1);
                st.AddVertex(p11);
                st.AddVertex(p01);
                st.AddVertex(p00);
                st.AddVertex(p00);
                st.AddVertex(p10);
                st.AddVertex(p11);
            }
        }
        ArrayMesh mesh = st.Commit();
        if (mesh == null || mesh.GetFaces().Length == 0)
            return;
        var body = new StaticBody3D { Name = "TerrainCollision" };
        var col = new CollisionShape3D();
        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(mesh.GetFaces());
        col.Shape = shape;
        body.AddChild(col);
        AddChild(body);
    }

    private Vector3 CornerPos(int x, int y)
    {
        float wx = _map!.CenterOffsetX + x * W3TerrainMap.TileSize;
        float wz = _map.CenterOffsetY + y * W3TerrainMap.TileSize;
        float wy = _map.FinalGroundHeightWithRamp(
            Math.Clamp(x, 0, _map.Width - 1),
            Math.Clamp(y, 0, _map.Height - 1)) * W3TerrainMap.TileSize;
        return new Vector3(wx, wy, wz);
    }
}
