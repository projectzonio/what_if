using System;
using System.Collections.Generic;
using System.Diagnostics;
using WhatIf.Planet;

namespace WhatIf.Planet.Experiment
{
    /// <summary>Road experiment r1-r5 (RG-160 content-roads section 4): portals on level-10 borders, snap, heading, corner-region refinement, global corridor graph.</summary>
    public static class RoadChecks
    {
        static readonly double R = CubeSphere.Radius;
        static Row Mk(string check, string seed, string metric, string value, string thr, bool pass, bool diag = false) =>
            new Row { Check = check, Region = "global", Seed = seed, Metric = metric, Measured = value, Threshold = thr, Pass = pass, Diag = diag };

        static Vec3 RandUnit(ref ulong st)
        {
            for (; ; )
            {
                var v = new Vec3(SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1);
                double l = v.Length; if (l > 0.05 && l <= 1) return v / l;
            }
        }

        /// <summary>A great-circle arc of the given length that passes through p with the heading hint, p at its middle.</summary>
        static void ArcThrough(Vec3 p, Vec3 hint, double length, out Vec3 a, out Vec3 b)
        {
            a = RoadArc.Advance(p, -hint, length * 0.5); b = RoadArc.Advance(p, hint, length * 0.5);
        }

        // ---------------------------------------------------------------- arc sets
        static List<Vec3[]> ArcsR1(int total)
        {
            var arcs = new List<Vec3[]>(); ulong st = 0x1234ABCD;
            for (int i = 0; i < total; i++)
            {
                Vec3 a, b;
                if (i % 3 != 2)
                {
                    // through a random point of one of the 12 cube edges (24 face rims), heading within +-60 deg of the edge normal
                    int f = (int)(SplitMix64.Next(ref st) % 6), side = (int)(SplitMix64.Next(ref st) % 4); double t = SplitMix64.NextDouble(ref st) * 2 - 1;
                    Vec3 w = side == 0 ? CubeSphere.AxisU(f) : side == 1 ? -CubeSphere.AxisU(f) : side == 2 ? CubeSphere.AxisV(f) : -CubeSphere.AxisV(f);
                    Vec3 along = side < 2 ? CubeSphere.AxisV(f) : CubeSphere.AxisU(f);
                    Vec3 p = (CubeSphere.Normal(f) + w + along * CubeSphere.Unwarp(t)).Normalized();
                    Vec3 perp = (w - p * Vec3.Dot(w, p)).Normalized(); double ang = (SplitMix64.NextDouble(ref st) - 0.5) * 2 * Math.PI / 3;
                    Vec3 hint = perp * Math.Cos(ang) + Vec3.Cross(p, perp) * Math.Sin(ang);
                    ArcThrough(p, hint, 20000 + SplitMix64.NextDouble(ref st) * 60000, out a, out b);
                }
                else
                {
                    Vec3 p = RandUnit(ref st), hint = RandUnit(ref st);
                    ArcThrough(p, hint, 20000 + SplitMix64.NextDouble(ref st) * 60000, out a, out b);
                }
                arcs.Add(new[] { a, b });
            }
            return arcs;
        }

        static List<Vec3[]> ArcsR2(int total)
        {
            var arcs = new List<Vec3[]>(); ulong st = 0x5EED0002; var cubeV = new List<Vec3>();
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2) cubeV.Add(new Vec3(sx, sy, sz).Normalized());
            for (int i = 0; i < total; i++)
            {
                Vec3 v;
                if (i % 3 == 0) v = cubeV[(i / 3) % 8];
                else
                {
                    int f = (int)(SplitMix64.Next(ref st) % 6), ci = (int)(SplitMix64.Next(ref st) % 1025), cj = (int)(SplitMix64.Next(ref st) % 1025);
                    v = Lattice.Compose(f, CubeSphere.Unwarp(-1.0 + 2.0 * ci / 1024), CubeSphere.Unwarp(-1.0 + 2.0 * cj / 1024));
                }
                Vec3 p = RoadArc.Advance(v, RandUnit(ref st), SplitMix64.NextDouble(ref st) * 200.0);      // within 200 m of the vertex
                Vec3 a, b; ArcThrough(p, RandUnit(ref st), 30000, out a, out b);
                arcs.Add(new[] { a, b });
            }
            return arcs;
        }

