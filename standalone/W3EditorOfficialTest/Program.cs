// W3EditorOfficialTest —— 用官方 1.32 存档 fixture 逐角点验证 W3Editor.Core 的 TerrainBrush（官方 1:1 版）
// 逻辑 1:1 移植自 docs/terrain-reference 的 Program.cs（对照同一组 cases.json / fixtures / 字段表），
// 但被驱动的是用户的 TerrainBrush，而不是参考实现。
// 通过标准：每个用例的 匹配/相关角点 与参考实现 results.md 完全一致。
using System.Text;
using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

string baseDir = args.Length > 0 ? Path.GetFullPath(args[0]) : ResolveDefaultBase();

// 默认 fixtures 目录：从程序集位置向上找仓库内的 docs/terrain-reference（含 cases.json）
static string ResolveDefaultBase()
{
    for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
    {
        string cand = Path.Combine(d.FullName, "docs", "terrain-reference");
        if (File.Exists(Path.Combine(cand, "cases.json"))) return cand;
    }
    return "/tmp/rev-pr2/docs/terrain-reference";   // 兜底：历史工作路径
}

string Fix(string n) => Path.Combine(baseDir, "fixtures", n + ".w3e");
W3EMap Load(string n) => W3EMap.Load(Fix(n));
var cases = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(Path.Combine(baseDir, "cases.json")))!;

// 每个算法对照哪些字段（与参考实现相同）
var fields = new Dictionary<string, string[]>
{
    ["raise"] = new[] { "h" }, ["lower"] = new[] { "h" }, ["plateau"] = new[] { "h" }, ["smooth"] = new[] { "h" },
    ["cliff"] = new[] { "L", "ct" }, ["cliff_repeat"] = new[] { "L", "ct" }, ["water_on_water"] = new[] { "L", "water", "w", "h", "ct" }, ["cliff_type"] = new[] { "ct" }, ["footprint"] = new[] { "L" },
    ["ramp"] = new[] { "ramp" }, ["shallow"] = new[] { "L", "water", "w", "h", "ct" }, ["deep"] = new[] { "L", "water", "w", "h", "ct" },
    ["texture"] = new[] { "tex" }, ["blight"] = new[] { "blight" }, ["boundary"] = new[] { "bound" },
};
int Get(W3Corner c, string f) => f switch
{
    "h" => TerrainBrush.ToRaw(c.GroundHeight),
    "w" => TerrainBrush.ToRaw(c.WaterHeight),
    "L" => c.LayerHeight,
    "ct" => c.CliffTexture,
    "tex" => c.GroundTexture,
    "ramp" => c.Ramp ? 1 : 0, "water" => c.Water ? 1 : 0, "blight" => c.Blight ? 1 : 0, "bound" => c.Boundary ? 1 : 0, _ => 0,
};

// 官方形状 → 用户枚举
BrushShape ShapeOf(string s) => s == "square" ? BrushShape.Square : BrushShape.Circle;

// 每次点击 = 一次官方完整施加（单次点击语义）
void Stamp(W3EMap m, int cx, int cy, BrushSettings s)
{
    var stroke = new BrushStroke();
    TerrainBrush.Apply(m, (float)cx, (float)cy, s, erase: false, stroke, 0.5f);
}

