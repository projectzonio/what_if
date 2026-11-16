using System;
using System.Collections.Generic;
using WhatIf.Planet;

// Dependency-free tests for WhatIf.Planet (RG-161): exit 0 = ALL PASSED, 1 = a check failed. Fast (< 10 s), no Unity, no files.
var failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 && !ok ? " :: " + detail : "")}");
    if (!ok) failures++;
}

// ---- SplitMix64 ----
{
    ulong s = 0;
    Check("splitmix64 seed 0 first value", SplitMix64.Next(ref s) == 0xE220A8397B1DCDAFUL);
    Check("splitmix64 seed 0 second value", SplitMix64.Next(ref s) == 0x6E789E6AA1B965F4UL);
}

// ---- cube <-> sphere round trip ----
{
    Check("warp: a(u=1) = 1, a(0) = 0", Math.Abs(CubeSphere.Warp(1) - 1) < 1e-15 && CubeSphere.Warp(0) == 0);
    Check("warp round trip", Math.Abs(CubeSphere.Unwarp(CubeSphere.Warp(0.37)) - 0.37) < 1e-15);
    double worst = 0; ulong st = 7;
    var dirs = new List<Vec3>();
    for (int f = 0; f < 6; f++)
    {
        dirs.Add(CubeSphere.Normal(f));                                                      // face centres
        foreach (double e in new[] { -1.0, 1.0 })
        {
            dirs.Add((CubeSphere.Normal(f) + CubeSphere.AxisU(f) * e).Normalized());          // edge midpoints
            dirs.Add((CubeSphere.Normal(f) + CubeSphere.AxisV(f) * e).Normalized());
            foreach (double e2 in new[] { -1.0, 1.0 }) dirs.Add((CubeSphere.Normal(f) + CubeSphere.AxisU(f) * e + CubeSphere.AxisV(f) * e2).Normalized());   // corners
        }
    }
    for (int i = 0; i < 20000; i++) dirs.Add(new Vec3(SplitMix64.NextDouble(ref st) - 0.5, SplitMix64.NextDouble(ref st) - 0.5, SplitMix64.NextDouble(ref st) - 0.5).Normalized());
    foreach (var d in dirs)
    {
        int f; double a, b; CubeSphere.ToFaceAB(d, out f, out a, out b);
        worst = Math.Max(worst, (CubeSphere.FromFaceAB(f, a, b) - d).Length);
        if (Math.Abs(a) > 1 + 1e-12 || Math.Abs(b) > 1 + 1e-12) worst = 1;
    }
    Check("direction -> (face,a,b) -> direction, faces/edges/corners/20000 random", worst < 1e-14, worst.ToString("R"));
    Check("(1,1,1)/sqrt3 is face 0 corner a=b=1", CubeSphere.RegionOf(new Vec3(1, 1, 1).Normalized(), 10).Equals(new RegionId(0, 10, 1023, 1023)));
    double cellMin = 1e30, cellMax = 0;   // area spread of the equal-angle warp at level 6 is about 1.414:1
    {
        int n = 64;
        for (int r = 0; r < n; r++) for (int c = 0; c < n; c++)
            {
                var id = new RegionId(0, 6, r, c);
                Vec3 p00 = CubeSphere.RegionCorner(id, 0, 0), p10 = CubeSphere.RegionCorner(id, 1, 0), p01 = CubeSphere.RegionCorner(id, 0, 1), p11 = CubeSphere.RegionCorner(id, 1, 1);
                double area = 0.5 * Vec3.Cross(p10 - p00, p01 - p00).Length + 0.5 * Vec3.Cross(p10 - p11, p01 - p11).Length;   // two flat triangles
                cellMin = Math.Min(cellMin, area); cellMax = Math.Max(cellMax, area);
            }
    }
    Check("equal-angle area spread 1.35..1.45 : 1", cellMax / cellMin > 1.35 && cellMax / cellMin < 1.45, (cellMax / cellMin).ToString("F3"));
    var r10 = CubeSphere.RegionOf(new Vec3(1, 0, 0), 10);
    double sizeKm = (CubeSphere.RegionCorner(r10, 1, 0) - CubeSphere.RegionCorner(r10, 0, 0)).Length * CubeSphere.Radius / 1000;
    Check("level-10 region about 9.8 km at the face centre", sizeKm > 9.5 && sizeKm < 10.1, sizeKm.ToString("F2"));
}