        // ---------------------------------------------------------------- one side's view of a portal
        static Vec3 OwnPos(RegionId self, RegionId other, Border canon, long sCm, out Vec3 n)
        {
            Vec3 s, e; Border.OwnView(self, other, canon, out s, out e);
            n = Vec3.Cross(s, e).Normalized(); double phi = sCm / 100.0 / R;
            return s * Math.Cos(phi) + Vec3.Cross(n, s) * Math.Sin(phi);
        }

        sealed class Stats
        {
            public long Arcs, Portals, Unresolved, SMismatch, Snapped, LenOver, OffSegment, Clamped; public string MaxShiftArc = "", MaxLenArc = ""; public double MaxLenPctAll; public long Eligible, Excluded, EligibleOver; public double MaxLenPctEligible, MaxLenPctExcluded, MaxSnapB, MaxLateralA, MaxSlideA; public string MaxLenEligibleArc = "";
            public double MaxPosMm, MaxHeadDiff, MaxQuantMm, MaxShift, MaxLenPct, MinVertexDist = 1e30;
            public Dictionary<string, int> CubeEdges = new Dictionary<string, int>(); public long BorderCrossings;
        }

        static string Detail(List<RoadArc.Placed> cr, List<Vec3> pts, double radius, bool arcOff)
        {
            var sb = new System.Text.StringBuilder($"; a -> first portal {Vec3.Angle(pts[0], pts[1]) * radius:F0} m, last portal -> b {Vec3.Angle(pts[pts.Count - 2], pts[pts.Count - 1]) * radius:F0} m; portals:");
            foreach (var pl in cr) if (pl.C.Resolved)
                {
                    double raw = Math.Min(pl.SRawCm, pl.B.LengthCm - pl.SRawCm) / 100.0, fin = Math.Min(pl.SCm, pl.B.LengthCm - pl.SCm) / 100.0;
                    sb.Append($" [{pl.B.Id} raw {raw:F1} m from the vertex -> {fin:F1} m, moved {Math.Abs(pl.SCm - pl.SRawCm) / 100.0:F1} m{(pl.ShiftedLine ? " (shifted line)" : pl.Grouped ? " (along the border)" : "")}]");
                }
            return sb.ToString() + (arcOff ? " (has off-segment portal)" : "");
        }

        static string Describe(Vec3 a, Vec3 b)
        {
            Vec3 m = RoadArc.At(a, b, Vec3.Angle(a, b), 0.5); double best = 1e30; Vec3 bv = a; Vec3 nr = Vec3.Cross(a, b).Normalized();
            RegionId r = CubeSphere.RegionOf(m, Border.Level);
            for (int cu = 0; cu <= 1; cu++) for (int cv = 0; cv <= 1; cv++) { Vec3 v = CubeSphere.RegionCorner(r, cu, cv); double d = Math.Abs(Vec3.Dot(v, nr)) * R; if (d < best) { best = d; bv = v; } }
            return $"a=({a.X:R},{a.Y:R},{a.Z:R}) b=({b.X:R},{b.Y:R},{b.Z:R}) len {Vec3.Angle(a, b) * R:F0} m, mid-region corner nearest to the road circle at {best:F1} m";
        }

        /// <summary>RG-164 metric (user 2026-11-12): an arc counts for the r2 length row when both its ends are >= 1 km (along the true arc) from the nearest shifted portal; no shifted portal = eligible.</summary>
        public static bool Eligible(double firstShiftM, double lastShiftToBM) => firstShiftM < 0 || (firstShiftM >= 1000.0 && lastShiftToBM >= 1000.0);

        /// <summary>Distance along the true arc from a to the first shifted portal (> 1 cm from its raw crossing) and from the last one to b; -1 / -1 when no portal moved.</summary>
        public static void ShiftEnds(Vec3 a, Vec3 b, List<RoadArc.Placed> cr, out double firstShiftM, out double lastShiftToBM)
        {
            firstShiftM = -1; lastShiftToBM = -1;
            foreach (var pl in cr)
            {
                if (!pl.C.Resolved || Math.Abs(pl.SCm - pl.SRawCm) <= 1) continue;
                Vec3 rawP = pl.B.PointAt(pl.SRawCm / 100.0); if (firstShiftM < 0) firstShiftM = Vec3.Angle(a, rawP) * R; lastShiftToBM = Vec3.Angle(rawP, b) * R;
            }
        }

