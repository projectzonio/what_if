using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Experiment;
using WhatIf.Planet.Surface;
using WhatIf.Planet.WorldGen;
using static WhatIf.Planet.Anchors.Experiment.Program;

namespace WhatIf.Planet.Anchors.Experiment
{
    /// <summary>RG-178 rows s1-s5 (CPU only): sectors of variant B (AnchorSector) on the RG-177 areas. The region-first variant A of the original rows was removed in RG-185: the A columns print "-" and the A-relative targets are report only.</summary>
    public static class Sectors
    {
        const int SeededSectors = 2048, ColdPoints = 20; const double ColdM = 600;
        static ulong H(ulong h, ulong v) { h ^= v + 0x9E3779B97F4A7C15UL + (h << 6) + (h >> 2); return SplitMix64.Mix(h); }
        static ulong Bits(double d) => (ulong)BitConverter.DoubleToInt64Bits(d);
        static ulong Bits(Vec3 v) => H(H(Bits(v.X), Bits(v.Y)), Bits(v.Z));
        static ulong Fnv(string s) { ulong h = 14695981039346656037UL; foreach (char c in s) { h ^= c; h *= 1099511628211UL; } return h; }

        public static List<SectorId> SectorsOf(RegionId r)
        {
            var l = new List<SectorId>(1024);
            for (int a = 0; a < SectorId.PerRegionSide; a++) for (int b = 0; b < SectorId.PerRegionSide; b++) l.Add(new SectorId(r.Face, r.Row * SectorId.PerRegionSide + a, r.Col * SectorId.PerRegionSide + b));
            return l;
        }

        /// <summary>Sector build of variant B (anchors).</summary>
        public sealed class Variant
        {
            public string Name; public Action<SectorId, AdaptedSector> Build;
            /// <summary>Footprint probe: for the built sector, per vertex: true + (terrain - road y) when the vertex lies within half width of a road centreline.</summary>
            public Func<SectorId, AdaptedSector, bool[], double[], int> Foot;
            /// <summary>RG-182: the same for the ground edges of the settlement worlds (streets and entrance roads).</summary>
            public Func<SectorId, AdaptedSector, bool[], double[], int> FootStreets;
        }

