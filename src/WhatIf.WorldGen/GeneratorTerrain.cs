using System;
using System.Collections.Generic;
using System.Linq;

namespace WhatIf.WorldGen
{
    /// <summary>W5a (RG-118): road vertical profiles, tunnels and viaducts. Runs only when the spec has a terrain block with relief; otherwise the W3.1 flat assembly is used unchanged.</summary>
    public sealed partial class Generator
    {
        // design constants (metres)
        const double TunnelCore = 12.0, TunnelFlank = 7.5, TunnelRefit = 11.5, LiningM = 7.0;
        const double ViaductCore = 6.0, ViaductFlank = 2.0, ViaductRefit = 5.5, ViaductMinM = 30.0;
        const double GradeUse = 0.9, JunctionGrade = 0.03, JunctionZoneM = 15.0, DeckThicknessAndClearance = 5.3, CrossingZoneM = 15.0;
        const int RefitPasses = 8;
        const double BasinCut = 4.0, BasinFill = 3.0;
        const double EntranceSlackM = 8.0, EntranceSlackRunM = 150.0;   // RG-182
        // vertical curves (RG-123): relaxation of the curvature projection, tolerance of the stop test and of the infeasible count
        const int VcOuter = 30000, VcSweeps = 20000;
        const long VcWorkBudget = 600000000L;   // station updates per vertical-curve call: bounds the worst case when the bounds contradict the limit (seed 8 ran > 10 min without it)
        const double VcRelax = 1.95, VcStopTol = 1.01, VcCountTol = 1.02, VcMargin = 1.03, VcRelaxZoneM = 600.0, BasinFillRelaxed = 5.9, BasinCutRelaxed = 11.5;

        TerrainModel _terrain;

        sealed class SpanRun { public int I0, I1; public bool Tunnel, Cross; }

        sealed class RoadSamples
        {
            public Road R;
            public int M;
            public double[] S, K, Y, Nat;
            public V2[] P;
            public List<int> CutIdx = new List<int>(), CutNode = new List<int>();
            public List<SpanRun> Runs = new List<SpanRun>();
            public List<int[]> CrossRanges = new List<int[]>();   // sample ranges of the motorway's crossing bridges (deck over another road)
            public List<int[]> Plateaus = new List<int[]>();      // RG-130: sample ranges of a minor leg's level plateau at a junction (grade 0)
        }

        /// <summary>RG-130: a minor leg at a junction (degree &gt;= 3) holds node height from the node to PlateauPastCutM past the adapter's cut line (widest half width at the
        /// node + 2 m). Inside a sibling leg's road rectangle the surface is at node height, so a minor road already falling there left a 5-7 cm lip at the rectangle edge.
        /// The two major legs (best class, then the pair closest to 180 deg) keep their profile, as on the measured maps (geometry-patterns: the major road keeps its
        /// grade through the junction, the minor one adapts). Nodes with a motorway leg are left to the RG-128 ramp pin. Returns the plateau length per (cut, direction).</summary>
        const double PlateauPastCutM = 1.0;

        void PlateauLegs(Dictionary<Cut, double> fwd, Dictionary<Cut, double> back)
        {
            var legs = new Dictionary<int, List<Tuple<Cut, int, string, double>>>();   // node -> (cut, direction along the road, class, plan angle away from the node)
            foreach (var r in _roads)
            {
                r.Cuts.Sort((p, q) => p.S.CompareTo(q.S));
                for (int i = 0; i < r.Cuts.Count; i++)
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        if (i + dir < 0 || i + dir >= r.Cuts.Count) continue;
                        Cut c = r.Cuts[i]; V2 p = _g.Nodes[c.Node].P, q = r.C.At(c.S + dir * Math.Min(2.0, Math.Abs(r.Cuts[i + dir].S - c.S) * 0.5));
                        List<Tuple<Cut, int, string, double>> l;
                        if (!legs.TryGetValue(c.Node, out l)) legs[c.Node] = l = new List<Tuple<Cut, int, string, double>>();
                        l.Add(Tuple.Create(c, dir, r.Class, Math.Atan2(q.Z - p.Z, q.X - p.X)));
                    }
            }
            foreach (var l in legs.Values)
            {
                if (l.Count < 3 || l.Any(x => x.Item3 == RoadClass.Motorway)) continue;
                int bi = -1, bj = -1; double best = double.MaxValue;
                for (int i = 0; i < l.Count; i++)
                    for (int j = i + 1; j < l.Count; j++)
                    {
                        double d = Math.Abs(l[i].Item4 - l[j].Item4) % (2 * Math.PI); d = Math.Min(d, 2 * Math.PI - d);
                        double key = Math.Max(ClassOrder(l[i].Item3), ClassOrder(l[j].Item3)) * 10.0 + (Math.PI - d);
                        if (key < best) { best = key; bi = i; bj = j; }
                    }
                double reach = l.Max(x => RoadClass.Width(x.Item3)) * 0.5 + 2.0 + PlateauPastCutM;
                for (int k = 0; k < l.Count; k++)
                    if (k != bi && k != bj) (l[k].Item2 > 0 ? fwd : back)[l[k].Item1] = reach;
            }
        }

        static int ClassOrder(string c)
        {
            switch (c) { case RoadClass.Motorway: return 0; case RoadClass.Primary: return 1; case RoadClass.Link: return 2; case RoadClass.Secondary: return 3; case RoadClass.Local: return 4; default: return 5; }
        }

