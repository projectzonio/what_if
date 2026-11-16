using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhatIf.Planet;
using WhatIf.Planet.Surface;

namespace WhatIf.Planet.FarLayer.Experiment
{
    public static class Checks
    {
        public static readonly ulong[] Seeds = { 20261112UL, 7UL };
        public static string F(double v, string f = "F3") => v.ToString(f, CultureInfo.InvariantCulture);
        static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);
        public static double Pct(List<double> v, double p) { if (v.Count == 0) return 0; var a = v.OrderBy(x => x).ToList(); return a[Math.Min(a.Count - 1, (int)Math.Ceiling(p * a.Count) - 1 < 0 ? 0 : (int)Math.Ceiling(p * a.Count) - 1)]; }

        // ---------- f1 coverage ----------
        /// <summary>Random directions in the horizon cap (half uniform, half log-uniform in angle from 50 m) + the given hole-edge / sector vertex directions; returns (checked, violations).</summary>
        public static (int, int) Coverage(CoverageIndex idx, FarCamera cam, int random, IEnumerable<Vec3> extra, ref ulong st)
        {
            Vec3 c = cam.Pos.Normalized(); double rc = cam.Pos.Length, thMax = Math.Acos(CubeSphere.Radius / rc) * 0.999; int n = 0, bad = 0;
            Vec3 e1 = Vec3.Cross(c, Math.Abs(c.Y) < 0.9 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized(), e2 = Vec3.Cross(c, e1);
            for (int i = 0; i < random; i++, n++)
            {
                double th, phi = SplitMix64.NextDouble(ref st) * 2 * Math.PI, u = SplitMix64.NextDouble(ref st);
                if ((i & 1) == 0) th = Math.Acos(1 - u * (1 - Math.Cos(thMax))); else { double lo = Math.Log(50.0 / CubeSphere.Radius), hi = Math.Log(Math.Max(thMax, 100.0 / CubeSphere.Radius)); th = Math.Exp(lo + u * (hi - lo)); }
                if (th > thMax) th = thMax;
                if (idx.Count((c * Math.Cos(th) + (e1 * Math.Cos(phi) + e2 * Math.Sin(phi)) * Math.Sin(th)).Normalized()) != 1) bad++;
            }
            foreach (Vec3 d in extra) { n++; if (idx.Count(d) != 1) bad++; }
            return (n, bad);
        }
        /// <summary>All 65 x 65 vertex directions of every near sector (full) or only the border vertices of the exposed sides (hole edge).</summary>
        public static IEnumerable<Vec3> NearVertices(NearSet near, bool all)
        {
            if (all) { foreach (var s in near.Sectors) { var q = new QuadSector(s); for (int j = 0; j < 65; j++) for (int i = 0; i < 65; i++) yield return q.Direction(i, j); } yield break; }
            foreach (var e in near.Exposed) { var q = new QuadSector(e.Key); for (int k = 0; k < 65; k++) yield return Border(q, e.Value, k); }
        }
        static Vec3 Border(QuadSector q, int side, int k) => side == 0 ? q.Direction(64, k) : side == 1 ? q.Direction(0, k) : side == 2 ? q.Direction(k, 64) : q.Direction(k, 0);

        // ---------- f2 near / far gap on the hole edge ----------
        /// <summary>|near mesh height (float, SurfaceSector) - far mesh radius| on every vertex of every exposed sector side. exact = SurfaceSector.Sample per exposed sector, else the same value from Height cast to float.</summary>
        public static List<double> Gaps(Sim sim, FarCamera cam, bool exact, out int missing)
        {
            var gaps = new List<double>(); missing = 0; var ctx = sim.Ctx(cam); var h = new float[65 * 65]; var k5 = new byte[65 * 65]; var f5 = new float[65 * 65]; SectorId last = default(SectorId); bool have = false;
            foreach (var e in sim.Near.Exposed)
            {
                var q = new QuadSector(e.Key); if (exact && (!have || !last.Equals(e.Key))) { SurfaceSector.Sample(sim.Surf, e.Key, h, k5, f5); last = e.Key; have = true; }
                for (int k = 0; k < 65; k++)
                {
                    int i = e.Value == 0 ? 64 : e.Value == 1 ? 0 : k, j = e.Value == 2 ? 64 : e.Value == 3 ? 0 : k; Vec3 d = q.Direction(i, j);
                    double hn = exact ? (double)h[j * 65 + i] : (double)(float)sim.Surf.Height(d); var id = CubeSphere.RegionOf(d, 13);
                    FarNode node = sim.Layer.Node(id.Face, 13, id.Row, id.Col); double r;
                    if (!node.Built || !FarMesh.RadiusAt(node, d, ctx, out r)) { missing++; continue; }
                    gaps.Add(Math.Abs(CubeSphere.Radius + hn - r));
                }
            }
            return gaps;
        }

        // ---------- f8 shared edge bits (selected, same level, adjacent) ----------
        public static (int, int) EdgeBits(FrameSelection sel, FarLayer layer, in MorphContext c)
        {
            var set = new HashSet<long>(sel.Nodes.Select(x => x.Node.Key)); int cmp = 0, diff = 0;
            foreach (var x in sel.Nodes)
            {
                FarNode a = x.Node;
                foreach (int dir in new[] { 0, 2 })
                {
                    RegionId nb = CubeSphere.Neighbour(new RegionId(a.Face, a.Level, a.Row, a.Col), dir); if (!set.Contains(NodeKey.Pack(nb.Face, nb.Level, nb.Row, nb.Col))) continue;
                    FarNode b = layer.Node(nb.Face, nb.Level, nb.Row, nb.Col);
                    for (int t = 0; t < 17; t++)
                    {
                        int ai = dir == 0 ? 16 : t, aj = dir == 0 ? t : 16; Vec3 da = a.Dir(ai, aj); int found = 0, bi = 0, bj = 0;
                        for (int e = 0; e < 4; e++) for (int u = 0; u < 17; u++) { int ci = e == 0 ? 0 : e == 1 ? 16 : u, cj = e == 2 ? 0 : e == 3 ? 16 : u; Vec3 db = b.Dir(ci, cj); if (db.X == da.X && db.Y == da.Y && db.Z == da.Z) { found++; bi = ci; bj = cj; } }
                        cmp++; Vec3 pa = FarMesh.Morphed(a, ai, aj, c);
                        if (found == 0) { diff++; continue; }
                        Vec3 pb = FarMesh.Morphed(b, bi, bj, c); if (Bits(pa.X) != Bits(pb.X) || Bits(pa.Y) != Bits(pb.Y) || Bits(pa.Z) != Bits(pb.Z) || Bits(a.H[aj * 17 + ai]) != Bits(b.H[bj * 17 + bi])) diff++;
                    }
                }
            }
            return (cmp, diff);
        }

        // ---------- f7 morph continuity on random nodes ----------
        public static IEnumerable<Row> F7(ulong seed)
        {
            var surf = new PlanetSurface(seed); var layer = new FarLayer(surf.Height); ulong st = seed + 77; double worst = 0; int nodes = 0, badBits = 0;
            for (int t = 0; t < 1000; t++)
            {
                int level = 1 + (int)(SplitMix64.Next(ref st) % 13), face = (int)(SplitMix64.Next(ref st) % 6), cells = 1 << level, row = (int)(SplitMix64.Next(ref st) % (ulong)cells), col = (int)(SplitMix64.Next(ref st) % (ulong)cells);
                var n = layer.Node(face, level, row, col); var p = layer.Node(face, level - 1, row >> 1, col >> 1); layer.Build(n); layer.Build(p); nodes++;
                for (int j = 0; j < 17; j++) for (int i = 0; i < 17; i++)
                    {
                        Vec3 d = n.Dir(i, j), own = FarMesh.Morphed(n, i, j, 0.0), want0 = d * (CubeSphere.Radius + surf.Height(d));
                        if (Bits(own.X) != Bits(want0.X) || Bits(own.Y) != Bits(want0.Y) || Bits(own.Z) != Bits(want0.Z)) badBits++;
                        double px = ((col & 1) * 16 + i) / 2.0, py = ((row & 1) * 16 + j) / 2.0; int ix = Math.Min(15, (int)px), iy = Math.Min(15, (int)py); double fx = px - ix, fy = py - iy;
                        Vec3 a = p.Position(ix, iy), dd = p.Position(ix + 1, iy + 1), b = p.Position(ix + 1, iy), cc = p.Position(ix, iy + 1);
                        Vec3 want = fx >= fy ? a + (b - a) * fx + (dd - b) * fy : a + (dd - cc) * fx + (cc - a) * fy;
                        worst = Math.Max(worst, (FarMesh.Morphed(n, i, j, 1.0) - want).Length);
                    }
            }
            yield return Row.Proto("f7.morph_continuity." + seed, $"{F(worst * 1000, "F4")} mm", "< 1 mm", worst < 1e-3, $"{nodes} random nodes of levels 1..13 on all faces: max |vertex at morph 1 - parent mesh (independently built parent node, triangle interpolation)| over 289 vertices each");
            yield return Row.Proto("f7.morph0_bits." + seed, badBits + " differ", "0", badBits == 0, "morph 0: vertex = direction x (R + Height(direction)), double bits, same 1000 nodes");
        }
    }
}
