using System;
using System.Collections.Generic;
using WhatIf.Planet.FarLayer;

namespace WhatIf.Planet.Runtime
{
    /// <summary>Tunables of the streaming runtime (RG-172). Defaults = the ticket: 600 / 760 m, no near sectors above 1500 m, region lead 30 s, floor 2 km.</summary>
    public sealed class StreamerParams
    {
        public double LoadRadiusM = 600.0, UnloadRadiusM = 760.0;
        /// <summary>No near sector is wanted above this ground clearance (m).</summary>
        public double MaxClearanceM = 1500.0;
        /// <summary>Regions are wanted (prefetch) up to this clearance: a descent from 3 km to the ground takes more than a region build.</summary>
        public double RegionClearanceM = 3000.0;
        public double PrefetchLeadS = 60.0, PrefetchFloorM = 2000.0;
        /// <summary>Finished sector results the main thread integrates per frame.</summary>
        public int IntegratePerFrame = 4;
        /// <summary>Cache bounds: sectors / regions kept (wanted ones are never evicted), and a byte cap over all caches (sectors, regions estimate, far nodes).</summary>
        public int MaxSectors = 96, MaxRegions = 6;
        public long MaxCacheBytes = 140L << 20;
        public long RegionBytesEstimate = 3L << 20;
        /// <summary>The region plan (prefetch set) is recomputed every N frames and whenever the camera enters another region.</summary>
        public int RegionPlanEvery = 4;
        /// <summary>Far layer node build budget per frame on the main thread (ms).</summary>
        public double FarBuildBudgetMs = 2.0;
        /// <summary>Speed smoothing (1 = instantaneous).</summary>
        public double SpeedSmoothing = 0.1;
        /// <summary>Lead (s) of the prefetch is at least PrefetchLeadS and at least LeadBuildFactor x the longest region build seen so far (the measured build time, not a guess).</summary>
        public double LeadBuildFactor = 1.5;
        /// <summary>Region build time (s) assumed before one was measured: the slowest region of the experiment (cube-edge neighbour) took 50-65 s with the workers competing.</summary>
        public double ExpectedRegionBuildS = 60.0;
        /// <summary>Beyond PrefetchFloorM only regions inside this cone around the heading (degrees, half angle) are prefetched; within the floor all directions.</summary>
        public double ConeHalfAngleDeg = 12.0;
        /// <summary>Speed below which the camera counts as standing: no cone, full ring of the floor radius.</summary>
        public double MovingSpeedMps = 5.0;
        /// <summary>Region builds in parallel (threads that only build regions). The sector thread is extra: 2 region workers + 1 sector worker by default.</summary>
        public int RegionWorkers = 2;
        public double PrefetchM(double speedMps) => Math.Max(PrefetchFloorM, speedMps * PrefetchLeadS);
        public double PrefetchM(double speedMps, double longestRegionBuildS) => Math.Max(PrefetchFloorM, speedMps * Math.Max(PrefetchLeadS, LeadBuildFactor * Math.Max(ExpectedRegionBuildS, longestRegionBuildS)));
    }

    public struct WantedSector { public SectorId Id; public double DistM; }

    /// <summary>
    /// The wanted near set: level-15 sectors whose centre is within LoadRadius of the ground point, plus (hysteresis) sectors that were wanted and are still within
    /// UnloadRadius; none above MaxClearance. BFS over SectorId.Neighbour (crosses cube edges). Not thread safe.
    /// </summary>
    public sealed class SectorWanted
    {
        readonly HashSet<SectorId> visited = new HashSet<SectorId>(); readonly Queue<SectorId> queue = new Queue<SectorId>();
        public void Compute(Vec3 groundDir, double clearanceM, HashSet<SectorId> previous, StreamerParams p, List<WantedSector> result)
        {
            result.Clear();
            if (clearanceM > p.MaxClearanceM) return;
            visited.Clear(); queue.Clear();
            double visitR = p.UnloadRadiusM + 700.0;      // a sector centre is at most ~230 m from its corner; the front keeps one more ring
            SectorId s0 = SectorId.At(groundDir); visited.Add(s0); queue.Enqueue(s0);
            while (queue.Count > 0)
            {
                SectorId s = queue.Dequeue();
                double d = Vec3.Angle(NearSet.CentreDir(s), groundDir) * CubeSphere.Radius;
                if (d > visitR) continue;
                if (d <= p.LoadRadiusM || (d <= p.UnloadRadiusM && previous != null && previous.Contains(s))) result.Add(new WantedSector { Id = s, DistM = d });
                for (int k = 0; k < 4; k++) { SectorId n = s.Neighbour(k); if (visited.Add(n)) queue.Enqueue(n); }
            }
        }
    }

