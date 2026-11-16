using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Experiment;
using WhatIf.Planet.Surface;
using WhatIf.Planet.WorldGen;
using static WhatIf.Planet.Anchors.Experiment.Program;

namespace WhatIf.Planet.Anchors.Experiment
{
    /// <summary>Timing facts of one repeat and area (rows c1, c2).</summary>
    public sealed class Cost
    {
        public List<double> RouteMs = new List<double>(), SegMs = new List<double>(), ColdB = new List<double>();
        public double SettlementS, BytesPerKm, RoadKm;
        public List<string> ColdNotes = new List<string>();
    }

    public static class Measure
    {
        const int ColdPoints = 10;   // per area: 20 in all
        const double ColdM = 600;

        public static int Run(string dir, List<Area> areas, int repeats, string md, string csv, string sha, string hw, string unity)
        {
            var costs = new Dictionary<string, List<Cost>>(); var sets = new Dictionary<string, AnchorSet>();
            foreach (var a in areas) costs[a.Name] = new List<Cost>();
            var shared = new PlanetSurface(Seed);   // the warm surface for c2
            var benchNotes = new List<string>();
            {
                // cost of one ground sample (surface + detail) along a 10 m walk in the centre region of each area: the unit cost of B's routing and stations
                foreach (var a in areas)
                {
                    var ch = new RegionChart(a.Centre); var bsurf = new PlanetSurface(Seed); const int n = 200000; double sink = 0;
                    var sw = Stopwatch.StartNew(); for (int i = 0; i < n; i++) sink += Ground.Height(bsurf, ch.ToDir(-4000 + (i % 800) * 10, -4000 + (i / 800) * 32), out _); double t1 = sw.Elapsed.TotalSeconds;
                    sw.Restart(); for (int i = 0; i < n; i++) sink += Ground.Detail(Seed, ch.ToDir(-4000 + (i % 800) * 10, -4000 + (i / 800) * 32)); double t2 = sw.Elapsed.TotalSeconds;
                    benchNotes.Add($"area {a.Name}: one ground sample (surface HeightKind + detail) {t1 / n * 1e6:F2} us, of which detail {t2 / n * 1e6:F2} us ({n} samples on a 10 m walk; checksum {sink:G4})");
                }
            }
            for (int rep = 1; rep <= repeats; rep++)
                foreach (var a in areas)
                {
                    var c = new Cost();
                    var surf = new PlanetSurface(Seed);
                    long b0 = GC.GetTotalMemory(true);
                    var set = AnchorSet.Build(new SettlementField(Seed, surf), new CorridorBuilder(surf), a.Regions, false);
                    long b1 = GC.GetTotalMemory(true);
                    c.SettlementS = set.SettlementSeconds;
                    foreach (var co in set.Corridors) c.RouteMs.Add(co.RouteSeconds * 1000);
                    foreach (var sg in set.Segments.Values.SelectMany(x => x)) c.SegMs.Add(sg.Seconds * 1000);
                    c.RoadKm = set.Corridors.Where(x => x.Routed).Sum(x => x.LengthM) / 1000;
                    c.BytesPerKm = (b1 - b0) / Math.Max(1e-9, c.RoadKm);
                    GC.KeepAlive(set);
                    Cold(a, shared, c, rep);
                    costs[a.Name].Add(c);
                    if (rep == 1) sets[a.Name] = set;
                    Console.WriteLine($"rep {rep} area {a.Name}: B {set.Settlements.Count} settlements {set.SettlementSeconds:F2} s, {set.Corridors.Count} corridors, route median {Stats.Q(c.RouteMs, 0.5):F1} ms, segment median {Stats.Q(c.SegMs, 0.5):F1} ms; cold B {Stats.Q(c.ColdB, 0.5):F2} s");
                }

            var rows = new List<string[]>(); var notes = new List<string>(benchNotes); var pngs = new List<string>();
            void Row(string row, string area, string metric, string av, string bv, string target, string verdict) => rows.Add(new[] { row, area, metric, av, bv, target, verdict });
            string Pct(int a, int b) => b == 0 ? "0 / 0" : $"{a} / {b} ({100.0 * a / b:F0} %)";
            var rules = CorridorBuilder.Rules;
            foreach (var a in areas)
            {
                var set = sets[a.Name]; var nb = Nets.B(set, shared);
                int landRegions = a.Regions.Count(id => !Ocean(id, shared));
                Row("a1", a.Name, $"settlement density on the same area ({a.Regions.Count} regions, {landRegions} not ocean): count (cities / towns / villages) and per non-ocean region", "-",
                    $"{set.Settlements.Count} ({set.Settlements.Count(s => s.Class == SettlementClass.City)} / {set.Settlements.Count(s => s.Class == SettlementClass.Town)} / {set.Settlements.Count(s => s.Class == SettlementClass.Village)}); {set.Settlements.Count / (double)Math.Max(1, landRegions):F1} per region", "report", "report");
                string Ends(Net n) => n.BorderEnds.Count == 0 ? "0" : $"{n.BorderEnds.Values.Sum()} ({string.Join(", ", n.BorderEnds.Select(kv => kv.Key + " " + kv.Value))})";
                Row("n1", a.Name, "road edges ending within 50 m of a level-10 region border, not at a settlement (corridor ends that are not settlements)", "-", Ends(nb), "B = 0", nb.BorderEnds.Values.Sum() == 0 ? "PASS" : "FAIL");
                var lb = nb.Largest();
                Row("n2", a.Name, "settlements in the largest connected component", "-", Pct(lb.InLargest, lb.Total), "report", "report");
                var pb = Nets.Pairs(nb, 0x177B);
                Row("n3", a.Name, "seeded settlement pairs 5-40 km apart: reachable; route / great circle median, P95", "-", $"{Pct(pb.Reached, pb.Pairs)}; {F(pb.Med, "F2")}, {F(pb.P95, "F2")}", "report", "report");
                foreach (var cls in nb.Classes.Keys.OrderBy(x => Array.IndexOf(WhatIf.WorldGen.RoadClass.All, x)))
                {
                    var r = rules.Of(cls);
                    string Fx(Net n) { if (!n.Classes.TryGetValue(cls, out var f) || f.Km == 0) return "-"; return $"{f.Km:F1} km; grade {f.MaxGrade * 100:F1} %; R min {F(f.MinRadius, "F0")} m; beyond {f.Beyond / f.Km:F2} /km; water {f.WaterM:F0} m"; }
                    bool bOk = !nb.Classes.TryGetValue(cls, out var fb) || (fb.WaterM == 0 && fb.MaxGrade <= r.MaxGradePct / 100 + 1e-9 && fb.MinRadius >= r.MinRadiusM - 1e-6);
                    Row("n4", a.Name, $"{cls}: length; max grade (limit {r.MaxGradePct} %); min horizontal radius (limit {r.MinRadiusM} m); stations beyond the vertical-curve limit (VcCountTol) per km; road on water", "-", Fx(nb), "B water 0; grade, radius within limits", bOk ? "PASS" : "FAIL");
                }
                Row("n4", a.Name, "settlements on water; links not routable (water / grade, no bridges in the spike)", "-", $"{nb.SettlementsOnWater}; {nb.Unroutable} of {set.Corridors.Count} links unroutable", "B on water 0", nb.SettlementsOnWater == 0 ? "PASS" : "FAIL");
                notes.AddRange(nb.Notes.Select(x => $"area {a.Name}: {x}"));
                var worst = set.Corridors.Where(x => x.Routed).SelectMany(x => set.Segments[x.Link.Key].Select(sg => (x, sg))).OrderByDescending(t => t.sg.Seconds).First();
                notes.Add($"area {a.Name} B: slowest segment profile (repeat 1) {worst.sg.Seconds * 1000:F0} ms: {worst.x.Link.RoadClass} {worst.x.Link.A} - {worst.x.Link.B}, segment {worst.sg.Index} of {worst.x.Joints.Length - 1}, {worst.sg.LengthM:F0} m, {worst.sg.I1 - worst.sg.I0 + 1} stations, {worst.sg.BeyondCurve} stations still beyond the curve limit{(worst.sg.BeyondCurve > 0 ? " (bounds and pinned joints contradict the curve limit: the vertical-curve pass ran until its stall exit)" : "")}, max grade {worst.sg.MaxGrade * 100:F1} %");
                notes.Add($"area {a.Name} B: {nb.Straight} of {set.Corridors.Count(x => x.Routed)} routed corridors stayed ONE straight chord after string pulling (no fillet; 'R min none' in n4 = no curve at all in that class): the 200 m lattice + 20 m height tolerance straightens almost everything on this ground");
                var infeas = set.Segments.Values.SelectMany(x => x).Sum(x => x.Infeasible); var segLen = set.Segments.Values.SelectMany(x => x).Select(x => x.LengthM).ToList();
                notes.Add($"area {a.Name} B: {set.Settlements.Count} settlements ({set.Settlements.Count(s => s.Class == SettlementClass.City)} cities, {set.Settlements.Count(s => s.Class == SettlementClass.Town)} towns, {set.Settlements.Count(s => s.Class == SettlementClass.Village)} villages), {set.Corridors.Count} links, {set.Segments.Values.Sum(x => x.Length)} segments of {F(Stats.Q(segLen, 0) / 1000, "F2")}-{F(Stats.Q(segLen, 1) / 1000, "F2")} km (median {F(Stats.Q(segLen, 0.5) / 1000, "F2")}; {segLen.Count(x => x < 2000)} shorter than 2 km = corridors under 2 km), {infeas} contradicting profile bounds");
                string png = $"anchors_area{a.Name}.png"; Draw.Image(Path.Combine(dir, png), a, nb, set, shared); pngs.Add($"{png} {Png.Sha256(Path.Combine(dir, png))}");
            }
            // n5
            foreach (var a in areas)
            {
                var h = new[] { "forward", "reverse", "threads4" }.Select(m => { var p = Path.Combine(dir, $"hash-{m}.txt"); return File.Exists(p) ? File.ReadAllLines(p).Where(l => l.StartsWith(a.Name + ",")).Select(l => l.Split(',')[1]).FirstOrDefault() ?? "missing" : "missing"; }).ToList();
                bool same = h.All(x => x != "missing" && x == h[0]);
                Row("n5", a.Name, "anchor hash: process 1 forward, process 2 reverse order (areas and requests), process 3 four threads", "-", string.Join(" / ", h.Select(x => x.Length > 12 ? x.Substring(0, 12) : x)), "identical", same ? "PASS" : "FAIL");
            }
            // c rows: median over the repeats, [min-max] spread
            foreach (var a in areas)
            {
                var cs = costs[a.Name];
                string Sp(Func<Cost, double> f, string fmt) { var v = cs.Select(f).ToList(); return $"{F(Stats.Q(v, 0.5), fmt)} [{F(v.Min(), fmt)}-{F(v.Max(), fmt)}]"; }
                var c0 = cs[0];
                double rMed = Stats.Q(cs.Select(c => Stats.Q(c.RouteMs, 0.5)).ToList(), 0.5), sMed = Stats.Q(cs.Select(c => Stats.Q(c.SegMs, 0.5)).ToList(), 0.5);
                Row("c1", a.Name, "B settlements of the area (s); coarse route per corridor median / max (ms); profile per segment median / P95 / max (ms); retained bytes per km of road", "-",
                    $"{Sp(c => c.SettlementS, "F2")} s; route {Sp(c => Stats.Q(c.RouteMs, 0.5), "F1")} / {Sp(c => c.RouteMs.Max(), "F0")} ms; segment {Sp(c => Stats.Q(c.SegMs, 0.5), "F1")} / {Sp(c => Stats.Q(c.SegMs, 0.95), "F1")} / {Sp(c => c.SegMs.Max(), "F0")} ms; {Sp(c => c.BytesPerKm / 1024, "F1")} KiB/km ({c0.RoadKm:F0} km)",
                    "route <= 50 ms median, segment <= 300 ms median", rMed <= 50 && sMed <= 300 ? "PASS" : "FAIL");
                Row("c2", a.Name, $"cold-ready: {ColdPoints} seeded land points, time until all content within {ColdM} m exists (settlements + segments touching); median / max (s)",
                    "-", $"{Sp(c => Stats.Q(c.ColdB, 0.5), "F2")} / {Sp(c => c.ColdB.Max(), "F2")}", "report", "report");
                notes.AddRange(c0.ColdNotes.Select(x => $"area {a.Name} c2 (repeat 1): {x}"));
            }
            Write(dir, md, csv, rows, notes, pngs, areas, sha, hw, unity, repeats);
            return 0;
        }

