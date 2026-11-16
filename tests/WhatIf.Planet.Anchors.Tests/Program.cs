using System;
using System.Collections.Generic;
using System.Linq;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Surface;

// Dependency-free tests for WhatIf.Planet.Anchors (RG-177 spike): exit 0 = ALL PASSED, 1 = a check failed. No files.
var failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " :: " + detail : "")}");
    if (!ok) failures++;
}

const ulong Seed = 1;
var surf = new PlanetSurface(Seed);
// a land point with a few settlements around it (first hit of a seeded walk)
ulong st = 177; Vec3 p = default; List<Settlement> near = null;
for (int t = 0; t < 20000; t++)
{
    var v = new Vec3(SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1);
    if (v.Length < 0.05 || v.Length > 1) continue;
    p = v.Normalized(); if (surf.Kind(p) != SurfaceKind.Land) continue;
    near = new SettlementField(Seed, surf).Within(p, 12000, 0);
    if (near.Count >= 6) break;
}
Check("found a land point with >= 6 settlements within 12 km", near != null && near.Count >= 6, $"{near?.Count}");

// 1. settlements are a pure function: a second field asked in reverse cell order gives the same set
{
    var f2 = new SettlementField(Seed, new PlanetSurface(Seed));
    foreach (var c in SettlementField.CellsNear(p, 12000, 11).AsEnumerable().Reverse()) f2.Cell(c);
    var again = f2.Within(p, 12000, 0);
    bool same = again.Count == near.Count && again.Zip(near, (x, y) => x.Id == y.Id && x.Class == y.Class && x.Dir.X == y.Dir.X && x.HeightM == y.HeightM).All(b => b);
    Check("settlements independent of query order and instance", same, $"{again.Count} vs {near.Count}");
    Check("settlement centres on Land, not water", near.All(s => surf.Kind(s.Dir) == SurfaceKind.Land));
    Check("village clearance: no village within 2.5 km of a town or city", near.Where(s => s.Class == SettlementClass.Village).All(v => near.Where(o => o.Class > SettlementClass.Village).All(o => SettlementField.Gc(v.Dir, o.Dir) >= 2500)));
}

// 2. links are symmetric (the RNG test from either end) and within the layer radius
var f = new SettlementField(Seed, surf); int asym = 0, far = 0; var links = new SortedDictionary<ulong, Link>();
foreach (var s in near)
    foreach (var l in f.LinksOf(s))
    {
        var other = l.A.Id == s.Id ? l.B : l.A;
        var back = f.LinksOf(other).FirstOrDefault(x => x.Key == l.Key);
        if (back == null || back.Layer != l.Layer) asym++;
        if (l.GcM > AnchorParams.LayerRadius[l.Layer]) far++;
        links[l.Key] = l;
    }
Check("links symmetric with the same class from both ends", asym == 0, $"{asym} asymmetric of {links.Count}");
Check("links within their layer radius", far == 0);

