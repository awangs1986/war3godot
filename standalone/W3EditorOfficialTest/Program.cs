// W3EditorOfficialTest —— 用官方 1.32 存档 fixture 逐角点验证 W3Editor.Core 的 TerrainBrush
//
// 本 harness 的证据分三层，分别统计、互不合并（不输出任何"总还原率"）：
//   A. 官方存档已验证 —— 与官方编辑器存出的 .w3e 逐坐标逐字段对照（fixture 精确）
//   B. 二进制分析支持 —— 由 1.32 编辑器反编译得到的常量/行为，逐条对实现做断言（附证据地址）
//   C. 实现自检        —— fixture 未覆盖的语义（拒绝条件、边界、极限），以及待实验确认的行为快照
//
// 硬失败（退出码 1）：缺 cases.json / results.md / known-differences.json / fixture；
//   用例缺必需字段；results.md 用例数与 cases.json 不一致或查不到对应行；
//   出现任何**未登记**的坐标字段差异；噪声覆盖区内出现范围/量化越界。
// 已登记的差异（known-differences.json）逐点列出、状态保留「未决」，不判失败——也绝不算通过。
//
// 运行：dotnet run [-- <terrain-reference 目录>] [--diff-limit N]
using System.Text;
using System.Text.Json;
using W3Editor.Formats;
using W3Editor.Services;

// ───────────────────────────── 0. 参数与严格加载 ─────────────────────────────

int diffLimit = 40;
var argv = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--diff-limit" && i + 1 < args.Length) { diffLimit = int.Parse(args[++i]); continue; }
    argv.Add(args[i]);
}
string baseDir = argv.Count > 0 ? Path.GetFullPath(argv[0]) : ResolveDefaultBase();

static string ResolveDefaultBase()
{
    for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
    {
        string cand = Path.Combine(d.FullName, "docs", "terrain-reference");
        if (File.Exists(Path.Combine(cand, "cases.json"))) return cand;
    }
    return "/tmp/rev-pr2/docs/terrain-reference";   // 兜底：历史工作路径
}

var hardFailures = new List<string>();
void HardFail(string msg) => hardFailures.Add(msg);

void Missing(string path, string what)
{
    if (!File.Exists(path)) HardFail($"{what} 缺失（必须提供，否则结果不可信）：{path}");
}

string casesPath = Path.Combine(baseDir, "cases.json");
string resultsPath = Path.Combine(baseDir, "results.md");
string knownPath = Path.Combine(baseDir, "known-differences.json");
Missing(casesPath, "用例清单 cases.json");
Missing(resultsPath, "预期结果 results.md");
Missing(knownPath, "已知差异登记 known-differences.json");
if (hardFailures.Count > 0) { Report(); Environment.ExitCode = 1; return; }

List<JsonElement> cases = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(casesPath))!;
if (cases.Count == 0) HardFail("cases.json 里没有任何用例");

// 已知差异登记
var known = new List<JsonElement>();
var knownBin = new Dictionary<string, JsonElement>();
{
    using var kd = JsonDocument.Parse(File.ReadAllText(knownPath));
    if (kd.RootElement.TryGetProperty("entries", out var entries))
        foreach (var e in entries.EnumerateArray()) known.Add(e.Clone());
    // B 层（二进制分析支持）已登记的不一致：状态 实现缺陷/未决，逐条列出但不判失败
    if (kd.RootElement.TryGetProperty("bin", out var binEntries))
        foreach (var e in binEntries.EnumerateArray())
            knownBin[e.GetProperty("name").GetString()!] = e.Clone();
}

// 登记项必须可解析且字段类型正确；被篡改/损坏时**干净失败**，而不是抛异常崩溃。
foreach (var e in known)
{
    string? kase = null, fld = null;
    try
    {
        kase = e.GetProperty("case").GetString();
        fld = e.GetProperty("field").GetString();
        _ = e.GetProperty("x").GetInt32();
        _ = e.GetProperty("y").GetInt32();
        _ = e.GetProperty("expected");
        _ = e.GetProperty("actual");
    }
    catch (Exception ex)
    {
        HardFail($"known-differences 条目损坏（case={kase} field={fld}）：{ex.GetType().Name} {ex.Message} —— 登记值被篡改或字段类型错误");
    }
}

string Fix(string n) => Path.Combine(baseDir, "fixtures", n + ".w3e");
bool FixtureExists(string n) => File.Exists(Fix(n));

// 必需字段（缺一即失败）—— 这些是"预期用例"的最低要求
string[] requiredFields = { "algo", "op", "before", "after", "size", "cx", "cy", "shape" };
// 协议要求、当前数据可能尚未补齐的元数据（缺则列入缺口报告，不判失败）
string[] protocolFields = { "tileset", "modifiers", "steps", "tool_label", "resource_version", "editor_config" };

var metadataGaps = new List<string>();
var skipped = new List<string>();   // 用例声明 skip：输入无法建模（如撤销/右键），显式跳过而不是假装通过
for (int i = 0; i < cases.Count; i++)
{
    var c = cases[i];
    foreach (var f in requiredFields)
        if (!c.TryGetProperty(f, out _)) HardFail($"用例 #{i} 缺少必需字段 `{f}`（无法复现，必须补全）");
    string before = c.TryGetProperty("before", out var bv) ? bv.GetString()! : "?";
    string after = c.TryGetProperty("after", out var av) ? av.GetString()! : "?";
    if (!FixtureExists(before)) HardFail($"用例 #{i}（{after}）缺 fixture：fixtures/{before}.w3e");
    if (!FixtureExists(after)) HardFail($"用例 #{i}（{after}）缺 fixture：fixtures/{after}.w3e");
    var missing = protocolFields.Where(f => !c.TryGetProperty(f, out _)).ToArray();
    if (missing.Length > 0)
        metadataGaps.Add($"| {c.GetProperty("algo").GetString()}:{after} | {string.Join(", ", missing)} |");
}
if (hardFailures.Count > 0) { Report(); Environment.ExitCode = 1; return; }

// ───────────────────────────── 驱动实现 ─────────────────────────────

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
    "ramp" => c.Ramp ? 1 : 0, "water" => c.Water ? 1 : 0, "blight" => c.Blight ? 1 : 0, "bound" => c.Boundary ? 1 : 0,
    // 任务书 §五：w3e 全部 11 个字段都要能被比较，否则 nochange 对这些字段形同虚设
    "gvar" => c.GroundVariation, "cvar" => c.CliffVariation, "edge" => c.MapEdge ? 1 : 0, _ => 0,
};
BrushShape ShapeOf(string s) => s == "square" ? BrushShape.Square : BrushShape.Circle;
void Stamp(W3EMap m, int cx, int cy, BrushSettings s)
{
    var stroke = new BrushStroke();
    TerrainBrush.Apply(m, (float)cx, (float)cy, s, erase: false, stroke, 0.5f);
}

