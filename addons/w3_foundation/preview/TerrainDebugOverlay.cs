using System;
using System.Collections.Generic;
using Godot;
using W3Foundation.Terrain.Data;
using W3Foundation.Terrain.Rendering;

namespace W3Foundation.Preview;

/// <summary>
/// Terrain diagnostics rendered independently from the terrain material. Tile
/// labels are allocated lazily, pooled, and shown only within 650 units and the
/// camera frustum, matching the established large-map performance rule.
/// </summary>
[GlobalClass]
public partial class TerrainDebugOverlay : Node3D
{
    public const float DefaultLabelNearDistance = 650.0f;

    [Export] public bool GridEnabled { get; set; } = true;
    [Export] public bool ChunkBoundsEnabled { get; set; }
    [Export] public bool TileLabelsEnabled { get; set; } = true;
    [Export] public float LabelNearDistance { get; set; } = DefaultLabelNearDistance;

    public int ActiveLabelCount { get; private set; }
    public Vector2I HoveredTile { get; private set; } = new(-1, -1);

    private TerrainWorld? _terrain;
    private MeshInstance3D? _gridMesh;
    private MeshInstance3D? _chunkMesh;
    private MeshInstance3D? _hoverMesh;
    private readonly Dictionary<Vector2I, Label3D> _labels = new();
    private readonly HashSet<Vector2I> _activeLabels = new();

    public void Setup(TerrainWorld terrain)
    {
        _terrain = terrain;
        RebuildLines();
    }

