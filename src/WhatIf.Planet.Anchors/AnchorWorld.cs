using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WhatIf.Planet;
using WhatIf.Planet.Surface;
using Generator = WhatIf.WorldGen.Generator;
using WorldRules = WhatIf.WorldGen.WorldRules;
using WorldSpec = WhatIf.WorldGen.WorldSpec;
using TerrainSpec = WhatIf.WorldGen.TerrainSpec;
using TerrainModel = WhatIf.WorldGen.TerrainModel;

namespace WhatIf.Planet.Anchors
{
    /// <summary>A routed corridor with its lazily profiled segments (thread safe: segments are pure functions, a race computes one twice).</summary>
    public sealed class RoutedLink
    {
        public readonly Corridor C; public readonly double HalfWidthM; readonly Segment[] segs; readonly object gate = new object();
        public readonly double MinX, MaxX, MinZ, MaxZ;   // chart bounding box of the centreline
        public RoutedLink(Corridor c)
        {
            C = c; HalfWidthM = 0.5 * WhatIf.WorldGen.RoadClass.Width(c.Link.RoadClass);
            segs = new Segment[c.Joints.Length - 1];
            MinX = MinZ = double.MaxValue; MaxX = MaxZ = double.MinValue;
            for (int i = 0; i < c.X.Length; i++) { MinX = Math.Min(MinX, c.X[i]); MaxX = Math.Max(MaxX, c.X[i]); MinZ = Math.Min(MinZ, c.Z[i]); MaxZ = Math.Max(MaxZ, c.Z[i]); }
        }
        public Segment Seg(int k) { var s = segs[k]; if (s != null) return s; s = CorridorBuilder.Profile(C, k); lock (gate) { if (segs[k] == null) segs[k] = s; return segs[k]; } }
        public int SegmentCount => segs.Length;
        /// <summary>Segment index that owns the station interval [i, i+1].</summary>
        public int SegOf(int i) { int k = 0; while (k + 1 < segs.Length && C.Joints[k + 1] <= i) k++; return k; }
        /// <summary>Slowest profiled segment so far (s) and its stall flag (diagnostics of the cold first touch).</summary>
        public double MaxSegmentSeconds { get { double m = 0; lock (gate) foreach (var s in segs) if (s != null) m = Math.Max(m, s.Seconds); return m; } }
        public Segment SlowestSegment { get { Segment m = null; lock (gate) foreach (var s in segs) if (s != null && (m == null || s.Seconds > m.Seconds)) m = s; return m; } }
        public int ProfiledSegments { get { int n = 0; lock (gate) foreach (var s in segs) if (s != null) n++; return n; } }
    }

    /// <summary>
    /// RG-184: the ground streets of a settlement world in compact form (what AnchorSector reads besides the terrain model): per segment of every ground edge
    /// ax, az, bx, bz, ya, yb, half width (floats, settlement chart metres). The graph and its samples are dropped after the build.
    /// </summary>
    public sealed class StreetSet
    {
        public const int Stride = 7;
        public readonly float[] D; public int Count => D.Length / Stride;
        public StreetSet(WhatIf.WorldGen.RoadGraph g)
        {
            int n = 0; foreach (var e in g.Edges) if (e.Span == "ground" && e.Pts != null) n += Math.Max(0, e.Pts.Length - 1);
            D = new float[n * Stride]; int o = 0;
            foreach (var e in g.Edges)
            {
                if (e.Span != "ground" || e.Pts == null) continue;
                float hw = (float)(0.5 * e.WidthM);
                for (int k = 0; k + 1 < e.Pts.Length; k++)
                {
                    D[o++] = (float)e.Pts[k].X; D[o++] = (float)e.Pts[k].Z; D[o++] = (float)e.Pts[k + 1].X; D[o++] = (float)e.Pts[k + 1].Z;
                    D[o++] = (float)e.H[k]; D[o++] = (float)e.H[k + 1]; D[o++] = hw;
                }
            }
        }
    }