// ---- neighbour table ----
{
    var back = new Dictionary<string, int>();
    int bad = 0, undirected = 0;
    var seen = new HashSet<string>();
    for (int f = 0; f < 6; f++) for (int d = 0; d < 4; d++)
        {
            var r = new RegionId(f, 0, 0, 0); var nb = CubeSphere.Neighbour(r, d);
            bool symmetric = false;
            for (int d2 = 0; d2 < 4; d2++) if (CubeSphere.Neighbour(nb, d2).Equals(r)) symmetric = true;
            if (!symmetric || nb.Face == f) bad++;
            string key = Math.Min(f, nb.Face) + "-" + Math.Max(f, nb.Face);
            if (seen.Add(key)) undirected++;
        }
    Check("level 0: 24 directed relations, all symmetric, 12 distinct cube edges", bad == 0 && undirected == 12, bad + " bad, " + undirected + " edges");

    foreach (int level in new[] { 1, 2, 3, 5, 10 })
    {
        int n = 1 << level; int asym = 0, geom = 0, checkedCount = 0;
        for (int f = 0; f < 6; f++) for (int i = 0; i < n; i++) for (int d = 0; d < 4; d++)
                {
                    // cells on the face rim in direction d
                    int row = d == 3 ? 0 : d == 2 ? n - 1 : i, col = d == 1 ? 0 : d == 0 ? n - 1 : i;
                    var r = new RegionId(f, level, row, col); var nb = CubeSphere.Neighbour(r, d);
                    checkedCount++;
                    bool symmetric = false;
                    for (int d2 = 0; d2 < 4; d2++) if (CubeSphere.Neighbour(nb, d2).Equals(r)) symmetric = true;
                    if (!symmetric) asym++;
                    // geometric: neighbour centre is about one cell away (less than 1.6 cells), never a far jump
                    double cell = (CubeSphere.RegionCorner(r, 1, 0) - CubeSphere.RegionCorner(r, 0, 0)).Length;
                    if ((CubeSphere.RegionCentre(r) - CubeSphere.RegionCentre(nb)).Length > 1.7 * cell) geom++;
                    // the shared side: both regions have a corner pair that coincides
                    int shared = 0;
                    for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++)
                            if ((CubeSphere.RegionCorner(r, a & 1, a >> 1) - CubeSphere.RegionCorner(nb, b & 1, b >> 1)).Length < 1e-12) shared++;
                    if (shared != 2) geom++;
                }
        Check($"level {level}: neighbour of my neighbour is me, shared side has 2 common corners ({checkedCount} rim cells x 4)", asym == 0 && geom == 0, asym + " asym, " + geom + " geom");
    }

    // 8 cube corners: three regions meet, each pair adjacent
    int cornerBad = 0, corners = 0;
    for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                Vec3 c = new Vec3(sx, sy, sz).Normalized(); corners++;
                var set = new HashSet<RegionId>();
                for (int f = 0; f < 6; f++) for (int row = 0; row < 8; row++) for (int col = 0; col < 8; col++)
                        {
                            var r = new RegionId(f, 3, row, col);
                            for (int a = 0; a < 4; a++) if ((CubeSphere.RegionCorner(r, a & 1, a >> 1) - c).Length < 1e-12) set.Add(r);
                        }
                if (set.Count != 3) { cornerBad++; continue; }
                var arr = new List<RegionId>(set);
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) if (i != j)
                        {
                            bool adj = false;
                            for (int d = 0; d < 4; d++) if (CubeSphere.Neighbour(arr[i], d).Equals(arr[j])) adj = true;
                            if (!adj) cornerBad++;
                        }
            }
    Check("8 cube corners: 3 regions each, pairwise adjacent through the neighbour table", corners == 8 && cornerBad == 0, cornerBad.ToString());
}