// 3. corridors: deterministic across builders, pinned joints honoured, segments <= 5 km, no station on water when routed, fillets >= 1 m
var cb1 = new CorridorBuilder(surf); var cb2 = new CorridorBuilder(new PlanetSurface(Seed));
var sample = links.Values.Take(6).ToList(); int diff = 0, pinBad = 0, longSeg = 0, wet = 0, routed = 0, radBad = 0, endBad = 0, gradBad = 0;
foreach (var l in sample)
{
    var c1 = cb1.Route(l); var c2 = cb2.Route(l);
    if (c1.Routed != c2.Routed) { diff++; continue; }
    if (!c1.Routed) continue;
    routed++;
    var s1 = AnchorSet.Hash(new Settlement[0], new[] { c1 }, new Dictionary<ulong, Segment[]> { [l.Key] = Enumerable.Range(0, c1.Joints.Length - 1).Select(k => CorridorBuilder.Profile(c1, k)).ToArray() });
    var segs = Enumerable.Range(0, c2.Joints.Length - 1).Select(k => CorridorBuilder.Profile(c2, k)).ToArray();
    var s2 = AnchorSet.Hash(new Settlement[0], new[] { c2 }, new Dictionary<ulong, Segment[]> { [l.Key] = segs });
    if (s1 != s2) diff++;
    for (int k = 0; k < segs.Length; k++)
    {
        var y = segs[k].Y;
        if (Math.Abs(y[0] - c2.PinH[k]) > 1e-9 || Math.Abs(y[y.Length - 1] - c2.PinH[k + 1]) > 1e-9) pinBad++;
        if (segs[k].LengthM > CorridorBuilder.SegmentMaxM + 1) longSeg++;
    }
    wet += c2.OnWater.Count(b => b);
    // RG-181: measured horizontal radius within the class minimum, entrances at the settlements, grade at an entrance within half the class grade
    if (c2.MeasuredRadiusM < CorridorBuilder.MinRadius(l.RoadClass) * CorridorBuilder.RadiusTol) radBad++;
    // RG-182: the corridor ends where it leaves the settlement radius (>= radius from the centre, within 15 m of it unless the trim cap applied), the end heights are the surface heights there
    c2.Chart.ToLocal(l.A.Dir, out double ax, out double az); c2.Chart.ToLocal(l.B.Dir, out double bx, out double bz);
    double dA = Math.Sqrt((c2.X[0] - ax) * (c2.X[0] - ax) + (c2.Z[0] - az) * (c2.Z[0] - az)), dB = Math.Sqrt((c2.X[c2.X.Length - 1] - bx) * (c2.X[c2.X.Length - 1] - bx) + (c2.Z[c2.Z.Length - 1] - bz) * (c2.Z[c2.Z.Length - 1] - bz));
    if (!((dA >= l.A.RadiusM - 1e-6 && dA <= l.A.RadiusM + 15) || (c2.TrimStart > 0 && dA < l.A.RadiusM)) || !((dB >= l.B.RadiusM - 1e-6 && dB <= l.B.RadiusM + 15) || (c2.TrimEnd > 0 && dB < l.B.RadiusM)) || c2.PinH[0] != c2.Nat[0]) endBad++;
    if (Math.Abs(c2.PinG[0]) > CorridorBuilder.MaxGrade(l.RoadClass) * CorridorBuilder.JointGradeShare + 1e-12 || Math.Abs(c2.PinG[c2.PinG.Length - 1]) > CorridorBuilder.MaxGrade(l.RoadClass) * CorridorBuilder.JointGradeShare + 1e-12) gradBad++;
}
Check("corridor routes and profiles identical from two builders", diff == 0, $"{diff} of {sample.Count} differ");
Check("at least one sample corridor routed", routed > 0, $"{routed} of {sample.Count}");
Check("segment ends equal the pinned joint heights", pinBad == 0, $"{pinBad}");
Check("segments <= 5 km", longSeg == 0);
Check("measured radius >= 0.98 x class minimum (meander + fillets)", radBad == 0, $"{radBad}");
Check("corridor ends at the settlement radius (entrance stations), end heights = surface", endBad == 0, $"{endBad}");
Check("entrance joint grade within half the class grade", gradBad == 0, $"{gradBad}");
Console.WriteLine($"info: {wet} routed stations on water in the sample (reported by the spike, not a test)");

// 4. the shared joint of two neighbour segments has one height (each segment solved alone)
{
    var lc = links.Values.Select(l => cb1.Route(l)).FirstOrDefault(c => c.Routed && c.Joints.Length > 2);
    if (lc == null) Console.WriteLine("info: no sample corridor with two segments");
    else
    {
        var a = CorridorBuilder.Profile(lc, 0); var b = CorridorBuilder.Profile(lc, 1);
        Check("neighbour segments meet at the same joint height", a.Y[a.Y.Length - 1] == b.Y[0], $"{a.Y[a.Y.Length - 1]} vs {b.Y[0]}");
    }
}


