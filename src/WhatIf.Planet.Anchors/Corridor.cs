using System;
using System.Collections.Generic;
using System.Diagnostics;
using WhatIf.Planet;
using WhatIf.Planet.Surface;
using Generator = WhatIf.WorldGen.Generator;
using WorldRules = WhatIf.WorldGen.WorldRules;

namespace WhatIf.Planet.Anchors
{
    /// <summary>Local gnomonic chart at a centre direction (the RegionPlane expression, any centre): x along E, z along N, metres; a great circle is a straight line.</summary>
    public sealed class Chart
    {
        public readonly Vec3 C, E, N;
        public Chart(Vec3 centre)
        {
            C = centre.Normalized(); CubeSphere.ToFaceAB(C, out int f, out _, out _);
            CubeSphere.TangentAxes(f, C, out E, out N);
        }
        public void ToLocal(Vec3 d, out double x, out double z) { double k = CubeSphere.Radius / Vec3.Dot(d, C); x = Vec3.Dot(d, E) * k; z = Vec3.Dot(d, N) * k; }
        public Vec3 ToDir(double x, double z) => (C * CubeSphere.Radius + E * x + N * z).Normalized();
    }

    /// <summary>One 2-5 km piece of a corridor with its own vertical profile (stations I0..I1 of the corridor, both joints included).</summary>
    public sealed class Segment
    {
        public int Index, I0, I1; public double[] Y; public double Seconds, MaxGrade; public int BeyondCurve, Infeasible;
        public double LengthM;
    }

    /// <summary>
    /// A routed corridor between two settlements (pure function of the link and the seed): coarse A* on a chart lattice, string pulling, fillets, 10 m stations.
    /// Routed = false when water or slope leaves no path (the spike forbids bridges); then there is no road.
    /// </summary>
    public sealed class Corridor
    {
        public Link Link; public Chart Chart; public bool Routed; public string Why = "";
        public double RouteSeconds, LengthM, MinRadiusM = double.PositiveInfinity, MeasuredRadiusM = double.PositiveInfinity, MeanderScale; public double[] S; public bool Legacy; public int Expanded, Vertices;
        public double[] X, Z, Nat; public bool[] OnWater; public double Ds;
        public int TrimStart, TrimEnd;   // RG-182: stations removed at each end (inside the settlement radius); the first / last remaining station is the entrance
        public int[] Joints;   // station index of every joint, 0 and last included
        public double[] PinH, PinG;   // pinned height and grade at each joint
        // routing domain (chart): origin at A, u along A->B, v across; for the 600 m tests
        public double Ux, Uz, Lu0, Lu1, Lv;
        /// <summary>Chart distance from a chart point to the routing domain rectangle (0 inside).</summary>
        public double DomainDist(double x, double z)
        {
            double ax, az; Chart.ToLocal(Link.A.Dir, out ax, out az);
            double dx = x - ax, dz = z - az, u = dx * Ux + dz * Uz, v = -dx * Uz + dz * Ux;
            double du = Math.Max(0, Math.Max(Lu0 - u, u - Lu1)), dv = Math.Max(0, Math.Abs(v) - Lv);
            return Math.Sqrt(du * du + dv * dv);
        }
    }

    /// <summary>RG-177 routing and segment profiles. One instance per thread (shares the SettlementField's surface).</summary>
    public sealed class CorridorBuilder
    {
        public const double GridM = 200, MarginM = 1500, MinHalfWidthM = 2000, HalfWidthShare = 0.3, StationM = 10, PullSampleM = 40, PullDevM = 8, LegacyPullDevM = 20;
        // RG-181 meander: deterministic lateral sine (seeded by the link key) on the pulled + filleted line, lobe by lobe only where the ground beside the line is dry and as high as the line
        public const double MeanderMaxM = 120, MeanderShare = 0.05, MeanderMinM = 800, MeanderWaveMinM = 1800, MeanderWaveSpanM = 1400, MeanderDevM = 6, MeanderTaperM = 500, RadiusTol = 0.98;
        static readonly double[] MeanderScales = { 1.0, 0.6, 0.3, 0.0 };
        // RG-181 feasible joints: joint height = ground smoothed over +-JointSmoothM, kept within half the cut / fill bounds and within JointGradeUse x the class grade of its neighbours
        public const double JointSmoothM = 500, JointBoundShare = 0.5, JointGradeUse = 0.8;
        public const double SegmentMaxM = 5000, CutM = 25, FillM = 15, GradeUse = 0.9, JointGradeShare = 0.5, DesignRadiusFactor = 1.5;
        public const int PullLookahead = 30, StallOuter = 5;   // RG-181: 300 -> 5 outer rounds without improvement (one round of a 5 km segment ~ 45 ms; a contradicting segment ground on for 2.7 s cold)
        public static readonly WorldRules Rules = WorldRules.Synthetic();
        readonly PlanetSurface surf;
        public CorridorBuilder(PlanetSurface surface) { surf = surface; }
        /// <summary>The RG-177/178 corridor (20 m pull tolerance, no meander, joints pinned to the raw surface): only for the before column of the RG-181 measurement.</summary>
        public bool Legacy;
        double PullDev => Legacy ? LegacyPullDevM : PullDevM;