    /// <summary>The WorldGen world of one settlement (Motorway = false, one centre), generated in the gnomonic chart at the settlement centre. TerrainModel is serialised by a lock.
    /// RG-184: keeps only what AnchorSector reads (terrain model, StreetSet, entrances); Gen (graph, samples, log) only with keepGraph (experiments, tests).</summary>
    public sealed class SettlementWorld
    {
        public readonly Settlement S; public readonly Chart Chart; public readonly double ReachM;
        public WhatIf.WorldGen.GenResult Gen; public double Seconds; public string Failed;
        public TerrainModel Terrain; public StreetSet Streets; public int GraphNodes; public double StreetKm;
        public bool Ok => Terrain != null;
        public readonly object Gate = new object();
        public double EntranceSeconds; public int EntranceCount;
        public List<WhatIf.WorldGen.Entrance> Ents = new List<WhatIf.WorldGen.Entrance>();
        /// <param name="before">the RG-181 world (village street budget for every class, no entrances) for the before columns of the RG-182 measurement</param>
        /// <param name="keepGraph">keep the full GenResult (graph, samples, log) after the build; the sectors do not need it</param>
        public SettlementWorld(AnchorWorld w, Settlement s, bool before = false, bool keepGraph = false)
        {
            S = s; Chart = new Chart(s.Dir); ReachM = s.RadiusM + AnchorParams.BlendM; ulong seed = w.Seed;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ents = before ? new List<WhatIf.WorldGen.Entrance>() : w.EntrancesOf(s, Chart); Ents = ents; EntranceSeconds = sw.Elapsed.TotalSeconds; EntranceCount = ents.Count; sw.Restart();
            var spec = new WorldSpec { Seed = (long)SplitMix64.Mix(seed ^ s.Id), VcStallOuter = 300, Motorway = false, Villages = 0, CentreIsCity = before ? (bool?)null : s.Class != SettlementClass.Village, Entrances = ents };
            spec.Terrain = new TerrainSpec { ReliefM = 60, BaseWavelengthM = 1400, Octaves = 5 };
            spec.SizeKm = Math.Ceiling(2 * (s.RadiusM + AnchorParams.BlendM + 300) / 100.0) / 10.0;
            spec.City = new WhatIf.WorldGen.V2(0, 0); spec.CityRadiusM = s.RadiusM;
            try
            {
                Gen = Generator.Run(spec, WorldRules.Synthetic(), (x, z) => AnchorWorld.ThreadSurface(seed).Height(Chart.ToDir(x, z)), (x, z) => { var k = AnchorWorld.ThreadSurface(seed).Kind(Chart.ToDir(x, z)); return k == SurfaceKind.Ocean || k == SurfaceKind.Sea || k == SurfaceKind.Lake; });
            }
            catch (InvalidOperationException e) { Failed = e.Message; }
            if (Gen != null)
            {
                Terrain = Gen.Terrain; Streets = new StreetSet(Gen.Graph); GraphNodes = Gen.Graph.Nodes.Count;
                foreach (var e in Gen.Graph.Edges) StreetKm += e.LengthM / 1000;
                if (!keepGraph) Gen = null;
            }
            Seconds = sw.Elapsed.TotalSeconds;
        }
    }

    /// <summary>
    /// RG-178 variant B content source: every level-15 sector is a pure function of (seed, vertex directions): the ground (surface + global detail) with the settlement
    /// WorldGen terrain blended in inside radius + BlendM and the corridor carve (WorldGen rule: road height inside half width + 1.5 m, then cut / fill 1 : 1.5)
    /// of the nearest corridor. No region, no border taper. Thread safe: shared caches are concurrent; field, builder and surface are per thread.
    /// </summary>
    public sealed class AnchorWorld
    {
        public readonly ulong Seed;
        public AnchorWorld(ulong seed) { Seed = seed; }

        [ThreadStatic] static PlanetSurface tsSurf; [ThreadStatic] static SettlementField tsField; [ThreadStatic] static CorridorBuilder tsBuilder;
        public static PlanetSurface ThreadSurface(ulong seed) { var s = tsSurf; if (s == null || s.Seed != seed) { tsSurf = s = new PlanetSurface(seed); tsField = null; tsBuilder = null; } return s; }
        public SettlementField Field { get { var s = ThreadSurface(Seed); return tsField ?? (tsField = new SettlementField(Seed, s)); } }
        public CorridorBuilder Builder { get { var s = ThreadSurface(Seed); return tsBuilder ?? (tsBuilder = new CorridorBuilder(s)); } }