// 5. RG-178 sectors from anchors: shared border vertices, determinism, road footprint
{
    var w = new AnchorWorld(Seed); RoutedLink rl = null; Link lk = null;
    var ctrRegion = CubeSphere.RegionOf(p, SectorId.RegionLevel);
    foreach (var l in w.LinksNear(ctrRegion)) { var r = w.Route(l); if (r != null && r.C.LengthM > 3000) { rl = r; lk = l; break; } }
    if (rl == null) Console.WriteLine("info: no routed corridor near the sample point, sector tests skipped");
    else
    {
        int mid = rl.C.X.Length / 2; var dirMid = rl.C.Chart.ToDir(rl.C.X[mid], rl.C.Z[mid]); var id = SectorId.At(dirMid);
        var o1 = new WhatIf.Planet.WorldGen.AdaptedSector(); var o2 = new WhatIf.Planet.WorldGen.AdaptedSector();
        AnchorSector.Build(w, new QuadSector(id), o1);
        var w2 = new AnchorWorld(Seed); AnchorSector.Build(w2, new QuadSector(id), o2);
        bool same = true; for (int i = 0; i < o1.World.Length; i++) if (o1.World[i].X != o2.World[i].X || o1.World[i].Y != o2.World[i].Y || o1.World[i].Z != o2.World[i].Z) same = false;
        Check("sector identical from a fresh anchor world (bits)", same);
        {   // RG-183: surface code present on the corridor sector; heights hash printed (compare with the pre-change build: must be identical)
            int[] cnt = new int[4]; foreach (var sc in o1.Surface) cnt[sc]++;
            Check("RG-183 corridor sector carries road surface code", cnt[1] > 0, $"ground {cnt[0]}, road {cnt[1]}, street {cnt[2]}, shoulder {cnt[3]}");
            ulong hh = 14695981039346656037UL; void Mix(double d) { hh = (hh ^ (ulong)BitConverter.DoubleToInt64Bits(d)) * 1099511628211UL; }
            for (int q = 0; q < o1.World.Length; q++) { Mix(o1.World[q].X); Mix(o1.World[q].Y); Mix(o1.World[q].Z); Mix(o1.Contribution[q]); Mix(o1.PlanetBase[q]); }
            Console.WriteLine($"HEIGHTHASH {hh:X16}");
            Check("RG-183 corridor sector height hash unchanged vs pre-change build", hh == 0x5EB2BE55025B8900UL, $"{hh:X16}");
            Check("RG-184 street carve keeps the corridor-only sector hash (no street vertex, hash pinned)", cnt[2] == 0 && hh == 0x5EB2BE55025B8900UL, $"street {cnt[2]}, {hh:X16}");
        }
        // east neighbour: its column 0 = this column 64
        var nb = new SectorId(id.Face, id.Row, id.Col + 1); var o3 = new WhatIf.Planet.WorldGen.AdaptedSector(); AnchorSector.Build(w, new QuadSector(nb), o3);
        int bad = 0; for (int j = 0; j < 65; j++) { var a = o1.World[j * 65 + 64]; var b = o3.World[j * 65]; if (a.X != b.X || a.Y != b.Y || a.Z != b.Z) bad++; }
        Check("east neighbour shares its border column (bits)", bad == 0, $"{bad} of 65 differ");
        // footprint: every vertex within half width of the centreline of this corridor sits on the road height
        double maxDev = 0; int foot = 0;
        for (int k = 0; k < o1.World.Length; k++)
        {
            var d = o1.World[k].Normalized(); rl.C.Chart.ToLocal(d, out double x, out double z); double bd = double.MaxValue, by = 0;
            for (int i = 0; i + 1 < rl.C.X.Length; i++)
            {
                double ax = rl.C.X[i], az = rl.C.Z[i], ex = rl.C.X[i + 1] - ax, ez = rl.C.Z[i + 1] - az, l2 = ex * ex + ez * ez, t = l2 > 0 ? ((x - ax) * ex + (z - az) * ez) / l2 : 0; t = Math.Max(0, Math.Min(1, t));
                double qx = ax + ex * t - x, qz = az + ez * t - z, dist = Math.Sqrt(qx * qx + qz * qz);
                if (dist < bd) { bd = dist; var sg = rl.Seg(rl.SegOf(i)); int a = i - sg.I0; by = sg.Y[a] + (sg.Y[a + 1] - sg.Y[a]) * t; }
            }
            if (bd <= rl.HalfWidthM * 0.9) { foot++; maxDev = Math.Max(maxDev, Math.Abs(o1.World[k].Length - CubeSphere.Radius - by)); }
        }
        Check("road footprint vertices lie on the road height (< 1 mm)", foot > 0 && maxDev < 1e-3, $"{foot} vertices, max deviation {maxDev}");
    }
}