        static bool Water(SurfaceKind k) => k == SurfaceKind.Ocean || k == SurfaceKind.Sea || k == SurfaceKind.Lake;
        public static double MaxGrade(string cls) => Rules.Of(cls).MaxGradePct / 100.0;
        public static double MinRadius(string cls) => Rules.Of(cls).MinRadiusM;

        /// <summary>The corridor shell without routing: chart at the midpoint, axis A->B and the routing domain rectangle (for the 600 m tests).</summary>
        public Corridor Domain(Link link) => Domain(link, out _, out _, out _, out _, out _, out _, out _);

        Corridor Domain(Link link, out double L, out int n, out double hu, out double hv, out int mu, out int mv, out double[] ab)
        {
            var c = new Corridor { Link = link, Chart = new Chart(link.A.Dir + link.B.Dir), Legacy = Legacy };
            c.Chart.ToLocal(link.A.Dir, out double ax, out double az); c.Chart.ToLocal(link.B.Dir, out double bx, out double bz);
            L = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            c.Ux = (bx - ax) / L; c.Uz = (bz - az) / L;
            n = Math.Max(1, (int)Math.Round(L / GridM)); hu = L / n; hv = GridM;
            mu = (int)Math.Ceiling(MarginM / hu); mv = (int)Math.Ceiling(Math.Max(MinHalfWidthM, HalfWidthShare * L) / hv);
            c.Lu0 = -mu * hu; c.Lu1 = (n + mu) * hu; c.Lv = mv * hv;
            ab = new[] { ax, az, bx, bz };
            return c;
        }

        public Corridor Route(Link link)
        {
            var sw = Stopwatch.StartNew();
            var c = Domain(link, out double L, out int n, out double hu, out double hv, out int mu, out int mv, out var ab);
            double ax = ab[0], az = ab[1], bx = ab[2], bz = ab[3];
            string cls = link.RoadClass; double g = MaxGrade(cls);
            int nu = n + 1 + 2 * mu, nv = 2 * mv + 1, N = nu * nv;
            var h = new double[N]; var st = new byte[N];   // 0 unknown, 1 land, 2 water
            void P(int idx, out double x, out double z) { int i = idx / nv, j = idx % nv; double u = (i - mu) * hu, v = (j - mv) * hv; x = ax + u * c.Ux - v * c.Uz; z = az + u * c.Uz + v * c.Ux; }
            bool Land(int idx)
            {
                if (st[idx] == 0) { P(idx, out double x, out double z); h[idx] = Ground.Height(surf, c.Chart.ToDir(x, z), out var k); st[idx] = Water(k) ? (byte)2 : (byte)1; }
                return st[idx] == 1;
            }
            int s0 = mu * nv + mv, s1 = (mu + n) * nv + mv;
            var gs = new double[N]; var par = new int[N]; var closed = new bool[N];
            for (int i = 0; i < N; i++) { gs[i] = double.PositiveInfinity; par[i] = -1; }
            var heap = new MinHeap(); gs[s0] = 0; Land(s0); Land(s1); heap.Push(Heur(s0), s0);
            double Heur(int idx) { int i = idx / nv, j = idx % nv; double du = (i - mu - n) * hu, dv = (j - mv) * hv; return Math.Sqrt(du * du + dv * dv); }
            while (heap.Count > 0)
            {
                heap.Pop(out _, out int cur);
                if (closed[cur]) continue;
                closed[cur] = true; c.Expanded++;
                if (cur == s1) break;
                int ci = cur / nv, cj = cur % nv;
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int i = ci + di, j = cj + dj; if (i < 0 || j < 0 || i >= nu || j >= nv) continue;
                        int nb = i * nv + j; if (closed[nb] || !Land(nb)) continue;
                        double len = Math.Sqrt(di * di * hu * hu + dj * dj * hv * hv), gr = Math.Abs(h[nb] - h[cur]) / len;
                        if (gr > 2 * g) continue;
                        double ex = Math.Max(0, gr / g - 0.5), ng = gs[cur] + len * (1 + 4 * ex * ex);
                        if (ng < gs[nb]) { gs[nb] = ng; par[nb] = cur; heap.Push(ng + Heur(nb), nb); }
                    }
            }
            if (!closed[s1]) { c.Routed = false; c.Why = st[s1] == 2 ? "end on water" : "no land path within the domain (water or grade > 2 x class max)"; c.RouteSeconds = sw.Elapsed.TotalSeconds; return c; }
            var path = new List<int>(); for (int q = s1; q >= 0; q = par[q]) path.Add(q); path.Reverse();
            var px = new List<double>(); var pz = new List<double>(); var ph = new List<double>();
            foreach (int q in path) { P(q, out double x, out double z); px.Add(x); pz.Add(z); ph.Add(h[q]); }
            px[0] = ax; pz[0] = az; px[px.Count - 1] = bx; pz[pz.Count - 1] = bz;
            Pull(c.Chart, px, pz, ph, g, out var vx, out var vz);
            c.Vertices = vx.Count;
            Stations(c, vx, vz, MinRadius(cls) * DesignRadiusFactor);
            c.Routed = true; c.RouteSeconds = sw.Elapsed.TotalSeconds;
            return c;
        }

