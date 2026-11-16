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
using GNode = WhatIf.WorldGen.GNode;
using GEdge = WhatIf.WorldGen.GEdge;
using V2 = WhatIf.WorldGen.V2;

namespace WhatIf.Planet.Anchors.Experiment
{
    /// <summary>
    /// RG-182 rows e1-e5 (CPU only): the settlement worlds of the two areas before (RG-181: village street budget for every class, the corridor ran to the centre) and after
    /// (town / city street budget, pinned corridor entrances), plus a before / after street plan of one city and one town per area.
    /// </summary>
    public static class SettlementRows
    {
        static readonly string[] ClassName = { "village", "town", "city" };
        static bool Water(SurfaceKind k) => k == SurfaceKind.Ocean || k == SurfaceKind.Sea || k == SurfaceKind.Lake;

        sealed class UF
        {
            readonly int[] p; public UF(int n) { p = Enumerable.Range(0, n).ToArray(); }
            public int Find(int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }
            public void Union(int a, int b) { p[Find(a)] = Find(b); }
        }

        static string Q3(List<double> v, string f = "F1") => v.Count == 0 ? "-" : $"{F(Stats.Q(v, 0.5), f)} / {F(Stats.Q(v, 0.95), f)} / {F(v.Max(), f)}";

        public static int Run(string dir, List<Area> areas, string md, string csv, string sha, string hw, string unity)
        {
            var rows = new List<string[]>(); var notes = new List<string>(); var pngs = new List<string>();
            void Row(string row, string area, string metric, string before, string after, string target, string verdict) => rows.Add(new[] { row, area, metric, before, after, target, verdict });
            foreach (var a in areas)
            {
                var aw = new AnchorWorld(Seed) { KeepGraphs = true }; var f = aw.Field;   // RG-184: the e-rows read the street graph
                var sets = AnchorSet.InRegions(f, a.Regions, false);
                var worlds = sets.Select(s => (s, w: aw.World(s))).ToList();   // after: cached, in id order
                var before = sets.Select(s => (s, w: new SettlementWorld(aw, s, true, true))).ToList();
                notes.Add($"area {a.Name}: settlements in the 25 regions {string.Join(", ", Enumerable.Range(0, 3).Select(c => $"{sets.Count(s => (int)s.Class == c)} {ClassName[c]}"))}; routed corridors in the world cache {aw.RoutedCount} (settlement worlds route every link of their settlement, also those that leave the area)");

                // ---- e1
                var nodesA = new double[3];
                for (int c = 0; c < 3; c++) { var ok = worlds.Where(t => (int)t.s.Class == c && t.w.Gen != null).Select(t => (double)t.w.Gen.Graph.Nodes.Count).ToList(); nodesA[c] = ok.Count == 0 ? 0 : Stats.Q(ok, 0.5); }
                double vMed = nodesA[0];
                for (int c = 0; c < 3; c++)
                {
                    var wb = before.Where(t => (int)t.s.Class == c).ToList(); var wa = worlds.Where(t => (int)t.s.Class == c).ToList();
                    string Fx(List<(Settlement s, SettlementWorld w)> l)
                    {
                        if (l.Count == 0) return "none in the area";
                        var ok = l.Where(t => t.w.Gen != null).ToList(); var n = ok.Select(t => (double)t.w.Gen.Graph.Nodes.Count).ToList(); var km = ok.Select(t => t.w.Gen.Graph.Edges.Sum(e => e.LengthM) / 1000).ToList();
                        return $"n {l.Count}; nodes median {F(n.Count == 0 ? 0 : Stats.Q(n, 0.5), "F0")} / max {F(n.DefaultIfEmpty(0).Max(), "F0")}; street km median {F(km.Count == 0 ? 0 : Stats.Q(km, 0.5), "F1")}; failed {l.Count - ok.Count}";
                    }
                    bool pass = wa.Count == 0 || (wa.All(t => t.w.Gen != null) && (c == 0 || vMed == 0 || nodesA[c] >= 5 * vMed));
                    Row("e1", a.Name, $"{ClassName[c]} settlement worlds: count, graph nodes median / max, street km median, failed builds", Fx(wb), Fx(wa), c == 0 ? "0 failed" : "node median >= 5 x village median; 0 failed", wa.Count == 0 ? "n/a (none in the area)" : pass ? "PASS" : "FAIL");
                }

                // ---- e2, e3, e4
                int ends = 0, connected = 0, noWorld = 0, fallbacks = 0, unrouted = 0; var gapH = new List<double>(); var gapV = new List<double>(); var gapA = new List<double>();
                long stations = 0, wet = 0; var notConn = new List<string>();
                foreach (var (s, w) in worlds)
                {
                    var links = f.LinksOf(s); var routed = new List<(RoutedLink rl, bool atA)>();
                    foreach (var l in links) { var rl = aw.Route(l); if (rl == null) { unrouted++; continue; } routed.Add((rl, l.A.Id == s.Id)); }
                    ends += routed.Count;
                    if (w.Gen == null) { noWorld += routed.Count; continue; }
                    fallbacks += w.Gen.Log.Count(x => x.Contains("fallback lead-in"));
                    var g = w.Gen.Graph; var uf = new UF(g.Nodes.Count); foreach (var e in g.Edges) uf.Union(e.A, e.B);
                    int centre = g.Nodes.OrderBy(n => n.P.X * n.P.X + n.P.Z * n.P.Z).First().Id; int cc = uf.Find(centre);
                    for (int i = 0; i < routed.Count; i++)
                    {
                        var ent = w.Ents[i]; var (rl, atA) = routed[i];
                        GNode node = null; double bd = 0.5; foreach (var n in g.Nodes) { double d = V2.Dist(n.P, ent.Point); if (d < bd) { bd = d; node = n; } }
                        if (node == null || uf.Find(node.Id) != cc) { notConn.Add($"{s} entrance {i}"); continue; }
                        connected++;
                        // e3: corridor end station (profiled) vs the first station of the entrance road
                        var c = rl.C; int n0 = c.X.Length, ei = atA ? 0 : n0 - 1;
                        w.Chart.ToLocal(c.Chart.ToDir(c.X[ei], c.Z[ei]), out double x0, out double z0);
                        double yc = atA ? rl.Seg(0).Y[0] : rl.Seg(rl.SegmentCount - 1).Y[rl.Seg(rl.SegmentCount - 1).Y.Length - 1];
                        GEdge edge = g.Edges.FirstOrDefault(e => e.A == node.Id || e.B == node.Id); if (edge == null) continue;
                        bool first = edge.A == node.Id; int last = edge.Pts.Length - 1;
                        V2 p0 = first ? edge.Pts[0] : edge.Pts[last], p1 = first ? edge.Pts[1] : edge.Pts[last - 1]; double h0 = first ? edge.H[0] : edge.H[last];
                        gapH.Add(Math.Sqrt((p0.X - x0) * (p0.X - x0) + (p0.Z - z0) * (p0.Z - z0))); gapV.Add(Math.Abs(h0 - yc));
                        V2 d1 = (p1 - p0).Unit; double ang = Math.Acos(Math.Max(-1, Math.Min(1, d1.X * ent.Dir.X + d1.Z * ent.Dir.Z))) * 180 / Math.PI; gapA.Add(ang);
                        if (ang > 1 || Math.Abs(h0 - yc) > 0.05) notes.Add($"area {a.Name} e3 heading {ang:F1} deg, dy {Math.Abs(h0 - yc):F3} m, edge H {edge.H.Length}: {s} entrance {i} ({ent.Class}), edge {edge.Class} {edge.LengthM:F0} m, edges at the node {g.Edges.Count(e => e.A == node.Id || e.B == node.Id)}, graph log '{string.Join(" | ", w.Gen.Log.Where(x => x.StartsWith("entrance " + (i + 1) + ":")).Select(x => x.Length > 90 ? x.Substring(0, 90) : x))}'");
                    }
                    var surf = AnchorWorld.ThreadSurface(Seed);
                    foreach (var e in g.Edges) foreach (var p in e.Pts) { stations++; if (Water(surf.Kind(w.Chart.ToDir(p.X, p.Z)))) wet++; }
                }
                Row("e2", a.Name, $"corridor ends at the area's settlements (routed links; {unrouted} unroutable link ends skipped) / entrances connected to the street graph (same graph component as the centre node)", "-", $"{ends} / {connected} ({100.0 * connected / Math.Max(1, ends):F1} %); settlement worlds without a graph: {noWorld} ends; fallback bends {fallbacks}", "100 %", connected == ends && ends > 0 ? "PASS" : "FAIL");
                if (notConn.Count > 0) notes.Add($"area {a.Name} e2 not connected: {string.Join("; ", notConn.Take(8))}");
                double mh = gapH.DefaultIfEmpty(0).Max(), mv = gapV.DefaultIfEmpty(0).Max(), ma2 = gapA.DefaultIfEmpty(0).Max();
                Row("e3", a.Name, $"entrance gap over {gapH.Count} entrances: max horizontal offset (m) / max vertical offset (m) / max heading change (deg) between the corridor's last station (profiled) and the entrance road's first station", "-", $"{F(mh, "F4")} / {F(mv, "F4")} / {F(ma2, "F2")}; heading > 1 deg: {gapA.Count(x => x > 1)}", "<= 0.01 / <= 0.05 / <= 1", mh <= 0.01 && mv <= 0.05 && ma2 <= 1 ? "PASS" : "FAIL");
                Row("e4", a.Name, "street graph points (every edge sample, entrance roads included) on water (ocean / sea / lake)", "-", $"{wet} of {stations}", "0", wet == 0 ? "PASS" : "FAIL");

                // ---- e5
                for (int c = 0; c < 3; c++)
                {
                    var wa = worlds.Where(t => (int)t.s.Class == c).ToList(); if (wa.Count == 0) continue;
                    var wb = before.Where(t => (int)t.s.Class == c).ToList();
                    var tb = wb.Select(t => t.w.Seconds).ToList(); var ta = wa.Select(t => t.w.Seconds).ToList(); var te = wa.Select(t => t.w.EntranceSeconds).ToList();
                    // memory: three fresh worlds of the class, retained, GC delta (entrances route from the warm cache of aw)
                    int k = Math.Min(3, wa.Count);
                    long Mem(bool bef) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long m0 = GC.GetTotalMemory(true); var keep = wa.Take(k).Select(t => new SettlementWorld(aw, t.s, bef)).ToList(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long m1 = GC.GetTotalMemory(true); GC.KeepAlive(keep); return (m1 - m0) / k; }
                    long memB = Mem(true), memA = Mem(false);
                    double max = ta.Max();
                    Row("e5", a.Name, $"{ClassName[c]} settlement world build (first build in the process, WorldGen only): median / P95 / max (s); entrance routing from the cold cache median / max (s); managed memory per world (MiB, mean of {k} fresh worlds)", $"{Q3(tb, "F3")}; mem {memB / 1048576.0:F2}", $"{Q3(ta, "F3")}; entrances {F(Stats.Q(te, 0.5), "F3")} / {F(te.Max(), "F3")}; mem {memA / 1048576.0:F2}", c == 2 ? "city max <= 1.0 s, report memory" : "report", c == 2 ? (max <= 1.0 ? "PASS" : "FAIL") : "report");
                }

                // ---- plans
                foreach (int c in new[] { 2, 1 })
                {
                    var t = worlds.Where(x => (int)x.s.Class == c && x.w.Gen != null && x.w.Ents.Count > 0).OrderByDescending(x => x.w.Ents.Count).FirstOrDefault();
                    if (t.s == null) { notes.Add($"area {a.Name}: no {ClassName[c]} with a graph and entrances for the plan"); continue; }
                    var b = before.First(x => x.s.Id == t.s.Id);
                    string png = $"plan_area{a.Name}_{ClassName[c]}.png"; Plan(Path.Combine(dir, png), t.s, b.w, t.w); pngs.Add($"{png} {Png.Sha256(Path.Combine(dir, png))} ({t.s}, {t.w.Ents.Count} entrances)");
                }
            }
            var m = new StringBuilder();
            m.Append("\n## Settlements B3 (RG-182): town / city streets and pinned corridor entrances\n\n");
            m.Append($"Generated by `tools/whatif-anchors/run-anchors.ps1 -Settlements` ({DateTime.Now:yyyy-MM-dd HH:mm}); commit {sha}; {hw}. Seed {Seed}. {unity}\n\n");
            m.Append("before = the RG-181 settlement world (`CentreIsCity` unset: every class gets the village street budget of 550-900 m, no entrance roads; the corridor ran to the centre); after = `CentreIsCity` for town / city (arterial seeds, street budget 2 pi R^2 / 112.5 m) and one `WorldSpec.Entrances` road per routed corridor end: the corridor stops where it crosses the settlement radius, the entrance road starts at that station (position, direction, profiled height pinned) and runs straight along the corridor heading until it joins the street graph.\n\n");
            m.Append("| row | area | measurement | before (RG-181) | after (RG-182) | target | verdict |\n|---|---|---|---|---|---|---|\n");
            foreach (var r in rows) m.Append("| " + string.Join(" | ", r.Select(x => x.Replace("|", "/"))) + " |\n");
            m.Append("\nPlans (raw folder, git-ignored): left before (the corridor drawn schematically straight from its entrance to the centre, where it used to end), right after; north up, settlement radius ring grey, streets white, entrance roads by class (motorway red, primary orange, secondary yellow), entrance points magenta:\n\n");
            foreach (var p in pngs) m.Append($"- `{p}`\n");
            m.Append("\nNotes:\n\n"); foreach (var n in notes) m.Append($"- {n}\n");
            var c2 = new StringBuilder(); foreach (var r in rows) c2.Append(string.Join(",", r.Select(x => "\"" + x.Replace("\"", "'") + "\"")) + "\n");
            if (md == null) { File.WriteAllText(Path.Combine(dir, "settlements-section.md"), m.ToString()); File.WriteAllText(Path.Combine(dir, "settlements.csv"), "row,area,metric,before,after,target,verdict\n" + c2); }
            else { File.AppendAllText(md, m.ToString()); File.AppendAllText(csv, c2.ToString()); }
            Console.WriteLine($"settlements: {rows.Count} rows, FAIL {rows.Count(r => r[6].StartsWith("FAIL"))}");
            return 0;
        }