        public static Variant VariantB(AnchorWorld w) => new Variant
        {
            Name = "B",
            Build = (id, o) => AnchorSector.Build(w, new QuadSector(id), o),
            Foot = (id, o, inF, dy) =>
            {
                var sec = new QuadSector(id); var cen = sec.Up; double rad = 0;
                foreach (int c in new[] { 0, 64, 64 * 65, 65 * 65 - 1 }) rad = Math.Max(rad, SettlementField.Gc(cen, o.World[c].Normalized()));
                rad += 20 + 60;
                var near = new List<(RoutedLink rl, int lo, int hi)>();
                foreach (var rl in w.RoutedLinks)
                {
                    rl.C.Chart.ToLocal(cen, out double cx, out double cz);
                    int lo = -1, hi = -1;
                    for (int k = 0; k < rl.C.X.Length; k++) { double dx = rl.C.X[k] - cx, dz = rl.C.Z[k] - cz; if (dx * dx + dz * dz <= rad * rad) { if (lo < 0) lo = k; hi = k; } }
                    if (lo >= 0) near.Add((rl, Math.Max(0, lo - 1), Math.Min(rl.C.X.Length - 1, hi + 1)));
                }
                int n = 0;
                for (int k = 0; k < AdaptedSector.Count; k++)
                {
                    inF[k] = false; var d = o.World[k].Normalized(); double bestE = double.MaxValue, by = 0;
                    foreach (var (rl, lo, hi) in near)
                    {
                        rl.C.Chart.ToLocal(d, out double x, out double z);
                        for (int i = lo; i < hi; i++)
                        {
                            double ax = rl.C.X[i], az = rl.C.Z[i], ex = rl.C.X[i + 1] - ax, ez = rl.C.Z[i + 1] - az, l2 = ex * ex + ez * ez;
                            double t = l2 > 0 ? ((x - ax) * ex + (z - az) * ez) / l2 : 0;
                            if ((t < 0 && i == 0) || (t > 1 && i == rl.C.X.Length - 2)) continue;   // RG-182: AnchorSector carves no end cap (the settlement world's entrance road takes over)
                            t = t < 0 ? 0 : (t > 1 ? 1 : t);
                            double qx = ax + ex * t - x, qz = az + ez * t - z, dist = Math.Sqrt(qx * qx + qz * qz);
                            if (dist - rl.HalfWidthM < bestE && dist <= rl.HalfWidthM) { var sg = rl.Seg(rl.SegOf(i)); int a = i - sg.I0; bestE = dist - rl.HalfWidthM; by = sg.Y[a] + (sg.Y[a + 1] - sg.Y[a]) * t; }
                        }
                    }
                    if (bestE == double.MaxValue) continue;
                    double h = o.World[k].Length - CubeSphere.Radius; inF[k] = true; dy[k] = h - by; n++;
                }
                return n;
            },
            FootStreets = (id, o, inF, dy) =>
            {
                int n = 0; for (int k = 0; k < AdaptedSector.Count; k++) inF[k] = false;
                foreach (var s in w.SettlementsNear(id.Region))
                {
                    var sw = w.World(s); if (!sw.Ok) continue;
                    if (!streetIdx.TryGetValue(s.Id, out var ri)) streetIdx[s.Id] = ri = new RoadIndex(sw.Streets);
                    for (int k = 0; k < AdaptedSector.Count; k++)
                    {
                        if (inF[k]) continue;
                        var d = o.World[k].Normalized(); sw.Chart.ToLocal(d, out double x, out double z);
                        if (x * x + z * z > sw.ReachM * sw.ReachM) continue;
                        if (ri.Nearest(x, z, out double y, out double hw)) { inF[k] = true; dy[k] = o.World[k].Length - CubeSphere.Radius - y; n++; }
                    }
                }
                return n;
            }
        };

        static readonly Dictionary<ulong, RoadIndex> streetIdx = new Dictionary<ulong, RoadIndex>();

        /// <summary>Chart grid of the ground road segments of one settlement world (cell 64 m): nearest centreline within its half width.</summary>
        sealed class RoadIndex
        {
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>(); readonly List<(double ax, double az, double bx, double bz, double ya, double yb, double hw)> segs = new List<(double, double, double, double, double, double, double)>();
            const double Cell = 64;
            static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;
            /// <summary>RG-184: the settlement worlds keep their streets as a StreetSet (the graph is dropped after the build).</summary>
            public RoadIndex(StreetSet st)
            {
                var d = st.D; for (int o = 0; o < d.Length; o += StreetSet.Stride) Add(d[o], d[o + 1], d[o + 2], d[o + 3], d[o + 4], d[o + 5], d[o + 6]);
            }
            void Add(double ax, double az, double bx, double bz, double ya, double yb, double hw)
            {
                int id = segs.Count; segs.Add((ax, az, bx, bz, ya, yb, hw));
                int x0 = (int)Math.Floor((Math.Min(ax, bx) - hw) / Cell), x1 = (int)Math.Floor((Math.Max(ax, bx) + hw) / Cell);
                int z0 = (int)Math.Floor((Math.Min(az, bz) - hw) / Cell), z1 = (int)Math.Floor((Math.Max(az, bz) + hw) / Cell);
                for (int a = x0; a <= x1; a++) for (int b = z0; b <= z1; b++) { var k = Key(a, b); if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>(); l.Add(id); }
            }
            public bool Nearest(double x, double z, out double y, out double hwOut)
            {
                y = 0; hwOut = 0; double best = double.MaxValue; bool have = false;
                if (!cells.TryGetValue(Key((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell)), out var l)) return false;
                foreach (int id in l)
                {
                    var s = segs[id]; double ex = s.bx - s.ax, ez = s.bz - s.az, l2 = ex * ex + ez * ez;
                    double t = l2 > 0 ? ((x - s.ax) * ex + (z - s.az) * ez) / l2 : 0; t = t < 0 ? 0 : (t > 1 ? 1 : t);
                    double qx = s.ax + ex * t - x, qz = s.az + ez * t - z, d = Math.Sqrt(qx * qx + qz * qz);
                    if (d <= s.hw && d - s.hw < best) { best = d - s.hw; y = s.ya + (s.yb - s.ya) * t; hwOut = s.hw; have = true; }
                }
                return have;
            }
        }

