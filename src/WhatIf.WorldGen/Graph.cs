using System;
using System.Collections.Generic;

namespace WhatIf.WorldGen
{
    public static class NodeKind
    {
        public const string End = "end", Joint = "joint", Junction = "junction", Interchange = "interchange", Portal = "portal";
    }

    public sealed class GNode
    {
        public int Id;
        public V2 P;
        public double Y;
        public string Kind = NodeKind.Joint;
        public int Layer;
        public bool Boundary;
        public string InterchangeKind;   // "diamond" on the gore nodes of the interchange, else null
    }

    /// <summary>One road piece between two nodes. Pts/H/Curv are samples every ~StepM along the centreline (Pts[0] = node A, last = node B).</summary>
    public sealed class GEdge
    {
        public int Id, A, B;
        public string Class;
        public string Span = "ground";   // ground | bridge | tunnel
        public int Layer;
        public int Lanes;
        public double WidthM;
        public int MaxSpeedKmh;
        public bool Oneway;
        public V2[] Pts;
        public double[] H;               // height of each sample, metres (y up)
        public double[] Curv;            // signed curvature 1/m at each sample (left positive), analytic from the generator
        public double LengthM;
        public double StepM { get { return Pts.Length > 1 ? LengthM / (Pts.Length - 1) : 0; } }
    }

    public sealed class GSpan
    {
        public int Id;
        public string Kind;              // bridge | tunnel
        public string Class;
        public List<int> Edges = new List<int>();
        public int StartNode, EndNode;
        public double LengthM;
        public double ClearanceM;        // bridge: deck above the crossed road (design 5 m)
        public double CoverDepthM;       // tunnel
        public bool Viaduct;             // bridge over ground with no crossed road (RG-118): clearance above the terrain, not above a road
        public int Layer;
    }

    public sealed class RoadGraph
    {
        public readonly List<GNode> Nodes = new List<GNode>();
        public readonly List<GEdge> Edges = new List<GEdge>();
        public readonly List<GSpan> Spans = new List<GSpan>();

        public GNode AddNode(V2 p, double y, string kind, int layer = 0)
        {
            var n = new GNode { Id = Nodes.Count, P = p, Y = y, Kind = kind, Layer = layer };
            Nodes.Add(n);
            return n;
        }

        public GEdge AddEdge(int a, int b, string cls, V2[] pts, double[] h, double[] curv, int layer = 0, string span = "ground")
        {
            double len = 0;
            for (int i = 1; i < pts.Length; i++) len += V2.Dist(pts[i], pts[i - 1]);
            var e = new GEdge
            {
                Id = Edges.Count,
                A = a,
                B = b,
                Class = cls,
                Pts = pts,
                H = h,
                Curv = curv,
                LengthM = len,
                Layer = layer,
                Span = span,
                Lanes = RoadClass.Lanes(cls),
                WidthM = RoadClass.Width(cls),
                MaxSpeedKmh = (int)Math.Round(RoadClass.DesignSpeedMs(cls) * 3.6),
            };
            Edges.Add(e);
            return e;
        }

        public int[] Degrees()
        {
            var d = new int[Nodes.Count];
            foreach (var e in Edges) { d[e.A]++; d[e.B]++; }
            return d;
        }

        /// <summary>RG-142: WidthM is one constant per edge, so a class change at a degree-2 node would be a width step. Every chain of edges joined at degree-2 nodes gets ONE width (class tag and topology stay): the widest
        /// one (a motorway is never narrowed), unless a chain end sits at a junction whose widest leg CLASS width is smaller than that - the terrain plateau of a junction is sized from its leg classes, so a wider
        /// leg would overhang it (seed 3 edge 1171); then the narrowest one. A chain with a bridge or tunnel span always takes the widest (a tube/deck cross-section is designed for the class width: narrowing the 1 km primary tunnel on seed 1 to 6 m produced a sweep tunnel_edge). Returns the number of edges changed.</summary>
        public int MatchWidthsAtDegree2Nodes()
        {
            var inc = Incident();
            var parent = new int[Edges.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            Func<int, int> find = null;
            find = x => parent[x] == x ? x : (parent[x] = find(parent[x]));
            for (int n = 0; n < inc.Length; n++)
                if (inc[n].Count == 2) parent[find(inc[n][0])] = find(inc[n][1]);
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < Edges.Count; i++)
            {
                List<int> l;
                int r = find(i);
                if (!groups.TryGetValue(r, out l)) groups[r] = l = new List<int>();
                l.Add(i);
            }
            int changed = 0;
            foreach (var l in groups.Values)
            {
                double wMax = 0, wMin = double.MaxValue;
                foreach (int ei in l) { wMax = Math.Max(wMax, Edges[ei].WidthM); wMin = Math.Min(wMin, Edges[ei].WidthM); }
                if (wMax == wMin) continue;
                double w = wMax;
                bool spanInChain = false;
                foreach (int ei in l) if (Edges[ei].Span != "ground") spanInChain = true;
                if (wMax < 10.0 && !spanInChain)
                    foreach (int ei in l)
                        foreach (int n in new[] { Edges[ei].A, Edges[ei].B })
                        {
                            if (inc[n].Count < 3) continue;
                            double legMax = 0;
                            foreach (int o in inc[n]) legMax = Math.Max(legMax, RoadClass.Width(Edges[o].Class));
                            if (legMax < wMax) w = wMin;
                        }
                foreach (int ei in l) if (Edges[ei].WidthM != w) { Edges[ei].WidthM = w; changed++; }
            }
            return changed;
        }