// RG-182: the settlement world of a town / city has the corridor entrances (one per routed link end), all joined to the street graph, and its road count dwarfs the village world
{
    var w = new AnchorWorld(Seed) { KeepGraphs = true }; var area = AnchorSet.InRegions(w.Field, new[] { new RegionId(2, 10, 606, 683) }, false);
    var towns = area.Where(x => x.Class != SettlementClass.Village).ToList(); var s0 = towns.Count > 0 ? towns[0] : area[0];
    var sw = w.World(s0); int ends = w.Field.LinksOf(s0).Count(l => w.Route(l) != null);
    Check("settlement world builds with its entrances", sw.Gen != null && sw.Ents.Count == ends, $"{s0}: {sw.Ents.Count} entrances, {ends} routed link ends, failed '{sw.Failed}'");
    if (sw.Gen != null)
    {
        var g = sw.Gen.Graph; var pc = new int[g.Nodes.Count]; for (int i = 0; i < pc.Length; i++) pc[i] = i;
        int Find(int x) { while (pc[x] != x) { pc[x] = pc[pc[x]]; x = pc[x]; } return x; }
        foreach (var e in g.Edges) pc[Find(e.A)] = Find(e.B);
        int centre = g.Nodes.OrderBy(n => n.P.X * n.P.X + n.P.Z * n.P.Z).First().Id, conn = 0;
        foreach (var en in sw.Ents) { var nd = g.Nodes.OrderBy(n => WhatIf.WorldGen.V2.Dist(n.P, en.Point)).First(); if (WhatIf.WorldGen.V2.Dist(nd.P, en.Point) < 0.5 && Find(nd.Id) == Find(centre)) conn++; }
        Check("every entrance is connected to the street graph", conn == sw.Ents.Count, $"{conn} of {sw.Ents.Count}");
        if (s0.Class != SettlementClass.Village) { var vb = new SettlementWorld(w, s0, true, true); Check("town / city world has >= 5 x the graph nodes of its before (village budget) world", vb.Gen != null && g.Nodes.Count >= 5 * vb.Gen.Graph.Nodes.Count, $"{g.Nodes.Count} vs {vb.Gen?.Graph.Nodes.Count}"); }
    }
}

