// 【官方 1:1】2026-10-15 — 由 2026-07-22 的 "1:1 HiveWE 封口版" 迁移而来
//
// 依据（详见 docs/official-terrain-algorithms.md，ch0-12 + §12.15 修正清单）：
//   · 1.27 源码逆向：CTerrainHeight 0x57e700/0x581460/0x57e3e0→0x57e440/0x57f8f0、
//     CTerrainCliff 0x574470/0x574a50/0x5748c0/0x573990、CTerrainWater 0x603cc0、
//     Ramp 0x583310/0x584270（地址对照 docs/classic-we-rea-comparison.md）
//   · 1.32 存档黑盒对照：docs/terrain-reference（45 个 fixture 用例，逐角点验证）
//   核心认识：1:1 HiveWE ≠ 1:1 官方。HiveWE 的连续 frameDelta 模型、线性衰减、
//   8 邻均匀平滑、无坡度钳制等全部换成官方离散单次施加 + 流水线模型。
//
// 官方模型（本文件实现的全部要点）：
//   · 尺寸档位 1/2/3/5/8，半径 R = size−1（角点）；圆形 d²≤R²，方形 |dx|,|dy|≤R
//   · 量化 raw = trunc((world+2048)·4)；512 raw = 1 层 = 128 world（float32 精度）
//   · 高度流水线：①先算完（用施加前值）②量化写回 ③SlopeClamp（MaxSlope 50° +
//     MaxHeight 1536 / MinHeight −128，递归 8 邻）④记录改动
//   · Raise/Lower：Δworld = size·6.4·cos(πd/2R)，强制圆形，每次施加只加一次
//   · Plateau：按下捕获 h0（this+0x948），覆盖集合全写 h0，遵守形状
//   · Noise：Δ = (rand%21−10)·6.4 world（0x581460，n = int(128/6.4)+1 = 21）
//   · Smooth：核 [1,2,1;2,0,2;1,2,1]，new = W1·h + W2·Σ(k·h邻)/Σk，
//     W1/W2 = 0.80/0.20（Shift 1.00/0.05），快照式（先算完再写）
//   · Cliff：T = L(按下点)+Δ 按下时捕获、拖动全程用；T 越出 [0,15] → 整笔拒绝；
//     覆盖集合写 L=T + 悬崖类型 + 随机变体；3×3 外扩染类型；层差传播（8 邻 ±2，
//     改动点写类型+变体，递归）
//   · 浅水/深水：写水旗+水面高度（覆盖集合，水面外扩一圈）→ 整平 size+2 到 h0 →
//     悬崖 size 到 T；干地 Δ=−1/−2，已有水按 水面−地面总高 计算
//   · Ramp：只响应点击角点；4 邻恰有一个高 1 层 → 写 3 角（c 外侧、c、高侧）；
//     无 4 邻更高且恰有一个斜邻高 1 层 → 3×3；其余不动（层级不变，只写 ramp 标志）
//   · Texture：覆盖集合写纹理下标 + 重随变体（约 91% 落 {0,4,8,12,16,17}，
//     其余均匀 1..15）；不清 blight（官方行为，与旧 HiveWE 版不同）
//   · Blight：覆盖集合写 blight 旗（水角点不接收）+ 外围约 R+2 随机改贴图
//   · Boundary：覆盖集合写/清 boundary 旗
//
// 未验证项（与参考实现同等诚实）：
//   · 1.32 静态代码段磁盘加密（.text 熵 8.000），一切 1.32 行为只能黑盒存档验证
//   · 拖拽中多次施加的叠加细节（本实现：一次 Apply 调用 = 一次官方完整施加）
//   · 右键语义（本实现沿用 Raise↔Lower 等反转映射，无存档对照）
//   · 悬崖按水夹层 0x5764a0（"夹后值改写 T" 怪癖）、水相关高度下限、
//     悬崖后高度修正 0x57f270 → 均未实现（参考实现也未实现）
//   · DeepExtra（this+0x31c）常数未读出，取 128 world
//   · 官方噪声随机序列不可复现（只能范围+量化+统计分布对照）
//
// 备份：TerrainBrush.cs.pre_hivewe_test.bak / TerrainBrush.cs.before_1to1.bak
//       D:\temp\cursor\w3editor\algo_backup\
// 验证：standalone/W3EditorOfficialTest（对照 docs/terrain-reference 官方 fixture，
//       逐角点比较，期望值 = 参考实现 results.md 的逐用例数字）
using W3Editor.Formats;

namespace W3Editor.Services;

