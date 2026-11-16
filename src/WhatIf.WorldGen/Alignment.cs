using System;
using System.Collections.Generic;

namespace WhatIf.WorldGen
{
    /// <summary>Dense centreline (spacing at most DenseStepM) with analytic curvature and station; edges are cut from it at the final resample.</summary>
    public sealed class Centreline
    {
        public V2[] P;
        public double[] K;
        public double[] S;
        public double Length { get { return S[S.Length - 1]; } }

        public Centreline(V2[] p, double[] k)
        {
            P = p; K = k;
            S = new double[p.Length];
            for (int i = 1; i < p.Length; i++) S[i] = S[i - 1] + V2.Dist(p[i], p[i - 1]);
        }

        int Seg(double s)
        {
            int lo = 0, hi = S.Length - 2;
            if (s <= 0) return 0;
            if (s >= Length) return hi;
            while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (S[mid] <= s) lo = mid; else hi = mid - 1; }
            return lo;
        }

        public V2 At(double s)
        {
            int i = Seg(s);
            double d = S[i + 1] - S[i];
            double t = d < 1e-12 ? 0 : Math.Min(1, Math.Max(0, (s - S[i]) / d));
            return P[i] + (P[i + 1] - P[i]) * t;
        }

        public double KAt(double s)
        {
            int i = Seg(s);
            double d = S[i + 1] - S[i];
            double t = d < 1e-12 ? 0 : Math.Min(1, Math.Max(0, (s - S[i]) / d));
            return K[i] + (K[i + 1] - K[i]) * t;
        }

        /// <summary>Unit tangent at station s (central difference over about 2 m).</summary>
        public V2 Tangent(double s)
        {
            return (At(Math.Min(Length, s + 1.0)) - At(Math.Max(0, s - 1.0))).Unit;
        }