        static Stats Analyse(List<Vec3[]> arcs, bool snap)
        {
            var st = new Stats();
            foreach (var arc in arcs)
            {
                bool arcOff = false;
                Vec3 a = arc[0], b = arc[1], nr = Vec3.Cross(a, b).Normalized(); st.Arcs++;
                var cr = RoadArc.Route(a, b, snap ? 256.0 : 0.0); var pts = new List<Vec3> { a };
                Vec3 prev = a;
                foreach (var pl in cr)
                {
                    var c = pl.C;
                    if (!c.Resolved) { st.Unresolved++; continue; }
                    Border bx = pl.B, by = Border.Between(c.To, c.From);
                    Portal py = Portal.Cross(by, pl.Normal, bx.PointAt(pl.SRawCm / 100.0));
                    if ((!pl.Grouped && py.SCm != pl.SCm) || bx.Id != by.Id) st.SMismatch++;
                    st.Portals++; st.BorderCrossings++;
                    if (c.From.Face != c.To.Face) { string k = Math.Min(c.From.Face, c.To.Face) + "-" + Math.Max(c.From.Face, c.To.Face); int n0; st.CubeEdges.TryGetValue(k, out n0); st.CubeEdges[k] = n0 + 1; }
                    long sCm = pl.SCm; Vec3 road = nr;
                    if (pl.OffSegment) { st.OffSegment++; arcOff = true; }
                    if (pl.Grouped)
                    {
                        st.Snapped++; double sh = Math.Abs(pl.SCm - pl.SRawCm) / 100.0; if (sh > st.MaxShift) { st.MaxShift = sh; st.MaxShiftArc = Describe(a, b); }
                        st.MaxSnapB = Math.Max(st.MaxSnapB, Math.Abs(Portal.Snap(pl.B, pl.SRawCm, 256.0) - pl.SRawCm) / 100.0);       // candidate B, single portal, whichever candidate won
                        if (pl.ShiftedLine) { st.MaxSlideA = Math.Max(st.MaxSlideA, sh); st.MaxLateralA = Math.Max(st.MaxLateralA, Math.Abs(Vec3.Dot(pl.B.PointAt(pl.SCm / 100.0), nr)) * R); }
                    }
                    if (pl.ShiftedLine) st.Clamped++;
                    Vec3 trueP = Vec3.Cross(nr, bx.Normal).Normalized(); if (Vec3.Dot(trueP, c.Q) < 0) trueP = -trueP;
                    Vec3 posX = bx.PointAt(sCm / 100.0);
                    if (snap)
                    {
                        Vec3 cn = Vec3.Cross(prev, posX); if (cn.Length > 1e-14) road = cn.Normalized();
                        st.MinVertexDist = Math.Min(st.MinVertexDist, Math.Min((posX - bx.S).Length * R, (posX - bx.E).Length * R));
                    }
                    else st.MaxQuantMm = Math.Max(st.MaxQuantMm, (posX - trueP).Length * R * 1000);
                    // each region's own reconstruction of the position and heading (own face basis, own border vertices)
                    Vec3 nX, nY; Vec3 pX = OwnPos(c.From, c.To, bx, sCm, out nX), pY = OwnPos(c.To, c.From, bx, sCm, out nY);
                    st.MaxPosMm = Math.Max(st.MaxPosMm, Math.Max((pX - pY).Length, Math.Max((pX - posX).Length, (pY - posX).Length)) * R * 1000);
                    double hX = Portal.HeadingAt(nX, pX, road), hY = Portal.HeadingAt(nY, pY, road);
                    st.MaxHeadDiff = Math.Max(st.MaxHeadDiff, Math.Abs(hX - hY));
                    pts.Add(posX); prev = posX;
                }
                if (snap)
                {
                    pts.Add(b); double len = 0; for (int i = 1; i < pts.Count; i++) len += Vec3.Angle(pts[i - 1], pts[i]) * R;
                    double truth = Vec3.Angle(a, b) * R; double pct = Math.Abs(len - truth) / truth * 100; if (pct > st.MaxLenPct) { st.MaxLenPct = pct; st.MaxLenArc = Describe(a, b) + Detail(cr, pts, R, arcOff); }
                    if (pct >= 0.5) st.LenOver++;
                    if (pct > st.MaxLenPctAll) st.MaxLenPctAll = pct;
                    double firstShiftM, lastShiftToBM; ShiftEnds(a, b, cr, out firstShiftM, out lastShiftToBM);
                    if (Eligible(firstShiftM, lastShiftToBM))
                    {
                        st.Eligible++; if (pct >= 0.5) st.EligibleOver++;
                        if (pct > st.MaxLenPctEligible) { st.MaxLenPctEligible = pct; st.MaxLenEligibleArc = Describe(a, b) + Detail(cr, pts, R, arcOff); }
                    }
                    else { st.Excluded++; st.MaxLenPctExcluded = Math.Max(st.MaxLenPctExcluded, pct); }
                }
            }
            return st;
        }

