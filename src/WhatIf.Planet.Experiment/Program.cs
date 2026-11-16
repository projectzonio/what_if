using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WhatIf.Planet;
using WhatIf.Planet.Experiment;

// RG-162: WI-ADR-001 experiment runner (quadtree sectors, row 3 revised). Modes:
//   checks <outdir>            checks (a), (b), (c), r1-r5, w1-w4 (RG-165), deformation -> abc.csv, deform.md (single thread; r5 is timed, so run under the measurement lock)
//   cost <outdir> <n>          check (d) and w5 once -> d_run<n>.csv, w_run<n>.csv
//   report <outdir> <md> <csv> --sha <sha> --hw "<cpu; power>" --wall <s>   reads abc.csv + d_run*.csv + w_run*.csv, writes result table + verdicts
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length < 2) { Console.Error.WriteLine("usage: checks|cost|report <outdir> ..."); return 2; }
string mode = args[0], dir = args[1]; Directory.CreateDirectory(dir);
string Opt(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
var regions = RegionCase.All();

if (mode == "checks")
{
    var sw = Stopwatch.StartNew(); var rows = new List<Row>();
    foreach (var rc in regions) { rows.AddRange(Checks.CheckA(rc)); Console.WriteLine($"a {rc.Name} done {sw.Elapsed.TotalSeconds:F1}s"); }
    rows.AddRange(Checks.CheckACube()); Console.WriteLine($"a cube done {sw.Elapsed.TotalSeconds:F1}s");
    foreach (var rc in regions) { rows.AddRange(Checks.CheckB(rc)); Console.WriteLine($"b {rc.Name} done {sw.Elapsed.TotalSeconds:F1}s"); }
    foreach (var rc in regions) { rows.AddRange(Checks.CheckC(rc)); Console.WriteLine($"c {rc.Name} done {sw.Elapsed.TotalSeconds:F1}s"); }
    rows.AddRange(RoadChecks.R1R2R3()); Console.WriteLine($"r1-r3 done {sw.Elapsed.TotalSeconds:F1}s");
    rows.AddRange(RoadChecks.R4()); Console.WriteLine($"r4 done {sw.Elapsed.TotalSeconds:F1}s");
    rows.AddRange(RoadChecks.R5()); Console.WriteLine($"r5 done {sw.Elapsed.TotalSeconds:F1}s");
    rows.AddRange(WorldGenChecks.Run()); Console.WriteLine($"w1-w4 done {sw.Elapsed.TotalSeconds:F1}s");
    File.WriteAllText(Path.Combine(dir, "deform.md"), Deform.Report()); Console.WriteLine($"deformation report done {sw.Elapsed.TotalSeconds:F1}s");
    File.WriteAllLines(Path.Combine(dir, "abc.csv"), rows.Select(r => r.Csv()));
    Peak(dir, "checks", sw.Elapsed.TotalSeconds);
    Console.WriteLine($"abc.csv: {rows.Count} rows, {sw.Elapsed.TotalSeconds:F1} s");
    return 0;
}
if (mode == "roads")
{
    foreach (var r in RoadChecks.R1R2R3()) Console.WriteLine(r.Csv());
    return 0;
}
if (mode == "worldgen")   // RG-165 quick look: w1-w4 rows + one w5 pass, no files
{
    foreach (var r in WorldGenChecks.Run()) Console.WriteLine(r.Csv());
    WorldGenChecks.CostRun();
    return 0;
}
if (mode == "cost")
{
    string n = args[2]; var lines = new List<string> { "region,seed,median_ms,p95_ms" };
    foreach (var rc in regions) foreach (ulong seed in Checks.Seeds)
        {
            double med, p95; Checks.CostRun(rc, seed, out med, out p95);
            lines.Add($"{rc.Name},{seed},{med:F5},{p95:F5}"); Console.WriteLine($"d run {n} {rc.Name} seed {seed}: median {med:F4} ms, P95 {p95:F4} ms");
        }
    File.WriteAllLines(Path.Combine(dir, $"d_run{n}.csv"), lines);
    File.WriteAllLines(Path.Combine(dir, $"w_run{n}.csv"), WorldGenChecks.CostRun());
    Peak(dir, "cost" + n, -1);
    return 0;
}
if (mode == "report") return Report();
Console.Error.WriteLine("unknown mode " + mode); return 2;

int Report()
{
    string md = args[2], csv = args[3], sha = Opt("--sha", "unknown"), hw = Opt("--hw", "unknown"), wall = Opt("--wall", "unknown");
    var rows = new List<Row>();
    foreach (var line in File.ReadAllLines(Path.Combine(dir, "abc.csv")))
    {
        var p = line.Split(','); rows.Add(new Row { Check = p[0], Region = p[1], Seed = p[2], Metric = p[3], Measured = p[4], Threshold = p[5], Pass = p[6] == "PASS", Diag = p[7] == "diag" });
    }
    var runs = Directory.GetFiles(dir, "d_run*.csv").OrderBy(f => f).Select(f => File.ReadAllLines(f).Skip(1).Select(l => l.Split(',')).ToList()).ToList();
    double maxSpread = 0;
    foreach (var rc in regions) foreach (ulong seed in Checks.Seeds)
        {
            var meds = runs.Select(r => double.Parse(r.First(x => x[0] == rc.Name && x[1] == seed.ToString())[2])).ToList();
            var p95s = runs.Select(r => double.Parse(r.First(x => x[0] == rc.Name && x[1] == seed.ToString())[3])).ToList();
            double med = Median(meds), p95 = Median(p95s), spread = (meds.Max() - meds.Min()) / med; maxSpread = Math.Max(maxSpread, spread);
            double share = 38 * med / 10.0;      // sectors/s * ms/sector / 1000 ms/s * 100 %
            string s = seed.ToString(), n = runs.Count + " runs";
            rows.Add(new Row { Check = "d", Region = rc.Name, Seed = s, Metric = $"median ms/sector (median of {n} medians: {string.Join(" / ", meds.Select(m => m.ToString("F4")))}; spread {spread * 100:F1} %)", Measured = med.ToString("F4"), Threshold = "<= 0.5", Pass = med <= 0.5 });
            rows.Add(new Row { Check = "d", Region = rc.Name, Seed = s, Metric = $"P95 ms/sector (median of {n} P95s: {string.Join(" / ", p95s.Select(m => m.ToString("F4")))})", Measured = p95.ToString("F4"), Threshold = "<= 3", Pass = p95 <= 3 });
            rows.Add(new Row { Check = "d", Region = rc.Name, Seed = s, Metric = "CPU share of one core at 900 km/h / 5 km (38 sectors/s x median) %", Measured = share.ToString("F3"), Threshold = "< 2", Pass = share < 2 });
        }
    var wruns = Directory.GetFiles(dir, "w_run*.csv").OrderBy(f => f).Select(f => File.ReadAllLines(f).Skip(1).Select(l => l.Split(',')).ToList()).ToList();
    if (wruns.Count == 0) rows.Add(new Row { Check = "w5", Region = "all", Seed = "all", Metric = "w_run*.csv missing", Measured = "-", Threshold = "< 3", Pass = false });
    else foreach (var (cname, _) in WorldGenChecks.Cases()) foreach (ulong seed in Checks.Seeds)
            {
                string s = seed.ToString(); var x = wruns.Select(r => r.First(y => y[0] == cname && y[1] == s)).ToList();
                List<double> Col(int i) => x.Select(y => double.Parse(y[i])).ToList();
                double med = Median(Col(2)), p95 = Median(Col(3)); string n = wruns.Count + " runs";
                rows.Add(new Row { Check = "w5", Region = cname, Seed = s, Metric = $"adapter build of one sector (input + TerrainModel + 65 x 65 heights) median ms (median of {n} medians: {string.Join(" / ", Col(2).Select(m => m.ToString("F4")))})", Measured = med.ToString("F4"), Threshold = "< 3", Pass = med < 3 });
                rows.Add(new Row { Check = "w5", Region = cname, Seed = s, Metric = $"adapter build P95 ms (median of {n} P95s: {string.Join(" / ", Col(3).Select(m => m.ToString("F4")))})", Measured = p95.ToString("F4"), Threshold = "< 3", Pass = p95 < 3 });
                rows.Add(new Row { Check = "w5", Region = cname, Seed = s, Metric = "parts median ms: SectorPlanetInput / TerrainModel construction / 65 x 65 heights (median of the run medians)", Measured = $"{Median(Col(4)):F4} / {Median(Col(5)):F4} / {Median(Col(6)):F4}", Threshold = "report", Pass = true, Diag = true });
            }
    var peaks = Directory.GetFiles(dir, "peak_*.txt").OrderBy(f => f).Select(f => File.ReadAllText(f).Trim()).ToList();
    rows.Add(new Row { Check = "peak", Region = "all", Seed = "all", Metric = "experiment processes (RG-167 notebook budget): peak working set MB; managed heap MB at the end; wall s (checks only)", Measured = peaks.Count == 0 ? "missing" : string.Join(" / ", peaks), Threshold = "report", Pass = true, Diag = true });
    File.WriteAllLines(csv, new[] { "check,region,seed,metric,measured,threshold,pass,kind" }.Concat(rows.Select(r => r.Csv())), new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(dir, "spread.txt"), maxSpread.ToString("F4"));

    string[] rule =
    {
        "a|(a) fails -> the canonical-vertex rule is wrong: fix it inside RG-162 (a bug, not a re-decision).",
        "b|(b) fails -> the int64 lattice rule (row 4) is wrong.",
        "c|(c) fails at the corner -> add the missing cube-edge rotation.",
        "d|(d) fails -> keep the planet layer coarser and restrict fine sectors.",
        "r1|r1 fails -> portal s / position is not canonical; fix in Border.Between / Portal.Cross.",
        "r2|r2 fails -> the snap rule needs another distance or the threshold was a wrong O guess: report to the orchestrator.",
        "r3|r3 fails -> heading is not computed on a canonical tangent.",
        "r4|r4 fails -> the fillet / region-frame refinement needs another construction.",
        "r5|r5 fails -> the global graph exceeds its time or memory budget: report to the orchestrator.",
        "w1|w1 fails -> the planet base is not evaluated from the canonical vertex direction on both sides (adapter bug, RG-165).",
        "w2|w2 fails -> the adapted mesh opens a seam: the taper does not reach 0 on the rim or the frames disagree (adapter bug, RG-165).",
        "w3|w3 fails -> the WorldGen contribution is not 0 on the quad edge: fix the taper (RG-165).",
        "w4|w4 is report only (footprint of the deformed quad in the tangent plane).",
        "w5|w5 fails -> the adapter does not fit the streaming budget (BudgetMs 3 ms): report to the orchestrator.",
    };
    var sb = new StringBuilder();
    sb.AppendLine("# What If planet experiment (RG-162, WI-ADR-001 row 3: per-face quadtree leaf sectors; replaces the RG-161 table; RG-165 adds group w = WorldGen adapter; the region-first groups e and p were removed in RG-185)\n");
    sb.AppendLine($"- Hardware: {hw}\n- Commit: {sha}\n- Wall time of the whole experiment: {wall} s\n- Library `src/WhatIf.Planet` (netstandard2.1; QuadSector, Lattice, Border, Portal, CorridorGraph), console `src/WhatIf.Planet.Experiment`; terrain height = `PlanetNoise.Height` (3D value noise, 4 octaves, 150 m, cells 4096..512 m); WhatIf.WorldGen is reached only through the adapter `src/WhatIf.Planet.WorldGen` (RG-165, group w; doc `what-if-planet-worldgen-adapter.md`).");
    sb.AppendLine($"- Check (d): {runs.Count} separate processes under the measurement lock, single thread, Release, warm; max spread of the medians {maxSpread * 100:F1} %.");
    sb.AppendLine("- Metric change (user 2026-11-12, RG-164): r2 road length is measured only on arcs whose both ends are >= 1 km (along the true arc) from the nearest shifted portal (a portal > 1 cm from its raw crossing); 8 of 3000 arcs (0.27 %) are excluded and reported as diag rows, the old all-arcs maximum 0.5236 % stays visible. The shift line is now: single-portal snap shift (candidate B, `Portal.Snap`) <= 256 m and 0 portals off the segment; candidate A's lateral offset and slide along a border are diag. Geometry unchanged (library not touched).");
    sb.AppendLine($"- Group w (RG-165): w5 timed in the same {wruns.Count} cost processes as check (d) (global warm-up >= 1 s, then 5 warm + 51 timed builds per case and seed); w1-w4 run once in the checks process; sectors: face centre F0.r16384.c16384, cube-edge midpoint F0.r16384.c32767, cube vertex F0.r32767.c32767, each with its 4 neighbours; seeds as (a)-(d); taper band {WhatIf.Planet.WorldGen.SectorAdapter.TaperM:F0} m.");
    sb.AppendLine("- Row kinds: `protocol` rows decide the verdict; `diag` rows are extra evidence and never turn a FAIL into a PASS.\n");
    bool all = true;
    sb.AppendLine("## Verdict per check (decision rule of what-if-planet-adr-draft.md §4)\n\n| check | verdict | protocol rows failing | meaning for WI-ADR-001 |\n|---|---|---|---|");
    foreach (var r in rule)
    {
        string c = r.Split('|')[0]; var pr = rows.Where(x => x.Check == c && !x.Diag).ToList(); int bad = pr.Count(x => !x.Pass); all &= bad == 0;
        sb.AppendLine($"| {c} | {(bad == 0 ? "PASS" : "FAIL")} | {bad} of {pr.Count} | {(bad == 0 ? "check passed" : r.Split('|')[1])} |");
    }
    sb.AppendLine($"\n**Overall: {(all ? "all of (a)-(d), r1-r5, w1-w5 PASS -> the orchestrator may set WI-ADR-001 Accepted." : "NOT all checks pass -> WI-ADR-001 stays Proposed; apply the decision rule of each failing check.")}**\n");
    sb.AppendLine("## Deformation report (report only, no threshold)\n");
    string defPath = Path.Combine(dir, "deform.md"); sb.AppendLine(File.Exists(defPath) ? File.ReadAllText(defPath) : "deform.md missing -> FAIL of the protocol");
    sb.AppendLine("## Result table\n\n| check | region | seed | metric | measured | threshold | pass | kind |\n|---|---|---|---|---|---|---|---|");
    foreach (var r in rows) sb.AppendLine($"| {r.Check} | {r.Region} | {r.Seed} | {r.Metric.Replace("|", "/")} | {r.Measured} | {r.Threshold} | {(r.Pass ? "PASS" : "FAIL")} | {(r.Diag ? "diag" : "protocol")} |");
    sb.AppendLine("\nSeed `all` = the metric does not depend on the seed (geometry only). Same table as CSV: `what-if-planet-experiment.csv`.");
    File.WriteAllText(md, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"verdict {(all ? "ALL PASS" : "NOT ALL PASS")}; rows {rows.Count}; max spread {maxSpread * 100:F1} %");
    return 0;
}

// RG-167: peak working set + managed heap of this process (the notebook is the budget: i3-1215U, shared with the user's rides)
static void Peak(string dir, string name, double wall)
{
    var p = Process.GetCurrentProcess(); p.Refresh();
    File.WriteAllText(Path.Combine(dir, $"peak_{name}.txt"), $"{name} {p.PeakWorkingSet64 / 1048576.0:F0} MB peak; {GC.GetTotalMemory(false) / 1048576.0:F0} MB managed" + (wall >= 0 ? $"; {wall:F0} s" : ""));
}

static double Median(List<double> v) { var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
