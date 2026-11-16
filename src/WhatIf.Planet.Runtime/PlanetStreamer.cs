using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using WhatIf.Planet.FarLayer;
using WhatIf.Planet.WorldGen;

namespace WhatIf.Planet.Runtime
{
    /// <summary>RG-178: what a sector build needs from the unit the streamer prepares per level-10 region: the anchor content (AnchorContent). Build runs on the sector worker under the entry's SectorLock.</summary>
    public interface ISectorContent { void Build(QuadSector sec, AdaptedSector o); }

    public sealed class RegionEntry
    {
        public readonly RegionId Id;
        public volatile ISectorContent Content; public volatile bool Failed; public string Error;
        public double Priority;                 // written by the main thread, read by the workers (a stale value only reorders)
        public int Claimed;                     // 0 = free, 1 = a worker builds it, 2 = cancelled
        public double RequestedAt, ReadyAt; public long LastWanted;
        public readonly object SectorLock = new object();   // content is not thread safe: one sector build at a time per region
        public RegionEntry(RegionId id) { Id = id; }
    }

    public sealed class SectorJob
    {
        public readonly SectorId Id; public readonly RegionEntry Region;
        public double Priority; public int Claimed;          // Claimed: 0 free, 1 building, 2 cancelled
        public AdaptedSector Data; public bool Failed; public int Worker; public double BuildMs;
        public SectorJob(SectorId id, RegionEntry r) { Id = id; Region = r; }
    }

    sealed class SectorEntry
    {
        public SectorId Id; public SectorJob Job; public AdaptedSector Data; public bool Ready; public long LastWanted; public double WantedAt, ReadyAt; public bool RegionWasMissing;
    }

    /// <summary>Counters of one background worker (A = regions first, B = sectors).</summary>
    public sealed class WorkerStats { public long SectorsBuilt, RegionsBuilt, BusyTicks; public double MaxSectorMs, MaxRegionS; }

    /// <summary>What the last Update saw and did (main thread).</summary>
    public struct FrameInfo
    {
        public int Wanted, Ready, WantedIn300, NotReadyIn300, WantedIn600, NotReadyIn600, WaitingForRegion600, Integrated, NearCount, Queue, FarBuilt, FarFallbacks, FarRequests, RegionsReady, RegionsPending;
        public double SpeedMps; public RegionId CameraRegion; public bool CameraRegionReady;
    }

    /// <summary>
    /// RG-172: the streaming runtime a viewer drives. Update(cameraDir, altitudeM, dt) on the MAIN thread: wanted near sectors (SectorWanted), wanted regions with a speed
    /// scaled prefetch (RegionPlan), job bookkeeping, bounded integration of finished results, NearSet from READY sectors only (the hole opens only where a near sector is
    /// ready), far layer selection + budgeted node builds. Two background threads: A builds region worlds (nearest/ahead first) and takes sectors when idle, B builds
    /// sectors by distance. A sector whose region world is not ready waits (the far layer keeps covering it). Regions come from a factory so the surface base of RG-169
    /// plugs in without code changes here. Not re-entrant; Update/Dispose from one thread.
    /// </summary>
    public sealed class PlanetStreamer : IDisposable
    {
        public const double R = CubeSphere.Radius;
        public readonly StreamerParams P; public readonly FarLayer.FarLayer Far;
        public readonly NearSet Near = new NearSet(); public readonly FrameSelection Selection = new FrameSelection();
        readonly Func<RegionId, ISectorContent> regionFactory; readonly Func<Vec3, double> height;
        /// <summary>Called on the worker thread right after a sector was built (determinism sampling in the experiment).</summary>
        public Action<SectorId, AdaptedSector> OnSectorBuilt;

        // shared with the workers
        readonly ConcurrentDictionary<RegionId, RegionEntry> regions = new ConcurrentDictionary<RegionId, RegionEntry>();
        readonly ConcurrentDictionary<SectorId, SectorJob> pending = new ConcurrentDictionary<SectorId, SectorJob>();
        readonly ConcurrentQueue<SectorJob> finished = new ConcurrentQueue<SectorJob>();
        readonly ConcurrentBag<AdaptedSector> pool = new ConcurrentBag<AdaptedSector>();
        readonly ManualResetEventSlim wake = new ManualResetEventSlim(false);
        readonly List<Thread> threads = new List<Thread>(); volatile bool stop;
        /// <summary>StatsA = all region workers together, StatsB = the sector worker.</summary>
        public readonly WorkerStats StatsA = new WorkerStats(), StatsB = new WorkerStats();
        /// <summary>Every region world build: id, start and ready time (s on Clock), seconds.</summary>
        public readonly ConcurrentQueue<(RegionId Id, double StartS, double ReadyS)> RegionBuilds = new ConcurrentQueue<(RegionId, double, double)>();
        volatile float longestRegionS;