        readonly ConcurrentDictionary<RegionId, Link[]> regionLinks = new ConcurrentDictionary<RegionId, Link[]>();
        readonly ConcurrentDictionary<ulong, RoutedLink> routed = new ConcurrentDictionary<ulong, RoutedLink>();   // null value = unroutable
        readonly ConcurrentDictionary<ulong, SettlementWorld> worlds = new ConcurrentDictionary<ulong, SettlementWorld>();
        readonly ConcurrentDictionary<RegionId, Settlement[]> regionSettlements = new ConcurrentDictionary<RegionId, Settlement[]>();
        public int RoutedCount => routed.Count; public int WorldCount => worlds.Count;
        public IEnumerable<RoutedLink> RoutedLinks => routed.Values.Where(r => r != null);
        public IEnumerable<SettlementWorld> Worlds => worlds.Values;

        /// <summary>Candidate links whose routing domain comes within reachM of the region (level 10): superset of every link that can touch a sector of the region.</summary>
        public Link[] LinksNear(RegionId region)
        {
            return regionLinks.GetOrAdd(region, r =>
            {
                var f = Field; var ctr = CubeSphere.RegionCentre(r); double rad = 0;
                for (int cu = 0; cu < 2; cu++) for (int cv = 0; cv < 2; cv++) rad = Math.Max(rad, SettlementField.Gc(ctr, CubeSphere.RegionCorner(r, cu, cv)));
                rad += AnchorParams.ReachM + 50;
                var links = new SortedDictionary<ulong, Link>(); var cb = Builder;
                for (int k = 0; k < 3; k++)
                {
                    double rk = AnchorParams.LayerRadius[k], dk = Math.Sqrt((rk + CorridorBuilder.MarginM) * (rk + CorridorBuilder.MarginM) + Math.Pow(Math.Max(CorridorBuilder.MinHalfWidthM, CorridorBuilder.HalfWidthShare * rk), 2)) + rad;
                    foreach (var s in f.Within(ctr, dk, k)) foreach (var l in f.LinksOf(s)) links[l.Key] = l;
                }
                var res = new List<Link>();
                foreach (var l in links.Values)
                {
                    var shell = cb.Domain(l); shell.Chart.ToLocal(ctr, out double px, out double pz);
                    if (shell.DomainDist(px, pz) <= rad) res.Add(l);
                }
                return res.ToArray();
            });
        }

        public RoutedLink Route(Link l)
        {
            if (routed.TryGetValue(l.Key, out var r)) return r;
            var c = Builder.Route(l); r = c.Routed ? new RoutedLink(c) : null;
            return routed.GetOrAdd(l.Key, r);
        }

        /// <summary>Settlements whose centre lies within reach (radius + blend) of the region (level 10) disc.</summary>
        public Settlement[] SettlementsNear(RegionId region)
        {
            return regionSettlements.GetOrAdd(region, r =>
            {
                var ctr = CubeSphere.RegionCentre(r); double rad = 0;
                for (int cu = 0; cu < 2; cu++) for (int cv = 0; cv < 2; cv++) rad = Math.Max(rad, SettlementField.Gc(ctr, CubeSphere.RegionCorner(r, cu, cv)));
                return Field.Within(ctr, rad + AnchorParams.RadiusM[2] + AnchorParams.BlendM + 50, 0).Where(s => SettlementField.Gc(ctr, s.Dir) <= rad + s.RadiusM + AnchorParams.BlendM + 50).ToArray();
            });
        }

        /// <summary>
        /// The per-region preparation a streamer runs ahead of the camera (on a region worker): candidate links, their routes, the profiles of every segment that comes within the
        /// region disc, and the WorldGen worlds of the settlements that reach it. Afterwards the sectors of the region only read caches.
        /// </summary>
        public void Prepare(RegionId region)
        {
            var ctr = CubeSphere.RegionCentre(region); double rad = 0;
            for (int cu = 0; cu < 2; cu++) for (int cv = 0; cv < 2; cv++) rad = Math.Max(rad, SettlementField.Gc(ctr, CubeSphere.RegionCorner(region, cu, cv)));
            rad += AnchorParams.ReachM + 60;
            foreach (var l in LinksNear(region))
            {
                var rl = Route(l); if (rl == null) continue;
                rl.C.Chart.ToLocal(ctr, out double cx, out double cz);
                for (int k = 0; k < rl.SegmentCount; k++)
                {
                    bool hit = false;
                    for (int i = rl.C.Joints[k]; i <= rl.C.Joints[k + 1] && !hit; i++) { double dx = rl.C.X[i] - cx, dz = rl.C.Z[i] - cz; hit = dx * dx + dz * dz <= rad * rad; }
                    if (hit) rl.Seg(k);
                }
            }
            foreach (var s in SettlementsNear(region)) World(s);
        }