        static ulong SectorHash(AdaptedSector o) { ulong h = 1469598103934665603UL; for (int i = 0; i < AdaptedSector.Count; i++) h = H(h, Bits(o.World[i])); return h; }
        static Vec3 VertexDir(SectorId id, int i, int j) => Lattice.OnEdge(id.Col * QuadSector.Sub + i, id.Row * QuadSector.Sub + j) ? Lattice.Direction(id.Face, id.Col * QuadSector.Sub + i, id.Row * QuadSector.Sub + j)
            : Lattice.Compose(id.Face, Lattice.Coord(id.Col * QuadSector.Sub + i), Lattice.Coord(id.Row * QuadSector.Sub + j));

        public sealed class SweepResult
        {
            public long Instances, Differing, FootInstances, FootDiffering, Sectors, FootVertices, FootAbove010, FootRoadSectors, StreetVertices, StreetAbove010; public long[] StreetAboveByCode = new long[4]; public string StreetWorst = ""; public double StreetMaxAbove = double.NegativeInfinity; public double MaxAbove = double.NegativeInfinity, MaxBelow = double.NegativeInfinity;
            public int Hmm; public string WorstAbove = "";
        }

        /// <summary>s1 + s2: builds every sector of the area, compares the height bits of vertices shared across sector borders (cube edge and cube vertex included) and measures the footprint.</summary>
        public static SweepResult Sweep(Variant v, List<RegionId> regions)
        {
            var res = new SweepResult(); var seen = new Dictionary<ulong, ulong>(); var footSeen = new HashSet<ulong>();
            var o = new AdaptedSector(); var inF = new bool[AdaptedSector.Count]; var dy = new double[AdaptedSector.Count];
            foreach (var r in regions)
                foreach (var id in SectorsOf(r))
                {
                    v.Build(id, o); res.Sectors++;
                    int nf = v.Foot(id, o, inF, dy);
                    if (nf > 0) res.FootRoadSectors++;
                    if (v.FootStreets != null)
                    {
                        var sF = new bool[AdaptedSector.Count]; var sD = new double[AdaptedSector.Count];
                        if (v.FootStreets(id, o, sF, sD) > 0) for (int k = 0; k < AdaptedSector.Count; k++) if (sF[k]) { res.StreetVertices++; if (sD[k] > 0.10) { res.StreetAbove010++; res.StreetAboveByCode[o.Surface[k]]++; } if (sD[k] > res.StreetMaxAbove) { res.StreetMaxAbove = sD[k]; res.StreetWorst = $"{id} vertex {k} code {o.Surface[k]}"; } }
                    }
                    for (int k = 0; k < AdaptedSector.Count; k++)
                    {
                        int i = k % 65, j = k / 65;
                        if (inF[k]) { res.FootVertices++; double a = dy[k]; if (a > res.MaxAbove) { res.MaxAbove = a; res.WorstAbove = id.ToString(); } if (a > 0.10) res.FootAbove010++; if (-a > res.MaxBelow) res.MaxBelow = -a; }
                        if (i != 0 && i != 64 && j != 0 && j != 64) continue;
                        ulong key = Bits(VertexDir(id, i, j)), val = Bits(o.World[k]);
                        if (seen.TryGetValue(key, out var old)) { res.Instances++; bool diff = old != val; if (diff) res.Differing++; if (inF[k]) { res.FootInstances++; if (diff) res.FootDiffering++; } }
                        else seen[key] = val;
                    }
                }
            return res;
        }