    public struct WantedRegion { public RegionId Id; public double Cost; }

    /// <summary>
    /// The wanted regions: the camera's region (cost -1) and every region that has a sample point within prefetchM of the camera (4 rings x 32 azimuths + 5 points
    /// along the heading). Cost = distance x (1.6 - cos(angle to the heading)): regions ahead first. Not thread safe.
    /// </summary>
    public sealed class RegionPlan
    {
        readonly Dictionary<RegionId, double> best = new Dictionary<RegionId, double>();
        public const int Azimuths = 32;
        public void Compute(Vec3 dir, Vec3 heading, double prefetchM, List<WantedRegion> result, double floorM = double.MaxValue, double coneHalfAngleDeg = 180.0)
        {
            result.Clear(); best.Clear();
            double r = CubeSphere.Radius;
            Vec3 e1 = heading.Length > 0.5 ? heading.Normalized() : Vec3.Cross(dir, new Vec3(0.3, 0.9, 0.1)).Normalized(), e2 = Vec3.Cross(dir, e1);
            best[CubeSphere.RegionOf(dir, SectorId.RegionLevel)] = -1.0;
            bool haveHeading = heading.Length > 0.5;
            for (int ring = 0; ring <= 4; ring++)
            {
                double d = ring == 0 ? Math.Min(prefetchM, floorM) : prefetchM * ring / 4.0;      // ring 0 = the floor radius, full circle
                if (ring == 0 && floorM >= prefetchM) continue;
                for (int a = 0; a < Azimuths; a++)
                {
                    double phi = 2 * Math.PI * a / Azimuths, c = Math.Cos(phi);
                    if (haveHeading && d > floorM && c < Math.Cos(coneHalfAngleDeg * Math.PI / 180.0)) continue;   // outside the cone: not prefetched beyond the floor
                    Add(dir, e1, e2, c, Math.Sin(phi), d, d * (haveHeading ? 1.6 - c : 1.0), r);
                }
            }
            if (haveHeading) for (int k = 1; k <= 5; k++) { double d = prefetchM * k / 5.0; Add(dir, e1, e2, 1.0, 0.0, d, d * 0.6, r); }
            foreach (var kv in best) result.Add(new WantedRegion { Id = kv.Key, Cost = kv.Value });
        }
        void Add(Vec3 dir, Vec3 e1, Vec3 e2, double c, double s, double d, double cost, double r)
        {
            Vec3 p = (dir * r + (e1 * c + e2 * s) * d).Normalized();
            RegionId id = CubeSphere.RegionOf(p, SectorId.RegionLevel); double old;
            if (!best.TryGetValue(id, out old) || cost < old) best[id] = cost;
        }
    }

    /// <summary>The far layer's NearSet from READY sectors only: the same derivation as NearSet.Update (hole masks, ancestor keys, exposed sides) without the BFS.</summary>
    public static class NearSetBuilder
    {
        public static void Fill(NearSet ns, IEnumerable<SectorId> ready, Vec3 groundDir)
        {
            ns.GroundDir = groundDir; ns.Sectors.Clear(); ns.Masks.Clear(); ns.Ancestors.Clear(); ns.Exposed.Clear();
            foreach (SectorId s in ready) ns.Sectors.Add(s);
            foreach (SectorId s in ns.Sectors)
            {
                int bit = ((s.Row & 3) << 2) | (s.Col & 3), shift = SectorId.Level - 13; long k13 = NodeKey.Pack(s.Face, 13, s.Row >> shift, s.Col >> shift);
                ushort m; ns.Masks.TryGetValue(k13, out m); ns.Masks[k13] = (ushort)(m | (1 << bit));
                for (int l = 0; l <= 13; l++) ns.Ancestors.Add(NodeKey.Pack(s.Face, l, s.Row >> (SectorId.Level - l), s.Col >> (SectorId.Level - l)));
                for (int k = 0; k < 4; k++) if (!ns.Sectors.Contains(s.Neighbour(k))) ns.Exposed.Add(new KeyValuePair<SectorId, int>(s, k));
            }
        }
    }
}