        // main thread only
        readonly Dictionary<SectorId, SectorEntry> sectors = new Dictionary<SectorId, SectorEntry>();
        readonly HashSet<SectorId> wantedSet = new HashSet<SectorId>(); readonly List<WantedSector> wanted = new List<WantedSector>();
        readonly List<SectorId> readyNear = new List<SectorId>();
        readonly SectorWanted sectorWanted = new SectorWanted(); readonly RegionPlan regionPlan = new RegionPlan(); readonly List<WantedRegion> wantedRegions = new List<WantedRegion>();
        long frame, planFrame = -1000; RegionId lastCameraRegion; bool haveLast; Vec3 lastDir, heading; double speed;
        public FrameInfo Info; public long TotalFrames => frame;
        /// <summary>Request-to-ready latency (s) of every sector, and the part not caused by waiting for the region world; waits (s) of sectors that were requested before their region was ready.</summary>
        public readonly List<double> LatencyS = new List<double>(), LatencyNoRegionS = new List<double>(), RegionWaitS = new List<double>();
        public int MaxQueue, SectorRequests, SectorFailures, RegionFailures, SectorRequestsAboveCut;
        public int NotReady300Frames;
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public double NowS => Clock.Elapsed.TotalSeconds;
        public int FarBuiltTotal;

        /// <summary>RG-178: any content source (variant B: anchors). contentFactory(region) runs on a region worker and returns what the sector worker builds that region's sectors from.</summary>
        public PlanetStreamer(Func<RegionId, ISectorContent> contentFactory, Func<Vec3, double> heightFunc, StreamerParams p = null, FarParams farParams = null)
        {
            this.regionFactory = contentFactory; height = heightFunc; P = p ?? new StreamerParams();
            Far = new FarLayer.FarLayer(heightFunc, farParams);
            for (int i = 0; i < Math.Max(1, P.RegionWorkers); i++) { var t = new Thread(() => Worker(true, StatsA, 0)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "planet-regions-" + i }; threads.Add(t); }
            threads.Add(new Thread(() => Worker(false, StatsB, 1)) { IsBackground = true, Name = "planet-sectors" });
            foreach (var t in threads) t.Start();
        }

        /// <summary>Prebuilds the coarse far levels 0..maxLevel (start-up set, pinned). Main thread.</summary>
        public int PrebuildFar(int maxLevel = 3) => Far.Prebuild(maxLevel);