        /// <summary>String pulling: from vertex i the farthest j (&lt;= i + PullLookahead) whose straight chord is dry every 40 m, climbs at most the class grade and stays within 20 m of the linear height between the ends.</summary>
        void Pull(Chart ch, List<double> px, List<double> pz, List<double> ph, double g, out List<double> vx, out List<double> vz)
        {
            vx = new List<double> { px[0] }; vz = new List<double> { pz[0] };
            int i = 0, last = px.Count - 1;
            while (i < last)
            {
                int best = i + 1;
                for (int j = Math.Min(last, i + PullLookahead); j > i + 1; j--) if (Straight(ch, px[i], pz[i], ph[i], px[j], pz[j], ph[j], g)) { best = j; break; }
                vx.Add(px[best]); vz.Add(pz[best]); i = best;
            }
        }

        bool Straight(Chart ch, double x0, double z0, double h0, double x1, double z1, double h1, double g)
        {
            double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            if (Math.Abs(h1 - h0) > g * len) return false;
            int m = Math.Max(2, (int)Math.Ceiling(len / PullSampleM));
            for (int k = 1; k < m; k++)
            {
                double t = (double)k / m, hh = Ground.Height(surf, ch.ToDir(x0 + (x1 - x0) * t, z0 + (z1 - z0) * t), out var kind);
                if (Water(kind) || Math.Abs(hh - (h0 + (h1 - h0) * t)) > PullDev) return false;
            }
            return true;
        }