        /// <summary>c2 for one area: ColdPoints seeded land points; B = a fresh field + builder.</summary>
        /// <summary>The 17 x 17 lattice probe of the removed region world IsOcean: every sample Ocean or Sea.</summary>
        static bool Ocean(RegionId id, PlanetSurface surf)
        {
            int step = RegionChart.Span / 16;
            for (int b = 0; b <= 16; b++)
                for (int a = 0; a <= 16; a++)
                {
                    var k = surf.Kind(Lattice.Direction(id.Face, id.Col * RegionChart.Span + a * step, id.Row * RegionChart.Span + b * step));
                    if (k != SurfaceKind.Ocean && k != SurfaceKind.Sea) return false;
                }
            return true;
        }

        static void Cold(Area a, PlanetSurface surf, Cost c, int rep)
        {
            ulong st = 0xC2C2UL + (ulong)a.Name[0]; int n = 0;
            while (n < ColdPoints)
            {
                var r = a.Regions[(int)(SplitMix64.Next(ref st) % (ulong)a.Regions.Count)]; int cells = 1 << r.Level;
                var p = CubeSphere.FromFaceAB(r.Face, -1.0 + 2.0 * (r.Col + SplitMix64.NextDouble(ref st)) / cells, -1.0 + 2.0 * (r.Row + SplitMix64.NextDouble(ref st)) / cells);
                if (surf.Kind(p) != SurfaceKind.Land) continue;
                n++;
                var sw = new Stopwatch();
                sw.Restart();
                var f = new SettlementField(Seed, surf); var cb = new CorridorBuilder(surf);
                int sets = f.Within(p, ColdM + AnchorParams.RadiusM[2], 0).Count(s => SettlementField.Gc(p, s.Dir) <= ColdM + s.RadiusM);
                var links = new SortedDictionary<ulong, Link>();
                for (int k = 0; k < 3; k++)
                {
                    double rk = AnchorParams.LayerRadius[k], dk = Math.Sqrt((rk + CorridorBuilder.MarginM) * (rk + CorridorBuilder.MarginM) + Math.Pow(Math.Max(CorridorBuilder.MinHalfWidthM, CorridorBuilder.HalfWidthShare * rk), 2)) + ColdM;
                    foreach (var s in f.Within(p, dk, k)) foreach (var l in f.LinksOf(s)) links[l.Key] = l;
                }
                int routed = 0, segs = 0;
                foreach (var l in links.Values)
                {
                    var shell = cb.Domain(l); shell.Chart.ToLocal(p, out double px, out double pz);
                    if (shell.DomainDist(px, pz) > ColdM) continue;
                    var co = cb.Route(l); routed++;
                    if (!co.Routed) continue;
                    for (int k = 0; k + 1 < co.Joints.Length; k++)
                    {
                        bool hit = false;
                        for (int i = co.Joints[k]; i <= co.Joints[k + 1] && !hit; i++) hit = (co.X[i] - px) * (co.X[i] - px) + (co.Z[i] - pz) * (co.Z[i] - pz) <= ColdM * ColdM;
                        if (hit) { CorridorBuilder.Profile(co, k); segs++; }
                    }
                }
                c.ColdB.Add(sw.Elapsed.TotalSeconds);
                if (rep == 1) c.ColdNotes.Add($"point {n}: B {sets} settlements, {links.Count} candidate links, {routed} routed, {segs} segments profiled, {c.ColdB.Last():F2} s");
            }
        }

