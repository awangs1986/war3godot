using System;
using System.Collections.Generic;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Editing;

namespace W3Foundation.Terrain.Rendering;

/// <summary>
/// Tool/runtime terrain node. Geometry is split into independent chunks while
/// material indices live in a GPU texture, so texture painting does not rebuild meshes.
/// </summary>
[Tool]
[GlobalClass]
public partial class TerrainWorld : Node3D
{
    private TerrainDocument? _document;
    private Node3D? _chunkRoot;
    private MeshInstance3D? _brushPreview;
    private ShaderMaterial? _groundMaterial;
    private StandardMaterial3D? _waterMaterial;
    private ImageTexture? _groundIndexTexture;
    private ImageTexture? _paletteTexture;
    private readonly Dictionary<Vector2I, Node3D> _chunks = new();
    private readonly HashSet<Vector2I> _dirtyChunks = new();
    private bool _fullRebuildQueued = true;
    private bool _waterVisible = true;
    private bool _debugFlatLighting;
    private bool _debugMaterialIds;

    [Export]
    public TerrainDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(_document, value))
                return;
            DisconnectDocument();
            _document = value;
            ConnectDocument();
            QueueFullRebuild();
        }
    }

    [Export(PropertyHint.Range, "4,64,1")]
    public int ChunkSize { get; set; } = 16;

    [Export]
    public bool ShowTileGrid { get; set; } = true;

    [Export]
    public bool CreateDefaultDocument { get; set; } = true;

    public override void _Ready()
    {
        if (_document == null && CreateDefaultDocument)
        {
            _document = new TerrainDocument { TileWidth = 32, TileHeight = 32 };
            _document.EnsureInitialized();
        }
        else
        {
            _document?.EnsureInitialized();
        }

        ConnectDocument();
        SetProcess(true);
        QueueFullRebuild();
    }

    public override void _ExitTree()
    {
        DisconnectDocument();
    }

    public override void _Process(double delta)
    {
        if (_fullRebuildQueued)
        {
            RebuildAll();
            return;
        }

        if (_dirtyChunks.Count > 0)
            FlushDirtyChunks();
    }

    public Rect2I ApplyBrush(Vector2 cornerPosition, TerrainBrush brush, TerrainDocument? strokeBaseline = null)
    {
        if (_document == null)
            return new Rect2I();
        return TerrainBrushEngine.Apply(_document, cornerPosition, brush, strokeBaseline);
    }

    public float SampleHeight(Vector2 localXZ)
    {
        if (_document == null || !_document.IsInitialized)
            return 0.0f;

        float gx = Math.Clamp(localXZ.X / _document.TileSize, 0.0f, _document.TileWidth);
        float gy = Math.Clamp(localXZ.Y / _document.TileSize, 0.0f, _document.TileHeight);
        int x0 = Math.Clamp((int)MathF.Floor(gx), 0, _document.TileWidth);
        int y0 = Math.Clamp((int)MathF.Floor(gy), 0, _document.TileHeight);
        int x1 = Math.Min(x0 + 1, _document.TileWidth);
        int y1 = Math.Min(y0 + 1, _document.TileHeight);
        float tx = gx - x0;
        float ty = gy - y0;
        float h0 = Mathf.Lerp(_document.GetWorldHeight(x0, y0), _document.GetWorldHeight(x1, y0), tx);
        float h1 = Mathf.Lerp(_document.GetWorldHeight(x0, y1), _document.GetWorldHeight(x1, y1), tx);
        return Mathf.Lerp(h0, h1, ty);
    }

    public bool ContainsLocalPoint(Vector2 localXZ)
    {
        return _document != null &&
            localXZ.X >= 0.0f && localXZ.Y >= 0.0f &&
            localXZ.X <= _document.TileWidth * _document.TileSize &&
            localXZ.Y <= _document.TileHeight * _document.TileSize;
    }

    public void SetWaterVisible(bool visible)
    {
        _waterVisible = visible;
        if (_chunkRoot == null)
            return;
        foreach (Node chunk in _chunkRoot.GetChildren())
        {
            if (chunk.GetNodeOrNull<MeshInstance3D>("Water") is { } water)
                water.Visible = visible;
        }
    }

    public void SetDebugFlatLighting(bool enabled)
    {
        _debugFlatLighting = enabled;
        _groundMaterial?.SetShaderParameter("debug_flat_lighting", enabled);
    }

    public void SetDebugMaterialIds(bool enabled)
    {
        _debugMaterialIds = enabled;
        _groundMaterial?.SetShaderParameter("debug_material_ids", enabled);
    }

    public Vector2 LocalPointToCorner(Vector3 localPoint)
    {
        if (_document == null)
            return Vector2.Zero;
        return new Vector2(localPoint.X / _document.TileSize, localPoint.Z / _document.TileSize);
    }

    public void SetBrushPreview(Vector3 localPoint, float radiusInCorners, Color color)
    {
        if (_document == null)
            return;

        EnsurePreview();
        var mesh = (ImmediateMesh)_brushPreview!.Mesh;
        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        const int segments = 64;
        float worldRadius = Math.Max(0.5f, radiusInCorners) * _document.TileSize;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            var xz = new Vector2(
                localPoint.X + MathF.Cos(angle) * worldRadius,
                localPoint.Z + MathF.Sin(angle) * worldRadius);
            mesh.SurfaceSetColor(color);
            mesh.SurfaceAddVertex(new Vector3(xz.X, SampleHeight(xz) + 5.0f, xz.Y));
        }
        mesh.SurfaceEnd();

        float centerHeight = SampleHeight(new Vector2(localPoint.X, localPoint.Z)) + 7.0f;
        float markerSize = Math.Min(worldRadius * 0.38f, _document.TileSize * 0.75f);
        Vector3 center = new(localPoint.X, centerHeight, localPoint.Z);
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(center + Vector3.Left * markerSize);
        mesh.SurfaceAddVertex(center + Vector3.Right * markerSize);
        mesh.SurfaceAddVertex(center + Vector3.Back * markerSize);
        mesh.SurfaceAddVertex(center + Vector3.Forward * markerSize);
        mesh.SurfaceAddVertex(center);
        mesh.SurfaceAddVertex(center + Vector3.Forward * markerSize * 1.45f);
        mesh.SurfaceAddVertex(center + Vector3.Forward * markerSize * 1.45f);
        mesh.SurfaceAddVertex(center + new Vector3(-markerSize * 0.34f, 0.0f, markerSize * 0.95f));
        mesh.SurfaceAddVertex(center + Vector3.Forward * markerSize * 1.45f);
        mesh.SurfaceAddVertex(center + new Vector3(markerSize * 0.34f, 0.0f, markerSize * 0.95f));
        mesh.SurfaceEnd();
        _brushPreview.Visible = true;
    }

    public void HideBrushPreview()
    {
        if (_brushPreview != null)
            _brushPreview.Visible = false;
    }

    public void QueueFullRebuild()
    {
        _fullRebuildQueued = true;
        SetProcess(true);
    }

    private void ConnectDocument()
    {
        if (!IsInsideTree() || _document == null)
            return;
        _document.TerrainEdited -= OnTerrainEdited;
        _document.TerrainEdited += OnTerrainEdited;
    }

    private void DisconnectDocument()
    {
        if (_document != null)
            _document.TerrainEdited -= OnTerrainEdited;
    }

    private void OnTerrainEdited(Rect2I dirtyCorners, bool geometryChanged)
    {
        UpdateGroundIndexTexture();
        UpdatePaletteTexture();
        if (geometryChanged)
            MarkChunksDirty(dirtyCorners);
    }

    private void RebuildAll()
    {
        _fullRebuildQueued = false;
        _dirtyChunks.Clear();
        _chunks.Clear();

        if (_chunkRoot != null && IsInstanceValid(_chunkRoot))
        {
            RemoveChild(_chunkRoot);
            _chunkRoot.QueueFree();
        }
        _chunkRoot = new Node3D { Name = "_TerrainChunks" };
        AddChild(_chunkRoot);

        if (_document == null)
            return;
        _document.EnsureInitialized();
        BuildSharedMaterial();

        int chunksX = Mathf.CeilToInt(_document.TileWidth / (float)Math.Max(1, ChunkSize));
        int chunksY = Mathf.CeilToInt(_document.TileHeight / (float)Math.Max(1, ChunkSize));
        for (int cy = 0; cy < chunksY; cy++)
        {
            for (int cx = 0; cx < chunksX; cx++)
                RebuildChunk(new Vector2I(cx, cy));
        }
    }

    private void FlushDirtyChunks()
    {
        var dirty = new List<Vector2I>(_dirtyChunks);
        _dirtyChunks.Clear();
        foreach (Vector2I coordinate in dirty)
            RebuildChunk(coordinate);
    }

    private void MarkChunksDirty(Rect2I dirtyCorners)
    {
        if (_document == null)
            return;
        int size = Math.Max(1, ChunkSize);
        int minX = Math.Max(0, dirtyCorners.Position.X - 1) / size;
        int minY = Math.Max(0, dirtyCorners.Position.Y - 1) / size;
        int maxCornerX = Math.Min(_document.TileWidth, dirtyCorners.End.X + 1);
        int maxCornerY = Math.Min(_document.TileHeight, dirtyCorners.End.Y + 1);
        int maxX = maxCornerX / size;
        int maxY = maxCornerY / size;
        for (int cy = minY; cy <= maxY; cy++)
        {
            for (int cx = minX; cx <= maxX; cx++)
                _dirtyChunks.Add(new Vector2I(cx, cy));
        }
    }

    private void RebuildChunk(Vector2I coordinate)
    {
        if (_document == null || _chunkRoot == null)
            return;

        int startX = coordinate.X * Math.Max(1, ChunkSize);
        int startY = coordinate.Y * Math.Max(1, ChunkSize);
        if (startX >= _document.TileWidth || startY >= _document.TileHeight)
            return;
        int endX = Math.Min(startX + ChunkSize, _document.TileWidth);
        int endY = Math.Min(startY + ChunkSize, _document.TileHeight);

        if (_chunks.TryGetValue(coordinate, out Node3D? oldChunk) && IsInstanceValid(oldChunk))
        {
            _chunkRoot.RemoveChild(oldChunk);
            oldChunk.QueueFree();
        }

        var chunk = new Node3D { Name = $"Chunk_{coordinate.X}_{coordinate.Y}" };
        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = BuildChunkMesh(startX, startY, endX, endY),
            MaterialOverride = _groundMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        chunk.AddChild(ground);
        ArrayMesh? waterMesh = BuildWaterMesh(startX, startY, endX, endY);
        if (waterMesh != null)
        {
            var water = new MeshInstance3D
            {
                Name = "Water",
                Mesh = waterMesh,
                MaterialOverride = GetWaterMaterial(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = _waterVisible,
            };
            chunk.AddChild(water);
        }
        _chunkRoot.AddChild(chunk);
        _chunks[coordinate] = chunk;
    }

    private ArrayMesh BuildChunkMesh(int startX, int startY, int endX, int endY)
    {
        int width = endX - startX;
        int height = endY - startY;
        var vertices = new Vector3[(width + 1) * (height + 1)];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        var indices = new int[width * height * 6];

        int vertex = 0;
        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                float worldX = x * _document!.TileSize;
                float worldZ = y * _document.TileSize;
                vertices[vertex] = new Vector3(worldX, _document.GetWorldHeight(x, y), worldZ);
                normals[vertex] = CalculateNormal(x, y);
                uvs[vertex] = new Vector2(x / (float)_document.TileWidth, y / (float)_document.TileHeight);
                vertex++;
            }
        }

        int index = 0;
        int stride = width + 1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i00 = y * stride + x;
                int i10 = i00 + 1;
                int i01 = i00 + stride;
                int i11 = i01 + 1;
                indices[index++] = i00;
                indices[index++] = i01;
                indices[index++] = i10;
                indices[index++] = i10;
                indices[index++] = i01;
                indices[index++] = i11;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private ArrayMesh? BuildWaterMesh(int startX, int startY, int endX, int endY)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                int i00 = _document!.CornerIndex(x, y);
                int i10 = _document.CornerIndex(x + 1, y);
                int i01 = _document.CornerIndex(x, y + 1);
                int i11 = _document.CornerIndex(x + 1, y + 1);
                bool w00 = (_document.CornerFlags[i00] & TerrainDocument.FlagWater) != 0;
                bool w10 = (_document.CornerFlags[i10] & TerrainDocument.FlagWater) != 0;
                bool w01 = (_document.CornerFlags[i01] & TerrainDocument.FlagWater) != 0;
                bool w11 = (_document.CornerFlags[i11] & TerrainDocument.FlagWater) != 0;
                int waterCorners = (w00 ? 1 : 0) + (w10 ? 1 : 0) + (w01 ? 1 : 0) + (w11 ? 1 : 0);
                if (waterCorners < 2)
                    continue;

                float fallback = Math.Max(
                    Math.Max(_document.GetWorldHeight(x, y), _document.GetWorldHeight(x + 1, y)),
                    Math.Max(_document.GetWorldHeight(x, y + 1), _document.GetWorldHeight(x + 1, y + 1))) + 8.0f;
                int first = vertices.Count;
                vertices.Add(new Vector3(x * _document.TileSize, w00 ? _document.WaterHeights[i00] : fallback, y * _document.TileSize));
                vertices.Add(new Vector3((x + 1) * _document.TileSize, w10 ? _document.WaterHeights[i10] : fallback, y * _document.TileSize));
                vertices.Add(new Vector3(x * _document.TileSize, w01 ? _document.WaterHeights[i01] : fallback, (y + 1) * _document.TileSize));
                vertices.Add(new Vector3((x + 1) * _document.TileSize, w11 ? _document.WaterHeights[i11] : fallback, (y + 1) * _document.TileSize));
                normals.Add(Vector3.Up);
                normals.Add(Vector3.Up);
                normals.Add(Vector3.Up);
                normals.Add(Vector3.Up);
                indices.Add(first);
                indices.Add(first + 2);
                indices.Add(first + 1);
                indices.Add(first + 1);
                indices.Add(first + 2);
                indices.Add(first + 3);
            }
        }

        if (vertices.Count == 0)
            return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private StandardMaterial3D GetWaterMaterial()
    {
        if (_waterMaterial != null)
            return _waterMaterial;
        _waterMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.12f, 0.48f, 0.7f, 0.62f),
            Metallic = 0.08f,
            Roughness = 0.16f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return _waterMaterial;
    }

    private Vector3 CalculateNormal(int x, int y)
    {
        float left = _document!.GetWorldHeight(x - 1, y);
        float right = _document.GetWorldHeight(x + 1, y);
        float down = _document.GetWorldHeight(x, y - 1);
        float up = _document.GetWorldHeight(x, y + 1);
        return new Vector3(left - right, _document.TileSize * 2.0f, down - up).Normalized();
    }

    private void BuildSharedMaterial()
    {
        var shader = GD.Load<Shader>("res://addons/w3_foundation/shaders/terrain_ground.gdshader");
        _groundMaterial = new ShaderMaterial { Shader = shader };
        UpdateGroundIndexTexture();
        UpdatePaletteTexture();
        _groundMaterial.SetShaderParameter("map_tiles", new Vector2I(_document!.TileWidth, _document.TileHeight));
        _groundMaterial.SetShaderParameter("map_corners", new Vector2I(_document.CornerWidth, _document.CornerHeight));
        _groundMaterial.SetShaderParameter("grid_strength", ShowTileGrid ? 0.08f : 0.0f);
        _groundMaterial.SetShaderParameter("debug_flat_lighting", _debugFlatLighting);
        _groundMaterial.SetShaderParameter("debug_material_ids", _debugMaterialIds);
    }

    private void UpdateGroundIndexTexture()
    {
        if (_document == null || !_document.IsInitialized)
            return;
        var image = Image.CreateEmpty(_document.CornerWidth, _document.CornerHeight, false, Image.Format.R8);
        for (int y = 0; y < _document.CornerHeight; y++)
        {
            for (int x = 0; x < _document.CornerWidth; x++)
            {
                byte value = _document.GroundMaterials[_document.CornerIndex(x, y)];
                image.SetPixel(x, y, new Color(value / 255.0f, 0.0f, 0.0f));
            }
        }
        if (_groundIndexTexture == null ||
            _groundIndexTexture.GetWidth() != _document.CornerWidth ||
            _groundIndexTexture.GetHeight() != _document.CornerHeight)
            _groundIndexTexture = ImageTexture.CreateFromImage(image);
        else
            _groundIndexTexture.Update(image);
        _groundMaterial?.SetShaderParameter("ground_index", _groundIndexTexture);
    }

    private void UpdatePaletteTexture()
    {
        if (_document == null)
            return;
        int width = Math.Max(1, _document.Palette.Count);
        var image = Image.CreateEmpty(width, 1, false, Image.Format.Rgba8);
        for (int x = 0; x < width; x++)
        {
            Color color = x < _document.Palette.Count ? _document.Palette[x].PreviewColor : Colors.Magenta;
            image.SetPixel(x, 0, color);
        }
        if (_paletteTexture == null || _paletteTexture.GetWidth() != width)
            _paletteTexture = ImageTexture.CreateFromImage(image);
        else
            _paletteTexture.Update(image);
        _groundMaterial?.SetShaderParameter("palette_tex", _paletteTexture);
    }

    private void EnsurePreview()
    {
        if (_brushPreview != null && IsInstanceValid(_brushPreview))
            return;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = false,
        };
        _brushPreview = new MeshInstance3D
        {
            Name = "_TerrainBrushPreview",
            Mesh = new ImmediateMesh(),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_brushPreview);
    }
}