        // ------------------------------------------------------------------ main thread
        public void Update(Vec3 cameraDir, double altitudeM, double dt)
        {
            frame++;
            double now = NowS;
            UpdateMotion(cameraDir, dt);
            double groundH = height(cameraDir);

            // 1. wanted sectors (+ hysteresis from the previous wanted set)
            sectorWanted.Compute(cameraDir, altitudeM, wantedSet, P, wanted);
            wantedSet.Clear();
            for (int i = 0; i < wanted.Count; i++) wantedSet.Add(wanted[i].Id);

            // 2. region plan (every few frames or when the camera enters another region)
            RegionId camRegion = CubeSphere.RegionOf(cameraDir, SectorId.RegionLevel);
            if (frame - planFrame >= P.RegionPlanEvery || !camRegion.Equals(lastCameraRegion) || !haveLast) { PlanRegions(cameraDir, altitudeM, camRegion); planFrame = frame; }
            lastCameraRegion = camRegion; haveLast = true;

            // 3. jobs for wanted sectors, priorities, cancel what is no longer wanted
            int wanted300 = 0, notReady300 = 0, wanted600 = 0, notReady600 = 0, waitingRegion = 0, newJobs = 0;
            for (int i = 0; i < wanted.Count; i++)
            {
                WantedSector w = wanted[i]; SectorEntry e;
                if (!sectors.TryGetValue(w.Id, out e))
                {
                    RegionEntry re = regions.GetOrAdd(w.Id.Region, id => new RegionEntry(id) { RequestedAt = now });
                    re.LastWanted = frame;
                    e = new SectorEntry { Id = w.Id, WantedAt = now, RegionWasMissing = re.Content == null };
                    var job = new SectorJob(w.Id, re) { Priority = w.DistM }; e.Job = job; sectors[w.Id] = e; pending[w.Id] = job; newJobs++; SectorRequests++;
                    if (altitudeM > P.MaxClearanceM) SectorRequestsAboveCut++;
                }
                else if (e.Job != null) e.Job.Priority = w.DistM;
                else if (!e.Ready) { var job = new SectorJob(w.Id, regions.GetOrAdd(w.Id.Region, id => new RegionEntry(id) { RequestedAt = now })) { Priority = w.DistM }; e.Job = job; e.WantedAt = now; pending[w.Id] = job; newJobs++; SectorRequests++; }
                e.LastWanted = frame;
            }
            CancelUnwanted();
            if (newJobs > 0) wake.Set();
            if (pending.Count > MaxQueue) MaxQueue = pending.Count;

            // 4. bounded integration of finished results
            int integrated = 0; SectorJob fin;
            while (integrated < P.IntegratePerFrame && finished.TryDequeue(out fin))
            {
                integrated++; SectorEntry e;
                if (fin.Data == null) { SectorFailures++; if (sectors.TryGetValue(fin.Id, out e) && e.Job == fin) sectors.Remove(fin.Id); continue; }
                if (!sectors.TryGetValue(fin.Id, out e) || e.Job != fin) { Recycle(fin.Data); continue; }
                e.Data = fin.Data; e.Job = null; e.Ready = true; e.ReadyAt = now;
                double lat = now - e.WantedAt; LatencyS.Add(lat);
                double regionReady = fin.Region.ReadyAt, noRegion = e.RegionWasMissing ? now - Math.Max(e.WantedAt, regionReady) : lat; LatencyNoRegionS.Add(noRegion);
                if (e.RegionWasMissing) RegionWaitS.Add(Math.Max(0, regionReady - e.WantedAt));
            }

            // 5. statistics + the near set from ready sectors only
            readyNear.Clear();
            for (int i = 0; i < wanted.Count; i++)
            {
                WantedSector w = wanted[i]; SectorEntry e = sectors[w.Id];
                if (w.DistM <= 300) { wanted300++; if (!e.Ready) notReady300++; }
                if (w.DistM <= 600) { wanted600++; if (!e.Ready) { notReady600++; if (e.Job != null && e.Job.Region.Content == null) waitingRegion++; } }
                if (e.Ready) readyNear.Add(w.Id);
            }
            // hole only where a near sector is ready AND its level-13 far node is built (a forced node that is not built would leave a gap)
            int fb = 0;
            for (int i = readyNear.Count - 1; i >= 0; i--)
            {
                SectorId s = readyNear[i]; int shift = SectorId.Level - 13; FarNode n = Far.Node(s.Face, 13, s.Row >> shift, s.Col >> shift);
                if (n.Built) continue;
                if (fb < 1) { Far.Build(n); fb++; FarBuiltTotal++; } else readyNear.RemoveAt(i);
            }
            NearSetBuilder.Fill(Near, readyNear, cameraDir);

            // 6. far layer
            FarCamera cam = new FarCamera { Pos = cameraDir * (R + groundH + altitudeM), Ground = cameraDir * (R + groundH), Fwd = heading.Length > 0.5 ? heading : Vec3.Cross(cameraDir, new Vec3(0, 0, 1)).Normalized() };
            Far.Select(cam, Near, Selection);
            int built = Far.BuildPending(Selection, P.FarBuildBudgetMs); FarBuiltTotal += built;

            TrimCaches();
            int rReady = 0, rPending = 0; foreach (var kv in regions) { if (kv.Value.Content != null) rReady++; else rPending++; }
            RegionEntry cr; bool camReady = regions.TryGetValue(camRegion, out cr) && cr.Content != null;
            Info = new FrameInfo
            {
                Wanted = wanted.Count,
                Ready = readyNear.Count,
                WantedIn300 = wanted300,
                NotReadyIn300 = notReady300,
                WantedIn600 = wanted600,
                NotReadyIn600 = notReady600,
                WaitingForRegion600 = waitingRegion,
                Integrated = integrated,
                NearCount = Near.Count,
                Queue = pending.Count,
                FarBuilt = built + fb,
                FarFallbacks = Selection.Fallbacks,
                FarRequests = Selection.Requests.Count,
                RegionsReady = rReady,
                RegionsPending = rPending,
                SpeedMps = speed,
                CameraRegion = camRegion,
                CameraRegionReady = camReady
            };
            if (altitudeM <= P.MaxClearanceM && frame > 1 && notReady300 > 0) NotReady300Frames++;
        }

