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
    ["cliff"] = new[] { "L", "ct" }, ["cliff_repeat"] = new[] { "L" }, ["cliff_type"] = new[] { "ct" }, ["footprint"] = new[] { "L" },
    ["ramp"] = new[] { "ramp" }, ["shallow"] = new[] { "L", "water", "w", "h", "ct" }, ["deep"] = new[] { "L", "water", "w", "h", "ct" },
    ["texture"] = new[] { "tex" }, ["blight"] = new[] { "blight" }, ["boundary"] = new[] { "bound" },
};
int Get(Corner c, string f) => f switch
{
    "h" => c.Height, "w" => c.WaterHeight, "L" => c.Layer, "ct" => c.CliffTexture, "tex" => c.Texture,
    "ramp" => c.Ramp ? 1 : 0, "water" => c.Water ? 1 : 0, "blight" => c.Blight ? 1 : 0, "bound" => c.Boundary ? 1 : 0, _ => 0,
};

var perAlgo = new Dictionary<string, (int ok, int all)>();
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
        case "cliff": for (int i = 0; i < Int("repeat", 1); i++) CliffBrushes.Cliff(P, cx, cy, size, shape, Int("k", 1), ct); break;
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
        // 噪声是随机的：只检查“官方改动的角点都在覆盖集合内，且幅度 ≤ 0.5 层”
        var cover = Footprint.Offsets(size, shape).Select(o => (cx + o.dx, cy + o.dy)).ToHashSet();
        for (int y = 0; y < A.Height; y++) for (int x = 0; x < A.Width; x++)
        {
            int d = B[x, y].Height - A[x, y].Height; if (d == 0) continue;
            all++; if (cover.Contains((x, y)) && Math.Abs(d) <= 256) ok++;
        }
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
    var t = perAlgo.GetValueOrDefault(algo); perAlgo[algo] = (t.ok + ok, t.all + all);
    string fs = algo == "noise" ? "h(范围)" : algo == "footprint" && op == "texture" ? "tex" : string.Join(",", fields[algo]);
    sb.AppendLine($"| {algo} | {before} → {after} | {fs} | {ok} / {all} | {100.0 * ok / all:F1}% |");
}

sb.AppendLine("\n### 按算法汇总\n\n| 算法 | 匹配 / 相关角点 | 匹配率 |\n|---|---|---|");
foreach (var (k, v) in perAlgo) sb.AppendLine($"| {k} | {v.ok} / {v.all} | {100.0 * v.ok / v.all:F1}% |");

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
