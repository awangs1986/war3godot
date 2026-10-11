// 【测试算法 / 1:1 HiveWE】2026-07-22 — 已封口
// 实测 Raise @ (32,32) size=4 Δ=0.5：存盘后 4225 角点 GroundHeight 与 HiveWE 全等
// 备份：TerrainBrush.cs.pre_hivewe_test.bak / TerrainBrush.cs.before_1to1.bak
//       D:\temp\cursor\w3editor\algo_backup\
using W3Editor.Formats;

namespace W3Editor.Services;

/// <summary>
/// Brush tool set — 1:1 behavioural port of HiveWE terrain operators.
/// </summary>
public enum BrushOp
{
    None,
    Texture,
    Raise,
    Lower,
    Plateau,
    Smooth,
    /// <summary>HiveWE exposes this deformation but its current operator is a no-op.</summary>
    Ripple,
    CliffLower2,
    CliffLower1,
    CliffLevel,
    CliffRaise1,
    CliffRaise2,
    WaterShallow,
    WaterDeep,
    Ramp,
    BoundaryAdd,
    BoundaryRemove,
    /// <summary>Internal: right-button erase for the water tools (not a palette entry).</summary>
    WaterErase,
}

/// <summary>HiveWE <c>Brush::Shape</c>.</summary>
public enum BrushShape
{
    Circle,
    Square,
    Diamond,
}

public sealed class BrushSettings
{
    public BrushOp Op = BrushOp.None;
    /// <summary>
    /// HiveWE UI brush size (after <c>set_size</c> divide by size_granularity=4).
    /// Footprint in corners = this value; internal size.x = Radius * 4.
    /// </summary>
    public int Radius = 4;
    /// <summary>
    /// Unused by HiveWE height path (HiveWE uses frame_delta only).
    /// Kept for MCP: when calling Apply without timed input, pass as frameDelta.
    /// </summary>
    public float Strength = 0.6f;
    public int TextureIndex;
    public BrushShape Shape = BrushShape.Circle;
    /// <summary>HiveWE <c>CliffOperator::cliff_id</c> written into corner cliff texture.</summary>
    public int CliffTextureId;
}

/// <summary>
/// Per-stroke state. HiveWE computes stroke targets once in apply_begin from
/// the integer center of the clipped initial brush area; stamps then reuse them.
/// </summary>
public sealed class BrushStroke
{
    public bool Initialized;
    public float PlateauGround;
    public float PlateauWater;
    public int TargetLayer;
}

/// <summary>
/// 【测试算法 / TEST ALGORITHM — HiveWE 1:1 — 2026-07-22】
/// Corner brush ops matching HiveWE:
/// <list type="bullet">
/// <item>size_granularity=4; area = UI size; falloff denom = UI*4</item>
/// <item>get_unclipped_pos corner: floor(mouse - size/2 + 1)</item>
/// <item>Brush::contains circle/square/diamond</item>
/// <item>Raise/Lower: falloff * frame_delta (press 0.5, drag dt)</item>
/// <item>Smooth / cliff / ramp / texture blight-near-cliff gates</item>
/// </list>
/// Warsmash has no terrain brush — HiveWE is authoritative.
/// </summary>
public static class TerrainBrush
{
    public const int SizeGranularity = 4;
    private const float MinHeight = -16f;
    private const float MaxHeight = 15.99f;
    private const int MaxLayer = 15;
    private const float WaterOffset = -0.7f;

    private static float FinalGround(W3Corner c) => c.GroundHeight + c.LayerHeight - 2f;
    private static float FinalWater(W3Corner c) => c.WaterHeight + WaterOffset;

    /// <summary>
    /// One HiveWE <c>TerrainBrush::apply(frame_delta)</c> at mouse corner (cx, cy).
    /// Press should pass <paramref name="frameDelta"/> = 0.5; drag passes real dt.
    /// </summary>
    public static void Apply(W3EMap map, int cx, int cy, BrushSettings s, bool erase, BrushStroke stroke,
        float frameDelta = 0.5f) => Apply(map, (float)cx, (float)cy, s, erase, stroke, frameDelta);