// ---- frame transport ----
{
    var f = Frame.At(new Vec3(0, 0, CubeSphere.Radius), new Vec3(1, 0, 0));
    Check("frame: initial frame orthonormal", f.Error() < 1e-15, f.Error().ToString("R"));
    double alpha = 1000.0 / CubeSphere.Radius; Vec3 ctr = new Vec3(1, 1, 1).Normalized();
    Vec3 e = Vec3.Cross(ctr, new Vec3(0, 0, 1)).Normalized(), nn = Vec3.Cross(ctr, e);
    int steps = 6280; double worst = 0, ang = 0;
    Frame fr = default; Frame prev = default;
    for (int i = 0; i <= steps; i++)
    {
        double phi = 2 * Math.PI * i / steps;
        Vec3 p = (ctr * Math.Cos(alpha) + (e * Math.Cos(phi) + nn * Math.Sin(phi)) * Math.Sin(alpha)) * CubeSphere.Radius;
        if (i == 0) { fr = Frame.At(p, nn); prev = fr; continue; }
        double th = fr.Transport(p);
        worst = Math.Max(worst, fr.Error()); ang = Math.Max(ang, Math.Abs(Frame.RotationAngle(prev, fr) - th)); prev = fr;
    }
    Check("frame: 6280 transport steps keep orthonormality and Up = radial", worst < 1e-12, worst.ToString("E2"));
    Check("frame: measured step rotation = transport angle", ang < 1e-12, ang.ToString("E2"));
    var start = Frame.At(new Vec3(0, 0, CubeSphere.Radius), new Vec3(1, 0, 0)); var same = start;
    Check("frame: transport to the same position is the identity", same.Transport(start.Position) == 0 && (same.Forward - start.Forward).Length == 0);
}

// ---- quad sector + lattice ----
{
    const int M = SectorId.PerFaceSide;
    Check("lattice: a = I / 2^20 - 1 is exact, centre I = 2^20 gives u = 0", Lattice.Coord(1 << 20) == 0.0 && Lattice.Coord(0) == -Lattice.Coord(Lattice.N));
    Check("lattice: tan(pi/4) is not exactly 1 in double (why the canonical rule exists)", Lattice.Coord(Lattice.N) != 1.0);
    var id = new SectorId(0, 1234, 20000);
    Check("sector id: region = level-10 ancestor (32 x 32 sectors)", id.Region.Equals(new RegionId(0, 10, 1234 >> 5, 20000 >> 5)) && id.Region.Row == 38 && id.Region.Col == 625);
    var sf = new QuadSector(id);
    Check("sector: centre on the sphere, axes orthonormal, east x north = up", Math.Abs(sf.Centre.Length - CubeSphere.Radius) < 1e-6 && Math.Abs(Vec3.Dot(sf.East, sf.North)) < 1e-14 && Math.Abs(Vec3.Dot(sf.East, sf.Up)) < 1e-14 && (Vec3.Cross(sf.East, sf.North) - sf.Up).Length < 1e-14);
    Check("sector: SectorId.At(centre) returns the id", SectorId.At(sf.Up).Equals(id));
    var mx = new float[65 * 65]; var my = new float[65 * 65]; var mz = new float[65 * 65]; var w = new Vec3[65 * 65];
    sf.Build(42, true, mx, my, mz, w);
    Check("sector: centre vertex local x,z = 0 and y within the height amplitude", Math.Abs(mx[32 * 65 + 32]) < 1e-3 && Math.Abs(mz[32 * 65 + 32]) < 1e-3 && Math.Abs(my[32 * 65 + 32]) < 200);
    var m2x = new float[65 * 65]; var m2y = new float[65 * 65]; var m2z = new float[65 * 65]; sf.Build(42, true, m2x, m2y, m2z);
    Check("sector: Build is deterministic", BitsEqual(my, m2y) && BitsEqual(mx, m2x));
    double dBack = 0; for (int i = 0; i < w.Length; i++) dBack = Math.Max(dBack, (sf.LocalToWorld(mx[i], my[i], mz[i]) - w[i]).Length);
    Check("sector: float local mesh transformed back is within 0.1 mm of the double world position", dBack < 1e-4, dBack.ToString("E2"));

    // canonical vertex rule: every shared vertex of edge / corner sectors is bit-identical from both sides
    int bad = 0, shared = 0;
    bool Same(Vec3 p, Vec3 q) => BitConverter.DoubleToInt64Bits(p.X) == BitConverter.DoubleToInt64Bits(q.X) && BitConverter.DoubleToInt64Bits(p.Y) == BitConverter.DoubleToInt64Bits(q.Y) && BitConverter.DoubleToInt64Bits(p.Z) == BitConverter.DoubleToInt64Bits(q.Z);
    for (int f = 0; f < 6; f++) for (int d = 0; d < 4; d++) foreach (int idx in new[] { 0, 5, M / 2, M - 1 })
            {
                int row = d == 3 ? 0 : d == 2 ? M - 1 : idx, col = d == 1 ? 0 : d == 0 ? M - 1 : idx;
                var a = new SectorId(f, row, col); var b = a.Neighbour(d);
                var qa = new QuadSector(a); var qb = new QuadSector(b);
                var wa = new Vec3[65 * 65]; var wb = new Vec3[65 * 65]; var t = new float[65 * 65];
                qa.Build(7, true, t, t, t, wa); qb.Build(7, true, t, t, t, wb);
                // the shared vertices are the ones whose world positions coincide to < 1 mm; each of those must be identical bit for bit
                for (int i = 0; i < wa.Length; i++)
                {
                    if (!(i % 65 == 0 || i % 65 == 64 || i / 65 == 0 || i / 65 == 64)) continue;
                    for (int k = 0; k < wb.Length; k++)
                    {
                        if (!(k % 65 == 0 || k % 65 == 64 || k / 65 == 0 || k / 65 == 64)) continue;
                        if ((wa[i] - wb[k]).Length < 1e-3) { shared++; if (!Same(wa[i], wb[k])) bad++; }
                    }
                }
            }
    Check($"canonical vertex: {shared} shared edge vertices of 96 cube-edge sector pairs are bit-identical from both sides", bad == 0 && shared >= 96 * 65, bad + " differ, " + shared + " shared");
    int cbad = 0, ccount = 0;
    for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                var c = new Vec3(sx, sy, sz); var dirs = new List<Vec3>();
                for (int f = 0; f < 6; f++) if (Vec3.Dot(CubeSphere.Normal(f), c) > 0)
                    {
                        bool hiU = Vec3.Dot(CubeSphere.AxisU(f), c) > 0, hiV = Vec3.Dot(CubeSphere.AxisV(f), c) > 0;
                        dirs.Add(new QuadSector(new SectorId(f, hiV ? M - 1 : 0, hiU ? M - 1 : 0)).Direction(hiU ? 64 : 0, hiV ? 64 : 0));
                    }
                ccount++; if (dirs.Count != 3 || !Same(dirs[0], dirs[1]) || !Same(dirs[0], dirs[2])) cbad++;
            }
    Check("canonical vertex: the cube vertex is the same 3 double bits seen from its 3 corner sectors, 8 vertices", ccount == 8 && cbad == 0, cbad.ToString());
    int cf, ci, cj; Lattice.Canonical(4, Lattice.N, Lattice.N, out cf, out ci, out cj);
    Check("canonical vertex: lattice corner (face 4, N, N) is evaluated on the lowest face of the corner", cf == 0, cf.ToString());
}