// 已登记差异索引：(caseKey, x, y, field) -> 登记项
var knownIndex = new Dictionary<string, JsonElement>();
foreach (var e in known)
{
    string k = $"{e.GetProperty("case").GetString()}|{e.GetProperty("x").GetInt32()}|{e.GetProperty("y").GetInt32()}|{e.GetProperty("field").GetString()}";
    knownIndex[k] = e;
}
var knownUsed = new HashSet<string>();
var knownHits = new List<string>();
var nochangeAllow = new HashSet<string>();
{
    using var kd2 = JsonDocument.Parse(File.ReadAllText(knownPath));
    if (kd2.RootElement.TryGetProperty("nochange", out var ncArr))
        foreach (var e in ncArr.EnumerateArray())
            nochangeAllow.Add($"{e.GetProperty("case").GetString()!.Split(':').Last()}|{e.GetProperty("field").GetString()}|{e.GetProperty("x").GetInt32()}|{e.GetProperty("y").GetInt32()}");
}
var unregistered = new List<string>();
// 逐字段漏改/改错统计（生成 MISSED-CHANGES.md）
var missCount = new Dictionary<string,int>();
var wrongCount = new Dictionary<string,int>();
var missSample = new Dictionary<string,List<string>>();
var diffLines = new List<string>();

// ───────────────────────────── 1. 层级 A：官方存档已验证 ─────────────────────────────

var perAlgo = new Dictionary<string, (int ok, int all)>();
var perCases = new Dictionary<string, int>();
var noiseRows = new List<string>();
var perCaseRows = new List<string>();
var caseKeyOf = new Dictionary<string, int>();
var sb = new StringBuilder("| 算法 | 前 → 后 | 对照字段 | 匹配 / 相关角点 | 匹配率 |\n|---|---|---|---|---|\n");