var perAlgo = new Dictionary<string, (int ok, int all)>();
var perCases = new Dictionary<string, int>();
var noiseNotes = new List<string>();
var perCaseRows = new List<string>();
var sb = new StringBuilder("| 算法 | 前 → 后 | 对照字段 | 匹配 / 相关角点 | 匹配率 |\n|---|---|---|---|---|\n");
foreach (var c in cases)
{
    string algo = c.GetProperty("algo").GetString()!, op = c.GetProperty("op").GetString()!;
    string before = c.GetProperty("before").GetString()!, after = c.GetProperty("after").GetString()!;
    int size = c.GetProperty("size").GetInt32(), cx = c.GetProperty("cx").GetInt32(), cy = c.GetProperty("cy").GetInt32();
    var shape = ShapeOf(c.GetProperty("shape").GetString());
    int Int(string k, int d) => c.TryGetProperty(k, out var v) ? v.GetInt32() : d;
    var A = Load(before); var B = Load(after);
    var P = W3EMap.LoadBytes(A.SaveBytes());   // 干净副本（v11 往返字节精确）

    int ct;
    if (c.TryGetProperty("ct_id", out var idEl))
    {
        string id = idEl.GetString()!;
        ct = P.CliffTilesets.IndexOf(id);
        if (ct < 0) throw new InvalidOperationException($"图 {after} 的悬崖列表里没有 {id}");
    }
    else ct = Int("ct", 1);

    // ---- 驱动用户的 TerrainBrush ----
    if (algo == "noise")
        TerrainBrush.Rng = new Random(1);   // 单次施加用固定种子；分布统计下面单独跑 1..200
    switch (op)
    {
        case "raise": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Raise, Radius = size }); break;
        case "lower": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Lower, Radius = size }); break;
        case "plateau": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Plateau, Radius = size, Shape = shape }); break;
        case "smooth": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Smooth, Radius = size, Shape = shape }); break;
        case "smooth_shift": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Smooth, Radius = size, Shape = shape, Shift = true }); break;
        case "noise": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Noise, Radius = size, Shape = shape }); break;
        case "cliff":
            var k = Int("k", 1);
            var cop = k == 2 ? BrushOp.CliffRaise2 : k == 1 ? BrushOp.CliffRaise1 : k == -1 ? BrushOp.CliffLower1 : BrushOp.CliffLower2;
            var clicks = c.TryGetProperty("clicks", out var cl)
                ? cl.EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32())).ToList()
                : Enumerable.Repeat((0, 0), Int("repeat", 1)).ToList();
            foreach (var (ox, oy) in clicks)
                Stamp(P, cx + ox, cy + oy, new BrushSettings { Op = cop, Radius = size, Shape = shape, CliffTextureId = ct });
            break;
        case "shallow": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.WaterShallow, Radius = size, CliffTextureId = ct }); break;
        case "deep": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.WaterDeep, Radius = size, CliffTextureId = ct }); break;
        case "ramp": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Ramp, Radius = size }); break;
        case "texture": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Texture, Radius = size, Shape = shape, TextureIndex = Int("tex", 1) }); break;
        case "blight": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Blight, Radius = size, Shape = shape }); break;
        case "boundary": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.BoundaryAdd, Radius = size, Shape = shape }); break;
        default: throw new InvalidOperationException($"未知操作 {op}");
    }

    int ok = 0, all = 0;
    if (algo == "noise")
    {
        // 噪声是随机的：比较 范围+量化（官方存档和用户输出各自检查）+ k 的分布（统计）
        var cover = new HashSet<(int, int)>();
        int r = size - 1;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (shape == BrushShape.Square || dx * dx + dy * dy <= r * r)
                    cover.Add((cx + dx, cy + dy));
        int? KOf(W3Corner h0, int raw1)
        {
            float w0 = h0.GroundHeight * 128f;
            for (int k = -10; k <= 10; k++)
                if (TerrainBrush.ToRawWorld(w0 + k * 6.4f) == raw1) return k;
            return null;
        }
        var kOff = new int[21]; var kRef = new double[21];
        for (int y = 0; y < A.Height; y++)
            for (int x = 0; x < A.Width; x++)
            {
                var A0 = A.Corners[y * A.Width + x];
                foreach (var M in new[] { B, P })
                {
                    if (M.Corners[y * M.Width + x].GroundHeight == A0.GroundHeight) continue;
                    all++;
                    var kk = KOf(A0, TerrainBrush.ToRaw(M.Corners[y * M.Width + x].GroundHeight));
                    if (cover.Contains((x, y)) && kk is not null) ok++;
                }
                if (cover.Contains((x, y)))
                {
                    var kk = KOf(A0, TerrainBrush.ToRaw(B.Corners[y * B.Width + x].GroundHeight));
                    if (kk is not null) kOff[kk.Value + 10]++;
                }
            }
        for (int seed = 1; seed <= 200; seed++)
        {
            TerrainBrush.Rng = new Random(seed);
            var Q = W3EMap.LoadBytes(A.SaveBytes());
            Stamp(Q, cx, cy, new BrushSettings { Op = BrushOp.Noise, Radius = size, Shape = shape });
            foreach (var (x, y) in cover)
            {
                if (x < 0 || y < 0 || x >= Q.Width || y >= Q.Height) continue;
                var kk = KOf(A.Corners[y * A.Width + x], TerrainBrush.ToRaw(Q.Corners[y * Q.Width + x].GroundHeight));
                if (kk is not null) kRef[kk.Value + 10] += 1 / 200.0;
            }
        }
        TerrainBrush.Rng = new Random();
        double n = kOff.Sum(), tv = 0, mo = 0, mr = 0, nr = kRef.Sum();
        for (int i = 0; i < 21; i++)
        {
            tv += Math.Abs(kOff[i] / n - kRef[i] / nr) / 2;
            mo += (i - 10) * kOff[i] / n;
            mr += (i - 10) * kRef[i] / nr;
        }
        noiseNotes.Add($"| {after} | {(int)n} | {mo:F2} | {mr:F2} | {tv:F2} | {Math.Sqrt(21.0 / n) / 2:F2} |");
    }
    else
    {
        // 相关角点 = 官方或用户输出任何一方改动过的角点；逐字段比较
        foreach (var f in (algo == "footprint" && op == "texture" ? new[] { "tex" } : fields[algo]))
            for (int i = 0; i < A.Corners.Length; i++)
            {
                int a = Get(A.Corners[i], f), b = Get(B.Corners[i], f), p = Get(P.Corners[i], f);
                if (a == b && a == p) continue;
                all++;
                if (b == p) ok++;
            }
    }
    if (all == 0) { all = 1; ok = 1; }
    var t = perAlgo.GetValueOrDefault(algo);
    perAlgo[algo] = (t.ok + ok, t.all + all);
    perCases[algo] = perCases.GetValueOrDefault(algo) + 1;
    string fs = algo == "noise" ? "h(范围+量化，官方+用户)" : algo == "footprint" && op == "texture" ? "tex" : string.Join(",", fields[algo]);
    string row = $"| {algo} | {before} → {after} | {fs} | {ok} / {all} | {100.0 * ok / all:F1}% |";
    sb.AppendLine(row);
    perCaseRows.Add($"{algo}:{after}\t{ok}/{all}");
}
Console.WriteLine(sb.ToString());

