using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using WhatIf.Planet;
using WhatIf.Planet.FarLayer;
using WhatIf.Planet.Runtime;
using WhatIf.Planet.WorldGen;

// Dependency-free tests for WhatIf.Planet.Runtime (RG-172): exit 0 = ALL PASSED, 1 = a check failed. The streamer is driven with a cheap region factory
// (a no-op region factory) and a fake sector builder, so every test takes milliseconds; the real builders are covered by the experiment (t8).
var failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 && !ok ? " :: " + detail : "")}");
    if (!ok) failures++;
}
const double R = CubeSphere.Radius;
Func<Vec3, double> height = d => 80.0 * Math.Sin(d.X * 37.0) * Math.Cos(d.Y * 29.0) + 20.0 * Math.Sin(d.Z * 301.0);
Vec3 At(int face, double a, double b) => CubeSphere.FromFaceAB(face, a, b);
Vec3 Move(Vec3 dir, Vec3 tangent, double metres) => (dir * R + tangent * metres).Normalized();
Vec3 Tangent(Vec3 dir) => Vec3.Cross(dir, new Vec3(0.3, 0.9, 0.1)).Normalized();

// ---- wanted sectors: radius, hysteresis, altitude cut ----
{
    var p = new StreamerParams(); var sw = new SectorWanted(); var res = new List<WantedSector>(); Vec3 g = At(0, 0.21, -0.33);
    sw.Compute(g, 2.0, null, p, res);
    double maxD = 0; foreach (var w in res) maxD = Math.Max(maxD, w.DistM);
    Check("wanted: every sector centre within the load radius", res.Count >= 10 && res.Count <= 40 && maxD <= p.LoadRadiusM, $"{res.Count} sectors, max {maxD:F1} m");
    var first = new HashSet<SectorId>(); foreach (var w in res) first.Add(w.Id);
    Check("wanted: the ground sector is wanted", first.Contains(SectorId.At(g)));

    // hysteresis: a point moved 100 m away keeps the previously wanted sectors up to 760 m and adds nothing new beyond 600 m
    Vec3 g2 = Move(g, Tangent(g), 100.0); var res2 = new List<WantedSector>(); sw.Compute(g2, 2.0, first, p, res2);
    int kept = 0, beyondLoad = 0, beyondUnload = 0;
    foreach (var w in res2) { if (w.DistM > p.LoadRadiusM) { beyondLoad++; if (!first.Contains(w.Id)) kept = -1000; } if (w.DistM > p.UnloadRadiusM) beyondUnload++; }
    var res3 = new List<WantedSector>(); sw.Compute(g2, 2.0, null, p, res3);
    Check("wanted: hysteresis keeps previously wanted sectors between 600 and 760 m", beyondLoad > 0 && kept == 0 && res2.Count > res3.Count, $"beyond load {beyondLoad}, with {res2.Count} vs without {res3.Count}");
    Check("wanted: nothing beyond the unload radius", beyondUnload == 0);
    Check("wanted: a sector never wanted before is not added beyond the load radius", res3.TrueForAll(w => w.DistM <= p.LoadRadiusM));

    var res4 = new List<WantedSector>(); sw.Compute(g, 1499.0, null, p, res4); var res5 = new List<WantedSector>(); sw.Compute(g, 1501.0, first, p, res5);
    Check("wanted: altitude cut at 1500 m ground clearance", res4.Count > 0 && res5.Count == 0, $"{res4.Count} below, {res5.Count} above");

    Vec3 edge = At(0, 1.0 - 1e-5, 0.2); var res6 = new List<WantedSector>(); sw.Compute(edge, 2.0, null, p, res6); var faces = new HashSet<int>(); foreach (var w in res6) faces.Add(w.Id.Face);
    Check("wanted: the near set crosses a cube edge", faces.Count == 2, string.Join(",", faces));
}