        public List<int>[] Incident()
        {
            var inc = new List<int>[Nodes.Count];
            for (int i = 0; i < inc.Length; i++) inc[i] = new List<int>();
            foreach (var e in Edges) { inc[e.A].Add(e.Id); if (e.B != e.A) inc[e.B].Add(e.Id); }
            return inc;
        }

        /// <summary>Direction (heading, radians) in which edge e leaves node n, measured on a chord of about lookM along the centreline.</summary>
        public static double LegHeading(GEdge e, int node, double lookM = 15.0)
        {
            int k = Math.Max(1, (int)Math.Ceiling(lookM / Math.Max(e.StepM, 1e-6)));
            k = Math.Min(k, e.Pts.Length - 1);
            if (e.A == node) return (e.Pts[k] - e.Pts[0]).Heading;
            return (e.Pts[e.Pts.Length - 1 - k] - e.Pts[e.Pts.Length - 1]).Heading;
        }

        /// <summary>Smallest angle (degrees) between two neighbouring legs of node n; 180 when fewer than two legs.</summary>
        public double MinLegAngleDeg(int node, List<int> incident)
        {
            var hs = new List<double>();
            foreach (int ei in incident)
            {
                var e = Edges[ei];
                if (e.A == node) hs.Add(LegHeading(e, node));
                if (e.B == node) hs.Add(LegHeading(e, node));
            }
            if (hs.Count < 2) return 180.0;
            hs.Sort();
            double min = 360.0;
            for (int i = 0; i < hs.Count; i++)
            {
                double next = i + 1 < hs.Count ? hs[i + 1] : hs[0] + 2 * Math.PI;
                min = Math.Min(min, (next - hs[i]) * 180.0 / Math.PI);
            }
            return min;
        }
    }

    /// <summary>Spatial hash of line segments (cell size metres) for crossing tests.</summary>
    public sealed class SegIndex
    {
        readonly double _cell;
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        public readonly List<V2> A = new List<V2>(), B = new List<V2>();
        public readonly List<int> Owner = new List<int>();

        public SegIndex(double cell) { _cell = cell; }

        static long Key(int cx, int cz) { return ((long)cx << 32) ^ (uint)cz; }

        public int Add(V2 a, V2 b, int owner)
        {
            int id = A.Count;
            A.Add(a); B.Add(b); Owner.Add(owner);
            int x0 = (int)Math.Floor(Math.Min(a.X, b.X) / _cell), x1 = (int)Math.Floor(Math.Max(a.X, b.X) / _cell);
            int z0 = (int)Math.Floor(Math.Min(a.Z, b.Z) / _cell), z1 = (int)Math.Floor(Math.Max(a.Z, b.Z) / _cell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    List<int> l;
                    long k = Key(x, z);
                    if (!_cells.TryGetValue(k, out l)) { l = new List<int>(); _cells[k] = l; }
                    l.Add(id);
                }
            return id;
        }

        /// <summary>Ids of stored segments whose cells overlap the box of segment a-b (may contain duplicates; callers de-duplicate).</summary>
        public void Query(V2 a, V2 b, List<int> into)
        {
            into.Clear();
            int x0 = (int)Math.Floor(Math.Min(a.X, b.X) / _cell), x1 = (int)Math.Floor(Math.Max(a.X, b.X) / _cell);
            int z0 = (int)Math.Floor(Math.Min(a.Z, b.Z) / _cell), z1 = (int)Math.Floor(Math.Max(a.Z, b.Z) / _cell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    List<int> l;
                    if (_cells.TryGetValue(Key(x, z), out l)) into.AddRange(l);
                }
            if (into.Count > 1) { into.Sort(); int w = 1; for (int i = 1; i < into.Count; i++) if (into[i] != into[w - 1]) into[w++] = into[i]; into.RemoveRange(w, into.Count - w); }
        }
    }
}