Console.WriteLine("### 按算法汇总（用户 TerrainBrush）\n");
Console.WriteLine("| 算法 | 用例数 | 匹配 / 相关角点 | 匹配率 |");
Console.WriteLine("|---|---|---|---|");
foreach (var (k, v) in perAlgo)
    Console.WriteLine($"| {k} | {perCases[k]} | {v.ok} / {v.all} | {100.0 * v.ok / v.all:F1}% |");

Console.WriteLine("\n### 噪声 k 分布（官方存档 vs 用户输出 200 种子平均）\n");
Console.WriteLine("| 存档 | 覆盖点数 | 官方平均 k | 用户平均 k | 总变差距离 TV | 均匀抽样下 TV 量级 |");
Console.WriteLine("|---|---|---|---|---|---|");
foreach (var l in noiseNotes) Console.WriteLine(l);

// ---- 边缘用例 sanity（fixture 未覆盖的语义）----
Console.WriteLine("\n### 边缘用例 sanity（fixture 未覆盖的语义）\n");
int sanityFail = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"- {name}: {(pass ? "OK" : "FAIL")}");
    if (!pass) sanityFail++;
}
{
    // base 图前提：平坦 L=2、h=8192
    var b0 = Load("base");
    var c0 = b0.Corners[32 * b0.Width + 31];
    Check("前提：base (31,32) L=2 h=8192", c0.LayerHeight == 2 && TerrainBrush.ToRaw(c0.GroundHeight) == 8192);

    // 1) 悬崖目标层 16 → 整笔拒绝（map 字节全等）
    var m = Load("base");
    m.Corners[32 * m.Width + 31].LayerHeight = 15;
    byte[] before = m.SaveBytes();
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.CliffRaise1, Radius = 3, CliffTextureId = 1 });
    Check("悬崖 T=16 整笔拒绝：地图字节全等", m.SaveBytes().SequenceEqual(before));

    // 2) L=0 干地放浅水：T=−1 → 悬崖步拒绝，但水旗/水面/整平仍生效
    m = Load("base");
    m.Corners[32 * m.Width + 31].LayerHeight = 0;
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.WaterShallow, Radius = 3, CliffTextureId = 1 });
    var wc = m.Corners[32 * m.Width + 31];
    Check("L=0 放浅水：水旗已设", wc.Water);
    Check("L=0 放浅水：悬崖步被拒（L 仍 0）", wc.LayerHeight == 0);
    Check("L=0 放浅水：水面高度 = 点击点地面总高(−2 层)", TerrainBrush.ToRaw(wc.WaterHeight) == 8192 + (0 + 0 - 2) * 512);

    // 3) 坡道：层差 2 → 什么都不做
    m = Load("base");
    m.Corners[31 * m.Width + 32].LayerHeight = 4;   // 东侧邻居高 2 层
    byte[] withSetup = m.SaveBytes();
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.Ramp, Radius = 1 });
    byte[] afterRamp = m.SaveBytes();
    Check("坡道层差 2 无操作：地图字节全等（含 setup）",
        afterRamp.SequenceEqual(withSetup));

    // 4) 尺寸 1 升高：只改中心，+25 raw（(6.4+2048)·4=8217.6→8217）
    m = Load("base");
    int hBefore = TerrainBrush.ToRaw(m.Corners[32 * m.Width + 31].GroundHeight);
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.Raise, Radius = 1 });
    int hCenter = TerrainBrush.ToRaw(m.Corners[32 * m.Width + 31].GroundHeight);
    int changed = 0;
    for (int i = 0; i < m.Corners.Length; i++)
        if (TerrainBrush.ToRaw(m.Corners[i].GroundHeight) != TerrainBrush.ToRaw(b0.Corners[i].GroundHeight)) changed++;
    Check("尺寸 1 升高：中心 +25 raw", hCenter == hBefore + 25);
    Check("尺寸 1 升高：只有中心 1 点改动", changed == 1);

    // 5) 方形整平在地图角 (0,0)：部分足迹不越界、覆盖点全平到 h0。
    //    扰动点放 (2,2)（足迹内，被整平覆盖）和 (10,10)（远处，坡度钳制递归够不到）。
    m = Load("base");
    m.Corners[2 * m.Width + 2].GroundHeight = 1.5f;
    m.Corners[10 * m.Width + 10].GroundHeight = 1.5f;
    Stamp(m, 0, 0, new BrushSettings { Op = BrushOp.Plateau, Radius = 3, Shape = BrushShape.Square });
    bool allFlat = true, outsideUnchanged = true;
    for (int y = 0; y <= 2; y++)
        for (int x = 0; x <= 2; x++)
            if (TerrainBrush.ToRaw(m.Corners[y * m.Width + x].GroundHeight) != 8192)
                allFlat = false;
    for (int i = 0; i < m.Corners.Length; i++)
    {
        int x = i % m.Width, y = i / m.Width;
        float expect = (x == 10 && y == 10) ? 1.5f : 0f;
        if (x <= 2 && y <= 2) continue;
        if (m.Corners[i].GroundHeight != expect) outsideUnchanged = false;
    }
    Check("方形整平 @ 地图角：覆盖 3×3 全平到 h0=8192、区外不动、不越界", allFlat && outsideUnchanged);

    // 6) 升高强制圆形：Shape=Square 时 (33,31)（d=√5≈2.24>2）不改动；(32,32)（d=1）改动。
    //    注：d=R=2 的点 Δ=cos(π/2)=0 本来就不动，不能拿来当正例。
    m = Load("base");
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.Raise, Radius = 3, Shape = BrushShape.Square });
    bool diagUntouched = TerrainBrush.ToRaw(m.Corners[31 * m.Width + 33].GroundHeight) ==
                         TerrainBrush.ToRaw(b0.Corners[31 * m.Width + 33].GroundHeight);
    bool innerChanged = TerrainBrush.ToRaw(m.Corners[32 * m.Width + 32].GroundHeight) !=
                        TerrainBrush.ToRaw(b0.Corners[32 * m.Width + 32].GroundHeight);
    Check("升高强制圆形：Square 设置下 d=√5 点不动、d=1 点改动", diagUntouched && innerChanged);
}
if (sanityFail > 0) Environment.ExitCode = 1;