        /// <summary>Samples [s0,s1] into n+1 evenly spaced points with n = round((s1-s0)/stepM), at least 1.</summary>
        public void Resample(double s0, double s1, double stepM, out V2[] pts, out double[] k)
        {
            int n = Math.Max(1, (int)Math.Round((s1 - s0) / stepM));
            pts = new V2[n + 1];
            k = new double[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double s = s0 + (s1 - s0) * i / n;
                pts[i] = At(s);
                k[i] = KAt(s);
            }
        }
    }

    public static class Clothoid
    {
        /// <summary>Position and heading of a clothoid with curvature k(s) = s/(R*Ls) at arc length s, start on the origin along +x, left turn.
        /// Numeric Fresnel integral (composite Simpson, at most 0.25 m per panel), deterministic.</summary>
        public static void Point(double ls, double r, double s, out double x, out double y, out double theta)
        {
            double a = 1.0 / (2.0 * r * ls);
            theta = a * s * s;
            int n = Math.Max(4, 2 * (int)Math.Ceiling(s / 0.5));
            double h = s / n, sx = 0, sy = 0;
            for (int i = 0; i <= n; i++)
            {
                double u = i * h, w = (i == 0 || i == n) ? 1 : (i % 2 == 1 ? 4 : 2);
                double t = a * u * u;
                sx += w * Math.Cos(t); sy += w * Math.Sin(t);
            }
            x = sx * h / 3.0; y = sy * h / 3.0;
        }

        /// <summary>Shift p and tangent offset k of a spiral-arc-spiral with spiral length ls and radius r (exact from the Fresnel end point).</summary>
        public static void ShiftAndOffset(double ls, double r, out double p, out double k)
        {
            double x, y, th;
            Point(ls, r, ls, out x, out y, out th);
            p = y - r * (1 - Math.Cos(th));
            k = x - r * Math.Sin(th);
        }
    }

    /// <summary>Line - clothoid - arc - clothoid centreline through a waypoint polyline (PIs).</summary>
    public static class AlignmentBuilder
    {
        public const double DenseStepM = 2.0;
        const double MinDeflection = 0.006; // rad, below this the PI is a straight

        sealed class Turn { public int I; public double Delta, R, Ls, T; }

        /// <summary>Builds the centreline. fixedPi marks PIs that must not be dropped (stubs, interchange). cls fixes minimum radius and design speed;
        /// drawR gives the preferred radius for each curve (clamped up to the class minimum and to the 3 s arc rule). Returns false with err set when the
        /// polyline cannot be fitted (tangents longer than segments); the caller retries with other waypoints.</summary>
        public static bool Build(V2[] pis, bool[] fixedPi, string cls, WorldRules rules, Func<double> drawR, out Centreline cl, out string err)
        {
            cl = null; err = null;
            var pts = new List<V2>(pis);
            var fx = new List<bool>(fixedPi);
            var drawn = new List<double>();
            for (int i = 0; i < pts.Count; i++) drawn.Add(drawR());
            double v = RoadClass.DesignSpeedMs(cls), rmin = rules.Of(cls).MinRadiusM;

            while (true)
            {
                int n = pts.Count;
                if (n < 2) { err = "fewer than two waypoints"; return false; }
                var lens = new double[n - 1];
                var heads = new double[n - 1];
                for (int i = 0; i < n - 1; i++)
                {
                    lens[i] = V2.Dist(pts[i], pts[i + 1]);
                    if (lens[i] < 1e-6) { err = "coincident waypoints"; return false; }
                    heads[i] = (pts[i + 1] - pts[i]).Heading;
                }
                List<Turn> turns = null;
                int bad = -1;
                for (int level = 0; level < 3 && (level == 0 || bad >= 0); level++)
                {
                    turns = new List<Turn>();
                    for (int i = 1; i < n - 1; i++)
                    {
                        double d = V2.AngDiff(heads[i], heads[i - 1]);
                        if (Math.Abs(d) < MinDeflection) continue;
                        if (Math.Abs(d) > 2.6) { err = "deflection over 150 degrees at waypoint " + i; return false; }
                        double ls = 1.5 * v;
                        if (level == 2) ls = Math.Max(10.0, ls * 0.5);
                        double rlow = Math.Max(rmin, (rules.ArcSeconds * v + ls) / Math.Abs(d));
                        double r = level == 0 ? Math.Max(drawn[i], rlow) : rlow;
                        double p, k;
                        Clothoid.ShiftAndOffset(ls, r, out p, out k);
                        turns.Add(new Turn { I = i, Delta = d, R = r, Ls = ls, T = k + (r + p) * Math.Tan(Math.Abs(d) / 2) });
                    }
                    bad = FirstViolation(turns, lens, rules.EndStraightM);
                }
                if (bad < 0) { cl = Assemble(pts, heads, turns); return true; }

                // still not fitting: drop the non-fixed PI of the violated segment with the smaller deflection
                int drop = -1; double best = double.MaxValue;
                foreach (var t in turns)
                    if ((t.I == bad || t.I == bad + 1) && !fx[t.I] && Math.Abs(t.Delta) < best) { best = Math.Abs(t.Delta); drop = t.I; }
                if (drop < 0) { err = "tangents do not fit segment " + bad + " (length " + lens[bad].ToString("0.#") + " m) and no PI may be dropped"; return false; }
                pts.RemoveAt(drop); fx.RemoveAt(drop); drawn.RemoveAt(drop);
            }
        }

        static int FirstViolation(List<Turn> turns, double[] lens, double endStraight)
        {
            var t = new double[lens.Length + 1];
            foreach (var x in turns) t[x.I] = x.T;
            for (int j = 0; j < lens.Length; j++)
            {
                double need = t[j] + t[j + 1] + (j == 0 ? endStraight : 0) + (j == lens.Length - 1 ? endStraight : 0) + 0.01;
                if (need > lens[j]) return j;
            }
            return -1;
        }

        static Centreline Assemble(List<V2> pts, double[] heads, List<Turn> turns)
        {
            var P = new List<V2>();
            var K = new List<double>();
            V2 cur = pts[0];
            P.Add(cur); K.Add(0);
            foreach (var t in turns)
            {
                V2 pi = pts[t.I];
                V2 uin = V2.FromHeading(heads[t.I - 1]), uout = V2.FromHeading(heads[t.I]);
                AddLine(P, K, cur, pi - uin * t.T);
                // first half: spiral then half the arc, local to the tangent point
                double sg = t.Delta > 0 ? 1.0 : -1.0;
                double h0 = heads[t.I - 1];
                V2 t0 = pi - uin * t.T;
                var half = new List<V2>(); var halfK = new List<double>();
                int ns = Math.Max(2, (int)Math.Ceiling(t.Ls / DenseStepM));
                for (int i = 1; i <= ns; i++)
                {
                    double s = t.Ls * i / ns, x, y, th;
                    Clothoid.Point(t.Ls, t.R, s, out x, out y, out th);
                    half.Add(t0 + new V2(x, sg * y).Rot(h0)); halfK.Add(sg * s / (t.R * t.Ls));
                }
                double ths = t.Ls / (2 * t.R);
                double hs = h0 + sg * ths;
                V2 s1 = half[half.Count - 1];
                V2 c = s1 + new V2(-Math.Sin(hs), Math.Cos(hs)) * (sg * t.R);
                double phi = Math.Abs(t.Delta) - t.Ls / t.R;
                int na = Math.Max(1, (int)Math.Ceiling(0.5 * phi * t.R / DenseStepM));
                for (int i = 1; i <= na; i++)
                {
                    double a = 0.5 * phi * i / na;
                    double h = hs + sg * a;
                    half.Add(c - new V2(-Math.Sin(h), Math.Cos(h)) * (sg * t.R)); halfK.Add(sg / t.R);
                }
                V2 d = (uout - uin).Unit;
                foreach (var q in half) { P.Add(q); }
                foreach (var kk in halfK) K.Add(kk);
                for (int i = half.Count - 2; i >= 0; i--)
                {
                    V2 w = half[i] - pi;
                    P.Add(pi + d * (2 * V2.Dot(d, w)) - w);
                    K.Add(halfK[i]);
                }
                V2 t1 = pi + uout * t.T;
                P.Add(t1); K.Add(0);
                cur = t1;
            }
            AddLine(P, K, cur, pts[pts.Count - 1]);
            // remove zero-length duplicates
            var P2 = new List<V2>(); var K2 = new List<double>();
            for (int i = 0; i < P.Count; i++)
                if (P2.Count == 0 || V2.Dist(P[i], P2[P2.Count - 1]) > 1e-6) { P2.Add(P[i]); K2.Add(K[i]); }
            return new Centreline(P2.ToArray(), K2.ToArray());
        }

        static void AddLine(List<V2> P, List<double> K, V2 a, V2 b)
        {
            double len = V2.Dist(a, b);
            if (len < 1e-6) return;
            int n = Math.Max(1, (int)Math.Ceiling(len / DenseStepM));
            for (int i = 1; i <= n; i++) { P.Add(a + (b - a) * ((double)i / n)); K.Add(0); }
        }

        /// <summary>Tangent length of a spiral-arc-spiral for deflection delta (test helper, same formula as Build).</summary>
        public static double TangentLength(double delta, double r, double ls)
        {
            double p, k;
            Clothoid.ShiftAndOffset(ls, r, out p, out k);
            return k + (r + p) * Math.Tan(Math.Abs(delta) / 2);
        }
    }
}