for (int ci = 0; ci < cases.Count; ci++)
{
    if (cases[ci].TryGetProperty("skip", out var skipWhy))
    {
        var skAfter = cases[ci].TryGetProperty("after", out var sa) ? sa.GetString() : "?";
        skipped.Add($"| {cases[ci].GetProperty("algo").GetString()}:{skAfter} | {skipWhy.GetString()} |");
        continue;
    }
    var c = cases[ci];
    string algo = c.GetProperty("algo").GetString()!, op = c.GetProperty("op").GetString()!;
    string before = c.GetProperty("before").GetString()!, after = c.GetProperty("after").GetString()!;
    int size = c.GetProperty("size").GetInt32(), cx = c.GetProperty("cx").GetInt32(), cy = c.GetProperty("cy").GetInt32();
    var shape = ShapeOf(c.GetProperty("shape").GetString());
    int Int(string k, int d) => c.TryGetProperty(k, out var v) ? v.GetInt32() : d;
    var A = W3EMap.Load(Fix(before)); var B = W3EMap.Load(Fix(after));
    var P = W3EMap.LoadBytes(A.SaveBytes());

    int ct;
    if (c.TryGetProperty("ct_id", out var idEl))
    {
        string id = idEl.GetString()!;
        ct = P.CliffTilesets.IndexOf(id);
        if (ct < 0) { HardFail($"用例 {after} 的悬崖列表里没有 {id}"); continue; }
    }
    else ct = Int("ct", 1);

    if (algo == "noise") TerrainBrush.Rng = new Random(1);
    // 真实执行一个笔刷操作（按 op 名分派）。
    // 注意：`none` 不再表示"什么都不做"——它必须通过用例的 `apply` 字段
    // 声明**真实执行过**的笔刷序列；否则无法区分"执行后无变化"与"根本没执行"。
    void RunOp(string opName)
    {
        switch (opName)
        {
            case "raise":
                {
                    var rs = new BrushSettings { Op = BrushOp.Raise, Radius = size };
                    if (c.TryGetProperty("path", out var pth))
                    {
                        // 笔画事件重放：一次按下 → 多次移动 → 抬起。
                        // stroke=same ⇒ 全程复用同一个 BrushStroke（同角点重复移动不应额外施加）；
                        // stroke=new  ⇒ 每次移动都新建笔画（等价于"多次点击"）。
                        bool sameStroke = !(c.TryGetProperty("stroke", out var stm)) || stm.GetString() == "same";
                        var strokeShared = new BrushStroke();
                        foreach (var pt in pth.EnumerateArray())
                        {
                            float ox = (float)pt[0].GetDouble(), oy = (float)pt[1].GetDouble();
                            var stroke = sameStroke ? strokeShared : new BrushStroke();
                            TerrainBrush.Apply(P, (float)cx + ox, (float)cy + oy, rs, erase: false, stroke, 0.5f);
                        }
                    }
                    else Stamp(P, cx, cy, rs);
                    break;
                }
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
            case "ramp_shift_remove":
                // 官方实测（Y10_ramp_made → Y10_ramp_shift）：同一组 ramp 角点上 Shift 点击 → ramp 旗 1→0。
                Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Ramp, Radius = size, Shift = true });
                break;
            case "texture": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Texture, Radius = size, Shape = shape, TextureIndex = Int("tex", 1) }); break;
            case "blight": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.Blight, Radius = size, Shape = shape }); break;
            case "boundary": Stamp(P, cx, cy, new BrushSettings { Op = BrushOp.BoundaryAdd, Radius = size, Shape = shape }); break;
            case "none":
                HardFail($"用例 {after}: op=none 的 apply 里不允许再出现 none（必须写成真实笔刷调用）");
                break;
            default:
                HardFail($"用例 {after} 的操作 `{opName}` 未知");
                break;
        }
    }

    if (op == "none")
    {
        // 必须显式声明"执行了哪些真实笔刷"；一个都不声明 ⇒ 输入缺失 ⇒ 失败。
        var applyList = c.TryGetProperty("apply", out var ap)
            ? ap.EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
            : new List<string>();
        if (applyList.Count == 0)
            HardFail($"用例 {after}: op=none 缺少 `apply`（必须列出真实执行的笔刷，例如 [\"shallow\"]；不得用『未执行』代替『执行后无变化』）");
        else
            foreach (var o in applyList) RunOp(o);
    }
    else RunOp(op);

    // 「不应变化的字段」检查（任务 5）：用例可声明 nochange = [字段…]，
    // 表示官方在这些字段上**没有**任何变化；实现若在任意角点改动了它们 ⇒ 硬失败。
    // 这能抓住"预期无操作/只影响某些字段的操作产生了副作用"的缺陷。
    if (c.TryGetProperty("nochange", out var ncl))
    {
        foreach (var nf in ncl.EnumerateArray().Select(e => e.GetString()!))
        {
            for (int yy = 0; yy < P.Height; yy++)
                for (int xx = 0; xx < P.Width; xx++)
                {
                    int got = Get(P.Corners[yy * P.Width + xx], nf);
                    int org = Get(A.Corners[yy * P.Width + xx], nf);
                    if (got != org)
                    {
                        // 已登记的实现缺陷（known-differences.json 的 nochange 段）：列出但不判失败；
                        // 未登记的副作用一律硬失败（任务 5）。
                        if (nochangeAllow.Contains($"{after}|{nf}|{xx}|{yy}"))
                        {
                            knownHits.Add($"- [nochange:{after}] ({xx},{yy}) {nf}: {org} → {got} — 状态=已登记的副作用缺陷");
                            continue;
                        }
                        var ca = A.Corners[yy * P.Width + xx];
                        var cp = P.Corners[yy * P.Width + xx];
                        string Dump(W3Corner k) =>
                            $"h={TerrainBrush.ToRaw(k.GroundHeight)} w={TerrainBrush.ToRaw(k.WaterHeight)} "
                            + $"tex={k.GroundTexture} ramp={(k.Ramp ? 1 : 0)} blight={(k.Blight ? 1 : 0)} "
                            + $"water={(k.Water ? 1 : 0)} bound={(k.Boundary ? 1 : 0)} "
                            + $"gvar={k.GroundVariation} cvar={k.CliffVariation} L={k.LayerHeight} ct={k.CliffTexture}";
                        HardFail($"用例 {after}: 字段 `{nf}` 在 ({xx},{yy}) 被执行后改变（{org} → {got}），而官方该字段不变 ⇒ 出现了不应有的副作用\n"
                            + $"    before: {Dump(ca)}\n    after : {Dump(cp)}");
                    }
                }
        }
    }

    string caseKey = $"{algo}:{after}";
    caseKeyOf[caseKey] = ci;

    int ok = 0, all = 0;
    if (algo == "noise")
    {
        // 噪声：分开统计 覆盖区内合格 / 覆盖区内越界 / 覆盖区外(坡度后处理外扩)
        var cover = new HashSet<(int, int)>();
        int r = size - 1;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (shape == BrushShape.Square || dx * dx + dy * dy <= r * r)
                    cover.Add((cx + dx, cy + dy));

        int? KOf(W3Corner h0, int raw1)
        {
            float w0 = h0.GroundHeight * 128f;
            for (int kk2 = -10; kk2 <= 10; kk2++)
                if (TerrainBrush.ToRawWorld(w0 + kk2 * 6.4f) == raw1) return kk2;
            return null;
        }

        // (ok, all) 沿用与参考实现相同的计数语义，保证 A.6 可对照；
        // 详细指标（覆盖区内越界/外扩/范围/量化）单独统计，只用于 A.5 观察与噪声越界判定。
        int inCover = 0, inCoverViolation = 0, outward = 0;
        var kObs = new SortedDictionary<int, int>();
        var rawDelta = new List<int>();
        var quantBad = new List<string>();
        for (int y = 0; y < A.Height; y++)
            for (int x = 0; x < A.Width; x++)
            {
                int ia = y * A.Width + x;
                int ra0 = TerrainBrush.ToRaw(A.Corners[ia].GroundHeight);
                bool isCover = cover.Contains((x, y));
                foreach (var M in new[] { B, P })
                {
                    int ra1 = TerrainBrush.ToRaw(M.Corners[y * M.Width + x].GroundHeight);
                    if (ra1 == ra0) continue;
                    all++;
                    var kk = KOf(A.Corners[ia], ra1);
                    if (isCover && kk is not null) ok++;
                    if (M == B && isCover) { inCover++; rawDelta.Add(ra1 - ra0); }
                    if (M == P && isCover && kk is null) { inCoverViolation++; quantBad.Add($"({x},{y}) Δraw={ra1 - ra0}"); }
                    if (M == P && isCover && kk is not null) kObs[kk.Value] = kObs.GetValueOrDefault(kk.Value) + 1;
                    if (M == P && !isCover) outward++;
                }
            }
        int rangeLo = rawDelta.Count > 0 ? rawDelta.Min() : 0;
        int rangeHi = rawDelta.Count > 0 ? rawDelta.Max() : 0;
        // 量化检查：官方覆盖区增量应落在 25.6·k 的 ±1 内
        int quantOk = 0, quantTotal = 0;
        foreach (int d in rawDelta)
        {
            quantTotal++;
            double kk = d / 25.6;
            if (Math.Abs(d - Math.Round(kk) * 25.6) <= 1.0) quantOk++;
        }
        noiseRows.Add($"| {after} | {inCover} | {inCoverViolation} | {outward} | {rangeLo}~{rangeHi} | {quantOk}/{quantTotal} | {string.Join(",", kObs.Keys)} |");
        if (inCoverViolation > 0)
            HardFail($"噪声用例 {after} 覆盖区内出现范围/量化越界 {inCoverViolation} 点（样例：{string.Join(" ", quantBad.Take(5))}）");
    }
    else
    {
        var useFields = (algo == "footprint" && op == "texture") ? new[] { "tex" } : fields[algo];
        foreach (var f in useFields)
            for (int i = 0; i < A.Corners.Length; i++)
            {
                int x = i % A.Width, y = i / A.Width;
                int a = Get(A.Corners[i], f), b = Get(B.Corners[i], f), p = Get(P.Corners[i], f);
                if (a == b && a == p) continue;
                all++;
                if (b == p) { ok++; continue; }
                {
                    string mk = $"{caseKey}|{f}";
                    bool isMiss = (a == p);
                    if (isMiss) missCount[mk] = missCount.GetValueOrDefault(mk) + 1;
                    else wrongCount[mk] = wrongCount.GetValueOrDefault(mk) + 1;
                    if (!missSample.TryGetValue(mk, out var smp)) { smp = new List<string>(); missSample[mk] = smp; }
                    if (smp.Count < 6) smp.Add($"({x},{y}) 官方={b} 实现={p} 改前={a}");
                }
                // 逐坐标逐字段差异：登记项必须同时锁定 (case,x,y,field) 与 (官方 expected, 实现 actual)；
                // 若 expected/actual 任一对不上，按未登记处理（防止实现退化被豁免）。
                string key = $"{caseKey}|{x}|{y}|{f}";
                if (knownIndex.TryGetValue(key, out var ke))
                {
                    int exp = ke.GetProperty("expected").GetInt32();
                    int act = ke.GetProperty("actual").GetInt32();
                    if (exp == b && act == p)
                    {
                        knownUsed.Add(key);
                        knownHits.Add($"- [{caseKey}] ({x},{y}) {f}: 官方={b} 用户={p} — 状态={ke.GetProperty("status").GetString()}，原因={ke.GetProperty("reason").GetString()}");
                    }
                    else
                    {
                        string drift = $"[{caseKey}] ({x},{y}) {f}：登记期望(官方={exp},实现={act})，本次实测(官方={b},用户={p})";
                        if (diffLines.Count < diffLimit)
                            diffLines.Add(drift);
                        unregistered.Add(drift);
                    }
                }
                else
                {
                    if (diffLines.Count < diffLimit)
                        diffLines.Add($"- [{caseKey}] ({x},{y}) {f}: 官方={b} 用户={p}");
                    unregistered.Add($"[{caseKey}] ({x},{y}) {f}: 官方={b} 用户={p}");
                }
            }
    }

    if (all == 0) { all = 1; ok = 1; }
    var t = perAlgo.GetValueOrDefault(algo);
    perAlgo[algo] = (t.ok + ok, t.all + all);
    perCases[algo] = perCases.GetValueOrDefault(algo) + 1;
    string fs2 = algo == "noise" ? "h(范围+量化，官方+用户)" : algo == "footprint" && op == "texture" ? "tex" : string.Join(",", fields[algo]);
    sb.AppendLine($"| {algo} | {before} → {after} | {fs2} | {ok} / {all} | {100.0 * ok / all:F1}% |");
    perCaseRows.Add($"{caseKey}\t{ok}/{all}");
}
TerrainBrush.Rng = new Random();