        /// <summary>Fillets every interior vertex with the design radius (shrunk so the two tangent lengths fit their legs: half a shared leg, an end leg up to 30 m from the end), samples the result at ~10 m stations, and the surface along it.</summary>
        void Stations(Corridor c, List<double> vx, List<double> vz, double rDesign)
        {
            int nv = vx.Count; var ox = new List<double> { vx[0] }; var oz = new List<double> { vz[0] };
            double Leg(int a) => Math.Sqrt((vx[a + 1] - vx[a]) * (vx[a + 1] - vx[a]) + (vz[a + 1] - vz[a]) * (vz[a + 1] - vz[a]));
            for (int i = 1; i + 1 < nv; i++)
            {
                double l1 = Leg(i - 1), l2 = Leg(i);
                double d1x = (vx[i] - vx[i - 1]) / l1, d1z = (vz[i] - vz[i - 1]) / l1, d2x = (vx[i + 1] - vx[i]) / l2, d2z = (vz[i + 1] - vz[i]) / l2;
                double cr = d1x * d2z - d1z * d2x, dt = d1x * d2x + d1z * d2z, delta = Math.Atan2(Math.Abs(cr), dt);
                if (delta < 1e-6) { ox.Add(vx[i]); oz.Add(vz[i]); continue; }
                double cap = Math.Min(i == 1 ? l1 - 30 : 0.5 * l1, i + 2 == nv ? l2 - 30 : 0.5 * l2), tn = Math.Tan(delta / 2);
                double r = Math.Max(1, Math.Min(rDesign, cap / tn)), t = r * tn;
                c.MinRadiusM = Math.Min(c.MinRadiusM, r);
                double t1x = vx[i] - d1x * t, t1z = vz[i] - d1z * t, sg = cr >= 0 ? 1 : -1, nx = -d1z * sg, nz = d1x * sg;
                double cx = t1x + nx * r, cz = t1z + nz * r, a0 = Math.Atan2(t1z - cz, t1x - cx);
                int m = Math.Max(1, (int)Math.Ceiling(r * delta / 2.0));
                for (int k = 0; k <= m; k++) { double a = a0 + sg * delta * k / m; ox.Add(cx + r * Math.Cos(a)); oz.Add(cz + r * Math.Sin(a)); }
            }
            ox.Add(vx[nv - 1]); oz.Add(vz[nv - 1]);
            // resample by chord at equal steps of ~StationM
            var cum = new double[ox.Count];
            for (int i = 1; i < ox.Count; i++) cum[i] = cum[i - 1] + Math.Sqrt((ox[i] - ox[i - 1]) * (ox[i] - ox[i - 1]) + (oz[i] - oz[i - 1]) * (oz[i] - oz[i - 1]));
            double L = cum[ox.Count - 1]; int ns = Math.Max(2, (int)Math.Round(L / StationM) + 1);
            double ds0 = L / (ns - 1);
            var bx = new double[ns]; var bz = new double[ns];
            for (int k = 0, seg = 0; k < ns; k++)
            {
                double s = k == ns - 1 ? L : k * ds0;
                while (seg + 2 < ox.Count && cum[seg + 1] < s) seg++;
                double span = cum[seg + 1] - cum[seg], t = span > 0 ? (s - cum[seg]) / span : 0;
                bx[k] = ox[seg] + (ox[seg + 1] - ox[seg]) * t; bz[k] = oz[seg] + (oz[seg + 1] - oz[seg]) * t;
            }
            double rMin = MinRadius(c.Link.RoadClass);
            // RG-181: the largest meander scale whose line stays dry and keeps the class minimum radius (measured on the stations); scale 0 = the pulled line
            foreach (double sc in Legacy ? new[] { 0.0 } : MeanderScales)
            {
                var mx = bx; var mz = bz;
                if (sc > 0 && L < MeanderMinM) continue;
                if (sc > 0) Meander(c, bx, bz, ds0, L, sc, out mx, out mz);
                FillStations(c, mx, mz);
                c.MeanderScale = sc;
                if (sc == 0) break;
                bool wet = false; for (int k = 0; k < ns && !wet; k++) wet = c.OnWater[k];
                if (!wet && c.MeasuredRadiusM >= rMin * RadiusTol) break;
            }
            if (c.X == null) FillStations(c, bx, bz);
            if (Legacy) { c.Nat[0] = c.Link.A.HeightM; c.Nat[c.X.Length - 1] = c.Link.B.HeightM; }
            else
                TrimToEntrances(c);   // RG-182: the corridor ends where it crosses the settlement radius; the settlement world's entrance road takes over inside
            Joints(c);
        }

        /// <summary>RG-182 entrance share limit: a trim never removes more than this share of the stations from one end (two settlements closer than their radii).</summary>
        const double TrimMaxShare = 0.4;

        /// <summary>Cuts the stations to the part outside both settlement radii (first station at distance >= radius from A, last at >= radius from B). The end heights are the surface
        /// heights at the entrance stations (the settlement world pins its entrance roads to them).</summary>
        static void TrimToEntrances(Corridor c)
        {
            int ns = c.X.Length;
            c.Chart.ToLocal(c.Link.A.Dir, out double ax, out double az); c.Chart.ToLocal(c.Link.B.Dir, out double bx, out double bz);
            double ra = c.Link.A.RadiusM, rb = c.Link.B.RadiusM;
            int cap = (int)(TrimMaxShare * (ns - 1)), k0 = 0, k1 = ns - 1;
            while (k0 < cap && Math.Sqrt((c.X[k0] - ax) * (c.X[k0] - ax) + (c.Z[k0] - az) * (c.Z[k0] - az)) < ra) k0++;
            while (ns - 1 - k1 < cap && Math.Sqrt((c.X[k1] - bx) * (c.X[k1] - bx) + (c.Z[k1] - bz) * (c.Z[k1] - bz)) < rb) k1--;
            c.TrimStart = k0; c.TrimEnd = ns - 1 - k1;
            int m = k1 - k0 + 1;
            var x = new double[m]; var z = new double[m]; var nat = new double[m]; var wet = new bool[m]; var s = new double[m];
            for (int k = 0; k < m; k++)
            {
                x[k] = c.X[k0 + k]; z[k] = c.Z[k0 + k]; nat[k] = c.Nat[k0 + k]; wet[k] = c.OnWater[k0 + k];
                if (k > 0) s[k] = s[k - 1] + Math.Sqrt((x[k] - x[k - 1]) * (x[k] - x[k - 1]) + (z[k] - z[k - 1]) * (z[k] - z[k - 1]));
            }
            c.X = x; c.Z = z; c.Nat = nat; c.OnWater = wet; c.S = s; c.LengthM = s[m - 1]; c.Ds = c.LengthM / (m - 1);
        }