        static double SmoothWindow(string c)
        {
            switch (c) { case RoadClass.Motorway: return 400; case RoadClass.Link: return 150; case RoadClass.Primary: return 200; case RoadClass.Secondary: return 120; default: return 60; }
        }

        static bool SpansAllowed(string c) { return c == RoadClass.Motorway || c == RoadClass.Link || c == RoadClass.Primary || c == RoadClass.Secondary; }

        int _plateauLegs, _plateauSkipped;

        RoadSamples BuildSamples(Road r, Dictionary<Cut, double> fwd, Dictionary<Cut, double> back)
        {
            r.Cuts.Sort((p, q) => p.S.CompareTo(q.S));
            var rs = new RoadSamples { R = r };
            var s = new List<double>(); var pts = new List<V2>(); var k = new List<double>();
            for (int i = 0; i < r.Cuts.Count; i++)
            {
                if (i == 0)
                {
                    s.Add(r.Cuts[0].S); pts.Add(r.C.At(r.Cuts[0].S)); k.Add(r.C.KAt(r.Cuts[0].S));
                }
                else
                {
                    var a = r.Cuts[i - 1]; var b = r.Cuts[i];
                    if (b.S - a.S < 1.0) throw new InvalidOperationException("road " + r.Tag + ": cuts closer than 1 m at station " + a.S);
                    int n = Math.Max(1, (int)Math.Round((b.S - a.S) / _rules.StepM));
                    double pa, pb; fwd.TryGetValue(a, out pa); back.TryGetValue(b, out pb);
                    if (pa + pb > 0 && pa + pb + 2.0 > b.S - a.S) { _plateauSkipped += (pa > 0 ? 1 : 0) + (pb > 0 ? 1 : 0); pa = pb = 0; }   // edge too short for its plateaus
                    var st = new List<double>();
                    for (int j = 1; j <= n; j++) st.Add(a.S + (b.S - a.S) * j / n);
                    // the plateau end is a station of its own (stations are ~10 m apart); a regular station closer than 1 m to it is dropped
                    foreach (double pe in new[] { pa > 0 ? a.S + pa : double.NaN, pb > 0 ? b.S - pb : double.NaN })
                        if (!double.IsNaN(pe)) { st.RemoveAll(v => v < b.S - 1e-9 && Math.Abs(v - pe) < 1.0); st.Add(pe); _plateauLegs++; }
                    st.Sort();
                    int first = s.Count - 1;
                    foreach (double v in st)
                    {
                        if (pa > 0 && v == a.S + pa) rs.Plateaus.Add(new[] { first, s.Count });
                        if (pb > 0 && v == b.S - pb) rs.Plateaus.Add(new[] { s.Count, first + st.Count });
                        s.Add(v); pts.Add(r.C.At(v)); k.Add(r.C.KAt(v));
                    }
                }
                rs.CutIdx.Add(s.Count - 1); rs.CutNode.Add(r.Cuts[i].Node);
                pts[s.Count - 1] = _g.Nodes[r.Cuts[i].Node].P;
            }
            rs.M = s.Count; rs.S = s.ToArray(); rs.P = pts.ToArray(); rs.K = k.ToArray();
            rs.Nat = new double[rs.M]; rs.Y = new double[rs.M];
            for (int i = 0; i < rs.M; i++) rs.Nat[i] = _terrain.Natural(rs.P[i].X, rs.P[i].Z);
            return rs;
        }

        static double[] SmoothValues(double[] s, double[] v, double win)
        {
            int m = s.Length;
            var o = new double[m];
            var w = new double[m];
            for (int i = 0; i < m; i++)
            {
                double a = i > 0 ? s[i - 1] : s[i], b = i + 1 < m ? s[i + 1] : s[i];
                w[i] = Math.Max(0.5, (b - a) * 0.5);
            }
            double half = win * 0.5;
            for (int i = 0; i < m; i++)
            {
                double sum = 0, ws = 0;
                for (int j = i; j >= 0 && s[i] - s[j] <= half; j--) { sum += w[j] * v[j]; ws += w[j]; }
                for (int j = i + 1; j < m && s[j] - s[i] <= half; j++) { sum += w[j] * v[j]; ws += w[j]; }
                o[i] = sum / ws;
            }
            return o;
        }

        /// <summary>Closest profile to target with |dy| &lt;= g * ds per segment and lo &lt;= y &lt;= hi. Bounds are propagated along the chain first, so the two clamping sweeps
        /// stay inside them; where the bounds contradict each other the upper bound wins. Returns the number of contradicting samples.</summary>
        static int SolveProfile(double[] s, double[] target, double[] lo, double[] hi, double[] g, double[] y)
        {
            int m = s.Length;
            return SolveProfile(s, target, lo, hi, g, y, new double[m], new double[m]);
        }

