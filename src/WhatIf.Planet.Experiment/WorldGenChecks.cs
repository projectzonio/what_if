using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WhatIf.Planet;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Experiment
{
    /// <summary>RG-165 check group w: the WorldGen adapter on one face-centre, one cube-edge-midpoint and one cube-vertex sector (+ their 4 neighbours), 3 seeds.</summary>
    public static class WorldGenChecks
    {
        const int M = SectorId.PerFaceSide, V = AdaptedSector.Verts;

        public static (string Name, SectorId Id)[] Cases() => new[]
        {
            ("face-centre", new SectorId(0, M / 2, M / 2)),
            ("cube-edge-mid", new SectorId(0, M / 2, M - 1)),
            ("cube-vertex", new SectorId(0, M - 1, M - 1)),
        };

        static Row Mk(string check, string region, string seed, string metric, string measured, string thr, bool pass, bool diag = false) =>
            new Row { Check = check, Region = region, Seed = seed, Metric = metric, Measured = measured, Threshold = thr, Pass = pass, Diag = diag };
        static string F(double v, string f = "G6") => v.ToString(f);

        /// <summary>The case sector and its 4 neighbours, and every 4-neighbour pair among them (5 at a cube vertex: the two other faces' corner sectors too).</summary>
        public static List<(SectorId A, SectorId B)> Pairs(SectorId c, out List<SectorId> set)
        {
            set = new List<SectorId> { c }; for (int d = 0; d < 4; d++) set.Add(c.Neighbour(d));
            var l = new List<(SectorId, SectorId)>();
            for (int a = 0; a < set.Count; a++) for (int b = a + 1; b < set.Count; b++)
                    for (int d = 0; d < 4; d++) if (set[a].Neighbour(d).Equals(set[b])) { l.Add((set[a], set[b])); break; }
            return l;
        }

        static int[] Side(int s)
        {
            var r = new int[V];
            for (int k = 0; k < V; k++) r[k] = s == 0 ? AdaptedSector.Idx(V - 1, k) : s == 1 ? AdaptedSector.Idx(0, k) : s == 2 ? AdaptedSector.Idx(k, V - 1) : AdaptedSector.Idx(k, 0);
            return r;
        }

        /// <summary>Index pairs of the shared side of two adapted sectors (nearest side midpoints, orientation by the smaller summed distance).</summary>
        public static List<(int A, int B)> Shared(AdaptedSector a, AdaptedSector b)
        {
            int sa = 0, sb = 0; double best = double.MaxValue;
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++)
                {
                    double d = (a.World[Side(i)[V / 2]] - b.World[Side(j)[V / 2]]).Length;
                    if (d < best) { best = d; sa = i; sb = j; }
                }
            int[] ia = Side(sa), ib = Side(sb); double fwd = 0, rev = 0;
            for (int k = 0; k < V; k++) { fwd += (a.World[ia[k]] - b.World[ib[k]]).Length; rev += (a.World[ia[k]] - b.World[ib[V - 1 - k]]).Length; }
            var l = new List<(int, int)>(); for (int k = 0; k < V; k++) l.Add((ia[k], rev < fwd ? ib[V - 1 - k] : ib[k]));
            return l;
        }

        sealed class Built { public SectorPlanetInput In; public AdaptedSector S; }
        static Built Make(SectorId id, ulong seed)
        {
            var b = new Built { In = SectorPlanetInput.Build(id, seed), S = new AdaptedSector() };
            SectorAdapter.Build(b.In, SectorAdapter.Model(b.In), b.S); return b;
        }

        /// <summary>w1-w3 (per case and seed) and w4 (per case). Deterministic, not timed.</summary>
        public static List<Row> Run()
        {
            var rows = new List<Row>();
            foreach (var (name, id) in Cases())
            {
                var pairs = Pairs(id, out var set);
                foreach (ulong seed in Checks.Seeds)
                {
                    string s = seed.ToString();
                    var built = set.ToDictionary(x => x, x => Make(x, seed));
                    double baseDiff = 0, gridBaseDiff = 0, gapD = 0, gapF = 0; int unequal = 0, shared = 0;
                    foreach (var (ia, ib) in pairs)
                    {
                        Built a = built[ia], b = built[ib];
                        foreach (var (ka, kb) in Shared(a.S, b.S))
                        {
                            shared++;
                            double pa = a.S.PlanetBase[ka], pb = b.S.PlanetBase[kb];
                            if (BitConverter.DoubleToInt64Bits(pa) != BitConverter.DoubleToInt64Bits(pb)) unequal++;
                            baseDiff = Math.Max(baseDiff, Math.Abs(pa - pb));
                            gridBaseDiff = Math.Max(gridBaseDiff, Math.Abs(a.In.Base(a.S.X[ka], a.S.Z[ka]) - b.In.Base(b.S.X[kb], b.S.Z[kb])));
                            gapD = Math.Max(gapD, (a.S.World[ka] - b.S.World[kb]).Length);
                            Vec3 fa = a.In.Sector.LocalToWorld(a.S.Lx[ka], a.S.Ly[ka], a.S.Lz[ka]), fb = b.In.Sector.LocalToWorld(b.S.Lx[kb], b.S.Ly[kb], b.S.Lz[kb]);
                            gapF = Math.Max(gapF, (fa - fb).Length);
                        }
                    }
                    string pp = $"{pairs.Count} pairs / {shared} shared vertices";
                    rows.Add(Mk("w1", name, s, $"shared-edge planet base height max /diff/ m ({pp}; {unequal} not bit-identical)", F(baseDiff), "= 0 (bit-identical)", baseDiff == 0 && unequal == 0));
                    rows.Add(Mk("w1", name, s, $"shared-edge grid-interpolated WorldGen base max /diff/ m (each side's own frame and 16 m grid; why the edge uses the planet base)", F(gridBaseDiff), "report", true, true));
                    rows.Add(Mk("w2", name, s, $"seam of the adapted mesh: max world gap mm, double positions ({pp})", F(gapD * 1000), "< 1", gapD * 1000 < 1));
                    rows.Add(Mk("w2", name, s, $"seam of the adapted mesh: max world gap mm, each side's float local mesh back through its own frame ({pp})", F(gapF * 1000), "< 1", gapF * 1000 < 1));

                    // w3 / interpolation error over the case sector
                    var c = built[id]; var S = c.S;
                    double edgeC = 0, maxC = 0, sT = 0, sC = 0, sAdd = 0, interp = 0;
                    for (int n = 0; n < AdaptedSector.Count; n++)
                    {
                        maxC = Math.Max(maxC, Math.Abs(S.Contribution[n]));
                        interp = Math.Max(interp, Math.Abs(c.In.Base(S.X[n], S.Z[n]) - S.PlanetBase[n]));
                        if (S.Taper[n] == 0) edgeC = Math.Max(edgeC, Math.Abs(S.Taper[n] * S.Contribution[n]));
                    }
                    for (int j = 0; j < V; j++) for (int i = 0; i < V; i++)
                            foreach (var (di, dj) in new[] { (1, 0), (0, 1) })
                            {
                                if (i + di >= V || j + dj >= V) continue;
                                int p = AdaptedSector.Idx(i, j), q = AdaptedSector.Idx(i + di, j + dj);
                                if (S.Taper[p] >= 1 && S.Taper[q] >= 1) continue;
                                double ds = Math.Sqrt((S.X[q] - S.X[p]) * (S.X[q] - S.X[p]) + (S.Z[q] - S.Z[p]) * (S.Z[q] - S.Z[p]));
                                double dT = S.Taper[q] * S.Contribution[q] - S.Taper[p] * S.Contribution[p], dC = S.Contribution[q] - S.Contribution[p];
                                sT = Math.Max(sT, Math.Abs(dT) / ds); sC = Math.Max(sC, Math.Abs(dC) / ds); sAdd = Math.Max(sAdd, Math.Abs(dT - dC) / ds);
                            }
                    rows.Add(Mk("w3", name, s, "max /WorldGen contribution/ on the quad edge m (taper x contribution on the 256 rim vertices)", F(edgeC), "= 0", edgeC == 0));
                    rows.Add(Mk("w3", name, s, $"taper band {SectorAdapter.TaperM:F0} m: max slope change by the taper /d(t c) - dc/ / ds (rise/run)", F(sAdd), "report", true, true));
                    rows.Add(Mk("w3", name, s, "taper band: max slope of the tapered contribution /d(t c)/ds/ (rise/run)", F(sT), "report", true, true));
                    rows.Add(Mk("w3", name, s, "taper band: max slope of the untapered contribution /dc/ds/ (rise/run)", F(sC), "report", true, true));
                    rows.Add(Mk("w3", name, s, "max /WorldGen contribution/ anywhere in the sector m (detail octaves 2-4 of relief 60 m; no roads in v1)", F(maxC), "report", true, true));
                    rows.Add(Mk("w3", name, s, "16 m Catmull-Rom grid vs exact planet base at the 4225 vertices: max /diff/ m", F(interp), "report", true, true));
                }
                rows.AddRange(W4(name, id));
            }
            return rows;
        }

        static List<Row> W4(string name, SectorId id)
        {
            var inp = SectorPlanetInput.Build(id, Checks.Seeds[0]); var S = new AdaptedSector();
            SectorAdapter.Build(inp, SectorAdapter.Model(inp), S);
            int[] k = { AdaptedSector.Idx(0, 0), AdaptedSector.Idx(V - 1, 0), AdaptedSector.Idx(V - 1, V - 1), AdaptedSector.Idx(0, V - 1) };
            var len = new double[4]; double angDev = 0, area = 0;
            for (int a = 0; a < 4; a++)
            {
                int p = k[a], q = k[(a + 1) & 3], o = k[(a + 3) & 3];
                len[a] = Math.Sqrt((S.X[q] - S.X[p]) * (S.X[q] - S.X[p]) + (S.Z[q] - S.Z[p]) * (S.Z[q] - S.Z[p]));
                double e1x = S.X[q] - S.X[p], e1z = S.Z[q] - S.Z[p], e2x = S.X[o] - S.X[p], e2z = S.Z[o] - S.Z[p];
                double ang = Math.Atan2(Math.Abs(e1x * e2z - e1z * e2x), e1x * e2x + e1z * e2z) * 180 / Math.PI;
                angDev = Math.Max(angDev, Math.Abs(ang - 90));
                area += S.X[p] * S.Z[q] - S.X[q] * S.Z[p];
            }
            area = Math.Abs(area) * 0.5;
            double band = 0; int inBand = 0;
            for (int n = 0; n < AdaptedSector.Count; n++) if (S.Taper[n] < 1) { inBand++; band = Math.Max(band, S.EdgeDistM[n]); }
            const string a0 = "all";
            return new List<Row>
            {
                Mk("w4", name, a0, $"footprint side lengths m in the tangent plane (row 0 / col 64 / row 64 / col 0)", string.Join(" / ", len.Select(x => x.ToString("F2"))), "report", true, true),
                Mk("w4", name, a0, "footprint max corner angle deviation from 90 deg", F(angDev, "F3"), "report", true, true),
                Mk("w4", name, a0, "footprint area / (256 m x 256 m)", F(area / 65536.0, "F4"), "report", true, true),
                Mk("w4", name, a0, $"WorldGen domain: base grid {inp.Side} x {inp.Side} at 16 m (interior +-{inp.HalfM:F0} m; RG-160 assumed 19 x 19 = +-128 m)", inp.Side.ToString(), "report", true, true),
                Mk("w4", name, a0, $"vertices inside the {SectorAdapter.TaperM:F0} m taper band (of {AdaptedSector.Count}); max band distance m", $"{inBand} ({100.0 * inBand / AdaptedSector.Count:F1} %); {band:F2} m", "report", true, true),
            };
        }

        /// <summary>w5: per case and seed, a global warm-up (>= 60 builds, >= 1 s), then per case and seed 5 warm + 51 timed builds (input, model construction, 65 x 65 heights). Single thread.</summary>
        public static List<string> CostRun()
        {
            var lines = new List<string> { "case,seed,median_ms,p95_ms,input_med_ms,model_med_ms,heights_med_ms" };
            var o = new AdaptedSector(); var sw = new Stopwatch(); double tick = 1000.0 / Stopwatch.Frequency;
            // global warm-up (tiered JIT promotes after 30 calls + a background delay): >= 60 builds and >= 1 s over all cases
            var warm = Stopwatch.StartNew(); int wn = 0;
            while (wn < 60 || warm.ElapsedMilliseconds < 1000)
                foreach (var (_, id) in Cases()) { var wi = SectorPlanetInput.Build(id, 1); SectorAdapter.Build(wi, SectorAdapter.Model(wi), o); wn++; }
            foreach (var (name, id) in Cases())
                foreach (ulong seed in Checks.Seeds)
                {
                    var tot = new List<double>(); var tin = new List<double>(); var tmo = new List<double>(); var the = new List<double>();
                    for (int rep = 0; rep < 56; rep++)
                    {
                        sw.Restart(); var inp = SectorPlanetInput.Build(id, seed); long a = sw.ElapsedTicks;
                        var model = SectorAdapter.Model(inp); long b = sw.ElapsedTicks;
                        SectorAdapter.Build(inp, model, o); long c = sw.ElapsedTicks;
                        if (rep < 5) continue;
                        tot.Add(c * tick); tin.Add(a * tick); tmo.Add((b - a) * tick); the.Add((c - b) * tick);
                    }
                    lines.Add($"{name},{seed},{Q(tot, 0.5):F5},{Q(tot, 0.95):F5},{Q(tin, 0.5):F5},{Q(tmo, 0.5):F5},{Q(the, 0.5):F5}");
                    Console.WriteLine($"w5 {name} seed {seed}: median {Q(tot, 0.5):F4} ms, P95 {Q(tot, 0.95):F4} ms");
                }
            return lines;
        }

        static double Q(List<double> v, double q) { var s = v.OrderBy(x => x).ToList(); return s[Math.Min(s.Count - 1, (int)Math.Ceiling(q * s.Count) - 1)]; }
    }
}
