using System;
using System.Collections.Generic;
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
    /// <summary>RG-181 rows r1-r7: the corridors before (CorridorBuilder.Legacy = RG-177/178) and after (surface-aware routing, meander, feasible joints) on the same areas, plus before / after pictures.</summary>
    public static class Corridors
    {
        const double ChordDevM = 20, ChordShare = 0.9;
        static string Cls(Corridor c) => c.Link.RoadClass;

        sealed class Built { public AnchorSet Set; public double Wall; }

        static Built Build(Area a, bool legacy)
        {
            var surf = new PlanetSurface(Seed); var cb = new CorridorBuilder(surf) { Legacy = legacy };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var set = AnchorSet.Build(new SettlementField(Seed, surf), cb, a.Regions, false);
            return new Built { Set = set, Wall = sw.Elapsed.TotalSeconds };
        }

        /// <summary>True when more than 90 % of the stations lie within 20 m of the chord A-B (the straight line in the corridor's chart).</summary>
        public static bool IsChord(Corridor c)
        {
            c.Chart.ToLocal(c.Link.A.Dir, out double ax, out double az); c.Chart.ToLocal(c.Link.B.Dir, out double bx, out double bz);
            double L = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)), ux = (bx - ax) / L, uz = (bz - az) / L; int near = 0;
            for (int i = 0; i < c.X.Length; i++) { double dx = c.X[i] - ax, dz = c.Z[i] - az; if (Math.Abs(-dx * uz + dz * ux) < ChordDevM) near++; }
            return near > ChordShare * c.X.Length;
        }

        public static int Run(string dir, List<Area> areas, string md, string csv, string sha, string hw, string unity)
        {
            var rows = new List<string[]>(); var notes = new List<string>(); var pngs = new List<string>();
            void Row(string row, string area, string metric, string before, string after, string target, string verdict) => rows.Add(new[] { row, area, metric, before, after, target, verdict });
            var rules = CorridorBuilder.Rules;
            foreach (var a in areas)
            {
                Build(a, false);   // warm-up (JIT, surface tables); the timed builds follow
                var bo = Build(a, true); var bn = Build(a, false);
                var so = bo.Set; var sn = bn.Set;
                var ro = so.Corridors.Where(c => c.Routed).ToList(); var rn = sn.Corridors.Where(c => c.Routed).ToList();
                notes.Add($"area {a.Name}: before {so.Corridors.Count} links, {ro.Count} routed (wall {bo.Wall:F1} s); after {sn.Corridors.Count} links, {rn.Count} routed (wall {bn.Wall:F1} s)");
                foreach (var c in so.Corridors.Where(c => !c.Routed).Select(c => (c, "before")).Concat(sn.Corridors.Where(c => !c.Routed).Select(c => (c, "after")))) notes.Add($"area {a.Name} {c.Item2}: unroutable {Cls(c.c)} {c.c.Link.A} - {c.c.Link.B}: {c.c.Why}");
                var classes = rn.Concat(ro).Select(Cls).Distinct().OrderBy(x => Array.IndexOf(WhatIf.WorldGen.RoadClass.All, x)).ToList();
                // r1
                string Ratio(List<Corridor> l, string cls) { var v = l.Where(c => Cls(c) == cls).Select(c => c.LengthM / c.Link.GcM).ToList(); return v.Count == 0 ? "-" : $"{F(Stats.Q(v, 0.5), "F3")} / {F(Stats.Q(v, 0.95), "F3")} (n {v.Count})"; }
                foreach (var cls in classes) Row("r1", a.Name, $"{cls}: route length / great-circle length, median / P95", Ratio(ro, cls), Ratio(rn, cls), "report", "report");
                // r2
                int co = ro.Count(IsChord), cn = rn.Count(IsChord);
                Row("r2", a.Name, "chord share: routed corridors whose stations lie within 20 m of the chord over > 90 % of the length", $"{co} / {ro.Count} ({100.0 * co / Math.Max(1, ro.Count):F0} %)", $"{cn} / {rn.Count} ({100.0 * cn / Math.Max(1, rn.Count):F0} %)", "after <= 20 %", 100.0 * cn / Math.Max(1, rn.Count) <= 20 ? "PASS" : "FAIL");
                // r3
                foreach (var cls in classes)
                {
                    var r = rules.Of(cls); string Fx(AnchorSet s, List<Corridor> l, out bool ok)
                    {
                        var cs = l.Where(c => Cls(c) == cls).ToList(); ok = true; if (cs.Count == 0) return "-";
                        double gmax = cs.SelectMany(c => s.Segments[c.Link.Key]).Select(g => g.MaxGrade).DefaultIfEmpty(0).Max(), rmin = cs.Min(c => c.MeasuredRadiusM);
                        int gv = cs.SelectMany(c => s.Segments[c.Link.Key]).Count(g => g.MaxGrade > r.MaxGradePct / 100 + 1e-9), rv = cs.Count(c => c.MeasuredRadiusM < r.MinRadiusM * CorridorBuilder.RadiusTol), rv0 = cs.Count(c => c.MeasuredRadiusM < r.MinRadiusM - 1e-6);
                        ok = gv == 0 && rv == 0;
                        return $"grade {gmax * 100:F2} % ({gv} segs over); R min {F(rmin, "F0")} m ({rv} corridors < {CorridorBuilder.RadiusTol:F2} x, {rv0} < 1.00 x limit)";
                    }
                    string b = Fx(so, ro, out _), n = Fx(sn, rn, out bool okn);
                    Row("r3", a.Name, $"{cls}: max grade (limit {r.MaxGradePct} %), min horizontal radius measured on the stations (circle through stations 30 m apart; limit {r.MinRadiusM} m)", b, n, "0 violations", okn ? "PASS" : "FAIL");
                }
                // r4
                string Seg(AnchorSet s, out int stalls, out int segs, out double max)
                {
                    var l = s.Segments.Values.SelectMany(x => x).ToList(); var ms = l.Select(x => x.Seconds * 1000).ToList(); stalls = l.Count(x => x.BeyondCurve > 0); segs = l.Count; max = ms.Max();
                    return $"{F(Stats.Q(ms, 0.5), "F1")} / {F(Stats.Q(ms, 0.95), "F1")} / {F(max, "F0")} ms ({segs} segments); stall exits {stalls} ({l.Sum(x => x.BeyondCurve)} stations beyond the curve limit)";
                }
                string r4o = Seg(so, out _, out _, out _), r4n = Seg(sn, out int st, out _, out double mx);
                Row("r4", a.Name, "segment profile solve (warm process, single thread): median / P95 / max; segments that ended beyond the curve limit (the vertical-curve pass ran to its stall exit)", r4o, r4n, "max <= 300 ms; stall exits counted (target 0)", mx <= 300 ? (st == 0 ? "PASS" : "PASS (max), stall exits " + st) : "FAIL");
                // r5
                string Rt(List<Corridor> l) { var v = l.Select(c => c.RouteSeconds * 1000).ToList(); return $"{F(Stats.Q(v, 0.5), "F1")} / {F(v.Max(), "F0")} ms"; }
                Row("r5", a.Name, "coarse route time per corridor (A*, pull, meander, stations, joints): median / max", Rt(ro), Rt(rn), "max <= 50 ms", rn.Max(c => c.RouteSeconds * 1000) <= 50 ? "PASS" : "FAIL");
                // r6
                int wo = ro.Sum(c => c.OnWater.Count(x => x)), wn = rn.Sum(c => c.OnWater.Count(x => x));
                Row("r6", a.Name, "stations on water (ocean / sea / lake) over all routed corridors", $"{wo} of {ro.Sum(c => c.X.Length)}", $"{wn} of {rn.Sum(c => c.X.Length)}", "0", wn == 0 ? "PASS" : "FAIL");
                // meander facts
                notes.Add($"area {a.Name} after: meander scale per routed corridor 1.0 / 0.6 / 0.3 / 0 (no meander): {string.Join(" / ", new[] { 1.0, 0.6, 0.3, 0.0 }.Select(x => rn.Count(c => c.MeanderScale == x)))}; corridors shorter than {CorridorBuilder.MeanderMinM:F0} m: {rn.Count(c => c.LengthM < CorridorBuilder.MeanderMinM)}");
                notes.Add($"area {a.Name}: route length total before {ro.Sum(c => c.LengthM) / 1000:F1} km, after {rn.Sum(c => c.LengthM) / 1000:F1} km; polyline vertices after pulling median before {Stats.Q(ro.Select(c => (double)c.Vertices).ToList(), 0.5):F0}, after {Stats.Q(rn.Select(c => (double)c.Vertices).ToList(), 0.5):F0}");
                var worst = sn.Corridors.Where(c => c.Routed).SelectMany(c => sn.Segments[c.Link.Key].Select(g => (c, g))).OrderByDescending(t => t.g.Seconds).First();
                notes.Add($"area {a.Name} after: slowest segment {worst.g.Seconds * 1000:F0} ms ({Cls(worst.c)}, {worst.g.LengthM:F0} m, {worst.g.BeyondCurve} stations beyond the curve limit)");
                foreach (var t in sn.Corridors.Where(c => c.Routed).SelectMany(c => sn.Segments[c.Link.Key].Select(g => (c, g))).Where(t => t.g.BeyondCurve > 0).OrderByDescending(t => t.g.Seconds).Take(5)) notes.Add($"area {a.Name} after: stall exit {Cls(t.c)} {t.c.Link.A} - {t.c.Link.B} segment {t.g.Index} of {t.c.Joints.Length - 1}: {t.g.Seconds * 1000:F0} ms, {t.g.LengthM:F0} m, {t.g.BeyondCurve} stations beyond");
                foreach (var (name, step) in new[] { ("overview", 120.0), ("zoom", 30.0) })
                {
                    string png = $"corridors_area{a.Name}_{name}.png"; DrawPair(Path.Combine(dir, png), a, so, sn, step, name == "zoom"); pngs.Add($"{png} {Png.Sha256(Path.Combine(dir, png))}");
                }
            }
            // r7
            foreach (var a in areas)
            {
                var h = new[] { "forward", "reverse", "threads4" }.Select(m => { var p = Path.Combine(dir, $"hash-{m}.txt"); return File.Exists(p) ? File.ReadAllLines(p).Where(l => l.StartsWith(a.Name + ",")).Select(l => l.Split(',')[1]).FirstOrDefault() ?? "missing" : "missing"; }).ToList();
                bool same = h.All(x => x != "missing" && x == h[0]);
                Row("r7", a.Name, "anchor hash (settlements, corridor stations, segment profiles): process 1 forward, process 2 reverse order, process 3 four threads", "-", string.Join(" / ", h.Select(x => x.Length > 12 ? x.Substring(0, 12) : x)), "identical", same ? "PASS" : "FAIL");
            }
            var m = new StringBuilder();
            m.Append("\n## Corridors B2 (RG-181): surface-aware routing, meander, feasible joints\n\n");
            m.Append($"Generated by `tools/whatif-anchors/run-anchors.ps1 -Corridors` ({DateTime.Now:yyyy-MM-dd HH:mm}); commit {sha}; {hw}. Seed {Seed}. {unity}\n\n");
            m.Append("before = `CorridorBuilder.Legacy = true` (the RG-177/178 corridor: pull tolerance 20 m, no meander, joints pinned to the raw surface); after = the default builder: pull tolerance 8 m, deterministic meander per half-wave lobe (link key seed, wavelength 1.8-3.2 km, amplitude min(120 m, 5 % of the length), only where the ground beside the line is dry and within 6 m of the line height, radius check on the stations), joints from the smoothed ground (+-500 m) with grade 0 at local extremes. Timing rows: one warm-up build, then one timed single-thread build per column.\n\n");
            m.Append("| row | area | measurement | before (RG-178) | after (RG-181) | target | verdict |\n|---|---|---|---|---|---|---|\n");
            foreach (var r in rows) m.Append("| " + string.Join(" | ", r.Select(x => x.Replace("|", "/"))) + " |\n");
            m.Append("\nPictures (raw folder, git-ignored): left before, right after; overview 120 m per pixel, zoom 30 m per pixel around the area centre; motorway red, primary orange, secondary yellow, other white, settlements magenta:\n\n");
            foreach (var p in pngs) m.Append($"- `{p}`\n");
            m.Append("\nNotes:\n\n"); foreach (var n in notes) m.Append($"- {n}\n");
            var c2 = new StringBuilder(); foreach (var r in rows) c2.Append(string.Join(",", r.Select(x => "\"" + x.Replace("\"", "'") + "\"")) + "\n");
            if (md == null) { File.WriteAllText(Path.Combine(dir, "corridors-section.md"), m.ToString()); File.WriteAllText(Path.Combine(dir, "corridors.csv"), "row,area,metric,before,after,target,verdict\n" + c2); }
            else { File.AppendAllText(md, m.ToString()); File.AppendAllText(csv, c2.ToString()); }
            Console.WriteLine($"corridors: {rows.Count} rows, FAIL {rows.Count(r => r[6].StartsWith("FAIL"))}");
            return 0;
        }

        /// <summary>Before (left) and after (right) on the same surface; north up, 600 x 600 px at step m per pixel; zoom draws the stations as the polyline with 2 px lines.</summary>
        static void DrawPair(string path, Area a, AnchorSet so, AnchorSet sn, double step, bool zoom)
        {
            const int N = 600, Gap = 12; int W = 2 * N + Gap; var img = new byte[W * N * 3]; var ch = new RegionChart(a.Centre); var surf = new PlanetSurface(Seed);
            for (int i = 0; i < img.Length; i++) img[i] = 255;
            for (int py = 0; py < N; py++)
                for (int px = 0; px < N; px++)
                {
                    var sm = surf.Sample(ch.ToDir((px - N / 2.0) * step, (N / 2.0 - py) * step)); byte r, g, b;
                    switch (sm.Kind)
                    {
                        case SurfaceKind.Ocean: r = 10; g = 30; b = 90; break;
                        case SurfaceKind.Sea: r = 30; g = 80; b = 150; break;
                        case SurfaceKind.Lake: r = 70; g = 160; b = 210; break;
                        case SurfaceKind.Beach: r = 225; g = 210; b = 150; break;
                        default: double f = sm.Forest; r = (byte)(165 - 120 * f); g = (byte)(175 - 60 * f); b = (byte)(95 - 50 * f); break;
                    }
                    for (int side = 0; side < 2; side++) { int o = (py * W + px + side * (N + Gap)) * 3; img[o] = r; img[o + 1] = g; img[o + 2] = b; }
                }
            void Dot(int x, int y, byte r, byte g, byte b, int t) { for (int dy = 0; dy < t; dy++) for (int dx = 0; dx < t; dx++) { int xx = x + dx - t / 2, yy = y + dy - t / 2; if (xx < 0 || yy < 0 || xx >= W || yy >= N) continue; int o = (yy * W + xx) * 3; img[o] = r; img[o + 1] = g; img[o + 2] = b; } }
            bool Px(Vec3 d, int side, out int x, out int y)
            {
                ch.ToChart(d, out double cx, out double cz); x = (int)Math.Round(cx / step + N / 2.0); y = (int)Math.Round(N / 2.0 - cz / step);
                bool ok = x >= 0 && y >= 0 && x < N && y < N; x += side * (N + Gap); return ok;
            }
            void Line(Vec3 p, Vec3 q, int side, byte r, byte g, byte b, int t)
            {
                Px(p, side, out int x0, out int y0); Px(q, side, out int x1, out int y1);
                int m = Math.Max(1, Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
                if (m > 4000) return;
                for (int k = 0; k <= m; k++) { int x = x0 + (x1 - x0) * k / m, y = y0 + (y1 - y0) * k / m, lx = x - side * (N + Gap); if (lx >= 0 && lx < N && y >= 0 && y < N) Dot(x, y, r, g, b, t); }
            }
            void Roads(AnchorSet s, int side)
            {
                foreach (var c in s.Corridors.Where(x => x.Routed))
                {
                    string cls = Cls(c); byte r = 255, g = 255, b = 255; int t = zoom ? 2 : 1;
                    if (cls == WhatIf.WorldGen.RoadClass.Motorway) { r = 220; g = 30; b = 30; t = zoom ? 3 : 3; }
                    else if (cls == WhatIf.WorldGen.RoadClass.Primary || cls == WhatIf.WorldGen.RoadClass.Link) { r = 255; g = 140; b = 0; t = 2; }
                    else if (cls == WhatIf.WorldGen.RoadClass.Secondary) { r = 255; g = 230; b = 60; t = 2; }
                    for (int i = 0; i + 3 < c.X.Length; i += 3) Line(c.Chart.ToDir(c.X[i], c.Z[i]), c.Chart.ToDir(c.X[i + 3], c.Z[i + 3]), side, r, g, b, t);
                }
                foreach (var st in s.Settlements) if (Px(st.Dir, side, out int x, out int y)) Dot(x, y, 230, 0, 230, st.Class == SettlementClass.City ? 9 : st.Class == SettlementClass.Town ? 7 : 5);
            }
            Roads(so, 0); Roads(sn, 1);
            Png.Write(path, W, N, img);
        }
    }
}