// ---- region plan: camera region first, prefetch scales with speed, floor 2 km ----
{
    var p = new StreamerParams(); var plan = new RegionPlan(); var res = new List<WantedRegion>(); Vec3 c = CubeSphere.RegionCentre(new RegionId(0, 10, 300, 400));
    plan.Compute(c, new Vec3(0, 0, 0), p.PrefetchM(0), res);
    Check("regions: the camera region alone when it is far from every border", res.Count == 1 && res[0].Cost < 0, res.Count + " regions");
    Check("regions: lead 60 s at 120 m/s = 7.2 km, floor 2 km", Math.Abs(p.PrefetchM(120) - 7200) < 1e-9 && Math.Abs(p.PrefetchM(10) - 2000) < 1e-9);
    // 1 km from the east border of the region (about 9.8 km wide): the east neighbour is wanted, the west one is not
    RegionId home = new RegionId(0, 10, 300, 400); Vec3 east = (CubeSphere.RegionCorner(home, 1, 0) + CubeSphere.RegionCorner(home, 1, 1)).Normalized();
    Vec3 tg = Vec3.Cross(Vec3.Cross(c, east), c).Normalized(); Vec3 nearEast = Move(east, -tg, 1000.0);
    plan.Compute(nearEast, tg, 2000.0, res);
    bool eastWanted = false, westWanted = false; foreach (var w in res) { if (w.Id.Equals(new RegionId(0, 10, 300, 401))) eastWanted = true; if (w.Id.Equals(new RegionId(0, 10, 300, 399))) westWanted = true; }
    Check("regions: the neighbour whose border is within prefetch is wanted, a far one is not", eastWanted && !westWanted, $"east {eastWanted} west {westWanted} of {res.Count}");
    plan.Compute(nearEast, tg, 120 * 30.0, res); double ce = 0, cw = 0; bool hasW = false;
    foreach (var w in res) { if (w.Id.Equals(new RegionId(0, 10, 300, 401))) ce = w.Cost; if (w.Id.Equals(new RegionId(0, 10, 300, 399))) { cw = w.Cost; hasW = true; } }
    Check("regions: ahead is cheaper than behind", !hasW || ce < cw, $"{ce} vs {cw}");
}

// ---- hole only when ready ----
PlanetStreamer Make(Action<RegionId> region, Action<QuadSector, AdaptedSector> build, StreamerParams p = null)
    => new PlanetStreamer(id => { region(id); return new FakeContent(build); }, height, p);
void Fake(QuadSector s, AdaptedSector o) { o.Lx[0] = s.Id.Row; o.Lx[1] = s.Id.Col; o.Lx[2] = s.Id.Face; Thread.Sleep(1); }
bool Pump(PlanetStreamer s, Vec3 dir, double alt, Func<bool> until, double seconds = 15)
{
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed.TotalSeconds < seconds) { s.Update(dir, alt, 1 / 60.0); if (until()) return true; Thread.Sleep(2); }
    return false;
}
{
    var gate = new ManualResetEventSlim(false); var seenWrong = 0;
    using (var s = Make(id => { }, (sec, o) => { gate.Wait(); Fake(sec, o); }))
    {
        Vec3 g = At(1, -0.2, 0.4);
        for (int i = 0; i < 30; i++) { s.Update(g, 2.0, 1 / 60.0); Thread.Sleep(2); }
        Check("hole: no near sector in the near set while nothing is ready", s.Near.Count == 0 && s.Info.Wanted > 10 && s.Info.NotReadyIn300 > 0, $"near {s.Near.Count} wanted {s.Info.Wanted}");
        Check("hole: far layer masks nothing yet", s.Near.Masks.Count == 0 && s.Selection.MaskedQuads == 0);
        gate.Set();
        bool ok = Pump(s, g, 2.0, () => { foreach (var x in s.Near.Sectors) if (!s.IsSectorReady(x)) seenWrong++; return s.Info.NotReadyIn600 == 0 && s.Info.Wanted > 0; });
        Check("hole: every sector of the near set was ready", ok && seenWrong == 0 && s.Near.Count == s.Info.Wanted, $"ok {ok} wrong {seenWrong} near {s.Near.Count} wanted {s.Info.Wanted}");
        Check("hole: masks follow the near set", s.Near.Masks.Count > 0 && s.Selection.MaskedQuads > 0);
        Check("hole: data of a ready sector is the built data", s.SectorData(SectorId.At(g)) != null && s.SectorData(SectorId.At(g)).Lx[0] == SectorId.At(g).Row);
    }
}

