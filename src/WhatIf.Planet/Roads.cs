using System;
using System.Collections.Generic;

namespace WhatIf.Planet
{
    /// <summary>
    /// A level-10 region border, canonicalised from the LOWER RegionId side (face, row, col order), so both neighbouring regions compute exactly the same
    /// border plane, start vertex and length. A border is a great-circle arc: the equal-angle line u = const is the plane through the origin with normal
    /// U - u N (RG-160 content-roads section 4).
    /// </summary>
    public sealed class Border
    {
        public const int Level = 10;
        public readonly RegionId Lower, Upper; public readonly int Side;      // Side = direction from Lower to Upper (0 = +col, 1 = -col, 2 = +row, 3 = -row)
        public readonly Vec3 S, E, Normal;                                    // start / end vertex (unit), unit plane normal = S x E (S -> E is counter-clockwise about it)
        public readonly double Length;                                        // metres on the sphere
        public readonly long LengthCm;

        Border(RegionId lower, RegionId upper, int side)
        {
            Lower = lower; Upper = upper; Side = side;
            double n = 1 << Level, lo = -1.0 + 2.0 * (side < 2 ? lower.Row : lower.Col) / n, hi = -1.0 + 2.0 * ((side < 2 ? lower.Row : lower.Col) + 1) / n;
            double c = side == 0 ? -1.0 + 2.0 * (lower.Col + 1) / n : side == 1 ? -1.0 + 2.0 * lower.Col / n : side == 2 ? -1.0 + 2.0 * (lower.Row + 1) / n : -1.0 + 2.0 * lower.Row / n;
            if (side < 2) { S = Lattice.Compose(lower.Face, CubeSphere.Unwarp(c), CubeSphere.Unwarp(lo)); E = Lattice.Compose(lower.Face, CubeSphere.Unwarp(c), CubeSphere.Unwarp(hi)); }
            else { S = Lattice.Compose(lower.Face, CubeSphere.Unwarp(lo), CubeSphere.Unwarp(c)); E = Lattice.Compose(lower.Face, CubeSphere.Unwarp(hi), CubeSphere.Unwarp(c)); }
            Normal = Vec3.Cross(S, E).Normalized();
            Length = CubeSphere.Radius * Vec3.Angle(S, E); LengthCm = (long)Math.Floor(Length * 100.0);   // floor: a portal at LengthCm - 256 m is never closer than 256 m to the end vertex
        }

        static bool Less(RegionId a, RegionId b) => a.Face != b.Face ? a.Face < b.Face : a.Row != b.Row ? a.Row < b.Row : a.Col < b.Col;

        /// <summary>The border between two adjacent level-10 regions, in either argument order.</summary>
        public static Border Between(RegionId a, RegionId b)
        {
            RegionId lo = Less(a, b) ? a : b, hi = Less(a, b) ? b : a;
            for (int d = 0; d < 4; d++) if (CubeSphere.Neighbour(lo, d).Equals(hi)) return new Border(lo, hi, d);
            throw new ArgumentException("regions are not adjacent: " + a + " " + b);
        }
        public static bool Adjacent(RegionId a, RegionId b)
        {
            for (int d = 0; d < 4; d++) if (CubeSphere.Neighbour(a, d).Equals(b)) return true;
            return false;
        }

        public string Id => $"F{Lower.Face}.L{Level}.r{Lower.Row}.c{Lower.Col}.s{Side}";

        /// <summary>Unit direction at arc length s (metres) from the start vertex.</summary>
        public Vec3 PointAt(double s)
        {
            double phi = s / CubeSphere.Radius, cs = Math.Cos(phi), sn = Math.Sin(phi);
            return S * cs + Vec3.Cross(Normal, S) * sn;
        }

        /// <summary>Arc length (metres) from the start vertex of the point on the border nearest to p.</summary>
        public double ArcLength(Vec3 p) => CubeSphere.Radius * Math.Atan2(Vec3.Dot(Vec3.Cross(S, p), Normal), Vec3.Dot(S, p));