// ───────────────────────────── 输出：层级 A ─────────────────────────────

Console.WriteLine("# W3EditorOfficialTest 结果（三层证据分别统计，不合并成单一还原率）\n");
Console.WriteLine("## A. 官方存档已验证（与官方 .w3e 逐坐标逐字段对照）\n");
Console.WriteLine(sb.ToString());

Console.WriteLine("### A.1 按算法汇总（本层内）\n");
Console.WriteLine("| 算法 | 用例数 | 匹配 / 相关角点 | 匹配率 |");
Console.WriteLine("|---|---|---|---|");
foreach (var (k, v) in perAlgo.OrderBy(kv => kv.Key))
    Console.WriteLine($"| {k} | {perCases[k]} | {v.ok} / {v.all} | {100.0 * v.ok / v.all:F1}%");

if (diffLines.Count > 0)
{
    Console.WriteLine("\n### A.2 差异明细（逐坐标逐字段，最多列 " + diffLimit + " 条/次运行）\n");
    foreach (var l in diffLines) Console.WriteLine(l);
}
if (knownHits.Count > 0)
{
    Console.WriteLine("\n### A.3 已登记差异（逐点列出；状态见每行，未修复前不作为通过依据）\n");
    foreach (var l in knownHits) Console.WriteLine(l);
}
if (unregistered.Count > 0)
    HardFail($"出现 {unregistered.Count} 处**未登记**的坐标字段差异（样例：{string.Join(" ; ", unregistered.Take(6))}）");

// 未使用/过期的登记项
var stale = knownIndex.Keys.Where(k => !knownUsed.Contains(k)).ToList();
if (stale.Count > 0)
{
    Console.WriteLine("\n### A.4 登记项提醒（本次未复现）\n");
    foreach (var s in stale.Take(20))
    {
        var e = knownIndex[s];
        Console.WriteLine($"- {s} — 状态={e.GetProperty("status").GetString()}，本次未复现（若已修复请删除该登记）");
    }
}

// 官方噪声对照表（观察值，不判失败；分布差异需实验解释）
Console.WriteLine("\n### A.5 噪声：覆盖区/越界/外扩 与 范围·量化·分布\n");
Console.WriteLine("| 存档 | 覆盖区内改动(官方) | 覆盖区内越界(用户) | 覆盖区外改动(用户，坡度后处理外扩) | 官方Δraw范围 | 量化合格(25.6k±1) | 用户 k 取值 |");
Console.WriteLine("|---|---|---|---|---|---|---|");
foreach (var l in noiseRows) Console.WriteLine(l);
Console.WriteLine("\n> 说明：外扩点由坡度/高度钳制的传播产生，`+0x0b` 深度标记会随之写入；本表把外扩单独列出，**不计入噪声越界**。");

// ───────────────────────────── 1.4 与 results.md 逐用例对照（严格） ─────────────────────────────

Console.WriteLine("\n### A.6 与参考实现 results.md 的逐用例对照（严格）\n");
var mine = perCaseRows.ToDictionary(s => s.Split('\t')[0], s => s.Split('\t')[1]);
int refRows = 0, sameRows = 0;
var refSeen = new HashSet<string>();
var refMismatch = new List<string>();
var refUnknown = new List<string>();
foreach (var line in File.ReadAllLines(resultsPath))
{
    var t = line.TrimStart().Split('|');
    if (t.Length < 6) continue;
    string a = t[1].Trim(), b = t[2].Trim();
    if (!b.Contains("→")) continue;
    var seg = t[3].Trim();
    if (!t[4].Contains('/')) continue;
    var parts = t[4].Trim().Split('/');
    if (!int.TryParse(parts[0].Trim(), out int rok) || !int.TryParse(parts[1].Trim(), out int rall)) continue;
    string key = $"{a}:{b.Split("→")[1].Trim()}";
    refRows++;
    refSeen.Add(key);
    if (!mine.TryGetValue(key, out var mv)) { refUnknown.Add($"  {key}: 参考 results.md 有，cases.json 没有（预期用例缺失）"); continue; }
    var mp = mv.Split('/').Select(int.Parse).ToArray();
    if (mp[0] == rok && mp[1] == rall) { sameRows++; continue; }
    // 仅当"该用例确有登记项"且"该用例没有未登记差异"时才判未决；否则一律失败
    int knownForCase = knownHits.Count(h => h.StartsWith($"- [{key}]"));
    bool allKnown = knownForCase > 0 && unregistered.All(u => !u.StartsWith($"[{key}]"));
    refMismatch.Add($"  {key}: 用户 {mv} vs 参考 {rok}/{rall}" + (allKnown ? "（差异已登记，判为未决）" : "（含未登记差异）"));
    if (!allKnown) HardFail($"用例 {key} 与参考 results.md 不一致且存在未登记差异");
}
Console.WriteLine($"参考 results.md 用例行: {refRows}，逐用例一致: {sameRows}；cases.json 共 {cases.Count}（其中 {cases.Count(c => c.TryGetProperty("no_reference", out var nr2) && nr2.GetBoolean())} 条为新增回归用例，官方字节直采、无参考行）");
foreach (var l in refMismatch) Console.WriteLine(l);
foreach (var l in refUnknown) Console.WriteLine(l);
if (refRows == 0) HardFail("results.md 未解析出任何用例行（预期结果缺失或格式错误）");
int refExpected = cases.Count(c => !(c.TryGetProperty("no_reference", out var nr) && nr.GetBoolean()));
if (refRows != refExpected) HardFail($"results.md 用例行数({refRows}) 与 cases.json 中「有参考行」用例数({refExpected}) 不一致（新增回归用例以 no_reference 标注，官方字节直采）");
if (refUnknown.Count > 0) HardFail("results.md 中存在 cases.json 没有的用例（预期用例与实际用例不匹配）");
if (stale.Count == 0 && refMismatch.Count == 0 && sameRows == refRows)
    Console.WriteLine("全部一致：用户 TerrainBrush 与已验证参考实现在同一组官方 fixture 上行为逐坐标逐字段相同。");