    /// <summary>Preserves HiveWE mouse_world fractions for footprint and ramp direction.</summary>
    public static void Apply(
        W3EMap map, float cx, float cy, BrushSettings s, bool erase, BrushStroke stroke,
        float frameDelta = 0.5f)
    {
        if (!float.IsFinite(cx) || !float.IsFinite(cy)) return;
        BrushOp op = Reverse(s.Op, erase);
        if (op == BrushOp.None)
            return;

        int uiSize = Math.Clamp(s.Radius, 1, 32);
        int internalSize = uiSize * SizeGranularity;
        // TerrainBrush::get_unclipped_pos (corner type)
        int posX = (int)MathF.Floor(cx - uiSize / 2f + 1f);
        int posY = (int)MathF.Floor(cy - uiSize / 2f + 1f);
        int areaX = Math.Max(0, posX);
        int areaY = Math.Max(0, posY);
        int areaR = Math.Min(map.Width, posX + uiSize);
        int areaB = Math.Min(map.Height, posY + uiSize);
        if (areaR <= areaX || areaB <= areaY)
            return;

        int adjX = Math.Min(posX, 0);
        int adjY = Math.Min(posY, 0);
        // HiveWE stores these in int (float expression truncates toward zero).
        int centerX = (int)(areaX + (areaR - areaX) * 0.5f);
        int centerY = (int)(areaY + (areaB - areaY) * 0.5f);
        if (!stroke.Initialized)
        {
            InitializeStroke(map, centerX, centerY, op, stroke);
            stroke.Initialized = true;
        }

        bool Inside(int i, int j)
        {
            int lx = i - areaX - adjX;
            int ly = j - areaY - adjY;
            return Contains(lx, ly, uiSize, s.Shape);
        }

        switch (op)
        {
            case BrushOp.Texture:
                ApplyTexture(map, areaX, areaY, areaR, areaB, Inside, s.TextureIndex, erase);
                break;
            case BrushOp.Raise:
            case BrushOp.Lower:
            case BrushOp.Plateau:
            case BrushOp.Smooth:
            case BrushOp.Ripple:
                ApplyHeight(map, areaX, areaY, areaR, areaB, Inside, centerX, centerY,
                    internalSize, op, frameDelta, stroke);
                break;
            case BrushOp.Ramp:
                if (erase)
                    ApplyRampErase(map, areaX, areaY, areaR, areaB, Inside);
                else
                    ApplyRamps(map, areaX, areaY, areaR, areaB, Inside, cx, cy);
                break;
            case BrushOp.WaterErase:
                ApplyWaterErase(map, areaX, areaY, areaR, areaB, Inside);
                break;
            case BrushOp.BoundaryAdd:
            case BrushOp.BoundaryRemove:
                ApplyBoundary(map, areaX, areaY, areaR, areaB, Inside, op == BrushOp.BoundaryAdd);
                break;
            default:
                ApplyCliffs(map, areaX, areaY, areaR, areaB, Inside, posX, posY, uiSize, s, op, stroke);
                break;
        }
    }

    /// <summary>HiveWE <c>Brush::contains</c> — args in brush-local UI units.</summary>
    public static bool Contains(int posX, int posY, int uiSize, BrushShape shape)
    {
        int half = uiSize / 2;
        return shape switch
        {
            BrushShape.Square => true,
            BrushShape.Diamond => Math.Abs(posX - half) + Math.Abs(posY - half) <= half,
            _ => (posX - half) * (posX - half) + (posY - half) * (posY - half) <= half * half,
        };
    }

    private static BrushOp Reverse(BrushOp op, bool erase)
    {
        if (!erase)
            return op;
        return op switch
        {
            BrushOp.Raise => BrushOp.Lower,
            BrushOp.Lower => BrushOp.Raise,
            BrushOp.CliffRaise1 => BrushOp.CliffLower1,
            BrushOp.CliffRaise2 => BrushOp.CliffLower2,
            BrushOp.CliffLower1 => BrushOp.CliffRaise1,
            BrushOp.CliffLower2 => BrushOp.CliffRaise2,
            BrushOp.WaterShallow => BrushOp.WaterErase,
            BrushOp.WaterDeep => BrushOp.WaterErase,
            BrushOp.BoundaryAdd => BrushOp.BoundaryRemove,
            BrushOp.BoundaryRemove => BrushOp.BoundaryAdd,
            _ => op,
        };
    }

