using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Anchors.Experiment;
using WhatIf.Planet.Runtime;
using WhatIf.Planet.Surface;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Runtime.Experiment
{
    /// <summary>RG-178: variant B content: sectors from the anchors (AnchorSector); the per-region preparation is AnchorWorld.Prepare.</summary>
    public sealed class AnchorContent : ISectorContent
    {
        readonly AnchorWorld w; public AnchorContent(AnchorWorld world) { w = world; }
        public void Build(QuadSector sec, AdaptedSector o) => AnchorSector.Build(w, sec, o);
    }

    /// <summary>RG-178 row s6: the runtime drive along a corridor chain of the RG-177 areas, anchor path (B; the region path A was removed in RG-185). Writes rows-adrive-B.txt.</summary>
    public static class AnchorDrive
    {
        static string F(double v, string f = "F3") => Stats.F(v, f);

        public static CorridorPath PickPath(double maxM, out string areaName)
        {
            CorridorPath best = null; areaName = "";
            foreach (var a in Anchors.Experiment.Program.Areas())
            {
                var surf = new PlanetSurface(Scenarios.Seed); var set = AnchorSet.Build(new SettlementField(Scenarios.Seed, surf), new CorridorBuilder(surf), a.Regions, false);
                var p = CorridorPath.Best(set, maxM);
                Console.WriteLine($"area {a.Name}: best chain {p?.RegionBorders} region borders, {p?.FaceEdges} cube edges, {p?.LengthM / 1000:F1} km: {p?.Description}");
                if (p != null && (best == null || p.RegionBorders > best.RegionBorders || (p.RegionBorders == best.RegionBorders && p.LengthM > best.LengthM))) { best = p; areaName = a.Name; }
            }
            return best;
        }

        public static int Run(string dir, double seconds, double speed)
        {
            var path = PickPath(speed * seconds, out string area);
            double secs = Math.Min(seconds, path.LengthM / speed - 1); const double Warm = 10.0;
            Console.WriteLine($"path from area {area}: {path.LengthM / 1000:F1} km, {path.RegionBorders} region borders, {path.FaceEdges} cube edges; drive {secs:F0} s at {speed} m/s");
            var rows = new List<Row>(); string sfx = "-B";
            rows.Add(Row.Report("s6-path" + sfx, $"{path.LengthM / 1000:F1} km, {path.RegionBorders} region borders, {path.FaceEdges} cube edges, {secs:F0} s at {speed} m/s", $"area {area}; chain: {path.Description}"));
            Func<double, Vec3> dirAt = t => path.DirAtS(speed * t);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long gc0 = GC.GetTotalMemory(true);
            AnchorWorld aw = new AnchorWorld(Scenarios.Seed);
            var hs = Scenarios.HeightFunc();
            using (var s = new PlanetStreamer(id => { aw.Prepare(id); return new AnchorContent(aw); }, Scenarios.HeightFunc()))
            {
                s.PrebuildFar(3);
                double settleS = Scenarios.Settle(s, dirAt(0), 0.0, 180, true);
                Console.WriteLine($"settled after {settleS:F1} s; regions ready {s.ReadyRegionCount}");
                var r = Scenarios.Run(s, dirAt, 0.0, secs, Warm, true, true, hs);
                long gcEnd = GC.GetTotalMemory(true);
                File.WriteAllText(System.IO.Path.Combine(dir, "adrive-frames" + sfx + ".csv"), Scenarios.Csv(r, "adrive"));
                double p95 = Stats.Pct(r.FrameMs, 0.95), p999 = Stats.Pct(r.FrameMs, 0.999);
                rows.Add(Row.Proto("t1-s6" + sfx, $"P95 {F(p95)} ms / P99.9 {F(p999)} ms / max {F(r.FrameMs.Max())} ms, {r.FrameMs.Count(x => x > 4.0)} frames > 4 ms", "P95 <= 1.0 ms", p95 <= 1.0, $"{r.Frames} frames; median {F(Stats.Pct(r.FrameMs, 0.5))} ms; GC gen0/1/2 {r.GcC0}/{r.GcC1}/{r.GcC2}, pause {F(r.GcPauseMs, "F0")} ms; slow frames: {string.Join("; ", r.SlowFrames.Take(5))}"));
                rows.Add(Row.Proto("t2-holes-s6" + sfx, $"{r.NotReady300AfterWarm} frames", "= 0", r.NotReady300AfterWarm == 0, $"frames after {Warm:F0} s with a wanted sector inside 300 m not ready; coverage violations after warm {r.CoverageViolationsAfterWarm} in {r.CoverageChecksAfterWarm} checks (all {r.CoverageViolations}); waiting for a region world max {r.MaxWaiting600}, max not ready inside 600 m {r.MaxNotReady600}"));
                var leads = r.Entries.Where(e => !double.IsNaN(e.ReadyS)).Select(e => e.EnterS - e.ReadyS).ToList(); int notReady = r.Entries.Count(e => double.IsNaN(e.ReadyS) || e.ReadyS > e.EnterS);
                rows.Add(Row.Report("t3-lead-s6" + sfx, $"min lead {(leads.Count > 0 ? F(leads.Min(), "F1") : "n/a")} s, {notReady} entries without a ready region", $"{r.Entries.Count} region entries"));
                double dMiB = (gcEnd - gc0) / 1048576.0;
                rows.Add(Row.Report("t6-memory-s6" + sfx, $"{F(dMiB, "F1")} MiB managed after the drive", $"GC.GetTotalMemory(true) after minus before; accounted cache {F(s.CacheBytes / 1048576.0, "F1")} MiB; sectors ready {s.ReadySectorCount}, regions ready {s.ReadyRegionCount}; B anchor world: {aw.RoutedCount} routes, {aw.WorldCount} settlement worlds"));
                var A = s.StatsA; var B = s.StatsB; var rb = s.RegionBuilds.Select(b => b.ReadyS - b.StartS).ToList(); var lat = s.LatencyS;
                rows.Add(Row.Report("t4-s6" + sfx, $"sectors {B.SectorsBuilt + A.SectorsBuilt} built, max {F(Math.Max(A.MaxSectorMs, B.MaxSectorMs), "F1")} ms each; region units {A.RegionsBuilt}, longest {F(A.MaxRegionS, "F1")} s", $"request-to-ready P95 {F(Stats.Pct(lat, 0.95))} s, max {F(lat.Count > 0 ? lat.Max() : 0)} s; queue max {s.MaxQueue}; A busy {F(100.0 * A.BusyTicks / Stopwatch.Frequency / r.WallS, "F1")} %, B busy {F(100.0 * B.BusyTicks / Stopwatch.Frequency / r.WallS, "F1")} %"));
            }
            File.WriteAllLines(System.IO.Path.Combine(dir, "rows-adrive" + sfx + ".txt"), rows.Select(x => x.Line()));
            foreach (var x in rows) Console.WriteLine($"{x.Result,-6} {x.Id,-16} {x.Value} [{x.Threshold}] {x.Note}");
            return 0;
        }
    }
}