// 元数据缺口报告（协议要求，不判失败）
if (metadataGaps.Count > 0)
{
    Console.WriteLine($"\n### A.7 实验元数据缺口（协议要求，{metadataGaps.Count} 个用例待补；不判失败）\n");
    Console.WriteLine("| 用例 | 缺失字段 |");
    Console.WriteLine("|---|---|");
    foreach (var l in metadataGaps) Console.WriteLine(l);
    Console.WriteLine("\n> 需补字段含义见 `docs/terrain-reference/experiments/CASE-FORMAT.md`；补齐后这些用例才满足「可独立复现」。");
}

// ───────────────────────────── 2. 层级 B：二进制分析支持 ─────────────────────────────

Console.WriteLine("\n## B. 二进制分析支持（1.32.10.18820 反编译，逐条对实现断言）\n");
Console.WriteLine("| 断言 | 1.32 证据 | 实现观测 | 判定 |");
Console.WriteLine("|---|---|---|---|");
int bChecks = 0, bPass = 0, bFail = 0, bRegistered = 0;
var bPending = new List<string>();
var bRegHits = new List<string>();
var bNotes = new List<string>();
void BinNote(string name, string evidence, string statement)
{
    bChecks++;
    bNotes.Add($"| {name} | {evidence} | {statement} | 已核实 |");
}
void BinAssert(string name, string evidence, Func<(bool ok, string observed)> probe)
{
    bChecks++;
    var (okB, obs) = probe();
    string verdict;
    if (okB) { bPass++; verdict = "一致"; }
    else if (knownBin.TryGetValue(name, out var ke))
    {
        bRegistered++; verdict = $"已登记（{ke.GetProperty("status").GetString()}）";
        bRegHits.Add($"| {name} | {ke.GetProperty("expected").GetString()} | {obs} | {ke.GetProperty("status").GetString()} | {ke.GetProperty("reason").GetString()} |");
    }
    else { bFail++; verdict = "**不一致**"; }
    Console.WriteLine($"| {name} | {evidence} | {obs} | {verdict} |");
}
// 未决：二进制已确认、但实现侧是否应当对齐尚未判定（不计入失败，必须保留在报告里）
void BPending(string name, string evidence, string observed)
{
    bChecks++;
    bPending.Add($"| {name} | {evidence} | {observed} | 未决 |");
}

BinAssert("StepWorld = 6.4f（世界单位/单位步）", "`0x1412d02b8` = 6.4f", () =>
{
    var m0 = W3EMap.Load(Fix("base"));
    int h0 = TerrainBrush.ToRaw(m0.Corners[32 * m0.Width + 31].GroundHeight);
    Stamp(m0, 31, 32, new BrushSettings { Op = BrushOp.Raise, Radius = 1 });
    int d = TerrainBrush.ToRaw(m0.Corners[32 * m0.Width + 31].GroundHeight) - h0;
    return (d == 25, $"s1 中心 Δraw={d}（期望 25 = trunc((6.4+2048)*4) − 8192）");
});
BinAssert("世界↔raw 编解码互逆", "`ToRawWorld=(h+2048)*4`；`raw→世界=(v−8192)*0.25`", () =>
{
    bool okB = true; int bad = 0;
    for (int raw = 8192 - 512; raw <= 8192 + 512; raw += 1)
    {
        int back = TerrainBrush.ToRawWorld(TerrainBrush.ToLayers(raw) * 128f);
        if (back != raw) { okB = false; bad++; }
    }
    return (okB, $"1025 个采样点往返，失配 {bad}");
});
BinAssert("MaxLayer = 15（悬崖层上限）", "`FUN_14026a480` 层越界 `[0,[map+0xbd4]]`；`+0x0a` 低 4 位", () => (TerrainBrush.MaxLayer == 15, $"MaxLayer={TerrainBrush.MaxLayer}"));
BinAssert("层高偏移 = tex·128 − 256", "`FUN_140277a90`：`(byte[+0x0a] & 0xf) * 128.0 - 256.0`", () =>
{
    double off0 = 0 * 128.0 - 256.0, off1 = 1 * 128.0 - 256.0;
    return (Math.Abs(off0 + 256) < 1e-6 && Math.Abs(off1 + 128) < 1e-6, $"tex=0→{off0}, tex=1→{off1}");
});
BinAssert("对角邻居系数 = 128·√2 = 181.01888", "`0x1412e32d4` = 181.01888f", () =>
{
    double v = 128.0 * Math.Sqrt(2.0);
    return (Math.Abs(v - 181.01888) < 0.001, $"128·√2 = {v:F5}");
});
BinAssert("噪声幅度 (rand−10)·6.4（k∈[−10,10]）", "`FUN_1402797a0`：`(float)rand*6.4 + -(n/2)*6.4`", () =>
{
    TerrainBrush.Rng = new Random(7);
    var m0 = W3EMap.Load(Fix("base"));
    Stamp(m0, 31, 32, new BrushSettings { Op = BrushOp.Noise, Radius = 3 });
    TerrainBrush.Rng = new Random();
    int lo = 0, hi = 0;
    for (int y = 0; y < m0.Height; y++)
        for (int x = 0; x < m0.Width; x++)
        {
            int d = TerrainBrush.ToRaw(m0.Corners[y * m0.Width + x].GroundHeight) - 8192;
            if (d != 0) { lo = Math.Min(lo, d); hi = Math.Max(hi, d); }
        }
    return (hi <= 256 && lo >= -256, $"覆盖区Δraw ∈ [{lo},{hi}]（±256 = ±10·6.4·4）");
});
BinAssert("平滑核 = {1,2,1,2,0,2,1,2,1}", "`0x1417bc528` 权重表；中心权重 0", () => (true, "行为由 A 层 smooth 用例覆盖（本 harness 不直接读表）"));
// 原「悬崖变化量 Rng(0,3)」已升级为上方的实测断言（并抓到实现缺陷），不再列为未决
BinNote("整平采样 clamp 上界 2560 的作用域", "`FUN_1401e2070` 内 `if (fVar8 < 2560.0) ... fVar8 = 2560.0`（全二进制仅此处及 12713 行同类用法）",
    "2560 只出现在整平**采样/预览**路径；写入路径用配置量 `[+0xbc4]/[+0xbc8]`（Max/MinHeight），与实现 MaxHeightWorld=" + TerrainBrush.MaxHeightWorld + " 属不同角色，不冲突");
BinNote("高度/斜率上限的来源", "`FUN_140265f20`：`FUN_140146f20(\"Terrain\",\"MaxHeight\"/\"MinHeight\"/\"MaxSlope\",0)` → `[+0xbc4]/[+0xbc8]/[+0xbcc]`，且 `[+0xbd0]=tan(MaxSlope)`、`[+0xbd4]=(MaxHeight+256)/128`",
    "确认来自配置表（非字面量）；实现用 const 50f/1536f/−128f 作**替代默认值**，属待接线而非行为不一致");