        void UpdateMotion(Vec3 dir, double dt)
        {
            if (haveLast && dt > 1e-6)
            {
                double ang = Vec3.Angle(lastDir, dir), moved = ang * R;
                if (moved > 0.001)
                {
                    Vec3 t = (dir - lastDir); t = (t - dir * Vec3.Dot(t, dir)); if (t.Length > 0) heading = t.Normalized();
                    speed += (moved / dt - speed) * P.SpeedSmoothing;
                }
                else speed *= 1.0 - P.SpeedSmoothing;
            }
            lastDir = dir;
        }

        void PlanRegions(Vec3 dir, double altitudeM, RegionId camRegion)
        {
            if (altitudeM > P.RegionClearanceM) { DropUnwantedRegions(null); return; }
            bool moving = speed > P.MovingSpeedMps;
            regionPlan.Compute(dir, heading, P.PrefetchM(speed, longestRegionS), wantedRegions, moving ? P.PrefetchFloorM : double.MaxValue, moving ? P.ConeHalfAngleDeg : 180.0);
            double now = NowS; bool added = false;
            for (int i = 0; i < wantedRegions.Count; i++)
            {
                WantedRegion w = wantedRegions[i];
                RegionEntry e = regions.GetOrAdd(w.Id, id => { added = true; return new RegionEntry(id) { RequestedAt = now }; });
                e.Priority = w.Cost; e.LastWanted = frame;
            }
            DropUnwantedRegions(wantedRegions);
            if (added) wake.Set();
        }

        // pending regions nobody wants any more are cancelled; ready regions beyond the cap are evicted (oldest not-wanted first) in TrimCaches
        void DropUnwantedRegions(List<WantedRegion> keep)
        {
            foreach (var kv in regions)
            {
                RegionEntry e = kv.Value;
                if (e.Content != null || e.LastWanted == frame) continue;
                if (Interlocked.CompareExchange(ref e.Claimed, 2, 0) == 0) { RegionEntry rm; regions.TryRemove(kv.Key, out rm); }
            }
        }

        readonly List<SectorId> dropList = new List<SectorId>();
        void CancelUnwanted()
        {
            dropList.Clear();
            foreach (var kv in sectors)
            {
                SectorEntry e = kv.Value; if (e.LastWanted == frame) continue;
                if (e.Job != null && Interlocked.CompareExchange(ref e.Job.Claimed, 2, 0) == 0) { SectorJob j; pending.TryRemove(e.Id, out j); dropList.Add(e.Id); }
            }
            for (int i = 0; i < dropList.Count; i++) sectors.Remove(dropList[i]);
        }

        // ------------------------------------------------------------------ caches
        public const long SectorBytes = (long)AdaptedSector.Count * (6 * 8 + 24 + 12);
        public long CacheBytes
        {
            get
            {
                long b = 0; foreach (var e in sectors.Values) if (e.Ready) b += SectorBytes;
                foreach (var kv in regions) if (kv.Value.Content != null) b += P.RegionBytesEstimate;
                return b + (long)Far.BuiltCount * (FarNode.Verts * FarNode.Verts * 8);
            }
        }
        public int ReadySectorCount { get { int n = 0; foreach (var e in sectors.Values) if (e.Ready) n++; return n; } }
        public int ReadyRegionCount { get { int n = 0; foreach (var kv in regions) if (kv.Value.Content != null) n++; return n; } }

        readonly List<SectorEntry> evictScratch = new List<SectorEntry>();
        void TrimCaches()
        {
            int ready = 0; foreach (var e in sectors.Values) if (e.Ready) ready++;
            long bytes = -1;
            if (ready > P.MaxSectors || (bytes = CacheBytes) > P.MaxCacheBytes)
            {
                evictScratch.Clear();
                foreach (var e in sectors.Values) if (e.Ready && e.LastWanted != frame) evictScratch.Add(e);
                evictScratch.Sort((a, b) => a.LastWanted.CompareTo(b.LastWanted));
                int i = 0;
                while (i < evictScratch.Count && (ready > P.MaxSectors || bytes > P.MaxCacheBytes))
                {
                    SectorEntry e = evictScratch[i++]; sectors.Remove(e.Id); Recycle(e.Data); ready--; if (bytes >= 0) bytes -= SectorBytes;
                }
            }
            int rReady = 0; foreach (var kv in regions) if (kv.Value.Content != null) rReady++;
            while (rReady > P.MaxRegions)
            {
                RegionEntry oldest = null;
                foreach (var kv in regions) { RegionEntry e = kv.Value; if (e.Content != null && e.LastWanted < planFrame && (oldest == null || e.LastWanted < oldest.LastWanted)) oldest = e; }
                if (oldest == null) break;
                RegionEntry rm; regions.TryRemove(oldest.Id, out rm); rReady--;
            }
        }