    private static void InitializeStroke(W3EMap map, int cx, int cy, BrushOp op, BrushStroke stroke)
    {
        var center = map.GetCorner(Math.Clamp(cx, 0, map.Width - 1), Math.Clamp(cy, 0, map.Height - 1));
        if (center == null)
            return;
        stroke.PlateauGround = center.GroundHeight;
        stroke.PlateauWater = center.WaterHeight;

        int layer = center.LayerHeight;
        switch (op)
        {
            case BrushOp.CliffLower1: layer -= 1; break;
            case BrushOp.CliffLower2: layer -= 2; break;
            case BrushOp.CliffRaise1: layer += 1; break;
            case BrushOp.CliffRaise2: layer += 2; break;
            case BrushOp.CliffLevel: break;
            case BrushOp.WaterShallow:
                if (!center.Water) layer -= 1;
                else if (FinalWater(center) > FinalGround(center) + 1f) layer += 1;
                break;
            case BrushOp.WaterDeep:
                if (!center.Water) layer -= 2;
                else if (FinalWater(center) < FinalGround(center) + 1f) layer -= 1;
                break;
        }
        stroke.TargetLayer = Math.Clamp(layer, 0, MaxLayer);
    }

    // ---- TextureOperator ----

    private static void ApplyTexture(
        W3EMap map, int ax, int ay, int ar, int ab,
        Func<int, int, bool> inside, int textureIndex, bool erase)
    {
        if (erase || textureIndex < 0 || textureIndex >= map.GroundTilesets.Count)
            return;
        for (int j = ay; j < ab; j++)
        {
            for (int i = ax; i < ar; i++)
            {
                if (!inside(i, j)) continue;
                var c = map.GetCorner(i, j)!;
                // HiveWE blight branch: skip when cliff near. We have no blight tile id
                // in the palette path — always write ground texture (non-blight path).
                if (CliffNear(map, i, j))
                {
                    // Still allow non-blight paint (HiveWE only skips when painting blight).
                }
                c.Blight = false;
                c.GroundTexture = textureIndex;
                c.GroundVariation = W3EMap.RandomGroundVariation(Random.Shared);
            }
        }
    }

    private static bool CliffNear(W3EMap map, int i, int j)
    {
        for (int l = -1; l <= 1; l++)
        for (int k = -1; k <= 1; k++)
        {
            int x = i + k, y = j + l;
            if (x < 0 || y < 0 || x >= map.Width - 1 || y >= map.Height - 1) continue;
            if (TileIsCliff(map, x, y)) return true;
        }
        return false;
    }

    private static bool TileIsCliff(W3EMap map, int i, int j)
    {
        int bl = map.GetCorner(i, j)!.LayerHeight;
        int br = map.GetCorner(i + 1, j)!.LayerHeight;
        int tl = map.GetCorner(i, j + 1)!.LayerHeight;
        int tr = map.GetCorner(i + 1, j + 1)!.LayerHeight;
        return bl != br || bl != tl || bl != tr;
    }

    // ---- HeightOperator ----

    private static void ApplyHeight(
        W3EMap map, int ax, int ay, int ar, int ab,
        Func<int, int, bool> inside, int centerX, int centerY,
        int internalSize, BrushOp op, float frameDelta, BrushStroke stroke)
    {
        int areaW = ar - ax;
        int areaH = ab - ay;
        if (op == BrushOp.Smooth)
        {
            ApplySmoothHiveWe(map, ax, ay, ar, ab, areaW, areaH, inside);
            return;
        }

        for (int j = ay; j < ab; j++)
        {
            for (int i = ax; i < ar; i++)
            {
                if (!inside(i, j)) continue;
                var c = map.GetCorner(i, j)!;
                switch (op)
                {
                    case BrushOp.Ripple:
                        // HiveWE terrain_operators.cpp currently leaves the
                        // ripple branch empty; preserve that authoritative
                        // behavior until its operator gains semantics.
                        break;
                    case BrushOp.Raise:
                    case BrushOp.Lower:
                    {
                        // delta = max(0, 1 - distance / brush->size.x * √2) * frame_delta
                        double distance = Math.Sqrt((centerX - i) * (centerX - i) + (centerY - j) * (centerY - j));
                        double delta = Math.Max(0.0, 1.0 - distance / internalSize * Math.Sqrt(2.0)) * frameDelta;
                        float h = c.GroundHeight + (float)(op == BrushOp.Raise ? delta : -delta);
                        c.GroundHeight = Math.Clamp(h, MinHeight, MaxHeight);
                        break;
                    }
                    case BrushOp.Plateau:
                        c.GroundHeight = Math.Clamp(stroke.PlateauGround, MinHeight, MaxHeight);
                        break;
                }
            }
        }
    }

