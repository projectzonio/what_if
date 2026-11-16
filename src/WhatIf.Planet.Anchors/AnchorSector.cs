using System;
using System.Collections.Generic;
using WhatIf.Planet;
using WhatIf.Planet.Surface;
using WhatIf.Planet.WorldGen;
using TerrainModel = WhatIf.WorldGen.TerrainModel;

namespace WhatIf.Planet.Anchors
{
    /// <summary>
    /// RG-178: a level-15 sector from the anchors, same output type as SectorBuild (AdaptedSector). Per lattice vertex d (a pure function of d and the anchors that reach it):
    /// h0 = ground (surface + global detail); inside a settlement's radius + BlendM the WorldGen settlement terrain T (surface base, its own detail and street carve) blends in
    /// with a smoothstep weight; then the nearest corridor (smallest distance - half width, ties by link key) shapes the terrain with the TerrainModel rule:
    /// road height y inside half width + 1.5 m, then cut / fill 1 : 1.5 back to h. No region, no taper: two sectors that share a vertex ask the same question.
    /// </summary>
    public static class AnchorSector
    {
        sealed class Cand { public RoutedLink L; public int I0, I1; }

        [ThreadStatic] static double[][] scratch;

        const double ShoulderCode = 2.0;   // RG-183: gravel shoulder drawn this far outside the road edge (m)

        /// <summary>
        /// RG-183 surface code 2 (street) / 3 (shoulder) and RG-184 street carve for the settlement vertices idx[0..m) at local plane points (xs, zs), from the settlement world's
        /// StreetSet. Per vertex the street with the smallest distance - half width (ties: lower segment index) gives the road height y; the blended height hh is then shaped with the
        /// corridor rule (y inside half width + 1.5 m, then cut / fill 1 : 1.5). Inside the full-weight blend hh is already the WorldGen carved terrain, so the carve changes it only
        /// where the nearest-street rules differ; outside (1.0 - 1.15 R, blend weight &lt; 1) it pulls the blended ground down / up to the street. This pass only records the
        /// nearest street per vertex over all settlements (sEff / sY, smaller distance - half width wins, ties: the settlement met first); Build applies codes and carve once
        /// after every settlement blend, and the corridor carve leaves a vertex alone where a street is nearer than the corridor.
        /// </summary>
        static void StreetPass(StreetSet st, double[] xs, double[] zs, List<int> idx, int m, double[] sEff, double[] sY)
        {
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            for (int k = 0; k < m; k++) { x0 = Math.Min(x0, xs[k]); x1 = Math.Max(x1, xs[k]); z0 = Math.Min(z0, zs[k]); z1 = Math.Max(z1, zs[k]); }
            const double Reach = TerrainModel.ShoulderM + TerrainModel.MaxDiffM * TerrainModel.SlopeRun;
            var d = st.D; var segs = new List<int>();
            for (int s = 0, o = 0; s < st.Count; s++, o += StreetSet.Stride)
            {
                double pad = d[o + 6] + Reach;
                if (Math.Max(d[o], d[o + 2]) < x0 - pad || Math.Min(d[o], d[o + 2]) > x1 + pad || Math.Max(d[o + 1], d[o + 3]) < z0 - pad || Math.Min(d[o + 1], d[o + 3]) > z1 + pad) continue;
                segs.Add(s);
            }
            int ns = segs.Count; if (ns == 0) return;
            // 32 m cell grid over the vertex box: each segment is listed in every cell its (half width + carve reach) box touches, so a vertex only tests the segments of its own cell
            const double Cell = 32.0; int gw = (int)((x1 - x0) / Cell) + 1, gh = (int)((z1 - z0) / Cell) + 1; var cells = new List<int>[gw * gh];
            foreach (int s in segs)
            {
                int o = s * StreetSet.Stride; double pad = d[o + 6] + Reach;
                double bx0 = Math.Min(d[o], d[o + 2]) - pad, bx1 = Math.Max(d[o], d[o + 2]) + pad, bz0 = Math.Min(d[o + 1], d[o + 3]) - pad, bz1 = Math.Max(d[o + 1], d[o + 3]) + pad;
                int cx0 = Math.Max(0, (int)Math.Floor((bx0 - x0) / Cell)), cx1 = Math.Min(gw - 1, (int)Math.Floor((bx1 - x0) / Cell)), cz0 = Math.Max(0, (int)Math.Floor((bz0 - z0) / Cell)), cz1 = Math.Min(gh - 1, (int)Math.Floor((bz1 - z0) / Cell));
                for (int cz = cz0; cz <= cz1; cz++) for (int cx = cx0; cx <= cx1; cx++) { var l = cells[cz * gw + cx] ?? (cells[cz * gw + cx] = new List<int>()); l.Add(s); }
            }
            for (int k = 0; k < m; k++)
            {
                double x = xs[k], z = zs[k], best = double.MaxValue, by = 0;
                var cl = cells[Math.Min(gh - 1, (int)((z - z0) / Cell)) * gw + Math.Min(gw - 1, (int)((x - x0) / Cell))]; if (cl == null) continue;
                for (int q = 0; q < cl.Count; q++)
                {
                    int o = cl[q] * StreetSet.Stride; double ax = d[o], az = d[o + 1], ex = d[o + 2] - ax, ez = d[o + 3] - az, l2 = ex * ex + ez * ez;
                    double t = l2 > 0 ? ((x - ax) * ex + (z - az) * ez) / l2 : 0; t = t < 0 ? 0 : (t > 1 ? 1 : t);
                    double qx = ax + ex * t - x, qz = az + ez * t - z, eff = Math.Sqrt(qx * qx + qz * qz) - d[o + 6];
                    if (eff < best) { best = eff; by = d[o + 4] + (d[o + 5] - (double)d[o + 4]) * t; }
                }
                if (best > Reach) continue;
                int n = idx[k]; if (best < sEff[n]) { sEff[n] = best; sY[n] = by; }
            }
        }