        /// <summary>RG-173: the same solve with caller-owned scratch arrays lo2 / hi2 (length >= s.Length): VerticalCurves calls it thousands of times per road, the two clones per call were 99 % of a region build's allocation.</summary>
        static int SolveProfile(double[] s, double[] target, double[] lo, double[] hi, double[] g, double[] y, double[] lo2, double[] hi2)
        {
            int m = s.Length;
            Array.Copy(lo, lo2, m); Array.Copy(hi, hi2, m);
            for (int k = 1; k < m; k++)
            {
                double d = g[k - 1] * (s[k] - s[k - 1]);
                lo2[k] = Math.Max(lo2[k], lo2[k - 1] - d); hi2[k] = Math.Min(hi2[k], hi2[k - 1] + d);
            }
            for (int k = m - 2; k >= 0; k--)
            {
                double d = g[k] * (s[k + 1] - s[k]);
                lo2[k] = Math.Max(lo2[k], lo2[k + 1] - d); hi2[k] = Math.Min(hi2[k], hi2[k + 1] + d);
            }
            int bad = 0;
            for (int k = 0; k < m; k++)
            {
                if (lo2[k] > hi2[k] + 1e-9) bad++;
                y[k] = Math.Min(Math.Max(target[k], lo2[k]), hi2[k]);
            }
            for (int k = 1; k < m; k++)
            {
                double d = g[k - 1] * (s[k] - s[k - 1]);
                double v = Math.Min(Math.Max(y[k], y[k - 1] - d), y[k - 1] + d);
                y[k] = Math.Min(Math.Max(v, lo2[k]), hi2[k]);
            }
            for (int k = m - 2; k >= 0; k--)
            {
                double d = g[k] * (s[k + 1] - s[k]);
                double v = Math.Min(Math.Max(y[k], y[k + 1] - d), y[k + 1] + d);
                y[k] = Math.Min(Math.Max(v, lo2[k]), hi2[k]);
            }
            return bad;
        }

        /// <summary>Vertical-curve pass (RG-123, W5c). The bounded solve leaves sharp grade breaks; the curvature of every station is limited to 1 / R_min (crest or sag
        /// by sign, spec vcurve_scale). Alternating projections: (a) per violating station the Euclidean projection onto its curvature half-space over the free
        /// neighbours (a grade break spread symmetrically = a parabolic vertical curve, pinned stations never move), (b) the bounded solve again (lo/hi, pins, grade cap)
        /// with the rounded profile as its target. Stops when no station exceeds the limit by more than 0.2 %. Returns the number of stations still more than 2 %
        /// beyond the limit after the last bounded solve (the bounds and the curve limit contradict each other there); y is always a profile that honours every bound.</summary>
        int VerticalCurves(RoadSamples rs, double[] target, double[] lo, double[] hi, double[] g, bool[] pinned, double[] y)
        {
            int m = rs.M;
            if (m < 3) return 0;
            double scale = Math.Max(1e-6, _spec.VCurveScale);
            double kc = 1.0 / (WorldRules.VRadiusMin(rs.R.Class, true) * scale), ks = 1.0 / (WorldRules.VRadiusMin(rs.R.Class, false) * scale);
            var s = Chord(rs.P);   // horizontal distance between the stations, as the validator measures it (a cut node may sit off the arc length)
            return VerticalCurvesCore(s, lo, hi, g, pinned, y, kc, ks, _spec.VcStallOuter);
        }

        /// <summary>RG-177: the station loop of VerticalCurves, unchanged, as a static over the chord distances s and the curvature limits (kc crest, ks sag, 1/m).</summary>
        static int VerticalCurvesCore(double[] s, double[] lo, double[] hi, double[] g, bool[] pinned, double[] y, double kc, double ks, int stallN)
        {
            int m = s.Length;
            if (Violations(s, y, kc, ks, VcStopTol) == 0) return 0;
            var cur = new double[m]; var lo2 = new double[m]; var hi2 = new double[m];
            long work = 0; int bestBad = int.MaxValue, stall = 0;
            for (int outer = 0; outer < VcOuter && work < VcWorkBudget; outer++)
            {
                Array.Copy(y, cur, m);
                for (int sweep = 0; sweep < VcSweeps && work < VcWorkBudget; sweep++)
                {
                    int moved = 0; work += m;
                    for (int i = 1; i + 1 < m; i++)
                    {
                        double a = s[i] - s[i - 1], b = s[i + 1] - s[i];
                        if (a < 1e-6 || b < 1e-6) continue;
                        double h = (a + b) * 0.5;
                        double k = ((cur[i + 1] - cur[i]) / b - (cur[i] - cur[i - 1]) / a) / h, dk;
                        if (k < -kc * VcStopTol) dk = -kc / VcMargin - k; else if (k > ks * VcStopTol) dk = ks / VcMargin - k; else continue;
                        double c0 = pinned[i - 1] ? 0 : 1 / (a * h), c1 = pinned[i] ? 0 : -(1 / a + 1 / b) / h, c2 = pinned[i + 1] ? 0 : 1 / (b * h);
                        double nn = c0 * c0 + c1 * c1 + c2 * c2;
                        if (nn < 1e-18) continue;
                        double f = dk * VcRelax / nn;
                        cur[i - 1] += f * c0; cur[i] += f * c1; cur[i + 1] += f * c2;
                        moved++;
                    }
                    if (moved == 0) break;
                }
                SolveProfile(s, cur, lo, hi, g, y, lo2, hi2);
                if (Violations(s, y, kc, ks, VcStopTol) == 0) break;
                if (stallN > 0)   // planet only (spec vc_stall_outer): the count beyond the limit has not improved for stallN outer rounds -> the bounds contradict the curve, more rounds buy nothing
                {
                    int nb = Violations(s, y, kc, ks, VcCountTol);
                    if (nb < bestBad) { bestBad = nb; stall = 0; } else if (++stall >= stallN) break;
                }
            }
            return Violations(s, y, kc, ks, VcCountTol);
        }