        /// <summary>The same border seen from one of its two regions' own face basis: start/end are the side's own vertices (swapped to the canonical order).</summary>
        public static void OwnView(RegionId self, RegionId other, Border canon, out Vec3 s, out Vec3 e)
        {
            int d = -1; for (int k = 0; k < 4; k++) if (CubeSphere.Neighbour(self, k).Equals(other)) d = k;
            if (d < 0) throw new ArgumentException("not adjacent");
            int cu0, cv0, cu1, cv1;
            if (d == 0) { cu0 = 1; cv0 = 0; cu1 = 1; cv1 = 1; }
            else if (d == 1) { cu0 = 0; cv0 = 0; cu1 = 0; cv1 = 1; }
            else if (d == 2) { cu0 = 0; cv0 = 1; cu1 = 1; cv1 = 1; } else { cu0 = 0; cv0 = 0; cu1 = 1; cv1 = 0; }
            Vec3 a = CubeSphere.RegionCorner(self, cu0, cv0), b = CubeSphere.RegionCorner(self, cu1, cv1);
            if ((a - canon.S).Length <= (b - canon.S).Length) { s = a; e = b; } else { s = b; e = a; }
        }
    }

    /// <summary>
    /// A road crossing a region border: borderId, s = arc length from the border's canonical start vertex quantised to 1 cm (int64), the heading relative to
    /// the border tangent. Both regions derive the same record because it is computed on the canonical border only.
    /// </summary>
    public readonly struct Portal
    {
        public readonly Border Border; public readonly long SCm; public readonly double Heading;
        public Portal(Border b, long sCm, double heading) { Border = b; SCm = sCm; Heading = heading; }
        public Vec3 Position => Border.PointAt(SCm / 100.0);

        /// <summary>Crossing of a road great circle (unit normal nr, travel direction = nr x p) with a border; q = approximate crossing (picks one of the two antipodal solutions).</summary>
        public static Portal Cross(Border b, Vec3 nr, Vec3 q)
        {
            Vec3 p = Vec3.Cross(nr, b.Normal).Normalized(); if (Vec3.Dot(p, q) < 0) p = -p;
            long s = (long)Math.Round(b.ArcLength(p) * 100.0);
            return new Portal(b, s, HeadingAt(b.Normal, p, nr));
        }

        /// <summary>Heading (rad) of the road relative to the border tangent t = n x p at p, measured towards m = p x t.</summary>
        public static double HeadingAt(Vec3 borderNormal, Vec3 p, Vec3 roadNormal)
        {
            Vec3 t = Vec3.Cross(borderNormal, p).Normalized(), m = Vec3.Cross(p, t), w = Vec3.Cross(roadNormal, p).Normalized();
            return Math.Atan2(Vec3.Dot(w, m), Vec3.Dot(w, t));
        }

        /// <summary>Snap (RG-160 section 4): a portal closer than minDist to a region vertex moves along the border to exactly minDist from it.</summary>
        public static long Snap(Border b, long sCm, double minDist)
        {
            long m = (long)Math.Round(minDist * 100.0);
            if (sCm < m) return m; if (sCm > b.LengthCm - m) return b.LengthCm - m; return sCm;
        }
    }

    /// <summary>Arc of a great circle between two unit vectors and the region crossings along it.</summary>
    public static class RoadArc
    {
        public struct Crossing { public RegionId From, To; public Vec3 Q; public bool Resolved; }

        public static Vec3 At(Vec3 a, Vec3 b, double theta, double t) =>
            theta < 1e-15 ? a : (a * Math.Sin((1 - t) * theta) + b * Math.Sin(t * theta)) / Math.Sin(theta);

        /// <summary>End point of a great-circle arc of the given length starting at a unit vector with a heading hint.</summary>
        public static Vec3 Advance(Vec3 a, Vec3 hint, double lengthM)
        {
            Vec3 t = (hint - a * Vec3.Dot(hint, a)).Normalized(); double phi = lengthM / CubeSphere.Radius;
            return (a * Math.Cos(phi) + t * Math.Sin(phi)).Normalized();
        }

        const long TolCm = 5;      // quantisation tolerance for "the crossing lies on the border segment"

        /// <summary>Crossing of the road great circle (normal nr) with the border between two adjacent regions, near q; ok = lies on the border segment (0 <= s <= LengthCm, within TolCm).</summary>
        static bool OnSegment(RegionId r0, RegionId r1, Vec3 nr, Vec3 q, out Vec3 pos, out double violationCm)
        {
            Border bd = Border.Between(r0, r1); Portal p = Portal.Cross(bd, nr, q); pos = bd.PointAt(p.SCm / 100.0);
            violationCm = Math.Max(Math.Max(0, -p.SCm), Math.Max(0, p.SCm - bd.LengthCm));
            return violationCm <= TolCm;
        }

