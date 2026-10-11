using System;
using System.IO;
using Godot;
using Diablo1Like.W3.Assets;
using Diablo1Like.W3.Paths;

namespace Diablo1Like.W3.Terrain;

/// <summary>
/// HiveWE 风格 GPU 水面：单个 MultiMesh，干瓦片在顶点着色器丢弃。
/// 布局数据（角点水位/地面高度/湿瓦片掩码）来自共享核心 <see cref="WaterLayout"/>，
/// 与 GDS w3_gpu_water.gd 使用同一份 W3TerrainMap，保证语义一致。
/// </summary>
[GlobalClass]
public partial class W3WaterRenderer : Node3D
{
    private W3TerrainMap? _map;
    private int _waterTiles;
    private float _waterHeightOffset = -0.7f;
    private int _waterTextureCount;

    public void Build(GodotObject mapResource, GodotObject? textureProvider = null)
    {
        ClearRenderer();
        if (mapResource == null)
            return;
        _map = W3TerrainMapBridge.FromGodotResource(mapResource);
        WaterConfig config = LoadWaterConfig();
        _waterHeightOffset = config.HeightOffset;

        int cornersX = _map.Width;
        int cornersY = _map.Height;
        int tilesX = _map.TileWidth;
        int tilesY = _map.TileHeight;
        if (tilesX <= 0 || tilesY <= 0)
            return;

        var layout = new WaterLayout(_map);
        float[] waterHeights = layout.BuildWaterHeightMap();
        float[] groundHeights = layout.BuildGroundHeightMap();
        byte[] existsMask = layout.BuildExistsMap(out _waterTiles);
        if (_waterTiles == 0)
        {
            GD.Print("W3WaterRenderer(C#): no water tiles");
            return;
        }

        var shader = GD.Load<Shader>("res://addons/w3kit/shaders/w3_gpu_water.gdshader");
        var material = new ShaderMaterial { Shader = shader, RenderPriority = 2 };
        material.SetShaderParameter("water_height_tex", BuildRfTexture(waterHeights, cornersX, cornersY));
        material.SetShaderParameter("ground_height_tex", BuildRfTexture(groundHeights, cornersX, cornersY));
        material.SetShaderParameter("water_exists_tex", BuildR8Texture(existsMask, tilesX, tilesY));
        material.SetShaderParameter("map_tiles", new Vector2I(tilesX, tilesY));
        material.SetShaderParameter("map_corners", new Vector2I(cornersX, cornersY));
        material.SetShaderParameter("tile_size", W3TerrainMap.TileSize);
        material.SetShaderParameter("water_height_offset", _waterHeightOffset);
        Texture2DArray? frames = LoadWaterFrames(config);
        if (frames != null)
            material.SetShaderParameter("water_textures", frames);
        material.SetShaderParameter("water_texture_count", _waterTextureCount);
        material.SetShaderParameter("water_texture_rate", config.TextureRate);
        material.SetShaderParameter("shallow_color_min", ToColor(config.ShallowMin));
        material.SetShaderParameter("shallow_color_max", ToColor(config.ShallowMax));
        material.SetShaderParameter("deep_color_min", ToColor(config.DeepMin));
        material.SetShaderParameter("deep_color_max", ToColor(config.DeepMax));

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = CreateUnitQuad(),
            InstanceCount = tilesX * tilesY,
        };
        float yMin = 1.0e9f;
        float yMax = -1.0e9f;
        for (int index = 0; index < multiMesh.InstanceCount; index++)
        {
            int tileX = index % tilesX;
            int tileY = index / tilesX;
            float wx = _map.CenterOffsetX + tileX * W3TerrainMap.TileSize;
            float wz = _map.CenterOffsetY + tileY * W3TerrainMap.TileSize;
            multiMesh.SetInstanceTransform(index, new Transform3D(Basis.Identity, new Vector3(wx, 0.0f, wz)));
        }
        for (int y = 0; y < cornersY; y++)
        {
            for (int x = 0; x < cornersX; x++)
            {
                W3TerrainMap.Corner corner = _map.GetCorner(x, y);
                if (!corner.Water)
                    continue;
                float wy = (corner.WaterHeight + _waterHeightOffset) * W3TerrainMap.TileSize;
                yMin = Mathf.Min(yMin, wy);
                yMax = Mathf.Max(yMax, wy);
            }
        }
        if (yMax < yMin)
        {
            yMin = 0.0f;
            yMax = 128.0f;
        }

