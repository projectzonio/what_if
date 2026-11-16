using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace WhatIf.WorldGen
{
    /// <summary>Read-only view of a placed settlement (RG-151): used by the Scatter stage. Does not enter the road dataset or its hash.</summary>
    public sealed class SettlementInfo
    {
        public string Name;
        public V2 C;
        public double Radius;
        public bool IsCity;
    }

    public sealed class GenResult
    {
        public RoadGraph Graph;
        public readonly List<string> Log = new List<string>();
        public double Seconds;
        public int Unconnected;
        /// <summary>Natural and carved terrain (RG-118). Never null; with a flat spec every height is 0 and there are no holes.</summary>
        public TerrainModel Terrain;
        /// <summary>The city and the villages, in placement order (RG-151). Read-only data, not exported.</summary>
        public readonly List<SettlementInfo> Settlements = new List<SettlementInfo>();
    }

    /// <summary>Road graph generator on flat terrain (RG-110, W3). Stages: settlements, motorway corridor, local street sets, diamond interchange
    /// with its primary spine, primary MST + loops, secondary roads, then assembly (bridges, vertical profile, nodes, edges).
    /// One RNG stream per stage (and per routing attempt), so a change in one stage never reshuffles the others.</summary>
    public sealed partial class Generator
    {
        internal sealed class Cut { public double S; public int Node; }

        internal sealed class Road
        {
            public int Id;
            public string Class, Tag;
            public Centreline C;
            public List<Cut> Cuts = new List<Cut>();
            public int StartNode, EndNode;
            public bool NoCross;
            public double ZoneA = 1, ZoneB = 0; // protected station range (crossing roads rejected), empty when ZoneA > ZoneB
        }

        internal sealed class Settlement
        {
            public string Name;
            public V2 C;
            public double Radius;
            public bool IsCity;
            public double P1, P2, A1, A2;   // seeded radius noise (street growth)
            public List<Road> Streets = new List<Road>();
        }

        struct Crossing { public double SNew, SR, Angle; public Road R; public V2 P; }

        sealed class Attach { public Road R; public double S; public bool IsEnd; public V2 Q, N; public double Score; public int Node = -1; }

        const int Shift = 20;
        const int RouteAttempts = 160;
        const double RouteLegMarginDeg = 0.3;   // RG-118: tolerance on top of the chord angle (seed 9 had 69.5 deg on the chords where the dense polyline said 70+)
        // interchange dimensions (design values, metres)
        const double GoreD = 380, RampLead = 110, RampEBefore = 90, RampEAfter = 140;
        const double RampLen = 200, DeckHeight = 5.0;

        readonly WorldSpec _spec;
        readonly WorldRules _rules;
        readonly RoadGraph _g = new RoadGraph();
        readonly List<Road> _roads = new List<Road>();
        readonly SegIndex _idx = new SegIndex(100);
        readonly List<Settlement> _set = new List<Settlement>();
        readonly HashSet<int> _usedEnds = new HashSet<int>();
        readonly GenResult _res = new GenResult();
        Road _motorway;
        double _sP;                       // motorway station of the interchange
        V2 _pInter, _vNear;               // interchange centre, unit vector from it toward the city
        readonly List<double> _mxCross = new List<double>();
        List<KeyValuePair<double, double>> _bridges = new List<KeyValuePair<double, double>>();

        Func<double, double, bool> _isWater;   // RG-169: optional water mask (x, z) -> true on water; null = no mask (bit-identical to before)
        public const double WaterStepM = 16.0;

        Generator(WorldSpec spec, WorldRules rules, Func<double, double, double> baseHeight, Func<double, double, bool> isWater = null)
        {
            _isWater = isWater;
            _spec = spec; _rules = rules;
            _terrain = new TerrainModel(spec, baseHeight);
            foreach (var s in spec.AllowProvisional) rules.AllowProvisional.Add(s);
        }

        public static GenResult Run(WorldSpec spec, WorldRules rules) { return Run(spec, rules, null); }

        /// <summary>RG-167: the same generator on a terrain whose low-frequency base is baseHeight (x, z) -> m (the TerrainModel hook of RG-165); roads are
        /// routed and profiled on that terrain. null gives exactly Run(spec, rules).</summary>
        public static GenResult Run(WorldSpec spec, WorldRules rules, Func<double, double, double> baseHeight) { return Run(spec, rules, baseHeight, null); }

        /// <summary>RG-169: with a water mask isWater (x, z) -> true on water (null = exactly the overload without it): village sites and street / route candidates on water are
        /// rejected, and every road edge with a vertex (about every 4 m) on water is dropped (candidates are tested every WaterStepM) from the graph afterwards (no bridges; nodes left without an edge
        /// are removed, spans with a dropped edge too). What is left may be several components: the caller reports them.</summary>
        public static GenResult Run(WorldSpec spec, WorldRules rules, Func<double, double, double> baseHeight, Func<double, double, bool> isWater)
        {
            var sw = Stopwatch.StartNew();
            var gen = new Generator(spec, rules, baseHeight, isWater);
            using (new Prof.Scope("Execute")) gen.Execute();
            using (new Prof.Scope("DropWaterEdges")) { if (isWater != null) gen.DropWaterEdges(); }
            gen._res.Graph = gen._g;
            int widened; using (new Prof.Scope("MatchWidths")) widened = gen._g.MatchWidthsAtDegree2Nodes();
            gen.Log("width joints: " + widened + " edges matched at degree-2 nodes");
            using (new Prof.Scope("TerrainBind")) gen._terrain.Bind(gen._g);
            gen._res.Terrain = gen._terrain;
            foreach (var st in gen._set) gen._res.Settlements.Add(new SettlementInfo { Name = st.Name, C = st.C, Radius = st.Radius, IsCity = st.IsCity });
            gen._res.Seconds = sw.Elapsed.TotalSeconds;
            return gen._res;
        }

        void Log(string m) { _res.Log.Add(m); }

        /// <summary>True when a sample of the polyline (every WaterStepM, ends included) lies on water.</summary>
        bool WaterOn(V2[] pts)
        {
            double next = 0, acc = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                if (i > 0) acc += V2.Dist(pts[i], pts[i - 1]);
                if (i == 0 || i == pts.Length - 1 || acc >= next) { if (_isWater(pts[i].X, pts[i].Z)) return true; while (next <= acc) next += WaterStepM; }
            }
            return false;
        }

        /// <summary>True when any vertex of the edge polyline (about every 4 m) lies on water.</summary>
        bool WaterOnAny(V2[] pts) { for (int i = 0; i < pts.Length; i++) if (_isWater(pts[i].X, pts[i].Z)) return true; return false; }

        bool WaterOn(V2 a, V2 b)
        {
            double len = V2.Dist(a, b); int n = Math.Max(1, (int)Math.Ceiling(len / WaterStepM));
            for (int i = 0; i <= n; i++) { V2 q = a + (b - a) * ((double)i / n); if (_isWater(q.X, q.Z)) return true; }
            return false;
        }

        void DropWaterEdges()
        {
            var keep = new List<GEdge>(); var edgeMap = new int[_g.Edges.Count]; int dropped = 0;
            foreach (var e in _g.Edges) { if (WaterOnAny(e.Pts)) { edgeMap[e.Id] = -1; dropped++; } else { edgeMap[e.Id] = keep.Count; keep.Add(e); } }
            if (dropped == 0) return;
            var used = new bool[_g.Nodes.Count];
            foreach (var e in keep) { used[e.A] = true; used[e.B] = true; }
            var nodeMap = new int[_g.Nodes.Count]; var nodes = new List<GNode>();
            foreach (var n in _g.Nodes) { if (used[n.Id]) { nodeMap[n.Id] = nodes.Count; nodes.Add(n); } else nodeMap[n.Id] = -1; }
            var spans = new List<GSpan>();
            foreach (var sp in _g.Spans)
            {
                bool all = true; foreach (int ei in sp.Edges) if (edgeMap[ei] < 0) all = false;
                if (!all) continue;   // a span with a dropped edge goes entirely (its remaining edges stay as ground edges)
                sp.Id = spans.Count; sp.StartNode = nodeMap[sp.StartNode]; sp.EndNode = nodeMap[sp.EndNode];
                for (int k = 0; k < sp.Edges.Count; k++) sp.Edges[k] = edgeMap[sp.Edges[k]];
                spans.Add(sp);
            }
            for (int k = 0; k < keep.Count; k++) { keep[k].Id = k; keep[k].A = nodeMap[keep[k].A]; keep[k].B = nodeMap[keep[k].B]; }
            for (int k = 0; k < nodes.Count; k++) nodes[k].Id = k;
            _g.Edges.Clear(); _g.Edges.AddRange(keep); _g.Nodes.Clear(); _g.Nodes.AddRange(nodes); _g.Spans.Clear(); _g.Spans.AddRange(spans);
            Log("water: " + dropped + " road edge(s) on water dropped, " + keep.Count + " kept");
        }
        Rng Stage(string name) { return Rng.ForStage(_spec.Seed, name); }
        double Floor(string cls) { return _rules.SpacingFloor(cls); }

        void Execute()
        {
            Log("spec: " + _spec.Describe());
            using (new Prof.Scope("PlaceSettlements")) PlaceSettlements();
            if (_spec.Motorway) using (new Prof.Scope("BuildMotorway")) BuildMotorway();
            using (new Prof.Scope("BuildStreets")) { foreach (var s in _set) BuildStreets(s); }
            if (_spec.Entrances.Count > 0) using (new Prof.Scope("BuildEntrances")) BuildEntrances();
            if (_spec.Motorway) using (new Prof.Scope("BuildInterchange")) BuildInterchange();
            using (new Prof.Scope("ConnectPrimary")) ConnectPrimary();
            using (new Prof.Scope("ConnectSecondary")) ConnectSecondary();
            using (new Prof.Scope("Assemble")) Assemble();
        }

        // ---------------------------------------------------------------- road registry

        Road AddRoad(string cls, string tag, Centreline c, int startNode, int endNode, List<Cut> inner)
        {
            var r = new Road { Id = _roads.Count, Class = cls, Tag = tag, C = c };
            if (startNode < 0) startNode = _g.AddNode(c.P[0], 0, NodeKind.Joint).Id;
            if (endNode < 0) endNode = _g.AddNode(c.P[c.P.Length - 1], 0, NodeKind.Joint).Id;
            r.StartNode = startNode; r.EndNode = endNode;
            r.Cuts.Add(new Cut { S = 0, Node = startNode });
            if (inner != null) r.Cuts.AddRange(inner);
            r.Cuts.Add(new Cut { S = c.Length, Node = endNode });
            _roads.Add(r);
            foreach (var cut in r.Cuts) CountCut(cut.Node);
            for (int i = 0; i < c.P.Length - 1; i++) _idx.Add(c.P[i], c.P[i + 1], (r.Id << Shift) | i);
            return r;
        }

        readonly Dictionary<int, int> _cutCount = new Dictionary<int, int>();   // cuts per node over all roads (degree before assembly)

        void CountCut(int node) { int d; _cutCount.TryGetValue(node, out d); _cutCount[node] = d + 1; }

        int NodeOn(Road r, double s)
        {
            foreach (var c in r.Cuts) if (Math.Abs(c.S - s) < 1.0) return c.Node;
            var n = _g.AddNode(r.C.At(s), 0, NodeKind.Joint);
            r.Cuts.Add(new Cut { S = s, Node = n.Id });
            CountCut(n.Id);
            return n.Id;
        }

        bool CutClear(Road r, double s, double clear)
        {
            foreach (var c in r.Cuts) if (Math.Abs(c.S - s) < clear) return false;
            return true;
        }

        static Centreline Line(V2 a, V2 b)
        {
            double len = V2.Dist(a, b);
            int n = Math.Max(1, (int)Math.Ceiling(len / AlignmentBuilder.DenseStepM));
            var p = new V2[n + 1];
            for (int i = 0; i <= n; i++) p[i] = a + (b - a) * ((double)i / n);
            return new Centreline(p, new double[n + 1]);
        }

        // ---------------------------------------------------------------- stage 1: settlements

        void PlaceSettlements()
        {
            var rng = Stage("settlements");
            double half = _spec.Half;
            bool centreCity = _spec.CentreIsCity ?? _spec.Motorway;
            _set.Add(new Settlement { Name = _spec.Motorway ? "city" : (centreCity ? "town0" : "village0"), C = _spec.City, Radius = _spec.CityRadiusM, IsCity = centreCity });
            double margin = 900;
            var corridor = CorridorFrame();
            int placed = 0;
            for (int tries = 0; tries < 4000 && placed < _spec.Villages; tries++)
            {
                var p = new V2(rng.Range(-half + margin, half - margin), rng.Range(-half + margin, half - margin));
                bool ok = true;
                foreach (var s in _set)
                    if (V2.Dist(p, s.C) < _spec.VillageMinKm * 1000 + (s.IsCity ? _spec.CityRadiusM * 0.5 : 0)) { ok = false; break; }
                if (!ok) continue;
                if (_isWater != null && (_isWater(p.X, p.Z) || _isWater(p.X + 200, p.Z) || _isWater(p.X - 200, p.Z) || _isWater(p.X, p.Z + 200) || _isWater(p.X, p.Z - 200))) continue;
                if (_spec.Motorway && DistToCorridor(p, corridor) < 1500) continue;
                _set.Add(new Settlement { Name = "village" + (placed + 1), C = p, Radius = 350 });
                placed++;
            }
            if (placed < _spec.Villages) Log("WARNING: placed " + placed + " of " + _spec.Villages + " villages (spacing/corridor constraints)");
            Log("settlements: city + " + placed + " villages" + string.Concat(_set.Skip(1).Select(s => "; " + s.Name + " (" + s.C.X.ToString("0", CultureInfo.InvariantCulture) + "," + s.C.Z.ToString("0", CultureInfo.InvariantCulture) + ")")));
        }

        // corridor waypoints E, S0, S1, X (nominal motorway chord) and the interchange frame
        V2[] CorridorFrame()
        {
            V2 e = _spec.MotorwayEntry, x = _spec.MotorwayExit;
            V2 u = (x - e).Unit, n = u.Left;
            double l = V2.Dot(_spec.City - e, n);
            double sg = l >= 0 ? 1.0 : -1.0;
            double d = _spec.CityRadiusM + 2000;
            _pInter = _spec.City - n * (sg * d);
            _vNear = n * sg;
            double d1 = 800;
            return new[] { e, _pInter - u * d1, _pInter + u * d1, x };
        }

        static double DistToCorridor(V2 p, V2[] c)
        {
            double best = double.MaxValue;
            for (int i = 0; i < c.Length - 1; i++)
            {
                V2 ab = c[i + 1] - c[i];
                double t = Math.Max(0, Math.Min(1, V2.Dot(p - c[i], ab) / V2.Dot(ab, ab)));
                best = Math.Min(best, V2.Dist(p, c[i] + ab * t));
            }
            return best;
        }

        // ---------------------------------------------------------------- random-walk waypoints (PIs)

        List<V2> Walk(Rng rng, string cls, V2 start, double heading, V2 target)
        {
            var cr = _rules.Of(cls);
            double v = RoadClass.DesignSpeedMs(cls), ls = 1.5 * v, lim = _spec.Half - 250;
            var pis = new List<V2> { start };
            V2 pos = start; double h = heading;
            for (int guard = 0; guard < 400; guard++)
            {
                double r = Math.Min(2000, Math.Max(cr.MinRadiusM, cr.CurveRadius.Present ? cr.CurveRadius.Sample(rng) : cr.MinRadiusM * 2));
                double c = Math.Max(cr.CurveRun.Present ? cr.CurveRun.Sample(rng) : 60, _rules.ArcSeconds * v + ls);
                double delta = Math.Min(1.0, Math.Max(0.05, c / r));
                double t = AlignmentBuilder.TangentLength(delta, Math.Max(r, (_rules.ArcSeconds * v + ls) / delta), ls);
                double straight = Math.Max(cr.StraightRun.Present ? cr.StraightRun.Sample(rng) : 60, 2.4 * t + 20);
                double dist = V2.Dist(pos, target);
                if (dist < straight + 2.2 * t) break;
                pos = pos + V2.FromHeading(h) * straight;
                V2 aim = (Math.Abs(pos.X) > lim || Math.Abs(pos.Z) > lim) ? new V2(0, 0) : target;
                double err = V2.AngDiff((aim - pos).Heading, h);
                double sg = (Math.Abs(err) > delta || Math.Abs(err) > 0.45) ? Math.Sign(err) : (rng.Chance(0.55) ? Math.Sign(err == 0 ? 1 : err) : -Math.Sign(err == 0 ? 1 : err));
                h += sg * delta;
                pis.Add(pos);
            }
            pis.Add(target);
            return pis;
        }

        // ---------------------------------------------------------------- stage 2: motorway

        void BuildMotorway()
        {
            var corridor = CorridorFrame();
            V2 e = corridor[0], s0 = corridor[1], s1 = corridor[2], x = corridor[3];
            string cls = RoadClass.Motorway;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                var rng = Stage("motorway/" + attempt);
                var w0 = Walk(rng, cls, e, (s0 - e).Heading, s0);
                var w1 = Walk(rng, cls, s1, (x - s1).Heading, x);
                var pis = new List<V2>(w0); pis.AddRange(w1);
                var fx = new bool[pis.Count];
                fx[w0.Count - 1] = true; fx[w0.Count] = true;
                Centreline cl; string err;
                var rr = Stage("motorway-radius/" + attempt);
                var cr = _rules.Of(cls);
                if (!AlignmentBuilder.Build(pis.ToArray(), fx, cls, _rules, () => cr.CurveRadius.Present ? cr.CurveRadius.Sample(rr) : 800, out cl, out err)) continue;
                if (!InsideWorld(cl, 20) || Selfcross(cl)) continue;
                bool nearSettlement = false;
                foreach (var s in _set) if (DistToPolyline(cl, s.C) < s.Radius + (s.IsCity ? 1200 : 700)) nearSettlement = true;
                if (nearSettlement) continue;
                _motorway = AddRoad(cls, "motorway", cl, -1, -1, null);
                _g.Nodes[_motorway.StartNode].Boundary = true;
                _g.Nodes[_motorway.EndNode].Boundary = true;
                // interchange station = nearest point of the built centreline to the nominal P
                double best = double.MaxValue;
                for (int i = 0; i < cl.P.Length; i++)
                {
                    double d = V2.Dist(cl.P[i], _pInter);
                    if (d < best) { best = d; _sP = cl.S[i]; }
                }
                _pInter = cl.At(_sP);
                _motorway.ZoneA = _sP - 700; _motorway.ZoneB = _sP + 700;
                Log("motorway: attempt " + attempt + ", " + (cl.Length / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " km, interchange at station " + _sP.ToString("0", CultureInfo.InvariantCulture));
                return;
            }
            throw new InvalidOperationException("motorway: no valid alignment in 200 attempts");
        }

        bool InsideWorld(Centreline c, double inset)
        {
            double lim = _spec.Half - inset;
            foreach (var p in c.P) if (Math.Abs(p.X) > _spec.Half + 1 || Math.Abs(p.Z) > _spec.Half + 1) return false;
            // interior points must stay clear of the border band except the first/last 300 m
            for (int i = 0; i < c.P.Length; i++)
                if (c.S[i] > 300 && c.S[i] < c.Length - 300 && (Math.Abs(c.P[i].X) > lim || Math.Abs(c.P[i].Z) > lim)) return false;
            return true;
        }

        static double DistToPolyline(Centreline c, V2 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < c.P.Length; i += 2) best = Math.Min(best, V2.Dist(c.P[i], p));
            return best;
        }

        static bool Selfcross(Centreline c)
        {
            var idx = new SegIndex(100);
            var hits = new List<int>();
            for (int i = 0; i < c.P.Length - 1; i++)
            {
                idx.Query(c.P[i], c.P[i + 1], hits);
                foreach (int h in hits)
                {
                    double t, u;
                    if (Geo.SegCross(c.P[i], c.P[i + 1], idx.A[h], idx.B[h], out t, out u)) return true;
                }
                idx.Add(c.P[i], c.P[i + 1], i);
            }
            return false;
        }

        // ---------------------------------------------------------------- stage 3: street sets

        int NewNode(V2 p) { return _g.AddNode(p, 0, NodeKind.Joint).Id; }

        // Street growth (RG-116, W3.1): one seeded segment-growth routine for the city and the villages (Parish & Mueller 2001, simplified).
        // Every accepted segment is a straight local Road between two nodes; crossings, snaps and T joins go through NodeOn/AddRoad like every other road.
        // ProbeSegment applies the local constraints without changing anything; ApplySegment commits the result. Tests drive both directly.
        const double GrowLenMin = 60, GrowLenMax = 140, GrowDriftDeg = 8, BranchDevDeg = 15;
        const double SnapNodeM = 25, SnapStreetM = 20, MinSegM = 40, RadiusNoise = 0.15;
        const double PContinue = 0.95, PBranch = 0.8, PBranchBoth = 0.22;
        // grid-equivalent street density of the old generator: two line families 112.5 m apart -> 2 / 112.5 m of street per m2 of disk
        const double GridEquivSpacingM = 112.5;

        sealed class Prop
        {
            public int Gen, Id, From;
            public double Heading, Dist;
        }

        sealed class PropOrder : IComparer<Prop>
        {
            public int Compare(Prop a, Prop b)
            {
                int c = a.Gen.CompareTo(b.Gen);
                if (c != 0) return c;
                c = a.Dist.CompareTo(b.Dist);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            }
        }

        /// <summary>How a proposed segment ends after the local constraints.</summary>
        internal enum EndKind { Free, Cross, Extend, Snap }

        /// <summary>Result of ProbeSegment: Reject is null when the segment is accepted.</summary>
        internal sealed class Probe
        {
            public EndKind Kind = EndKind.Free;
            public V2 Pt;
            public Road Target; public double Station;   // Cross / Extend: the street that gets a T and where
            public int Node = -1;                        // Snap: the node the segment ends on
            public string Reject;
        }

        readonly Dictionary<int, List<V2>> _legs = new Dictionary<int, List<V2>>();   // outgoing leg directions per node (growth stage only)
        readonly HashSet<long> _joined = new HashSet<long>();                          // node pairs already joined by a growth segment

        static long PairKey(int a, int b) { return ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b); }

        void AddLeg(int node, V2 dir)
        {
            List<V2> l;
            if (!_legs.TryGetValue(node, out l)) { l = new List<V2>(); _legs[node] = l; }
            l.Add(dir.Unit);
        }

        /// <summary>True when direction d makes at least LegAngleMinDeg with every leg already at the node.</summary>
        bool LegsOk(int node, V2 d)
        {
            List<V2> l;
            if (!_legs.TryGetValue(node, out l)) return true;
            double min = Math.Cos(_rules.LegAngleMinDeg * Math.PI / 180.0);
            foreach (var v in l) if (V2.Dot(v, d.Unit) > min) return false;
            return true;
        }

        /// <summary>Settlement radius with seeded +-15 % low-frequency noise (two sines of the bearing).</summary>
        static double NoisyRadius(Settlement s, V2 p)
        {
            double th = (p - s.C).Heading;
            return s.Radius * (1.0 + RadiusNoise * (s.A1 * Math.Sin(2 * th + s.P1) + s.A2 * Math.Sin(3 * th + s.P2)));
        }

        int NearestNode(V2 p, double within, int except)
        {
            // nodes are found through the segment index: every node is an end or a cut of some street or road
            var hits = new List<int>();
            V2 pad = new V2(within, within);
            _idx.Query(p - pad, p + pad, hits);
            int best = -1; double bd = within;
            foreach (int h in hits)
            {
                var r = _roads[_idx.Owner[h] >> Shift];
                foreach (var cut in r.Cuts)
                {
                    if (cut.Node == except) continue;
                    double d = V2.Dist(_g.Nodes[cut.Node].P, p);
                    if (d < bd || (d == bd && best >= 0 && cut.Node < best)) { bd = d; best = cut.Node; }
                }
            }
            return best;
        }

        bool CrossesAnything(V2 a, V2 pt)
        {
            var hits = new List<int>();
            _idx.Query(a, pt, hits);
            foreach (int h in hits)
            {
                double t, u;
                if (Geo.SegCross(a, pt, _idx.A[h], _idx.B[h], out t, out u, true) && V2.Dist(pt, a + (pt - a) * t) > 0.5 && V2.Dist(a, a + (pt - a) * t) > 0.5) return true;
            }
            return false;
        }

        /// <summary>Local constraints for a proposed segment from node 'from' toward b, checked in this order: (a) crossing an existing street -> cut there and stop,
        /// (b) end within 25 m of a node -> snap, (c) end within 20 m of a street -> extend onto it as a T, (d) reject: too short, outside the noisy radius,
        /// a leg angle below the validator minimum, a repeated pair of nodes, a snap or T that would cross something, a cut too close to another node.</summary>
        internal Probe ProbeSegment(Settlement s, int from, V2 b)
        {
            string cls = RoadClass.Local;
            var pb = new Probe { Pt = b };
            V2 a = _g.Nodes[from].P;
            var hits = new List<int>();

            double bestT = double.MaxValue;
            _idx.Query(a, b, hits);
            foreach (int h in hits)
            {
                double t, u;
                if (!Geo.SegCross(a, b, _idx.A[h], _idx.B[h], out t, out u, true)) continue;   // half-open: a hit exactly on a street vertex counts once
                if (V2.Dist(a, a + (b - a) * t) < 0.5) continue;                                // touching at the own start node is no crossing
                if (t < bestT)
                {
                    int own = _idx.Owner[h]; var r = _roads[own >> Shift]; int li = own & ((1 << Shift) - 1);
                    bestT = t; pb.Target = r; pb.Station = r.C.S[li] + u * (r.C.S[li + 1] - r.C.S[li]); pb.Pt = a + (b - a) * t; pb.Kind = EndKind.Cross;
                }
            }
            if (pb.Kind == EndKind.Cross && pb.Target.Class != cls) { pb.Reject = "crosses a " + pb.Target.Class + " road"; return pb; }
            {
                // a node the path passes within the snap distance of ends the segment there, if it comes before the crossing (no street may graze a node it does not join)
                V2 ab = pb.Pt - a; double l2 = V2.Dot(ab, ab);
                var near = new List<int>();
                V2 pad = new V2(SnapNodeM, SnapNodeM);
                V2 lo = new V2(Math.Min(a.X, pb.Pt.X), Math.Min(a.Z, pb.Pt.Z)) - pad, hi = new V2(Math.Max(a.X, pb.Pt.X), Math.Max(a.Z, pb.Pt.Z)) + pad;
                _idx.Query(lo, hi, near);
                int bestN = -1; double bestAlong = double.MaxValue;
                foreach (int h in near)
                {
                    foreach (var cut in _roads[_idx.Owner[h] >> Shift].Cuts)
                    {
                        if (cut.Node == from) continue;
                        V2 np = _g.Nodes[cut.Node].P;
                        double along = V2.Dot(np - a, ab) / l2;
                        if (along <= 0 || along >= 1) continue;
                        if (V2.Dist(np, a + ab * along) >= SnapNodeM) continue;
                        if (along < bestAlong || (along == bestAlong && cut.Node < bestN)) { bestAlong = along; bestN = cut.Node; }
                    }
                }
                if (bestN >= 0) { pb.Kind = EndKind.Snap; pb.Node = bestN; pb.Pt = _g.Nodes[bestN].P; pb.Target = null; }
            }
            if (pb.Kind == EndKind.Free)
            {
                int n = NearestNode(b, SnapNodeM, from);
                if (n >= 0) { pb.Kind = EndKind.Snap; pb.Node = n; pb.Pt = _g.Nodes[n].P; }
                else
                {
                    var hits2 = new List<int>();
                    V2 pad = new V2(SnapStreetM, SnapStreetM);
                    _idx.Query(b - pad, b + pad, hits2);
                    double bd = SnapStreetM;
                    foreach (int h in hits2)
                    {
                        V2 sa = _idx.A[h], sb = _idx.B[h], ab = sb - sa;
                        double tt = Math.Max(0, Math.Min(1, V2.Dot(b - sa, ab) / V2.Dot(ab, ab)));
                        V2 q = sa + ab * tt; double d = V2.Dist(q, b);
                        if (d >= bd) continue;
                        int own = _idx.Owner[h]; var r = _roads[own >> Shift]; int li = own & ((1 << Shift) - 1);
                        if (r.Class != cls) continue;
                        bd = d; pb.Target = r; pb.Station = r.C.S[li] + tt * (r.C.S[li + 1] - r.C.S[li]); pb.Pt = q; pb.Kind = EndKind.Extend;
                    }
                }
            }
            if (pb.Kind == EndKind.Cross || pb.Kind == EndKind.Extend)
            {
                // a crossing or T that lands within the snap distance of a node becomes a snap onto that node
                int n = NearestNode(pb.Pt, SnapNodeM, from);
                if (n >= 0) { pb.Kind = EndKind.Snap; pb.Node = n; pb.Pt = _g.Nodes[n].P; pb.Target = null; }
            }

            double seg = V2.Dist(a, pb.Pt);
            if (seg < (_entranceRelaxed ? 2.0 : Math.Max(MinSegM, Floor(cls)))) { pb.Reject = "too short"; return pb; }
            if (_isWater != null && WaterOn(a, pb.Pt)) { pb.Reject = "water"; return pb; }
            if (!_entranceMode && V2.Dist(pb.Pt, s.C) > NoisyRadius(s, pb.Pt)) { pb.Reject = "outside the settlement radius"; return pb; }
            V2 dn = (pb.Pt - a).Unit;
            if (!_entranceRelaxed && !LegsOk(from, dn)) { pb.Reject = "leg angle below minimum at the start node"; return pb; }
            if (pb.Kind == EndKind.Snap)
            {
                if (_entranceMode && _pinY.ContainsKey(pb.Node)) { pb.Reject = "snap onto another pinned entrance node"; return pb; }   // two pinned heights metres apart cannot both hold
                if (_joined.Contains(PairKey(from, pb.Node))) { pb.Reject = "nodes already joined"; return pb; }
                if (!_entranceRelaxed && !LegsOk(pb.Node, dn * -1.0)) { pb.Reject = "leg angle below minimum at the snapped node"; return pb; }
                if (CrossesAnything(a, pb.Pt)) { pb.Reject = "snapped segment crosses a street"; return pb; }
            }
            else if (pb.Kind == EndKind.Extend)
            {
                if (CrossesAnything(a, pb.Pt)) { pb.Reject = "extended segment crosses a street"; return pb; }
            }
            if (pb.Kind == EndKind.Cross || pb.Kind == EndKind.Extend)
            {
                if (pb.Target.NoCross || (!_entranceRelaxed && !CutClear(pb.Target, pb.Station, Floor(cls)))) { pb.Reject = "T too close to another node of the street"; return pb; }
                if (!_entranceRelaxed && Math.Abs(V2.Dot(pb.Target.C.Tangent(pb.Station), dn)) > Math.Cos(_rules.LegAngleMinDeg * Math.PI / 180.0)) { pb.Reject = "crossing angle below minimum"; return pb; }
            }
            return pb;
        }

        /// <summary>Creates the end node (or reuses one), the street Road and the legs for an accepted probe. Returns the new end node.</summary>
        internal int ApplySegment(Settlement s, int from, Probe pb, string tag)
        {
            int endNode;
            if (pb.Kind == EndKind.Snap) endNode = pb.Node;
            else if (pb.Kind == EndKind.Cross || pb.Kind == EndKind.Extend)
            {
                endNode = NodeOn(pb.Target, pb.Station);
                AddLeg(endNode, pb.Target.C.Tangent(pb.Station)); AddLeg(endNode, pb.Target.C.Tangent(pb.Station) * -1.0);
            }
            else endNode = NewNode(pb.Pt);
            V2 a = _g.Nodes[from].P;
            var road = AddRoad(RoadClass.Local, tag, Line(a, _g.Nodes[endNode].P), from, endNode, null);
            s.Streets.Add(road);
            _joined.Add(PairKey(from, endNode));
            V2 dn = (_g.Nodes[endNode].P - a).Unit;
            AddLeg(from, dn); AddLeg(endNode, dn * -1.0);
            return endNode;
        }

        void BuildStreets(Settlement s)
        {
            var rng = Stage("streets/" + s.Name);
            string tag = s.IsCity ? "city-street" : "village-street";
            s.P1 = rng.Range(0, 2 * Math.PI); s.P2 = rng.Range(0, 2 * Math.PI); s.A1 = rng.Range(0.4, 0.7); s.A2 = 1.0 - s.A1;
            double budget = s.IsCity ? 2.0 * Math.PI * s.Radius * s.Radius / GridEquivSpacingM : rng.Range(550, 900);
            double total = 0;
            var queue = new SortedSet<Prop>(new PropOrder());
            int nextId = 0, accepted = 0, rejected = 0, snaps = 0, crosses = 0, exts = 0;

            int centre = NewNode(s.C);
            var headings = new List<double>();
            if (s.IsCity)
            {
                // 3-5 arterial seeds, at least LegAngleMinDeg apart (the validator needs 70 degrees at the junction they meet in)
                int k = 3 + rng.Int(3);
                double th = rng.Range(0, 2 * Math.PI), step = 2 * Math.PI / k;
                double jit = Math.Max(0, (step - (_rules.LegAngleMinDeg + 1) * Math.PI / 180.0) / 2) * 0.9;
                for (int i = 0; i < k; i++) headings.Add(th + i * step + rng.Range(-jit, jit));
            }
            else
            {
                // main street through the centre toward the nearest other settlement (the road that will reach the village)
                double best = double.MaxValue; V2 to = _set[0].C;
                foreach (var o in _set) if (o != s && V2.Dist(o.C, s.C) < best) { best = V2.Dist(o.C, s.C); to = o.C; }
                double h = (to - s.C).Heading + rng.Range(-0.3, 0.3);
                headings.Add(h); headings.Add(h + Math.PI);
            }
            foreach (double h in headings)
                queue.Add(new Prop { Gen = 0, Id = nextId++, From = centre, Heading = h, Dist = 0 });

            while (queue.Count > 0 && total < budget)
            {
                var pr = queue.Min; queue.Remove(pr);
                V2 a = _g.Nodes[pr.From].P;
                double len = rng.Range(GrowLenMin, GrowLenMax);
                var pb = ProbeSegment(s, pr.From, a + V2.FromHeading(pr.Heading) * len);
                if (pb.Reject != null) { rejected++; continue; }
                int endNode = ApplySegment(s, pr.From, pb, tag);
                total += s.Streets[s.Streets.Count - 1].C.Length; accepted++;
                if (pb.Kind == EndKind.Snap) snaps++; else if (pb.Kind == EndKind.Cross) crosses++; else if (pb.Kind == EndKind.Extend) exts++;
                if (pb.Kind != EndKind.Free) continue;   // a street that ends on another street stops growing

                // propose from the new end: continue (drift +-8 deg) and side branches (90 +- 15 deg)
                double hd = (pb.Pt - a).Heading;
                double dist = V2.Dist(pb.Pt, s.C);
                if (rng.Chance(PContinue))
                    queue.Add(new Prop { Gen = pr.Gen + 1, Id = nextId++, From = endNode, Heading = hd + rng.Range(-GrowDriftDeg, GrowDriftDeg) * Math.PI / 180.0, Dist = dist });
                if (rng.Chance(PBranch))
                {
                    int sg = rng.Chance(0.5) ? 1 : -1;
                    int nb = rng.Chance(PBranchBoth / PBranch) ? 2 : 1;
                    for (int i = 0; i < nb; i++)
                    {
                        int side = i == 0 ? sg : -sg;
                        queue.Add(new Prop { Gen = pr.Gen + 2, Id = nextId++, From = endNode, Heading = hd + side * (Math.PI / 2 + rng.Range(-BranchDevDeg, BranchDevDeg) * Math.PI / 180.0), Dist = dist });
                    }
                }
            }
            Log("streets " + s.Name + ": " + s.Streets.Count + " segments, " + (total / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km of "
                + (budget / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km budget (grid-equivalent), " + rejected + " rejected, "
                + snaps + " snaps, " + crosses + " crossings, " + exts + " T extensions");
        }

        // ---- RG-182: pinned entrances ----
        bool _entranceMode, _entranceRelaxed;   // relaxed: the junction quality rules (min segment, leg and crossing angles, cut spacing) do not reject; the heading of the entrance does not move
                                                // the entrance probe may start outside the noisy radius
        readonly Dictionary<int, double> _pinY = new Dictionary<int, double>();  // node -> pinned height (seeds nodeY of the vertical profile)
        static readonly double[] EntranceSteps = { 80, 55, 110, 140, 65, 95 };

        /// <summary>
        /// Entrance road of every spec entrance: a node at the entrance point (height pinned), then straight segments along the entrance direction (the first one never bends:
        /// the corridor's heading is kept) until a segment ends on the street graph (cross / snap / T). A rejected segment retries with other lengths; when all fail the
        /// road bends once to the nearest street node. The roads have the entrance class; the street graph is the one that BuildStreets left.
        /// </summary>
        void BuildEntrances()
        {
            var s = _set[0];
            var own = new HashSet<int>();
            var baseNodes = new List<int>();
            foreach (var r in _roads) foreach (var c in r.Cuts) if (own.Add(c.Node)) baseNodes.Add(c.Node);
            _entranceMode = true;
            int ei = 0;
            foreach (var e in _spec.Entrances)
            {
                ei++;
                int cur = NewNode(e.Point); _pinY[cur] = e.Y;
                V2 dir = e.Dir.Unit; bool joined = false;
                for (int it = 0; it < 60 && !joined; it++)
                {
                    V2 a = _g.Nodes[cur].P; Probe ok = null; string why = "";
                    for (int pass = 0; pass < 2 && ok == null; pass++)
                        foreach (double len in EntranceSteps)
                        {
                            _entranceRelaxed = it == 0 && pass == 1;
                            var pb = ProbeSegment(s, cur, a + dir * len);
                            if (pb.Reject != null) { if (it == 0) why += " [" + len + ": " + pb.Reject + "]"; continue; }
                            if (it == 0 && V2.Dot((pb.Pt - a).Unit, dir) < 0.99995) continue;   // the first segment keeps the corridor heading (a snap or T would bend it)
                            ok = pb; break;
                        }
                    _entranceRelaxed = false;
                    if (ok == null) { if (it == 0) Log("entrance " + ei + ": first segment rejected" + why); break; }
                    int end = ApplyEntrance(cur, ok, e.Class, "entrance" + ei);
                    joined = ok.Kind != EndKind.Free; cur = end;
                }
                if (!joined)
                {
                    // fallback: straight lead-in along the heading (the entrance direction is kept), then one bend to a street node. Pass 0 keeps the junction rules
                    // (lead-in >= 20 m, node >= 20 m away, leg angle), pass 1 drops the leg angle and the distances, pass 2 ignores crossings (last resort, a 10 m lead-in).
                    V2 a = _g.Nodes[cur].P; double bestScore = double.MaxValue, bestD = 0; int bestN = -1, usedPass = 0;
                    for (int pass = 0; pass < 3 && bestN < 0; pass++)
                    {
                        usedPass = pass;
                        for (double d = pass == 0 ? 20 : 10; d <= (pass == 2 ? 10 : 600); d += pass == 0 ? 20 : 10)
                        {
                            V2 p = a + dir * d;
                            if (pass < 2 && CrossesAnything(a, p)) break;
                            foreach (int n in baseNodes.OrderBy(q => V2.Dist(_g.Nodes[q].P, p)).Take(8))
                            {
                                V2 np = _g.Nodes[n].P; double dist = V2.Dist(np, p);
                                if (dist < (pass == 0 ? 20 : 3)) continue;
                                if (pass == 0 && !LegsOk(n, (p - np).Unit)) continue;
                                if (pass < 2 && CrossesAnything(p, np)) continue;
                                double bend = Math.Acos(Math.Max(-1, Math.Min(1, V2.Dot(dir, (np - p).Unit)))) * 180 / Math.PI;
                                double score = bend + 0.05 * (d + dist);
                                if (score < bestScore) { bestScore = score; bestD = d; bestN = n; }
                            }
                        }
                    }
                    if (bestN >= 0)
                    {
                        V2 p = a + dir * bestD; int from = NewNode(p);
                        AddRoad(e.Class, "entrance" + ei, Line(a, p), cur, from, null); AddLeg(cur, dir); AddLeg(from, dir * -1.0);
                        AddRoad(e.Class, "entrance" + ei, Line(p, _g.Nodes[bestN].P), from, bestN, null); AddLeg(bestN, p - _g.Nodes[bestN].P);
                        Log("entrance " + ei + ": fallback lead-in " + bestD.ToString("0", CultureInfo.InvariantCulture) + " m (pass " + usedPass + "), bend " + bestScore.ToString("0", CultureInfo.InvariantCulture) + " to the street node " + bestN);
                    }
                    else Log("entrance " + ei + ": fallback failed, entrance left unconnected");
                }
            }
            _entranceMode = false;
        }

        int ApplyEntrance(int from, Probe pb, string cls, string tag)
        {
            int endNode;
            if (pb.Kind == EndKind.Snap) endNode = pb.Node;
            else if (pb.Kind == EndKind.Cross || pb.Kind == EndKind.Extend)
            {
                endNode = NodeOn(pb.Target, pb.Station);
                AddLeg(endNode, pb.Target.C.Tangent(pb.Station)); AddLeg(endNode, pb.Target.C.Tangent(pb.Station) * -1.0);
            }
            else endNode = NewNode(pb.Pt);
            V2 a = _g.Nodes[from].P;
            AddRoad(cls, tag, Line(a, _g.Nodes[endNode].P), from, endNode, null);
            _joined.Add(PairKey(from, endNode));
            V2 dn = (_g.Nodes[endNode].P - a).Unit;
            AddLeg(from, dn); AddLeg(endNode, dn * -1.0);
            return endNode;
        }

        // ---- test hooks (internal, visible to WhatIf.WorldGen.Tests only) ----

        internal static Generator ForTest(WorldSpec spec, WorldRules rules) { return new Generator(spec, rules, null); }

        internal Settlement TestSettlement(V2 c, double radius)
        {
            var s = new Settlement { Name = "test", C = c, Radius = radius, IsCity = true, A1 = 0, A2 = 0 };
            _set.Add(s);
            return s;
        }

        /// <summary>Adds a street between two new nodes at a and b (free ends), registering legs so later probes see them.</summary>
        internal int[] TestStreet(Settlement s, V2 a, V2 b)
        {
            int na = NewNode(a), nb = NewNode(b);
            var r = AddRoad(RoadClass.Local, "test-street", Line(a, b), na, nb, null);
            s.Streets.Add(r); _joined.Add(PairKey(na, nb));
            AddLeg(na, (b - a)); AddLeg(nb, (a - b));
            return new[] { na, nb };
        }

        internal RoadGraph TestGraph { get { return _g; } }
        internal int TestRoadCount { get { return _roads.Count; } }

        // ---------------------------------------------------------------- crossings and routing

        List<Crossing> FindCrossings(Centreline c)
        {
            var list = new List<Crossing>();
            var hits = new List<int>();
            for (int i = 0; i < c.P.Length - 1; i++)
            {
                _idx.Query(c.P[i], c.P[i + 1], hits);
                foreach (int h in hits)
                {
                    double t, u;
                    if (!Geo.SegCross(c.P[i], c.P[i + 1], _idx.A[h], _idx.B[h], out t, out u, true)) continue;
                    int own = _idx.Owner[h];
                    var r = _roads[own >> Shift]; int li = own & ((1 << Shift) - 1);
                    double sNew = c.S[i] + t * (c.S[i + 1] - c.S[i]);
                    if (sNew < 1.5 || sNew > c.Length - 1.5) continue;
                    double sR = r.C.S[li] + u * (r.C.S[li + 1] - r.C.S[li]);
                    double ang = Math.Abs(V2.AngDiff((c.P[i + 1] - c.P[i]).Heading, (_idx.B[h] - _idx.A[h]).Heading)) * 180 / Math.PI;
                    list.Add(new Crossing { SNew = sNew, SR = sR, Angle = Math.Min(ang, 180 - ang), R = r, P = c.P[i] + (c.P[i + 1] - c.P[i]) * t });
                }
            }
            list.Sort((p, q) => p.SNew.CompareTo(q.SNew));
            var outl = new List<Crossing>();
            foreach (var x in list)
                if (outl.Count == 0 || outl[outl.Count - 1].R != x.R || Math.Abs(outl[outl.Count - 1].SNew - x.SNew) > 3) outl.Add(x);
            return outl;
        }

        /// <summary>Accepts or rejects the crossings of a candidate road. On success lists the X junctions to create and the motorway bridge stations.</summary>
        bool Evaluate(Centreline c, string cls, bool allowZone, out List<Crossing> junctions, out List<double> bridges)
        {
            junctions = new List<Crossing>(); bridges = new List<double>();
            if (!InsideWorld(c, 60)) return Why("outside-world");
            if (Selfcross(c)) return Why("selfcross");
            if (_isWater != null && WaterOn(c.P)) return Why("water");
            double fl = Floor(cls);
            foreach (var x in FindCrossings(c))
            {
                if (x.R.NoCross) return Why("crosses-nocross-road");
                if (x.R.Class == RoadClass.Motorway)
                {
                    if (x.Angle < 30) return Why("motorway-angle");
                    if (x.SR < 300 || x.SR > x.R.C.Length - 300) return Why("motorway-near-end");
                    if (!allowZone && x.SR >= x.R.ZoneA && x.SR <= x.R.ZoneB) return Why("motorway-zone");
                    bridges.Add(x.SR);
                    continue;
                }
                if (x.Angle < _rules.LegAngleMinDeg || ChordLegAngleDeg(c, x.SNew, x.R.C, x.SR) < _rules.LegAngleMinDeg + RouteLegMarginDeg) return Why("leg-angle");
                if (x.SNew < fl || x.SNew > c.Length - fl) return Why("junction-near-end");
                if (!CutClear(x.R, x.SR, Floor(x.R.Class))) return Why("junction-near-node");
                foreach (var j in junctions)
                {
                    if (Math.Abs(j.SNew - x.SNew) < fl) return Why("junctions-too-close");
                    if (j.R == x.R && Math.Abs(j.SR - x.SR) < Floor(x.R.Class)) return Why("junctions-too-close-on-road");
                }
                junctions.Add(x);
            }
            return true;
        }

        /// <summary>Smallest angle between a leg of road a and a leg of road b at a crossing, measured on chords of 15 m and 20 m along the centrelines (the validator uses the edge samples, 10 m apart).</summary>
        static double ChordLegAngleDeg(Centreline a, double sa, Centreline b, double sb)
        {
            double min = 180;
            foreach (double look in new[] { 15.0, 20.0 })
            {
                var la = new[] { (a.At(Math.Min(a.Length, sa + look)) - a.At(sa)).Heading, (a.At(Math.Max(0, sa - look)) - a.At(sa)).Heading };
                var lb = new[] { (b.At(Math.Min(b.Length, sb + look)) - b.At(sb)).Heading, (b.At(Math.Max(0, sb - look)) - b.At(sb)).Heading };
                foreach (double ha in la) foreach (double hb in lb) min = Math.Min(min, Math.Abs(V2.AngDiff(ha, hb)) * 180 / Math.PI);
            }
            return min;
        }

        readonly Dictionary<string, int> _routeWhy = new Dictionary<string, int>();
        bool Why(string k) { int n; _routeWhy.TryGetValue(k, out n); _routeWhy[k] = n + 1; return false; }

        List<Attach> Candidates(Settlement s, V2 toward, int max)
        {
            var list = new List<Attach>();
            double fl = Floor(RoadClass.Local) * 1.5;
            foreach (var r in s.Streets)
            {
                for (int endSel = 0; endSel < 2; endSel++)
                {
                    int node = endSel == 0 ? r.StartNode : r.EndNode;
                    if (_usedEnds.Contains(node)) continue;
                    double st = endSel == 0 ? 0 : r.C.Length;
                    V2 q = r.C.At(st);
                    V2 n = endSel == 0 ? r.C.Tangent(0) * -1 : r.C.Tangent(st);
                    if (!DegreeOne(node)) continue;
                    if (V2.Dot((toward - q).Unit, n) > 0.3) list.Add(new Attach { R = r, S = st, IsEnd = true, Q = q, N = n, Node = node, Score = V2.Dist(q, toward) - 150 });
                }
                for (double st = fl; st <= r.C.Length - fl; st += 8)
                {
                    V2 q = r.C.At(st), t = r.C.Tangent(st);
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        V2 n = t.Left * sd;
                        if (V2.Dot((toward - q).Unit, n) > 0.35 && CutClear(r, st, fl)) list.Add(new Attach { R = r, S = st, Q = q, N = n, Score = V2.Dist(q, toward) });
                    }
                }
            }
            list.Sort((p, q) => p.Score.CompareTo(q.Score));
            var pick = new List<Attach>();
            foreach (var a in list)
            {
                bool dup = false;
                foreach (var b in pick) if (V2.Dist(a.Q, b.Q) < 45) { dup = true; break; }
                if (!dup) pick.Add(a);
                if (pick.Count >= max) break;
            }
            return pick;
        }

        bool DegreeOne(int node)
        {
            int d;
            return _cutCount.TryGetValue(node, out d) && d == 1;
        }

        void Commit(string cls, string tag, Centreline c, Attach a, Attach b, List<Crossing> junctions, List<double> bridges, bool free2 = false)
        {
            var inner = new List<Cut>();
            foreach (var x in junctions)
            {
                var n = _g.AddNode(x.P, 0, NodeKind.Junction);
                x.R.Cuts.Add(new Cut { S = x.SR, Node = n.Id });
                CountCut(n.Id);
                inner.Add(new Cut { S = x.SNew, Node = n.Id });
            }
            int sn = a == null ? -1 : (a.IsEnd ? a.Node : NodeOn(a.R, a.S));
            int en = b == null ? -1 : (b.IsEnd ? b.Node : NodeOn(b.R, b.S));
            if (a != null && a.IsEnd) _usedEnds.Add(a.Node);
            if (b != null && b.IsEnd) _usedEnds.Add(b.Node);
            AddRoad(cls, tag, c, sn, en, inner);
            _mxCross.AddRange(bridges);
        }

        bool TryRoute(string cls, Settlement a, Settlement b, int attempt, string tag)
        {
            var ca = Candidates(a, b.C, 10); var cb = Candidates(b, a.C, 10);
            if (ca.Count == 0 || cb.Count == 0) return Why("no-candidates");
            var A = ca[attempt % ca.Count]; var B = cb[(attempt / ca.Count) % cb.Count];
            double stub = Math.Min(300, Math.Max(60, 2.5 * _rules.Of(cls).MinRadiusM));
            V2 s1 = A.Q + A.N * stub, t2 = B.Q + B.N * stub;
            if (V2.Dist(s1, t2) < 3 * stub) return Why("stubs-too-close");
            var rng = Stage("route/" + tag + "/" + attempt);
            var walk = Walk(rng, cls, s1, A.N.Heading, t2);
            var pis = new List<V2> { A.Q }; pis.AddRange(walk); pis.Add(B.Q);
            var fx = new bool[pis.Count];
            fx[1] = true; fx[pis.Count - 2] = true;
            var cr = _rules.Of(cls);
            Centreline cl; string err;
            if (!AlignmentBuilder.Build(pis.ToArray(), fx, cls, _rules, () => cr.CurveRadius.Present ? cr.CurveRadius.Sample(rng) : cr.MinRadiusM * 2, out cl, out err)) return Why("align:" + err);
            List<Crossing> js; List<double> bs;
            if (!Evaluate(cl, cls, false, out js, out bs)) return false;
            Commit(cls, tag, cl, A, B, js, bs);
            return true;
        }

        /// <summary>Tries up to RouteAttempts routes between two settlements. Logs the outcome; the caller decides whether a failure matters.</summary>
        bool Connect(string cls, Settlement a, Settlement b, string tag)
        {
            _routeWhy.Clear();
            for (int attempt = 0; attempt < RouteAttempts; attempt++)
                if (TryRoute(cls, a, b, attempt, tag)) { Log(tag + ": connected after " + (attempt + 1) + " attempt(s)"); return true; }
            Log("no route " + tag + " in " + RouteAttempts + " attempts: " + string.Join(", ", _routeWhy.OrderBy(kv => kv.Key).Select(kv => kv.Key + " x" + kv.Value)));
            return false;
        }

        // ---------------------------------------------------------------- stage 4: interchange

        void BuildInterchange()
        {
            string cls = RoadClass.Primary;
            var city = _set[0];
            V2 u = _motorway.C.Tangent(_sP), vNear = _vNear;
            // keep the spine exactly perpendicular to the motorway
            vNear = (V2.Dot(u.Left, vNear) >= 0 ? u.Left : u.Left * -1);
            double eMax = RampEAfter;
            V2 w0b = _pInter + vNear * (eMax + 350), f = _pInter - vNear * (eMax + 250);
            double stub = Math.Min(300, Math.Max(60, 2.5 * _rules.Of(cls).MinRadiusM));
            var why = new Dictionary<string, int>();
            Action<string> Why = k => { int c; why.TryGetValue(k, out c); why[k] = c + 1; };
            for (int attempt = 0; attempt < 160; attempt++)
            {
                var ca = Candidates(city, _pInter, 10);
                if (ca.Count == 0) { Why("no-candidates"); break; }
                var A = ca[attempt % ca.Count];
                V2 s1 = A.Q + A.N * stub;
                if (V2.Dist(s1, w0b) < 2.5 * stub) { Why("too-close"); continue; }
                var rng = Stage("spine/" + attempt);
                var walk = Walk(rng, cls, s1, A.N.Heading, w0b);
                var pis = new List<V2> { A.Q }; pis.AddRange(walk); pis.Add(f);
                var fx = new bool[pis.Count];
                fx[1] = true; fx[pis.Count - 2] = true;
                var cr = _rules.Of(cls);
                Centreline cl; string err;
                if (!AlignmentBuilder.Build(pis.ToArray(), fx, cls, _rules, () => cr.CurveRadius.Present ? cr.CurveRadius.Sample(rng) : cr.MinRadiusM * 2, out cl, out err)) { Why("build:" + err); continue; }
                List<Crossing> js; List<double> bs;
                if (!Evaluate(cl, cls, true, out js, out bs)) { Why("evaluate"); continue; }
                // the only motorway crossing must be the one at the interchange
                if (bs.Count != 1 || Math.Abs(bs[0] - _sP) > 10) { Why("motorway-crossings:" + bs.Count); continue; }
                Commit(cls, "interchange-spine", cl, A, null, js, bs);
                var spine = _roads[_roads.Count - 1];
                spine.ZoneA = 0; spine.ZoneB = 0;
                MakeRamps(spine, u);
                Log("interchange: spine attempt " + attempt + ", length " + cl.Length.ToString("0", CultureInfo.InvariantCulture) + " m");
                return;
            }
            throw new InvalidOperationException("interchange: no valid spine in 160 attempts: " + string.Join("; ", why.Select(kv => kv.Key + " x" + kv.Value)));
        }

        void MakeRamps(Road spine, V2 u)
        {
            // station of P on the spine
            double best = double.MaxValue, sSp = 0;
            for (int i = 0; i < spine.C.P.Length; i++)
            {
                double d = V2.Dist(spine.C.P[i], _pInter);
                if (d < best) { best = d; sSp = spine.C.S[i]; }
            }
            spine.ZoneA = sSp - 250; spine.ZoneB = spine.C.Length; // roads may not cross the interchange part of the spine
            int ramp = 0;
            foreach (int su in new[] { -1, 1 })
            {
                double sGore = _sP + su * GoreD;
                int gore = NodeOn(_motorway, sGore);
                _g.Nodes[gore].InterchangeKind = "diamond";
                V2 g = _motorway.C.At(sGore);
                foreach (int sv in new[] { -1, 1 })   // -1 toward the city (spine station decreases), +1 away
                {
                    double e = su < 0 ? RampEBefore : RampEAfter;
                    double sR = sSp + sv * e;
                    V2 q = spine.C.At(sR);
                    V2 uq = u * (1.0 / 1.0);
                    V2 b = q + uq * (su * RampLead);
                    var cr = _rules.Of(RoadClass.Link);
                    int idx = ramp++;
                    var rr = Stage("ramp/" + idx);
                    Centreline cl; string err;
                    if (!AlignmentBuilder.Build(new[] { g, b, q }, new[] { false, true, false }, RoadClass.Link, _rules, () => cr.CurveRadius.Present ? cr.CurveRadius.Sample(rr) : 120, out cl, out err))
                        throw new InvalidOperationException("interchange ramp " + idx + ": " + err);
                    int rn = NodeOn(spine, sR);
                    var road = AddRoad(RoadClass.Link, "interchange-ramp", cl, gore, rn, null);
                    road.NoCross = true;
                }
            }
        }

        // ---------------------------------------------------------------- stages 5/6: primary and secondary

        void ConnectPrimary()
        {
            int nv = _set.Count - 1;
            if (nv < 1) return;
            int k = Math.Max(1, nv - Math.Max(1, nv / 4));
            var city = _set[0];
            var prim = new List<Settlement> { city };
            var vil = _set.Skip(1).OrderBy(s => V2.Dist(s.C, city.C)).ToList();
            for (int i = 0; i < k; i++) prim.Add(vil[i]);
            // Prim's MST over the primary set
            var inTree = new List<int> { 0 };
            var pairs = new List<Tuple<int, int>>();
            while (inTree.Count < prim.Count)
            {
                double best = double.MaxValue; int bi = -1, bj = -1;
                foreach (int i in inTree)
                    for (int j = 0; j < prim.Count; j++)
                        if (!inTree.Contains(j))
                        {
                            double d = V2.Dist(prim[i].C, prim[j].C);
                            if (d < best) { best = d; bi = i; bj = j; }
                        }
                inTree.Add(bj); pairs.Add(Tuple.Create(bi, bj));
            }
            // RG-118: when a tree pair cannot be routed (seed 9: village2-village4), the village is joined to the nearest other connected primary settlement instead
            var made = new List<Tuple<int, int>>();
            var linked = new List<int> { 0 };
            foreach (var p in pairs)
            {
                bool ok = Connect(RoadClass.Primary, prim[p.Item1], prim[p.Item2], "primary-" + prim[p.Item1].Name + "-" + prim[p.Item2].Name);
                int from = p.Item1;
                if (!ok)
                {
                    foreach (int m in linked.Where(m => m != p.Item1).OrderBy(m => V2.Dist(prim[m].C, prim[p.Item2].C)).ThenBy(m => m))
                        if (Connect(RoadClass.Primary, prim[m], prim[p.Item2], "primary-" + prim[m].Name + "-" + prim[p.Item2].Name)) { ok = true; from = m; break; }
                }
                if (ok) { made.Add(Tuple.Create(from, p.Item2)); linked.Add(p.Item2); }
                else { Log("WARNING: " + prim[p.Item2].Name + " could not be joined to the primary network"); _res.Unconnected++; }
            }
            // loop edges: shortest non-tree pairs
            var extra = new List<Tuple<double, int, int>>();
            for (int i = 0; i < prim.Count; i++)
                for (int j = i + 1; j < prim.Count; j++)
                    if (!made.Any(p => (p.Item1 == i && p.Item2 == j) || (p.Item1 == j && p.Item2 == i)))
                        extra.Add(Tuple.Create(V2.Dist(prim[i].C, prim[j].C), i, j));
            extra.Sort((p, q) => p.Item1.CompareTo(q.Item1));
            int loops = Math.Min(extra.Count, 1 + Stage("loops").Int(2));
            for (int i = 0; i < loops; i++)
                Connect(RoadClass.Primary, prim[extra[i].Item2], prim[extra[i].Item3], "primary-loop-" + prim[extra[i].Item2].Name + "-" + prim[extra[i].Item3].Name);   // a loop is optional: no route is only logged
            _primarySet = prim;
        }

        List<Settlement> _primarySet;

        void ConnectSecondary()
        {
            var done = new List<Settlement>(_primarySet ?? new List<Settlement> { _set[0] });
            var rest = _set.Where(s => !done.Contains(s)).OrderBy(s => V2.Dist(s.C, _set[0].C)).ToList();
            foreach (var v in rest)
            {
                var order = done.OrderBy(s => V2.Dist(s.C, v.C)).ToList();
                bool ok = false;
                foreach (var target in order)
                {
                    int before = _res.Unconnected;
                    for (int attempt = 0; attempt < 100 && !ok; attempt++) ok = TryRoute(RoadClass.Secondary, v, target, attempt, "secondary-" + v.Name + "-" + target.Name);
                    if (ok) { Log("secondary-" + v.Name + "-" + target.Name + ": connected"); break; }
                }
                if (!ok) { Log("WARNING: secondary road to " + v.Name + " could not be routed"); _res.Unconnected++; }
                done.Add(v);
            }
        }

        // ---------------------------------------------------------------- stage 7: assembly

        double MotorwayY(double s)
        {
            double y = 0;
            foreach (var b in _bridges)
            {
                double d = s < b.Key ? b.Key - s : (s > b.Value ? s - b.Value : 0);
                double f = d >= RampLen ? 0 : (1 - d / RampLen);
                f = f * f * (3 - 2 * f);
                y = Math.Max(y, DeckHeight * f);
            }
            return y;
        }

        bool InBridge(double s0, double s1)
        {
            double m = 0.5 * (s0 + s1);
            foreach (var b in _bridges) if (m > b.Key && m < b.Value) return true;
            return false;
        }

        void PlanBridges()
        {
            var rng = Stage("bridges");
            var cr = _rules.Of(RoadClass.Motorway);
            var spans = new List<KeyValuePair<double, double>>();
            foreach (double s in _mxCross.Distinct().OrderBy(x => x))
            {
                double len = Math.Max(80.0, Math.Min(160.0, cr.BridgeLength.Present ? cr.BridgeLength.Sample(rng) : 80.0));
                spans.Add(new KeyValuePair<double, double>(s - len / 2, s + len / 2));
            }
            var merged = new List<KeyValuePair<double, double>>();
            foreach (var sp in spans)
            {
                if (merged.Count > 0 && sp.Key - merged[merged.Count - 1].Value < 2 * RampLen + 40)
                    merged[merged.Count - 1] = new KeyValuePair<double, double>(merged[merged.Count - 1].Key, sp.Value);
                else merged.Add(sp);
            }
            _bridges = merged;
            foreach (var b in _bridges)
            {
                NodeOn(_motorway, b.Key); NodeOn(_motorway, b.Value);
            }
        }

        void Assemble()
        {
            using (new Prof.Scope("Asm.PlanBridges")) PlanBridges();
            foreach (var st in _set) _terrain.AddSettlement(st.C, st.Radius);
            var motorNodes = new HashSet<int>();
            var portalNodes = new HashSet<int>();
            foreach (var b in _bridges) { portalNodes.Add(NodeOn(_motorway, b.Key)); portalNodes.Add(NodeOn(_motorway, b.Value)); }
            if (_terrain.Enabled) AssembleTerrain(portalNodes);
            else foreach (var r in _roads)
                {
                    r.Cuts.Sort((p, q) => p.S.CompareTo(q.S));
                    for (int i = 0; i + 1 < r.Cuts.Count; i++)
                    {
                        var a = r.Cuts[i]; var b = r.Cuts[i + 1];
                        if (b.S - a.S < 1.0) throw new InvalidOperationException("road " + r.Tag + ": cuts closer than 1 m at station " + a.S);
                        bool motor = r == _motorway;
                        int n = Math.Max(1, (int)Math.Round((b.S - a.S) / _rules.StepM));
                        var pts = new V2[n + 1]; var h = new double[n + 1]; var k = new double[n + 1];
                        for (int j = 0; j <= n; j++)
                        {
                            double s = a.S + (b.S - a.S) * j / n;
                            pts[j] = r.C.At(s); k[j] = r.C.KAt(s); h[j] = motor ? MotorwayY(s) : 0;
                        }
                        // exact node positions
                        pts[0] = _g.Nodes[a.Node].P; pts[n] = _g.Nodes[b.Node].P;
                        bool bridge = motor && InBridge(a.S, b.S);
                        var e = _g.AddEdge(a.Node, b.Node, r.Class, pts, h, k, bridge ? 1 : 0, bridge ? "bridge" : "ground");
                        if (bridge)
                        {
                            _g.Spans.Add(new GSpan
                            {
                                Id = _g.Spans.Count,
                                Kind = "bridge",
                                Class = r.Class,
                                StartNode = a.Node,
                                EndNode = b.Node,
                                LengthM = e.LengthM,
                                ClearanceM = DeckHeight,
                                Layer = 1,
                            });
                            _g.Spans[_g.Spans.Count - 1].Edges.Add(e.Id);
                        }
                        if (motor)
                        {
                            _g.Nodes[a.Node].Y = MotorwayY(a.S); _g.Nodes[b.Node].Y = MotorwayY(b.S);
                        }
                    }
                }
            foreach (int n in portalNodes) _g.Nodes[n].Layer = 1;
            // node kinds from degree, spans and flags
            var deg = _g.Degrees();
            var inc = _g.Incident();
            foreach (var n in _g.Nodes)
            {
                int d = deg[n.Id];
                if (d == 1) n.Kind = NodeKind.End;
                else if (d >= 3) n.Kind = n.InterchangeKind != null ? NodeKind.Interchange : NodeKind.Junction;
                else
                {
                    var e0 = _g.Edges[inc[n.Id][0]]; var e1 = _g.Edges[inc[n.Id][1]];
                    n.Kind = e0.Span != e1.Span ? NodeKind.Portal : NodeKind.Joint;
                }
            }
            Log("graph: " + _g.Nodes.Count + " nodes, " + _g.Edges.Count + " edges, " + _g.Spans.Count + " bridge spans");
        }
    }
}