        /// <summary>Level-10 region crossings along the arc a -> b (step 500 m, then halving until the two regions are adjacent). Unresolved = region jump that never became adjacent.
        /// A crossing is only emitted for a border segment the road really crosses (RG-163): when two adjacent regions are separated by a crossing that lies outside their
        /// shared segment, the road passes a vertex through a third region adjacent to both, and the two crossings are emitted through that region.</summary>
        public static List<Crossing> Crossings(Vec3 a, Vec3 b)
        {
            var list = new List<Crossing>(); double theta = Vec3.Angle(a, b); Vec3 nr = Vec3.Cross(a, b).Normalized();
            int steps = Math.Max(1, (int)Math.Ceiling(theta * CubeSphere.Radius / 500.0));
            RegionId prev = CubeSphere.RegionOf(a, Border.Level); double tPrev = 0;
            for (int k = 1; k <= steps; k++)
            {
                double t = (double)k / steps; RegionId r = CubeSphere.RegionOf(At(a, b, theta, t), Border.Level);
                if (!r.Equals(prev)) Resolve(a, b, nr, theta, tPrev, t, prev, r, list, 0);
                prev = r; tPrev = t;
            }
            return list;
        }

        /// <summary>Emit r0 -> m -> r1 for the region m (neighbour of r0 and of r1) whose two border segments the road really crosses; false when there is none.</summary>
        static bool Via(RegionId r0, RegionId r1, Vec3 nr, Vec3 qm, List<Crossing> list)
        {
            double bestViol = double.MaxValue; RegionId bm = r0; Vec3 q1 = qm, q2 = qm; bool found = false;
            for (int d = 0; d < 4; d++)
            {
                RegionId m = CubeSphere.Neighbour(r0, d); if (m.Equals(r1) || m.Equals(r0) || !Border.Adjacent(m, r1)) continue;
                Vec3 p1, p2; double v1, v2; OnSegment(r0, m, nr, qm, out p1, out v1); OnSegment(m, r1, nr, qm, out p2, out v2);
                double v = Math.Max(v1, v2); if (v < bestViol) { bestViol = v; bm = m; q1 = p1; q2 = p2; found = true; }
            }
            if (!found || bestViol > TolCm) return false;
            list.Add(new Crossing { From = r0, To = bm, Q = q1, Resolved = true }); list.Add(new Crossing { From = bm, To = r1, Q = q2, Resolved = true });
            return true;
        }

        static void Resolve(Vec3 a, Vec3 b, Vec3 nr, double theta, double t0, double t1, RegionId r0, RegionId r1, List<Crossing> list, int depth)
        {
            Vec3 qm = At(a, b, theta, 0.5 * (t0 + t1));
            if (Border.Adjacent(r0, r1))
            {
                Vec3 pos; double viol;
                if (OnSegment(r0, r1, nr, qm, out pos, out viol)) { list.Add(new Crossing { From = r0, To = r1, Q = qm, Resolved = true }); return; }
                // the shared segment is not crossed: the road runs through a region m adjacent to both (cube vertex, or a pass beside a region vertex)
                if (!Via(r0, r1, nr, qm, list)) list.Add(new Crossing { From = r0, To = r1, Q = qm, Resolved = true });   // no consistent attribution: stays off-segment, counted by the experiment
                return;
            }
            if (depth > 40)
            {
                // the road passes (within 1e-12 rad of) a region vertex diagonally: attribute it to the region m that both borders' segments contain
                if (!Via(r0, r1, nr, qm, list)) list.Add(new Crossing { From = r0, To = r1, Q = qm, Resolved = false });
                return;
            }
            double tm = 0.5 * (t0 + t1); RegionId rm = CubeSphere.RegionOf(At(a, b, theta, tm), Border.Level);
            if (!rm.Equals(r0)) Resolve(a, b, nr, theta, t0, tm, r0, rm, list, depth + 1);
            if (!rm.Equals(r1)) Resolve(a, b, nr, theta, tm, t1, rm, r1, list, depth + 1);
        }

        /// <summary>A crossing placed on its canonical border: raw s, final s after the joint snap, the road normal the final s was computed from.</summary>
        public sealed class Placed
        {
            public Crossing C; public Border B; public long SRawCm, SCm; public Vec3 Normal; public bool Grouped, ShiftedLine;
            public bool OffSegment => B != null && (SRawCm < -TolCm || SRawCm > B.LengthCm + TolCm);
        }