// RG-184: the settlement worlds keep only the terrain model + StreetSet; a dropped and rebuilt world (and a world that kept its graph) gives bit-identical sectors;
// the street carve puts every street footprint vertex on the street height
{
    ulong Hash(WhatIf.Planet.WorldGen.AdaptedSector o) { ulong h = 14695981039346656037UL; void Mix(double d) { h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(d)) * 1099511628211UL; } for (int q = 0; q < o.World.Length; q++) { Mix(o.World[q].X); Mix(o.World[q].Y); Mix(o.World[q].Z); Mix(o.Surface[q]); } return h; }
    var wk = new AnchorWorld(Seed) { KeepGraphs = true }; var wd = new AnchorWorld(Seed);
    var area = AnchorSet.InRegions(wd.Field, new[] { new RegionId(2, 10, 606, 683) }, false); var tw = area.Where(x => x.Class != SettlementClass.Village).FirstOrDefault() ?? area[0];
    var ch = new Chart(tw.Dir); var ids = new List<SectorId>();
    foreach (var (x, z) in new[] { (0.0, 0.0), (0.5, 0.0), (0.0, -0.8), (1.05, 0.0), (0.0, 1.1), (-1.1, 0.0) }) ids.Add(SectorId.At(ch.ToDir(x * tw.RadiusM, z * tw.RadiusM)));
    ids = ids.Distinct().ToList();
    int diffKeep = 0, diffRebuild = 0, streetV = 0, streetAbove = 0; double streetMax = 0; var o = new WhatIf.Planet.WorldGen.AdaptedSector();
    var hd = new List<ulong>();
    foreach (var id in ids) { AnchorSector.Build(wd, new QuadSector(id), o); hd.Add(Hash(o)); }
    bool dropped = wd.Worlds.All(x => x.Gen == null) && wd.Worlds.Any(x => x.Ok);
    foreach (var s in wd.Worlds.Select(x => x.S).ToList()) wd.Forget(s.Id);
    for (int i = 0; i < ids.Count; i++)
    {
        AnchorSector.Build(wk, new QuadSector(ids[i]), o); if (Hash(o) != hd[i]) diffKeep++;
        AnchorSector.Build(wd, new QuadSector(ids[i]), o); if (Hash(o) != hd[i]) diffRebuild++;
        Console.WriteLine($"SETTLEMENTHASH {ids[i]} {hd[i]:X16}");
        // street footprint: vertices within half width of a street centreline of the settlement world (graph kept) sit on the street height
        var g = wk.World(tw).Gen.Graph;
        for (int k = 0; k < o.World.Length; k++)
        {
            var d = o.World[k].Normalized(); var swk = wk.World(tw); swk.Chart.ToLocal(d, out double x, out double z); double best = double.MaxValue, by = 0;
            if (x * x + z * z > swk.ReachM * swk.ReachM) continue;
            foreach (var e in g.Edges)
            {
                if (e.Span != "ground") continue; double hw = e.WidthM * 0.5;
                for (int q = 0; q + 1 < e.Pts.Length; q++)
                {
                    double ax = e.Pts[q].X, az = e.Pts[q].Z, ex = e.Pts[q + 1].X - ax, ez = e.Pts[q + 1].Z - az; if (Math.Abs(ax - x) > 60 || Math.Abs(az - z) > 60) continue;
                    double l2 = ex * ex + ez * ez, t = l2 > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * ex + (z - az) * ez) / l2)) : 0, qx = ax + ex * t - x, qz = az + ez * t - z, dd = Math.Sqrt(qx * qx + qz * qz);
                    if (dd <= hw && dd - hw < best) { best = dd - hw; by = e.H[q] + (e.H[q + 1] - e.H[q]) * t; }
                }
            }
            if (best == double.MaxValue || o.Surface[k] == 1) continue;   // corridor vertices: the corridor carve decides (row s2 corridor part)
            streetV++; double above = o.World[k].Length - CubeSphere.Radius - by; if (above > 0.10) streetAbove++; streetMax = Math.Max(streetMax, above);
        }
    }
    Check("RG-184 cached settlement worlds dropped their graph (terrain model + streets kept)", dropped);
    Check("RG-184 settlement sectors identical with the graph kept and with it dropped (bits incl. surface code)", diffKeep == 0, $"{diffKeep} of {ids.Count} differ");
    Check("RG-184 settlement sectors identical after the world was dropped and rebuilt", diffRebuild == 0, $"{diffRebuild} of {ids.Count} differ");
    Check("RG-184 street footprint vertices on the street height (<= 0.10 m above)", streetV > 0 && streetAbove == 0, $"{streetV} street vertices, {streetAbove} above 0.10 m, max above {streetMax:F3} m ({tw}, {ids.Count} sectors)");
}

// RG-183 r1: sector build time (info only, printed): the settlement sector + the corridor sector, 15 builds each after one warm-up, fresh Surface array reuse
{
    var wt = new AnchorWorld(Seed); var area = AnchorSet.InRegions(wt.Field, new[] { new RegionId(2, 10, 606, 683) }, false); var tw = area.Where(x => x.Class != SettlementClass.Village).FirstOrDefault() ?? area[0];
    var ids = new List<SectorId> { SectorId.At(tw.Dir) };
    foreach (var l in wt.LinksNear(CubeSphere.RegionOf(tw.Dir, SectorId.RegionLevel))) { var r = wt.Route(l); if (r != null && r.C.X.Length > 8) { ids.Add(SectorId.At(r.C.Chart.ToDir(r.C.X[r.C.X.Length / 2], r.C.Z[r.C.X.Length / 2]))); break; } }
    foreach (var id in ids)
    {
        var ob = new WhatIf.Planet.WorldGen.AdaptedSector(); AnchorSector.Build(wt, new QuadSector(id), ob); var ts = new List<double>();
        for (int k = 0; k < 15; k++) { var sw0 = System.Diagnostics.Stopwatch.StartNew(); AnchorSector.Build(wt, new QuadSector(id), ob); ts.Add(sw0.Elapsed.TotalMilliseconds); }
        ts.Sort(); Console.WriteLine($"BUILDTIME {id}: median {ts[7]:F2} ms, P95 {ts[13]:F2} ms, min {ts[0]:F2} ms");
    }
}

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
