using System;
using System.Collections.Generic;
using System.Diagnostics;
using WhatIf.Planet;

namespace WhatIf.Planet.Experiment
{
    /// <summary>One result row: check | region | seed | metric | measured | threshold | pass. Diag rows are reported but do not decide the verdict.</summary>
    public sealed class Row
    {
        public string Check, Region, Seed, Metric, Measured, Threshold; public bool Pass, Diag;
        public string Csv() => string.Join(",", Check, Region, Seed, Metric.Replace(',', ';'), Measured.Replace(',', ';'), Threshold.Replace(',', ';'), Pass ? "PASS" : "FAIL", Diag ? "diag" : "protocol");
    }

    public sealed class RegionCase
    {
        public string Name; public Vec3 Dir; public RegionId Id;
        public RegionCase(string name, Vec3 dir) { Name = name; Dir = dir.Normalized(); Id = CubeSphere.RegionOf(Dir, Level); }
        public const int Level = 10;
        public static RegionCase[] All()
        {
            double d = Math.PI / 180;
            return new[]
            {
                new RegionCase("R0", new Vec3(1, 0, 0)),
                new RegionCase("R45", new Vec3(Math.Cos(45 * d) * Math.Cos(20 * d), Math.Cos(45 * d) * Math.Sin(20 * d), Math.Sin(45 * d))),
                new RegionCase("R89", new Vec3(Math.Cos(89.99 * d), 0, Math.Sin(89.99 * d))),
                new RegionCase("RC", new Vec3(1, 1, 1)),
            };
        }
    }

    public static class Checks
    {
        public static readonly ulong[] Seeds = { 1, 42, 0x7FFFFFFF };
        static string Mm(double m) => (m * 1000).ToString("G6");

        static Row Mk(string check, string region, string seed, string metric, double value, string valueText, string thrText, bool pass, bool diag = false) =>
            new Row { Check = check, Region = region, Seed = seed, Metric = metric, Measured = valueText, Threshold = thrText, Pass = pass, Diag = diag };

        // ------------------------------------------------------------------ (a) shared-edge mismatch between level-15 quadtree sectors
        const int M = SectorId.PerFaceSide;
        static int Idx(int i, int j) => j * QuadSector.Verts + i;

        /// <summary>The 65 vertex indices of side 0 (+col), 1 (-col), 2 (+row), 3 (-row) of a sector grid, in increasing other coordinate.</summary>
        static int[] SideIdx(int side)
        {
            var r = new int[QuadSector.Verts];
            for (int k = 0; k < r.Length; k++) r[k] = side == 0 ? Idx(64, k) : side == 1 ? Idx(0, k) : side == 2 ? Idx(k, 64) : Idx(k, 0);
            return r;
        }

        /// <summary>
        /// Builds both sectors, pairs the nearest sides (by midpoint) and returns max vertical (along the radial) and horizontal distance over the 65 shared-edge
        /// vertices: from the double world positions, and from each side's own float local mesh transformed back with its own frame.
        /// </summary>
        public static void PairMismatch(SectorId ia, SectorId ib, ulong seed, bool terrain, out double vD, out double hD, out double vF, out double hF)
        {
            var a = new QuadSector(ia); var b = new QuadSector(ib); int n = QuadSector.Verts * QuadSector.Verts;
            var ax = new float[n]; var ay = new float[n]; var az = new float[n]; var bx = new float[n]; var by = new float[n]; var bz = new float[n];
            var wa = new Vec3[n]; var wb = new Vec3[n];
            a.Build(seed, terrain, ax, ay, az, wa); b.Build(seed, terrain, bx, by, bz, wb);
            int sa = 0, sb = 0; double best = double.MaxValue;
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++)
                {
                    double d = (wa[SideIdx(i)[32]] - wb[SideIdx(j)[32]]).Length;
                    if (d < best) { best = d; sa = i; sb = j; }
                }
            int[] ia2 = SideIdx(sa), ib2 = SideIdx(sb);
            double fwd = 0, rev = 0; int m = ia2.Length;
            for (int k = 0; k < m; k++) { fwd += (wa[ia2[k]] - wb[ib2[k]]).Length; rev += (wa[ia2[k]] - wb[ib2[m - 1 - k]]).Length; }
            vD = hD = vF = hF = 0;
            for (int k = 0; k < m; k++)
            {
                int ka = ia2[k], kb = rev < fwd ? ib2[m - 1 - k] : ib2[k];
                Acc(wa[ka], wb[kb], ref vD, ref hD);
                Acc(a.LocalToWorld(ax[ka], ay[ka], az[ka]), b.LocalToWorld(bx[kb], by[kb], bz[kb]), ref vF, ref hF);
            }
        }
        static void Acc(Vec3 p, Vec3 q, ref double v, ref double h)
        {
            Vec3 diff = p - q, up = (p + q).Normalized();
            v = Math.Max(v, Math.Abs(Vec3.Dot(diff, up))); h = Math.Max(h, (diff - up * Vec3.Dot(diff, up)).Length);
        }

