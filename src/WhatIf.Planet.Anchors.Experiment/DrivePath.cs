using System;
using System.Collections.Generic;
using System.Linq;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Surface;

namespace WhatIf.Planet.Anchors.Experiment
{
    /// <summary>A path along corridor centrelines (RG-178 s6): constant speed along a chain of corridors, directions interpolated between 10 m stations.</summary>
    public sealed class CorridorPath
    {
        public readonly List<Vec3> Pts = new List<Vec3>(); readonly List<double> cum = new List<double>(); public string Description = ""; public int RegionBorders, FaceEdges; public double LengthM;
        public Vec3 DirAtS(double s)
        {
            s = Math.Max(0, Math.Min(LengthM, s)); int lo = 0, hi = cum.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (cum[m] <= s) lo = m; else hi = m; }
            double span = cum[hi] - cum[lo], t = span > 0 ? (s - cum[lo]) / span : 0;
            return (Pts[lo] * (1 - t) + Pts[hi] * t).Normalized();
        }
        void Finish()
        {
            cum.Clear(); double c = 0; cum.Add(0);
            for (int i = 1; i < Pts.Count; i++) { c += SettlementField.Gc(Pts[i - 1], Pts[i]); cum.Add(c); }
            LengthM = c; RegionBorders = 0; FaceEdges = 0; RegionId prev = default; bool have = false;
            for (double s = 0; s <= LengthM; s += 10)
            {
                var r = CubeSphere.RegionOf(DirAtS(s), SectorId.RegionLevel);
                if (have && !r.Equals(prev)) { RegionBorders++; if (r.Face != prev.Face) FaceEdges++; }
                prev = r; have = true;
            }
        }

        /// <summary>RG-184 B5: the road path from one city centre to another: inside each settlement the shortest street path of its WorldGen world (centre -> entrance, entrance ->
        /// entrance), between them the corridor stations. Among all city pairs of the set joined by routed corridors (shortest chain by length) it keeps the pair that crosses >= minBorders
        /// old region borders, preferring a cube edge, then the shorter path. planner must keep its graphs (AnchorWorld.KeepGraphs).</summary>
        public static CorridorPath CityToCity(AnchorWorld planner, AnchorSet set, int minBorders)
        {
            var routed = set.Corridors.Where(c => c.Routed).ToList(); var byS = new Dictionary<ulong, List<Corridor>>();
            foreach (var c in routed) foreach (var id in new[] { c.Link.A.Id, c.Link.B.Id }) { if (!byS.TryGetValue(id, out var l)) byS[id] = l = new List<Corridor>(); l.Add(c); }
            var cities = set.Settlements.Where(s => s.Class == SettlementClass.City).OrderBy(s => s.Id).ToList(); CorridorPath best = null;
            for (int i = 0; i < cities.Count; i++)
                for (int j = i + 1; j < cities.Count; j++)
                {
                    var chain = Chain(byS, cities[i], cities[j]); if (chain == null) continue;
                    var p = Build(planner, chain, cities[i]); if (p == null) continue;
                    int Score(CorridorPath x) => (x.RegionBorders >= minBorders ? 2 : 0) + (x.FaceEdges > 0 ? 1 : 0);
                    if (best == null || Score(p) > Score(best) || (Score(p) == Score(best) && p.LengthM < best.LengthM)) best = p;
                }
            return best;
        }

        /// <summary>Shortest chain of corridors (by length) from settlement a to b; ties by link key (Dijkstra, deterministic).</summary>
        static List<Corridor> Chain(Dictionary<ulong, List<Corridor>> byS, Settlement a, Settlement b)
        {
            var dist = new Dictionary<ulong, double> { [a.Id] = 0 }; var via = new Dictionary<ulong, Corridor>(); var done = new HashSet<ulong>();
            var q = new SortedSet<(double d, ulong id)> { (0, a.Id) };
            while (q.Count > 0)
            {
                var (d, id) = q.Min; q.Remove(q.Min); if (!done.Add(id)) continue; if (id == b.Id) break;
                if (!byS.TryGetValue(id, out var cs)) continue;
                foreach (var c in cs.OrderBy(x => x.Link.Key))
                {
                    ulong o = c.Link.A.Id == id ? c.Link.B.Id : c.Link.A.Id; double nd = d + c.LengthM;
                    if (!dist.TryGetValue(o, out double od) || nd < od) { dist[o] = nd; via[o] = c; q.Add((nd, o)); }
                }
            }
            if (!via.ContainsKey(b.Id)) return null;
            var res = new List<Corridor>(); for (ulong id = b.Id; id != a.Id;) { var c = via[id]; res.Add(c); id = c.Link.A.Id == id ? c.Link.B.Id : c.Link.A.Id; }
            res.Reverse(); return res;
        }

