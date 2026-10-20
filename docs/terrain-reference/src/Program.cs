// Program.cs —— 用实测存档逐角点对照参考实现，输出每个算法的匹配率
// 运行：dotnet run --project docs/terrain-reference
using System.Text;
using System.Text.Json;
using TerrainReference;

var baseDir = AppContext.BaseDirectory;
W3eMap Load(string n) => W3eMap.Load(Path.Combine(baseDir, "fixtures", n + ".w3e"));
var cases = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(Path.Combine(baseDir, "cases.json")))!;

// 每个算法对照哪些字段
var fields = new Dictionary<string, string[]>
{
    ["raise"] = new[] { "h" }, ["lower"] = new[] { "h" }, ["plateau"] = new[] { "h" }, ["smooth"] = new[] { "h" },
    ["cliff"] = new[] { "L", "ct" }, ["cliff_repeat"] = new[] { "L", "ct" }, ["water_on_water"] = new[] { "L", "water", "w", "h", "ct" }, ["cliff_type"] = new[] { "ct" }, ["footprint"] = new[] { "L" },
    ["ramp"] = new[] { "ramp" }, ["shallow"] = new[] { "L", "water", "w", "h", "ct" }, ["deep"] = new[] { "L", "water", "w", "h", "ct" },
    ["texture"] = new[] { "tex" }, ["blight"] = new[] { "blight" }, ["boundary"] = new[] { "bound" },
};
int Get(Corner c, string f) => f switch
{
    "h" => c.Height, "w" => c.WaterHeight, "L" => c.Layer, "ct" => c.CliffTexture, "tex" => c.Texture,
    "ramp" => c.Ramp ? 1 : 0, "water" => c.Water ? 1 : 0, "blight" => c.Blight ? 1 : 0, "bound" => c.Boundary ? 1 : 0, _ => 0,
};