// ---- a sector waits for its region world (no fallback), the far layer keeps covering ----
{
    var gate = new ManualResetEventSlim(false);
    using (var s = Make(id => gate.Wait(), Fake))
    {
        Vec3 g = At(2, 0.1, 0.1);
        for (int i = 0; i < 40; i++) { s.Update(g, 2.0, 1 / 60.0); Thread.Sleep(2); }
        Check("region wait: sectors wait, nothing built, none in the near set", s.Info.WaitingForRegion600 > 10 && s.Near.Count == 0 && s.StatsB.SectorsBuilt == 0 && s.StatsA.SectorsBuilt == 0, $"waiting {s.Info.WaitingForRegion600} built {s.StatsB.SectorsBuilt}");
        gate.Set();
        bool ok = Pump(s, g, 2.0, () => s.Info.NotReadyIn600 == 0 && s.Info.Wanted > 0);
        Check("region wait: after the region is ready all sectors get built", ok && s.RegionWaitS.Count > 10, $"ok {ok} waits {s.RegionWaitS.Count}");
    }
}

// ---- altitude: no near sector requested above 1500 m, regions only below 3000 m ----
{
    int calls = 0;
    using (var s = Make(id => Interlocked.Increment(ref calls), Fake))
    {
        Vec3 g = At(3, 0.3, -0.1); Vec3 t = Tangent(g);
        for (int i = 0; i < 60; i++) { s.Update(Move(g, t, i * 4.0), 5000.0, 1 / 60.0); Thread.Sleep(2); }
        Check("altitude: 0 sectors requested at 5 km, no region built", s.SectorRequests == 0 && s.Near.Count == 0 && calls == 0, $"requests {s.SectorRequests} regions {calls}");
        for (int i = 0; i < 20; i++) { s.Update(g, 1400.0, 1 / 60.0); Thread.Sleep(2); }
        Check("altitude: below 1500 m sectors are requested again", s.SectorRequests > 10);
        int before = s.SectorRequests; s.Update(g, 1600.0, 1 / 60.0);
        Check("altitude: above 1500 m the wanted set is empty again", s.Info.Wanted == 0 && s.SectorRequests == before);
    }
}

// ---- caches are bounded; a moving camera keeps up ----
{
    var p = new StreamerParams { MaxSectors = 40, MaxRegions = 3 };
    using (var s = Make(id => { }, Fake, p))
    {
        Vec3 g = At(4, -0.4, 0.2); Vec3 t = Tangent(g); double dist = 0; int notReady300 = 0;
        var sw = Stopwatch.StartNew(); double last = 0;
        while (sw.Elapsed.TotalSeconds < 6)
        {
            double now = sw.Elapsed.TotalSeconds, dt = now - last; last = now; dist += 120.0 * dt;
            s.Update(Move(g, t, dist), 0.0, dt); if (now > 1.0 && s.Info.NotReadyIn300 > 0) notReady300++; Thread.Sleep(3);
        }
        Check("caches: sector cache stays at the cap", s.ReadySectorCount <= p.MaxSectors + 5, "ready " + s.ReadySectorCount);
        Check("caches: region cache stays at the cap", s.ReadyRegionCount <= p.MaxRegions + 1, "regions " + s.ReadyRegionCount);
        Check("caches: byte count under the cap", s.CacheBytes <= p.MaxCacheBytes, "bytes " + s.CacheBytes);
        Check("caches: no sector inside 300 m missing at 120 m/s (fake builds)", notReady300 == 0, notReady300 + " frames");
        Check("caches: speed estimate near 120 m/s", Math.Abs(s.Info.SpeedMps - 120) < 25, s.Info.SpeedMps.ToString("F1"));
    }
}

Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
return failures == 0 ? 0 : 1;

sealed class FakeContent : ISectorContent
{
    readonly Action<QuadSector, AdaptedSector> build;
    public FakeContent(Action<QuadSector, AdaptedSector> build) { this.build = build; }
    public void Build(QuadSector sec, AdaptedSector o) => build(sec, o);
}