// ---- portals, corridor graph, fillet ----
{
    // hand-picked arcs: one inside a face crossing level-10 borders, one across a cube edge (faces 0 / 2), one near a cube vertex
    var arcs = new[]
    {
        new[] { new Vec3(1, 0.2, 0.1).Normalized(), new Vec3(1, 0.26, 0.12).Normalized() },
        new[] { new Vec3(1, 0.9, 0.3).Normalized(), new Vec3(0.9, 1, 0.31).Normalized() },
        new[] { new Vec3(1, 0.99, 0.98).Normalized(), new Vec3(0.97, 1, 1).Normalized() },
    };
    int portals = 0, asym = 0; bool crossedFace = false;
    foreach (var arc in arcs)
    {
        Vec3 nr = Vec3.Cross(arc[0], arc[1]).Normalized();
        foreach (var c in RoadArc.Crossings(arc[0], arc[1]))
        {
            portals++; if (c.From.Face != c.To.Face) crossedFace = true;
            var px = Portal.Cross(Border.Between(c.From, c.To), nr, c.Q); var py = Portal.Cross(Border.Between(c.To, c.From), nr, c.Q);
            if (px.SCm != py.SCm || px.Border.Id != py.Border.Id) asym++;
        }
    }
    Check($"portal s: bit-identical from both regions on hand-picked arcs ({portals} portals, one across a cube edge)", portals >= 3 && crossedFace && asym == 0, portals + " portals, " + asym + " asym, crossedFace " + crossedFace);
    var b = Border.Between(new RegionId(0, 10, 500, 500), new RegionId(0, 10, 500, 501));
    Check("border: the same canonical border from either side, s = 0 at the start vertex, s = length at the end vertex", b.Id == Border.Between(new RegionId(0, 10, 500, 501), new RegionId(0, 10, 500, 500)).Id && (b.PointAt(0) - b.S).Length < 1e-15 && (b.PointAt(b.Length) - b.E).Length < 1e-9);
    Check("snap: a portal 10 m from the vertex moves to 256 m; one at 5 km stays", Portal.Snap(b, 1000, 256.0) == 25600 && Portal.Snap(b, 500000, 256.0) == 500000);
    var graph = CorridorGraph.Build(42); var graph2 = CorridorGraph.Build(42);
    Check("corridor graph: deterministic, 49152 settlements, more edges than nodes", graph.NodeCount == 49152 && graph.EdgeCount == graph2.EdgeCount && graph.EdgeCount > 49152);
    var fl = RegionPlane.Fillet(new[] { 0.0, 0.0 }, new[] { 2000.0, 0.0 }, new[] { 2000.0, 2000.0 }, 344.83);
    Check("fillet: a 90 degree turn ends at the portals", fl != null && Math.Abs(fl[fl.Count - 1][0] - 2000) < 1e-9 && Math.Abs(fl[fl.Count - 1][1] - 2000) < 1e-9);
}