/// <summary>官方笔刷操作集。Noise/Blight 为 2026-10-15 按 §12.15 新增。</summary>
public enum BrushOp
{
    None,
    Texture,
    Raise,
    Lower,
    Plateau,
    Smooth,
    /// <summary>HiveWE 遗留：官方无对应操作（no-op）。</summary>
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
    /// <summary>噪声（0x581460）：Δ=(rand%21−10)·6.4 world，流水线量化+坡度钳制。</summary>
    Noise,
    /// <summary>荒芜：覆盖集合写 blight 旗（水角点不接收）+ 外围随机贴图。</summary>
    Blight,
}

/// <summary>官方形状：圆 / 方。Diamond 为 HiveWE 遗留，按圆形处理（官方无菱形）。</summary>
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
    /// 官方笔刷档位 1/2/3/5/8（半径 = Radius−1 角点）。兼容任意 1..32。
    /// </summary>
    public int Radius = 4;
    /// <summary>[兼容字段] 官方单次施加模型不使用；保留给旧调用方。</summary>
    public float Strength = 0.6f;
    public int TextureIndex;
    public BrushShape Shape = BrushShape.Circle;
    /// <summary>
    /// 写入角点 cliffTexture 字节的值 = 本图悬崖列表（CliffTilesets）下标（官方 0x574a50 语义）。
    /// </summary>
    public int CliffTextureId;
    /// <summary>Shift 平滑：W1/W2 = 1.00/0.05（默认 0.80/0.20）。</summary>
    public bool Shift = false;
}

/// <summary>
/// 笔画状态：官方在按下时捕获目标（Plateau 的 h0 → this+0x948；Cliff 的 T → this+0x94c），
/// 拖动全程复用。
/// </summary>
public sealed class BrushStroke
{
    public bool Initialized;
    /// <summary>按下时点击点 h 字段的世界高度（world 单位，= GroundHeight·128）。</summary>
    public float PlateauGround;
    /// <summary>[兼容字段] 官方 1.32 水流程不再需要；保留。</summary>
    public float PlateauWater;
    /// <summary>按下时捕获的悬崖目标层 T = L(按下点)+Δ（未钳制，越界时整笔拒绝）。</summary>
    public int TargetLayer;

    /// <summary>
    /// 本笔画已经施加过的角点（键 = y*Width+x）。
    /// 官方实测（FINDINGS R1/R6）：**1 次按下-抬起 = 1 次施加**；同点按住不连发；
    /// 同角点内抖动不额外施加；拖动不按帧累积。⇒ 同一笔画内同一角点只施加一次。
    /// 新的一次点击必须新建 <see cref="BrushStroke"/>，否则会被这里抑制。
    /// </summary>
    public readonly HashSet<int> AppliedCorners = new();
}

/// <summary>
/// 【官方 1:1 / TEST ALGORITHM — 2026-10-15】地形角点笔刷，
/// 1:1 官方编辑器（1.27 源码 + 1.32 存档验证），不是 HiveWE。
/// 一次 <see cref="Apply"/> 调用 = 一次官方完整施加（单次点击语义）。
/// </summary>
public static class TerrainBrush
{
    // ---- 官方常数（1.27 MiscData + 源码逆向）----
    public const int SizeGranularity = 4;   // [兼容字段] 官方无粒度概念，保留
    public const int MaxLayer = 15;         // this+0x988
    public const float MaxSlopeDeg = 50f;   // MiscData MaxSlope
    public const float MaxHeightWorld = 1536f;  // 12 层
    public const float MinHeightWorld = -128f;  // −1 层
    const float StepWorld = 6.4f;               // 0.05 层
    const float WaterOffsetWorldL = -0.7f * 128f;  // L 地形水面偏移（Water.slk）
    const float WaterOffsetWorldA = -0.6f * 128f;  // A 地形（Water.slk）
    const float DeepExtraWorld = 128f;      // this+0x31c【未验证常数，LS03→LS04 只约束 (38.4,166.4)】

    /// <summary>
    /// 随机源：悬崖变体 / 噪声 / 纹理变体共用。官方随机序列不可复现，
    /// 对照测试只比较范围+量化+统计分布；harness 可注入固定种子。
    /// </summary>
    public static Random Rng = new Random();

