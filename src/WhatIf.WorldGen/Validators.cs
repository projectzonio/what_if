using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhatIf.WorldGen
{
    public sealed class Finding
    {
        public string Check, Message;
        public Finding(string check, string message) { Check = check; Message = message; }
        public override string ToString() { return Check + ": " + Message; }
    }

    /// <summary>Plan section 3 points 1-3 with the RG-109 thresholds. Each validator returns findings (empty = pass); All() runs them in order.</summary>
    public static class Validators
    {
        static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        /// <summary>All validators including the terrain checks (RG-118). With a flat terrain model it equals All(g, rules).</summary>
        public static List<Finding> All(RoadGraph g, WorldRules rules, TerrainModel terrain)
        {
            var l = All(g, rules);
            if (terrain != null && terrain.Enabled) { l.AddRange(Terrain(g, rules, terrain)); l.AddRange(VCurve(g, rules)); }
            return l;
        }

        public const double MaxCutM = 12.0, MaxFillM = 6.0, MinTunnelCoverM = 7.5, MinViaductClearanceM = 2.0;

        /// <summary>Terrain checks (RG-118): ground samples cut at most 12 m and fill at most 6 m, tunnel samples at least 7.5 m below the natural surface,
        /// no tunnel or viaduct on a class that may not have one (local, track).</summary>
        public static List<Finding> Terrain(RoadGraph g, WorldRules rules, TerrainModel terrain)
        {
            var l = new List<Finding>();
            foreach (var e in g.Edges)
            {
                double worstCut = 0, worstFill = 0, minCover = double.MaxValue;
                for (int i = 0; i < e.Pts.Length; i++)
                {
                    double d = terrain.Natural(e.Pts[i].X, e.Pts[i].Z) - e.H[i];   // positive = road below the natural surface
                    if (e.Span == "ground") { worstCut = Math.Max(worstCut, d); worstFill = Math.Max(worstFill, -d); }
                    else if (e.Span == "tunnel") minCover = Math.Min(minCover, d);
                }
                if (worstCut > MaxCutM + 1e-6) l.Add(new Finding("cut-depth", "edge " + e.Id + " (" + e.Class + ") cut " + F(worstCut) + " m > " + F(MaxCutM) + " m"));
                if (worstFill > MaxFillM + 1e-6) l.Add(new Finding("fill-height", "edge " + e.Id + " (" + e.Class + ") fill " + F(worstFill) + " m > " + F(MaxFillM) + " m"));
                if (e.Span == "tunnel" && minCover < MinTunnelCoverM - 1e-6) l.Add(new Finding("tunnel-cover", "edge " + e.Id + " (" + e.Class + ") surface only " + F(minCover) + " m above the road < " + F(MinTunnelCoverM) + " m"));
                if (e.Span != "ground" && (e.Class == RoadClass.Local || e.Class == RoadClass.Track))
                    l.Add(new Finding("span-on-local", "edge " + e.Id + " (" + e.Class + ") is a " + e.Span + ": local streets never get a tunnel or a viaduct"));
            }
            return l;
        }

        public const double VCurveTolerance = 0.98, Gravity = 9.81;

        /// <summary>Vertical radius from three consecutive stations (heights over horizontal distance, as the profile solver sees them).
        /// Returns false when the middle station is straight; crest = true when the grade falls (convex up).</summary>
        public static bool VRadius(V2 p0, double h0, V2 p1, double h1, V2 p2, double h2, out double radius, out bool crest)
        {
            radius = double.MaxValue; crest = false;
            double a = V2.Dist(p0, p1), b = V2.Dist(p1, p2);
            if (a < 1e-6 || b < 1e-6) return false;
            double k = ((h2 - h1) / b - (h1 - h0) / a) / ((a + b) * 0.5);
            if (Math.Abs(k) < 1e-9) return false;
            radius = 1.0 / Math.Abs(k); crest = k < 0;
            return true;
        }

        /// <summary>Smallest crest and sag radius over the interior stations of an edge (double.MaxValue when none is curved).</summary>
        public static void VRadii(GEdge e, out double crest, out double sag)
        {
            crest = sag = double.MaxValue;
            for (int i = 1; i + 1 < e.Pts.Length; i++)
            {
                double r; bool c;
                if (!VRadius(e.Pts[i - 1], e.H[i - 1], e.Pts[i], e.H[i], e.Pts[i + 1], e.H[i + 1], out r, out c)) continue;
                if (c) crest = Math.Min(crest, r); else sag = Math.Min(sag, r);
            }
        }

        /// <summary>Vertical curves (RG-123): the radius of every interior station, and across every degree-2 span boundary (tunnel / ground / viaduct joint), at least the class minimum for a crest or a sag
        /// (2 % tolerance). A crest below R lifts a car off the road above sqrt(g R); the finding names the edge, the position and that speed.</summary>
        public static List<Finding> VCurve(RoadGraph g, WorldRules rules)
        {
            var l = new List<Finding>();
            Action<GEdge, string, int, double, bool, double> report = (e, cls, i, r, crest, need) =>
                l.Add(new Finding("vcurve", "edge " + e.Id + " (" + cls + ") " + (crest ? "crest" : "sag") + " radius " + F(r) + " m < minimum " + F(need) + " m at " + F(e.Pts[i].X) + "," + F(e.Pts[i].Z)
                    + " (height " + F(e.H[i]) + " m), lift-off " + F(Math.Sqrt(Gravity * r) * 3.6) + " km/h"));
            foreach (var e in g.Edges)
                for (int i = 1; i + 1 < e.Pts.Length; i++)
                {
                    double r; bool crest;
                    if (!VRadius(e.Pts[i - 1], e.H[i - 1], e.Pts[i], e.H[i], e.Pts[i + 1], e.H[i + 1], out r, out crest)) continue;
                    double need = WorldRules.VRadiusMin(e.Class, crest);
                    if (r < need * VCurveTolerance) report(e, e.Class, i, r, crest, need);
                }
            var inc = g.Incident();
            foreach (var n in g.Nodes)
            {
                if (inc[n.Id].Count != 2) continue;
                var e0 = g.Edges[inc[n.Id][0]]; var e1 = g.Edges[inc[n.Id][1]];
                if (e0.Pts.Length < 2 || e1.Pts.Length < 2 || e0.Id == e1.Id || e0.Span == e1.Span) continue;   // only a tunnel / ground / viaduct boundary is surely inside one road; two road ends that meet at a joint are profiled separately
                int at0 = e0.B == n.Id ? e0.Pts.Length - 1 : 0, prev0 = e0.B == n.Id ? at0 - 1 : 1;
                int at1 = e1.A == n.Id ? 0 : e1.Pts.Length - 1, next1 = e1.A == n.Id ? 1 : at1 - 1;
                double r; bool crest;
                if (!VRadius(e0.Pts[prev0], e0.H[prev0], e0.Pts[at0], e0.H[at0], e1.Pts[next1], e1.H[next1], out r, out crest)) continue;
                double need = Math.Min(WorldRules.VRadiusMin(e0.Class, crest), WorldRules.VRadiusMin(e1.Class, crest));
                if (r < need * VCurveTolerance) report(e0, e0.Class, at0, r, crest, need);
            }
            return l;
        }

        public static List<Finding> All(RoadGraph g, WorldRules rules)
        {
            var l = new List<Finding>();
            l.AddRange(Radius(g, rules));
            l.AddRange(Grade(g, rules));
            l.AddRange(CurvatureJoins(g, rules));
            l.AddRange(Connected(g));
            l.AddRange(LegAngles(g, rules));
            l.AddRange(Crossings(g, rules));
            l.AddRange(Spacing(g, rules));
            l.AddRange(Spans(g, rules));
            return l;
        }

        /// <summary>|curvature| of every sample at most 1 / class minimum radius (0.5 % tolerance).</summary>
        public static List<Finding> Radius(RoadGraph g, WorldRules rules)
        {
            var l = new List<Finding>();
            foreach (var e in g.Edges)
            {
                double rmin = rules.Of(e.Class).MinRadiusM, worst = 0;
                foreach (double k in e.Curv) worst = Math.Max(worst, Math.Abs(k));
                if (worst > 1.005 / rmin) l.Add(new Finding("radius", "edge " + e.Id + " (" + e.Class + ") radius " + F(1 / worst) + " m < minimum " + F(rmin) + " m"));
            }
            return l;
        }

        /// <summary>Largest |dh/ds| between consecutive samples at most the class maximum grade.</summary>
        public static List<Finding> Grade(RoadGraph g, WorldRules rules)
        {
            var l = new List<Finding>();
            foreach (var e in g.Edges)
            {
                double max = rules.Of(e.Class).MaxGradePct, worst = 0;
                for (int i = 1; i < e.Pts.Length; i++)
                {
                    double ds = V2.Dist(e.Pts[i], e.Pts[i - 1]);
                    if (ds > 1e-9) worst = Math.Max(worst, Math.Abs(e.H[i] - e.H[i - 1]) / ds * 100.0);
                }
                if (worst > max + 0.01) l.Add(new Finding("grade", "edge " + e.Id + " (" + e.Class + ") grade " + F(worst) + " % > maximum " + F(max) + " %"));
            }
            return l;
        }

        /// <summary>At a degree-2 node the curvature of the two edges must agree (continuous centreline): jump at most rules.CurvatureJumpMax.</summary>
        public static List<Finding> CurvatureJoins(RoadGraph g, WorldRules rules)
        {
            var l = new List<Finding>();
            var inc = g.Incident();
            foreach (var n in g.Nodes)
            {
                if (inc[n.Id].Count != 2) continue;
                var e0 = g.Edges[inc[n.Id][0]]; var e1 = g.Edges[inc[n.Id][1]];
                double k0 = e0.B == n.Id ? e0.Curv[e0.Curv.Length - 1] : -e0.Curv[0];   // travelling e0 -> node
                double k1 = e1.A == n.Id ? e1.Curv[0] : -e1.Curv[e1.Curv.Length - 1];   // travelling node -> e1
                if (Math.Abs(k0 - k1) > rules.CurvatureJumpMax)
                    l.Add(new Finding("curvature-join", "node " + n.Id + ": curvature jump " + F(Math.Abs(k0 - k1)) + " 1/m between edges " + e0.Id + " and " + e1.Id));
            }
            return l;
        }

        public static List<Finding> Connected(RoadGraph g)
        {
            var l = new List<Finding>();
            var parent = new int[g.Nodes.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            foreach (var e in g.Edges) parent[find(e.A)] = find(e.B);
            var comps = new Dictionary<int, int>();
            foreach (var n in g.Nodes) { int r = find(n.Id); comps[r] = comps.ContainsKey(r) ? comps[r] + 1 : 1; }
            if (comps.Count > 1) l.Add(new Finding("connected", comps.Count + " connected components (expected 1)"));
            return l;
        }

        /// <summary>Generated T/X junctions: smallest leg angle at least 70 degrees. Interchange (fork) nodes: at least the fork minimum.</summary>
        public static List<Finding> LegAngles(RoadGraph g, WorldRules rules)
        {
            var l = new List<Finding>();
            var inc = g.Incident();
            foreach (var n in g.Nodes)
            {
                if (inc[n.Id].Count < 3) continue;
                double a = g.MinLegAngleDeg(n.Id, inc[n.Id]);
                double need = n.Kind == NodeKind.Interchange ? rules.MinFork(RoadClass.Motorway) : rules.LegAngleMinDeg;
                if (n.Kind != NodeKind.Junction && n.Kind != NodeKind.Interchange) continue;
                if (a < need - 0.5) l.Add(new Finding("leg-angle", "node " + n.Id + " (" + n.Kind + ") min leg angle " + F(a) + "° < " + F(need) + "°"));
            }
            return l;
        }

        /// <summary>Two edges that cross on the same layer need a shared node at the crossing; edges on different layers need the deck at least ClearanceM above.</summary>
        public static List<Finding> Crossings(RoadGraph g, WorldRules rules)
        {
            var l = new List<Finding>();
            var idx = new SegIndex(50);
            var segEdge = new List<int>(); var segI = new List<int>();
            var hits = new List<int>();
            var reported = new HashSet<long>();
            foreach (var e in g.Edges)
            {
                for (int i = 0; i < e.Pts.Length - 1; i++)
                {
                    idx.Query(e.Pts[i], e.Pts[i + 1], hits);
                    foreach (int h in hits)
                    {
                        double t, u;
                        if (!Geo.SegCross(e.Pts[i], e.Pts[i + 1], idx.A[h], idx.B[h], out t, out u, true)) continue;
                        var f = g.Edges[segEdge[h]];
                        if (f.Id == e.Id) continue;
                        V2 xp = e.Pts[i] + (e.Pts[i + 1] - e.Pts[i]) * t;
                        if (SharedNodeAt(g, e, f, xp)) continue;
                        int j = segI[h];
                        long key = ((long)Math.Min(e.Id, f.Id) << 32) | (uint)Math.Max(e.Id, f.Id);
                        if (e.Layer == f.Layer)
                        {
                            if (reported.Add(key)) l.Add(new Finding("crossing", "edges " + e.Id + " (" + e.Class + ") and " + f.Id + " (" + f.Class + ") cross on layer " + e.Layer + " without a junction node at " + F(e.Pts[i].X) + "," + F(e.Pts[i].Z)));
                        }
                        else
                        {
                            double ye = e.H[i] + (e.H[i + 1] - e.H[i]) * t, yf = f.H[j] + (f.H[j + 1] - f.H[j]) * u;
                            double clear = (e.Layer > f.Layer ? ye - yf : yf - ye);
                            if (clear < rules.ClearanceM - 0.01 && reported.Add(key))
                                l.Add(new Finding("clearance", "edges " + e.Id + " and " + f.Id + " cross with clearance " + F(clear) + " m < " + F(rules.ClearanceM) + " m"));
                        }
                    }
                    idx.Add(e.Pts[i], e.Pts[i + 1], e.Id);
                    segEdge.Add(e.Id); segI.Add(i);
                }
            }
            return l;
        }

        static bool SharedNodeAt(RoadGraph g, GEdge e, GEdge f, V2 p)
        {
            foreach (int n in new[] { e.A, e.B })
                if ((n == f.A || n == f.B) && V2.Dist(g.Nodes[n].P, p) < 0.05) return true;
            return false;
        }

        /// <summary>Along a chain of one class (degree-2 nodes contracted) the length between two junctions (degree 3 or more) is at least the class floor.</summary>
        public static List<Finding> Spacing(RoadGraph g, WorldRules rules)
        {
            var l = new List<Finding>();
            var inc = g.Incident();
            var seen = new bool[g.Edges.Count];
            foreach (var e0 in g.Edges)
            {
                if (seen[e0.Id]) continue;
                string cls = e0.Class;
                double len = 0;
                int endA = e0.A, endB = e0.B;
                seen[e0.Id] = true; len += e0.LengthM;
                foreach (int dir in new[] { 0, 1 })
                {
                    int node = dir == 0 ? endA : endB; int prev = e0.Id;
                    while (inc[node].Count == 2)
                    {
                        int nxtId = inc[node][0] == prev ? inc[node][1] : inc[node][0];
                        var nx = g.Edges[nxtId];
                        if (nx.Class != cls || seen[nxtId]) break;
                        seen[nxtId] = true; len += nx.LengthM;
                        node = nx.A == node ? nx.B : nx.A; prev = nxtId;
                    }
                    if (dir == 0) endA = node; else endB = node;
                }
                if (inc[endA].Count >= 3 && inc[endB].Count >= 3 && endA != endB)
                {
                    double fl = rules.SpacingFloor(cls);
                    if (len < fl) l.Add(new Finding("spacing", "class " + cls + " chain between junctions " + endA + " and " + endB + " is " + F(len) + " m < " + F(fl) + " m"));
                }
            }
            return l;
        }

        /// <summary>Bridges at least the class minimum (60 m on motorway and link) with clearance at least 5 m; motorway tunnels at least 150 m.</summary>
        public static List<Finding> Spans(RoadGraph g, WorldRules rules)
        {
            var l = new List<Finding>();
            foreach (var s in g.Spans)
            {
                if (s.Kind == "bridge")
                {
                    double min = rules.BridgeMin(s.Class);
                    if (s.LengthM < min) l.Add(new Finding("bridge-length", "span " + s.Id + " (" + s.Class + ") " + F(s.LengthM) + " m < " + F(min) + " m"));
                    double minClear = s.Viaduct ? MinViaductClearanceM : rules.ClearanceM;   // a viaduct over ground (RG-118) needs 2 m above the terrain, a bridge over a road 5 m
                    if (s.ClearanceM < minClear - 1e-9) l.Add(new Finding("bridge-clearance", "span " + s.Id + " clearance " + F(s.ClearanceM) + " m < " + F(minClear) + " m"));
                }
                else if (s.Kind == "tunnel" && s.LengthM < rules.TunnelMin(s.Class))
                    l.Add(new Finding("tunnel-length", "span " + s.Id + " (" + s.Class + ") " + F(s.LengthM) + " m < " + F(rules.TunnelMin(s.Class)) + " m"));
            }
            return l;
        }
    }
}