        static Vec3 CorEnd(Corridor c, bool atA) { int i = atA ? 0 : c.X.Length - 1; return c.Chart.ToDir(c.X[i], c.Z[i]); }

        /// <summary>Street path inside a settlement world between two plane points (nearest graph nodes), shortest by edge length; directions in the settlement chart. null = no world / not connected.</summary>
        static List<Vec3> Streets(SettlementWorld w, double x0, double z0, double x1, double z1, out double lenM)
        {
            lenM = 0; if (w.Gen == null) return null; var g = w.Gen.Graph;
            int Near(double x, double z) { int bi = -1; double bd = double.MaxValue; foreach (var n in g.Nodes) { double d = (n.P.X - x) * (n.P.X - x) + (n.P.Z - z) * (n.P.Z - z); if (d < bd) { bd = d; bi = n.Id; } } return bi; }
            int s = Near(x0, z0), t = Near(x1, z1); if (s < 0 || t < 0) return null;
            var adj = new List<WhatIf.WorldGen.GEdge>[g.Nodes.Count]; foreach (var e in g.Edges) { (adj[e.A] ?? (adj[e.A] = new List<WhatIf.WorldGen.GEdge>())).Add(e); (adj[e.B] ?? (adj[e.B] = new List<WhatIf.WorldGen.GEdge>())).Add(e); }
            var dist = new double[g.Nodes.Count]; var via = new WhatIf.WorldGen.GEdge[g.Nodes.Count]; for (int i = 0; i < dist.Length; i++) dist[i] = double.PositiveInfinity;
            var q = new SortedSet<(double, int)> { (0, s) }; dist[s] = 0;
            while (q.Count > 0)
            {
                var (d, u) = q.Min; q.Remove(q.Min); if (d > dist[u]) continue; if (u == t) break;
                if (adj[u] == null) continue;
                foreach (var e in adj[u]) { int v = e.A == u ? e.B : e.A; double nd = d + e.LengthM; if (nd < dist[v]) { dist[v] = nd; via[v] = e; q.Add((nd, v)); } }
            }
            if (double.IsInfinity(dist[t])) return null;
            var pts = new List<Vec3>(); var rev = new List<(WhatIf.WorldGen.GEdge e, bool fwd)>();
            for (int v = t; v != s;) { var e = via[v]; int u = e.A == v ? e.B : e.A; rev.Add((e, e.A == u)); v = u; }
            rev.Reverse(); lenM = dist[t];
            foreach (var (e, fwd) in rev) for (int k = 0; k < e.Pts.Length; k++) { var p = e.Pts[fwd ? k : e.Pts.Length - 1 - k]; pts.Add(w.Chart.ToDir(p.X, p.Z)); }
            return pts;
        }

        static CorridorPath Build(AnchorWorld planner, List<Corridor> chain, Settlement start)
        {
            var p = new CorridorPath(); var parts = new List<string>(); var cur = start; Vec3? from = null;   // from = null: the settlement centre
            for (int k = 0; k <= chain.Count; k++)
            {
                var w = planner.World(cur);
                double x0 = 0, z0 = 0; if (from.HasValue) w.Chart.ToLocal(from.Value, out x0, out z0);
                Corridor c = k < chain.Count ? chain[k] : null; bool fwd = c != null && c.Link.A.Id == cur.Id;
                double x1 = 0, z1 = 0; if (c != null) w.Chart.ToLocal(CorEnd(c, fwd), out x1, out z1);
                var st = Streets(w, x0, z0, x1, z1, out double len); if (st == null) return null;
                p.Pts.AddRange(st); parts.Add($"streets {cur} {len / 1000:F1} km");
                if (c == null) break;
                for (int i = 0; i < c.X.Length; i++) { int q = fwd ? i : c.X.Length - 1 - i; p.Pts.Add(c.Chart.ToDir(c.X[q], c.Z[q])); }
                parts.Add($"{c.Link.RoadClass} {c.LengthM / 1000:F1} km"); from = CorEnd(c, !fwd); cur = fwd ? c.Link.B : c.Link.A;
            }
            p.Description = string.Join(" > ", parts); p.Finish();
            return p;
        }