        public static int Run(string dir, List<Area> areas, string md, string csv, string sha, string hw, bool closeout = false)
        {
            var rows = new List<string[]>(); var notes = new List<string>();
            // RG-184 close-out (rows k1-k4): stall summary first (warm segment timings, own anchor sets), per-area facts collected below
            Dictionary<string, Stalls.Summary> stalls = null; var kMem = new Dictionary<string, double>(); var kCold = new Dictionary<string, double>(); var kP95 = new Dictionary<string, List<double>>(); var kSame = new Dictionary<string, (long diff, int same, int n)>();
            if (closeout) { var sbs = new StringBuilder(); stalls = Stalls.Collect(areas, s => { Console.WriteLine(s); sbs.AppendLine(s); }); File.WriteAllText(Path.Combine(dir, "stalls.txt"), sbs.ToString()); }
            void Row(string row, string area, string metric, string a, string b, string target, string verdict) => rows.Add(new[] { row, area, metric, a, b, target, verdict });
            string P(double v, string f = "F2") => F(v, f);
            var s1 = new Dictionary<string, SweepResult>();
            foreach (var a in areas)
            {
                Console.WriteLine($"== area {a.Name}"); var swT = Stopwatch.StartNew();
                double km2 = a.Regions.Count * 4 * Math.PI * CubeSphere.Radius * CubeSphere.Radius / (6.0 * Math.Pow(4, 10)) / 1e6;

                // ---- s4 + B world: fresh AnchorWorld, touch every sector, drop the sectors, measure the retained caches
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long m0 = GC.GetTotalMemory(true);
                var aw = new AnchorWorld(Seed);
                { var o = new AdaptedSector(); foreach (var r in a.Regions) foreach (var id in SectorsOf(r)) AnchorSector.Build(aw, new QuadSector(id), o); }
                var tsw = AnchorWorld.ThreadSurface(Seed); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long m1 = GC.GetTotalMemory(true);
                double bMb = (m1 - m0) / 1048576.0; kMem[a.Name] = bMb;
                int sets = aw.Worlds.Count(x => x.Ok), setsFail = aw.Worlds.Count(x => !x.Ok);
                Row("s4", a.Name, $"retained memory per km2 of the area ({km2:F0} km2, all {a.Regions.Count * 1024} sectors touched once, sector data dropped): the anchor cache (routed corridors {aw.RoutedCount}, profiled segments, {aw.WorldCount} settlement worlds)", "-", $"{bMb:F1} MiB; {bMb / km2 * 1024:F1} KiB/km2", "report", "report");
                notes.Add($"area {a.Name} s4: B settlement worlds generated {sets}, without a WorldGen world {setsFail}{(setsFail > 0 ? " (" + string.Join("; ", aw.Worlds.Where(x => !x.Ok).Select(x => x.Failed).Distinct()) + ")" : "")}; settlement world build median {P(Stats.Q(aw.Worlds.Where(x => x.Ok).Select(x => x.Seconds).ToList(), 0.5))} s, max {P(aw.Worlds.Max(x => x.Seconds))} s; profiled segments {aw.RoutedLinks.Sum(x => x.ProfiledSegments)} of {aw.RoutedLinks.Sum(x => x.SegmentCount)}; RG-184 world LRU cap {aw.WorldByteCap / 1048576.0:F0} MiB: worlds built {aw.WorldsBuilt}, evicted {aw.WorldsEvicted}, cached {aw.WorldCount} ({aw.CachedWorldBytes / 1048576.0:F1} MiB estimated)");

                // ---- s1 + s2 sweep (B on the warm world)
                var vb = VariantB(aw);
                var swB = Stopwatch.StartNew(); var rb = Sweep(vb, a.Regions); double tB = swB.Elapsed.TotalSeconds;
                Console.WriteLine($"sweep B {tB:F0} s");
                s1[a.Name] = rb;
                Row("s1", a.Name, $"sector border vertices shared by two or more sectors (all {rb.Sectors} sectors of the area, old region borders, cube edge and cube vertex included): instances compared / differing world position bits", "-", $"{rb.Instances} / {rb.Differing}", "B = 0", rb.Differing == 0 ? "PASS" : "FAIL");
                Row("s2", a.Name, "the same, vertices inside a road footprint (half width) only: instances / differing bits", "-", $"{rb.FootInstances} / {rb.FootDiffering}", "B 0 bits", rb.FootDiffering == 0 ? "PASS" : "FAIL");
                Row("s2", a.Name, $"terrain above the road inside its footprint (vertices within half width of a centreline; sectors with roads {rb.FootRoadSectors}): footprint vertices; > 0.10 m above; max above (m); max below (m)", "-", $"{rb.FootVertices}; {rb.FootAbove010}; {P(rb.MaxAbove, "F3")}; {P(rb.MaxBelow, "F3")}", "report", "report");
                Row("s2", a.Name, "RG-182 settlement streets (ground edges of the settlement worlds incl. entrance roads, within half width): footprint vertices; > 0.10 m above; max above (m). The corridor part is the row above", "-", $"{rb.StreetVertices}; {rb.StreetAbove010}; {P(rb.StreetMaxAbove, "F3")}", "report", "report");
                notes.Add($"area {a.Name} s2: worst terrain-above-road vertex in sector {rb.WorstAbove}; footprint vertices are the lattice vertices (4.8 m apart) within half width of a ground road centreline (corridors only)");

                // ---- s3 seeded sectors
                ulong st = 0x178UL * 7 + (ulong)a.Name[0]; var list = new List<SectorId>();
                while (list.Count < SeededSectors)
                {
                    var r = a.Regions[(int)(SplitMix64.Next(ref st) % (ulong)a.Regions.Count)];
                    list.Add(new SectorId(r.Face, r.Row * 32 + (int)(SplitMix64.Next(ref st) % 32), r.Col * 32 + (int)(SplitMix64.Next(ref st) % 32)));
                }
                var oo = new AdaptedSector(); var tbw = new List<double>(); var tbc = new List<double>(); var names = new List<(double, SectorId, string)>();
                // RG-184: the warm B timing runs on an uncapped anchor world warmed with the list (k4 = sector build cost; the world cache policy is row k2)
                var awW = new AnchorWorld(Seed) { WorldByteCap = 0 }; var vbw = VariantB(awW); foreach (var id in list) vbw.Build(id, oo);
                foreach (var id in list) { long t0 = Stopwatch.GetTimestamp(); vbw.Build(id, oo); double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency; tbw.Add(ms); names.Add((ms, id, "")); }
                // cold first touch of B: a fresh AnchorWorld per run of the list (anchors, routes, settlement worlds, profiles built on demand)
                var awc = new AnchorWorld(Seed); var vbc = VariantB(awc);
                foreach (var id in list) { long t0 = Stopwatch.GetTimestamp(); vbc.Build(id, oo); tbc.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency); }
                notes.Add($"area {a.Name} s3 cold ingredients (RG-181): slowest profiled segment {awc.RoutedLinks.Select(r => r.MaxSegmentSeconds).DefaultIfEmpty(0).Max() * 1000:F0} ms of {awc.RoutedLinks.Sum(r => r.ProfiledSegments)} profiled; slowest settlement world {awc.Worlds.Select(w => w.Seconds).DefaultIfEmpty(0).Max() * 1000:F0} ms of {awc.WorldCount} ({awc.Worlds.Count(w => w.Seconds > 0.1)} over 100 ms); routes {awc.RoutedCount}");
                { var sl = awc.RoutedLinks.Select(r => (r, g: r.SlowestSegment)).Where(t => t.g != null).OrderByDescending(t => t.g.Seconds).First(); notes.Add($"area {a.Name} s3 cold slowest segment: {sl.r.C.Link.RoadClass} {sl.r.C.Link.A} - {sl.r.C.Link.B} segment {sl.g.Index} of {sl.r.SegmentCount}, {sl.g.LengthM:F0} m, {sl.g.BeyondCurve} stations beyond the curve limit, {sl.g.Seconds * 1000:F0} ms, joints H {sl.r.C.PinH[sl.g.Index]:F1} / {sl.r.C.PinH[sl.g.Index + 1]:F1} m, grades {sl.r.C.PinG[sl.g.Index]:F3} / {sl.r.C.PinG[sl.g.Index + 1]:F3}, meander scale {sl.r.C.MeanderScale}"); }
                kCold[a.Name] = tbc.Max(); kP95[a.Name] = new List<double> { Stats.Q(tbw, 0.95) };
                for (int rep = 1; closeout && rep < 3; rep++)   // k4: two more repeats of the warm B timing on the same list (median of 3 repeats)
                {
                    var rb2 = new List<double>();
                    foreach (var id in list) { long t0 = Stopwatch.GetTimestamp(); vbw.Build(id, oo); rb2.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency); }
                    kP95[a.Name].Add(Stats.Q(rb2, 0.95));
                }
                var worstW = names.OrderByDescending(x => x.Item1).First(); var worstC = tbc.Select((x, i) => (x, i)).OrderByDescending(x => x.x).First();
                Row("s3", a.Name, $"sector build, {SeededSectors} seeded sectors of the area, single thread, warm (anchors, routes, profiles and settlement worlds cached): median / P95 / worst (ms)", "-", $"{P(Stats.Q(tbw, 0.5))} / {P(Stats.Q(tbw, 0.95))} / {P(tbw.Max())}", "report", "report");
                Row("s3", a.Name, "the same list built cold by B (fresh AnchorWorld: routes, segment profiles and settlement worlds on first use, in list order): median / P95 / worst (ms)", "-", $"{P(Stats.Q(tbc, 0.5))} / {P(Stats.Q(tbc, 0.95))} / {P(tbc.Max())}", "report", "report");
                notes.Add($"area {a.Name} s3: worst warm B sector {worstW.Item2} {worstW.Item1:F2} ms; worst cold B sector {list[worstC.i]} {worstC.x:F0} ms; cold sectors > 100 ms {tbc.Count(x => x > 100)}, > 1000 ms {tbc.Count(x => x > 1000)}");