        /// <summary>Before (left) and after (right) street plan of one settlement in its own chart: 700 x 700 px, the radius ring at 80 % of the half width.</summary>
        static void Plan(string path, Settlement s, SettlementWorld before, SettlementWorld after)
        {
            const int N = 700, Gap = 12; int W = 2 * N + Gap; var img = new byte[W * N * 3]; double half = s.RadiusM * 1.25, step = 2 * half / N;
            for (int i = 0; i < img.Length; i += 3) { img[i] = 28; img[i + 1] = 32; img[i + 2] = 36; }
            void Dot(int x, int y, byte r, byte g, byte b, int t) { for (int dy = 0; dy < t; dy++) for (int dx = 0; dx < t; dx++) { int xx = x + dx - t / 2, yy = y + dy - t / 2; if (xx < 0 || yy < 0 || xx >= W || yy >= N) continue; int o = (yy * W + xx) * 3; img[o] = r; img[o + 1] = g; img[o + 2] = b; } }
            void Px(double x, double z, int side, out int px, out int py) { px = (int)Math.Round(x / step + N / 2.0) + side * (N + Gap); py = (int)Math.Round(N / 2.0 - z / step); }
            void Line(double x0, double z0, double x1, double z1, int side, byte r, byte g, byte b, int t)
            {
                Px(x0, z0, side, out int ax, out int ay); Px(x1, z1, side, out int bx, out int by); int m = Math.Max(1, Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)));
                for (int k = 0; k <= m; k++) { int x = ax + (bx - ax) * k / m, y = ay + (by - ay) * k / m, lx = x - side * (N + Gap); if (lx >= 0 && lx < N && y >= 0 && y < N) Dot(x, y, r, g, b, t); }
            }
            for (int side = 0; side < 2; side++)
            {
                for (int k = 0; k < 720; k++) { double a0 = k * Math.PI / 360; Px(s.RadiusM * Math.Cos(a0), s.RadiusM * Math.Sin(a0), side, out int rx, out int ry); if (rx - side * (N + Gap) >= 0 && rx - side * (N + Gap) < N && ry >= 0 && ry < N) Dot(rx, ry, 110, 110, 110, 1); }
                var w = side == 0 ? before : after;
                foreach (var e in w.Gen.Graph.Edges)
                {
                    byte r = 235, g = 235, b = 235; int t = 1;
                    if (e.Class == WhatIf.WorldGen.RoadClass.Motorway) { r = 220; g = 30; b = 30; t = 3; } else if (e.Class == WhatIf.WorldGen.RoadClass.Primary) { r = 255; g = 140; b = 0; t = 2; } else if (e.Class == WhatIf.WorldGen.RoadClass.Secondary) { r = 255; g = 230; b = 60; t = 2; }
                    for (int i = 0; i + 1 < e.Pts.Length; i += 4) { int j = Math.Min(e.Pts.Length - 1, i + 4); Line(e.Pts[i].X, e.Pts[i].Z, e.Pts[j].X, e.Pts[j].Z, side, r, g, b, t); }
                }
                foreach (var en in after.Ents)
                {
                    if (side == 0) Line(en.Point.X, en.Point.Z, 0, 0, 0, 255, 140, 0, 1);
                    Px(en.Point.X, en.Point.Z, side, out int ex, out int ey); Dot(ex, ey, 255, 0, 255, 9);
                }
            }
            Png.Write(path, W, N, img);
        }
    }
}
