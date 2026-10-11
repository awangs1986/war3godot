using Godot;

namespace Diablo1Like.W3.Sandbox;

/// <summary>
/// 预览用 tile 调试：网格线 + 相机范围内、经视锥测试的 tile 坐标标签。
/// 迁移自 scripts/w3/w3_tile_debug_overlay.gd。<c>_map</c> 仍是 w3e_map.gd
/// （GDScript 门面，R1 遗留），用 GodotObject 承载并走 Call/Get 动态访问。
/// </summary>
[GlobalClass]
public partial class W3TileDebugOverlay : Node3D
{
    // 与 addons/w3kit/w3e_map.gd 的 TILE_SIZE 保持一致。
    private const float TileSize = 128.0f;
    private const float LabelNear = 650.0f;

    [Export] public float GridYBias = 4.0f;
    [Export] public float LabelYBias = 12.0f;

    private GodotObject? _map;
    private MeshInstance3D? _gridMi;
    private Node3D? _labelsRoot;
    private Node3D? _highlightRoot;
    private bool _enabled;
    private Vector2I _hoverTile = new(-1, -1);
    private Vector2I _focusTile = new(-1, -1);
    private readonly Godot.Collections.Dictionary<string, Label3D> _labelPool = new();
    private readonly Godot.Collections.Dictionary<string, bool> _activeKeys = new();

    public void setup(GodotObject? map)
    {
        _map = map;
        Clear();
        if (_map == null)
            return;
        _labelsRoot = new Node3D { Name = "TileLabels" };
        AddChild(_labelsRoot);
        _highlightRoot = new Node3D { Name = "FocusHighlight" };
        AddChild(_highlightRoot);
        Visible = _enabled;
    }

    public void set_debug_enabled(bool on)
    {
        _enabled = on;
        // G 是完整网格的显式开关；goto 只需要它的小型聚焦标记——同步构建整张
        // 地图网格会让第一次跳转卡顿。
        if (on && _gridMi == null)
            BuildGrid();
        Visible = on;
        if (!on)
            HideAllLabels();
        else if (_focusTile.X >= 0)
            highlight_tile(_focusTile);
    }

    public bool is_debug_enabled() => _enabled;

    /// <summary>只读探针：网格线网格是否已构建（供 tools/w3_goto_smoke.gd 等外部探针使用，
    /// 替代 GDScript 时代直接访问下划线私有字段的写法）。</summary>
    public bool has_grid() => _gridMi != null;

    /// <summary>只读探针：当前聚焦高亮下的子节点数量。</summary>
    public int highlight_child_count() => _highlightRoot?.GetChildCount() ?? 0;

    public bool toggle()
    {
        set_debug_enabled(!_enabled);
        return _enabled;
    }

    public Vector2I get_hover_tile() => _hoverTile;

    public Vector2I get_focus_tile() => _focusTile;

    /// <summary>强制显示一个 tile：粗红边框、红色坐标标签、TL/TR/BL/BR 角标。</summary>
    public void highlight_tile(Vector2I tile)
    {
        _focusTile = tile;
        if (_highlightRoot == null || _map == null)
            return;
        foreach (Node c in _highlightRoot.GetChildren())
            c.QueueFree();
        if (tile.X < 0 || tile.Y < 0)
            return;
        if (tile.X >= TileCountX() || tile.Y >= TileCountY())
            return;

        int x = tile.X;
        int y = tile.Y;
        Vector3 p00 = CornerWorldPos(x, y);
        Vector3 p10 = CornerWorldPos(x + 1, y);
        Vector3 p01 = CornerWorldPos(x, y + 1);
        Vector3 p11 = CornerWorldPos(x + 1, y + 1);
        float yTop = Mathf.Max(Mathf.Max(p00.Y, p10.Y), Mathf.Max(p01.Y, p11.Y)) + GridYBias + 2.0f;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        var red = new Color(1.0f, 0.08f, 0.08f, 1.0f);
        Vector3[] ring =
        {
            new(p00.X, yTop, p00.Z), new(p10.X, yTop, p10.Z),
            new(p10.X, yTop, p10.Z), new(p11.X, yTop, p11.Z),
            new(p11.X, yTop, p11.Z), new(p01.X, yTop, p01.Z),
            new(p01.X, yTop, p01.Z), new(p00.X, yTop, p00.Z),
        };
        Vector3 mid = (p00 + p11) * 0.5f;
        for (int i = 0; i < ring.Length; i += 2)
        {
            Vector3 a = ring[i];
            Vector3 b = ring[i + 1];
            st.SetColor(red);
            st.AddVertex(a);
            st.SetColor(red);
            st.AddVertex(b);
            Vector3 a2 = a.Lerp(mid, 0.06f);
            Vector3 b2 = b.Lerp(mid, 0.06f);
            a2.Y = yTop + 1.0f;
            b2.Y = yTop + 1.0f;
            st.SetColor(red);
            st.AddVertex(a2);
            st.SetColor(red);
            st.AddVertex(b2);
        }

        var mi = new MeshInstance3D { Mesh = st.Commit() };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            RenderPriority = 20,
        };
        mi.MaterialOverride = mat;
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _highlightRoot.AddChild(mi);