        /// <summary>
        /// Crossings of the arc a -> b placed on their borders. minDist > 0 = joint snap (RG-163): all crossings within minDist of the SAME region vertex form a group; the
        /// road's closest approach to that vertex is moved radially away to minDist + 5 cm on the side the road already passes (the side of the traversed region when it
        /// passes through the vertex), keeping its heading there, and the group's crossings are recomputed on the shifted great circle (canonical s, int64 cm).
        /// </summary>
        static double PathLength(Vec3 a, Vec3 b, List<Placed> res)
        {
            double len = 0; Vec3 prev = a;
            foreach (var pl in res) { if (!pl.C.Resolved) continue; Vec3 p = pl.B.PointAt(pl.SCm / 100.0); len += Vec3.Angle(prev, p); prev = p; }
            return (len + Vec3.Angle(prev, b)) * CubeSphere.Radius;
        }

        public static List<Placed> Route(Vec3 a, Vec3 b, double minDist)
        {
            Vec3 nr = Vec3.Cross(a, b).Normalized(); var res = new List<Placed>();
            foreach (var c in Crossings(a, b))
            {
                var pl = new Placed { C = c, Normal = nr, B = c.Resolved ? Border.Between(c.From, c.To) : null };
                if (c.Resolved) { Portal p = Portal.Cross(pl.B, nr, c.Q); pl.SRawCm = p.SCm; pl.SCm = p.SCm; }
                res.Add(pl);
            }
            if (minDist <= 0) return res;
            var verts = new List<Vec3>(); var groups = new List<List<Placed>>();
            foreach (var pl in res)
            {
                if (!pl.C.Resolved) continue;
                for (int k = 0; k < 2; k++)
                {
                    double dist = (k == 0 ? pl.SRawCm : pl.B.LengthCm - pl.SRawCm) / 100.0; if (dist >= minDist) continue;
                    Vec3 v = k == 0 ? pl.B.S : pl.B.E; int gi = verts.FindIndex(x => (x - v).Length * CubeSphere.Radius < 1.0);
                    if (gi < 0) { verts.Add(v); groups.Add(new List<Placed>()); gi = verts.Count - 1; }
                    groups[gi].Add(pl);
                }
            }
            for (int gi = 0; gi < groups.Count; gi++)
            {
                Vec3 v = verts[gi]; var grp = groups[gi]; double vn = Vec3.Dot(v, nr);
                Vec3 ut = (nr - v * Vec3.Dot(nr, v)).Normalized(), c0 = (v - nr * vn).Normalized(), t = Vec3.Cross(nr, c0).Normalized(); double sigma;
                if (Math.Abs(vn) * CubeSphere.Radius >= 1.0) sigma = vn > 0 ? -1 : 1;          // the closest approach is on the side c0 - v ~ -vn * nr
                else
                {
                    double sum = 0;                                                             // through the vertex: go to the side of the borders the road crosses
                    foreach (var pl in grp)
                    {
                        Vec3 w = (pl.B.S - v).Length < (pl.B.E - v).Length ? Vec3.Cross(pl.B.Normal, pl.B.S).Normalized() : -Vec3.Cross(pl.B.Normal, pl.B.E).Normalized();
                        sum += Vec3.Dot(ut, w);
                    }
                    sigma = sum >= 0 ? 1 : -1;
                }
                double phi = (minDist + 0.05) / CubeSphere.Radius; Vec3 u = ut * sigma;
                Vec3 c1 = (v * Math.Cos(phi) + u * Math.Sin(phi)).Normalized(), t1 = (t - c1 * Vec3.Dot(t, c1)).Normalized(), n1 = Vec3.Cross(c1, t1).Normalized();
                // two candidates for the group: (A) the shifted great circle, (B) each portal moved along its border away from the vertex. A is right for a pass through
                // the corner of a region (both portals move to the same side); B is right for a road nearly tangent to a border, where A would slide the portal km along
                // the border. The candidate with the shorter whole polyline (the smaller length change against the true arc) wins; a candidate that needs a clamp loses.
                var sA = new long[grp.Count]; var sB = new long[grp.Count]; bool aOk = true;
                for (int k = 0; k < grp.Count; k++)
                {
                    var pl = grp[k]; sA[k] = Portal.Cross(pl.B, n1, pl.B.PointAt(pl.SRawCm / 100.0)).SCm;
                    if (Portal.Snap(pl.B, sA[k], minDist) != sA[k]) aOk = false;
                    sB[k] = Portal.Snap(pl.B, pl.SRawCm, minDist);
                }
                double lenA = double.MaxValue, lenB;
                if (aOk) { for (int k = 0; k < grp.Count; k++) grp[k].SCm = sA[k]; lenA = PathLength(a, b, res); }
                for (int k = 0; k < grp.Count; k++) grp[k].SCm = sB[k]; lenB = PathLength(a, b, res);
                bool useA = aOk && lenA <= lenB;
                for (int k = 0; k < grp.Count; k++) { grp[k].SCm = useA ? sA[k] : sB[k]; grp[k].Normal = useA ? n1 : nr; grp[k].Grouped = true; grp[k].ShiftedLine = useA; }
            }
            return res;
        }
    }