// ---- noise ----
{
    ulong st = 3; int detBad = 0, rangeBad = 0; double minV = 1, maxV = -1;
    for (int i = 0; i < 20000; i++)
    {
        Vec3 p = new Vec3((SplitMix64.NextDouble(ref st) - 0.5) * 1.2e7, (SplitMix64.NextDouble(ref st) - 0.5) * 1.2e7, (SplitMix64.NextDouble(ref st) - 0.5) * 1.2e7);
        float a = PlanetNoise.Fbm(42, p.X, p.Y, p.Z, 12, 4), b = PlanetNoise.Fbm(42, p.X, p.Y, p.Z, 12, 4);
        if (BitConverter.SingleToInt32Bits(a) != BitConverter.SingleToInt32Bits(b)) detBad++;
        if (a < -1.0001f || a > 1.0001f) rangeBad++;
        minV = Math.Min(minV, a); maxV = Math.Max(maxV, a);
    }
    Check("noise: deterministic for the same double input, in [-1,1]", detBad == 0 && rangeBad == 0 && minV < -0.3 && maxV > 0.3);
    var smp = new PlanetNoise.HeightSampler(42); int smpBad = 0; ulong st2 = 11;
    for (int i = 0; i < 20000; i++)   // walk like a sector build (cells repeat) and jump (cells change): bits must equal PlanetNoise.Height
    {
        Vec3 p = i % 50 == 0 ? new Vec3((SplitMix64.NextDouble(ref st2) - 0.5) * 1.2e7, (SplitMix64.NextDouble(ref st2) - 0.5) * 1.2e7, 4e6) : new Vec3(3e6 + i * 4.0, -2e6, 4e6 + i * 0.5);
        if (BitConverter.SingleToInt32Bits(smp.Sample(p)) != BitConverter.SingleToInt32Bits(PlanetNoise.Height(42, p))) smpBad++;
    }
    Check("noise: HeightSampler (corner cache) is bit-identical to PlanetNoise.Height", smpBad == 0, smpBad.ToString());
    Vec3 q = new Vec3(4.5e6, 3.1e6, 2.9e6);
    Check("noise: seed changes the value", PlanetNoise.Fbm(1, q.X, q.Y, q.Z, 12, 4) != PlanetNoise.Fbm(2, q.X, q.Y, q.Z, 12, 4));
    // continuity across a lattice cell boundary (cell 4096 m): value noise is continuous
    double x0 = 4096.0 * 1000; double d = Math.Abs(PlanetNoise.Value(5, x0 - 1e-6, 17.3, 5.1, 12) - PlanetNoise.Value(5, x0 + 1e-6, 17.3, 5.1, 12));
    Check("noise: continuous across a cell boundary", d < 1e-6, d.ToString("E2"));
    // x rebuilt as origin + local differs by at most 1 ulp(double) in position => at most a few float ulp in value
    Vec3 org = new Vec3(4.5e6, 3.1e6, 2.9e6), loc = new Vec3(123.456789, -78.9, 45.678);
    float v0 = PlanetNoise.Height(9, org + loc), v1 = PlanetNoise.HeightFromOrigin(9, org, loc);
    Check("noise: HeightFromOrigin(origin, local) = Height(origin + local) bit for bit", BitConverter.SingleToInt32Bits(v0) == BitConverter.SingleToInt32Bits(v1));
}