// ---- 与参考实现 results.md 的逐用例对照 ----
Console.WriteLine("\n### 与参考实现 results.md 的逐用例对照\n");
string refPath = Path.Combine(baseDir, "results.md");
int refLines = 0, sameLines = 0;
var mismatches = new List<string>();
if (File.Exists(refPath))
{
    var mine = perCaseRows.ToDictionary(s => s.Split('\t')[0], s => s.Split('\t')[1]);
    foreach (var line in File.ReadAllLines(refPath))
    {
        var t = line.TrimStart().Split('|');
        if (t.Length < 6) continue;
        string a = t[1].Trim(), b = t[2].Trim();
        if (!b.Contains("→")) continue;
        string key = $"{a}:{b.Split("→")[1].Trim()}";
        if (!int.TryParse(t[4].Trim().Split('/')[0].Trim(), out int rok) || !int.TryParse(t[4].Trim().Split('/')[1].Trim(), out int rall))
            continue;
        refLines++;
        if (mine.TryGetValue(key, out var mv))
        {
            var mparts = mv.Split('/').Select(int.Parse).ToArray();
            int mok = mparts[0], mall = mparts[1];
            if (mok == rok && mall == rall) sameLines++;
            else mismatches.Add($"  {key}: 用户 {mv} vs 参考 {rok}/{rall}");
        }
        else
            mismatches.Add($"  {key}: 参考有、用户 harness 没有");
    }
}
Console.WriteLine($"参考 results.md 用例行: {refLines}，逐用例一致: {sameLines}");
if (mismatches.Count > 0)
{
    Console.WriteLine("不一致（用户 vs 参考）:");
    foreach (var m in mismatches) Console.WriteLine(m);
    Environment.ExitCode = 1;
}
else
    Console.WriteLine("全部一致：用户 TerrainBrush 与已验证参考实现在同一组官方 fixture 上行为逐角点相同。");