    private static void ApplyBoundary(
        W3EMap map, int ax, int ay, int ar, int ab,
        Func<int, int, bool> inside, bool add)
    {
        for (int j = ay; j < ab; j++)
        for (int i = ax; i < ar; i++)
        {
            if (inside(i, j))
                map.GetCorner(i, j)!.Boundary = add;
        }
    }

    /// <summary>HiveWE HeightOperator::smooth (ground only; deform_water default false).</summary>
    private static void ApplySmoothHiveWe(
        W3EMap map, int ax, int ay, int ar, int ab, int areaW, int areaH,
        Func<int, int, bool> inside)
    {
        var heights = new float[areaW * areaH];
        for (int j = ay; j < ab; j++)
        for (int i = ax; i < ar; i++)
            heights[(i - ax) * areaH + (j - ay)] = map.GetCorner(i, j)!.GroundHeight;

        for (int j = ay; j < ab; j++)
        {
            for (int i = ax; i < ar; i++)
            {
                if (!inside(i, j)) continue;
                var c = map.GetCorner(i, j)!;
                float current = c.GroundHeight;
                float accumulate = 0f;
                int x0 = Math.Max(0, i - 1), x1 = Math.Min(map.Width - 1, i + 1);
                int y0 = Math.Max(0, j - 1), y1 = Math.Min(map.Height - 1, j + 1);
                int cells = (x1 - x0 + 1) * (y1 - y0 + 1);
                for (int l = y0; l <= y1; l++)
                {
                    for (int k = x0; k <= x1; k++)
                    {
                        // HiveWE progressive scan quirk preserved 1:1
                        if ((k < i || l < j) && k <= i
                            && k - ax >= 0 && l - ay >= 0 && k < ar && l < ab)
                            accumulate += heights[(k - ax) * areaH + (l - ay)];
                        else
                            accumulate += map.GetCorner(k, l)!.GroundHeight;
                    }
                }
                accumulate -= current;
                float next = 0.97f * current + 0.03f * (accumulate / Math.Max(1, cells - 1));
                c.GroundHeight = Math.Clamp(next, MinHeight, MaxHeight);
            }
        }
    }

    // ---- CliffOperator ----

    private static void ApplyCliffs(
        W3EMap map, int ax, int ay, int ar, int ab,
        Func<int, int, bool> inside, int posX, int posY, int uiSize,
        BrushSettings s, BrushOp op, BrushStroke stroke)
    {
        var seed = new Queue<(int X, int Y)>();
        for (int j = ay; j < ab; j++)
        {
            for (int i = ax; i < ar; i++)
            {
                if (!inside(i, j)) continue;
                var c = map.GetCorner(i, j)!;
                c.Ramp = false;
                c.LayerHeight = stroke.TargetLayer;
                c.CliffTexture = s.CliffTextureId;
                switch (op)
                {
                    case BrushOp.WaterShallow:
                        c.Water = true;
                        c.WaterHeight = c.LayerHeight - 1;
                        break;
                    case BrushOp.WaterDeep:
                        c.Water = true;
                        c.WaterHeight = c.LayerHeight;
                        break;
                    default:
                        if (c.Water && FinalWater(c) < FinalGround(c))
                            c.Water = false;
                        break;
                }
                seed.Enqueue((i, j));
            }
        }
        // HiveWE check_nearby propagation (BFS equivalent of its recursion)
        while (seed.Count > 0)
        {
            var (i, j) = seed.Dequeue();
            PropagateCliffNearby(map, posX, posY, uiSize, s.Shape, i, j, seed);
        }

        // HiveWE: placing cliffs clears blight on the tile's four corners
        int ex0 = Math.Max(0, ax - 1), ey0 = Math.Max(0, ay - 1);
        int ex1 = Math.Min(map.Width - 2, ar), ey1 = Math.Min(map.Height - 2, ab);
        for (int i = ex0; i <= ex1; i++)
        {
            for (int j = ey0; j <= ey1; j++)
            {
                if (!TileIsCliff(map, i, j)) continue;
                map.GetCorner(i, j)!.Blight = false;
                map.GetCorner(i + 1, j)!.Blight = false;
                map.GetCorner(i, j + 1)!.Blight = false;
                map.GetCorner(i + 1, j + 1)!.Blight = false;
            }
        }
    }