    public enum RoadClass : byte { Motorway = 0, Primary = 1 }

    /// <summary>
    /// Global corridor graph (RG-160 decision D3: main corridors only). Settlements: 2 per level-6 cell (49 152, about one per (100 km)^2 on the whole
    /// sphere, no land mask), jittered inside the cell, deterministic from the world seed (SplitMix64); node id = cell * 2 + k. Edges: every node to its
    /// 3 nearest within the 3 x 3 cells around it (undirected, deduplicated), a great-circle arc each; motorway when both ends are hubs, else primary.
    /// </summary>
    public sealed class CorridorGraph
    {
        public const int CellLevel = 6, PerCell = 2;
        public Vec3[] Nodes; public int[] EdgeA, EdgeB; public RoadClass[] Class;
        public int NodeCount => Nodes.Length; public int EdgeCount => EdgeA.Length;
        public long Bytes => Nodes.Length * 24L + EdgeA.Length * 9L;

        static bool Hub(int id) => (SplitMix64.Mix((ulong)id * 0x9E3779B97F4A7C15UL) & 3) == 0;

        public static CorridorGraph Build(ulong worldSeed)
        {
            int n = 1 << CellLevel, cells = 6 * n * n;
            var nodes = new Vec3[cells * PerCell];
            for (int c = 0; c < cells; c++)
            {
                int face = c / (n * n), row = (c / n) % n, col = c % n;
                ulong st = worldSeed ^ ((ulong)(c + 1) * 0xD6E8FEB86659FD93UL);
                for (int k = 0; k < PerCell; k++)
                {
                    double fa = 0.1 + 0.8 * SplitMix64.NextDouble(ref st), fb = 0.1 + 0.8 * SplitMix64.NextDouble(ref st);   // stay 10 % away from the cell border
                    nodes[c * PerCell + k] = Lattice.Compose(face, CubeSphere.Unwarp(-1.0 + 2.0 * (col + fa) / n), CubeSphere.Unwarp(-1.0 + 2.0 * (row + fb) / n));
                }
            }
            var keys = new long[nodes.Length * 3]; int nk = 0;
            var around = new int[9]; var best = new int[3]; var bestD = new double[3];
            for (int c = 0; c < cells; c++)
            {
                int cnt = 0; around[cnt++] = c; var self = new RegionId(c / (n * n), CellLevel, (c / n) % n, c % n);
                for (int d = 0; d < 4; d++)
                {
                    RegionId nb = CubeSphere.Neighbour(self, d); Add(around, ref cnt, Cell(nb, n));
                    for (int d2 = 0; d2 < 4; d2++) { RegionId nb2 = CubeSphere.Neighbour(nb, d2); Add(around, ref cnt, Cell(nb2, n)); }
                }
                for (int k = 0; k < PerCell; k++)
                {
                    int id = c * PerCell + k; Vec3 p = nodes[id]; int found = 0;
                    for (int q = 0; q < cnt && q < 9; q++)
                        for (int m = 0; m < PerCell; m++)
                        {
                            int o = around[q] * PerCell + m; if (o == id) continue;
                            double dd = (nodes[o] - p).Length;
                            int pos = found; while (pos > 0 && bestD[pos - 1] > dd) pos--;
                            if (pos >= 3) continue;
                            for (int z = Math.Min(found, 2); z > pos; z--) { best[z] = best[z - 1]; bestD[z] = bestD[z - 1]; }
                            best[pos] = o; bestD[pos] = dd; if (found < 3) found++;
                        }
                    for (int z = 0; z < found; z++) keys[nk++] = (long)Math.Min(id, best[z]) * nodes.Length + Math.Max(id, best[z]);
                }
            }
            Array.Sort(keys, 0, nk);
            int ne = 0; for (int i = 0; i < nk; i++) if (i == 0 || keys[i] != keys[i - 1]) ne++;
            var g = new CorridorGraph { Nodes = nodes, EdgeA = new int[ne], EdgeB = new int[ne], Class = new RoadClass[ne] };
            int e = 0;
            for (int i = 0; i < nk; i++)
            {
                if (i > 0 && keys[i] == keys[i - 1]) continue;
                int a = (int)(keys[i] / nodes.Length), b = (int)(keys[i] % nodes.Length);
                g.EdgeA[e] = a; g.EdgeB[e] = b; g.Class[e] = Hub(a) && Hub(b) ? RoadClass.Motorway : RoadClass.Primary; e++;
            }
            return g;
        }
        static int Cell(RegionId r, int n) => (r.Face * n + r.Row) * n + r.Col;
        static void Add(int[] arr, ref int cnt, int cell)
        {
            for (int i = 0; i < cnt; i++) if (arr[i] == cell) return;
            if (cnt < arr.Length) arr[cnt++] = cell;
        }
    }

