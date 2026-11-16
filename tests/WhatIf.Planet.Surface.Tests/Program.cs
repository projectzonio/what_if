using System;
using WhatIf.Planet;
using WhatIf.Planet.Surface;

// Dependency-free tests for WhatIf.Planet.Surface (RG-168): exit 0 = ALL PASSED, 1 = a check failed. Fast (< 20 s), no files.
var failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 && !ok ? " :: " + detail : "")}");
    if (!ok) failures++;
}
Vec3 Rand(ref ulong st)
{
    while (true) { var v = new Vec3(SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1); double l = v.Length; if (l > 0.05 && l <= 1) return v / l; }
}
long Bits(double d) => BitConverter.DoubleToInt64Bits(d);

const ulong Seed = 20261112UL;
var a = new PlanetSurface(Seed); var b = new PlanetSurface(Seed);
ulong st = 1; int bad = 0, badH = 0; int[] kinds = new int[5];
for (int i = 0; i < 400; i++)
{
    Vec3 d = Rand(ref st); var s1 = a.Sample(d); var s2 = b.Sample(d); kinds[(int)s1.Kind]++;
    if (Bits(s1.HeightM) != Bits(s2.HeightM) || s1.Kind != s2.Kind || Bits(s1.WaterLevelM) != Bits(s2.WaterLevelM) || BitConverter.SingleToInt32Bits(s1.Forest) != BitConverter.SingleToInt32Bits(s2.Forest) || s1.Biome != s2.Biome) bad++;
    if (Bits(b.Height(d)) != Bits(s1.HeightM)) badH++;
}
Check("two instances give identical samples (cache independent)", bad == 0, bad + " differ");
Check("Height(dir) == Sample(dir).HeightM bits", badH == 0, badH + " differ");
Check("ocean and land both occur", kinds[0] > 0 && kinds[3] > 0, string.Join("/", kinds));

// shared cube-edge vertices: two sectors across an edge give the same bits at the shared vertices
{
    var id = new SectorId(0, 100, SectorId.PerFaceSide - 1); var nid = id.Neighbour(0);
    var qa = new QuadSector(id); var qb = new QuadSector(nid); int cmp = 0, diff = 0;
    for (int j = 0; j < 65; j++) for (int jj = 0; jj < 65; jj++)
        {
            Vec3 da = qa.Direction(64, j);
            for (int e = 0; e < 4; e++) { Vec3 db = e == 0 ? qb.Direction(jj, 0) : e == 1 ? qb.Direction(jj, 64) : e == 2 ? qb.Direction(0, jj) : qb.Direction(64, jj); if (da.X == db.X && da.Y == db.Y && da.Z == db.Z) { cmp++; if (Bits(a.Height(da)) != Bits(b.Height(db))) diff++; } }
        }
    Check("cube-edge vertices shared by two sectors: same height bits", cmp >= 65 && diff == 0, $"{cmp} compared, {diff} differ");
}

// sector batch == plain sample
{
    var id = SectorId.At(Rand(ref st)); var q = new QuadSector(id); var h = new float[65 * 65]; var k = new byte[65 * 65]; var f = new float[65 * 65]; int d2 = 0;
    SurfaceSector.Sample(a, id, h, k, f);
    for (int j = 0; j < 65; j += 4) for (int i = 0; i < 65; i += 4) { var s = b.Sample(q.Direction(i, j)); if ((float)s.HeightM != h[j * 65 + i] || (byte)s.Kind != k[j * 65 + i] || s.Forest != f[j * 65 + i]) d2++; }
    Check("SurfaceSector.Sample == PlanetSurface.Sample", d2 == 0, d2 + " differ");
}