        var lab = new Label3D
        {
            Text = $"{x},{y}",
            FontSize = 96,
            PixelSize = 0.55f,
            Modulate = new Color(1.0f, 0.05f, 0.05f, 1.0f),
            OutlineModulate = new Color(0, 0, 0, 1),
            OutlineSize = 12,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(mid.X, yTop + LabelYBias + 8.0f, mid.Z),
        };
        _highlightRoot.AddChild(lab);

        AddCornerTag("BL", new Vector3(p00.X, yTop + 6.0f, p00.Z), new Color(1, 0.35f, 0.35f));
        AddCornerTag("BR", new Vector3(p10.X, yTop + 6.0f, p10.Z), new Color(1, 0.35f, 0.35f));
        AddCornerTag("TL", new Vector3(p01.X, yTop + 6.0f, p01.Z), new Color(1, 0.35f, 0.35f));
        AddCornerTag("TR", new Vector3(p11.X, yTop + 6.0f, p11.Z), new Color(1, 0.35f, 0.35f));

        Visible = true;
        _enabled = true;
    }

    private void AddCornerTag(string text, Vector3 pos, Color col)
    {
        var lab = new Label3D
        {
            Text = text,
            FontSize = 64,
            PixelSize = 0.35f,
            Modulate = col,
            OutlineModulate = new Color(0, 0, 0, 1),
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = pos,
        };
        _highlightRoot!.AddChild(lab);
    }

    private void Clear()
    {
        foreach (Node c in GetChildren())
            c.QueueFree();
        _gridMi = null;
        _labelsRoot = null;
        _highlightRoot = null;
        _labelPool.Clear();
        _activeKeys.Clear();
    }

    private Vector3 Corner(int x, int y)
    {
        Vector3 p = CornerWorldPos(x, y);
        p.Y += GridYBias;
        return p;
    }

    private void BuildGrid()
    {
        int tilesX = TileCountX();
        int tilesY = TileCountY();
        int cornersX = _map!.Get("width").AsInt32();
        int cornersY = _map.Get("height").AsInt32();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        var col = new Color(0.15f, 0.95f, 0.35f, 0.85f);
        for (int y = 0; y < cornersY; y++)
        {
            for (int x = 0; x < tilesX; x++)
            {
                st.SetColor(col);
                st.AddVertex(Corner(x, y));
                st.SetColor(col);
                st.AddVertex(Corner(x + 1, y));
            }
        }
        for (int x = 0; x < cornersX; x++)
        {
            for (int y = 0; y < tilesY; y++)
            {
                st.SetColor(col);
                st.AddVertex(Corner(x, y));
                st.SetColor(col);
                st.AddVertex(Corner(x, y + 1));
            }
        }
        ArrayMesh mesh = st.Commit();
        _gridMi = new MeshInstance3D { Name = "TileGridLines", Mesh = mesh };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1, 1, 1, 1),
            NoDepthTest = true,
            RenderPriority = 10,
        };
        _gridMi.MaterialOverride = mat;
        _gridMi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_gridMi);
    }

    public void update_hover(Camera3D? camera)
    {
        _hoverTile = new Vector2I(-1, -1);
        if (camera == null || _map == null)
        {
            HideAllLabels();
            return;
        }
        UpdateHoverTile(camera);
        UpdateNearLabels(camera);
    }

    private void UpdateHoverTile(Camera3D camera)
    {
        Vector3 from = camera.GlobalPosition;
        Vector3 to = from + -camera.GlobalTransform.Basis.Z * 20000.0f;
        var space = GetWorld3D().DirectSpaceState;
        var q = PhysicsRayQueryParameters3D.Create(from, to);
        q.CollideWithAreas = false;
        q.CollideWithBodies = true;
        Godot.Collections.Dictionary hit = space.IntersectRay(q);
        Vector2 worldXz;
        if (hit.ContainsKey("position"))
        {
            var pos = (Vector3)hit["position"];
            worldXz = new Vector2(pos.X, pos.Z);
        }
        else
        {
            Vector3 dir = -camera.GlobalTransform.Basis.Z;
            if (Mathf.Abs(dir.Y) < 0.001f)
                return;
            float yPlane = camera.GlobalPosition.Y * 0.5f;
            float t = (yPlane - from.Y) / dir.Y;
            if (t < 0.0f)
                return;
            Vector3 p = from + dir * t;
            worldXz = new Vector2(p.X, p.Z);
        }
        Vector2 centerOffset = _map!.Get("center_offset").AsVector2();
        float lx = (worldXz.X - centerOffset.X) / TileSize;
        float ly = (worldXz.Y - centerOffset.Y) / TileSize;
        int tx = (int)Mathf.Floor(lx);
        int ty = (int)Mathf.Floor(ly);
        if (tx < 0 || ty < 0 || tx >= TileCountX() || ty >= TileCountY())
            return;
        _hoverTile = new Vector2I(tx, ty);
    }

    private Vector3 TileCenter(int tx, int ty)
    {
        Vector3 c00 = CornerWorldPos(tx, ty);
        Vector3 c11 = CornerWorldPos(tx + 1, ty + 1);
        Vector3 mid = (c00 + c11) * 0.5f;
        mid.Y = Mathf.Max(c00.Y, c11.Y) + LabelYBias;
        return mid;
    }

    private void UpdateNearLabels(Camera3D camera)
    {
        if (_labelsRoot == null)
            return;
        _activeKeys.Clear();
        Vector3 camPos = camera.GlobalPosition;
        int tilesX = TileCountX();
        int tilesY = TileCountY();
        float near2 = LabelNear * LabelNear;
        Vector2 centerOffset = _map!.Get("center_offset").AsVector2();
        int minTx = (int)Mathf.Floor((camPos.X - LabelNear - centerOffset.X) / TileSize) - 1;
        int maxTx = (int)Mathf.Floor((camPos.X + LabelNear - centerOffset.X) / TileSize) + 1;
        int minTy = (int)Mathf.Floor((camPos.Z - LabelNear - centerOffset.Y) / TileSize) - 1;
        int maxTy = (int)Mathf.Floor((camPos.Z + LabelNear - centerOffset.Y) / TileSize) + 1;
        minTx = Mathf.Clamp(minTx, 0, tilesX - 1);
        maxTx = Mathf.Clamp(maxTx, 0, tilesX - 1);
        minTy = Mathf.Clamp(minTy, 0, tilesY - 1);
        maxTy = Mathf.Clamp(maxTy, 0, tilesY - 1);

        for (int ty = minTy; ty <= maxTy; ty++)
        {
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                Vector3 mid = TileCenter(tx, ty);
                if (camPos.DistanceSquaredTo(mid) > near2)
                    continue;
                if (!camera.IsPositionInFrustum(mid))
                    continue;
                string key = $"{tx},{ty}";
                _activeKeys[key] = true;
                if (!_labelPool.TryGetValue(key, out Label3D? lab) || !IsInstanceValid(lab))
                {
                    lab = MakeTileLabel(key);
                    _labelPool[key] = lab;
                }
                lab.Visible = true;
                lab.Position = mid;
            }
        }

        foreach (string key in _labelPool.Keys)
        {
            if (_activeKeys.ContainsKey(key))
                continue;
            Label3D lab = _labelPool[key];
            if (lab != null && IsInstanceValid(lab))
                lab.Visible = false;
        }
    }

    private Label3D MakeTileLabel(string text)
    {
        var lab = new Label3D
        {
            Text = text,
            FontSize = 48,
            PixelSize = 0.45f,
            Modulate = new Color(1.0f, 0.95f, 0.2f, 1.0f),
            OutlineModulate = new Color(0, 0, 0, 1),
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Visible = false,
        };
        _labelsRoot!.AddChild(lab);
        return lab;
    }

    private void HideAllLabels()
    {
        foreach (string key in _labelPool.Keys)
        {
            Label3D lab = _labelPool[key];
            if (lab != null && IsInstanceValid(lab))
                lab.Visible = false;
        }
        _activeKeys.Clear();
    }

    private int TileCountX() => _map!.Call("tile_count_x").AsInt32();
    private int TileCountY() => _map!.Call("tile_count_y").AsInt32();
    private Vector3 CornerWorldPos(int x, int y) => _map!.Call("corner_world_pos", x, y).AsVector3();
}