        public static List<Row> R1R2R3()
        {
            var rows = new List<Row>();
            var s1 = Analyse(ArcsR1(10000), false);
            int edgesHit = s1.CubeEdges.Count; int minEdge = int.MaxValue; foreach (var kv in s1.CubeEdges) minEdge = Math.Min(minEdge, kv.Value);
            string cov = $"{s1.Arcs} arcs, {s1.BorderCrossings} border crossings, {edgesHit} of 12 cube edges crossed (min {minEdge} crossings per edge), {s1.Unresolved} unresolved";
            rows.Add(Mk("r1", "all", $"portal s (int64 1 cm) from both regions differs: count over portals ({cov})", s1.SMismatch.ToString(), "= 0", s1.SMismatch == 0 && edgesHit == 12 && s1.Unresolved == 0));
            rows.Add(Mk("r1", "all", "portal position reconstructed by each region from its own face basis: max mismatch mm (planet frame)", s1.MaxPosMm.ToString("G6"), "< 1", s1.MaxPosMm < 1.0));
            rows.Add(Mk("r1", "all", "diag: quantised portal vs the true intersection of the arc with the border, max mm (1 cm quantisation)", s1.MaxQuantMm.ToString("G6"), "report", true, true));
            var s2 = Analyse(ArcsR2(3000), true);
            string c2 = $"{s2.Arcs} arcs near cube / region vertices, {s2.Portals} portals, {s2.Snapped} in joint-snap groups ({s2.Clamped} on the shifted line, the rest moved along the border), {s2.Unresolved} unresolved";
            rows.Add(Mk("r2", "all", $"nearest distance of any portal to a region vertex after the snap, m ({c2})", s2.MinVertexDist.ToString("F4"), ">= 256 (255.999 for chord)", s2.MinVertexDist >= 255.999 && s2.Snapped > 0));
            double exShare = 100.0 * s2.Excluded / s2.Arcs;
            rows.Add(Mk("r2", "all", $"road length change vs the true arc (polyline through the shifted portals), max % over eligible arcs only (both ends >= 1 km from the nearest shifted portal; {s2.Eligible} eligible of {s2.Arcs}, {s2.Eligible - s2.EligibleOver} below 0.5 %)", s2.MaxLenPctEligible.ToString("G4"), "< 0.5", s2.MaxLenPctEligible < 0.5 && s2.Eligible > 0));
            rows.Add(Mk("r2", "all", $"diag: excluded arcs (an end < 1 km from a shifted portal): count of {s2.Arcs}, share {exShare:F2} %", s2.Excluded.ToString(), "report", true, true));
            rows.Add(Mk("r2", "all", "diag: max road length change among the excluded arcs, %", s2.MaxLenPctExcluded.ToString("G4"), "report", true, true));
            rows.Add(Mk("r2", "all", $"diag: old metric, max road length change over all {s2.Arcs} arcs, % (arcs >= 0.5 %: {s2.LenOver})", s2.MaxLenPctAll.ToString("G4"), "report", true, true));
            rows.Add(Mk("r2", "all", "single-portal snap shift (candidate B, Portal.Snap of the raw crossing), max m", s2.MaxSnapB.ToString("F2"), "<= 256", s2.MaxSnapB <= 256.0 + 1e-6));
            rows.Add(Mk("r2", "all", "diag: candidate A (shifted great circle): max lateral offset of the shifted road line from the true arc, m", s2.MaxLateralA.ToString("F2"), "report", true, true));
            rows.Add(Mk("r2", "all", "diag: candidate A: max slide of a crossing along its border, m", s2.MaxSlideA.ToString("F2"), "report", true, true));
            rows.Add(Mk("r2", "all", "portals off their border segment before the snap (raw s < -5 cm or > LengthCm + 5 cm), count", s2.OffSegment.ToString(), "= 0", s2.OffSegment == 0));
            Console.WriteLine("r2 max shift arc: " + s2.MaxShiftArc); Console.WriteLine("r2 max length arc (all): " + s2.MaxLenArc); Console.WriteLine("r2 max length arc (eligible): " + s2.MaxLenEligibleArc);
            rows.Add(Mk("r2", "all", "portal s mismatch between the two regions after the snap, count", s2.SMismatch.ToString(), "= 0", s2.SMismatch == 0));
            rows.Add(Mk("r3", "all", $"heading vs border tangent, difference between the two regions, max rad (r1 set, {s1.Portals} portals)", s1.MaxHeadDiff.ToString("E2"), "< 1e-9", s1.MaxHeadDiff < 1e-9));
            rows.Add(Mk("r3", "all", $"same on the snapped r2 set ({s2.Portals} portals, heading of the bent arc), max rad", s2.MaxHeadDiff.ToString("E2"), "< 1e-9", s2.MaxHeadDiff < 1e-9));
            return rows;
        }