        /// <summary>RG-182: the pinned entrances of a settlement: for every routed link that ends here, the corridor's end station in the settlement chart, the inward direction and the pinned height.</summary>
        public List<WhatIf.WorldGen.Entrance> EntrancesOf(Settlement s, Chart ch)
        {
            var res = new List<WhatIf.WorldGen.Entrance>();
            foreach (var l in Field.LinksOf(s))
            {
                var rl = Route(l); if (rl == null) continue;
                var c = rl.C; int n = c.X.Length; bool atA = l.A.Id == s.Id; int i = atA ? 0 : n - 1, j = atA ? 1 : n - 2;
                ch.ToLocal(c.Chart.ToDir(c.X[i], c.Z[i]), out double x0, out double z0); ch.ToLocal(c.Chart.ToDir(c.X[j], c.Z[j]), out double x1, out double z1);
                var d = new WhatIf.WorldGen.V2(x0 - x1, z0 - z1).Unit;
                res.Add(new WhatIf.WorldGen.Entrance { Point = new WhatIf.WorldGen.V2(x0, z0), Dir = d, Y = atA ? c.PinH[0] : c.PinH[c.PinH.Length - 1], Class = l.RoadClass });
            }
            return res;
        }

        /// <summary>RG-184: keep the full GenResult of the cached settlement worlds (experiments that read the street graph); default false = terrain model + StreetSet only.</summary>
        public bool KeepGraphs;
        /// <summary>RG-184 k2: byte cap of the cached settlement worlds (0 = unlimited). Dropping the graph alone left the WorldGen terrain model (~180 B per street segment,
        /// measured: area A 12.8 of 14.9 MiB) above 3x the RG-181 cache, so the worlds are an LRU: estimated bytes = street segments x WorldBytesPerSegment (measured 207 B incl.
        /// the StreetSet); the least recently used worlds go first, never the one just asked for. A dropped world is rebuilt on its next use (bit-identical, see the tests).</summary>
        public long WorldByteCap = DefaultWorldByteCap;
        public const long DefaultWorldByteCap = 6L << 20; public const int WorldBytesPerSegment = 207;
        readonly object lruGate = new object(); readonly Dictionary<ulong, long> lastUse = new Dictionary<ulong, long>(); long useClock, cachedBytes;
        public int WorldsEvicted, WorldsBuilt;
        public long CachedWorldBytes { get { lock (lruGate) return cachedBytes; } }
        static long Bytes(SettlementWorld sw) => sw.Ok ? (long)sw.Streets.Count * WorldBytesPerSegment : 4096;

        public SettlementWorld World(Settlement s)
        {
            bool added = false;
            var sw = worlds.GetOrAdd(s.Id, _ => { added = true; return new SettlementWorld(this, s, false, KeepGraphs); });
            if (WorldByteCap <= 0) { if (added) Interlocked.Increment(ref WorldsBuilt); return sw; }
            lock (lruGate)
            {
                worlds.TryAdd(s.Id, sw);   // another thread may have evicted it between GetOrAdd and here: keep dictionary and LRU in step
                bool known = lastUse.ContainsKey(s.Id); lastUse[s.Id] = ++useClock;
                if (!known) { cachedBytes += Bytes(sw); WorldsBuilt++; }
                while (cachedBytes > WorldByteCap && lastUse.Count > 1)
                {
                    ulong victim = 0; long oldest = long.MaxValue;
                    foreach (var kv in lastUse) if (kv.Key != s.Id && (kv.Value < oldest || (kv.Value == oldest && kv.Key < victim))) { oldest = kv.Value; victim = kv.Key; }
                    if (worlds.TryRemove(victim, out var gone)) cachedBytes -= Bytes(gone);
                    lastUse.Remove(victim); WorldsEvicted++;
                }
            }
            return sw;
        }
        /// <summary>RG-184: drops a cached settlement world; the next World(s) rebuilds it (a pure function: bit-identical sectors).</summary>
        public bool Forget(ulong settlementId)
        {
            bool r = worlds.TryRemove(settlementId, out var gone);
            lock (lruGate) if (lastUse.Remove(settlementId) && gone != null) cachedBytes -= Bytes(gone);
            return r;
        }
    }
}