        float x0 = _map.CenterOffsetX;
        float z0 = _map.CenterOffsetY;
        float x1 = x0 + tilesX * W3TerrainMap.TileSize;
        float z1 = z0 + tilesY * W3TerrainMap.TileSize;
        var customAabb = new Aabb(
            new Vector3(Mathf.Min(x0, x1), yMin - 64.0f, Mathf.Min(z0, z1)),
            new Vector3(Mathf.Abs(x1 - x0), (yMax - yMin) + 128.0f, Mathf.Abs(z1 - z0)));

        var instance = new MultiMeshInstance3D
        {
            Name = "GpuWater",
            Multimesh = multiMesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            CustomAabb = customAabb,
            ExtraCullMargin = 256.0f,
        };
        AddChild(instance);
        GD.Print(
            $"W3WaterRenderer(C#): wet={_waterTiles} / {tilesX * tilesY} instanced=1 offset={_waterHeightOffset:F2} frames={_waterTextureCount}");
    }

    // GDS 兼容小写别名：沙盒/工具脚本迁移前仍以 GDScript 语法调用。
    public void build(GodotObject mapResource, GodotObject? textureProvider = null) => Build(mapResource, textureProvider);

    public int GetWaterTileCount() => _waterTiles;
    public int get_water_tile_count() => GetWaterTileCount();

    public int GetWaterTextureCount() => _waterTextureCount;
    public int get_water_texture_count() => GetWaterTextureCount();

    private void ClearRenderer()
    {
        foreach (Node child in GetChildren())
            child.QueueFree();
        _map = null;
        _waterTiles = 0;
        _waterTextureCount = 0;
    }

    /// <summary>TerrainArt/Water.slk 的 tileset 行；经典/重制共用同一套 SD 帧（见原 GDS 注释）。</summary>
    private WaterConfig LoadWaterConfig()
    {
        char tileset = _map?.Tileset ?? 'L';
        var resolver = new W3PathResolverBridge();
        string path = resolver.ResolveGamePath("TerrainArt/Water.slk");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new WaterConfig();
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch
        {
            return new WaterConfig();
        }
        return WaterConfig.Parse(text, tileset);
    }

    /// <summary>动画水帧：故意只走经典资产根（resolve_path），Reforged 没有独立的水面帧集。</summary>
    private Texture2DArray? LoadWaterFrames(WaterConfig config)
    {
        _waterTextureCount = 0;
        int count = Math.Max(0, config.TextureCount);
        string baseName = config.TextureFile;
        if (count == 0 || string.IsNullOrEmpty(baseName))
            return null;
        var resolver = new W3PathResolverBridge();
        var images = new Godot.Collections.Array<Image>();
        for (int frame = 0; frame < count; frame++)
        {
            string rel = $"{baseName}{frame:D2}.blp";
            string absolute = resolver.ResolvePath(rel);
            Image? image = BlpDecoderBridge.LoadImage(absolute);
            if (image == null)
            {
                GD.PushError($"W3WaterRenderer: missing water frame {rel}");
                return null;
            }
            if (image.GetFormat() != Image.Format.Rgba8)
                image.Convert(Image.Format.Rgba8);
            if (image.GetWidth() != 128 || image.GetHeight() != 128)
                image.Resize(128, 128, Image.Interpolation.Bilinear);
            images.Add(image);
        }
        var array = new Texture2DArray();
        if (array.CreateFromImages(images) != Error.Ok)
        {
            GD.PushError($"W3WaterRenderer: failed to create {images.Count}-frame texture array");
            return null;
        }
        _waterTextureCount = images.Count;
        return array;
    }

    private static ImageTexture BuildRfTexture(float[] values, int width, int height)
    {
        byte[] data = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, data, 0, data.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(width, height, false, Image.Format.Rf, data));
    }

    private static ImageTexture BuildR8Texture(byte[] values, int width, int height)
    {
        byte[] data = new byte[values.Length];
        for (int i = 0; i < values.Length; i++)
            data[i] = values[i] != 0 ? (byte)255 : (byte)0;
        return ImageTexture.CreateFromImage(Image.CreateFromData(width, height, false, Image.Format.R8, data));
    }

    private static Color ToColor(float[] rgba) => new(rgba[0], rgba[1], rgba[2], rgba[3]);

    private static ArrayMesh CreateUnitQuad()
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] vertices =
        {
            new(0, 0, 0), new(1, 0, 0), new(0, 0, 1),
            new(1, 0, 0), new(1, 0, 1), new(0, 0, 1),
        };
        foreach (Vector3 vertex in vertices)
        {
            surface.SetNormal(Vector3.Up);
            // HiveWE water.vert：UV 来自瓦片本地坐标，而非常规展开。
            surface.SetUV(new Vector2(vertex.X, vertex.Z));
            surface.AddVertex(vertex);
        }
        return surface.Commit();
    }
}