// ---- RG-163: crossing attribution and joint snap at region / cube vertices ----
{
    Vec3 mid = Lattice.Compose(0, CubeSphere.Unwarp(-1.0 + 2.0 * 400 / 1024), CubeSphere.Unwarp(-1.0 + 2.0 * 400 / 1024));      // a 4-region vertex inside face 0
    Vec3 cube = new Vec3(1, 1, 1).Normalized();                                                                                  // 3-region cube vertex
    var cases = new List<(string name, Vec3 vertex, double offsetM, double headingDeg)>
    {
        ("diagonal through a 4-region vertex", mid, 0, 45), ("50 m beside a 4-region vertex", mid, 50, 45), ("40 m beside, nearly tangent to a border", mid, 40, 1.0),
        ("through a cube vertex", cube, 0, 30), ("50 m beside a cube vertex", cube, 50, 100), ("through a cube vertex along an edge", cube, 5, 0.5),
    };
    foreach (var cs in cases)
    {
        Vec3 east, north; CubeSphere.TangentAxes(CubeSphere.RegionOf(cs.vertex, 10).Face, cs.vertex, out east, out north);
        double h = cs.headingDeg * Math.PI / 180; Vec3 hint = east * Math.Cos(h) + north * Math.Sin(h);
        Vec3 side = Vec3.Cross(cs.vertex, hint).Normalized(); Vec3 p = RoadArc.Advance(cs.vertex, side, cs.offsetM);
        Vec3 ha = (hint - p * Vec3.Dot(hint, p)).Normalized(); Vec3 a = RoadArc.Advance(p, -ha, 15000), b = RoadArc.Advance(p, ha, 15000);
        var route = RoadArc.Route(a, b, 256.0); bool inSeg = true, same = true, clear = true, resolved = true; int grouped = 0;
        foreach (var pl in route)
        {
            resolved &= pl.C.Resolved; inSeg &= !pl.OffSegment; if (!pl.C.Resolved) continue; Border by = Border.Between(pl.C.To, pl.C.From); same &= by.Id == pl.B.Id && by.LengthCm == pl.B.LengthCm;
            if (pl.Grouped) grouped++; Vec3 pos = pl.B.PointAt(pl.SCm / 100.0);
            clear &= Math.Min((pos - pl.B.S).Length, (pos - pl.B.E).Length) * CubeSphere.Radius >= 255.999 || !pl.Grouped && Math.Min(pl.SRawCm, pl.B.LengthCm - pl.SRawCm) >= 25600;
        }
        Check($"route {cs.name}: {route.Count} crossings, all resolved and on their border segment", route.Count > 0 && resolved && inSeg);
        Check($"route {cs.name}: grouped portals >= 256 m from the vertex ({grouped} grouped), s identical from both sides", clear && same);
    }
}

// ---- RG-164: r2 eligibility (both ends >= 1 km from the nearest shifted portal) ----
{
    Vec3 v = CubeSphere.RegionCorner(new RegionId(0, 10, 500, 500), 0, 0), east, north; CubeSphere.TangentAxes(CubeSphere.RegionOf(v, 10).Face, v, out east, out north);
    Vec3 hint = (east + north).Normalized(), side = Vec3.Cross(v, hint).Normalized(); Vec3 p = RoadArc.Advance(v, side, 50);
    Vec3 ha = (hint - p * Vec3.Dot(hint, p)).Normalized();
    Func<double, double, bool> eligible = (backM, fwdM) =>
    {
        Vec3 a = RoadArc.Advance(p, -ha, backM), b = RoadArc.Advance(p, ha, fwdM); var route = RoadArc.Route(a, b, 256.0); double f, l;
        WhatIf.Planet.Experiment.RoadChecks.ShiftEnds(a, b, route, out f, out l); return WhatIf.Planet.Experiment.RoadChecks.Eligible(f, l);
    };
    Check("r2 eligibility: arc ending 300 m after a shifted portal is excluded", !eligible(15000, 300));
    Check("r2 eligibility: arc starting 300 m before a shifted portal is excluded", !eligible(300, 15000));
    Check("r2 eligibility: arc ending >= 1 km after the shifted portals is counted", eligible(15000, 3000));
    Check("r2 eligibility: arc with no shifted portal is counted", eligible(15000, 15000) && WhatIf.Planet.Experiment.RoadChecks.Eligible(-1, -1));
}