        static KeyValuePair<SectorId, SectorId> Pair(SectorId a, SectorId b) => new KeyValuePair<SectorId, SectorId>(a, b);

        /// <summary>All 4-neighbour pairs in the 5 x 5 block around a sector (clipped at the face rim).</summary>
        public static List<KeyValuePair<SectorId, SectorId>> BlockPairs(SectorId c)
        {
            var l = new List<KeyValuePair<SectorId, SectorId>>();
            for (int r = c.Row - 2; r <= c.Row + 2; r++) for (int k = c.Col - 2; k <= c.Col + 2; k++)
                {
                    if (r < 0 || k < 0 || r >= M || k >= M) continue;
                    var s = new SectorId(c.Face, r, k);
                    if (k < c.Col + 2 && k + 1 < M) l.Add(Pair(s, new SectorId(c.Face, r, k + 1)));
                    if (r < c.Row + 2 && r + 1 < M) l.Add(Pair(s, new SectorId(c.Face, r + 1, k)));
                }
            return l;
        }

        /// <summary>Sector pairs across the four borders of a level-10 region (3 positions per border; the neighbour may lie across a cube edge).</summary>
        public static List<KeyValuePair<SectorId, SectorId>> RegionBorderPairs(RegionId r)
        {
            var l = new List<KeyValuePair<SectorId, SectorId>>();
            for (int d = 0; d < 4; d++) foreach (double frac in new[] { 0.25, 0.5, 0.75 })
                {
                    int off = (int)(frac * SectorId.PerRegionSide);
                    int row = d == 3 ? r.Row * 32 : d == 2 ? (r.Row + 1) * 32 - 1 : r.Row * 32 + off, col = d == 1 ? r.Col * 32 : d == 0 ? (r.Col + 1) * 32 - 1 : r.Col * 32 + off;
                    var s = new SectorId(r.Face, row, col); l.Add(Pair(s, s.Neighbour(d)));
                }
            return l;
        }

        /// <summary>Pairs across the 12 cube edges (24 face rims x 6 positions).</summary>
        public static List<KeyValuePair<SectorId, SectorId>> CubeEdgePairs()
        {
            var l = new List<KeyValuePair<SectorId, SectorId>>();
            for (int f = 0; f < 6; f++) for (int d = 0; d < 4; d++) foreach (int idx in new[] { 0, 1, 7, M / 2, M - 2, M - 1 })
                    {
                        int row = d == 3 ? 0 : d == 2 ? M - 1 : idx, col = d == 1 ? 0 : d == 0 ? M - 1 : idx;
                        var s = new SectorId(f, row, col); l.Add(Pair(s, s.Neighbour(d)));
                    }
            return l;
        }