        /// <summary>The old region borders the path crosses: path distance of each crossing (bisected to 0.5 m) and the two regions.</summary>
        public List<(double S, RegionId From, RegionId To)> BorderCrossings()
        {
            var res = new List<(double, RegionId, RegionId)>(); var prev = CubeSphere.RegionOf(DirAtS(0), SectorId.RegionLevel); double ps = 0;
            for (double s = 10; s <= LengthM + 10; s += 10)
            {
                double sc = Math.Min(s, LengthM); var r = CubeSphere.RegionOf(DirAtS(sc), SectorId.RegionLevel);
                if (!r.Equals(prev))
                {
                    double lo = ps, hi = sc; while (hi - lo > 0.5) { double m = 0.5 * (lo + hi); if (CubeSphere.RegionOf(DirAtS(m), SectorId.RegionLevel).Equals(prev)) lo = m; else hi = m; }
                    res.Add((0.5 * (lo + hi), prev, r));
                }
                prev = r; ps = sc; if (sc >= LengthM) break;
            }
            return res;
        }

        /// <summary>The chain of routed corridors of an anchor set that crosses the most old region borders within maxM: start at every routed corridor (both directions), continue at the end settlement with the unused link of smallest turning angle.</summary>
        public static CorridorPath Best(AnchorSet set, double maxM)
        {
            var routed = set.Corridors.Where(c => c.Routed).ToList(); CorridorPath best = null;
            foreach (var start in routed.OrderByDescending(c => c.LengthM).Take(40))
                for (int dirn = 0; dirn < 2; dirn++)
                {
                    var used = new HashSet<ulong> { start.Link.Key }; var p = new CorridorPath(); var chain = new List<string>();
                    void Add(Corridor c, bool fwd) { for (int k = 0; k < c.X.Length; k++) { int i = fwd ? k : c.X.Length - 1 - k; p.Pts.Add(c.Chart.ToDir(c.X[i], c.Z[i])); } chain.Add($"{c.Link.RoadClass} {c.Link.A.Cell}-{c.Link.B.Cell} {(c.LengthM / 1000):F1} km"); }
                    Add(start, dirn == 0); double len = start.LengthM; var endS = dirn == 0 ? start.Link.B : start.Link.A;
                    while (len < maxM)
                    {
                        var prev = p.Pts[p.Pts.Count - 2]; var last = p.Pts[p.Pts.Count - 1]; Corridor bestC = null; bool bf = true; double bestTurn = double.MaxValue;
                        foreach (var c in routed)
                        {
                            if (used.Contains(c.Link.Key)) continue;
                            bool fwd; if (c.Link.A.Id == endS.Id) fwd = true; else if (c.Link.B.Id == endS.Id) fwd = false; else continue;
                            var nxt = c.Chart.ToDir(c.X[fwd ? 3 : c.X.Length - 4], c.Z[fwd ? 3 : c.Z.Length - 4]);
                            double turn = Math.Abs(Vec3.Angle(last - prev, nxt - last));
                            if (turn < bestTurn) { bestTurn = turn; bestC = c; bf = fwd; }
                        }
                        if (bestC == null) break;
                        used.Add(bestC.Link.Key); Add(bestC, bf); len += bestC.LengthM; endS = bf ? bestC.Link.B : bestC.Link.A;
                    }
                    p.Description = string.Join(" > ", chain); p.Finish();
                    if (best == null || p.RegionBorders > best.RegionBorders || (p.RegionBorders == best.RegionBorders && p.LengthM > best.LengthM)) best = p;
                }
            return best;
        }
    }
}