// ---- RG-165: WorldGen adapter (SectorPlanetInput v1 + SectorAdapter) ----
{
    const int M = SectorId.PerFaceSide;
    var id = new SectorId(0, M - 1, M - 1);    // cube-vertex sector: the most deformed quad (one 120 deg corner)
    var inp = WhatIf.Planet.WorldGen.SectorPlanetInput.Build(id, 42);
    var inp2 = WhatIf.Planet.WorldGen.SectorPlanetInput.Build(id, 42);
    bool nodes = true;
    for (int iz = 0; iz < inp.Side; iz++) for (int ix = 0; ix < inp.Side; ix++)
            if (inp.Base(inp.GridX(ix), inp.GridX(iz)) != inp.BaseHeightM[iz * inp.Side + ix] && ix > 0 && iz > 0 && ix < inp.Side - 1 && iz < inp.Side - 1) nodes = false;
    Check("adapter input: deterministic, Catmull-Rom returns the stored sample at every interior grid node", BitsEqual(inp.BaseHeightM, inp2.BaseHeightM) && inp.RegionSeed == inp2.RegionSeed && nodes);
    Check("adapter input: the region seed depends on the world seed and on the region, not on the sector inside the region",
        inp.RegionSeed != WhatIf.Planet.WorldGen.SectorPlanetInput.RegionSeedOf(id, 43)
        && inp.RegionSeed == WhatIf.Planet.WorldGen.SectorPlanetInput.RegionSeedOf(new SectorId(0, M - 2, M - 3), 42)
        && inp.RegionSeed != WhatIf.Planet.WorldGen.SectorPlanetInput.RegionSeedOf(new SectorId(0, M - 33, M - 1), 42));

    var a = new WhatIf.Planet.WorldGen.AdaptedSector();
    WhatIf.Planet.WorldGen.SectorAdapter.Build(inp, WhatIf.Planet.WorldGen.SectorAdapter.Model(inp), a);
    int n = QuadSector.Verts * QuadSector.Verts;
    var qx = new float[n]; var qy = new float[n]; var qz = new float[n]; var qw = new Vec3[n];
    new QuadSector(id).Build(42, true, qx, qy, qz, qw);
    int rimBad = 0, cover = 0; double maxC = 0;
    for (int j = 0; j < QuadSector.Verts; j++) for (int i = 0; i < QuadSector.Verts; i++)
        {
            int k = j * QuadSector.Verts + i; bool rim = i == 0 || j == 0 || i == 64 || j == 64;
            if (rim && (a.Taper[k] != 0 || BitConverter.DoubleToInt64Bits(a.World[k].X) != BitConverter.DoubleToInt64Bits(qw[k].X) || BitConverter.DoubleToInt64Bits(a.World[k].Y) != BitConverter.DoubleToInt64Bits(qw[k].Y) || BitConverter.DoubleToInt64Bits(a.World[k].Z) != BitConverter.DoubleToInt64Bits(qw[k].Z))) rimBad++;
            if (Math.Abs(a.X[k]) > inp.HalfM || Math.Abs(a.Z[k]) > inp.HalfM) cover++;
            maxC = Math.Max(maxC, Math.Abs(a.Contribution[k]));
        }
    Check("adapter: every rim vertex has taper 0 and the world position of the plain QuadSector.Build (terrain on), bit for bit", rimBad == 0, rimBad + " rim vertices differ");
    Check("adapter: the base grid covers every vertex of the deformed quad", cover == 0, cover + " outside");
    Check("adapter: WorldGen detail is present but bounded (0 < max |contribution| < relief 60 m)", maxC > 0.1 && maxC < 60, maxC.ToString("F3"));
}

Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
return failures == 0 ? 0 : 1;

static bool BitsEqual(float[] a, float[] b)
{
    for (int i = 0; i < a.Length; i++) if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false;
    return true;
}