    /// <summary>
    /// One step of HiveWE <c>CliffOperator::check_nearby</c>.
    /// Contains skip uses HiveWE's coordinates as written (incl. <c>l-k</c>).
    /// </summary>
    private static void PropagateCliffNearby(
        W3EMap map, int begx, int begy, int uiSize, BrushShape shape,
        int i, int j, Queue<(int X, int Y)> outQueue)
    {
        int x0 = Math.Max(0, i - 1), x1 = Math.Min(map.Width - 1, i + 1);
        int y0 = Math.Max(0, j - 1), y1 = Math.Min(map.Height - 1, j + 1);
        int srcLayer = map.GetCorner(i, j)!.LayerHeight;
        int cliffTex = map.GetCorner(i, j)!.CliffTexture;
        for (int l = y0; l <= y1; l++)
        {
            for (int k = x0; k <= x1; k++)
            {
                map.GetCorner(k, l)!.CliffTexture = cliffTex;
                if (k == 0 && l == 0) continue;

                int difference = srcLayer - map.GetCorner(k, l)!.LayerHeight;
                int containsX = begx + (k - i);
                int containsY = begy + (l - k);
                if (Math.Abs(difference) > 2 && !Contains(containsX, containsY, uiSize, shape))
                {
                    var c = map.GetCorner(k, l)!;
                    int next = Math.Clamp(srcLayer - Math.Clamp(difference, -2, 2), 0, MaxLayer);
                    if (c.LayerHeight == next && !c.Ramp) continue;
                    c.LayerHeight = next;
                    c.Ramp = false;
                    outQueue.Enqueue((k, l));
                }
            }
        }
    }

    private static void ApplyWaterErase(
        W3EMap map, int ax, int ay, int ar, int ab, Func<int, int, bool> inside)
    {
        for (int j = ay; j < ab; j++)
        for (int i = ax; i < ar; i++)
        {
            if (!inside(i, j)) continue;
            var c = map.GetCorner(i, j)!;
            c.Water = false;
            c.WaterHeight = 0f;
        }
    }

    // ---- ramps ----

    private static void ApplyRamps(
        W3EMap map, int ax, int ay, int ar, int ab,
        Func<int, int, bool> inside, float mouseCx, float mouseCy)
    {
        // HiveWE: horizontal = (mouse_pos.x > i) - (mouse_pos.x < i)
        for (int j = ay; j < ab; j++)
        for (int i = ax; i < ar; i++)
        {
            if (!inside(i, j)) continue;
            int horizontal = (mouseCx > i ? 1 : 0) - (mouseCx < i ? 1 : 0);
            int vertical = (mouseCy > j ? 1 : 0) - (mouseCy < j ? 1 : 0);
            UpdateRamp(map, i, j, horizontal, vertical);
        }
    }

    private static void ApplyRampErase(
        W3EMap map, int ax, int ay, int ar, int ab, Func<int, int, bool> inside)
    {
        for (int j = ay; j < ab; j++)
        for (int i = ax; i < ar; i++)
        {
            if (!inside(i, j)) continue;
            map.GetCorner(i, j)!.Ramp = false;
        }
    }