var perAlgo = new Dictionary<string, (int ok, int all)>();
var perCases = new Dictionary<string, int>();
var noiseNotes = new List<string>();
var sb = new StringBuilder("| 算法 | 前 → 后 | 对照字段 | 匹配 / 相关角点 | 匹配率 |\n|---|---|---|---|---|\n");
foreach (var c in cases)
{
    string algo = c.GetProperty("algo").GetString()!, op = c.GetProperty("op").GetString()!;
    string before = c.GetProperty("before").GetString()!, after = c.GetProperty("after").GetString()!;
    int size = c.GetProperty("size").GetInt32(), cx = c.GetProperty("cx").GetInt32(), cy = c.GetProperty("cy").GetInt32();
    var shape = c.GetProperty("shape").GetString() == "square" ? Shape.Square : Shape.Circle;
    int Int(string k, int d) => c.TryGetProperty(k, out var v) ? v.GetInt32() : d;
    var A = Load(before); var B = Load(after); var P = A.Clone();
    int ct = c.TryGetProperty("ct_id", out var id) ? CliffBrushes.CliffTypeIndex(P, id.GetString()!) : Int("ct", 1);
    switch (op)
    {
        case "raise": HeightBrushes.RaiseLower(P, cx, cy, size, +1); break;
        case "lower": HeightBrushes.RaiseLower(P, cx, cy, size, -1); break;
        case "plateau": HeightBrushes.Plateau(P, cx, cy, size, shape); break;
        case "smooth": HeightBrushes.Smooth(P, cx, cy, size, false, shape); break;
        case "smooth_shift": HeightBrushes.Smooth(P, cx, cy, size, true, shape); break;
        case "noise": HeightBrushes.Noise(P, cx, cy, size, new Random(1), shape); break;
        case "cliff":
            // clicks：每次点击相对 (cx,cy) 的偏移；没有就按 repeat 次原地点击
            var clicks = c.TryGetProperty("clicks", out var cl)
                ? cl.EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32())).ToList()
                : Enumerable.Repeat((0, 0), Int("repeat", 1)).ToList();
            foreach (var (ox, oy) in clicks) CliffBrushes.Cliff(P, cx + ox, cy + oy, size, shape, Int("k", 1), ct);
            break;
        case "shallow": CliffBrushes.Water(P, cx, cy, size, 1, ct); break;
        case "deep": CliffBrushes.Water(P, cx, cy, size, 2, ct); break;
        case "ramp": CliffBrushes.Ramp(P, cx, cy); break;
        case "texture": TextureBrushes.Texture(P, cx, cy, size, shape, Int("tex", 1), new Random(1)); break;
        case "blight": TextureBrushes.Blight(P, cx, cy, size, shape); break;
        case "boundary": TextureBrushes.Boundary(P, cx, cy, size, shape, true); break;
    }
    int ok = 0, all = 0;
    if (algo == "noise")
    {
        // 噪声是随机的，官方随机序列无法复现。这里同时检查官方存档和参考实现的输出：
        //  a) 范围：改动点都在覆盖集合内；b) 量化：Δ 必须是某个 k∈[−10,10] 的 trunc(world+k·6.4) 结果；
        //  c) 分布：k 的直方图，官方 vs 参考实现（种子 1..200 的平均）用总变差距离比较，写进 noiseNotes。
        var cover = Footprint.Offsets(size, shape).Select(o => (cx + o.dx, cy + o.dy)).ToHashSet();
        int? KOf(int h0, int h1) { for (int k = -10; k <= 10; k++) if (HeightPipeline.Raw(HeightPipeline.World(h0) + k * 6.4f) == h1) return k; return null; }
        var kOff = new int[21]; var kRef = new double[21];
        for (int y = 0; y < A.Height; y++) for (int x = 0; x < A.Width; x++)
        {
            foreach (var M in new[] { B, P })
            {
                if (M[x, y].Height == A[x, y].Height) continue;
                all++; var k = KOf(A[x, y].Height, M[x, y].Height);
                if (cover.Contains((x, y)) && k is not null) ok++;
            }
            if (cover.Contains((x, y))) { var k = KOf(A[x, y].Height, B[x, y].Height); if (k is not null) kOff[k.Value + 10]++; }
        }
        for (int seed = 1; seed <= 200; seed++)
        {
            var Q = A.Clone(); HeightBrushes.Noise(Q, cx, cy, size, new Random(seed), shape);
            foreach (var (x, y) in cover) if (Q.Inside(x, y)) { var k = KOf(A[x, y].Height, Q[x, y].Height); if (k is not null) kRef[k.Value + 10] += 1 / 200.0; }
        }
        double n = kOff.Sum(), tv = 0, mo = 0, mr = 0, nr = kRef.Sum();
        for (int i = 0; i < 21; i++) { tv += Math.Abs(kOff[i] / n - kRef[i] / nr) / 2; mo += (i - 10) * kOff[i] / n; mr += (i - 10) * kRef[i] / nr; }
        // 期望：官方样本只有 N 个点，均匀分布下 TV 的抽样噪声约 sqrt(21/N)/2
        noiseNotes.Add($"| {after} | {(int)n} | {mo:F2} | {mr:F2} | {tv:F2} | {Math.Sqrt(21.0 / n) / 2:F2} |");
    }
    else
    {
        // 相关角点 = 官方或参考实现任何一方改动过的角点；逐字段比较
        foreach (var f in (algo == "footprint" && op == "texture" ? new[] { "tex" } : fields[algo]))
            for (int i = 0; i < A.Corners.Length; i++)
            {
                int a = Get(A.Corners[i], f), b = Get(B.Corners[i], f), p = Get(P.Corners[i], f);
                if (a == b && a == p) continue;
                all++; if (b == p) ok++;
            }
    }
    if (all == 0) { all = 1; ok = 1; } // 两边都没改动（例如两层差坡道）：算作一致
    var t = perAlgo.GetValueOrDefault(algo); perAlgo[algo] = (t.ok + ok, t.all + all); perCases[algo] = perCases.GetValueOrDefault(algo) + 1;
    string fs = algo == "noise" ? "h(范围+量化，官方+参考)" : algo == "footprint" && op == "texture" ? "tex" : string.Join(",", fields[algo]);
    sb.AppendLine($"| {algo} | {before} → {after} | {fs} | {ok} / {all} | {100.0 * ok / all:F1}% |");
}