    // ---- raw 换算（float32 下与 raw 双向精确：512 = 2^9）----
    /// <summary>GroundHeight（层分数）→ 文件 raw 14 位。</summary>
    public static int ToRaw(float groundHeight) => (int)(groundHeight * 512f + 8192f);
    /// <summary>文件 raw → GroundHeight（层分数）。</summary>
    public static float ToLayers(int raw) => (raw - 8192) / 512f;
    /// <summary>世界高度 → raw（官方 ftol：向 0 截断）。</summary>
    public static int ToRawWorld(float world) => (int)((world + 2048f) * 4f);
    /// <summary>角点 h 字段世界高度（= 官方 World(raw)）。</summary>
    public static float WorldOf(W3Corner c) => c.GroundHeight * 128f;

    private static bool Inside(W3EMap m, int x, int y) => x >= 0 && y >= 0 && x < m.Width && y < m.Height;
    private static W3Corner At(W3EMap m, int x, int y) => m.Corners[y * m.Width + x];

    // ================= 公开 API（签名与 HiveWE 版兼容）=================

    public static void Apply(W3EMap map, int cx, int cy, BrushSettings s, bool erase, BrushStroke stroke, float frameDelta = 0.5f)
        => Apply(map, (float)cx, (float)cy, s, erase, stroke, frameDelta);

    /// <summary>
    /// 在鼠标角点 (cx, cy) 施加一次笔刷（官方 = 一次完整点击）。
    /// 中心取最近的角点（官方：中心永远是鼠标下最近的角点，偶数尺寸不偏半格）。
    /// <paramref name="frameDelta"/> 仅为签名兼容：官方单次施加，不按帧累积。
    /// </summary>
    public static void Apply(W3EMap map, float cx, float cy, BrushSettings s, bool erase, BrushStroke stroke, float frameDelta = 0.5f)
    {
        if (map == null || s == null || stroke == null) return;
        BrushOp op = s.Op;
        if (op == BrushOp.None) return;
        if (erase) op = Reverse(op);

        int size = Math.Clamp(s.Radius, 1, 32);
        int ccx = Math.Clamp((int)MathF.Round(cx), 0, map.Width - 1);
        int ccy = Math.Clamp((int)MathF.Round(cy), 0, map.Height - 1);

        if (!stroke.Initialized)
        {
            stroke.Initialized = true;
            InitializeStroke(map, ccx, ccy, op, stroke);
        }
        // 同一笔画内同一角点只施加一次（官方：按下-抬起 1 次施加；抖动/拖动不重复施加）
        if (!stroke.AppliedCorners.Add(ccy * map.Width + ccx)) return;
        Stroke(map, ccx, ccy, size, s, op, stroke);
    }

    /// <summary>右键 = 官方语义的反转（无存档对照，沿用旧映射）。【未测】</summary>
    internal static BrushOp Reverse(BrushOp op) => op switch
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

    /// <summary>按下捕获（官方 this+0x948 / this+0x94c）：拖动全程复用。</summary>
    private static void InitializeStroke(W3EMap map, int cx, int cy, BrushOp op, BrushStroke stroke)
    {
        var c = At(map, cx, cy);
        switch (op)
        {
            case BrushOp.Plateau:
                stroke.PlateauGround = WorldOf(c);
                break;
            case BrushOp.CliffLower1: stroke.TargetLayer = c.LayerHeight - 1; break;
            case BrushOp.CliffLower2: stroke.TargetLayer = c.LayerHeight - 2; break;
            case BrushOp.CliffRaise1: stroke.TargetLayer = c.LayerHeight + 1; break;
            case BrushOp.CliffRaise2: stroke.TargetLayer = c.LayerHeight + 2; break;
            case BrushOp.CliffLevel: stroke.TargetLayer = c.LayerHeight; break;
            default: break;
        }
    }

    // ================= 分派 =================

    private static void Stroke(W3EMap m, int cx, int cy, int size, BrushSettings s, BrushOp op, BrushStroke stroke)
    {
        var shape = s.Shape == BrushShape.Diamond ? BrushShape.Circle : s.Shape;
        switch (op)
        {
            case BrushOp.Raise: RaiseLower(m, cx, cy, size, +1); break;
            case BrushOp.Lower: RaiseLower(m, cx, cy, size, -1); break;
            case BrushOp.Plateau: Plateau(m, cx, cy, size, shape, stroke.PlateauGround); break;
            case BrushOp.Smooth: Smooth(m, cx, cy, size, shape, s.Shift); break;
            case BrushOp.Noise: Noise(m, cx, cy, size, shape); break;
            case BrushOp.Ripple: break;  // [兼容] 官方无对应
            case BrushOp.CliffLower1:
            case BrushOp.CliffLower2:
            case BrushOp.CliffLevel:
            case BrushOp.CliffRaise1:
            case BrushOp.CliffRaise2:
                Cliff(m, cx, cy, size, shape, stroke.TargetLayer, s.CliffTextureId);
                break;
            case BrushOp.WaterShallow: Water(m, cx, cy, size, 1, s.CliffTextureId); break;
            case BrushOp.WaterDeep: Water(m, cx, cy, size, 2, s.CliffTextureId); break;
            case BrushOp.WaterErase: break;  // 【未测】右键消水无存档对照
            case BrushOp.Ramp: Ramp(m, cx, cy, s.Shift); break;
            case BrushOp.Texture: Texture(m, cx, cy, size, shape, s); break;
            case BrushOp.Blight: Blight(m, cx, cy, size, shape); break;
            case BrushOp.BoundaryAdd: Boundary(m, cx, cy, size, shape, add: true); break;
            case BrushOp.BoundaryRemove: Boundary(m, cx, cy, size, shape, add: false); break;
            default: break;
        }
    }