    /// <summary>HiveWE <c>CliffOperator::update_ramp</c>.</summary>
    private static void UpdateRamp(W3EMap map, int i, int j, int horizontal, int vertical)
    {
        var origin = map.GetCorner(i, j);
        if (origin == null) return;
        int originLevel = origin.LayerHeight;
        int targetLevel = originLevel - 1;
        int cliffTex = origin.CliffTexture;

        int Layer(int x, int y) => map.GetCorner(x, y)?.LayerHeight ?? int.MinValue;
        bool RampAt(int x, int y) => map.GetCorner(x, y)?.Ramp ?? false;
        bool Valid(int x, int y) =>
            x >= 0 && x < map.Width && y >= 0 && y < map.Height
            && map.GetCorner(x, y)!.LayerHeight == targetLevel;

        bool CheckRampDirection(int dirX, int dirY)
        {
            if (i + 2 * dirX < 0 || i + 2 * dirX > map.Width || j + 2 * dirY < 0 || j + 2 * dirY > map.Height)
                return false;
            for (int step = 1; step <= 2; step++)
                if (!Valid(i + step * dirX, j + step * dirY))
                    return false;
            if (Layer(i + dirY, j + dirX) > originLevel || Layer(i - dirY, j - dirX) > originLevel)
                return false;
            foreach (int side in new[] { -1, 1 })
            {
                if (RampAt(i, j + side)
                    && (!RampAt(i + dirX, j + side + dirY) || !RampAt(i + 2 * dirX, j + side + 2 * dirY)))
                    return false;
                if (RampAt(i + side, j)
                    && (!RampAt(i + side + dirX, j + dirY) || !RampAt(i + side + 2 * dirX, j + 2 * dirY)))
                    return false;
            }
            return true;
        }

        bool allowH = horizontal != 0 && CheckRampDirection(horizontal, 0);
        bool allowV = vertical != 0 && CheckRampDirection(0, vertical);
        bool allowD = horizontal != 0 && vertical != 0;
        for (int dx = 0; dx <= 2 && allowD; dx++)
        for (int dy = 0; dy <= 2 && allowD; dy++)
            if ((dx != 0 || dy != 0) && !Valid(i + dx * horizontal, j + dy * vertical))
                allowD = false;

        void SetRamp(int x, int y)
        {
            var c = map.GetCorner(x, y);
            if (c == null) return;
            c.Ramp = true;
            c.CliffTexture = cliffTex;
        }

        if (allowH)
            for (int step = 0; step <= 2; step++)
                SetRamp(i + step * horizontal, j);
        if (allowV)
            for (int step = 0; step <= 2; step++)
                SetRamp(i, j + step * vertical);
        if (allowD)
            for (int dx = 0; dx <= 2; dx++)
            for (int dy = 0; dy <= 2; dy++)
                SetRamp(i + dx * horizontal, j + dy * vertical);

        // HiveWE L-corner completion adds only the flag. These corners keep
        // their own authored cliff texture (sealed ground-texture ownership).
        if (allowH || allowD)
        {
            if (Valid(i + horizontal, j + 1) && RampAt(i, j) && RampAt(i + 1, j) && RampAt(i + 2, j)
                && RampAt(i, j + 1) && RampAt(i, j + 2))
                map.GetCorner(i + horizontal, j + 1)!.Ramp = true;
            if (Valid(i + horizontal, j - 1) && RampAt(i, j) && RampAt(i + 1, j) && RampAt(i + 2, j)
                && RampAt(i, j - 1) && RampAt(i, j - 2))
                map.GetCorner(i + horizontal, j - 1)!.Ramp = true;
        }
        if (allowV || allowD)
        {
            if (Valid(i + 1, j + vertical) && RampAt(i, j) && RampAt(i, j + 1) && RampAt(i, j + 2)
                && RampAt(i + 1, j) && RampAt(i + 2, j))
                map.GetCorner(i + 1, j + vertical)!.Ramp = true;
            if (Valid(i - 1, j + vertical) && RampAt(i, j) && RampAt(i, j + 1) && RampAt(i, j + 2)
                && RampAt(i - 1, j) && RampAt(i - 2, j))
                map.GetCorner(i - 1, j + vertical)!.Ramp = true;
        }
    }
}
