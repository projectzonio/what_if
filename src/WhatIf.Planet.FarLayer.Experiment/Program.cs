using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WhatIf.Planet;
using WhatIf.Planet.FarLayer;
using WhatIf.Planet.FarLayer.Experiment;
using WhatIf.Planet.Surface;

// RG-171: What If planet far layer experiment. Modes:
//   checks <outdir>        rows f1, f2, f7, f8 (seed 20261112 + seed 7 for f2/f7) + the images -> rows.csv, images.txt, PNGs
//   cost <outdir> <n>      rows f3-f6 measurements once (one process per repetition, run under the measurement lock) -> cost<n>.csv
//   report <outdir> <md> <csv> --sha <sha> --hw "<..>" --wall <s>
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length < 2) { Console.Error.WriteLine("usage: checks|cost|report <outdir> ..."); return 2; }
string mode = args[0], dir = args[1]; Directory.CreateDirectory(dir);
string Opt(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
string Sha(string path) { using (var f = File.OpenRead(path)) return string.Concat(SHA256.Create().ComputeHash(f).Select(b => b.ToString("x2"))); }
string F(double v, string f = "F3") => Checks.F(v, f);
const int Frames = 120 * CamPath.Fps;

if (mode == "cost") return Cost();
if (mode == "checks") return RunChecks();
if (mode == "report") return Report();
Console.Error.WriteLine("unknown mode " + mode); return 2;

int Cost()
{
    var lines = new List<string> { "scenario,metric,value" }; void L(string sc, string m, double v) => lines.Add($"{sc},{m},{v.ToString("R", CultureInfo.InvariantCulture)}");
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long m0 = GC.GetTotalMemory(true);
    var sw = Stopwatch.StartNew(); var sim = new Sim(Checks.Seeds[0]); double ctor = sw.Elapsed.TotalMilliseconds - sim.PrebuildMs; var cams = new Cameras(sim.Surf, Checks.Seeds[0]);
    L("global", "surface_ctor_ms", ctor); L("global", "prebuild_ms", sim.PrebuildMs); L("global", "prebuild_nodes", sim.PrebuildNodes);
    long mPre = GC.GetTotalMemory(true);
    for (int c = 0; c < 4; c++)
    {
        string sc = cams.Names[c]; sim.Settle(cams.Fixed[c]); var st = new List<Sim.Stat>();
        for (int i = 0; i < 2200; i++) { var s = sim.Frame(cams.Fixed[c], 2.0); if (i >= 200) st.Add(s); }
        Summ(sc, st);
    }
    foreach (var (name, path) in new[] { ("drive", cams.Drive), ("flight", cams.Flight) })
    {
        var sw2 = Stopwatch.StartNew(); int startNodes = sim.Settle(path.At(0)); L(name, "startup_ms", sw2.Elapsed.TotalMilliseconds); L(name, "startup_nodes", startNodes);
        var st = new List<Sim.Stat>(Frames); int built0 = sim.Layer.BuiltCount;
        for (int f = 0; f < Frames; f++) st.Add(sim.Frame(path.At(f), 2.0));
        Summ(name, st);
        L(name, "build_ms_p95", Checks.Pct(st.Select(x => x.BuildMs).ToList(), 0.95)); L(name, "build_ms_max", st.Max(x => x.BuildMs)); L(name, "build_ms_p95_busy", Checks.Pct(st.Where(x => x.Built > 0).Select(x => x.BuildMs).DefaultIfEmpty(0).ToList(), 0.95));
        L(name, "nodes_built", st.Sum(x => x.Built)); L(name, "nodes_built_per_s", st.Sum(x => x.Built) / 120.0); L(name, "frames_incomplete", st.Count(x => x.Incomplete > 0)); L(name, "frames_parentless", st.Count(x => x.Parentless > 0)); L(name, "frames_fallback", st.Count(x => x.Fallbacks > 0));
    }
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long m1 = GC.GetTotalMemory(true);
    L("global", "cache_nodes", sim.Layer.NodeCount); L("global", "cache_built", sim.Layer.BuiltCount); L("global", "memory_mib_after_e_f", (m1 - mPre) / 1048576.0); L("global", "memory_mib_total_vs_start", (m1 - m0) / 1048576.0);
    L("global", "bytes_per_node_heights", 289 * 8); L("global", "bytes_per_node_upload", 289 * 24);
    File.WriteAllLines(Path2(dir, $"cost{args[2]}.csv"), lines); foreach (var l in lines) Console.WriteLine(l);
    return 0;

    void Summ(string sc, List<Sim.Stat> st)
    {
        var sel = st.Select(x => x.SelMs).ToList(); var tot = st.Select(x => x.UpdMs + x.SelMs).ToList();
        L(sc, "sel_ms_median", Checks.Pct(sel, 0.5)); L(sc, "sel_ms_p95", Checks.Pct(sel, 0.95)); L(sc, "total_ms_median", Checks.Pct(tot, 0.5)); L(sc, "total_ms_p95", Checks.Pct(tot, 0.95)); L(sc, "total_ms_max", tot.Max());
        L(sc, "frustum_ms_median", Checks.Pct(st.Select(x => x.FrustumMs).ToList(), 0.5)); L(sc, "nodes_max", st.Max(x => x.Nodes)); L(sc, "nodes_median", Checks.Pct(st.Select(x => (double)x.Nodes).ToList(), 0.5)); L(sc, "tris_max", st.Max(x => x.Tris)); L(sc, "skirt_tris_max", st.Max(x => x.SkirtTris));
        L(sc, "fnodes_max", st.Max(x => x.FNodes)); L(sc, "ftris_max", st.Max(x => x.FTris));
    }
}
string Path2(string a, string b) => System.IO.Path.Combine(a, b);

int RunChecks()
{
    var sw = Stopwatch.StartNew(); var rows = new List<Row>(); var images = new List<string>(); ulong st = 12345;
    var gapAll = new Dictionary<string, List<double>>(); int missingAll = 0; int lvlBad = 0, bitsCmp = 0, bitsDiff = 0, tPairs = 0, lvlFrames = 0; double tWorst = 0;
    var protoLines = new List<string>();
    foreach (ulong seed in Checks.Seeds)
    {
        bool main = seed == Checks.Seeds[0]; var sim = new Sim(seed); var cams = new Cameras(sim.Surf, seed); string tag = seed.ToString();
        Console.WriteLine($"seed {seed}: cameras found {sw.Elapsed.TotalSeconds:F1}s"); if (main) protoLines.AddRange(Describe(cams, sim.Surf));
        for (int c = 0; c < 4; c++)
        {
            var cam = cams.Fixed[c]; sim.Settle(cam); var idx = new CoverageIndex(sim.Sel, sim.Near); string name = cams.Names[c];
            int miss; var gaps = Checks.Gaps(sim, cam, true, out miss); missingAll += miss; gapAll[name + "." + tag] = gaps;
            rows.Add(Row.Report($"f2.gap.{name}.{tag}", $"max {F(gaps.Max())} m; P95 {F(Checks.Pct(gaps, 0.95))} m", $"{gaps.Count} hole-edge vertices of {sim.Near.Exposed.Count} exposed sector sides ({sim.Near.Count} near sectors), SurfaceSector height (float) vs far mesh radius at the same direction (triangle plane, morph applied); {miss} vertices without a built level-13 node"));
            if (!main) continue;
            var (n1, b1) = Checks.Coverage(idx, cam, 100000, Checks.NearVertices(sim.Near, true), ref st);
            rows.Add(Row.Proto($"f1.coverage.{name}", $"{b1} violations", "0", b1 == 0 && sim.Sel.IncompleteRoots == 0, $"{n1} directions: 100000 random in the horizon cap (half uniform, half log-uniform in angle from 50 m) + all 65 x 65 vertices of the {sim.Near.Count} near sectors; covered exactly once by one unmasked far quad or one near sector"));
            lvlBad += SeamChecks.LevelViolations(sim.Sel); lvlFrames++; int pairs; var ctx = sim.Ctx(cam); tWorst = Math.Max(tWorst, SeamChecks.MaxTJunctionGap(sim.Sel, sim.Layer, ctx, out pairs)); tPairs += pairs;
            var (cmp, diff) = Checks.EdgeBits(sim.Sel, sim.Layer, ctx); bitsCmp += cmp; bitsDiff += diff;
            if (c == 0 || c == 2 || c == 3)
            {
                string f = $"top_{name}.png"; double half = c == 0 ? 6000 : c == 2 ? 16000 : 300000; Images.TopView(System.IO.Path.Combine(dir, f), idx, sim.Near, cam, half, 1000); images.Add(f);
                if (c == 3) { Images.Overview(System.IO.Path.Combine(dir, "overview_d_land_10km.png"), idx, cam, 1024, 512); images.Add("overview_d_land_10km.png"); }
            }
            Console.WriteLine($"  camera {name}: nodes {sim.Sel.Nodes.Count} tris {sim.Sel.Triangles} {sw.Elapsed.TotalSeconds:F1}s");
        }
        foreach (var (name, path, every1, every8, every2) in new[] { ("drive", cams.Drive, 10, 30, main ? 60 : 600), ("flight", cams.Flight, 10, 30, main ? 60 : 600) })
        {
            sim.Settle(path.At(0)); var gaps = new List<double>(); int nChecked = 0, nBad = 0, miss = 0;
            for (int f = 0; f < Frames; f++)
            {
                var cam = path.At(f); sim.Frame(cam, 2.0);
                if (main && f % every1 == 0)
                {
                    var idx = new CoverageIndex(sim.Sel, sim.Near); var (n1, b1) = Checks.Coverage(idx, cam, 3000, Checks.NearVertices(sim.Near, false), ref st); nChecked += n1; nBad += b1; if (sim.Sel.IncompleteRoots > 0) nBad++;
                }
                if (main && f % every8 == 0 && sim.Sel.Requests.Count == 0 && sim.Sel.Fallbacks == 0) { lvlBad += SeamChecks.LevelViolations(sim.Sel); lvlFrames++; int pairs; var ctx = sim.Ctx(cam); tWorst = Math.Max(tWorst, SeamChecks.MaxTJunctionGap(sim.Sel, sim.Layer, ctx, out pairs)); tPairs += pairs; var (cmp, diff) = Checks.EdgeBits(sim.Sel, sim.Layer, ctx); bitsCmp += cmp; bitsDiff += diff; }
                if (f % every2 == 0 && sim.Sel.Requests.Count == 0) { int m2; gaps.AddRange(Checks.Gaps(sim, cam, false, out m2)); miss += m2; }
            }
            missingAll += miss; gapAll[name + "." + tag] = gaps;
            rows.Add(Row.Report($"f2.gap.{name}.{tag}", $"max {F(gaps.Max())} m; P95 {F(Checks.Pct(gaps, 0.95))} m", $"{gaps.Count} hole-edge vertices on every {every2}th frame of {Frames} (Height cast to float = SurfaceSector height); {miss} vertices without a built level-13 node"));
            if (main) rows.Add(Row.Proto($"f1.coverage.{name}", $"{nBad} violations", "0", nBad == 0, $"{nChecked} directions on every 10th frame of {Frames}: 3000 random in the horizon cap (half uniform, half log-uniform) + all hole-edge vertices; frames with an uncovered region counted as violations"));
            Console.WriteLine($"  {name} seed {seed} done {sw.Elapsed.TotalSeconds:F1}s");
        }
        rows.AddRange(Checks.F7(seed)); Console.WriteLine($"f7 seed {seed} done {sw.Elapsed.TotalSeconds:F1}s");
    }
    rows.Add(Row.Proto("f8.level_balance", $"{lvlBad} violations", "0", lvlBad == 0, $"adjacent selected nodes (same face or across a cube edge) differing by more than 1 level, {lvlFrames} frames (4 fixed cameras + every 30th frame of drive and flight)"));
    rows.Add(Row.Proto("f8.edge_bits", $"{bitsDiff} differ of {bitsCmp}", "0", bitsDiff == 0 && bitsCmp > 0, "edge vertices shared by two adjacent selected nodes of the same level (incl. across cube faces): morphed double position and height bits, same frames"));
    rows.Add(Row.Proto("f8.t_junction", $"{F(tWorst * 1000, "F4")} mm", "< 1 mm", tWorst < 1e-3, $"max distance of a fine node's morphed edge vertex to the coarse neighbour's morphed edge polyline (level l+1 next to level l), {tPairs} fine/coarse edges, same frames"));
    // skirt: calibrate on seed 1 cameras a-d, verify on everything else
    string s1 = Checks.Seeds[0].ToString(); var calib = Checks.Seeds.SelectMany(sd => cams4(sd.ToString())).SelectMany(n => gapAll[n]).ToList(); double skirt = Math.Max(3.0, FarMesh.SkirtDepth(calib.Max()));
    var others = gapAll.Where(kv => !cams4(s1).Contains(kv.Key)).SelectMany(kv => kv.Value).ToList(); var everything = gapAll.SelectMany(kv => kv.Value).ToList(); int covered = everything.Count(g => g <= skirt);
    rows.Insert(0, Row.Proto("f2.skirt", $"skirtM {F(skirt, "F1")} m; covers {covered} of {everything.Count} gaps", "100 %", covered == everything.Count && missingAll == 0, $"skirtM = max(3.0 m, max gap of cameras a-d of all seeds ({F(calib.Max())} m) x 1.25 rounded up to 0.5 m); checked against all {others.Count} other gaps (seed 7 a-d, drive, flight, both seeds; overall max {F(everything.Max())} m); {missingAll} vertices without a far node"));
    File.WriteAllLines(Path2(dir, "rows.csv"), rows.Select(r => r.Csv())); File.WriteAllLines(Path2(dir, "images.txt"), images.Select(i => Sha(Path2(dir, i)) + "  " + i)); File.WriteAllLines(Path2(dir, "cameras.txt"), protoLines);
    Console.WriteLine($"rows.csv: {rows.Count} rows, {sw.Elapsed.TotalSeconds:F1} s"); return 0;
    IEnumerable<string> cams4(string t) => new[] { "a_coast_2m", "b_lakeshore_2m", "c_land_300m", "d_land_10km" }.Select(n => n + "." + t);
}

IEnumerable<string> Describe(Cameras c, PlanetSurface s)
{
    string D(Vec3 d) { int f; double a, b; CubeSphere.ToFaceAB(d, out f, out a, out b); return $"dir ({d.X:F9}, {d.Y:F9}, {d.Z:F9}) face {f} a {a:F5} b {b:F5} sector {SectorId.At(d)} ground {s.Height(d):F1} m"; }
    for (int i = 0; i < 4; i++) yield return $"{c.Names[i]}: {D(c.Dirs[i])}";
    yield return $"drive: great circle through the cube-edge point {D(c.DriveEdge)}, tangent ({c.Drive.T.X:F6}, {c.Drive.T.Y:F6}, {c.Drive.T.Z:F6}), s -1800..+1800 m, 30 m/s";
    yield return $"flight: start {D(c.Flight.E)}, tangent ({c.Flight.T.X:F6}, {c.Flight.T.Y:F6}, {c.Flight.T.Z:F6}), 250 m/s, 5000 m above sea level";
}

int Report()
{
    string md = args[2], csv = args[3], sha = Opt("--sha", "unknown"), hw = Opt("--hw", "unknown"), wall = Opt("--wall", "unknown");
    var rows = File.ReadAllLines(Path2(dir, "rows.csv")).Select(l => l).ToList();
    var costs = Directory.GetFiles(dir, "cost*.csv").OrderBy(f => f).Select(f => File.ReadAllLines(f).Skip(1).Select(l => l.Split(',')).ToDictionary(p => p[0] + "|" + p[1], p => double.Parse(p[2], CultureInfo.InvariantCulture))).ToList();
    double Med(string sc, string m) { var v = costs.Select(c => c[sc + "|" + m]).OrderBy(x => x).ToList(); return v[v.Count / 2]; }
    double Max(string sc, string m) => costs.Max(c => c[sc + "|" + m]);
    string[] scs = { "a_coast_2m", "b_lakeshore_2m", "c_land_300m", "d_land_10km", "drive", "flight" }; var all = new List<Row>();
    foreach (var sc in scs)
    {
        double n = Max(sc, "nodes_max"), t = Max(sc, "tris_max");
        all.Add(Row.Proto($"f3.draw.{sc}", $"{n:F0} nodes; {t:F0} triangles", "<= 480 nodes; <= 250000 triangles", n <= 480 && t <= 250000, $"max over {(sc == "drive" || sc == "flight" ? Frames + " frames" : "2000 frames")}, all around (no frustum), skirt triangles included (max {Max(sc, "skirt_tris_max"):F0}); median nodes {Med(sc, "nodes_median"):F0}; 90x59 deg frustum: max {Max(sc, "fnodes_max"):F0} nodes, {Max(sc, "ftris_max"):F0} triangles"));
        double m = Med(sc, "total_ms_median");
        all.Add(Row.Proto($"f4.select.{sc}", $"median {F(m)} ms", "<= 1.0 ms", m <= 1.0, $"near set update + selection + masks per frame, single thread, median of {costs.Count} processes (P95 {F(Med(sc, "total_ms_p95"))} ms, max {F(Med(sc, "total_ms_max"))} ms; selection alone median {F(Med(sc, "sel_ms_median"))} ms; frustum selection {F(Med(sc, "frustum_ms_median"))} ms)"));
    }
    foreach (var sc in new[] { "drive", "flight" })
    {
        double p95 = Med(sc, "build_ms_p95"), inc = Max(sc, "frames_incomplete"), pl = Max(sc, "frames_parentless");
        all.Add(Row.Proto($"f5.build_p95.{sc}", $"P95 {F(p95)} ms; max {F(Max(sc, "build_ms_max"))} ms", "P95 <= 2.0 ms", p95 <= 2.0, $"node build time per frame (budget 2.0 ms, coarse levels first, then nearest), {Frames} frames; frames that built something: P95 {F(Med(sc, "build_ms_p95_busy"))} ms; {Max(sc, "nodes_built"):F0} nodes built = {Max(sc, "nodes_built_per_s"):F1} per second"));
        all.Add(Row.Proto($"f5.no_gap.{sc}", $"{inc:F0} frames with a gap; {pl:F0} frames with an unbuilt parent", "0", inc == 0 && pl == 0, $"frames in which part of the visible area had no built node (gap) / a requested node whose parent is not built either; {Max(sc, "frames_fallback"):F0} frames drew a built ancestor instead of unbuilt children; start-up at the first pose (unbudgeted): {Med(sc, "startup_nodes"):F0} nodes in {F(Med(sc, "startup_ms"), "F0")} ms"));
    }
    all.Add(Row.Report("f5.prebuild", $"{F(Med("global", "prebuild_ms"), "F0")} ms for {Med("global", "prebuild_nodes"):F0} nodes", $"6 roots + levels 1..3 (single thread); PlanetSurface constructor {F(Med("global", "surface_ctor_ms"), "F0")} ms"));
    double mem = Max("global", "memory_mib_after_e_f");
    all.Add(Row.Proto("f6.memory", $"{F(mem, "F1")} MiB", "<= 64 MiB", mem <= 64, $"managed memory growth after the 4 fixed cameras + drive + flight (GC.GetTotalMemory(true), max over processes): {Max("global", "cache_nodes"):F0} nodes cached, {Max("global", "cache_built"):F0} built; per node {Med("global", "bytes_per_node_heights"):F0} B heights (kept) + {Med("global", "bytes_per_node_upload"):F0} B upload buffers (xyz + morph delta, created at upload, not cached)"));
    var rowsAll = rows.Concat(all.Select(r => r.Csv())).ToList();
    File.WriteAllLines(csv, new[] { "row,kind,value,threshold,result,note" }.Concat(rowsAll), new UTF8Encoding(false));
    var imgs = File.Exists(Path2(dir, "images.txt")) ? File.ReadAllLines(Path2(dir, "images.txt")) : new string[0]; var cam = File.Exists(Path2(dir, "cameras.txt")) ? File.ReadAllLines(Path2(dir, "cameras.txt")) : new string[0];
    int fails = rowsAll.Count(l => l.Split(',').Contains("FAIL"));
    var mdb = new StringBuilder(); string design = Path2(AppContext.BaseDirectory, "../../../../../tools/whatif-farlayer/doc-design.md"); string designText = File.Exists(design) ? File.ReadAllText(design) : "(design text missing)\n";
    mdb.AppendLine("# What If planet far layer (RG-171)\n");
    mdb.AppendLine($"- Hardware: {hw}\n- Commit: {sha}\n- Wall time of the run: {wall} s; FAIL rows: {fails}\n- Library `src/WhatIf.Planet.FarLayer`, console `src/WhatIf.Planet.FarLayer.Experiment`, runner `tools/whatif-farlayer/run-farlayer.ps1`; seeds {string.Join(", ", Checks.Seeds)}; defaults Range13 {new FarParams().Range13} m, morph fraction {new FarParams().MorphFraction}.\n");
    mdb.Append(designText).AppendLine();
    mdb.AppendLine("## Camera directions (seed 20261112 and seed 7)\n"); foreach (var l in cam) mdb.AppendLine("- " + l);
    mdb.AppendLine("\n## Result table\n\n| row | kind | value | threshold | result | note |\n|---|---|---|---|---|---|");
    foreach (var l in rowsAll) { var p = SplitCsv(l); mdb.AppendLine("| " + string.Join(" | ", p.Select(x => x.Replace("|", "/"))) + " |"); }
    mdb.AppendLine("\n## Images (raw folder, git-ignored; sha256)\n"); foreach (var l in imgs) mdb.AppendLine("- `" + l.Substring(66) + "` " + l.Substring(0, 16) + "...");
    File.WriteAllText(md, mdb.ToString(), new UTF8Encoding(false)); Console.WriteLine($"report: {rowsAll.Count} rows, {fails} FAIL rows -> {csv}"); return 0;
}

static List<string> SplitCsv(string l)
{
    var r = new List<string>(); var sb = new StringBuilder(); bool q = false;
    for (int i = 0; i < l.Length; i++) { char c = l[i]; if (q) { if (c == '"') { if (i + 1 < l.Length && l[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); } else if (c == '"') q = true; else if (c == ',') { r.Add(sb.ToString()); sb.Clear(); } else sb.Append(c); }
    r.Add(sb.ToString()); return r;
}
