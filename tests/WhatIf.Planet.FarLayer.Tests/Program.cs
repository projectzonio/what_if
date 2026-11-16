using System;
using System.Collections.Generic;
using WhatIf.Planet;
using WhatIf.Planet.FarLayer;

// Dependency-free tests for WhatIf.Planet.FarLayer (RG-171): exit 0 = ALL PASSED, 1 = a check failed. Synthetic height function (fast, no surface needed).
var failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 && !ok ? " :: " + detail : "")}");
    if (!ok) failures++;
}
long Bits(double d) => BitConverter.DoubleToInt64Bits(d);
bool SameVec(Vec3 a, Vec3 b) => Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z);
Func<Vec3, double> height = d => 900.0 * Math.Sin(d.X * 37.0) * Math.Cos(d.Y * 29.0) + 300.0 * Math.Sin(d.Z * 301.0 + d.X * 120.0) + 40.0 * Math.Sin(d.Y * 4000.0) + 1300.0;   // >= 0: land everywhere (the horizon is the sea-level sphere)
const double R = CubeSphere.Radius;

FarCamera Cam(Vec3 dir, double above)
{
    Vec3 east, north; int face; double a, b; CubeSphere.ToFaceAB(dir, out face, out a, out b); CubeSphere.TangentAxes(face, dir, out east, out north);
    return new FarCamera { Pos = dir * (R + height(dir) + above), Ground = dir * (R + height(dir)), Fwd = north };
}
void Settle(FarLayer layer, in FarCamera cam, NearSet near, FrameSelection sel)
{
    for (int i = 0; i < 40; i++) { layer.Select(cam, near, sel); if (sel.Requests.Count == 0) return; layer.BuildPending(sel, 0); }
}
Vec3 Rand(ref ulong st)
{
    while (true) { var v = new Vec3(SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1); double l = v.Length; if (l > 0.05 && l <= 1) return v / l; }
}

// ---- f1: coverage exactly once, fixed cameras (incl. one on a cube edge, one high) ----
foreach (var (name, dir, above) in new[] { ("ground", FarMesh_Dir(0, 0.3, -0.2), 2.0), ("300m", FarMesh_Dir(2, -0.5, 0.4), 300.0), ("10km", FarMesh_Dir(4, 0.1, 0.9), 10000.0), ("cube edge", FarMesh_Dir(0, 1.0, 0.0), 2.0) })
{
    var layer = new FarLayer(height, new FarParams()); layer.Prebuild(3); var near = new NearSet(); var sel = new FrameSelection(); var cam = Cam(dir, above);
    near.Update(dir); Settle(layer, cam, near, sel);
    var idx = new CoverageIndex(sel, near); ulong st = 5; int bad = 0, n = 6000; var hist = new int[4];
    double thMax = Math.Acos(R / cam.Pos.Length) * 0.999; Vec3 e1 = Vec3.Cross(dir, new Vec3(0.3, 0.9, 0.1)).Normalized(), e2 = Vec3.Cross(dir, e1);
    for (int i = 0; i < n; i++)
    {
        double ct = 1 - SplitMix64.NextDouble(ref st) * (1 - Math.Cos(thMax)), phi = SplitMix64.NextDouble(ref st) * 2 * Math.PI, sn = Math.Sqrt(1 - ct * ct);
        int cnt = idx.Count((dir * ct + (e1 * Math.Cos(phi) + e2 * Math.Sin(phi)) * sn).Normalized()); hist[Math.Min(cnt, 3)]++; if (cnt != 1) bad++;
    }
    int badNear = 0; foreach (var s in near.Sectors) { var q = new QuadSector(s); for (int j = 0; j < 65; j += 8) for (int i = 0; i < 65; i += 8) if (idx.Count(q.Direction(i, j)) != 1) badNear++; }
    Check($"f1 coverage exactly once ({name})", bad == 0 && badNear == 0 && sel.IncompleteRoots == 0 && near.Count > 3, $"{bad}/{n} random (counts 0/1/2/3+: {string.Join("/", hist)}), {badNear} near vertices, incomplete {sel.IncompleteRoots}, near {near.Count}");
    Check($"f8 adjacent nodes differ <= 1 level ({name})", SeamChecks.LevelViolations(sel) == 0, SeamChecks.LevelViolations(sel) + " violations");
    var mc = new MorphContext { Cam = cam.Pos, Ground = cam.Ground, P = layer.P }; int pairs; double gap = SeamChecks.MaxTJunctionGap(sel, layer, mc, out pairs);
    Check($"f8 T-junction gap < 1 mm ({name})", gap < 1e-3, $"{gap} m over {pairs} edges");
}
Vec3 FarMesh_Dir(int face, double a, double b) => CubeSphere.FromFaceAB(face, a, b);