        /// <summary>Stations from positions: ground height and water per station, cumulative chord length, length and the measured horizontal radius (circle through stations i-3, i, i+3 = 30 m chords).</summary>
        void FillStations(Corridor c, double[] x, double[] z)
        {
            int ns = x.Length; c.X = x; c.Z = z; c.Nat = new double[ns]; c.OnWater = new bool[ns]; c.S = new double[ns];
            for (int k = 0; k < ns; k++)
            {
                if (k > 0) c.S[k] = c.S[k - 1] + Math.Sqrt((x[k] - x[k - 1]) * (x[k] - x[k - 1]) + (z[k] - z[k - 1]) * (z[k] - z[k - 1]));
                c.Nat[k] = Ground.Height(surf, c.Chart.ToDir(x[k], z[k]), out var kind); c.OnWater[k] = Water(kind);
            }
            c.LengthM = c.S[ns - 1]; c.Ds = c.LengthM / (ns - 1);
            c.MeasuredRadiusM = double.PositiveInfinity;
            for (int k = 3; k + 3 < ns; k++)
            {
                double ax = x[k - 3], az = z[k - 3], bx = x[k], bz = z[k], cx = x[k + 3], cz = z[k + 3];
                double ab = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)), bc = Math.Sqrt((cx - bx) * (cx - bx) + (cz - bz) * (cz - bz)), ca = Math.Sqrt((ax - cx) * (ax - cx) + (az - cz) * (az - cz));
                double area2 = Math.Abs((bx - ax) * (cz - az) - (bz - az) * (cx - ax));
                if (area2 > 1e-9) c.MeasuredRadiusM = Math.Min(c.MeasuredRadiusM, ab * bc * ca / (2 * area2));
            }
        }

        static double Smooth(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

        /// <summary>
        /// Deterministic meander (seeded by the link key only): lateral offset d(s) = A * taper(s) * f(s) * sin(w s + phi) from the line's normal. Wavelength 1.8-3.2 km, amplitude
        /// min(120 m, 5 % of the length) x scale, zero over the first / last 500 m (entrances untouched). f is chosen per half-wave lobe from {1, .6, .3, 0}: the largest value whose
        /// offset points (every 40 m) are dry and within MeanderDevM of the height of the line point, so the road bends where the terrain is flat and stays straight on slopes.
        /// f is blended smoothstep between lobe centres (no kinks).
        /// </summary>
        void Meander(Corridor c, double[] bx, double[] bz, double ds, double L, double scale, out double[] mx, out double[] mz)
        {
            int ns = bx.Length; mx = new double[ns]; mz = new double[ns];
            ulong st = c.Link.Key ^ 0x51A0D3B2C4E6F871UL;
            double lam = Math.Min(MeanderWaveMinM + MeanderWaveSpanM * SplitMix64.NextDouble(ref st), L / 1.5), w = 2 * Math.PI / lam, phi = 2 * Math.PI * SplitMix64.NextDouble(ref st);
            double amp = Math.Min(MeanderMaxM, MeanderShare * L) * scale;
            var nx = new double[ns]; var nz = new double[ns];
            for (int k = 0; k < ns; k++)
            {
                int a = Math.Max(0, k - 1), b = Math.Min(ns - 1, k + 1); double tx = bx[b] - bx[a], tz = bz[b] - bz[a], tl = Math.Sqrt(tx * tx + tz * tz);
                nx[k] = tl > 0 ? -tz / tl : 0; nz[k] = tl > 0 ? tx / tl : 0;
            }
            double Taper(int k) { double s = k * ds; return Smooth(Math.Min(s, L - s) / MeanderTaperM); }
            int lobe0 = (int)Math.Floor(phi / Math.PI), lobes = (int)Math.Floor((w * L + phi) / Math.PI) - lobe0 + 1;
            var fl = new double[lobes + 1];   // lobe index relative to lobe0
            var hBase = new double[ns]; var haveBase = new bool[ns];
            double Hb(int k) { if (!haveBase[k]) { hBase[k] = Ground.Height(surf, c.Chart.ToDir(bx[k], bz[k]), out _); haveBase[k] = true; } return hBase[k]; }
            int step = Math.Max(1, (int)Math.Round(PullSampleM / ds));
            for (int q = 0; q < fl.Length; q++)
            {
                double s0 = ((lobe0 + q) * Math.PI - phi) / w, s1 = ((lobe0 + q + 1) * Math.PI - phi) / w;
                int k0 = Math.Max(0, (int)Math.Ceiling(s0 / ds)), k1 = Math.Min(ns - 1, (int)Math.Floor(s1 / ds));
                fl[q] = 0;
                foreach (double f in MeanderScales)
                {
                    if (f == 0) break;
                    bool ok = true;
                    for (int k = k0; k <= k1 && ok; k += step)
                    {
                        double d = amp * Taper(k) * f * Math.Sin(w * k * ds + phi); if (Math.Abs(d) < 0.5) continue;
                        double hh = Ground.Height(surf, c.Chart.ToDir(bx[k] + nx[k] * d, bz[k] + nz[k] * d), out var kind);
                        ok = !Water(kind) && Math.Abs(hh - Hb(k)) <= MeanderDevM;
                    }
                    if (ok) { fl[q] = f; break; }
                }
            }
            for (int k = 0; k < ns; k++)
            {
                double s = k * ds, u = (w * s + phi) / Math.PI - lobe0, q = Math.Floor(u); int qi = Math.Min((int)q, fl.Length - 1);
                double t = u - q, fa = fl[qi], f2;   // blend between the centres of lobe qi and its neighbour
                if (t < 0.5) { double fp = qi > 0 ? fl[qi - 1] : fa; f2 = fp + (fa - fp) * Smooth(t + 0.5); }
                else { double fn = qi + 1 < fl.Length ? fl[qi + 1] : fa; f2 = fa + (fn - fa) * Smooth(t - 0.5); }
                double d = amp * Taper(k) * f2 * Math.Sin(w * s + phi);
                mx[k] = bx[k] + nx[k] * d; mz[k] = bz[k] + nz[k] * d;
            }
            mx[0] = bx[0]; mz[0] = bz[0]; mx[ns - 1] = bx[ns - 1]; mz[ns - 1] = bz[ns - 1];
        }

        /// <summary>
        /// Joints: ceil(L / 5 km) equal pieces. RG-181 feasible joints: height = ground smoothed over +-500 m, kept within half the cut / fill bounds of the raw surface, then limited
        /// to 0.8 x class grade x distance to its neighbours (forward, backward; the entrances stay at their heights); grade = 0 at a local extreme of the joint heights (a crest / sag
        /// must not be forced to a slope), otherwise the smaller of the two neighbouring slopes, clamped to half the class grade. Legacy = RG-177: surface height and surface slope +-30 m.
        /// </summary>
        void Joints(Corridor c)
        {
            int ns = c.X.Length; double L = c.LengthM;
            int nseg = Math.Max(1, (int)Math.Ceiling(L / SegmentMaxM));
            c.Joints = new int[nseg + 1]; c.PinH = new double[nseg + 1]; c.PinG = new double[nseg + 1];
            double g = MaxGrade(c.Link.RoadClass), gmax = g * JointGradeShare;
            for (int k = 0; k <= nseg; k++) c.Joints[k] = (int)Math.Round((double)k * (ns - 1) / nseg);
            if (Legacy)
            {
                for (int k = 0; k <= nseg; k++)
                {
                    int j = c.Joints[k]; c.PinH[k] = c.Nat[j];
                    if (k == 0 || k == nseg) continue;
                    int a = Math.Max(0, j - 3), b = Math.Min(ns - 1, j + 3);
                    c.PinG[k] = Math.Max(-gmax, Math.Min(gmax, (c.Nat[b] - c.Nat[a]) / ((b - a) * c.Ds)));
                }
                return;
            }
            PinJoints(c, nseg, g, gmax);
            if (Fix == StallFix.Slide && nseg > 1 && SlideJoints(c, nseg, g)) PinJoints(c, nseg, g, gmax);
        }

        /// <summary>Candidate (b) settings: a joint whose ground slope (over +-JointSlopeM) differs from its pinned grade by more than JointSlideTol x the class grade slides by up to
        /// JointSlideM to the nearest station where the difference is within the tolerance (neighbour segments stay &lt;= SegmentMaxM); then heights and grades are pinned again.</summary>
        public const double JointSlideM = 600, JointSlideTol = 0.25, JointSlopeM = 300;

        static double GroundSlope(Corridor c, int j)
        {
            int ns = c.X.Length, h = Math.Max(1, (int)Math.Round(JointSlopeM / c.Ds)), a = Math.Max(0, j - h), b = Math.Min(ns - 1, j + h);
            return (c.Nat[b] - c.Nat[a]) / (c.S[b] - c.S[a]);
        }

        static bool SlideJoints(Corridor c, int nseg, double g)
        {
            int n = Math.Max(1, (int)Math.Round(JointSlideM / c.Ds)); double tol = JointSlideTol * g; bool moved = false;
            for (int k = 1; k < nseg; k++)
            {
                int j0 = c.Joints[k];
                if (Math.Abs(GroundSlope(c, j0) - c.PinG[k]) <= tol) continue;
                for (int d = 1; d <= n; d++)
                {
                    int best = -1;
                    foreach (int j in new[] { j0 - d, j0 + d })
                    {
                        if (j <= c.Joints[k - 1] + 1 || j >= c.Joints[k + 1] - 1) continue;
                        if (c.S[j] - c.S[c.Joints[k - 1]] > SegmentMaxM || c.S[c.Joints[k + 1]] - c.S[j] > SegmentMaxM) continue;
                        if (Math.Abs(GroundSlope(c, j) - c.PinG[k]) <= tol) { best = j; break; }
                    }
                    if (best >= 0) { c.Joints[k] = best; moved = true; break; }
                }
            }
            return moved;
        }

        void PinJoints(Corridor c, int nseg, double g, double gmax)
        {
            int ns = c.X.Length;
            int hw = Math.Max(1, (int)Math.Round(JointSmoothM / c.Ds));
            for (int k = 0; k <= nseg; k++)
            {
                int j = c.Joints[k];
                if (k == 0 || k == nseg) { c.PinH[k] = c.Nat[j]; continue; }
                double sum = 0; int cnt = 0; for (int i = Math.Max(0, j - hw); i <= Math.Min(ns - 1, j + hw); i++) { sum += c.Nat[i]; cnt++; }
                c.PinH[k] = Math.Max(c.Nat[j] - CutM * JointBoundShare, Math.Min(c.Nat[j] + FillM * JointBoundShare, sum / cnt));
            }
            double Dist(int k) => c.S[c.Joints[k + 1]] - c.S[c.Joints[k]];
            for (int k = 1; k < nseg; k++) { double lim = JointGradeUse * g * Dist(k - 1); c.PinH[k] = Math.Max(c.PinH[k - 1] - lim, Math.Min(c.PinH[k - 1] + lim, c.PinH[k])); }
            for (int k = nseg - 1; k >= 1; k--) { double lim = JointGradeUse * g * Dist(k); c.PinH[k] = Math.Max(c.PinH[k + 1] - lim, Math.Min(c.PinH[k + 1] + lim, c.PinH[k])); }
            // entrances (RG-181): the grade follows the surface slope over the 300 m inside the corridor (not 0: a climb of 4 % into a settlement cannot flatten out within one pinned station
            // and one crest radius, it ran the curve pass to its stall exit), clamped to half the class grade
            int hs = Math.Max(1, (int)Math.Round(300 / c.Ds));
            double sIn = (c.Nat[Math.Min(ns - 1, c.Joints[0] + hs)] - c.Nat[c.Joints[0]]) / (c.S[Math.Min(ns - 1, c.Joints[0] + hs)] - c.S[c.Joints[0]]);
            c.PinG[0] = Math.Max(-gmax, Math.Min(gmax, sIn));
            int jl = c.Joints[nseg], jb = Math.Max(0, jl - hs);
            double sOut = (c.Nat[jl] - c.Nat[jb]) / (c.S[jl] - c.S[jb]);
            c.PinG[nseg] = Math.Max(-gmax, Math.Min(gmax, sOut));
            for (int k = 1; k < nseg; k++)
            {
                double s1 = (c.PinH[k] - c.PinH[k - 1]) / Dist(k - 1), s2 = (c.PinH[k + 1] - c.PinH[k]) / Dist(k);
                c.PinG[k] = s1 * s2 <= 0 ? 0 : Math.Max(-gmax, Math.Min(gmax, Math.Sign(s1) * Math.Min(Math.Abs(s1), Math.Abs(s2))));
            }
        }

        /// <summary>RG-184 stall exit fix (planet only; NightRacer never routes corridors). None = the RG-182 joints; Slide = candidate (b): a joint whose ground slope contradicts its
        /// pinned grade slides to the nearest agreeing station before the profiles are solved (SlideJoints). Candidate (a), cut / fill slack at the joints, changed no profile of
        /// either area (every station beyond the curve limit was free, not at a bound) and was deleted.</summary>
        public enum StallFix { None, Slide }
        /// <summary>Per builder (the RG-184 measurement routes both; the default is the kept fix).</summary>
        public StallFix Fix = StallFix.Slide;

        /// <summary>Inputs of the profile solve of segment k (shared by Profile and the stall diagnostics): chord distance, target, bounds, grade cap, pins.</summary>
        public static void ProfileInputs(Corridor c, int k, out double[] s, out double[] tg, out double[] lo, out double[] hi, out double[] gr, out bool[] pin)
        {
            int i0 = c.Joints[k], i1 = c.Joints[k + 1], m = i1 - i0 + 1; string cls = c.Link.RoadClass;
            s = new double[m]; tg = new double[m]; lo = new double[m]; hi = new double[m]; gr = new double[Math.Max(1, m - 1)]; pin = new bool[m];
            double g = MaxGrade(cls) * GradeUse;
            for (int i = 0; i < m; i++)
            {
                if (i > 0) { double dx = c.X[i0 + i] - c.X[i0 + i - 1], dz = c.Z[i0 + i] - c.Z[i0 + i - 1]; s[i] = s[i - 1] + Math.Sqrt(dx * dx + dz * dz); gr[i - 1] = g; }
                tg[i] = c.Nat[i0 + i]; lo[i] = tg[i] - CutM; hi[i] = tg[i] + FillM;
            }
            var tgs = tg; var los = lo; var his = hi; var pins = pin;
            void Pin(int i, double v) { los[i] = his[i] = tgs[i] = v; pins[i] = true; }
            Pin(0, c.PinH[k]); Pin(m - 1, c.PinH[k + 1]);
            if (m >= 4) { Pin(1, c.PinH[k] + c.PinG[k] * s[1]); Pin(m - 2, c.PinH[k + 1] - c.PinG[k + 1] * (s[m - 1] - s[m - 2])); }
        }

        /// <summary>Vertical profile of segment k alone: target = surface, bounds surface - 25 m .. + 15 m, both joint stations and their neighbours pinned (height + grade), grade cap 0.9 x class max, the WorldGen vertical-curve pass (class limits, stall 300).</summary>
        public static Segment Profile(Corridor c, int k)
        {
            var sw = Stopwatch.StartNew();
            int i0 = c.Joints[k], i1 = c.Joints[k + 1], m = i1 - i0 + 1; string cls = c.Link.RoadClass;
            ProfileInputs(c, k, out var s, out var tg, out var lo, out var hi, out var gr, out var pin); var y = new double[m];
            int beyond = Generator.SolveVerticalProfile(s, tg, lo, hi, gr, pin, y, cls, 1.0, c.Legacy ? 300 : StallOuter, out int infeasible);
            var seg = new Segment { Index = k, I0 = i0, I1 = i1, Y = y, BeyondCurve = beyond, Infeasible = infeasible, LengthM = s[m - 1] };
            for (int i = 1; i < m; i++) if (s[i] - s[i - 1] > 1e-6) seg.MaxGrade = Math.Max(seg.MaxGrade, Math.Abs(y[i] - y[i - 1]) / (s[i] - s[i - 1]));
            seg.Seconds = sw.Elapsed.TotalSeconds;
            return seg;
        }
    }

    /// <summary>Binary min-heap of (key, node); ties broken by the smaller node index (deterministic).</summary>
    sealed class MinHeap
    {
        readonly List<double> k = new List<double>(); readonly List<int> v = new List<int>();
        public int Count => k.Count;
        bool Less(int a, int b) => k[a] < k[b] || (k[a] == k[b] && v[a] < v[b]);
        void Swap(int a, int b) { var tk = k[a]; k[a] = k[b]; k[b] = tk; var tv = v[a]; v[a] = v[b]; v[b] = tv; }
        public void Push(double key, int node)
        {
            k.Add(key); v.Add(node); int i = k.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (!Less(i, p)) break; Swap(i, p); i = p; }
        }
        public void Pop(out double key, out int node)
        {
            key = k[0]; node = v[0]; int last = k.Count - 1; Swap(0, last); k.RemoveAt(last); v.RemoveAt(last);
            int i = 0;
            while (true) { int l = 2 * i + 1, r = l + 1, m = i; if (l < k.Count && Less(l, m)) m = l; if (r < k.Count && Less(r, m)) m = r; if (m == i) break; Swap(i, m); i = m; }
        }
    }
}