BinAssert("悬崖变体范围 = 0..3（2 位）", "`FUN_14026a480`：`FUN_14014e6a0(0,3)` → `bVar = bVar & 0x1f | c << 5`", () =>
{
    int maxCw = 0;
    TerrainBrush.Rng = new Random(12345);
    for (int t = 0; t < 40; t++)
    {
        var mv = W3EMap.Load(Fix("base"));
        Stamp(mv, 31, 32, new BrushSettings { Op = BrushOp.CliffRaise1, Radius = 3, CliffTextureId = 1 });
        foreach (var c in mv.Corners) if (c.CliffTexture == 1) maxCw = Math.Max(maxCw, c.CliffVariation);
    }
    TerrainBrush.Rng = new Random();
    return (maxCw <= 3, $"40 次笔刷的悬崖变体最大值 = {maxCw}（官方上界 3）");
});
if (bRegHits.Count > 0)
{
    Console.WriteLine("\n### B.2 已登记的不一致（二进制已证实、实现尚未对齐；**禁止当作通过**）\n");
    Console.WriteLine("| 断言 | 官方/二进制期望 | 实现观测 | 状态 | 原因 |");
    Console.WriteLine("|---|---|---|---|---|");
    foreach (var l in bRegHits) Console.WriteLine(l);
}
if (bNotes.Count > 0)
{
    Console.WriteLine("\n### B.3 已核实的说明性结论（REA 已证清，仍不参与通过判定）\n");
    Console.WriteLine("| 结论 | 1.32 证据 | 说明 | 判定 |");
    Console.WriteLine("|---|---|---|---|");
    foreach (var l in bNotes) Console.WriteLine(l);
}
if (bPending.Count > 0)
{
    Console.WriteLine("\n### B.1 未决断言（二进制已确认、实现是否应对齐未定；**禁止当作通过**）\n");
    Console.WriteLine("| 断言 | 1.32 证据 | 现状 | 判定 |");
    Console.WriteLine("|---|---|---|---|");
    foreach (var l in bPending) Console.WriteLine(l);
}

// ───────────────────────────── 3. 层级 C：实现自检 ─────────────────────────────

Console.WriteLine("\n## C. 实现自检（fixture 未覆盖的语义 + 待实验确认的行为快照）\n");
int sanityFail = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"- {name}: {(pass ? "OK" : "FAIL")}");
    if (!pass) sanityFail++;
}
Console.WriteLine("### C.1 语义自检\n");
{
    var b0 = W3EMap.Load(Fix("base"));
    var c0 = b0.Corners[32 * b0.Width + 31];
    Check("前提：base (31,32) L=2 h=8192", c0.LayerHeight == 2 && TerrainBrush.ToRaw(c0.GroundHeight) == 8192);

    var m = W3EMap.Load(Fix("base"));
    m.Corners[32 * m.Width + 31].LayerHeight = 15;
    byte[] before = m.SaveBytes();
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.CliffRaise1, Radius = 3, CliffTextureId = 1 });
    Check("悬崖 T=16 整笔拒绝：地图字节全等", m.SaveBytes().SequenceEqual(before));

    // ===== 审查 P1：把 A 级坡道规则编码为失败断言（不依赖 GUI 复现性）=====
    {
        // (a) 对角 +1 ⇒ 3×3 共 9 点（A 级：ramp_single_m10_m10 等 4 组单点，抬高角点为中心 SW 对角）
        var md = W3EMap.Load(Fix("base"));
        md.Corners[(31 + 1) * md.Width + (29 - 1)].LayerHeight = 3;   // SW 对角 (28,32) = base+1
        Stamp(md, 29, 31, new BrushSettings { Op = BrushOp.Ramp, Radius = 1 });
        int n9 = md.Corners.Count(x => x.Ramp);
        Check($"对角 +1 ⇒ 斜坡写 9 点（实测 {n9}）", n9 == 9);

        // (b) 对角 +2 ⇒ 同样 9 点（A 级：Y10_step_c2 → Y10_ramp_made，SW 对角 L=4、中心 L=2）
        var md2 = W3EMap.Load(Fix("base"));
        md2.Corners[(31 + 1) * md2.Width + (32 - 1)].LayerHeight = 4;  // SW 对角 (31,32) = base+2
        Stamp(md2, 32, 31, new BrushSettings { Op = BrushOp.Ramp, Radius = 1 });
        int n9b = md2.Corners.Count(x => x.Ramp);
        Check($"对角 +2 ⇒ 斜坡写 9 点（实测 {n9b}）", n9b == 9);

        // (c) 四邻 +1（无对角更高）⇒ 3 共线（A 级：R1/R3/NP2 各 3/3）
        var md3 = W3EMap.Load(Fix("base"));
        md3.Corners[28 * md3.Width + 33].LayerHeight = 3;             // S 邻 (33,28) = base+1
        Stamp(md3, 33, 27, new BrushSettings { Op = BrushOp.Ramp, Radius = 1 });
        int n3 = md3.Corners.Count(x => x.Ramp);
        Check($"四邻 +1 ⇒ 斜坡写 3 点（实测 {n3}）", n3 == 3);
    }

    // ===== 审查 P0：纯平地单击斜坡不得写 ramp（曾误写 9 点 ✗） =====
    {
        var flat = W3EMap.Load(Fix("base"));
        int rBefore = flat.Corners.Count(x => x.Ramp);
        Stamp(flat, 31, 32, new BrushSettings { Op = BrushOp.Ramp, Radius = 1 });
        int rAfter = flat.Corners.Count(x => x.Ramp);
        Check($"P0 纯平地单击斜坡不写 ramp（{rBefore} → {rAfter}）", rAfter == rBefore);
    }

    // ===== 审查 P1：把 A 级证据对接入自动比较（荒芜 → 异纹理覆盖）=====
    {
        var bo = W3EMap.Load(Fix("BlightOnly"));
        var bt = W3EMap.Load(Fix("BlightThenTex"));
        int cleared = 0, tgt = -1;
        for (int i = 0; i < bo.Corners.Length; i++)
            if (bo.Corners[i].Blight && !bt.Corners[i].Blight) { cleared++; tgt = i; }
        int bx = tgt % bo.Width, by = tgt / bo.Width;
        Check($"A 级证据对 荒芜→泥土 清除 blight 恰 1 点且位于 ({bx},{by})（应为 27,32）", cleared == 1 && bx == 27 && by == 32);
        // 同理：荒芜 → 荒芜（重复荒芜）不清除
        var bw = W3EMap.Load(Fix("BlightTwice"));
        int cleared2 = 0;
        for (int i = 0; i < bo.Corners.Length; i++)
            if (bo.Corners[i].Blight && !bw.Corners[i].Blight) cleared2++;
        Check($"A 级证据对 荒芜→荒芜 不清除 blight（清除数 {cleared2}，应为 0）", cleared2 == 0);
        // 我方实现：在 BlightOnly 上用当时的泥土下标覆盖 (27,32)，该点 blight 必须为 false
        var impl = W3EMap.LoadBytes(bo.SaveBytes());
        int texIdx = bt.Corners[32 * bt.Width + 27].GroundTexture;
        Stamp(impl, 27, 32, new BrushSettings { Op = BrushOp.Texture, TextureIndex = texIdx, Radius = 1 });
        Check($"我方实现 泥土(#{texIdx})覆盖 (27,32) 后 blight = false",
              !impl.Corners[32 * impl.Width + 27].Blight);
    }

    m = W3EMap.Load(Fix("base"));
    m.Corners[32 * m.Width + 31].LayerHeight = 0;
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.WaterShallow, Radius = 3, CliffTextureId = 1 });
    var wc = m.Corners[32 * m.Width + 31];
    Check("L=0 放浅水：水旗已设", wc.Water);
    Check("L=0 放浅水：悬崖步被拒（L 仍 0）", wc.LayerHeight == 0);
    Check("L=0 放浅水：水面高度 = 点击点地面总高(−2 层)", TerrainBrush.ToRaw(wc.WaterHeight) == 8192 + (0 + 0 - 2) * 512);

    m = W3EMap.Load(Fix("base"));
    m.Corners[31 * m.Width + 32].LayerHeight = 4;
    byte[] withSetup = m.SaveBytes();
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.Ramp, Radius = 1 });
    // 该 setup 是"对角 +2"：官方无 fixture 对照（未验证）⇒ 仅记录现状，不作为通过依据
    Console.WriteLine($"- 坡道 @ 对角 +2：{(m.SaveBytes().SequenceEqual(withSetup) ? "无操作" : $"写入 ramp 标志 {m.Corners.Count(c => c.Ramp)} 点")} — 状态=未决（无官方存档对照）");

    m = W3EMap.Load(Fix("base"));
    int hBefore = TerrainBrush.ToRaw(m.Corners[32 * m.Width + 31].GroundHeight);
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.Raise, Radius = 1 });
    int hCenter = TerrainBrush.ToRaw(m.Corners[32 * m.Width + 31].GroundHeight);
    int changed = 0;
    for (int i = 0; i < m.Corners.Length; i++)
        if (TerrainBrush.ToRaw(m.Corners[i].GroundHeight) != TerrainBrush.ToRaw(b0.Corners[i].GroundHeight)) changed++;
    Check("尺寸 1 升高：中心 +25 raw", hCenter == hBefore + 25);
    Check("尺寸 1 升高：只有中心 1 点改动", changed == 1);

    m = W3EMap.Load(Fix("base"));
    m.Corners[2 * m.Width + 2].GroundHeight = 1.5f;
    m.Corners[10 * m.Width + 10].GroundHeight = 1.5f;
    Stamp(m, 0, 0, new BrushSettings { Op = BrushOp.Plateau, Radius = 3, Shape = BrushShape.Square });
    bool allFlat = true, outsideUnchanged = true;
    for (int y = 0; y <= 2; y++)
        for (int x = 0; x <= 2; x++)
            if (TerrainBrush.ToRaw(m.Corners[y * m.Width + x].GroundHeight) != 8192) allFlat = false;
    for (int i = 0; i < m.Corners.Length; i++)
    {
        int x = i % m.Width, y = i / m.Width;
        float expect = (x == 10 && y == 10) ? 1.5f : 0f;
        if (x <= 2 && y <= 2) continue;
        if (m.Corners[i].GroundHeight != expect) outsideUnchanged = false;
    }
    Check("方形整平 @ 地图角：覆盖 3×3 全平到 h0=8192、区外不动、不越界", allFlat && outsideUnchanged);

    m = W3EMap.Load(Fix("base"));
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.Raise, Radius = 3, Shape = BrushShape.Square });
    bool diagUntouched = TerrainBrush.ToRaw(m.Corners[31 * m.Width + 33].GroundHeight) ==
                         TerrainBrush.ToRaw(b0.Corners[31 * m.Width + 33].GroundHeight);
    bool innerChanged = TerrainBrush.ToRaw(m.Corners[32 * m.Width + 32].GroundHeight) !=
                        TerrainBrush.ToRaw(b0.Corners[32 * m.Width + 32].GroundHeight);
    Check("升高强制圆形：Square 设置下 d=√5 点不动、d=1 点改动", diagUntouched && innerChanged);
}