    // ================= 覆盖集合（官方 Footprint）=================

    /// <summary>半径 R = size−1；圆形 dx²+dy²≤R²，方形 max(|dx|,|dy|)≤R。</summary>
    internal static IEnumerable<(int dx, int dy)> Offsets(int size, BrushShape shape)
    {
        int r = size - 1;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (shape == BrushShape.Square || dx * dx + dy * dy <= r * r)
                    yield return (dx, dy);
    }

    private static HashSet<(int dx, int dy)> Dilate(IEnumerable<(int dx, int dy)> set)
    {
        var o = new HashSet<(int, int)>();
        foreach (var (dx, dy) in set)
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    o.Add((dx + i, dy + j));
        return o;
    }

    internal static double Dist(int dx, int dy) => Math.Sqrt(dx * dx + dy * dy);

    // ================= 高度组（流水线 ①②③④）=================

    /// <summary>
    /// ①对覆盖集合每个点算新世界高度（float32，用施加前值，先算完再写）；
    /// ②量化 raw = trunc((world+2048)·4) 全部写回；③SlopeClamp；
    /// ④（改动记录交给调用方自己的 dirty 跟踪）。
    /// </summary>
    internal static void ApplyHeight(W3EMap m, IEnumerable<(int x, int y)> cover, Func<int, int, float?> compute)
    {
        var buf = new List<(int x, int y, int raw)>();
        foreach (var (x, y) in cover)
        {
            if (!Inside(m, x, y)) continue;
            float? w = compute(x, y);
            if (w is null) continue;
            buf.Add((x, y, ToRawWorld(w.Value)));
        }
        foreach (var (x, y, raw) in buf)
            At(m, x, y).GroundHeight = ToLayers(raw);
        if (buf.Count > 0)
            SlopeClamp(m, buf.Select(b => (b.x, b.y)).ToList());
    }

    /// <summary>
    /// 0x57e3e0 → 0x57e600 → 0x57e440/0x57f8f0 坡度钳制。
    /// 区间 = [MinHeight−off, MaxHeight−off] ∩ ⋂邻居 [h邻∓S]，off = (L−2)·128，下限再取 min(·,0)；
    /// S = tan50°·128（正交）/ tan50°·181.02（对角）。MaxSlope ≥ 85 时跳过坡度部分。
    /// 扫描：改动区包围矩形外扩一圈，x 外层 y 内层；越界夹回并递归 8 邻（深度防重入）。
    /// 未实现：水相关下限（0x51dcb0 / 0x605fc0 / 0x605020）。
    /// </summary>
    internal static void SlopeClamp(W3EMap m, List<(int x, int y)> seeds)
    {
        if (seeds.Count == 0) return;
        float tan = (float)Math.Tan(MaxSlopeDeg * Math.PI / 180.0);
        var depth = new Dictionary<(int, int), int>();

        void Visit(int x, int y, int d)
        {
            if (!Inside(m, x, y)) return;
            if (depth.TryGetValue((x, y), out int seen) && seen <= d) return;
            var c = At(m, x, y);
            float off = (c.LayerHeight - 2) * 128f;
            float hi = MaxHeightWorld - off, lo = Math.Min(MinHeightWorld - off, 0f);
            if (MaxSlopeDeg < 85f)
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        if ((i == 0 && j == 0) || !Inside(m, x + i, y + j)) continue;
                        float s = tan * (i != 0 && j != 0 ? 181.02f : 128f);
                        float hn = WorldOf(At(m, x + i, y + j));
                        hi = Math.Min(hi, hn + s);
                        lo = Math.Max(lo, hn - s);
                    }
            if (hi < lo) (lo, hi) = (hi, lo);
            float w = WorldOf(c);
            if (w >= lo && w <= hi) return;
            c.GroundHeight = ToLayers(ToRawWorld(w < lo ? lo : hi));
            depth[(x, y)] = d + 1;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    if (i != 0 || j != 0) Visit(x + i, y + j, d + 1);
        }

