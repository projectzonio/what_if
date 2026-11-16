using System;
using System.Collections.Generic;
using System.Linq;
using WhatIf.WorldGen;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Surface;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Anchors.Experiment
{
    /// <summary>Per-class rule facts (row n4).</summary>
    public sealed class ClassFacts
    {
        public double Km, MaxGrade, MinRadius = double.PositiveInfinity, WaterM; public long Beyond; public int Edges;
    }

    /// <summary>The (variant B) road network over the area in global terms: nodes (unit directions), edges (length, class), settlements (attach node or -1), plus the n1 / n4 facts.</summary>
    public sealed class Net
    {
        public readonly List<Vec3> Nodes = new List<Vec3>();
        public readonly List<(int A, int B, double Len, string Cls)> Edges = new List<(int, int, double, string)>();
        public readonly List<(Vec3 Dir, int Node, bool City)> Settlements = new List<(Vec3, int, bool)>();
        public readonly SortedDictionary<string, int> BorderEnds = new SortedDictionary<string, int>();   // n1 per class (stays empty for B: an end is always a settlement)
        public readonly SortedDictionary<string, ClassFacts> Classes = new SortedDictionary<string, ClassFacts>();
        public int SettlementsOnWater, Unroutable, Straight;   // Straight (B): routed corridors that stayed one chord (no fillet)
        public List<string> Notes = new List<string>();
        public List<(List<Vec3> Line, string Cls)> Draw = new List<(List<Vec3>, string)>();   // for the top view

        public ClassFacts Of(string c) { if (!Classes.TryGetValue(c, out var f)) Classes[c] = f = new ClassFacts(); return f; }
        public void End(string c) { BorderEnds.TryGetValue(c, out int n); BorderEnds[c] = n + 1; }

        int[] parent;
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        public int[] Components()
        {
            parent = Enumerable.Range(0, Nodes.Count).ToArray();
            foreach (var e in Edges) { int a = Find(e.A), b = Find(e.B); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }
            return Enumerable.Range(0, Nodes.Count).Select(Find).ToArray();
        }

        /// <summary>n2: settlements in the largest component (a settlement with no attach node is its own component).</summary>
        public (int InLargest, int Total) Largest()
        {
            var comp = Components(); var count = new Dictionary<int, int>();
            foreach (var s in Settlements) if (s.Node >= 0) { count.TryGetValue(comp[s.Node], out int n); count[comp[s.Node]] = n + 1; }
            return (count.Count == 0 ? (Settlements.Count > 0 ? 1 : 0) : count.Values.Max(), Settlements.Count);
        }

        /// <summary>Shortest path length (m) from node a to every node.</summary>
        public double[] Dijkstra(int a)
        {
            if (adj == null) { adj = new List<(int, double)>[Nodes.Count]; for (int i = 0; i < adj.Length; i++) adj[i] = new List<(int, double)>(); foreach (var e in Edges) { adj[e.A].Add((e.B, e.Len)); adj[e.B].Add((e.A, e.Len)); } }
            var d = Enumerable.Repeat(double.PositiveInfinity, Nodes.Count).ToArray(); d[a] = 0;
            var pq = new SortedSet<(double, int)> { (0, a) };
            while (pq.Count > 0)
            {
                var (dist, u) = pq.Min; pq.Remove(pq.Min);
                if (dist > d[u]) continue;
                foreach (var (v, l) in adj[u]) if (dist + l < d[v]) { if (!double.IsInfinity(d[v])) pq.Remove((d[v], v)); d[v] = dist + l; pq.Add((d[v], v)); }
            }
            return d;
        }
        List<(int, double)>[] adj;
    }

    public static class Nets
    {
        const double R = CubeSphere.Radius;
        static bool Water(SurfaceKind k) => k == SurfaceKind.Ocean || k == SurfaceKind.Sea || k == SurfaceKind.Lake;

        /// <summary>Variant B: settlements = nodes, routed corridors = edges. n1 counts corridor ends that are not settlements within 50 m of a region border (an end is always a settlement by construction; unroutable links give no road).</summary>
        public static Net B(AnchorSet a, PlanetSurface surf)
        {
            var net = new Net(); var idx = new Dictionary<ulong, int>();
            foreach (var s in a.Settlements)
            {
                idx[s.Id] = net.Nodes.Count; net.Nodes.Add(s.Dir); net.Settlements.Add((s.Dir, idx[s.Id], s.Class == SettlementClass.City));
                if (Water(surf.Kind(s.Dir))) net.SettlementsOnWater++;
            }
            foreach (var c in a.Corridors)
            {
                string cls = c.Link.RoadClass; var f = net.Of(cls); f.Edges++;
                if (!c.Routed) { net.Unroutable++; net.Notes.Add($"unroutable {cls} {c.Link.A} - {c.Link.B} ({c.Link.GcM / 1000:F1} km): {c.Why}"); continue; }
                net.Edges.Add((idx[c.Link.A.Id], idx[c.Link.B.Id], c.LengthM, cls));
                var line = new List<Vec3>(); for (int i = 0; i < c.X.Length; i++) line.Add(c.Chart.ToDir(c.X[i], c.Z[i]));
                net.Draw.Add((line, cls));
                f.Km += c.LengthM / 1000; f.MinRadius = Math.Min(f.MinRadius, c.MinRadiusM); if (double.IsInfinity(c.MinRadiusM)) net.Straight++;
                f.WaterM += c.OnWater.Count(b => b) * c.Ds;
                foreach (var sg in a.Segments[c.Link.Key]) { f.MaxGrade = Math.Max(f.MaxGrade, sg.MaxGrade); f.Beyond += sg.BeyondCurve; }
            }
            return net;
        }

        /// <summary>n3: up to 200 seeded settlement pairs 5-40 km apart (great circle): reachable share, route / great-circle median and P95.</summary>
        public static (int Pairs, int Reached, double Med, double P95) Pairs(Net n, ulong seed)
        {
            var st = seed; var ratios = new List<double>(); int pairs = 0, reached = 0; var cache = new Dictionary<int, double[]>();
            if (n.Settlements.Count < 2) return (0, 0, double.NaN, double.NaN);
            for (int t = 0; t < 200000 && pairs < 200; t++)
            {
                int i = (int)(SplitMix64.Next(ref st) % (ulong)n.Settlements.Count), j = (int)(SplitMix64.Next(ref st) % (ulong)n.Settlements.Count);
                if (i == j) continue;
                double gc = SettlementField.Gc(n.Settlements[i].Dir, n.Settlements[j].Dir);
                if (gc < 5000 || gc > 40000) continue;
                pairs++;
                int a = n.Settlements[i].Node, b = n.Settlements[j].Node;
                if (a < 0 || b < 0) continue;
                if (!cache.TryGetValue(a, out var d)) cache[a] = d = n.Dijkstra(a);
                if (double.IsInfinity(d[b])) continue;
                reached++; ratios.Add(d[b] / gc);
            }
            ratios.Sort();
            return (pairs, reached, ratios.Count > 0 ? Stats.Q(ratios, 0.5) : double.NaN, ratios.Count > 0 ? Stats.Q(ratios, 0.95) : double.NaN);
        }
    }

    public static class Stats
    {
        public static double Q(List<double> v, double q) { if (v.Count == 0) return double.NaN; var s = v.OrderBy(x => x).ToList(); return s[Math.Max(0, Math.Min(s.Count - 1, (int)Math.Ceiling(q * s.Count) - 1))]; }
    }
}