// 验证强度：每条结论的来源。只看匹配率会误导——有些行为只在 1.27 源码里读到，1.32 存档根本没覆盖。
var scope = new Dictionary<string, (string tag, string note)>
{
    ["raise"] = ("1.27源码+1.32存档验证", "cos 衰减、步长、取整、强制圆形"),
    ["lower"] = ("1.27源码+1.32存档验证", "同上，取负"),
    ["plateau"] = ("1.27源码+1.32存档验证", "h0、坡度钳制（P2 的 8500/9110）；方形整平仅 1.27 源码"),
    ["noise"] = ("1.27源码+1.32存档验证（统计）", "范围+量化逐点；k 的分布只能统计比较"),
    ["smooth"] = ("1.27源码+1.32存档验证", "1-2-1 核；方形平滑仅 1.27 源码"),
    ["cliff"] = ("1.27源码+1.32存档验证", "单次 ±1/±2；越界整笔拒绝仅 1.27 源码"),
    ["cliff_repeat"] = ("1.27源码+1.32存档验证（部分）", "层级+悬崖类型；s1_x3 的命中点是搜索推断；s3_x3 剩 20 个字段（4 L + 16 ct）未解释"),
    ["cliff_type"] = ("1.32存档验证", "按钮→本图悬崖列表下标（1.27 源码未逐行核对）"),
    ["ramp"] = ("1.27源码+1.32存档验证", "层差恰为 1 的前置条件；写角细节仅 1.32 存档"),
    ["shallow"] = ("1.27源码+1.32存档验证", "平地/缓坡；陡坡未测"),
    ["deep"] = ("1.27源码+1.32存档验证", "平地/缓坡；陡坡未测"),
    ["water_on_water"] = ("1.27源码+1.32存档验证（1 例）", "已有浅水上放深水；DeepExtra 常数未读出"),
    ["texture"] = ("仅1.32存档", "1.27 写入函数未定位"),
    ["blight"] = ("仅1.32存档", "1.27 写入函数未定位"),
    ["boundary"] = ("仅1.32存档", "1.27 只到入口 0x5f7d10"),
    ["footprint"] = ("1.27源码+1.32存档验证", "圆/方覆盖集合"),
};
sb.AppendLine("\n### 按算法：匹配与验证强度\n\n| 算法 | 用例数 | 匹配 / 相关角点 | 匹配率 | 验证范围 | 说明 |\n|---|---|---|---|---|---|");
foreach (var (k, v) in perAlgo)
{
    var (tag, note) = scope.GetValueOrDefault(k, ("?", ""));
    sb.AppendLine($"| {k} | {perCases[k]} | {v.ok} / {v.all} | {100.0 * v.ok / v.all:F1}% | {tag} | {note} |");
}
sb.AppendLine("\n### 只有 1.27 源码、没有任何 1.32 存档覆盖的行为（参考实现已写或已标注，但不能算“已验证”）\n");
foreach (var line in new[] {
    "方形整平 / 方形噪声 / 方形平滑（重制版可能改过）",
    "悬崖目标层越出 0..15 时整笔拒绝（1.32 只有“层 0 上 −2 无变化”，clamp 和拒绝两种解释都成立）",
    "MaxHeight / MinHeight 绝对上下限（参考实现已写，存档没有触发）",
    "高度钳制里的水相关下限 0x51dcb0 / 0x605fc0（未实现）",
    "悬崖笔刷按水夹层 0x5764a0 及“夹后值改写 T”的怪癖（未实现）",
    "悬崖后高度上下限修正 0x57f270 → 0x57ef30（未实现）",
    "陡坡上放水：整平 s+2 走坡度钳制（参考实现已走流水线，未测）",
})
    sb.AppendLine($"- {line}");
sb.AppendLine("\n### 噪声：k 的分布（官方存档 vs 参考实现 200 个种子平均）\n\n| 存档 | 覆盖点数 | 官方平均 k | 参考平均 k | 总变差距离 TV | 均匀抽样下 TV 量级 |\n|---|---|---|---|---|---|");
foreach (var l in noiseNotes) sb.AppendLine(l);
sb.AppendLine("\nTV 与“均匀抽样下的量级”相当，说明官方 k 和均匀 −10..10 没有可区分的差异；这只是统计一致，不是逐点验证。");

// ---------- 渲染端自检（没有官方数值，只验证结构） ----------
sb.AppendLine("\n### 渲染端子系统自检（官方存档里没有标准答案）\n");
{
    var m = Load("raise_s5_c_h08"); double maxGap = 0;
    for (int y = 20; y < 44; y++) for (int x = 20; x < 44; x++)
    {
        double z00 = Rendering.GroundZ(m[x, y]), z10 = Rendering.GroundZ(m[x + 1, y]), z01 = Rendering.GroundZ(m[x, y + 1]), z11 = Rendering.GroundZ(m[x + 1, y + 1]);
        maxGap = Math.Max(maxGap, Math.Abs(Rendering.Bilinear(z00, z10, z01, z11, .5, .5) - Rendering.Triangle(z00, z10, z01, z11, .5, .5)));
    }
    sb.AppendLine($"- 高度插值：在 `raise_s5_c_h08` 的山丘上，同一格中心“双线性”与“三角”最大相差 {maxGap:F2} 世界单位。这就是悬崖顶点和地面用不同插值时的裂缝大小。");
}
{
    var m = Load("MX_quad"); var cell = Rendering.GroundLayers(m, 33, 30);
    sb.AppendLine($"- 地表过渡：`MX_quad` 格 (33,30) 的层 = {string.Join(" ", cell.Select(l => $"tex{l.tex}:mask{Convert.ToString(l.mask, 2).PadLeft(4, '0')}"))}。4 种贴图各占一个角（掩码 0001/0010/0100/1000），按下标从小到大叠放，与截图 `MX_quad_zoom.jpg` 一致。");
}
{
    var m = Load("LS04_deep"); int wet = 0, shore = 0;
    for (int y = 0; y < m.Height - 1; y++) for (int x = 0; x < m.Width - 1; x++)
    {
        var cs = new[] { m[x, y], m[x + 1, y], m[x, y + 1], m[x + 1, y + 1] };
        int n = cs.Count(c => Rendering.Depth(c, -0.7) > 0); if (n == 4) wet++; else if (n > 0) shore++;
    }
    sb.AppendLine($"- 海岸：`LS04_deep`（L 图，水偏移 −0.7）里全水格 {wet} 个、岸边格 {shore} 个。岸边格就是需要按水深做透明渐变的格子。");
}
Console.WriteLine(sb.ToString());
File.WriteAllText(Path.Combine(baseDir, "results.md"), sb.ToString());