        int x0 = seeds.Min(p => p.x) - 1, x1 = seeds.Max(p => p.x) + 1;
        int y0 = seeds.Min(p => p.y) - 1, y1 = seeds.Max(p => p.y) + 1;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                Visit(x, y, 0);
    }

    // ---------------- 升高 / 降低 ----------------
    /// <summary>
    /// Δworld = size·6.4·cos(π/2·d/R)，只取 d &lt; R；强制圆形（0x57e700 bVar22）。
    /// 每次施加只加一次（不按帧累积）。量化后 +76.8→+76、−76.8→−77。
    /// </summary>
    internal static void RaiseLower(W3EMap m, int cx, int cy, int size, int sign)
    {
        int r = size - 1;
        ApplyHeight(m, Offsets(size, BrushShape.Circle).Select(o => (cx + o.dx, cy + o.dy)), (x, y) =>
        {
            double d = Dist(x - cx, y - cy);
            if (r > 0 && d >= r) return null;   // d = R 处 cos = 0
            float f = r == 0 ? 1f : (float)Math.Cos(Math.PI * d / (2 * r));
            return WorldOf(At(m, x, y)) + sign * size * StepWorld * f;
        });
    }

    // ---------------- 整平 ----------------
    /// <summary>
    /// 覆盖集合全写 h0（按下捕获，this+0x948），无衰减，遵守形状。
    /// 边缘一圈 h0−611 是流水线③坡度钳制的结果，不是笔刷本身。
    /// </summary>
    internal static void Plateau(W3EMap m, int cx, int cy, int size, BrushShape shape, float h0World)
        => ApplyHeight(m, Offsets(size, shape).Select(o => (cx + o.dx, cy + o.dy)), (_, _) => h0World);

    // ---------------- 噪声 ----------------
    /// <summary>
    /// 0x581460：n = int(128/6.4)+1 = 21，Δ = (rand%21−10)·6.4 world，无衰减，遵守形状。
    /// 官方随机序列不可复现（对照只比较范围+量化+分布）。
    /// </summary>
    internal static void Noise(W3EMap m, int cx, int cy, int size, BrushShape shape)
        => ApplyHeight(m, Offsets(size, shape).Select(o => (cx + o.dx, cy + o.dy)), (x, y) =>
            WorldOf(At(m, x, y)) + (Rng.Next(21) - 10) * StepWorld);

    // ---------------- 平滑 ----------------
    /// <summary>
    /// 0x57e700 op 4：new = W1·h + W2·Σ(k·h邻)/Σk，核 [1,2,1;2,0,2;1,2,1]（中心 0），
    /// 地图外邻居不计入 Σk；先算完再写（快照）。W1/W2 = 0.80/0.20，Shift = 1.00/0.05。
    /// </summary>
    internal static void Smooth(W3EMap m, int cx, int cy, int size, BrushShape shape, bool shift)
    {
        float w1 = shift ? 1.00f : 0.80f, w2 = shift ? 0.05f : 0.20f;
        var kernel = new[] { 1f, 2f, 1f, 2f, 0f, 2f, 1f, 2f, 1f };
        ApplyHeight(m, Offsets(size, shape).Select(o => (cx + o.dx, cy + o.dy)), (x, y) =>
        {
            float sum = 0, wsum = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    if (!Inside(m, x + i, y + j)) continue;
                    float k = kernel[(j + 1) * 3 + (i + 1)];
                    wsum += k;
                    sum += WorldOf(At(m, x + i, y + j)) * k;
                }
            return sum * w2 / wsum + WorldOf(At(m, x, y)) * w1;
        });
    }

    // ================= 悬崖组 =================

    /// <summary>
    /// 1.27 源码 0x574470（CTerrainCliff.cpp）：
    /// 1) T 由调用方传入（Apply 层在按下时捕获 L(中心)+Δ，this+0x94c，拖动全程用）；
    /// 2) T &lt; 0 或 T &gt; MaxLayer → 整笔什么都不做（拒绝，不是 clamp）；
    /// 3) 3×3 外扩染悬崖类型；覆盖集合写 L=T + 类型 + 随机变体（0x5748c0）；
    /// 4) 层差传播（0x573950 → 0x573b40 → 0x573990）。
    /// 未实现：按水夹层 0x5764a0（"夹后值改写 T" 怪癖）。
    /// </summary>
    internal static bool Cliff(W3EMap m, int cx, int cy, int size, BrushShape shape, int targetLayer, int cliffType, bool randomizeVariation = true)
    {
        int T = targetLayer;
        if (T < 0 || T > MaxLayer) return false;          // 整笔拒绝【仅 1.27 源码】
        var cover = Offsets(size, shape).ToList();
        foreach (var (dx, dy) in Dilate(cover))
            if (Inside(m, cx + dx, cy + dy))
                At(m, cx + dx, cy + dy).CliffTexture = cliffType;
        var brushed = new List<(int x, int y)>();
        foreach (var (dx, dy) in cover)
        {
            int x = cx + dx, y = cy + dy;
            if (!Inside(m, x, y)) continue;
            var c = At(m, x, y);
            c.LayerHeight = T;
            c.CliffTexture = cliffType;
            // 变体只有 2 位：官方 FUN_14026a480 `FUN_14014e6a0(0,3)` → `&0x1f | c<<5`
            // （REA 2026-10-16 实测；官方 fixture 的 cw 只出现 {0,1,2}）
            if (randomizeVariation) c.CliffVariation = Rng.Next(4);   // 水刷路径不改变体（官方存档 byte5 不变）
            brushed.Add((x, y));
        }
        Propagate(m, brushed, cliffType, randomizeVariation);
        return true;
    }

    /// <summary>
    /// 0x573990：改动点的 8 邻层级夹到 [L−2, L+2]（再夹 0..MaxLayer）；
    /// 变了就同时写所选悬崖类型 + 随机变体，深度记 d+1，以邻居为中心递归。
    /// 笔刷写过的点（深度 1）和更浅深度处理过的点不再改。
    /// 起点：改动区包围矩形外扩一圈，x 外层 y 内层，深度 1。
    /// </summary>
    private static void Propagate(W3EMap m, List<(int x, int y)> brushed, int cliffType, bool randomizeVariation = true)
    {
        if (brushed.Count == 0) return;
        var depth = new Dictionary<(int, int), int>();
        foreach (var p in brushed) depth[p] = 1;

        void Visit(int x, int y, int d)
        {
            int L = At(m, x, y).LayerHeight;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    int nx = x + i, ny = y + j;
                    if ((i == 0 && j == 0) || !Inside(m, nx, ny)) continue;
                    if (depth.TryGetValue((nx, ny), out int seen) && !(d < seen)) continue;
                    var nc = At(m, nx, ny);
                    int t = nc.LayerHeight;
                    if (t > L + 2) t = L + 2;
                    if (t < L - 2) t = L - 2;
                    t = Math.Clamp(t, 0, MaxLayer);
                    if (t == nc.LayerHeight) continue;
                    nc.LayerHeight = t;
                    if (cliffType != -1) nc.CliffTexture = cliffType;   // 传播点也染类型
                    if (randomizeVariation) nc.CliffVariation = Rng.Next(4);   // 传播点同样只有 2 位（同上）
                    depth[(nx, ny)] = d + 1;
                    Visit(nx, ny, d + 1);
                }
        }

        int x0 = brushed.Min(p => p.x) - 1, x1 = brushed.Max(p => p.x) + 1;
        int y0 = brushed.Min(p => p.y) - 1, y1 = brushed.Max(p => p.y) + 1;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (Inside(m, x, y)) Visit(x, y, 1);
    }

    // ---------------- 浅水 / 深水 ----------------
    /// <summary>
    /// 1.27 源码 0x603cc0（CTerrainWater.cpp）：水笔刷 = 写水 + 整平 + 悬崖，三步复用流水线。
    /// 1) 点击点 c：干地 Δ = −depth；已有水 Δ = floor((水面−地面总高)/128)（水面含偏移，
    ///    深水再减 DeepExtra），水面值 = 现有水面；干地水面值 = c 的地面总高；
    /// 2) 覆盖集合（圆）设水旗；水面高度写 3×3 外扩（外扩一圈为 1.32 存档实测）；
    /// 3) 整平 op 2，尺寸 size+2，值 h0（走流水线，陡坡上会被坡度钳制）；
    /// 4) 悬崖笔刷，尺寸 size，目标 T（越界整笔拒绝、传播、染类型全部生效）。
    /// </summary>
    internal static void Water(W3EMap m, int cx, int cy, int size, int depth, int cliffType)
    {
        var c = At(m, cx, cy);
        int dL;
        float waterWh;   // 写入 WaterHeight 字段的值（层分数）
        float offW = m.Tileset == 'A' ? WaterOffsetWorldA : WaterOffsetWorldL;
        if (!c.Water)
        {
            dL = -depth;
            waterWh = c.GroundHeight + c.LayerHeight - 2f;   // 地面总高（= c.Height + (L−2)·512 的层分数）
        }
        else
        {
            float surf = c.WaterHeight * 128f + offW - (depth == 2 ? DeepExtraWorld : 0f);
            float g = c.GroundHeight * 128f + (c.LayerHeight - 2) * 128f;
            dL = (int)((surf - g) / 128f);
            if (surf < g) dL -= 1;
            waterWh = c.WaterHeight;
        }
        int T = c.LayerHeight + dL;
        float h0 = WorldOf(c);   // 整平步要的是世界单位（参考实现 h0 = World(c.Height)）
        var cover = Offsets(size, BrushShape.Circle).ToList();
        foreach (var (dx, dy) in Dilate(cover))
            if (Inside(m, cx + dx, cy + dy))
                At(m, cx + dx, cy + dy).WaterHeight = waterWh;
        foreach (var (dx, dy) in cover)
            if (Inside(m, cx + dx, cy + dy))
                At(m, cx + dx, cy + dy).Water = true;
        Plateau(m, cx, cy, size + 2, BrushShape.Circle, h0);
        Cliff(m, cx, cy, size, BrushShape.Circle, T, cliffType, randomizeVariation: false);
    }

    // ---------------- 坡道 ----------------
    /// <summary>
    /// 0x583310/0x584270：只响应点击角点（低地角点 c）：
    /// · 4 邻恰有一个高 1 层 → 写 3 角：c 外侧一个、c、高侧一个；
    /// · 无 4 邻更高、恰有一个斜邻高 1 层（台地凸角）→ 以 c 为中心 3×3；
    /// · 层差 2 或点在高处 → 什么都不做（实测 58 个位置全部无变化）。
    /// 层级不变，只写 ramp 标志。Shift = 官方 invert=0 ⇒ **清除**同组 ramp 旗
    /// （实测 Y10_ramp_made → Y10_ramp_shift：同 9 点 1→0，其余字段 0 变化；
    ///  REA FUN_14027d450：`bit==invert` 跳过，invert=1 → `or 0x0002`，invert=0 → `and 0xfffd`）。
    /// </summary>
    /// <summary>
    /// 修饰键 → 坡道语义（审查 P0：2D 与 3D 视图必须用**同一处**判定，避免只接了一条路径）。
    /// 只对 Ramp 生效；其它笔刷忽略 Shift。
    /// </summary>
    public static bool ShiftFor(BrushOp op, bool modifierDown) => modifierDown && op == BrushOp.Ramp;
    internal static void Ramp(W3EMap m, int cx, int cy, bool remove = false)
    {
        int L = At(m, cx, cy).LayerHeight;
        var dirs4 = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        var high4 = dirs4.Where(d => Inside(m, cx + d.Item1, cy + d.Item2) && At(m, cx + d.Item1, cy + d.Item2).LayerHeight == L + 1).ToList();
        bool anyHigher4 = dirs4.Any(d => Inside(m, cx + d.Item1, cy + d.Item2) && At(m, cx + d.Item1, cy + d.Item2).LayerHeight > L);
        bool target = !remove;   // 官方 FUN_14027d450：invert=1 置位、invert=0 清位（Shift 即 invert=0）
        if (high4.Count == 1)
        {
            var (dx, dy) = high4[0];
            foreach (int t in new[] { -1, 0, 1 })
                if (Inside(m, cx + t * dx, cy + t * dy))
                    At(m, cx + t * dx, cy + t * dy).Ramp = target;
            return;
        }
        if (anyHigher4) return;
        // A 级（本次单点四组 + Y10_ramp_made）：
        //   · 对角 +1（本次：抬高点 (28,32) 是中心 (29,31) 的 SW 对角）⇒ 官方写 3×3 共 9 点 ✓
        //   · 对角 +2（Y10_step_c2：SW (31,32) L=4，中心 L=2）⇒ 官方同样写 9 点 ✓
        // ⇒ 触发条件是「**任一对角比中心高**」（不是"恰好 +1"，也不是"四邻全等" ✗）
        var anyDiagHigher = new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) }
            .Any(d => Inside(m, cx + d.Item1, cy + d.Item2) && At(m, cx + d.Item1, cy + d.Item2).LayerHeight > L);
        if (anyDiagHigher)
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                    if (Inside(m, cx + i, cy + j))
                        At(m, cx + i, cy + j).Ramp = target;
    }

    // ================= 纹理组 =================

    /// <summary>
    /// 覆盖集合（遵守形状）写纹理下标 + 重随变体：约 91% 落 {0,4,8,12,16,17}，
    /// 其余均匀 1..15（12.10）。
    ///
    /// **是否清除 blight：未验证。**
    /// 曾据 D0/D3/D5/D6 系列存档断言"荒芜位=byte5 bit2+bit3、异纹理覆盖清除荒芜"，
    /// 该结论已撤回：用权威字段表重解码（byte4 低 4 位纹理、0x10 坡道、0x20 荒芜、
    /// 0x40 水、0x80 边界；byte5=地表变体/悬崖变体；byte6=层级/悬崖类型）后，
    /// 四份存档的 **blight 计数均为 0**，那几笔实际只改了 tex/groundVar/layer/cliffVar/cliffType。
    /// 见 `docs/terrain-reference/experiments/CORRECTIONS.md`。
    /// **B 级结论（反编译 + 记录模型，逐行可核）**：纹理笔刷**会清除 blight**。
    /// 1) 记录里的标志字是 `word[+0x0c]`（不是 w3e 的 byte4，两者布局不同）：
    ///    见 `1.32-brush-water-texture-cliff.md` 标志位表 —— 边界=bit6(0x0040)、**blight=bit2(0x0004)**、跳过=bit0、有水=bit4。
    /// 2) `ghidra_286a60.c:190` `uVar6 = 0xfffb;` → `:192` `auStack_d0._12_2_ &= uVar6` ⇒ **清 bit2 = 清 blight** ✓
    ///    （`:168` 的 `0xffbf` 是 NOTH 分支清 bit6=边界 ✓）
    /// 3) `:174` 只写 `byte8 & 0xf`（w3e byte4 低 4 位=纹理）、`:183/:188` 只写 `byte9 & 0x1f`（地表变体）✓
    /// ⇒ 纹理落笔 = 写纹理/变体 + **清 blight**（仅当本来有 blight 才产生体素差异）。
    /// **A 级仍缺**：现有官方存档没有"先画荒芜→同角点纹理覆盖"的样本（D 系列 blight 计数全 0）。见 CORRECTIONS §1/§12/§19。
    internal static void Texture(W3EMap m, int cx, int cy, int size, BrushShape shape, BrushSettings s)
    {
        var common = new[] { 0, 4, 8, 12, 16, 17 };
        foreach (var (dx, dy) in Offsets(size, shape))
        {
            int x = cx + dx, y = cy + dy;
            if (!Inside(m, x, y)) continue;
            var c = At(m, x, y);
            c.GroundTexture = s.TextureIndex;
            c.GroundVariation = Rng.NextDouble() < 0.91 ? common[Rng.Next(common.Length)] : Rng.Next(1, 16);
            c.Blight = false;   // B 级：ghidra_286a60.c:190,192 → word[+0x0c] &= 0xfffb（清 bit2 = blight）
        }
    }

    /// <summary>
    /// 覆盖集合（遵守形状）写 blight 旗（水角点不接收，实测点水中心无变化）。
    /// 官方还会在外围约 R+2 内随机改一部分贴图（不规则边缘，实测 1.32；
    /// 1.27 写入函数未定位，贴图取值细节未验证，这里按参考实现演示参数）。
    /// </summary>
    internal static void Blight(W3EMap m, int cx, int cy, int size, BrushShape shape)
    {
        foreach (var (dx, dy) in Offsets(size, shape))
        {
            int x = cx + dx, y = cy + dy;
            if (Inside(m, x, y) && !At(m, x, y).Water)
                At(m, x, y).Blight = true;
        }
        int r = size + 1;   // 官方外围约 R+2（R = size−1）
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (Inside(m, cx + dx, cy + dy) && Rng.NextDouble() < 0.3)
                    At(m, cx + dx, cy + dy).GroundTexture = 1;   // 与参考实现一致（贴图取值细节未验证）
    }

    /// <summary>放置/移除边界：只改 boundary 旗，其余字段不动（1.32 实测 13/13）。</summary>
    internal static void Boundary(W3EMap m, int cx, int cy, int size, BrushShape shape, bool add)
    {
        foreach (var (dx, dy) in Offsets(size, shape))
        {
            int x = cx + dx, y = cy + dy;
            if (Inside(m, x, y))
                At(m, x, y).Boundary = add;
        }
    }
}