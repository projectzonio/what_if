using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using WhatIf.Planet;
using WhatIf.Planet.Anchors;
using WhatIf.Planet.Experiment;
using WhatIf.Planet.Surface;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Anchors.Experiment
{
    /// <summary>One area of the spike: 5 x 5 level-10 regions around a block centre (RG-169 blocks A and B, seed 1).</summary>
    public sealed class Area
    {
        public string Name; public RegionId Centre; public List<RegionId> Regions; public string Why;
        public string Faces => string.Join("+", Regions.Select(r => r.Face).Distinct().OrderBy(f => f));
    }

    public static class Program
    {
        public const ulong Seed = 1;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string F(double v, string f = "F1") => double.IsNaN(v) ? "n/a" : double.IsInfinity(v) ? "none" : v.ToString(f, Inv);

        /// <summary>usage: hash &lt;dir&gt; forward|reverse|threads4  |  measure &lt;dir&gt; [--repeats 3] [--md path --csv path --sha s --hw s --unity s]</summary>
        public static int Main(string[] args)
        {
            if (args.Length < 2) { Console.Error.WriteLine("usage: hash <dir> forward|reverse|threads4 | measure <dir> [--repeats n] [--md f --csv f --sha s --hw s --unity s]"); return 2; }
            string dir = args[1]; Directory.CreateDirectory(dir);
            string Opt(string k, string def) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
            var areas = Areas();
            if (args[0] == "hash") { Hash(dir, args.Length > 2 ? args[2] : "forward", areas); return 0; }
            if (args[0] == "corridors") return Corridors.Run(dir, areas, Opt("--md", null), Opt("--csv", null), Opt("--sha", ""), Opt("--hw", ""), Opt("--unity", ""));
            if (args[0] == "stalls" || args[0] == "seg") return Stalls.Run(dir, areas, args.Length > 3 ? args[2] : null, args.Length > 3 ? int.Parse(args[3], Inv) : -1);   // RG-184: replaces the hard-coded seg mode
            if (args[0] == "memprobe")   // RG-184 k2 diagnosis: retained bytes of the settlement worlds of an area split into terrain model / StreetSet / rest
            {
                var a = areas[args.Length > 2 ? int.Parse(args[2], Inv) : 0]; var aw = new AnchorWorld(Seed);
                var sets = AnchorSet.InRegions(aw.Field, a.Regions, false); foreach (var s in sets) aw.Field.LinksOf(s).ForEach(l => aw.Route(l));
                long M() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
                long m0 = M(); var ws = sets.Select(s => aw.World(s)).ToList(); long m1 = M();
                long segs = ws.Where(w => w.Ok).Sum(w => (long)w.Streets.Count);
                foreach (var w in ws) w.Streets = null; long m2 = M();
                foreach (var w in ws) w.Terrain = null; long m3 = M(); GC.KeepAlive(ws);
                Console.WriteLine($"area {a.Name}: {ws.Count} worlds (city {ws.Count(w => w.S.Class == SettlementClass.City)}, town {ws.Count(w => w.S.Class == SettlementClass.Town)}), street segments {segs}: total {(m1 - m0) / 1048576.0:F2} MiB, StreetSet {(m1 - m2) / 1048576.0:F2}, terrain model {(m2 - m3) / 1048576.0:F2}, rest {(m3 - m0) / 1048576.0:F2}; per segment {(m1 - m0) / (double)Math.Max(1, segs):F0} B");
                return 0;
            }
            if (args[0] == "settlements") return SettlementRows.Run(dir, areas, Opt("--md", null), Opt("--csv", null), Opt("--sha", ""), Opt("--hw", ""), Opt("--unity", ""));
            if (args[0] == "sectors") return Sectors.Run(dir, areas, Opt("--md", null), Opt("--csv", null), Opt("--sha", ""), Opt("--hw", ""), Array.IndexOf(args, "--closeout") >= 0);
            if (args[0] == "smoke")
            {
                var a = areas[args.Length > 2 ? int.Parse(args[2]) : 1]; var w = new AnchorWorld(Seed); var vb = Sectors.VariantB(w); var o = new AdaptedSector(); var inF = new bool[AdaptedSector.Count]; var dy = new double[AdaptedSector.Count];
                var ids = Sectors.SectorsOf(a.Centre).Take(200).ToList(); int foot = 0; double mx = -1e9;
                foreach (var id in ids) { var sw = Stopwatch.StartNew(); vb.Build(id, o); double ms = sw.Elapsed.TotalMilliseconds; int n = vb.Foot(id, o, inF, dy); foot += n; for (int k = 0; k < inF.Length; k++) if (inF[k]) mx = Math.Max(mx, dy[k]); if (ms > 50 || n > 0) Console.WriteLine($"{id} {ms:F1} ms foot {n}"); }
                Console.WriteLine($"foot vertices {foot}, max above {mx:F4}; routed {w.RoutedCount}, worlds {w.WorldCount} failed {w.Worlds.Count(x => !x.Ok)}"); return 0;
            }
            if (args[0] == "measure") return Measure.Run(dir, areas, int.Parse(Opt("--repeats", "3"), Inv), Opt("--md", null), Opt("--csv", null), Opt("--sha", ""), Opt("--hw", ""), Opt("--unity", ""));
            Console.Error.WriteLine("unknown mode " + args[0]); return 2;
        }

        /// <summary>The two areas: 5 x 5 regions at chart offsets (dc, dr) x mean side length around the RG-169 block A and B centres.</summary>
        public static List<Area> Areas()
        {
            var s = new PlanetSurface(Seed); var res = new List<Area>();
            foreach (var b in new[] { BlockChecks.FindBlockA(s), BlockChecks.FindBlockB(s) })
            {
                // walk over region neighbours: dc steps along the centre chart's E, then dr steps along N; each step takes the neighbour whose centre moves most along
                // the wanted 3D direction (across a cube edge the cell axes rotate; chart offsets alone merged two regions there)
                var c = new RegionChart(b.Centre);
                RegionId Walk(RegionId r, Vec3 dir, int steps)
                {
                    for (int s0 = 0; s0 < steps; s0++)
                    {
                        var cc = CubeSphere.RegionCentre(r); RegionId best = r; double bd = double.NegativeInfinity;
                        for (int d = 0; d < 4; d++) { var nb = CubeSphere.Neighbour(r, d); double v = Vec3.Dot(CubeSphere.RegionCentre(nb) - cc, dir); if (v > bd) { bd = v; best = nb; } }
                        r = best;
                    }
                    return r;
                }
                var l = new List<RegionId>();
                for (int dr = -2; dr <= 2; dr++) for (int dc = -2; dc <= 2; dc++) l.Add(Walk(Walk(b.Centre, dc < 0 ? -c.E : c.E, Math.Abs(dc)), dr < 0 ? -c.N : c.N, Math.Abs(dr)));
                l = l.Distinct().OrderBy(SettlementField.IdOf).ToList();
                var a = new Area { Name = b.Name, Centre = b.Centre, Regions = l, Why = b.Why };
                Console.WriteLine($"area {a.Name}: centre {a.Centre}, {a.Regions.Count} distinct regions, faces {a.Faces}");
                res.Add(a);
            }
            return res;
        }

        /// <summary>n5: anchor hash per area. forward = areas A, B, cells/links/corridors in id order; reverse = areas B, A and everything reversed; threads4 = 4 threads, each its own surface/field/builder, corridors dealt round robin.</summary>
        static void Hash(string dir, string mode, List<Area> areas)
        {
            var lines = new List<string>();
            var order = mode == "reverse" ? Enumerable.Reverse(areas).ToList() : areas;
            foreach (var a in order)
            {
                string h;
                if (mode != "threads4")
                {
                    var surf = new PlanetSurface(Seed); var set = AnchorSet.Build(new SettlementField(Seed, surf), new CorridorBuilder(surf), a.Regions, mode == "reverse");
                    h = set.Hash();
                }
                else
                {
                    var surf0 = new PlanetSurface(Seed); var f0 = new SettlementField(Seed, surf0);
                    var sets = AnchorSet.InRegions(f0, a.Regions, false); var ids = new HashSet<ulong>(sets.Select(s => s.Id)); var links = new SortedDictionary<ulong, Link>();
                    foreach (var s in sets) foreach (var l in f0.LinksOf(s)) if (ids.Contains(l.A.Id) && ids.Contains(l.B.Id)) links[l.Key] = l;
                    var all = links.Values.ToList(); var parts = new AnchorSet[4]; var th = new Thread[4];
                    for (int t = 0; t < 4; t++)
                    {
                        int tt = t;
                        th[t] = new Thread(() =>
                        {
                            var surf = new PlanetSurface(Seed); var f = new SettlementField(Seed, surf); var cb = new CorridorBuilder(surf); var p = new AnchorSet();
                            // each thread re-derives its own copy of the links' settlements and routes every 4th link (no shared mutable state)
                            var mine = new List<Link>();
                            for (int i = tt; i < all.Count; i += 4)
                            {
                                var la = f.Cell(all[i].A.Cell); var lb = f.Cell(all[i].B.Cell);
                                var own = f.LinksOf(la).First(x => x.Key == all[i].Key);
                                if (own.B.Id != lb.Id) throw new InvalidOperationException("link mismatch");
                                mine.Add(own);
                            }
                            foreach (var l in mine) p.Add(cb.Route(l));
                            parts[tt] = p;
                        });
                        th[t].Start();
                    }
                    foreach (var t in th) t.Join();
                    var cors = parts.SelectMany(p => p.Corridors).ToList(); var segs = new Dictionary<ulong, Segment[]>();
                    foreach (var p in parts) foreach (var kv in p.Segments) segs[kv.Key] = kv.Value;
                    h = AnchorSet.Hash(sets, cors, segs);
                }
                lines.Add($"{a.Name},{h}");
                Console.WriteLine($"hash {mode} area {a.Name}: {h}");
            }
            File.WriteAllLines(Path.Combine(dir, $"hash-{mode}.txt"), lines.OrderBy(x => x));
        }
    }
}
