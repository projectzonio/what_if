using System;
using System.Collections.Generic;

namespace WhatIf.Planet.FarLayer
{
    /// <summary>Protocol checks of a frame selection, shared by the unit tests and the experiment console (rows f1, f8).</summary>
    public sealed class CoverageIndex
    {
        readonly Dictionary<long, ushort> selected = new Dictionary<long, ushort>(); readonly NearSet near;
        public CoverageIndex(FrameSelection s, NearSet n) { near = n; foreach (var x in s.Nodes) selected[x.Node.Key] = x.Mask; }
        public bool TryGet(long key, out ushort mask) => selected.TryGetValue(key, out mask);

        /// <summary>
        /// How often direction d is covered: unmasked far quads of selected nodes containing it + 1 if its level-15 sector is in the near set (half-open cells,
        /// so a lattice point belongs to exactly one quad and one sector). The protocol wants exactly 1 for every direction that is visible.
        /// </summary>
        public int Count(Vec3 d)
        {
            int face; double a, b; CubeSphere.ToFaceAB(d, out face, out a, out b); int c = 0;
            for (int l = 0; l <= 15; l++)
            {
                int n = 1 << l, col = Math.Min(n - 1, Math.Max(0, (int)Math.Floor((a + 1.0) * 0.5 * n))), row = Math.Min(n - 1, Math.Max(0, (int)Math.Floor((b + 1.0) * 0.5 * n)));
                if (l == 15) { if (near.Contains(new SectorId(face, row, col))) c++; break; }
                ushort m; if (!selected.TryGetValue(NodeKey.Pack(face, l, row, col), out m)) continue;
                if (l == 13)
                {
                    int ns = 1 << 15, sc = Math.Min(ns - 1, Math.Max(0, (int)Math.Floor((a + 1.0) * 0.5 * ns))), sr = Math.Min(ns - 1, Math.Max(0, (int)Math.Floor((b + 1.0) * 0.5 * ns)));
                    if (((m >> (((sr & 3) << 2) | (sc & 3))) & 1) != 0) continue;     // that sector's quads are not drawn
                }
                c++;
            }
            return c;
        }
    }

    public static class SeamChecks
    {
        /// <summary>Adjacent selected nodes (same face or across a cube edge) that differ by more than one level, seen from the finer node.</summary>
        public static int LevelViolations(FrameSelection s)
        {
            var sel = new HashSet<long>(); foreach (var x in s.Nodes) sel.Add(x.Node.Key); int bad = 0;
            foreach (var x in s.Nodes)
            {
                FarNode n = x.Node;
                for (int dir = 0; dir < 4; dir++)
                {
                    RegionId nb = CubeSphere.Neighbour(new RegionId(n.Face, n.Level, n.Row, n.Col), dir);
                    if (sel.Contains(NodeKey.Pack(nb.Face, nb.Level, nb.Row, nb.Col))) continue;
                    for (int k = 1; k <= nb.Level; k++)
                        if (sel.Contains(NodeKey.Pack(nb.Face, nb.Level - k, nb.Row >> k, nb.Col >> k))) { if (k >= 2) bad++; break; }
                }
            }
            return bad;
        }

        /// <summary>
        /// T-junction gap (m): for every selected fine node next to a selected coarse node one level up, the distance of each fine edge vertex (morphed with c) to the
        /// polyline of the coarse node's edges (morphed with c). pairs = number of fine/coarse edges examined.
        /// </summary>
        public static double MaxTJunctionGap(FrameSelection s, FarLayer layer, in MorphContext c, out int pairs)
        {
            var sel = new HashSet<long>(); foreach (var x in s.Nodes) sel.Add(x.Node.Key); double worst = 0; pairs = 0;
            foreach (var x in s.Nodes)
            {
                FarNode f = x.Node; if (f.Level == 0) continue;
                for (int dir = 0; dir < 4; dir++)
                {
                    RegionId nb = CubeSphere.Neighbour(new RegionId(f.Face, f.Level, f.Row, f.Col), dir);
                    if (sel.Contains(NodeKey.Pack(nb.Face, nb.Level, nb.Row, nb.Col))) continue;
                    long ck = NodeKey.Pack(nb.Face, nb.Level - 1, nb.Row >> 1, nb.Col >> 1); if (!sel.Contains(ck)) continue;
                    FarNode co = layer.Node(nb.Face, nb.Level - 1, nb.Row >> 1, nb.Col >> 1); pairs++;
                    var ring = new List<Vec3>(); for (int k = 0; k < 4; k++) for (int t = 0; t <= FarNode.Grid; t++) ring.Add(EdgeVertex(co, k, t, c));   // 4 edges
                    for (int t = 0; t <= FarNode.Grid; t++)
                    {
                        Vec3 p = dir == 0 ? FarMesh.Morphed(f, 16, t, c) : dir == 1 ? FarMesh.Morphed(f, 0, t, c) : dir == 2 ? FarMesh.Morphed(f, t, 16, c) : FarMesh.Morphed(f, t, 0, c); double best = double.MaxValue;
                        for (int k = 0; k < 4; k++) for (int e = 0; e < FarNode.Grid; e++) best = Math.Min(best, SegDist(p, ring[k * 17 + e], ring[k * 17 + e + 1]));
                        worst = Math.Max(worst, best);
                    }
                }
            }
            return worst;
        }
        static Vec3 EdgeVertex(FarNode n, int edge, int t, in MorphContext c) => edge == 0 ? FarMesh.Morphed(n, 0, t, c) : edge == 1 ? FarMesh.Morphed(n, 16, t, c) : edge == 2 ? FarMesh.Morphed(n, t, 0, c) : FarMesh.Morphed(n, t, 16, c);
        static double SegDist(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 ab = b - a; double l2 = Vec3.Dot(ab, ab), t = l2 > 0 ? Math.Max(0, Math.Min(1, Vec3.Dot(p - a, ab) / l2)) : 0; return (p - (a + ab * t)).Length;
        }
    }
}