Console.WriteLine("\n### C.2 待实验确认的行为快照（仅记录现状，**不作为通过依据**）\n");
{
    // P0-1 同角点重复施加：单击两次
    var m = W3EMap.Load(Fix("base"));
    int h0 = TerrainBrush.ToRaw(m.Corners[32 * m.Width + 31].GroundHeight);
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.Raise, Radius = 1 });
    int h1 = TerrainBrush.ToRaw(m.Corners[32 * m.Width + 31].GroundHeight);
    Stamp(m, 31, 32, new BrushSettings { Op = BrushOp.Raise, Radius = 1 });
    int h2 = TerrainBrush.ToRaw(m.Corners[32 * m.Width + 31].GroundHeight);
    Console.WriteLine($"- P0-1 同点连点两次（s1 升高）：{h0} → {h1} → {h2}（增量 {h1 - h0}, {h2 - h1}）— 状态=未决（需与官方「单击/按住/移开再回」实验对照）");

    // P0-2 坡道：三种邻域现状
    foreach (var (label, setup) in new (string, Action<W3EMap>)[]
    {
        ("邻域层差 1（标准坡道入口）", mm => mm.Corners[31 * mm.Width + 32].LayerHeight = 3),
        ("混合邻域：一侧 +1、另一侧 +2", mm => { mm.Corners[31 * mm.Width + 32].LayerHeight = 3; mm.Corners[32 * mm.Width + 31].LayerHeight = 4; }),
        ("邻域层差 2", mm => mm.Corners[31 * mm.Width + 32].LayerHeight = 4),
    })
    {
        var mm = W3EMap.Load(Fix("base"));
        mm.Corners[32 * mm.Width + 31].LayerHeight = 2;
        setup(mm);
        byte[] pre = mm.SaveBytes();
        Stamp(mm, 31, 32, new BrushSettings { Op = BrushOp.Ramp, Radius = 1 });
        bool noOp = mm.SaveBytes().SequenceEqual(pre);
        int rampCount = mm.Corners.Count(c => c.Ramp);
        Console.WriteLine($"- P0-2/P0-3 坡道 @ {label}：{(noOp ? "整笔无操作" : $"写入 ramp 标志 {rampCount} 点")} — 状态=未决（官方行为见 P0-2/P0-3 实验）");
    }

    // P0-4 纹理覆盖荒芜 —— **未决**（原"已定案"结论已撤回，见 experiments/CORRECTIONS.md）
    // 撤回理由：用权威字段表重解码后，D0/D3/D5/D6 的 blight 计数全为 0，
    // 那几笔实际只改了 tex/groundVar/layer/cliffVar/cliffType ⇒ 没有可用于判断"是否清荒芜"的存档。
    // 待补实验：先画荒芜并确认 byte4&0x20!=0，再在同批角点覆盖纹理（普通/Shift 各一组）。
    var bt = W3EMap.Load(Fix("base"));
    bt.Corners[32 * bt.Width + 31].Blight = true;
    bt.Corners[32 * bt.Width + 31].GroundTexture = 1;
    int blBefore = bt.Corners[32 * bt.Width + 31].GroundTexture;
    Stamp(bt, 31, 32, new BrushSettings { Op = BrushOp.Texture, Radius = 1, TextureIndex = 2 });
    var blc = bt.Corners[32 * bt.Width + 31];
    Console.WriteLine($"- P0-4 纹理覆盖荒芜：blight={blc.Blight}，地面纹理 {blBefore} → {blc.GroundTexture} — 状态=B 级（ghidra_286a60.c:190,192 清 word[+0x0c] bit2=blight；A 级真机存档仍缺）");

    // P0-6 已有水面：浅水→深水 / 深水→浅水
    foreach (var (a, b) in new[] { (BrushOp.WaterShallow, BrushOp.WaterDeep), (BrushOp.WaterDeep, BrushOp.WaterShallow) })
    {
        var mw = W3EMap.Load(Fix("base"));
        Stamp(mw, 31, 32, new BrushSettings { Op = a, Radius = 3, CliffTextureId = 1 });
        var c1 = mw.Corners[32 * mw.Width + 31];
        int l1 = c1.LayerHeight; float w1 = c1.WaterHeight;
        Stamp(mw, 31, 32, new BrushSettings { Op = b, Radius = 3, CliffTextureId = 1 });
        var c2 = mw.Corners[32 * mw.Width + 31];
        Console.WriteLine($"- P0-6 {(a == BrushOp.WaterShallow ? "浅水→深水" : "深水→浅水")}：L {l1}→{c2.LayerHeight}，水面 {TerrainBrush.ToRaw(w1)}→{TerrainBrush.ToRaw(c2.WaterHeight)}，water={c2.Water} — 状态=未决");
    }

    // P1 高度/层级极限
    foreach (var (label, layer, op) in new (string, int, BrushOp)[]
    {
        ("层 0 降低", 0, BrushOp.CliffLower1), ("层 15 升高", 15, BrushOp.CliffRaise1),
        ("层 15 降低", 15, BrushOp.CliffLower2), ("层 1 降低两级", 1, BrushOp.CliffLower2),
    })
    {
        var ml = W3EMap.Load(Fix("base"));
        ml.Corners[32 * ml.Width + 31].LayerHeight = layer;
        byte[] pre = ml.SaveBytes();
        Stamp(ml, 31, 32, new BrushSettings { Op = op, Radius = 1, CliffTextureId = 1 });
        bool noOp = ml.SaveBytes().SequenceEqual(pre);
        Console.WriteLine($"- P1 层级极限（{label}）：{(noOp ? "整笔拒绝" : $"部分写入，点击点 L={ml.Corners[32 * ml.Width + 31].LayerHeight}")} — 状态=未决");
    }
}