        // ---------------------------------------------------------------- r4 corner-region refinement
        static bool SegCross(double[] p1, double[] p2, double[] p3, double[] p4)
        {
            if (Math.Max(p1[0], p2[0]) < Math.Min(p3[0], p4[0]) || Math.Max(p3[0], p4[0]) < Math.Min(p1[0], p2[0])) return false;
            if (Math.Max(p1[1], p2[1]) < Math.Min(p3[1], p4[1]) || Math.Max(p3[1], p4[1]) < Math.Min(p1[1], p2[1])) return false;
            double d1 = Orient(p3, p4, p1), d2 = Orient(p3, p4, p2), d3 = Orient(p1, p2, p3), d4 = Orient(p1, p2, p4);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
        static double Orient(double[] a, double[] b, double[] c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);

        public static List<Row> R4()
        {
            var regions = new List<RegionId>(); int n = 1 << Border.Level;
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var c = new Vec3(sx, sy, sz);
                        for (int f = 0; f < 6; f++) if (Vec3.Dot(CubeSphere.Normal(f), c) > 0)
                                regions.Add(new RegionId(f, Border.Level, Vec3.Dot(CubeSphere.AxisV(f), c) > 0 ? n - 1 : 0, Vec3.Dot(CubeSphere.AxisU(f), c) > 0 ? n - 1 : 0));
                    }
            int[][] sidePairs = { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 3, 1 } };
            ulong st = 0xC0FFEE04; int polylines = 0, selfX = 0, outside = 0, badEnds = 0, rejected = 0, maxPts = 0;
            double[] minR = { 1e30, 1e30 }; double[] radii = { RegionPlane.MotorwayMinRadius, RegionPlane.PrimaryMinRadius };
            foreach (var reg in regions)
            {
                var plane = new RegionPlane(reg);
                foreach (var sp in sidePairs) for (int cls = 0; cls < 2; cls++)
                    {
                        var borders = new Border[2]; var ends = new double[2][];
                        for (int k = 0; k < 2; k++)
                        {
                            borders[k] = Border.Between(reg, CubeSphere.Neighbour(reg, sp[k]));
                            double s = 1000 + SplitMix64.NextDouble(ref st) * (borders[k].Length - 2000);
                            ends[k] = plane.ToLocal(borders[k].PointAt(Math.Round(s * 100) / 100.0));
                        }
                        var j = new[] { (SplitMix64.NextDouble(ref st) - 0.5) * 2000, (SplitMix64.NextDouble(ref st) - 0.5) * 2000 };
                        var pl = RegionPlane.Fillet(ends[0], j, ends[1], radii[cls]);
                        if (pl == null) { rejected++; continue; }
                        polylines++; maxPts = Math.Max(maxPts, pl.Count);
                        for (int i = 0; i + 2 < pl.Count; i++)
                        {
                            double ax = pl[i + 1][0] - pl[i][0], ay = pl[i + 1][1] - pl[i][1], bx = pl[i + 2][0] - pl[i + 1][0], by = pl[i + 2][1] - pl[i + 1][1], cx = pl[i + 2][0] - pl[i][0], cy = pl[i + 2][1] - pl[i][1];
                            double cr = Math.Abs(ax * by - ay * bx); if (cr < 1e-9) continue;
                            double rad = Math.Sqrt(ax * ax + ay * ay) * Math.Sqrt(bx * bx + by * by) * Math.Sqrt(cx * cx + cy * cy) / (2 * cr);
                            minR[cls] = Math.Min(minR[cls], rad);
                        }
                        for (int i = 0; i + 1 < pl.Count; i++) for (int k = i + 2; k + 1 < pl.Count; k++) if (SegCross(pl[i], pl[i + 1], pl[k], pl[k + 1])) selfX++;
                        for (int i = 1; i + 1 < pl.Count; i++) if (!CubeSphere.RegionOf(plane.ToDir(pl[i][0], pl[i][1]), Border.Level).Equals(reg)) outside++;
                        for (int k = 0; k < 2; k++)
                        {
                            double[] pt = k == 0 ? pl[0] : pl[pl.Count - 1]; Vec3 d = plane.ToDir(pt[0], pt[1]);
                            if (Math.Abs(Vec3.Dot(d, borders[k].Normal)) * R > 1e-3) badEnds++;
                        }
                    }
            }
            string cfg = $"{polylines} polylines in the 24 corner regions (3 per cube vertex) x 3 side pairs x 2 classes, <= {maxPts} points at 4 m, {rejected} rejected";
            return new List<Row>
            {
                Mk("r4", "all", $"self-intersections of the refined corridor ({cfg})", selfX.ToString(), "= 0", selfX == 0 && polylines > 0 && rejected == 0),
                Mk("r4", "all", "min horizontal radius motorway (m, circumradius of 4 m samples in the region plane)", minR[0].ToString("F3"), ">= " + RegionPlane.MotorwayMinRadius, minR[0] >= RegionPlane.MotorwayMinRadius - 1e-6),
                Mk("r4", "all", "min horizontal radius primary (m, same)", minR[1].ToString("F3"), ">= " + RegionPlane.PrimaryMinRadius, minR[1] >= RegionPlane.PrimaryMinRadius - 1e-6),
                Mk("r4", "all", "interior samples outside the region (maps back to another level-10 region)", outside.ToString(), "= 0", outside == 0),
                Mk("r4", "all", "end points not on the portal border (> 1 mm from the border plane)", badEnds.ToString(), "= 0", badEnds == 0),
            };
        }

        // ---------------------------------------------------------------- r5 global corridor graph
        public static List<Row> R5()
        {
            var times = new List<double>(); CorridorGraph g = null; long retained = 0;
            for (int i = 0; i < 4; i++)         // run 0 = JIT warm-up (untimed), then median of 3
            {
                g = null; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long before = GC.GetTotalMemory(true);
                var sw = Stopwatch.StartNew(); g = CorridorGraph.Build(42); sw.Stop();
                long after = GC.GetTotalMemory(true); GC.KeepAlive(g);
                if (i > 0) times.Add(sw.Elapsed.TotalSeconds); retained = after - before;
            }
            times.Sort(); double med = times[1]; int mot = 0; foreach (var c in g.Class) if (c == RoadClass.Motorway) mot++;
            string info = $"{g.NodeCount} settlements, {g.EdgeCount} edges ({mot} motorway), times {string.Join(" / ", times.ConvertAll(t => t.ToString("F3")))} s";
            return new List<Row>
            {
                Mk("r5", "all", $"time to build the whole-planet corridor graph, median of 3, single thread, s ({info})", med.ToString("F3"), "< 1", med < 1.0),
                Mk("r5", "all", $"retained memory of the graph (GC.GetTotalMemory delta), MB; arrays alone {g.Bytes / 1048576.0:F2} MB", (retained / 1048576.0).ToString("F2"), "< 5", retained < 5 * 1048576L),
            };
        }
    }
}
