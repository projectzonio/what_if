using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Anchors.Experiment;
using WhatIf.Planet.Runtime;
using WhatIf.Planet.Surface;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Runtime.Experiment
{
    /// <summary>
    /// RG-184 B5: the runtime t-rows of what-if-planet-runtime.md on anchor content (PlanetStreamer with the ISectorContent constructor, AnchorWorld.Prepare per region) along a
    /// city-to-city road path (streets of the settlement worlds + corridors). Modes, each its own process: cpath (pick + save the path), cdrive --speed v (t1-t4, t6, samples),
    /// cverify --speed v (t8 against a fresh anchor world; t9 road continuity at the crossed old region borders), ccold (t5), cflight (t1-flight, t7, flight coverage), creport.
    /// </summary>
    public static class AnchorRun
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v, string f = "F3") => Stats.F(v, f);
        const int MinBorders = 3;

        sealed class SavedPath
        {
            public readonly List<Vec3> Pts = new List<Vec3>(); readonly List<double> cum = new List<double>(); public double LengthM; public string Info = "";
            public void Finish() { cum.Clear(); double c = 0; cum.Add(0); for (int i = 1; i < Pts.Count; i++) { c += SettlementField.Gc(Pts[i - 1], Pts[i]); cum.Add(c); } LengthM = c; }
            public Vec3 DirAtS(double s)
            {
                s = Math.Max(0, Math.Min(LengthM, s)); int lo = 0, hi = cum.Count - 1;
                while (hi - lo > 1) { int m = (lo + hi) / 2; if (cum[m] <= s) lo = m; else hi = m; }
                double span = cum[hi] - cum[lo], t = span > 0 ? (s - cum[lo]) / span : 0;
                return (Pts[lo] * (1 - t) + Pts[hi] * t).Normalized();
            }
        }

        static SavedPath Load(string dir)
        {
            var p = new SavedPath(); var lines = File.ReadAllLines(Path.Combine(dir, "cpath.txt"));
            p.Info = lines[0];
            foreach (var l in lines.Skip(1)) { var q = l.Split(' '); p.Pts.Add(new Vec3(double.Parse(q[0], Inv), double.Parse(q[1], Inv), double.Parse(q[2], Inv))); }
            p.Finish(); return p;
        }

        static PlanetStreamer Streamer(AnchorWorld aw) => new PlanetStreamer(id => { aw.Prepare(id); return new AnchorContent(aw); }, Scenarios.HeightFunc());

        public static int Run(string mode, string dir, Func<string, string, string> opt)
        {
            switch (mode)
            {
                case "cpath": return PathMode(dir);
                case "cdrive": return Drive(dir, double.Parse(opt("--speed", "120"), Inv));
                case "cverify": return Verify(dir, double.Parse(opt("--speed", "120"), Inv), opt("--t9", "yes") == "yes");
                case "ccold": return Cold(dir);
                case "cflight": return Flight(dir);
                case "ct9": WriteRows(dir, "ct9", new List<Row> { Continuity(dir, Load(dir)) }); return 0;
            }
            return 2;
        }

        static void WriteRows(string dir, string name, List<Row> rows)
        {
            File.WriteAllLines(Path.Combine(dir, "rows-" + name + ".txt"), rows.Select(r => r.Line()));
            foreach (var r in rows) Console.WriteLine($"{r.Result,-6} {r.Id,-18} {r.Value} [{r.Threshold}] {r.Note}");
        }

        /// <summary>Picks the city-to-city path over both RG-177 areas (fresh planner anchor world that keeps the street graphs) and saves it as unit directions (R format).</summary>
        static int PathMode(string dir)
        {
            CorridorPath best = null; string area = "";
            foreach (var a in Anchors.Experiment.Program.Areas())
            {
                var surf = new PlanetSurface(Scenarios.Seed); var set = AnchorSet.Build(new SettlementField(Scenarios.Seed, surf), new CorridorBuilder(surf), a.Regions, false);
                var planner = new AnchorWorld(Scenarios.Seed) { KeepGraphs = true };
                var p = CorridorPath.CityToCity(planner, set, MinBorders);
                Console.WriteLine($"area {a.Name}: {(p == null ? "no city pair joined by routed corridors" : $"{p.LengthM / 1000:F1} km, {p.RegionBorders} region borders, {p.FaceEdges} cube edges: {p.Description}")}");
                if (p == null) continue;
                int S(CorridorPath x) => (x.RegionBorders >= MinBorders ? 2 : 0) + (x.FaceEdges > 0 ? 1 : 0);
                if (best == null || S(p) > S(best) || (S(p) == S(best) && p.LengthM < best.LengthM)) { best = p; area = a.Name; }
            }
            if (best == null) { Console.Error.WriteLine("no city-to-city path"); return 3; }
            var lines = new List<string> { $"area {area}; {best.LengthM / 1000:F1} km, {best.RegionBorders} old region borders, {best.FaceEdges} cube edges; {best.Description}" };
            lines.AddRange(best.Pts.Select(v => $"{v.X.ToString("R", Inv)} {v.Y.ToString("R", Inv)} {v.Z.ToString("R", Inv)}"));
            File.WriteAllLines(Path.Combine(dir, "cpath.txt"), lines);
            var cr = best.BorderCrossings();
            File.WriteAllLines(Path.Combine(dir, "cpath-borders.txt"), cr.Select(c => $"{c.S.ToString("R", Inv)} {c.From} {c.To}"));
            Console.WriteLine(lines[0]); return 0;
        }

        static int Drive(string dir, double speed)
        {
            var path = Load(dir); const double Warm = 10.0; string sfx = "-c" + speed.ToString("F0", Inv);
            double secs = Math.Floor(path.LengthM / speed - 1);
            Console.WriteLine($"city-to-city drive {speed} m/s, {secs:F0} s: {path.Info}");
            var rows = new List<Row> { Row.Report("c-path" + sfx, $"{path.LengthM / 1000:F1} km at {speed} m/s, {secs:F0} s", path.Info) };
            Func<double, Vec3> dirAt = t => path.DirAtS(speed * t);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long gc0 = GC.GetTotalMemory(true);
            var aw = new AnchorWorld(Scenarios.Seed); var hs = Scenarios.HeightFunc();
            using (var s = Streamer(aw))
            {
                var samples = new ConcurrentQueue<string>(); int cnt = 0;
                s.OnSectorBuilt = (id, d) => { if (Interlocked.Increment(ref cnt) > 0 && samples.Count < 300) samples.Enqueue($"{id.Face} {id.Row} {id.Col} {SectorHash.Of(d)}"); };
                int pre = s.PrebuildFar(3); double settleS = Scenarios.Settle(s, dirAt(0), 0.0, 180, true);
                Console.WriteLine($"far prebuild {pre} nodes; settled after {settleS:F1} s; regions ready {s.ReadyRegionCount}");
                var r = Scenarios.Run(s, dirAt, 0.0, secs, Warm, true, true, hs);
                long gcEnd = GC.GetTotalMemory(true); long cacheEnd = s.CacheBytes;
                File.WriteAllText(Path.Combine(dir, "cdrive-frames" + sfx + ".csv"), Scenarios.Csv(r, "cdrive"));
                File.WriteAllLines(Path.Combine(dir, "samples" + sfx + ".txt"), samples.Take(260));
                File.WriteAllLines(Path.Combine(dir, "cdrive-slow-frames" + sfx + ".txt"), r.SlowFrames);
                double p95 = Stats.Pct(r.FrameMs, 0.95), p999 = Stats.Pct(r.FrameMs, 0.999);
                rows.Add(Row.Proto("t1" + sfx, $"P95 {F(p95)} ms / P99.9 {F(p999)} ms / max {F(r.FrameMs.Max())} ms, {r.FrameMs.Count(x => x > 4.0)} frames > 4 ms", "P95 <= 1.0 ms, P99.9 <= 2.0 ms (frames > 4 ms reported)", p95 <= 1.0 && p999 <= 2.0,
                    $"{r.Frames} frames; median {F(Stats.Pct(r.FrameMs, 0.5))} ms, P99 {F(Stats.Pct(r.FrameMs, 0.99))} ms; GC gen0/1/2 {r.GcC0}/{r.GcC1}/{r.GcC2}, pause {F(r.GcPauseMs, "F0")} ms; without frames that built far nodes P95 {F(Stats.Pct(r.FrameMsNoFar, 0.95))} ms; slow frames: {string.Join("; ", r.SlowFrames.Take(5))}"));
                rows.Add(Row.Proto("t2-holes" + sfx, $"{r.NotReady300AfterWarm} frames", "= 0", r.NotReady300AfterWarm == 0, $"frames after the first {Warm:F0} s with a wanted sector inside 300 m that is not ready"));
                rows.Add(Row.Proto("t2-coverage" + sfx, $"{r.CoverageViolationsAfterWarm} violations", "= 0", r.CoverageViolationsAfterWarm == 0, $"far coverage exactly once, every 10th frame after {Warm:F0} s: {r.CoverageChecksAfterWarm} checks x 3000 random directions + every 8th vertex of all near sectors ({r.NearVerticesChecked} vertices); all {r.CoverageChecks} checks: {r.CoverageViolations}"));
                rows.Add(Row.Report("t2-waiting" + sfx, $"max {r.MaxWaiting600} sectors, max wait {F(s.RegionWaitS.Count > 0 ? s.RegionWaitS.Max() : 0, "F2")} s", $"sectors inside 600 m that waited for their region's anchor preparation: {s.RegionWaitS.Count}; most not ready inside 600 m in one frame: {r.MaxNotReady600}"));
                var leads = r.Entries.Where(e => !double.IsNaN(e.ReadyS)).Select(e => e.EnterS - e.ReadyS).ToList(); int notReady = r.Entries.Count(e => double.IsNaN(e.ReadyS) || e.ReadyS > e.EnterS);
                double minLead = leads.Count > 0 ? leads.Min() : double.NaN;
                rows.Add(Row.Proto("t3-lead" + sfx, $"min lead {F(minLead, "F1")} s, {notReady} entries without a ready region", "lead >= 5 s, 0 without", notReady == 0 && minLead >= 5.0, $"{r.Entries.Count} region entries: {string.Join("; ", r.Entries.Select(e => e.Id + " lead " + F(e.EnterS - e.ReadyS, "F1") + " s"))}"));
                double wall = r.WallS; var A = s.StatsA; var B = s.StatsB; var lat = s.LatencyS;
                rows.Add(Row.Report("t4" + sfx, $"B {F(B.SectorsBuilt / wall, "F1")} sectors/s (max {F(B.MaxSectorMs, "F1")} ms each), {A.RegionsBuilt} region preparations (longest {F(A.MaxRegionS, "F2")} s)", $"request-to-ready P95 {F(Stats.Pct(lat, 0.95))} s, max {F(lat.Count > 0 ? lat.Max() : 0)} s; queue max {s.MaxQueue}; A busy {F(100.0 * A.BusyTicks / Stopwatch.Frequency / wall, "F1")} %, B busy {F(100.0 * B.BusyTicks / Stopwatch.Frequency / wall, "F1")} %; process CPU {F(r.ProcCpuS / wall * 100, "F0")} % of one core"));
                double slopeA = Stats.Slope(r.MemT.Where(t => t >= secs - 100).ToList(), r.MemMiB.Where((m, i) => r.MemT[i] >= secs - 100).ToList()), slopeGc = (gcEnd - r.Gc200) / 1048576.0 / 100.0, dMiB = (gcEnd - gc0) / 1048576.0;
                rows.Add(Row.Proto("t6-memory" + sfx, $"{F(dMiB, "F1")} MiB managed after the drive", "<= 150 MiB", dMiB <= 150.0, $"GC.GetTotalMemory(true) after minus before the streamer and its anchor world; accounted cache {F(cacheEnd / 1048576.0, "F1")} MiB; anchor world: {aw.RoutedCount} routes, {aw.WorldCount} settlement worlds cached ({aw.CachedWorldBytes / 1048576.0:F1} MiB est., cap {aw.WorldByteCap >> 20} MiB), built {aw.WorldsBuilt}, evicted {aw.WorldsEvicted}"));
                rows.Add(Row.Proto("t6-slope" + sfx, $"{F(slopeGc, "F3")} MiB/s (GC), {F(slopeA, "F3")} MiB/s (accounted)", "<= 0.1 MiB/s", slopeGc <= 0.1 && slopeA <= 0.1, $"last 100 s: {(r.Gc200 - gc0) / 1048576.0:F1} -> {dMiB:F1} MiB"));
            }
            WriteRows(dir, "cdrive" + sfx, rows);
            return 0;
        }

        static int Verify(string dir, double speed, bool t9)
        {
            string sfx = "-c" + speed.ToString("F0", Inv); var rows = new List<Row>();
            var all = File.ReadAllLines(Path.Combine(dir, "samples" + sfx + ".txt")).Where(l => l.Length > 0).Select(l => l.Split(' ')).Select(p => (Id: new SectorId(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2])), Hash: p[3])).Take(200).ToList();
            var aw = new AnchorWorld(Scenarios.Seed); int same = 0; var diff = new List<string>(); var o = new AdaptedSector(); var sw = Stopwatch.StartNew();
            foreach (var x in all.AsEnumerable().Reverse()) { AnchorSector.Build(aw, new QuadSector(x.Id), o); if (SectorHash.Of(o) == x.Hash) same++; else diff.Add(x.Id.ToString()); }
            rows.Add(Row.Proto("t8-determinism" + sfx, $"{same}/{all.Count} bit-identical", "= 200/200", all.Count >= 200 && same == all.Count, $"sectors sampled from the drive streamer (every build up to 300, hashed on the worker) vs AnchorSector.Build in this fresh process, fresh anchor world, reverse order ({sw.Elapsed.TotalSeconds:F0} s){(diff.Count > 0 ? "; differing: " + string.Join(", ", diff.Take(5)) : "")}"));
            if (t9) rows.Add(Continuity(dir, Load(dir)));
            WriteRows(dir, "cverify" + sfx, rows);
            return 0;
        }

        /// <summary>t9: at every old region border the path crosses, the lattice vertices within 2 m of the path centre line (sampled every 1 m over +-10 m around the crossing,
        /// sectors built by a fresh anchor world) must carry Surface 1 (corridor) or 2 (street). A crossing with any other code, or with no vertex within 2 m, is a gap.</summary>
        static Row Continuity(string dir, SavedPath path)
        {
            var cr = File.ReadAllLines(Path.Combine(dir, "cpath-borders.txt")).Where(l => l.Length > 0).Select(l => l.Split(' ')).Select(p => (S: double.Parse(p[0], Inv), From: p[1], To: p[2])).ToList();
            var aw = new AnchorWorld(Scenarios.Seed); var cache = new Dictionary<SectorId, AdaptedSector>(); int gaps = 0; var notes = new List<string>(); int checkedV = 0;
            AdaptedSector Sec(SectorId id) { if (!cache.TryGetValue(id, out var o)) { o = new AdaptedSector(); AnchorSector.Build(aw, new QuadSector(id), o); cache[id] = o; } return o; }
            foreach (var c in cr)
            {
                int n = 0, bad = 0; var codes = new int[4];
                for (double ds = -10; ds <= 10; ds += 1)
                {
                    var q = path.DirAtS(c.S + ds); var id = SectorId.At(q); var o = Sec(id);
                    var qs = new QuadSector(id); int bi = -1; double bd = double.MaxValue;
                    for (int k = 0; k < AdaptedSector.Count; k++) { double d = SettlementField.Gc(q, o.World[k].Normalized()); if (d < bd) { bd = d; bi = k; } }
                    if (bd > 2.0) continue;
                    n++; int code = o.Surface[bi]; codes[code]++; if (code != 1 && code != 2) bad++;
                }
                checkedV += n; bool gap = n == 0 || bad > 0; if (gap) gaps++;
                notes.Add($"{c.From}>{c.To} at {c.S / 1000:F2} km: {n} vertices, codes ground/corridor/street/shoulder {codes[0]}/{codes[1]}/{codes[2]}/{codes[3]}{(gap ? " GAP" : "")}");
            }
            File.WriteAllLines(Path.Combine(dir, "t9-crossings.txt"), notes);
            return Row.Proto("t9-continuity", $"{gaps} gaps of {cr.Count} crossed old region borders", "0 gaps", gaps == 0 && cr.Count > 0, $"{checkedV} vertices within 2 m of the path centre line (+-10 m around each crossing, every 1 m); {string.Join("; ", notes)}");
        }

        static int Cold(string dir)
        {
            var path = Load(dir); var rows = new List<Row>(); Vec3 spawn = path.DirAtS(path.LengthM);   // the destination city centre: settlement world + its corridors cold
            var aw = new AnchorWorld(Scenarios.Seed);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using (var s = Streamer(aw))
            {
                var sw0 = Stopwatch.StartNew(); s.PrebuildFar(3); double preS = sw0.Elapsed.TotalSeconds;
                var sw = Stopwatch.StartNew(); double tRegion = double.NaN, tFirst = double.NaN, tDone = double.NaN; var ms = new List<double>(); double last = 0;
                while (sw.Elapsed.TotalSeconds < 120)
                {
                    double now = sw.Elapsed.TotalSeconds; long a = Stopwatch.GetTimestamp(); s.Update(spawn, 0.0, Math.Max(1e-3, now - last)); ms.Add((Stopwatch.GetTimestamp() - a) * 1000.0 / Stopwatch.Frequency); last = now;
                    if (double.IsNaN(tRegion) && s.IsRegionReady(CubeSphere.RegionOf(spawn, SectorId.RegionLevel))) tRegion = now;
                    if (double.IsNaN(tFirst) && s.Info.Ready > 0) tFirst = now;
                    if (s.NearComplete(600)) { tDone = now; break; }
                    Thread.Sleep(10);
                }
                bool ok = !double.IsNaN(tDone);
                rows.Add(Row.Proto("t5-cold-c", ok ? $"{F(tDone, "F1")} s" : "not complete in 120 s", "<= 30 s", ok && tDone <= 30.0, $"empty caches, fresh anchor world, teleport to the destination city centre ({CubeSphere.RegionOf(spawn, SectorId.RegionLevel)}): region preparation ready after {F(tRegion, "F1")} s, first sector {F(tFirst, "F1")} s, 600 m near set ({s.Info.WantedIn600} sectors) complete {F(tDone, "F1")} s; far prebuild {F(preS, "F1")} s before the clock; main thread Update P95 {F(Stats.Pct(ms, 0.95))} ms / max {F(ms.Max())} ms; anchor world {aw.RoutedCount} routes, {aw.WorldCount} settlement worlds"));
            }
            WriteRows(dir, "ccold", rows);
            return 0;
        }

        static int Flight(string dir)
        {
            const double Speed = 250.0, Seconds = 60.0, Alt = 5000.0; var rows = new List<Row>(); var path = Load(dir);
            Func<double, Vec3> dirAt = t => path.DirAtS(Math.Min(path.LengthM, Speed * t));   // over the drive path, 5 km up
            var aw = new AnchorWorld(Scenarios.Seed);
            using (var s = Streamer(aw))
            {
                s.PrebuildFar(3); Scenarios.Settle(s, dirAt(0), Alt, 120, false);
                int built0 = s.FarBuiltTotal; var r = Scenarios.Run(s, dirAt, Alt, Seconds, 10.0, true, false, Scenarios.HeightFunc());
                double p95 = Stats.Pct(r.FrameMs, 0.95), p999 = Stats.Pct(r.FrameMs, 0.999);
                rows.Add(Row.Proto("t1-flight-c", $"P95 {F(p95)} ms / P99.9 {F(p999)} ms / max {F(r.FrameMs.Max())} ms", "P95 <= 1.0 ms, P99.9 <= 2.0 ms (frames > 4 ms reported)", p95 <= 1.0 && p999 <= 2.0, $"{r.FrameMs.Count(x => x > 4.0)} frames > 4 ms; {r.Frames} frames at {Speed} m/s, {Alt} m above ground along the drive path, {s.FarBuiltTotal - built0} far nodes built"));
                rows.Add(Row.Proto("t7-flight-c", $"{s.SectorRequests} near sectors requested, {s.StatsA.RegionsBuilt} region preparations", "= 0 above 1500 m", s.SectorRequests == 0 && s.SectorRequestsAboveCut == 0, $"anchor world afterwards: {aw.RoutedCount} routes, {aw.WorldCount} settlement worlds"));
                rows.Add(Row.Proto("t2-flight-coverage-c", $"{r.CoverageViolationsAfterWarm} violations", "= 0", r.CoverageViolationsAfterWarm == 0, $"far coverage exactly once, {r.CoverageChecksAfterWarm} checks x 3000 random directions after 10 s"));
            }
            WriteRows(dir, "cflight", rows);
            return 0;
        }

        /// <summary>creport &lt;dir&gt; &lt;section.md&gt; &lt;rows.csv&gt;: the rows of this run as a doc section (appended to what-if-planet-runtime.md by the runner) and csv lines.</summary>
        public static int Report(string dir, string md, string csv, string sha, string hw, string wall)
        {
            var rows = new List<Row>();
            foreach (var n in new[] { "cdrive-c120", "cverify-c120", "cdrive-c30", "cverify-c30", "ccold", "cflight" }) { string f = Path.Combine(dir, "rows-" + n + ".txt"); if (File.Exists(f)) rows.AddRange(File.ReadAllLines(f).Where(l => l.Length > 0).Select(Row.Parse)); }
            string Q(string s) => s != null && (s.Contains(",") || s.Contains("\"")) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            File.WriteAllLines(csv, rows.Select(r => string.Join(",", r.Id, r.Kind, Q(r.Value), Q(r.Threshold), r.Result, Q(r.Note))));
            var sb = new StringBuilder();
            sb.AppendLine("## Anchor content, city-to-city drive (RG-184 B5)").AppendLine();
            sb.AppendLine($"Generated by `tools/whatif-runtime/run-anchor-runtime.ps1`; commit `{sha}`; {hw}; wall {wall} s; seed {Scenarios.Seed}. `PlanetStreamer` with the `ISectorContent` constructor (RG-178): the region worker runs `AnchorWorld.Prepare`, the sector worker `AnchorSector.Build`. Path: `CorridorPath.CityToCity` (streets of the settlement worlds, shortest street path centre -> entrance, then the corridor stations, entrance -> centre). Budgets unchanged; row t9 is new (road continuity at the crossed old region borders).").AppendLine();
            sb.AppendLine("| id | kind | value | threshold | result | note |").AppendLine("|---|---|---|---|---|---|");
            foreach (var r in rows) sb.AppendLine($"| {r.Id} | {r.Kind} | {r.Value} | {r.Threshold} | **{r.Result}** | {r.Note.Replace("|", "/")} |");
            sb.AppendLine().AppendLine($"Protocol rows: {rows.Count(r => r.Kind == "protocol" && r.Result == "PASS")} PASS, {rows.Count(r => r.Kind == "protocol" && r.Result == "FAIL")} FAIL; report rows: {rows.Count(r => r.Kind == "report")}.");
            File.WriteAllText(md, sb.ToString());
            Console.WriteLine($"wrote {md} and {csv}");
            return 0;
        }
    }
}