// ---- f7: morph continuity ----
{
    var layer = new FarLayer(height); ulong st = 11; double worst = 0; int nodes = 0, badBits = 0;
    for (int t = 0; t < 120; t++)
    {
        int level = 1 + (int)(SplitMix64.Next(ref st) % 13), face = (int)(SplitMix64.Next(ref st) % 6), cells = 1 << level;
        int row = (int)(SplitMix64.Next(ref st) % (ulong)cells), col = (int)(SplitMix64.Next(ref st) % (ulong)cells);
        var n = layer.Node(face, level, row, col); var p = layer.Node(face, level - 1, row >> 1, col >> 1); layer.Build(n); layer.Build(p); nodes++;
        for (int j = 0; j < 17; j++) for (int i = 0; i < 17; i++)
            {
                if (Bits(FarMesh.Morphed(n, i, j, 0.0).X) != Bits(n.Dir(i, j).X * (R + height(n.Dir(i, j))))) badBits++;
                double px = ((col & 1) * 16 + i) / 2.0, py = ((row & 1) * 16 + j) / 2.0; int ix = Math.Min(15, (int)px), iy = Math.Min(15, (int)py); double fx = px - ix, fy = py - iy;
                Vec3 a = p.Position(ix, iy), d = p.Position(ix + 1, iy + 1), b = p.Position(ix + 1, iy), c = p.Position(ix, iy + 1);
                Vec3 want = fx >= fy ? a + (b - a) * fx + (d - b) * fy : a + (d - c) * fx + (c - a) * fy;
                worst = Math.Max(worst, (FarMesh.Morphed(n, i, j, 1.0) - want).Length);
            }
    }
    Check("f7 morph 1 equals the parent mesh (< 1 mm)", worst < 1e-3, $"{worst} m on {nodes} nodes");
    Check("f7 morph 0 equals own height bits", badBits == 0, badBits + " differ");
}

// ---- f8: shared edge vertices bit-identical, same face and across a cube edge ----
{
    var layer = new FarLayer(height); int cmp = 0, diff = 0;
    foreach (var (face, level, row, col) in new[] { (0, 5, 10, 31), (0, 6, 63, 20), (3, 4, 3, 15), (4, 7, 0, 77), (2, 5, 9, 9) })
        for (int dir = 0; dir < 4; dir++)
        {
            var nb = CubeSphere.Neighbour(new RegionId(face, level, row, col), dir); var A = layer.Node(face, level, row, col); var B = layer.Node(nb.Face, nb.Level, nb.Row, nb.Col); layer.Build(A); layer.Build(B);
            for (int a = 0; a < 17; a++) for (int b = 0; b < 17; b++)
                {
                    bool onEdge = dir == 0 ? a == 16 : dir == 1 ? a == 0 : dir == 2 ? b == 16 : b == 0; if (!onEdge) continue;
                    Vec3 da = A.Dir(a, b);
                    for (int c = 0; c < 17; c++) for (int d = 0; d < 17; d++) if (SameVec(da, B.Dir(c, d))) { cmp++; if (Bits(A.H[b * 17 + a]) != Bits(B.H[d * 17 + c]) || !SameVec(A.Position(a, b), B.Position(c, d))) diff++; }
                }
        }
    Check("f8 shared edge vertices bit-identical (incl. across cube edges)", cmp >= 5 * 4 * 17 / 2 && diff == 0, $"{cmp} compared, {diff} differ");
}

// ---- cache: bounded, pinned levels kept, selection stable after eviction ----
{
    var layer = new FarLayer(height, new FarParams { MaxNodes = 2500 }); layer.Prebuild(3); var near = new NearSet(); var sel = new FrameSelection(); ulong st = 3;
    for (int i = 0; i < 40; i++) { var d = Rand(ref st); near.Update(d); Settle(layer, Cam(d, 2.0), near, sel); }
    Check("node cache stays bounded and keeps the pinned levels", layer.NodeCount <= 2500 && layer.Node(1, 3, 2, 2).Built, $"{layer.NodeCount} nodes");
}
// ---- morph fraction must respect the T-junction-safe bound (RG-174) ----
{
    var fp = new FarParams(); double safe = 0.5 - 0.725 * 1250.0 / fp.Range13 - 0.01;
    Check("f8 default morph fraction <= T-junction-safe bound", fp.MorphFraction <= safe + 1e-12, $"{fp.MorphFraction} vs bound {safe}");
}
Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
return failures == 0 ? 0 : 1;