                // ---- s5 cold ready
                var cB = new List<double>(); var cn = new List<string>();
                var surf = new PlanetSurface(Seed); ulong st2 = 0xC5C5UL + (ulong)a.Name[0]; int n = 0;
                while (n < ColdPoints)
                {
                    var r = a.Regions[(int)(SplitMix64.Next(ref st2) % (ulong)a.Regions.Count)]; int cells = 1 << r.Level;
                    var p = CubeSphere.FromFaceAB(r.Face, -1.0 + 2.0 * (r.Col + SplitMix64.NextDouble(ref st2)) / cells, -1.0 + 2.0 * (r.Row + SplitMix64.NextDouble(ref st2)) / cells);
                    if (surf.Kind(p) != SurfaceKind.Land) continue;
                    n++;
                    var secs = SectorsWithin(p, ColdM);
                    var touch = secs.Select(s => s.Region).Distinct().ToList();
                    var sw = Stopwatch.StartNew(); var cb = new AnchorWorld(Seed); foreach (var s in secs) AnchorSector.Build(cb, new QuadSector(s), oo); cB.Add(sw.Elapsed.TotalSeconds);
                    cn.Add($"point {n}: {secs.Count} sectors in {touch.Count} regions, B {cB.Last():F2} s (settlement worlds {cb.WorldCount}, corridors routed {cb.RoutedCount})");
                }
                Row("s5", a.Name, $"cold-ready: {ColdPoints} seeded land points, time until all {ColdM:F0} m sectors are built (fresh anchor world + sectors); median / P95 / max (s)", "-", $"{P(Stats.Q(cB, 0.5))} / {P(Stats.Q(cB, 0.95))} / {P(cB.Max())}", "report", "report");
                notes.AddRange(cn.Select(x => $"area {a.Name} s5: {x}"));