        public static void Build(AnchorWorld w, QuadSector sec, AdaptedSector o)
        {
            int face = sec.Id.Face, i0 = sec.Id.Col * QuadSector.Sub, j0 = sec.Id.Row * QuadSector.Sub;
            double r = CubeSphere.Radius; const int V = AdaptedSector.Verts; const int Cnt = V * V;
            var surf = AnchorWorld.ThreadSurface(w.Seed);
            var u = new double[V]; var v = new double[V];
            for (int k = 0; k < V; k++) { u[k] = Lattice.Coord(i0 + k); v[k] = Lattice.Coord(j0 + k); }
            var sc = scratch ?? (scratch = new double[6][]);
            for (int q = 0; q < 6; q++) if (sc[q] == null) sc[q] = new double[Cnt];
            double[] hs = sc[0], hh = sc[1], hx = sc[2], hz = sc[3], sEff = sc[4], sY = sc[5];   // sEff / sY: nearest street (RG-184)
            for (int n = 0; n < Cnt; n++) sEff[n] = double.MaxValue;   // hs surface only; hh working height; hx / hz batch inputs
            var dirs = o.World;
            var surfCode = o.Surface; Array.Clear(surfCode, 0, Cnt);   // RG-183
            for (int j = 0, n = 0; j < V; j++)
                for (int i = 0; i < V; i++, n++)
                {
                    int I = i0 + i, J = j0 + j;
                    Vec3 d = Lattice.OnEdge(I, J) ? Lattice.Direction(face, I, J) : Lattice.Compose(face, u[i], v[j]);
                    dirs[n] = d;
                    double s0 = surf.HeightKind(d, out _);
                    hs[n] = s0; o.PlanetBase[n] = s0 + Ground.Detail(w.Seed, d);
                    hh[n] = o.PlanetBase[n];
                }
            var cen = (dirs[0] + dirs[Cnt - 1]).Normalized(); double rad = 0;
            foreach (int c in new[] { 0, V - 1, (V - 1) * V, Cnt - 1 }) rad = Math.Max(rad, SettlementField.Gc(cen, dirs[c]));
            rad += 20;
            var region = sec.Id.Region;

            // settlements, id order (the array is sorted by id)
            foreach (var s in w.SettlementsNear(region))
            {
                double reach = s.RadiusM + AnchorParams.BlendM;
                if (SettlementField.Gc(cen, s.Dir) > rad + reach) continue;
                var sw = w.World(s); if (!sw.Ok) continue;
                int m = 0; var idx = new List<int>(); var wt = new List<double>();
                for (int n = 0; n < Cnt; n++)
                {
                    sw.Chart.ToLocal(dirs[n], out double x, out double z);
                    double dist = Math.Sqrt(x * x + z * z);
                    if (dist >= reach) continue;
                    double t = Math.Min(1.0, (reach - dist) / AnchorParams.BlendM); t = t * t * (3 - 2 * t);
                    if (t <= 0) continue;
                    hx[m] = x; hz[m] = z; idx.Add(n); wt.Add(t); m++;
                }
                if (m == 0) continue;
                var bh = new double[m]; var res = new double[m];
                for (int k = 0; k < m; k++) bh[k] = hs[idx[k]];
                lock (sw.Gate) sw.Terrain.HeightBatch(hx, hz, m, res, bh);
                for (int k = 0; k < m; k++) { int n = idx[k]; hh[n] = hh[n] + wt[k] * (res[k] - hh[n]); }
                StreetPass(sw.Streets, hx, hz, idx, m, sEff, sY);
            }
            for (int n = 0; n < Cnt; n++)   // RG-183 street codes + RG-184 street carve, after every settlement blend
            {
                double best = sEff[n]; if (best == double.MaxValue) continue;
                if (best <= 0) surfCode[n] = 2; else if (best <= ShoulderCode) surfCode[n] = 3;
                double kk = Math.Max(0, (best - TerrainModel.ShoulderM) / TerrainModel.SlopeRun), dh = hh[n] - sY[n];
                hh[n] = sY[n] + (dh < -kk ? -kk : (dh > kk ? kk : dh));
            }

            // corridors that come within the carve reach of the sector disc
            var cands = new List<(Cand c, Chart ch)>();
            const double Reach = TerrainModel.ShoulderM + TerrainModel.MaxDiffM * TerrainModel.SlopeRun;
            foreach (var link in w.LinksNear(region))
            {
                var rl = w.Route(link); if (rl == null) continue;
                var cor = rl.C; cor.Chart.ToLocal(cen, out double cx, out double cz);
                double lim = rad + rl.HalfWidthM + Reach + 10;
                if (cx < rl.MinX - lim || cx > rl.MaxX + lim || cz < rl.MinZ - lim || cz > rl.MaxZ + lim) continue;
                int lo = -1, hi = -1;
                for (int k = 0; k < cor.X.Length; k++) { double dx = cor.X[k] - cx, dz = cor.Z[k] - cz; if (dx * dx + dz * dz <= lim * lim) { if (lo < 0) lo = k; hi = k; } }
                if (lo < 0) continue;
                cands.Add((new Cand { L = rl, I0 = Math.Max(0, lo - 1), I1 = Math.Min(cor.X.Length - 1, hi + 1) }, cor.Chart));
            }
            if (cands.Count > 0)
            {
                var px = new double[cands.Count]; var pz = new double[cands.Count];
                for (int n = 0; n < Cnt; n++)
                {
                    double bestEff = double.MaxValue, bestY = 0; ulong bestKey = 0; bool have = false;
                    for (int c = 0; c < cands.Count; c++)
                    {
                        var cd = cands[c].c; var cor = cd.L.C;
                        cands[c].ch.ToLocal(dirs[n], out double x, out double z);
                        double bd = double.MaxValue, by = 0;
                        for (int k = cd.I0; k < cd.I1; k++)
                        {
                            double ax = cor.X[k], az = cor.Z[k], ex = cor.X[k + 1] - ax, ez = cor.Z[k + 1] - az, l2 = ex * ex + ez * ez;
                            double t = l2 > 0 ? ((x - ax) * ex + (z - az) * ez) / l2 : 0;
                            if ((t < 0 && k == 0) || (t > 1 && k == cor.X.Length - 2)) continue;   // RG-182: no end cap: inside the settlement radius the entrance road of the settlement world takes over
                            t = t < 0 ? 0 : (t > 1 ? 1 : t);
                            double qx = ax + ex * t - x, qz = az + ez * t - z, d2 = qx * qx + qz * qz;
                            if (d2 >= bd) continue;
                            if (d2 > (cd.L.HalfWidthM + Reach) * (cd.L.HalfWidthM + Reach)) continue;
                            var sg = cd.L.Seg(cd.L.SegOf(k)); int a = k - sg.I0;
                            bd = d2; by = sg.Y[a] + (sg.Y[a + 1] - sg.Y[a]) * t;
                        }
                        if (bd == double.MaxValue) continue;
                        double eff = Math.Sqrt(bd) - cd.L.HalfWidthM; ulong key = cor.Link.Key;
                        if (!have || eff < bestEff || (eff == bestEff && key < bestKey)) { have = true; bestEff = eff; bestY = by; bestKey = key; }
                    }
                    if (!have || sEff[n] < bestEff) continue;   // RG-184: a nearer street decides (corridor-only sectors: sEff = max, unchanged)
                    if (bestEff <= 0) surfCode[n] = 1; else if (bestEff <= ShoulderCode && surfCode[n] == 0) surfCode[n] = 3;
                    double kk = Math.Max(0, (bestEff - TerrainModel.ShoulderM) / TerrainModel.SlopeRun), dh = hh[n] - bestY;
                    hh[n] = bestY + (dh < -kk ? -kk : (dh > kk ? kk : dh));
                }
            }

            for (int n = 0; n < Cnt; n++)
            {
                double h = hh[n]; Vec3 p = dirs[n] * (r + h), rel = p - sec.Centre;
                o.X[n] = 0; o.Z[n] = 0; o.Taper[n] = 1; o.EdgeDistM[n] = 1e9; o.Contribution[n] = h - o.PlanetBase[n];
                o.World[n] = p;
                o.Lx[n] = (float)Vec3.Dot(rel, sec.East); o.Ly[n] = (float)Vec3.Dot(rel, sec.Up); o.Lz[n] = (float)Vec3.Dot(rel, sec.North);
            }
        }
    }
}