        void Recycle(AdaptedSector d) { if (d != null && pool.Count < 64) pool.Add(d); }

        // ------------------------------------------------------------------ workers
        void Worker(bool regionsFirst, WorkerStats st, int id)
        {
            while (!stop)
            {
                wake.Reset();
                try
                {
                    if (regionsFirst) { if (TryRegion(st)) continue; }
                    else if (TrySector(st, id)) continue;
                }
                catch (Exception) { /* a failing job is recorded on its entry; the worker lives on */ }
                wake.Wait(10);
            }
        }

        bool TryRegion(WorkerStats st)
        {
            RegionEntry best = null;
            foreach (var kv in regions) { RegionEntry e = kv.Value; if (e.Content == null && !e.Failed && e.Claimed == 0 && (best == null || e.Priority < best.Priority)) best = e; }
            if (best == null || Interlocked.CompareExchange(ref best.Claimed, 1, 0) != 0) return false;
            long t0 = Stopwatch.GetTimestamp(); double startS = NowS;
            try { ISectorContent w = regionFactory(best.Id); best.ReadyAt = NowS; best.Content = w; RegionBuilds.Enqueue((best.Id, startS, best.ReadyAt)); }
            catch (Exception ex) { best.Error = ex.Message; best.Failed = true; RegionFailures++; }
            long dt = Stopwatch.GetTimestamp() - t0; Interlocked.Add(ref st.BusyTicks, dt); Interlocked.Increment(ref st.RegionsBuilt);
            double sec = dt / (double)Stopwatch.Frequency; st.MaxRegionS = Math.Max(st.MaxRegionS, sec); if (sec > longestRegionS) longestRegionS = (float)sec;
            return true;
        }

        bool TrySector(WorkerStats st, int id)
        {
            SectorJob best = null;
            foreach (var kv in pending) { SectorJob j = kv.Value; if (j.Claimed == 0 && j.Region.Content != null && (best == null || j.Priority < best.Priority)) best = j; }
            if (best == null || Interlocked.CompareExchange(ref best.Claimed, 1, 0) != 0) return false;
            long t0 = Stopwatch.GetTimestamp(); ISectorContent w = best.Region.Content; AdaptedSector data;
            if (!pool.TryTake(out data)) data = new AdaptedSector();
            try
            {
                lock (best.Region.SectorLock) w.Build(new QuadSector(best.Id), data);
                best.Data = data; best.Worker = id; OnSectorBuilt?.Invoke(best.Id, data);
            }
            catch (Exception) { best.Data = null; best.Failed = true; }
            long dt = Stopwatch.GetTimestamp() - t0; Interlocked.Add(ref st.BusyTicks, dt); st.SectorsBuilt++;
            best.BuildMs = dt * 1000.0 / Stopwatch.Frequency; st.MaxSectorMs = Math.Max(st.MaxSectorMs, best.BuildMs);
            SectorJob rm; pending.TryRemove(best.Id, out rm); finished.Enqueue(best);
            return true;
        }

        // ------------------------------------------------------------------ queries (main thread)
        public bool IsSectorReady(SectorId id) { SectorEntry e; return sectors.TryGetValue(id, out e) && e.Ready; }
        public AdaptedSector SectorData(SectorId id) { SectorEntry e; return sectors.TryGetValue(id, out e) && e.Ready ? e.Data : null; }
        public bool IsRegionReady(RegionId id) { RegionEntry e; return regions.TryGetValue(id, out e) && e.Content != null; }
        /// <summary>Wall time (s on Clock) at which the region world became ready, or NaN.</summary>
        public double RegionReadyAt(RegionId id) { RegionEntry e; return regions.TryGetValue(id, out e) && e.Content != null ? e.ReadyAt : double.NaN; }
        public IEnumerable<SectorId> WantedSectors { get { foreach (var w in wanted) yield return w.Id; } }
        public IReadOnlyList<WantedSector> Wanted => wanted;
        /// <summary>True when at least one sector is wanted within radiusM of the last camera and all of them are ready (cold spawn, tests).</summary>
        public bool NearComplete(double radiusM) { int n = 0; foreach (var w in wanted) if (w.DistM <= radiusM) { n++; if (!IsSectorReady(w.Id)) return false; } return n > 0; }

        public double LongestRegionBuildS => longestRegionS;
        public void Dispose() { stop = true; wake.Set(); foreach (var t in threads) t.Join(100); }
    }
}