                // ---- determinism of B sectors: another thread, reverse order, fresh anchor world
                var sample = list.Take(200).ToList(); var h1 = new List<ulong>(); foreach (var id in sample) { vb.Build(id, oo); h1.Add(SectorHash(oo)); }
                var h2 = new ulong[sample.Count]; var th = new Thread(() => { var w2 = new AnchorWorld(Seed); var o2 = new AdaptedSector(); for (int i = sample.Count - 1; i >= 0; i--) { AnchorSector.Build(w2, new QuadSector(sample[i]), o2); h2[i] = SectorHash(o2); } }); th.Start(); th.Join();
                int same = Enumerable.Range(0, sample.Count).Count(i => h1[i] == h2[i]); kSame[a.Name] = (rb.Differing, same, sample.Count);
                Row("s1", a.Name, "B sector determinism: 200 seeded sectors rebuilt on another thread in reverse order with a fresh anchor world: identical sector hashes", "-", $"{same} / {sample.Count}", "all", same == sample.Count ? "PASS" : "FAIL");
            }
            if (closeout) KRows(dir, areas, rows, notes, stalls, kMem, kCold, kP95, kSame, s1);
            Write(dir, md, csv, rows, notes, areas, sha, hw);
            return 0;
        }

        /// <summary>RG-181 s4 anchor cache (MiB) per area: the k2 baseline (RG-182 outbox).</summary>
        static readonly Dictionary<string, double> Rg181Cache = new Dictionary<string, double> { ["A"] = 4.2, ["B"] = 6.9 };

        /// <summary>RG-184 rows k1-k4 from the facts this run collected (r7 from the hash-*.txt files of the runner's three hash processes in the same folder).</summary>
        static void KRows(string dir, List<Area> areas, List<string[]> rows, List<string> notes, Dictionary<string, Stalls.Summary> stalls, Dictionary<string, double> kMem, Dictionary<string, double> kCold,
            Dictionary<string, List<double>> kP95, Dictionary<string, (long diff, int same, int n)> kSame, Dictionary<string, SweepResult> s1)
        {
            string P(double v, string f = "F2") => F(v, f);
            Dictionary<string, string> Hashes(string mode) { var f = Path.Combine(dir, $"hash-{mode}.txt"); return File.Exists(f) ? File.ReadAllLines(f).Where(l => l.Contains(",")).ToDictionary(l => l.Split(',')[0], l => l.Split(',')[1]) : null; }
            var hf = Hashes("forward"); var hr = Hashes("reverse"); var ht = Hashes("threads4");
            foreach (var a in areas)
            {
                var st = stalls[a.Name];
                bool k1 = st.MaxMs <= 300 && kCold[a.Name] <= 300;
                rows.Add(new[] { "k1", a.Name, $"stall exits (segments ending beyond the curve limit) / stations beyond; slowest segment profile (warm); s3 cold worst sector. Before = RG-182 joints, after = joint slide (candidate b, {st.Moved} corridors moved a joint)",
                    $"{st.ExitsBefore} / {st.StationsBefore}; {P(st.MaxMsBefore, "F0")} ms", $"{st.Exits} / {st.Stations}; {P(st.MaxMs, "F0")} ms ({st.Slowest}); cold worst {P(kCold[a.Name], "F0")} ms",
                    "0 stall exits or each named with cause; segment <= 300 ms; cold worst <= 300 ms", k1 ? (st.Exits == 0 ? "PASS" : $"PASS ({st.Exits} named)") : "FAIL" });
                foreach (var n in st.Named) notes.Add($"area {a.Name} k1 remaining stall exit: {n}");
                bool r7 = hf != null && hr != null && ht != null && hf.TryGetValue(a.Name, out var h0) && hr.TryGetValue(a.Name, out var h1) && ht.TryGetValue(a.Name, out var h2) && h0 == h1 && h1 == h2;
                double ratio = kMem[a.Name] / Rg181Cache[a.Name]; var ks = kSame[a.Name];
                rows.Add(new[] { "k2", a.Name, $"s4 anchor cache after touching every sector (settlement worlds keep terrain model + StreetSet only, LRU byte cap AnchorWorld.DefaultWorldByteCap) vs RG-181 {P(Rg181Cache[a.Name], "F1")} MiB; r7 anchor hash forward / reverse / 4 threads; s1 differing bits; 200 / 200 determinism",
                    "-", $"{P(kMem[a.Name], "F1")} MiB = {P(ratio)} x; r7 {(r7 ? "identical" : "DIFFERENT or missing")}; s1 {ks.diff} bits; {ks.same} / {ks.n}", "<= 3 x; r7 + s1 + 200/200 unchanged", ratio <= 3 && r7 && ks.diff == 0 && ks.same == ks.n ? "PASS" : "FAIL" });
                var rb = s1[a.Name];
                rows.Add(new[] { "k3", a.Name, "s2 street footprint (ground edges of the settlement worlds incl. entrance roads, within half width): vertices; > 0.10 m above the street; max above (m)", "-",
                    $"{rb.StreetVertices}; {rb.StreetAbove010}; {P(rb.StreetMaxAbove, "F3")}", "0, or each remaining one named", rb.StreetAbove010 == 0 ? "PASS" : "FAIL" });
                notes.Add($"area {a.Name} k3 remaining street vertices > 0.10 m by surface code ground / corridor / street / shoulder: {string.Join(" / ", rb.StreetAboveByCode)}; worst {rb.StreetWorst}");
                var med = kP95[a.Name].OrderBy(x => x).ElementAt(kP95[a.Name].Count / 2);
                rows.Add(new[] { "k4", a.Name, $"s3 warm B P95 (ms per repeat), {kP95[a.Name].Count} repeats on the same {SeededSectors} seeded sectors, median of the repeats (the A column of RG-184 was removed in RG-185)", "-", $"{string.Join("; ", kP95[a.Name].Select(x => P(x)))} -> median {P(med)} ms",
                    "report", "report" });
            }
        }

        /// <summary>Level-15 sectors whose disc reaches the circle (centre-to-centre within radius + half diagonal): breadth-first from the sector under p.</summary>
        public static List<SectorId> SectorsWithin(Vec3 p, double radiusM)
        {
            var start = SectorId.At(p); var seen = new HashSet<SectorId> { start }; var q = new Queue<SectorId>(); q.Enqueue(start); var res = new List<SectorId>();
            while (q.Count > 0)
            {
                var s = q.Dequeue(); var qs = new QuadSector(s); double half = 0;
                foreach (var (i, j) in new[] { (0, 0), (64, 0), (0, 64), (64, 64) }) half = Math.Max(half, SettlementField.Gc(qs.Up, qs.Direction(i, j)));
                if (!s.Equals(start) && SettlementField.Gc(p, qs.Up) - half > radiusM) continue;
                res.Add(s);
                for (int d = 0; d < 4; d++) { var nb = s.Neighbour(d); if (seen.Add(nb)) q.Enqueue(nb); }
            }
            return res;
        }

        static void Write(string dir, string md, string csv, List<string[]> rows, List<string> notes, List<Area> areas, string sha, string hw)
        {
            md = md ?? Path.Combine(dir, "sectors.md"); csv = csv ?? Path.Combine(dir, "sectors.csv");
            var c = new StringBuilder("row,area,metric,variant_a,variant_b,target,verdict\n");
            foreach (var r in rows) c.Append(string.Join(",", r.Select(x => "\"" + x.Replace("\"", "'") + "\"")) + "\n");
            File.WriteAllText(csv, c.ToString());
            var m = new StringBuilder();
            m.Append($"Generated by `tools/whatif-anchors/run-anchors.ps1 -Sectors` ({DateTime.Now:yyyy-MM-dd HH:mm}); commit {sha}; {hw}. Seed {Seed}. Single process, single thread for s3 / s5 timings.\n\n");
            m.Append("| row | area | measurement | A (regions) | B (anchors) | target | verdict |\n|---|---|---|---|---|---|---|\n");
            foreach (var r in rows) m.Append("| " + string.Join(" | ", r.Select(x => x.Replace("|", "/"))) + " |\n");
            m.Append("\n### Notes\n\n"); foreach (var n in notes) m.Append($"- {n}\n");
            File.WriteAllText(md, m.ToString());
            Console.WriteLine($"wrote {md} and {csv}");
        }
    }
}