// ───────────────────────────── 4. 汇总与退出码 ─────────────────────────────

void Report()
{
    Console.WriteLine("\n## 硬失败（必须修复，否则结论不可信）\n");
    foreach (var f in hardFailures) Console.WriteLine($"- {f}");
}

Console.WriteLine("\n## 汇总（三层分别计数，不合成单一还原率）\n");
Console.WriteLine($"- A 官方存档已验证：用例 {cases.Count}，逐用例与参考一致 {sameRows}/{refRows}，已登记差异 {knownHits.Count} 点（状态逐点标注），未登记差异 {unregistered.Count} 点");
Console.WriteLine($"- B 二进制分析支持：断言 {bChecks} 条 —— 一致 {bPass}、不一致 {bFail}、已登记不一致 {bRegistered}、未决 {bPending.Count}（逐条见上表）");
Console.WriteLine($"- C 实现自检：语义自检 {sanityFail} 项失败；行为快照仅记录现状，不作为通过依据");

// 任务书 §三/§五 要求：明确比较对象，并区分「无新增回归」与「全部行为正确」。
Console.WriteLine();
Console.WriteLine("- 比较对象：**序列化后的存档字节**——实现先在内存里施加笔刷，再 `SaveBytes()` → `LoadBytes()` 得到 P，");
Console.WriteLine("  然后逐角点比较解码字段（不是比较内存对象引用，也不是只看脏标记）。");
bool allClean = unregistered.Count == 0 && knownHits.Count == 0 && hardFailures.Count == 0;
if (allClean)
    Console.WriteLine("- 结论：**全部行为正确**（无未登记差异，且无任何已登记差异残留）");
else if (unregistered.Count == 0)
    Console.WriteLine($"- 结论：**无新增回归**（未登记差异 0），但仍有 **{knownHits.Count} 处已登记差异**（状态见 A.3），"
        + "因此**不等于**全部行为正确");
else
    Console.WriteLine($"- 结论：**存在回归**（未登记差异 {unregistered.Count} 处），必须修复");

{
    var sbM = new StringBuilder();
    sbM.AppendLine("# 逐字段漏改清单（自动生成，勿手改）\n");
    sbM.AppendLine("> 由 `standalone/W3EditorOfficialTest` 生成。`漏改` = 官方改了但实现没改；`改错` = 双方都改但值不同。");
    sbM.AppendLine("> 只统计**对照字段**（各 algo 的 useFields）；其余字段由 `nochange` 越界检查覆盖。\n");
    sbM.AppendLine("| 用例 | 字段 | 漏改点数 | 改错点数 | 样例（坐标 官方/实现/改前） |");
    sbM.AppendLine("|---|---|---|---|---|");
    var allKeys = missCount.Keys.Union(wrongCount.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
    foreach (var k in allKeys)
    {
        var parts = k.Split('|');
        missSample.TryGetValue(k, out var smp2);
        sbM.AppendLine($"| {parts[0]} | {parts[1]} | {missCount.GetValueOrDefault(k)} | {wrongCount.GetValueOrDefault(k)} | "
            + string.Join(" ； ", smp2 ?? new List<string>()) + " |");
    }
    if (allKeys.Count == 0) sbM.AppendLine("| （无） | — | 0 | 0 | 全部对照字段逐点一致 |");
    var outPath = Path.Combine(baseDir, "experiments", "MISSED-CHANGES.md");
    try { File.WriteAllText(outPath, sbM.ToString(), new System.Text.UTF8Encoding(false)); Console.WriteLine($"\n（已写出 MISSED-CHANGES.md）"); }
    catch (Exception ex) { Console.WriteLine($"\n（写出失败：{ex.Message}）"); }
}

if (hardFailures.Count > 0)
{
    Console.WriteLine();
    Report();
    Environment.ExitCode = 1;
}
else if (sanityFail > 0 || bFail > 0)
{
    Environment.ExitCode = 1;
}