        /// <summary>RG-177 public entry (additive; the Generator path does not call it): the generator's vertical profile of one road piece outside a Generator run.
        /// s = horizontal chord distance of the stations (increasing), target = wanted heights, lo/hi = height bounds (lo = hi pins a station; pinned[i] keeps it fixed
        /// in the curve pass), g = grade cap per interval (length >= s.Length - 1). Step 1 = the bounded solve, step 2 = the vertical-curve pass with the class limits
        /// WorldRules.VRadiusMin(roadClass) * vcurveScale and the planet stall rule stallOuter (WorldSpec.VcStallOuter; 0 = off). y receives the profile. Returns the
        /// number of stations beyond the curve limit by more than VcCountTol (2 %); infeasible = samples whose bounds contradict each other after propagation.</summary>
        public static int SolveVerticalProfile(double[] s, double[] target, double[] lo, double[] hi, double[] g, bool[] pinned, double[] y, string roadClass, double vcurveScale, int stallOuter, out int infeasible)
        {
            infeasible = SolveProfile(s, target, lo, hi, g, y);
            if (s.Length < 3) return 0;
            double scale = Math.Max(1e-6, vcurveScale);
            double kc = 1.0 / (WorldRules.VRadiusMin(roadClass, true) * scale), ks = 1.0 / (WorldRules.VRadiusMin(roadClass, false) * scale);
            return VerticalCurvesCore(s, lo, hi, g, pinned, y, kc, ks, stallOuter);
        }

        /// <summary>RG-177: stations of a profile beyond the class curve limit by more than VcCountTol (the count VerticalCurves returns), for measuring any profile the same way.</summary>
        public static int VerticalCurveViolations(double[] s, double[] y, string roadClass, double vcurveScale)
        {
            double scale = Math.Max(1e-6, vcurveScale);
            return Violations(s, y, 1.0 / (WorldRules.VRadiusMin(roadClass, true) * scale), 1.0 / (WorldRules.VRadiusMin(roadClass, false) * scale), VcCountTol);
        }

        /// <summary>Fallback when a sag or crest cannot be rounded inside a settlement basin (the basin fill/cut limit contradicts the curve): within 600 m of each such station the basin
        /// bounds widen from fill 3 m / cut 4 m to fill 5.9 m / cut 11.5 m (inside the 6 m / 12 m validator limits). Logged by count; pins never move. Returns true when a bound changed.</summary>
        bool RelaxBasinBounds(RoadSamples rs, bool[] pinned, double[] lo, double[] hi, double[] y)
        {
            int m = rs.M; double scale = Math.Max(1e-6, _spec.VCurveScale);
            double kc = 1.0 / (WorldRules.VRadiusMin(rs.R.Class, true) * scale), ks = 1.0 / (WorldRules.VRadiusMin(rs.R.Class, false) * scale);
            var near = new bool[m]; bool any = false; var cs = Chord(rs.P);
            for (int i = 1; i + 1 < m; i++)
            {
                double a = cs[i] - cs[i - 1], b = cs[i + 1] - cs[i];
                if (a < 1e-6 || b < 1e-6) continue;
                double k = ((y[i + 1] - y[i]) / b - (y[i] - y[i - 1]) / a) / ((a + b) * 0.5);
                if (k >= -kc * VcCountTol && k <= ks * VcCountTol) continue;
                for (int j = 0; j < m; j++) if (Math.Abs(cs[j] - cs[i]) <= VcRelaxZoneM) near[j] = true;
            }
            for (int j = 0; j < m; j++)
            {
                if (!near[j] || pinned[j] || !_terrain.InBasin(rs.P[j].X, rs.P[j].Z)) continue;
                if (hi[j] < rs.Nat[j] + BasinFillRelaxed - 1e-9 && hi[j] <= rs.Nat[j] + BasinFill + 1e-9) { hi[j] = rs.Nat[j] + BasinFillRelaxed; any = true; }
                if (lo[j] > rs.Nat[j] - BasinCutRelaxed + 1e-9 && lo[j] >= rs.Nat[j] - BasinCut - 1e-9) { lo[j] = rs.Nat[j] - BasinCutRelaxed; any = true; }
            }
            if (any) Log("terrain: road " + rs.R.Tag + " (id " + rs.R.Id + "): basin fill/cut widened to 5.9 m / 11.5 m within 600 m of a vertical curve that the standard bounds cannot round");
            return any;
        }

        static double[] Chord(V2[] p)
        {
            var c = new double[p.Length];
            for (int i = 1; i < p.Length; i++) c[i] = c[i - 1] + V2.Dist(p[i], p[i - 1]);
            return c;
        }

        static int Violations(double[] s, double[] y, double kc, double ks, double tol)
        {
            int n = 0;
            for (int i = 1; i + 1 < s.Length; i++)
            {
                double a = s[i] - s[i - 1], b = s[i + 1] - s[i];
                if (a < 1e-6 || b < 1e-6) continue;
                double k = ((y[i + 1] - y[i]) / b - (y[i] - y[i - 1]) / a) / ((a + b) * 0.5);
                if (k < -kc * tol || k > ks * tol) n++;
            }
            return n;
        }