        static void Write(string dir, string md, string csv, List<string[]> rows, List<string> notes, List<string> pngs, List<Area> areas, string sha, string hw, string unity, int repeats)
        {
            md = md ?? Path.Combine(dir, "result.md"); csv = csv ?? Path.Combine(dir, "result.csv");
            var c = new StringBuilder("row,area,metric,variant_a,variant_b,target,verdict\n");
            foreach (var r in rows) c.Append(string.Join(",", r.Select(x => "\"" + x.Replace("\"", "'") + "\"")) + "\n");
            File.WriteAllText(csv, c.ToString());
            var m = new StringBuilder();
            m.Append("# What If planet - anchor spike 1 (RG-177): anchors (B); the region worlds (A) were removed in RG-185 (A column prints a dash)\n\n");
            m.Append($"Generated by `tools/whatif-anchors/run-anchors.ps1` ({DateTime.Now:yyyy-MM-dd HH:mm}); commit {sha}; {hw}. Seed {Seed}. Timing rows: {repeats} repeats in one process, value = median of the repeats [min-max]. {unity}\n\n");
            m.Append("B = `src/WhatIf.Planet.Anchors`: settlements per cell (village level 11, town level 10, city level 9), relative-neighbourhood links per layer (secondary <= 10 km among all, primary <= 20 km among towns + cities, motorway <= 40 km among cities), A* on a 200 m chart lattice (water forbidden, grade > 2 x class max forbidden, slope cost), string pulling, fillets at 1.5 x class minimum radius, 10 m stations, segments of <= 5 km with the joint height and grade pinned, the WorldGen vertical-curve solver per segment (`Generator.SolveVerticalProfile`).\n\n");
            foreach (var a in areas) m.Append($"- Area {a.Name}: 5 x 5 regions around {a.Centre} ({a.Regions.Count} distinct, faces {a.Faces}); block: {a.Why}\n");
            m.Append("\n| row | area | measurement | A (regions) | B (anchors) | target | verdict |\n|---|---|---|---|---|---|---|\n");
            foreach (var r in rows) m.Append("| " + string.Join(" | ", r.Select(x => x.Replace("|", "/"))) + " |\n");
            m.Append("\n## How to read this (limits of the comparison)\n\n");
            m.Append("- The spike does not decide; RG-178 adds sectors and applies the decision rule. FAIL rows would stay FAIL here.\n");
            m.Append("- n4 on B's side: the ground is the planet surface + a global detail layer of the WorldGen size (`Ground`, about +-11 m at 64-256 m cells), so grades are measured on comparable relief. Grade and radius limits hold by construction (grade cap 0.9 x class max, fillets >= class minimum); the informative B numbers are the vertical-curve counts and the share of straight corridors.\n");
            m.Append("- Straightness: almost every B corridor is a single straight chord between two settlements (Notes). A real road of these classes bends every few hundred metres (fitted WorldGen straight runs, p50 51-85 m for secondary to motorway). The cost rows are not affected (station count depends on length only), the look is: RG-178 needs a meander / leg-length rule.\n");
            m.Append("\n## Top views\n\nRaw folder (git-ignored), region borders black, motorway red, primary orange, secondary yellow, other white, settlements magenta, unroutable B links grey dots:\n\n");
            foreach (var p in pngs) m.Append($"- `{p}`\n");
            m.Append("\n## Notes\n\n"); foreach (var n in notes) m.Append($"- {n}\n");
            File.WriteAllText(md, m.ToString());
            Console.WriteLine($"wrote {md} and {csv}");
        }
    }
}