// lake: constant level, no spill within the guaranteed band, level above sea, found on a fixed seed
{
    Lake lake = null; int tries = 0;
    for (; lake == null && tries < 200000; tries++) { Vec3 p = Rand(ref st) * CubeSphere.Radius; lake = a.LakeOfCell(PlanetSurface.CellOf(p.X), PlanetSurface.CellOf(p.Y), PlanetSurface.CellOf(p.Z)); }
    Check("a lake exists on the fixed seed", lake != null);
    if (lake != null)
    {
        int spill = 0, levelBad = 0, n = 720;
        for (int k = 0; k < n; k++)
        {
            double th = 2 * Math.PI * k / n, rho = lake.Radius(th);
            foreach (double u in new[] { 0.5, 3, 10, 30, 60, 100, 120 }) { var o = a.Sample(lake.PointAt(th, rho + u)); if (o.Kind == SurfaceKind.Lake || o.HeightM < lake.Level - 1e-9) spill++; }
            var inside = a.Sample(lake.PointAt(th, rho * 0.5)); if (inside.Kind != SurfaceKind.Lake || inside.WaterLevelM != lake.Level) levelBad++;
        }
        Check("lake: no spill within 120 m of the shore", spill == 0, spill + " samples");
        Check("lake: constant water level inside", levelBad == 0, levelBad + " samples");
        Check("lake level >= 2 m above sea", lake.Level >= 2.0, lake.Level.ToString());
    }
}

// forest rules
{
    int badF = 0; ulong s2 = 9;
    for (int i = 0; i < 20000; i++) { var o = a.Sample(Rand(ref s2)); if (o.Forest > 0 && (o.Kind != SurfaceKind.Land || o.HeightM > SurfaceParams.TreeLineM)) badF++; if (o.Forest < 0 || o.Forest > 1) badF++; }
    Check("forest only on land below the tree line, in [0,1]", badF == 0, badF + " samples");
}
// RG-169: FbmLayer3 = three FbmLayers bit for bit; Kind(dir) = Sample(dir).Kind
{
    ulong sa = 11, sb = 22, sc = 33; var l1 = new FbmLayer(sa, 20, 2); var l2 = new FbmLayer(sb, 20, 2); var l3 = new FbmLayer(sc, 20, 2); var l3x = new FbmLayer3(sa, sb, sc, 20, 2);
    ulong s3 = 5; int badW = 0;
    for (int i = 0; i < 20000; i++)
    {
        Vec3 d = Rand(ref s3) * CubeSphere.Radius; if (i % 3 == 0) d = new Vec3(d.X * 1e-3 + 4.1e6, d.Y * 1e-3 - 2.2e6, d.Z * 1e-3 + 1.3e6);   // clusters: the corner cache hits
        l3x.Sample(d.X, d.Y, d.Z, out double x, out double y, out double z);
        if (Bits(x) != Bits(l1.Sample(d.X, d.Y, d.Z)) || Bits(y) != Bits(l2.Sample(d.X, d.Y, d.Z)) || Bits(z) != Bits(l3.Sample(d.X, d.Y, d.Z))) badW++;
    }
    Check("FbmLayer3 gives the same bits as three FbmLayers of the same seeds", badW == 0, badW + " differ");
    ulong s4 = 77; int badK = 0;
    for (int i = 0; i < 5000; i++) { Vec3 d = Rand(ref s4); if (a.Kind(d) != a.Sample(d).Kind) badK++; }
    Check("Kind(dir) == Sample(dir).Kind", badK == 0, badK + " differ");
    // coast detail is continuous: sampled along 4 m steps across the coast the height never jumps more than the 4 m slope allows
    ulong s5 = 3; double worst = 0; int crossings = 0;
    for (int tries = 0; tries < 4000 && crossings < 30; tries++)
    {
        Vec3 d0 = Rand(ref s5); double x0 = a.ContinentX(d0); if (Math.Abs(x0) > 0.01) continue;
        crossings++; Vec3 e = Vec3.Cross(d0, new Vec3(0, 1, 0)).Normalized(); double prev = a.Height(d0);
        for (int k = 1; k <= 400; k++) { Vec3 d = (d0 * CubeSphere.Radius + e * (4.0 * k)).Normalized(); double h = a.Height(d); worst = Math.Max(worst, Math.Abs(h - prev)); prev = h; }
    }
    Check("coast detail: heights along 1.6 km walks across the coast are continuous (max step per 4 m < 40 m)", crossings >= 10 && worst < 40, crossings + " walks, max step " + worst.ToString("F2"));
}
Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
return failures == 0 ? 0 : 1;