        /// <summary>Position of the first station beyond the limit, for the log line.</summary>
        string VcWhere(RoadSamples rs, Road r)
        {
            double scale = Math.Max(1e-6, _spec.VCurveScale);
            double kc = 1.0 / (WorldRules.VRadiusMin(r.Class, true) * scale), ks = 1.0 / (WorldRules.VRadiusMin(r.Class, false) * scale);
            var cw = Chord(rs.P);
            for (int i = 1; i + 1 < rs.M; i++)
            {
                double a = cw[i] - cw[i - 1], b = cw[i + 1] - cw[i];
                if (a < 1e-6 || b < 1e-6) continue;
                double k = ((rs.Y[i + 1] - rs.Y[i]) / b - (rs.Y[i] - rs.Y[i - 1]) / a) / ((a + b) * 0.5);
                if (k < -kc * VcCountTol || k > ks * VcCountTol)
                    return "; first at station " + rs.S[i].ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m (" + rs.P[i].X.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "," + rs.P[i].Z.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "), radius " + (1 / Math.Abs(k)).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m";
            }
            return "";
        }

        /// <summary>Maximal runs of samples with val &gt; flank that contain at least one sample with val &gt; core.</summary>
        static List<int[]> FindRuns(double[] val, double core, double flank)
        {
            var l = new List<int[]>();
            int m = val.Length;
            for (int i = 0; i < m;)
            {
                if (val[i] <= flank) { i++; continue; }
                int j = i; bool hasCore = false;
                while (j < m && val[j] > flank) { if (val[j] > core) hasCore = true; j++; }
                if (hasCore) l.Add(new[] { i, j - 1 });
                i = j;
            }
            return l;
        }

        void AssembleTerrain(HashSet<int> portalNodes)
        {
            var rss = new Dictionary<int, RoadSamples>();
            var fwd = new Dictionary<Cut, double>(); var back = new Dictionary<Cut, double>();
            using (new Prof.Scope("Asm.PlateauLegs")) PlateauLegs(fwd, back);
            using (new Prof.Scope("Asm.BuildSamples")) { foreach (var r in _roads) rss[r.Id] = BuildSamples(r, fwd, back); }
            Log("terrain: " + _plateauLegs + " minor junction leg(s) with a level plateau, " + _plateauSkipped + " skipped (edge shorter than its plateaus + 2 m)");

            var deg = new Dictionary<int, int>();
            foreach (var r in _roads)
                for (int i = 0; i < r.Cuts.Count; i++)
                {
                    int d; deg.TryGetValue(r.Cuts[i].Node, out d);
                    deg[r.Cuts[i].Node] = d + (i == 0 || i == r.Cuts.Count - 1 ? 1 : 2);
                }

            var nodeY = new Dictionary<int, double>();
            foreach (var kv in _pinY) nodeY[kv.Key] = kv.Value;   // RG-182: pinned entrance heights
            SegIndex motorIdx = null; RoadSamples motor = null;
            int infeasible = 0, tunnels = 0, viaducts = 0, unresolved = 0, vcInfeasible = 0;
            foreach (var r in _roads.OrderBy(q => ClassOrder(q.Class)).ThenBy(q => q.Id))
            {
                var rs = rss[r.Id];
                int vbad;
                int bad; using (new Prof.Scope("Asm.ProfileRoad")) bad = ProfileRoad(rs, deg, nodeY, motorIdx, motor, ref unresolved, out vbad);
                infeasible += bad; vcInfeasible += vbad;
                if (vbad > 0) Log("terrain: road " + r.Tag + " (" + r.Class + ", id " + r.Id + ") has " + vbad + " sample(s) below the minimum vertical radius" + VcWhere(rs, r));
                if (bad > 0) Log("terrain: road " + r.Tag + " (" + r.Class + ", id " + r.Id + ") has " + bad + " contradicting bound sample(s)");
                if (r == _motorway)
                {
                    motor = rs;
                    motorIdx = new SegIndex(50);
                    for (int i = 0; i + 1 < rs.M; i++) motorIdx.Add(rs.P[i], rs.P[i + 1], i);
                }
                foreach (var run in rs.Runs) { if (run.Tunnel) tunnels++; else viaducts++; }
            }

            // boundary nodes of the spans, then the edges
            foreach (var r in _roads)
            {
                var rs = rss[r.Id];
                foreach (var run in rs.Runs)
                    foreach (int idx in new[] { run.I0, run.I1 })
                    {
                        if (rs.CutIdx.Contains(idx)) continue;
                        var n = _g.AddNode(rs.P[idx], rs.Y[idx], NodeKind.Joint);
                        nodeY[n.Id] = rs.Y[idx];
                        int at = rs.CutIdx.FindIndex(c => c > idx);
                        rs.CutIdx.Insert(at, idx); rs.CutNode.Insert(at, n.Id);
                    }
            }
            foreach (var r in _roads)
            {
                var rs = rss[r.Id];
                var runOf = new int[rs.M];
                for (int i = 0; i < rs.M; i++) runOf[i] = -1;
                for (int q = 0; q < rs.Runs.Count; q++)
                    for (int i = rs.Runs[q].I0; i < rs.Runs[q].I1; i++) runOf[i] = q;   // segment i..i+1
                var spanOfRun = new Dictionary<int, GSpan>();
                for (int c = 0; c + 1 < rs.CutIdx.Count; c++)
                {
                    int a = rs.CutIdx[c], b = rs.CutIdx[c + 1];
                    int q = runOf[a];
                    for (int i = a; i < b; i++)
                        if (runOf[i] != q) throw new InvalidOperationException("road " + r.Tag + ": span boundary inside an edge at sample " + i);
                    int len = b - a + 1;
                    var pts = new V2[len]; var h = new double[len]; var k = new double[len];
                    for (int i = 0; i < len; i++) { pts[i] = rs.P[a + i]; h[i] = rs.Y[a + i]; k[i] = rs.K[a + i]; }
                    var run = q >= 0 ? rs.Runs[q] : null;
                    string kind = run == null ? "ground" : (run.Tunnel ? "tunnel" : "bridge");
                    var e = _g.AddEdge(rs.CutNode[c], rs.CutNode[c + 1], r.Class, pts, h, k, InCross(rs, a, b) ? 1 : 0, kind);
                    if (run != null)
                    {
                        GSpan sp;
                        if (!spanOfRun.TryGetValue(q, out sp))
                        {
                            sp = new GSpan { Id = _g.Spans.Count, Kind = kind, Class = r.Class, StartNode = rs.CutNode[c], Layer = run.Cross ? 1 : 0, Viaduct = !run.Tunnel && !run.Cross };
                            double metric = double.MaxValue;
                            for (int i = run.I0; i <= run.I1; i++)
                                metric = Math.Min(metric, run.Tunnel ? rs.Nat[i] - rs.Y[i] - LiningM : rs.Y[i] - rs.Nat[i]);
                            if (run.Tunnel) sp.CoverDepthM = metric; else sp.ClearanceM = run.Cross ? DeckHeight : metric;
                            _g.Spans.Add(sp); spanOfRun[q] = sp;
                        }
                        sp.EndNode = rs.CutNode[c + 1]; sp.LengthM += e.LengthM; sp.Edges.Add(e.Id);
                        if (run.Cross) { portalNodes.Add(sp.StartNode); portalNodes.Add(sp.EndNode); }
                    }
                }
            }
            foreach (var kv in nodeY) _g.Nodes[kv.Key].Y = kv.Value;
            Log("terrain: " + tunnels + " tunnel run(s), " + viaducts + " viaduct run(s), " + infeasible + " contradicting bound sample(s), " + unresolved + " unresolved refit run(s), " + vcInfeasible + " vertical-curve infeasible sample(s)");
        }