        /// <summary>The 3 sectors at each of the 8 cube vertices, all 3 pairs.</summary>
        public static List<KeyValuePair<SectorId, SectorId>> CubeCornerPairs(out int corners)
        {
            var l = new List<KeyValuePair<SectorId, SectorId>>(); corners = 0;
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var c = new Vec3(sx, sy, sz); var ids = new List<SectorId>(); corners++;
                        for (int f = 0; f < 6; f++)
                            if (Vec3.Dot(CubeSphere.Normal(f), c) > 0) ids.Add(new SectorId(f, Vec3.Dot(CubeSphere.AxisV(f), c) > 0 ? M - 1 : 0, Vec3.Dot(CubeSphere.AxisU(f), c) > 0 ? M - 1 : 0));
                        for (int i = 0; i < ids.Count; i++) for (int j = i + 1; j < ids.Count; j++) l.Add(Pair(ids[i], ids[j]));
                    }
            return l;
        }

        static void AddRows(List<Row> rows, string region, string seed, string what, List<KeyValuePair<SectorId, SectorId>> pairs, ulong s, bool terrain)
        {
            double mvD = 0, mhD = 0, mvF = 0, mhF = 0;
            foreach (var p in pairs)
            {
                double a, b, c, d; PairMismatch(p.Key, p.Value, s, terrain, out a, out b, out c, out d);
                mvD = Math.Max(mvD, a); mhD = Math.Max(mhD, b); mvF = Math.Max(mvF, c); mhF = Math.Max(mhF, d);
            }
            string thr = "< 1 mm", t = terrain ? " with terrain height" : " (h = 0)";
            rows.Add(Mk("a", region, seed, $"{what} ({pairs.Count} pairs){t}: max vertical mm, double world", mvD, Mm(mvD), thr, mvD < 1e-3));
            rows.Add(Mk("a", region, seed, $"{what} ({pairs.Count} pairs){t}: max horizontal mm, double world", mhD, Mm(mhD), thr, mhD < 1e-3));
            rows.Add(Mk("a", region, seed, $"{what} ({pairs.Count} pairs){t}: max vertical mm, float local mesh back-transformed", mvF, Mm(mvF), thr, mvF < 1e-3));
            rows.Add(Mk("a", region, seed, $"{what} ({pairs.Count} pairs){t}: max horizontal mm, float local mesh back-transformed", mhF, Mm(mhF), thr, mhF < 1e-3));
        }

        public static List<Row> CheckA(RegionCase rc)
        {
            var rows = new List<Row>(); SectorId anchor = SectorId.At(rc.Dir);
            var block = BlockPairs(anchor); var border = RegionBorderPairs(rc.Id);
            AddRows(rows, rc.Name, "all", "5 x 5 block around the anchor sector", block, 0, false);
            AddRows(rows, rc.Name, "all", "level-10 region border", border, 0, false);
            foreach (ulong seed in Seeds)
            {
                AddRows(rows, rc.Name, seed.ToString(), "5 x 5 block around the anchor sector", block, seed, true);
                AddRows(rows, rc.Name, seed.ToString(), "level-10 region border", border, seed, true);
            }
            return rows;
        }

        /// <summary>Cube edges and cube vertices do not depend on the anchor regions: one set of rows named "cube".</summary>
        public static List<Row> CheckACube()
        {
            var rows = new List<Row>(); int corners;
            var edges = CubeEdgePairs(); var cp = CubeCornerPairs(out corners);
            AddRows(rows, "cube", "all", "across the 12 cube edges", edges, 0, false);
            AddRows(rows, "cube", "all", $"3 sectors at each of {corners} cube vertices", cp, 0, false);
            foreach (ulong seed in Seeds)
            {
                AddRows(rows, "cube", seed.ToString(), "across the 12 cube edges", edges, seed, true);
                AddRows(rows, "cube", seed.ToString(), $"3 sectors at each of {corners} cube vertices", cp, seed, true);
            }
            return rows;
        }

        // ------------------------------------------------------------------ (b) noise from two origins
        public static List<Row> CheckB(RegionCase rc)
        {
            var rows = new List<Row>(); double R = CubeSphere.Radius;
            Vec3 c = CubeSphere.RegionCentre(rc.Id), e, nn; CubeSphere.TangentAxes(rc.Id.Face, c, out e, out nn);
            SectorId s0 = SectorId.At(rc.Dir);
            Vec3 oA = new QuadSector(s0).Centre, oB = new QuadSector(s0.Neighbour(0)).Centre, oC = new QuadSector(new SectorId(s0.Face, s0.Row, Math.Max(0, Math.Min(M - 1, s0.Col + (s0.Col > M / 2 ? -391 : 391))))).Centre;
            Vec3[] origins = { oA, oB, oC };
            foreach (ulong seed in Seeds)
            {
                ulong st = SplitMix64.Mix(seed ^ 0xB5297A4D); long sameDiff = 0, rebuiltDiff = 0, floatDiff = 0; int rebuiltUlp = 0; double floatMax = 0; int total = 1000000;
                for (int i = 0; i < total; i++)
                {
                    double ox = (SplitMix64.NextDouble(ref st) - 0.5) * 10000, oz = (SplitMix64.NextDouble(ref st) - 0.5) * 10000;
                    Vec3 x = (c * R + e * ox + nn * oz).Normalized() * R;
                    int rb = BitConverter.SingleToInt32Bits(PlanetNoise.Height(seed, x));
                    foreach (Vec3 o in origins)
                    {
                        if (BitConverter.SingleToInt32Bits(PlanetNoise.Height(seed, x)) != rb) sameDiff++;
                        Vec3 local = x - o;
                        int rebuilt = BitConverter.SingleToInt32Bits(PlanetNoise.HeightFromOrigin(seed, o, local));
                        if (rebuilt != rb) { rebuiltDiff++; rebuiltUlp = Math.Max(rebuiltUlp, Math.Abs(rebuilt - rb)); }
                        Vec3 lf = new Vec3((float)local.X, (float)local.Y, (float)local.Z);       // what a float-local consumer would do
                        float fl = PlanetNoise.HeightFromOrigin(seed, o, lf), ref0 = PlanetNoise.Height(seed, x);
                        if (BitConverter.SingleToInt32Bits(fl) != rb) { floatDiff++; floatMax = Math.Max(floatMax, Math.Abs((double)fl - ref0)); }
                    }
                }
                string s = seed.ToString();
                rows.Add(Mk("b", rc.Name, s, "same double x from origins A, B (256 m), C (100 km): differing float bits of 3e6", sameDiff, sameDiff.ToString(), "= 0", sameDiff == 0));
                rows.Add(Mk("b", rc.Name, s, "x rebuilt as origin + local (double): differing results of 3e6", rebuiltDiff, rebuiltDiff.ToString(), "report", true));
                rows.Add(Mk("b", rc.Name, s, "x rebuilt as origin + local (double): max difference in float ulp", rebuiltUlp, rebuiltUlp.ToString(), "<= 1 ulp", rebuiltUlp <= 1));
                rows.Add(Mk("b", rc.Name, s, "diag: local rounded to float before the rebuild: differing results of 3e6", floatDiff, floatDiff.ToString(), "report", true, true));
                rows.Add(Mk("b", rc.Name, s, "diag: local rounded to float: max height difference mm", floatMax, Mm(floatMax), "report", true, true));
            }
            return rows;
        }

        // ------------------------------------------------------------------ (c) frame transport
        public static List<Row> CheckC(RegionCase rc)
        {
            double R = CubeSphere.Radius; double alpha = 1000.0 / R; Vec3 ctr = rc.Dir; string what = "1 km loop around the region centre";
            if (rc.Name == "R89") { ctr = new Vec3(0, 0, 1); alpha = 0.01 * Math.PI / 180; what = "circle of latitude 89.99 deg (radius " + (alpha * R).ToString("F0") + " m)"; }
            if (rc.Name == "RC") { what = "1 km loop around the cube corner (1,1,1)/sqrt3"; }
            Vec3 e = Vec3.Cross(ctr, Math.Abs(ctr.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0)).Normalized(), nn = Vec3.Cross(ctr, e);   // e x nn = ctr: counter-clockwise from outside
            int steps = (int)Math.Round(2 * Math.PI * Math.Sin(alpha) * R / 1.0);
            double worstDev = 0, worstErr = 0; var angles = new double[steps]; Frame fr = default, prev = default; Vec3 prevDir = default;
            Frame first = default;
            for (int i = 0; i <= steps; i++)
            {
                double phi = 2 * Math.PI * i / steps;
                Vec3 p = (ctr * Math.Cos(alpha) + (e * Math.Cos(phi) + nn * Math.Sin(phi)) * Math.Sin(alpha)) * R;
                if (i == 0) { fr = Frame.At(p, nn); first = fr; prev = fr; prevDir = p.Normalized(); continue; }
                fr.Transport(p);
                double analytic = Vec3.Angle(prevDir, p.Normalized()), measured = Frame.RotationAngle(prev, fr);
                worstDev = Math.Max(worstDev, Math.Abs(measured - analytic)); angles[i - 1] = measured;
                worstErr = Math.Max(worstErr, fr.Error()); prev = fr; prevDir = p.Normalized();
            }
            var sorted = (double[])angles.Clone(); Array.Sort(sorted); double median = sorted[sorted.Length / 2], max = sorted[sorted.Length - 1];
            double hol = Math.Atan2(Vec3.Dot(Vec3.Cross(first.Forward, fr.Forward), first.Up), Vec3.Dot(first.Forward, fr.Forward));
            double expect = 2 * Math.PI * (1 - Math.Cos(alpha));          // spherical cap area / R^2
            string m = $" ({what}, {steps} steps)";
            return new List<Row>
            {
                Mk("c", rc.Name, "all", "max |per-step rotation - analytic| rad" + m, worstDev, worstDev.ToString("E2"), "< 1e-9", worstDev < 1e-9),
                Mk("c", rc.Name, "all", "max step rotation / median step rotation", max / median, (max / median).ToString("F6"), "<= 2", max / median <= 2),
                Mk("c", rc.Name, "all", $"closing holonomy {hol:E6} rad vs cap area/R^2 {expect:E6}: |difference| rad", Math.Abs(hol - expect), Math.Abs(hol - expect).ToString("E2"), "< 1e-9", Math.Abs(hol - expect) < 1e-9),
                Mk("c", rc.Name, "all", "max frame orthonormality / up=radial error after the loop", worstErr, worstErr.ToString("E2"), "report < 1e-9", worstErr < 1e-9),
            };
        }

        // ------------------------------------------------------------------ (d) cost; returns (median ms, P95 ms) per region x seed
        public static void CostRun(RegionCase rc, ulong seed, out double medianMs, out double p95Ms)
        {
            var lx = new float[65 * 65]; var ly = new float[65 * 65]; var lz = new float[65 * 65];
            int r0 = rc.Id.Row * 32, c0 = rc.Id.Col * 32;
            for (int i = 0; i < 300; i++) new QuadSector(new SectorId(rc.Id.Face, (r0 + 64 + i / 20) % M, (c0 + 64 + i % 20) % M)).Build(seed, true, lx, ly, lz);   // warm (JIT + caches), untimed, other sectors
            var ms = new double[1000]; int k = 0; double sink = 0;
            for (int iz = 0; iz < 32 && k < 1000; iz++) for (int ix = 0; ix < 32 && k < 1000; ix++)     // 1000 of the 32 x 32 sectors of the region
                {
                    long t0 = Stopwatch.GetTimestamp();
                    var q = new QuadSector(new SectorId(rc.Id.Face, r0 + iz, c0 + ix)); q.Build(seed, true, lx, ly, lz);
                    long t1 = Stopwatch.GetTimestamp(); ms[k++] = (t1 - t0) * 1000.0 / Stopwatch.Frequency; sink += ly[2113];
                }
            if (sink == double.MinValue) Console.WriteLine();      // keep the work observable
            Array.Sort(ms); medianMs = ms[500]; p95Ms = ms[(int)(ms.Length * 0.95)];
        }
    }
}