    public override void _Process(double delta)
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (_terrain == null || camera == null)
            return;
        UpdateForCamera(camera);
    }

    public void UpdateForCamera(Camera3D camera)
    {
        if (_terrain?.Document == null)
            return;
        UpdateNearLabels(camera);
        UpdateHoveredTile(camera);
    }

    public void RebuildLines()
    {
        if (_terrain?.Document == null)
            return;
        TerrainDocument document = _terrain.Document;
        EnsureLineNodes();
        BuildGridMesh((ImmediateMesh)_gridMesh!.Mesh, document, 1, new Color(0.15f, 0.8f, 1.0f, 0.42f));
        BuildGridMesh((ImmediateMesh)_chunkMesh!.Mesh, document, Math.Max(1, _terrain.ChunkSize), new Color(1.0f, 0.55f, 0.1f, 0.9f));
        _gridMesh.Visible = GridEnabled;
        _chunkMesh.Visible = ChunkBoundsEnabled;
    }

    public void SetGridEnabled(bool enabled)
    {
        GridEnabled = enabled;
        if (_gridMesh != null) _gridMesh.Visible = enabled;
    }

    public void SetChunkBoundsEnabled(bool enabled)
    {
        ChunkBoundsEnabled = enabled;
        if (_chunkMesh != null) _chunkMesh.Visible = enabled;
    }

    private void UpdateNearLabels(Camera3D camera)
    {
        TerrainDocument document = _terrain!.Document!;
        _activeLabels.Clear();
        if (!TileLabelsEnabled)
        {
            HideInactiveLabels();
            return;
        }

        Vector3 localCamera = _terrain.ToLocal(camera.GlobalPosition);
        float tileSize = document.TileSize;
        int minX = Math.Clamp(Mathf.FloorToInt((localCamera.X - LabelNearDistance) / tileSize) - 1, 0, document.TileWidth - 1);
        int maxX = Math.Clamp(Mathf.CeilToInt((localCamera.X + LabelNearDistance) / tileSize) + 1, 0, document.TileWidth - 1);
        int minY = Math.Clamp(Mathf.FloorToInt((localCamera.Z - LabelNearDistance) / tileSize) - 1, 0, document.TileHeight - 1);
        int maxY = Math.Clamp(Mathf.CeilToInt((localCamera.Z + LabelNearDistance) / tileSize) + 1, 0, document.TileHeight - 1);
        float nearSquared = LabelNearDistance * LabelNearDistance;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var tile = new Vector2I(x, y);
                Vector2 xz = new((x + 0.5f) * tileSize, (y + 0.5f) * tileSize);
                Vector3 localMid = new(xz.X, _terrain.SampleHeight(xz) + 18.0f, xz.Y);
                Vector3 worldMid = _terrain.ToGlobal(localMid);
                if (camera.GlobalPosition.DistanceSquaredTo(worldMid) > nearSquared || !camera.IsPositionInFrustum(worldMid))
                    continue;

                Label3D label = GetOrCreateLabel(tile);
                label.Position = localMid;
                label.Visible = true;
                _activeLabels.Add(tile);
            }
        }
        HideInactiveLabels();
    }

    private void HideInactiveLabels()
    {
        foreach ((Vector2I tile, Label3D label) in _labels)
            if (!_activeLabels.Contains(tile)) label.Visible = false;
        ActiveLabelCount = _activeLabels.Count;
    }

    private Label3D GetOrCreateLabel(Vector2I tile)
    {
        if (_labels.TryGetValue(tile, out Label3D? label))
            return label;
        label = new Label3D
        {
            Name = $"Tile_{tile.X}_{tile.Y}",
            Text = $"{tile.X},{tile.Y}",
            FontSize = 22,
            OutlineSize = 6,
            PixelSize = 0.65f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Modulate = new Color(0.8f, 0.96f, 1.0f),
        };
        AddChild(label);
        _labels[tile] = label;
        return label;
    }

    private void UpdateHoveredTile(Camera3D camera)
    {
        TerrainDocument document = _terrain!.Document!;
        Vector2 viewportCenter = GetViewport().GetVisibleRect().Size * 0.5f;
        Vector3 rayOrigin = camera.ProjectRayOrigin(viewportCenter);
        Vector3 rayDirection = camera.ProjectRayNormal(viewportCenter);
        Vector3 localOrigin = _terrain.ToLocal(rayOrigin);
        Vector3 localDirection = (_terrain.ToLocal(rayOrigin + rayDirection) - localOrigin).Normalized();
        if (Mathf.Abs(localDirection.Y) < 0.00001f)
            return;

        float distance = -localOrigin.Y / localDirection.Y;
        for (int iteration = 0; iteration < 5; iteration++)
        {
            Vector3 point = localOrigin + localDirection * distance;
            distance = (new Vector3(point.X, _terrain.SampleHeight(new Vector2(point.X, point.Z)), point.Z).Y - localOrigin.Y) / localDirection.Y;
        }
        Vector3 hit = localOrigin + localDirection * distance;
        int x = Mathf.FloorToInt(hit.X / document.TileSize);
        int y = Mathf.FloorToInt(hit.Z / document.TileSize);
        if (x < 0 || y < 0 || x >= document.TileWidth || y >= document.TileHeight)
        {
            HoveredTile = new Vector2I(-1, -1);
            _hoverMesh!.Visible = false;
            return;
        }

        HoveredTile = new Vector2I(x, y);
        DrawHoverSquare(x, y, document);
    }

    private void EnsureLineNodes()
    {
        _gridMesh ??= CreateLineNode("_TileGrid", new Color(0.15f, 0.8f, 1.0f));
        _chunkMesh ??= CreateLineNode("_ChunkBounds", new Color(1.0f, 0.55f, 0.1f));
        _hoverMesh ??= CreateLineNode("_HoveredTile", new Color(1.0f, 0.95f, 0.15f));
    }

    private MeshInstance3D CreateLineNode(string name, Color color)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = color,
            NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = new ImmediateMesh(),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        return node;
    }

    private void BuildGridMesh(ImmediateMesh mesh, TerrainDocument document, int step, Color color)
    {
        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int x = 0; x <= document.TileWidth; x += step)
        {
            for (int y = 0; y < document.TileHeight; y++)
                AddLine(mesh, new Vector2(x * document.TileSize, y * document.TileSize), new Vector2(x * document.TileSize, (y + 1) * document.TileSize), color);
        }
        if (document.TileWidth % step != 0)
            for (int y = 0; y < document.TileHeight; y++) AddLine(mesh, new Vector2(document.TileWidth * document.TileSize, y * document.TileSize), new Vector2(document.TileWidth * document.TileSize, (y + 1) * document.TileSize), color);
        for (int y = 0; y <= document.TileHeight; y += step)
        {
            for (int x = 0; x < document.TileWidth; x++)
                AddLine(mesh, new Vector2(x * document.TileSize, y * document.TileSize), new Vector2((x + 1) * document.TileSize, y * document.TileSize), color);
        }
        if (document.TileHeight % step != 0)
            for (int x = 0; x < document.TileWidth; x++) AddLine(mesh, new Vector2(x * document.TileSize, document.TileHeight * document.TileSize), new Vector2((x + 1) * document.TileSize, document.TileHeight * document.TileSize), color);
        mesh.SurfaceEnd();
    }

    private void AddLine(ImmediateMesh mesh, Vector2 from, Vector2 to, Color color)
    {
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(new Vector3(from.X, _terrain!.SampleHeight(from) + 5.0f, from.Y));
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(new Vector3(to.X, _terrain.SampleHeight(to) + 5.0f, to.Y));
    }

    private void DrawHoverSquare(int x, int y, TerrainDocument document)
    {
        EnsureLineNodes();
        var mesh = (ImmediateMesh)_hoverMesh!.Mesh;
        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        Color color = new(1.0f, 0.95f, 0.15f);
        Vector2[] corners =
        {
            new(x * document.TileSize, y * document.TileSize),
            new((x + 1) * document.TileSize, y * document.TileSize),
            new((x + 1) * document.TileSize, (y + 1) * document.TileSize),
            new(x * document.TileSize, (y + 1) * document.TileSize),
            new(x * document.TileSize, y * document.TileSize),
        };
        foreach (Vector2 corner in corners)
        {
            mesh.SurfaceSetColor(color);
            mesh.SurfaceAddVertex(new Vector3(corner.X, _terrain!.SampleHeight(corner) + 10.0f, corner.Y));
        }
        mesh.SurfaceEnd();
        _hoverMesh.Visible = true;
    }
}