        /// <summary>Vertical profile of one road. Returns the number of samples whose bounds contradict each other (pins, ceilings and grade limits not reconcilable).</summary>
        int ProfileRoad(RoadSamples rs, Dictionary<int, int> deg, Dictionary<int, double> nodeY, SegIndex motorIdx, RoadSamples motor, ref int unresolved, out int vcBad)
        {
            vcBad = 0;
            var r = rs.R; int m = rs.M;
            var rule = _rules.Of(r.Class);
            double gBase = GradeUse * rule.MaxGradePct / 100.0;
            double[] target; using (new Prof.Scope("PR.Smooth")) target = SmoothValues(rs.S, rs.Nat, SmoothWindow(r.Class));
            bool motorway = r == _motorway;
            if (motorway) for (int i = 0; i < m; i++) target[i] += MotorwayY(rs.S[i]);

            var lo = new double[m]; var hi = new double[m]; var pinned = new bool[m]; var g = new double[Math.Max(0, m - 1)];
            bool allowed = SpansAllowed(r.Class);
            for (int i = 0; i < m; i++)
            {
                lo[i] = allowed ? double.NegativeInfinity : rs.Nat[i] - TunnelRefit;
                hi[i] = allowed ? double.PositiveInfinity : rs.Nat[i] + ViaductRefit;
            }
            // inside a settlement basin every road hugs the natural surface (no tunnel, no viaduct): cut at most BasinCut, fill at most BasinFill
            for (int i = 0; i < m; i++)
                if (_terrain.InBasin(rs.P[i].X, rs.P[i].Z)) { lo[i] = Math.Max(lo[i], rs.Nat[i] - BasinCut); hi[i] = Math.Min(hi[i], rs.Nat[i] + BasinFill); }
            // RG-182: a road that starts at a pinned entrance keeps the pinned (corridor) height although the settlement's own natural surface lies up to a few metres from it:
            // the basin bounds widen over the first EntranceSlackRunM, so the road eases onto the street level within its grade
            if (_pinY.ContainsKey(r.StartNode))
                for (int i = 0; i < m; i++)
                    if (rs.S[i] <= EntranceSlackRunM) { lo[i] = Math.Min(lo[i], rs.Nat[i] - BasinCut - EntranceSlackM); hi[i] = Math.Max(hi[i], rs.Nat[i] + BasinFill + EntranceSlackM); }
            for (int i = 0; i < g.Length; i++) g[i] = gBase;
            for (int c = 0; c < rs.CutIdx.Count; c++)
            {
                int idx = rs.CutIdx[c], node = rs.CutNode[c];
                double ny;
                if (nodeY.TryGetValue(node, out ny)) { lo[idx] = hi[idx] = ny; pinned[idx] = true; }
                int d; deg.TryGetValue(node, out d);
                if (d >= 3)
                    for (int i = 0; i < g.Length; i++)
                        if (Math.Abs(rs.S[i] - rs.S[idx]) <= JunctionZoneM || Math.Abs(rs.S[i + 1] - rs.S[idx]) <= JunctionZoneM) g[i] = Math.Min(g[i], JunctionGrade);
            }
            foreach (var pl in rs.Plateaus) for (int i = pl[0]; i < pl[1]; i++) g[i] = 0;   // RG-130: minor leg level with the node up to past the cut line
            // RG-128: a road that leaves the motorway at a node is pinned to the motorway surface while its footprint overlaps the motorway footprint (ramp at ~27 deg: 30-45 m of overlap, up to 0.98 m between the two ribbons = the "retardér")
            var prRamp = new Prof.Scope("PR.RampPin");
            if (!motorway && motor != null)
            {
                double clear = RoadClass.Width(RoadClass.Motorway) * 0.5 + RoadClass.Width(r.Class) * 0.5 + 1.5 + 0.5;
                for (int c = 0; c < rs.CutIdx.Count; c++)
                {
                    int start = rs.CutIdx[c];
                    if (NearestOnMotor(motor, rs.P[start]) > 0.5) continue;
                    foreach (int dir in new[] { 1, -1 })
                        for (int i = start + dir; i >= 0 && i < m; i += dir)
                        {
                            double my; double dist = NearestOnMotor(motor, rs.P[i], out my);
                            if (dist >= clear || rs.CutIdx.Contains(i)) break;
                            lo[i] = hi[i] = my; pinned[i] = true;
                            int seg = dir > 0 ? i - 1 : i;
                            g[seg] = gBase;
                            int next = dir > 0 ? i : i - 1;
                            if (next >= 0 && next < g.Length) g[next] = gBase;
                        }
                }
            }
            prRamp.Dispose(); var prCross = new Prof.Scope("PR.Crossing");
            // crossing under a motorway bridge: the lower road stays 5 m + deck below
            if (!motorway && motor != null)
            {
                var hits = new List<int>();
                for (int i = 0; i + 1 < m; i++)
                {
                    motorIdx.Query(rs.P[i], rs.P[i + 1], hits);
                    foreach (int h in hits)
                    {
                        double t, u;
                        if (!Geo.SegCross(rs.P[i], rs.P[i + 1], motor.P[h], motor.P[h + 1], out t, out u, true)) continue;   // half-open: a hit on a sample vertex counts once
                        double deck = motor.Y[h] + (motor.Y[h + 1] - motor.Y[h]) * u;
                        V2 x = rs.P[i] + (rs.P[i + 1] - rs.P[i]) * t;
                        bool atNode = false;
                        foreach (int cn in rs.CutNode) if (V2.Dist(_g.Nodes[cn].P, x) < 0.5) atNode = true;
                        if (atNode) continue;   // a ramp that starts on the motorway is a junction, not a crossing
                        for (int j = Math.Max(0, i - 3); j <= Math.Min(m - 1, i + 4); j++)
                            if (j == i || j == i + 1 || V2.Dist(rs.P[j], x) <= CrossingZoneM)
                                hi[j] = Math.Min(hi[j], deck - DeckThicknessAndClearance);
                    }
                }
            }

            prCross.Dispose();
            if (motorway)
                foreach (var br in _bridges)
                {
                    int i0 = -1, i1 = -1;
                    for (int c = 0; c < rs.CutIdx.Count; c++)
                    {
                        if (Math.Abs(r.Cuts[c].S - br.Key) < 1.0) i0 = rs.CutIdx[c];
                        if (Math.Abs(r.Cuts[c].S - br.Value) < 1.0) i1 = rs.CutIdx[c];
                    }
                    if (i0 >= 0 && i1 > i0) rs.CrossRanges.Add(new[] { i0, i1 });
                }

            int bad = 0;
            List<SpanRun> runs = new List<SpanRun>();
            var y = rs.Y;
            var d2 = new double[m];
            for (int pass = 0; pass <= RefitPasses; pass++)
            {
                using (new Prof.Scope("PR.Solve")) bad = SolveProfile(rs.S, target, lo, hi, g, y);
                using (new Prof.Scope("PR.VCurves")) vcBad = VerticalCurves(rs, target, lo, hi, g, pinned, y);
                bool relaxed; using (new Prof.Scope("PR.Relax")) relaxed = vcBad > 0 && RelaxBasinBounds(rs, pinned, lo, hi, y);
                if (relaxed)
                {
                    using (new Prof.Scope("PR.Solve")) bad = SolveProfile(rs.S, target, lo, hi, g, y);
                    using (new Prof.Scope("PR.VCurves")) vcBad = VerticalCurves(rs, target, lo, hi, g, pinned, y);
                }
                runs.Clear();
                bool changed = false;
                if (allowed)
                {
                    for (int i = 0; i < m; i++) d2[i] = rs.Nat[i] - y[i];     // depth below the natural surface
                    foreach (var rr in FindRuns(d2, TunnelCore, TunnelFlank))
                    {
                        bool ok = rs.S[rr[1]] - rs.S[rr[0]] >= _rules.TunnelMin(r.Class) && !HasInteriorJunction(rs, rr, deg) && !StraddlesCrossBridge(rs, rr);
                        if (ok) runs.Add(new SpanRun { I0 = rr[0], I1 = rr[1], Tunnel = true });
                        else if (pass < RefitPasses)
                        {
                            for (int i = rr[0]; i <= rr[1]; i++) if (!pinned[i]) lo[i] = Math.Max(lo[i], rs.Nat[i] - TunnelRefit);
                            changed = true;
                        }
                        else unresolved++;
                    }
                    for (int i = 0; i < m; i++) d2[i] = y[i] - rs.Nat[i];      // height above the natural surface
                    double vmin = Math.Max(ViaductMinM, _rules.BridgeMin(r.Class));
                    foreach (var rr in FindRuns(d2, ViaductCore, ViaductFlank))
                    {
                        bool ok = rs.S[rr[1]] - rs.S[rr[0]] >= vmin && !HasInteriorJunction(rs, rr, deg);
                        if (ok) runs.Add(new SpanRun { I0 = rr[0], I1 = rr[1], Tunnel = false });
                        else if (pass < RefitPasses)
                        {
                            for (int i = rr[0]; i <= rr[1]; i++) if (!pinned[i]) hi[i] = Math.Min(hi[i], rs.Nat[i] + ViaductRefit);
                            changed = true;
                        }
                        else unresolved++;
                    }
                }
                if (!changed) break;
            }

            // the crossing bridges of the motorway (W3) join the viaduct runs; overlapping or touching bridge runs merge into one span.
            // A crossing inside a tunnel run stays part of the tunnel (two tunnels crossing); only its edges get layer 1.
            foreach (var cr in rs.CrossRanges)
            {
                bool inTunnel = false;
                foreach (var t in runs) if (t.Tunnel && t.I0 <= cr[0] && cr[1] <= t.I1) inTunnel = true;
                if (!inTunnel) runs.Add(new SpanRun { I0 = cr[0], I1 = cr[1], Tunnel = false, Cross = true });
            }
            rs.Runs = MergeBridgeRuns(runs);
            for (int c = 0; c < rs.CutIdx.Count; c++)
                if (!nodeY.ContainsKey(rs.CutNode[c])) nodeY[rs.CutNode[c]] = y[rs.CutIdx[c]];
            return bad;
        }

