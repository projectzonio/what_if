using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using WhatIf.Planet;
using WhatIf.Planet.Surface;
using WhatIf.Planet.Surface.Experiment;

// RG-168: What If planet surface experiment. Modes:
//   checks <outdir>        rows s1-s6 (3 seeds) + the images + det digest file -> rows.csv, images.txt, PNGs (single thread except the image pass)
//   det <outdir> <n>       10000 x 3 sample bits -> det<n>.sha (run in two separate processes, the report compares them)
//   cost <outdir> <n>      row s7 once -> cost<n>.csv (run under the measurement lock, one process per repetition)
//   probe                  quick look: land shares and timing, no files
//   report <outdir> <md> <csv> --sha <sha> --hw "<..>" --wall <s>
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length < 1) { Console.Error.WriteLine("usage: checks|det|cost|probe|report <outdir> ..."); return 2; }
string mode = args[0], dir = args.Length > 1 ? args[1] : "."; if (mode != "probe") Directory.CreateDirectory(dir);
string Opt(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
string Sha(string path) { using (var f = File.OpenRead(path)) return string.Concat(SHA256.Create().ComputeHash(f).Select(b => b.ToString("x2"))); }

if (mode == "probe")
{
    foreach (ulong seed in Checks.Seeds)
    {
        var sw = Stopwatch.StartNew(); var surf = new PlanetSurface(seed); double ctor = sw.Elapsed.TotalMilliseconds; ulong st = 1; var k = new int[5]; int forested = 0, landN = 0;
        sw.Restart(); for (int i = 0; i < 200000; i++) { var o = surf.Sample(Checks.RandomDir(ref st)); k[(int)o.Kind]++; if (o.Kind >= SurfaceKind.Beach) { landN++; if (o.Forest >= 0.5f) forested++; } }
        Console.WriteLine($"seed {seed}: ctor {ctor:F1} ms thr {surf.SeaThreshold:F4}; 200k samples {sw.Elapsed.TotalMilliseconds:F0} ms ({sw.Elapsed.TotalMilliseconds * 1e6 / 200000:F0} ns); ocean {k[0]} sea {k[1]} beach {k[2]} land {k[3]} lake {k[4]}; land {100.0 * landN / 200000:F1} % forest {100.0 * forested / Math.Max(landN, 1):F1} %");
        var h = new float[65 * 65]; var kk = new byte[65 * 65]; var f = new float[65 * 65]; var ids = new List<SectorId>(); for (int i = 0; i < 300; i++) ids.Add(SectorId.At(Checks.FindLand(surf, ref st)));
        for (int i = 0; i < 20; i++) SurfaceSector.Sample(surf, ids[i], h, kk, f);
        var t = new List<double>(); foreach (var id in ids) { sw.Restart(); SurfaceSector.Sample(surf, id, h, kk, f); t.Add(sw.Elapsed.TotalMilliseconds); }
        t.Sort(); Console.WriteLine($"  land sector median {t[150]:F3} ms P95 {t[285]:F3} ms");
    }
    return 0;
}
if (mode == "strip")
{
    var surf = new PlanetSurface(20261112); ulong st = 4242; Vec3 d0 = Checks.FindLand(surf, ref st); var id0 = SectorId.At(d0);
    var h = new float[65 * 65]; var kk = new byte[65 * 65]; var f = new float[65 * 65]; var sw = Stopwatch.StartNew();
    for (int i = 0; i < 100; i++) { var id = new SectorId(id0.Face, Math.Min(id0.Row + i / 10, 32767), Math.Min(id0.Col + i % 10, 32767)); SurfaceSector.Sample(surf, id, h, kk, f); }
    for (int i = 0; i < 100; i++) { var id = new SectorId(id0.Face, Math.Min(id0.Row + i / 10, 32767), Math.Min(id0.Col + i % 10, 32767)); sw.Restart(); SurfaceSector.Sample(surf, id, h, kk, f); double ms = sw.Elapsed.TotalMilliseconds; if (i % 11 == 0) { var cnt = new int[5]; foreach (var k in kk) cnt[k]++; double mx = 0; foreach (var v in f) mx = Math.Max(mx, v); Console.WriteLine($"{id} {ms:F3} ms kinds {string.Join("/", cnt)} hmax {h.Max():F0} forestmax {mx:F2} lakes {surf.LakeCacheCount}"); } }
    return 0;
}
if (mode == "det")
{
    File.WriteAllText(Path.Combine(dir, $"det{args[2]}.sha"), string.Concat(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(Checks.DetDigest())).Select(b => b.ToString("x2"))));
    return 0;
}
if (mode == "cost")
{
    var all = new List<string> { "metric,median_or_value,p95_or_note,note" };
    foreach (ulong seed in Checks.Seeds) foreach (var l in Checks.Cost(seed)) all.Add($"{seed},{l}");
    File.WriteAllLines(Path.Combine(dir, $"cost{args[2]}.csv"), all);
    foreach (var l in all) Console.WriteLine(l);
    return 0;
}
if (mode == "checks")
{
    var sw = Stopwatch.StartNew(); var rows = new List<Row>(); var images = new List<string>();
    rows.AddRange(Checks.S1()); Console.WriteLine($"s1 done {sw.Elapsed.TotalSeconds:F1}s");
    foreach (ulong seed in Checks.Seeds)
    {
        var map = Maps.Planet(seed, 2048, 1024, true);
        rows.AddRange(Checks.S2S5(seed, map)); Console.WriteLine($"s2/s5 seed {seed} done {sw.Elapsed.TotalSeconds:F1}s");
        rows.AddRange(Checks.S4(seed)); Console.WriteLine($"s4 seed {seed} done {sw.Elapsed.TotalSeconds:F1}s");
        rows.AddRange(Checks.S6(seed)); Console.WriteLine($"s6 seed {seed} done {sw.Elapsed.TotalSeconds:F1}s");
        var surf = new PlanetSurface(seed); ulong st = seed * 5 + 11;
        string tag = "seed" + seed;
        Maps.WritePng(Path.Combine(dir, $"planet_{tag}.png"), 2048, 1024, Maps.Rgb(map, 2 * Math.PI * CubeSphere.Radius / 2048, 6.0)); images.Add($"planet_{tag}.png");
        // cube faces: 512 x 512 each (one image per face)
        Parallel.For(0, 6, f => { var s2 = new PlanetSurface(seed); var fr = Maps.CubeFace(s2, f, 512); Maps.WritePng(Path.Combine(dir, $"cube_{tag}_f{f}.png"), 512, 512, Maps.Rgb(fr, 10000000.0 / 512, 6.0)); });
        for (int f = 0; f < 6; f++) images.Add($"cube_{tag}_f{f}.png");
        // three region-sized crops at 16 m (612 x 612): coast, lake, forested upland
        Vec3 coast = Checks.FindCoast(surf, ref st); var lake = Checks.FindLake(surf, ref st); Vec3 forest = default(Vec3);
        for (int tries = 0; tries < 100000; tries++) { Vec3 d = Checks.FindLand(surf, ref st); var o = surf.Sample(d); if (o.HeightM > 300 && o.HeightM < 1500 && o.Forest > 0.8f) { forest = d; break; } }
        var crops = new List<(string, Vec3)> { ("coast", coast), ("lake", lake.Centre), ("forest", forest) };
        foreach (var (name, c) in crops)
        {
            if (c.Length == 0) { rows.Add(Row.Report("img." + name + "." + tag, "none found", "no forested upland found")); continue; }
            var r = Maps.Tangent(surf, c, 612, 16.0);
            Maps.WritePng(Path.Combine(dir, $"region_{name}_{tag}.png"), 612, 612, Maps.Rgb(r, 16.0, 6.0)); images.Add($"region_{name}_{tag}.png");
            rows.AddRange(Checks.S3(name + "." + tag, r, $"crop of 612 x 612 at 16 m around {SectorId.At(c)} (level-10 region {CubeSphere.RegionOf(c, 10)})"));
        }
        double th0 = lake.Radius(0); var sh = Maps.Tangent(surf, lake.PointAt(0, th0), 500, 4.0);
        Maps.WritePng(Path.Combine(dir, $"lakeshore_{tag}.png"), 500, 500, Maps.Rgb(sh, 4.0, 8.0)); images.Add($"lakeshore_{tag}.png");
        Console.WriteLine($"images seed {seed} done {sw.Elapsed.TotalSeconds:F1}s");
    }
    File.WriteAllLines(Path.Combine(dir, "rows.csv"), rows.Select(r => r.Csv()));
    File.WriteAllLines(Path.Combine(dir, "images.txt"), images.Select(i => Sha(Path.Combine(dir, i)) + "  " + i));
    Console.WriteLine($"rows.csv: {rows.Count} rows, {sw.Elapsed.TotalSeconds:F1} s");
    return 0;
}
if (mode == "report") return Report();
Console.Error.WriteLine("unknown mode " + mode); return 2;