    /// <summary>
    /// Corridor refinement inside one region (RG-160 section 4, r4): straight legs from portal to junction to portal in the region's gnomonic plane (a great
    /// circle is a straight line there), the turn filleted with a circle of the class minimum horizontal radius, everything sampled at 4 m.
    /// </summary>
    public sealed class RegionPlane
    {
        // Class minimum horizontal radii: artifacts/worldgen/world-rules.json classes.<c>.radius.min_radius_m, sha256 24c9a9c6...c723 (git-ignored file, values copied).
        public const double MotorwayMinRadius = 344.83, PrimaryMinRadius = 121.31, Step = 4.0;
        public readonly Vec3 C, E, N;
        public RegionPlane(RegionId r) { C = CubeSphere.RegionCentre(r); CubeSphere.TangentAxes(r.Face, C, out E, out N); }
        public double[] ToLocal(Vec3 dir) { double w = Vec3.Dot(dir, C); return new[] { Vec3.Dot(dir, E) / w * CubeSphere.Radius, Vec3.Dot(dir, N) / w * CubeSphere.Radius }; }
        public Vec3 ToDir(double x, double z) => (C * CubeSphere.Radius + E * x + N * z).Normalized();

        /// <summary>Polyline p1 -> junction -> p2 with a fillet of the given radius; null when the legs are too short for the fillet.</summary>
        public static List<double[]> Fillet(double[] p1, double[] j, double[] p2, double radius)
        {
            double l1 = Dist(p1, j), l2 = Dist(j, p2);
            double d1x = (j[0] - p1[0]) / l1, d1y = (j[1] - p1[1]) / l1, d2x = (p2[0] - j[0]) / l2, d2y = (p2[1] - j[1]) / l2;
            double cross = d1x * d2y - d1y * d2x, dot = d1x * d2x + d1y * d2y, delta = Math.Atan2(Math.Abs(cross), dot);
            double t = radius * Math.Tan(delta / 2);
            if (t >= l1 || t >= l2) return null;
            var pts = new List<double[]>();
            double[] t1 = { j[0] - d1x * t, j[1] - d1y * t }, t2 = { j[0] + d2x * t, j[1] + d2y * t };
            Straight(pts, p1, t1);
            double sgn = cross >= 0 ? 1 : -1, nx = -d1y * sgn, ny = d1x * sgn;                 // unit normal from the leg towards the circle centre
            double cx = t1[0] + nx * radius, cy = t1[1] + ny * radius, a0 = Math.Atan2(t1[1] - cy, t1[0] - cx);
            int m = Math.Max(1, (int)Math.Ceiling(radius * delta / Step));
            for (int k = 1; k < m; k++) { double a = a0 + sgn * delta * k / m; pts.Add(new[] { cx + radius * Math.Cos(a), cy + radius * Math.Sin(a) }); }
            if (t > 1e-9) pts.Add(t2);
            var tail = new List<double[]>(); Straight(tail, t2, p2); tail.RemoveAt(0); pts.AddRange(tail);
            return pts;
        }
        static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));
        static void Straight(List<double[]> pts, double[] a, double[] b)
        {
            double len = Dist(a, b); int m = Math.Max(1, (int)Math.Ceiling(len / Step));
            for (int k = 0; k <= m; k++) { if (k == 0 && pts.Count > 0) continue; pts.Add(new[] { a[0] + (b[0] - a[0]) * k / m, a[1] + (b[1] - a[1]) * k / m }); }
        }
    }
}