        static double NearestOnMotor(RoadSamples motor, V2 p) { double y; return NearestOnMotor(motor, p, out y); }

        static double NearestOnMotor(RoadSamples motor, V2 p, out double y)
        {
            double best = double.MaxValue; y = 0;
            for (int i = 0; i + 1 < motor.M; i++)
            {
                V2 a = motor.P[i], b = motor.P[i + 1];
                double dx = b.X - a.X, dz = b.Z - a.Z, l2 = dx * dx + dz * dz;
                double t = l2 > 1e-9 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / l2)) : 0;
                double ex = a.X + dx * t - p.X, ez = a.Z + dz * t - p.Z, d = ex * ex + ez * ez;
                if (d < best) { best = d; y = motor.Y[i] + (motor.Y[i + 1] - motor.Y[i]) * t; }
            }
            return Math.Sqrt(best);
        }

        static bool InCross(RoadSamples rs, int a, int b)
        {
            foreach (var cr in rs.CrossRanges) if (cr[0] <= a && b <= cr[1]) return true;
            return false;
        }

        static List<SpanRun> MergeBridgeRuns(List<SpanRun> runs)
        {
            var tun = runs.Where(q => q.Tunnel).OrderBy(q => q.I0).ToList();
            var br = runs.Where(q => !q.Tunnel).OrderBy(q => q.I0).ToList();
            var merged = new List<SpanRun>();
            foreach (var q in br)
            {
                if (merged.Count > 0 && q.I0 <= merged[merged.Count - 1].I1)
                {
                    var p = merged[merged.Count - 1];
                    p.I1 = Math.Max(p.I1, q.I1); p.Cross |= q.Cross;
                }
                else merged.Add(new SpanRun { I0 = q.I0, I1 = q.I1, Cross = q.Cross });
            }
            // a tunnel never shares a segment with a bridge: the bridge loses the overlapping samples (cannot happen for valid runs, kept as a guard)
            var all = new List<SpanRun>(tun);
            foreach (var b in merged)
            {
                bool clash = false;
                foreach (var t in tun) if (b.I0 < t.I1 && t.I0 < b.I1) clash = true;
                if (!clash) all.Add(b);
            }
            all.Sort((p, q) => p.I0.CompareTo(q.I0));
            return all;
        }

        bool HasInteriorJunction(RoadSamples rs, int[] run, Dictionary<int, int> deg)
        {
            for (int c = 0; c < rs.CutIdx.Count; c++)
            {
                int idx = rs.CutIdx[c];
                if (idx <= run[0] || idx >= run[1]) continue;
                int d; deg.TryGetValue(rs.CutNode[c], out d);
                if (d >= 3) return true;
            }
            return false;
        }

        /// <summary>True when a crossing bridge sticks out of the tunnel run (a portal would sit on the deck). A bridge fully inside the run is fine.</summary>
        static bool StraddlesCrossBridge(RoadSamples rs, int[] run)
        {
            foreach (var cr in rs.CrossRanges)
                if (cr[0] < run[1] && run[0] < cr[1] && !(run[0] <= cr[0] && cr[1] <= run[1])) return true;
            return false;
        }
    }
}