int Report()
{
    string md = args[2], csv = args[3], sha = Opt("--sha", "unknown"), hw = Opt("--hw", "unknown"), wall = Opt("--wall", "unknown");
    var lines = File.ReadAllLines(Path.Combine(dir, "rows.csv")).ToList();
    // s1 two-process determinism
    var d1 = Directory.GetFiles(dir, "det*.sha").OrderBy(f => f).Select(File.ReadAllText).ToList();
    bool same = d1.Count >= 2 && d1.All(x => x == d1[0]);
    lines.Insert(0, new Row { Id = "s1.two_process", Kind = "protocol", Value = same ? "0 differing bits" : "DIFFERENT", Threshold = "0", Result = same ? "PASS" : "FAIL", Note = $"3 x 10000 random directions evaluated in {d1.Count} separate processes: sha256 of all sample bits {(d1.Count > 0 ? d1[0].Substring(0, 16) : "-")}..." }.Csv());
    // s7 from cost<n>.csv: median over processes of each metric
    var costs = Directory.GetFiles(dir, "cost*.csv").OrderBy(f => f).Select(f => File.ReadAllLines(f).Skip(1).Select(l => l.Split(',')).ToList()).ToList();
    var sb = new StringBuilder();
    double Med(IEnumerable<double> v) { var a = v.OrderBy(x => x).ToList(); return a[a.Count / 2]; }
    string[] sets = { "land_strip", "land_scattered", "coast", "lake_shore", "ocean" };
    var costRows = new List<Row>(); double worstMed = 0;
    foreach (ulong seed in Checks.Seeds)
        foreach (var set in sets)
        {
            var med = costs.Select(c => double.Parse(c.First(x => x[0] == seed.ToString() && x[1] == set)[2])).ToList(); var p95 = costs.Select(c => double.Parse(c.First(x => x[0] == seed.ToString() && x[1] == set)[3])).ToList();
            double m = Med(med), spread = (med.Max() - med.Min()) / m; if (set != "ocean") worstMed = Math.Max(worstMed, m);
            costRows.Add(Row.Report($"s7.sector.{set}.{seed}", $"median {m:F3} ms; P95 {Med(p95):F3} ms", $"median of {costs.Count} processes ({string.Join(" / ", med.Select(x => x.ToString("F3")))}); spread {spread * 100:F1} %"));
        }
    var all = new List<Row>(); all.AddRange(costRows);
    all.Insert(0, Row.Proto("s7.sector_median", $"{worstMed:F3} ms", "<= 1.0 ms (RG-169; was 1.5)", worstMed <= 1.0, "SurfaceSector.Sample 65 x 65, single thread, worst median over land / coast / lake-shore sets and 3 seeds (median of the processes); 100 sectors per set, caches warm, JIT warm"));
    var hn = costs.Select(c => double.Parse(c.First(x => x[1] == "height_ns_per_call")[2])).ToList();
    all.Add(Row.Report("s7.height_ns", $"{Med(hn):F0} ns/call", "Height(dir) on directions inside 100 land sectors (cache-friendly walk order is NOT used: random vertices), median of all seed x process runs"));
    var mem = costs.SelectMany(c => c.Where(x => x[1] == "memory_mib_after_1000_sectors")).Select(x => double.Parse(x[2])).ToList();
    all.Add(Row.Proto("s7.memory", $"{mem.Max():F3} MiB", "<= 16 MiB", mem.Max() <= 16, "managed memory of one PlanetSurface (layer + lake-cell caches) after 1000 random sectors (max over seeds and processes)"));
    var mp = costs.SelectMany(c => c.Where(x => x[1] == "planet_map_2048x1024_s")).Select(x => double.Parse(x[2])).ToList();
    all.Add(Row.Report("s7.planet_map", $"{Med(mp):F2} s", "2048 x 1024 equirectangular map, single thread (about 2.1 M samples)"));
    var rowsAll = lines.Concat(all.Select(r => r.Csv())).ToList();
    File.WriteAllLines(csv, new[] { "row,kind,value,threshold,result,note" }.Concat(rowsAll), new UTF8Encoding(false));
    var imgs = File.Exists(Path.Combine(dir, "images.txt")) ? File.ReadAllLines(Path.Combine(dir, "images.txt")) : new string[0];
    int fails = rowsAll.Count(l => l.Split(',').Contains("FAIL"));
    File.WriteAllText(Path.Combine(dir, "report_meta.txt"), $"{sha}\n{hw}\n{wall}\n{fails}\n");
    File.WriteAllLines(Path.Combine(dir, "report_images.txt"), imgs);
    var mdb = new StringBuilder();
    string design = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "whatif-surface", "doc-design.md");
    string designText = File.Exists(design) ? File.ReadAllText(design) : "(design text missing)\n";
    mdb.AppendLine("# What If planet surface layer (RG-168)\n");
    mdb.AppendLine($"- Hardware: {hw}\n- Commit: {sha}\n- Wall time of the run: {wall} s; FAIL rows: {fails}\n- Library `src/WhatIf.Planet.Surface`, console `src/WhatIf.Planet.Surface.Experiment`, runner `tools/whatif-surface/run-surface.ps1`; seeds {string.Join(", ", Checks.Seeds)}.\n");
    mdb.Append(designText).AppendLine();
    mdb.AppendLine("## Result table\n\n| row | kind | value | threshold | result | note |\n|---|---|---|---|---|---|");
    foreach (var l in rowsAll) { var p = SplitCsv(l); mdb.AppendLine("| " + string.Join(" | ", p.Select(x => x.Replace("|", "/"))) + " |"); }
    mdb.AppendLine("\n## Images (raw folder, git-ignored; sha256)\n");
    foreach (var l in imgs) mdb.AppendLine("- `" + l.Substring(66) + "` " + l.Substring(0, 16) + "...");
    File.WriteAllText(md, mdb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"report: {rowsAll.Count} rows, {fails} FAIL rows -> {csv}");
    return 0;
}

static List<string> SplitCsv(string l)
{
    var r = new List<string>(); var sb = new StringBuilder(); bool q = false;
    for (int i = 0; i < l.Length; i++) { char c = l[i]; if (q) { if (c == '"') { if (i + 1 < l.Length && l[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); } else if (c == '"') q = true; else if (c == ',') { r.Add(sb.ToString()); sb.Clear(); } else sb.Append(c); }
    r.Add(sb.ToString()); return r;
}
